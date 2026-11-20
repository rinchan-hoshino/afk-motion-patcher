#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace THE_cattail.AfkMotionPatcher
{
    public static class AfkMotionAutoMatcher
    {
        public struct AutoMatchSummary
        {
            public int AddedHierarchyMappings;
            public int AddedBlendShapeMappings;
        }

        public struct RestPoseDeltaSummary
        {
            public int AddedOffsets;
            public int UpdatedOffsets;
        }

        private static readonly Regex TrailingDigitsRegex = new Regex(@"(?:[._\-\s]*0*)(\d+)$", RegexOptions.Compiled);
        private static readonly Regex SplitTokenRegex = new Regex(@"[._\-\s]+|(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])", RegexOptions.Compiled);

        private static readonly (string[] GroupA, string[] GroupB)[] BlendShapeSynonyms =
        {
            (new[] { "eyebrow", "brow" }, new[] { "mayu" }),
            (new[] { "nagomi", "gentle", "calm", "soft" }, new[] { "なごみ" }),
            (new[] { "smile", "happy", "joy", "laugh", "niko" }, new[] { "笑い", "にこ", "笑顔" }),
            (new[] { "angry", "mad", "tsurime" }, new[] { "怒り", "つり目" }),
            (new[] { "surprise", "surprised", "shock", "odoroki" }, new[] { "驚き", "びっくり" }),
            (new[] { "blink", "close", "closed" }, new[] { "まばたき", "閉じ" }),
            (new[] { "wink" }, new[] { "ウィンク", "ウインク" }),
            (new[] { "sad", "sorrow", "tareme" }, new[] { "悲しみ", "たれ目" }),
        };

        public static AutoMatchSummary AutoMatchHierarchyAndBlendShapes(
            AfkMotionPatch patch, GameObject targetAvatar, IEnumerable<EditorCurveBinding> sourceBindings = null)
        {
            var summary = new AutoMatchSummary();
            if (!patch || !patch.sourceAvatar || !targetAvatar) return summary;

            var bindings = sourceBindings?.ToArray() ?? CollectSourceBindings(patch);
            if (bindings.Length == 0) return summary;

            summary.AddedHierarchyMappings = AutoMatchHierarchy(patch, patch.sourceAvatar, targetAvatar, bindings);
            summary.AddedBlendShapeMappings = AutoMatchBlendShapes(patch, targetAvatar, bindings);
            return summary;
        }

        public static int AutoMatchHierarchy(
            AfkMotionPatch patch, GameObject sourceAvatar, GameObject targetAvatar, IEnumerable<EditorCurveBinding> sourceBindings)
        {
            if (!patch || !sourceAvatar || !targetAvatar || sourceBindings == null) return 0;

            var existingFromPaths = new HashSet<string>(
                patch.rendererPathRemaps.Where(r => r != null && !string.IsNullOrEmpty(r.fromPath)).Select(r => r.fromPath),
                StringComparer.Ordinal);
            var usedTargetPaths = new HashSet<string>(
                patch.rendererPathRemaps.Where(r => r != null && !string.IsNullOrEmpty(r.toPath)).Select(r => r.toPath),
                StringComparer.Ordinal);

            var targetTransforms = CollectTransformPaths(targetAvatar.transform);
            var targetSmrPaths = new HashSet<string>(
                targetAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Select(r => AnimationUtility.CalculateTransformPath(r.transform, targetAvatar.transform)),
                StringComparer.Ordinal);

            var sourcePathTypes = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var b in sourceBindings)
            {
                if (string.IsNullOrEmpty(b.path)) continue;
                var isSmr = b.type == typeof(SkinnedMeshRenderer);
                if (!sourcePathTypes.TryGetValue(b.path, out var prev))
                    sourcePathTypes[b.path] = isSmr;
                else
                    sourcePathTypes[b.path] = prev || isSmr;
            }

            var added = 0;
            foreach (var pair in sourcePathTypes.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var sourcePath = pair.Key;
                var isSmr = pair.Value;
                if (existingFromPaths.Contains(sourcePath)) continue;
                if (targetAvatar.transform.Find(sourcePath) != null) continue;

                var bestTarget = FindBestTargetPath(sourcePath, isSmr, sourceAvatar, targetAvatar, targetTransforms, targetSmrPaths, usedTargetPaths);
                if (string.IsNullOrEmpty(bestTarget)) continue;

                patch.rendererPathRemaps.Add(new AfkMotionPatch.RendererPathRemap
                {
                    fromPath = sourcePath,
                    toPath = bestTarget
                });
                existingFromPaths.Add(sourcePath);
                usedTargetPaths.Add(bestTarget);
                added++;
            }

            return added;
        }

        public static int AutoMatchBlendShapes(
            AfkMotionPatch patch, GameObject targetAvatar, IEnumerable<EditorCurveBinding> sourceBindings)
        {
            if (!patch || !targetAvatar || sourceBindings == null) return 0;

            var targetShapes = targetAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r && r.sharedMesh)
                .SelectMany(r => Enumerable.Range(0, r.sharedMesh.blendShapeCount).Select(r.sharedMesh.GetBlendShapeName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (targetShapes.Length == 0) return 0;

            var targetShapeSet = new HashSet<string>(targetShapes, StringComparer.Ordinal);
            var existingFrom = new HashSet<string>(
                patch.blendShapeNameRemaps.Where(r => r != null && !string.IsNullOrEmpty(r.fromName)).Select(r => r.fromName),
                StringComparer.Ordinal);

            var sourceShapes = sourceBindings
                .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                .Select(b => b.propertyName.Substring("blendShape.".Length))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal);

            var added = 0;
            foreach (var sourceShape in sourceShapes)
            {
                if (existingFrom.Contains(sourceShape)) continue;
                if (targetShapeSet.Contains(sourceShape)) continue;

                var bestTarget = FindBestBlendShapeMatch(sourceShape, targetShapes);
                if (string.IsNullOrEmpty(bestTarget)) continue;

                patch.blendShapeNameRemaps.Add(new AfkMotionPatch.BlendShapeNameRemap
                {
                    fromName = sourceShape,
                    toName = bestTarget,
                    scale = 1f,
                    offset = 0f,
                    clamp = true,
                    clampMin = 0f,
                    clampMax = 100f
                });
                existingFrom.Add(sourceShape);
                added++;
            }

            return added;
        }

        public static RestPoseDeltaSummary MatchRestPoseDeltas(
            AfkMotionPatch patch, GameObject targetAvatar, IEnumerable<EditorCurveBinding> sourceBindings = null)
        {
            var summary = new RestPoseDeltaSummary();
            if (!patch || !patch.sourceAvatar || !targetAvatar) return summary;

            var bindings = sourceBindings?.ToArray() ?? CollectSourceBindings(patch);
            var candidatePairs = new List<(string SourcePath, string TargetPath)>();
            var seenTargets = new HashSet<string>(StringComparer.Ordinal);

            // 1. Mapped hierarchy bones first
            foreach (var remap in patch.rendererPathRemaps)
            {
                if (remap == null || string.IsNullOrEmpty(remap.fromPath) || string.IsNullOrEmpty(remap.toPath)) continue;
                if (seenTargets.Add(remap.toPath))
                    candidatePairs.Add((remap.fromPath, remap.toPath));
            }

            // 2. Animated transform bones with identical paths
            foreach (var b in bindings.Where(b => b.type == typeof(Transform) && !string.IsNullOrEmpty(b.path)))
            {
                if (seenTargets.Add(b.path))
                    candidatePairs.Add((b.path, b.path));
            }

            foreach (var pair in candidatePairs)
            {
                if (!TryComputeRestPoseDelta(patch.sourceAvatar, targetAvatar, pair.SourcePath, pair.TargetPath, out var delta))
                    continue;
                if (delta.sqrMagnitude < 0.25f) continue; // Ignore sub-0.5 degree noise

                delta = ClampEulerForSlider(delta);
                var existing = patch.boneRotationOffsets.FirstOrDefault(o => o != null && o.bonePath == pair.TargetPath);
                if (existing != null)
                {
                    existing.eulerOffset = delta;
                    summary.UpdatedOffsets++;
                }
                else
                {
                    patch.boneRotationOffsets.Add(new AfkMotionPatch.BoneRotationOffset
                    {
                        bonePath = pair.TargetPath,
                        eulerOffset = delta
                    });
                    summary.AddedOffsets++;
                }
            }

            return summary;
        }

        public static bool TryComputeRestPoseDelta(
            GameObject sourceAvatar, GameObject targetAvatar, string sourcePath, string targetPath, out Vector3 eulerDelta)
        {
            eulerDelta = Vector3.zero;
            if (!sourceAvatar || !targetAvatar || string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
                return false;

            var src = sourceAvatar.transform.Find(sourcePath);
            var dst = targetAvatar.transform.Find(targetPath);
            if (!src || !dst) return false;

            var deltaQuat = Quaternion.Inverse(src.localRotation) * dst.localRotation;
            eulerDelta = NormalizeEulerAngles(deltaQuat.eulerAngles);
            return true;
        }

        public static bool TryComputeRestPoseDeltaForTargetBone(
            AfkMotionPatch patch, GameObject targetAvatar, string targetPath, out Vector3 eulerDelta)
        {
            eulerDelta = Vector3.zero;
            if (!patch || !patch.sourceAvatar || !targetAvatar || string.IsNullOrEmpty(targetPath))
                return false;

            var remap = patch.rendererPathRemaps.FirstOrDefault(r => r != null && r.toPath == targetPath);
            var sourcePath = remap != null && !string.IsNullOrEmpty(remap.fromPath) ? remap.fromPath : targetPath;
            return TryComputeRestPoseDelta(patch.sourceAvatar, targetAvatar, sourcePath, targetPath, out eulerDelta);
        }

        public static Vector3 NormalizeEulerAngles(Vector3 euler)
        {
            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z));
        }

        public static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private static Vector3 ClampEulerForSlider(Vector3 euler)
        {
            return new Vector3(
                Mathf.Clamp(Mathf.Round(euler.x * 10f) / 10f, -90f, 90f),
                Mathf.Clamp(Mathf.Round(euler.y * 10f) / 10f, -90f, 90f),
                Mathf.Clamp(Mathf.Round(euler.z * 10f) / 10f, -90f, 90f));
        }

        private static EditorCurveBinding[] CollectSourceBindings(AfkMotionPatch patch)
        {
            if (!AfkMotionSourceGraph.TryExtract(patch, out var graph, out _))
                return Array.Empty<EditorCurveBinding>();

            return graph.EffectiveSourceClips(patch)
                .SelectMany(clip => AnimationUtility.GetCurveBindings(clip)
                    .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                .ToArray();
        }

        private static List<string> CollectTransformPaths(Transform root)
        {
            var paths = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                paths.Add(AnimationUtility.CalculateTransformPath(t, root));
            }
            return paths;
        }

        private static string FindBestTargetPath(
            string sourcePath, bool isSmr, GameObject sourceAvatar, GameObject targetAvatar,
            List<string> targetTransforms, HashSet<string> targetSmrPaths, HashSet<string> usedTargetPaths)
        {
            var sourceSig = ParseBoneSignature(sourcePath);
            string bestPath = null;
            float bestScore = 0f;

            var candidates = isSmr
                ? targetTransforms.Where(targetSmrPaths.Contains)
                : targetTransforms.Where(p => !targetSmrPaths.Contains(p));

            foreach (var candidatePath in candidates)
            {
                if (usedTargetPaths.Contains(candidatePath)) continue;
                var candidateSig = ParseBoneSignature(candidatePath);

                // Side must match if both specify a side
                if (sourceSig.Side != 0 && candidateSig.Side != 0 && sourceSig.Side != candidateSig.Side)
                    continue;
                if ((sourceSig.Side != 0) != (candidateSig.Side != 0))
                    continue;

                // Trailing index must match if both specify an index (e.g. Tail.001 <-> Tail_1)
                if (sourceSig.Index != candidateSig.Index)
                    continue;

                var stemScore = ComputeTokenSimilarity(sourceSig.StemTokens, candidateSig.StemTokens);
                if (stemScore < 0.7f) continue;

                var ancestorScore = ComputeTokenSimilarity(sourceSig.AncestorTokens, candidateSig.AncestorTokens);
                var score = stemScore * 0.75f + ancestorScore * 0.25f;

                if (isSmr && sourceAvatar && targetAvatar)
                {
                    var srcSmr = sourceAvatar.transform.Find(sourcePath)?.GetComponent<SkinnedMeshRenderer>();
                    var dstSmr = targetAvatar.transform.Find(candidatePath)?.GetComponent<SkinnedMeshRenderer>();
                    if (srcSmr && dstSmr && srcSmr.sharedMesh && dstSmr.sharedMesh)
                    {
                        if (dstSmr.sharedMesh.blendShapeCount > 0) score += 0.1f;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPath = candidatePath;
                }
            }

            return bestScore >= 0.72f ? bestPath : null;
        }

        private static string FindBestBlendShapeMatch(string sourceShape, string[] targetShapes)
        {
            var srcInfo = ParseBlendShapeInfo(sourceShape);
            string bestTarget = null;
            float bestScore = 0f;

            foreach (var candidate in targetShapes)
            {
                var dstInfo = ParseBlendShapeInfo(candidate);
                if (srcInfo.Side != 0 && dstInfo.Side != 0 && srcInfo.Side != dstInfo.Side)
                    continue;

                var score = ScoreBlendShapeSimilarity(srcInfo, dstInfo);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                }
            }

            return bestScore >= 0.65f ? bestTarget : null;
        }

        private static float ScoreBlendShapeSimilarity(BlendShapeInfo a, BlendShapeInfo b)
        {
            if (string.Equals(a.Compact, b.Compact, StringComparison.OrdinalIgnoreCase))
                return 1f;

            // Prefix/suffix match like eye_nagomi <-> eye_nagomi_2
            if (a.Compact.Length >= 4 && b.Compact.Length >= 4)
            {
                if (b.Compact.StartsWith(a.Compact, StringComparison.OrdinalIgnoreCase)
                    || a.Compact.StartsWith(b.Compact, StringComparison.OrdinalIgnoreCase))
                {
                    var ratio = (float)Mathf.Min(a.Compact.Length, b.Compact.Length) / Mathf.Max(a.Compact.Length, b.Compact.Length);
                    if (ratio >= 0.7f) return 0.75f + 0.2f * ratio;
                }
            }

            var tokenSim = ComputeTokenSimilarity(a.ExpandedTokens, b.ExpandedTokens);
            var editSim = 1f - (float)LevenshteinDistance(a.Compact, b.Compact) / Mathf.Max(1, Mathf.Max(a.Compact.Length, b.Compact.Length));
            return Mathf.Max(tokenSim, editSim * 0.9f);
        }

        private struct BoneSignature
        {
            public string[] StemTokens;
            public string[] AncestorTokens;
            public int Side; // -1 = Left, +1 = Right, 0 = None
            public int Index; // -1 = None, >= 0 = numeric index
        }

        private static BoneSignature ParseBoneSignature(string path)
        {
            var segments = path.Split('/');
            var leaf = segments[segments.Length - 1];
            var index = -1;
            var match = TrailingDigitsRegex.Match(leaf);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                index = parsed;
                leaf = leaf.Substring(0, match.Index);
            }

            var rawTokens = Tokenize(leaf);
            var side = ExtractSide(rawTokens, out var stemTokens);
            var ancestorTokens = segments.Take(segments.Length - 1)
                .SelectMany(Tokenize)
                .Where(t => t != "armature" && t != "root")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new BoneSignature
            {
                StemTokens = stemTokens,
                AncestorTokens = ancestorTokens,
                Side = side,
                Index = index
            };
        }

        private struct BlendShapeInfo
        {
            public string Compact;
            public string[] ExpandedTokens;
            public int Side;
        }

        private static BlendShapeInfo ParseBlendShapeInfo(string name)
        {
            var tokens = Tokenize(name);
            var side = ExtractSide(tokens, out var coreTokens);
            var expanded = new HashSet<string>(coreTokens, StringComparer.OrdinalIgnoreCase);

            foreach (var (groupA, groupB) in BlendShapeSynonyms)
            {
                var hasA = coreTokens.Any(t => groupA.Contains(t, StringComparer.OrdinalIgnoreCase));
                var hasB = coreTokens.Any(t => groupB.Contains(t, StringComparer.OrdinalIgnoreCase));
                if (hasA || hasB)
                {
                    foreach (var s in groupA) expanded.Add(s);
                    foreach (var s in groupB) expanded.Add(s);
                }
            }

            var sb = new StringBuilder();
            foreach (var t in coreTokens) sb.Append(t.ToLowerInvariant());

            return new BlendShapeInfo
            {
                Compact = sb.ToString(),
                ExpandedTokens = expanded.ToArray(),
                Side = side
            };
        }

        private static string[] Tokenize(string input)
        {
            if (string.IsNullOrEmpty(input)) return Array.Empty<string>();
            return SplitTokenRegex.Split(input)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .ToArray();
        }

        private static int ExtractSide(string[] tokens, out string[] remaining)
        {
            var side = 0;
            var list = new List<string>(tokens.Length);
            foreach (var t in tokens)
            {
                if (t == "l" || t == "left") { side = -1; continue; }
                if (t == "r" || t == "right") { side = 1; continue; }
                list.Add(t);
            }
            remaining = list.ToArray();
            return side;
        }

        private static float ComputeTokenSimilarity(string[] a, string[] b)
        {
            if (a.Length == 0 && b.Length == 0) return 1f;
            if (a.Length == 0 || b.Length == 0) return 0f;
            var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
            var setB = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
            var intersect = setA.Count(setB.Contains);
            var union = setA.Count + setB.Count - intersect;
            return union > 0 ? (float)intersect / union : 0f;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
            if (string.IsNullOrEmpty(t)) return s.Length;
            var d = new int[s.Length + 1, t.Length + 1];
            for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= t.Length; j++) d[0, j] = j;
            for (var i = 1; i <= s.Length; i++)
            {
                for (var j = 1; j <= t.Length; j++)
                {
                    var cost = char.ToLowerInvariant(s[i - 1]) == char.ToLowerInvariant(t[j - 1]) ? 0 : 1;
                    d[i, j] = Mathf.Min(
                        Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[s.Length, t.Length];
        }
    }
}
#endif
