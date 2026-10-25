// THE WET ROAD's numbers and its three measurements - how wet a pixel is,
// how much it mirrors, and the sky it mirrors - shared by PSX/Lit's two
// passes: the surface itself (the NIGHT PASS in PSXLit.shader's header) and
// the world in the water (PSXWetSSR.cginc, the "PSXWetSSR" pass). ONE copy, so
// the second pass replaces exactly the sky the first one laid down: two lists
// of constants that must agree, will not.
//
// Include it after the fog/ambient/light uniforms PSXSkyReflect.cginc reads
// (_PSXFogColor, _PSXAmbient, _PSXLightColor) and after PSXTone.cginc.
#ifndef PSX_WET_INCLUDED
#define PSX_WET_INCLUDED

// THE WET ROAD (the NIGHT PASS in the header). Like the car
// paint's, the look lives in these #defines, not in materials,
// so tuning it never means rebuilding a scene. Linear-space.
#define WET_MIN            0.001  // below this a surface is dry: no puddles, no mirror, no spec work
#define WET_UP_GAIN        4.0    // only faces that look UP get wet: saturate(N.y * GAIN - BIAS)
#define WET_UP_BIAS        2.8    //   is 0 at 45 degrees of slope and full by 18, so a wall, a
                                  //   kerb's face and a deck's soffit stay dry under a wet material
#define PUDDLE_SCALE       0.28   // puddle noise frequency on world XZ: cells about 3.6 m
#define PUDDLE_LO          0.30   // contrast on the two-octave noise, so it reads as puddles on
#define PUDDLE_HI          0.70   //   damp tarmac rather than as an even grey film
#define PUDDLE_MIN         0.45   // how wet the driest patch is, as a share of the hour's wet
#define PUDDLE_FADE_M      60.0   // beyond this the pattern fades to its mean: a 3.6 m cell is
#define PUDDLE_FAR         0.50   //   under a pixel out there at 240 lines, and would shimmer
#define WET_DARKEN         0.32   // wet asphalt is darker: albedo x (1 - this x wet)
#define WET_F0             0.03   // water's reflectance face-on (Schlick): the road is a mirror
                                  //   only at the grazing angle a chase camera sees it at
#define WET_SKY_LOD        1.5    // how soft the sky is in the water (the paint's glass value)
#define WET_SKY            1.0    // the sky's weight in it
#define WET_LAMP_POW       80.0   // a street lamp's streak down the road: Blinn-Phong on a flat
#define WET_LAMP_GAIN      1.6    //   plane seen at a grazing angle stretches TOWARD the eye by
                                  //   itself, which is the streak - no anisotropy needed
#define WET_HEAD_POW       60.0   // an oncoming low beam's streak
#define WET_HEAD_GAIN      0.9
#define WET_SPEC_BASE      0.25   // the streaks' floor under the fresnel: a streak is there
                                  //   face-on too, only weaker
// THE LAND BAND, for a road. PSXSkyReflect.cginc's defaults are
// the car paint's (land up to ten degrees), and a road seen from a
// chase camera reflects at R.y 0.03-0.15 - every bit of it inside
// that band, so the wet road would mirror fog x 0.4 instead of the
// blue dusk or the sodium skyglow it is there to show. The land
// stops at the horizon here.
#define LAND_TOP           0.0
#define LAND_SOFT          0.03
#define LAND_REFLECT       0.8
#define GROUND_REFLECT     0.25

// A sine-free hash (Dave Hoskins' hash12): sin() of a large world
// coordinate loses its fraction on a mobile GPU, this does not.
float PSXLitHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Value noise: a hash per lattice corner, smoothly blended.
float PSXLitNoise(float2 p)
{
    float2 c = floor(p);
    float2 f = p - c;
    float2 s = f * f * (3.0 - 2.0 * f);
    float a = PSXLitHash(c);
    float b = PSXLitHash(c + float2(1.0, 0.0));
    float d = PSXLitHash(c + float2(0.0, 1.0));
    float e = PSXLitHash(c + float2(1.0, 1.0));
    return lerp(lerp(a, b, s.x), lerp(d, e, s.x), s.y);
}

// The hour's sky (the _PSXSky* globals and PSXSkyIn). AFTER the
// fog/ambient/light uniforms it reads, and after the band above.
#include "PSXSkyReflect.cginc"

// THE WET ROAD, one: how wet THIS pixel is - only if it looks up, more
// in the puddles. wetMat is the material's _Wet times the hour's
// _PSXWetness. 0 on a dry pixel; above 0 (never below WET_MIN x
// PUDDLE_MIN of it) on a wet one.
//
// THE WEATHER'S PATCHES (2026-10-25, weather layers). The road's water and
// snow are not one sheet: low spots and shaded stretches keep them longest,
// so a drying road (or a treated one after snow) is wet in some places and
// dry in others - and the TYRES read the very same mask (RoadSurface.cs ports
// PSXWetPatch and PSXLocalShare line for line; keep the two in step). Set by
// PSXGlobals from TimeOfDay.Apply; _PSXWetPatchOn 0 (any scene or tool that
// never set it) keeps _PSXWetness the one uniform film it always was.
float _PSXWetPatchOn;   // 1 = the layered weather drives the road
float _PSXWetWater;     // 0..1 the open road's water (rain, meltwater, slush, ice)
float _PSXWetDamp;      // 0..1 the hour's art-licence damp (clear nights), everywhere
float _PSXRoadSnow;     // 0..1 snow (and a share of slush) lying on the road

#define PATCH_SCALE        0.0263 // drying-patch cells about 38 m: a stretch, not a puddle
#define PATCH_LO           0.25
#define PATCH_HI           0.75
#define PATCH_SPAN         0.85   // a deck at amount a is covered where patch > 1 - a/SPAN..
#define PATCH_EDGE         0.15   //   ..with this soft an edge
#define ROAD_SNOW_COLOR    float3(0.78, 0.80, 0.84)

float PSXWetPatch(float2 xz)
{
    float2 p = xz * PATCH_SCALE;
    float n = PSXLitNoise(p + 101.3) * 0.65 + PSXLitNoise(p * 2.03 + 47.7) * 0.35;
    return smoothstep(PATCH_LO, PATCH_HI, n);
}

float PSXLocalShare(float amount, float patch)
{
    return saturate((amount - PATCH_SPAN * (1.0 - patch)) / PATCH_EDGE);
}

// How white the road is with snow here, 0..1 - only an up-facing material
// that can be wet (roads, pavements, kerbs: wetMask is its _Wet).
float PSXRoadSnowAt(float wetMask, float3 N, float3 wpos)
{
    if (_PSXWetPatchOn < 0.5 || _PSXRoadSnow <= WET_MIN || wetMask <= WET_MIN) return 0.0;
    float up = saturate(N.y * WET_UP_GAIN - WET_UP_BIAS);
    float s = PSXLocalShare(_PSXRoadSnow, PSXWetPatch(wpos.xz));
    // Tyre tracks and drifts inside a covered patch.
    float grain = PSXLitNoise(wpos.xz * 0.9 + 3.7);
    return wetMask * up * s * lerp(0.55, 1.0, grain);
}

float PSXWetAmount(float wetMat, float3 N, float3 wpos, float eyeDist)
{
    float w = wetMat * saturate(N.y * WET_UP_GAIN - WET_UP_BIAS);
    if (w > WET_MIN && _PSXWetPatchOn > 0.5)
    {
        // wetMat is _Wet x _PSXWetness, and _PSXWetness is the larger of the
        // two below: rescale to THIS spot's share.
        float g = max(_PSXWetDamp, PSXLocalShare(_PSXWetWater, PSXWetPatch(wpos.xz)));
        w *= g / max(_PSXWetness, 1e-4);
    }
    if (w > WET_MIN)
    {
        float2 p = wpos.xz * PUDDLE_SCALE;
        float n = PSXLitNoise(p) * 0.65 + PSXLitNoise(p * 2.03 + 17.17) * 0.35;
        float puddle = smoothstep(PUDDLE_LO, PUDDLE_HI, n);
        puddle = lerp(puddle, PUDDLE_FAR, saturate(eyeDist / PUDDLE_FADE_M));
        w *= lerp(PUDDLE_MIN, 1.0, puddle);
    }
    else w = 0.0;
    return w;
}

// How much of what it faces the water mirrors: Schlick from WET_F0.
float PSXWetFresnel(float3 N, float3 V)
{
    return WET_F0 + (1.0 - WET_F0) * pow(1.0 - saturate(dot(N, V)), 5.0);
}

// The sky in the water: the hour's own sky through the paint's lookup,
// adapted, never exposed (C3). adapt = PSXAdaptGain().
float3 PSXWetSky(float3 R, float adapt)
{
    return PSXSkyIn(R, WET_SKY_LOD) * (WET_SKY * adapt);
}

#endif
