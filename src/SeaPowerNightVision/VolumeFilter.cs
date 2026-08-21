using System;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// The good one: injects a global <c>Volume</c> into the game's scriptable render pipeline
    /// (URP or HDRP) and drives the stock post-processing overrides — exposure, saturation,
    /// colour filter, contrast, bloom, vignette and film grain.
    /// <para>
    /// Because this is the pipeline's own post-processing, it is applied to the rendered scene
    /// before the interface is composited on top. The UI is therefore completely unaffected, and
    /// the result is a proper tone-mapped image rather than a coloured quad drawn over the frame.
    /// The <c>Volume</c>'s weight gives a free, perfectly smooth fade when toggling.
    /// </para>
    /// </summary>
    internal class VolumeFilter : INightVisionFilter
    {
        private readonly NightVisionSettings _settings;

        private GameObject _host;
        private object _volume;
        private ScriptableObject _profile;

        private object _colorAdjustments;
        private object _vignette;
        private object _filmGrain;
        private object _bloom;

        private float _lastWeight = -1f;
        private float _gainScale = 1f;
        private int _lastSettingsHash;

        public VolumeFilter(NightVisionSettings settings)
        {
            _settings = settings;
        }

        public string Name { get; private set; } = "Volume post-processing";

        public bool IsTruePostProcess => true;

        public bool TryInitialize()
        {
            try
            {
                var volumeType = Reflect.FindType("UnityEngine.Rendering.Volume");
                var profileType = Reflect.FindType("UnityEngine.Rendering.VolumeProfile");
                var componentType = Reflect.FindType("UnityEngine.Rendering.VolumeComponent");

                if (volumeType == null || profileType == null || componentType == null)
                {
                    return false;
                }

                // ColorAdjustments is the one override we cannot do without.
                var colorAdjustmentsType = Reflect.FindDerivedType(componentType, "ColorAdjustments");
                if (colorAdjustmentsType == null)
                {
                    return false;
                }

                _host = new GameObject("SeaPowerNightVisionVolume");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.layer = 0; // Default layer, which every camera's volume mask includes.

                _volume = _host.AddComponent(volumeType);
                Reflect.SetMember(_volume, "isGlobal", true);
                Reflect.SetMember(_volume, "priority", 10000f); // Win against the game's own volumes.
                Reflect.SetMember(_volume, "weight", 0f);

                _profile = ScriptableObject.CreateInstance(profileType);
                _profile.hideFlags = HideFlags.HideAndDontSave;
                if (!Reflect.SetMember(_volume, "sharedProfile", _profile))
                {
                    Reflect.SetMember(_volume, "profile", _profile);
                }

                _colorAdjustments = AddOverride(componentType, "ColorAdjustments");
                _vignette = AddOverride(componentType, "Vignette");
                _filmGrain = AddOverride(componentType, "FilmGrain");
                _bloom = AddOverride(componentType, "Bloom");

                if (_colorAdjustments == null)
                {
                    Dispose();
                    return false;
                }

                var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                Name = (pipeline != null ? pipeline.GetType().Name.Replace("Asset", "") : "SRP") + " volume post-processing";

                NightVisionPlugin.Log.LogInfo(
                    $"Using {Name}. Overrides: ColorAdjustments" +
                    (_vignette != null ? ", Vignette" : "") +
                    (_filmGrain != null ? ", FilmGrain" : "") +
                    (_bloom != null ? ", Bloom" : ""));

                return true;
            }
            catch (Exception e)
            {
                NightVisionPlugin.Log.LogWarning("Volume post-processing unavailable: " + e.Message);
                Dispose();
                return false;
            }
        }

        private object AddOverride(Type componentType, string shortName)
        {
            var type = Reflect.FindDerivedType(componentType, shortName);
            if (type == null)
            {
                return null;
            }

            try
            {
                var add = _profile.GetType().GetMethod("Add", new[] { typeof(Type), typeof(bool) });
                return add?.Invoke(_profile, new object[] { type, true });
            }
            catch (Exception e)
            {
                NightVisionPlugin.Log.LogWarning($"Could not add the {shortName} override: {e.Message}");
                return null;
            }
        }

        public void Apply(float weight, float gainScale)
        {
            _gainScale = gainScale;

            if (_volume == null)
            {
                return;
            }

            // The volume weight does the fading for us, in the pipeline, for free.
            if (!Mathf.Approximately(weight, _lastWeight))
            {
                _lastWeight = weight;
                Reflect.SetMember(_volume, "weight", Mathf.Clamp01(weight));
            }

            // Only rebuild the override values when the player actually changes something.
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

            // Gain is expressed to the player as a multiplier; exposure wants stops (EV).
            var exposureEv = Mathf.Log(Mathf.Max(1f, gain * _gainScale), 2f) * 2f;

            Reflect.SetParameter(_colorAdjustments, "postExposure", exposureEv);
            Reflect.SetParameter(_colorAdjustments, "contrast", _settings.Contrast.Value);
            Reflect.SetParameter(_colorAdjustments, "saturation", -100f * tintStrength);
            Reflect.SetParameter(_colorAdjustments, "colorFilter", Color.Lerp(Color.white, tint, tintStrength));

            if (_vignette != null)
            {
                Reflect.SetParameter(_vignette, "intensity", _settings.Vignette.Value ? _settings.VignetteStrength.Value * 0.6f : 0f);
                Reflect.SetParameter(_vignette, "smoothness", 0.6f);
                Reflect.SetParameter(_vignette, "color", Color.black);
            }

            if (_filmGrain != null)
            {
                Reflect.SetParameter(_filmGrain, "intensity", _settings.SensorNoise.Value);
                Reflect.SetParameter(_filmGrain, "response", 0.8f);
            }

            if (_bloom != null)
            {
                // A little halation around bright sources is what really sells an intensifier.
                Reflect.SetParameter(_bloom, "intensity", _settings.TubeGlow.Value);
                Reflect.SetParameter(_bloom, "threshold", 0.7f);
                Reflect.SetParameter(_bloom, "scatter", 0.75f);
                Reflect.SetParameter(_bloom, "tint", tint);
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
            _colorAdjustments = null;
            _vignette = null;
            _filmGrain = null;
            _bloom = null;
            _lastWeight = -1f;
            _lastSettingsHash = 0;
        }
    }
}
