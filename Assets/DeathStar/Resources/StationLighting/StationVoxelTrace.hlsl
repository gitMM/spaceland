#ifndef STATION_VOXEL_TRACE_INCLUDED
#define STATION_VOXEL_TRACE_INCLUDED
// Premultiplied outgoing diffuse radiance and coverage; world units are metres.
Texture3D<float4> _StationVoxelRadiance;
SamplerState sampler_voxel_linear_clamp;
float4 _StationVoxelMin; // xyz minimum, w inverse volume width
float4 _StationVoxelParams; // voxel width, reach, strength, near handoff
float4 _StationVoxelGrid; // inverse directional base resolution, maximum directional mip
// Shared by every cone: normalized distance, mip, atlas margin, near handoff.
float4 _StationVoxelSteps[32];
int _StationVoxelStepCount;

float4 StationVoxelSample(float3 uv,float3 lobes,float3 weights,float4 step)
{
    // Six independent mip chains packed along X. Keep the footprint in its lobe.
    uv=clamp(uv,step.z,1-step.z);
    float4 value=0;
    [unroll] for(int axis=0;axis<3;axis++) {
        // Axial station walls/floors often have an exactly zero lobe weight.
        // Skip its texture read without approximating the directional blend.
        [branch] if(weights[axis]>0) {
            float3 at=uv;at.x=(at.x+lobes[axis])/6;
            value+=_StationVoxelRadiance.SampleLevel(sampler_voxel_linear_clamp,at,step.y)*weights[axis];
        }
    }
    return value;
}
float3 StationVoxelCone(float3 origin,float3 direction,float nearHandoff)
{
    // Origin and distances are already normalized to the cubic volume.
    float3 weights=direction*direction;
    float3 lobes=float3(direction.x<=0?0:1,direction.y<=0?2:3,direction.z<=0?4:5);
    float visibility=1;
    float3 radiance=0;
    [loop] for(int i=0;i<_StationVoxelStepCount && visibility>.025;i++) {
        float4 step=_StationVoxelSteps[i];
        float3 uv=origin+direction*step.x;
        if(any(uv<=0)||any(uv>=1))break;
        float4 sampleValue=StationVoxelSample(uv,lobes,weights,step);
        // Half-footprint opacity correction. Rationalization avoids subtracting
        // two nearly equal values and then dividing by a tiny opacity.
        float transmittance=sqrt(saturate(1-sampleValue.a));
        float handoff=nearHandoff>0?step.w:1;
        radiance+=visibility*sampleValue.rgb*(handoff/(1+transmittance));
        visibility*=transmittance;
    }
    return radiance;
}
float3 StationVoxelDiffuse(float3 p,float3 n,float handoff)
{
    float3 t=normalize(cross(abs(n.y)<.95?float3(0,1,0):float3(1,0,0),n));
    float3 b=cross(n,t);
    float3 origin=(p-_StationVoxelMin.xyz+n*(_StationVoxelParams.x*1.25))*_StationVoxelMin.w;
    // Six fixed cosine-weighted hemisphere cones, with no temporal history.
    float3 light=StationVoxelCone(origin,n,handoff)*.25;
    [unroll] for(int i=0;i<5;i++) {
        float a=i*1.2566370614;
        float3 d=(n+t*cos(a)+b*sin(a))*.70710678;
        light+=StationVoxelCone(origin,d,handoff)*.15;
    }
    return light*3.14159265;
}
#endif
