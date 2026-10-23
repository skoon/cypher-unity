// The soft dark disc under each ground vehicle (the prototype's contact shadow; scene shadows are off). One small triangle fan, instanced from the same buffer as the bodies:
// it lies just above the asphalt under the vehicle, a little smaller than its footprint, and fades from dark in the middle to nothing at the rim. Blended over the ground, after
// the opaque geometry, behind the fog.
Shader "Cypher/VehicleShadow"
{
    Properties
    {
        _Opacity("Opacity", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Shadow"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VehiclePose.hlsl"

            StructuredBuffer<VehicleInstance> _Vehicles;
            float _InstanceBase;

            CBUFFER_START(UnityPerMaterial)
            half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 disc : TEXCOORD0;   // -1 to 1 across the disc
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                VehicleInstance v = _Vehicles[(uint)(_InstanceBase + 0.5) + instanceID];
                Varyings output;
                output.positionCS = TransformWorldToHClip(VehShadowWorld(input.positionOS, v));
                output.disc = input.positionOS.xz;   // the unit disc
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // the prototype's radial gradient: opaque in the middle, 72% at 45% of the radius, nothing at the rim
                float r = saturate(length(input.disc));
                float a = r < 0.45 ? lerp(1.0, 0.72, r / 0.45) : lerp(0.72, 0.0, (r - 0.45) / 0.55);
                half3 colour = MixFog(half3(0, 0, 0), input.fogFactor);
                return half4(colour, a * _Opacity);
            }
            ENDHLSL
        }
    }
}
