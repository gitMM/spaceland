// Painted and bare-metal surfaces of the settlement tiles. Lit through URP with station shadows, SSILVB AO and probes. The pattern code
// in uv0.x adds paint detail that geometry doesn't carry: staggered shell panels with seams and slightly different
// greys, hazard stripes, deck plating, grille slots and stencilled markings. Vertex alpha adds grime near the base,
// streaks and blotches. All patterns are box-filtered so they average out instead of shimmering at a distance.
Shader "DeathStar/Tile Surface"
{
    Properties
    {
        // TileLook authors linear reflectance. A Color property applies an
        // extra sRGB decode (.36 becomes .106), suppressing both bounce ends.
        _BaseColor("Base reflectance (linear)", Vector) = (0.6, 0.6, 0.6, 1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        [HDR] _Emission("Emission", Color) = (0, 0, 0, 0)
        _PatternStrength("Pattern strength", Range(0, 2)) = 1
        // StationKit's own copies: 1 instanced props, 2 pipes and cables (TileKitFade).
        [HideInInspector] _TileKitMode("Kit mode", Float) = 0
        // StationChunks' copies: 1 draws a chunk's massing from its baked atlas.
        [HideInInspector] _TileChunk("Chunk", Float) = 0
        [HideInInspector] _TileChunkColour("Chunk colour", 2D) = "grey" {}
        [HideInInspector] _TileChunkNormal("Chunk normal", 2D) = "bump" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Metallic;
            half _Smoothness;
            half4 _Emission;
            half _PatternStrength;
            half _TileKitMode;
            half _TileChunk;
        CBUFFER_END
        TEXTURE2D(_TileChunkColour); SAMPLER(sampler_TileChunkColour);
        TEXTURE2D(_TileChunkNormal); SAMPLER(sampler_TileChunkNormal);
        #define TILE_KIT_MODE _TileKitMode
        #include "TileCommon.hlsl"
        ENDHLSL

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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _STATION_SCREEN_OCCLUSION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "TileSurfacePass.hlsl"
            ENDHLSL
        }

        Pass {
            Name "StationGiSource"
            Tags { "LightMode"="StationGiSource" }
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #define STATION_GI_SOURCE 1
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _STATION_SCREEN_OCCLUSION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "TileSurfacePass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TileShadowVert
            #pragma fragment TileShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "TileShadow.hlsl"
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
