# Changelog

All notable changes to AFK Motion Patcher are documented in this file.

## [1.0.0] - 2026-08-13

### Added

- Source-authoritative extraction of the AFK-connected Action Controller graph.
- Recursive Motion retargeting with hierarchy, BlendShape, curve, and advanced binding mappings.
- Source / Result preview following the extracted source graph and transition timing.
- NDMF build integration and automatic PhysBone `Is Animated` handling on generated clones.
- Editor localization for English, Japanese, Simplified Chinese, Traditional Chinese, and Korean.

### Fixed

- Result Preview processes an isolated target copy through NDMF, so Modular Avatar Bone Proxy and Merge Armature attachments follow the sampled target pose without modifying the source avatar or recursively processing AFK Motion Patcher.
- VRChat SDK validation recognizes the patch as an editor-only authoring component, preserving it until NDMF processing.
- Processed avatars do not retain the patch authoring component.
- Generated Action controllers start in a motionless neutral state and enter the extracted AFK graph only when `AFK` becomes true, preserving locomotion and tracking from the first frame.
