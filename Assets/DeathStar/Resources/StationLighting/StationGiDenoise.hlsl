#ifndef STATION_GI_DENOISE_INCLUDED
#define STATION_GI_DENOISE_INCLUDED
TEXTURE2D_X(_GiGeometry);
TEXTURE2D_X(_GiHistory);
TEXTURE2D_X(_GiHistoryGeometry);
TEXTURE2D_X(_GiMotion);
float4x4 _GiPreviousVP,_GiPreviousView,_GiCurrentVP;
float4 _GiTemporalParameters; // valid history, motion vectors valid, maximum history length
int _GiFilterStride;

float2 GiGeometryUv(float2 uv) {return (floor(uv*_ScaledScreenParams.xy)+.5)/_ScaledScreenParams.xy;}
float GiLuminance(float3 c) {return dot(c,float3(.2126,.7152,.0722));}
float GiFootprint(float eye) {
    return 2*lerp(eye,1,unity_OrthoParams.w)/(abs(unity_CameraProjection._m11)*_GiSize.y);
}
float4 GiGeometry(Varyings input):SV_Target {
    float2 uv=GiGeometryUv(input.texcoord);
    float3 p,n;float eye;
    if(!Surface(uv,p,n,eye))return 0;
    return float4(SsilGeometricNormal(uv,p,n),eye);
}
float GiGuideWeight(float3 p,float3 n,float eye,float2 uv,float4 guide) {
    if(guide.a<=0)return 0;
    float3 q;if(!SsilPosition(GiGeometryUv(uv),q))return 0;
    float plane=max(abs(dot(q-p,n)),abs(dot(q-p,guide.xyz)));
    float tolerance=max(.006,GiFootprint(eye)*.75);
    return exp2(-plane/tolerance)*pow(saturate(dot(n,guide.xyz)),8);
}
struct GiTemporalOutput {float4 light:SV_Target0;float4 geometry:SV_Target1;};
GiTemporalOutput GiTemporal(Varyings input) {
    GiTemporalOutput o;
    float2 uv=input.texcoord;
    float4 guide=SAMPLE_TEXTURE2D_X_LOD(_GiGeometry,sampler_PointClamp,uv,0);
    float4 raw=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,uv,0);
    o.geometry=guide;o.light=float4(max(0,raw.rgb),1);
    if(guide.a<=0){o.light=0;return o;}
    if(_GiTemporalParameters.x<.5)return o;
    float3 p;if(!SsilPosition(GiGeometryUv(uv),p))return o;
    float4 previousClip=mul(_GiPreviousVP,float4(p,1));
    if(previousClip.w<=0)return o;
    float2 previousUv=ComputeNormalizedDeviceCoordinates(p,_GiPreviousVP);
    // Match the orientation of the inverse projection used by URP's depth buffer.
    float selfY=ComputeNormalizedDeviceCoordinates(p,_GiCurrentVP).y;
    if(abs(1-selfY-uv.y)<abs(selfY-uv.y))previousUv.y=1-previousUv.y;
    if(_GiTemporalParameters.y>.5)previousUv=GiGeometryUv(uv)-SAMPLE_TEXTURE2D_X_LOD(_GiMotion,sampler_PointClamp,GiGeometryUv(uv),0).xy;
    if(any(previousUv<=0)||any(previousUv>=1))return o;
    float expectedEye=-mul(_GiPreviousView,float4(p,1)).z;
    float tolerance=max(.015,GiFootprint(guide.a)*2);
    float2 texel=previousUv*_GiSize.xy-.5;
    float2 basePixel=floor(texel),fraction=frac(texel);
    float4 history=0;float weights=0;
    // Validate each bilinear tap separately, so a disocclusion cannot mix a
    // foreground's history into a newly revealed background.
    [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++) {
        float2 q=(basePixel+float2(x,y)+.5)*_GiSize.zw;
        if(any(q<=0)||any(q>=1))continue;
        float4 oldGuide=SAMPLE_TEXTURE2D_X_LOD(_GiHistoryGeometry,sampler_PointClamp,q,0);
        if(oldGuide.a<=0||abs(oldGuide.a-expectedEye)>tolerance||dot(oldGuide.xyz,guide.xyz)<.9)continue;
        float w=(x?fraction.x:1-fraction.x)*(y?fraction.y:1-fraction.y);
        history+=SAMPLE_TEXTURE2D_X_LOD(_GiHistory,sampler_PointClamp,q,0)*w;weights+=w;
    }
    if(weights<.1)return o;
    history/=weights;
    float3 mean=0,second=0,lo=raw.rgb,hi=raw.rgb;float total=0;
    [unroll]for(int j=-1;j<=1;j++)[unroll]for(int i=-1;i<=1;i++) {
        float2 q=uv+float2(i,j)*_GiSize.zw;
        if(any(q<=0)||any(q>=1))continue;
        float4 g=SAMPLE_TEXTURE2D_X_LOD(_GiGeometry,sampler_PointClamp,q,0);
        float w=GiGuideWeight(p,guide.xyz,guide.a,q,g);
        if(w<.05)continue;
        float3 c=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,q,0).rgb;
        mean+=c*w;second+=c*c*w;total+=w;lo=min(lo,c);hi=max(hi,c);
    }
    mean/=max(total,1e-5);second/=max(total,1e-5);
    float3 sigma=sqrt(max(0,second-mean*mean));
    // Clip radiance to this frame's valid neighbourhood. This removes stale
    // illumination without feeding the accumulated result into source shading.
    float3 clipped=clamp(history.rgb,max(lo,mean-2*sigma),min(hi,mean+2*sigma));
    float count=min(history.a+1,_GiTemporalParameters.z);
    float clipping=length(clipped-history.rgb)/max(.05,length(history.rgb));
    count=lerp(count,min(count,2),saturate(clipping));
    o.light=float4(lerp(clipped,raw.rgb,rcp(max(count,1))),count);
    return o;
}
float4 GiSpatial(Varyings input):SV_Target {
    float2 uv=input.texcoord;
    float4 center=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,uv,0);
    float4 guide=SAMPLE_TEXTURE2D_X_LOD(_GiGeometry,sampler_PointClamp,uv,0);
    if(guide.a<=0)return 0;
    float3 p;if(!SsilPosition(GiGeometryUv(uv),p))return 0;
    float centerLuma=GiLuminance(center.rgb),mean=0,second=0,total=0;
    // Local luminance variance sets the range weight: noise is smoothed more
    // than a coherent light boundary. Geometry gates both statistics and taps.
    [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++) {
        float2 q=uv+float2(x,y)*_GiSize.zw*_GiFilterStride;
        if(any(q<=0)||any(q>=1))continue;
        float4 g=SAMPLE_TEXTURE2D_X_LOD(_GiGeometry,sampler_PointClamp,q,0);
        float w=GiGuideWeight(p,guide.xyz,guide.a,q,g);
        float l=GiLuminance(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,q,0).rgb);
        mean+=l*w;second+=l*l*w;total+=w;
    }
    mean/=max(total,1e-5);second/=max(total,1e-5);
    float sigma=max(.02,sqrt(max(0,second-mean*mean))*3+centerLuma*.2);
    float3 sum=0;float weights=0;
    [unroll]for(int j=-1;j<=1;j++)[unroll]for(int i=-1;i<=1;i++) {
        float2 q=uv+float2(i,j)*_GiSize.zw*_GiFilterStride;
        if(any(q<=0)||any(q>=1))continue;
        float4 g=SAMPLE_TEXTURE2D_X_LOD(_GiGeometry,sampler_PointClamp,q,0);
        float3 c=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,q,0).rgb;
        float w=GiGuideWeight(p,guide.xyz,guide.a,q,g)*(i==0?2:1)*(j==0?2:1);
        w*=exp2(-abs(GiLuminance(c)-centerLuma)/sigma);
        sum+=c*w;weights+=w;
    }
    return float4(sum/max(weights,1e-5),center.a);
}
float4 GiUpsample(Varyings input):SV_Target {
    float3 p,n;float eye;
    if(!Surface(input.texcoord,p,n,eye))return 0;
    n=SsilGeometricNormal(input.texcoord,p,n);
    float2 pixel=input.texcoord*_GiSize.xy-.5,basePixel=floor(pixel);
    float3 sum=0;float weights=0;
    [unroll]for(int y=-1;y<=2;y++)[unroll]for(int x=-1;x<=2;x++) {
        float2 at=basePixel+float2(x,y),q=(at+.5)*_GiSize.zw;
        if(any(q<=0)||any(q>=1))continue;
        float4 guide=SAMPLE_TEXTURE2D_X_LOD(_GiGeometry,sampler_PointClamp,q,0);
        float2 d=at-pixel;
        float w=GiGuideWeight(p,n,eye,q,guide)*exp2(-dot(d,d));
        sum+=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,q,0).rgb*w;weights+=w;
    }
    return float4(sum/max(weights,1e-5),1);
}
#endif
