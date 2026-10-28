#ifndef DEATHSTAR_TILE_SHADOW_INCLUDED
#define DEATHSTAR_TILE_SHADOW_INCLUDED

#include "TileCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct TileShadowVaryings
{
    float4 positionCS : SV_POSITION;
    float fade        : TEXCOORD0;
};
TileShadowVaryings TileShadowVert(TileAttributes v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    TileShadowVaryings o;
    o.fade = TileKitFade();
    float3 p = TransformObjectToWorld(v.positionOS.xyz);
    float3 n = TransformObjectToWorldNormal(v.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 l = normalize(_LightPosition - p);
    #else
        float3 l = _LightDirection;
    #endif
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, l)));
    return o;
}

half4 TileShadowFrag(TileShadowVaryings i) : SV_Target { TileKitClip(i.fade, i.positionCS); return 0; }

#endif
