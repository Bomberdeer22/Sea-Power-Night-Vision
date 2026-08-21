# Sea Power — Night Vision

A simple mod for **Sea Power: Naval Combat in the Missile Age** that adds a **toggleable night
vision mode**, so night operations are actually readable instead of a black screen with a few
running lights.

Press **Ctrl+N** in a mission and the world is amplified and tinted like an image intensifier.
Press it again and everything returns exactly to how the game rendered it.

```
Ctrl + N                 toggle night vision on / off
Ctrl + Shift + N         cycle tube type (Gen-3 green -> white phosphor -> neutral boost -> amber)
Ctrl + PageUp / PageDown gain up / down (1.0x .. 8.0x)
Ctrl + Alt + N           open the in-game settings panel
```

The settings panel lets you tune gain, contrast, tint, glow, vignette, grain and the light
amplification while the mission runs — no config-file editing, no restart. It is driven by the
**keyboard** (arrow keys select and adjust, Shift for bigger steps, Enter toggles, Escape closes)
as well as the mouse, because Sea Power captures the mouse for camera control and that can stop
IMGUI panels receiving clicks.

All keys, colours and intensities are configurable — see [Configuration](#configuration).

---

## What it actually does

Night vision is applied in two layers, which is why it reads much better than simply turning up
your monitor brightness:

| Layer | What happens |
| --- | --- |
| **A real post-process filter** | The mod injects a global `Volume` into the game's own render pipeline and drives the stock post-processing overrides: post-exposure (gain), saturation and colour filter (phosphor tint), contrast, bloom (halation around running lights and gunfire), vignette and film grain. This is the same machinery the game uses for its own grading, so it is applied to the rendered 3D scene **inside the pipeline, before the interface is composited** — the UI, labels and tactical map are physically untouched. Toggling fades the volume's weight, which is smooth and free. |
| **Scene amplification** | Ambient light, sky/equator/ground ambient colours and scene light intensities are multiplied while the mode is on, and night haze/fog density is reduced. This reveals unlit hull surfaces, wakes and the horizon — real photons, not just brighter pixels. Very bright lights (muzzle flashes, explosions, > 8 intensity) are deliberately left alone so they don't blow out. |

### Filter backends

The mod picks the best available path at startup and logs which one it chose:

1. **Volume post-processing** (URP/HDRP) — drives the pipeline's stock grading overrides.
2. **Post Processing Stack v2** — same approach for games using the legacy package.
3. **Built-in pipeline image effect** — what Sea Power actually uses. Installs a real
   `OnRenderImage` effect on the world camera, so Unity runs it as part of that camera's rendering
   on the scene colour buffer. Screen-space UI is composited afterwards and is untouched.

The first two are found by reflection, so the mod never has to reference URP/HDRP/PPv2 at compile
time and stays a single dependency-free DLL. Force a specific one with `Backend` in the config if
you ever need to.

### A note on the built-in pipeline path

Sea Power ships the built-in render pipeline with **no post-processing package**, so there is no
grading stack to drive and no way to compile a shader at runtime. The image effect therefore
composites with fixed-function blending by default: gain, shadow lift, phosphor tint, vignette,
scanlines and grain all work, but blending cannot mix colour channels, so the tint is per-channel
rather than true monochrome.

For the real thing — luminance extraction, halation, the lot — build the optional shader bundle
once and drop it next to the DLL. It takes about five minutes and needs Unity 6000.0.x:
see [unity/README.md](unity/README.md). The mod detects and uses it automatically.

Everything the mod changes is captured before it is touched and restored on toggle-off, on scene
change and on unload. If the game's own time-of-day lighting moves a light while night vision is
active, the mod adopts the new value as the baseline instead of fighting it.

---

## Installing (players)

1. Install **BepInEx 5.4.x (x64, Mono)** into your Sea Power folder — extract so that `BepInEx/`
   sits next to `Sea Power.exe`. Run the game once, then close it.
2. Drop `SeaPowerNightVision.dll` into
   `Sea Power/BepInEx/plugins/SeaPowerNightVision/`.
3. Launch the game, load a mission, press **Ctrl+N**.

A config file is generated at
`Sea Power/BepInEx/config/io.github.bomberdeer22.seapower.nightvision.cfg` on first launch.

> Steam Workshop distribution: build with `-AnchorChain` and ship the DLL together with
> `workshop/_info.ini`. Users then need [Anchor Chain](https://seapower-modders.github.io/AnchorChain/)
> (the community chainloader) instead of a manual `plugins/` install.

---

## Building (developers)

Requirements: **.NET SDK 8.0+**, and a Sea Power install with BepInEx 5.4.x already present (the
project references the game's `Managed` DLLs and `BepInEx/core` directly — nothing is vendored).

Windows:

```bat
build.cmd                                                     :: auto-detects the Steam install
build.cmd -GameDir "D:\SteamLibrary\steamapps\common\Sea Power"
build.cmd -AnchorChain                                        :: + Steam Workshop entry point
```

`build.cmd` is just a wrapper around `build.ps1`. If you prefer to call PowerShell directly and
hit *"cannot be loaded... is not digitally signed"*, that is Windows blocking downloaded scripts —
use either of these:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1          # one-off, nothing is changed
Unblock-File .\build.ps1                                      # or clear the download flag once
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned           # ...and allow local scripts
```

Linux / macOS / WSL:

```bash
./build.sh "/mnt/d/SteamLibrary/steamapps/common/Sea Power"
```

Or directly:

```bash
dotnet build src/SeaPowerNightVision/SeaPowerNightVision.csproj -c Release \
  /p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Sea Power"
```

On a successful build the DLL is copied into `BepInEx/plugins/SeaPowerNightVision/`
(disable with `/p:CopyToPlugins=false`).

### Source layout

```
unity/                        optional shader + Unity project files for the full-quality look
src/SeaPowerNightVision/
  NightVisionPlugin.cs        BepInEx entry point, creates the persistent controller
  NightVisionSettings.cs      every config entry + per-mode tint/gain presets
  NightVisionController.cs    on/off state, hotkeys, fade, backend selection
  NightVisionUI.cs            in-game settings panel and the NVG status readout
  INightVisionFilter.cs       the interface each rendering backend implements
  VolumeFilter.cs             URP/HDRP volume post-processing (the good path)
  PostProcessV2Filter.cs      legacy Post Processing Stack v2 path
  OverlayFilter.cs            built-in pipeline path; installs the image effect on the world camera
  NightVisionScreenEffect.cs  the camera image effect (shader blit, or fixed-function passes)
  NightVisionShaders.cs       finds the optional high-quality shader bundle
  SceneLightBooster.cs        ambient, light-intensity and fog amplification + exact restore
  Reflect.cs                  reflection helpers for driving the pipeline without referencing it
  InputBridge.cs              legacy Input / Input System compatibility for the hotkeys
  AnchorChainEntryPoint.cs    optional Workshop chainloader entry (compiled with /p:AnchorChain=true)
workshop/_info.ini            Sea Power mod-manager metadata for Workshop uploads
```

---

## Configuration

`BepInEx/config/io.github.bomberdeer22.seapower.nightvision.cfg`

### 1. Hotkeys

| Key | Default | Purpose |
| --- | --- | --- |
| `ToggleKey` | `Ctrl + N` | Toggle night vision |
| `CycleModeKey` | `Ctrl + Shift + N` | Next tube type |
| `GainUpKey` / `GainDownKey` | `Ctrl + PageUp` / `Ctrl + PageDown` | Gain ±0.5x |
| `SettingsWindowKey` | `Ctrl + Alt + N` | Open the in-game settings panel |

### 2. General

| Setting | Default | Purpose |
| --- | --- | --- |
| `Mode` | `Gen3Green` | `Gen3Green`, `WhitePhosphor`, `LowLightBoost` (no tint), `AmberHotSpot` |
| `EnabledOnStart` | `false` | Start with the tubes down |
| `ShowIndicator` | `true` | Small `NVG <mode> x<gain>` readout, bottom-left |
| `AutoEnableAtNight` | `false` | Switch on/off automatically as the scene darkens |
| `AutoEnableThreshold` | `0.12` | Brightness below which auto-mode engages |
| `FadeSeconds` | `0.25` | Fade time when toggling (0 = instant) |

### 3. Image

| Setting | Default | Purpose |
| --- | --- | --- |
| `Gain` | `3.0` | Brightness multiplication (1–8), applied as post-exposure |
| `Contrast` | `12` | Contrast added while active; helps ships separate from the sea |
| `TubeGlow` | `0.35` | Bloom/halation around bright sources |
| `ShadowLift` | `0.06` | Additive glow floor in pure black areas |
| `TintStrength` | `0.85` | How strongly the phosphor colour is applied |
| `CustomTintColor` | *(empty)* | Hex override, e.g. `#6BFF8A` |

### 4. Tube effects

`Vignette` (on, `0.55`), `Scanlines` (off, `0.15`, spacing `3`), `SensorNoise` (off — it is the
only setting with a real frame-time cost).

### 5. World lighting

| Setting | Default | Purpose |
| --- | --- | --- |
| `BoostSceneLighting` | `true` | Master switch for the amplification layer |
| `AmbientBoost` | `2.5` | Ambient light multiplier |
| `LightBoost` | `1.6` | Scene light intensity multiplier |
| `FogReduction` | `0.35` | How much night haze is cut |
| `LightScanInterval` | `2.0` | Seconds between rescans for newly spawned lights |

### 6. Advanced

`Backend` (`Auto`, `Volume`, `PostProcessingV2`, `Overlay`, `None`), `AffectAllCameras` (overlay
backend only — turn on if a secondary/periscope view stays dark), `VerboseLogging`.

### Suggested presets

```ini
# "Just let me see" — minimal stylisation, maximum readability
Mode = LowLightBoost
Gain = 2.5
Vignette = false
AmbientBoost = 5

# Cinematic Gen-3
Mode = Gen3Green
Gain = 3.5
ShadowLift = 0.1
Scanlines = true
SensorNoise = 0.25
```

---

## Troubleshooting

**Nothing happens on Ctrl+N.** Check `BepInEx/LogOutput.log` for
`Sea Power Night Vision 1.0.0 loaded`. If it is missing, BepInEx is not installed correctly
(`BepInEx/` must sit next to `Sea Power.exe`) or the DLL is not under `BepInEx/plugins/`.

**The tint bleeds onto the UI.** The filter runs on the world camera, so screen-space UI should
never be affected. If some interface element *is* tinted, it is being drawn by the world camera
itself — tell me which element and I'll add it to the exclusion logic.

**The image is green but colours look odd rather than monochrome.** You're on the fixed-function
path, which cannot mix colour channels. Build the optional shader bundle
([unity/README.md](unity/README.md)) for true monochrome-plus-phosphor.

**The world brightens but there is no tint at all.** No filter backend could start; only the
lighting amplification is running. Raise `AmbientBoost` to compensate and check the log.

**A picture-in-picture view stays dark.** Set `AffectAllCameras = true`.

**Too washed out / blooming.** Lower `Gain` to ~2.0 and `AmbientBoost` to ~2.5, and raise
`TintStrength` for contrast.

**Multiplayer.** This is a purely client-side visual mod; it changes no unit, sensor or weapon
data and does not affect detection ranges — it only changes what your screen looks like.

---

## License

MIT — see [LICENSE](LICENSE).
