#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace THE_cattail.AfkMotionPatcher
{
    internal static class AfkMotionHumanoidBaker
    {
        private static readonly EditorCurveBinding RootQxBinding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.x");
        private static readonly EditorCurveBinding RootQyBinding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.y");
        private static readonly EditorCurveBinding RootQzBinding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.z");
        private static readonly EditorCurveBinding RootQwBinding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.w");
        private static readonly EditorCurveBinding RootTxBinding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.x");
        private static readonly EditorCurveBinding RootTyBinding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.y");
        private static readonly EditorCurveBinding RootTzBinding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.z");

        private static readonly string[] ArmMuscleNames =
        {
            "Left Arm Down-Up",
            "Left Arm Front-Back",
            "Left Arm Twist In-Out",
            "Left Forearm Stretch",
            "Left Forearm Twist In-Out",
            "Left Hand Down-Up",
            "Left Hand In-Out",
            "Right Arm Down-Up",
            "Right Arm Front-Back",
            "Right Arm Twist In-Out",
            "Right Forearm Stretch",
            "Right Forearm Twist In-Out",
            "Right Hand Down-Up",
            "Right Hand In-Out",
        };

        private static readonly string[] LegCloseMuscleNames =
        {
            "Left Upper Leg In-Out",
            "Right Upper Leg In-Out",
        };

        private static readonly string[] KneeBendMuscleNames =
        {
            "Left Lower Leg Stretch",
            "Right Lower Leg Stretch",
        };

        internal static float ComputeContactWeight(float distance, float fullDistance, float fadeDistance)
        {
            var full = Mathf.Max(0.01f, fullDistance);
            var fade = Mathf.Max(full, fadeDistance);
            if (fade <= full + 1e-5f) return distance <= full ? 1f : 0f;
            var u = Mathf.Clamp01((fade - distance) / (fade - full));
            return u * u * (3f - 2f * u);
        }

        internal static bool EnsureRootQuaternionContinuity(
            Func<EditorCurveBinding, AnimationCurve> getCurve,
            Action<EditorCurveBinding, AnimationCurve> setCurve,
            float frameRate)
        {
            var cx = getCurve(RootQxBinding);
            var cy = getCurve(RootQyBinding);
            var cz = getCurve(RootQzBinding);
            var cw = getCurve(RootQwBinding);
            if (cx == null || cy == null || cz == null || cw == null) return false;

            var keyTimes = CollectSortedTimes(cx, cy, cz, cw);
            if (keyTimes.Count == 0) return false;

            var sampleRate = Mathf.Clamp(frameRate > 1f ? frameRate : 30f, 15f, 60f);
            var step = 1f / sampleRate;

            var rawKeyQuats = new Quaternion[keyTimes.Count];
            var fixedKeyQuats = new Quaternion[keyTimes.Count];
            var keyNegated = new bool[keyTimes.Count];
            var hadAnyIssue = false;

            for (var i = 0; i < keyTimes.Count; i++)
            {
                var t = keyTimes[i];
                var raw = new Quaternion(cx.Evaluate(t), cy.Evaluate(t), cz.Evaluate(t), cw.Evaluate(t));
                var sqrMag = raw.x * raw.x + raw.y * raw.y + raw.z * raw.z + raw.w * raw.w;
                if (Mathf.Abs(sqrMag - 1f) > 0.02f) hadAnyIssue = true;
                var q = sqrMag > 1e-8f ? NormalizeQuaternion(raw) : Quaternion.identity;
                rawKeyQuats[i] = q;
                if (i > 0 && Quaternion.Dot(fixedKeyQuats[i - 1], q) < 0f)
                {
                    q = NegateQuaternion(q);
                    keyNegated[i] = true;
                    hadAnyIssue = true;
                }
                fixedKeyQuats[i] = q;
            }

            var sampleTimes = new List<float>(keyTimes.Count * 2);
            var quatsList = new List<Quaternion>(keyTimes.Count * 2);

            for (var i = 0; i < keyTimes.Count; i++)
            {
                sampleTimes.Add(keyTimes[i]);
                quatsList.Add(fixedKeyQuats[i]);

                if (i + 1 >= keyTimes.Count) break;

                var t0 = keyTimes[i];
                var t1 = keyTimes[i + 1];
                var dt = t1 - t0;
                if (dt <= step * 1.5f) continue;

                var rawFlipped = Quaternion.Dot(rawKeyQuats[i], rawKeyQuats[i + 1]) < 0f;
                if (rawFlipped)
                {
                    // Original curve crossed through zero between opposite hemispheres;
                    // rebuild interior via Slerp if the repaired rotation arc is large.
                    if (Quaternion.Dot(fixedKeyQuats[i], fixedKeyQuats[i + 1]) < 0.992f)
                    {
                        var steps = Mathf.Clamp(Mathf.CeilToInt(dt / step), 2, 64);
                        for (var s = 1; s < steps; s++)
                        {
                            var u = (float)s / steps;
                            sampleTimes.Add(Mathf.Lerp(t0, t1, u));
                            quatsList.Add(Quaternion.Slerp(fixedKeyQuats[i], fixedKeyQuats[i + 1], u));
                        }
                    }
                }
                else
                {
                    var midT = 0.5f * (t0 + t1);
                    var qm = new Quaternion(cx.Evaluate(midT), cy.Evaluate(midT), cz.Evaluate(midT), cw.Evaluate(midT));
                    var midNorm = Mathf.Sqrt(qm.x * qm.x + qm.y * qm.y + qm.z * qm.z + qm.w * qm.w);
                    if (Quaternion.Dot(fixedKeyQuats[i], fixedKeyQuats[i + 1]) < 0.992f || Mathf.Abs(midNorm - 1f) > 0.02f)
                    {
                        var steps = Mathf.Clamp(Mathf.CeilToInt(dt / step), 2, 64);
                        for (var s = 1; s < steps; s++)
                        {
                            var st = Mathf.Lerp(t0, t1, (float)s / steps);
                            var raw = new Quaternion(cx.Evaluate(st), cy.Evaluate(st), cz.Evaluate(st), cw.Evaluate(st));
                            var sqrMag = raw.x * raw.x + raw.y * raw.y + raw.z * raw.z + raw.w * raw.w;
                            if (Mathf.Abs(sqrMag - 1f) > 0.02f) hadAnyIssue = true;
                            var q = sqrMag > 1e-8f ? NormalizeQuaternion(raw) : Quaternion.identity;
                            if (keyNegated[i]) q = NegateQuaternion(q);
                            if (Quaternion.Dot(quatsList[quatsList.Count - 1], q) < 0f)
                            {
                                q = NegateQuaternion(q);
                                hadAnyIssue = true;
                            }
                            sampleTimes.Add(st);
                            quatsList.Add(q);
                        }
                    }
                }
            }

            var allNegativeW = true;
            for (var i = 0; i < quatsList.Count; i++)
            {
                if (i > 0 && Quaternion.Dot(quatsList[i - 1], quatsList[i]) < 0f)
                {
                    quatsList[i] = NegateQuaternion(quatsList[i]);
                    hadAnyIssue = true;
                }
                if (quatsList[i].w > 0f) allNegativeW = false;
            }

            if (allNegativeW)
            {
                hadAnyIssue = true;
                for (var i = 0; i < quatsList.Count; i++)
                    quatsList[i] = NegateQuaternion(quatsList[i]);
            }

            if (!hadAnyIssue && sampleTimes.Count == keyTimes.Count) return false;

            var nqx = new AnimationCurve();
            var nqy = new AnimationCurve();
            var nqz = new AnimationCurve();
            var nqw = new AnimationCurve();
            for (var i = 0; i < sampleTimes.Count; i++)
            {
                var t = sampleTimes[i];
                var q = quatsList[i];
                nqx.AddKey(t, q.x);
                nqy.AddKey(t, q.y);
                nqz.AddKey(t, q.z);
                nqw.AddKey(t, q.w);
            }
            ApplyClampedAutoTangents(nqx);
            ApplyClampedAutoTangents(nqy);
            ApplyClampedAutoTangents(nqz);
            ApplyClampedAutoTangents(nqw);

            setCurve(RootQxBinding, nqx);
            setCurve(RootQyBinding, nqy);
            setCurve(RootQzBinding, nqz);
            setCurve(RootQwBinding, nqw);
            return true;
        }

        internal static bool ApplyHumanoidPoseAdjustment(
            GameObject avatarRoot,
            AfkMotionPatch patch,
            float frameRate,
            Func<EditorCurveBinding, AnimationCurve> getCurve,
            Action<EditorCurveBinding, AnimationCurve> setCurve)
        {
            if (!avatarRoot || !patch || patch.humanoidPoseAdjustment == null) return false;
            var adj = patch.humanoidPoseAdjustment;
            if (!adj.enabled) return false;

            var hasHandOffset = adj.handOffsetInHeadSpace.sqrMagnitude > 1e-8f || Mathf.Abs(adj.elbowOutwardBias) > 1e-5f;
            var hasLegClose = Mathf.Abs(adj.legCloseAdjustment) > 1e-5f;
            var hasKneeBend = Mathf.Abs(adj.kneeBendAdjustment) > 1e-5f;
            if (!hasHandOffset && !hasLegClose && !hasKneeBend) return false;

            var sourceAnimator = avatarRoot.GetComponent<Animator>();
            if (!sourceAnimator || !sourceAnimator.isHuman || !sourceAnimator.avatar || !sourceAnimator.avatar.isValid)
                return false;

            // Load existing muscle & root curves from the clip
            var muscleNames = HumanTrait.MuscleName;
            var muscleCount = HumanTrait.MuscleCount;
            var muscleBindings = new EditorCurveBinding[muscleCount];
            var muscleCurves = new AnimationCurve[muscleCount];
            var hasAnyHumanoidMuscle = false;
            var maxTime = 0f;

            for (var i = 0; i < muscleCount; i++)
            {
                muscleBindings[i] = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscleNames[i]);
                var c = getCurve(muscleBindings[i]);
                muscleCurves[i] = c;
                if (c != null && c.length > 0)
                {
                    hasAnyHumanoidMuscle = true;
                    var endT = c.keys[c.length - 1].time;
                    if (endT > maxTime) maxTime = endT;
                }
            }
            if (!hasAnyHumanoidMuscle) return false;

            var rootTx = getCurve(RootTxBinding);
            var rootTy = getCurve(RootTyBinding);
            var rootTz = getCurve(RootTzBinding);
            var rootQx = getCurve(RootQxBinding);
            var rootQy = getCurve(RootQyBinding);
            var rootQz = getCurve(RootQzBinding);
            var rootQw = getCurve(RootQwBinding);
            foreach (var rc in new[] { rootTx, rootTy, rootTz, rootQx, rootQy, rootQz, rootQw })
            {
                if (rc != null && rc.length > 0)
                {
                    var endT = rc.keys[rc.length - 1].time;
                    if (endT > maxTime) maxTime = endT;
                }
            }

            var armMuscleIndices = ResolveMuscleIndices(ArmMuscleNames, muscleNames);
            var legCloseIndices = ResolveMuscleIndices(LegCloseMuscleNames, muscleNames);
            var kneeBendIndices = ResolveMuscleIndices(KneeBendMuscleNames, muscleNames);

            var sampleTimes = BuildAdaptiveSampleTimes(maxTime, frameRate, rootQx, rootQw, armMuscleIndices, muscleCurves);
            if (sampleTimes.Count == 0) return false;

            var skeletonRoot = CreateSkeletonClone(avatarRoot, sourceAnimator);
            HumanPoseHandler poseHandler = null;
            try
            {
                var skeletonAnimator = skeletonRoot.GetComponent<Animator>();
                poseHandler = new HumanPoseHandler(sourceAnimator.avatar, skeletonRoot.transform);

                var defaultPose = new HumanPose();
                poseHandler.GetHumanPose(ref defaultPose);
                var defaultMuscles = defaultPose.muscles != null ? (float[])defaultPose.muscles.Clone() : new float[muscleCount];

                var head = skeletonAnimator.GetBoneTransform(HumanBodyBones.Head);
                var chest = skeletonAnimator.GetBoneTransform(HumanBodyBones.Chest)
                    ?? skeletonAnimator.GetBoneTransform(HumanBodyBones.Spine);
                var lUpper = skeletonAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lLower = skeletonAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var lHand = skeletonAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                var lMid = skeletonAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                var rUpper = skeletonAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                var rLower = skeletonAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                var rHand = skeletonAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                var rMid = skeletonAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);

                if (!head || !lUpper || !lLower || !lHand || !rUpper || !rLower || !rHand)
                    return false;

                var modifiedIndices = new List<int>();
                if (hasHandOffset) modifiedIndices.AddRange(armMuscleIndices);
                if (hasLegClose) modifiedIndices.AddRange(legCloseIndices);
                if (hasKneeBend) modifiedIndices.AddRange(kneeBendIndices);
                modifiedIndices = modifiedIndices.Distinct().ToList();

                var bakedValues = new Dictionary<int, float[]>(modifiedIndices.Count);
                foreach (var idx in modifiedIndices)
                    bakedValues[idx] = new float[sampleTimes.Count];

                var workPose = new HumanPose
                {
                    bodyPosition = defaultPose.bodyPosition,
                    bodyRotation = defaultPose.bodyRotation,
                    muscles = new float[muscleCount]
                };
                var updatedPose = new HumanPose();

                var maxWeightSeen = 0f;
                for (var si = 0; si < sampleTimes.Count; si++)
                {
                    var t = sampleTimes[si];
                    workPose.bodyPosition = new Vector3(
                        rootTx != null ? rootTx.Evaluate(t) : defaultPose.bodyPosition.x,
                        rootTy != null ? rootTy.Evaluate(t) : defaultPose.bodyPosition.y,
                        rootTz != null ? rootTz.Evaluate(t) : defaultPose.bodyPosition.z);
                    if (rootQx != null && rootQy != null && rootQz != null && rootQw != null)
                    {
                        var rq = new Quaternion(rootQx.Evaluate(t), rootQy.Evaluate(t), rootQz.Evaluate(t), rootQw.Evaluate(t));
                        workPose.bodyRotation = rq.x * rq.x + rq.y * rq.y + rq.z * rq.z + rq.w * rq.w > 1e-8f
                            ? NormalizeQuaternion(rq)
                            : Quaternion.identity;
                    }
                    else
                    {
                        workPose.bodyRotation = defaultPose.bodyRotation;
                    }

                    for (var m = 0; m < muscleCount; m++)
                        workPose.muscles[m] = muscleCurves[m] != null ? muscleCurves[m].Evaluate(t) : defaultMuscles[m];

                    poseHandler.SetHumanPose(ref workPose);

                    var lAnchor = lMid ? Vector3.Lerp(lHand.position, lMid.position, 0.5f) : lHand.position;
                    var rAnchor = rMid ? Vector3.Lerp(rHand.position, rMid.position, 0.5f) : rHand.position;
                    var avgDist = 0.5f * (Vector3.Distance(lAnchor, head.position) + Vector3.Distance(rAnchor, head.position));
                    var w = ComputeContactWeight(avgDist, adj.contactFullDistance, adj.contactFadeDistance);
                    if (w > maxWeightSeen) maxWeightSeen = w;

                    if (w > 1e-4f && hasHandOffset)
                    {
                        var worldOffset = head.TransformDirection(adj.handOffsetInHeadSpace * w);
                        var chestRight = chest ? chest.right : skeletonRoot.transform.right;

                        var origLRot = lHand.rotation;
                        var origRRot = rHand.rotation;
                        SolveTwoBoneArmIK(lUpper, lLower, lHand, lHand.position + worldOffset, origLRot,
                            -adj.elbowOutwardBias * w, chestRight);
                        SolveTwoBoneArmIK(rUpper, rLower, rHand, rHand.position + worldOffset, origRRot,
                            adj.elbowOutwardBias * w, chestRight);

                        poseHandler.GetHumanPose(ref updatedPose);
                        foreach (var idx in armMuscleIndices)
                        {
                            var baseVal = workPose.muscles[idx];
                            var ikVal = updatedPose.muscles != null && idx < updatedPose.muscles.Length
                                ? updatedPose.muscles[idx]
                                : baseVal;
                            bakedValues[idx][si] = Mathf.Clamp(Mathf.Lerp(baseVal, ikVal, w), -1.5f, 1.5f);
                        }
                    }
                    else if (hasHandOffset)
                    {
                        foreach (var idx in armMuscleIndices)
                            bakedValues[idx][si] = workPose.muscles[idx];
                    }

                    if (hasLegClose)
                    {
                        foreach (var idx in legCloseIndices)
                            bakedValues[idx][si] = Mathf.Clamp(workPose.muscles[idx] + adj.legCloseAdjustment * w, -1f, 1f);
                    }
                    if (hasKneeBend)
                    {
                        foreach (var idx in kneeBendIndices)
                            bakedValues[idx][si] = Mathf.Clamp(workPose.muscles[idx] + adj.kneeBendAdjustment * w, -1f, 1f);
                    }
                }

                if (maxWeightSeen <= 1e-4f) return false;

                foreach (var idx in modifiedIndices)
                {
                    var values = bakedValues[idx];
                    var curve = new AnimationCurve();
                    for (var si = 0; si < sampleTimes.Count; si++)
                        curve.AddKey(sampleTimes[si], values[si]);
                    ApplyClampedAutoTangents(curve);
                    setCurve(muscleBindings[idx], curve);
                }

                return true;
            }
            finally
            {
                poseHandler?.Dispose();
                if (skeletonRoot) UnityEngine.Object.DestroyImmediate(skeletonRoot);
            }
        }

        internal static void SolveTwoBoneArmIK(
            Transform upperArm,
            Transform lowerArm,
            Transform hand,
            Vector3 targetHandPos,
            Quaternion targetHandRot,
            float elbowOutwardBias,
            Vector3 outwardAxis)
        {
            var a = upperArm.position;
            var b = lowerArm.position;
            var c = hand.position;
            var ab = b - a;
            var bc = c - b;
            var ac = c - a;
            var lenA = ab.magnitude;
            var lenB = bc.magnitude;
            if (lenA < 1e-4f || lenB < 1e-4f) return;

            var at = targetHandPos - a;
            var distT = at.magnitude;
            if (distT < 1e-4f) return;

            var minReach = Mathf.Abs(lenA - lenB) + 1e-4f;
            var maxReach = lenA + lenB - 1e-4f;
            var clampedDist = Mathf.Clamp(distT, minReach, maxReach);
            var dirAT = at / distT;

            var origAxis = ac.sqrMagnitude > 1e-6f ? ac.normalized : dirAT;
            var origPole = ab - Vector3.Project(ab, origAxis);
            if (Mathf.Abs(elbowOutwardBias) > 1e-5f)
                origPole += outwardAxis * elbowOutwardBias;

            var pole = origPole - Vector3.Project(origPole, dirAT);
            if (pole.sqrMagnitude < 1e-6f)
                pole = Vector3.Cross(dirAT, Vector3.up);
            if (pole.sqrMagnitude < 1e-6f)
                pole = Vector3.Cross(dirAT, Vector3.right);
            pole.Normalize();

            var cosAlpha = Mathf.Clamp(
                (lenA * lenA + clampedDist * clampedDist - lenB * lenB) / (2f * lenA * clampedDist),
                -1f, 1f);
            var sinAlpha = Mathf.Sqrt(Mathf.Max(0f, 1f - cosAlpha * cosAlpha));
            var targetB = a + dirAT * (lenA * cosAlpha) + pole * (lenA * sinAlpha);

            var currAB = lowerArm.position - upperArm.position;
            var newAB = targetB - upperArm.position;
            if (currAB.sqrMagnitude > 1e-8f && newAB.sqrMagnitude > 1e-8f)
                upperArm.rotation = Quaternion.FromToRotation(currAB, newAB) * upperArm.rotation;

            var currBC = hand.position - lowerArm.position;
            var newBC = targetHandPos - lowerArm.position;
            if (currBC.sqrMagnitude > 1e-8f && newBC.sqrMagnitude > 1e-8f)
                lowerArm.rotation = Quaternion.FromToRotation(currBC, newBC) * lowerArm.rotation;

            hand.rotation = targetHandRot;
        }

        private static GameObject CreateSkeletonClone(GameObject avatarRoot, Animator sourceAnimator)
        {
            var cloneRoot = new GameObject(avatarRoot.name + "_PoseBakeSkeleton")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            cloneRoot.transform.position = Vector3.zero;
            cloneRoot.transform.rotation = Quaternion.identity;
            cloneRoot.transform.localScale = avatarRoot.transform.localScale;

            CopyTransformChildren(avatarRoot.transform, cloneRoot.transform);

            var cloneAnimator = cloneRoot.AddComponent<Animator>();
            cloneAnimator.avatar = sourceAnimator.avatar;
            cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return cloneRoot;
        }

        private static void CopyTransformChildren(Transform source, Transform dest)
        {
            for (var i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i);
                var childClone = new GameObject(child.name) { hideFlags = HideFlags.HideAndDontSave };
                var t = childClone.transform;
                t.SetParent(dest, false);
                t.localPosition = child.localPosition;
                t.localRotation = child.localRotation;
                t.localScale = child.localScale;
                CopyTransformChildren(child, t);
            }
        }

        private static List<int> ResolveMuscleIndices(string[] targetNames, string[] allMuscleNames)
        {
            var result = new List<int>(targetNames.Length);
            foreach (var name in targetNames)
            {
                var idx = Array.IndexOf(allMuscleNames, name);
                if (idx >= 0) result.Add(idx);
            }
            return result;
        }

        private static List<float> CollectSortedTimes(params AnimationCurve[] curves)
        {
            var set = new SortedSet<float>();
            foreach (var c in curves)
            {
                if (c == null) continue;
                foreach (var k in c.keys) set.Add(k.time);
            }
            return set.ToList();
        }

        private static List<float> BuildAdaptiveSampleTimes(
            float maxTime,
            float frameRate,
            AnimationCurve rootQx,
            AnimationCurve rootQw,
            List<int> armIndices,
            AnimationCurve[] muscleCurves)
        {
            if (maxTime <= 1e-4f) return new List<float> { 0f };

            var set = new SortedSet<float> { 0f, maxTime };
            foreach (var idx in armIndices)
            {
                var c = muscleCurves[idx];
                if (c == null) continue;
                foreach (var k in c.keys) set.Add(Mathf.Clamp(k.time, 0f, maxTime));
            }
            if (rootQx != null) foreach (var k in rootQx.keys) set.Add(Mathf.Clamp(k.time, 0f, maxTime));
            if (rootQw != null) foreach (var k in rootQw.keys) set.Add(Mathf.Clamp(k.time, 0f, maxTime));

            // Ensure dense enough coverage: 30Hz for short clips (<= 6s), 10Hz max interval for long loops
            var maxStep = maxTime <= 6f ? (1f / Mathf.Clamp(frameRate > 1f ? frameRate : 30f, 15f, 30f)) : 0.12f;
            var existing = set.ToList();
            for (var i = 0; i < existing.Count - 1; i++)
            {
                var t0 = existing[i];
                var t1 = existing[i + 1];
                var dt = t1 - t0;
                if (dt > maxStep * 1.25f)
                {
                    var steps = Mathf.CeilToInt(dt / maxStep);
                    for (var s = 1; s < steps; s++)
                        set.Add(Mathf.Lerp(t0, t1, (float)s / steps));
                }
            }

            // Merge timestamps closer than 2ms to avoid degenerate tangents
            var filtered = new List<float>(set.Count);
            foreach (var t in set)
            {
                if (filtered.Count == 0 || t - filtered[filtered.Count - 1] >= 0.002f || Mathf.Approximately(t, maxTime))
                {
                    if (filtered.Count > 0 && Mathf.Approximately(t, maxTime) && t - filtered[filtered.Count - 1] < 0.002f)
                        filtered[filtered.Count - 1] = maxTime;
                    else
                        filtered.Add(t);
                }
            }
            return filtered;
        }

        private static Quaternion NormalizeSafe(Quaternion q)
        {
            var sqrMag = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return sqrMag > 1e-8f ? NormalizeQuaternion(q) : Quaternion.identity;
        }

        private static Quaternion NormalizeQuaternion(Quaternion q)
        {
            var inv = 1f / Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        private static Quaternion NegateQuaternion(Quaternion q)
        {
            return new Quaternion(-q.x, -q.y, -q.z, -q.w);
        }

        private static void ApplyClampedAutoTangents(AnimationCurve curve)
        {
            for (var i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
        }
    }
}
#endif
