#ifndef DEATHSTAR_TILE_SHADOW_INCLUDED
#define DEATHSTAR_TILE_SHADOW_INCLUDED

#include "TileCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

float4 TileShadowVert(TileAttributes v) : SV_POSITION
{
    UNITY_SETUP_INSTANCE_ID(v);
    float3 p = TransformObjectToWorld(v.positionOS.xyz);
    float3 n = TransformObjectToWorldNormal(v.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 l = normalize(_LightPosition - p);
    #else
        float3 l = _LightDirection;
    #endif
    return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, l)));
}

half4 TileShadowFrag() : SV_Target { return 0; }

#endif
