#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace THE_cattail.AfkMotionPatcher.Tests
{
    public sealed class AfkMotionPatchV2Tests
    {
        private readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _cleanup)
            {
                if (obj) Object.DestroyImmediate(obj);
            }
            _cleanup.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            if (obj) _cleanup.Add(obj);
            return obj;
        }

        [Test]
        public void PreserveExistingAction_KeepsGoGoLocoStatesAndParameters_AndStripsOldAfkEntry()
        {
            // Build Source Avatar with AFK graph
            var sourceAvatar = Track(new GameObject("SourceAvatar"));
            var sourceDesc = sourceAvatar.AddComponent<VRCAvatarDescriptor>();
            var sourceClip = Track(new AnimationClip { name = "SourceAfkLoop" });
            AnimationUtility.SetEditorCurve(sourceClip,
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_nagomi"),
                AnimationCurve.Constant(0f, 1f, 80f));

            var sourceController = Track(BuildSimpleAfkController("SourceAction", sourceClip));
            AssignActionController(sourceDesc, sourceController);

            // Build Target Avatar with GoGoLoco Action layer + old AFK branch
            var targetAvatar = Track(new GameObject("TargetAvatar"));
            var targetDesc = targetAvatar.AddComponent<VRCAvatarDescriptor>();
            var targetController = Track(new AnimatorController { name = "GoGoLocoAction" });
            targetController.AddParameter("AFK", AnimatorControllerParameterType.Bool);
            targetController.AddParameter("Go/Pose", AnimatorControllerParameterType.Int);
            targetController.AddParameter("VRCEmote", AnimatorControllerParameterType.Int);

            var gogoSm = new AnimatorStateMachine { name = "GoGoLoco" };
            targetController.AddLayer(new AnimatorControllerLayer
            {
                name = "GoGoLoco",
                stateMachine = gogoSm,
                defaultWeight = 1f
            });
            var idle = gogoSm.AddState("WaitForAction");
            var sitPose = gogoSm.AddState("GoGo_SitPose");
            var oldAfk = gogoSm.AddState("OldAfkState");
            gogoSm.defaultState = idle;

            var toSit = idle.AddTransition(sitPose);
            toSit.hasExitTime = false;
            toSit.AddCondition(AnimatorConditionMode.Equals, 3f, "Go/Pose");

            var toOldAfk = idle.AddTransition(oldAfk);
            toOldAfk.hasExitTime = false;
            toOldAfk.AddCondition(AnimatorConditionMode.If, 0f, "AFK");

            AssignActionController(targetDesc, targetController);

            var patch = targetAvatar.AddComponent<AfkMotionPatch>();
            patch.sourceAvatar = sourceAvatar;
            patch.actionMergeMode = AfkMotionPatch.ActionMergeMode.PreserveExistingAction;

            var prevIgnore = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                using (new OverrideTemporaryDirectoryScope(null))
                {
                    AvatarProcessor.ProcessAvatar(targetAvatar);
                }
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = prevIgnore;
            }

            var resultActionLayer = targetDesc.baseAnimationLayers
                .First(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action);
            var resultController = resultActionLayer.animatorController as AnimatorController;
            Assert.That(resultController, Is.Not.Null);

            // 1. GoGoLoco layer & parameters are 100% preserved
            Assert.That(resultController.layers.Length, Is.GreaterThanOrEqualTo(2));
            var preservedGogoLayer = resultController.layers.FirstOrDefault(l => l.name == "GoGoLoco");
            Assert.That(preservedGogoLayer, Is.Not.Null);
            Assert.That(resultController.parameters.Any(p => p.name == "Go/Pose"), Is.True);
            Assert.That(resultController.parameters.Any(p => p.name == "VRCEmote"), Is.True);
            Assert.That(resultController.parameters.Any(p => p.name == "AFK"), Is.True);

            var resultIdle = preservedGogoLayer.stateMachine.states
                .Select(s => s.state)
                .FirstOrDefault(s => s.name == "WaitForAction");
            Assert.That(resultIdle, Is.Not.Null);
            // Go/Pose transition stays intact
            Assert.That(resultIdle.transitions.Any(t => t.conditions.Any(c => c.parameter == "Go/Pose")), Is.True);
            // Old AFK == true transition is stripped
            Assert.That(resultIdle.transitions.Any(t => t.conditions.Any(c => c.parameter == "AFK" && c.mode == AnimatorConditionMode.If)), Is.False);

            // 2. Appended source AFK layer exists with weight 1.0 and AFK == true transition
            var appendedAfkLayer = resultController.layers.Last();
            Assert.That(appendedAfkLayer.defaultWeight, Is.EqualTo(1f).Within(0.001f));
            var hasNewAfkEntry = appendedAfkLayer.stateMachine.states
                .SelectMany(s => s.state.transitions)
                .Any(t => t.conditions.Any(c => c.parameter == "AFK" && c.mode == AnimatorConditionMode.If));
            Assert.That(hasNewAfkEntry, Is.True);
        }

        [Test]
        public void AutoMatch_MatchesIsomorphicNonHumanoidBonesAndSimilarBlendShapes()
        {
            var sourceAvatar = Track(new GameObject("Source"));
            var srcTailRoot = CreateChild(sourceAvatar.transform, "Armature/Hips/Tail");
            CreateChild(srcTailRoot, "Tail.001");
            CreateChild(srcTailRoot, "Tail.002");
            CreateChild(sourceAvatar.transform, "Armature/Hips/Spine/Head/Ear_Root/Ear_L");

            var targetAvatar = Track(new GameObject("Target"));
            var dstTailRoot = CreateChild(targetAvatar.transform, "Armature/Hips/Tail");
            CreateChild(dstTailRoot, "Tail_1");
            CreateChild(dstTailRoot, "Tail_2");
            CreateChild(targetAvatar.transform, "Armature/Hips/Spine/Head/Ear_L");

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(targetAvatar.transform, false);
            var smr = bodyGo.AddComponent<SkinnedMeshRenderer>();
            var mesh = Track(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("eye_nagomi_2", 100f, new Vector3[3], null, null);
            mesh.AddBlendShapeFrame("mayu_angry_1", 100f, new Vector3[3], null, null);
            smr.sharedMesh = mesh;

            var patch = targetAvatar.AddComponent<AfkMotionPatch>();
            patch.sourceAvatar = sourceAvatar;

            var bindings = new[]
            {
                EditorCurveBinding.FloatCurve("Armature/Hips/Tail/Tail.001", typeof(Transform), "localEulerAnglesRaw.x"),
                EditorCurveBinding.FloatCurve("Armature/Hips/Tail/Tail.002", typeof(Transform), "localEulerAnglesRaw.x"),
                EditorCurveBinding.FloatCurve("Armature/Hips/Spine/Head/Ear_Root/Ear_L", typeof(Transform), "localEulerAnglesRaw.z"),
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_nagomi"),
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eyebrow_angry_1"),
            };

            var summary = AfkMotionAutoMatcher.AutoMatchHierarchyAndBlendShapes(patch, targetAvatar, bindings);

            Assert.That(summary.AddedHierarchyMappings, Is.EqualTo(3));
            Assert.That(patch.rendererPathRemaps.Any(r => r.fromPath.EndsWith("Tail.001") && r.toPath.EndsWith("Tail_1")), Is.True);
            Assert.That(patch.rendererPathRemaps.Any(r => r.fromPath.EndsWith("Tail.002") && r.toPath.EndsWith("Tail_2")), Is.True);
            Assert.That(patch.rendererPathRemaps.Any(r => r.fromPath.EndsWith("Ear_Root/Ear_L") && r.toPath.EndsWith("Head/Ear_L")), Is.True);

            Assert.That(summary.AddedBlendShapeMappings, Is.EqualTo(2));
            Assert.That(patch.blendShapeNameRemaps.Any(r => r.fromName == "eye_nagomi" && r.toName == "eye_nagomi_2"), Is.True);
            Assert.That(patch.blendShapeNameRemaps.Any(r => r.fromName == "eyebrow_angry_1" && r.toName == "mayu_angry_1"), Is.True);
        }

        [Test]
        public void RestPoseDelta_AndBoneRotationOffset_ShiftsEulerCurvesCorrectly()
        {
            var sourceAvatar = Track(new GameObject("Source"));
            var srcEar = CreateChild(sourceAvatar.transform, "Armature/Head/Ear_L");
            srcEar.localRotation = Quaternion.Euler(0f, 0f, 10f);

            var targetAvatar = Track(new GameObject("Target"));
            var dstEar = CreateChild(targetAvatar.transform, "Armature/Head/Ear_L");
            dstEar.localRotation = Quaternion.Euler(0f, 0f, 35f);

            var patch = targetAvatar.AddComponent<AfkMotionPatch>();
            patch.sourceAvatar = sourceAvatar;

            var bindings = new[]
            {
                EditorCurveBinding.FloatCurve("Armature/Head/Ear_L", typeof(Transform), "localEulerAnglesRaw.z")
            };

            var deltaSummary = AfkMotionAutoMatcher.MatchRestPoseDeltas(patch, targetAvatar, bindings);
            Assert.That(deltaSummary.AddedOffsets, Is.EqualTo(1));
            Assert.That(patch.boneRotationOffsets[0].eulerOffset.z, Is.EqualTo(25f).Within(0.2f));

            var sourceClip = Track(new AnimationClip { name = "EarClip" });
            AnimationUtility.SetEditorCurve(sourceClip,
                EditorCurveBinding.FloatCurve("Armature/Head/Ear_L", typeof(Transform), "localEulerAnglesRaw.z"),
                AnimationCurve.Constant(0f, 1f, 10f));

            var previewClip = Track(AfkMotionPatchEditorUtil.CreatePreviewClip(
                null, sourceClip, targetAvatar, patch, "Test", out _, out _));

            var curve = AnimationUtility.GetEditorCurve(previewClip,
                EditorCurveBinding.FloatCurve("Armature/Head/Ear_L", typeof(Transform), "localEulerAnglesRaw.z"));
            Assert.That(curve, Is.Not.Null);
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(35f).Within(0.2f));
        }

        [Test]
        public void AnimationEvent_IsStrippedSafelyByDefault_WithoutFailingValidation()
        {
            var sourceAvatar = Track(new GameObject("SourceWithEvent"));
            var sourceDesc = sourceAvatar.AddComponent<VRCAvatarDescriptor>();
            var clip = Track(new AnimationClip { name = "ClipWithEvent" });
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Hips", typeof(Transform), "localEulerAnglesRaw.x"),
                AnimationCurve.Constant(0f, 1f, 5f));
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = 0.25f, functionName = "OnCustomEvent" }
            });

            var controller = Track(BuildSimpleAfkController("SourceEventAction", clip));
            AssignActionController(sourceDesc, controller);

            var targetAvatar = Track(new GameObject("Target"));
            var patch = targetAvatar.AddComponent<AfkMotionPatch>();
            patch.sourceAvatar = sourceAvatar;
            patch.stripAnimationEvents = true;

            Assert.That(AfkMotionSourceGraph.TryExtract(patch, out var graph, out var graphError), Is.True, graphError);
            Assert.That(graph.ValidateStateMotions(patch, out var motionError), Is.True, motionError);
            Assert.That(graph.GetClipsWithAnimationEvents(patch).Count, Is.EqualTo(1));

            var previewClip = Track(AfkMotionPatchEditorUtil.CreatePreviewClip(
                null, clip, targetAvatar, patch, "StripTest", out _, out _));
            Assert.That(AnimationUtility.GetAnimationEvents(previewClip).Length, Is.EqualTo(0));

            patch.stripAnimationEvents = false;
            var keptPreviewClip = Track(AfkMotionPatchEditorUtil.CreatePreviewClip(
                null, clip, targetAvatar, patch, "KeepTest", out _, out _));
            Assert.That(AnimationUtility.GetAnimationEvents(keptPreviewClip).Length, Is.EqualTo(1));
        }

        private static Transform CreateChild(Transform parent, string relativePath)
        {
            var current = parent;
            foreach (var part in relativePath.Split('/'))
            {
                var existing = current.Find(part);
                if (existing)
                {
                    current = existing;
                    continue;
                }
                var go = new GameObject(part);
                go.transform.SetParent(current, false);
                current = go.transform;
            }
            return current;
        }

        private static AnimatorController BuildSimpleAfkController(string name, AnimationClip afkClip)
        {
            var controller = new AnimatorController { name = name };
            controller.AddParameter(AfkMotionSourceGraph.AfkParameterName, AnimatorControllerParameterType.Bool);
            var sm = new AnimatorStateMachine { name = "Action" };
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "Action",
                stateMachine = sm,
                defaultWeight = 0f
            });
            var wait = sm.AddState("Wait");
            var loop = sm.AddState("AFK Loop");
            loop.motion = afkClip;
            sm.defaultState = wait;

            var enter = wait.AddTransition(loop);
            enter.hasExitTime = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, AfkMotionSourceGraph.AfkParameterName);

            var exit = loop.AddTransition(wait);
            exit.hasExitTime = false;
            exit.AddCondition(AnimatorConditionMode.IfNot, 0f, AfkMotionSourceGraph.AfkParameterName);
            return controller;
        }

        private static void AssignActionController(VRCAvatarDescriptor desc, RuntimeAnimatorController controller)
        {
            if (!desc.GetComponent<Animator>()) desc.gameObject.AddComponent<Animator>();
            desc.customizeAnimationLayers = true;
            desc.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Base, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Additive, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Gesture, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Action, isDefault = false, animatorController = controller },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = true }
            };
            desc.specialAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Sitting, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.TPose, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.IKPose, isDefault = true }
            };
        }
    }
}
#endif
