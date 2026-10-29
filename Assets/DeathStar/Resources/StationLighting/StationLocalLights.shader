Shader "Hidden/DeathStar/Local Lights"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Front ZWrite Off ZTest Always Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            struct Lamp { float4 a,b,color,plane; };
            StructuredBuffer<Lamp> _LocalLamps;
            struct Varyings {float4 positionCS:SV_POSITION;nointerpolation uint id:TEXCOORD0;};
            Varyings Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID)
            {
                static const uint corners[36]={0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5};
                uint c=corners[vertex];float3 sign=float3(c&1,(c>>1)&1,(c>>2)&1)*2-1;
                Lamp l=_LocalLamps[instance];float3 centre=(l.a.xyz+l.b.xyz)*.5;
                float3 extent=abs(l.b.xyz-l.a.xyz)*.5+l.a.w;
                Varyings o;o.positionCS=TransformWorldToHClip(centre+sign*extent);o.id=instance;return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 uv=i.positionCS.xy/_ScaledScreenParams.xy;
                float depth=SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                if(depth<1e-7)discard;
                #else
                if(depth>.9999999)discard;
                depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
                #endif
                float3 p=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
                Lamp l=_LocalLamps[i.id];float3 ab=l.b.xyz-l.a.xyz;
                float3 nearest=l.a.xyz+ab*saturate(dot(p-l.a.xyz,ab)/max(dot(ab,ab),1e-8));
                float3 delta=nearest-p;float distance=length(delta);float t=distance/max(l.a.w,.001);
                clip(1-t);
                // Optional mounting plane prevents kit lights spilling through their supporting wall.
                float side=dot(l.plane.xyz,l.plane.xyz)>.1?smoothstep(-.005,.015,dot(p,l.plane.xyz)+l.plane.w):1;
                float facing=saturate(dot(SampleSceneNormals(uv),delta/max(distance,.001)));
                float falloff=(1-t*t);falloff*=falloff;
                return half4(l.color.rgb*(l.b.w*falloff*facing*side),0);
            }
            ENDHLSL
        }
    }
}
