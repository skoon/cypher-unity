#ifndef CYPHER_CITY_LIT_INPUT_INCLUDED
#define CYPHER_CITY_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

// Smoothness (R) multiplied into _Smoothness. Defaults to white, so a material without one is uniform.
TEXTURE2D(_MaskMap);
SAMPLER(sampler_MaskMap);

// One layout for every pass so the SRP Batcher can batch the material. _BaseMap, _BaseColor,
// _Cutoff and _BaseMap_ST keep their URP names because the stock depth/shadow pass includes use them.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _MaskMap_ST;
half4 _BaseColor;
half4 _EmissionColor;
half4 _GlassColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _GlassSmoothness;
half _GlassMetallic;
half _WindowMix;
half _VertexEmission;
half _WorldUV;
half _PropSurface;
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// Planar projection along the dominant axis of the normal, in metres. Lets a surface whose mesh UVs
// are used for something else (the walls' window atlas) still carry a tiling material texture.
float2 CityWorldPlanarUV(float3 positionWS, float3 normalWS)
{
    float3 n = abs(normalWS);
    if (n.y > max(n.x, n.z)) return positionWS.xz;
    return n.x > n.z ? positionWS.zy : positionWS.xy;
}

// With _PropSurface the surface comes from the vertex instead: vertexSurface = (emission, roughness, metalness).
// 1 (props): albedo = BaseMap (the shared grime) * vertex colour, glowing by its own colour.
// 2 (decals): the atlas gives the colour and the opacity; the atlas glows by the vertex's emission.
//
// Albedo = BaseMap * BaseColor * vertex colour; smoothness = MaskMap.r * _Smoothness. Where the window
// atlas (_EmissionMap) alpha marks glass, the surface becomes dark reflective glass and the atlas RGB
// glows. The atlas always uses the mesh UV; the base and mask maps use the mesh UV or, with _WorldUV,
// planar world coordinates.
void InitializeCitySurface(float2 uv, float3 positionWS, float3 normalWS, half3 vertexColor, half3 vertexSurface, out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;

    if (_PropSurface > 0.5)
    {
        half4 baseSample = SampleAlbedoAlpha(uv * _BaseMap_ST.xy + _BaseMap_ST.zw, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
        bool decal = _PropSurface > 1.5;
        half3 albedo = baseSample.rgb * _BaseColor.rgb * vertexColor;
        surfaceData.albedo = albedo;
        surfaceData.metallic = saturate(vertexSurface.z);
        surfaceData.smoothness = 1.0 - clamp(vertexSurface.y, 0.04, 1.0);
        surfaceData.specular = half3(0.0, 0.0, 0.0);
        surfaceData.normalTS = half3(0.0, 0.0, 1.0);
        surfaceData.emission = (decal ? albedo : vertexColor) * vertexSurface.x;
        surfaceData.occlusion = 1.0;
        surfaceData.alpha = decal ? baseSample.a : 1.0;
        return;
    }

    float2 surfaceUV = _WorldUV > 0.5 ? CityWorldPlanarUV(positionWS, normalWS) : uv;

    half4 albedoAlpha = SampleAlbedoAlpha(surfaceUV * _BaseMap_ST.xy + _BaseMap_ST.zw, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    half3 surfaceAlbedo = albedoAlpha.rgb * _BaseColor.rgb * vertexColor;
    half mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, surfaceUV * _MaskMap_ST.xy + _MaskMap_ST.zw).r;

    half4 atlas = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv);
    half glass = atlas.a * _WindowMix;

    surfaceData.albedo = lerp(surfaceAlbedo, _GlassColor.rgb * vertexColor, glass);
    surfaceData.metallic = lerp(_Metallic, _GlassMetallic, glass);
    surfaceData.smoothness = lerp(_Smoothness * mask, _GlassSmoothness, glass);
    surfaceData.specular = half3(0.0, 0.0, 0.0);
    surfaceData.normalTS = half3(0.0, 0.0, 1.0);
    surfaceData.emission = atlas.rgb * _EmissionColor.rgb * glass + vertexColor * _VertexEmission;
    surfaceData.occlusion = 1.0;
    surfaceData.alpha = 1.0;
}

#endif
