// Three-colour night sky: zenith, horizon glow, and a below-horizon colour that matches the fog so
// the street fades into the sky without a seam. Colours come from Core.NightRig via NightLighting.
// Also the reflection source for wet streets. Unlit, SRP Batcher compatible.
Shader "Cypher/SkyGradient"
{
    Properties
    {
        _Zenith("Zenith", Color) = (0.039, 0.071, 0.149, 1)
        _Horizon("Horizon", Color) = (0.271, 0.188, 0.333, 1)
        _Ground("Below Horizon", Color) = (0.165, 0.153, 0.251, 1)
        _Exponent("Gradient Exponent", Range(0.1, 4)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Sky"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Zenith;
            half4 _Horizon;
            half4 _Ground;
            half _Exponent;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float y = normalize(input.direction).y;
                half3 above = lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(y), _Exponent));
                half3 below = lerp(_Horizon.rgb, _Ground.rgb, saturate(-y * 4.0));
                return half4(y >= 0 ? above : below, 1);
            }
            ENDHLSL
        }
    }
}
