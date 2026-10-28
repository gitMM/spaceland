#ifndef DEATHSTAR_TILE_GREEBLE_INCLUDED
#define DEATHSTAR_TILE_GREEBLE_INCLUDED

// The station-wide relief (GreebleField.cs) as a height function in station space, and a trace through it for the far
// level of detail. There every exposed face draws a shell instead of the relief's geometry: over each free rect of the
// face a box from the face up to its relief's top, with sides only where the relief stops (TileMesh.Shells.cs). The
// pixel shader walks the view ray from where it enters the shell, through the relief, to the face; then toward the sun.
// Ported: the districts and their cuts, rims, the recursive plates and every leaf form (boxes, the carved armour's
// corner cuts and splits, drums, domes, rings, ramps, steps, sunk frames, ribs, block grids, stepped pyramids, glyph
// bars), arcs and wells with their ribs, blocks, drums, buttresses and end blocks, and the bed under curves.
// Approximations: the shader doesn't know a face's holes (a leaf beside a family form may take another shape than the
// mesh's, and relief carries on over a neighbour's hole), boxes under a curve are judged by their leaf, the four
// curves nearest the ray count, and a sector's straight sides are drawn round.
// The compiler inlines every call, and fxc gives up on this file when the height function is inlined at many sites:
// GreebleHeight, GreebleUnder and GreebleTrace each have one call site in a pass.

#include "TileRelief.hlsl"   // ReliefAt, ReliefAxis, ReliefMix, ReliefFrame, ReliefLower, ReliefDepthAt

// Global (StationLightingSettings): x 1 traces the relief in the far level's shells, y and z the distances over which
// it sinks into the face.
float4 _TileGreeble;

#define GREEBLE_BIG 32.0
#define GREEBLE_SMALL 12.0
#define GREEBLE_TWO_PI 6.2831853
#define GREEBLE_CURVES 4
#define GREEBLE_CURVE_TONE 0x80000000u   // a tone seed with this bit set is a curve's (GreebleTone)
#define GREEBLE_EMPTY float4(1, 1, -1, -1)

float GreebleLevel(int i) { return i <= 0 ? .1 : i == 1 ? .2 : i == 2 ? .32 : i == 3 ? .45 : i == 4 ? .6 : .8; }

uint GreebleHash(uint a, int b) { return ReliefMix(a * 0x9E3779B1u ^ ReliefMix((uint)b + 0x632BE5ABu)); }
float GreebleR(uint seed, int k) { return (GreebleHash(seed, k) & 0xFFFFFFu) / 16777216.0; }
// C#'s Mathf.Clamp: below lo gives lo even when lo is over hi.
float GreebleClamp(float x, float lo, float hi) { return x < lo ? lo : (x > hi ? hi : x); }
bool GreebleIn(float2 p, float4 r) { return p.x >= r.x && p.x <= r.z && p.y >= r.y && p.y <= r.w; }
bool GreebleOverlaps(float4 a, float4 b) { return a.x < b.z && a.z > b.x && a.y < b.w && a.w > b.y; }
float4 GreebleInset(float4 r, float d) { return r + float4(d, d, -d, -d); }
float GreebleWrap(float a) { return a - GREEBLE_TWO_PI * floor(a / GREEBLE_TWO_PI + .5); }   // to -pi..pi
float2 GreebleDir(float a) { float s, c; sincos(a, s, c); return float2(c, s); }

// A curve: a band round c from radius ri to ro over span radians from a0, or a full ring (a well).
struct GreebleCurve { float2 c; float ri, ro, a0, span, h; bool well, endsInWell; float2 wellAt; uint seed; };

// What a face shares: its plane's hash and tiling offset, and the curves near the ray.
struct GreebleField
{
    uint plane; float2 off;
    GreebleCurve curves[GREEBLE_CURVES]; int count;
};

// The last top-level plate and leaf looked up: a ray stays in one for many steps.
struct GreebleCache
{
    float4 plate; uint plateSeed; float density; int level;
    uint rims;          // bits 0-3: a rim along the bottom, top, left, right; 4-7: rim k's bed test done; 8-11: it's in the bed
    float4 leaf, q;     // the last leaf's rect, and its rect less the gap round it
    uint leafSeed; int leafLevel; float form; bool clear, under;   // form: GreebleR(seed, 4), which shape the leaf takes
};

// ---- districts and curves

// Lattice coordinates of p in the district tiling. Cell (i, j)'s squares hold only points with i in fi-1.33..fi and
// j in fj-0.88..fj+0.46.
float2 GreebleLattice(GreebleField f, float2 p)
{
    const float A = GREEBLE_BIG, B = GREEBLE_SMALL;
    float2 d = p - f.off;
    return float2(A * d.x + B * d.y, A * d.y - B * d.x) / (A * A + B * B);
}
void GreebleCells(float2 lo, float2 hi, out int2 c0, out int2 c1)
{
    c0 = (int2)ceil(lo - float2(1.34, .89)); c1 = (int2)floor(hi + float2(.01, .46));
}
// The district at lattice cell (i, j): its square and the 12 m square beside it, seed, density and level
// (GreebleField.Plates).
void GreebleDistrict(GreebleField f, int i, int j, out float4 big, out float4 small, out uint seed, out float density, out int level)
{
    float2 p = f.off + float2(i * GREEBLE_BIG - j * GREEBLE_SMALL, i * GREEBLE_SMALL + j * GREEBLE_BIG);
    seed = GreebleHash(GreebleHash(f.plane, i + 104729), j);
    density = GreebleR(seed, 1) < .3 ? .32 : lerp(.75, 1.6, GreebleR(seed, 3));
    level = (density < .5 ? 0 : 1) + (int)(GreebleR(seed, 2) * 3);
    big = float4(p, p + GREEBLE_BIG);
    small = float4(p.x + GREEBLE_BIG, p.y, p.x + GREEBLE_BIG + GREEBLE_SMALL, p.y + GREEBLE_SMALL);
}
// One cut of GreebleField.Split at depth 0 or 1: whether r is cut, its halves and their seeds.
bool GreebleSplit(float4 r, uint seed, int depth, out float4 a, out float4 b, out uint sa, out uint sb)
{
    a = r; b = r; sa = GreebleHash(seed, 301); sb = GreebleHash(seed, 302);
    float w = r.z - r.x, h = r.w - r.y;
    if (depth == 1 && (max(w, h) < 26 || GreebleR(seed, 1) < .25)) return false;
    bool alongU = w > h + .5 || (abs(w - h) <= .5 && GreebleR(seed, 2) < .5);
    float len = alongU ? w : h, start = alongU ? r.x : r.y;
    float cut = start + round(len * lerp(.34, .66, GreebleR(seed, 3)) * 2) * .5;
    if (alongU) { a.z = cut; b.x = cut; } else { a.w = cut; b.y = cut; }
    return true;
}

void GreebleAddCurve(inout GreebleField f, GreebleCurve cv, float4 area)
{
    // only those that reach the area (the bed reaches 1.7 m past a well, and a leaf judged by it up to 6.5 m wide)
    float reach = cv.ro + 1.7 + 3.5;
    float2 nearest = clamp(cv.c, area.xy, area.zw);
    if (dot(nearest - cv.c, nearest - cv.c) > reach * reach || f.count >= GREEBLE_CURVES) return;
    if (f.count == 0) f.curves[0] = cv; else if (f.count == 1) f.curves[1] = cv; else if (f.count == 2) f.curves[2] = cv; else f.curves[3] = cv;
    f.count++;
}
// The curves of one top-level plate (GreebleField.Curves).
void GreebleCurvesOf(inout GreebleField f, float4 r, uint seed, float4 area)
{
    float2 centre = (r.xy + r.zw) * .5 + float2((GreebleR(seed, 42) - .5) * 3, (GreebleR(seed, 43) - .5) * 3);
    GreebleCurve well = (GreebleCurve)0;
    well.c = centre; well.ri = 1.75; well.ro = 2.45; well.span = GREEBLE_TWO_PI; well.h = GreebleLevel(4); well.well = true;
    if (GreebleR(seed, 41) < .5)
    {
        float rm = lerp(4, 6.8, GreebleR(seed, 44)), w = lerp(1.3, 2.2, GreebleR(seed, 45));
        GreebleCurve arc = (GreebleCurve)0;
        arc.c = centre; arc.ri = rm - w * .5; arc.ro = rm + w * .5;
        arc.a0 = GreebleR(seed, 46) * GREEBLE_TWO_PI; arc.span = lerp(1.6, 4.4, GreebleR(seed, 47));
        arc.h = GreebleLevel(3 + (int)(GreebleR(seed, 48) * 2)); arc.seed = GreebleHash(seed, 90);
        if (GreebleR(seed, 49) < .5)
        {
            arc.endsInWell = true; arc.wellAt = centre + GreebleDir(arc.a0 + arc.span) * rm;
            well.c = arc.wellAt; well.seed = GreebleHash(seed, 91);
            GreebleAddCurve(f, well, area);
        }
        GreebleAddCurve(f, arc, area);
    }
    else if (GreebleR(seed, 50) < .35)
    {
        well.seed = GreebleHash(seed, 92);
        GreebleAddCurve(f, well, area);
    }
}
// Every curve that can reach area, from the top-level plates within 13 m of it (as a face gathers them round its
// window): each district's plates in turn through one call.
void GreebleGather(inout GreebleField f, float4 area)
{
    float4 wide = area + float4(-13, -13, 13, 13);
    float2 l0 = GreebleLattice(f, wide.xy), l1 = GreebleLattice(f, wide.zy), l2 = GreebleLattice(f, wide.xw), l3 = GreebleLattice(f, wide.zw);
    int2 c0, c1; GreebleCells(min(min(l0, l1), min(l2, l3)), max(max(l0, l1), max(l2, l3)), c0, c1);
    [loop] for (int i = c0.x; i <= c1.x; i++)
    [loop] for (int j = c0.y; j <= c1.y; j++)
    {
        float4 big, small; uint seed; float density; int level;
        GreebleDistrict(f, i, j, big, small, seed, density, level);
        bool bigNear = GreebleOverlaps(big, wide);
        if (!bigNear && !GreebleOverlaps(small, wide)) continue;
        // the district's top-level plates: the 12 m square, then up to four cuts of the big one
        float4 h0, h1, q0, q1, q2, q3; uint s0, s1, t0, t1, t2, t3;
        GreebleSplit(big, seed, 0, h0, h1, s0, s1);
        bool cut0 = GreebleSplit(h0, s0, 1, q0, q1, t0, t1), cut1 = GreebleSplit(h1, s1, 1, q2, q3, t2, t3);
        [loop] for (int k = 0; k < 5; k++)
        {
            float4 r; uint s;
            if (k == 0) { r = small; s = GreebleHash(seed, 303); }
            else if (!bigNear) break;
            else if (k == 1) { r = cut0 ? q0 : h0; s = cut0 ? t0 : s0; }
            else if (k == 2) { if (!cut0) continue; r = q1; s = t1; }
            else if (k == 3) { r = cut1 ? q2 : h1; s = cut1 ? t2 : s1; }
            else { if (!cut1) continue; r = q3; s = t3; }
            if (GreebleOverlaps(r, wide)) GreebleCurvesOf(f, r, s, area);
        }
    }
}

// Whether a rect lies under a curve or its buttresses (GreebleField.UnderCurve): plates there flatten into the bed.
bool GreebleUnder(GreebleField f, float4 r)
{
    [loop] for (int k = 0; k < GREEBLE_CURVES; k++)
    {
        if (k >= f.count) break;
        GreebleCurve cv = f.curves[k];
        float reach = cv.ro + (cv.well ? 1.7 : 1.4), inner = cv.well ? 0 : cv.ri - 1.1;
        float2 nearest = clamp(cv.c, r.xy, r.zw), farthest = max(abs(r.xy - cv.c), abs(r.zw - cv.c));
        // wholly out of reach, or wholly inside an arc's hole
        if (dot(nearest - cv.c, nearest - cv.c) > reach * reach || dot(farthest, farthest) < inner * inner) continue;
        [loop] for (int x = 0; x <= 4; x++)
        [loop] for (int y = 0; y <= 4; y++)
        {
            float2 d = lerp(r.xy, r.zw, float2(x, y) / 4) - cv.c;
            float d2 = dot(d, d);
            if (d2 < inner * inner || d2 > reach * reach) continue;
            if (cv.span >= 6.28) return true;
            float margin = 1 / max(sqrt(d2), 1);
            float a = atan2(d.y, d.x) - cv.a0 + margin;
            a -= GREEBLE_TWO_PI * floor(a / GREEBLE_TWO_PI);
            if (a <= cv.span + 2 * margin) return true;
        }
    }
    return false;
}

// The curve's piece nearest angle ang (from a0) out of count, centred offset pieces along; wrap: round a well.
int GreebleSlot(GreebleCurve cv, float ang, int count, float offset, bool wrap, int last)
{
    int k = (int)round(ang / cv.span * count - offset);
    return wrap && cv.well ? (k % count + count) % count : clamp(k, 0, last);
}
// Whether the point at angle a and radius r round the curve lies within reach of the well its arc runs into: pieces
// there are left out.
bool GreebleNearWell(GreebleCurve cv, float a, float r, float reach)
{
    [branch] if (!cv.endsInWell) return false;
    return distance(cv.c + GreebleDir(a) * r, cv.wellAt) < reach;
}
// Height of one curve's pieces at p, 0 off them (GreebleField.Curved).
float GreebleCurveHeight(GreebleCurve cv, float2 p)
{
    float2 d = p - cv.c;
    float rho2 = dot(d, d), outer = cv.ro + 1.45;
    if (rho2 > outer * outer) return 0;
    float rho = sqrt(rho2);
    // an arc's hole and a well's moat hold nothing
    if (cv.well ? (rho > .75 && rho < cv.ri - .1) : rho < cv.ri - .85) return 0;
    float rm = (cv.ri + cv.ro) * .5, w = cv.ro - cv.ri, ea = .8 / rm;
    float phi = atan2(d.y, d.x);
    float ang = phi - cv.a0; ang -= GREEBLE_TWO_PI * floor(ang / GREEBLE_TWO_PI);   // 0..2pi from a0
    // nor does the gap past an arc's ends
    if (!cv.well && ang > cv.span + ea && ang < GREEBLE_TWO_PI - ea) return 0;
    float best = 0;
    // the band, in pieces about half a metre long (each left out where it runs into its well)
    int n = (int)ceil(cv.span * rm / .5);
    if (ang <= cv.span && rho >= cv.ri && rho <= cv.ro)
    {
        int k = min((int)floor(ang / cv.span * n), n - 1);
        if (!GreebleNearWell(cv, cv.a0 + cv.span * (k + .5) / n, rm, 2.45)) best = cv.h;
    }
    // cross ribs about every 1.6 m
    int ribs = max(1, (int)round(cv.span * rm / 1.6));
    {
        int k = GreebleSlot(cv, ang, ribs, .5, true, ribs - 1);
        float b = cv.a0 + cv.span * (k + .5) / ribs;
        if (abs(GreebleWrap(phi - b)) <= .28 / rm && rho >= cv.ri - .1 && rho <= cv.ro + .1 && !GreebleNearWell(cv, b, rm, 2.9))
            best = max(best, min(.8, cv.h + .08));
    }
    // blocks and drums along the top, at the ribs' midpoints
    if (ribs > 1)
    {
        int k = GreebleSlot(cv, ang, ribs, 1, false, ribs - 2);
        uint s = GreebleHash(cv.seed, k + 10);
        float b = cv.a0 + cv.span * (k + 1.0) / ribs;
        float rr = rm + (GreebleR(s, 2) - .5) * w * .45;
        if (GreebleR(s, 0) >= .25 && !GreebleNearWell(cv, b, rr, 2.9))
        {
            float top = min(.8, cv.h + .12 + GreebleR(s, 3) * .1);
            if (GreebleR(s, 4) < .55)
            {
                float hw = lerp(.18, .3, GreebleR(s, 5)), ha = lerp(.2, .45, GreebleR(s, 6)) / rr;
                if (abs(rho - rr) <= hw && abs(GreebleWrap(phi - b)) <= ha) best = max(best, top);
            }
            else if (distance(p, cv.c + GreebleDir(b) * rr) <= lerp(.18, .32, GreebleR(s, 5))) best = max(best, top);
        }
    }
    // angled buttresses down the outer side, and on arcs some down the inner side
    int nb = cv.well ? 8 : max(1, (int)round(cv.span * rm / 2.8));
    {
        int k = GreebleSlot(cv, ang, nb, .5, true, nb - 1);
        uint s = GreebleHash(cv.seed, k + 200);
        float b = cv.a0 + cv.span * (k + .5) / nb;
        float len = cv.well ? 1.4 : lerp(.8, 1.2, GreebleR(s, 0));
        if (abs(GreebleWrap(phi - b)) <= .16 / cv.ro && rho >= cv.ro - .05 && rho <= cv.ro + len && !GreebleNearWell(cv, b, cv.ro + len * .5, 3.2))
            best = max(best, lerp(cv.h, GreebleLevel(0), (rho - (cv.ro - .05)) / (len + .05)));
    }
    if (!cv.well && cv.ri > 1.4)
    {
        int k = GreebleSlot(cv, ang, nb, 1, false, nb - 1);
        float b = cv.a0 + cv.span * (k + 1.0) / nb;
        if (GreebleR(GreebleHash(cv.seed, k + 200), 1) < .4 && abs(GreebleWrap(phi - b)) <= .16 / cv.ri && rho >= cv.ri - .8 && rho <= cv.ri + .05)
            best = max(best, lerp(GreebleLevel(0), cv.h, (rho - (cv.ri - .8)) / .85));
    }
    if (cv.well)
    {
        // the drum in the middle
        if (rho <= .75) best = max(best, rho < .45 ? .55 : lerp(.55, .4, (rho - .45) / .3));
        return best;
    }
    // heavy blocks at the free ends
    if (rho >= cv.ri - .2 && rho <= cv.ro + .2)
    {
        float top = min(.8, cv.h + .2);
        if (abs(GreebleWrap(phi - cv.a0)) <= ea) best = max(best, top);
        if (!cv.endsInWell && abs(GreebleWrap(phi - cv.a0 - cv.span)) <= ea) best = max(best, top);
    }
    return best;
}

// ---- plates

// Whether a leaf lies within one tile's face (GreebleField.Clear, less the holes the shader doesn't know).
bool GreebleClear(float4 q)
{
    float2 hi = floor((q.xy + 2.5) / 5) * 5 + 2.5;
    return q.z <= hi.x && q.w <= hi.y;
}
void GreebleTake(float2 p, float4 r, float h, uint seed, inout float best, inout uint tone)
{
    if (h > best && GreebleIn(p, r)) { best = h; tone = seed; }
}

// The carved armour's plate shapes at relief scale (GreebleField.CutLeaf, TileMesh.CutPlate).
float GreebleCut(float2 p, float4 q, uint seed, int lv, float t)
{
    float w = q.z - q.x, ht = q.w - q.y, s = min(w, ht) / .6, cut = .05;
    int lo = lv < 5 ? lv : lv - 1;
    float hlo = GreebleLevel(lo), hhi = GreebleLevel(lo + 1);
    int kind = t < .27 ? 0 : t < .34 ? 1 : 2;
    bool turn = ht > w;
    float2 a = turn ? p.yx : p;
    float4 r = turn ? q.yxwz : q;
    float rw = r.z - r.x, rh = r.w - r.y;
    if (kind == 1 && rw <= rh + .22 * s) kind = 3;                          // too short for a diagonal: a corner
    if (kind == 2 && (rw <= .6 * s || rh < .26 * s + 2 * cut)) kind = 3;    // too small for a jog: a corner
    if (kind == 0 || kind == 3)
    {
        float c = min(lerp(.12, .26, GreebleR(seed, 63)) * s, min(w, ht) * .45);
        int corner = ((int)(GreebleR(seed, 64) * 4)) & 3;
        float2 at = corner == 0 ? q.xy : corner == 1 ? q.zy : corner == 2 ? q.zw : q.xw;
        float2 d = abs(p - at);
        return d.x + d.y < c ? 0 : (kind == 0 ? hlo : GreebleLevel(lv));
    }
    float d; bool first;
    if (kind == 1)
    {
        first = GreebleR(seed, 67) < .5;
        float x0 = lerp(r.x + .1 * s, r.z - rh - .1 * s, GreebleR(seed, 65));
        d = GreebleR(seed, 66) < .5 ? ((a.x - x0) - (a.y - r.y)) * .7071 : ((a.x - x0) - (r.w - a.y)) * .7071;
    }
    else
    {
        first = GreebleR(seed, 70) < .5;
        float ya = lerp(r.y + .13 * s, r.w - .13 * s, GreebleR(seed, 68));
        float yb = ya < (r.y + r.w) * .5 ? min(ya + .24 * s, r.w - .1 * s) : max(ya - .24 * s, r.y + .1 * s);
        float run = abs(yb - ya), xa = lerp(r.x + .1 * s, r.z - .1 * s - run, GreebleR(seed, 69)), xb = xa + run;
        d = a.y - (a.x <= xa ? ya : a.x >= xb ? yb : ya + (a.x - xa) * (yb - ya) / max(run, 1e-4));
    }
    if (abs(d) < cut) return 0;
    return (d < 0) == first ? hlo : hhi;
}

// Height of the cached leaf's form at p (GreebleField.Leaf), and the seed of the piece there for its tone.
float GreebleLeaf(float2 p, GreebleCache c, out uint tone)
{
    uint seed = c.leafSeed; int lv = c.leafLevel; float4 q = c.q; float t = c.form;
    tone = seed;
    if (!GreebleIn(p, q)) return 0;
    float w = q.z - q.x, ht = q.w - q.y, small = min(w, ht), big = max(w, ht);
    float L0 = GreebleLevel(0), L1 = GreebleLevel(1), L2 = GreebleLevel(2);
    if (c.under) return w > .12 && ht > .12 ? L0 : 0;
    if (small < .12) return 0;
    float h = GreebleLevel(lv), up = GreebleLevel(min(lv + 1, 5)), up2 = GreebleLevel(min(lv + 2, 5));
    float best = 0;
    [branch] if (t < .18) return h;
    [branch] if (t < .39) return GreebleCut(p, q, seed, lv, t);
    [branch] if (t < .47 && big < small * 1.5 && small > .9 && c.clear)
    {
        // round: a drum with a cap, a dome on a plinth, or a ring round a sunk disc
        float rr = small * .46, rho = distance(p, (q.xy + q.zw) * .5), t2 = GreebleR(seed, 6), top = max(h, L2);
        if (t2 < .35) return rho <= rr * .55 ? min(.8, top + .15) : (rho <= rr ? top : 0);
        if (t2 < .7)
        {
            if (rho > rr) return 0;
            float dr = .85 * rr;
            return rho > dr ? L0 : L0 + (.8 - L0) * sqrt(saturate(1 - (rho / dr) * (rho / dr)));
        }
        return rho > rr ? 0 : (rho >= rr * .62 ? top : L0);
    }
    [branch] if (t < .53 && small > .6 && c.clear)
    {
        // a ramp rising along the long side
        bool alongU = w >= ht;
        float x = alongU ? (p.x - q.x) / w : (p.y - q.y) / ht;
        return lerp(L0, max(h, L2), GreebleR(seed, 7) < .5 ? x : 1 - x);
    }
    [branch] if (t < .60)
    {
        // a stepped plate
        GreebleTake(p, q, h, seed, best, tone);
        if (up > h) GreebleTake(p, GreebleInset(q, min(.4, small * .22)), up, seed + 1, best, tone);
        return best;
    }
    [branch] if (t < .67 && small > .6)
    {
        // a sunk panel in a raised frame
        if (GreebleIn(p, GreebleInset(q, min(.22, small * .18)))) { tone = seed + 2; return L0; }
        return h;
    }
    [branch] if (t < .75 && small > .5)
    {
        // ribs along the long side on a low plate
        int n = max(1, (int)floor(small / .36));
        float pitch = small / n, x = w >= ht ? p.y - q.y : p.x - q.x;
        float local = x - min(floor(x / pitch), n - 1) * pitch;
        if (local >= pitch * .28 && local <= pitch * .72) { tone = seed + 3; return max(h, L1); }
        return L0;
    }
    [branch] if (t < .85)
    {
        // a grid of small blocks at mixed heights
        int nx = max(1, (int)round(w / .5)), ny = max(1, (int)round(ht / .5));
        float cw = w / nx, ch = ht / ny;
        int x = min((int)floor((p.x - q.x) / cw), nx - 1), y = min((int)floor((p.y - q.y) / ch), ny - 1);
        uint s = GreebleHash(seed, x * 31 + y * 977 + 5);
        if (GreebleR(s, 0) < .12) return 0;
        if (!GreebleIn(p, float4(q.x + x * cw + .05, q.y + y * ch + .05, q.x + (x + 1) * cw - .05, q.y + (y + 1) * ch - .05))) return 0;
        tone = s;
        return GreebleLevel(clamp(lv - 1 + (int)(GreebleR(s, 1) * 3), 0, 5));
    }
    [branch] if (t < .93 && small > 1.3 && big < small * 1.7)
    {
        // a stepped pyramid
        GreebleTake(p, q, h, seed, best, tone);
        if (up > h) GreebleTake(p, GreebleInset(q, small * .2), up, seed + 1, best, tone);
        if (up2 > up) GreebleTake(p, GreebleInset(q, small * .36), up2, seed + 2, best, tone);
        return best;
    }
    // a glyph: a few bars standing out of a sunk plate
    best = L0;
    int bars = 2 + (int)(GreebleR(seed, 5) * 3);
    [unroll] for (int k = 0; k < 4; k++)
    {
        if (k >= bars) break;
        uint s = GreebleHash(seed, k + 50);
        bool alongU = GreebleR(s, 0) < .5;
        float th = min(.18, small * .25), length = (alongU ? w : ht) * lerp(.3, .8, GreebleR(s, 1));
        float a = lerp(alongU ? q.x : q.y, (alongU ? q.z : q.w) - length, GreebleR(s, 2));
        float b = lerp(alongU ? q.y : q.x, (alongU ? q.w : q.z) - th, GreebleR(s, 3));
        GreebleTake(p, alongU ? float4(a, b, a + length, b + th) : float4(b, a, b + th, a + length), max(L1, h) - k * .015, s, best, tone);
    }
    return best;
}

// The plate's rim k (bottom, top, left, right; GreebleField.Build), and its inside past the rims.
float4 GreebleRim(GreebleCache c, int k)
{
    float rw = .55;
    float4 o = GreebleInset(c.plate, .15);
    float y0 = (c.rims & 1u) ? o.y + rw : o.y, y1 = (c.rims & 2u) ? o.w - rw : o.w;
    return k == 0 ? float4(o.x, o.y, o.z, o.y + rw) : k == 1 ? float4(o.x, o.w - rw, o.z, o.w)
         : k == 2 ? float4(o.x, y0, o.x + rw, y1) : float4(o.z - rw, y0, o.z, y1);
}
float4 GreebleInner(GreebleCache c)
{
    float g = .15 + .55 + .05;
    return c.plate + float4((c.rims & 4u) ? g : 0, (c.rims & 1u) ? g : 0, (c.rims & 8u) ? -g : 0, (c.rims & 2u) ? -g : 0);
}
// Finds the top-level plate holding p, and which edges carry a rim (GreebleField.Plates, Split and Build).
void GreebleFindPlate(GreebleField f, float2 p, inout GreebleCache c)
{
    float2 l = GreebleLattice(f, p);
    int2 c0, c1; GreebleCells(l, l, c0, c1);
    c.plate = float4(p, p); c.plateSeed = 0; c.density = 1; c.level = 0;
    bool found = false;
    [loop] for (int i = c0.x; i <= c1.x && !found; i++)
    [loop] for (int j = c0.y; j <= c1.y && !found; j++)
    {
        float4 big, small; uint seed; float density; int level;
        GreebleDistrict(f, i, j, big, small, seed, density, level);
        c.density = density; c.level = level;
        if (GreebleIn(p, small)) { c.plate = small; c.plateSeed = GreebleHash(seed, 303); found = true; }
        else if (GreebleIn(p, big))
        {
            float4 r = big, a, b; uint s = seed, sa, sb;
            [unroll] for (int depth = 0; depth < 2; depth++)
            {
                if (!GreebleSplit(r, s, depth, a, b, sa, sb)) break;
                bool first = GreebleIn(p, a);
                r = first ? a : b; s = first ? sa : sb;
            }
            c.plate = r; c.plateSeed = s; found = true;
        }
    }
    // a raised rim along some edges of the plate, never all four
    uint seed = c.plateSeed;
    uint e = (GreebleR(seed, 60) < .5 ? 1u : 0u) | (GreebleR(seed, 61) < .5 ? 2u : 0u) | (GreebleR(seed, 62) < .5 ? 4u : 0u) | (GreebleR(seed, 63) < .5 ? 8u : 0u);
    uint pick = 1u << (((int)(GreebleR(seed, 64) * 4)) & 3);
    if (e == 0) e = pick; else if (e == 15) e ^= pick;
    c.rims = GreebleR(seed, 40) < .55 ? e : 0;
    c.leaf = GREEBLE_EMPTY;
}
// Walks the plate's recursive cuts down to the leaf holding p, and keeps what the leaf's form needs (GreebleField.Node).
void GreebleFindLeaf(float2 p, inout GreebleCache c)
{
    float4 r = GreebleInner(c); uint seed = c.plateSeed; int lv = c.level;
    [loop] for (int depth = 0; depth < 14; depth++)
    {
        float w = r.z - r.x, ht = r.w - r.y, big = max(w, ht);
        float stop = depth < 1 ? 0 : saturate((.16 + .13 * depth) / c.density);
        if (big < 1.2 || (big < 6.5 && GreebleR(seed, 0) < stop)) break;
        bool alongU = w > ht * 1.3 || (ht <= w * 1.3 && GreebleR(seed, 1) < .5);
        float len = alongU ? w : ht, start = alongU ? r.x : r.y;
        int parts = len > 4.5 && GreebleR(seed, 2) < .35 ? 3 : 2;
        float c1 = start + round(((1 + (GreebleR(seed, 4) - .5) * .55) / parts) * len / .25) * .25;
        c1 = GreebleClamp(c1, start + .5, start + len - .5 * (parts - 1));
        float c2 = start + len;
        if (parts == 3)
        {
            c2 = start + round(((2 + (GreebleR(seed, 5) - .5) * .55) / 3) * len / .25) * .25;
            c2 = GreebleClamp(c2, c1 + .5, start + len - .5);
        }
        float x = alongU ? p.x : p.y;
        int k = x < c1 ? 0 : (parts == 3 && x >= c2) ? 2 : 1;
        float lo = k == 0 ? start : k == 1 ? c1 : c2, hi = k == 0 ? c1 : (k == 1 && parts == 3) ? c2 : start + len;
        if (alongU) { r.x = lo; r.z = hi; } else { r.y = lo; r.w = hi; }
        int pick = (int)(GreebleR(seed, 10 + k) * 6);
        lv = clamp(lv + (pick == 0 ? -1 : pick < 3 ? 0 : pick < 5 ? 1 : 2), 0, 5);
        seed = GreebleHash(seed, k + 20);
    }
    c.leaf = r; c.leafSeed = seed; c.leafLevel = lv;
    c.q = GreebleInset(r, .07 + GreebleR(seed, 3) * .07);
    c.form = GreebleR(seed, 4); c.clear = GreebleClear(c.q); c.under = false;
}

// The relief's height over the face at p (u, v on the face plane), and the seed of the piece there for its tone
// (0: the face itself).
float GreebleHeight(GreebleField f, float2 p, inout GreebleCache c, out uint tone)
{
    tone = 0;
    if (!GreebleIn(p, c.plate)) GreebleFindPlate(f, p, c);
    // which piece of the plate p is on: rim 0-3, the inside past the rims (4) or the gap between them (5)
    int part = 5;
    [unroll] for (int k = 0; k < 4; k++) if (part == 5 && (c.rims >> k & 1u) && GreebleIn(p, GreebleRim(c, k))) part = k;
    if (part == 5 && GreebleIn(p, GreebleInner(c))) part = 4;
    bool newLeaf = part == 4 && !GreebleIn(p, c.leaf);
    if (newLeaf) GreebleFindLeaf(p, c);
    // whether a rim met for the first time, or a new leaf, lies in the bed under a curve
    if (newLeaf || (part < 4 && !(c.rims >> (4 + part) & 1u)))
    {
        bool under = GreebleUnder(f, part < 4 ? GreebleRim(c, part) : c.q);
        if (part < 4) c.rims |= (1u << (4 + part)) | (under ? 1u << (8 + part) : 0u);
        else c.under = under;
    }
    float best = 0;
    if (part < 4) { best = (c.rims >> (8 + part) & 1u) ? GreebleLevel(0) : GreebleLevel(min(5, c.level + 1)); tone = c.plateSeed; }
    else if (part == 4) { uint leafTone; float h = GreebleLeaf(p, c, leafTone); if (h > 0) { best = h; tone = leafTone; } }
    [loop] for (int k = 0; k < GREEBLE_CURVES; k++)
    {
        if (k >= f.count) break;
        float h = GreebleCurveHeight(f.curves[k], p);
        if (h > best) { best = h; tone = f.curves[k].seed | GREEBLE_CURVE_TONE; }
    }
    return best;
}
// The tone of a piece: GreebleField shades each box .93-1.07, each curve .95-1.05.
float GreebleTone(uint tone)
{
    if (tone == 0) return 1.03;   // the face between pieces is the wall's plain white, not the relief's .97
    if (tone & GREEBLE_CURVE_TONE) return .95 + GreebleR(tone & ~GREEBLE_CURVE_TONE, 1) * .1;
    return .93 + GreebleR(tone, 9) * .14;
}

// ---- the trace

// A face's shell, from its vertices (TileMesh.Shells.cs): uv1 the free rect (u, v min and max), uv2 (axis * 2 + 1 if
// the face looks along +axis, the shell's top, side bits: 1 u min, 2 u max, 4 v min, 8 v max), uv6.w the face plane.
struct GreebleShell { int axis, ua, va; float sign, plane, top; float4 rect; uint sides; };
GreebleShell GreebleShellOf(float4 uv1, float4 uv2, float plane)
{
    GreebleShell s;
    int code = (int)round(uv2.x);
    s.axis = clamp(code >> 1, 0, 2); s.ua = (s.axis + 1) % 3; s.va = (s.axis + 2) % 3;
    s.sign = (code & 1) ? 1 : -1; s.plane = plane; s.top = uv2.y; s.rect = uv1; s.sides = (uint)round(uv2.z);
    return s;
}
float3 GreebleToFace(GreebleShell s, float3 p) { return float3(ReliefAt(p, s.ua), ReliefAt(p, s.va), s.sign * (ReliefAt(p, s.axis) - s.plane)); }
float3 GreebleToFaceDir(GreebleShell s, float3 d) { return float3(ReliefAt(d, s.ua), ReliefAt(d, s.va), s.sign * ReliefAt(d, s.axis)); }
float3 GreebleFromFace(GreebleShell s, float3 q) { return ReliefAxis(s.ua) * q.x + ReliefAxis(s.va) * q.y + ReliefAxis(s.axis) * (s.plane + s.sign * q.z); }
float3 GreebleFromFaceDir(GreebleShell s, float3 q) { return ReliefAxis(s.ua) * q.x + ReliefAxis(s.va) * q.y + ReliefAxis(s.axis) * (s.sign * q.z); }
// How far along d (face space) the walk from e may go before it leaves the shell where the relief stops.
float GreebleExit(GreebleShell s, float3 e, float3 d)
{
    float t = 1e9;
    if ((s.sides & 1u) && d.x < -1e-6) t = min(t, (s.rect.x - e.x) / d.x);
    if ((s.sides & 2u) && d.x > 1e-6) t = min(t, (s.rect.z - e.x) / d.x);
    if ((s.sides & 4u) && d.y < -1e-6) t = min(t, (s.rect.y - e.y) / d.y);
    if ((s.sides & 8u) && d.y > 1e-6) t = min(t, (s.rect.w - e.y) / d.y);
    return max(t, 0);
}

struct GreebleHit
{
    bool hit;          // false: the ray left the shell without meeting relief or face
    bool face;         // it met the face between pieces
    float3 station;    // where, in station space
    float3 normal;     // station space
    float tone;
    float shadow;      // 1 lit, 0 in the relief's own shadow
};

// Walks the ray (station space, dir unit) from where it enters the shell at e0 through the relief to the face, a step
// a pixel or so of travel across the face long and then halving onto the crossing; then toward the sun if sunS isn't
// zero. lower scales every height (the band where the relief sinks into the face); footprint: metres a pixel spans.
GreebleHit GreebleTrace(GreebleShell s, float3 e0, float3 dirS, float3 sunS, float lower, float footprint)
{
    GreebleHit hit = (GreebleHit)0;
    hit.station = e0; hit.normal = GreebleFromFaceDir(s, float3(0, 0, 1)); hit.tone = 1; hit.shadow = 1;
    float3 e = GreebleToFace(s, e0), d = GreebleToFaceDir(s, dirS);
    float top = s.top * lower;
    // where the walk ends: the face, the top on the way out, a side where the relief stops, or 12 m on
    float tEnd = min(12, GreebleExit(s, e, d));
    bool toFace = false;
    if (d.z < -1e-6) { float tf = -e.z / d.z; if (tf <= tEnd) { tEnd = tf; toFace = true; } }
    else if (d.z > 1e-6) tEnd = min(tEnd, max(top - e.z, 0) / d.z);
    // skip the empty slab above the sunk relief
    float t0 = (e.z > top && d.z < -1e-6) ? (e.z - top) / -d.z : 0;
    if (t0 > tEnd) { if (!toFace) return hit; t0 = tEnd; }
    float3 a = e + d * t0, b = e + d * tEnd;
    GreebleField f = (GreebleField)0;
    f.plane = GreebleHash(ReliefMix((uint)(s.axis * 2 + (s.sign > 0 ? 1 : 0))), (int)round(s.plane * 4));
    f.off = float2(GreebleR(f.plane, 1), GreebleR(f.plane, 2)) * GREEBLE_BIG;
    GreebleGather(f, float4(min(a.xy, b.xy), max(a.xy, b.xy)));
    GreebleCache c = (GreebleCache)0;
    c.plate = c.leaf = GREEBLE_EMPTY;
    // The height only changes across the face, so the steps follow the ray's travel across it; halving stops at a
    // tenth of a pixel.
    float travel = (tEnd - t0) * length(d.xy), pixel = max(footprint * .7, .015);
    int steps = (int)clamp(ceil(travel / pixel), 2, 56);
    int halvings = (int)clamp(ceil(log2(max(travel / steps, 1e-4) / (.1 * footprint))), 2, 7);
    float probe = max(footprint * .5, .01);
    float3 l = GreebleToFaceDir(s, sunS);
    // One loop and one call of the height function: phase 0 walks the ray, 1 halves onto the crossing, 2 reads the
    // hit and four probes round it (a side faces the way the height falls; a top tilts with a ramp or dome), 3 walks
    // toward the sun, 4 is done. A ray entering through the top can't meet anything at its first point.
    int phase = 0, k = e.z >= top - 1e-4 ? 1 : 0, sunSteps = 1;
    float tPrev = t0, lo = 0, hi = 0, reach = 0, hq = 0;
    float2 g = 0;
    float3 q = a, from = 0;
    uint hitTone = 0;
    [loop] for (int it = 0; it < 100 && phase < 4; it++)
    {
        float t = phase == 0 ? lerp(t0, tEnd, k / (float)steps) : phase == 1 ? (lo + hi) * .5 : reach * k / sunSteps;
        float2 off = k == 1 ? float2(probe, 0) : k == 2 ? float2(-probe, 0) : k == 3 ? float2(0, probe) : k == 4 ? float2(0, -probe) : 0;
        float3 at = phase < 2 ? e + d * t : phase == 2 ? float3(q.xy + off, q.z) : from + l * t;
        uint tone;
        float h = GreebleHeight(f, at.xy, c, tone) * lower;
        bool sunNext = false;
        if (phase == 0)
        {
            if (h >= at.z) { lo = tPrev; hi = t; phase = 1; k = 0; }
            else if (++k > steps)
            {
                if (!toFace) return hit;   // out of the shell past every piece
                q = e + d * tEnd; q.z = 0;
                hit.hit = true; hit.face = true; hit.tone = GreebleTone(0);
                sunNext = true;
            }
            else tPrev = t;
        }
        else if (phase == 1)
        {
            if (h >= at.z) hi = t; else lo = t;
            if (++k >= halvings) { q = e + d * hi; phase = 2; k = 0; }
        }
        else if (phase == 2)
        {
            if (k == 0) { hq = h; hitTone = tone; } else g += off * h;
            if (++k > 4)
            {
                g /= 2 * probe * probe;
                float3 n = float3(0, 0, 1);
                // (a side if the piece stands clear above where the halving left the ray, given how far it got)
                if (hq > q.z + .004 + (hi - lo) * abs(d.z)) n = dot(g, g) > 1e-6 ? float3(-normalize(g), 0) : float3(-normalize(d.xy + 1e-6), 0);
                else { q.z = hq; if (dot(g, g) < 9) n = normalize(float3(-g, 1)); }   // steeper: an edge nearby
                hit.hit = true; hit.tone = GreebleTone(hitTone); hit.normal = GreebleFromFaceDir(s, n);
                sunNext = true;
            }
        }
        else
        {
            if (h > at.z) { hit.shadow = 0; phase = 4; }
            else if (++k > sunSteps) phase = 4;
        }
        if (sunNext)
        {
            // toward the sun: anything higher on the way out of the slab shadows the hit; a step a pixel across
            phase = 4;
            if (l.z > 1e-3 && top > q.z)
            {
                reach = min(min((top - q.z) / l.z, 8), GreebleExit(s, q, l));
                sunSteps = (int)clamp(ceil(reach * length(l.xy) / footprint), 2, 16);
                from = q + float3(0, 0, .003); phase = 3; k = 1;
            }
        }
    }
    if (hit.hit) hit.station = GreebleFromFace(s, q);
    return hit;
}

// A shell pixel (TileMesh.Shells.cs: rect and code are uv1 and uv2, station uv6): the trace from where the view ray
// enters the shell, toward the sun if sunWS isn't zero. wx, wy, sx, sy: screen derivatives of the camera-relative and
// station positions. positionWS and normalWS: the traced surface. False where the ray leaves the shell (what's behind
// shows) or the frame can't be inverted.
bool GreebleShellSurface(float3 viewWS, float3 nWS, float4 station, float4 rect, float4 code, float3 wx, float3 wy, float3 sx, float3 sy,
    float footprint, float3 sunWS, out GreebleHit hit, out float3 positionWS, out float3 normalWS, out float lower)
{
    GreebleShell shell = GreebleShellOf(rect, code, station.w);
    lower = ReliefLower(_TileGreeble, length(viewWS));
    hit = (GreebleHit)0; positionWS = viewWS + _WorldSpaceCameraPos; normalWS = nWS;
    float3 ns; float3x3 toStation, toWorld;
    if (_TileGreeble.x < .5 || !ReliefFrame(wx, wy, nWS, sx, sy, ns, toStation, toWorld)) return false;
    float3 sunS = dot(sunWS, sunWS) > 0 ? normalize(mul(toStation, sunWS)) : 0;
    hit = GreebleTrace(shell, station.xyz, normalize(mul(toStation, viewWS)), sunS, lower, footprint);
    positionWS += mul(toWorld, hit.station - station.xyz);
    normalWS = normalize(mul(toWorld, hit.normal));
    return hit.hit && all(isfinite(positionWS)) && all(isfinite(normalWS));
}
// Depth of a traced point, no nearer than the shell itself: pieces 3 mm behind where they are, so the mesh relief of
// a nearer level wins while both draw at a handover; the face 1 mm in front, so it wins over the wall under it.
float GreebleDepth(float3 positionWS, bool face, float shellDepth)
{
    float z = ReliefDepthAt(positionWS, face ? -.001 : .003);
    #if UNITY_REVERSED_Z
        return min(z, shellDepth);
    #else
        return max(z, shellDepth);
    #endif
}

#endif
