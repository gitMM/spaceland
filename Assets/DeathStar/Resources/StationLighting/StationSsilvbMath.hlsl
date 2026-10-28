// Adapted from C:/Transhuman/Button, 2026-10-28.
#ifndef STATION_SSILVB_MATH_INCLUDED
#define STATION_SSILVB_MATH_INCLUDED
// Integral from 0 to theta of cos(theta-N) * abs(sin(theta)).
// Valid over the hemisphere centred on the projected normal angle N.
float SsilPrimitive(float theta, float cosN, float sinN)
{
    float s,c; sincos(theta,s,c);
    return (theta < 0 ? -1 : 1) * .5 * (cosN*s*s + sinN*(theta-s*c));
}
uint SsilSectorMask(float lo, float hi)
{
    // Sector centres: avoid expanding every sub-sector sliver into a whole bit.
    // Explicit bounds avoid undefined shifts by 32 on different graphics APIs.
    uint first=(uint)clamp(ceil(saturate(lo)*32-.5),0,32);
    uint end=(uint)clamp(ceil(saturate(hi)*32-.5),0,32);
    if(end<=first || first>=32)return 0u;
    uint lower=first==0?0u:(0xffffffffu >> (32-first));
    uint upper=end==32?0xffffffffu:(0xffffffffu >> (32-end));
    return upper & ~lower;
}
#endif
