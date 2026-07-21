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
        private SerializedProperty _rendererPathRemaps;
        private SerializedProperty _blendShapeNameRemaps;
        private SerializedProperty _sourceKeyAdjustments;
        private SerializedProperty _sourceBindingRules;

        private ReorderableList _rendererPathList;
        private ReorderableList _blendShapeList;
        private ReorderableList _sourceKeyAdjustmentList;
        private ReorderableList _bindingRuleList;
        private List<EditorCurveBinding> _sourceBindingsCache;
        private List<EditorCurveBinding> _targetBindingsCache;

        private bool _showSourceMotions;
        private bool _showRendererPaths;
        private bool _showBlendShapeMappings;
        private bool _showSourceCurveAdjustments;
        private bool _showAdditionalBindingRules;

        private const string FoldoutPrefsPrefix = "THE_cattail.AfkMotionPatcher.Inspector.";
        private const float RowGap = 3f;
        private const float ElementPadding = 4f;
        private const float DynamicLabelGap = 4f;
        private const float MaxDynamicLabelWidthRatio = 0.4f;

        private void OnEnable()
        {
            _sourceAvatar = serializedObject.FindProperty(nameof(AfkMotionPatch.sourceAvatar));
            _rendererPathRemaps = serializedObject.FindProperty(nameof(AfkMotionPatch.rendererPathRemaps));
            _blendShapeNameRemaps = serializedObject.FindProperty(nameof(AfkMotionPatch.blendShapeNameRemaps));
            _sourceKeyAdjustments = serializedObject.FindProperty(nameof(AfkMotionPatch.sourceKeyAdjustments));
            _sourceBindingRules = serializedObject.FindProperty(nameof(AfkMotionPatch.sourceBindingRules));

            _rendererPathList = CreateRendererPathList();
            _blendShapeList = CreateBlendShapeList();
            _sourceKeyAdjustmentList = CreateSourceKeyAdjustmentList();
            _bindingRuleList = CreateBindingRuleList();

            _showSourceMotions = LoadFoldoutState("SourceAfk", true);
            _showRendererPaths = LoadFoldoutState("RendererPaths", false);
            _showBlendShapeMappings = LoadFoldoutState("BlendShapeMappings", false);
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
            EditorGUIUtility.labelWidth = Mathf.Min(96f, EditorGUIUtility.currentViewWidth * 0.32f);
            try
            {
                DrawLanguageSwitcher();
                EditorGUILayout.Space(2f);
                DrawStatus(patch);
                DrawPrimaryActions(patch);
                DrawSourceAfk();
                DrawRendererPaths();
                DrawBlendShapeMappings();
                DrawSourceCurveAdjustments();
                DrawAdditionalBindingRules();
                if (serializedObject.ApplyModifiedProperties()) InvalidateBindingCaches();
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
            }
            else if (avatar.GetComponentsInChildren<AfkMotionPatch>(true)
                         .Count(candidate => candidate.enabled && candidate.gameObject.activeInHierarchy) > 1)
            {
                EditorGUILayout.HelpBox(T("Multiple active AFK Motion Patch components were found under this Avatar. Keep exactly one enabled component."), MessageType.Error);
            }
            else if (!_sourceAvatar.objectReferenceValue)
            {
                EditorGUILayout.HelpBox(T("Source Avatar is missing. Assign the Avatar prefab whose AFK motion is being adapted."), MessageType.Error);
            }
            else if (!AfkMotionSourceGraph.TryExtract(patch, out var sourceGraph, out var graphError))
            {
                EditorGUILayout.HelpBox(F("AFK source graph error: {0}", graphError), MessageType.Error);
            }
            else if (!sourceGraph.ValidateStateMotions(patch, out var motionError))
            {
                EditorGUILayout.HelpBox(F("The adapter preset's internal source Motion data is invalid. Update or reimport the preset. {0}", motionError), MessageType.Error);
            }
            else if (!sourceGraph.EffectiveSourceClips(patch).Any())
            {
                EditorGUILayout.HelpBox(T("The AFK source graph and adapter preset provide no usable Motion. Update or reimport the source package and preset."), MessageType.Error);
            }
        }

        private static void DrawPrimaryActions(AfkMotionPatch patch)
        {
            if (GUILayout.Button(T("Open Preview"), GUILayout.Height(28f)))
            {
                AfkMotionPreviewWindow.Open(patch);
            }
        }

        private void DrawSourceAfk()
        {
            _showSourceMotions = BeginPersistentFoldout(
                _showSourceMotions, T("AFK Source"), "SourceAfk", _sourceAvatar);
            if (_showSourceMotions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_sourceAvatar, new GUIContent(T("Avatar")));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRendererPaths()
        {
            _showRendererPaths = BeginPersistentFoldout(
                _showRendererPaths, F("Hierarchy Mappings ({0})", _rendererPathRemaps.arraySize), "RendererPaths", _rendererPathRemaps);
            if (_showRendererPaths)
            {
                DrawGroupDescription(T("Maps animated objects or bones between different source and target Avatar hierarchies."));
                _rendererPathList.DoLayoutList();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBlendShapeMappings()
        {
            _showBlendShapeMappings = BeginPersistentFoldout(
                _showBlendShapeMappings, F("BlendShape Mappings ({0})", _blendShapeNameRemaps.arraySize), "BlendShapeMappings", _blendShapeNameRemaps);
            if (_showBlendShapeMappings)
            {
                DrawGroupDescription(T("Maps a source BlendShape to the corresponding BlendShape on the target avatar."));
                _blendShapeList.DoLayoutList();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSourceCurveAdjustments()
        {
            _showSourceCurveAdjustments = BeginPersistentFoldout(
                _showSourceCurveAdjustments, F("Curve Adjustments ({0})", _sourceKeyAdjustments.arraySize),
                "SourceCurveAdjustments", _sourceKeyAdjustments);
            if (_showSourceCurveAdjustments)
            {
                DrawGroupDescription(T("Adjusts animation curve values before they are copied or mapped."));
                _sourceKeyAdjustmentList.DoLayoutList();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAdditionalBindingRules()
        {
            _showAdditionalBindingRules = BeginPersistentFoldout(
                _showAdditionalBindingRules, F("Advanced Binding Mappings ({0})", _sourceBindingRules.arraySize),
                "AdditionalBindingRules", _sourceBindingRules);
            if (_showAdditionalBindingRules)
            {
                DrawGroupDescription(T("Maps one animation property to an additional target property with optional value adjustment."));
                _bindingRuleList.DoLayoutList();
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
                    var labelWidth = MeasureLabelWidth(T("Source"), T("Target"), T("Scale"), T("Offset"),
                        T("Clamp"), T("Minimum"), T("Maximum"));
                    DrawBlendShapePicker(ref rect, element.FindPropertyRelative("fromName"), T("Source"), true, labelWidth);
                    DrawBlendShapePicker(ref rect, element.FindPropertyRelative("toName"), T("Target"), false, labelWidth);
                    DrawTuningFields(ref rect, element, labelWidth);
                },
                onAddCallback = _ => AddElement(_blendShapeNameRemaps, element =>
                {
                    element.FindPropertyRelative("fromName").stringValue = string.Empty;
                    element.FindPropertyRelative("toName").stringValue = string.Empty;
                    SetTuningDefaults(element);
                })
            };
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
            var avatar = GetAvatarRoot();
            var current = avatar
                ? (string.IsNullOrEmpty(property.stringValue) ? avatar : avatar.Find(property.stringValue))
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
            AdvanceRow(ref rect);
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
        }

        private void SetTargetBinding(string elementPath, EditorCurveBinding binding)
        {
            serializedObject.Update();
            var element = serializedObject.FindProperty(elementPath);
            element.FindPropertyRelative("targetPath").stringValue = binding.path;
            element.FindPropertyRelative("targetPropertyName").stringValue = binding.propertyName;
            serializedObject.ApplyModifiedProperties();
            InvalidateBindingCaches();
        }

        private void ClearTargetBinding(string elementPath)
        {
            serializedObject.Update();
            var element = serializedObject.FindProperty(elementPath);
            element.FindPropertyRelative("targetPath").stringValue = string.Empty;
            element.FindPropertyRelative("targetPropertyName").stringValue = string.Empty;
            serializedObject.ApplyModifiedProperties();
            InvalidateBindingCaches();
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
