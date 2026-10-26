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
float _PSXWetWater;     // 0..1 the open road's water (rain, meltwater, black ice)
float _PSXWetDamp;      // 0..1 the hour's art-licence damp (clear nights), everywhere
float _PSXRoadSnow;     // 0..1 snow lying on the road
float _PSXRoadSlush;    // 0..1 treated snow churned to slush on the road
float _PSXRoadSnowFresh; // 1 while snow is falling on it (fresh), 0 = packed under traffic

#define PATCH_SCALE        0.0263 // drying-patch cells about 38 m: a stretch, not a puddle
#define PATCH_LO           0.25
#define PATCH_HI           0.75
#define PATCH_SPAN         0.85   // a deck at amount a is covered where patch > 1 - a/SPAN..
#define PATCH_EDGE         0.15   //   ..with this soft an edge

// THE COVER, TEXTURED (2026-10-26, the owner from a snowy NoDa start: "The
// roads look terrible during snow. I thought it was broken textures."). The
// first cut was hard-edged value noise at 1-5 cycles a metre, white or
// grey-brown on black wet tarmac: under the PSX dither it read as broken
// texture. Now the cover comes off the owner's pack (Resources/RoadCover.png,
// tools/roadcover/make_cover.py: R fresh snow's grain = PSX Textures v3.1
// snow_2, G slush = snow_melting_1, B packed snow = snow_1, each band-passed
// luminance at mean 0.5; A = snow_melting_1's broad shapes, equalised), laid
// on the WORLD's XZ - so the colour is the shader's (snow bright but not
// lit-up white, slush a matte grey-brown) and the grain is the pack's:
//   EXTENT: the tyres' patch share (PSXLocalShare, untouched: RoadSurface.cs
//     reads the same), broken where it runs out into soft-edged gaps by the
//     sheet's A field at two scales (COVER_FIELD_*); a covered stretch is
//     whole, and only its edge thins. The gaps show the wet road, darkened
//     and barely mirroring (PSXWetAmount: COVER_GAP_MIRROR).
//   GRAIN: the colour channel at two octaves (COVER_FINE_M and x COVER_COARSE),
//     low contrast, faded to its mean with distance by its own mips.
// No wheel tracks: a road's U runs over its painter's layout, not across its
// lanes (CityMeshes.EmitRibbon draws paint columns at fixed U), so nothing
// in the mesh says where a lane's wheels run.
// Linear albedo: Turf_Snow (the snow dress's ground) is 0.72; a road's snow is
// a little greyer, compacted and grimy where it thins; slush is a wet, grimy
// grey-brown at about a third of that (the first cut's 0.56 read as snow).
// THE COVER MUST LOOK LIKE SNOW (2026-10-26, the owner's snowy Chimney Rock:
// the road read "like sand/ice"). Under a warm key and the warm grade a
// neutral 0.55 went beige; snow in shade is BLUE (it is lit by the sky), so
// the snow colours lean cool and the slush keeps its brown only faintly:
//   FRESH  (snow falling, _PSXRoadSnowFresh 1): white-grey, cool, fine grain;
//   PACKED (after the plough, traffic on it): grey-white, its coarse grain
//          darkened into compacted streaks;
//   SLUSH: wet grey-brown lumps in darker watery churn.
// (Measured on the Blue Ridge frames, weather3: fresh road snow came out as
// bright as the verge's snow and a shade cooler than it under the stage's
// warm grade; the city's reads neutral-cool white.)
#define ROAD_SNOW_COLOR    float3(0.59, 0.63, 0.71)   // fresh snow on a road (a touch under the verge's), cool
#define ROAD_SNOW_PACKED   float3(0.44, 0.46, 0.50)   // packed under traffic: grey-white
#define ROAD_SNOW_EDGE     float3(0.30, 0.31, 0.33)   // where it thins: compacted, grimy
#define ROAD_SLUSH_LIGHT   float3(0.32, 0.318, 0.305) // slush lumps: ice in it, grey with grime
#define ROAD_SLUSH_DARK    float3(0.115, 0.112, 0.106) // churned, watery slush between them
#define COVER_PACKED_STREAK 0.45  // how dark packed snow's compacted streaks go (only ever darker:
                                  //   the sheet's B is sd 0.14 about 0.5, so its darkest tenth ~17%)
#define COVER_SLUSH_WATERY 0.45   // the watery churn is half see-through: the wet road under it shows
#define COVER_FINE_M       2.6    // the grain's repeat, m
#define COVER_COARSE       0.29   // and a second octave this many times as fine (~9 m), offset
#define COVER_FIELD_M      17.0   // the extent field's repeat, m ...
#define COVER_FIELD_M2     43.0   //   ...and a second, turned, so neither repeat shows
#define COVER_FIELD_LOD    2.0    // its blobs are tens of texels: a fixed mip, safe in any branch
#define COVER_EDGE         0.30   // how soft a gap's edge is, in the field's units
#define COVER_SNOW_GRAIN   0.22   // the grain's contrast on fresh snow (x (2t - 1), each octave)
#define COVER_SLUSH_LUMPS  1.6    // how far the slush tile's lumps push dark..light
#define COVER_GAP_MIRROR   0.6    // in a slushy stretch the gaps' water mirrors 1 - this of its own
sampler2D _PSXRoadCoverTex;
float _PSXRoadCoverOn;            // 1 once PSXGlobals bound the sheet

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

// The extent field here, 0..1 and about flat in distribution (the sheet's A
// at two scales, the second turned). 0.5 everywhere without the sheet.
float PSXCoverField(float2 xz)
{
    if (_PSXRoadCoverOn < 0.5) return 0.5;
    float2 q = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
    float a = tex2Dlod(_PSXRoadCoverTex, float4(xz / COVER_FIELD_M + float2(0.13, 0.71), 0.0, COVER_FIELD_LOD)).a;
    float b = tex2Dlod(_PSXRoadCoverTex, float4(q / COVER_FIELD_M2 + float2(0.52, 0.29), 0.0, COVER_FIELD_LOD)).a;
    // the mean of two flat fields is a triangle: stretched back toward flat
    return saturate(0.5 + (a * 0.62 + b * 0.38 - 0.5) * 1.55);
}

// How much of THIS spot a deck's share s covers, 0..1: ALL of a patch the
// tyres call covered, nothing where they call it bare, and between, the
// field's soft-edged blobs - the cover thins where the field is low first.
float PSXCoverOf(float s, float field)
{
    float c = saturate((s - (1.0 - field) * (1.0 - COVER_EDGE)) / COVER_EDGE);
    return c * c * (3.0 - 2.0 * c);
}

// How much of the road is under SNOW here, 0..1 - only an up-facing material
// that can be wet (roads, pavements, kerbs: wetMask is its _Wet). Same
// patches as the tyres.
float PSXRoadSnowAt(float wetMask, float3 N, float3 wpos)
{
    if (_PSXWetPatchOn < 0.5 || _PSXRoadSnow <= WET_MIN || wetMask <= WET_MIN) return 0.0;
    float up = saturate(N.y * WET_UP_GAIN - WET_UP_BIAS);
    float s = PSXLocalShare(_PSXRoadSnow, PSXWetPatch(wpos.xz));
    return wetMask * up * PSXCoverOf(s, PSXCoverField(wpos.xz));
}

// How much of the road is under SLUSH here, 0..1 (owner, 2026-10-25: "slush
// does not reflect like this"). Treated snow churned with brine is MATTE and
// grey-brown; where it runs out the wet road shows, dark, and barely mirrors.
float PSXRoadSlushAt(float wetMask, float3 N, float3 wpos)
{
    if (_PSXWetPatchOn < 0.5 || _PSXRoadSlush <= WET_MIN || wetMask <= WET_MIN) return 0.0;
    float up = saturate(N.y * WET_UP_GAIN - WET_UP_BIAS);
    float s = PSXLocalShare(_PSXRoadSlush, PSXWetPatch(wpos.xz));
    return wetMask * up * PSXCoverOf(s, PSXCoverField(wpos.xz + 211.0));
}

// Snow and slush are not water: whatever they cover mirrors nothing (both
// passes - PSX/Lit's own sky glint and the PSXWetSSR world - go through
// PSXWetAmount, so they agree).
float PSXRoadCoverAt(float3 N, float3 wpos)
{
    return max(PSXRoadSnowAt(1.0, N, wpos), PSXRoadSlushAt(1.0, N, wpos));
}

// How much of its water a pixel MIRRORS, after it has darkened the albedo
// (both passes multiply PSXWetAmount by it, so they agree): a slushy
// stretch's gaps are wet, dark and churned and hardly mirror. Black ice and
// plain water keep the whole gloss.
float PSXWetGloss(float3 wpos)
{
    if (_PSXWetPatchOn < 0.5 || _PSXRoadSlush <= WET_MIN) return 1.0;
    return 1.0 - COVER_GAP_MIRROR * PSXLocalShare(_PSXRoadSlush, PSXWetPatch(wpos.xz));
}

// THE COVER'S COLOUR over the albedo (PSX/Lit, after the wet darkening):
// snow and slush in their extents, the pack's grain in each. dWx/dWy: the
// world position's screen derivatives, taken outside any branch.
float3 PSXRoadCoverAlbedo(float3 albedo, float wetMask, float3 N, float3 wpos, float3 dWx, float3 dWy)
{
    float cSnow = PSXRoadSnowAt(wetMask, N, wpos);
    float cSlush = PSXRoadSlushAt(wetMask, N, wpos);
    if (cSnow + cSlush <= 0.0) return albedo;
    float4 g = float4(0.5, 0.5, 0.5, 0.5), h = float4(0.5, 0.5, 0.5, 0.5);
    if (_PSXRoadCoverOn > 0.5)
    {
        float fs = 1.0 / COVER_FINE_M, cs = fs * COVER_COARSE;
        g = tex2Dgrad(_PSXRoadCoverTex, wpos.xz * fs, dWx.xz * fs, dWy.xz * fs);
        h = tex2Dgrad(_PSXRoadCoverTex, wpos.xz * cs + float2(0.37, 0.61), dWx.xz * cs, dWy.xz * cs);
    }
    if (cSnow > 0.0)
    {
        // FRESH: the fresh tile's fine grain over the packed tile's broad
        // streaks, both low contrast. PACKED: the fine grain fainter, the
        // broad streaks only ever DARKER (compacted tracks of grime, never
        // bright blotches). Greyer and grimier where it thins.
        float fresh = saturate(_PSXRoadSnowFresh);
        float kFresh = 1.0 + COVER_SNOW_GRAIN * ((2.0 * g.r - 1.0) * 0.75 + (2.0 * h.b - 1.0) * 0.5);
        float kPacked = 1.0 + COVER_SNOW_GRAIN * 0.5 * (2.0 * g.r - 1.0) - COVER_PACKED_STREAK * saturate(1.0 - 2.0 * h.b);
        float3 top = lerp(ROAD_SNOW_PACKED * kPacked, ROAD_SNOW_COLOR * kFresh, fresh);
        float3 snow = lerp(ROAD_SNOW_EDGE * kPacked, top, saturate(cSnow * 2.5 - 1.2));
        albedo = lerp(albedo, snow, cSnow);
    }
    if (cSlush > 0.0)
    {
        // the slush tile's own lumps (light, solid) in its watery churn
        // (dark, half see-through to the wet road and its paint)
        float lumps = saturate(0.5 + COVER_SLUSH_LUMPS * ((g.g - 0.5) * 0.65 + (h.g - 0.5) * 0.35));
        float3 slush = lerp(ROAD_SLUSH_DARK, ROAD_SLUSH_LIGHT, lumps);
        albedo = lerp(albedo, slush, cSlush * lerp(COVER_SLUSH_WATERY, 1.0, lumps));
    }
    return albedo;
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
        w *= 1.0 - PSXRoadCoverAt(N, wpos);
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
