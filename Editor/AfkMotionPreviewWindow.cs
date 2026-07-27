#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;
using UnityEditor;
using static THE_cattail.AfkMotionPatcher.AfkMotionPatchLocalization;
using UnityEditor.Animations;
using UnityEngine;

namespace THE_cattail.AfkMotionPatcher
{
    public sealed class AfkMotionPreviewWindow : EditorWindow
    {
        private const float DefaultFrameRate = 60f;
        private const float PaneToolbarHeight = 20f;
        private const float PlaybackRightColumnWidth = 112f;
        private static GUIStyle _previewBackground;
        private static GUIStyle PreviewBackground => _previewBackground ?? GUIStyle.none;

        private static readonly string[] PlaybackSpeedLabels = { "0.25×", "0.5×", "1×", "1.5×", "2×", "4×" };
        private static readonly float[] PlaybackSpeedValues = { 0.25f, 0.5f, 1f, 1.5f, 2f, 4f };

        private GameObject _sourcePreviewRoot;
        private GameObject _targetPreviewRoot;
        private AfkMotionPatch _sourcePatch;
        private AnimationClip _sourceClip;
        private AnimationClip _previewClip;
        private IReadOnlyList<AnimationClip> _sourceClips = Array.Empty<AnimationClip>();
        private IReadOnlyList<AnimationClip> _previewClips = Array.Empty<AnimationClip>();
        private AfkMotionSourceGraph _sourceGraph;
        private IReadOnlyList<AfkMotionSourceGraph.PreviewStep> _previewSteps = Array.Empty<AfkMotionSourceGraph.PreviewStep>();
        private int _stepIndex;
        private bool _isPlaying;
        private bool _loopCurrentClip;
        private double _lastUpdateTime;
        private float _time;
        private float _playbackSpeed = 1f;
        private int _patchStateHash;
        private bool _forcePaneContentRefresh;
        private GUIStyle _rightAlignedMiniLabel;

        private PreviewPane _sourcePane;
        private PreviewPane _targetPane;

        public static void Open(AfkMotionPatch patch)
        {
            var wasOpen = HasOpenInstances<AfkMotionPreviewWindow>();
            var window = wasOpen
                ? GetWindow<AfkMotionPreviewWindow>(T("AFK Motion Preview"))
                : CreateInstance<AfkMotionPreviewWindow>();
            window.titleContent = new GUIContent(T("AFK Motion Preview"));
            window.minSize = new Vector2(520f, 500f);
            if (patch) window.LoadFromPatch(patch);
            if (!wasOpen) window.Show();
            window.Focus();
            window._time = 0f;
            window._isPlaying = patch;
            window._lastUpdateTime = EditorApplication.timeSinceStartup;
            window.Repaint();
        }

        private void OnEnable()
        {
            _sourcePane = new PreviewPane("Source");
            _targetPane = new PreviewPane("Result");
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += OnProjectChanged;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            UpdateLocalizedWindow();
            LanguagePrefs.RegisterLanguageChangeCallback(this, window =>
            {
                window.UpdateLocalizedWindow();
                window.Repaint();
            });
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.projectChanged -= OnProjectChanged;
            Undo.undoRedoPerformed -= OnProjectChanged;
            DisposePreviewClip();
            _sourcePane?.Dispose();
            _targetPane?.Dispose();
        }

        private void UpdateLocalizedWindow()
        {
            titleContent = new GUIContent(T("AFK Motion Preview"));
        }

        private void OnInspectorUpdate()
        {
            if (!_sourcePatch || EditorApplication.isCompiling) return;
            var hash = ComputePatchStateHash(_sourcePatch);
            if (!_previewClip || hash != _patchStateHash) RebuildPreviewClip();
        }

        private void OnProjectChanged()
        {
            _forcePaneContentRefresh = true;
            if (_sourcePatch) RebuildPreviewClip();
        }

        private void OnGUI()
        {
            if (_previewBackground == null) _previewBackground = new GUIStyle("PreBackground");
            if (_rightAlignedMiniLabel == null)
            {
                _rightAlignedMiniLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            }
            DrawClipSelector();
            DrawPlayback();
            DrawPreviewPanes();
        }

        private void DrawClipSelector()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                for (var index = 0; index < _previewSteps.Count; index++)
                {
                    DrawClipToggle(index, _previewSteps[index].Label);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawClipToggle(int index, string label)
        {
            var selected = _stepIndex == index;
            if (GUILayout.Toggle(selected, label, EditorStyles.toolbarButton, GUILayout.MinWidth(72f), GUILayout.MaxWidth(140f)) && !selected)
            {
                SelectPreviewStep(index);
                _time = 0f;
                _lastUpdateTime = EditorApplication.timeSinceStartup;
            }
        }

        private void DrawPlayback()
        {
            var maxTime = GetMaxTime();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(_isPlaying ? T("Pause") : T("Play"), GUILayout.Width(78f)))
                    {
                        if (!_isPlaying && _time >= maxTime - 0.0001f)
                        {
                            _time = 0f;
                            if (!_loopCurrentClip && _stepIndex >= _previewSteps.Count - 1)
                            {
                                SelectPreviewStep(0);
                                maxTime = GetMaxTime();
                            }
                        }
                        _isPlaying = !_isPlaying;
                        _lastUpdateTime = EditorApplication.timeSinceStartup;
                    }
                    if (GUILayout.Button(T("Stop"), GUILayout.Width(78f)))
                    {
                        _isPlaying = false;
                        _time = 0f;
                    }
                    if (GUILayout.Button("<", GUILayout.Width(30f))) StepFrame(-1f / DefaultFrameRate, maxTime);
                    if (GUILayout.Button(">", GUILayout.Width(30f))) StepFrame(1f / DefaultFrameRate, maxTime);
                    _loopCurrentClip = EditorGUILayout.ToggleLeft(T("Loop"), _loopCurrentClip, GUILayout.Width(54f));
                    if (GUILayout.Button(T("Reset View"), GUILayout.Width(88f)))
                    {
                        _sourcePane.ResetView();
                        _targetPane.ResetView();
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        FormatTime(_time) + " / " + FormatTime(maxTime),
                        _rightAlignedMiniLabel, GUILayout.Width(PlaybackRightColumnWidth));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var sliderRect = GUILayoutUtility.GetRect(40f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                    EditorGUI.BeginChangeCheck();
                    var newTime = GUI.HorizontalSlider(sliderRect, Mathf.Clamp(_time, 0f, maxTime), 0f, maxTime);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _time = newTime;
                        _isPlaying = false;
                    }
                    var speedIndex = ClosestSpeedIndex(_playbackSpeed);
                    var nextSpeedIndex = EditorGUILayout.Popup(
                        speedIndex, PlaybackSpeedLabels, GUILayout.Width(PlaybackRightColumnWidth));
                    _playbackSpeed = PlaybackSpeedValues[nextSpeedIndex];
                }
            }
        }

        private static int ClosestSpeedIndex(float speed)
        {
            var best = 0;
            var bestDistance = Mathf.Abs(speed - PlaybackSpeedValues[0]);
            for (var i = 1; i < PlaybackSpeedValues.Length; i++)
            {
                var distance = Mathf.Abs(speed - PlaybackSpeedValues[i]);
                if (distance >= bestDistance) continue;
                best = i;
                bestDistance = distance;
            }
            return best;
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            var minutes = Mathf.FloorToInt(seconds / 60f);
            var remainder = seconds - minutes * 60f;
            return $"{minutes}:{remainder:00.00}";
        }

        private void DrawPreviewPanes()
        {
            const float gap = 6f;
            var rect = GUILayoutUtility.GetRect(
                10f, 10000f, 100f, 10000f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var transitionDuration = GetTransitionDuration();
            var transitionElapsed = Mathf.Clamp(_time - GetStateDuration(), 0f, transitionDuration);
            var transitionProgress = transitionDuration > 0.0001f
                ? transitionElapsed / transitionDuration
                : 0f;
            var nextIndex = _stepIndex + 1;
            var nextSourceClip = transitionDuration > 0f && nextIndex < _sourceClips.Count
                ? _sourceClips[nextIndex]
                : null;
            var nextPreviewClip = transitionDuration > 0f && nextIndex < _previewClips.Count
                ? _previewClips[nextIndex]
                : null;
            var transition = GetCurrentTransition();
            var nextSourceTime = nextSourceClip && transition
                ? transition.offset * nextSourceClip.length + transitionElapsed
                : 0f;
            var nextPreviewTime = nextPreviewClip && transition
                ? transition.offset * nextPreviewClip.length + transitionElapsed
                : 0f;

            if (position.width >= 700f)
            {
                var half = (rect.width - gap) * 0.5f;
                var left = new Rect(rect.x, rect.y, half, rect.height);
                var right = new Rect(rect.x + half + gap, rect.y, half, rect.height);
                _sourcePane.Draw(left, _sourcePreviewRoot, _sourceClip, _time, true,
                    nextSourceClip, nextSourceTime, transitionDuration, transitionProgress);
                _targetPane.CopyCameraFrom(_sourcePane);
                _targetPane.Draw(right, _targetPreviewRoot, _previewClip, _time, true,
                    nextPreviewClip, nextPreviewTime, transitionDuration, transitionProgress);
            }
            else
            {
                var half = (rect.height - gap) * 0.5f;
                var top = new Rect(rect.x, rect.y, rect.width, half);
                var bottom = new Rect(rect.x, rect.y + half + gap, rect.width, half);
                _sourcePane.Draw(top, _sourcePreviewRoot, _sourceClip, _time, true,
                    nextSourceClip, nextSourceTime, transitionDuration, transitionProgress);
                _targetPane.CopyCameraFrom(_sourcePane);
                _targetPane.Draw(bottom, _targetPreviewRoot, _previewClip, _time, true,
                    nextPreviewClip, nextPreviewTime, transitionDuration, transitionProgress);
            }
            _sourcePane.CopyCameraFrom(_targetPane);
        }

        private void LoadFromPatch(AfkMotionPatch patch)
        {
            _isPlaying = false;
            _sourcePatch = patch;
            _stepIndex = 0;
            _time = 0f;
            _loopCurrentClip = false;
            RebuildPreviewClip();
            _time = 0f;
        }

        private void ResolvePreviewInputsFromPatch()
        {
            _targetPreviewRoot = _sourcePatch
                ? AfkMotionPatchEditorUtil.FindAvatarRoot(_sourcePatch.gameObject)
                : null;
            _sourcePreviewRoot = _sourcePatch ? _sourcePatch.sourceAvatar : null;
            _sourceGraph = null;
            _previewSteps = Array.Empty<AfkMotionSourceGraph.PreviewStep>();
            _sourceClips = Array.Empty<AnimationClip>();
            _sourceClip = null;
            if (!_sourcePatch || !AfkMotionSourceGraph.TryExtract(
                    _sourcePatch, out _sourceGraph, out _)) return;
            _previewSteps = _sourceGraph.PreviewSteps
                .Where(step => step.NormalizedDuration > 0.0001f
                    && _sourceGraph.ResolvePreviewClip(_sourcePatch, step.State))
                .ToList();
            _sourceClips = _previewSteps
                .Select(step => _sourceGraph.ResolvePreviewClip(_sourcePatch, step.State))
                .ToList();
            _stepIndex = Mathf.Clamp(_stepIndex, 0, Mathf.Max(0, _previewSteps.Count - 1));
            if (_sourceClips.Count > 0) _sourceClip = _sourceClips[_stepIndex];
        }

        private void RebuildPreviewClip()
        {
            DisposePreviewClip();
            ResolvePreviewInputsFromPatch();
            _patchStateHash = ComputePatchStateHash(_sourcePatch);

            if (_sourcePatch && _targetPreviewRoot && _previewSteps.Count > 0)
            {
                // Retarget the complete route once so playback never rebuilds clips between states.
                var clips = new List<AnimationClip>(_previewSteps.Count);
                var visited = new HashSet<AnimationClip>();
                for (var index = 0; index < _previewSteps.Count; index++)
                {
                    var step = _previewSteps[index];
                    var previewClip = AfkMotionPatchEditorUtil.CreatePreviewClip(
                        null, _sourceClips[index], _targetPreviewRoot, _sourcePatch, step.Label,
                        out _, out _);
                    if (previewClip) SetPreviewClipGraphHideFlags(previewClip, visited);
                    clips.Add(previewClip);
                }
                _previewClips = clips;
            }

            UpdateCurrentPreviewClips();
            RebuildPreviewObjects();
        }

        private void SelectPreviewStep(int index)
        {
            _stepIndex = Mathf.Clamp(index, 0, Mathf.Max(0, _previewSteps.Count - 1));
            UpdateCurrentPreviewClips();
            RebuildPreviewObjects();
        }

        private void UpdateCurrentPreviewClips()
        {
            _sourceClip = _stepIndex >= 0 && _stepIndex < _sourceClips.Count
                ? _sourceClips[_stepIndex]
                : null;
            _previewClip = _stepIndex >= 0 && _stepIndex < _previewClips.Count
                ? _previewClips[_stepIndex]
                : null;
        }

        private void RebuildPreviewObjects()
        {
            _sourcePane.SetContent(_sourcePreviewRoot, false, _sourceClip, _sourceClips, _forcePaneContentRefresh);
            _targetPane.SetContent(_targetPreviewRoot, true, _previewClip, _previewClips, _forcePaneContentRefresh);
            _forcePaneContentRefresh = false;
            Repaint();
        }

        private void DisposePreviewClip()
        {
            var visited = new HashSet<AnimationClip>();
            foreach (var previewClip in _previewClips)
                DestroyPreviewClipGraph(previewClip, visited);
            _previewClips = Array.Empty<AnimationClip>();
            _previewClip = null;
        }

        private static void SetPreviewClipGraphHideFlags(AnimationClip clip, ISet<AnimationClip> visited)
        {
            if (!clip || !visited.Add(clip)) return;
            clip.hideFlags = HideFlags.HideAndDontSave;
            var reference = AnimationUtility.GetAnimationClipSettings(clip).additiveReferencePoseClip;
            SetPreviewClipGraphHideFlags(reference, visited);
        }

        private static void DestroyPreviewClipGraph(AnimationClip clip, ISet<AnimationClip> visited)
        {
            if (!clip || !visited.Add(clip)) return;
            var reference = AnimationUtility.GetAnimationClipSettings(clip).additiveReferencePoseClip;
            DestroyPreviewClipGraph(reference, visited);
            DestroyImmediate(clip);
        }

        private int ComputePatchStateHash(AfkMotionPatch patch)
        {
            return patch ? EditorJsonUtility.ToJson(patch).GetHashCode() : 0;
        }

        private float GetMaxTime()
        {
            return Mathf.Max(0.01f, GetStateDuration() + GetTransitionDuration());
        }

        private float GetStateDuration()
        {
            var clipLength = Mathf.Max(_sourceClip ? _sourceClip.length : 0f, _previewClip ? _previewClip.length : 0f);
            var normalizedDuration = _previewSteps.Count > 0
                ? Mathf.Max(0f, _previewSteps[Mathf.Clamp(_stepIndex, 0, _previewSteps.Count - 1)].NormalizedDuration)
                : 1f;
            return clipLength * normalizedDuration;
        }

        private AnimatorStateTransition GetCurrentTransition()
        {
            if (_loopCurrentClip || _stepIndex < 0 || _stepIndex >= _previewSteps.Count - 1) return null;
            var step = _previewSteps[_stepIndex];
            var nextStep = _previewSteps[_stepIndex + 1];
            var transition = step.TransitionToNext;
            if (!transition || !transition.hasFixedDuration
                || step.TransitionTargetState != nextStep.State
                || !UsesDefaultPreviewTiming(step.State)
                || !UsesDefaultPreviewTiming(nextStep.State)) return null;
            return transition;
        }

        private static bool UsesDefaultPreviewTiming(AnimatorState state)
        {
            return state
                && Mathf.Approximately(state.speed, 1f)
                && !state.speedParameterActive
                && !state.timeParameterActive
                && Mathf.Approximately(state.cycleOffset, 0f)
                && !state.cycleOffsetParameterActive
                && !state.mirror
                && !state.mirrorParameterActive;
        }

        private float GetTransitionDuration()
        {
            var transition = GetCurrentTransition();
            return transition ? Mathf.Max(0f, transition.duration) : 0f;
        }

        private float GetNextStateStartTime()
        {
            var transition = GetCurrentTransition();
            if (!transition || _stepIndex + 1 >= _sourceClips.Count) return 0f;
            var nextClip = _sourceClips[_stepIndex + 1];
            return transition.offset * (nextClip ? nextClip.length : 0f) + GetTransitionDuration();
        }

        private void OnEditorUpdate()
        {
            if (!_isPlaying) return;
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Max(0f, (float)(now - _lastUpdateTime));
            _lastUpdateTime = now;
            _time += dt * _playbackSpeed;

            for (var guard = 0; guard < 4; guard++)
            {
                var maxTime = GetMaxTime();
                if (_time < maxTime) break;
                var overflow = _time - maxTime;
                if (_loopCurrentClip)
                {
                    _time = maxTime > 0f ? overflow % maxTime : 0f;
                    break;
                }
                var nextStateStartTime = GetNextStateStartTime();
                if (!AdvanceToNextClip())
                {
                    _time = maxTime;
                    _isPlaying = false;
                    break;
                }
                _time = nextStateStartTime + overflow;
            }
            Repaint();
        }

        private bool AdvanceToNextClip()
        {
            if (_stepIndex >= _previewSteps.Count - 1) return false;
            SelectPreviewStep(_stepIndex + 1);
            return true;
        }

        private void StepFrame(float delta, float maxTime)
        {
            _isPlaying = false;
            _time = Mathf.Clamp(_time + delta, 0f, maxTime);
        }

        private static void ProcessResultPreviewClone(GameObject instance)
        {
            UnpackPreviewPrefabInstances(instance);
            foreach (var patch in instance.GetComponentsInChildren<AfkMotionPatch>(true))
                UnityEngine.Object.DestroyImmediate(patch);
            using (new nadena.dev.ndmf.OverrideTemporaryDirectoryScope(null))
                nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(instance);
        }

        private static void UnpackPreviewPrefabInstances(GameObject instance)
        {
            while (true)
            {
                var prefabInstanceRoot = instance.GetComponentsInChildren<Transform>(true)
                    .Select(transform => PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject))
                    .FirstOrDefault(root => root
                        && (root == instance || root.transform.IsChildOf(instance.transform)));
                if (!prefabInstanceRoot) return;
                PrefabUtility.UnpackPrefabInstance(
                    prefabInstanceRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
        }

        private sealed class PreviewPane : IDisposable
        {
            private enum ViewTool { None, Pan, Zoom, Orbit }

            private readonly string _label;
            private PreviewRenderUtility _preview;
            private GameObject _sourceRoot;
            private bool _processAvatar;
            private AnimationClip _clip;
            private IReadOnlyList<AnimationClip> _resetSources = Array.Empty<AnimationClip>();
            private AnimationClip _resetClip;
            private GameObject _instance;
            private Animator _animator;
            private AnimatorController _sampleController;
            private AnimatorStateMachine _sampleStateMachine;
            private readonly List<AnimatorState> _sampleStates = new List<AnimatorState>();
            private readonly Dictionary<AnimationClip, int> _stateHashes = new Dictionary<AnimationClip, int>();
            private Vector2 _orbit;
            private Vector3 _focus;
            private float _distance = 2.2f;
            private float _minimumDistance = 0.2f;
            private bool _needsFit = true;
            private bool _hasCameraView;
            private ViewTool _viewTool;

            public PreviewPane(string label)
            {
                _label = label;
                _preview = new PreviewRenderUtility();
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.backgroundColor = new Color(49f / 255f, 49f / 255f, 49f / 255f, 1f);
                _preview.camera.nearClipPlane = 0.01f;
                _preview.camera.farClipPlane = 100f;
                _preview.camera.fieldOfView = 25f;
                if (_preview.lights != null && _preview.lights.Length > 0)
                {
                    _preview.lights[0].intensity = 1.2f;
                    _preview.lights[0].transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                }
            }

            public void SetContent(GameObject root, bool processAvatar, AnimationClip clip,
                IReadOnlyList<AnimationClip> resetSources = null, bool forceRebuild = false)
            {
                resetSources = resetSources ?? _resetSources;
                if (!forceRebuild && _sourceRoot == root && _processAvatar == processAvatar)
                {
                    if (!ReferenceEquals(_resetSources, resetSources))
                    {
                        ResetToRestPose();
                        RebuildResetClip(resetSources);
                    }
                    _clip = clip;
                    return;
                }
                var preserveCamera = _hasCameraView && _sourceRoot == root;
                _sourceRoot = root;
                _processAvatar = processAvatar;
                _clip = clip;
                DestroyInstance();
                _needsFit = !preserveCamera;
                if (!_sourceRoot)
                {
                    _hasCameraView = false;
                    return;
                }
                _instance = UnityEngine.Object.Instantiate(_sourceRoot);
                _instance.name = _sourceRoot.name + " (AFK Preview)";
                _instance.hideFlags = HideFlags.HideAndDontSave;
                _instance.SetActive(true);
                try
                {
                    if (_processAvatar) ProcessResultPreviewClone(_instance);
                    _preview.AddSingleGO(_instance);
                    RebuildResetClip(resetSources);
                }
                catch
                {
                    DestroyInstance();
                    throw;
                }
            }

            public void Draw(Rect rect, GameObject root, AnimationClip clip, float time, bool interactive,
                AnimationClip nextClip = null, float nextTime = 0f,
                float transitionDuration = 0f, float transitionProgress = 0f)
            {
                SetContent(root, _processAvatar, clip);
                GUI.Box(rect, GUIContent.none);
                var title = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, PaneToolbarHeight);
                GUI.Label(title, T(_label), EditorStyles.whiteMiniLabel);

                var previewRect = new Rect(rect.x, rect.y + PaneToolbarHeight, rect.width, rect.height - PaneToolbarHeight);
                if (interactive) HandleDrag(previewRect);

                if (_instance && clip)
                {
                    SampleAnimation(clip, time, nextClip, nextTime, transitionDuration, transitionProgress);
                }

                if (!_instance)
                {
                    GUI.Label(previewRect, T("Preview root not resolved"), EditorStyles.centeredGreyMiniLabel);
                    return;
                }

                if (_needsFit) FitCamera();
                Render(previewRect);
            }

            private void SampleAnimation(AnimationClip clip, float time, AnimationClip nextClip,
                float nextTime, float transitionDuration, float transitionProgress)
            {
                if (!_animator || !_stateHashes.TryGetValue(clip, out var stateHash))
                {
                    ResetToRestPose();
                    clip.SampleAnimation(_instance, ResolveSampleTime(clip, time));
                    return;
                }

                _animator.Play(stateHash, 0, ResolveNormalizedTime(clip, time));
                _animator.Update(0f);
                if (!nextClip || transitionDuration <= 0.0001f
                    || !_stateHashes.TryGetValue(nextClip, out var nextStateHash)) return;
                _animator.CrossFadeInFixedTime(nextStateHash, transitionDuration, 0,
                    Mathf.Max(0f, nextTime), Mathf.Clamp01(transitionProgress));
                _animator.Update(0f);
            }

            private static float ResolveSampleTime(AnimationClip clip, float time)
            {
                return clip && clip.length > 0.0001f ? Mathf.Clamp(time, 0f, clip.length) : 0f;
            }

            private static float ResolveNormalizedTime(AnimationClip clip, float time)
            {
                return clip && clip.length > 0.0001f ? Mathf.Max(0f, time) / clip.length : 0f;
            }

            public void ResetView()
            {
                _needsFit = true;
                _hasCameraView = false;
            }

            public void CopyCameraFrom(PreviewPane other)
            {
                if (other == null || other == this || !other._hasCameraView) return;
                _orbit = other._orbit;
                _focus = other._focus;
                _distance = other._distance;
                _minimumDistance = other._minimumDistance;
                _needsFit = false;
                _hasCameraView = true;
            }

            private void Render(Rect rect)
            {
                if (Event.current.type != EventType.Repaint) return;
                _preview.BeginPreview(rect, GUIStyle.none);
                UpdateCamera();
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.Render();
                var texture = _preview.EndPreview();
                // Copy an opaque frame so editor repaints cannot blend against pixels from an older preview.
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
            }

            // Input mapping and motion follow UnityEditor.AvatarPreview from UnityCsReference 2022.3.
            private void HandleDrag(Rect rect)
            {
                var evt = Event.current;
                var id = GUIUtility.GetControlID(FocusType.Passive, rect);
                var eventType = evt.GetTypeForControl(id);
                if (eventType == EventType.KeyDown && evt.keyCode == KeyCode.F && rect.Contains(evt.mousePosition))
                {
                    ResetView();
                    evt.Use();
                    return;
                }
                if (eventType == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
                {
                    ZoomCamera(evt, HandleUtility.niceMouseDeltaZoom * (evt.shift ? 2f : 0.5f));
                    return;
                }
                if (eventType == EventType.MouseDown && rect.Contains(evt.mousePosition))
                {
                    _viewTool = ResolveViewTool(evt);
                    if (_viewTool != ViewTool.None)
                    {
                        EditorGUIUtility.SetWantsMouseJumping(1);
                        GUIUtility.hotControl = id;
                        evt.Use();
                    }
                }
                else if (eventType == EventType.MouseUp && GUIUtility.hotControl == id)
                {
                    _viewTool = ViewTool.None;
                    GUIUtility.hotControl = 0;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    evt.Use();
                }
                else if (eventType == EventType.MouseDrag && GUIUtility.hotControl == id)
                {
                    switch (_viewTool)
                    {
                        case ViewTool.Pan:
                            PanCamera(evt);
                            break;
                        case ViewTool.Orbit:
                            OrbitCamera(evt, rect);
                            break;
                        case ViewTool.Zoom:
                            ZoomCamera(evt, -HandleUtility.niceMouseDeltaZoom * (evt.shift ? 2f : 0.5f));
                            break;
                    }
                }
                if (rect.Contains(evt.mousePosition))
                {
                    EditorGUIUtility.AddCursorRect(rect, CurrentCursor());
                }
            }

            private static ViewTool ResolveViewTool(Event evt)
            {
                var controlKeyOnMac = evt.control && Application.platform == RuntimePlatform.OSXEditor;
                var actionKey = EditorGUI.actionKey;
                var noModifiers = !actionKey && !controlKeyOnMac && !evt.alt;
                if ((evt.button <= 0 && noModifiers) || (evt.button <= 0 && actionKey) || evt.button == 2) return ViewTool.Pan;
                if ((evt.button <= 0 && controlKeyOnMac) || (evt.button == 1 && evt.alt)) return ViewTool.Zoom;
                if ((evt.button <= 0 && evt.alt) || evt.button == 1) return ViewTool.Orbit;
                return ViewTool.None;
            }

            private MouseCursor CurrentCursor()
            {
                switch (_viewTool)
                {
                    case ViewTool.Pan: return MouseCursor.Pan;
                    case ViewTool.Zoom: return MouseCursor.Zoom;
                    case ViewTool.Orbit: return MouseCursor.Orbit;
                    default: return MouseCursor.Arrow;
                }
            }

            private void PanCamera(Event evt)
            {
                var camera = _preview.camera;
                var screenPosition = camera.WorldToScreenPoint(_focus);
                screenPosition += new Vector3(-evt.delta.x, evt.delta.y, 0f);
                _focus += camera.ScreenToWorldPoint(screenPosition) - _focus;
                _hasCameraView = true;
                evt.Use();
            }

            private void OrbitCamera(Event evt, Rect rect)
            {
                _orbit -= evt.delta * (evt.shift ? 3f : 1f) / Mathf.Min(rect.width, rect.height) * 140f;
                _orbit.y = Mathf.Clamp(_orbit.y, -90f, 90f);
                _hasCameraView = true;
                evt.Use();
            }

            private void ZoomCamera(Event evt, float delta)
            {
                var zoomDelta = -delta * 0.05f;
                _distance += _distance * zoomDelta;
                _distance = Mathf.Clamp(_distance, _minimumDistance, 20f);
                _hasCameraView = true;
                evt.Use();
            }

            private void FitCamera()
            {
                _needsFit = false;
                var bounds = CalculateBounds(_instance);
                var size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.2f);
                _focus = bounds.center;
                _distance = Mathf.Clamp(size * 2.6f, 0.5f, 15f);
                _minimumDistance = Mathf.Max(_distance / 10f, 0.05f);
                _orbit = Vector2.zero;
                _hasCameraView = true;
            }

            private void UpdateCamera()
            {
                var rotation = Quaternion.Euler(_orbit.y * 0.25f, _orbit.x * 0.25f, 0f);
                var direction = rotation * new Vector3(0f, 0f, -1f);
                _preview.camera.transform.position = _focus - direction * _distance;
                _preview.camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            private static Bounds CalculateBounds(GameObject root)
            {
                if (!root) return new Bounds(Vector3.zero, Vector3.one);
                var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
                if (renderers.Length == 0) return new Bounds(root.transform.position + Vector3.up, Vector3.one);
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }

            private void RebuildResetClip(IReadOnlyList<AnimationClip> clips)
            {
                DestroySampleController();
                if (_resetClip) UnityEngine.Object.DestroyImmediate(_resetClip);
                _resetSources = clips ?? Array.Empty<AnimationClip>();
                _resetClip = null;
                if (!_instance || _resetSources.Count == 0) return;

                var resetClip = new AnimationClip
                {
                    name = _sourceRoot.name + " (AFK Preview Rest Pose)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                foreach (var binding in _resetSources.Where(item => item)
                             .SelectMany(AnimationUtility.GetCurveBindings).Distinct())
                {
                    float value;
                    if (binding.isDiscreteCurve)
                    {
                        if (!AnimationUtility.GetDiscreteIntValue(_instance, binding, out var discreteValue)) continue;
                        value = discreteValue;
                    }
                    else if (!AnimationUtility.GetFloatValue(_instance, binding, out value)) continue;
                    AnimationUtility.SetEditorCurve(
                        resetClip, binding, AnimationCurve.Constant(0f, 1f, value));
                }
                foreach (var binding in _resetSources.Where(item => item)
                             .SelectMany(AnimationUtility.GetObjectReferenceCurveBindings).Distinct())
                {
                    AnimationUtility.GetObjectReferenceValue(_instance, binding, out var value);
                    if (!value && !AnimationUtility.GetAnimatedObject(_instance, binding)) continue;
                    AnimationUtility.SetObjectReferenceCurve(resetClip, binding,
                        new[] { new ObjectReferenceKeyframe { time = 0f, value = value } });
                }
                _resetClip = resetClip;
                BuildSampleController();
            }

            private void BuildSampleController()
            {
                AfkMotionPatchEditorUtil.EnableAnimationOnAffectedPhysBones(_instance, _resetSources);
                _animator = _instance ? _instance.GetComponent<Animator>() : null;
                if (!_animator) return;

                _sampleController = new AnimatorController
                {
                    name = _sourceRoot.name + " (AFK Preview Controller)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _sampleStateMachine = new AnimatorStateMachine
                {
                    name = "AFK Preview",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _sampleController.layers = new[]
                {
                    new AnimatorControllerLayer
                    {
                        name = "AFK Preview",
                        defaultWeight = 1f,
                        stateMachine = _sampleStateMachine
                    }
                };
                var childStates = new List<ChildAnimatorState>();
                for (var index = 0; index < _resetSources.Count; index++)
                {
                    var clip = _resetSources[index];
                    if (!clip || _stateHashes.ContainsKey(clip)) continue;
                    var stateName = "Clip " + index;
                    var state = new AnimatorState
                    {
                        name = stateName,
                        motion = clip,
                        writeDefaultValues = true,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    if (!_sampleStateMachine.defaultState) _sampleStateMachine.defaultState = state;
                    _sampleStates.Add(state);
                    childStates.Add(new ChildAnimatorState
                    {
                        state = state,
                        position = new Vector3(200f, index * 60f, 0f)
                    });
                    _stateHashes[clip] = Animator.StringToHash("AFK Preview." + stateName);
                }
                _sampleStateMachine.states = childStates.ToArray();

                _animator.enabled = true;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.runtimeAnimatorController = _sampleController;
                _animator.Rebind();
                _animator.Update(0f);
            }

            private void DestroySampleController()
            {
                if (_animator && _animator.runtimeAnimatorController == _sampleController)
                    _animator.runtimeAnimatorController = null;
                foreach (var state in _sampleStates)
                    if (state) UnityEngine.Object.DestroyImmediate(state);
                _sampleStates.Clear();
                _stateHashes.Clear();
                if (_sampleStateMachine) UnityEngine.Object.DestroyImmediate(_sampleStateMachine);
                if (_sampleController) UnityEngine.Object.DestroyImmediate(_sampleController);
                _sampleStateMachine = null;
                _sampleController = null;
                _animator = null;
            }

            private void ResetToRestPose()
            {
                if (_instance && _resetClip) _resetClip.SampleAnimation(_instance, 0f);
            }

            private void DestroyInstance()
            {
                DestroySampleController();
                if (_resetClip) UnityEngine.Object.DestroyImmediate(_resetClip);
                _resetClip = null;
                _resetSources = Array.Empty<AnimationClip>();
                if (_instance) UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }

            public void Dispose()
            {
                DestroyInstance();
                _preview?.Cleanup();
                _preview = null;
            }
        }
    }
}
#endif
