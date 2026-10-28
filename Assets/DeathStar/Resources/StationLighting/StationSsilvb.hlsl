// Adapted from C:/Transhuman/Button, 2026-10-28.
#ifndef STATION_SSILVB_INCLUDED
#define STATION_SSILVB_INCLUDED
// Visibility bitmask transport: Therrien, Levesque & Gilet, 2023,
// https://arxiv.org/abs/2301.11376 . Independently implemented for Station URP.
// We partition projected solid angle, rather than unweighted angle, into 32
// sectors. Popcount then integrates cosine-weighted visibility without a loop
// over bits, or the normal-dependent bias of uniform-angle popcount AO.
float4 _GiSliceParameters; // slices, samples per side, surface thickness, unused

#include "StationSsilvbMath.hlsl"
bool SsilPosition(float2 uv,out float3 position)
{
    uv=(floor(uv*_ScaledScreenParams.xy)+.5)/_ScaledScreenParams.xy;
    float depth=SampleSceneDepth(uv);
    position=0;
    #if UNITY_REVERSED_Z
        if(depth<1e-6)return false;
    #else
        if(depth>.999999)return false;
        depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
    #endif
    position=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
    return true;
}
float3 SsilGeometricNormal(float2 uv,float3 p,float3 fallback)
{
    // Normal maps must not turn a flat wall/floor into a large occluder. Use
    // nearest depth neighbours to reject coplanar samples, while keeping the
    // shading normal for the lighting integral. Select either side at edges.
    float2 texel=rcp(_ScaledScreenParams.xy);
    float3 l,r,b,t;
    bool vl=SsilPosition(uv-float2(texel.x,0),l),vr=SsilPosition(uv+float2(texel.x,0),r);
    bool vb=SsilPosition(uv-float2(0,texel.y),b),vt=SsilPosition(uv+float2(0,texel.y),t);
    float3 dx=vl&&(!vr||dot(l-p,l-p)<dot(r-p,r-p))?p-l:r-p;
    float3 dy=vb&&(!vt||dot(b-p,b-p)<dot(t-p,t-p))?p-b:t-p;
    float3 normal=cross(dx,dy);float lengthSq=dot(normal,normal);
    if(!(vl||vr)||!(vb||vt)||lengthSq<1e-16)return fallback;
    normal*=rsqrt(lengthSq);
    return dot(normal,fallback)<0?-normal:normal;
}
float4 TraceSsilvb(float2 uv,float3 p,float3 n,float eye)
{
    float3 geometricNormal=SsilGeometricNormal(uv,p,n);
    float3 view=GetWorldSpaceNormalizeViewDir(p);
    float centerDepth=SampleSceneDepth(uv);
    #if !UNITY_REVERSED_Z
        centerDepth=lerp(UNITY_NEAR_CLIP_VALUE,1,centerDepth);
    #endif
    bool giEnabled=_GiParameters.x>0;
    // Keep the AO sampling pattern identical in normal rendering, AO debug,
    // and with GI disabled. Only the radiance fetches depend on giEnabled.
    float radius=max(_GiParameters.y,_GiAoParameters.x);
    // Radius projected to UV, including orthographic cameras and aspect ratio.
    float2 uvRadius=.5*float2(abs(UNITY_MATRIX_P._m00),abs(UNITY_MATRIX_P._m11))*radius;
    uvRadius/=lerp(max(eye,.001),1,unity_OrthoParams.w);
    int slices=(int)_GiSliceParameters.x, steps=(int)_GiSliceParameters.y;
    float2 pixel=floor(uv*_GiSize.xy);
    float noise=frac(52.9829189*frac(dot(pixel,float2(.06711056,.00583715))));
    float3 lighting=0;float occlusion=0,normalization=0;
    [loop] for(int slice=0;slice<slices;slice++) {
        float angle=PI*(slice+noise)/slices;
        float s,c;sincos(angle,s,c);
        float2 offset=float2(c,s)*uvRadius;
        // Derive the slice from the same inverse GPU projection as depth. This
        // handles render-target y orientation and off-axis perspective exactly.
        float3 axis=ComputeWorldSpacePosition(uv+offset,centerDepth,UNITY_MATRIX_I_VP)-p;
        float3 tangent=normalize(axis-view*dot(axis,view));
        float nv=dot(n,view),nt=dot(n,tangent);
        float projectedLength=sqrt(nv*nv+nt*nt);
        if(projectedLength<1e-5)continue;
        float cosN=nv/projectedLength,sinN=nt/projectedLength;
        float normalAngle=atan2(sinN,cosN);
        float minAngle=normalAngle-HALF_PI,maxAngle=normalAngle+HALF_PI;
        float integralMin=SsilPrimitive(minAngle,cosN,sinN);
        float integralRange=max(1e-5,SsilPrimitive(maxAngle,cosN,sinN)-integralMin);
        float sliceWeight=projectedLength*integralRange;
        normalization+=sliceWeight;
        uint visibility=0u,aoVisibility=0u;
        float sliceAo=0;float3 sliceLight=0;
        float pixelLength=length(offset*_ScaledScreenParams.xy);
        float jitter=frac(noise*7.13+slice*.61803399);
        [loop] for(int step=0;step<steps;step++) {
            float t=(step+.5+jitter*.5)/steps;
            float distanceUV=max(1.25/max(pixelLength,1),t*t);
            [unroll] for(int side=-1;side<=1;side+=2) {
                float2 sampleUv=uv+offset*(distanceUV*side);
                if(any(sampleUv<=0)||any(sampleUv>=1))continue;
                float3 q;if(!SsilPosition(sampleUv,q))continue;
                float3 delta=q-p;
                float distanceSq=dot(delta,delta);
                if(distanceSq<1e-8 || distanceSq>radius*radius)continue;
                // Reject samples on/below the receiving plane (including itself).
                if(dot(geometricNormal,delta)<=.002)continue;
                float distance=sqrt(distanceSq);
                float3 direction=delta/distance;
                float3 back=delta-view*_GiSliceParameters.z;
                float frontAngle=atan2(dot(delta,tangent),dot(delta,view));
                float backAngle=atan2(dot(back,tangent),dot(back,view));
                float lo=clamp(min(frontAngle,backAngle),minAngle,maxAngle);
                float hi=clamp(max(frontAngle,backAngle),minAngle,maxAngle);
                uint mask=SsilSectorMask((SsilPrimitive(lo,cosN,sinN)-integralMin)/integralRange,
                                        (SsilPrimitive(hi,cosN,sinN)-integralMin)/integralRange);
                float edge=min(min(sampleUv.x,sampleUv.y),min(1-sampleUv.x,1-sampleUv.y));
                float confidence=smoothstep(0,.025,edge);
                if(distance<_GiAoParameters.x) {
                    uint freshAo=mask & ~aoVisibility;
                    aoVisibility|=mask;
                    sliceAo+=countbits(freshAo)*confidence*(1-smoothstep(_GiAoParameters.x*.75,_GiAoParameters.x,distance));
                }
                uint fresh=mask & ~visibility;
                // Unlit/unsupported/backfacing surfaces still block later emitters.
                visibility|=mask;
                if(!giEnabled || fresh==0u || distance>_GiParameters.y)continue;
                float3 hitNormal=LoadSceneNormals((uint2)(sampleUv*_ScaledScreenParams.xy));
                if(dot(hitNormal,-direction)<=0)continue;
                float4 source=SAMPLE_TEXTURE2D_X_LOD(_GiSource,sampler_PointClamp,sampleUv,0);
                // Lambertian source radiance is independent of exit angle. The
                // receiver cosine is already integrated by the sector mapping.
                sliceLight+=source.rgb*source.a*countbits(fresh)*confidence*
                    (1-smoothstep(_GiParameters.y*.75,_GiParameters.y,distance));
            }
            if(visibility==0xffffffffu && aoVisibility==0xffffffffu)break;
        }
        lighting+=sliceLight*sliceWeight;occlusion+=sliceAo*sliceWeight;
    }
    float weight=rcp(max(normalization,1e-5)*32);
    return float4(max(0,lighting*(PI*_GiParameters.x*weight)),saturate(occlusion*weight*_GiAoParameters.y));
}
float4 TraceSsilvb(Varyings input):SV_Target
{
    float2 uv=(floor(input.texcoord*_ScaledScreenParams.xy)+.5)/_ScaledScreenParams.xy;
    float3 p,n;float eye;
    if(!Surface(uv,p,n,eye))return float4(0,0,0,-1);
    return TraceSsilvb(uv,p,n,eye);
}
#endif
