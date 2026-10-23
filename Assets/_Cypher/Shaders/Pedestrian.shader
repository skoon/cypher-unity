// The city's pedestrians: every person in a few instanced draws, posed entirely in the vertex shader. A vertex carries its body part (TEXCOORD1.x) and its
// position in that part's joint frame; the instance (a StructuredBuffer entry written by Cypher.Core.CrowdFrame, indexed by SV_InstanceID plus a per-draw
// base) supplies the person's gait, build and colours, and PedestrianPose.hlsl does the rest. Shading is URP's PBR with the city's night fills, as CityLit,
// with faceted normals taken from screen-space derivatives (the prototype's flat shading); the trim is unlit and glows. No shadows, no depth/normals passes:
// scene shadows are off and the people draw only in the forward pass.
Shader "Cypher/Pedestrian"
{
    Properties
    {
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
            #include "PedestrianPose.hlsl"

            StructuredBuffer<PedInstance> _Peds;
            float _InstanceBase;   // where this draw's run starts in the buffer

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 part : TEXCOORD1;   // x: the body part id
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                nointerpolation float3 albedo : TEXCOORD1;
                nointerpolation float3 emission : TEXCOORD2;    // non-zero: unlit and glowing (the trim)
                nointerpolation float2 surface : TEXCOORD3;     // smoothness, metallic
                half fogFactor : TEXCOORD4;
            };

            float3 PedColour(uint hex)
            {
                float3 srgb = float3((hex >> 16) & 255u, (hex >> 8) & 255u, hex & 255u) / 255.0;
                return SRGBToLinear(srgb);
            }

            // The surface of each part (the prototype's materials): cloth, skin, umbrella silk, glowing trim.
            void PartSurface(uint part, PedInstance p, out float3 albedo, out float3 emission, out float2 surface)
            {
                emission = float3(0, 0, 0);
                surface = float2(1.0 - 0.58, 0.06);   // body cloth: roughness 0.58, metalness 0.06
                if (part == PED_PART_HEAD)
                {
                    albedo = PedColour(p.colours.y);
                    surface = float2(1.0 - 0.72, 0.0);
                }
                else if (part == PED_PART_THIGH_L || part == PED_PART_SHIN_L || part == PED_PART_THIGH_R || part == PED_PART_SHIN_R)
                {
                    albedo = PedColour(p.colours.z);
                }
                else if (part == PED_PART_HOOD)
                {
                    albedo = PedColour(p.colours.x) * 1.15;
                }
                else if (part == PED_PART_PACK)
                {
                    albedo = PedColour(p.colours.x) * 0.7;
                }
                else if (part == PED_PART_UMBRELLA)
                {
                    albedo = PedColour(p.extra.x);
                    surface = float2(1.0 - 0.34, 0.18);
                }
                else if (part == PED_PART_TRIM)
                {
                    albedo = float3(0, 0, 0);
                    emission = PedColour(p.colours.w) * p.c.z;
                }
                else
                {
                    albedo = PedColour(p.colours.x);   // torso, arms and the cheap bodies are the coat
                }
            }

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                PedInstance p = _Peds[(uint)(_InstanceBase + 0.5) + instanceID];
                uint part = (uint)(input.part.x + 0.5);

                if (!PedWorn(part, p.extra.y))
                {
                    // a part this person lacks collapses to a point and is clipped away with all its triangles
                    output.positionCS = float4(0, 0, 0, 0);
                    return output;
                }

                float3 positionWS = PedPoseVertex(part, input.positionOS, p, _Time.y);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                PartSurface(part, p, output.albedo, output.emission, output.surface);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                if (any(input.emission > 0.0))
                {
                    half3 glow = MixFog(input.emission, input.fogFactor);
                    return half4(glow, 1.0);
                }

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
                surfaceData.metallic = input.surface.y;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = input.surface.x;
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
