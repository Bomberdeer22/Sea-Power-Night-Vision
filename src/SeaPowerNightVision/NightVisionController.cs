using System;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Owns the on/off state, the hotkeys, the fade, and whichever filter backend is in use.
    /// </summary>
    public class NightVisionController : MonoBehaviour
    {
        private NightVisionSettings _settings;
        private SceneLightBooster _lightBooster;
        private INightVisionFilter _filter;
        private NightVisionUI _ui;

        private float _nextLightScan;
        private bool _autoState;
        private bool _autoInitialised;

        /// <summary>True while the operator has night vision switched on.</summary>
        public bool Active { get; private set; }

        /// <summary>0..1 fade weight applied to the filter.</summary>
        public float Weight { get; private set; }

        /// <summary>Description of the active filter backend, for the UI and the log.</summary>
        public string FilterName => _filter != null ? _filter.Name : "none";

        /// <summary>True when the filter runs inside the game's own post-processing (UI untouched).</summary>
        public bool FilterIsTruePostProcess => _filter != null && _filter.IsTruePostProcess;

        private void Awake()
        {
            _settings = NightVisionPlugin.Settings;
            _lightBooster = new SceneLightBooster(_settings);
            _ui = new NightVisionUI(this, _settings);

            SelectFilter();

            NightVisionPlugin.Log.LogInfo(
                $"Night vision ready. Filter: {FilterName}. " +
                $"Toggle {_settings.ToggleKey.Value}, settings panel {_settings.SettingsWindowKey.Value}.");

            if (_settings.EnabledOnStart.Value)
            {
                SetActive(true, instant: true);
            }
        }

        private void OnDestroy()
        {
            SetActive(false, instant: true);
            _lightBooster.Restore();
            _filter?.Dispose();
            _filter = null;
        }

        /// <summary>Picks the best available filter, honouring the Backend config override.</summary>
        private void SelectFilter()
        {
            _filter?.Dispose();
            _filter = null;

            switch (_settings.Backend.Value)
            {
                case FilterBackend.None:
                    return;
                case FilterBackend.Volume:
                    _filter = Try(new VolumeFilter(_settings));
                    break;
                case FilterBackend.PostProcessingV2:
                    _filter = Try(new PostProcessV2Filter(_settings));
                    break;
                case FilterBackend.Overlay:
                    _filter = Try(new OverlayFilter(_settings));
                    break;
                default:
                    _filter = Try(new VolumeFilter(_settings))
                              ?? Try(new PostProcessV2Filter(_settings))
                              ?? Try(new OverlayFilter(_settings));
                    break;
            }

            if (_filter == null)
            {
                NightVisionPlugin.Log.LogWarning(
                    "No screen filter could be initialised; only the world lighting boost will apply.");
            }
        }

        private static INightVisionFilter Try(INightVisionFilter filter)
        {
            try
            {
                if (filter.TryInitialize())
                {
                    return filter;
                }

                filter.Dispose();
            }
            catch (Exception e)
            {
                NightVisionPlugin.Log.LogWarning($"Filter '{filter.Name}' failed to start: {e.Message}");
                filter.Dispose();
            }

            return null;
        }

        private void Update()
        {
            HandleHotkeys();
            UpdateAutoMode();
            UpdateWeight();

            _filter?.Apply(Weight);

            if (Weight <= 0.001f && !Active)
            {
                _lightBooster.Restore();
                return;
            }

            if (_settings.BoostSceneLighting.Value)
            {
                if (Time.unscaledTime >= _nextLightScan)
                {
                    _nextLightScan = Time.unscaledTime + Mathf.Max(0.25f, _settings.LightScanInterval.Value);
                    _lightBooster.Apply(Weight);
                }
                else
                {
                    _lightBooster.UpdateWeight(Weight);
                }
            }
            else
            {
                _lightBooster.Restore();
            }
        }

        private void HandleHotkeys()
        {
            if (IsPressed(_settings.SettingsWindowKey.Value))
            {
                _ui.Visible = !_ui.Visible;
                return;
            }

            if (IsPressed(_settings.CycleModeKey.Value))
            {
                CycleMode();
                return; // Ctrl+Shift+N would otherwise also satisfy the plain Ctrl+N toggle.
            }

            if (IsPressed(_settings.ToggleKey.Value))
            {
                Toggle();
            }

            if (!Active)
            {
                return;
            }

            if (IsPressed(_settings.GainUpKey.Value))
            {
                AdjustGain(+0.5f);
            }
            else if (IsPressed(_settings.GainDownKey.Value))
            {
                AdjustGain(-0.5f);
            }
        }

        private static bool IsPressed(BepInEx.Configuration.KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !InputBridge.GetKeyDown(shortcut.MainKey))
            {
                return false;
            }

            foreach (var modifier in shortcut.Modifiers)
            {
                if (!InputBridge.GetKey(modifier))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Flips night vision on/off, as if the operator flipped the tubes down.</summary>
        public void Toggle()
        {
            SetActive(!Active, instant: false);
        }

        public void SetActive(bool active, bool instant)
        {
            if (Active == active)
            {
                return;
            }

            Active = active;

            if (instant || _settings.FadeSeconds.Value <= 0f)
            {
                Weight = active ? 1f : 0f;
                _filter?.Apply(Weight);
            }

            if (!active)
            {
                _lightBooster.Restore();
            }

            NightVisionPlugin.Log.LogInfo(active
                ? $"Night vision ON ({_settings.Mode.Value}, gain {_settings.Gain.Value:0.0}x)"
                : "Night vision OFF");
        }

        public void CycleMode()
        {
            var values = (NightVisionMode[])Enum.GetValues(typeof(NightVisionMode));
            var index = Array.IndexOf(values, _settings.Mode.Value);
            SetMode(values[(index + 1) % values.Length]);
        }

        public void SetMode(NightVisionMode mode)
        {
            _settings.Mode.Value = mode;

            if (!Active)
            {
                SetActive(true, instant: false);
            }

            NightVisionPlugin.Log.LogInfo($"Night vision mode: {mode}");
        }

        public void AdjustGain(float delta)
        {
            var value = Mathf.Clamp(_settings.Gain.Value + delta, 1f, 8f);
            if (Mathf.Approximately(value, _settings.Gain.Value))
            {
                return;
            }

            _settings.Gain.Value = value;
        }

        /// <summary>Rebuilds the filter, e.g. after the backend is changed in the settings panel.</summary>
        public void ReloadFilter()
        {
            SelectFilter();
            _filter?.Apply(Weight);
        }

        private void UpdateWeight()
        {
            var target = Active ? 1f : 0f;
            var fade = _settings.FadeSeconds.Value;

            Weight = fade <= 0f
                ? target
                : Mathf.MoveTowards(Weight, target, Time.unscaledDeltaTime / fade);
        }

        private void UpdateAutoMode()
        {
            if (!_settings.AutoEnableAtNight.Value)
            {
                _autoInitialised = false;
                return;
            }

            var dark = EstimateSceneBrightness() < _settings.AutoEnableThreshold.Value;

            if (!_autoInitialised)
            {
                _autoInitialised = true;
                _autoState = dark;
                SetActive(dark, instant: true);
                return;
            }

            if (dark == _autoState)
            {
                return; // No transition: leave whatever the player chose manually.
            }

            _autoState = dark;
            SetActive(dark, instant: false);
        }

        /// <summary>
        /// Rough 0..1 estimate of how bright the world currently is, from ambient light plus the
        /// strongest directional light.
        /// </summary>
        private static float EstimateSceneBrightness()
        {
            var ambient = RenderSettings.ambientLight.grayscale * Mathf.Max(0.01f, RenderSettings.ambientIntensity);
            var sun = RenderSettings.sun;
            var directional = 0f;

            if (sun != null && sun.isActiveAndEnabled)
            {
                directional = sun.color.grayscale * sun.intensity;
            }

            return Mathf.Clamp01(ambient + directional * 0.5f);
        }

        private void OnGUI()
        {
            _ui.Draw(Weight);
        }
    }
}
