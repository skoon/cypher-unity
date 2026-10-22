// Puffs of steam: soft, additive, camera-facing quads animated entirely in the vertex shader, so a whole city's worth costs one draw and
// nothing on the CPU. Port of the prototype's per-frame loop (Props._updateSteam): a puff cycles t = frac(phase + time * speed),
// rises by t * rise, drifts with the wind, grows from 0.35 to 1.85 times its size, and is brightest mid-cycle and nearby.
// Per quad (see Cypher.Core.SteamMesh): position = where the puff starts, uv = corner, colour.r = drift, TEXCOORD1 = (phase, speed, size, rise).
Shader "Cypher/Steam"
{
    Properties
    {
        [MainTexture] _BaseMap("Puff", 2D) = "white" {}
        _Wind("Wind (the prototype's 0.4 plus the weather's)", Float) = 0.7
        _FadeDistance("Fade-out distance (m)", Float) = 60
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Steam"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _Wind;
            half _FadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 puff : TEXCOORD1;   // phase, speed, size, rise
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 tint : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                float phase = input.puff.x, speed = input.puff.y, size = input.puff.z, rise = input.puff.w;
                float drift = input.color.r;

                float t = frac(phase + _Time.y * speed);
                float3 centre = TransformObjectToWorld(input.positionOS.xyz);
                centre.y += t * rise;
                centre.x += drift * t * _Wind;
                centre.z += drift * 0.6 * t * _Wind;

                float s = size * (0.35 + t * 1.5);
                float2 corner = input.uv - 0.5;
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = centre + (right * corner.x + up * corner.y) * s;

                float2 away = centre.xz - _WorldSpaceCameraPos.xz;
                float fade2 = _FadeDistance * _FadeDistance;
                float near = dot(away, away) > fade2 ? 0.0 : 1.0 - dot(away, away) / fade2;
                float a = sin(t * 3.14159265) * 0.16 * near;

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.tint = half3(a * 0.72, a * 0.78, a * 0.9);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 puff = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                return half4(puff.rgb * input.tint, puff.a);
            }
            ENDHLSL
        }
    }
}
