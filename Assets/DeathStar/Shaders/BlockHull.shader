Shader "DeathStar/Block Hull"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; float4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.color=v.color;return o;
            }
            float Line(float p,float count,float width)
            {
                float q=p*count;float d=min(frac(q),1-frac(q));float aa=max(fwidth(q),0.0001);
                return (1-smoothstep(width,width+aa,d))*saturate(1-aa);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float edge=min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y));
                float aa=max(fwidth(edge),0.0001);
                float seam=1-smoothstep(0.015,0.015+aa,edge);
                float inset=(1-smoothstep(0.035,0.035+aa,edge))*(1-seam);
                float circuit=max(Line(i.uv.x,3+floor(i.color.g*3),0.012),Line(i.uv.y,2,0.014));
                float ventZone=step(0.62,i.color.g)*step(0.19,i.uv.x)*step(i.uv.x,0.78)*step(0.28,i.uv.y)*step(i.uv.y,0.66);
                float vents=Line(i.uv.y,19,0.2)*ventZone;
                float albedo=i.color.r*(1-0.75*seam-0.2*inset)*(1-0.16*circuit)*(1-0.55*vents);
                float3 n=normalize(i.normal);float3 sun=normalize(float3(-0.5,0.8,-0.65));
                float lit=saturate(dot(n,sun));float fill=saturate(dot(n,normalize(float3(0.6,0.1,0.8))));
                float3 view=normalize(_WorldSpaceCameraPos-i.world);
                float spec=pow(saturate(dot(n,normalize(sun+view))),48)*0.12*lit;
                float3 color=albedo*(float3(0.16,0.21,0.28)+lit*float3(1.05,0.97,0.82)+fill*float3(0.08,0.13,0.17))+spec;
                float light=step(0.78,i.color.b)*step(0.9,i.uv.x)*step(i.uv.x,0.96)*step(0.13,i.uv.y)*step(i.uv.y,0.18);
                color+=light*float3(0.28,0.8,1.1);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
