# AFK Motion Patcher

AFK Motion Patcher is a non-destructive NDMF editor tool for retargeting a source VRChat avatar's AFK motion graph onto any target avatar—while preserving your target avatar's existing Action layers (GoGoLoco, dances, sitting/lying poses, and custom emotes).

## Installation

1. [Add the VPM repository to VCC](vcc://vpm/addRepo?url=https%3A%2F%2Fvpm.k-neco.com%2Findex.json).
2. In VCC, open **Manage Project** and add **AFK Motion Patcher**.

If the VCC link does not open, add `https://vpm.k-neco.com/index.json` under **Settings > Packages > Add Repository**.

Alternatively, import `AFK-Motion-Patcher-2.0.1.unitypackage` from BOOTH or GitHub Releases.

## Highlights

- **Build-Time Humanoid Contact & Pose Tuning**: Contact-weighted 2-bone analytic arm IK baking (for face-touch and wide-sleeve clearance) and leg/knee posture adjustment written directly into standard Humanoid muscle curves with zero runtime overhead.

- **Non-Destructive Action Merge (`PreserveExistingAction`)**: Keeps all existing Action Controller layers and parameters (GoGoLoco, dances, poses) intact, surgically removing only legacy `AFK == true` trigger branches before appending the retargeted AFK layer. Optional `ReplaceEntireAction` mode is also available.
- **One-Click Smart Auto-Match**: Automatically matches non-humanoid bone chains (such as `Tail.001..007 ↔ Tail_1..7` and symmetric `Ear_L/R` hierarchies) and similar expression BlendShapes between source and target avatars.
- **Rest-Pose Delta & Intuitive Angle Sliders**: Calculates initial local rotation differences between source and target bones in one click, and lets you fine-tune **Pitch / Yaw / Roll (-90° ~ +90°)** and expression strength with live sliders.
- **Live Side-by-Side Source / Result Preview**: Inspect the full AFK state sequence (Intro, Loop, Outro, and custom transitions) side-by-side with synchronized camera controls. Every slider adjustment refreshes the Preview window on the same frame.
- **Safe AnimationEvent Handling**: Automatically strips unrelated `AnimationEvent` entries from source AFK clips by default (configurable), preventing build errors on avatars without matching receiver scripts.
- **Automatic PhysBone `Is Animated` Activation**: Enables `Is Animated` on affected PhysBone chains in Preview and NDMF build clones without modifying your original avatar prefabs.
- **Five-Language Native UI**: Shares NDMF / Modular Avatar's language selector with full support for English, 日本語, 简体中文, 繁體中文, and 한국어.
- **Zero Runtime Footprint**: Runs purely at NDMF build time (`INDMFEditorOnly`) with no runtime scripts, extra synced parameters, or Modular Avatar dependency.

## Requirements

- Unity 2022.3
- VRChat Avatars SDK 3 (`>=3.7.0 <3.11.0`)
- NDMF with Animator Services (`>=1.11.0`)

## Workflow

1. Add **THE_cattail / AFK Motion Patch** to any GameObject under your target VRChat Avatar.
2. In **1. Source & Action Merge**, assign the **Source Avatar** prefab and choose **Preserve Existing Action (Recommended)**.
3. In **2. Auto-Match & Pose Tuning**, click **Auto-Match Hierarchy & BlendShapes** and **Match Rest-Pose Delta**.
4. Click **Open Preview** and fine-tune ear/tail **Pitch / Yaw / Roll** sliders or expression **Strength** sliders while watching the live **Source / Result** view.
5. Expand **3. Advanced Rules** only if you need raw hierarchy remaps, curve adjustments, or additional binding rules.

Character-specific Adapter packages (such as **Chocolat AFK Adapters**) can also install pre-tuned configurations in one click.

## Repository Scope

This open-source repository contains only the generic AFK Motion Patcher tool. Avatar-specific presets and third-party assets are not included. Use only source avatars and animations you are licensed to use.

## License

AFK Motion Patcher is licensed under the [MIT License](LICENSE).
