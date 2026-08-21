using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// The in-game panel (default Ctrl+Alt+N) and the small status readout.
    /// <para>
    /// The panel is fully usable from the keyboard — arrow keys to select and adjust, Enter to
    /// activate, Escape to close — because games that capture the mouse for camera control can
    /// leave IMGUI unable to receive clicks at all. Mouse input works too when the game allows it.
    /// </para>
    /// </summary>
    internal class NightVisionUI
    {
        private const int WindowId = 0x5EA9;

        private readonly NightVisionController _controller;
        private readonly NightVisionSettings _settings;
        private readonly List<PanelRow> _rows = new List<PanelRow>();

        private Rect _window = new Rect(60f, 90f, 360f, 0f);
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _selectedStyle;
        private GUIStyle _indicatorStyle;
        private GUIStyle _windowStyle;
        private Texture2D _panelBackground;
        private Texture2D _selectionBackground;
        private bool _stylesReady;
        private int _selected;

        public bool Visible { get; set; }

        public NightVisionUI(NightVisionController controller, NightVisionSettings settings)
        {
            _controller = controller;
            _settings = settings;
            BuildRows();
        }

        /// <summary>One adjustable line in the panel.</summary>
        private class PanelRow
        {
            public string Label;
            public string Section;

            // Slider rows
            public Func<float> Get;
            public Action<float> Set;
            public float Min;
            public float Max;
            public float Step;
            public string Format = "{0:0.00}";

            // Toggle / action rows
            public Func<bool> GetToggle;
            public Action<bool> SetToggle;
            public Action Activate;
            public Func<string> ActionLabel;

            public Func<bool> Visible = () => true;

            public bool IsSlider => Get != null;
            public bool IsToggle => GetToggle != null;
            public bool IsAction => Activate != null;
        }

        private void BuildRows()
        {
            _rows.Add(new PanelRow
            {
                Section = null,
                Activate = () => _controller.Toggle(),
                ActionLabel = () => _controller.Active ? "Tubes DOWN  (on)" : "Tubes UP  (off)"
            });

            _rows.Add(new PanelRow
            {
                Section = "Tube type",
                Activate = () => _controller.CycleMode(),
                ActionLabel = () => $"< {Describe(_settings.Mode.Value)} >"
            });

            AddSlider("Image", "Gain", () => _settings.Gain.Value, v => _settings.Gain.Value = v, 1f, 8f, 0.25f, "{0:0.0}x");
            AddSlider(null, "Contrast", () => _settings.Contrast.Value, v => _settings.Contrast.Value = v, -50f, 60f, 2f, "{0:0}");
            AddSlider(null, "Tint", () => _settings.TintStrength.Value, v => _settings.TintStrength.Value = v, 0f, 1f, 0.05f, "{0:P0}");
            AddSlider(null, "Glow", () => _settings.TubeGlow.Value, v => _settings.TubeGlow.Value = v, 0f, 2f, 0.05f, "{0:0.00}");

            _rows.Add(new PanelRow
            {
                Section = "Tube artefacts",
                Label = "Vignette",
                GetToggle = () => _settings.Vignette.Value,
                SetToggle = v => _settings.Vignette.Value = v
            });

            AddSlider(null, "Vignette amount", () => _settings.VignetteStrength.Value, v => _settings.VignetteStrength.Value = v,
                0f, 1f, 0.05f, "{0:P0}", () => _settings.Vignette.Value);

            AddSlider(null, "Sensor grain", () => _settings.SensorNoise.Value, v => _settings.SensorNoise.Value = v, 0f, 1f, 0.05f, "{0:P0}");

            _rows.Add(new PanelRow
            {
                Section = "Light amplification",
                Label = "Amplify world lighting",
                GetToggle = () => _settings.BoostSceneLighting.Value,
                SetToggle = v => _settings.BoostSceneLighting.Value = v
            });

            AddSlider(null, "Ambient", () => _settings.AmbientBoost.Value, v => _settings.AmbientBoost.Value = v,
                1f, 12f, 0.25f, "{0:0.0}x", () => _settings.BoostSceneLighting.Value);
            AddSlider(null, "Lights", () => _settings.LightBoost.Value, v => _settings.LightBoost.Value = v,
                1f, 6f, 0.1f, "{0:0.0}x", () => _settings.BoostSceneLighting.Value);
            AddSlider(null, "Cut haze", () => _settings.FogReduction.Value, v => _settings.FogReduction.Value = v,
                0f, 1f, 0.05f, "{0:P0}", () => _settings.BoostSceneLighting.Value);

            _rows.Add(new PanelRow
            {
                Section = "Behaviour",
                Label = "Auto on at night",
                GetToggle = () => _settings.AutoEnableAtNight.Value,
                SetToggle = v => _settings.AutoEnableAtNight.Value = v
            });

            _rows.Add(new PanelRow
            {
                Label = "Show NVG readout",
                GetToggle = () => _settings.ShowIndicator.Value,
                SetToggle = v => _settings.ShowIndicator.Value = v
            });

            _rows.Add(new PanelRow
            {
                Section = null,
                Activate = ResetDefaults,
                ActionLabel = () => "Reset to defaults"
            });
        }

        private void AddSlider(string section, string label, Func<float> get, Action<float> set,
            float min, float max, float step, string format, Func<bool> visible = null)
        {
            _rows.Add(new PanelRow
            {
                Section = section,
                Label = label,
                Get = get,
                Set = set,
                Min = min,
                Max = max,
                Step = step,
                Format = format,
                Visible = visible ?? (() => true)
            });
        }

        /// <summary>
        /// Keyboard control, called from the controller while the panel is open. This is the
        /// reliable path: keyboard input reaches the mod even when the game owns the mouse.
        /// </summary>
        public void HandleKeyboard()
        {
            if (InputBridge.GetKeyDown(KeyCode.Escape))
            {
                Visible = false;
                return;
            }

            if (InputBridge.GetKeyDown(KeyCode.DownArrow))
            {
                Move(1);
            }
            else if (InputBridge.GetKeyDown(KeyCode.UpArrow))
            {
                Move(-1);
            }

            var fast = InputBridge.GetKey(KeyCode.LeftShift) || InputBridge.GetKey(KeyCode.RightShift);
            var scale = fast ? 4f : 1f;

            if (InputBridge.GetKeyDown(KeyCode.RightArrow))
            {
                Nudge(+scale);
            }
            else if (InputBridge.GetKeyDown(KeyCode.LeftArrow))
            {
                Nudge(-scale);
            }

            if (InputBridge.GetKeyDown(KeyCode.Return) || InputBridge.GetKeyDown(KeyCode.KeypadEnter))
            {
                Activate();
            }
        }

        private void Move(int direction)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                _selected = (_selected + direction + _rows.Count) % _rows.Count;
                if (_rows[_selected].Visible())
                {
                    return;
                }
            }
        }

        private PanelRow Current => _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

        private void Nudge(float scale)
        {
            var row = Current;
            if (row == null)
            {
                return;
            }

            if (row.IsSlider)
            {
                row.Set(Mathf.Clamp(row.Get() + row.Step * scale, row.Min, row.Max));
            }
            else if (row.IsToggle)
            {
                row.SetToggle(!row.GetToggle());
            }
            else if (row.IsAction)
            {
                row.Activate();
            }
        }

        private void Activate()
        {
            var row = Current;
            if (row == null)
            {
                return;
            }

            if (row.IsAction)
            {
                row.Activate();
            }
            else if (row.IsToggle)
            {
                row.SetToggle(!row.GetToggle());
            }
        }

        public void Draw(float weight)
        {
            EnsureStyles();

            if (_settings.ShowIndicator.Value && weight > 0.01f)
            {
                DrawIndicator(weight);
            }

            if (!Visible)
            {
                return;
            }

            var previousDepth = GUI.depth;
            GUI.depth = -1000;
            _window = GUILayout.Window(WindowId, _window, DrawWindow, "Night Vision", _windowStyle);
            GUI.depth = previousDepth;
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _panelBackground = MakeTexture(new Color(0.04f, 0.07f, 0.05f, 0.95f));
            _selectionBackground = MakeTexture(new Color(0.15f, 0.35f, 0.20f, 0.95f));

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

            _selectedStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold };
            _selectedStyle.normal.textColor = Color.white;
            _selectedStyle.normal.background = _selectionBackground;
            _selectedStyle.padding = new RectOffset(4, 4, 1, 1);

            _indicatorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };

            _stylesReady = true;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
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
            GUILayout.Label("Arrow keys to select and adjust · Enter to toggle · Esc to close", _labelStyle);
            GUILayout.Space(4f);

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (!row.Visible())
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(row.Section))
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(row.Section, _headerStyle);
                }

                DrawRow(row, i == _selected, i);
            }

            GUILayout.Space(8f);

            var backendNote = _controller.FilterIsTruePostProcess
                ? "Running as a camera image effect — the UI is not affected."
                : "No screen filter active; see the log.";
            GUILayout.Label(backendNote, _labelStyle);
            GUILayout.Label($"Backend: {_controller.FilterName}", _labelStyle);

            GUILayout.Space(4f);
            GUILayout.Label($"Toggle {_settings.ToggleKey.Value} · cycle {_settings.CycleModeKey.Value} · this panel {_settings.SettingsWindowKey.Value}", _labelStyle);

            if (_settings.VerboseLogging.Value)
            {
                GUILayout.Label($"mouse {Input.mousePosition} · cursor {Cursor.lockState}", _labelStyle);
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private void DrawRow(PanelRow row, bool selected, int index)
        {
            var style = selected ? _selectedStyle : _labelStyle;

            if (row.IsAction)
            {
                if (GUILayout.Button(row.ActionLabel(), GUILayout.Height(24f)))
                {
                    _selected = index;
                    row.Activate();
                }

                if (selected)
                {
                    var rect = GUILayoutUtility.GetLastRect();
                    GUI.Label(new Rect(rect.x - 10f, rect.y + 2f, 12f, 20f), ">", _headerStyle);
                }

                return;
            }

            GUILayout.BeginHorizontal();

            if (row.IsToggle)
            {
                GUILayout.Label(selected ? "> " + row.Label : "  " + row.Label, style, GUILayout.Width(200f));
                var value = GUILayout.Toggle(row.GetToggle(), row.GetToggle() ? " on" : " off");
                if (value != row.GetToggle())
                {
                    _selected = index;
                    row.SetToggle(value);
                }
            }
            else
            {
                GUILayout.Label(selected ? "> " + row.Label : "  " + row.Label, style, GUILayout.Width(150f));
                var value = GUILayout.HorizontalSlider(row.Get(), row.Min, row.Max);
                if (!Mathf.Approximately(value, row.Get()))
                {
                    _selected = index;
                    row.Set(value);
                }

                GUILayout.Label(string.Format(row.Format, row.Get()), _labelStyle, GUILayout.Width(52f));
            }

            GUILayout.EndHorizontal();
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
                case NightVisionMode.WhitePhosphor: return "White phosphor";
                case NightVisionMode.LowLightBoost: return "Clear (no tint)";
                case NightVisionMode.AmberHotSpot: return "Amber";
                default: return "Gen-3 green";
            }
        }
    }
}
