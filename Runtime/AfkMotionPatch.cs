using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;

namespace THE_cattail.AfkMotionPatcher
{
    [DisallowMultipleComponent]
    [AddComponentMenu("THE_cattail/AFK Motion Patch")]
    public sealed class AfkMotionPatch : MonoBehaviour, INDMFEditorOnly
    {
        public enum ActionMergeMode
        {
            PreserveExistingAction = 0,
            ReplaceEntireAction = 1,
        }

        public enum SourceBindingComponent
        {
            Any = 0,
            Transform = 1,
            SkinnedMeshRenderer = 2,
        }

        [Serializable]
        public sealed class SourceStateMotion
        {
            [Tooltip("Stable layer/state-machine/state path from the source Action Controller.")]
            public string statePath;
            public Motion motion;
        }

        [Serializable]
        public sealed class BoneRotationOffset
        {
            [Tooltip("Target avatar bone path (relative to Avatar root).")]
            public string bonePath;
            [Tooltip("Local rotation offset in degrees: X = Pitch, Y = Yaw, Z = Roll.")]
            public Vector3 eulerOffset;
        }

        [Serializable]
        public sealed class HumanoidPoseAdjustment
        {
            [Tooltip("Enable build-time & preview-time Humanoid pose adjustment baked directly into Humanoid muscle curves (zero runtime overhead).")]
            public bool enabled;

            [Tooltip("Contact-weighted hand position offset in Head-local space (meters: +X right cheek outward, +Y up, +Z forward away from face). Automatically 0 while standing and 1 when hands are near head.")]
            public Vector3 handOffsetInHeadSpace;

            [Tooltip("Hand-to-head distance (meters) below which the contact weight reaches 1.0.")]
            public float contactFullDistance = 0.20f;

            [Tooltip("Hand-to-head distance (meters) above which the contact weight smoothly fades to 0.0, keeping start/end standing poses untouched.")]
            public float contactFadeDistance = 0.30f;

            [Tooltip("Optional outward elbow pole bias (meters) when solving 2-bone arm IK.")]
            public float elbowOutwardBias;

            [Tooltip("Contact-weighted Upper Leg In-Out muscle adjustment (negative brings knees closer together).")]
            public float legCloseAdjustment;

            [Tooltip("Contact-weighted Lower Leg Stretch muscle adjustment (negative bends knees tighter).")]
            public float kneeBendAdjustment;
        }

        [Serializable]
        public sealed class RendererPathRemap
        {
            public string fromPath = "Body";
            public string toPath = "Body";
        }

        [Serializable]
        public sealed class BlendShapeNameRemap
        {
            public string fromName;
            public string toName;
            public float scale = 1f;
            public float offset = 0f;
            public bool clamp;
            public float clampMin = 0f;
            public float clampMax = 100f;
        }

        [Serializable]
        public sealed class SourceKeyAdjustment
        {
            [Tooltip("Empty means any source path.")]
            public string sourcePath;
            public SourceBindingComponent sourceComponent = SourceBindingComponent.Any;
            [Tooltip("Empty means any property on the selected source path/component.")]
            public string sourcePropertyName;

            public float scale = 1f;
            public float offset = 0f;
            public bool clamp;
            public float clampMin = 0f;
            public float clampMax = 100f;
        }

        [Serializable]
        public sealed class SourceBindingRule
        {
            [Tooltip("Empty means any source path.")]
            public string sourcePath;
            public SourceBindingComponent sourceComponent = SourceBindingComponent.Any;
            [Tooltip("Empty means any property on the selected source path/component.")]
            public string sourcePropertyName;

            [Tooltip("Empty keeps the source path after renderer path remaps.")]
            public string targetPath;
            [Tooltip("Empty keeps the source property after blendShape/property remaps.")]
            public string targetPropertyName;

            public float scale = 1f;
            public float offset = 0f;
            public bool clamp;
            public float clampMin = 0f;
            public float clampMax = 100f;
        }

        [Tooltip("Avatar prefab whose Action Controller and AFK motion bindings define Preview and build output.")]
        public GameObject sourceAvatar;

        [Tooltip("Preserve existing Action layers (GoGoLoco, dances, poses) and surgically replace only AFK triggers, or replace the entire Action layer.")]
        public ActionMergeMode actionMergeMode = ActionMergeMode.PreserveExistingAction;

        [Tooltip("Safely strip AnimationEvents from source AFK clips during build and preview.")]
        public bool stripAnimationEvents = true;

        [HideInInspector]
        public List<SourceStateMotion> sourceStateMotions = new List<SourceStateMotion>();

        [Tooltip("Intuitive Pitch / Yaw / Roll degree offsets applied to target bones.")]
        public List<BoneRotationOffset> boneRotationOffsets = new List<BoneRotationOffset>();

        [Tooltip("Optional build-time Humanoid contact & pose tuning (2-bone arm IK sleeve clearance and leg posture, automatically faded to 0 when standing).")]
        public HumanoidPoseAdjustment humanoidPoseAdjustment = new HumanoidPoseAdjustment();

        public List<RendererPathRemap> rendererPathRemaps = new List<RendererPathRemap>();
        public List<BlendShapeNameRemap> blendShapeNameRemaps = new List<BlendShapeNameRemap>();
        [Tooltip("Adjustments applied to the original copied source keys before they are written.")]
        public List<SourceKeyAdjustment> sourceKeyAdjustments = new List<SourceKeyAdjustment>();
        [Tooltip("Advanced additive remap/adjust rules. Source curves are always copied first; matching rules append extra mapped keys with optional scale/offset/clamp.")]
        public List<SourceBindingRule> sourceBindingRules = new List<SourceBindingRule>();
    }
}
