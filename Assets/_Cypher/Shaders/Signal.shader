// The lenses of the signal heads: three small boxes on every head, drawn in one instanced draw from a static list of heads (position, yaw, which axis it governs). Which lamp is lit
// is not stored anywhere: it comes from one global, the cycle's state (_CypherSignalState: x the axis that has green, y 1 in the amber phase), worked out here the way
// Cypher.Core.SignalLamps does, so a changing signal costs the CPU nothing. Unlit, behind the fog; lit lamps are far above 1 and bloom.
Shader "Cypher/Signal"
{
    Properties
    {
        _WalkY("Height of the pavement", Float) = 0.185
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Lens"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VehiclePose.hlsl"

            StructuredBuffer<float4> _Heads;   // x, z, yaw, axis (0 governs traffic along X, 1 along Z)
            float4 _CypherSignalState;         // x: the axis with green (or amber); y: 1 while it is amber

            CBUFFER_START(UnityPerMaterial)
            float _WalkY;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 colour : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            static const float LAMP_OFF = 0.045;

            half3 LampColour(uint lamp, float axis)
            {
                bool mine = abs(axis - _CypherSignalState.x) < 0.5;
                bool amberPhase = _CypherSignalState.y > 0.5;
                bool red = !mine, amber = mine && amberPhase, go = mine && !amberPhase;
                if (lamp == 0u) return red ? half3(4.2, 0.08, 0.06) : half3(LAMP_OFF, LAMP_OFF * 0.4, LAMP_OFF * 0.4);
                if (lamp == 1u) return amber ? half3(4.0, 1.8, 0.12) : half3(LAMP_OFF, LAMP_OFF * 0.7, LAMP_OFF * 0.3);
                return go ? half3(0.15, 4.0, 1.1) : half3(LAMP_OFF * 0.4, LAMP_OFF, LAMP_OFF * 0.6);
            }

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                float4 head = _Heads[instanceID / 3u];
                uint lamp = instanceID % 3u;
                float yOffset = lamp == 0u ? 0.28 : (lamp == 1u ? 0.0 : -0.28);   // red on top
                float yaw = head.z;
                float3 centre = float3(head.x - sin(yaw) * 0.16, _WalkY + 5.32 + yOffset, head.y - cos(yaw) * 0.16);
                float3 positionWS = VehRotY(input.positionOS, yaw) + centre;

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.colour = LampColour(lamp, head.w);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(MixFog(input.colour, input.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }
}
