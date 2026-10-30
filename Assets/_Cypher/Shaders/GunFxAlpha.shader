// Gunfire effects that cover rather than glow: smoke puffs (_Mode 0, a soft disc) and the bullet-hole decals (_Mode 1, the texture tinted by the vertex
// colour), alpha blended, depth-tested, not written. Decals are pulled toward the camera so they never fight the wall they sit on, and take the fog.
// See GunFx.shader for the additive half.
Shader "Cypher/GunFxAlpha"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Mode("Mode (0 smoke, 1 texture)", Float) = 0
        [Toggle] _Fog("Fog", Float) = 0
        _OffsetFactor("Depth Offset Factor", Float) = 0
        _OffsetUnits("Depth Offset Units", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Mode;
            float _Fog;
            float _OffsetFactor;
            float _OffsetUnits;
            CBUFFER_END
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : TEXCOORD0; float2 uv : TEXCOORD1; half fog : TEXCOORD2; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c;
                if (_Mode < 0.5)
                {
                    float2 d = input.uv - 0.5;
                    float r2 = dot(d, d);
                    if (r2 > 0.25) discard;
                    float a = smoothstep(0.25, 0.02, r2);
                    c = half4(input.color.rgb, a * a * input.color.a);
                }
                else
                {
                    half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    c = half4(t.rgb * input.color.rgb, t.a * input.color.a);
                }
                if (_Fog > 0.5) c.rgb = MixFog(c.rgb, input.fog);
                return c;
            }
            ENDHLSL
        }
    }
}
