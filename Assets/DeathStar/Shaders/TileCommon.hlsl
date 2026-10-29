#ifndef DEATHSTAR_TILE_COMMON_INCLUDED
#define DEATHSTAR_TILE_COMMON_INCLUDED

// Shared by the settlement tile shaders. Vertex layout from TileMesh:
//   color.rgb tint, color.a wear;  uv0 = (TilePattern, seed, edge kinds, part thickness);
//   uv1, uv2 = distances to the face's edges 0-3 and 4-7 (TileMesh.Edges.cs), or on glass pane metres and pane size in xy;
//   uv3 = family overlay colour (alpha 0 for none).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct TileAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float4 color      : COLOR;
    float4 code       : TEXCOORD0;
    float4 local      : TEXCOORD1;
    float4 extent     : TEXCOORD2;
    float4 overlay    : TEXCOORD3;
    float4 relief     : TEXCOORD6;   // plain armour of the mid and far levels: station position, layout (TileRelief.hlsl);
                                     // on a relief shell its station position and face plane (TileGreeble.hlsl)
    float2 atlas      : TEXCOORD7;   // StationChunks: where a chunk's massing reads its baked atlas
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Global: 1 renders every tile surface as plain grey clay (relief check).
float _TileClay;
// Global, while StationChunks bakes: 1 writes world normal and height over _TileBakePlane (xyz normal, w offset),
// 2 colour and glow, instead of lighting.
float _TileBake;
float4 _TileBakePlane;
// Global: 1 tints every tile with its family's overlay colour (planning view).
float _TileOverlay;
// Per renderer: alpha 1 replaces the family colour with this one (the Foundry's shape view).
float4 _TileOverlayTint;
// StationKit's props, by their material copies (TILE_KIT_MODE, a per-material value so nothing leaks to the tiles'
// batched draws): 1 instanced props, which take how far they have grown from their own y over x scale; 2 pipes and
// cables, world-space meshes flattened along their plate's normal, whose matrix's determinant is how far they have
// grown. As they flatten they also dither out, so a prop leaving at the ring's edge never pops.
#ifndef TILE_KIT_MODE
    #define TILE_KIT_MODE 0
#endif
float TileKitFade()
{
    float mode = TILE_KIT_MODE;
    if (mode < .5) return 1;
    if (mode > 1.5) return saturate(determinant((float3x3)UNITY_MATRIX_M));
    float3 x = float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20), y = float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21);
    return saturate(length(y) / max(length(x), 1e-5));
}
// Only the kit's copies dither (_TILE_KIT): a pixel shader that may discard keeps the depth test from writing early, so
// the tiles' own carry no discard.
void TileKitClip(float fade, float4 positionCS)
{
    #if defined(_TILE_KIT)
    if (fade < 1) clip(fade - InterleavedGradientNoise(positionCS.xy, 0) * .998 - .001);
    #endif
}

// Global: how many times supersampled the picture is (the lab), so fades follow the final picture's pixels. 0 = 1.
float _TileFootprintScale;
// Metres a pixel of the final picture spans here. viewWS: the position relative to the camera (its derivatives stay
// exact far from the origin).
float TileFootprint(float3 viewWS)
{
    return max(max(length(ddx(viewWS)), length(ddy(viewWS))), 1e-5) * max(_TileFootprintScale, 1);
}

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


#if defined(_TILE_SHELL)
// The far level's station relief (TileGreeble.hlsl): the depth passes trace it too, so the depth texture holds the
// relief and not its shell, and drop pixels whose ray leaves the shell.
#include "TileGreeble.hlsl"
#if UNITY_REVERSED_Z
    #define TILE_SHELL_DEPTH SV_DepthLessEqual
#else
    #define TILE_SHELL_DEPTH SV_DepthGreaterEqual
#endif
struct TileDepthVaryings
{
    noperspective centroid float4 positionCS : SV_POSITION;
    float3 viewWS     : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float4 station    : TEXCOORD2;
    float4 rect       : TEXCOORD3;
    float4 shell      : TEXCOORD4;
};
TileDepthVaryings TileDepthVert(TileAttributes v)
{
    TileDepthVaryings o;
    UNITY_SETUP_INSTANCE_ID(v);
    float3 p = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(p);
    o.viewWS = p - _WorldSpaceCameraPos;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.station = v.relief; o.rect = v.local; o.shell = v.extent;
    return o;
}
TileDepthVaryings TileDepthNormalVert(TileAttributes v) { return TileDepthVert(v); }
// The traced surface's depth and world normal (no sun: depth only); false where the ray leaves the shell.
bool TileShellSurface(TileDepthVaryings i, out float depth, out float3 normalWS)
{
    float3 wx = ddx(i.viewWS), wy = ddy(i.viewWS), sx = ddx(i.station.xyz), sy = ddy(i.station.xyz);
    GreebleHit hit; float3 positionWS; float lower;
    bool shown = GreebleShellSurface(i.viewWS, normalize(i.normalWS), i.station, i.rect, i.shell, wx, wy, sx, sy,
        TileFootprint(i.viewWS), 0, hit, positionWS, normalWS, lower);
    depth = GreebleDepth(positionWS, hit.face, i.positionCS.z);
    return shown && isfinite(depth);
}
half TileDepthFrag(TileDepthVaryings i, out float depth : TILE_SHELL_DEPTH) : SV_Target
{
    float3 n; if (!TileShellSurface(i, depth, n)) clip(-1);
    return depth;
}
half4 TileDepthNormalFrag(TileDepthVaryings i, out float depth : TILE_SHELL_DEPTH) : SV_Target
{
    float3 n; if (!TileShellSurface(i, depth, n)) clip(-1);
    return half4(n, 0);
}
#else
struct TileDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float fade        : TEXCOORD0;
};
TileDepthVaryings TileDepthVert(TileAttributes v)
{
    TileDepthVaryings o;
    UNITY_SETUP_INSTANCE_ID(v);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.fade = TileKitFade();
    return o;
}
half TileDepthFrag(TileDepthVaryings i) : SV_Target { TileKitClip(i.fade, i.positionCS); return i.positionCS.z; }

struct TileDepthNormalVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS   : TEXCOORD0;
    float fade        : TEXCOORD1;
};
TileDepthNormalVaryings TileDepthNormalVert(TileAttributes v)
{
    TileDepthNormalVaryings o;
    UNITY_SETUP_INSTANCE_ID(v);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.fade = TileKitFade();
    return o;
}
half4 TileDepthNormalFrag(TileDepthNormalVaryings i) : SV_Target { TileKitClip(i.fade, i.positionCS); return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
#endif

#endif
