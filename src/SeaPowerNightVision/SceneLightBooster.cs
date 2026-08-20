using System.Collections.Generic;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Raises the world's ambient light, scene light intensities and cuts haze while night
    /// vision is on, then puts everything back exactly as it was when it is switched off.
    /// <para>
    /// This is the half of the mod that makes ships actually visible: brightening the
    /// framebuffer alone only amplifies whatever few photons the renderer produced, whereas
    /// lifting ambient light reveals unlit hull surfaces the way an intensifier tube does.
    /// </para>
    /// </summary>
    internal class SceneLightBooster
    {
        private class LightState
        {
            public Light Light;
            public float OriginalIntensity;
            public float AppliedIntensity;
        }

        private readonly NightVisionSettings _settings;
        private readonly List<LightState> _lights = new List<LightState>();

        private bool _applied;
        private float _lastWeight = -1f;

        private Color _originalAmbientLight;
        private Color _originalAmbientSky;
        private Color _originalAmbientEquator;
        private Color _originalAmbientGround;
        private float _originalAmbientIntensity;
        private float _originalFogDensity;
        private Color _originalFogColor;
        private float _originalFogStart;
        private float _originalFogEnd;

        public SceneLightBooster(NightVisionSettings settings)
        {
            _settings = settings;
        }

        /// <summary>Captures the untouched state (once) and applies the boost at the given fade weight.</summary>
        public void Apply(float weight)
        {
            if (weight <= 0.001f)
            {
                Restore();
                return;
            }

            if (!_applied)
            {
                CaptureEnvironment();
                _applied = true;
            }

            RescanLights();
            ApplyWeight(weight, force: true);
        }

        /// <summary>Cheap per-frame update that only re-applies values when the fade weight changed.</summary>
        public void UpdateWeight(float weight)
        {
            if (!_applied)
            {
                if (weight > 0.001f)
                {
                    Apply(weight);
                }

                return;
            }

            if (weight <= 0.001f)
            {
                Restore();
                return;
            }

            ApplyWeight(weight, force: false);
        }

        /// <summary>Puts ambient light, lights and fog back to their captured values.</summary>
        public void Restore()
        {
            if (!_applied)
            {
                return;
            }

            RenderSettings.ambientLight = _originalAmbientLight;
            RenderSettings.ambientSkyColor = _originalAmbientSky;
            RenderSettings.ambientEquatorColor = _originalAmbientEquator;
            RenderSettings.ambientGroundColor = _originalAmbientGround;
            RenderSettings.ambientIntensity = _originalAmbientIntensity;
            RenderSettings.fogDensity = _originalFogDensity;
            RenderSettings.fogColor = _originalFogColor;
            RenderSettings.fogStartDistance = _originalFogStart;
            RenderSettings.fogEndDistance = _originalFogEnd;

            foreach (var state in _lights)
            {
                if (state.Light == null)
                {
                    continue;
                }

                // Only restore if nothing else changed the light in the meantime, so we never
                // fight the game's own time-of-day lighting.
                if (Mathf.Abs(state.Light.intensity - state.AppliedIntensity) < 0.001f)
                {
                    state.Light.intensity = state.OriginalIntensity;
                }
            }

            _lights.Clear();
            _applied = false;
            _lastWeight = -1f;

            if (_settings.VerboseLogging.Value)
            {
                NightVisionPlugin.Log.LogInfo("Restored original scene lighting.");
            }
        }

        private void CaptureEnvironment()
        {
            _originalAmbientLight = RenderSettings.ambientLight;
            _originalAmbientSky = RenderSettings.ambientSkyColor;
            _originalAmbientEquator = RenderSettings.ambientEquatorColor;
            _originalAmbientGround = RenderSettings.ambientGroundColor;
            _originalAmbientIntensity = RenderSettings.ambientIntensity;
            _originalFogDensity = RenderSettings.fogDensity;
            _originalFogColor = RenderSettings.fogColor;
            _originalFogStart = RenderSettings.fogStartDistance;
            _originalFogEnd = RenderSettings.fogEndDistance;
        }

        private void RescanLights()
        {
            _lights.RemoveAll(state => state.Light == null);

            var known = new HashSet<Light>();
            foreach (var state in _lights)
            {
                known.Add(state.Light);
            }

            foreach (var light in Object.FindObjectsOfType<Light>())
            {
                if (light == null || known.Contains(light))
                {
                    continue;
                }

                // Leave weapon flashes and explosion lights alone: they are already bright and
                // amplifying them looks awful.
                if (light.intensity > 8f)
                {
                    continue;
                }

                _lights.Add(new LightState
                {
                    Light = light,
                    OriginalIntensity = light.intensity,
                    AppliedIntensity = light.intensity
                });
            }

            if (_settings.VerboseLogging.Value)
            {
                NightVisionPlugin.Log.LogInfo($"Night vision is boosting {_lights.Count} scene light(s).");
            }
        }

        private void ApplyWeight(float weight, bool force)
        {
            if (!force && Mathf.Abs(weight - _lastWeight) < 0.005f)
            {
                return;
            }

            _lastWeight = weight;

            var ambientScale = Mathf.Lerp(1f, Mathf.Max(1f, _settings.AmbientBoost.Value), weight);
            var lightScale = Mathf.Lerp(1f, Mathf.Max(1f, _settings.LightBoost.Value), weight);
            var fogScale = Mathf.Lerp(1f, 1f - Mathf.Clamp01(_settings.FogReduction.Value), weight);

            RenderSettings.ambientLight = ScaleColor(_originalAmbientLight, ambientScale);
            RenderSettings.ambientSkyColor = ScaleColor(_originalAmbientSky, ambientScale);
            RenderSettings.ambientEquatorColor = ScaleColor(_originalAmbientEquator, ambientScale);
            RenderSettings.ambientGroundColor = ScaleColor(_originalAmbientGround, ambientScale);
            RenderSettings.ambientIntensity = _originalAmbientIntensity * ambientScale;

            RenderSettings.fogDensity = _originalFogDensity * fogScale;
            RenderSettings.fogStartDistance = _originalFogStart;
            RenderSettings.fogEndDistance = _originalFogEnd <= 0f
                ? _originalFogEnd
                : Mathf.Lerp(_originalFogEnd, _originalFogEnd * 2f, weight * Mathf.Clamp01(_settings.FogReduction.Value));

            foreach (var state in _lights)
            {
                if (state.Light == null)
                {
                    continue;
                }

                // If the game moved the light's intensity itself, adopt the new value as the baseline.
                if (Mathf.Abs(state.Light.intensity - state.AppliedIntensity) > 0.001f)
                {
                    state.OriginalIntensity = state.Light.intensity;
                }

                var applied = state.OriginalIntensity * lightScale;
                state.Light.intensity = applied;
                state.AppliedIntensity = applied;
            }
        }

        private static Color ScaleColor(Color color, float scale)
        {
            return new Color(
                Mathf.Clamp01(color.r * scale),
                Mathf.Clamp01(color.g * scale),
                Mathf.Clamp01(color.b * scale),
                color.a);
        }
    }
}
