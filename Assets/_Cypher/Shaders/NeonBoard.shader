// LED hoardings and holograms: one shader plays the ad panels on both. _Holo = 0 is the opaque hoarding (fog
// blends it toward the fog colour); _Holo = 1 is the additive projection (hollow toward its edges, with a
// heavier interlace, fog fades it out). Port of the prototype's billboard shader (Neon.js). Glitch, ticker
// scroll, scanlines and the colour split are all keyed off the per-quad seed so no two screens corrupt on the
// same frame. Vertex colour = linear tint. TEXCOORD0 = atlas coordinates inside one of the four panels of the
// board atlas (Core.BoardArt.AtlasUv); the shader finds the panel and the position in it from them, so the
// quad carries no other texture data. TEXCOORD1 = (seed, glitch, ticker speed, gain).
Shader "Cypher/NeonBoard"
{
    Properties
    {
        _BoardAtlas("Board atlas", 2D) = "black" {}
        _PanelInset("Panel inset (share of one panel)", Float) = 0.015625
        _Brightness("Brightness", Range(0.0, 4.0)) = 1.0
        _Holo("Hologram (0 opaque hoarding, 1 additive projection)", Float) = 0
        [HideInInspector] _SrcBlend("Src blend", Float) = 1
        [HideInInspector] _DstBlend("Dst blend", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Board"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _PanelInset;
            half _Brightness;
            half _Holo;
            CBUFFER_END

            TEXTURE2D(_BoardAtlas);
            SAMPLER(sampler_BoardAtlas);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 params : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 tint : TEXCOORD0;
                float2 uv : TEXCOORD1;
                nointerpolation float4 params : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.tint = input.color.rgb;
                output.uv = input.uv;
                output.params = input.params;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float H11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float H21(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            // Mains hum only: a screen does not flicker like a tube.
            float Hum(float seed, float t)
            {
                return 1.0 + 0.05 * sin(t * 96.0 + seed * 31.0) + 0.028 * sin(t * 57.3 + seed * 11.0);
            }

            // How much of a fine pattern the screen can still show: 1 while each cycle spans several pixels, 0 once it cannot be resolved.
            float Resolvable(float phase)
            {
                return saturate(1.0 - fwidth(phase) / 2.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float seed = input.params.x;
                float glitch = input.params.y;
                float tickerSpeed = input.params.z;
                float gain = input.params.w;

                // Which panel, and where in it: 0..1 from its left and bottom, with the inset trimmed off.
                float2 g = input.uv * 2.0;
                float2 panel = floor(g);
                float2 local = (frac(g) - _PanelInset) / (1.0 - 2.0 * _PanelInset);
                float2 ddxUv = ddx(input.uv), ddyUv = ddy(input.uv);   // the atlas gradients, free of the tearing below

                // Glitch events: short windows where whole scanline blocks tear sideways.
                float ev = step(0.80, H11(floor(t * 2.7 + seed * 17.0)));
                float row = floor(local.y * 42.0);
                float tear = (H21(float2(row, floor(t * 20.0))) - 0.5) * 0.20 * ev * glitch;
                float2 uv = local;
                uv.x = frac(uv.x + tear);

                // The bottom ticker band scrolls on its own, under the picture.
                float ticker = step(uv.y, 0.135);
                uv.x = lerp(uv.x, frac(uv.x - t * tickerSpeed), ticker);
                uv.y += sin(t * 0.6 + seed * 8.0) * 0.0015;

                float2 base = (panel + _PanelInset + clamp(uv, 0.002, 0.998) * (1.0 - 2.0 * _PanelInset)) * 0.5;
                float ca = (0.0018 + ev * 0.010) * 0.5;   // colour split, in atlas units
                half3 col;
                col.r = SAMPLE_TEXTURE2D_GRAD(_BoardAtlas, sampler_BoardAtlas, base + float2(ca, 0.0), ddxUv, ddyUv).r;
                col.g = SAMPLE_TEXTURE2D_GRAD(_BoardAtlas, sampler_BoardAtlas, base, ddxUv, ddyUv).g;
                col.b = SAMPLE_TEXTURE2D_GRAD(_BoardAtlas, sampler_BoardAtlas, base - float2(ca, 0.0), ddxUv, ddyUv).b;

                // Scanlines, and a slow bright bar rolling up the panel.
                float lines = local.y * 620.0;
                col *= 0.80 + 0.20 * sin(lines) * Resolvable(lines);
                float roll = frac(t * 0.16 + seed);
                float bar = smoothstep(0.11, 0.0, abs(frac(local.y - roll) - 0.0));
                bar += smoothstep(0.11, 0.0, abs(frac(local.y - roll) - 1.0));
                col *= 1.0 + bar * 0.45;

                // Dropped-signal frames punch the whole panel to noise for a beat.
                float dead = step(0.985, H11(floor(t * 1.7 + seed * 41.0)));
                col = lerp(col, H21(local * 480.0 + t) * 0.35, dead * 0.85);

                col *= input.tint * gain * Hum(seed, t) * _Brightness;

                // Bezel: the panel's edge is unlit housing, not screen.
                float2 e = min(local, 1.0 - local);
                col *= smoothstep(0.0, 0.020, min(e.x, e.y));

                if (_Holo > 0.5)
                {
                    // Projection: additive, hollow toward the edges, with a heavier interlace.
                    float fade = smoothstep(0.0, 0.30, min(e.x, e.y) * 3.2);
                    float interlace = local.y * 190.0 - t * 5.0;
                    col *= fade * (0.72 + 0.28 * lerp(0.0, sin(interlace), Resolvable(interlace)));
                    col *= ComputeFogIntensity(input.fogFactor);
                    return half4(col, 1.0);
                }

                col = MixFog(col, input.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
