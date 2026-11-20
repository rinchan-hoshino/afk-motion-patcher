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

        [Test]
        public void RootQContinuity_RepairsOppositeHemisphereFlip_AndPreservesClipSettings()
        {
            var targetAvatar = Track(new GameObject("Target"));
            var patch = targetAvatar.AddComponent<AfkMotionPatch>();

            // Case 1: Clip with a corrupted opposite-hemisphere keyframe at t=0.5 (-q instead of +q)
            var flipClip = Track(new AnimationClip { name = "CorruptedHemisphereClip", frameRate = 30f });
            var q0 = Quaternion.Euler(10f, 0f, 0f);
            var qMid = Quaternion.Euler(20f, 5f, 0f);
            var qMidFlipped = new Quaternion(-qMid.x, -qMid.y, -qMid.z, -qMid.w);
            var q1 = Quaternion.Euler(30f, 10f, 0f);

            SetRootQCurves(flipClip, new[] { 0f, 0.5f, 1.0f }, new[] { q0, qMidFlipped, q1 });
            var srcSettings = AnimationUtility.GetAnimationClipSettings(flipClip);
            srcSettings.keepOriginalOrientation = true;
            srcSettings.keepOriginalPositionY = true;
            srcSettings.keepOriginalPositionXZ = true;
            srcSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(flipClip, srcSettings);

            var previewClip = Track(AfkMotionPatchEditorUtil.CreatePreviewClip(
                null, flipClip, targetAvatar, patch, "RootQRepair", out _, out _));

            var rx = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.x"));
            var ry = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.y"));
            var rz = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.z"));
            var rw = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.w"));
            Assert.That(rx, Is.Not.Null);

            Quaternion? prev = null;
            for (var i = 0; i <= 20; i++)
            {
                var t = i / 20f;
                var q = new Quaternion(rx.Evaluate(t), ry.Evaluate(t), rz.Evaluate(t), rw.Evaluate(t));
                var norm = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                Assert.That(norm, Is.EqualTo(1f).Within(0.05f), $"RootQ norm degraded at t={t:F2}");
                if (prev.HasValue)
                    Assert.That(Quaternion.Dot(prev.Value, q), Is.GreaterThanOrEqualTo(0f), $"Hemisphere discontinuity at t={t:F2}");
                var up = Quaternion.Normalize(q) * Vector3.up;
                Assert.That(up.y, Is.GreaterThan(0.5f), $"Avatar flipped upside-down at t={t:F2}");
                prev = q;
            }

            var dstSettings = AnimationUtility.GetAnimationClipSettings(previewClip);
            Assert.That(dstSettings.keepOriginalOrientation, Is.True);
            Assert.That(dstSettings.keepOriginalPositionY, Is.True);
            Assert.That(dstSettings.keepOriginalPositionXZ, Is.True);
            Assert.That(dstSettings.loopTime, Is.True);

            // Case 2: Single-hemisphere negative-w static clip (e.g. Stand w=-1) is normalized to w >= 0
            var standClip = Track(new AnimationClip { name = "NegativeWStand", frameRate = 30f });
            SetRootQCurves(standClip, new[] { 0f }, new[] { new Quaternion(0f, 0f, 0f, -1f) });
            var standPreview = Track(AfkMotionPatchEditorUtil.CreatePreviewClip(
                null, standClip, targetAvatar, patch, "StandHemisphere", out _, out _));
            var sw = AnimationUtility.GetEditorCurve(standPreview, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.w"));
            Assert.That(sw.Evaluate(0f), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void RootQContinuity_PreservesGenuineContinuous360Rotation()
        {
            var targetAvatar = Track(new GameObject("Target"));
            var patch = targetAvatar.AddComponent<AfkMotionPatch>();

            // Genuine smooth 360-degree pitch flip: w smoothly crosses from -1 through 0 to +1
            var flip360 = Track(new AnimationClip { name = "Backflip360", frameRate = 30f });
            var times = new float[9];
            var quats = new Quaternion[9];
            for (var i = 0; i < 9; i++)
            {
                var u = i / 8f;
                times[i] = u * 2.0f;
                var halfRad = u * Mathf.PI; // 0 .. pi -> 0 .. 360 deg
                quats[i] = new Quaternion(Mathf.Sin(halfRad), 0f, 0f, -Mathf.Cos(halfRad));
            }
            SetRootQCurves(flip360, times, quats);

            var previewClip = Track(AfkMotionPatchEditorUtil.CreatePreviewClip(
                null, flip360, targetAvatar, patch, "Flip360", out _, out _));
            var rx = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.x"));
            var ry = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.y"));
            var rz = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.z"));
            var rw = AnimationUtility.GetEditorCurve(previewClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.w"));

            // At t=1.0 (halfway through 360 backflip), up.y should still be inverted (~-1) and continuous
            var qMid = new Quaternion(rx.Evaluate(1.0f), ry.Evaluate(1.0f), rz.Evaluate(1.0f), rw.Evaluate(1.0f));
            var upMid = Quaternion.Normalize(qMid) * Vector3.up;
            Assert.That(upMid.y, Is.LessThan(-0.9f));

            Quaternion? prev = null;
            for (var i = 0; i <= 20; i++)
            {
                var t = i * 0.1f;
                var q = new Quaternion(rx.Evaluate(t), ry.Evaluate(t), rz.Evaluate(t), rw.Evaluate(t));
                if (prev.HasValue)
                    Assert.That(Quaternion.Dot(prev.Value, q), Is.GreaterThan(0.7f));
                prev = q;
            }
        }

        [Test]
        public void HumanoidPoseAdjustment_ContactWeightZeroWhenStanding_AndTwoBoneIKAccurate()
        {
            // Standing distance => weight 0 (zero pose jump at Intro start / Outro end)
            Assert.That(AfkMotionHumanoidBaker.ComputeContactWeight(0.35f, 0.20f, 0.30f), Is.EqualTo(0f));
            Assert.That(AfkMotionHumanoidBaker.ComputeContactWeight(0.30f, 0.20f, 0.30f), Is.EqualTo(0f));
            // Cheek contact distance => weight 1
            Assert.That(AfkMotionHumanoidBaker.ComputeContactWeight(0.20f, 0.20f, 0.30f), Is.EqualTo(1f));
            Assert.That(AfkMotionHumanoidBaker.ComputeContactWeight(0.14f, 0.20f, 0.30f), Is.EqualTo(1f));
            // Midpoint => smoothstep 0.5
            Assert.That(AfkMotionHumanoidBaker.ComputeContactWeight(0.25f, 0.20f, 0.30f), Is.EqualTo(0.5f).Within(0.001f));

            // Analytic 2-bone IK accuracy test
            var root = Track(new GameObject("ArmRoot"));
            var upper = new GameObject("UpperArm").transform;
            upper.SetParent(root.transform, false);
            upper.localPosition = Vector3.zero;
            var lower = new GameObject("LowerArm").transform;
            lower.SetParent(upper, false);
            lower.localPosition = new Vector3(0.20f, 0f, 0f);
            var hand = new GameObject("Hand").transform;
            hand.SetParent(lower, false);
            hand.localPosition = new Vector3(0.18f, 0f, 0f);
            // Slight initial bend
            lower.localRotation = Quaternion.Euler(0f, 45f, 0f);

            var origHandRot = Quaternion.Euler(15f, -25f, 10f);
            hand.rotation = origHandRot;
            var targetPos = hand.position + new Vector3(0.03f, -0.04f, 0.02f);

            AfkMotionHumanoidBaker.SolveTwoBoneArmIK(upper, lower, hand, targetPos, origHandRot, 0.01f, Vector3.right);

            Assert.That(Vector3.Distance(hand.position, targetPos), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(hand.rotation, origHandRot), Is.LessThan(0.1f));
            Assert.That(Vector3.Distance(upper.position, lower.position), Is.EqualTo(0.20f).Within(0.0005f));
            Assert.That(Vector3.Distance(lower.position, hand.position), Is.EqualTo(0.18f).Within(0.0005f));
        }

        [Test]
        public void ExitBlendShapeReset_ClearsAnimatedBlendShapesOnRestoreTrackingClip()
        {
            var sourceAvatar = Track(new GameObject("SourceAvatar"));
            var sourceDesc = sourceAvatar.AddComponent<VRCAvatarDescriptor>();

            var loopClip = Track(new AnimationClip { name = "AfkLoopExpr" });
            AnimationUtility.SetEditorCurve(loopClip,
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_nagomi"),
                AnimationCurve.Constant(0f, 1f, 100f));

            var standClip = Track(new AnimationClip { name = "StandNoExpr" });
            AnimationUtility.SetEditorCurve(standClip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.w"),
                AnimationCurve.Constant(0f, 0f, -1f));

            var sourceController = Track(new AnimatorController { name = "SourceActionWithExit" });
            sourceController.AddParameter("AFK", AnimatorControllerParameterType.Bool);
            var sm = new AnimatorStateMachine { name = "Action" };
            sourceController.AddLayer(new AnimatorControllerLayer { name = "Action", stateMachine = sm, defaultWeight = 0f });
            var wait = sm.AddState("Wait");
            var loop = sm.AddState("AFK");
            var restore = sm.AddState("Restore Tracking AFK");
            loop.motion = loopClip;
            restore.motion = standClip;
            sm.defaultState = wait;

            var enter = wait.AddTransition(loop);
            enter.hasExitTime = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "AFK");
            var exit = loop.AddTransition(restore);
            exit.hasExitTime = false;
            exit.AddCondition(AnimatorConditionMode.IfNot, 0f, "AFK");
            restore.AddExitTransition().hasExitTime = true;

            AssignActionController(sourceDesc, sourceController);

            var targetAvatar = Track(new GameObject("TargetAvatar"));
            var targetDesc = targetAvatar.AddComponent<VRCAvatarDescriptor>();
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(targetAvatar.transform, false);
            var smr = bodyGo.AddComponent<SkinnedMeshRenderer>();
            var mesh = Track(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("eye_nagomi_2", 100f, new Vector3[3], null, null);
            smr.sharedMesh = mesh;
            smr.SetBlendShapeWeight(0, 0f);

            var targetCtrl = Track(new AnimatorController { name = "TargetAction" });
            targetCtrl.AddLayer(new AnimatorControllerLayer { name = "Action", stateMachine = new AnimatorStateMachine { name = "Action" }, defaultWeight = 1f });
            AssignActionController(targetDesc, targetCtrl);

            var patch = targetAvatar.AddComponent<AfkMotionPatch>();
            patch.sourceAvatar = sourceAvatar;
            patch.blendShapeNameRemaps.Add(new AfkMotionPatch.BlendShapeNameRemap
            {
                fromName = "eye_nagomi",
                toName = "eye_nagomi_2",
                scale = 1f,
                clamp = true,
                clampMin = 0f,
                clampMax = 100f
            });

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

            var resultCtrl = targetDesc.baseAnimationLayers
                .First(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action)
                .animatorController as AnimatorController;
            var afkLayer = resultCtrl.layers.Last();
            var restoreState = afkLayer.stateMachine.states
                .Select(s => s.state)
                .FirstOrDefault(s => s.name == "Restore Tracking AFK");
            Assert.That(restoreState, Is.Not.Null);
            var restoreClip = restoreState.motion as AnimationClip;
            Assert.That(restoreClip, Is.Not.Null);

            var resetCurve = AnimationUtility.GetEditorCurve(restoreClip,
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_nagomi_2"));
            Assert.That(resetCurve, Is.Not.Null, "Restore Tracking AFK must explicitly reset AFK-animated BlendShapes.");
            Assert.That(resetCurve.Evaluate(0f), Is.EqualTo(0f).Within(0.001f));
        }

        private static void SetRootQCurves(AnimationClip clip, float[] times, Quaternion[] quats)
        {
            var cx = new AnimationCurve();
            var cy = new AnimationCurve();
            var cz = new AnimationCurve();
            var cw = new AnimationCurve();
            for (var i = 0; i < times.Length; i++)
            {
                cx.AddKey(times[i], quats[i].x);
                cy.AddKey(times[i], quats[i].y);
                cz.AddKey(times[i], quats[i].z);
                cw.AddKey(times[i], quats[i].w);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.x"), cx);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.y"), cy);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.z"), cz);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.w"), cw);
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
