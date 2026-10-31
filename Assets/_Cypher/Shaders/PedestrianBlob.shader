// The soft dark disc under each person (the prototype's contact shadow; scene shadows are off). One small triangle fan, instanced from the same buffer as the
// bodies: it lies just above the ground under the person's feet, scaled to their height (wider under an umbrella), and fades from dark in the middle to nothing
// at the rim. Blended over the ground, after the opaque geometry, behind the fog.
Shader "Cypher/PedestrianBlob"
{
    Properties
    {
        _Opacity("Opacity", Range(0, 1)) = 0.42
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
            Name "Blob"
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
            #include "PedestrianPose.hlsl"

            StructuredBuffer<PedInstance> _Peds;
            float _InstanceBase;
            half _Opacity;

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
                PedInstance p = _Peds[(uint)(_InstanceBase + 0.5) + instanceID];
                // a person under an umbrella casts a wider shadow
                float size = (((p.extra.y & PED_FLAG_UMBRELLA) != 0u) ? 1.5 : 0.95) * p.b.z;
                float3 positionWS = float3(p.a.x + input.positionOS.x * size, p.a.y + 0.016, p.a.z + input.positionOS.z * size);
                // a fighter lying dead is a body length back from its feet: the shadow goes with the body
                float fall = saturate(p.d.z) * saturate(p.d.z);
                positionWS.xz -= float2(sin(p.a.w), cos(p.a.w)) * (0.85 * p.b.z * fall);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.disc = input.positionOS.xz / 0.44;   // the disc's radius
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
