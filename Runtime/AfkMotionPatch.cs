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
        [HideInInspector]
        public List<SourceStateMotion> sourceStateMotions = new List<SourceStateMotion>();

        public List<RendererPathRemap> rendererPathRemaps = new List<RendererPathRemap>();
        public List<BlendShapeNameRemap> blendShapeNameRemaps = new List<BlendShapeNameRemap>();
        [Tooltip("Adjustments applied to the original copied source keys before they are written.")]
        public List<SourceKeyAdjustment> sourceKeyAdjustments = new List<SourceKeyAdjustment>();
        [Tooltip("Advanced additive remap/adjust rules. Source curves are always copied first; matching rules append extra mapped keys with optional scale/offset/clamp.")]
        public List<SourceBindingRule> sourceBindingRules = new List<SourceBindingRule>();
    }
}
