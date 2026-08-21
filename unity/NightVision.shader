Shader "Hidden/SeaPower/NightVision"
{
    // Full-quality image intensifier simulation for Sea Power.
    //
    // Models the parts of a real Gen-III tube that you actually notice:
    //   * response to luminance, not colour (a photocathode does not see red or blue)
    //   * photon-limited scintillation that gets worse the darker the scene is
    //   * halation: bright sources bleeding into their surroundings as the tube saturates
    //   * phosphor persistence: the smear left behind when you pan
    //   * fixed-pattern "chicken wire" from the microchannel plate
    //   * the circular field of view of the objective
    //
    // Build into nightvision.bundle and drop next to SeaPowerNightVision.dll — see unity/README.md.

    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _HistoryTex ("Previous frame", 2D) = "black" {}
        _Weight ("Fade weight", Range(0,1)) = 1
        _Gain ("Gain", Range(1,16)) = 3
        _Lift ("Shadow lift", Range(0,0.35)) = 0.06
        _Contrast ("Contrast", Range(0.5,2)) = 1.12
        _TintStrength ("Tint strength", Range(0,1)) = 0.85
        _TintColor ("Phosphor colour", Color) = (0.36,1,0.42,1)
        _Glow ("Glow", Range(0,2)) = 0.35
        _Halation ("Halation", Range(0,2)) = 0.6
        _Vignette ("Vignette", Range(0,1)) = 0.55
        _Noise ("Sensor grain", Range(0,1)) = 0
        _PhotonNoise ("Photon noise", Range(0,1)) = 0.35
        _Persistence ("Phosphor persistence", Range(0,0.8)) = 0.25
        _Scanlines ("Scanlines", Range(0,1)) = 0
        _ScanlineSpacing ("Scanline spacing", Float) = 3
        _TubeMask ("Tube mask", Range(0,1)) = 0
        _TubeRadius ("Tube radius", Float) = 0.88
        _Time01 ("Time", Float) = 0
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _HistoryTex;
            float4 _MainTex_TexelSize;

            float _Weight, _Gain, _Lift, _Contrast;
            float _TintStrength;
            float4 _TintColor;
            float _Glow, _Halation, _Vignette;
            float _Noise, _PhotonNoise, _Persistence;
            float _Scanlines, _ScanlineSpacing;
            float _TubeMask, _TubeRadius;
            float _Time01;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            // Bright sources bleed into their surroundings as the microchannel plate saturates
            // locally. Two rings of taps gives a decent halo for the cost.
            float halation(float2 uv)
            {
                float2 t = _MainTex_TexelSize.xy;
                float sum = 0;

                [unroll] for (int i = 0; i < 8; i++)
                {
                    float a = (i / 8.0) * 6.2831853;
                    float2 dir = float2(cos(a), sin(a));
                    float3 near = tex2D(_MainTex, uv + dir * t * 4.0).rgb;
                    float3 far  = tex2D(_MainTex, uv + dir * t * 11.0).rgb;
                    sum += max(dot(near, float3(0.2126,0.7152,0.0722)) - 0.55, 0.0);
                    sum += max(dot(far,  float3(0.2126,0.7152,0.0722)) - 0.70, 0.0) * 0.5;
                }

                return sum / 8.0;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 source = tex2D(_MainTex, i.uv).rgb;

                // 1. A photocathode responds to photons, not to colour.
                float luma = dot(source, float3(0.2126, 0.7152, 0.0722));

                // 2. Amplify, then lift the floor: even a capped tube glows faintly.
                float signal = luma * _Gain + _Lift;

                // 3. Contrast around mid grey.
                signal = (signal - 0.5) * _Contrast + 0.5;

                // 4. Halation from saturated areas.
                signal += halation(i.uv) * _Halation;

                // 5. Photon-limited noise. Shot noise goes as sqrt(N), so the fewer photons the
                //    worse the relative noise — dark areas boil, bright areas stay clean. This is
                //    the single most recognisable property of a real intensifier.
                if (_PhotonNoise > 0.001)
                {
                    float n = hash21(i.uv * _ScreenParams.xy + frac(_Time01) * 719.3) - 0.5;
                    float shot = _PhotonNoise * 0.5 / sqrt(max(signal, 0.02));
                    signal += n * shot * 0.35;
                }

                // 6. Plain electronic grain on top, if the player wants more.
                if (_Noise > 0.001)
                {
                    float n = hash21(i.uv * _ScreenParams.xy * 0.5 - frac(_Time01) * 331.1) - 0.5;
                    signal += n * _Noise * 0.25;
                }

                // 7. Fixed-pattern noise: the faint hexagonal "chicken wire" of the fibre-optic
                //    bundle and microchannel plate. Subtle, static, and instantly familiar.
                float2 hex = i.uv * _ScreenParams.xy / 34.0;
                hex.x += (floor(hex.y) % 2.0) * 0.5;
                float2 cell = abs(frac(hex) - 0.5);
                signal *= 1.0 - 0.035 * smoothstep(0.34, 0.5, max(cell.x, cell.y));

                signal = saturate(signal);

                // 8. Phosphor tint.
                float3 mono = signal * _TintColor.rgb;
                float3 boosted = source * _Gain + _Lift;
                float3 nv = lerp(boosted, mono, _TintStrength);

                // 9. Extra glow, tinted with the phosphor.
                nv += _TintColor.rgb * halation(i.uv) * _Glow * 0.5;

                // 10. Scanlines.
                if (_Scanlines > 0.001)
                {
                    float ln = frac(i.uv.y * _ScreenParams.y / max(_ScanlineSpacing, 2.0));
                    nv *= 1.0 - _Scanlines * step(0.5, ln);
                }

                // 11. Phosphor persistence: P43 has a decay of a few milliseconds, but the eye
                //     integrates it into a visible smear when panning.
                if (_Persistence > 0.001)
                {
                    float3 history = tex2D(_HistoryTex, i.uv).rgb;
                    nv = max(nv, history * _Persistence);
                }

                // 12. Vignette from the objective.
                if (_Vignette > 0.001)
                {
                    float2 c = i.uv - 0.5;
                    float falloff = 1.0 - smoothstep(0.25, 0.75, dot(c, c) * 2.0);
                    nv *= lerp(1.0, falloff, _Vignette);
                }

                // 13. The circular field of view of the tube, with a soft edge.
                if (_TubeMask > 0.5)
                {
                    float2 c = i.uv - 0.5;
                    c.x *= _ScreenParams.x / _ScreenParams.y;
                    float d = length(c) / max(_TubeRadius * 0.5, 0.05);
                    nv *= 1.0 - smoothstep(0.94, 1.0, d);
                }

                // 14. Fade between the untouched frame and the intensified one.
                return fixed4(lerp(source, nv, _Weight), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
