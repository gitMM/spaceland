            #include "../Resources/StationLighting/StationLighting.hlsl"
#include "../Resources/StationLighting/StationScreenGiSource.hlsl"
#include "TileEdges.hlsl"
#include "TileRelief.hlsl"
#include "TileReliefFold.hlsl"
// Global (StationLightingSettings): x 1 traces the armour's relief on plain armour, y and z the distances over which the
// plates sink into the face.
float4 _TileRelief;
float _StationAmbient, _StationEmission;
float4 _StationHabitatFamily, _StationHabitatTint;

            struct Varyings
            {
                // (noperspective centroid: required when the pixel shader writes conservative depth)
                noperspective centroid float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS   : TEXCOORD3;
                half4 color       : TEXCOORD4;
                float4 code       : TEXCOORD5;
                half4 overlay     : TEXCOORD6;
                float4 edge0      : TEXCOORD7;
                float4 edge1      : TEXCOORD8;
                float fade        : TEXCOORD9;
                float4 relief     : TEXCOORD10;
                float2 atlas      : TEXCOORD11;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(TileAttributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                // Match the depth/normal prepass exactly for the source redraw.
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.color = v.color;
                o.code = v.code;
                o.overlay = v.overlay;
                o.edge0 = v.local;
                o.edge1 = v.extent;
                o.fade = TileKitFade();
                o.relief = v.relief;
                o.atlas = v.atlas;
                return o;
            }

            // Metres across the face, on the object plane the surface faces most.
            float2 FacePlane(float3 p, float3 n)
            {
                float3 a = abs(n);
                if (a.y >= a.x && a.y >= a.z) return p.xz;
                return a.x >= a.z ? p.zy : p.xy;
            }
            // The object directions FacePlane's two axes run along.
            void FacePlaneAxes(float3 n, out float3 t, out float3 b)
            {
                float3 a = abs(n);
                if (a.y >= a.x && a.y >= a.z) { t = float3(1, 0, 0); b = float3(0, 0, 1); }
                else if (a.x >= a.z) { t = float3(0, 0, 1); b = float3(0, 1, 0); }
                else { t = float3(1, 0, 0); b = float3(0, 1, 0); }
            }

            #if defined(STATION_GI_SOURCE)
StationGiSourceOutput Frag(Varyings i)
#else
            // The relief trace moves the surface toward the camera, never away (reversed Z: depth only grows), so
            // conservative depth keeps early depth testing.
            #if UNITY_REVERSED_Z
                #define TILE_DEPTH SV_DepthGreaterEqual
            #else
                #define TILE_DEPTH SV_DepthLessEqual
            #endif
half4 Frag(Varyings i, out float depth : TILE_DEPTH) : SV_Target
#endif
            {
                UNITY_SETUP_INSTANCE_ID(i);
                TileCrossFade(i.positionCS);
                #if !defined(STATION_GI_SOURCE)
                // Interpolated depth isn't bit for bit the depth prepass's: nudge it toward the camera so the opaque
                // pass's depth test against the prepass never drops a pixel.
                #if UNITY_REVERSED_Z
                    #define TILE_NEARER(z) ((z) * (1 + 4e-6))
                #else
                    #define TILE_NEARER(z) ((z) * (1 - 4e-6))
                #endif
                depth = TILE_NEARER(i.positionCS.z);
                #endif
                TileKitClip(i.fade, i.positionCS);
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
                // Close-up surface: the painted metal base, then bevels, highlights, chips, creases and fine detail
                // from the face's own edges.
                TileEdgeSurface edged;
                edged.normalWS = nWS; edged.albedo = albedo; edged.metallic = metallic; edged.smoothness = smooth; edged.emission = 0;
                float3 axisT, axisB; FacePlaneAxes(normalize(i.normalOS), axisT, axisB);
                TilePaintedMetal(q, TransformObjectToWorldDir(axisT), TransformObjectToWorldDir(axisB), TileFootprint(i.positionWS), edged);
                TileEdges(i.edge0, i.edge1, i.code.z, i.code.w, i.positionWS, nWS, q, edged);
                albedo = edged.albedo; metallic = edged.metallic; smooth = edged.smoothness;
                float3 shadingN = edged.normalWS;
                // Screen derivatives for the relief trace, taken here in uniform control flow: inside its branch a 2 x 2
                // quad can straddle armour and other faces, and the derivatives there are undefined (NaN on bent tiles).
                float3 wx = ddx(i.positionWS), wy = ddy(i.positionWS), sx = ddx(i.relief.xyz), sy = ddy(i.relief.xyz);
                float reliefFootprint = TileFootprint(i.positionWS);
                float3 surfaceWS = i.positionWS;
                half occlusion = 1;
                StationSurfaceShadow = 1;
                // The armour's plates, traced where the mesh left the armour plain (mid and far levels).
                if (_TileRelief.x > .5 && i.relief.w != 0)
                {
                    int c = (int)round(i.relief.w), m = c >> 1, layout = ((m % 9) + 9) % 9;
                    ReliefFace face; face.ua = layout / 3; face.va = layout % 3; face.ax = 3 - face.ua - face.va;
                    face.key = (m - layout) / 9; face.allCalm = (c & 1) != 0;
                    float3 s0 = i.relief.xyz;
                    // World and station frames from screen derivatives (the Foundry bends one into the other).
                    float3 crossS = cross(sx, sy);
                    float3 ns = crossS * rsqrt(max(dot(crossS, crossS), 1e-30)); if (dot(cross(wx, wy), nWS) < 0) ns = -ns;
                    float3x3 W = transpose(float3x3(wx, wy, nWS)), S = transpose(float3x3(sx, sy, ns));
                    // A frame squashed flat (a face seen edge-on, or no area on screen) has no inverse: leave it plain.
                    float detW = abs(dot(wx, cross(wy, nWS))), detS = abs(dot(sx, cross(sy, ns)));
                    bool frame = detW > 1e-6 * dot(wx, wx) * length(wy) && detS > 1e-6 * dot(sx, sx) * length(sy) && dot(crossS, crossS) > 1e-24;
                    float3x3 toStation = mul(S, ReliefInverse(W)), toWorld = mul(W, ReliefInverse(S));
                    float distanceToCamera = distance(i.positionWS, _WorldSpaceCameraPos);
                    float lower = saturate((_TileRelief.z - distanceToCamera) / max(_TileRelief.z - _TileRelief.y, 1));
                    // the plain band along the face's edges (only the mid level carries edges)
                    float trim = 1e3;
                    [unroll] for (int e = 0; e < 4; e++) { trim = min(trim, i.edge0[e]); trim = min(trim, i.edge1[e]); }
                    if (trim > RELIEF_TRIM && frame)
                    {
                        float footprint = reliefFootprint;
                        float3 dirS = normalize(mul(toStation, i.positionWS - _WorldSpaceCameraPos));
                        float3 sunS = normalize(mul(toStation, _MainLightPosition.xyz));
                        ReliefHit hit = ReliefTrace(face, s0, ns, dirS, sunS, lower, footprint * 1.5);
                        surfaceWS = i.positionWS + mul(toWorld, hit.station - s0);
                        if (dot(hit.wall, hit.wall) > 0) shadingN = normalize(mul(toWorld, hit.wall));
                        // plate tones and the dark face showing in gaps fade as the plates sink...
                        half tone = hit.plate > 0 ? .95 + hit.plate * .1 : (hit.height <= 0 ? .78 : 1);
                        albedo *= lerp(1, tone, lower);
                        // ...gaps filled in under a pixel and a half keep their average darkness...
                        albedo *= 1 - .2 * saturate(footprint * 1.5 / .028 - 1) * lower;
                        // ...and once sunk, the plates' measured average stays in the face (TileReliefFold.hlsl): their
                        // shadows on each other by the sun's height, the sky they hide, the seams' share of the colour
                        // and their side normals' spread as roughness.
                        float fold = 1 - lower;
                        float sunHeight = degrees(asin(saturate(dot(sunS, ns))));
                        StationSurfaceShadow = hit.shadow * lerp(1, ReliefFoldSunAt(sunHeight), fold);
                        albedo *= lerp(1, ReliefFoldAlbedo, fold);
                        occlusion = lerp(1, ReliefFoldCavity, fold);
                        smooth = max(0, smooth - ReliefFoldSpread * .3 * fold);
                        // never let a bad trace through to lighting or depth
                        if (any(!isfinite(surfaceWS)) || any(!isfinite(shadingN)) || !isfinite(StationSurfaceShadow))
                        { surfaceWS = i.positionWS; shadingN = edged.normalWS; StationSurfaceShadow = 1; }
                        #if !defined(STATION_GI_SOURCE)
                        float4 clip = TransformWorldToHClip(surfaceWS);
                        depth = max(TILE_NEARER(clip.z / clip.w), depth);   // (conservative: never farther than the face)
                        #endif
                    }
                }
                // Physical paint is shared by visible shading and the SSILVB
                // diffuse-source pass; display overlays remain separate.
                if(_StationHabitatTint.a>.5 && distance(i.overlay.rgb,_StationHabitatFamily.rgb)<.01)
                    albedo *= _StationHabitatTint.rgb;
                half3 emission = _Emission.rgb * i.color.rgb + edged.emission;
                // A chunk's massing (StationChunks): the tiles as baked, colour, glow, normal and occlusion.
                if (_TileChunk > .5)
                {
                    half4 colour = SAMPLE_TEXTURE2D(_TileChunkColour, sampler_TileChunkColour, i.atlas);
                    half4 baked = SAMPLE_TEXTURE2D(_TileChunkNormal, sampler_TileChunkNormal, i.atlas);
                    albedo = colour.rgb; metallic = 0; smooth = .3;
                    emission = half3(1, .76, .52) * colour.a * 3.4;
                    shadingN = normalize(baked.rgb * 2 - 1); occlusion = baked.a;
                }
                if (_TileClay > 0.5) { albedo = 0.36; metallic = 0; smooth = 0.25; emission = 0; }
                // Planning overlay: family colour over the relief's own light and shade; untyped hull dims a little.
                half4 overlay = _TileOverlayTint.a > 0.5 ? (half4)_TileOverlayTint : i.overlay;
#if !defined(STATION_GI_SOURCE)
                if (_TileOverlay > 0.5) albedo = overlay.a > 0.5 ? albedo * overlay.rgb * 1.7 : albedo * 0.75;

#endif
                #if !defined(STATION_GI_SOURCE)
                if (_TileBake > .5)
                    return _TileBake < 1.5 ? half4(normalize(shadingN) * .5 + .5, dot(_TileBakePlane.xyz, surfaceWS) + _TileBakePlane.w)
                        : half4(albedo, Luminance(emission));
                #endif
                InputData input = (InputData)0;
                input.positionWS = surfaceWS;
                input.positionCS = i.positionCS;
                input.normalWS = NormalizeNormalPerPixel(shadingN);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(surfaceWS);
                input.shadowCoord = TransformWorldToShadowCoord(surfaceWS);
                input.bakedGI = SampleSH(input.normalWS) * _StationAmbient;
                input.tangentToWorld = float3x3(1,0,0,0,1,0,nWS.x,nWS.y,nWS.z);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metallic;
                s.smoothness = smooth;
                s.occlusion = occlusion;
                s.emission = emission * _StationEmission;
                s.normalTS = half3(0, 0, 1);
                s.alpha = 1;
#if defined(STATION_GI_SOURCE)
                return StationGiSource(input,s,1,1);
#else
                return StationFragmentPBR(input,s);
#endif
            }
