using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// A genuine camera image effect for the built-in render pipeline.
    /// <para>
    /// This is not an overlay drawn over the finished frame. Unity calls
    /// <see cref="OnRenderImage"/> as part of the camera's own rendering, handing over the scene
    /// colour buffer before anything else is composited, so the filter is applied to the 3D image
    /// exactly like the game's own effects would be — and screen-space UI, which is drawn after
    /// the cameras, is left completely untouched.
    /// </para>
    /// <para>
    /// Two quality paths. If the optional shader is present (see <see cref="NightVisionShaders"/>)
    /// the whole thing is done in one blit with real luminance extraction, phosphor tint, gamma,
    /// halation, vignette, scanlines and animated grain. Otherwise it falls back to fixed-function
    /// blend passes with the always-available <c>Hidden/Internal-Colored</c> shader: gain
    /// (multiply), shadow lift (add), tint (modulate) and the tube artefacts. The fallback cannot
    /// mix colour channels, so its tint is per-channel rather than true monochrome.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class NightVisionScreenEffect : MonoBehaviour
    {
        private const int MaxGainPasses = 4;

        private NightVisionSettings _settings;
        private Material _blendMaterial;
        private Material _shaderMaterial;
        private bool _shaderChecked;
        private bool _broken;
        private float _noisePhase;

        public Camera TargetCamera { get; private set; }

        /// <summary>0..1 fade weight, driven by the owning filter.</summary>
        public float Weight { get; set; }

        /// <summary>True when the high-quality shader path is in use.</summary>
        public bool UsingShader => _shaderMaterial != null;

        internal void Bind(NightVisionSettings settings)
        {
            _settings = settings;
            TargetCamera = GetComponent<Camera>();
        }

        private void OnDestroy()
        {
            if (_blendMaterial != null)
            {
                DestroyImmediate(_blendMaterial);
                _blendMaterial = null;
            }

            if (_shaderMaterial != null)
            {
                DestroyImmediate(_shaderMaterial);
                _shaderMaterial = null;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var weight = Mathf.Clamp01(Weight);

            if (_broken || _settings == null || weight <= 0.001f)
            {
                Graphics.Blit(source, destination);
                return;
            }

            EnsureShaderMaterial();

            if (_shaderMaterial != null)
            {
                RenderWithShader(source, destination, weight);
                return;
            }

            // Fixed-function path: copy the frame through, then composite onto it.
            Graphics.Blit(source, destination);

            if (!EnsureBlendMaterial())
            {
                return;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = destination;

            var width = destination != null ? destination.width : Screen.width;
            var height = destination != null ? destination.height : Screen.height;
            RenderPasses(weight, width, height);

            RenderTexture.active = previous;
        }

        private void EnsureShaderMaterial()
        {
            if (_shaderChecked)
            {
                return;
            }

            _shaderChecked = true;

            var shader = NightVisionShaders.Get();
            if (shader != null)
            {
                _shaderMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        private void RenderWithShader(RenderTexture source, RenderTexture destination, float weight)
        {
            var tint = _settings.GetTintColor();

            _shaderMaterial.SetFloat("_Weight", weight);
            _shaderMaterial.SetFloat("_Gain", Mathf.Max(1f, _settings.Gain.Value * _settings.GetModeGainScale()));
            _shaderMaterial.SetFloat("_Lift", _settings.ShadowLift.Value);
            _shaderMaterial.SetFloat("_Contrast", 1f + _settings.Contrast.Value / 100f);
            _shaderMaterial.SetFloat("_TintStrength", _settings.GetEffectiveTintStrength());
            _shaderMaterial.SetColor("_TintColor", tint);
            _shaderMaterial.SetFloat("_Glow", _settings.TubeGlow.Value);
            _shaderMaterial.SetFloat("_Vignette", _settings.Vignette.Value ? _settings.VignetteStrength.Value : 0f);
            _shaderMaterial.SetFloat("_Noise", _settings.SensorNoise.Value);
            _shaderMaterial.SetFloat("_Scanlines", _settings.Scanlines.Value ? _settings.ScanlineStrength.Value : 0f);
            _shaderMaterial.SetFloat("_ScanlineSpacing", Mathf.Max(2, _settings.ScanlineSpacing.Value));
            _shaderMaterial.SetFloat("_Time01", Time.unscaledTime);

            Graphics.Blit(source, destination, _shaderMaterial);
        }

        private void RenderPasses(float weight, int width, int height)
        {
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
                DrawScanlines(_settings.ScanlineStrength.Value * weight, _settings.ScanlineSpacing.Value, width, height);
            }

            if (_settings.SensorNoise.Value > 0.001f)
            {
                DrawNoise(tint, _settings.SensorNoise.Value * weight, width, height);
            }
        }

        private bool EnsureBlendMaterial()
        {
            if (_blendMaterial != null)
            {
                return true;
            }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                _broken = true;
                NightVisionPlugin.Log.LogError(
                    "Could not find the built-in 'Hidden/Internal-Colored' shader; the image effect is disabled. " +
                    "World lighting boost still works.");
                return false;
            }

            _blendMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _blendMaterial.SetInt("_ZWrite", 0);
            _blendMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _blendMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return true;
        }

        private void SetBlend(UnityEngine.Rendering.BlendMode source, UnityEngine.Rendering.BlendMode destination)
        {
            _blendMaterial.SetInt("_SrcBlend", (int)source);
            _blendMaterial.SetInt("_DstBlend", (int)destination);
            _blendMaterial.SetPass(0);
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
                SetBlend(UnityEngine.Rendering.BlendMode.DstColor, UnityEngine.Rendering.BlendMode.One);
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
            SetBlend(UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One);
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
            SetBlend(UnityEngine.Rendering.BlendMode.Zero, UnityEngine.Rendering.BlendMode.SrcColor);
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
            SetBlend(UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            GL.Begin(GL.QUADS);

            GradientQuad(0f, 0f, band, 1f, outer, inner, horizontal: true);
            GradientQuad(1f - band, 0f, 1f, 1f, inner, outer, horizontal: true);
            GradientQuad(0f, 0f, 1f, band, outer, inner, horizontal: false);
            GradientQuad(0f, 1f - band, 1f, 1f, inner, outer, horizontal: false);

            GL.End();
            GL.PopMatrix();
        }

        private void DrawScanlines(float strength, int spacing, int width, int height)
        {
            if (strength <= 0.001f)
            {
                return;
            }

            var step = Mathf.Max(2, spacing);
            var color = new Color(0f, 0f, 0f, Mathf.Clamp01(strength));

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, 0f, height);
            SetBlend(UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            GL.Begin(GL.QUADS);
            GL.Color(color);

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

        private void DrawNoise(Color tint, float amount, int width, int height)
        {
            var count = Mathf.RoundToInt(Mathf.Clamp01(amount) * 1500f);
            if (count <= 0)
            {
                return;
            }

            _noisePhase += Time.unscaledDeltaTime;
            var random = new System.Random(unchecked((int)(_noisePhase * 1000f)) ^ Time.frameCount);
            var brightness = 0.10f + 0.25f * amount;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, 0f, height);
            SetBlend(UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One);
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
