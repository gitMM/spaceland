// Painted and bare-metal surfaces of the settlement tiles. Lit through URP (shadows, SSAO, probes). The pattern code
// in uv0.x adds paint detail that geometry doesn't carry: staggered shell panels with seams and slightly different
// greys, hazard stripes, deck plating, grille slots and stencilled markings. Vertex alpha adds grime near the base,
// streaks and blotches. All patterns are box-filtered so they average out instead of shimmering at a distance.
Shader "DeathStar/Tile Surface"
{
    Properties
    {
        _BaseColor("Base colour", Color) = (0.6, 0.6, 0.6, 1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        [HDR] _Emission("Emission", Color) = (0, 0, 0, 0)
        _PatternStrength("Pattern strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "TileCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Metallic;
            half _Smoothness;
            half4 _Emission;
            half _PatternStrength;
        CBUFFER_END
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS   : TEXCOORD3;
                half4 color       : TEXCOORD4;
                float2 code       : TEXCOORD5;
                half4 overlay     : TEXCOORD6;
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
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.color = v.color;
                o.code = v.code;
                o.overlay = v.overlay;
                return o;
            }

            // Metres across the face, on the object plane the surface faces most.
            float2 FacePlane(float3 p, float3 n)
            {
                float3 a = abs(n);
                if (a.y >= a.x && a.y >= a.z) return p.xz;
                return a.x >= a.z ? p.zy : p.xy;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 q = FacePlane(i.positionOS, normalize(i.normalOS));
                int code = (int)round(i.code.x);
                float seed = i.code.y;
                half3 albedo = _BaseColor.rgb * i.color.rgb;
                half smooth = _Smoothness;
                half k = _PatternStrength;
                float3 nWS = normalize(i.normalWS);

                if (code == 1)          // shell panels, staggered row by row
                {
                    float2 size = float2(1.23, 1.23);
                    float2 c = q / size;
                    c.x += frac(floor(c.y) * 0.5);
                    float2 id = floor(c);
                    float seam = max(GridLine(c.x, 0.012 / size.x), GridLine(c.y, 0.012 / size.y));
                    float h = TileHash(id + seed * 31.7);
                    half tone = 1 + (h - 0.5) * 0.08;
                    tone *= h > 0.9 ? 0.9 : (h < 0.06 ? 1.05 : 1);
                    albedo *= lerp(1, tone, k) * (1 - seam * 0.55 * k);
                    smooth *= 1 - (h - 0.5) * 0.25;
                }
                else if (code == 2)     // hazard stripes
                {
                    float stripe = Stripe((q.x + q.y) / 0.3, 0.5);
                    albedo = lerp(albedo, half3(0.03, 0.03, 0.035), stripe);
                }
                else if (code == 3)     // deck plating
                {
                    float2 c = q / 2.5;
                    float h = TileHash(floor(c) + 3.1);
                    float seam = max(GridLine(c.x, 0.02 / 2.5), GridLine(c.y, 0.02 / 2.5));
                    float2 c2 = q / 1.25;
                    float seam2 = (h > 0.5 ? GridLine(c2.x, 0.008 / 1.25) : GridLine(c2.y, 0.008 / 1.25)) * 0.6;
                    half tone = 1 + (h - 0.5) * 0.16;
                    tone *= h > 0.88 ? 0.82 : 1;
                    float scuff = TileNoise(q * 0.21 + 3) * TileNoise(q * 1.7);
                    albedo *= tone * (1 - max(seam, seam2) * 0.6) * (1 - scuff * 0.35);
                }
                else if (code == 4)     // grille slots
                {
                    albedo *= 1 - Stripe(q.y / 0.06, 0.45) * 0.8;
                }
                else if (code == 5)     // stencilled markings: rows of blocky glyphs
                {
                    float2 g = q / float2(0.13, 0.19);
                    float2 cell = floor(g), f = frac(g);
                    float2 sub = floor(f * float2(3, 4));
                    float ink = step(0.5, TileHash(cell * 7.13 + sub * 1.7 + seed * 11));
                    ink *= step(0.14, f.x) * step(f.x, 0.86) * step(0.12, f.y) * step(f.y, 0.88);
                    ink *= step(0.22, TileHash(float2(cell.x, cell.y * 3.1 + 7)));
                    float far = saturate(max(fwidth(g.x), fwidth(g.y)) * 3 - 0.5);
                    albedo = lerp(albedo, half3(0.06, 0.06, 0.065), lerp(ink, 0.3, far) * 0.9);
                }

                // Grime: broad blotches, the same on every face (there is no down in space).
                half wear = i.color.a;
                float blotch = TileNoise(q * 0.8 + seed * 9) * TileNoise(q * 3.7 + 1.3);
                albedo *= 1 - wear * saturate(blotch * 0.16);

                half metallic = _Metallic;
                half3 emission = _Emission.rgb * i.color.rgb;
                if (_TileClay > 0.5) { albedo = 0.36; metallic = 0; smooth = 0.25; emission = 0; }
                // Planning overlay: family colour over the relief's own light and shade; untyped hull dims a little.
                if (_TileOverlay > 0.5) albedo = i.overlay.a > 0.5 ? albedo * i.overlay.rgb * 1.7 : albedo * 0.75;

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = NormalizeNormalPerPixel(nWS);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metallic;
                s.smoothness = smooth;
                s.occlusion = 1;
                s.emission = emission;
                s.normalTS = half3(0, 0, 1);
                s.alpha = 1;
                return half4(UniversalFragmentPBR(input, s).rgb, 1);
            }
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
