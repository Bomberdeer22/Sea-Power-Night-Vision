using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// The in-game panel (default Ctrl+Alt+N) and the small status readout.
    /// <para>
    /// Everything here is IMGUI drawn by the mod itself, so it is deliberately the only part of
    /// the mod that appears on top of the game's interface — and only while you open it.
    /// </para>
    /// </summary>
    internal class NightVisionUI
    {
        private const int WindowId = 0x5EA9;

        private readonly NightVisionController _controller;
        private readonly NightVisionSettings _settings;

        private Rect _window = new Rect(60f, 90f, 340f, 0f);
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _indicatorStyle;
        private GUIStyle _windowStyle;
        private Texture2D _panelBackground;
        private bool _stylesReady;

        public bool Visible { get; set; }

        public NightVisionUI(NightVisionController controller, NightVisionSettings settings)
        {
            _controller = controller;
            _settings = settings;
        }

        public void Draw(float weight)
        {
            EnsureStyles();

            if (_settings.ShowIndicator.Value && weight > 0.01f)
            {
                DrawIndicator(weight);
            }

            if (Visible)
            {
                _window = GUILayout.Window(WindowId, _window, DrawWindow, "Night Vision", _windowStyle);
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _panelBackground = new Texture2D(1, 1);
            _panelBackground.SetPixel(0, 0, new Color(0.04f, 0.07f, 0.05f, 0.94f));
            _panelBackground.Apply();
            _panelBackground.hideFlags = HideFlags.HideAndDontSave;

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _panelBackground;
            _windowStyle.onNormal.background = _panelBackground;
            _windowStyle.normal.textColor = new Color(0.6f, 1f, 0.7f);
            _windowStyle.onNormal.textColor = new Color(0.6f, 1f, 0.7f);
            _windowStyle.fontStyle = FontStyle.Bold;
            _windowStyle.padding = new RectOffset(12, 12, 22, 12);

            _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _labelStyle.normal.textColor = new Color(0.82f, 0.92f, 0.85f);

            _headerStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold };
            _headerStyle.normal.textColor = new Color(0.55f, 1f, 0.68f);

            _indicatorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };

            _stylesReady = true;
        }

        private void DrawIndicator(float weight)
        {
            var tint = _settings.GetTintColor();
            var previous = GUI.color;
            GUI.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(weight) * 0.8f);

            var label = $"NVG  {Describe(_settings.Mode.Value)}  x{_settings.Gain.Value:0.0}";
            GUI.Label(new Rect(16f, Screen.height - 32f, 420f, 24f), label, _indicatorStyle);

            GUI.color = previous;
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(2f);

            // Power
            GUILayout.BeginHorizontal();
            var buttonLabel = _controller.Active ? "Tubes DOWN  (on)" : "Tubes UP  (off)";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(28f)))
            {
                _controller.Toggle();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Tube type", _headerStyle);

            // Mode selector
            var modes = new[]
            {
                NightVisionMode.Gen3Green,
                NightVisionMode.WhitePhosphor,
                NightVisionMode.LowLightBoost,
                NightVisionMode.AmberHotSpot
            };

            GUILayout.BeginHorizontal();
            foreach (var mode in modes)
            {
                var selected = _settings.Mode.Value == mode;
                var style = selected ? GUI.skin.box : GUI.skin.button;
                if (GUILayout.Button(Describe(mode), style, GUILayout.Height(24f)) && !selected)
                {
                    _controller.SetMode(mode);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Image", _headerStyle);

            _settings.Gain.Value = Slider("Gain", _settings.Gain.Value, 1f, 8f, "{0:0.0}x");
            _settings.Contrast.Value = Slider("Contrast", _settings.Contrast.Value, -50f, 60f, "{0:0}");
            _settings.TintStrength.Value = Slider("Tint", _settings.TintStrength.Value, 0f, 1f, "{0:P0}");
            _settings.TubeGlow.Value = Slider("Glow / halation", _settings.TubeGlow.Value, 0f, 2f, "{0:0.00}");

            GUILayout.Space(8f);
            GUILayout.Label("Tube artefacts", _headerStyle);

            _settings.Vignette.Value = GUILayout.Toggle(_settings.Vignette.Value, " Vignette");
            if (_settings.Vignette.Value)
            {
                _settings.VignetteStrength.Value = Slider("Amount", _settings.VignetteStrength.Value, 0f, 1f, "{0:P0}");
            }

            _settings.SensorNoise.Value = Slider("Sensor grain", _settings.SensorNoise.Value, 0f, 1f, "{0:P0}");

            GUILayout.Space(8f);
            GUILayout.Label("Light amplification", _headerStyle);

            _settings.BoostSceneLighting.Value = GUILayout.Toggle(_settings.BoostSceneLighting.Value, " Amplify world lighting");
            if (_settings.BoostSceneLighting.Value)
            {
                _settings.AmbientBoost.Value = Slider("Ambient", _settings.AmbientBoost.Value, 1f, 12f, "{0:0.0}x");
                _settings.LightBoost.Value = Slider("Lights", _settings.LightBoost.Value, 1f, 6f, "{0:0.0}x");
                _settings.FogReduction.Value = Slider("Cut haze", _settings.FogReduction.Value, 0f, 1f, "{0:P0}");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Behaviour", _headerStyle);

            _settings.AutoEnableAtNight.Value = GUILayout.Toggle(_settings.AutoEnableAtNight.Value, " Switch on automatically at night");
            _settings.ShowIndicator.Value = GUILayout.Toggle(_settings.ShowIndicator.Value, " Show NVG readout");

            GUILayout.Space(8f);

            var backendNote = _controller.FilterIsTruePostProcess
                ? "Rendering through the game's post-processing — the UI is not affected."
                : "Fallback overlay in use; see the log for why.";
            GUILayout.Label(backendNote, _labelStyle);
            GUILayout.Label($"Backend: {_controller.FilterName}", _labelStyle);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset defaults"))
            {
                ResetDefaults();
            }
            if (GUILayout.Button("Close"))
            {
                Visible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"Toggle {_settings.ToggleKey.Value} · cycle {_settings.CycleModeKey.Value} · this panel {_settings.SettingsWindowKey.Value}", _labelStyle);

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(110f));
            var result = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(string.Format(format, result), _labelStyle, GUILayout.Width(52f));
            GUILayout.EndHorizontal();
            return result;
        }

        private void ResetDefaults()
        {
            _settings.Gain.Value = (float)_settings.Gain.DefaultValue;
            _settings.Contrast.Value = (float)_settings.Contrast.DefaultValue;
            _settings.TintStrength.Value = (float)_settings.TintStrength.DefaultValue;
            _settings.TubeGlow.Value = (float)_settings.TubeGlow.DefaultValue;
            _settings.Vignette.Value = (bool)_settings.Vignette.DefaultValue;
            _settings.VignetteStrength.Value = (float)_settings.VignetteStrength.DefaultValue;
            _settings.SensorNoise.Value = (float)_settings.SensorNoise.DefaultValue;
            _settings.AmbientBoost.Value = (float)_settings.AmbientBoost.DefaultValue;
            _settings.LightBoost.Value = (float)_settings.LightBoost.DefaultValue;
            _settings.FogReduction.Value = (float)_settings.FogReduction.DefaultValue;
        }

        private static string Describe(NightVisionMode mode)
        {
            switch (mode)
            {
                case NightVisionMode.WhitePhosphor: return "White";
                case NightVisionMode.LowLightBoost: return "Clear";
                case NightVisionMode.AmberHotSpot: return "Amber";
                default: return "Gen-3";
            }
        }
    }
}
