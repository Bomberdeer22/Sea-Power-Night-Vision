using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Owns the on/off state, listens for hotkeys, keeps the screen effect attached to the
    /// active camera(s) and drives the scene-lighting booster.
    /// </summary>
    public class NightVisionController : MonoBehaviour
    {
        private NightVisionSettings _settings;
        private SceneLightBooster _lightBooster;

        private readonly List<NightVisionScreenEffect> _effects = new List<NightVisionScreenEffect>();
        private GUIStyle _indicatorStyle;
        private float _nextCameraScan;
        private float _nextLightScan;
        private bool _autoState;
        private bool _autoInitialised;

        /// <summary>True while the operator has night vision switched on.</summary>
        public bool Active { get; private set; }

        /// <summary>0..1 fade weight used by the renderer so toggling is not a hard cut.</summary>
        public float Weight { get; private set; }

        private void Awake()
        {
            _settings = NightVisionPlugin.Settings;
            _lightBooster = new SceneLightBooster(_settings);

            NightVisionPlugin.Log.LogInfo("Night vision controller is running and listening for hotkeys.");

            if (_settings.EnabledOnStart.Value)
            {
                SetActive(true, instant: true);
            }
        }

        private void OnDestroy()
        {
            SetActive(false, instant: true);
            DetachAll();
        }

        private void Update()
        {
            HandleHotkeys();
            UpdateAutoMode();
            UpdateWeight();

            if (Weight <= 0f && !Active)
            {
                if (_effects.Count > 0)
                {
                    DetachAll();
                }

                return;
            }

            if (Time.unscaledTime >= _nextCameraScan)
            {
                _nextCameraScan = Time.unscaledTime + 0.5f;
                RefreshCameras();
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
            if (shortcut.MainKey == KeyCode.None)
            {
                return false;
            }

            if (!InputBridge.GetKeyDown(shortcut.MainKey))
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
            _settings.Mode.Value = values[(index + 1) % values.Length];

            if (!Active)
            {
                SetActive(true, instant: false);
            }

            NightVisionPlugin.Log.LogInfo($"Night vision mode: {_settings.Mode.Value}");
        }

        public void AdjustGain(float delta)
        {
            var value = Mathf.Clamp(_settings.Gain.Value + delta, 1f, 8f);
            if (Mathf.Approximately(value, _settings.Gain.Value))
            {
                return;
            }

            _settings.Gain.Value = value;
            NightVisionPlugin.Log.LogInfo($"Night vision gain: {value:0.0}x");
        }

        private void UpdateWeight()
        {
            var target = Active ? 1f : 0f;
            var fade = _settings.FadeSeconds.Value;

            if (fade <= 0f)
            {
                Weight = target;
                return;
            }

            Weight = Mathf.MoveTowards(Weight, target, Time.unscaledDeltaTime / fade);
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
        /// Rough 0..1 estimate of how bright the world currently is, from ambient light
        /// plus the strongest directional light.
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

        private void RefreshCameras()
        {
            _effects.RemoveAll(effect => effect == null || effect.TargetCamera == null);

            if (_settings.AffectAllCameras.Value)
            {
                foreach (var camera in Camera.allCameras)
                {
                    TryAttach(camera);
                }

                return;
            }

            var best = PickPrimaryCamera();
            if (best == null)
            {
                return;
            }

            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                if (_effects[i].TargetCamera != best)
                {
                    Detach(_effects[i]);
                }
            }

            TryAttach(best);
        }

        /// <summary>The camera the player is actually looking through: highest depth, rendering to the screen.</summary>
        private static Camera PickPrimaryCamera()
        {
            Camera best = null;

            foreach (var camera in Camera.allCameras)
            {
                if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture != null)
                {
                    continue;
                }

                if (best == null || camera.depth > best.depth)
                {
                    best = camera;
                }
            }

            return best != null ? best : Camera.main;
        }

        private void TryAttach(Camera camera)
        {
            if (camera == null || camera.targetTexture != null)
            {
                return;
            }

            for (var i = 0; i < _effects.Count; i++)
            {
                if (_effects[i].TargetCamera == camera)
                {
                    return;
                }
            }

            var effect = camera.gameObject.GetComponent<NightVisionScreenEffect>();
            if (effect == null)
            {
                effect = camera.gameObject.AddComponent<NightVisionScreenEffect>();
            }

            effect.Bind(this, _settings);
            _effects.Add(effect);

            if (_settings.VerboseLogging.Value)
            {
                NightVisionPlugin.Log.LogInfo($"Attached night vision effect to camera '{camera.name}' (depth {camera.depth}).");
            }
        }

        private void Detach(NightVisionScreenEffect effect)
        {
            _effects.Remove(effect);

            if (effect != null)
            {
                Destroy(effect);
            }
        }

        private void DetachAll()
        {
            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                Detach(_effects[i]);
            }

            _effects.Clear();
        }

        private void OnGUI()
        {
            if (!_settings.ShowIndicator.Value || Weight <= 0.01f)
            {
                return;
            }

            var tint = _settings.GetTintColor();
            var previous = GUI.color;
            GUI.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(Weight) * 0.85f);

            // GUIStyle instances may only be created inside OnGUI, so build it once and cache it.
            if (_indicatorStyle == null)
            {
                _indicatorStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft
                };
            }

            var label = $"NVG  {_settings.Mode.Value}  x{_settings.Gain.Value:0.0}";
            GUI.Label(new Rect(14f, Screen.height - 30f, 400f, 24f), label, _indicatorStyle);
            GUI.color = previous;
        }
    }
}
