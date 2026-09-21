// THE SUN'S SHADOW MAP, the receiving side - the open sky overhead - and the
// sun in the haze (the DAY PASS, 2026-09-21).
//
// The owner, with five daylight frames of Forza Horizon: improved lighting
// "during morning, day, and afternoon. Basically, when headlights and street
// lights are not the major driving factors of the lighting." Those frames were
// MEASURED (tools/day/day_stats.py holds the numbers), and what daylight is
// made of in them is four things this renderer had none of:
//
//   * THINGS CAST SHADOWS. The road in the sun against the road in a car's
//     shadow is 3.5 to 1 in linear light, and the shadow is BLUE - it is lit
//     by the sky alone - where the sunlit tarmac is neutral-warm. Here the
//     sun was a per-vertex lambert and nothing else: a road beside a tower or
//     under a bridge was lit exactly like one in a field.
//   * UNDER A ROOF THERE IS NO SKY EITHER. Deep in a tunnel the wall is 24
//     times darker than the day outside; under a gallery the road between
//     the shafts of light is 18 times darker than in them. A shadow map from
//     the sun cannot say that - shade from a tower still has the whole sky
//     over it - so there is a SECOND, smaller map, looking straight down,
//     that says whether anything is overhead, and the AMBIENT answers to it.
//   * UNDER A TREE THE LIGHT IS DAPPLED. The avenue frame's road is coins of
//     sun on shade. The map keeps TWO depths a texel - the first solid thing
//     and the first leaf - and where only leaves are in the way the sun is
//     not blocked but PATTERNED, by a noise fixed to the world, so the coins
//     stay put on the road as the car drives over them.
//   * THE HAZE IS BRIGHTEST TOWARD THE SUN. The far hills under a low sun
//     measure 0.93 where the near ones are 0.45 and the sky 40 degrees round
//     is 0.53: distance does not fade to one colour, it fades to a colour
//     that depends on which way you are looking. PSXFogTowardSun below.
//
// (The fifth thing - the sun having a colour and a face square-on to it being
// brighter than one half turned away - is PSX/Lit's shoulder, in that file.)
//
// THE MAPS. SunShadows.cs draws the casters near the camera into two square
// RGBA8 textures through PSX/ShadowCaster, which packs two 16-bit ray depths a
// texel (see that file for why not a depth buffer, and why two): the first
// SOLID surface in r,g and the first LEAF in b,a. The sun's map is read with the four texels round the
// point compared and blended by where the point falls between them: the
// hardware cannot filter a packed number, and an unfiltered map is a staircase
// down the edge of every shadow. The sky's map is read once: it is 35 cm a
// texel under a 240-line frame, and what it draws is the soft edge of "under
// something", which nobody sees the steps of.
//
// EVERY NUMBER IS OFF WHEN NOBODY SET IT. The strengths are 0 in any scene that
// never applied an hour (every interior), and at dusk and at night - and at 0
// each function returns 1 before it reads anything, on a branch that is the
// same for every pixel of a frame.
//
// Include AFTER the including shader has declared _PSXLightDir.
#ifndef PSX_SUN_SHADOW_INCLUDED
#define PSX_SUN_SHADOW_INCLUDED

sampler2D _PSXShadowMap;
sampler2D _PSXSkyMap;
// World -> each map: xy the texel (0..1), z the ray depth (0..1).
float4x4 _PSXShadowMatrix;
float4x4 _PSXSkyMatrix;
// x = strength 0..1 (0 = off), y = the map's resolution in texels,
// z = the normal offset in METRES for a face edge-on to the rays,
// w = the constant depth bias, in ray-depth units.
float4 _PSXShadowParams;
float4 _PSXSkyParams;
// rgb = what the haze gains looking straight at the sun (linear),
// a = how tight that glow is (the exponent). Alpha under 1 = off.
float4 _PSXFogSun;

// How much of a map's rim fades it out, as 1/width: 12.5 is the outer 8%. Past
// the rim there is no information, and no information is lit - but a shadow
// must not END at a line on the road, so it fades to lit instead.
#define PSX_SHADOW_RIM     12.5
// THE DAPPLE under leaves. Two octaves of value noise on world XZ, cells 2.2 m
// and 0.9 m; the sun comes through where the noise is over LO (all of it by
// HI). 0.50 / 0.60 lets it through over about a third of the ground, which is
// what the avenue frame shows. Past FADE_M the pattern is under a pixel at 240
// lines and would shimmer, so it goes to its mean.
#define PSX_DAPPLE_SCALE   0.45
#define PSX_DAPPLE_LO      0.50
#define PSX_DAPPLE_HI      0.60
#define PSX_DAPPLE_MEAN    0.33
#define PSX_DAPPLE_FADE_M  70.0
// What is left of the AMBIENT with a solid roof overhead, and how much of a
// roof a tree's crown is. 0.22: the tunnel frame's 24 to 1 is a camera
// exposed for the dark, and the game does not re-expose - 0.22 of a 0.2
// ambient against a sunlit 1.2 is 27 to 1 in the light, which the film
// grade's lifted floor then shows as about 3 to 1 on the screen.
#define PSX_SKY_ROOFED     0.22
#define PSX_SKY_LEAVES     0.55

// A sine-free hash (Dave Hoskins' hash12) - sin() of a large world coordinate
// loses its fraction on a mobile GPU - and value noise on it. PSX/Lit has the
// same pair for its puddles; these are named apart so both can be included.
float PSXShadeHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float PSXShadeNoise(float2 p)
{
    float2 c = floor(p);
    float2 f = p - c;
    float2 s = f * f * (3.0 - 2.0 * f);
    float a = PSXShadeHash(c);
    float b = PSXShadeHash(c + float2(1.0, 0.0));
    float d = PSXShadeHash(c + float2(0.0, 1.0));
    float e = PSXShadeHash(c + float2(1.0, 1.0));
    return lerp(lerp(a, b, s.x), lerp(d, e, s.x), s.y);
}

// One texel of a map: x = the first SOLID surface's ray depth, y = the first
// LEAF's. (A cleared texel is white and decodes to just past 1: nothing there.)
float2 PSXShadowTexel(sampler2D map, float2 uv)
{
    float4 e = tex2Dlod(map, float4(uv, 0.0, 0.0));
    return float2(dot(e.rg, float2(1.0, 1.0 / 255.0)), dot(e.ba, float2(1.0, 1.0 / 255.0)));
}

// How much sun one texel lets through to depth z: none if something solid is
// in front; else `throughLeaves` if leaves are; else all of it.
float PSXShadowTap(float2 uv, float z, float throughLeaves)
{
    float2 t = PSXShadowTexel(_PSXShadowMap, uv);
    return step(z, t.x) * lerp(throughLeaves, 1.0, step(z, t.y));
}

// 1 = in the sun, 0 = in shadow, for a world point with unit normal N, seen
// from eyeDist metres away. `leaf` is 1 when the RECEIVER is itself a cutout -
// a tree card, a fence - and then leaves do not shade it at all, from either
// map: a crown is two crossed cards standing in each other's shadow, the
// dapple is a pattern on the GROUND (on an upright card it would be stripes),
// and a tree lit the way it always was is the tree the owner signed off.
// Solid things still shade it: a tree behind a tower is in the tower's shadow.
float PSXSunShadow(float3 wpos, float3 N, float eyeDist, float leaf)
{
    float strength = _PSXShadowParams.x;
    if (strength <= 0.001) return 1.0;

    // THE NORMAL OFFSET. One texel of the map covers more and more ray depth
    // the more edge-on a face is to the sun - a road at sunset changes depth
    // by ten texel-widths across one texel - so a plain depth compare shadows
    // a face with itself in stripes ("acne"). Moving the point being tested
    // off the face along its own normal, further the more edge-on it is,
    // lifts it clear of its own texels; a fixed bias big enough to do the
    // same job would float every shadow a metre off its caster.
    float3 L = normalize(_PSXLightDir.xyz);
    float ndl = saturate(dot(N, L));
    float3 p = wpos + N * (_PSXShadowParams.z * sqrt(1.0 - ndl * ndl));

    float3 s = mul(_PSXShadowMatrix, float4(p, 1.0)).xyz;
    float2 fromMid = abs(s.xy - 0.5) * 2.0;
    float rim = saturate((1.0 - max(fromMid.x, fromMid.y)) * PSX_SHADOW_RIM);
    if (rim <= 0.0 || s.z >= 1.0 || s.z <= 0.0) return 1.0;

    // The coins of light, fixed to the world. Worked out whether or not
    // there are leaves overhead: it is two noise lookups, and a branch on a
    // texture read would cost more than it saved.
    float2 q = wpos.xz * PSX_DAPPLE_SCALE;
    float n = PSXShadeNoise(q) * 0.6 + PSXShadeNoise(q * 2.37 + 11.3) * 0.4;
    float dapple = smoothstep(PSX_DAPPLE_LO, PSX_DAPPLE_HI, n);
    dapple = lerp(dapple, PSX_DAPPLE_MEAN, saturate(eyeDist / PSX_DAPPLE_FADE_M));
    dapple = max(dapple, leaf);

    float res = _PSXShadowParams.y;
    float2 t = s.xy * res - 0.5;
    float2 f = frac(t);
    float2 uv0 = (floor(t) + 0.5) / res;
    float step1 = 1.0 / res;
    float z = s.z - _PSXShadowParams.w;
    float s00 = PSXShadowTap(uv0, z, dapple);
    float s10 = PSXShadowTap(uv0 + float2(step1, 0.0), z, dapple);
    float s01 = PSXShadowTap(uv0 + float2(0.0, step1), z, dapple);
    float s11 = PSXShadowTap(uv0 + float2(step1, step1), z, dapple);
    float lit = lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
    return lerp(1.0, lit, strength * rim);
}

// How much of the AMBIENT a world point keeps: 1 under open sky, down to
// PSX_SKY_ROOFED with a roof over it, partway under a tree.
float PSXSkyOpen(float3 wpos, float3 N, float leaf)
{
    float strength = _PSXSkyParams.x;
    if (strength <= 0.001) return 1.0;
    // The rays come straight down, so "edge-on" is how far N is from vertical:
    // a wall is moved out from under its own coping, a road is not moved.
    float3 p = wpos + N * (_PSXSkyParams.z * sqrt(saturate(1.0 - N.y * N.y)));
    float3 s = mul(_PSXSkyMatrix, float4(p, 1.0)).xyz;
    float2 fromMid = abs(s.xy - 0.5) * 2.0;
    float rim = saturate((1.0 - max(fromMid.x, fromMid.y)) * PSX_SHADOW_RIM);
    if (rim <= 0.0 || s.z >= 1.0 || s.z <= 0.0) return 1.0;
    float2 t = PSXShadowTexel(_PSXSkyMap, s.xy);
    float z = s.z - _PSXSkyParams.w;
    float keep = lerp(max(PSX_SKY_LEAVES, leaf), 1.0, step(z, t.y));   // leaves overhead, or not
    keep = lerp(PSX_SKY_ROOFED, keep, step(z, t.x));                   // a roof has the last word
    return lerp(1.0, keep, strength * rim);
}

// The fog colour in the direction the eye is looking: the hour's fog, plus
// the sun's glow in the haze. V is the unit vector FROM the pixel TO the eye,
// so -V is the way the eye looks.
//
// Off (the alpha under 1) returns the fog colour UNTOUCHED, before anything is
// normalised: in a scene with no PSXGlobals the light direction is a zero
// vector, normalize(0) is a NaN, and a NaN times a zero glow is still a NaN.
float3 PSXFogTowardSun(float3 fogColor, float3 V)
{
    if (_PSXFogSun.a < 1.0) return fogColor;
    float toward = saturate(dot(-V, normalize(_PSXLightDir.xyz)));
    return fogColor + _PSXFogSun.rgb * pow(toward, _PSXFogSun.a);
}

#endif
