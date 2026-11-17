// RAIN 2 ON THE GROUND (2026-11-16; the owner: "Splashing on the ground, how
// it pools on the road", and in the same breath "we need to start focusing
// HEAVILY on performance").
//
// Until now a wet road was ONE film with a soft 3.6 m noise in it: no place
// where water STANDS, nothing that says rain is landing on it. This file adds
// the two things a road in the rain shows, per wet pixel, with no texture but
// one tiny mask and nothing baked:
//
//   POOLS. Standing water where a road can hold it. A surface sheds water down
//     its slope (the roads are crowned and banked now, and the normal carries
//     that), so the steeper the pixel the less stands on it: a flat lot or a
//     sag fills first, a 2 % crossfall holds wheel-path dips, a 6 % bank or
//     grade holds nothing. The dips themselves are two octaves of world noise
//     (about 5 m and 1.6 m: a wheel-path dip, a patch joint), and the day's
//     standing water (_PSXRain2.z, the weather's own road water) raises the
//     water line through them - so pools grow as it rains and are the last
//     thing to dry. A pool is a full mirror; the film round it mirrors R2_FILM
//     of that. The city's gutter pans (its structure mesh's own atlas band,
//     named per renderer in _PoolBand by WetReflections.Tag) run full.
//   RINGS. Each drop that lands throws a ring: one drop a cell, a cell a
//     hash, the ring a single wave travelling out from where it fell. The
//     wave tilts the WATER's normal (_PSXSpecBend: the lamps' and beams'
//     glints, the sky and the world in the water - never the road's own
//     light), within R2_RING_FAR of the lens, more of them the harder it
//     rains, none where the cover mask says there is a deck or a roof
//     overhead (Rain2Ground probes the ground round the camera and keeps a
//     64 m world-aligned mask of it).
//
// Both passes of PSX/Lit call PSXRain2At first (the surface, and the world in
// the water), so the mirror the second pass trades in is the one the first
// laid down. The C# port of the pool (the tyres' puddle hit) is
// Rain2.PoolAt - keep the two in step.
//
// _PSXRain2 = (on, rain falling now 0..1, standing water 0..1, ring layers
// 0/1/2). All zero - every scene and tool that never heard of this, and
// PSX_RAIN2=0 - and nothing here is evaluated: the picture is the old one.
#ifndef PSX_RAIN2_INCLUDED
#define PSX_RAIN2_INCLUDED

#include "PSXSpecBend.cginc"

float4 _PSXRain2;
float4 _PSXRain2Cover;          // x: world metres -> mask uv; z: 1 = the mask is bound
sampler2D _PSXRain2CoverTex;    // R: 1 open sky .. 0 under a deck or a roof
float4 _PoolBand;               // per renderer: (v from, v to, 1 = on) - the gutter pan's atlas band

#define R2_FILM          0.60   // a film's mirror, as a share of standing water's
#define R2_SLOPE_LO      0.008  // tilt (rise over run) where a surface starts to shed its pools..
#define R2_SLOPE_HI      0.070  //   ..and where it holds none
#define R2_SHED          0.40   // how far a shedding surface lifts the water line (noise units)
#define R2_DIP_A         0.19   // the dips: cells about 5 m..
#define R2_DIP_B         0.61   //   ..and 1.6 m
#define R2_LINE_DRY      0.82   // the water line through the dips with almost none standing..
#define R2_LINE_FULL     0.54   //   ..and in a downpour (about 38 % of a flat lot, 27 % of a 2 % crossfall)
#define R2_EDGE          0.035  // a pool's edge, in noise units: crisp, it is a water line
#define R2_POOL_FADE_M   55.0   // beyond this the pools fade to their mean share over..
#define R2_POOL_FADE_SPAN 70.0  //   ..this many metres: a 1.6 m dip is under a pixel out there
#define R2_RING_FAR      30.0   // rings are drawn within this of the lens (fading from 55 % of it)
#define R2_RING_CELL_A   0.34   // one drop a cell: the cells' size, m (a ring grows to 0.28 of it)..
#define R2_RING_CELL_B   0.53   //   ..and the second layer's, turned, so neither grid shows
#define R2_RING_RATE     2.3    // a cell's drops a second in a downpour
#define R2_RING_W        0.075  // half the ring's wave, in cells
#define R2_RING_AMP      0.11   // the wave's tilt of the water's normal
#define R2_RING_FILM     0.45   // ..its share on the film (a pool is deep enough for the whole wave)
#define R2_GUTTER_FLOW   0.035  // the running gutter's own chop

// What PSXRain2At found here, for PSXWetAmount (PSXWet.cginc).
static float PSXR2Pool = 0.0;

float4 PSXR2Hash4(float2 p)
{
    float4 p4 = frac(float4(p.xyxy) * float4(0.1031, 0.1030, 0.0973, 0.1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

// One layer of rings on the plane `xz`: the water's slope there (its normal's
// tilt, unscaled). One drop a cell at a time, where and when the cell's hash
// says; `density` 0..1 is the share of cells that are live.
float2 PSXR2Rings(float2 xz, float cell, float density, float seed)
{
    float2 g = xz / cell;
    float2 id = floor(g);
    float4 h = PSXR2Hash4(id + seed);
    float T = _Time.y * R2_RING_RATE * (0.75 + 0.5 * h.z) + h.w * 23.0;
    float cyc = floor(T);
    float life = T - cyc;
    float4 k = PSXR2Hash4(id + cyc * 7.31 + seed + 5.0);
    // the drop lands in the cell's middle 40 %: its ring (0.28 of a cell at
    // most) never reaches the cell's edge, so no neighbour is ever read
    float2 d = (g - id) - (0.3 + 0.4 * k.xy);
    float r = length(d);
    float x = clamp((r - life * 0.28) / R2_RING_W, -1.0, 1.0);
    float wave = sin(x * 3.14159265);
    float fade = (1.0 - life) * (1.0 - life);
    return d * (wave * fade * step(k.z, density) / max(r, 0.02));
}

// Call once a fragment, before the lamp tables and PSXWetAmount. wetMat: the
// material's _Wet x the hour's wetness; uv: the mesh's own (the gutter band).
void PSXRain2At(float wetMat, float3 wpos, float3 N, float eyeDist, float2 uv)
{
    PSXR2Pool = 0.0;
    _PSXSpecBend = float3(0.0, 0.0, 0.0);
    float level = _PSXRain2.z;
    if (_PSXRain2.x < 0.5 || wetMat <= 0.001) return;
    if (N.y * 4.0 - 2.8 <= 0.0) return;         // PSXWet's own rule: only faces that look up are wet

    // 1 - where the water stands
    float2 p = wpos.xz;
    float pool = 0.0;
    float2 chop = float2(0.0, 0.0);
    if (level > 0.001)
    {
        float slope = length(N.xz) / max(N.y, 0.25);
        float shed = smoothstep(R2_SLOPE_LO, R2_SLOPE_HI, slope);
        float n = PSXLitNoise(p * R2_DIP_A + 31.7) * 0.62 + PSXLitNoise(p * R2_DIP_B + 7.3) * 0.38;
        float waterLine = lerp(R2_LINE_DRY, R2_LINE_FULL, level) + shed * R2_SHED;
        pool = smoothstep(waterLine, waterLine + R2_EDGE, n);
        float farT = saturate((eyeDist - R2_POOL_FADE_M) / R2_POOL_FADE_SPAN);
        pool = lerp(pool, saturate(0.5 + (0.5 - waterLine) * 2.4), farT);
        // the gutter pan runs full, and its water is moving
        if (_PoolBand.z > 0.5 && uv.y > _PoolBand.x && uv.y < _PoolBand.y)
        {
            pool = max(pool, saturate(level * 2.5));
            float2 q = float2(p.x + p.y, p.x - p.y) * 2.1 + _Time.y * float2(1.9, 1.3);
            chop = (float2(PSXLitNoise(q), PSXLitNoise(q + 31.3)) - 0.5) * (R2_GUTTER_FLOW * saturate(1.0 - eyeDist / 40.0));
        }
    }
    PSXR2Pool = pool;

    // 2 - the rain landing on it
    float rain = _PSXRain2.y;
    float2 bend = chop;
    if (rain > 0.01 && _PSXRain2.w > 0.5 && eyeDist < R2_RING_FAR)
    {
        float open = 1.0;
        if (_PSXRain2Cover.z > 0.5) open = tex2Dlod(_PSXRain2CoverTex, float4(p * _PSXRain2Cover.x, 0.0, 0.0)).r;
        float amp = R2_RING_AMP * open * (1.0 - smoothstep(R2_RING_FAR * 0.55, R2_RING_FAR, eyeDist))
                  * lerp(R2_RING_FILM, 1.0, pool);
        if (amp > 0.001)
        {
            float2 rg = PSXR2Rings(p, R2_RING_CELL_A, rain * 0.92, 3.1);
            if (_PSXRain2.w > 1.5)
            {
                float2 q2 = float2(p.x * 0.8 - p.y * 0.6, p.x * 0.6 + p.y * 0.8);
                float2 r2 = PSXR2Rings(q2, R2_RING_CELL_B, rain * 0.80, 11.7);
                rg += float2(r2.x * 0.8 + r2.y * 0.6, r2.y * 0.8 - r2.x * 0.6);
            }
            bend += rg * amp;
        }
    }
    _PSXSpecBend = float3(bend.x, 0.0, bend.y);
}

// The water's normal here: the surface's, tilted by the rings.
float3 PSXRain2Normal(float3 N)
{
    return normalize(N + _PSXSpecBend);
}

#endif
