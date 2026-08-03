# Changelog

All notable changes to AFK Motion Patcher are documented in this file.

## [1.0.0] - 2026-08-03

### Added

- Source-authoritative extraction of the AFK-connected Action Controller graph.
- Recursive Motion retargeting with hierarchy, BlendShape, curve, and advanced binding mappings.
- Source / Result preview following the extracted source graph and transition timing.
- NDMF build integration and automatic PhysBone `Is Animated` handling on generated clones.
- Editor localization for English, Japanese, Simplified Chinese, Traditional Chinese, and Korean.

### Fixed

- Result Preview processes an isolated target copy through NDMF, so Modular Avatar Bone Proxy and Merge Armature attachments follow the sampled target pose without modifying the source avatar or recursively processing AFK Motion Patcher.
