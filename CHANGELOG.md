# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-08-21

### Changed
- **Night vision is now a real post-process filter, not a screen overlay.** The mod injects a
  global `Volume` into the game's render pipeline and drives the stock overrides (post-exposure,
  saturation, colour filter, contrast, bloom, vignette, film grain). Because that runs inside the
  pipeline, before the interface is composited, the UI is no longer affected at all.
- Backends are selected automatically: URP/HDRP volume, then Post Processing Stack v2, then the
  old GL overlay as a last resort. Override with the new `Backend` setting.
- The GL fallback now binds to the *world* camera rather than the highest-depth camera, so even
  in fallback mode the interface is drawn on top of the effect instead of underneath it.
- Default `AmbientBoost` lowered to 2.5 now that exposure is handled properly by the filter.

### Added
- In-game settings panel (**Ctrl+Alt+N**) with live sliders for gain, contrast, tint, glow,
  vignette, grain and light amplification, plus tube-type buttons and a reset.
- `Contrast` and `TubeGlow` (bloom/halation) settings.
- Input System fallback so hotkeys work regardless of the game's input backend.
- Startup diagnostics in the log: Unity version, render pipeline, input backend, active filter.
- `diagnose.cmd` / `diagnose.ps1` for troubleshooting BepInEx installs, including `-FixDoorstop`.

### Removed
- The `BepInProcess` filter, which could silently prevent the plugin loading.

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
