// Adapted from C:/Transhuman/Button, 2026-10-28.
Shader "Hidden/DeathStar/Screen GI"
{
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
        TEXTURE2D_X(_GiSource); TEXTURE2D_X(_GiReceiver); TEXTURE2D_X(_GiDelta);
        TEXTURE2D_X(_GiFiltered);
        float4 _GiParameters; // strength, reach, unused, unused
        float4 _GiAoParameters; // AO radius, intensity
        float4 _GiSize;
        int _GiOnly;
        bool Surface(float2 uv,out float3 p,out float3 n,out float eye) {
            float raw=SampleSceneDepth(uv);
            p=0;n=0;eye=0;
            #if UNITY_REVERSED_Z
                if(raw<1e-6) return false;
            #else
                if(raw>.999999) return false;
                raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
            #endif
            p=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
            eye=-TransformWorldToView(p).z;
            n=normalize(LoadSceneNormals((uint2)(uv*_ScaledScreenParams.xy)));return true;
        }
        #include "StationSsilvb.hlsl"
        float4 Gather(float2 uv,float3 p,float3 n,bool aoOnly) {
            float2 center=floor(uv*_GiSize.xy)+.5;
            float4 sum=0;float weights=0;
            // SSILVB has much lower variance than individual rays. A 3x3
            // reconstruction retains contacts and reduces full-resolution cost.
            [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++) {
                float2 sampleUv=(center+float2(x,y))*_GiSize.zw;
                if(any(sampleUv<=0) || any(sampleUv>=1)) continue;
                float3 q,qn;float qe;
                float2 geometryUv=(floor(sampleUv*_ScaledScreenParams.xy)+.5)/_ScaledScreenParams.xy;
                if(!Surface(geometryUv,q,qn,qe)) continue;
                float4 value=SAMPLE_TEXTURE2D_X_LOD(_GiDelta,sampler_PointClamp,sampleUv,0);
                // AO-only trace stores visibility in R; combined trace uses A.
                // Call sites pass a literal, so each filter specializes at compile time.
                if(aoOnly)value=float4(0,0,0,value.r);
                float valid=step(0,value.a);
                float weight=exp2(-.5*(x*x+y*y)-abs(dot(q-p,n))*120)*pow(saturate(dot(qn,n)),16)*valid;
                sum+=value*weight;weights+=weight;
            }
            return sum/max(weights,1e-5);
        }
        struct FilterOutput {float4 gi:SV_Target0;float ao:SV_Target1;};
        FilterOutput FilterCombined(Varyings input) {
            FilterOutput o;o.gi=0;o.ao=1;
            float3 p,n;float eye;
            if(!Surface(input.texcoord,p,n,eye))return o;
            float4 value=Gather(input.texcoord,p,n,false);
            o.gi=float4(value.rgb,1);o.ao=1-saturate(value.a);return o;
        }
        float FilterAo(Varyings input):SV_Target {
            float3 p,n;float eye;
            if(!Surface(input.texcoord,p,n,eye))return 1;
            return 1-saturate(Gather(input.texcoord,p,n,true).a);
        }
        float TraceAo(Varyings input):SV_Target { return TraceSsilvb(input).a; }
        float4 CompositeCombined(Varyings input):SV_Target {
            uint2 pixel=(uint2)input.positionCS.xy;
            float4 color=LOAD_TEXTURE2D_X(_BlitTexture,pixel);
            // AO-only uses a 1x1 black fallback instead of a receiver allocation.
            // Sample with clamp rather than issuing an out-of-bounds integer load.
            float4 receiver=SAMPLE_TEXTURE2D_X_LOD(_GiReceiver,sampler_PointClamp,input.texcoord,0);
            float3 indirect=SAMPLE_TEXTURE2D_X_LOD(_GiFiltered,sampler_PointClamp,input.texcoord,0).rgb*receiver.rgb;
            if(_GiOnly==2)return SAMPLE_TEXTURE2D_X_LOD(_GiFiltered,sampler_PointClamp,input.texcoord,0);
            if(_GiOnly!=0)return float4(max(0,indirect),1);
            color.rgb+=indirect;
            return color;
        }
        TEXTURE2D_X(_GiDebugAo);
        float4 ShowAo(Varyings input):SV_Target {
            float ao=SAMPLE_TEXTURE2D_X_LOD(_GiDebugAo,sampler_PointClamp,input.texcoord,0).r;
            return float4(ao,ao,ao,1);
        }
        ENDHLSL
        Pass {
            Name "Combined GI AO filter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FilterCombined
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass {
            Name "Combined GI composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeCombined
            ENDHLSL
        }
        Pass {
            Name "Screen AO diagnostic"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ShowAo
            ENDHLSL
        }
        Pass {
            Name "AO only filter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FilterAo
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass {
            Name "SSILVB GI AO trace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment TraceSsilvb
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass {
            Name "SSILVB AO only trace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment TraceAo
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
