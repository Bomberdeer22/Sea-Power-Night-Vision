using UnityEngine;
using UnityEngine.Rendering;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Draws the image-intensifier look straight onto the camera's framebuffer using
    /// immediate-mode GL and Unity's built-in <c>Hidden/Internal-Colored</c> shader.
    /// <para>
    /// No custom shader or asset bundle is needed, which keeps the mod a single DLL and
    /// makes it robust against game updates. The effect is composed from four blended
    /// full-screen passes: gain (multiply), shadow lift (additive), phosphor tint
    /// (modulate) and the optional tube artefacts (vignette / scanlines / grain).
    /// </para>
    /// <para>
    /// Because this runs at the end of camera rendering, screen-space overlay UI is drawn
    /// afterwards and stays perfectly readable.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class NightVisionScreenEffect : MonoBehaviour
    {
        private const int MaxGainPasses = 4;

        private NightVisionController _controller;
        private NightVisionSettings _settings;
        private Material _material;
        private bool _srpMode;
        private bool _broken;
        private float _noisePhase;

        public Camera TargetCamera { get; private set; }

        internal void Bind(NightVisionController controller, NightVisionSettings settings)
        {
            _controller = controller;
            _settings = settings;
            TargetCamera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (TargetCamera == null)
            {
                TargetCamera = GetComponent<Camera>();
            }

            _srpMode = GraphicsSettings.currentRenderPipeline != null;

            if (_srpMode)
            {
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            }
        }

        private void OnDisable()
        {
            if (_srpMode)
            {
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                DestroyImmediate(_material);
                _material = null;
            }
        }

        // Built-in render pipeline path.
        private void OnPostRender()
        {
            if (!_srpMode)
            {
                Render();
            }
        }

        // Scriptable render pipeline path (URP/HDRP), in case the game ever moves to one.
        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == TargetCamera)
            {
                Render();
            }
        }

        private void Render()
        {
            if (_broken || _controller == null || _settings == null)
            {
                return;
            }

            var weight = Mathf.Clamp01(_controller.Weight);
            if (weight <= 0.001f)
            {
                return;
            }

            if (!EnsureMaterial())
            {
                return;
            }

            var gain = Mathf.Lerp(1f, Mathf.Max(1f, _settings.Gain.Value * _settings.GetModeGainScale()), weight);
            var tint = _settings.GetTintColor();

            DrawGain(gain);
            DrawShadowLift(tint, _settings.ShadowLift.Value * weight);
            DrawTint(tint, _settings.GetEffectiveTintStrength() * weight);

            if (_settings.Vignette.Value)
            {
                DrawVignette(_settings.VignetteStrength.Value * weight);
            }

            if (_settings.Scanlines.Value)
            {
                DrawScanlines(_settings.ScanlineStrength.Value * weight, _settings.ScanlineSpacing.Value);
            }

            if (_settings.SensorNoise.Value > 0.001f)
            {
                DrawNoise(tint, _settings.SensorNoise.Value * weight);
            }
        }

        private bool EnsureMaterial()
        {
            if (_material != null)
            {
                return true;
            }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                _broken = true;
                NightVisionPlugin.Log.LogError(
                    "Could not find the built-in 'Hidden/Internal-Colored' shader; the screen effect is disabled. " +
                    "World lighting boost still works.");
                return false;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.Always);
            _material.SetInt("_Cull", (int)CullMode.Off);
            return true;
        }

        private void SetBlend(BlendMode source, BlendMode destination)
        {
            _material.SetInt("_SrcBlend", (int)source);
            _material.SetInt("_DstBlend", (int)destination);
            _material.SetPass(0);
        }

        /// <summary>
        /// Multiplies the frame by <paramref name="gain"/> using <c>Blend DstColor One</c>,
        /// which yields <c>dst * (1 + c)</c>. Values above 2x are applied over several passes.
        /// </summary>
        private void DrawGain(float gain)
        {
            var remaining = Mathf.Clamp(gain, 1f, 16f);

            for (var pass = 0; pass < MaxGainPasses && remaining > 1.001f; pass++)
            {
                var step = Mathf.Min(2f, remaining);
                var c = Mathf.Clamp01(step - 1f);

                GL.PushMatrix();
                GL.LoadOrtho();
                SetBlend(BlendMode.DstColor, BlendMode.One);
                FullScreenQuad(new Color(c, c, c, 1f));
                GL.PopMatrix();

                remaining /= step;
            }
        }

        /// <summary>Additive floor so genuinely black pixels still glow like a real tube.</summary>
        private void DrawShadowLift(Color tint, float amount)
        {
            if (amount <= 0.001f)
            {
                return;
            }

            GL.PushMatrix();
            GL.LoadOrtho();
            SetBlend(BlendMode.One, BlendMode.One);
            FullScreenQuad(new Color(tint.r * amount, tint.g * amount, tint.b * amount, 1f));
            GL.PopMatrix();
        }

        /// <summary>Modulates the frame by the phosphor colour (<c>Blend Zero SrcColor</c>).</summary>
        private void DrawTint(Color tint, float strength)
        {
            if (strength <= 0.001f)
            {
                return;
            }

            var c = Color.Lerp(Color.white, tint, strength);

            GL.PushMatrix();
            GL.LoadOrtho();
            SetBlend(BlendMode.Zero, BlendMode.SrcColor);
            FullScreenQuad(new Color(c.r, c.g, c.b, 1f));
            GL.PopMatrix();
        }

        private void DrawVignette(float strength)
        {
            if (strength <= 0.001f)
            {
                return;
            }

            const float band = 0.32f;
            var outer = new Color(0f, 0f, 0f, Mathf.Clamp01(strength));
            var inner = new Color(0f, 0f, 0f, 0f);

            GL.PushMatrix();
            GL.LoadOrtho();
            SetBlend(BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
            GL.Begin(GL.QUADS);

            // Left
            GradientQuad(0f, 0f, band, 1f, outer, inner, horizontal: true);
            // Right
            GradientQuad(1f - band, 0f, 1f, 1f, inner, outer, horizontal: true);
            // Bottom
            GradientQuad(0f, 0f, 1f, band, outer, inner, horizontal: false);
            // Top
            GradientQuad(0f, 1f - band, 1f, 1f, inner, outer, horizontal: false);

            GL.End();
            GL.PopMatrix();
        }

        private void DrawScanlines(float strength, int spacing)
        {
            if (strength <= 0.001f)
            {
                return;
            }

            var height = Screen.height;
            var step = Mathf.Max(2, spacing);
            var color = new Color(0f, 0f, 0f, Mathf.Clamp01(strength));

            GL.PushMatrix();
            GL.LoadPixelMatrix();
            SetBlend(BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
            GL.Begin(GL.QUADS);
            GL.Color(color);

            var width = Screen.width;
            for (var y = 0; y < height; y += step)
            {
                GL.Vertex3(0f, y, 0f);
                GL.Vertex3(0f, y + 1f, 0f);
                GL.Vertex3(width, y + 1f, 0f);
                GL.Vertex3(width, y, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void DrawNoise(Color tint, float amount)
        {
            var count = Mathf.RoundToInt(Mathf.Clamp01(amount) * 1500f);
            if (count <= 0)
            {
                return;
            }

            _noisePhase += Time.unscaledDeltaTime;
            var random = new System.Random(unchecked((int)(_noisePhase * 1000f)) ^ Time.frameCount);
            var width = Screen.width;
            var height = Screen.height;
            var brightness = 0.10f + 0.25f * amount;

            GL.PushMatrix();
            GL.LoadPixelMatrix();
            SetBlend(BlendMode.One, BlendMode.One);
            GL.Begin(GL.QUADS);

            for (var i = 0; i < count; i++)
            {
                var x = (float)random.NextDouble() * width;
                var y = (float)random.NextDouble() * height;
                var intensity = brightness * (float)random.NextDouble();
                GL.Color(new Color(tint.r * intensity, tint.g * intensity, tint.b * intensity, 1f));

                GL.Vertex3(x, y, 0f);
                GL.Vertex3(x, y + 2f, 0f);
                GL.Vertex3(x + 2f, y + 2f, 0f);
                GL.Vertex3(x + 2f, y, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        private static void FullScreenQuad(Color color)
        {
            GL.Begin(GL.QUADS);
            GL.Color(color);
            GL.Vertex3(0f, 0f, 0f);
            GL.Vertex3(0f, 1f, 0f);
            GL.Vertex3(1f, 1f, 0f);
            GL.Vertex3(1f, 0f, 0f);
            GL.End();
        }

        /// <summary>Emits one gradient quad; must be called between GL.Begin(GL.QUADS)/GL.End().</summary>
        private static void GradientQuad(float x0, float y0, float x1, float y1, Color a, Color b, bool horizontal)
        {
            if (horizontal)
            {
                GL.Color(a);
                GL.Vertex3(x0, y0, 0f);
                GL.Vertex3(x0, y1, 0f);
                GL.Color(b);
                GL.Vertex3(x1, y1, 0f);
                GL.Vertex3(x1, y0, 0f);
            }
            else
            {
                GL.Color(a);
                GL.Vertex3(x0, y0, 0f);
                GL.Color(b);
                GL.Vertex3(x0, y1, 0f);
                GL.Vertex3(x1, y1, 0f);
                GL.Color(a);
                GL.Vertex3(x1, y0, 0f);
            }
        }
    }
}
