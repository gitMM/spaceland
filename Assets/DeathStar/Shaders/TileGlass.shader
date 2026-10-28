// Tile glazing: tinted glass in front of a fake room (interior mapping, no geometry behind it). The ray from the eye
// is traced through a box the size of the pane plus margins, so the room shifts with parallax as the camera moves.
// Each pane draws its own lighting level and sometimes a half-drawn blind. Fresnel reflection of the environment and a
// sun glint (shadowed) sit on top.
Shader "DeathStar/Tile Glass"
{
    Properties
    {
        _GlassTint("Glass tint", Color) = (0.5, 0.56, 0.6, 1)
        _RoomColor("Room colour", Color) = (1, 0.74, 0.5, 1)
        _RoomLight("Room brightness", Float) = 1.6
        _RoomDepth("Room depth", Float) = 2.8
        _Reflection("Reflection", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        float _StationEmission;
        #include "TileCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _GlassTint;
            half4 _RoomColor;
            half _RoomLight;
            half _RoomDepth;
            half _Reflection;
        CBUFFER_END
        ENDHLSL

        Pass {
            Name "StationGiSource"
            Tags { "LightMode"="StationGiSource" }
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TileDepthVert
            #pragma fragment GlassSource
            #pragma multi_compile_instancing
            struct GlassSourceOutput {float4 source:SV_Target0;float4 receiver:SV_Target1;};
            GlassSourceOutput GlassSource() {GlassSourceOutput o;o.source=0;o.receiver=0;return o;}
            ENDHLSL
        }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 local      : TEXCOORD3;
                float2 extent     : TEXCOORD4;
                float pane        : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TileAttributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w);
                o.local = v.local;
                o.extent = v.extent;
                // Pane's own number mixed with where the tile stands, so repeated tiles light their rooms differently.
                float3 origin = UNITY_MATRIX_M._m03_m13_m23;
                o.pane = frac(v.code.x + TileHash3(round(origin * 0.5) + 0.37) * 7.13);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 N = normalize(i.normalWS);
                float3 T = normalize(i.tangentWS.xyz);
                float3 B = cross(N, T) * i.tangentWS.w;
                float3 V = normalize(i.positionWS - _WorldSpaceCameraPos);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half sunVis = sun.shadowAttenuation * sun.distanceAttenuation;
                if (_TileClay > 0.5)
                    return half4(0.2 * (saturate(dot(N, sun.direction)) * sunVis * sun.color + SampleSH(N)), 1);

                // Trace the eye ray through the room: x across the pane, y up, z into the room.
                float2 size = max(i.extent, 0.05);
                float3 o = float3(i.local, 0);
                float3 d = float3(dot(V, T), dot(V, B), max(-dot(V, N), 1e-3));
                float3 lo = float3(-0.3, -1.0, 0), hi = float3(size.x + 0.3, size.y + 0.5, _RoomDepth);
                float3 tExit = max((lo - o) / d, (hi - o) / d);
                float t = min(tExit.x, min(tExit.y, tExit.z));
                float3 h = o + d * t;
                float3 r = (h - lo) / (hi - lo);
                half3 warm = _RoomColor.rgb;
                half3 c;
                float3 edge = min(h - lo, hi - h);
                if (t == tExit.z)
                {
                    c = warm * (0.38 + 0.3 * r.y);
                    c += warm * 0.7 * exp(-pow((r.x - 0.5) * 2.6, 2) - pow((r.y - 0.8) * 4.5, 2));
                    edge.z = 9;
                }
                else if (t == tExit.y)
                {
                    if (d.y < 0) c = warm * (0.06 + 0.12 * r.z);
                    else c = warm * (0.2 + 2.2 * step(abs(h.x - (lo.x + hi.x) * 0.5), 0.55) * step(abs(h.z - 1.2), 0.35));
                    edge.y = 9;
                }
                else
                {
                    c = warm * (0.14 + 0.22 * r.z) * (0.6 + 0.4 * r.y);
                    edge.x = 9;
                }
                c *= 0.45 + 0.55 * saturate(min(edge.x, min(edge.y, edge.z)) / 0.5);
                // A low furniture silhouette part way in, against the lit back wall.
                float zc = _RoomDepth * 0.55;
                float tc = (zc - o.z) / d.z;
                float3 hc = o + d * tc;
                float span = lerp(0.35, 0.75, frac(i.pane * 5.17));
                float x0 = lerp(lo.x, hi.x - span * (hi.x - lo.x), frac(i.pane * 3.91));
                if (tc < t && hc.y < lerp(-0.25, 0.25, frac(i.pane * 9.3)) && hc.x > x0 && hc.x < x0 + span * (hi.x - lo.x))
                    c = warm * 0.05;

                // Some rooms dark, some dim, most lit; some with a blind part way down.
                float lit = i.pane < 0.08 ? 0.05 : (i.pane < 0.22 ? 0.4 : 1.0);
                c *= lit * _RoomLight;
                float blind = frac(i.pane * 13.71);
                if (blind > 0.45)
                {
                    float cover = lerp(0.15, 0.55, frac(blind * 7.3));
                    float slat = Stripe(i.local.y / 0.055, 0.22);
                    half3 cloth = warm * lit * _RoomLight * 0.42 * (1 - slat * 0.55) + 0.015;
                    c = lerp(c, cloth, step(size.y * (1 - cover), i.local.y));
                }

                c *= _StationEmission;
                float nv = saturate(dot(N, -V));
                float fres = 0.04 + 0.96 * pow(1 - nv, 5);
                float3 R = reflect(V, N);
                half3 env = GlossyEnvironmentReflection(R, 0.05, 1) * _Reflection;
                half3 glint = sun.color * sunVis * pow(saturate(dot(R, sun.direction)), 900) * 40;
                half3 col = c * _GlassTint.rgb * (1 - fres) + (env + glint) * max(fres, 0.06);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TileDepthVert
            #pragma fragment TileDepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TileDepthNormalVert
            #pragma fragment TileDepthNormalFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
