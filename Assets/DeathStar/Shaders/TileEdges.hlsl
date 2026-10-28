#ifndef DEATHSTAR_TILE_EDGES_INCLUDED
#define DEATHSTAR_TILE_EDGES_INCLUDED

// The close-up surface of doc/close-up-detail-plan.md, all shading:
//  - layer 1, a painted metal base (TileLook.MetalTexture) projected on the face plane with hashed tiling;
//  - layer 2, shading from each face's own edges, which TileMesh.Edges.cs hands every vertex as distances to up to
//    eight convex, soft or concave edges (tile seams and flat joins carry none). Convex edges get a rounded bevel normal
//    sized to the part, a warm highlight where that bevel turns to the light, and flat wear chips of light bare metal;
//    concave edges a dark crease;
//  - fine detail laid out in the face's own rectangle (two square convex edges and their opposites): screws at the
//    corners and along the edges, an inset carved groove, dot-grid grilles and small stencils and indicator lights.
// Everything is antialiased and fades to its average once it is under a couple of pixels.

// Globals (StationLightingSettings): _TileStyle x 1 turns edges on, y wear, z edge highlight, w crease darkness;
// _TileDetail x 1 turns fine detail on, y density, z painted metal strength, w the base texture's size in metres.
float4 _TileStyle;
// x: how far wear chips reach, in bevels (0 unset: 1).
float4 _TileStyle2;
float4 _TileDetail;
// Global: how many times supersampled the picture is (the lab), so fades follow the final picture's pixels. 0 = 1.
float _TileFootprintScale;
// Metres a pixel of the final picture spans here.
float TileFootprint(float3 positionWS)
{
    return max(max(length(ddx(positionWS)), length(ddy(positionWS))), 1e-5) * max(_TileFootprintScale, 1);
}
TEXTURE2D(_TileMetalTex); SAMPLER(sampler_TileMetalTex);
// StationKit: _TileKitCentre xyz the ring's centre, w 1 while props are on; _TileKit x inner radius, y outer, z density.
float4 _TileKitCentre;
float4 _TileKit;

#define TILE_EDGE_CONVEX 1
#define TILE_EDGE_SOFT 2
#define TILE_EDGE_CONCAVE 3

struct TileEdgeSurface
{
    float3 normalWS;
    half3 albedo;
    half metallic;
    half smoothness;
    half3 emission;
};

// Hashed tiling (Heitz and Neyret 2018, by-example noise): three offset samples blended on a triangle grid, with the
// blend's variance restored, so a low-contrast texture never shows its repeat.
float2 TileHash2(float2 p) { return frac(sin(float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)))) * 43758.5453); }
half4 TileHashedSample(float2 uv)
{
    // One mip blurrier than the pixel asks for: a minified texture otherwise crawls as the camera moves. Close up
    // a texel spans many pixels, so nothing is lost there.
    float2 dx = ddx(uv) * 2, dy = ddy(uv) * 2;
    float2 skewed = mul(float2x2(1, 0, -0.57735027, 1.15470054), uv * 3.4641);
    float2 base = floor(skewed);
    float3 t = float3(frac(skewed), 0);
    t.z = 1 - t.x - t.y;
    float3 w; float2 v1, v2, v3;
    if (t.z > 0) { w = float3(t.z, t.y, t.x); v1 = base; v2 = base + float2(0, 1); v3 = base + float2(1, 0); }
    else { w = float3(-t.z, 1 - t.y, 1 - t.x); v1 = base + 1; v2 = base + float2(1, 0); v3 = base + float2(0, 1); }
    half4 a = SAMPLE_TEXTURE2D_GRAD(_TileMetalTex, sampler_TileMetalTex, uv + TileHash2(v1), dx, dy);
    half4 b = SAMPLE_TEXTURE2D_GRAD(_TileMetalTex, sampler_TileMetalTex, uv + TileHash2(v2), dx, dy);
    half4 c = SAMPLE_TEXTURE2D_GRAD(_TileMetalTex, sampler_TileMetalTex, uv + TileHash2(v3), dx, dy);
    w = w * w * w; w /= w.x + w.y + w.z;
    half4 mean = .5;
    return mean + ((a - mean) * w.x + (b - mean) * w.y + (c - mean) * w.z) * rsqrt(dot(w, w));
}

// Layer 1. q: metres across the face; tangent, bitangent: the world directions of q's axes; footprint: metres per
// pixel. Once a texel is under about half a pixel the bumps stop turning the normal (they would shimmer) and their
// spread goes into roughness instead.
void TilePaintedMetal(float2 q, float3 tangent, float3 bitangent, float footprint, inout TileEdgeSurface s)
{
    float k = _TileDetail.z;
    if (k <= 0) return;
    half4 m = TileHashedSample(q / _TileDetail.w);
    s.albedo *= 1 + (m.r - .5) * 1.2 * k;
    float texel = _TileDetail.w / 256;
    float bump = saturate(texel * 2 / footprint - .5);
    s.smoothness = saturate(s.smoothness - (m.g - .5) * .5 * k - .12 * k * (1 - bump));
    float2 n = (m.ba - .5) * .8 * k * bump;
    s.normalWS = normalize(s.normalWS + tangent * n.x + bitangent * n.y);
}

// A square ramp 1 inside distance field f (negative inside), antialiased over a pixel.
float TileInside(float f, float footprint) { return saturate(.5 - f / footprint); }

// Layer 2 and fine detail. d0, d1: distances to edges 0-7 (metres before the Foundry bends the tile); kinds: two bits
// per edge; packed: the part's thickness in millimetres plus the face's random number; q: metres across the face.
void TileEdges(float4 d0, float4 d1, float kinds, float packed, float3 positionWS, float3 nWS, float2 q, inout TileEdgeSurface s)
{
    // Derivatives first: every lane of a 2x2 quad shares the triangle, but keep them out of branches anyway.
    float3 dpdx = ddx(positionWS), dpdy = ddy(positionWS);
    float4 dx0 = ddx(d0), dy0 = ddy(d0), dx1 = ddx(d1), dy1 = ddy(d1);
    uint flags = (uint)round(kinds);
    if (_TileStyle.x < .5 || flags == 0) return;
    float part = floor(packed) * .001, random = frac(packed);

    // Surface gradient (Mikkelsen): each distance is affine on the face, so its gradient is exact per triangle. Its
    // length undoes the Foundry's stretch, turning distances back into true metres.
    float3 r1 = cross(dpdy, nWS), r2 = cross(nWS, dpdx);
    float det = dot(dpdx, r1);
    float inv = abs(det) > 1e-20 ? rcp(det) : 0;
    float footprint = max(max(length(dpdx), length(dpdy)), 1e-5) * max(_TileFootprintScale, 1);   // metres per pixel

    float em[8]; float3 eg[8]; uint ek[8];
    float r = clamp(part * .12, .006, .04);                           // bevel radius, chunky for the part's size
    float c1 = 1e3, c2 = 1e3; float3 g1 = 0, g2 = 0;                  // the two nearest convex edges
    float soft = 1e3; float3 gs = 0;
    float crease = 1e3;
    // (HLSL evaluates both sides of ?:, so unpack instead of indexing d0 or d1 by k)
    float dk[8] = { d0.x, d0.y, d0.z, d0.w, d1.x, d1.y, d1.z, d1.w };
    float xk[8] = { dx0.x, dx0.y, dx0.z, dx0.w, dx1.x, dx1.y, dx1.z, dx1.w };
    float yk[8] = { dy0.x, dy0.y, dy0.z, dy0.w, dy1.x, dy1.y, dy1.z, dy1.w };
    [unroll] for (int k = 0; k < 8; k++)
    {
        uint kind = (flags >> (2 * k)) & 3;
        float d = dk[k], gx = xk[k], gy = yk[k];
        float3 g = (gx * r1 + gy * r2) * inv;
        float len = length(g);
        if (len < 1e-4) kind = 0;
        float m = max(d, 0) / max(len, 1e-4); g /= max(len, 1e-4);   // true metres; g points into the face
        em[k] = m; eg[k] = g; ek[k] = kind;
        if (kind == TILE_EDGE_CONCAVE) crease = min(crease, m);
        else if (kind == TILE_EDGE_SOFT) { if (m < soft) { soft = m; gs = g; } }
        else if (kind == TILE_EDGE_CONVEX)
        {
            if (m < c1) { c2 = c1; g2 = g1; c1 = m; g1 = g; }
            else if (m < c2) { c2 = m; g2 = g; }
        }
    }

    // Rounded corners where two convex edges meet square on: distance to a quarter circle, and its gradient.
    float dist = c1; float3 inward = g1;
    if (c2 < r && abs(dot(g1, g2)) < .5)
    {
        float2 e = r - float2(c1, c2);
        dist = r - length(e);
        inward = normalize(g1 * e.x + g2 * e.y);
    }
    float bevelVis = saturate(r / footprint * .5 - .75);    // gone once the bevel is a pixel and a half, whole at 3.5
    float u = saturate(1 - dist / r);
    // Quarter-round profile: the normal turns toward the edge, half of a square edge's turn at the edge itself, so
    // both faces meet in the same direction.
    float tilt = asin(u) * .5 * bevelVis;
    float3 n = s.normalWS * cos(tilt) - inward * sin(tilt);
    // Soft edges (onto a chamfer) turn less.
    float us = saturate(1 - soft / (r * .6));
    float tiltSoft = asin(us) * .22 * saturate(r * .6 / footprint * .5 - .75);
    n = n * cos(tiltSoft) - gs * sin(tiltSoft);

    // Warm highlight on the bevel: brighter, warmer and glossier paint, which the light then picks out where the
    // bevel turns to it.
    float band = smoothstep(0, 1, u) * bevelVis;
    s.albedo = lerp(s.albedo, s.albedo * half3(1.45, 1.25, .95), band * _TileStyle.z);
    s.smoothness = lerp(s.smoothness, max(s.smoothness, .55), band * _TileStyle.z);

    // ---- fine detail, in the face's own rectangle: a (first convex edge), b (a convex edge square to it) and their
    // opposites give u, v across the face and its size U, V (1e3 where an opposite is missing).
    int a = -1, b = -1, a2 = -1, b2 = -1;
    [unroll] for (int i = 0; i < 8; i++) if (a < 0 && ek[i] == TILE_EDGE_CONVEX) a = i;
    [unroll] for (int j = 0; j < 8; j++)
    {
        if (a < 0 || ek[j] != TILE_EDGE_CONVEX || j == a) continue;
        float c = dot(eg[j], eg[max(a, 0)]);
        if (b < 0 && abs(c) < .3) b = j;
        if (a2 < 0 && c < -.9) a2 = j;
    }
    [unroll] for (int l = 0; l < 8; l++) if (b >= 0 && b2 < 0 && ek[l] == TILE_EDGE_CONVEX && dot(eg[l], eg[max(b, 0)]) < -.9) b2 = l;
    // Beyond the kit ring a plate that would hold a prop takes the prop's average darkening instead (StationKit
    // decides the same way: four sharp edges, 0.35-3 m a side, random under the density).
    uint flagged = 0, sharp = 0;
    [unroll] for (int fi = 0; fi < 8; fi++) { flagged += ek[fi] != 0 ? 1 : 0; sharp += ek[fi] == TILE_EDGE_CONVEX ? 1 : 0; }
    if (_TileKitCentre.w > .5 && TILE_KIT_MODE < .5 && flagged == 4 && sharp == 4 && a >= 0 && b >= 0 && a2 >= 0 && b2 >= 0)
    {
        float fu = em[max(a, 0)], fv = em[max(b, 0)];
        float U = fu + em[max(a2, 0)], V = fv + em[max(b2, 0)];
        if (min(U, V) >= .35 && max(U, V) <= 3 && random < _TileKit.z)
        {
            float3 centre = positionWS + eg[max(a, 0)] * (U * .5 - fu) + eg[max(b, 0)] * (V * .5 - fv);
            float grow = saturate((_TileKit.y - distance(centre, _TileKitCentre.xyz)) / max(_TileKit.y - _TileKit.x, 1e-3));
            float cover = saturate(.09 / (U * V));   // a prop covers about 0.09 m2 of its plate
            s.albedo *= 1 - .45 * cover * (1 - grow);
        }
    }
    if (_TileDetail.x > .5 && a >= 0 && b >= 0)
    {
        // (max: the compiler cannot see that a and b are set here)
        float fu = em[max(a, 0)], fv = em[max(b, 0)];
        float U = a2 >= 0 ? fu + em[max(a2, 0)] : 1e3, V = b2 >= 0 ? fv + em[max(b2, 0)] : 1e3;
        float3 gu = eg[max(a, 0)], gv = eg[max(b, 0)];
        float small = min(U, V);
        float density = _TileDetail.y;
        float h1 = frac(random * 7.31), h2 = frac(random * 13.7), h3 = frac(random * 29.3), h4 = frac(random * 53.9), h5 = frac(random * 97.1);

        // A carved groove inset from the face's edges: dark in the bottom, its two walls turned toward each other
        // so the light picks one out.
        float gi = clamp(small * .12, .05, .14);
        bool groove = small > .6 && small < 900 && h2 < density * .7;
        if (groove)
        {
            float f = min(min(fu, U - fu), min(fv, V - fv));
            float3 gf = f == fu ? gu : f == U - fu ? -gu : f == fv ? gv : -gv;
            float w = .006, t = (f - gi) / w;
            float vis = saturate(w * 2 / footprint - .5);
            float wall = saturate(1 - abs(t)) * vis;
            n += gf * (t < 0 ? 1 : -1) * wall * .9;
            s.albedo *= 1 - .6 * saturate(1 - abs(t) * 2.5) * vis;
            s.albedo *= 1 - .25 * saturate(w * 2 / footprint) * (1 - vis) * saturate(1 - abs(t));   // its average far off
        }

        // Screws at the corners, and along edges long enough for more, evenly spaced. Domed heads with a dark rim
        // and a slot.
        if (h1 < density * .9 && small > .14)
        {
            float rs = clamp(part * .035, .006, .012), inset = max(r * 1.4, rs * 2.2);
            float spacing = clamp(small * .4, .14, .4);
            float best = 1e3; float2 off = 0;
            [unroll] for (int side = 0; side < 4; side++)
            {
                // a row along each side: across = distance from that side, along = position along it
                float across = side == 0 ? fu : side == 1 ? U - fu : side == 2 ? fv : V - fv;
                float along = side < 2 ? fv : fu, L = side < 2 ? V : U;
                if (across > 900 || along > 900) continue;
                float run = L < 900 ? L - 2 * inset : 0;
                float count = max(1, round(run / spacing));
                float stride = run / count;
                float at = L < 900 ? inset + clamp(round((along - inset) / max(stride, 1e-3)), 0, count) * stride : inset;
                float2 o = side < 2 ? float2(across - inset, along - at) : float2(along - at, across - inset);
                float dd = length(o);
                if (dd < best) { best = dd; off = side == 1 ? float2(-o.x, o.y) : side == 3 ? float2(o.x, -o.y) : o; }
            }
            float vis = saturate(rs / footprint * .7 - .5);
            float head = TileInside(best - rs, footprint) * vis;
            float rim = saturate(1 - abs(best - rs * .85) / max(rs * .22, footprint)) * vis;
            float slot = TileInside(abs(off.x * .7071 + off.y * .7071) - rs * .14, footprint) * TileInside(best - rs * .75, footprint) * vis;
            float2 dome = off / rs;
            n -= (gu * dome.x + gv * dome.y) * .55 * head;
            s.albedo = lerp(s.albedo, s.albedo * .9 + .04, head);
            s.smoothness = lerp(s.smoothness, .6, head);
            s.albedo *= 1 - .7 * max(rim, slot);
        }

        // A dot-grid grille in the panel inside the groove (or the edges): whole holes only.
        if (h3 < density * .35 && small > .22 && small < 2.5)
        {
            float pitch = .032, hole = .0085, margin = groove ? gi + .03 : max(r * 2, .04);
            float2 lo = margin, hi = float2(U, V) - margin;
            float2 cells = floor((hi - lo) / pitch);
            float2 start = lo + ((hi - lo) - cells * pitch) * .5;
            float2 c = (float2(fu, fv) - start) / pitch;
            float inside = all(c >= 0) && all(c < cells) ? 1 : 0;
            float vis = saturate(pitch / footprint * .35 - .5);
            float holes = TileInside(length((frac(c) - .5) * pitch) - hole, footprint) * vis;
            float average = 3.14159 * hole * hole / (pitch * pitch) * (1 - vis);
            s.albedo *= 1 - .88 * max(holes, average) * inside;
            s.smoothness = lerp(s.smoothness, .2, holes * inside);
        }

        // A small stencil near the first corner, inside the groove: a warning triangle, a cross mark, a label of
        // blocky glyphs or an indicator light.
        if (h4 < density * .3 && small > .3 && small < 900)
        {
            float size = clamp(small * .12, .05, .1);
            float2 o = groove ? gi + .03 : max(r * 2.5, .05);
            float2 st = (float2(fu, fv) - o) / size;   // 0-1 across the stencil
            float vis = saturate(size / footprint * .1 - .5);
            if (h5 < .3)
            {
                // a warning triangle with an exclamation mark
                float2 c = st - float2(.5, .38);
                float tri = max(abs(c.x) * .866 + c.y * .5, -c.y) - .3;
                float bar = max(abs(c.x) - .045, abs(c.y - .05) - .12);
                float pip = length(c - float2(0, -.15)) - .05;
                float fill = TileInside(tri * size, footprint) * vis, mark = TileInside(min(bar, pip) * size, footprint) * vis;
                s.albedo = lerp(s.albedo, half3(.7, .5, .08), fill);
                s.albedo = lerp(s.albedo, half3(.03, .03, .03), mark * fill);
            }
            else if (h5 < .55)
            {
                float2 c = abs(st - .5);
                float mark = min(max(c.x - .06, c.y - .35), max(c.y - .06, c.x - .35));
                s.albedo = lerp(s.albedo, s.albedo * .25, TileInside(mark * size, footprint) * vis);
            }
            else if (h5 < .8)
            {
                // a label plate: pale, with two rows of dark blocky glyphs
                float2 c = st * float2(1, 2.5);
                float plate = max(abs(c.x - 1) - 1, abs(c.y - .5) - .5);
                float2 g = floor(c * float2(10, 4)), fg = frac(c * float2(10, 4));
                float ink = step(.45, TileHash(g + random * 41)) * step(.2, fg.x) * step(fg.x, .8) * step(.25, fg.y) * step(fg.y, .75);
                ink *= step(.5, fmod(g.y, 2)) * step(1, g.x) * step(g.x, 18);
                float pv = TileInside(plate * size * .4, footprint) * vis;
                s.albedo = lerp(s.albedo, half3(.55, .55, .52), pv);
                s.albedo = lerp(s.albedo, half3(.04, .04, .045), ink * pv * saturate(size / footprint * .05 - .5));
            }
            else
            {
                // an indicator light in a dark bezel
                float lamp = length(st - .25) - .1, bezel = length(st - .25) - .17;
                half3 colour = h1 < .4 ? half3(1, .12, .05) : h1 < .75 ? half3(1, .55, .08) : half3(.15, 1, .25);
                s.albedo = lerp(s.albedo, half3(.02, .02, .02), TileInside(bezel * size, footprint) * vis);
                s.emission += colour * 4 * TileInside(lamp * size, footprint);
            }
        }
    }
    s.normalWS = normalize(n);

    // Wear chips: flat blocks of light, glossy bare metal along sharp convex edges (pixel-art wear: lighter than
    // the paint, never darker), from stepped noise in square blocks about a part's hundredth across. Under about
    // three pixels a block they turn to their average coverage instead of shimmering.
    float wearBand = r * 2.5 * (_TileStyle2.x > 0 ? _TileStyle2.x : 1);
    float reach = saturate(1 - dist / wearBand);
    float cell = clamp(part * .03, .01, .02);
    float2 block = floor(q / cell);
    float noise = TileNoise(block * cell * 9) * .6 + TileHash(block + 17.3) * .4;
    float want = _TileStyle.y * reach * reach * 1.15;
    float chip = step(1 - want, noise);
    float chipVis = saturate(cell / footprint * .5 - 1);
    chip = lerp(saturate(want * .4), chip, chipVis);
    half3 bare = max(s.albedo * 1.7, half3(.62, .58, .5));
    s.albedo = lerp(s.albedo, bare, chip);
    s.metallic = lerp(s.metallic, .2, chip);
    s.smoothness = lerp(s.smoothness, .72, chip);

    // Creases: dirt darkening out from concave edges, and a thin dark line in the seam itself.
    float creaseBand = r * 2.5;
    float dirt = 1 - saturate(crease / creaseBand);
    dirt *= dirt;
    float lineWidth = .003;
    float seam = (1 - smoothstep(lineWidth, lineWidth + footprint, crease)) * saturate(lineWidth * 2 / footprint);
    float creaseVis = saturate(creaseBand / footprint * .5);
    s.albedo *= 1 - _TileStyle.w * max(dirt * .5 * creaseVis, seam * .75);
}

#endif
