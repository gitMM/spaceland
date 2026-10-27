#ifndef DEATH_STAR_COMMON
#define DEATH_STAR_COMMON
struct CellPlan { float height, tone, density, feature; uint kind, seed, reserved0, reserved1; };
// Exactly 32 bytes. Surface placement is reconstructed from cell identity.
struct Primitive { uint cell, flags; float lower, upper; float4 rect; };
StructuredBuffer<CellPlan> _Plans;
int _Resolution;
float _Radius;

void FaceFrame(uint face, out float3 n, out float3 u, out float3 v)
{
    if(face==0) { n=float3(1,0,0); u=float3(0,0,-1); v=float3(0,1,0); }
    else if(face==1) { n=float3(-1,0,0); u=float3(0,0,1); v=float3(0,1,0); }
    else if(face==2) { n=float3(0,1,0); u=float3(1,0,0); v=float3(0,0,-1); }
    else if(face==3) { n=float3(0,-1,0); u=float3(1,0,0); v=float3(0,0,1); }
    else if(face==4) { n=float3(0,0,1); u=float3(1,0,0); v=float3(0,1,0); }
    else { n=float3(0,0,-1); u=float3(-1,0,0); v=float3(0,1,0); }
}
float3 CubePoint(uint id, float2 uv, out float3 u, out float3 v)
{
    uint n=(uint)_Resolution;
    float3 normal;
    FaceFrame(id/(n*n),normal,u,v);
    float2 xy=float2(id%n,(id/n)%n);
    return normal + u*((xy.x+uv.x)*2/n-1) + v*((xy.y+uv.y)*2/n-1);
}
float3 CellDirection(uint id)
{ float3 u,v; return normalize(CubePoint(id,0.5,u,v)); }
uint PlanetHash(uint x) { x^=x>>16; x*=0x7feb352du; x^=x>>15; x*=0x846ca68bu; return x^(x>>16); }
float PlanetRandom(uint x) { return (PlanetHash(x)&0xffffffu)/16777216.0; }
#endif
