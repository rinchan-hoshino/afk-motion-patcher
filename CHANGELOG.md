# Changelog

All notable changes to **AFK Motion Patcher** are documented in this file.

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
