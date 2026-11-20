# AFK Motion Patcher 2.0.1 — Feature Map

| Area | File(s) | Responsibility |
| --- | --- | --- |
| Runtime Authoring Component | `Runtime/AfkMotionPatch.cs` | `INDMFEditorOnly` authoring data: `sourceAvatar`, `actionMergeMode`, `stripAnimationEvents`, `boneRotationOffsets`, `humanoidPoseAdjustment`, `rendererPathRemaps`, `blendShapeNameRemaps`, `sourceKeyAdjustments`, `sourceBindingRules`. |
| NDMF Build Plugin | `Editor/AfkMotionPatchPlugin.cs` | Extracts source AFK graph, retargets clips/BlendTrees, ensures clean exit BlendShape resets, enables `Is Animated` on affected PhysBones, and applies surgical `PreserveExistingAction` merge or `ReplaceEntireAction`. |
| Source Graph Extractor | `Editor/AfkMotionSourceGraph.cs` | Resolves `VRCAvatarDescriptor` Action Controller, discovers unique outer `AFK`-true entry root, builds preview step route, and reports clips with `AnimationEvent`s. |
| Curve & Bone Retargeting | `Editor/AfkMotionPatchEditorUtil.cs` | Clones source motions (with safe AnimationEvent stripping/retention), remaps paths/BlendShapes/bindings, applies `BoneRotationOffset`, invokes Humanoid baking & RootQ continuity, and restores exit BlendShapes. |
| Humanoid Contact & RootQ Baker | `Editor/AfkMotionHumanoidBaker.cs` | Ensures `RootQ` unit continuity and hemisphere alignment, and bakes contact-weighted 2-bone arm IK hand clearance and leg posture directly into Humanoid muscle curves via `HumanPoseHandler`. |
| Smart Auto-Matcher | `Editor/AfkMotionAutoMatcher.cs` | Matches isomorphic non-humanoid bone chains, similar BlendShapes (with JP/EN synonym normalization), and computes local rest-pose rotation deltas. |
| Three-Section Inspector | `Editor/AfkMotionPatchInspector.cs` | Clean workflow UI (*Source & Action Merge*, *Auto-Match & Pose Tuning* including Humanoid contact sliders, *Advanced Rules*), Pitch/Yaw/Roll & Strength sliders, and immediate Preview notification. |
| Live Preview Window | `Editor/AfkMotionPreviewWindow.cs` | Synchronized side-by-side `Source / Result` avatar preview with forced EditMode skinning refresh, playback controls, transition crossfades, and same-frame `NotifyPatchChanged` refresh. |
| Localization | `Editor/AfkMotionPatchLocalization.cs` | Shared NDMF/MA language selector with native `en-US`, `ja-JP`, `zh-Hans`, `zh-Hant`, `ko-KR` dictionaries. |
| EditMode Tests | `Tests/Editor/AfkMotionPatchV2Tests.cs`, `AfkMotionSourceGraphStartupTests.cs`, `AfkMotionPatchValidationTests.cs` | Automated NUnit verification for NDMF Action merge, auto-match, rest-pose delta, RootQ continuity, Humanoid contact/IK baking, exit BlendShape reset, AnimationEvent handling, and VRChat validation compliance. |
