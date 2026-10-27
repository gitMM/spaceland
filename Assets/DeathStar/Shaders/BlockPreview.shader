Shader "DeathStar/Block Preview"
{
    Properties { _Color("Color",Color)=(0.3,0.95,1,1) [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test",Float)=4 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off ZTest [_ZTest] Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            CBUFFER_END
            float4 Vert(float4 p:POSITION):SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 Frag():SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
