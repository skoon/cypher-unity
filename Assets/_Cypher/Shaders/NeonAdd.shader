// Additive emissive signage: glass tubes, backlit panels with procedural lettering bars, marquee bulbs and
// soft glow pools, all drawn procedurally from per-quad parameters baked into the mesh, so one material
// and a handful of draw calls cover every sign. Port of the prototype's sign shader (Neon.js), minus the
// glyph atlas. Vertex colour = linear tint. TEXCOORD1 = (seed, flicker mode + 8 * shape, extra, gain),
// where extra is chase segments (tubes), aspect (panels) or bulb count (bulbs). See Core.NeonMesher.
Shader "Cypher/NeonAdd"
{
    Properties
    {
        _Brightness("Brightness", Range(0.0, 4.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Neon"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 sign : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 tint : TEXCOORD0;
                float2 uv : TEXCOORD1;
                nointerpolation float4 sign : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.tint = input.color.rgb;
                output.uv = input.uv;
                output.sign = input.sign;
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

            // mode 0 steady hum | 1 dying tube | 2 slow breathe | 3 advert blink | 4 sputter
            float Flicker(float seed, float t, float mode)
            {
                // Mains hum: two incommensurate ripples so it never reads as a sine.
                float hum = 1.0 + 0.05 * sin(t * 96.0 + seed * 31.0) + 0.028 * sin(t * 57.3 + seed * 11.0);
                if (mode < 0.5) return hum;
                if (mode < 1.5)
                {
                    // A dying tube is fine most of the time, then stutters in a tight burst and
                    // occasionally drops out for the better part of a second.
                    float burst = step(frac(t * 0.107 + seed), 0.17);
                    float s = H11(floor(t * 21.0) + seed * 131.0);
                    float on = lerp(1.0, step(0.42, s) * (0.10 + 0.90 * s), burst);
                    float dead = step(0.972, H11(floor(t * 0.41) + seed * 71.0));
                    return hum * on * (1.0 - dead * 0.93);
                }
                if (mode < 2.5) return hum * (0.62 + 0.38 * sin(t * 1.55 + seed * 19.0));
                if (mode < 3.5)
                {
                    float ph = frac(t * 0.37 + seed);
                    return hum * (1.0 - step(0.62, ph) * step(ph, 0.72) * 0.95);
                }
                float s2 = H11(floor(t * 9.0) + seed * 53.0);
                return hum * (0.45 + 0.55 * step(0.25, s2));
            }

            // Backlit sign face. Lettering is faked with a grid of cells, each holding strokes chosen by a
            // hash: it reads as signage at street distance without needing a glyph atlas.
            float PanelPattern(float2 uv, float aspect, float seed)
            {
                float cols = clamp(floor(aspect * 3.5), 1.0, 24.0);
                float rows = clamp(floor(3.5 / max(aspect, 0.05)), 1.0, 24.0);
                float2 grid = uv * float2(cols, rows);
                float2 cell = floor(grid);
                float2 f = frac(grid);

                float h = H21(cell + seed * 13.7);
                float h2 = H21(cell.yx + seed * 7.3 + 17.0);
                float glyph = step(0.22, h);   // some cells stay empty, like spaces

                float vBar = step(abs(f.x - (h2 < 0.5 ? 0.3 : 0.7)), 0.09) * step(abs(f.y - 0.5), 0.38);
                float vMid = step(abs(f.x - 0.5), 0.09) * step(abs(f.y - 0.5), 0.38) * step(0.5, frac(h2 * 7.1));
                float hBar = step(abs(f.y - (frac(h2 * 3.7) * 0.5 + 0.25)), 0.08) * step(abs(f.x - 0.5), 0.34);
                float strokes = saturate(vBar + vMid + hBar) * glyph;

                float2 edge = min(uv, 1.0 - uv) * float2(aspect, 1.0);   // distance to the border, in sign-height units
                float frame = step(min(edge.x, edge.y), 0.045);

                return saturate(0.30 + strokes * 0.75 + frame * 0.60);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float seed = input.sign.x;
                float shape = floor(input.sign.y / 8.0 + 0.001);
                float mode = input.sign.y - shape * 8.0;
                float extra = input.sign.z;
                float gain = input.sign.w;
                float2 uv = input.uv;
                float t = _Time.y;

                float m = 0.0;
                float chase = 1.0;
                if (shape < 1.5)
                {
                    // Glass tube: a bright core with a halo across the quad's height.
                    float p = saturate(1.0 - abs(uv.y * 2.0 - 1.0));
                    m = pow(p, shape < 0.5 ? 2.2 : 1.1);
                    if (extra > 0.5)
                    {
                        // A bright band running along the tube.
                        float ph = frac(uv.x * extra - t * 0.55 + seed);
                        chase = 0.42 + 1.45 * smoothstep(0.60, 1.0, ph);
                    }
                }
                else if (shape < 2.5)
                {
                    m = PanelPattern(uv, extra, seed);
                }
                else if (shape < 3.5)
                {
                    float2 q = float2(frac(uv.x * max(extra, 1.0)) - 0.5, uv.y - 0.5);
                    m = pow(saturate(1.0 - length(float2(q.x * 4.0, q.y * 2.0))), 1.5);
                }
                else
                {
                    // Radial glow: street pools and wall washes. Capped below the white-hot threshold.
                    m = pow(saturate(1.0 - length(uv * 2.0 - 1.0)), 2.0) * 0.5;
                }

                // The halo takes the tint; the saturated core burns toward white, which is what makes a
                // real tube read as glass rather than coloured paint.
                float core = smoothstep(0.60, 0.97, m);
                half3 col = input.tint * (m * 1.85) + core * 1.55;
                col *= gain * Flicker(seed, t, mode) * chase * _Brightness;
                col *= ComputeFogIntensity(input.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
