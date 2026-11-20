#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace THE_cattail.AfkMotionPatcher
{
    internal sealed class AfkMotionSourceGraph
    {
        internal const string AfkParameterName = "AFK";

        internal sealed class LayerGraph
        {
            public int LayerIndex;
            public AnimatorControllerLayer Layer;
            public AnimatorState NeutralState;
            public AnimatorState AfkEntryState;
            public readonly HashSet<AnimatorState> States = new HashSet<AnimatorState>();
            public readonly List<AnimatorState> OrderedStates = new List<AnimatorState>();
            public readonly Dictionary<AnimatorState, string> StatePaths = new Dictionary<AnimatorState, string>();
            public readonly HashSet<AnimatorStateMachine> StateMachines = new HashSet<AnimatorStateMachine>();
            public readonly HashSet<AnimatorTransitionBase> Transitions = new HashSet<AnimatorTransitionBase>();
            public readonly Dictionary<AnimatorState, List<GraphEdge>> Edges = new Dictionary<AnimatorState, List<GraphEdge>>();
        }

        internal sealed class GraphEdge
        {
            public AnimatorState Source;
            public AnimatorState Destination;
            public AnimatorStateTransition StateTransition;
            public bool IsEntryFallback;
            public readonly List<AnimatorTransitionBase> Transitions = new List<AnimatorTransitionBase>();
        }

        internal sealed class PreviewStep
        {
            public AnimatorState State;
            public string StatePath;
            public string Label;
            public bool IsHoldState;
            public float NormalizedDuration = 1f;
            public AnimatorStateTransition TransitionToNext;
            public AnimatorState TransitionTargetState;
        }

        private sealed class TransitionRecord
        {
            public AnimatorState Source;
            public AnimatorStateMachine Owner;
            public AnimatorTransitionBase Transition;
        }

        private sealed class TransitionPath
        {
            public AnimatorState Destination;
            public bool IsEntryFallback;
            public readonly List<AnimatorTransitionBase> Transitions = new List<AnimatorTransitionBase>();
        }

        private sealed class LayerTopology
        {
            public readonly Dictionary<AnimatorState, AnimatorStateMachine> StateParents = new Dictionary<AnimatorState, AnimatorStateMachine>();
            public readonly Dictionary<AnimatorStateMachine, AnimatorStateMachine> MachineParents = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
        }

        private struct TraversalItem : IEquatable<TraversalItem>
        {
            public AnimatorState State;
            public bool PostExit;

            public bool Equals(TraversalItem other) => State == other.State && PostExit == other.PostExit;
            public override bool Equals(object obj) => obj is TraversalItem other && Equals(other);
            public override int GetHashCode() => ((State ? State.GetInstanceID() : 0) * 397) ^ PostExit.GetHashCode();
        }

        public AnimatorController Controller { get; private set; }
        public IReadOnlyList<LayerGraph> Layers => _layers;
        public IReadOnlyList<PreviewStep> PreviewSteps => _previewSteps;

        private readonly HashSet<string> _usedParameterNames = new HashSet<string>(StringComparer.Ordinal);

        private readonly List<LayerGraph> _layers = new List<LayerGraph>();
        private readonly List<PreviewStep> _previewSteps = new List<PreviewStep>();

        public string GetStatePath(AnimatorState state)
        {
            foreach (var layer in _layers)
                if (state && layer.StatePaths.TryGetValue(state, out var path)) return path;
            return null;
        }

        public Motion ResolveMotion(AfkMotionPatch patch, AnimatorState state)
        {
            var path = GetStatePath(state);
            var explicitMotion = patch?.sourceStateMotions?
                .LastOrDefault(item => item != null && item.statePath == path && item.motion)?.motion;
            return explicitMotion ? explicitMotion : state?.motion;
        }

        public IEnumerable<AnimationClip> EffectiveSourceClips(AfkMotionPatch patch)
        {
            var visited = new HashSet<Motion>();
            foreach (var state in _layers.SelectMany(layer => layer.OrderedStates))
                foreach (var clip in EnumerateClips(ResolveMotion(patch, state), visited))
                    if (clip) yield return clip;
        }

        public AnimationClip ResolvePreviewClip(AfkMotionPatch patch, AnimatorState state)
        {
            return ResolveMotion(patch, state) as AnimationClip;
        }

        public HashSet<string> GetUsedParameterNames(AfkMotionPatch patch)
        {
            var names = new HashSet<string>(_usedParameterNames, StringComparer.Ordinal);
            foreach (var layer in _layers)
                foreach (var state in layer.States)
                    CollectMotionParameters(ResolveMotion(patch, state), names);
            return names;
        }

        public bool ValidateStateMotions(AfkMotionPatch patch, out string error)
        {
            error = null;
            var validPaths = new HashSet<string>(_layers.SelectMany(layer => layer.StatePaths
                .Where(pair => pair.Key && layer.States.Contains(pair.Key))
                .Select(pair => pair.Value)), StringComparer.Ordinal);
            var configuredPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in patch?.sourceStateMotions ?? Enumerable.Empty<AfkMotionPatch.SourceStateMotion>())
            {
                if (item == null || string.IsNullOrEmpty(item.statePath))
                {
                    error = "A Source State Motion entry has no selected source state.";
                    return false;
                }
                if (!validPaths.Contains(item.statePath))
                {
                    error = $"Source State Motion path is not part of the extracted AFK graph: {item.statePath}";
                    return false;
                }
                if (!configuredPaths.Add(item.statePath))
                {
                    error = $"Source State Motion is configured more than once: {item.statePath}";
                    return false;
                }
                if (!item.motion)
                {
                    error = $"Source State Motion has no Motion assigned: {item.statePath}";
                    return false;
                }
            }
            return true;
        }

        public IReadOnlyList<AnimationClip> GetClipsWithAnimationEvents(AfkMotionPatch patch)
        {
            return EffectiveSourceClips(patch)
                .Where(clip => clip && AnimationUtility.GetAnimationEvents(clip).Length > 0)
                .Distinct()
                .ToArray();
        }


        public static bool TryExtract(AfkMotionPatch patch, out AfkMotionSourceGraph graph, out string error)
        {
            graph = null;
            if (!TryResolveSourceActionController(patch ? patch.sourceAvatar : null,
                    out var controller, out error)) return false;
            return TryExtract(controller, out graph, out error);
        }

        internal static bool TryResolveSourceActionController(GameObject sourceAvatar,
            out RuntimeAnimatorController controller, out string error)
        {
            controller = null;
            error = null;
            if (!sourceAvatar)
            {
                error = "Source Avatar is missing.";
                return false;
            }

            var descriptors = sourceAvatar.GetComponentsInChildren<VRCAvatarDescriptor>(true);
            if (descriptors.Length != 1)
            {
                error = $"Source Avatar must contain exactly one VRCAvatarDescriptor (found={descriptors.Length}).";
                return false;
            }

            var actionLayers = descriptors[0].baseAnimationLayers
                .Where(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.Action)
                .ToArray();
            if (actionLayers.Length != 1 || actionLayers[0].isDefault || !actionLayers[0].animatorController)
            {
                error = "Source Avatar must assign exactly one non-default custom Action Controller.";
                return false;
            }

            controller = actionLayers[0].animatorController;
            return true;
        }

        public static bool TryExtract(RuntimeAnimatorController runtimeController,
            out AfkMotionSourceGraph graph, out string error)
        {
            graph = null;
            if (!TryDiscoverEntryStatePaths(runtimeController, out var candidates, out error)) return false;
            if (candidates.Count == 0)
            {
                error = $"Source Action Controller has no state entered through an '{AfkParameterName}'-true transition.";
                return false;
            }

            var valid = new List<KeyValuePair<string, AfkMotionSourceGraph>>();
            var failures = new List<string>();
            foreach (var candidate in candidates)
            {
                if (TryExtract(runtimeController, candidate, out var candidateGraph, out var candidateError))
                    valid.Add(new KeyValuePair<string, AfkMotionSourceGraph>(candidate, candidateGraph));
                else
                    failures.Add($"{candidate}: {candidateError}");
            }

            if (valid.Count == 1)
            {
                if (!valid[0].Value.ContainsAllExtractedStatePaths(candidates))
                {
                    error = "Source Action Controller has AFK roots outside the resolved outer graph: "
                        + string.Join(", ", candidates.Where(path => !valid[0].Value.ContainsExtractedStatePath(path)));
                    return false;
                }
                graph = valid[0].Value;
                error = null;
                return true;
            }

            if (valid.Count > 1)
            {
                var dominant = valid.Where(candidate =>
                        candidate.Value.ContainsAllExtractedStatePaths(candidates))
                    .ToArray();
                if (dominant.Length == 1)
                {
                    graph = dominant[0].Value;
                    error = null;
                    return true;
                }

                error = "Source Action Controller has multiple independent AFK roots: "
                    + string.Join(", ", valid.Select(candidate => candidate.Key));
                return false;
            }

            error = "Source Action Controller has no supported AFK root. " + string.Join(" | ", failures);
            return false;
        }

        private bool ContainsExtractedStatePath(string statePath)
        {
            return _layers.Any(layer => layer.States.Any(state =>
                layer.StatePaths.TryGetValue(state, out var path) && path == statePath));
        }

        private bool ContainsAllExtractedStatePaths(IEnumerable<string> statePaths)
        {
            return statePaths.All(ContainsExtractedStatePath);
        }

        public static bool TryExtract(RuntimeAnimatorController runtimeController, string entryStatePath,
            out AfkMotionSourceGraph graph, out string error)
        {
            graph = null;
            error = null;
            var controller = runtimeController as AnimatorController;
            if (!controller)
            {
                error = runtimeController
                    ? "Source Action Controller must be an AnimatorController asset."
                    : "Source Action Controller is missing.";
                return false;
            }

            var afkParameter = controller.parameters.FirstOrDefault(parameter => parameter.name == AfkParameterName);
            if (afkParameter == null || afkParameter.type != AnimatorControllerParameterType.Bool)
            {
                error = $"Source Action Controller must declare the '{AfkParameterName}' Bool parameter.";
                return false;
            }

            if (string.IsNullOrEmpty(entryStatePath))
            {
                error = "Source AFK Entry State is missing.";
                return false;
            }

            var entryMatches = controller.layers
                .SelectMany(layer =>
                {
                    var paths = new Dictionary<AnimatorState, string>();
                    BuildStatePaths(layer.stateMachine, layer.name, paths);
                    return paths.Where(pair => pair.Value == entryStatePath).Select(pair => pair.Key);
                })
                .Where(state => state)
                .ToArray();
            if (entryMatches.Length != 1)
            {
                error = $"Source AFK Entry State path must identify exactly one state (matches={entryMatches.Length}): {entryStatePath}";
                return false;
            }
            var selectedEntryState = entryMatches[0];

            var result = new AfkMotionSourceGraph { Controller = controller };
            for (var layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                var layer = controller.layers[layerIndex];
                var extracted = ExtractLayer(layer, layerIndex, selectedEntryState, entryStatePath, out var layerError);
                if (!string.IsNullOrEmpty(layerError))
                {
                    error = layerError;
                    return false;
                }
                if (extracted != null) result._layers.Add(extracted);
            }

            if (result._layers.Count == 0)
            {
                error = $"Source AFK Entry State was not found in {controller.name}: {entryStatePath}";
                return false;
            }
            if (result._layers.Count > 1)
            {
                error = "Source AFK Preview/build currently supports exactly one Controller layer.";
                return false;
            }

            var duplicatePath = result._layers
                .SelectMany(layer => layer.StatePaths.Where(pair => layer.States.Contains(pair.Key)).Select(pair => pair.Value))
                .GroupBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (!string.IsNullOrEmpty(duplicatePath))
            {
                error = $"Extracted source state path is not unique: {duplicatePath}";
                return false;
            }

            result.CollectUsedParameters();
            result.BuildPreviewRoute();
            graph = result;
            return true;
        }

        private static bool TryDiscoverEntryStatePaths(RuntimeAnimatorController runtimeController,
            out IReadOnlyList<string> candidates, out string error)
        {
            candidates = Array.Empty<string>();
            error = null;
            var controller = runtimeController as AnimatorController;
            if (!controller)
            {
                error = runtimeController
                    ? "Source Action Controller must be an AnimatorController asset."
                    : "Source Action Controller is missing.";
                return false;
            }

            var paths = new List<string>();
            var allPaths = new List<string>();
            foreach (var layer in controller.layers)
            {
                if (layer?.stateMachine == null) continue;
                var topology = BuildTopology(layer.stateMachine);
                var statePaths = new Dictionary<AnimatorState, string>();
                BuildStatePaths(layer.stateMachine, layer.name, statePaths);
                allPaths.AddRange(statePaths.Values);
                foreach (var record in EnumerateTransitions(layer.stateMachine)
                             .Where(record => HasAfkTrueCondition(record.Transition)))
                {
                    foreach (var path in ResolveTransitionPaths(record.Transition, record.Owner, topology))
                    {
                        if (path.Destination && statePaths.TryGetValue(path.Destination, out var statePath))
                            paths.Add(statePath);
                    }
                }
            }

            var pathCounts = allPaths.GroupBy(path => path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var ambiguous = paths.Distinct(StringComparer.Ordinal)
                .Where(path => pathCounts.TryGetValue(path, out var count) && count != 1)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (ambiguous.Length > 0)
            {
                error = "Source Action Controller has ambiguous AFK candidate state paths: "
                    + string.Join(", ", ambiguous);
                return false;
            }

            candidates = paths.Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        private static LayerGraph ExtractLayer(AnimatorControllerLayer layer, int layerIndex,
            AnimatorState selectedEntryState, string entryStatePath, out string error)
        {
            error = null;
            if (layer?.stateMachine == null) return null;
            var topology = BuildTopology(layer.stateMachine);
            var statePaths = new Dictionary<AnimatorState, string>();
            BuildStatePaths(layer.stateMachine, layer.name, statePaths);
            if (!statePaths.TryGetValue(selectedEntryState, out var selectedPath)
                || selectedPath != entryStatePath) return null;
            if (layer.syncedLayerIndex >= 0)
            {
                error = $"Synced source Controller layers are not supported: {layer.name}";
                return null;
            }
            if (!topology.StateParents.TryGetValue(selectedEntryState, out var entryParent)
                || entryParent != layer.stateMachine)
            {
                error = $"Nested source AFK entry states are not supported: {entryStatePath}";
                return null;
            }
            var afkEntryState = selectedEntryState;
            var neutralEntryState = layer.stateMachine.defaultState;
            if (!neutralEntryState)
            {
                error = $"Source Controller layer has no neutral default state: {layer.name}";
                return null;
            }

            var records = EnumerateTransitions(layer.stateMachine).ToList();
            var entryRecords = records.Where(record => HasAfkTrueCondition(record.Transition)).ToList();
            var neutralEntryRecords = entryRecords.Where(record =>
                    (record.Source == neutralEntryState || record.Source == null)
                    && ResolveTransitionPaths(record.Transition, record.Owner, topology)
                        .Any(path => path.Destination == afkEntryState))
                .ToList();
            if (neutralEntryRecords.Count == 0)
            {
                error = $"Selected Source AFK Entry State must be reached from the neutral default state "
                    + $"through an '{AfkParameterName}'-true transition: {entryStatePath}";
                return null;
            }

            var layerGraph = new LayerGraph
            {
                Layer = layer,
                LayerIndex = layerIndex,
                NeutralState = neutralEntryState,
                AfkEntryState = afkEntryState
            };
            foreach (var pair in statePaths) layerGraph.StatePaths[pair.Key] = pair.Value;
            var queue = new Queue<TraversalItem>();
            queue.Enqueue(new TraversalItem { State = layerGraph.AfkEntryState, PostExit = false });

            var visited = new HashSet<TraversalItem>();
            var contextMasks = new Dictionary<AnimatorState, int>();
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (!item.State || !visited.Add(item)) continue;
                contextMasks.TryGetValue(item.State, out var contextMask);
                contextMasks[item.State] = contextMask | (item.PostExit ? 2 : 1);
                if (layerGraph.States.Add(item.State)) layerGraph.OrderedStates.Add(item.State);

                foreach (var record in GetOutgoingTransitions(item.State, topology))
                {
                    foreach (var path in ResolveTransitionPaths(record.Transition, record.Owner, topology))
                    {
                        if (item.PostExit && path.Transitions.Any(transition => transition.conditions
                                .Any(condition => condition.parameter != AfkParameterName)))
                            continue;

                        if (record.Transition is AnimatorStateTransition stateTransition
                            && !stateTransition.canTransitionToSelf && path.Destination == item.State) continue;

                        var postExit = item.PostExit || path.Transitions.Any(HasAfkFalseCondition);
                        if (path.Transitions.Any(HasAfkTrueCondition)) postExit = false;
                        AddEdge(layerGraph, item.State, record.Transition as AnimatorStateTransition, path);
                        if (path.Destination)
                            queue.Enqueue(new TraversalItem { State = path.Destination, PostExit = postExit });
                    }
                }
            }

            var overlappingState = contextMasks.FirstOrDefault(pair => pair.Value == 3).Key;
            if (overlappingState)
            {
                error = $"Source AFK state is reachable both before and after the AFK exit boundary: {layerGraph.StatePaths[overlappingState]}";
                return null;
            }

            if (layerGraph.States.Add(neutralEntryState)) layerGraph.OrderedStates.Insert(0, neutralEntryState);
            foreach (var record in neutralEntryRecords)
            {
                foreach (var path in ResolveTransitionPaths(record.Transition, record.Owner, topology)
                             .Where(path => path.Destination == afkEntryState))
                    AddEdge(layerGraph, neutralEntryState,
                        record.Transition as AnimatorStateTransition, path);
            }

            foreach (var stateMachine in EnumerateStateMachines(layer.stateMachine))
            {
                if (EnumerateStates(stateMachine).Any(layerGraph.States.Contains))
                    layerGraph.StateMachines.Add(stateMachine);
            }

            return layerGraph;
        }

        private static LayerTopology BuildTopology(AnimatorStateMachine root)
        {
            var topology = new LayerTopology();
            BuildTopology(root, null, topology);
            return topology;
        }

        private static void BuildTopology(AnimatorStateMachine stateMachine, AnimatorStateMachine parent,
            LayerTopology topology)
        {
            if (!stateMachine) return;
            if (parent) topology.MachineParents[stateMachine] = parent;
            foreach (var child in stateMachine.states)
                if (child.state) topology.StateParents[child.state] = stateMachine;
            foreach (var child in stateMachine.stateMachines)
                if (child.stateMachine) BuildTopology(child.stateMachine, stateMachine, topology);
        }

        private static IEnumerable<TransitionRecord> GetOutgoingTransitions(AnimatorState state, LayerTopology topology)
        {
            if (!state || !topology.StateParents.TryGetValue(state, out var owner)) yield break;
            foreach (var transition in ActiveTransitions(state.transitions
                         .Where(transition => transition != null).Cast<AnimatorTransitionBase>()))
                yield return new TransitionRecord { Source = state, Owner = owner, Transition = transition };

            var machine = owner;
            while (machine)
            {
                var anyTransitions = machine.anyStateTransitions
                    .Where(transition => transition != null)
                    .Cast<AnimatorTransitionBase>();
                foreach (var transition in ActiveTransitions(anyTransitions))
                    yield return new TransitionRecord { Source = state, Owner = machine, Transition = transition };
                if (!topology.MachineParents.TryGetValue(machine, out machine)) break;
            }
        }

        private static IEnumerable<AnimatorTransitionBase> ActiveTransitions(
            IEnumerable<AnimatorTransitionBase> transitions)
        {
            var active = transitions.Where(transition => transition != null && !transition.mute).ToList();
            return active.Any(transition => transition.solo)
                ? active.Where(transition => transition.solo)
                : active;
        }

        private static IEnumerable<TransitionPath> ResolveTransitionPaths(AnimatorTransitionBase transition,
            AnimatorStateMachine owner, LayerTopology topology)
        {
            return ResolveTransitionPaths(transition, owner, topology,
                new List<AnimatorTransitionBase>(), new HashSet<AnimatorTransitionBase>());
        }

        private static IEnumerable<TransitionPath> ResolveTransitionPaths(AnimatorTransitionBase transition,
            AnimatorStateMachine owner, LayerTopology topology, List<AnimatorTransitionBase> prefix,
            HashSet<AnimatorTransitionBase> visited)
        {
            if (transition == null || transition.mute || !visited.Add(transition)) yield break;
            var nextPrefix = new List<AnimatorTransitionBase>(prefix) { transition };

            if (transition.destinationState)
            {
                var path = new TransitionPath { Destination = transition.destinationState };
                path.Transitions.AddRange(nextPrefix);
                yield return path;
            }
            else if (transition.destinationStateMachine)
            {
                var destinationMachine = transition.destinationStateMachine;
                var entries = ActiveTransitions(destinationMachine.entryTransitions
                    .Where(entry => entry != null).Cast<AnimatorTransitionBase>()).Cast<AnimatorTransition>().ToArray();
                if (entries.Length == 0)
                {
                    var path = new TransitionPath { Destination = destinationMachine.defaultState };
                    path.Transitions.AddRange(nextPrefix);
                    yield return path;
                }
                else
                {
                    foreach (var entry in entries)
                        foreach (var path in ResolveTransitionPaths(entry, destinationMachine, topology, nextPrefix, visited))
                            yield return path;
                    if (destinationMachine.defaultState)
                    {
                        var fallback = new TransitionPath
                        {
                            Destination = destinationMachine.defaultState,
                            IsEntryFallback = true
                        };
                        fallback.Transitions.AddRange(nextPrefix);
                        yield return fallback;
                    }
                }
            }
            else if (transition.isExit)
            {
                if (owner && topology.MachineParents.TryGetValue(owner, out var parent))
                {
                    var exits = ActiveTransitions(parent.GetStateMachineTransitions(owner)
                        .Where(exit => exit != null).Cast<AnimatorTransitionBase>()).Cast<AnimatorTransition>().ToArray();
                    if (exits.Length == 0)
                    {
                        var path = new TransitionPath();
                        path.Transitions.AddRange(nextPrefix);
                        yield return path;
                    }
                    else
                    {
                        foreach (var exit in exits)
                            foreach (var path in ResolveTransitionPaths(exit, parent, topology, nextPrefix, visited))
                                yield return path;
                    }
                }
                else
                {
                    var path = new TransitionPath();
                    path.Transitions.AddRange(nextPrefix);
                    yield return path;
                }
            }
            visited.Remove(transition);
        }

        private static void AddEdge(LayerGraph graph, AnimatorState source,
            AnimatorStateTransition stateTransition, TransitionPath path)
        {
            if (!graph.Edges.TryGetValue(source, out var edges))
                graph.Edges[source] = edges = new List<GraphEdge>();
            if (edges.Any(edge => edge.Destination == path.Destination
                    && edge.IsEntryFallback == path.IsEntryFallback
                    && edge.Transitions.SequenceEqual(path.Transitions))) return;
            var graphEdge = new GraphEdge
            {
                Source = source,
                Destination = path.Destination,
                StateTransition = stateTransition,
                IsEntryFallback = path.IsEntryFallback
            };
            graphEdge.Transitions.AddRange(path.Transitions);
            edges.Add(graphEdge);
            foreach (var transition in path.Transitions) graph.Transitions.Add(transition);
        }

        private void CollectUsedParameters()
        {
            _usedParameterNames.Add(AfkParameterName);
            foreach (var layer in _layers)
            {
                foreach (var transition in layer.Transitions)
                    foreach (var condition in transition.conditions)
                        if (!string.IsNullOrEmpty(condition.parameter)) _usedParameterNames.Add(condition.parameter);

                foreach (var state in layer.States)
                {
                    if (state.speedParameterActive && !string.IsNullOrEmpty(state.speedParameter)) _usedParameterNames.Add(state.speedParameter);
                    if (state.mirrorParameterActive && !string.IsNullOrEmpty(state.mirrorParameter)) _usedParameterNames.Add(state.mirrorParameter);
                    if (state.cycleOffsetParameterActive && !string.IsNullOrEmpty(state.cycleOffsetParameter)) _usedParameterNames.Add(state.cycleOffsetParameter);
                    if (state.timeParameterActive && !string.IsNullOrEmpty(state.timeParameter)) _usedParameterNames.Add(state.timeParameter);
                }
            }
        }

        private static void CollectMotionParameters(Motion motion, HashSet<string> names)
        {
            if (!(motion is BlendTree tree)) return;
            if (!string.IsNullOrEmpty(tree.blendParameter)) names.Add(tree.blendParameter);
            if (!string.IsNullOrEmpty(tree.blendParameterY)) names.Add(tree.blendParameterY);
            foreach (var child in tree.children)
            {
                if (tree.blendType == BlendTreeType.Direct && !string.IsNullOrEmpty(child.directBlendParameter))
                    names.Add(child.directBlendParameter);
                CollectMotionParameters(child.motion, names);
            }
        }

        private void BuildPreviewRoute()
        {
            var layer = _layers.FirstOrDefault();
            if (layer == null || !layer.AfkEntryState) return;

            var defaults = Controller.parameters.ToDictionary(parameter => parameter.name, ParameterDefaultValue);
            var current = layer.AfkEntryState;
            var afk = true;
            var switchedToExit = false;
            var guard = 0;
            var visits = new Dictionary<Tuple<AnimatorState, bool>, int>();

            while (current && guard++ < 128)
            {
                var key = Tuple.Create(current, afk);
                visits.TryGetValue(key, out var visitCount);
                visits[key] = visitCount + 1;
                if (visitCount > 3) break;

                var currentPreviewStep = _previewSteps.LastOrDefault();
                if (currentPreviewStep == null || currentPreviewStep.State != current) currentPreviewStep = null;
                var outgoing = layer.Edges.TryGetValue(current, out var stateEdges)
                    ? stateEdges : new List<GraphEdge>();
                var matching = outgoing
                    .Where(edge => edge.Transitions.All(transition => TransitionConditionsMatch(transition, defaults, afk)))
                    .ToArray();
                var nonFallback = matching.Where(edge => !edge.IsEntryFallback).ToArray();
                var eligible = nonFallback.Length > 0 ? nonFallback : matching;

                var hasAfkFalseExit = outgoing.Any(edge => edge.Destination != current
                    && edge.Transitions.Any(HasAfkFalseCondition)
                    && edge.Transitions.All(transition => TransitionConditionsMatch(transition, defaults, false)));
                var hasProgressWhileAfk = eligible.Any(edge => edge.Destination != current);
                if (afk && !switchedToExit && hasAfkFalseExit && !hasProgressWhileAfk)
                {
                    AddPreviewStep(current, true, 1f);
                    afk = false;
                    switchedToExit = true;
                    continue;
                }
                if (!afk)
                {
                    var falseEdges = eligible.Where(edge => edge.Transitions.Any(HasAfkFalseCondition)).ToArray();
                    if (falseEdges.Length > 0) eligible = falseEdges;
                }

                var transitionToTake = eligible.FirstOrDefault(edge => edge.StateTransition == null || !edge.StateTransition.hasExitTime)
                    ?? eligible.FirstOrDefault();

                if (transitionToTake == null && afk && !switchedToExit)
                {
                    AddPreviewStep(current, true, 1f);
                    afk = false;
                    switchedToExit = true;
                    continue;
                }

                if (transitionToTake == null)
                {
                    AddPreviewStep(current, false, 1f);
                    break;
                }

                if (transitionToTake.StateTransition != null && transitionToTake.StateTransition.hasExitTime)
                {
                    currentPreviewStep = AddPreviewStep(
                        current, false, Mathf.Max(0f, transitionToTake.StateTransition.exitTime));
                }

                if (currentPreviewStep != null)
                {
                    currentPreviewStep.TransitionToNext = transitionToTake.StateTransition;
                    currentPreviewStep.TransitionTargetState = transitionToTake.Destination;
                }
                current = transitionToTake.Destination;
                if (!current) break;
            }


            for (var index = 0; index < _previewSteps.Count; index++)
            {
                var step = _previewSteps[index];
                var duplicateCount = _previewSteps.Count(candidate => candidate.State && candidate.State.name == step.State.name);
                step.Label = duplicateCount > 1 ? $"{step.State.name} {index + 1}" : step.State.name;
            }
        }

        private PreviewStep AddPreviewStep(AnimatorState state, bool hold, float normalizedDuration)
        {
            if (!state || normalizedDuration <= 0.0001f) return null;
            var previous = _previewSteps.LastOrDefault();
            if (previous != null && previous.State == state && previous.IsHoldState == hold) return previous;
            var step = new PreviewStep
            {
                State = state,
                StatePath = GetStatePath(state),
                Label = state.name,
                IsHoldState = hold,
                NormalizedDuration = normalizedDuration
            };
            _previewSteps.Add(step);
            return step;
        }

        private static void BuildStatePaths(AnimatorStateMachine stateMachine, string prefix,
            Dictionary<AnimatorState, string> paths)
        {
            if (!stateMachine) return;
            foreach (var child in stateMachine.states)
                if (child.state) paths[child.state] = prefix + "/" + child.state.name;
            foreach (var child in stateMachine.stateMachines)
                if (child.stateMachine) BuildStatePaths(child.stateMachine, prefix + "/" + child.stateMachine.name, paths);
        }

        private static IEnumerable<TransitionRecord> EnumerateTransitions(AnimatorStateMachine root)
        {
            foreach (var stateMachine in EnumerateStateMachines(root))
            {
                foreach (var transition in ActiveTransitions(stateMachine.anyStateTransitions
                             .Where(transition => transition != null).Cast<AnimatorTransitionBase>()).Cast<AnimatorStateTransition>())
                    yield return new TransitionRecord { Owner = stateMachine, Transition = transition };
                foreach (var transition in ActiveTransitions(stateMachine.entryTransitions
                             .Where(transition => transition != null).Cast<AnimatorTransitionBase>()).Cast<AnimatorTransition>())
                    yield return new TransitionRecord { Owner = stateMachine, Transition = transition };
                foreach (var child in stateMachine.states)
                {
                    if (!child.state) continue;
                    foreach (var transition in ActiveTransitions(child.state.transitions
                                 .Where(transition => transition != null).Cast<AnimatorTransitionBase>()).Cast<AnimatorStateTransition>())
                        yield return new TransitionRecord { Source = child.state, Owner = stateMachine, Transition = transition };
                }
                foreach (var child in stateMachine.stateMachines)
                {
                    if (!child.stateMachine) continue;
                    foreach (var transition in ActiveTransitions(stateMachine.GetStateMachineTransitions(child.stateMachine)
                                 .Where(transition => transition != null).Cast<AnimatorTransitionBase>()).Cast<AnimatorTransition>())
                        yield return new TransitionRecord { Owner = stateMachine, Transition = transition };
                }
            }
        }

        private static IEnumerable<AnimatorStateMachine> EnumerateStateMachines(AnimatorStateMachine root)
        {
            if (!root) yield break;
            yield return root;
            foreach (var child in root.stateMachines)
            {
                foreach (var nested in EnumerateStateMachines(child.stateMachine)) yield return nested;
            }
        }

        private static IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine root)
        {
            if (!root) yield break;
            foreach (var child in root.states)
                if (child.state) yield return child.state;
            foreach (var child in root.stateMachines)
                foreach (var state in EnumerateStates(child.stateMachine)) yield return state;
        }

        private static IEnumerable<AnimationClip> EnumerateClips(Motion motion, ISet<Motion> visited)
        {
            if (!motion || !visited.Add(motion)) yield break;
            if (motion is AnimationClip clip)
            {
                yield return clip;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                foreach (var reference in EnumerateClips(settings.additiveReferencePoseClip, visited))
                    yield return reference;
                yield break;
            }
            if (!(motion is BlendTree tree)) yield break;
            foreach (var child in tree.children)
                foreach (var nested in EnumerateClips(child.motion, visited)) yield return nested;
        }

        private static bool HasAfkTrueCondition(AnimatorTransitionBase transition)
            => transition != null && transition.conditions.Any(IsAfkTrueCondition);

        private static bool HasAfkFalseCondition(AnimatorTransitionBase transition)
            => transition != null && transition.conditions.Any(IsAfkFalseCondition);

        private static bool IsAfkTrueCondition(AnimatorCondition condition)
        {
            if (condition.parameter != AfkParameterName) return false;
            switch (condition.mode)
            {
                case AnimatorConditionMode.If: return true;
                case AnimatorConditionMode.Greater: return condition.threshold <= 0f;
                case AnimatorConditionMode.Equals: return condition.threshold > 0f;
                case AnimatorConditionMode.NotEqual: return Mathf.Abs(condition.threshold) < 0.0001f;
                default: return false;
            }
        }

        private static bool IsAfkFalseCondition(AnimatorCondition condition)
        {
            if (condition.parameter != AfkParameterName) return false;
            switch (condition.mode)
            {
                case AnimatorConditionMode.IfNot: return true;
                case AnimatorConditionMode.Less: return condition.threshold > 0f;
                case AnimatorConditionMode.Equals: return Mathf.Abs(condition.threshold) < 0.0001f;
                case AnimatorConditionMode.NotEqual: return condition.threshold > 0f;
                default: return false;
            }
        }

        private static bool TransitionConditionsMatch(AnimatorTransitionBase transition, Dictionary<string, float> defaults, bool afk)
        {
            return transition.conditions.All(condition => ConditionMatches(condition, defaults, afk));
        }

        private static bool ConditionMatches(AnimatorCondition condition, Dictionary<string, float> defaults, bool afk)
        {
            var value = condition.parameter == AfkParameterName
                ? (afk ? 1f : 0f)
                : (defaults.TryGetValue(condition.parameter, out var current) ? current : 0f);
            switch (condition.mode)
            {
                case AnimatorConditionMode.If: return value > 0.5f;
                case AnimatorConditionMode.IfNot: return value <= 0.5f;
                case AnimatorConditionMode.Greater: return value > condition.threshold;
                case AnimatorConditionMode.Less: return value < condition.threshold;
                case AnimatorConditionMode.Equals: return Mathf.Abs(value - condition.threshold) < 0.0001f;
                case AnimatorConditionMode.NotEqual: return Mathf.Abs(value - condition.threshold) >= 0.0001f;
                default: return false;
            }
        }

        private static float ParameterDefaultValue(AnimatorControllerParameter parameter)
        {
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Bool: return parameter.defaultBool ? 1f : 0f;
                case AnimatorControllerParameterType.Int: return parameter.defaultInt;
                default: return parameter.defaultFloat;
            }
        }
    }
}
#endif
