// Rain streaks for a stretched-billboard particle system: additive, unlit, soft across the width and
// fading at both ends so a streak reads as motion rather than a line. Particle colour supplies tint
// and alpha. Fades into fog like everything else.
Shader "Cypher/Rain"
{
    Properties
    {
        _Intensity("Intensity", Range(0.0, 4.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Rain"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // u across the streak, v along it.
                float across = 1.0 - abs(input.uv.x * 2.0 - 1.0);
                float along = smoothstep(0.0, 0.25, input.uv.y) * smoothstep(1.0, 0.55, input.uv.y);
                float m = pow(saturate(across), 1.5) * along;
                half3 col = input.color.rgb * input.color.a * m * _Intensity;
                col *= ComputeFogIntensity(input.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
