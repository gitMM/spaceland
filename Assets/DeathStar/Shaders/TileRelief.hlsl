#ifndef DEATHSTAR_TILE_RELIEF_INCLUDED
#define DEATHSTAR_TILE_RELIEF_INCLUDED

// The armour carving (TileMesh.Carving.cs) as a height function in station space, and a trace through it: the middle
// range of doc/close-up-detail-plan.md. Meshes that leave the armour plain (the mid and far levels of detail) carry each
// armour face's station position and layout key in uv6 (TileMesh.ReliefChannel), and TileSurface walks the view ray
// through the plates standing on it, traces once more toward the sun for the plates' own shadows, and writes its depth.
// Ported: 8 m zones calm or busy, courses along the tile's axis, plate edges by hash, plain plates, corner cuts, sunk
// panels and hatches (their frame and door; hinges and handles are left out), diagonal and jogged splits, the gaps,
// grooves and the plain band along the face's edges. Not ported: slivers dropped at edges, plates left out under other
// solids, round hatches (drawn square).

#define RELIEF_GAP 0.014
#define RELIEF_CUT 0.03
#define RELIEF_TRIM 0.08
#define RELIEF_ZONE 8.0
#define RELIEF_TOP 0.26   // the highest piece: 0.10 + 0.12 + a hatch's frame

static const float3 ReliefZoneOffset = float3(3.1, 1.7, 5.9);

// Component i of v, and v with component i set (selects: indexing a vector by a runtime index makes the compiler
// unroll the trace's loops).
float ReliefAt(float3 v, int i) { return i == 0 ? v.x : i == 1 ? v.y : v.z; }
float3 ReliefAxis(int i) { return float3(i == 0, i == 1, i == 2); }

// Inverse of a 3 x 3 matrix (rows a, b, c): the adjugate over the determinant.
float3x3 ReliefInverse(float3x3 m)
{
    float3 a = m[0], b = m[1], c = m[2];
    float3 r0 = cross(b, c), r1 = cross(c, a), r2 = cross(a, b);
    float det = dot(a, r0);
    return transpose(float3x3(r0, r1, r2)) / (abs(det) > 1e-20 ? det : 1e-20);
}

// The lowbias32 finaliser: a well mixed hash of h (TileGreeble's hash, GreebleField.Hash, uses it too).
uint ReliefMix(uint h) { h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; return h; }
float ReliefH01(int a, int b, int c, int k)
{
    uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u ^ (uint)k * 2654435761u;
    return (ReliefMix(h) & 0xFFFFFF) / 16777216.0;
}

// World and station frames at a pixel, from screen derivatives of both positions (the Foundry bends one into the
// other): ns the surface's station normal, toStation maps a world direction into station space, toWorld back. False
// where either frame is squashed flat (a face seen edge-on, or no area on screen): it has no inverse.
bool ReliefFrame(float3 wx, float3 wy, float3 nWS, float3 sx, float3 sy, out float3 ns, out float3x3 toStation, out float3x3 toWorld)
{
    float3 crossS = cross(sx, sy);
    ns = crossS * rsqrt(max(dot(crossS, crossS), 1e-30)); if (dot(cross(wx, wy), nWS) < 0) ns = -ns;
    float3x3 W = transpose(float3x3(wx, wy, nWS)), S = transpose(float3x3(sx, sy, ns));
    float detW = abs(dot(wx, cross(wy, nWS))), detS = abs(dot(sx, cross(sy, ns)));
    toStation = mul(S, ReliefInverse(W)); toWorld = mul(W, ReliefInverse(S));
    return detW > 1e-6 * dot(wx, wx) * length(wy) && detS > 1e-6 * dot(sx, sx) * length(sy) && dot(crossS, crossS) > 1e-24;
}
// How far traced relief stands at this distance from the camera: 1 whole, 0 sunk into the face over band.y-band.z m.
float ReliefLower(float4 band, float distance) { return saturate((band.z - distance) / max(band.z - band.y, 1)); }
// Clip-space depth of a world point pushed push metres away from the camera (negative: toward it).
float ReliefDepthAt(float3 surfaceWS, float push)
{
    float4 clip = TransformWorldToHClip(surfaceWS + normalize(surfaceWS - _WorldSpaceCameraPos) * push);
    return clip.z / clip.w;
}
bool ReliefEdge(int k, int row, int key, bool calm)
{
    return ((k % 4) + 4) % 4 == 0 || ReliefH01(k, row, key, 0) < (calm ? 0.2 : 0.45);
}

// A face's layout: ua, va the station axes its courses run across and stack along, ax its own; key the face plane's
// hash (as CarveFace makes it); calm 1 when the tile's budget made every plate calm.
struct ReliefFace { int ax, ua, va, key; bool allCalm; };

// Height of the carving at station point s on the face (0 the face itself), with gaps narrower than fill filled in
// (they are under a pixel; TileSurface folds their darkening instead). plate: the plate's own random number, for tone.
float ReliefHeight(ReliefFace f, float3 s, float fill, out float plate)
{
    plate = 0;
    float u = ReliefAt(s, f.ua), v = ReliefAt(s, f.va);
    float3 zone = floor((s + ReliefZoneOffset) / RELIEF_ZONE);
    int3 idx = (int3)zone;
    bool calm = f.allCalm || ReliefH01(idx.x, idx.y, idx.z, 77) < 0.4;
    float sc = calm ? 2 : 1, rowH = 0.62 * sc, cellW = 0.31 * sc;
    float gap = max(RELIEF_GAP - fill * .5, 0);
    // the zone, less its gap
    float zx0 = ReliefAt(zone, f.ua) * RELIEF_ZONE - ReliefAt(ReliefZoneOffset, f.ua), zy0 = ReliefAt(zone, f.va) * RELIEF_ZONE - ReliefAt(ReliefZoneOffset, f.va);
    if (u < zx0 + gap || u > zx0 + RELIEF_ZONE - gap || v < zy0 + gap || v > zy0 + RELIEF_ZONE - gap) return 0;
    int zkey = f.key * 2 + (calm ? 1 : 0);
    int row = (int)floor(v / rowH);
    float r0 = row * rowH, stag = row * 0.47 * sc;
    int k = (int)floor((u + stag) / cellW);
    [loop] for (int i = 0; i < 4 && !ReliefEdge(k, row, zkey, calm); i++) k--;
    int k2 = k + 1;
    [loop] for (int j = 0; j < 4 && !ReliefEdge(k2, row, zkey, calm); j++) k2++;
    float4 r = float4(k * cellW - stag + gap, r0 + gap, k2 * cellW - stag - gap, r0 + rowH - gap);   // xMin, yMin, xMax, yMax
    if (u < r.x || u > r.z || v < r.y || v > r.w) return 0;
    float w = r.z - r.x, hgt = r.w - r.y;
    float t = ReliefH01(k, row, zkey, 1), h = 0.04 + 0.03 * floor(ReliefH01(k, row, zkey, 2) * 3);
    plate = ReliefH01(k, row, zkey, 11);
    float4 odds = calm ? float4(0.58, 0.70, 0.82, 0.90) : float4(0.30, 0.46, 0.56, 0.60);
    float odds4 = calm ? 0.95 : 0.80;
    float cut = max(RELIEF_CUT - fill * .5, 0);
    float2 p = float2(u, v);
    if (t < odds.x) return h;
    if (t < odds.y)
    {
        // a corner cut away at 45 degrees
        float c = min(lerp(0.12, 0.26, ReliefH01(k, row, zkey, 3)) * sc, min(w, hgt) * 0.45);
        int corner = ((int)(ReliefH01(k, row, zkey, 4) * 4)) & 3;
        float2 at = corner == 0 ? r.xy : corner == 1 ? r.zy : corner == 2 ? r.zw : r.xw;
        float2 d = abs(p - at);
        return d.x + d.y < c ? 0 : h;
    }
    if (t < odds.w)
    {
        // a sunk panel in a raised frame, or on a whole plate that is big enough a hatch: a door in the frame. (A plate
        // too thin for a frame stays plain.)
        float thickness = w * hgt / (w + hgt);
        if (thickness <= 0.27) return h;
        bool hatchy = t >= odds.z;
        bool hatch = hatchy && min(w, hgt) >= 0.5 && max(w, hgt) >= 0.8;
        float small = hatchy ? min(w, hgt) : thickness * 1.4;
        float fw = clamp(small * 0.12, 0.05, 0.12), top = h + 0.03;
        float2 e = min(p - r.xy, r.zw - p);
        float edge = min(e.x, e.y);
        if (edge < fw) return top;
        if (edge < fw + gap) return 0;
        return hatch ? 0.03 : 0.012;
    }
    // splits: work with the long side along x
    bool turn = hgt > w;
    float2 q = turn ? p.yx : p;
    float4 rr = turn ? r.yxwz : r;
    float rw = rr.z - rr.x, rh = rr.w - rr.y;
    if (t < odds4)
    {
        if (rw <= rh + 0.22 * sc) return h;
        bool first = ReliefH01(k, row, zkey, 7) < 0.5;
        float lo = h + 0.05, hi = h + 0.12;
        float a = lerp(rr.x + 0.1 * sc, rr.z - rh - 0.1 * sc, ReliefH01(k, row, zkey, 5));
        bool rising = ReliefH01(k, row, zkey, 6) < 0.5;
        float d = rising ? ((q.x - a) - (q.y - rr.y)) * 0.7071 : ((q.x - a) - (rr.w - q.y)) * 0.7071;
        if (abs(d) < cut) return 0;
        return d < 0 ? (first ? lo : hi) : (first ? hi : lo);
    }
    {
        if (rw <= 0.6 * sc || rh < 0.26 * sc + 2 * RELIEF_CUT) return h;
        bool first = ReliefH01(k, row, zkey, 10) < 0.5;
        float lo = h + 0.05, hi = h + 0.12;
        float ya = lerp(rr.y + 0.13 * sc, rr.w - 0.13 * sc, ReliefH01(k, row, zkey, 8));
        float yb = ya < (rr.y + rr.w) * 0.5 ? min(ya + 0.24 * sc, rr.w - 0.1 * sc) : max(ya - 0.24 * sc, rr.y + 0.1 * sc);
        float run = abs(yb - ya), xa = lerp(rr.x + 0.1 * sc, rr.z - 0.1 * sc - run, ReliefH01(k, row, zkey, 9)), xb = xa + run;
        float jog = q.x <= xa ? ya : q.x >= xb ? yb : ya + (q.x - xa) * (yb - ya) / max(run, 1e-4);
        float d = q.y - jog;
        if (abs(d) < cut) return 0;
        return d < 0 ? (first ? lo : hi) : (first ? hi : lo);
    }
}

struct ReliefHit
{
    float3 station;   // where the ray stops, station space
    float height;     // its height over the face
    float3 wall;      // station-space outward normal of a plate side hit, or 0 for a top or the face
    float plate;      // the plate's random number (0 off plates)
    float shadow;     // 1 lit, 0 in a plate's shadow
};

// Walk the ray (station space, dir toward the face) from where it enters the slab of plates over the face to the face
// point s0, a fixed number of steps and then halving onto the crossing; then toward the sun the same way. lower scales
// every height (the band where the plates sink into the face).
ReliefHit ReliefTrace(ReliefFace f, float3 s0, float3 n, float3 dir, float3 toSun, float lower, float fill)
{
    // (heights over the face; the layout is read on the face plane, so a point above it never crosses into another
    // zone along the face's own axis)
    #define RELIEF_AT(q) ReliefHeight(f, (q) - n * dot((q) - s0, n), fill, drop)
    float drop;
    ReliefHit hit;
    hit.station = s0; hit.height = 0; hit.wall = 0; hit.plate = 0; hit.shadow = 1;
    float down = -dot(dir, n);
    if (lower <= 0 || down <= 1e-3) return hit;
    float top = RELIEF_TOP * lower;
    float span = top / down;                 // ray length through the slab
    float3 start = s0 - dir * span;
    float plate;
    // fixed steps, then halving
    const int Steps = 24;
    float tPrev = 0, tHit = -1;
    [loop] for (int i = 1; i <= Steps; i++)
    {
        float tt = span * i / Steps;
        float3 p = start + dir * tt;
        float above = top - down * tt;
        float h = RELIEF_AT(p) * lower;
        if (h >= above) { tHit = tt; break; }
        tPrev = tt;
    }
    if (tHit < 0) return hit;
    float t0 = tPrev, t1 = tHit;
    [unroll] for (int j = 0; j < 6; j++)
    {
        float tm = (t0 + t1) * 0.5;
        float h = RELIEF_AT(start + dir * tm) * lower;
        if (h >= top - down * tm) t1 = tm; else t0 = tm;
    }
    float3 p = start + dir * t1;
    float above = top - down * t1;
    float hHere = ReliefHeight(f, p - n * dot(p - s0, n), fill, plate) * lower;
    hit.station = p - n * (dot(p - s0, n) - hHere);
    hit.height = hHere;
    hit.plate = plate;
    // A side if the ray met the plate below its top: the side faces the way the height falls (probed across the
    // two course axes; a diagonal cut's side comes out diagonal).
    if (hHere > above + 0.003)
    {
        hit.wall = normalize(-(dir - n * dot(dir, n)));
        float3 eu = ReliefAxis(f.ua), ev = ReliefAxis(f.va);
        float du = RELIEF_AT(p - eu * 0.01) - RELIEF_AT(p + eu * 0.01);
        float dv = RELIEF_AT(p - ev * 0.01) - RELIEF_AT(p + ev * 0.01);
        float3 g = eu * du + ev * dv;
        if (dot(g, g) > 1e-8) hit.wall = normalize(g);
        hit.station = p;
        hit.height = above;
    }
    // Toward the sun: anything higher on the way out within the slab shadows the hit.
    float up = dot(toSun, n);
    if (up > 1e-3)
    {
        float reach = (top - hit.height) / up;
        float3 from = hit.station + n * 0.003;
        [loop] for (int s = 1; s <= 12; s++)
        {
            float tt = reach * s / 12;
            float3 q = from + toSun * tt;
            if (RELIEF_AT(q) * lower > hit.height + up * tt + 0.003) { hit.shadow = 0; break; }
        }
    }
    #undef RELIEF_AT
    return hit;
}

#endif
