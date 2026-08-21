using UnityEngine;
using UnityEngine.Rendering;

namespace SeaPowerNightVision
{
    /// <summary>
    /// A genuine camera image effect for the built-in render pipeline, modelling how an image
    /// intensifier actually behaves rather than just tinting the frame.
    /// <para>
    /// Unity calls <see cref="OnRenderImage"/> as part of the camera's rendering, handing over the
    /// scene colour buffer before anything else is composited, so this is a real in-engine filter
    /// and screen-space UI is untouched.
    /// </para>
    /// <para>Realism features implemented here rather than in the shader, so they work either way:</para>
    /// <list type="bullet">
    /// <item><description><b>Automatic gain control</b> — the scene's average brightness is measured
    /// on the GPU each few frames; gain is then driven to hold a target screen brightness, clamping
    /// down fast when something bright appears and recovering slowly, like a real tube's ABC.</description></item>
    /// <item><description><b>Photon-limited noise</b> — scintillation scaled by how dark the image is,
    /// so shadows boil and bright areas stay clean.</description></item>
    /// <item><description><b>Warm-up and collapse</b> — the surge when the tubes are switched on and
    /// the quick decay when they are switched off.</description></item>
    /// <item><description><b>Tube mask</b> — the circular field of view, drawn as real geometry.</description></item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public class NightVisionScreenEffect : MonoBehaviour
    {
        private const int MaxGainPasses = 4;
        private const int MeasureSize = 8;
        private const int MeasureInterval = 6;

        private NightVisionSettings _settings;
        private Material _blendMaterial;
        private Material _shaderMaterial;
        private RenderTexture _history;
        private Texture2D _measureTexture;
        private bool _shaderChecked;
        private bool _broken;
        private float _noisePhase;
        private int _frameCounter;

        /// <summary>Smoothed scene luminance, as seen by the tube's photocathode.</summary>
        private float _adaptedLuminance = 0.05f;

        public Camera TargetCamera { get; private set; }

        /// <summary>0..1 fade weight, driven by the owning filter.</summary>
        public float Weight { get; set; }

        /// <summary>Transient multiplier used for the warm-up surge.</summary>
        public float GainScale { get; set; } = 1f;

        /// <summary>True when the high-quality shader path is in use.</summary>
        public bool UsingShader => _shaderMaterial != null;

        /// <summary>The gain the AGC settled on this frame, for the HUD readout.</summary>
        public float CurrentGain { get; private set; } = 1f;

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

            ReleaseHistory();

            if (_measureTexture != null)
            {
                DestroyImmediate(_measureTexture);
                _measureTexture = null;
            }
        }

        private void ReleaseHistory()
        {
            if (_history != null)
            {
                _history.Release();
                DestroyImmediate(_history);
                _history = null;
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

            var gain = ComputeGain(source, weight);
            CurrentGain = gain;

            EnsureShaderMaterial();

            if (_shaderMaterial != null)
            {
                RenderWithShader(source, destination, weight, gain);
                return;
            }

            Graphics.Blit(source, destination);

            if (!EnsureBlendMaterial())
            {
                return;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = destination;

            var width = destination != null ? destination.width : Screen.width;
            var height = destination != null ? destination.height : Screen.height;
            RenderPasses(weight, gain, width, height);

            RenderTexture.active = previous;
        }

        /// <summary>
        /// Automatic gain control. Real intensifiers hold a roughly constant output brightness:
        /// they run wide open in starlight and stop down hard when a flare goes up. The recovery
        /// afterwards is deliberately much slower than the reaction, which is why you are briefly
        /// blind after a muzzle flash.
        /// </summary>
        private float ComputeGain(RenderTexture source, float weight)
        {
            var maxGain = Mathf.Max(1f, _settings.Gain.Value * _settings.GetModeGainScale());

            if (!_settings.AutoGain.Value)
            {
                return Mathf.Lerp(1f, maxGain * GainScale, weight);
            }

            if (++_frameCounter >= MeasureInterval)
            {
                _frameCounter = 0;
                MeasureLuminance(source);
            }

            var target = Mathf.Max(0.01f, _settings.AgcTarget.Value);
            var wanted = Mathf.Clamp(target / Mathf.Max(_adaptedLuminance, 0.0005f), 1f, maxGain);

            return Mathf.Lerp(1f, wanted * GainScale, weight);
        }

        private void MeasureLuminance(RenderTexture source)
        {
            var temporary = RenderTexture.GetTemporary(MeasureSize, MeasureSize, 0, RenderTextureFormat.Default);

            try
            {
                Graphics.Blit(source, temporary);

                if (_measureTexture == null)
                {
                    _measureTexture = new Texture2D(MeasureSize, MeasureSize, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                var previous = RenderTexture.active;
                RenderTexture.active = temporary;
                _measureTexture.ReadPixels(new Rect(0f, 0f, MeasureSize, MeasureSize), 0, 0, false);
                _measureTexture.Apply(false);
                RenderTexture.active = previous;

                var pixels = _measureTexture.GetPixels32();
                var sum = 0f;
                foreach (var pixel in pixels)
                {
                    sum += (0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b) / 255f;
                }

                var measured = sum / pixels.Length;

                // Fast attack, slow release: clamping down is near-instant, recovery takes seconds.
                var speed = Mathf.Max(0.2f, _settings.AgcSpeed.Value);
                var rate = measured > _adaptedLuminance ? speed : speed * 0.25f;
                _adaptedLuminance = Mathf.Lerp(_adaptedLuminance, measured,
                    Mathf.Clamp01(Time.unscaledDeltaTime * MeasureInterval * rate));
            }
            catch (System.Exception e)
            {
                NightVisionPlugin.Log.LogWarning("Auto-gain measurement failed, falling back to fixed gain: " + e.Message);
                _settings.AutoGain.Value = false;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(temporary);
            }
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

        private void RenderWithShader(RenderTexture source, RenderTexture destination, float weight, float gain)
        {
            var tint = _settings.GetTintColor();

            _shaderMaterial.SetFloat("_Weight", weight);
            _shaderMaterial.SetFloat("_Gain", gain);
            _shaderMaterial.SetFloat("_Lift", _settings.ShadowLift.Value);
            _shaderMaterial.SetFloat("_Contrast", 1f + _settings.Contrast.Value / 100f);
            _shaderMaterial.SetFloat("_TintStrength", _settings.GetEffectiveTintStrength());
            _shaderMaterial.SetColor("_TintColor", tint);
            _shaderMaterial.SetFloat("_Glow", _settings.TubeGlow.Value);
            _shaderMaterial.SetFloat("_Halation", _settings.Halation.Value);
            _shaderMaterial.SetFloat("_Vignette", _settings.Vignette.Value ? _settings.VignetteStrength.Value : 0f);
            _shaderMaterial.SetFloat("_Noise", _settings.SensorNoise.Value);
            _shaderMaterial.SetFloat("_PhotonNoise", _settings.PhotonNoise.Value);
            _shaderMaterial.SetFloat("_Scanlines", _settings.Scanlines.Value ? _settings.ScanlineStrength.Value : 0f);
            _shaderMaterial.SetFloat("_ScanlineSpacing", Mathf.Max(2, _settings.ScanlineSpacing.Value));
            _shaderMaterial.SetFloat("_TubeMask", _settings.TubeMask.Value ? 1f : 0f);
            _shaderMaterial.SetFloat("_TubeRadius", _settings.TubeRadius.Value);
            _shaderMaterial.SetFloat("_Time01", Time.unscaledTime);

            // Phosphor persistence: blend a little of the previous frame back in, which is the
            // smear you see when panning a real set of tubes.
            var persistence = _settings.Persistence.Value;
            if (persistence > 0.01f)
            {
                EnsureHistory(source);
                _shaderMaterial.SetTexture("_HistoryTex", _history);
                _shaderMaterial.SetFloat("_Persistence", persistence);
            }
            else
            {
                _shaderMaterial.SetFloat("_Persistence", 0f);
            }

            Graphics.Blit(source, destination, _shaderMaterial);

            if (persistence > 0.01f && _history != null)
            {
                Graphics.Blit(destination, _history);
            }
        }

        private void EnsureHistory(RenderTexture source)
        {
            if (_history != null && _history.width == source.width && _history.height == source.height)
            {
                return;
            }

            ReleaseHistory();

            _history = new RenderTexture(source.width, source.height, 0, source.format)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            _history.Create();
        }

        private void RenderPasses(float weight, float gain, int width, int height)
        {
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

            // Photon noise scales with darkness: the darker the scene the fewer photons, the more
            // the image scintillates. Plain grain does not do this and it is the giveaway.
            var photon = _settings.PhotonNoise.Value * Mathf.Clamp01(1.2f - _adaptedLuminance * 4f);
            var noise = Mathf.Max(_settings.SensorNoise.Value, photon);
            if (noise > 0.001f)
            {
                DrawNoise(tint, noise * weight, width, height);
            }

            if (_settings.TubeMask.Value)
            {
                DrawTubeMask(weight, width, height);
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
            _blendMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            _blendMaterial.SetInt("_Cull", (int)CullMode.Off);
            return true;
        }

        private void SetBlend(BlendMode source, BlendMode destination)
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

            GradientQuad(0f, 0f, band, 1f, outer, inner, horizontal: true);
            GradientQuad(1f - band, 0f, 1f, 1f, inner, outer, horizontal: true);
            GradientQuad(0f, 0f, 1f, band, outer, inner, horizontal: false);
            GradientQuad(0f, 1f - band, 1f, 1f, inner, outer, horizontal: false);

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// The circular field of view of the tube, drawn as a black ring that extends well past
        /// the screen edge so everything outside the circle is masked.
        /// </summary>
        private void DrawTubeMask(float weight, int width, int height)
        {
            const int segments = 96;

            var aspect = width / (float)Mathf.Max(1, height);
            var radius = Mathf.Max(0.05f, _settings.TubeRadius.Value) * 0.5f;
            var edge = new Color(0f, 0f, 0f, Mathf.Clamp01(weight));

            GL.PushMatrix();
            GL.LoadOrtho();
            SetBlend(BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
            GL.Begin(GL.QUADS);

            for (var i = 0; i < segments; i++)
            {
                var a0 = (i / (float)segments) * Mathf.PI * 2f;
                var a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;

                var inner0 = new Vector2(Mathf.Cos(a0) * radius / aspect, Mathf.Sin(a0) * radius);
                var inner1 = new Vector2(Mathf.Cos(a1) * radius / aspect, Mathf.Sin(a1) * radius);
                var outer0 = inner0 * 4f;
                var outer1 = inner1 * 4f;

                GL.Color(new Color(0f, 0f, 0f, edge.a * 0.85f));
                GL.Vertex3(0.5f + inner0.x, 0.5f + inner0.y, 0f);
                GL.Vertex3(0.5f + inner1.x, 0.5f + inner1.y, 0f);
                GL.Color(edge);
                GL.Vertex3(0.5f + outer1.x, 0.5f + outer1.y, 0f);
                GL.Vertex3(0.5f + outer0.x, 0.5f + outer0.y, 0f);
            }

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
            SetBlend(BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
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
            var count = Mathf.RoundToInt(Mathf.Clamp01(amount) * 2200f);
            if (count <= 0)
            {
                return;
            }

            _noisePhase += Time.unscaledDeltaTime;
            var random = new System.Random(unchecked((int)(_noisePhase * 1000f)) ^ Time.frameCount);
            var brightness = 0.10f + 0.30f * amount;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, 0f, height);
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
