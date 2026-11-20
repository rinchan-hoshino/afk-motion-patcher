#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;
using UnityEditor;
using static THE_cattail.AfkMotionPatcher.AfkMotionPatchLocalization;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace THE_cattail.AfkMotionPatcher
{
    [CustomEditor(typeof(AfkMotionPatch))]
    internal sealed class AfkMotionPatchInspector : UnityEditor.Editor
    {
        private SerializedProperty _sourceAvatar;
        private SerializedProperty _actionMergeMode;
        private SerializedProperty _stripAnimationEvents;
        private SerializedProperty _boneRotationOffsets;
        private SerializedProperty _humanoidPoseAdjustment;
        private SerializedProperty _rendererPathRemaps;
        private SerializedProperty _blendShapeNameRemaps;
        private SerializedProperty _sourceKeyAdjustments;
        private SerializedProperty _sourceBindingRules;

        private ReorderableList _boneOffsetList;
        private ReorderableList _rendererPathList;
        private ReorderableList _blendShapeList;
        private ReorderableList _sourceKeyAdjustmentList;
        private ReorderableList _bindingRuleList;
        private List<EditorCurveBinding> _sourceBindingsCache;
        private List<EditorCurveBinding> _targetBindingsCache;

        private bool _showSourceSection;
        private bool _showTuningSection;
        private bool _showHumanoidPoseTuning;
        private bool _showBoneOffsets;
        private bool _showBlendShapeMappings;
        private bool _showAdvancedSection;
        private bool _showRendererPaths;
        private bool _showSourceCurveAdjustments;
        private bool _showAdditionalBindingRules;
        private string _lastActionFeedback;

        private const string FoldoutPrefsPrefix = "THE_cattail.AfkMotionPatcher.Inspector.v2.";
        private const float RowGap = 3f;
        private const float ElementPadding = 4f;
        private const float DynamicLabelGap = 4f;
        private const float MaxDynamicLabelWidthRatio = 0.4f;

        private void OnEnable()
        {
            _sourceAvatar = serializedObject.FindProperty(nameof(AfkMotionPatch.sourceAvatar));
            _actionMergeMode = serializedObject.FindProperty(nameof(AfkMotionPatch.actionMergeMode));
            _stripAnimationEvents = serializedObject.FindProperty(nameof(AfkMotionPatch.stripAnimationEvents));
            _boneRotationOffsets = serializedObject.FindProperty(nameof(AfkMotionPatch.boneRotationOffsets));
            _humanoidPoseAdjustment = serializedObject.FindProperty(nameof(AfkMotionPatch.humanoidPoseAdjustment));
            _rendererPathRemaps = serializedObject.FindProperty(nameof(AfkMotionPatch.rendererPathRemaps));
            _blendShapeNameRemaps = serializedObject.FindProperty(nameof(AfkMotionPatch.blendShapeNameRemaps));
            _sourceKeyAdjustments = serializedObject.FindProperty(nameof(AfkMotionPatch.sourceKeyAdjustments));
            _sourceBindingRules = serializedObject.FindProperty(nameof(AfkMotionPatch.sourceBindingRules));

            _boneOffsetList = CreateBoneOffsetList();
            _rendererPathList = CreateRendererPathList();
            _blendShapeList = CreateBlendShapeList();
            _sourceKeyAdjustmentList = CreateSourceKeyAdjustmentList();
            _bindingRuleList = CreateBindingRuleList();

            _showSourceSection = LoadFoldoutState("SourceSection", true);
            _showTuningSection = LoadFoldoutState("TuningSection", true);
            _showHumanoidPoseTuning = LoadFoldoutState("HumanoidPoseTuning", true);
            _showBoneOffsets = LoadFoldoutState("BoneOffsets", true);
            _showBlendShapeMappings = LoadFoldoutState("BlendShapeMappings", false);
            _showAdvancedSection = LoadFoldoutState("AdvancedSection", false);
            _showRendererPaths = LoadFoldoutState("RendererPaths", false);
            _showSourceCurveAdjustments = LoadFoldoutState("SourceCurveAdjustments", false);
            _showAdditionalBindingRules = LoadFoldoutState("AdditionalBindingRules", false);

            EditorApplication.projectChanged += InvalidateBindingCaches;
            EditorApplication.hierarchyChanged += InvalidateBindingCaches;
            Undo.undoRedoPerformed += InvalidateBindingCaches;
            Undo.postprocessModifications += OnPostprocessModifications;
            LanguagePrefs.RegisterLanguageChangeCallback(this, inspector => inspector.Repaint());
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= InvalidateBindingCaches;
            EditorApplication.hierarchyChanged -= InvalidateBindingCaches;
            Undo.undoRedoPerformed -= InvalidateBindingCaches;
            Undo.postprocessModifications -= OnPostprocessModifications;
        }

        private void InvalidateBindingCaches()
        {
            _sourceBindingsCache = null;
            _targetBindingsCache = null;
            Repaint();
        }

        private UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            InvalidateBindingCaches();
            return modifications;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var patch = (AfkMotionPatch)target;
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(132f, EditorGUIUtility.currentViewWidth * 0.38f);
            try
            {
                DrawLanguageSwitcher();
                EditorGUILayout.Space(2f);
                DrawStatus(patch);
                DrawPrimaryActions(patch);
                EditorGUILayout.Space(4f);
                DrawSourceAndMergeSection();
                EditorGUILayout.Space(2f);
                DrawAutoMatchAndTuningSection(patch);
                EditorGUILayout.Space(2f);
                DrawAdvancedSection();

                if (serializedObject.ApplyModifiedProperties())
                {
                    InvalidateBindingCaches();
                    AfkMotionPreviewWindow.NotifyPatchChanged(patch);
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = oldLabelWidth;
            }
        }

        private void DrawStatus(AfkMotionPatch patch)
        {
            var avatar = patch.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (!avatar)
            {
                EditorGUILayout.HelpBox(T("Avatar root not found. Place this component anywhere under a VRChat avatar."), MessageType.Error);
                return;
            }
            if (avatar.GetComponentsInChildren<AfkMotionPatch>(true)
                .Count(candidate => candidate.enabled && candidate.gameObject.activeInHierarchy) > 1)
            {
                EditorGUILayout.HelpBox(T("Multiple active AFK Motion Patch components were found under this Avatar. Keep exactly one enabled component."), MessageType.Error);
                return;
            }
            if (!_sourceAvatar.objectReferenceValue)
            {
                EditorGUILayout.HelpBox(T("Source Avatar is missing. Assign the Avatar prefab whose AFK motion is being adapted."), MessageType.Error);
                return;
            }
            if (!AfkMotionSourceGraph.TryExtract(patch, out var sourceGraph, out var graphError))
            {
                EditorGUILayout.HelpBox(F("AFK source graph error: {0}", graphError), MessageType.Error);
                return;
            }
            if (!sourceGraph.ValidateStateMotions(patch, out var motionError))
            {
                EditorGUILayout.HelpBox(F("The adapter preset's internal source Motion data is invalid. Update or reimport the preset. {0}", motionError), MessageType.Error);
                return;
            }
            if (!sourceGraph.EffectiveSourceClips(patch).Any())
            {
                EditorGUILayout.HelpBox(T("The AFK source graph and adapter preset provide no usable Motion. Update or reimport the source package and preset."), MessageType.Error);
                return;
            }

            var eventClips = sourceGraph.GetClipsWithAnimationEvents(patch);
            if (eventClips.Count > 0)
            {
                var names = string.Join(", ", eventClips.Select(c => c.name));
                if (_stripAnimationEvents.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        F("Source AFK clips contain AnimationEvents ({0}). They will be stripped safely during build and preview.", names),
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        F("Source AFK clips contain AnimationEvents ({0}). Keeping them may fail or warn on avatars without matching receiver scripts.", names),
                        MessageType.Warning);
                }
            }
        }

        private static void DrawPrimaryActions(AfkMotionPatch patch)
        {
            if (GUILayout.Button(T("Open Preview"), GUILayout.Height(28f)))
            {
                AfkMotionPreviewWindow.Open(patch);
            }
        }

        private void DrawSourceAndMergeSection()
        {
            _showSourceSection = BeginPersistentFoldout(
                _showSourceSection, T("1. Source & Action Merge"), "SourceSection", _sourceAvatar);
            if (_showSourceSection)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_sourceAvatar, new GUIContent(T("Avatar")));

                    var modeLabels = new[]
                    {
                        new GUIContent(T("Preserve Existing Action (Recommended)")),
                        new GUIContent(T("Replace Entire Action"))
                    };
                    var currentMode = Mathf.Clamp(_actionMergeMode.enumValueIndex, 0, modeLabels.Length - 1);
                    var nextMode = EditorGUILayout.Popup(new GUIContent(T("Action Merge Mode")), currentMode, modeLabels);
                    if (nextMode != currentMode) _actionMergeMode.enumValueIndex = nextMode;

                    var modeDescription = nextMode == (int)AfkMotionPatch.ActionMergeMode.PreserveExistingAction
                        ? T("Keeps GoGoLoco, dances, and poses intact while replacing only the AFK trigger branch.")
                        : T("Replaces the entire Action Controller with only the extracted AFK graph.");
                    DrawGroupDescription(modeDescription);

                    EditorGUILayout.PropertyField(_stripAnimationEvents, new GUIContent(T("Strip Animation Events")));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAutoMatchAndTuningSection(AfkMotionPatch patch)
        {
            _showTuningSection = BeginPersistentFoldout(
                _showTuningSection, T("2. Auto-Match & Pose Tuning"), "TuningSection", _boneRotationOffsets);
            if (_showTuningSection)
            {
                var targetAvatar = GetAvatarRoot();
                var canAutoMatch = targetAvatar && patch.sourceAvatar;

                using (new EditorGUI.DisabledScope(!canAutoMatch))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(T("Auto-Match Hierarchy & BlendShapes"), GUILayout.Height(24f)))
                    {
                        Undo.RecordObject(patch, "Auto-Match Hierarchy & BlendShapes");
                        var summary = AfkMotionAutoMatcher.AutoMatchHierarchyAndBlendShapes(
                            patch, targetAvatar.gameObject, GetSourceBindings());
                        EditorUtility.SetDirty(patch);
                        serializedObject.Update();
                        InvalidateBindingCaches();
                        AfkMotionPreviewWindow.NotifyPatchChanged(patch);
                        _lastActionFeedback = (summary.AddedHierarchyMappings > 0 || summary.AddedBlendShapeMappings > 0)
                            ? F("Auto-Match complete: +{0} hierarchy, +{1} BlendShapes.",
                                summary.AddedHierarchyMappings, summary.AddedBlendShapeMappings)
                            : T("Auto-Match found no additional unmatched items.");
                    }

                    if (GUILayout.Button(T("Match Rest-Pose Delta"), GUILayout.Height(24f)))
                    {
                        Undo.RecordObject(patch, "Match Rest-Pose Delta");
                        var summary = AfkMotionAutoMatcher.MatchRestPoseDeltas(
                            patch, targetAvatar.gameObject, GetSourceBindings());
                        EditorUtility.SetDirty(patch);
                        serializedObject.Update();
                        InvalidateBindingCaches();
                        AfkMotionPreviewWindow.NotifyPatchChanged(patch);
                        _lastActionFeedback = (summary.AddedOffsets > 0 || summary.UpdatedOffsets > 0)
                            ? F("Rest-Pose Delta updated: +{0} added, {1} updated.",
                                summary.AddedOffsets, summary.UpdatedOffsets)
                            : T("No non-zero rest-pose angle difference was found for mapped bones.");
                    }
                }

                if (!string.IsNullOrEmpty(_lastActionFeedback))
                {
                    EditorGUILayout.HelpBox(_lastActionFeedback, MessageType.Info);
                }

                EditorGUILayout.Space(2f);
                DrawHumanoidPoseTuningSection();

                EditorGUILayout.Space(2f);
                _showBoneOffsets = DrawSubFoldout(
                    _showBoneOffsets,
                    F("Bone Pose Offsets ({0})", _boneRotationOffsets.arraySize),
                    "BoneOffsets",
                    _boneRotationOffsets);
                if (_showBoneOffsets)
                {
                    DrawGroupDescription(T("Fine-tune local Pitch / Yaw / Roll angles in degrees. Changes update the Preview window live."));
                    _boneOffsetList.DoLayoutList();
                }

                _showBlendShapeMappings = DrawSubFoldout(
                    _showBlendShapeMappings,
                    F("Expression BlendShapes ({0})", _blendShapeNameRemaps.arraySize),
                    "BlendShapeMappings",
                    _blendShapeNameRemaps);
                if (_showBlendShapeMappings)
                {
                    DrawGroupDescription(T("Maps source BlendShapes to target BlendShapes and adjusts expression strength."));
                    _blendShapeList.DoLayoutList();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawHumanoidPoseTuningSection()
        {
            if (_humanoidPoseAdjustment == null) return;
            _showHumanoidPoseTuning = DrawSubFoldout(
                _showHumanoidPoseTuning,
                T("Humanoid Contact & Pose Tuning"),
                "HumanoidPoseTuning",
                _humanoidPoseAdjustment);
            if (!_showHumanoidPoseTuning) return;

            DrawGroupDescription(T("Bakes contact-weighted arm/hand clearance and leg posture into Humanoid curves. Standing start and end poses remain unchanged."));

            var enabledProp = _humanoidPoseAdjustment.FindPropertyRelative("enabled");
            var handOffsetProp = _humanoidPoseAdjustment.FindPropertyRelative("handOffsetInHeadSpace");
            var fullDistProp = _humanoidPoseAdjustment.FindPropertyRelative("contactFullDistance");
            var fadeDistProp = _humanoidPoseAdjustment.FindPropertyRelative("contactFadeDistance");
            var elbowBiasProp = _humanoidPoseAdjustment.FindPropertyRelative("elbowOutwardBias");
            var legCloseProp = _humanoidPoseAdjustment.FindPropertyRelative("legCloseAdjustment");
            var kneeBendProp = _humanoidPoseAdjustment.FindPropertyRelative("kneeBendAdjustment");

            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(enabledProp, new GUIContent(T("Enable Humanoid Pose Tuning")));
                    if (GUILayout.Button(T("Reset"), EditorStyles.miniButton, GUILayout.Width(54f)))
                    {
                        handOffsetProp.vector3Value = Vector3.zero;
                        fullDistProp.floatValue = 0.20f;
                        fadeDistProp.floatValue = 0.30f;
                        elbowBiasProp.floatValue = 0f;
                        legCloseProp.floatValue = 0f;
                        kneeBendProp.floatValue = 0f;
                    }
                }

                using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
                {
                    var offsetCm = handOffsetProp.vector3Value * 100f;
                    var nextX = EditorGUILayout.Slider(new GUIContent(T("Hand Offset X (cm)")), offsetCm.x, -10f, 10f);
                    var nextY = EditorGUILayout.Slider(new GUIContent(T("Hand Offset Y (cm)")), offsetCm.y, -10f, 10f);
                    var nextZ = EditorGUILayout.Slider(new GUIContent(T("Hand Offset Z (cm)")), offsetCm.z, -10f, 10f);
                    if (!Mathf.Approximately(nextX, offsetCm.x)
                        || !Mathf.Approximately(nextY, offsetCm.y)
                        || !Mathf.Approximately(nextZ, offsetCm.z))
                    {
                        handOffsetProp.vector3Value = new Vector3(nextX, nextY, nextZ) * 0.01f;
                    }

                    elbowBiasProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent(T("Elbow Outward Bias (cm)")), elbowBiasProp.floatValue * 100f, -10f, 10f) * 0.01f;
                    legCloseProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent(T("Leg Close Adjustment")), legCloseProp.floatValue, -0.5f, 0.5f);
                    kneeBendProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent(T("Knee Bend Adjustment")), kneeBendProp.floatValue, -0.5f, 0.5f);
                    fullDistProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent(T("Contact Full Distance (m)")), fullDistProp.floatValue, 0.08f, 0.35f);
                    fadeDistProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent(T("Contact Fade Distance (m)")),
                        Mathf.Max(fadeDistProp.floatValue, fullDistProp.floatValue + 0.02f),
                        fullDistProp.floatValue + 0.02f, 0.50f);
                }
            }
        }

        private void DrawAdvancedSection()
        {
            var totalAdvanced = _rendererPathRemaps.arraySize
                + _sourceKeyAdjustments.arraySize
                + _sourceBindingRules.arraySize;
            _showAdvancedSection = BeginPersistentFoldout(
                _showAdvancedSection,
                $"{T("3. Advanced Rules")} ({totalAdvanced})",
                "AdvancedSection",
                _sourceBindingRules);
            if (_showAdvancedSection)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    _showRendererPaths = DrawSubFoldout(
                        _showRendererPaths,
                        F("Hierarchy Mappings ({0})", _rendererPathRemaps.arraySize),
                        "RendererPaths",
                        _rendererPathRemaps);
                    if (_showRendererPaths)
                    {
                        DrawGroupDescription(T("Maps animated objects or bones between different source and target Avatar hierarchies."));
                        _rendererPathList.DoLayoutList();
                    }

                    _showSourceCurveAdjustments = DrawSubFoldout(
                        _showSourceCurveAdjustments,
                        F("Curve Adjustments ({0})", _sourceKeyAdjustments.arraySize),
                        "SourceCurveAdjustments",
                        _sourceKeyAdjustments);
                    if (_showSourceCurveAdjustments)
                    {
                        DrawGroupDescription(T("Adjusts animation curve values before they are copied or mapped."));
                        _sourceKeyAdjustmentList.DoLayoutList();
                    }

                    _showAdditionalBindingRules = DrawSubFoldout(
                        _showAdditionalBindingRules,
                        F("Advanced Binding Mappings ({0})", _sourceBindingRules.arraySize),
                        "AdditionalBindingRules",
                        _sourceBindingRules);
                    if (_showAdditionalBindingRules)
                    {
                        DrawGroupDescription(T("Maps one animation property to an additional target property with optional value adjustment."));
                        _bindingRuleList.DoLayoutList();
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static bool LoadFoldoutState(string key, bool defaultValue)
        {
            return EditorPrefs.GetBool(FoldoutPrefsPrefix + key, defaultValue);
        }

        private bool BeginPersistentFoldout(bool expanded, string label, string key, SerializedProperty property)
        {
            var next = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, label);
            if (next != expanded) EditorPrefs.SetBool(FoldoutPrefsPrefix + key, next);
            ShowGroupContextMenu(GUILayoutUtility.GetLastRect(), property, label);
            return next;
        }

        private bool DrawSubFoldout(bool expanded, string label, string key, SerializedProperty property)
        {
            var next = EditorGUILayout.Foldout(expanded, label, true, EditorStyles.foldoutHeader);
            if (next != expanded) EditorPrefs.SetBool(FoldoutPrefsPrefix + key, next);
            ShowGroupContextMenu(GUILayoutUtility.GetLastRect(), property, label);
            return next;
        }

        private void ShowGroupContextMenu(Rect headerRect, SerializedProperty property, string label)
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.ContextClick || !headerRect.Contains(currentEvent.mousePosition)) return;

            var propertyPath = property.propertyPath;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(T("Copy Property Path")), false,
                () => EditorGUIUtility.systemCopyBuffer = propertyPath);

            if (AfkMotionPatchGroupOperations.TryGetApplyTarget(property, out var prefabPath, out var prefabName))
            {
                menu.AddItem(new GUIContent(F("Apply to Prefab '{0}'", prefabName)), false,
                    () => AfkMotionPatchGroupOperations.Apply(serializedObject, propertyPath, prefabPath));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(T("Apply to Prefab")));
            }

            if (AfkMotionPatchGroupOperations.CanRevert(property))
            {
                menu.AddItem(new GUIContent(T("Revert Group")), false,
                    () => AfkMotionPatchGroupOperations.Revert(serializedObject, propertyPath));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(T("Revert Group")));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(T("Copy Group")), false,
                () => AfkMotionPatchGroupOperations.Copy(serializedObject, propertyPath));
            if (AfkMotionPatchGroupOperations.CanPaste(propertyPath))
            {
                menu.AddItem(new GUIContent(T("Paste Group")), false,
                    () => AfkMotionPatchGroupOperations.Paste(serializedObject, propertyPath, label));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(T("Paste Group")));
            }

            currentEvent.Use();
            menu.ShowAsContext();
        }

        private static void DrawGroupDescription(string text)
        {
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
        }

        private void ShowElementContextMenu(Rect elementRect, SerializedProperty element)
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.ContextClick || !elementRect.Contains(currentEvent.mousePosition)) return;

            var propertyPath = element.propertyPath;
            var menu = new GenericMenu();
            if (AfkMotionPatchGroupOperations.TryGetApplyTarget(element, out var prefabPath, out var prefabName))
            {
                menu.AddItem(new GUIContent(F("Apply to Prefab '{0}'", prefabName)), false,
                    () => AfkMotionPatchGroupOperations.Apply(serializedObject, propertyPath, prefabPath));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(T("Apply to Prefab")));
            }
            if (AfkMotionPatchGroupOperations.CanRevert(element))
            {
                menu.AddItem(new GUIContent(T("Revert Item")), false,
                    () => AfkMotionPatchGroupOperations.Revert(serializedObject, propertyPath));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(T("Revert Item")));
            }
            currentEvent.Use();
            menu.ShowAsContext();
        }

        private ReorderableList CreateBoneOffsetList()
        {
            return new ReorderableList(serializedObject, _boneRotationOffsets, true, false, true, true)
            {
                elementHeight = LineHeight(4),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    var elementRect = rect;
                    var element = _boneRotationOffsets.GetArrayElementAtIndex(index);
                    ShowElementContextMenu(elementRect, element);
                    PrepareElementRect(ref rect);

                    var bonePathProp = element.FindPropertyRelative("bonePath");
                    var eulerProp = element.FindPropertyRelative("eulerOffset");
                    var labelWidth = MeasureLabelWidth(T("Bone"), T("Pitch (X)"), T("Yaw (Y)"), T("Roll (Z)"));

                    // Row 1: Target bone field + Delta + Reset buttons
                    const float btnWidth = 54f;
                    const float btnGap = 3f;
                    var boneRow = new Rect(rect.x, rect.y, rect.width - (btnWidth * 2f + btnGap * 2f), rect.height);
                    var deltaRect = new Rect(boneRow.xMax + btnGap, rect.y, btnWidth, rect.height);
                    var resetRect = new Rect(deltaRect.xMax + btnGap, rect.y, btnWidth, rect.height);

                    DrawTargetTransformFieldInline(boneRow, bonePathProp, T("Bone"), labelWidth);

                    var patch = (AfkMotionPatch)target;
                    var targetAvatar = GetAvatarRoot();
                    using (new EditorGUI.DisabledScope(!targetAvatar || !patch.sourceAvatar || string.IsNullOrEmpty(bonePathProp.stringValue)))
                    {
                        if (GUI.Button(deltaRect, T("Delta"), EditorStyles.miniButton))
                        {
                            if (AfkMotionAutoMatcher.TryComputeRestPoseDeltaForTargetBone(
                                patch, targetAvatar.gameObject, bonePathProp.stringValue, out var delta))
                            {
                                eulerProp.vector3Value = new Vector3(
                                    Mathf.Clamp(Mathf.Round(delta.x * 10f) / 10f, -90f, 90f),
                                    Mathf.Clamp(Mathf.Round(delta.y * 10f) / 10f, -90f, 90f),
                                    Mathf.Clamp(Mathf.Round(delta.z * 10f) / 10f, -90f, 90f));
                            }
                        }
                    }

                    if (GUI.Button(resetRect, T("Reset"), EditorStyles.miniButton))
                    {
                        eulerProp.vector3Value = Vector3.zero;
                    }
                    AdvanceRow(ref rect);

                    // Rows 2-4: Pitch (X), Yaw (Y), Roll (Z) sliders (-90° ~ +90°)
                    var euler = eulerProp.vector3Value;
                    var nextX = DrawAngleSlider(ref rect, T("Pitch (X)"), labelWidth, euler.x);
                    var nextY = DrawAngleSlider(ref rect, T("Yaw (Y)"), labelWidth, euler.y);
                    var nextZ = DrawAngleSlider(ref rect, T("Roll (Z)"), labelWidth, euler.z);
                    if (!Mathf.Approximately(nextX, euler.x)
                        || !Mathf.Approximately(nextY, euler.y)
                        || !Mathf.Approximately(nextZ, euler.z))
                    {
                        eulerProp.vector3Value = new Vector3(nextX, nextY, nextZ);
                    }
                },
                onAddCallback = _ => AddElement(_boneRotationOffsets, element =>
                {
                    element.FindPropertyRelative("bonePath").stringValue = string.Empty;
                    element.FindPropertyRelative("eulerOffset").vector3Value = Vector3.zero;
                })
            };
        }

        private static float DrawAngleSlider(ref Rect rect, string label, float labelWidth, float value)
        {
            var fieldRect = DynamicPrefixLabel(rect, label, labelWidth);
            var next = EditorGUI.Slider(fieldRect, value, -90f, 90f);
            AdvanceRow(ref rect);
            return next;
        }

        private ReorderableList CreateRendererPathList()
        {
            return new ReorderableList(serializedObject, _rendererPathRemaps, true, false, true, true)
            {
                elementHeight = LineHeight(2),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    var elementRect = rect;
                    var element = _rendererPathRemaps.GetArrayElementAtIndex(index);
                    ShowElementContextMenu(elementRect, element);
                    PrepareElementRect(ref rect);
                    var labelWidth = MeasureLabelWidth(T("Source"), T("Target"));
                    DrawSourcePathPicker(ref rect, element.FindPropertyRelative("fromPath"), T("Source"), labelWidth);
                    DrawTargetTransformField(ref rect, element.FindPropertyRelative("toPath"), T("Target"), labelWidth);
                }
            };
        }

        private ReorderableList CreateBlendShapeList()
        {
            return new ReorderableList(serializedObject, _blendShapeNameRemaps, true, false, true, true)
            {
                elementHeightCallback = _ => LineHeight(7),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    var elementRect = rect;
                    var element = _blendShapeNameRemaps.GetArrayElementAtIndex(index);
                    ShowElementContextMenu(elementRect, element);
                    PrepareElementRect(ref rect);
                    var labelWidth = MeasureLabelWidth(T("Source"), T("Target"), T("Strength"), T("Offset"),
                        T("Clamp"), T("Minimum"), T("Maximum"));
                    DrawBlendShapePicker(ref rect, element.FindPropertyRelative("fromName"), T("Source"), true, labelWidth);
                    DrawBlendShapePicker(ref rect, element.FindPropertyRelative("toName"), T("Target"), false, labelWidth);
                    DrawBlendShapeTuningFields(ref rect, element, labelWidth);
                },
                onAddCallback = _ => AddElement(_blendShapeNameRemaps, element =>
                {
                    element.FindPropertyRelative("fromName").stringValue = string.Empty;
                    element.FindPropertyRelative("toName").stringValue = string.Empty;
                    SetTuningDefaults(element);
                })
            };
        }

        private static void DrawBlendShapeTuningFields(ref Rect rect, SerializedProperty element, float labelWidth)
        {
            var scaleProp = element.FindPropertyRelative("scale");
            var fieldRect = DynamicPrefixLabel(rect, T("Strength"), labelWidth);
            scaleProp.floatValue = EditorGUI.Slider(fieldRect, scaleProp.floatValue, 0f, 2f);
            AdvanceRow(ref rect);

            var clamp = element.FindPropertyRelative("clamp");
            DrawFullWidthProperty(ref rect, element.FindPropertyRelative("offset"), T("Offset"), labelWidth);
            DrawFullWidthProperty(ref rect, clamp, T("Clamp"), labelWidth);
            using (new EditorGUI.DisabledScope(!clamp.boolValue))
            {
                DrawFullWidthProperty(ref rect, element.FindPropertyRelative("clampMin"), T("Minimum"), labelWidth);
                DrawFullWidthProperty(ref rect, element.FindPropertyRelative("clampMax"), T("Maximum"), labelWidth);
            }
        }

        private ReorderableList CreateSourceKeyAdjustmentList()
        {
            return new ReorderableList(serializedObject, _sourceKeyAdjustments, true, false, true, true)
            {
                elementHeightCallback = _ => LineHeight(6),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    var elementRect = rect;
                    var element = _sourceKeyAdjustments.GetArrayElementAtIndex(index);
                    ShowElementContextMenu(elementRect, element);
                    PrepareElementRect(ref rect);
                    var labelWidth = MeasureLabelWidth(T("Binding"), T("Scale"), T("Offset"), T("Clamp"),
                        T("Minimum"), T("Maximum"));
                    DrawSourceBindingPicker(ref rect, element, T("Binding"), labelWidth);
                    DrawTuningFields(ref rect, element, labelWidth);
                },
                onAddCallback = _ => AddElement(_sourceKeyAdjustments, element =>
                {
                    element.FindPropertyRelative("sourcePath").stringValue = string.Empty;
                    element.FindPropertyRelative("sourceComponent").enumValueIndex = 0;
                    element.FindPropertyRelative("sourcePropertyName").stringValue = string.Empty;
                    SetTuningDefaults(element);
                })
            };
        }

        private ReorderableList CreateBindingRuleList()
        {
            return new ReorderableList(serializedObject, _sourceBindingRules, true, false, true, true)
            {
                elementHeightCallback = _ => LineHeight(7),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    var elementRect = rect;
                    var element = _sourceBindingRules.GetArrayElementAtIndex(index);
                    ShowElementContextMenu(elementRect, element);
                    PrepareElementRect(ref rect);
                    var labelWidth = MeasureLabelWidth(T("Source"), T("Target"), T("Scale"), T("Offset"),
                        T("Clamp"), T("Minimum"), T("Maximum"));
                    DrawSourceBindingPicker(ref rect, element, T("Source"), labelWidth);
                    DrawTargetBindingPicker(ref rect, element, T("Target"), labelWidth);
                    DrawTuningFields(ref rect, element, labelWidth);
                },
                onAddCallback = _ => AddElement(_sourceBindingRules, element =>
                {
                    element.FindPropertyRelative("sourcePath").stringValue = string.Empty;
                    element.FindPropertyRelative("sourceComponent").enumValueIndex = 0;
                    element.FindPropertyRelative("sourcePropertyName").stringValue = string.Empty;
                    element.FindPropertyRelative("targetPath").stringValue = string.Empty;
                    element.FindPropertyRelative("targetPropertyName").stringValue = string.Empty;
                    SetTuningDefaults(element);
                })
            };
        }

        private void DrawSourcePathPicker(ref Rect rect, SerializedProperty property, string label, float labelWidth)
        {
            var propertyPath = property.propertyPath;
            DrawPickerButton(ref rect, label, labelWidth,
                string.IsNullOrEmpty(property.stringValue) ? T("<Avatar Root>") : property.stringValue,
                T("Select Source Path"), () =>
                {
                    var choices = GetSourceBindings()
                        .Select(binding => binding.path)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .Select(path => new PickerChoice(path, () => SetStringProperty(propertyPath, path)))
                        .ToList();
                    choices.Insert(0, new PickerChoice(T("<Avatar Root>"), () => SetStringProperty(propertyPath, string.Empty)));
                    return choices;
                });
        }

        private void DrawTargetTransformField(ref Rect rect, SerializedProperty property, string label, float labelWidth)
        {
            DrawTargetTransformFieldInline(rect, property, label, labelWidth);
            AdvanceRow(ref rect);
        }

        private void DrawTargetTransformFieldInline(Rect rect, SerializedProperty property, string label, float labelWidth)
        {
            var avatar = GetAvatarRoot();
            var current = avatar
                ? (string.IsNullOrEmpty(property.stringValue) ? null : avatar.Find(property.stringValue))
                : null;
            var fieldRect = DynamicPrefixLabel(rect, label, labelWidth);
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.ObjectField(fieldRect, current, typeof(Transform), true) as Transform;
            if (EditorGUI.EndChangeCheck())
            {
                if (!selected)
                {
                    SetStringProperty(property, string.Empty);
                }
                else if (avatar && (selected == avatar || selected.IsChildOf(avatar)))
                {
                    SetStringProperty(property, AnimationUtility.CalculateTransformPath(selected, avatar));
                }
                else
                {
                    EditorUtility.DisplayDialog(T("Select Target Object"), T("Choose an object under the same avatar."), T("OK"));
                }
            }
            if (!current && !string.IsNullOrEmpty(property.stringValue))
            {
                var oldColor = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.45f, 0.45f);
                EditorGUI.LabelField(fieldRect, property.stringValue);
                GUI.contentColor = oldColor;
            }
        }

        private void DrawBlendShapePicker(ref Rect rect, SerializedProperty property, string label, bool source,
            float labelWidth)
        {
            var propertyPath = property.propertyPath;
            DrawPickerButton(ref rect, label, labelWidth,
                string.IsNullOrEmpty(property.stringValue) ? T("Select...") : property.stringValue,
                source ? T("Select Source BlendShape") : T("Select Target BlendShape"), () =>
                {
                    var names = source ? GetSourceBlendShapes() : GetTargetBlendShapes();
                    var choices = names.Select(name => new PickerChoice(name, () => SetStringProperty(propertyPath, name))).ToList();
                    choices.Insert(0, new PickerChoice(T("(None)"), () => SetStringProperty(propertyPath, string.Empty)));
                    return choices;
                });
        }

        private void DrawSourceBindingPicker(ref Rect rect, SerializedProperty element, string label, float labelWidth)
        {
            var pathProperty = element.FindPropertyRelative("sourcePath");
            var componentProperty = element.FindPropertyRelative("sourceComponent");
            var nameProperty = element.FindPropertyRelative("sourcePropertyName");
            var elementPath = element.propertyPath;
            var display = FormatStoredBinding(pathProperty.stringValue,
                SourceComponentLabel((AfkMotionPatch.SourceBindingComponent)componentProperty.enumValueIndex),
                nameProperty.stringValue);
            DrawPickerButton(ref rect, label, labelWidth, display, T("Select Source Binding"), () =>
            {
                var choices = GetSourceBindings()
                    .Select(binding => new PickerChoice(FormatBinding(binding), () => SetSourceBinding(elementPath, binding)))
                    .ToList();
                choices.Insert(0, new PickerChoice(T("<All Source Bindings>"), () => ClearSourceBinding(elementPath)));
                return choices;
            });
        }

        private void DrawTargetBindingPicker(ref Rect rect, SerializedProperty element, string label, float labelWidth)
        {
            var targetPath = element.FindPropertyRelative("targetPath");
            var targetProperty = element.FindPropertyRelative("targetPropertyName");
            var elementPath = element.propertyPath;
            var sourceType = ResolveStoredSourceType(element);
            var display = string.IsNullOrEmpty(targetPath.stringValue) && string.IsNullOrEmpty(targetProperty.stringValue)
                ? T("Use Source Binding")
                : FormatStoredBinding(targetPath.stringValue, sourceType?.Name ?? T("Binding"), targetProperty.stringValue);
            DrawPickerButton(ref rect, label, labelWidth, display, T("Select Target Binding"), () =>
            {
                var resolvedSourceType = ResolveStoredSourceType(serializedObject.FindProperty(elementPath));
                var choices = resolvedSourceType == null
                    ? new List<PickerChoice>()
                    : GetTargetBindings().Where(binding => binding.type == resolvedSourceType)
                        .Select(binding => new PickerChoice(FormatBinding(binding), () => SetTargetBinding(elementPath, binding)))
                        .ToList();
                choices.Insert(0, new PickerChoice(T("Use Source Binding"), () => ClearTargetBinding(elementPath)));
                return choices;
            });
        }

        private static void DrawPickerButton(ref Rect rect, string label, float labelWidth, string display,
            string title, Func<List<PickerChoice>> choicesFactory)
        {
            var fieldRect = DynamicPrefixLabel(rect, label, labelWidth);
            if (GUI.Button(fieldRect, new GUIContent(display, display), EditorStyles.popup))
            {
                new PickerDropdown(title, choicesFactory()).Show(fieldRect);
            }
            AdvanceRow(ref rect);
        }

        private List<EditorCurveBinding> GetSourceBindings()
        {
            if (_sourceBindingsCache != null) return _sourceBindingsCache;
            if (!AfkMotionSourceGraph.TryExtract((AfkMotionPatch)target, out var graph, out _))
                return _sourceBindingsCache = new List<EditorCurveBinding>();
            return _sourceBindingsCache = graph.EffectiveSourceClips((AfkMotionPatch)target)
                .SelectMany(clip => AnimationUtility.GetCurveBindings(clip)
                    .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                .GroupBy(BindingKey)
                .Select(group => group.First())
                .OrderBy(FormatBinding, StringComparer.Ordinal)
                .ToList();
        }

        private List<EditorCurveBinding> GetTargetBindings()
        {
            if (_targetBindingsCache != null) return _targetBindingsCache;
            var avatar = GetAvatarRoot();
            if (!avatar) return _targetBindingsCache = new List<EditorCurveBinding>();
            return _targetBindingsCache = AnimationUtility.GetAnimatableBindings(avatar.gameObject, avatar.gameObject)
                .GroupBy(BindingKey)
                .Select(group => group.First())
                .OrderBy(FormatBinding, StringComparer.Ordinal)
                .ToList();
        }

        private IEnumerable<string> GetSourceBlendShapes()
        {
            return GetSourceBindings()
                .Where(binding => binding.type == typeof(SkinnedMeshRenderer)
                                  && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                .Select(binding => binding.propertyName.Substring("blendShape.".Length))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal);
        }

        private IEnumerable<string> GetTargetBlendShapes()
        {
            var avatar = GetAvatarRoot();
            if (!avatar) return Enumerable.Empty<string>();
            return avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh)
                .SelectMany(renderer => Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                    .Select(renderer.sharedMesh.GetBlendShapeName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal);
        }

        private Transform GetAvatarRoot()
        {
            return ((AfkMotionPatch)target).GetComponentInParent<VRCAvatarDescriptor>(true)?.transform;
        }

        private Type ResolveStoredSourceType(SerializedProperty element)
        {
            var path = element.FindPropertyRelative("sourcePath").stringValue;
            var property = element.FindPropertyRelative("sourcePropertyName").stringValue;
            var component = (AfkMotionPatch.SourceBindingComponent)element.FindPropertyRelative("sourceComponent").enumValueIndex;
            return GetSourceBindings().FirstOrDefault(binding => binding.path == path
                && binding.propertyName == property
                && (component == AfkMotionPatch.SourceBindingComponent.Any
                    || ToSourceComponent(binding.type) == component)).type;
        }

        private void SetSourceBinding(string elementPath, EditorCurveBinding binding)
        {
            serializedObject.Update();
            var element = serializedObject.FindProperty(elementPath);
            element.FindPropertyRelative("sourcePath").stringValue = binding.path;
            element.FindPropertyRelative("sourceComponent").enumValueIndex = (int)ToSourceComponent(binding.type);
            element.FindPropertyRelative("sourcePropertyName").stringValue = binding.propertyName;
            serializedObject.ApplyModifiedProperties();
            InvalidateBindingCaches();
            AfkMotionPreviewWindow.NotifyPatchChanged((AfkMotionPatch)target);
        }

        private void ClearSourceBinding(string elementPath)
        {
            serializedObject.Update();
            var element = serializedObject.FindProperty(elementPath);
            element.FindPropertyRelative("sourcePath").stringValue = string.Empty;
            element.FindPropertyRelative("sourceComponent").enumValueIndex = 0;
            element.FindPropertyRelative("sourcePropertyName").stringValue = string.Empty;
            serializedObject.ApplyModifiedProperties();
            InvalidateBindingCaches();
            AfkMotionPreviewWindow.NotifyPatchChanged((AfkMotionPatch)target);
        }

        private void SetTargetBinding(string elementPath, EditorCurveBinding binding)
        {
            serializedObject.Update();
            var element = serializedObject.FindProperty(elementPath);
            element.FindPropertyRelative("targetPath").stringValue = binding.path;
            element.FindPropertyRelative("targetPropertyName").stringValue = binding.propertyName;
            serializedObject.ApplyModifiedProperties();
            InvalidateBindingCaches();
            AfkMotionPreviewWindow.NotifyPatchChanged((AfkMotionPatch)target);
        }

        private void ClearTargetBinding(string elementPath)
        {
            serializedObject.Update();
            var element = serializedObject.FindProperty(elementPath);
            element.FindPropertyRelative("targetPath").stringValue = string.Empty;
            element.FindPropertyRelative("targetPropertyName").stringValue = string.Empty;
            serializedObject.ApplyModifiedProperties();
            InvalidateBindingCaches();
            AfkMotionPreviewWindow.NotifyPatchChanged((AfkMotionPatch)target);
        }

        private void SetStringProperty(SerializedProperty property, string value)
        {
            SetStringProperty(property.propertyPath, value);
        }

        private void SetStringProperty(string propertyPath, string value)
        {
            serializedObject.Update();
            serializedObject.FindProperty(propertyPath).stringValue = value;
            serializedObject.ApplyModifiedProperties();
            InvalidateBindingCaches();
            AfkMotionPreviewWindow.NotifyPatchChanged((AfkMotionPatch)target);
        }

        private static string SourceComponentLabel(AfkMotionPatch.SourceBindingComponent component)
        {
            switch (component)
            {
                case AfkMotionPatch.SourceBindingComponent.Transform:
                    return T("Transform");
                case AfkMotionPatch.SourceBindingComponent.SkinnedMeshRenderer:
                    return T("Skinned Mesh Renderer");
                default:
                    return T("Any Component");
            }
        }

        private static AfkMotionPatch.SourceBindingComponent ToSourceComponent(Type type)
        {
            if (type == typeof(Transform)) return AfkMotionPatch.SourceBindingComponent.Transform;
            if (type == typeof(SkinnedMeshRenderer)) return AfkMotionPatch.SourceBindingComponent.SkinnedMeshRenderer;
            return AfkMotionPatch.SourceBindingComponent.Any;
        }

        private static string BindingKey(EditorCurveBinding binding)
        {
            return binding.path + "\n" + binding.type?.AssemblyQualifiedName + "\n" + binding.propertyName;
        }

        private static string FormatBinding(EditorCurveBinding binding)
        {
            return FormatStoredBinding(binding.path, binding.type?.Name ?? T("Binding"), binding.propertyName);
        }

        private static string FormatStoredBinding(string path, string component, string property)
        {
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(property)) return T("<All Source Bindings>");
            return (string.IsNullOrEmpty(path) ? T("<Avatar Root>") : path) + "  •  " + component + "  •  " + property;
        }

        private static void AdvanceRow(ref Rect rect)
        {
            rect.y += EditorGUIUtility.singleLineHeight + RowGap;
        }

        private sealed class PickerChoice
        {
            public readonly string Label;
            public readonly Action Select;

            public PickerChoice(string label, Action select)
            {
                Label = label;
                Select = select;
            }
        }

        private sealed class PickerDropdown : AdvancedDropdown
        {
            private readonly string _title;
            private readonly List<PickerChoice> _choices;

            public PickerDropdown(string title, List<PickerChoice> choices) : base(new AdvancedDropdownState())
            {
                _title = title;
                _choices = choices;
                minimumSize = new Vector2(420f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(_title);
                foreach (var choice in _choices)
                {
                    root.AddChild(new ChoiceItem(choice));
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is ChoiceItem choice) choice.Value.Select?.Invoke();
            }

            private sealed class ChoiceItem : AdvancedDropdownItem
            {
                public readonly PickerChoice Value;

                public ChoiceItem(PickerChoice value) : base(value.Label)
                {
                    Value = value;
                }
            }
        }

        private static void AddElement(SerializedProperty list, System.Action<SerializedProperty> initialize)
        {
            var index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            initialize(list.GetArrayElementAtIndex(index));
        }

        private static void DrawTuningFields(ref Rect rect, SerializedProperty element, float labelWidth)
        {
            var clamp = element.FindPropertyRelative("clamp");
            DrawFullWidthProperty(ref rect, element.FindPropertyRelative("scale"), T("Scale"), labelWidth);
            DrawFullWidthProperty(ref rect, element.FindPropertyRelative("offset"), T("Offset"), labelWidth);
            DrawFullWidthProperty(ref rect, clamp, T("Clamp"), labelWidth);
            using (new EditorGUI.DisabledScope(!clamp.boolValue))
            {
                DrawFullWidthProperty(ref rect, element.FindPropertyRelative("clampMin"), T("Minimum"), labelWidth);
                DrawFullWidthProperty(ref rect, element.FindPropertyRelative("clampMax"), T("Maximum"), labelWidth);
            }
        }

        private static float LineHeight(int lineCount)
        {
            return ElementPadding * 2f + lineCount * EditorGUIUtility.singleLineHeight + Mathf.Max(0, lineCount - 1) * RowGap;
        }

        private static void PrepareElementRect(ref Rect rect)
        {
            rect.y += ElementPadding;
            rect.height = EditorGUIUtility.singleLineHeight;
        }

        private static float MeasureLabelWidth(params string[] labels)
        {
            return Mathf.Ceil(labels.Max(label => EditorStyles.label.CalcSize(new GUIContent(label)).x));
        }

        private static Rect DynamicPrefixLabel(Rect rect, string label, float measuredLabelWidth)
        {
            var indentedRect = EditorGUI.IndentedRect(rect);
            var content = new GUIContent(label, label);
            var labelWidth = Mathf.Min(measuredLabelWidth, indentedRect.width * MaxDynamicLabelWidthRatio);
            var labelRect = new Rect(indentedRect.x, indentedRect.y, labelWidth, indentedRect.height);
            EditorGUI.LabelField(labelRect, content);
            return new Rect(labelRect.xMax + DynamicLabelGap, indentedRect.y,
                Mathf.Max(0f, indentedRect.xMax - labelRect.xMax - DynamicLabelGap), indentedRect.height);
        }

        private static void DrawFullWidthProperty(ref Rect rect, SerializedProperty property, string label,
            float labelWidth)
        {
            var indentedRect = EditorGUI.IndentedRect(rect);
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(labelWidth, indentedRect.width * MaxDynamicLabelWidthRatio);
            try
            {
                EditorGUI.PropertyField(indentedRect, property, new GUIContent(label, label));
            }
            finally
            {
                EditorGUIUtility.labelWidth = oldLabelWidth;
            }
            rect.y += EditorGUIUtility.singleLineHeight + RowGap;
        }

        private static void SetTuningDefaults(SerializedProperty element)
        {
            element.FindPropertyRelative("scale").floatValue = 1f;
            element.FindPropertyRelative("offset").floatValue = 0f;
            element.FindPropertyRelative("clamp").boolValue = false;
            element.FindPropertyRelative("clampMin").floatValue = 0f;
            element.FindPropertyRelative("clampMax").floatValue = 100f;
        }
    }

    [InitializeOnLoad]
    internal static class AfkMotionPatchGroupOperations
    {
        private static GameObject _clipboardObject;
        private static AfkMotionPatch _clipboardPatch;
        private static string _clipboardPropertyPath;

        static AfkMotionPatchGroupOperations()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearClipboard;
            EditorApplication.quitting += ClearClipboard;
        }

        public static void Copy(SerializedObject sourceObject, string propertyPath)
        {
            sourceObject.Update();
            var source = sourceObject.FindProperty(propertyPath);
            if (source == null) return;

            EnsureClipboard();
            var clipboardObject = new SerializedObject(_clipboardPatch);
            clipboardObject.Update();
            clipboardObject.CopyFromSerializedProperty(source);
            clipboardObject.ApplyModifiedPropertiesWithoutUndo();
            _clipboardPropertyPath = propertyPath;
        }

        public static bool CanPaste(string propertyPath)
        {
            return _clipboardPatch && _clipboardPropertyPath == propertyPath;
        }

        public static void Paste(SerializedObject targetObject, string propertyPath, string label)
        {
            if (!CanPaste(propertyPath)) return;
            var clipboardObject = new SerializedObject(_clipboardPatch);
            clipboardObject.Update();
            var source = clipboardObject.FindProperty(propertyPath);
            if (source == null) return;

            targetObject.Update();
            Undo.RegisterCompleteObjectUndo(targetObject.targetObjects, $"Paste {label}");
            targetObject.CopyFromSerializedProperty(source);
            targetObject.ApplyModifiedProperties();
        }

        public static bool CanRevert(SerializedProperty property)
        {
            return property != null && property.isInstantiatedPrefab
                && GetGroupOverridePaths(property.serializedObject, property.propertyPath).Count > 0;
        }

        public static void Revert(SerializedObject targetObject, string propertyPath)
        {
            targetObject.Update();
            var overridePaths = GetGroupOverridePaths(targetObject, propertyPath)
                .OrderBy(path => path.EndsWith(".Array.size", StringComparison.Ordinal) ? 1 : 0)
                .ThenByDescending(path => path.Length)
                .ToArray();
            if (overridePaths.Length == 0) return;

            Undo.SetCurrentGroupName($"Revert {propertyPath}");
            foreach (var overridePath in overridePaths)
            {
                targetObject.Update();
                var property = targetObject.FindProperty(overridePath);
                if (property != null && property.prefabOverride)
                    PrefabUtility.RevertPropertyOverride(property, InteractionMode.UserAction);
            }
            targetObject.Update();
        }

        public static bool TryGetApplyTarget(SerializedProperty property,
            out string prefabPath, out string prefabName)
        {
            prefabPath = null;
            prefabName = null;
            if (!CanRevert(property) || EditorUtility.IsPersistent(property.serializedObject.targetObject)) return false;
            if (!(property.serializedObject.targetObject is Component component)) return false;

            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(component.gameObject);
            if (!instanceRoot || !PrefabUtility.IsPartOfPrefabThatCanBeAppliedTo(instanceRoot)) return false;
            prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(component.gameObject);
            if (string.IsNullOrEmpty(prefabPath)) return false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            prefabName = prefab ? prefab.name : instanceRoot.name;
            return true;
        }

        public static void Apply(SerializedObject targetObject, string propertyPath, string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return;
            targetObject.Update();
            var overridePaths = GetGroupOverridePaths(targetObject, propertyPath)
                .OrderBy(path => path.EndsWith(".Array.size", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(path => path.Length)
                .ToArray();
            foreach (var overridePath in overridePaths)
            {
                targetObject.Update();
                var property = targetObject.FindProperty(overridePath);
                if (property != null && property.prefabOverride)
                    PrefabUtility.ApplyPropertyOverride(property, prefabPath, InteractionMode.UserAction);
            }
            targetObject.Update();
        }

        private static IReadOnlyList<string> GetGroupOverridePaths(SerializedObject serializedObject,
            string propertyPath)
        {
            if (!(serializedObject.targetObject is Component component)) return Array.Empty<string>();
            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(component.gameObject);
            if (!instanceRoot) return Array.Empty<string>();
            var sourceComponent = PrefabUtility.GetCorrespondingObjectFromSource(component);
            if (!sourceComponent) return Array.Empty<string>();
            var modifications = PrefabUtility.GetPropertyModifications(instanceRoot);
            if (modifications == null) return Array.Empty<string>();
            var childPrefix = propertyPath + ".";
            return modifications
                .Where(modification => modification.target == sourceComponent)
                .Select(modification => modification.propertyPath)
                .Where(path => path == propertyPath || path.StartsWith(childPrefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static void EnsureClipboard()
        {
            if (_clipboardPatch) return;
            _clipboardObject = new GameObject("AfkMotionPatch Group Clipboard")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _clipboardPatch = _clipboardObject.AddComponent<AfkMotionPatch>();
            _clipboardPatch.hideFlags = HideFlags.HideAndDontSave;
        }

        private static void ClearClipboard()
        {
            if (_clipboardObject) UnityEngine.Object.DestroyImmediate(_clipboardObject);
            _clipboardObject = null;
            _clipboardPatch = null;
            _clipboardPropertyPath = null;
        }
    }
}
#endif
