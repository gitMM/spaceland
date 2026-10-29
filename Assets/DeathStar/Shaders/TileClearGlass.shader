// Window glass where a real room stands behind it (StationInteriors): see-through from both sides, a light tint, a
// Fresnel reflection of the environment and a sun glint. Further out the windows use Tile Glass's fake rooms instead.
Shader "DeathStar/Tile Clear Glass"
{
    Properties
    {
        _GlassTint("Glass tint", Color) = (0.6, 0.66, 0.7, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.1
        _Reflection("Reflection", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlassTint;
                half _Opacity;
                half _Reflection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 N = normalize(i.normalWS) * (front ? 1 : -1);
                float3 V = normalize(i.positionWS - _WorldSpaceCameraPos);
                float nv = saturate(dot(N, -V));
                float fres = 0.04 + 0.96 * pow(1 - nv, 5);
                float3 R = reflect(V, N);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 env = GlossyEnvironmentReflection(R, 0.05, 1) * _Reflection;
                half3 glint = sun.color * sun.shadowAttenuation * pow(saturate(dot(R, sun.direction)), 900) * 40;
                half a = saturate(_Opacity + fres * 0.8);
                half3 col = _GlassTint.rgb * _Opacity * 0.05 + (env + glint) * fres;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
