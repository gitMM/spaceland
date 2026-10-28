            #include "../Resources/StationLighting/StationLighting.hlsl"
#include "../Resources/StationLighting/StationScreenGiSource.hlsl"
#include "TileEdges.hlsl"
#include "TileRelief.hlsl"
#include "TileReliefFold.hlsl"
// Global (StationLightingSettings): x 1 traces the armour's relief on plain armour, y and z the distances over which the
// plates sink into the face, w 1 shows the relief view (carving orange, traced plates cyan dimming as they sink).
float4 _TileRelief;
float _StationAmbient, _StationEmission;
float4 _StationHabitatFamily, _StationHabitatTint, _StationDefaultTileTint;

            struct Varyings
            {
                // (noperspective centroid: required when the pixel shader writes conservative depth)
                noperspective centroid float4 positionCS : SV_POSITION;
                // The position relative to the camera (the world position is this plus the camera's): its screen
                // derivatives stay exact on a station far from the origin, where a world position's carry the rounding
                // of big numbers (speckled detail up close).
                float3 viewWS     : TEXCOORD0;
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
                o.viewWS = pos.positionWS - _WorldSpaceCameraPos;
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
            // conservative depth keeps early depth testing. A relief shell's trace moves it away, never toward.
            #if defined(_TILE_SHELL)
                #define TILE_DEPTH TILE_SHELL_DEPTH
            #elif UNITY_REVERSED_Z
                #define TILE_DEPTH SV_DepthGreaterEqual
            #else
                #define TILE_DEPTH SV_DepthLessEqual
            #endif
half4 Frag(Varyings i, out float depth : TILE_DEPTH) : SV_Target
#endif
            {
                UNITY_SETUP_INSTANCE_ID(i);
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
                float3 positionWS = i.viewWS + _WorldSpaceCameraPos;
                // Screen derivatives, taken here in uniform control flow: inside a branch a 2 x 2 quad can straddle
                // faces, and the derivatives there are undefined (NaN on bent tiles).
                float3 wx = ddx(i.viewWS), wy = ddy(i.viewWS), sx = ddx(i.relief.xyz), sy = ddy(i.relief.xyz);
                float footprint = TileFootprint(i.viewWS);
                float3 nWS = normalize(i.normalWS);
                #if defined(_TILE_SHELL)
                // The far level's station relief: walk the view ray through it inside the shell (TileGreeble.hlsl);
                // where the ray leaves the shell, whatever is behind shows. The GI sources take the wall under the
                // shell instead: a far relief's bounce isn't worth a third trace.
                GreebleHit shellHit = (GreebleHit)0; float3 shellWS = positionWS; float shellLower = 1;
                #if defined(STATION_GI_SOURCE)
                clip(-1);
                #else
                if (!GreebleShellSurface(i.viewWS, nWS, i.relief, i.edge0, i.edge1, wx, wy, sx, sy, footprint,
                    _MainLightPosition.xyz, shellHit, shellWS, nWS, shellLower)) clip(-1);
                #endif
                #endif
                // (on a shell, the shell's own face plane: its derivatives stay smooth where the trace steps)
                float2 q = FacePlane(i.positionOS, normalize(i.normalOS));
                int code = (int)round(i.code.x);
                float seed = i.code.y;
                half3 albedo = _BaseColor.rgb * i.color.rgb;
                half smooth = _Smoothness;
                half k = _PatternStrength;

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
                TilePaintedMetal(q, TransformObjectToWorldDir(axisT), TransformObjectToWorldDir(axisB), footprint, edged);
                TileEdges(i.edge0, i.edge1, i.code.z, i.code.w, positionWS, wx, wy, footprint, nWS, q, edged);
                albedo = edged.albedo; metallic = edged.metallic; smooth = edged.smoothness;
                float3 shadingN = edged.normalWS;
                float3 surfaceWS = positionWS;
                half occlusion = 1;
                float reliefShown = -1, reliefPlate = 0;   // for the relief view: how high the plates stand, and plate or face
                StationSurfaceShadow = 1;
                #if defined(_TILE_SHELL)
                {
                    surfaceWS = shellWS;
                    albedo *= shellHit.tone;
                    reliefShown = shellLower; reliefPlate = shellHit.face ? 0 : 1;
                    // Once sunk, a rough stand-in for the relief's average darkening (guessed, not measured like the
                    // armour's fold).
                    float fold = 1 - shellLower;
                    StationSurfaceShadow = shellHit.shadow * lerp(1, .82, fold);
                    albedo *= lerp(1, .92, fold); occlusion = lerp(1, .85, fold);
                    #if !defined(STATION_GI_SOURCE)
                    depth = GreebleDepth(shellWS, shellHit.face, i.positionCS.z);
                    #endif
                }
                bool hasRelief = false;
                #else
                // The armour's plates, traced where the mesh left the armour plain (mid and far levels).
                // (a mesh without uv6 reads w as 1; a real layout code is never under 2 either way)
                bool hasRelief = abs(i.relief.w) > 1.5;
                #endif
                if (_TileRelief.x > .5 && hasRelief)
                {
                    int c = (int)round(i.relief.w), m = c >> 1, layout = ((m % 9) + 9) % 9;
                    ReliefFace face; face.ua = layout / 3; face.va = layout % 3; face.ax = 3 - face.ua - face.va;
                    face.key = (m - layout) / 9; face.allCalm = (c & 1) != 0;
                    float3 s0 = i.relief.xyz;
                    // world and station frames (a frame squashed flat has no inverse: leave the armour plain there)
                    float3 ns; float3x3 toStation, toWorld;
                    bool frame = ReliefFrame(wx, wy, nWS, sx, sy, ns, toStation, toWorld);
                    float lower = ReliefLower(_TileRelief, length(i.viewWS));
                    // the plain band along the face's edges (only the mid level carries edges)
                    float trim = 1e3;
                    [unroll] for (int e = 0; e < 4; e++) { trim = min(trim, i.edge0[e]); trim = min(trim, i.edge1[e]); }
                    if (trim > RELIEF_TRIM && frame)
                    {
                        float3 dirS = normalize(mul(toStation, i.viewWS));
                        float3 sunS = normalize(mul(toStation, _MainLightPosition.xyz));
                        ReliefHit hit = ReliefTrace(face, s0, ns, dirS, sunS, lower, footprint * 1.5);
                        reliefShown = lower; reliefPlate = hit.height > 0 ? 1 : 0;
                        surfaceWS = positionWS + mul(toWorld, hit.station - s0);
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
                        { surfaceWS = positionWS; shadingN = edged.normalWS; StationSurfaceShadow = 1; }
                        #if !defined(STATION_GI_SOURCE)
                        // 3 mm behind where it hit: where a real plate of the near level shares a handover band with it,
                        // the plate wins instead of the two fighting.
                        depth = max(TILE_NEARER(ReliefDepthAt(surfaceWS, .003)), depth);   // (conservative: never farther than the face)
                        #endif
                    }
                }
                // Physical paint is shared by visible shading and the SSILVB
                // diffuse-source pass; display overlays remain separate.
                if(_StationHabitatTint.a>.5 && distance(i.overlay.rgb,_StationHabitatFamily.rgb)<.01)
                    albedo *= _StationHabitatTint.rgb;
                else
                    albedo *= _StationDefaultTileTint.rgb;
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
                #if !defined(STATION_GI_SOURCE)
                // Relief view: the near level's carving orange, plates traced by the shader cyan (the face between them
                // blue), dimmer as they sink into the face; everything else grey.
                if (_TileRelief.w > .5)
                {
                    half3 tint = half3(.12, .12, .12);
                    if (code == 7) tint = half3(1, .45, .08);
                    if (reliefShown >= 0) tint = lerp(half3(.05, .12, .3), reliefPlate > 0 ? half3(.1, .95, 1) : half3(.1, .3, .9), .25 + .75 * reliefShown);
                    else if (hasRelief) tint = half3(.12, .2, .45);   // the plain band along a traced face's edges
                    // mostly glowing, so the sun can't wash the colours out; a little lit, to keep the forms
                    albedo = tint * .12; metallic = 0; smooth = .1; emission = tint * .7;
                }
                #endif
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
