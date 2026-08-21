using System.Collections.Generic;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Last-resort filter for games with no usable post-processing stack: composites the look
    /// with immediate-mode GL at the end of the *world* camera's rendering.
    /// <para>
    /// The camera choice is what keeps the interface clean. Rather than grabbing the
    /// highest-depth camera (which is usually the UI camera, and painting over it is exactly the
    /// "cheap overlay" problem), this deliberately targets the scene camera that renders first,
    /// so anything drawn afterwards — UI cameras, screen-space overlay canvases — lands on top
    /// of the effect untouched.
    /// </para>
    /// </summary>
    internal class OverlayFilter : INightVisionFilter
    {
        /// <summary>Unity's built-in UI layer.</summary>
        private const int UiLayer = 5;

        private readonly NightVisionSettings _settings;
        private readonly List<NightVisionScreenEffect> _effects = new List<NightVisionScreenEffect>();

        private float _nextScan;

        public OverlayFilter(NightVisionSettings settings)
        {
            _settings = settings;
        }

        public string Name => "GL overlay (no post-processing stack found)";

        public bool IsTruePostProcess => false;

        public bool TryInitialize()
        {
            if (Shader.Find("Hidden/Internal-Colored") == null)
            {
                NightVisionPlugin.Log.LogWarning("The built-in colored shader is missing; the overlay filter cannot run.");
                return false;
            }

            NightVisionPlugin.Log.LogWarning(
                "No post-processing stack was found, so night vision is falling back to a GL overlay. " +
                "It is drawn on the world camera only, but a few UI elements rendered by that same camera may be tinted.");
            return true;
        }

        public void Apply(float weight)
        {
            if (weight <= 0.001f)
            {
                SetWeights(0f);
                return;
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                RefreshCameras();
            }

            SetWeights(weight);
        }

        private void SetWeights(float weight)
        {
            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                if (_effects[i] == null)
                {
                    _effects.RemoveAt(i);
                    continue;
                }

                _effects[i].Weight = weight;
            }
        }

        private void RefreshCameras()
        {
            _effects.RemoveAll(effect => effect == null || effect.TargetCamera == null);

            if (_settings.AffectAllCameras.Value)
            {
                foreach (var camera in Camera.allCameras)
                {
                    if (IsWorldCamera(camera))
                    {
                        Attach(camera);
                    }
                }

                return;
            }

            var target = PickWorldCamera();
            if (target == null)
            {
                return;
            }

            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                if (_effects[i].TargetCamera != target)
                {
                    Detach(_effects[i]);
                }
            }

            Attach(target);
        }

        /// <summary>
        /// The camera that draws the sea and ships: renders to the screen, and its culling mask
        /// covers more than just the UI layer.
        /// </summary>
        private static Camera PickWorldCamera()
        {
            if (Camera.main != null && IsWorldCamera(Camera.main))
            {
                return Camera.main;
            }

            Camera best = null;

            foreach (var camera in Camera.allCameras)
            {
                if (!IsWorldCamera(camera))
                {
                    continue;
                }

                // Lowest depth = drawn first = everything else composites over our effect.
                if (best == null || camera.depth < best.depth)
                {
                    best = camera;
                }
            }

            return best;
        }

        private static bool IsWorldCamera(Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture != null)
            {
                return false;
            }

            // Skip cameras that only render the UI layer.
            var mask = camera.cullingMask;
            if (mask == 0 || mask == 1 << UiLayer)
            {
                return false;
            }

            var name = camera.name.ToLowerInvariant();
            if (name.Contains("ui") || name.Contains("hud") || name.Contains("overlay") || name.Contains("minimap"))
            {
                return false;
            }

            return true;
        }

        private void Attach(Camera camera)
        {
            foreach (var existing in _effects)
            {
                if (existing.TargetCamera == camera)
                {
                    return;
                }
            }

            var effect = camera.gameObject.GetComponent<NightVisionScreenEffect>()
                         ?? camera.gameObject.AddComponent<NightVisionScreenEffect>();

            effect.Bind(_settings);
            _effects.Add(effect);

            if (_settings.VerboseLogging.Value)
            {
                NightVisionPlugin.Log.LogInfo($"Overlay attached to world camera '{camera.name}' (depth {camera.depth}).");
            }
        }

        private void Detach(NightVisionScreenEffect effect)
        {
            _effects.Remove(effect);

            if (effect != null)
            {
                effect.Weight = 0f;
                Object.Destroy(effect);
            }
        }

        public void Dispose()
        {
            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                Detach(_effects[i]);
            }

            _effects.Clear();
        }
    }
}
