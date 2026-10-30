// The guns in the player's hands (Cypher.Core.WeaponModels): vertex colour is the albedo, TEXCOORD1 = (metalness, roughness, emissive strength, glow
// channel), and _GlowChan scales the four channels (always on, ammo left, charge, muzzle heat) as the prototype's viewmodel material does. Lit like the city
// (URP PBR plus the night fill), no fog. The depth is squashed into the sliver just past the near plane, where nothing in the world can be (the player's capsule
// keeps walls further than that), so the gun never sinks into a wall yet still sorts against itself.
Shader "Cypher/Viewmodel"
{
    Properties
    {
        _GlowChan("Glow channels (always, ammo, charge, heat)", Vector) = (1, 1, 1, 0)
        _DepthSquash("Depth range kept (share of the depth buffer next to the near plane)", Range(0.001, 0.2)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityNightFill.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _GlowChan;
            float _DepthSquash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 mre : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half3 color : TEXCOORD2;
                float4 mre : TEXCOORD3;
                half3 vertexSH : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color.rgb;
                o.mre = input.mre;
                float4 cs = p.positionCS;
                // squash into the nearest sliver of the depth range (UNITY_NEAR_CLIP_VALUE is 1 with reversed Z, -1 in GL)
                float ndc = cs.z / cs.w;
                ndc = UNITY_NEAR_CLIP_VALUE + (ndc - UNITY_NEAR_CLIP_VALUE) * _DepthSquash;
                cs.z = ndc * cs.w;
                o.positionCS = cs;
                OUTPUT_SH(o.normalWS, o.vertexSH);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float channel = input.mre.w;
                float glow = _GlowChan.x;
                if (channel > 0.5) glow = _GlowChan.y;
                if (channel > 1.5) glow = _GlowChan.z;
                if (channel > 2.5) glow = _GlowChan.w;

                SurfaceData s = (SurfaceData)0;
                s.albedo = input.color;
                s.metallic = input.mre.x;
                s.smoothness = 1.0 - input.mre.y;
                s.occlusion = 1;
                s.alpha = 1;
                s.emission = input.color * input.mre.z * glow;

                InputData d = (InputData)0;
                d.positionWS = input.positionWS;
                d.normalWS = normalize(input.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                d.shadowCoord = float4(0, 0, 0, 0);   // the gun is not shadowed: the moon's shadow map has no business this close to the eye
                d.bakedGI = SAMPLE_GI(float2(0, 0), input.vertexSH, d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb += s.albedo * (1.0 - s.metallic) * CityFillLight(d.normalWS);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
    }
}
