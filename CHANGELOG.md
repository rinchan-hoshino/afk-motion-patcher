# Changelog

All notable changes to **AFK Motion Patcher** are documented in this file.

## [2.0.1] - 2026-11-20

### Added
- **Build-Time Humanoid Contact & Pose Tuning (`HumanoidPoseAdjustment`)**: Contact-weighted (`w = 0` while standing, `w = 1` near head) 2-bone analytic arm IK baking and leg/knee posture adjustment written directly into standard Humanoid muscle curves with zero runtime overhead, exposed in the Inspector with 5-language UI and live preview sync.
- **Root Quaternion Continuity & Hemisphere Normalization**: Automatically repairs opposite-hemisphere `RootQ` discontinuities (`Dot(prev, curr) < 0`) and normalizes single-hemisphere clips to `w >= 0` while preserving genuine continuous rotations and all `AnimationClipSettings`.
- **Clean Exit BlendShape Restoration**: Automatically injects rest-pose reset curves on exit/neutral AFK clips (such as `BlendOut AFK` / `Restore Tracking AFK`) so exiting AFK never leaves residual closed-eye or expression BlendShapes.

### Fixed
- **Preview SkinnedMeshRenderer Refresh**: Enabled `forceMatrixRecalculationPerRender` and `updateWhenOffscreen` on `AfkMotionPreviewWindow` preview instances to guarantee reliable multi-frame EditMode skinning updates.

## [2.0.0] - 2026-11-20

### Added
- **Non-Destructive Action Merge (`PreserveExistingAction`)**: Preserves existing Action layers and parameters (GoGoLoco, dances, poses) by surgically stripping only old `AFK == true` triggers before appending the retargeted AFK layer. Includes optional `ReplaceEntireAction` mode.
- **Smart Auto-Match (`Auto-Match Hierarchy & BlendShapes`)**: Automatically matches isomorphic non-humanoid bone chains (`Tail.001..007 ↔ Tail_1..7`, `Ear_L/R`) and similar BlendShapes between source and target avatars.
- **Rest-Pose Delta & Intuitive Angle Sliders**: One-click `Match Rest-Pose Delta` plus live **Pitch / Yaw / Roll (-90° ~ +90°)** sliders for target bones and **Strength** sliders for expression BlendShapes.
- **Same-Frame Live Preview Sync**: Dragging any pose or BlendShape slider in the Inspector immediately updates the open **Source / Result** Preview window.
- **Safe AnimationEvent Handling**: Automatically strips `AnimationEvent` entries from source AFK clips by default with an Inspector toggle and status notice.
- **Streamlined Three-Section Inspector**: Organized into *1. Source & Action Merge*, *2. Auto-Match & Pose Tuning*, and *3. Advanced Rules* (collapsed by default).
- **Five-Language Native Localization**: Complete UI translations for `en-US`, `ja-JP`, `zh-Hans`, `zh-Hant`, and `ko-KR`.
- **EditMode Test Suite**: Automated NUnit coverage for non-destructive GoGoLoco Action merging, hierarchy & BlendShape auto-matching, rest-pose delta & bone rotation offsets, and AnimationEvent stripping.
