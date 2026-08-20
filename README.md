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
```

All keys, colours and intensities are configurable — see [Configuration](#configuration).

---

## What it actually does

Night vision is applied in two layers, which is why it reads much better than simply turning up
your monitor brightness:

| Layer | What happens |
| --- | --- |
| **Scene amplification** | Ambient light, sky/equator/ground ambient colours and scene light intensities are multiplied while the mode is on, and night haze/fog density is reduced. This reveals unlit hull surfaces, wakes and the horizon — real photons, not just brighter black pixels. Very bright lights (muzzle flashes, explosions, > 8 intensity) are deliberately left alone so they don't bloom into a white screen. |
| **Tube simulation** | A full-screen composite is drawn at the end of camera rendering: multiplicative gain (applied over several passes so gains above 2x work), an additive shadow lift so true black still glows, a phosphor tint, and optional vignette, scanlines and animated sensor grain. |

Two details worth knowing:

* The effect is drawn **before screen-space UI**, so the tactical map overlay, labels and menus
  stay clean and readable while the 3D world is intensified.
* The screen effect uses Unity's built-in `Hidden/Internal-Colored` shader with immediate-mode
  `GL` drawing — **no asset bundle and no custom shader**, which keeps the mod a single DLL and
  makes it far less likely to break on a game update.

Everything the mod changes (ambient colours, fog, per-light intensities) is captured before it is
touched and restored on toggle-off, on scene change and on unload. If the game's own time-of-day
lighting moves a light while night vision is active, the mod adopts the new value as the baseline
instead of fighting it.

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

```powershell
.\build.ps1                                                   # auto-detects the Steam install
.\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Sea Power"
.\build.ps1 -AnchorChain                                      # + Steam Workshop entry point
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
src/SeaPowerNightVision/
  NightVisionPlugin.cs        BepInEx entry point, creates the persistent controller
  NightVisionSettings.cs      every config entry + per-mode tint/gain presets
  NightVisionController.cs    on/off state, hotkeys, fade, camera tracking, HUD readout
  NightVisionScreenEffect.cs  the GL composite (gain / lift / tint / vignette / scanlines / grain)
  SceneLightBooster.cs        ambient, light-intensity and fog amplification + exact restore
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
| `Gain` | `3.0` | Brightness multiplication (1–8) |
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
| `AmbientBoost` | `4.0` | Ambient light multiplier |
| `LightBoost` | `1.6` | Scene light intensity multiplier |
| `FogReduction` | `0.35` | How much night haze is cut |
| `LightScanInterval` | `2.0` | Seconds between rescans for newly spawned lights |

### 6. Advanced

`AffectAllCameras` (turn on if a secondary/periscope view stays dark), `VerboseLogging`.

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

**The world brightens but there is no green tint.** The log will contain a
`Hidden/Internal-Colored` error — the screen-effect layer is unavailable in that build; the
lighting amplification still works. Raise `AmbientBoost` to compensate.

**A picture-in-picture view stays dark.** Set `AffectAllCameras = true`.

**Too washed out / blooming.** Lower `Gain` to ~2.0 and `AmbientBoost` to ~2.5, and raise
`TintStrength` for contrast.

**Multiplayer.** This is a purely client-side visual mod; it changes no unit, sensor or weapon
data and does not affect detection ranges — it only changes what your screen looks like.

---

## License

MIT — see [LICENSE](LICENSE).
