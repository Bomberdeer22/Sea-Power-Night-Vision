using BepInEx.Configuration;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Available image-intensifier looks. Cycled in this order with the cycle hotkey.
    /// </summary>
    public enum NightVisionMode
    {
        /// <summary>Classic Gen-III image intensifier: strong gain, green phosphor.</summary>
        Gen3Green = 0,

        /// <summary>Modern white-phosphor tube: strong gain, cool grey/white image.</summary>
        WhitePhosphor = 1,

        /// <summary>No tint at all, just low-light amplification. Keeps ship/sea colours.</summary>
        LowLightBoost = 2,

        /// <summary>Amber "thermal-ish" look for spotting hot exhausts and gunfire.</summary>
        AmberHotSpot = 3
    }

    /// <summary>
    /// All user-facing configuration, written to
    /// <c>BepInEx/config/io.github.bomberdeer22.seapower.nightvision.cfg</c>.
    /// </summary>
    public class NightVisionSettings
    {
        public readonly ConfigEntry<KeyboardShortcut> ToggleKey;
        public readonly ConfigEntry<KeyboardShortcut> CycleModeKey;
        public readonly ConfigEntry<KeyboardShortcut> GainUpKey;
        public readonly ConfigEntry<KeyboardShortcut> GainDownKey;

        public readonly ConfigEntry<NightVisionMode> Mode;
        public readonly ConfigEntry<bool> EnabledOnStart;
        public readonly ConfigEntry<bool> ShowIndicator;
        public readonly ConfigEntry<bool> AutoEnableAtNight;
        public readonly ConfigEntry<float> AutoEnableThreshold;
        public readonly ConfigEntry<float> FadeSeconds;

        public readonly ConfigEntry<float> Gain;
        public readonly ConfigEntry<float> ShadowLift;
        public readonly ConfigEntry<float> TintStrength;
        public readonly ConfigEntry<string> CustomTintColor;

        public readonly ConfigEntry<bool> Vignette;
        public readonly ConfigEntry<float> VignetteStrength;
        public readonly ConfigEntry<bool> Scanlines;
        public readonly ConfigEntry<float> ScanlineStrength;
        public readonly ConfigEntry<int> ScanlineSpacing;
        public readonly ConfigEntry<float> SensorNoise;

        public readonly ConfigEntry<bool> BoostSceneLighting;
        public readonly ConfigEntry<float> AmbientBoost;
        public readonly ConfigEntry<float> LightBoost;
        public readonly ConfigEntry<float> FogReduction;
        public readonly ConfigEntry<float> LightScanInterval;

        public readonly ConfigEntry<bool> AffectAllCameras;
        public readonly ConfigEntry<bool> VerboseLogging;

        public NightVisionSettings(ConfigFile config)
        {
            const string keys = "1. Hotkeys";
            const string general = "2. General";
            const string image = "3. Image";
            const string tube = "4. Tube effects";
            const string world = "5. World lighting";
            const string advanced = "6. Advanced";

            ToggleKey = config.Bind(keys, "ToggleKey",
                new KeyboardShortcut(KeyCode.N, KeyCode.LeftControl),
                "Turns night vision on and off.");

            CycleModeKey = config.Bind(keys, "CycleModeKey",
                new KeyboardShortcut(KeyCode.N, KeyCode.LeftControl, KeyCode.LeftShift),
                "Cycles between Gen3Green / WhitePhosphor / LowLightBoost / AmberHotSpot.");

            GainUpKey = config.Bind(keys, "GainUpKey",
                new KeyboardShortcut(KeyCode.PageUp, KeyCode.LeftControl),
                "Increases image intensifier gain while night vision is active.");

            GainDownKey = config.Bind(keys, "GainDownKey",
                new KeyboardShortcut(KeyCode.PageDown, KeyCode.LeftControl),
                "Decreases image intensifier gain while night vision is active.");

            Mode = config.Bind(general, "Mode", NightVisionMode.Gen3Green,
                "Which tube look to use.");

            EnabledOnStart = config.Bind(general, "EnabledOnStart", false,
                "Start the game with night vision already switched on.");

            ShowIndicator = config.Bind(general, "ShowIndicator", true,
                "Draw a small 'NVG' status readout in the corner of the screen while active.");

            AutoEnableAtNight = config.Bind(general, "AutoEnableAtNight", false,
                "Automatically switch night vision on when the scene gets dark, and off again at dawn. " +
                "Manual toggling always overrides the automation until the light level crosses the threshold again.");

            AutoEnableThreshold = config.Bind(general, "AutoEnableThreshold", 0.12f,
                new ConfigDescription(
                    "Scene brightness (0 = pitch black, 1 = full daylight) below which AutoEnableAtNight kicks in.",
                    new AcceptableValueRange<float>(0.01f, 0.6f)));

            FadeSeconds = config.Bind(general, "FadeSeconds", 0.25f,
                new ConfigDescription("Fade-in/fade-out time when toggling, in seconds. 0 = instant.",
                    new AcceptableValueRange<float>(0f, 2f)));

            Gain = config.Bind(image, "Gain", 3.0f,
                new ConfigDescription("Brightness multiplication applied to the rendered image.",
                    new AcceptableValueRange<float>(1f, 8f)));

            ShadowLift = config.Bind(image, "ShadowLift", 0.06f,
                new ConfigDescription("Additive lift so pitch-black areas still show tube glow.",
                    new AcceptableValueRange<float>(0f, 0.35f)));

            TintStrength = config.Bind(image, "TintStrength", 0.85f,
                new ConfigDescription("How strongly the phosphor tint is applied (0 = no tint).",
                    new AcceptableValueRange<float>(0f, 1f)));

            CustomTintColor = config.Bind(image, "CustomTintColor", "",
                "Optional hex colour (e.g. #6BFF8A) that overrides the tint of the selected mode. Leave empty for the mode default.");

            Vignette = config.Bind(tube, "Vignette", true,
                "Darken the edges of the screen like looking through a tube.");

            VignetteStrength = config.Bind(tube, "VignetteStrength", 0.55f,
                new ConfigDescription("Vignette opacity.", new AcceptableValueRange<float>(0f, 1f)));

            Scanlines = config.Bind(tube, "Scanlines", false,
                "Draw faint horizontal scanlines.");

            ScanlineStrength = config.Bind(tube, "ScanlineStrength", 0.15f,
                new ConfigDescription("Scanline opacity.", new AcceptableValueRange<float>(0f, 1f)));

            ScanlineSpacing = config.Bind(tube, "ScanlineSpacing", 3,
                new ConfigDescription("Pixels between scanlines.", new AcceptableValueRange<int>(2, 16)));

            SensorNoise = config.Bind(tube, "SensorNoise", 0.0f,
                new ConfigDescription("Amount of animated sensor grain. 0 disables it (best for performance).",
                    new AcceptableValueRange<float>(0f, 1f)));

            BoostSceneLighting = config.Bind(world, "BoostSceneLighting", true,
                "Also raise the scene's ambient light and light intensities while active. " +
                "This is what actually makes unlit hulls and the horizon readable, rather than just brightening black pixels.");

            AmbientBoost = config.Bind(world, "AmbientBoost", 4.0f,
                new ConfigDescription("Multiplier applied to ambient light while night vision is active.",
                    new AcceptableValueRange<float>(1f, 12f)));

            LightBoost = config.Bind(world, "LightBoost", 1.6f,
                new ConfigDescription("Multiplier applied to directional/scene light intensity while active.",
                    new AcceptableValueRange<float>(1f, 6f)));

            FogReduction = config.Bind(world, "FogReduction", 0.35f,
                new ConfigDescription("How much night haze/fog density is cut while active (0 = untouched, 1 = removed).",
                    new AcceptableValueRange<float>(0f, 1f)));

            LightScanInterval = config.Bind(world, "LightScanInterval", 2.0f,
                new ConfigDescription("How often (seconds) the mod rescans the scene for new lights to boost.",
                    new AcceptableValueRange<float>(0.25f, 15f)));

            AffectAllCameras = config.Bind(advanced, "AffectAllCameras", false,
                "Apply the effect to every enabled camera instead of only the highest-depth one. " +
                "Turn this on if a picture-in-picture / periscope view stays dark.");

            VerboseLogging = config.Bind(advanced, "VerboseLogging", false,
                "Log camera attachment and lighting changes to the BepInEx console.");
        }

        /// <summary>Phosphor colour for the current mode, honouring <see cref="CustomTintColor"/>.</summary>
        public Color GetTintColor()
        {
            var custom = CustomTintColor.Value;
            if (!string.IsNullOrEmpty(custom) && ColorUtility.TryParseHtmlString(custom.Trim(), out var parsed))
            {
                return parsed;
            }

            switch (Mode.Value)
            {
                case NightVisionMode.WhitePhosphor:
                    return new Color(0.82f, 0.88f, 1.00f);
                case NightVisionMode.LowLightBoost:
                    return Color.white;
                case NightVisionMode.AmberHotSpot:
                    return new Color(1.00f, 0.72f, 0.34f);
                case NightVisionMode.Gen3Green:
                default:
                    return new Color(0.36f, 1.00f, 0.45f);
            }
        }

        /// <summary>Per-mode multiplier on top of <see cref="Gain"/>.</summary>
        public float GetModeGainScale()
        {
            switch (Mode.Value)
            {
                case NightVisionMode.WhitePhosphor:
                    return 1.0f;
                case NightVisionMode.LowLightBoost:
                    return 0.85f;
                case NightVisionMode.AmberHotSpot:
                    return 0.9f;
                case NightVisionMode.Gen3Green:
                default:
                    return 1.0f;
            }
        }

        /// <summary>Tint blend factor for the current mode (LowLightBoost stays neutral).</summary>
        public float GetEffectiveTintStrength()
        {
            return Mode.Value == NightVisionMode.LowLightBoost ? 0f : TintStrength.Value;
        }
    }
}
