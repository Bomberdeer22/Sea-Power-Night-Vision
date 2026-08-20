# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-08-20

### Added
- Toggleable night vision mode for Sea Power (default **Ctrl+N**).
- Four tube types, cycled with **Ctrl+Shift+N**: Gen-3 green, white phosphor, neutral
  low-light boost and amber hot-spot.
- Adjustable image-intensifier gain (1.0x–8.0x) with **Ctrl+PageUp / Ctrl+PageDown**.
- Scene amplification layer: ambient light, sky/equator/ground ambient colours, scene light
  intensities and fog density are boosted while active and restored exactly on toggle-off.
  Very bright lights (muzzle flashes, explosions) are excluded so they don't blow out.
- Screen composite layer built from Unity's built-in `Hidden/Internal-Colored` shader with
  immediate-mode GL: multi-pass gain, additive shadow lift, phosphor tint, vignette,
  scanlines and animated sensor grain. No asset bundle or custom shader required.
- Smooth configurable fade when toggling, and an optional `NVG <mode> x<gain>` HUD readout.
- Optional `AutoEnableAtNight` that engages the mode when the scene brightness drops below a
  configurable threshold.
- Full BepInEx configuration file covering hotkeys, image, tube effects, world lighting and
  advanced camera handling.
- Optional Anchor Chain entry point (`/p:AnchorChain=true`) plus `workshop/_info.ini` for
  Steam Workshop distribution.
- `build.ps1` (with Steam library auto-detection) and `build.sh` build helpers.
