#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(THE_cattail.AfkMotionPatcher.AfkMotionPatchPlugin))]

namespace THE_cattail.AfkMotionPatcher
{
    internal sealed class AfkMotionPatchPlugin : Plugin<AfkMotionPatchPlugin>
    {
        public override string QualifiedName => "com.the-cattail.afk-motion-patcher";
        public override string DisplayName => "AFK Motion Patcher";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                {
                    sequence.Run(AfkMotionPatchPass.Instance);
                });
        }
    }

    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    [DependsOnContext(typeof(AnimatorServicesContext))]
    internal sealed class AfkMotionPatchPass : Pass<AfkMotionPatchPass>
    {
        protected override void Execute(nadena.dev.ndmf.BuildContext context)
        {
            var avatarRoot = context.AvatarRootObject;
            if (!AfkMotionPatchEditorUtil.TryGetActivePatch(avatarRoot, out var patch)) return;

            if (!AfkMotionSourceGraph.TryExtract(patch, out var sourceGraph, out var graphError))
            {
                ReportSourceError(patch, $"[AfkMotionPatch] Could not extract the source AFK graph for avatar={avatarRoot.name}: {graphError}");
                return;
            }
            if (!sourceGraph.ValidateStateMotions(patch, out var motionError))
            {
                ReportSourceError(patch, $"[AfkMotionPatch] Invalid source state Motion configuration for avatar={avatarRoot.name}: {motionError}");
                return;
            }
            if (!sourceGraph.EffectiveSourceClips(patch).Any())
            {
                ReportSourceError(patch, $"[AfkMotionPatch] The extracted source AFK graph has no AnimationClip Motion for avatar={avatarRoot.name}.");
                return;
            }

            var controllerContext = context.Extension<AnimatorServicesContext>().ControllerContext;
            var generatedController = controllerContext.CloneContext.Clone(
                sourceGraph.Controller, VRCAvatarDescriptor.AnimLayerType.Action);
            if (generatedController == null)
            {
                ReportSourceError(patch, $"[AfkMotionPatch] Could not clone Source Action Controller {sourceGraph.Controller.name}.");
                return;
            }

            var layerGraphs = sourceGraph.Layers.ToDictionary(layer => layer.LayerIndex);
            var keptLayers = generatedController.Layers
                .Where(layer => layer.OriginalPhysicalLayerIndex.HasValue
                    && layerGraphs.ContainsKey(layer.OriginalPhysicalLayerIndex.Value))
                .ToList();

            if (keptLayers.Count != sourceGraph.Layers.Count)
            {
                ReportSourceError(patch, $"[AfkMotionPatch] Source AFK layer clone mismatch for avatar={avatarRoot.name}: expected={sourceGraph.Layers.Count}, actual={keptLayers.Count}.");
                return;
            }

            var privateMotions = new Dictionary<Motion, VirtualMotion>();
            var selectedStateCount = 0;
            foreach (var layer in keptLayers)
            {
                var layerIndex = layer.OriginalPhysicalLayerIndex.Value;
                var layerGraph = layerGraphs[layerIndex];
                var selectedVirtualStates = layerGraph.States
                    .Select(controllerContext.Clone)
                    .Where(state => state != null)
                    .ToHashSet();
                foreach (var sourceState in layerGraph.States)
                {
                    var virtualState = controllerContext.Clone(sourceState);
                    if (virtualState != null)
                    {
                        try
                        {
                            virtualState.Motion = AfkMotionPatchEditorUtil.CloneSourceMotion(
                                sourceGraph.ResolveMotion(patch, sourceState), privateMotions);
                        }
                        catch (Exception exception)
                        {
                            ReportSourceError(patch, $"[AfkMotionPatch] Could not privately clone source Motion on {sourceGraph.GetStatePath(sourceState)}: {exception.Message}");
                            return;
                        }
                        if (!TryRestoreCrossPlayableLayerControls(sourceState.behaviours, virtualState.Behaviours,
                                out var stateBehaviours, out var behaviourError))
                        {
                            ReportSourceError(patch, $"[AfkMotionPatch] Could not preserve source state behaviours on {sourceGraph.GetStatePath(sourceState)}: {behaviourError}");
                            return;
                        }
                        virtualState.Behaviours = stateBehaviours;
                    }
                }
                foreach (var sourceStateMachine in layerGraph.StateMachines)
                {
                    var virtualStateMachine = controllerContext.Clone(sourceStateMachine);
                    if (virtualStateMachine != null)
                    {
                        if (!TryRestoreCrossPlayableLayerControls(sourceStateMachine.behaviours, virtualStateMachine.Behaviours,
                                out var stateMachineBehaviours, out var behaviourError))
                        {
                            ReportSourceError(patch, $"[AfkMotionPatch] Could not preserve source state-machine behaviours on {sourceStateMachine.name}: {behaviourError}");
                            return;
                        }
                        virtualStateMachine.Behaviours = stateMachineBehaviours;
                    }
                }

                var selectedVirtualTransitions = layerGraph.Transitions
                    .Select(transition => CloneTransition(controllerContext, transition))
                    .Where(transition => transition != null)
                    .ToHashSet();
                var virtualEntry = controllerContext.Clone(layerGraph.EntryState);
                PruneLayer(layer, selectedVirtualStates, selectedVirtualTransitions, virtualEntry);
                selectedStateCount += selectedVirtualStates.Count;
            }

            generatedController.Layers = keptLayers;
            var usedParameters = sourceGraph.GetUsedParameterNames(patch);
            var declaredParameters = generatedController.Parameters.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var layer in keptLayers)
                CollectVirtualLayerParameters(layer, usedParameters, declaredParameters);
            generatedController.Parameters = generatedController.Parameters
                .Where(pair => usedParameters.Contains(pair.Key))
                .ToImmutableDictionary();
            generatedController.Name = $"AfkMotionPatch_{sourceGraph.Controller.name}_{avatarRoot.name}";

            var retargetedMotions = new HashSet<VirtualMotion>();
            var animatedTransformPaths = new HashSet<string>(StringComparer.Ordinal);
            var retargetedClipCount = 0;
            var copiedSourceCurves = 0;
            var appendedMappedCurves = 0;
            foreach (var layer in keptLayers)
            {
                foreach (var state in EnumerateStates(layer.StateMachine))
                {
                    RetargetMotion(state.Motion, avatarRoot, patch, retargetedMotions, animatedTransformPaths,
                        ref retargetedClipCount, ref copiedSourceCurves, ref appendedMappedCurves);
                }
                foreach (var motion in layer.SyncedLayerMotionOverrides.Values)
                {
                    RetargetMotion(motion, avatarRoot, patch, retargetedMotions, animatedTransformPaths,
                        ref retargetedClipCount, ref copiedSourceCurves, ref appendedMappedCurves);
                }
            }

            var enabledPhysBoneCount = AfkMotionPatchEditorUtil.EnableAnimationOnAffectedPhysBones(
                avatarRoot, animatedTransformPaths);

            // Animator Services must commit through the platform-owned Action slot so cross-playable layer controls
            // retain the correct VRChat context. We replace that slot's complete contents without reading or merging
            // any target-controller state, layer, transition, parameter, or Motion.
            if (!controllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.Action, out var actionSlot) || actionSlot == null)
            {
                ReportSourceError(patch, $"[AfkMotionPatch] VRChat Action layer slot is unavailable for avatar={avatarRoot.name}.");
                return;
            }
            actionSlot.Name = generatedController.Name;
            actionSlot.Parameters = generatedController.Parameters;
            actionSlot.Layers = generatedController.Layers;

            Debug.Log($"[AfkMotionPatch] Generated Action AFK graph from source controller via NDMF "
                + $"avatar={avatarRoot.name}, sourceController={sourceGraph.Controller.name}, "
                + $"layers={keptLayers.Count}, states={selectedStateCount}, clips={retargetedClipCount}, "
                + $"copiedSourceCurves={copiedSourceCurves}, appendedMappedCurves={appendedMappedCurves}, "
                + $"enabledPhysBones={enabledPhysBoneCount}", patch);
        }

        private static void ReportSourceError(AfkMotionPatch patch, string message)
        {
            Debug.LogError(message, patch);
        }

        private static bool TryRestoreCrossPlayableLayerControls(
            StateMachineBehaviour[] sourceBehaviours, ImmutableList<StateMachineBehaviour> clonedBehaviourList,
            out ImmutableList<StateMachineBehaviour> restored, out string error)
        {
            restored = clonedBehaviourList;
            error = null;
            if (sourceBehaviours.Length != clonedBehaviourList.Count)
            {
                error = $"behaviour count changed during virtualization (source={sourceBehaviours.Length}, cloned={clonedBehaviourList.Count})";
                return false;
            }

            var clonedBehaviours = clonedBehaviourList.ToArray();
            var used = new bool[clonedBehaviours.Length];
            var ordered = new List<StateMachineBehaviour>(sourceBehaviours.Length);
            foreach (var sourceBehaviour in sourceBehaviours)
            {
                var match = -1;
                for (var index = 0; index < clonedBehaviours.Length; index++)
                {
                    if (used[index] || !clonedBehaviours[index] || !sourceBehaviour
                        || clonedBehaviours[index].GetType() != sourceBehaviour.GetType()) continue;
                    match = index;
                    break;
                }
                if (match < 0)
                {
                    error = $"behaviour type could not be matched: {(sourceBehaviour ? sourceBehaviour.GetType().FullName : "null")}";
                    return false;
                }
                used[match] = true;
                if (sourceBehaviour is VRCAnimatorLayerControl control
                    && !string.Equals(control.playable.ToString(), "Action", StringComparison.Ordinal))
                {
                    var rawClone = UnityEngine.Object.Instantiate(sourceBehaviour);
                    rawClone.name = sourceBehaviour.name;
                    ordered.Add(rawClone);
                }
                else
                    ordered.Add(clonedBehaviours[match]);
            }
            restored = ordered.ToImmutableList();
            return true;
        }

        private static VirtualTransitionBase CloneTransition(VirtualControllerContext controllerContext,
            AnimatorTransitionBase transition)
        {
            switch (transition)
            {
                case AnimatorStateTransition stateTransition:
                    return controllerContext.Clone(stateTransition);
                case AnimatorTransition stateMachineTransition:
                    return controllerContext.Clone(stateMachineTransition);
                default:
                    return null;
            }
        }

        private static void PruneLayer(VirtualLayer layer, HashSet<VirtualState> selectedVirtualStates,
            HashSet<VirtualTransitionBase> selectedVirtualTransitions, VirtualState virtualEntry)
        {
            if (layer.StateMachine == null) return;
            PruneStateMachine(layer.StateMachine, selectedVirtualStates, selectedVirtualTransitions);

            if (virtualEntry != null)
            {
                layer.StateMachine.DefaultState = virtualEntry;
            }

            selectedVirtualStates = EnumerateStates(layer.StateMachine).ToHashSet();
            layer.SyncedLayerMotionOverrides = layer.SyncedLayerMotionOverrides
                .Where(pair => selectedVirtualStates.Contains(pair.Key))
                .ToImmutableDictionary();
            layer.SyncedLayerBehaviourOverrides = layer.SyncedLayerBehaviourOverrides
                .Where(pair => selectedVirtualStates.Contains(pair.Key))
                .ToImmutableDictionary();
        }

        private static bool PruneStateMachine(VirtualStateMachine stateMachine,
            HashSet<VirtualState> selectedVirtualStates, HashSet<VirtualTransitionBase> selectedVirtualTransitions)
        {
            var keptChildren = new List<VirtualStateMachine.VirtualChildStateMachine>();
            foreach (var child in stateMachine.StateMachines)
            {
                if (child.StateMachine != null && PruneStateMachine(
                        child.StateMachine, selectedVirtualStates, selectedVirtualTransitions))
                    keptChildren.Add(child);
            }
            stateMachine.StateMachines = keptChildren.ToImmutableList();

            var keptStates = stateMachine.States
                .Where(child => child.State != null && selectedVirtualStates.Contains(child.State))
                .ToImmutableList();
            stateMachine.States = keptStates;

            var selectedStatesInMachine = keptStates.Where(child => child.State != null).Select(child => child.State).ToHashSet();
            var selectedMachines = keptChildren.Select(child => child.StateMachine).ToHashSet();
            foreach (var child in keptStates)
            {
                child.State.Transitions = child.State.Transitions
                    .Where(selectedVirtualTransitions.Contains)
                    .ToImmutableList();
            }

            stateMachine.AnyStateTransitions = stateMachine.AnyStateTransitions
                .Where(selectedVirtualTransitions.Contains)
                .ToImmutableList();
            stateMachine.EntryTransitions = stateMachine.EntryTransitions
                .Where(selectedVirtualTransitions.Contains)
                .ToImmutableList();
            stateMachine.StateMachineTransitions = stateMachine.StateMachineTransitions
                .Where(pair => selectedMachines.Contains(pair.Key))
                .ToImmutableDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Where(selectedVirtualTransitions.Contains).ToImmutableList());

            if (stateMachine.DefaultState == null || !selectedStatesInMachine.Contains(stateMachine.DefaultState))
                stateMachine.DefaultState = null;

            return keptStates.Count() > 0 || keptChildren.Count > 0;
        }

        private static IEnumerable<VirtualState> EnumerateStates(VirtualStateMachine root)
        {
            if (root == null) yield break;
            foreach (var child in root.States)
                if (child.State != null) yield return child.State;
            foreach (var child in root.StateMachines)
                foreach (var state in EnumerateStates(child.StateMachine)) yield return state;
        }

        private static void CollectVirtualLayerParameters(VirtualLayer layer, HashSet<string> used,
            HashSet<string> declared)
        {
            CollectVirtualStateMachineParameters(layer.StateMachine, used, declared);
        }

        private static void CollectVirtualStateMachineParameters(VirtualStateMachine stateMachine,
            HashSet<string> used, HashSet<string> declared)
        {
            if (stateMachine == null) return;
            foreach (var behaviour in stateMachine.Behaviours)
                CollectBehaviourParameters(behaviour, used, declared);
            foreach (var child in stateMachine.States)
            {
                var state = child.State;
                if (state == null) continue;
                if (!string.IsNullOrEmpty(state.SpeedParameter)) used.Add(state.SpeedParameter);
                if (!string.IsNullOrEmpty(state.MirrorParameter)) used.Add(state.MirrorParameter);
                if (!string.IsNullOrEmpty(state.CycleOffsetParameter)) used.Add(state.CycleOffsetParameter);
                if (!string.IsNullOrEmpty(state.TimeParameter)) used.Add(state.TimeParameter);
                CollectVirtualMotionParameters(state.Motion, used);
                foreach (var behaviour in state.Behaviours)
                    CollectBehaviourParameters(behaviour, used, declared);
            }
            foreach (var child in stateMachine.StateMachines)
                CollectVirtualStateMachineParameters(child.StateMachine, used, declared);
        }

        private static void CollectVirtualMotionParameters(VirtualMotion motion, HashSet<string> used)
        {
            if (!(motion is VirtualBlendTree tree)) return;
            if (!string.IsNullOrEmpty(tree.BlendParameter)) used.Add(tree.BlendParameter);
            if (!string.IsNullOrEmpty(tree.BlendParameterY)) used.Add(tree.BlendParameterY);
            foreach (var child in tree.Children)
            {
                if (tree.BlendType == BlendTreeType.Direct && !string.IsNullOrEmpty(child.DirectBlendParameter))
                    used.Add(child.DirectBlendParameter);
                CollectVirtualMotionParameters(child.Motion, used);
            }
        }

        private static void CollectBehaviourParameters(StateMachineBehaviour behaviour,
            HashSet<string> used, HashSet<string> declared)
        {
            if (!behaviour) return;
            using (var serialized = new SerializedObject(behaviour))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.String
                        && declared.Contains(property.stringValue)) used.Add(property.stringValue);
                }
            }
        }

        private static void RetargetMotion(VirtualMotion motion, GameObject avatarRoot, AfkMotionPatch patch,
            ISet<VirtualMotion> visited, ISet<string> animatedTransformPaths,
            ref int clipCount, ref int copiedSourceCurves, ref int appendedMappedCurves)
        {
            if (motion == null || !visited.Add(motion)) return;
            switch (motion)
            {
                case VirtualClip clip:
                    clipCount++;
                    AfkMotionPatchEditorUtil.RetargetVirtualClipInPlace(clip, avatarRoot, patch,
                        out var copied, out var appended);
                    copiedSourceCurves += copied;
                    appendedMappedCurves += appended;
                    foreach (var binding in clip.GetFloatCurveBindings())
                        if (binding.type == typeof(Transform)) animatedTransformPaths.Add(binding.path);
                    RetargetMotion(clip.AdditiveReferencePoseClip, avatarRoot, patch, visited, animatedTransformPaths,
                        ref clipCount, ref copiedSourceCurves, ref appendedMappedCurves);
                    break;
                case VirtualBlendTree blendTree:
                    foreach (var child in blendTree.Children)
                        RetargetMotion(child.Motion, avatarRoot, patch, visited, animatedTransformPaths,
                            ref clipCount, ref copiedSourceCurves, ref appendedMappedCurves);
                    break;
            }
        }
    }
}
#endif
