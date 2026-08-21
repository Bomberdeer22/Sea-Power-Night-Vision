Shader "Hidden/SeaPower/NightVision"
{
    // Full-quality night vision image effect for Sea Power.
    //
    // Fixed-function blending can multiply and add, but it cannot mix colour channels, so it can
    // never turn the frame into true monochrome before tinting it. This shader can: it extracts
    // luminance, amplifies it, applies the phosphor colour, then adds the tube artefacts.
    //
    // Build it into nightvision.bundle and drop that next to SeaPowerNightVision.dll.
    // See unity/README.md for the (short) procedure.

    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Weight ("Fade weight", Range(0,1)) = 1
        _Gain ("Gain", Range(1,8)) = 3
        _Lift ("Shadow lift", Range(0,0.35)) = 0.06
        _Contrast ("Contrast", Range(0.5,2)) = 1.12
        _TintStrength ("Tint strength", Range(0,1)) = 0.85
        _TintColor ("Phosphor colour", Color) = (0.36,1,0.45,1)
        _Glow ("Halation", Range(0,2)) = 0.35
        _Vignette ("Vignette", Range(0,1)) = 0.55
        _Noise ("Sensor grain", Range(0,1)) = 0
        _Scanlines ("Scanlines", Range(0,1)) = 0
        _ScanlineSpacing ("Scanline spacing", Float) = 3
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
            float4 _MainTex_TexelSize;

            float _Weight;
            float _Gain;
            float _Lift;
            float _Contrast;
            float _TintStrength;
            float4 _TintColor;
            float _Glow;
            float _Vignette;
            float _Noise;
            float _Scanlines;
            float _ScanlineSpacing;
            float _Time01;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

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

            // Cheap 5-tap halation: bright sources bleed into their surroundings, which is a large
            // part of what makes an intensifier tube look like an intensifier tube.
            float3 halation(float2 uv)
            {
                float2 texel = _MainTex_TexelSize.xy * 3.0;
                float3 sum = tex2D(_MainTex, uv + float2( texel.x,  0)).rgb;
                sum += tex2D(_MainTex, uv + float2(-texel.x,  0)).rgb;
                sum += tex2D(_MainTex, uv + float2( 0,  texel.y)).rgb;
                sum += tex2D(_MainTex, uv + float2( 0, -texel.y)).rgb;
                sum *= 0.25;
                return max(sum - 0.6, 0.0); // only genuinely bright things bleed
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 source = tex2D(_MainTex, i.uv).rgb;

                // 1. Luminance: a real intensifier responds to photons, not colour.
                float luma = dot(source, float3(0.2126, 0.7152, 0.0722));

                // 2. Amplify, then lift the floor so pure black still glows faintly.
                float amplified = luma * _Gain + _Lift;

                // 3. Contrast around mid grey.
                amplified = saturate((amplified - 0.5) * _Contrast + 0.5);

                // 4. Halation from bright sources.
                float3 glow = halation(i.uv) * _Glow;
                amplified += dot(glow, float3(0.333, 0.333, 0.333));

                // 5. Phosphor tint: blend from the amplified original toward monochrome-tinted.
                float3 mono = amplified * _TintColor.rgb;
                float3 boosted = source * _Gain + _Lift;
                float3 nv = lerp(boosted, mono, _TintStrength);

                // 6. Scanlines.
                if (_Scanlines > 0.001)
                {
                    float line = frac(i.uv.y * _ScreenParams.y / max(_ScanlineSpacing, 2.0));
                    nv *= 1.0 - _Scanlines * step(0.5, line);
                }

                // 7. Animated sensor grain, scaled by darkness like real tube noise.
                if (_Noise > 0.001)
                {
                    float grain = hash21(i.uv * _ScreenParams.xy + frac(_Time01) * 431.7) - 0.5;
                    nv += grain * _Noise * 0.35 * (1.2 - saturate(amplified));
                }

                // 8. Vignette.
                if (_Vignette > 0.001)
                {
                    float2 centred = i.uv - 0.5;
                    float falloff = 1.0 - smoothstep(0.25, 0.75, dot(centred, centred) * 2.0);
                    nv *= lerp(1.0, falloff, _Vignette);
                }

                // 9. Fade between the untouched frame and the intensified one.
                return fixed4(lerp(source, nv, _Weight), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
