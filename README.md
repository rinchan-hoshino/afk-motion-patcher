# AFK Motion Patcher

AFK Motion Patcher is an NDMF editor plugin for retargeting a source VRChat avatar's AFK behavior graph through explicit mappings to a target avatar.

It is not a universal automatic converter. Differences in hierarchy, rest pose, BlendShapes, and animation bindings must be mapped explicitly in the component or supplied by a compatible Adapter package.

The source Avatar and its Action Controller are authoritative. The plugin extracts the state graph connected to the official `AFK` parameter, retargets the motions used by that graph according to those mappings, and generates the target Avatar's Action layer from the extracted graph. The target Avatar's original Action Controller is not inspected, merged, or used to classify states.

## Installation

1. [Add the repository to VCC](vcc://vpm/addRepo?url=https%3A%2F%2Fvpm.k-neco.com%2Findex.json).
2. In VCC, open **Manage Project** and add **AFK Motion Patcher**.

If the VCC link does not open, add `https://vpm.k-neco.com/index.json` under **Settings > Packages > Add Repository**.

## Features

- Resolves the Action Controller from the source Avatar descriptor and automatically selects the unique outer AFK root entered through an `AFK`-true transition.
- Retains the connected source states, nested state machines, transitions, conditions, exit timing, behaviours, and layer settings while excluding unrelated Action states.
- Supports adapter-owned source-state Motion overrides for imported Controllers whose Motion references are missing; these deterministic preset details are not exposed as user choices.
- Recursively retargets AnimationClips inside retained BlendTrees without flattening their structure.
- Copies source float and object-reference curves, then applies hierarchy mappings, additive BlendShape mappings, curve adjustments, and advanced binding mappings.
- Enables **Is Animated** on affected PhysBones in Preview and NDMF build clones when generated Transform curves target their chains, without editing the source Avatar or target prefab.
- Provides drag-and-drop target object fields and searchable source/target pickers, so hierarchy paths, BlendShapes, and bindings do not need to be typed manually.
- Provides a read-only Source / Result preview whose visible sections, order, and normalized state dwell are derived from the AFK source graph rather than fixed Intro / Loop / Outro assumptions.
- Shares MA/NDMF's Editor language preference and supports the same five locales: English, 日本語, 简体中文, 繁體中文, and 한국어. Missing translations fall back to English.
- Does not add a runtime Animator, parameter, Modular Avatar, or Gesture Manager dependency.

## Requirements

- Unity 2022.3
- VRChat Avatars SDK 3
- NDMF with Animator Services

## Usage

1. Add one enabled `THE_cattail/AFK Motion Patch` to an object under the target VRChat Avatar.
2. Choose the Editor language from the shared MA/NDMF selector, then under **AFK Source** assign the source Avatar prefab. Its descriptor's Action Controller and outer AFK root are resolved automatically.
3. Add retargeting rules only where the target Avatar's paths, rest pose, or BlendShape names differ. Right-click a section header to copy or paste the whole group, apply it to the connected prefab, or revert the whole group to prefab defaults.
4. Click **Open Preview**. The window remains bound to that component and follows the source graph's preview route; enable **Loop** to repeat only the current source state.
5. Use Unity's standard avatar-preview controls in either pane: drag to pan, Alt-drag or right-drag to orbit, middle-drag to pan, Alt-right-drag or the wheel to zoom, and **F** or **Reset View** to frame the avatar.
6. Validate the generated Action layer through NDMF build/Apply on Play and VRChat runtime tooling.

Separate Adapter packages may install and configure the component automatically.

AFK Motion Patcher does not include source Avatars, animations, or character-specific Adapter Presets. Use only source assets that you lawfully obtained and are permitted to use.

## Extraction boundary

The generated Action layer contains only the source Controller states reachable from the AFK entry branch and its exit path. Unrelated source Action states are excluded. The target Avatar's Action Controller is replaced at NDMF build time and is deliberately irrelevant to extraction.

If the source Controller has additional positive-duration motion states along its AFK exit path, Preview shows those states too. Zero-duration routing states remain in the compiled graph but are not displayed as full clips. The route is never collapsed to three hard-coded phases.

## Current limitations

- The source must be an `AnimatorController` asset; `AnimatorOverrideController` is not currently accepted as the graph source.
- The source Avatar must contain exactly one `VRCAvatarDescriptor` with one non-default custom Action Controller.
- A target Avatar must have exactly one active patch. Multiple active patches are rejected instead of choosing one by hierarchy order.
- The Action Controller must yield one unique supported outer AFK root. Multiple independent roots are rejected instead of asking the user to choose an internal graph anchor.
- Duplicate extracted state paths are rejected so adapter-owned Motion overrides cannot address more than one state.
- Synced source Controller layers and nested candidate roots are rejected; their external layer dependency or root entry chain cannot be preserved by an isolated AFK-only Action graph.
- A state that is reachable both before and after crossing an `AFK == false` exit boundary is rejected rather than merging those two transition contexts.
- Source clips with Animation Events are rejected. Additive reference-pose clips are cloned and retargeted privately with the rest of the Motion graph for both build and Preview.
- Preview supports retained states whose effective Motion is an AnimationClip. It reproduces source offsets and fixed-duration crossfades between directly adjacent previewable states that use default state timing; unsupported intermediate Motions or non-default state timing remain hard boundaries rather than being approximated incorrectly. BlendTree states are retained and recursively retargeted in NDMF build, but are omitted from Preview rather than flattened to an incorrect child clip.
- Preview is an editor approximation; final Animator, Gesture Manager, and VRChat runtime validation remain required.

## Repository scope

This open-source repository contains only the generic patcher. Avatar-specific source references, hidden state Motion overrides, and retargeting mappings belong to the separate closed-source AFK Motion Adapters package and are not published here.

## License

AFK Motion Patcher is available under the [MIT License](LICENSE).
