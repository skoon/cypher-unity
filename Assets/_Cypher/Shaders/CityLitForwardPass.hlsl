#ifndef CYPHER_CITY_LIT_FORWARD_PASS_INCLUDED
#define CYPHER_CITY_LIT_FORWARD_PASS_INCLUDED

#include "CityLitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Structure follows URP's LitForwardPass.hlsl, minus normal maps, parallax, detail, decals,
// lightmaps and LOD cross-fade, none of which the city uses.

#include "CityNightFill.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    float4 color      : COLOR;
    float4 texcoord1  : TEXCOORD1;   // only read in the per-vertex surface modes
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv        : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS  : TEXCOORD2;
    half3  color     : TEXCOORD4;
    half3  vertexSurface : TEXCOORD3;

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight : TEXCOORD5;   // x: fog, yzw: vertex light
#else
    half  fogFactor  : TEXCOORD5;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD6;
#endif

    half3 vertexSH   : TEXCOORD8;

    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.positionWS;
    inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    // No lightmaps: the first argument is unused on the SH path.
    inputData.bakedGI = SAMPLE_GI(float2(0, 0), input.vertexSH, inputData.normalWS);
    inputData.shadowMask = half4(1, 1, 1, 1);
}

Varyings CityPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
    half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

    output.uv = input.texcoord;   // raw: the atlas uses it as is; base/mask maps apply their own tiling
    output.color = input.color.rgb;
    output.vertexSurface = input.texcoord1.xyz;
    output.normalWS = normalInput.normalWS;
    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;

    OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    return output;
}

void CityPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData;
    InitializeCitySurface(input.uv, input.positionWS, input.normalWS, input.color, input.vertexSurface, surfaceData);

    InputData inputData;
    InitializeInputData(input, inputData);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    // Diffuse only: metals have no diffuse term, and the fills are unshadowed by design.
    color.rgb += surfaceData.albedo * (1.0 - surfaceData.metallic) * CityFillLight(inputData.normalWS);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = surfaceData.alpha;   // 1 for everything opaque; the decals' alpha when they are blended
    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
