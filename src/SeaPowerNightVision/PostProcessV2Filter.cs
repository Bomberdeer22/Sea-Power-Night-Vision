using System;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Fallback for games on the built-in render pipeline that use the legacy Post Processing
    /// Stack v2 package. Same idea as <see cref="VolumeFilter"/>: a global volume driving the
    /// stock Color Grading / Bloom / Vignette / Grain effects, applied inside the camera's
    /// post-processing chain and therefore underneath the UI.
    /// </summary>
    internal class PostProcessV2Filter : INightVisionFilter
    {
        private readonly NightVisionSettings _settings;

        private GameObject _host;
        private Component _volume;
        private ScriptableObject _profile;

        private object _colorGrading;
        private object _vignette;
        private object _grain;
        private object _bloom;

        private float _lastWeight = -1f;
        private float _gainScale = 1f;
        private int _lastSettingsHash;

        public PostProcessV2Filter(NightVisionSettings settings)
        {
            _settings = settings;
        }

        public string Name => "Post Processing Stack v2";

        public bool IsTruePostProcess => true;

        public bool TryInitialize()
        {
            try
            {
                var volumeType = Reflect.FindType("UnityEngine.Rendering.PostProcessing.PostProcessVolume");
                var profileType = Reflect.FindType("UnityEngine.Rendering.PostProcessing.PostProcessProfile");
                var settingsType = Reflect.FindType("UnityEngine.Rendering.PostProcessing.PostProcessEffectSettings");

                if (volumeType == null || profileType == null || settingsType == null)
                {
                    return false;
                }

                var colorGradingType = Reflect.FindDerivedType(settingsType, "ColorGrading");
                if (colorGradingType == null)
                {
                    return false;
                }

                _host = new GameObject("SeaPowerNightVisionVolume");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.layer = 0;

                _volume = _host.AddComponent(volumeType);
                Reflect.SetMember(_volume, "isGlobal", true);
                Reflect.SetMember(_volume, "priority", 10000f);
                Reflect.SetMember(_volume, "weight", 0f);

                _profile = ScriptableObject.CreateInstance(profileType);
                _profile.hideFlags = HideFlags.HideAndDontSave;
                Reflect.SetMember(_volume, "sharedProfile", _profile);

                _colorGrading = AddSettings(settingsType, "ColorGrading");
                _vignette = AddSettings(settingsType, "Vignette");
                _grain = AddSettings(settingsType, "Grain");
                _bloom = AddSettings(settingsType, "Bloom");

                if (_colorGrading == null)
                {
                    Dispose();
                    return false;
                }

                NightVisionPlugin.Log.LogInfo("Using Post Processing Stack v2 for the night vision filter.");
                return true;
            }
            catch (Exception e)
            {
                NightVisionPlugin.Log.LogWarning("PPv2 post-processing unavailable: " + e.Message);
                Dispose();
                return false;
            }
        }

        private object AddSettings(Type settingsBase, string shortName)
        {
            var type = Reflect.FindDerivedType(settingsBase, shortName);
            if (type == null)
            {
                return null;
            }

            try
            {
                var method = _profile.GetType().GetMethod("AddSettings", new[] { typeof(Type) });
                var instance = method?.Invoke(_profile, new object[] { type });
                MakeBoolParameter(instance, true);
                return instance;
            }
            catch (Exception e)
            {
                NightVisionPlugin.Log.LogWarning($"Could not add the {shortName} effect: {e.Message}");
                return null;
            }
        }

        /// <summary>PPv2 wraps even 'enabled' in a BoolParameter, so set it in place.</summary>
        private static void MakeBoolParameter(object instance, bool value)
        {
            var existing = Reflect.GetMember(instance, "enabled");
            if (existing != null)
            {
                Reflect.SetMember(existing, "value", value);
                Reflect.SetMember(existing, "overrideState", true);
            }
        }

        public void Apply(float weight, float gainScale)
        {
            _gainScale = gainScale;

            if (_volume == null)
            {
                return;
            }

            if (!Mathf.Approximately(weight, _lastWeight))
            {
                _lastWeight = weight;
                Reflect.SetMember(_volume, "weight", Mathf.Clamp01(weight));
            }

            var hash = _settings.GetVisualHash();
            if (hash == _lastSettingsHash)
            {
                return;
            }

            _lastSettingsHash = hash;
            Configure();
        }

        private void Configure()
        {
            var tint = _settings.GetTintColor();
            var tintStrength = _settings.GetEffectiveTintStrength();
            var gain = Mathf.Max(1f, _settings.Gain.Value * _settings.GetModeGainScale());
            var exposureEv = Mathf.Log(Mathf.Max(1f, gain * _gainScale), 2f) * 2f;

            Reflect.SetParameter(_colorGrading, "postExposure", exposureEv);
            Reflect.SetParameter(_colorGrading, "contrast", _settings.Contrast.Value);
            Reflect.SetParameter(_colorGrading, "saturation", -100f * tintStrength);
            Reflect.SetParameter(_colorGrading, "colorFilter", Color.Lerp(Color.white, tint, tintStrength));

            if (_vignette != null)
            {
                Reflect.SetParameter(_vignette, "intensity", _settings.Vignette.Value ? _settings.VignetteStrength.Value * 0.6f : 0f);
                Reflect.SetParameter(_vignette, "smoothness", 0.6f);
                Reflect.SetParameter(_vignette, "color", Color.black);
            }

            if (_grain != null)
            {
                Reflect.SetParameter(_grain, "intensity", _settings.SensorNoise.Value);
                Reflect.SetParameter(_grain, "size", 1.2f);
                Reflect.SetParameter(_grain, "colored", false);
            }

            if (_bloom != null)
            {
                Reflect.SetParameter(_bloom, "intensity", _settings.TubeGlow.Value * 3f);
                Reflect.SetParameter(_bloom, "threshold", 0.7f);
                Reflect.SetParameter(_bloom, "color", tint);
            }
        }

        public void Dispose()
        {
            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
            }

            if (_profile != null)
            {
                UnityEngine.Object.Destroy(_profile);
                _profile = null;
            }

            _volume = null;
            _colorGrading = null;
            _vignette = null;
            _grain = null;
            _bloom = null;
            _lastWeight = -1f;
            _lastSettingsHash = 0;
        }
    }
}
