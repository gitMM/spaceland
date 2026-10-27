#ifndef DEATHSTAR_TILE_COMMON_INCLUDED
#define DEATHSTAR_TILE_COMMON_INCLUDED

// Shared by the settlement tile shaders. Vertex layout from TileMesh:
//   color.rgb tint, color.a wear;  uv0 = (TilePattern, seed);  uv1 = pane metres;  uv2 = pane size;
//   uv3 = family overlay colour (alpha 0 for none).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct TileAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float4 color      : COLOR;
    float2 code       : TEXCOORD0;
    float2 local      : TEXCOORD1;
    float2 extent     : TEXCOORD2;
    float4 overlay    : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Global: 1 renders every tile surface as plain grey clay (relief check).
float _TileClay;
// Global: 1 tints every tile with its family's overlay colour (planning view).
float _TileOverlay;

float TileHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float TileHash3(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float TileNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(TileHash(i), TileHash(i + float2(1, 0)), f.x), lerp(TileHash(i + float2(0, 1)), TileHash(i + float2(1, 1)), f.x), f.y);
}

// Box-filtered square wave: 1 for the last `duty` of each unit of x. Averages out cleanly at a distance.
float StripeIntegral(float x, float duty) { return floor(x) * duty + max(frac(x) - (1 - duty), 0); }
float Stripe(float x, float duty)
{
    float w = max(fwidth(x), 1e-4);
    return saturate((StripeIntegral(x + 0.5 * w, duty) - StripeIntegral(x - 0.5 * w, duty)) / w);
}
// Thin line centred on every integer of x, `duty` of a unit wide.
float GridLine(float x, float duty) { return Stripe(x + duty * 0.5, duty); }


float4 TileDepthVert(TileAttributes v) : SV_POSITION
{
    UNITY_SETUP_INSTANCE_ID(v);
    return TransformObjectToHClip(v.positionOS.xyz);
}
half TileDepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }

struct TileDepthNormalVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS   : TEXCOORD0;
};
TileDepthNormalVaryings TileDepthNormalVert(TileAttributes v)
{
    TileDepthNormalVaryings o;
    UNITY_SETUP_INSTANCE_ID(v);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}
half4 TileDepthNormalFrag(TileDepthNormalVaryings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }

#endif
