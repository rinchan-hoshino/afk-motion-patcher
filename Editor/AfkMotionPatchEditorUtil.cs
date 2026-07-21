#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace THE_cattail.AfkMotionPatcher
{
    public static class AfkMotionPatchEditorUtil
    {
        public static GameObject FindAvatarRoot(GameObject child)
        {
            if (!child) return null;
            var descriptor = child.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor ? descriptor.gameObject : null;
        }

        public static bool TryGetActivePatch(GameObject avatarRoot, out AfkMotionPatch patch)
        {
            patch = null;
            if (avatarRoot == null) return false;

            var patches = avatarRoot.GetComponentsInChildren<AfkMotionPatch>(true)
                .OrderBy(p => GetPath(p.transform))
                .ToArray();
            if (patches.Length == 0) return false;

            var activePatches = patches.Where(p => p.enabled && p.gameObject.activeInHierarchy).ToArray();
            if (activePatches.Length == 0) return false;

            if (activePatches.Length > 1)
            {
                Debug.LogError($"[AfkMotionPatch] Multiple active patches found under avatar={avatarRoot.name}. Keep exactly one enabled patch. Candidates={string.Join(", ", activePatches.Select(p => GetPath(p.transform)))}", avatarRoot);
                return false;
            }

            patch = activePatches[0];
            return true;
        }

        public static VirtualMotion CloneSourceMotion(Motion source, IDictionary<Motion, VirtualMotion> memo)
        {
            if (!source) return null;
            if (memo.TryGetValue(source, out var existing)) return existing;

            if (source is AnimationClip sourceClip)
            {
                if (AnimationUtility.GetAnimationEvents(sourceClip).Length > 0)
                    throw new NotSupportedException($"Animation Events are not supported in source AFK Motions: {sourceClip.name}");
                var clip = VirtualClip.Create(sourceClip.name);
                memo[source] = clip;
                var settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
                var additiveReference = settings.additiveReferencePoseClip;
                CopyClipSettings(sourceClip, clip);
                if (additiveReference)
                {
                    clip.AdditiveReferencePoseClip = CloneSourceMotion(additiveReference, memo);
                    clip.AdditiveReferencePoseTime = settings.additiveReferencePoseTime;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
                    clip.SetFloatCurve(binding, AnimationUtility.GetEditorCurve(sourceClip, binding));
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(sourceClip))
                    clip.SetObjectCurve(binding, AnimationUtility.GetObjectReferenceCurve(sourceClip, binding));
                return clip;
            }

            if (source is BlendTree sourceTree)
            {
                var tree = VirtualBlendTree.Create(sourceTree.name);
                memo[source] = tree;
                tree.BlendParameter = sourceTree.blendParameter;
                tree.BlendParameterY = sourceTree.blendParameterY;
                tree.BlendType = sourceTree.blendType;
                tree.MaxThreshold = sourceTree.maxThreshold;
                tree.MinThreshold = sourceTree.minThreshold;
                tree.UseAutomaticThresholds = sourceTree.useAutomaticThresholds;
                using (var serializedTree = new SerializedObject(sourceTree))
                    tree.NormalizedBlendValues = serializedTree.FindProperty("m_NormalizedBlendValues").boolValue;
                tree.Children = sourceTree.children.Select(child => new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = CloneSourceMotion(child.motion, memo),
                    CycleOffset = child.cycleOffset,
                    DirectBlendParameter = child.directBlendParameter,
                    Mirror = child.mirror,
                    Threshold = child.threshold,
                    Position = child.position,
                    TimeScale = child.timeScale
                }).ToImmutableList();
                return tree;
            }

            throw new NotSupportedException($"Unsupported source Motion type: {source.GetType().FullName}");
        }

        public static void RetargetVirtualClipInPlace(VirtualClip clip, GameObject avatarRoot, AfkMotionPatch patch,
            out int copiedSourceCurveCount, out int appendedMappedCurveCount)
        {
            copiedSourceCurveCount = 0;
            appendedMappedCurveCount = 0;
            if (clip == null || avatarRoot == null || patch == null) return;

            // Snapshot every original binding and curve before writing anything. A mapped target can itself be an
            // original source binding; later iterations must still read that binding's original curve.
            var sourceFloatCurves = clip.GetFloatCurveBindings()
                .Select(binding => new { Binding = binding, Curve = clip.GetFloatCurve(binding) })
                .Where(item => item.Curve != null)
                .ToArray();
            foreach (var item in sourceFloatCurves)
            {
                var sourceBinding = item.Binding;
                var sourceCurve = item.Curve;
                var adjustedSourceCurve = TransformCurve(sourceCurve, GetSourceKeyTransform(sourceBinding, patch));
                clip.SetFloatCurve(sourceBinding, adjustedSourceCurve);
                copiedSourceCurveCount++;

                foreach (var mappedBinding in BuildAdditionalMappedBindings(sourceBinding, avatarRoot, patch))
                {
                    if (SameBinding(sourceBinding, mappedBinding.Binding)) continue;
                    clip.SetFloatCurve(mappedBinding.Binding, TransformCurve(adjustedSourceCurve, mappedBinding.Transform));
                    appendedMappedCurveCount++;
                }
            }

            var sourceObjectCurves = clip.GetObjectCurveBindings()
                .Select(binding => new { Binding = binding, Curve = clip.GetObjectCurve(binding)?.ToArray() })
                .Where(item => item.Curve != null)
                .ToArray();
            foreach (var item in sourceObjectCurves)
            {
                var sourceBinding = item.Binding;
                var sourceCurve = item.Curve;
                clip.SetObjectCurve(sourceBinding, sourceCurve.ToArray());
                copiedSourceCurveCount++;

                foreach (var mappedBinding in BuildAdditionalMappedBindings(sourceBinding, avatarRoot, patch))
                {
                    if (SameBinding(sourceBinding, mappedBinding.Binding)) continue;
                    clip.SetObjectCurve(mappedBinding.Binding, sourceCurve.ToArray());
                    appendedMappedCurveCount++;
                }
            }
        }

        public static int EnableAnimationOnAffectedPhysBones(
            GameObject avatarRoot, IEnumerable<AnimationClip> clips)
        {
            if (!avatarRoot) return 0;
            var animatedTransformPaths = (clips ?? Enumerable.Empty<AnimationClip>())
                .Where(clip => clip)
                .SelectMany(AnimationUtility.GetCurveBindings)
                .Where(binding => binding.type == typeof(Transform))
                .Select(binding => binding.path);
            return EnableAnimationOnAffectedPhysBones(avatarRoot, animatedTransformPaths);
        }

        public static int EnableAnimationOnAffectedPhysBones(
            GameObject avatarRoot, IEnumerable<string> animatedTransformPaths)
        {
            if (!avatarRoot) return 0;
            var animatedTransforms = (animatedTransformPaths ?? Enumerable.Empty<string>())
                .Where(path => path != null)
                .Select(path => path.Length == 0 ? avatarRoot.transform : avatarRoot.transform.Find(path))
                .Where(transform => transform)
                .Distinct()
                .ToArray();
            if (animatedTransforms.Length == 0) return 0;

            var changed = 0;
            foreach (var physBone in avatarRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var physBoneRoot = physBone.rootTransform ? physBone.rootTransform : physBone.transform;
                if (!animatedTransforms.Any(transform => IsTransformInPhysBoneChain(physBone, physBoneRoot, transform)))
                    continue;
                if (physBone.isAnimated) continue;
                physBone.isAnimated = true;
                changed++;
            }
            return changed;
        }

        private static bool IsTransformInPhysBoneChain(
            VRCPhysBone physBone, Transform physBoneRoot, Transform transform)
        {
            if (transform != physBoneRoot && !transform.IsChildOf(physBoneRoot)) return false;
            return !(physBone.ignoreTransforms ?? new List<Transform>())
                .Where(ignored => ignored)
                .Any(ignored => transform == ignored || transform.IsChildOf(ignored));
        }

        public static AnimationClip CreatePreviewClip(AnimationClip targetBaseClip, AnimationClip source,
            GameObject avatarRoot, AfkMotionPatch patch, string suffix,
            out int copiedSourceCurveCount, out int appendedMappedCurveCount)
        {
            return CreatePreviewClipRecursive(targetBaseClip, source, avatarRoot, patch, suffix,
                new Dictionary<AnimationClip, AnimationClip>(),
                out copiedSourceCurveCount, out appendedMappedCurveCount);
        }

        private static AnimationClip CreatePreviewClipRecursive(AnimationClip targetBaseClip, AnimationClip source,
            GameObject avatarRoot, AfkMotionPatch patch, string suffix,
            IDictionary<AnimationClip, AnimationClip> memo,
            out int copiedSourceCurveCount, out int appendedMappedCurveCount)
        {
            copiedSourceCurveCount = 0;
            appendedMappedCurveCount = 0;

            if (source == null || avatarRoot == null || patch == null) return null;
            if (memo.TryGetValue(source, out var existing)) return existing;

            var composed = new AnimationClip
            {
                name = $"AfkMotionPatchPreview_{(targetBaseClip != null ? targetBaseClip.name : "Empty")}_{source.name}_{suffix}"
            };
            memo[source] = composed;
            CopyClipSettings(source, composed);

            foreach (var sourceBinding in AnimationUtility.GetCurveBindings(source))
            {
                var sourceCurve = AnimationUtility.GetEditorCurve(source, sourceBinding);
                if (sourceCurve == null) continue;

                // Match build-time semantics: adjust source keys once, copy them, then append
                // path/property mappings from the same adjusted motion.
                var adjustedSourceCurve = TransformCurve(sourceCurve, GetSourceKeyTransform(sourceBinding, patch));
                AnimationUtility.SetEditorCurve(composed, sourceBinding, adjustedSourceCurve);
                copiedSourceCurveCount++;

                foreach (var mappedBinding in BuildAdditionalMappedBindings(sourceBinding, avatarRoot, patch))
                {
                    if (SameBinding(sourceBinding, mappedBinding.Binding)) continue;
                    AnimationUtility.SetEditorCurve(composed, mappedBinding.Binding, TransformCurve(adjustedSourceCurve, mappedBinding.Transform));
                    appendedMappedCurveCount++;
                }
            }

            foreach (var sourceBinding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var sourceCurve = AnimationUtility.GetObjectReferenceCurve(source, sourceBinding);
                if (sourceCurve == null) continue;

                AnimationUtility.SetObjectReferenceCurve(composed, sourceBinding, sourceCurve.ToArray());
                copiedSourceCurveCount++;

                foreach (var mappedBinding in BuildAdditionalMappedBindings(sourceBinding, avatarRoot, patch))
                {
                    if (SameBinding(sourceBinding, mappedBinding.Binding)) continue;
                    AnimationUtility.SetObjectReferenceCurve(composed, mappedBinding.Binding, sourceCurve.ToArray());
                    appendedMappedCurveCount++;
                }
            }

            var sourceSettings = AnimationUtility.GetAnimationClipSettings(source);
            if (sourceSettings.additiveReferencePoseClip)
            {
                var reference = CreatePreviewClipRecursive(null, sourceSettings.additiveReferencePoseClip,
                    avatarRoot, patch, suffix + "_AdditiveReference", memo,
                    out var referenceCopied, out var referenceAppended);
                var targetSettings = AnimationUtility.GetAnimationClipSettings(composed);
                targetSettings.additiveReferencePoseClip = reference;
                targetSettings.additiveReferencePoseTime = sourceSettings.additiveReferencePoseTime;
                AnimationUtility.SetAnimationClipSettings(composed, targetSettings);
                copiedSourceCurveCount += referenceCopied;
                appendedMappedCurveCount += referenceAppended;
            }

            return composed;
        }

        private static void CopyClipSettings(AnimationClip source, VirtualClip target)
        {
            target.FrameRate = source.frameRate;
            target.WrapMode = source.wrapMode;
            target.Legacy = source.legacy;
            target.LocalBounds = source.localBounds;
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.additiveReferencePoseClip = null;
            target.Settings = settings;
        }

        private static void CopyClipSettings(AnimationClip source, AnimationClip target)
        {
            target.frameRate = source.frameRate;
            target.wrapMode = source.wrapMode;
            target.legacy = source.legacy;
            target.localBounds = source.localBounds;
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.additiveReferencePoseClip = null;
            AnimationUtility.SetAnimationClipSettings(target, settings);
        }

        private sealed class CurveTransform
        {
            public float Scale = 1f;
            public float Offset;
            public bool Clamp;
            public float ClampMin;
            public float ClampMax = 100f;
        }

        private sealed class MappedBinding
        {
            public EditorCurveBinding Binding;
            public CurveTransform Transform = new CurveTransform();
        }

        private static IEnumerable<MappedBinding> BuildAdditionalMappedBindings(EditorCurveBinding binding, GameObject avatarRoot, AfkMotionPatch patch)
        {
            var basePath = RemapPath(binding.path, patch);
            var baseBinding = binding;
            baseBinding.path = basePath;

            var baseTransform = new CurveTransform();
            var propertyMapped = TryRemapBlendShapeProperty(binding.propertyName, patch, baseTransform, out var remappedProperty);
            if (basePath != binding.path || propertyMapped)
            {
                var mapped = baseBinding;
                mapped.propertyName = propertyMapped ? remappedProperty : binding.propertyName;
                if (ValidateMappedBinding(mapped, avatarRoot))
                {
                    yield return new MappedBinding { Binding = mapped, Transform = baseTransform };
                }
            }

            foreach (var rule in patch.sourceBindingRules.Where(rule => RuleMatches(rule, binding)))
            {
                var transform = new CurveTransform
                {
                    Scale = rule.scale,
                    Offset = rule.offset,
                    Clamp = rule.clamp,
                    ClampMin = rule.clampMin,
                    ClampMax = rule.clampMax
                };

                var mapped = binding;
                mapped.path = string.IsNullOrEmpty(rule.targetPath) ? basePath : rule.targetPath;
                mapped.propertyName = string.IsNullOrEmpty(rule.targetPropertyName)
                    ? (propertyMapped ? remappedProperty : binding.propertyName)
                    : rule.targetPropertyName;

                if (ValidateMappedBinding(mapped, avatarRoot))
                {
                    yield return new MappedBinding { Binding = mapped, Transform = transform };
                }
            }
        }

        private static CurveTransform GetSourceKeyTransform(EditorCurveBinding binding, AfkMotionPatch patch)
        {
            var adjustment = patch.sourceKeyAdjustments.FirstOrDefault(rule => SourceKeyAdjustmentMatches(rule, binding));
            if (adjustment == null) return new CurveTransform();
            return new CurveTransform
            {
                Scale = adjustment.scale,
                Offset = adjustment.offset,
                Clamp = adjustment.clamp,
                ClampMin = adjustment.clampMin,
                ClampMax = adjustment.clampMax
            };
        }

        private static bool SourceKeyAdjustmentMatches(AfkMotionPatch.SourceKeyAdjustment rule, EditorCurveBinding binding)
        {
            if (rule == null) return false;
            if (!string.IsNullOrEmpty(rule.sourcePath) && binding.path != rule.sourcePath) return false;
            if (!string.IsNullOrEmpty(rule.sourcePropertyName) && binding.propertyName != rule.sourcePropertyName) return false;
            return SourceComponentMatches(rule.sourceComponent, binding);
        }

        private static bool RuleMatches(AfkMotionPatch.SourceBindingRule rule, EditorCurveBinding binding)
        {
            if (rule == null) return false;
            if (!string.IsNullOrEmpty(rule.sourcePath) && binding.path != rule.sourcePath) return false;
            if (!string.IsNullOrEmpty(rule.sourcePropertyName) && binding.propertyName != rule.sourcePropertyName) return false;
            return SourceComponentMatches(rule.sourceComponent, binding);
        }

        private static bool SourceComponentMatches(AfkMotionPatch.SourceBindingComponent component, EditorCurveBinding binding)
        {
            switch (component)
            {
                case AfkMotionPatch.SourceBindingComponent.Transform:
                    return binding.type == typeof(Transform);
                case AfkMotionPatch.SourceBindingComponent.SkinnedMeshRenderer:
                    return binding.type == typeof(SkinnedMeshRenderer);
                default:
                    return true;
            }
        }

        private static string RemapPath(string path, AfkMotionPatch patch)
        {
            var pathRemap = patch.rendererPathRemaps.FirstOrDefault(r => r != null && path == (r.fromPath ?? string.Empty));
            return pathRemap != null ? pathRemap.toPath ?? string.Empty : path;
        }

        private static bool TryRemapBlendShapeProperty(string propertyName, AfkMotionPatch patch, CurveTransform transform, out string remappedPropertyName)
        {
            remappedPropertyName = propertyName;
            if (!propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) return false;

            var shape = propertyName.Substring("blendShape.".Length);
            var shapeRemap = patch.blendShapeNameRemaps.FirstOrDefault(r => r != null && shape == (r.fromName ?? string.Empty));
            if (shapeRemap == null) return false;

            transform.Scale = shapeRemap.scale;
            transform.Offset = shapeRemap.offset;
            transform.Clamp = shapeRemap.clamp;
            transform.ClampMin = shapeRemap.clampMin;
            transform.ClampMax = shapeRemap.clampMax;
            remappedPropertyName = "blendShape." + (shapeRemap.toName ?? string.Empty);
            return true;
        }

        private static bool ValidateMappedBinding(EditorCurveBinding targetBinding, GameObject avatarRoot)
        {
            if (targetBinding.type == typeof(SkinnedMeshRenderer) && targetBinding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
            {
                var shapeName = targetBinding.propertyName.Substring("blendShape.".Length);
                if (!BlendshapeExists(avatarRoot, targetBinding.path, shapeName)) return false;
            }

            return true;
        }

        private static bool SameBinding(EditorCurveBinding left, EditorCurveBinding right)
        {
            return left.path == right.path
                && left.type == right.type
                && left.propertyName == right.propertyName;
        }

        private static AnimationCurve TransformCurve(AnimationCurve source, CurveTransform transform)
        {
            var keys = source.keys.Select(key =>
            {
                key.value = ApplyValueTransform(key.value, transform);
                key.inTangent = TransformTangent(key.inTangent, transform.Scale);
                key.outTangent = TransformTangent(key.outTangent, transform.Scale);
                return key;
            }).ToArray();

            var curve = new AnimationCurve(keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return curve;
        }

        private static float TransformTangent(float tangent, float scale)
        {
            // Infinite tangents encode stepped/discrete curves. Preserve that sentinel instead of
            // producing NaN for valid transforms such as scale=0.
            return float.IsInfinity(tangent) ? tangent : tangent * scale;
        }

        private static float ApplyValueTransform(float value, CurveTransform transform)
        {
            var result = value * transform.Scale + transform.Offset;
            if (transform.Clamp) result = Mathf.Clamp(result, transform.ClampMin, transform.ClampMax);
            return result;
        }

        private static bool BlendshapeExists(GameObject root, string rendererPath, string shapeName)
        {
            var t = string.IsNullOrEmpty(rendererPath) ? root.transform : root.transform.Find(rendererPath);
            var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null) return false;
            for (var i = 0; i < mesh.blendShapeCount; i++) if (mesh.GetBlendShapeName(i) == shapeName) return true;
            return false;
        }

        public static string GetPath(Transform transform)
        {
            var parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                transform = transform.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif
