// Gunfire effects (Cypher.Core.ImpactSim), drawn from dynamic meshes the game rebuilds each frame. Vertex colour is the tint (alpha is the fade), TEXCOORD0 the
// quad's own coordinates (0..1). Two shaders in one file, chosen per material:
//   Cypher/GunFxAdditive: _Mode 0 a spark (a hot core and a soft halo, as the prototype's point sprite), 1 a beam (soft across its width: tracers, flash cones),
//                          2 a texture (the muzzle glow), 3 solid (the flash's core and petals); _Tint colours it (HDR). _DepthSquash > 0 puts it in the viewmodel's sliver of depth (the flash at the muzzle).
//   Cypher/GunFxAlpha: _Mode 0 a smoke puff (a soft disc), 1 a texture (the bullet-hole decal), with fog when _Fog is on.
Shader "Cypher/GunFxAdditive"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Mode("Mode (0 spark, 1 beam, 2 texture, 3 solid)", Float) = 0
        [HDR] _Tint("Tint", Color) = (1, 1, 1, 1)
        _Gain("Gain", Float) = 1
        _DepthSquash("Viewmodel depth squash (0 off)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Mode;
            float _Gain;
            float _DepthSquash;
            float4 _Tint;
            CBUFFER_END
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : TEXCOORD0; float2 uv : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float4 cs = TransformObjectToHClip(input.positionOS.xyz);
                if (_DepthSquash > 0)
                {
                    float ndc = cs.z / cs.w;
                    cs.z = (UNITY_NEAR_CLIP_VALUE + (ndc - UNITY_NEAR_CLIP_VALUE) * _DepthSquash) * cs.w;
                }
                o.positionCS = cs;
                o.color = input.color;
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 rgb;
                if (_Mode < 0.5)
                {
                    float2 d = input.uv - 0.5;
                    float r2 = dot(d, d);
                    if (r2 > 0.25) discard;
                    // a tight hot core with a soft halo reads as a molten fleck, not a blob
                    float core = smoothstep(0.055, 0.0, r2);
                    float halo = smoothstep(0.25, 0.0, r2);
                    rgb = input.color.rgb * (0.35 + core * 2.4) * (halo * 0.45 + core);
                }
                else if (_Mode < 1.5)
                {
                    float across = 1.0 - abs(input.uv.y * 2.0 - 1.0);
                    rgb = input.color.rgb * smoothstep(0.0, 0.7, across);
                }
                else if (_Mode < 2.5)
                {
                    rgb = input.color.rgb * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                }
                else
                {
                    rgb = input.color.rgb;
                }
                return half4(rgb * _Tint.rgb * input.color.a * _Gain, 1);
            }
            ENDHLSL
        }
    }
}
