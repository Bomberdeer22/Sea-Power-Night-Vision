using System.Collections.Generic;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Filter for the built-in render pipeline: installs a real camera image effect
    /// (<c>OnRenderImage</c>) on the world camera.
    /// <para>
    /// Unity runs image effects as part of the camera's rendering, on the scene colour buffer,
    /// before anything else is composited — so this is a true in-engine filter rather than
    /// something painted over the finished frame. Screen-space UI is drawn after the cameras and
    /// is therefore untouched.
    /// </para>
    /// <para>
    /// The camera choice matters just as much: targeting the highest-depth camera would grab the
    /// UI camera and filter the interface, which is precisely the "cheap overlay" failure mode.
    /// This deliberately picks the scene camera instead.
    /// </para>
    /// </summary>
    internal class OverlayFilter : INightVisionFilter
    {
        /// <summary>Unity's built-in UI layer.</summary>
        private const int UiLayer = 5;

        private readonly NightVisionSettings _settings;
        private readonly List<NightVisionScreenEffect> _effects = new List<NightVisionScreenEffect>();

        private float _nextScan;
        private float _gainScale = 1f;

        public OverlayFilter(NightVisionSettings settings)
        {
            _settings = settings;
        }

        public string Name { get; private set; } = "Built-in pipeline image effect";

        public bool IsTruePostProcess => true;

        public bool TryInitialize()
        {
            if (Shader.Find("Hidden/Internal-Colored") == null)
            {
                NightVisionPlugin.Log.LogWarning("The built-in colored shader is missing; the overlay filter cannot run.");
                return false;
            }

            if (NightVisionShaders.Get() != null)
            {
                Name = "Built-in pipeline image effect (shader)";
                NightVisionPlugin.Log.LogInfo(
                    "Night vision will run as a camera image effect using the full-quality shader.");
            }
            else
            {
                Name = "Built-in pipeline image effect (fixed-function)";
                NightVisionPlugin.Log.LogInfo(
                    "Night vision will run as a camera image effect on the world camera. " +
                    "No night vision shader was found, so the fixed-function path is used: gain, tint, " +
                    "vignette and grain all work, but the tint is per-channel rather than true monochrome. " +
                    "Drop nightvision.bundle next to the DLL for the full-quality look (see unity/README.md).");
            }

            return true;
        }

        public void Apply(float weight, float gainScale)
        {
            _gainScale = gainScale;

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
                _effects[i].GainScale = _gainScale;
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
