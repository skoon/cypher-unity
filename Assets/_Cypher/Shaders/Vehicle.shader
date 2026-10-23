// The city's vehicles and aircraft: every body, wheel and aircraft hull in a few instanced draws, placed in the vertex shader from a StructuredBuffer entry written by
// Cypher.Core.TrafficFrame (indexed by SV_INSTANCEID plus a per-draw base) and VehiclePose.hlsl. The draw's _Kind says which part it is (VehiclePose.hlsl); a "world" draw
// has no instance at all and just draws a mesh where it already is (the signal masts). Shading is URP's PBR with the city's night fills and the pooled point lights, as the
// pedestrians, with faceted normals from screen-space derivatives (the prototype's flat shading). No shadows, no depth/normals passes: scene shadows are off.
Shader "Cypher/Vehicle"
{
    Properties
    {
        _Smoothness("Smoothness", Range(0, 1)) = 0.70
        _Metallic("Metallic", Range(0, 1)) = 0.62
        _FixedAlbedo("Albedo when not painted", Color) = (0.05, 0.055, 0.07, 1)
        _UsePaint("1: the vehicle's own paint; 0: the albedo above", Range(0, 1)) = 1
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
            Name "Body"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityNightFill.hlsl"
            #include "VehiclePose.hlsl"

            StructuredBuffer<VehicleInstance> _Vehicles;
            float _InstanceBase;   // where this draw's run starts in the buffer
            float _Kind;           // what this draw is (VEH_KIND_*)

            CBUFFER_START(UnityPerMaterial)
            half _Smoothness;
            half _Metallic;
            half4 _FixedAlbedo;
            half _UsePaint;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                nointerpolation float3 albedo : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            float3 HexToLinear(uint hex)
            {
                float3 srgb = float3((hex >> 16) & 255u, (hex >> 8) & 255u, hex & 255u) / 255.0;
                return SRGBToLinear(srgb);
            }

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                uint kind = (uint)(_Kind + 0.5);
                float3 positionWS;
                float3 paint = _FixedAlbedo.rgb;   // a Color property reaches the shader already linear

                if (kind == VEH_KIND_WORLD)
                {
                    positionWS = TransformObjectToWorld(input.positionOS);
                }
                else
                {
                    uint index = (uint)(_InstanceBase + 0.5) + (kind == VEH_KIND_WHEEL ? (instanceID >> 2) : instanceID);
                    VehicleInstance v = _Vehicles[index];
                    if (_UsePaint > 0.5) paint = HexToLinear(v.colours.x);
                    if (kind == VEH_KIND_WHEEL)
                    {
                        bool present;
                        positionWS = VehWheelWorld(input.positionOS, v, instanceID & 3u, present);
                        if (!present)
                        {
                            // a wheel this vehicle lacks collapses to a point and is clipped away with all its triangles
                            output.positionCS = float4(0, 0, 0, 0);
                            return output;
                        }
                    }
                    else if (kind == VEH_KIND_AIRCRAFT)
                    {
                        positionWS = AirBodyWorld(input.positionOS, v);
                    }
                    else
                    {
                        positionWS = VehBodyWorld(input.positionOS, v);
                    }
                }

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.albedo = paint;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // faceted normals from the screen-space slope of the surface, turned to face the eye (a closed body is only ever seen from outside)
                float3 normalWS = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                if (dot(normalWS, viewDir) < 0.0) normalWS = -normalWS;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = float4(0, 0, 0, 0);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = input.albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                // diffuse only: the fills are unshadowed by design
                color.rgb += surfaceData.albedo * (1.0 - surfaceData.metallic) * CityFillLight(inputData.normalWS);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }
    }
}
