// Adapted from C:/Transhuman/Button, 2026-10-28.
Shader "Hidden/DeathStar/Screen GI"
{
    Properties { [HideInInspector] _StationVoxelRadiance ("Voxel radiance",3D) = "" {} }
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
        float4 _GiParameters; // screen strength, screen reach, voxel enabled, voxel-only debug
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
        #include "StationVoxelTrace.hlsl"
        float4 TraceCombined(Varyings input):SV_Target {
            float2 uv=(floor(input.texcoord*_ScaledScreenParams.xy)+.5)/_ScaledScreenParams.xy;
            float3 p,n;float eye;
            if(!Surface(uv,p,n,eye))return float4(0,0,0,-1);
            float4 result=TraceSsilvb(uv,p,n,eye);
            if(_GiParameters.z>=.5) {
                float handoff=(_GiParameters.x>0&&_GiParameters.w<.5)?_StationVoxelParams.w:0;
                result.rgb+=StationVoxelDiffuse(p,n,handoff)*_StationVoxelParams.z;
            }
            return result;
        }
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
        float FilterCombinedAo(Varyings input):SV_Target {
            float3 p,n;float eye;
            if(!Surface(input.texcoord,p,n,eye))return 1;
            return 1-saturate(Gather(input.texcoord,p,n,false).a);
        }
        #include "StationGiDenoise.hlsl"
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
        // Sun contact shadows: march from each visible surface toward the sun through the depth buffer. A step
        // lands in shadow when the visible surface there is in front of it by less than the assumed thickness.
        float4 _ContactParams;   // length near, length far, far distance, thickness
        float4 _ContactSettings; // steps, strength
        float4 _ContactLightDir; // toward the sun, world space
        float ContactShadow(Varyings input):SV_Target {
            float3 p,n;float eye;
            if(!Surface(input.texcoord,p,n,eye))return 1;
            float3 l=normalize(_ContactLightDir.xyz);
            // Faces turned from the sun get no direct light to shadow.
            if(dot(n,l)<=.01)return 1;
            float len=lerp(_ContactParams.x,_ContactParams.y,saturate(eye/_ContactParams.z));
            float thickness=_ContactParams.w;
            int steps=(int)_ContactSettings.x;
            // One pixel's footprint per metre of eye depth.
            // (The camera's own projection: in a blit the GPU projection can be flipped, with m11 negative.)
            float pixel=2/(abs(unity_CameraProjection._m11)*_ScaledScreenParams.y);
            // Start off the receiver by about a pixel so its own depth samples never shadow it.
            float3 start=p+(n+l)*(.003+eye*pixel);
            float jitter=InterleavedGradientNoise(input.positionCS.xy,0);
            // In a blit URP's view-projection can be flipped against the inverse that rebuilt p (its own source notes
            // invP*P may not be I). Calibrate on the pixel itself: if p projects to the mirrored row, mirror every step.
            float selfY=ComputeNormalizedDeviceCoordinates(p,UNITY_MATRIX_VP).y;
            bool mirror=abs(1-selfY-input.texcoord.y)<abs(selfY-input.texcoord.y);
            float shadow=0;
            [loop] for(int i=0;i<steps;i++) {
                float t=(i+jitter)/steps;t*=t;   // denser near the receiver, where contacts are
                float3 q=start+l*(len*t);
                float3 ndc=ComputeNormalizedDeviceCoordinatesWithZ(q,UNITY_MATRIX_VP);
                if(mirror)ndc.y=1-ndc.y;
                if(any(ndc.xy<=0)||any(ndc.xy>=1))break;
                float ray=-TransformWorldToView(q).z;
                if(ray<=0)break;
                float scene=LinearEyeDepth(SampleSceneDepth(ndc.xy),_ZBufferParams);
                float gap=ray-scene;
                // Depth is stored per pixel centre: allow for the slope across a pixel at this depth.
                float tolerance=.002+ray*pixel*1.5;
                float hit=saturate((gap-tolerance)/tolerance)*saturate((thickness-gap)/(thickness*.25));
                // The last quarter of the ray fades, so the shadow map takes over without a hard end.
                shadow=max(shadow,hit*saturate((1-t)*4));
            }
            return 1-shadow*_ContactSettings.y;
        }
        // The march jitters per pixel; a 3 x 3 average over neighbours at the same depth turns its dither into a soft
        // penumbra without bleeding across silhouettes.
        TEXTURE2D_X(_ContactRaw);
        float ContactBlur(Varyings input):SV_Target {
            uint2 pixel=(uint2)input.positionCS.xy;
            float raw=LOAD_TEXTURE2D_X(_CameraDepthTexture,pixel).r;
            float eye=LinearEyeDepth(raw,_ZBufferParams);
            float sum=0,weights=0;
            [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++) {
                int2 q=clamp(int2(pixel)+int2(x,y),0,int2(_ScaledScreenParams.xy)-1);
                float e=LinearEyeDepth(LOAD_TEXTURE2D_X(_CameraDepthTexture,q).r,_ZBufferParams);
                float w=exp2(-abs(e-eye)/max(eye*.01,1e-3))*(x==0&&y==0?2:1);
                sum+=LOAD_TEXTURE2D_X(_ContactRaw,q).r*w;weights+=w;
            }
            return sum/max(weights,1e-5);
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
            #pragma fragment TraceCombined
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
        Pass {
            Name "Sun contact shadows"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ContactShadow
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass {
            Name "Sun contact shadow blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ContactBlur
            ENDHLSL
        }
        Pass {
            Name "Combined trace AO filter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FilterCombinedAo
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass {
            Name "GI geometric guides"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment GiGeometry
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass {
            Name "GI temporal accumulation"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment GiTemporal
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass {
            Name "GI edge-aware spatial filter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment GiSpatial
            ENDHLSL
        }
        Pass {
            Name "GI geometric upsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment GiUpsample
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
