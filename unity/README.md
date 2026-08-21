# Optional: the full-quality shader

The mod works without this. Everything here is about one specific limitation.

Sea Power runs Unity's **built-in render pipeline with no post-processing package**, so there is no
stock grading stack for the mod to drive. Its image effect therefore composites with
fixed-function blending, which can multiply and add but **cannot mix colour channels**. That means
it can amplify, tint, vignette and add grain — but it can't turn the frame into true monochrome
before tinting it, the way a real image intensifier does. Coloured objects stay coloured-ish
instead of becoming brightness.

`NightVision.shader` does it properly: real luminance extraction, gain, contrast, phosphor tint,
halation around bright sources, scanlines, grain and vignette, all in a single blit. The mod loads
it automatically if it can find it.

## Building the bundle

You need Unity **6000.0.x** (match the game's `6000.0.67f1` as closely as you can — Unity Hub can
install an exact version). A shader has to be compiled by the editor; there is no way to do it at
runtime, which is why this isn't just shipped in the DLL.

1. Create a new **3D (Built-in Render Pipeline)** project.
2. Copy `NightVision.shader` into `Assets/`.
3. Copy `Editor/BuildNightVisionBundle.cs` into `Assets/Editor/`.
4. Menu bar → **Sea Power Night Vision → Build Shader Bundle**.
5. It builds `BuiltBundles/nightvision.bundle` and opens the folder.
6. Copy `nightvision.bundle` into:
   `Sea Power/BepInEx/plugins/SeaPowerNightVision/` — next to `SeaPowerNightVision.dll`.

Relaunch. The log will say:

```
Loaded the night vision shader from nightvision.bundle.
```

and the settings panel's backend line will read `Built-in pipeline image effect (shader)`.

## Editing the look

The shader is plain, commented ShaderLab. The numbered steps in `frag()` map onto the settings
panel sliders, so if you want a different curve, a harsher tube or a different halation threshold,
change it there and rebuild the bundle — no need to touch the C#.
