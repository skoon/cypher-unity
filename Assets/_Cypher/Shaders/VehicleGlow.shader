// The vehicles' glowing geometry, added to the picture: headlamps, tail lamps, indicator strips, the pool of light on the road under each vehicle and the headlight cones
// (a "ground" draw), and the aircraft's thrusters, navigation lamps and exhaust streaks (an "aircraft" draw). One instanced draw each, placed from the same buffer as the bodies
// by VehiclePose.hlsl. The mesh's vertex colour is already the prototype's colour times its alpha, so it is added as it is, tinted per vehicle: the lamps by the vehicle's lamp
// tint, the under-glow by its neon colour (times 1.7, which the prototype's instance colour had). Behind the fog, which fades an added colour to nothing.
Shader "Cypher/VehicleGlow"
{
    Properties
    {
        _Brightness("Brightness", Range(0, 4)) = 1
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
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "VehiclePose.hlsl"

            StructuredBuffer<VehicleInstance> _Vehicles;
            float _InstanceBase;
            float _Aircraft;   // 0: a ground vehicle's glow; 1: an aircraft's

            CBUFFER_START(UnityPerMaterial)
            half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float4 part : TEXCOORD1;   // x: the kind of ground glow (VEH_GLOW_*)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 colour : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                VehicleInstance v = _Vehicles[(uint)(_InstanceBase + 0.5) + instanceID];
                uint part = (uint)(input.part.x + 0.5);
                float3 positionWS;
                float3 tint = float3(1, 1, 1);
                if (_Aircraft > 0.5)
                {
                    positionWS = AirGlowWorld(input.positionOS, v);
                }
                else
                {
                    positionWS = VehGlowWorld(input.positionOS, part, v, _Time.y);
                    if (part == VEH_GLOW_LAMPS)
                    {
                        tint = float3(v.b.z, 1.0, 2.0 - v.b.z);
                    }
                    else if (part == VEH_GLOW_UNDERGLOW)
                    {
                        float3 srgb = float3((v.colours.y >> 16) & 255u, (v.colours.y >> 8) & 255u, v.colours.y & 255u) / 255.0;
                        tint = SRGBToLinear(srgb) * 1.7;
                    }
                }

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.colour = input.color.rgb * tint * _Brightness;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(input.colour * ComputeFogIntensity(input.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }
}
