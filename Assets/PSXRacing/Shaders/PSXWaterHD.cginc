// THE WATER, PS3-PS4 (2026-11-16, the water round). The owner, while a Lake
// Lure / Chimney Rock stage was being built: "A lot of potentially nice race
// tracks around Lake Lure. Would be a good time to update the water visuals as
// well." PSX/Water's PSX_WATER_HD variant (WaterLook.cs turns the keyword on;
// PSX_WATER_HD=0 is the old code path in PSXWater.shader, untouched).
//
// What changed, from the driver's seat:
//   * RIPPLES THAT ARE WAVES. water_ripples.png is a wind-wave height field
//     (a directional spectrum, inverse FFT on a periodic grid, so it tiles by
//     construction), its slopes in RG. Three octaves of it (two on the cheap
//     tiers) run DOWNWIND at the speed real waves of that length run, turned
//     against each other so no tile repeats; the wind is the day's
//     (_PSXWaterWind, from the cloud deck's), and its strength rises and falls
//     in drifting gust patches (the sheet's A field) - glassy calm between
//     ruffled cat's paws on a lake, all of it rough in a blow.
//   * RIVERS RUN. The vertex contract's uv2 is the flow (world x,z, m/s): the
//     ripples and the foam are advected down it (two phases, Vlachos' flow
//     map), the water gets rougher with speed, and colour b (white water)
//     turns it to foam with its own churned normals.
//   * THE WORLD IN IT. On a PC with REFLECTIONS on, the mirrored ray is marched
//     across the frame against the depth (the wet road's march, PSXWetSSR.cginc,
//     here against URP's own _CameraDepthTexture), and what it meets replaces
//     the sky: a calm lake mirrors its ridges and a sound its bridge.
//   * THE BED THROUGH IT. Where the pipeline copies the frame and its depth
//     (the PC asset does, for every camera; the water now draws at the head of
//     the transparents, after both copies), the water is a real volume: the
//     bed seen through it, bent by the ripples, lost channel by channel over
//     the true path (Beer-Lambert), the water's own colour scattered in. The
//     shore is soft (no line where the plane cuts the ground) and foams.
//   * RAIN RINGS while rain falls (_PSXWaterRain).
// Everything else is the old water's: the sky lookup, the swell, the lamps'
// streaks, the moon's path, the shore's swash, the fog, the emitter mask.
//
// PART B (2026-11-17, the review's items 6-17; the owner: "Why hasn't the
// upgraded bodies of water been implemented?"):
//   * NO STATIC (item 6). The march has no per-pixel jitter any more (it
//     flipped hit and miss pixel by pixel, fixed to the screen); each
//     crossing is weighed by how far the surface stands in front of where
//     the ray was (a smooth fade, not a cut), a partial one blends with the
//     next; what the ripple mips average away is roughness (Toksvig: the
//     sheet's slope variance lost at the pixel's footprint, plus the
//     normal's own screen-space variance), so far water is a soft mirror,
//     not a field of single bright pixels; the march and the wobble read
//     less of the ripples as they average away; octave 3 is gone by 60 m.
//   * NO EDGE BARS (item 10). The march ends only at the frame's top (or
//     bottom); across the sides it reads the clamped edge column, and
//     nothing fades toward the sky at the sides.
//   * RAIN ON IT (item 8). The rain rate roughens it at every distance,
//     takes the mirror away, greys it toward the sky and pocks it beyond the
//     rings; the sun's highlight follows the sky's clear share.
//   * RIVERS (item 12): white water only where the mesh says rapids, faded
//     to nothing at the banks (in the vertex: the stage meshes need no
//     rebuild); bank foam on running water capped and broken by the lace.
//   * LAKE LURE (item 11): where the frame's bed is far shallower than the
//     mesh's depth (the 2018 lidar's drawdown floor), the mesh's depth.
//   * Refraction and the mirror's wobble turned into view space (item 16).
#ifndef PSX_WATER_HD_INCLUDED
#define PSX_WATER_HD_INCLUDED

sampler2D _RippleTex;        // RG wave normal, B foam lace, A breakup (linear)
float4 _Absorb;              // per-metre extinction, rgb
float4 _Scatter;             // the water's own colour (in-scatter), rgb
float4 _WaterLook;           // x ripple tile m, y ripple strength, z foam gain, w river roughness per m/s
float4 _PSXWaterTier;        // x frame + depth copies, y screen-space mirror, z third octave (WaterLook.cs)
float4 _PSXWaterWind;        // xy wind toward (world xz, unit), z speed m/s at the surface, w the sky's clear share 0..1
float _PSXWaterRain;         // 0..1 how hard rain is falling on it
float _PSXWaterDebug;        // tools: 1 = r copies on, g mirror hit, b depth/10 m
float4 _PSXDistHaze;         // PSX/Lit's distant haze (the lit city at dusk and night): x amount, y start m, z 1/scale m
sampler2D_float _PSXWaterDepth;        // the depth attachment after the sky (SpeedBlurFeature.WaterDepthPass; no water in it)
sampler2D _CameraOpaqueTexture;        // URP's copy of the frame after the sky (no water in it)

// ---- the look ----
#define HD_GUST_TILE_M     190.0    // gust patches: the breakup field's repeat
#define HD_GUST_MIN        0.18     // the calmest patch keeps this share of the ripple
#define HD_WIND_FULL       6.0      // m/s at which the whole surface is ruffled
#define HD_WIND_CALM       0.35     // share of the ripple left at a dead calm
#define HD_OCT_SCALE       float3(1.0, 0.36, 0.125)   // x _WaterLook.x per octave
#define HD_OCT_TURN        float3(0.0, 0.62, -0.47)   // radians off the wind
#define HD_OCT_GAIN        float3(1.0, 0.62, 0.40)
#define HD_FLOW_PERIOD     1.6      // s, one flow phase
// THE MOTION (part B; the owner on PC49's Lake Lure: "moving very fast for a
// lake. It looked like an ocean"). The sea runs its ripples downwind at their
// waves' phase speed; still water (a lake, the city's) creeps at this share of
// it - less in a light air - in two readings turned this far apart, so it
// works in place; the gust patches cross it at this share of the wind.
#define HD_STILL_DRIFT     0.22
#define HD_STILL_TURN      0.45     // radians either side of the wind
#define HD_GUST_DRIFT      float2(0.18, 0.5)   // x still water, y the sea: m/s per m/s of wind
// Running water: the stage meshes' flow (uv2, lakelure's per-reach speeds,
// 0.15-1.95 m/s, median 1.1) is the reach's MEAN velocity from the bake; the
// surface of a boulder-bed mountain river runs faster than its mean, and the
// Rocky Broad's gorge (2-4 % grade) runs 1.5-2.7 m/s by Manning's: this gain.
#define HD_RIVER_FLOW      1.5
#define HD_REFRACT_M       0.35     // how far the ripples bend the bed (screen share x metres)
#define HD_SHORE_SOFT_M    0.06     // the waterline melts into the ground over this depth
#define HD_FOAM_M          0.55     // shore foam reaches this deep
#define HD_FOAM_TILE_M     3.4      // the lace's repeat
#define HD_FOAM_COLOR      float3(0.80, 0.83, 0.82)
#define HD_SURF_DEPTH_M    3.6      // the open sea's swell breaks shoreward of this depth (the coast
                                    //   bakes keep the seabed 4 m down: this is their whole shelf)
#define HD_FOAM_OPEN       2.5      // on the open coast the shore foam reaches this many times further
#define HD_SKY_LOD_CALM    0.6
#define HD_SKY_LOD_ROUGH   3.0
#define HD_SPEC_CALM       1400.0   // sun/moon highlight power on glassy water...
#define HD_SPEC_ROUGH      220.0    // ...and on a choppy one
#define HD_SPEC_MAX        5.0
#define HD_RAIN_FADE_M     45.0     // rings beyond this are under a pixel...
#define HD_RAIN_POCK       0.16     //   ...where the drops' pocks are a pale sheen (share toward the sky's grey)
#define HD_RAIN_ROUGH      0.25     // the rain's roughness, at every distance
#define HD_RAIN_MIRROR     0.40     // the share of the mirror a downpour takes away (the rest is broken up: HD_RAIN_WOBBLE)
#define HD_RAIN_WOBBLE     2.5      // a downpour breaks the mirror up this many times more (rings, rougher ripples)
#define HD_RAIN_SPARK      0.55     // each drop's ring catches the sky: this share of it on the ring's crest
#define HD_RAIN_GREY       0.10     // how far a downpour greys the water toward the sky (near; more with the pocks far out)
#define HD_SSR_STEPS       24       // (half beyond HD_SSR_FAR_STEPS_M)
#define HD_SSR_FAR_STEPS_M 300.0
#define HD_SSR_REFINE      5
#define HD_SSR_START_M     0.15
#define HD_SSR_MAX_M       1600.0   // a lake mirrors ridges a kilometre off
#define HD_SSR_FADE_FROM   0.80
#define HD_SSR_THICK_M     0.30     // a crossing counts while the surface stands no further in front of the
#define HD_SSR_THICK_K     0.020    //   ray's last step than this (+ this share of its depth), fading out to twice it
#define HD_SSR_EDGE        0.06
#define HD_SSR_SMOOTH      0.30     // the march reads the ripples at this share (a steadier mirror)...
#define HD_SSR_WOBBLE      0.035    //   ...and they wobble what it found by this much of the frame
#define HD_ENV_GAIN        0.85     // the world round the car (CarReflections' panorama) where the mirror finds nothing
#define HD_ENV_LOD         0.5      //   on screen, this much softer than the sky would be
#define HD_SSR_BLUR        0.022    // rough water mirrors its banks BLURRED (this share of the frame at rough 1),
                                    //   never the sky instead: the march's rough cut-off showed the Bat Cave river in
                                    //   rain as a pale sheet (the bright overcast horizon) - the owner's complaint
#define HD_SSR_FRES_ON     float2(0.024, 0.05)  // ...or where the mirror is this faint (looking down into it)
// SPECULAR ANTI-ALIASING (Toksvig). The sheet's slope variance at full
// detail (tools/water/make_ripples.py's sheet, measured: E|s|^2 = 0.0441),
// and the share of it the mips keep, as a ramp in the mip level (measured:
// 0.96 0.83 0.61 0.36 0.15 0.03 at levels 1-6).
#define HD_SHEET_VAR       0.0441
#define HD_SHEET_TEXELS    512.0
#define HD_GEO_AA          0.5      // the normal's own screen-space variance, as roughness...
#define HD_GEO_AA_MAX      0.035    //   ...capped: at 0.09 a grazing river read as fully rough (a blurred sky)
#define HD_SPEC_FLOOR      40.0     // the far glitter path's broadest

struct appdata_hd
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
    float2 flow : TEXCOORD1;     // THE CONTRACT: uv2 = flow velocity, world x,z, m/s
    fixed4 color : COLOR;        // r depth, g open ocean, b white water
};

struct v2f_hd
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 data : TEXCOORD1;     // x depth m (vertex), y open, z white water, w 1 = the mesh carries the contract
    float3 wpos : TEXCOORD2;
    half3 amb : TEXCOORD3;
    half3 sun : TEXCOORD4;
    float4 swell : TEXCOORD5;    // x ocean share, y crest, z ripple share, w fog
    float4 spos : TEXCOORD6;     // screen position
    float2 flow : TEXCOORD7;     // m/s
};

float2 HDWindDir()
{
    float2 w = _PSXWaterWind.xy;
    return dot(w, w) > 1e-4 ? normalize(w) : float2(0.8, 0.6);
}

v2f_hd vertHD(appdata_hd v)
{
    v2f_hd o;
    float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
    // A mesh with no colour stream (the city's lakes, rivers and creeks carry
    // none) reads white: no depth, no ocean, no white water, no flow.
    float has = all(v.color.rgb > 0.996) ? 0.0 : 1.0;
    float depthM = has > 0.5 ? (v.color.r - 0.5) * 2.0 * DEPTH_RANGE_M : DEPTH_RANGE_M;
    float open = has * v.color.g;
    // RIVERS (part B, item 12): the mesh's white water (StageRiver: the
    // reach's whiteness x 0.55..1 across the channel) as RAPIDS only - none
    // under 0.35, all of it by 0.9 - and none at the banks, whose rows stand
    // 0.3 m outside the water (depth <= 0). It used to read as a grey veil
    // over half the Rocky Broad with a white ribbon down both banks.
    float white = has * smoothstep(0.35, 0.9, v.color.b) * saturate(depthM / 0.6);
    float2 flow = has > 0.5 ? v.flow : float2(0.0, 0.0);

    float t = _Time.y;
    float3 disp = 0.0;
    float2 slope = 0.0;
    float ocean = _OceanWaves * open * saturate(depthM / SWELL_SHOAL_M);
    float2 wd = _OceanWaves > 0.5 ? normalize(_WaveDir.xz + float2(1e-4, 0.0)) : HDWindDir();
    if (ocean > 0.001)
    {
        float3 A = SWELL_A * ocean;
        float Q = SWELL_Q / (dot(SWELL_A, 6.2831853 / SWELL_L) * 3.0);
        Gerstner(wpos.xz, Rot(wd, SWELL_SPREAD.x), A.x, SWELL_L.x, Q, t, disp, slope);
        Gerstner(wpos.xz, Rot(wd, SWELL_SPREAD.y), A.y, SWELL_L.y, Q, t * 1.03, disp, slope);
        Gerstner(wpos.xz, Rot(wd, SWELL_SPREAD.z), A.z, SWELL_L.z, Q, t * 0.97, disp, slope);
    }
    // the slow ripple (centimetres), not on running water - and on the SEA
    // only (part B, the owner on PC49's HD Lake Lure: "the current/tide/waves
    // were moving very fast for a lake. It looked like an ocean"): a 7.5 m
    // train running across a mountain reservoir at 3.4 m/s is a swell; a lake
    // or a city river is flat but for its ripples, which are in the pixel
    float still = 1.0 - saturate(length(flow) * 2.0);
    Gerstner(wpos.xz, Rot(wd, 0.9), RIPPLE_A * saturate(depthM) * still * saturate(_OceanWaves), RIPPLE_L, 0.3, t, disp, slope);
    float crest = ocean > 0.001 ? saturate(disp.y / max(dot(SWELL_A, float3(1, 1, 1)) * ocean, 1e-3)) : 0.0;
    wpos += disp;
    float4 clipPos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
    if (_PSXSnap > 0.5 && clipPos.w > 0.0)
    {
        float2 grid = _ScreenParams.xy * 0.5;
        float2 ndc = clipPos.xy / clipPos.w;
        ndc = floor(ndc * grid + 0.5) / grid;
        clipPos.xy = ndc * clipPos.w;
    }
    o.pos = clipPos;
    o.spos = ComputeScreenPos(clipPos);
    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
    o.wpos = wpos;
    o.data = float4(depthM, open, white, has);
    o.flow = flow;
    float ndl = saturate(normalize(_PSXLightDir.xyz).y);
    half3 amb = _PSXSkyAmbient.rgb;
    half3 sunL = _PSXLightColor.rgb * ndl;
    if (_PSXSunModel < 0.5) { amb = saturate(amb + sunL); sunL = half3(0, 0, 0); }
    o.amb = amb;
    o.sun = sunL;
    float dist = length(wpos - _WorldSpaceCameraPos);
    float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
    o.swell = float4(ocean, crest, saturate(depthM), pow(fogT, max(_PSXFogCurve, 1.0)));
    return o;
}

// One octave of the sheet, run downwind and advected down the flow: the
// sheet's slope (n.xy of its normal) in world x,z, and its foam lace.
// d = the octave's own heading; scaleM its repeat; drift its phase speed.
// dxz, dyz: the world position's screen derivatives (taken outside any
// branch, so the finest octave can be skipped on the cheap tiers).
void HDOctave(float2 xz, float2 dxz, float2 dyz, float2 d, float scaleM, float drift, float2 flowMs, float t, float seaK,
              bool soft, out float2 nrm, out float lace, out float2 nrmSoft)
{
    float moving = saturate(length(flowMs) * 3.0);
    // (the sea keeps its one reading, run downwind as before: stillMix 0)
    float stillMix = (1.0 - moving) * (1.0 - seaK);
    // STILL WATER WORKS IN PLACE (part B; the owner on PC49's HD Lake Lure:
    // "the current/tide/waves were moving very fast for a lake. It looked
    // like an ocean"). One reading of the sheet slid rigidly downwind at the
    // phase speed of its waves - a current. Now still water reads it twice,
    // turned +-HD_STILL_TURN off the wind and each creeping its own way, at
    // equal weight: the ripples rise and fade where they are, as a ruffled
    // lake's do. Running water keeps the flow map's two phases (one heading).
    float turn = HD_STILL_TURN * stillMix;
    float2 d0 = Rot(d, turn), d1 = Rot(d, -turn);
    float2 e0 = float2(-d0.y, d0.x), e1 = float2(-d1.y, d1.x);
    float2 uv0 = float2(dot(xz, d0), dot(xz, e0)) / scaleM;
    float2 uv1 = float2(dot(xz, d1), dot(xz, e1)) / scaleM + float2(0.37, 0.61) * stillMix;
    float2 gx0 = float2(dot(dxz, d0), dot(dxz, e0)) / scaleM, gy0 = float2(dot(dyz, d0), dot(dyz, e0)) / scaleM;
    float2 gx1 = float2(dot(dxz, d1), dot(dxz, e1)) / scaleM, gy1 = float2(dot(dyz, d1), dot(dyz, e1)) / scaleM;
    uv0.x -= drift * t / scaleM;
    uv1.x -= drift * t / scaleM;
    // the flow in each reading's frame, tiles per second
    float2 f0 = float2(dot(flowMs, d0), dot(flowMs, e0)) / scaleM;
    float2 f1 = float2(dot(flowMs, d1), dot(flowMs, e1)) / scaleM;
    float ph = t / HD_FLOW_PERIOD;
    float p0 = frac(ph), p1 = frac(ph + 0.5);
    // on running water each phase restarts somewhere else, so no texel is
    // dragged far (still water: no restarts, no pulsing)
    float2 j0 = float2(0.37, 0.21) * floor(ph) * moving;
    float2 j1 = (float2(0.37, 0.21) * floor(ph + 0.5) + 0.5) * moving;
    float4 s0 = tex2Dgrad(_RippleTex, uv0 - f0 * (p0 * HD_FLOW_PERIOD) + j0, gx0, gy0);
    float4 s1 = tex2Dgrad(_RippleTex, uv1 - f1 * (p1 * HD_FLOW_PERIOD) + j1, gx1, gy1);
    float w0 = lerp(0.5, 1.0 - abs(1.0 - 2.0 * p0), moving);
    float w1 = 1.0 - w0;
    // the blend of two unrelated fields is flatter: give the contrast back
    float c = lerp(1.0, rsqrt(max(w0 * w0 + w1 * w1, 0.5)), max(moving, stillMix));
    float2 a0 = s0.rg * 2.0 - 1.0, a1 = s1.rg * 2.0 - 1.0;
    // the sheet's +U is each reading's heading: back to world x,z
    nrm = ((d0 * a0.x + e0 * a0.y) * w0 + (d1 * a1.x + e1 * a1.y) * w1) * c;
    lace = s0.b * w0 + s1.b * w1;
    // the same octave two mips softer (part B, item 6): what the mirror is
    // marched along and wobbled by - the ripples' shapes, never their
    // single-pixel detail (that flicked the mirrored world pixel by pixel)
    nrmSoft = 0.0;
    if (soft)
    {
        float4 q0 = tex2Dgrad(_RippleTex, uv0 - f0 * (p0 * HD_FLOW_PERIOD) + j0, gx0 * 4.0, gy0 * 4.0);
        float4 q1 = tex2Dgrad(_RippleTex, uv1 - f1 * (p1 * HD_FLOW_PERIOD) + j1, gx1 * 4.0, gy1 * 4.0);
        float2 b0 = q0.rg * 2.0 - 1.0, b1 = q1.rg * 2.0 - 1.0;
        nrmSoft = ((d0 * b0.x + e0 * b0.y) * w0 + (d1 * b1.x + e1 * b1.y) * w1) * c;
    }
}

// Rain on the water: rings spreading from drops, two layers of cells.
float2 HDRainRings(float2 xz, float t, float rate, out float crest)
{
    float2 s = 0.0;
    crest = 0.0;
    [unroll]
    for (int k = 0; k < 2; k++)
    {
        float cellM = k == 0 ? 0.55 : 0.83;
        float2 p = xz / cellM + k * 17.31;
        float2 c = floor(p);
        float2 f = p - c;
        float h = WaterHash(c + k * 5.7);
        float period = 0.75 + 0.5 * h;
        float cyc = t / period + h;
        float age = frac(cyc);
        float drop = step(WaterHash(c + floor(cyc) * 13.17 + 3.3), rate);
        float2 ctr = 0.3 + 0.4 * float2(WaterHash(c + 7.1), WaterHash(c + 11.9));
        float2 dv = f - ctr;
        float r = length(dv);
        float x = (r - age * 0.28) / 0.05;   // (part B: a ring wide enough to read from the car at 20 m)
        float dh = -2.0 * x * exp(-x * x) * (1.0 - age) * (1.0 - age);
        s += (dv / max(r, 1e-3)) * dh * drop;
        crest += exp(-x * x) * (1.0 - age) * (1.0 - age) * drop;
    }
    return s * 0.22;
}

// THE WORLD IN THE WATER: the wet road's march (PSXWetSSR.cginc), against
// URP's depth copy. Returns the confidence of a hit, and its uv.
float HDSceneZ(float2 uv) { return LinearEyeDepth(tex2Dlod(_PSXWaterDepth, float4(uv, 0.0, 0.0)).r); }
float2 HDUV(float2 ndc) { return float2(ndc.x, ndc.y * _ProjectionParams.x) * 0.5 + 0.5; }

// The ray's screen point at t, its x held inside the frame: a ray that runs
// off a side reads the edge column (what is most likely there) instead of
// ending in the sky (part B, item 10: the bright bars down both sides of a
// lake mirroring a dark ridge).
// The frame's copy where the mirror found something, blurred by how rough
// the water is: four taps, wider up the screen (a rough surface streaks a
// reflection vertically).
float4 HDMirrorTap(float2 uv, float blur)
{
    if (blur < 1e-4) return tex2Dlod(_CameraOpaqueTexture, float4(saturate(uv), 0.0, 0.0));
    float4 c = tex2Dlod(_CameraOpaqueTexture, float4(saturate(uv + float2(blur, 0.0)), 0.0, 0.0));
    c += tex2Dlod(_CameraOpaqueTexture, float4(saturate(uv - float2(blur, 0.0)), 0.0, 0.0));
    c += tex2Dlod(_CameraOpaqueTexture, float4(saturate(uv + float2(0.0, blur * 2.2)), 0.0, 0.0));
    c += tex2Dlod(_CameraOpaqueTexture, float4(saturate(uv - float2(0.0, blur * 0.8)), 0.0, 0.0));
    return c * 0.25;
}

float2 HDRayUV(float2 s0, float2 s1, float t)
{
    float2 s = lerp(s0, s1, t);
    s.x = clamp(s.x, -0.999, 0.999);
    return HDUV(s);
}

// Refine one crossing between tLo (in front) and tHi (behind): its uv and how
// sure (the top edge and the reach fade).
float HDRefine(float2 s0, float2 s1, float k0, float k1, float len, float tLo, float tHi, out float2 uv)
{
    float lo = tLo, hi = tHi;
    [unroll]
    for (int r = 0; r < HD_SSR_REFINE; r++)
    {
        float m = 0.5 * (lo + hi);
        if (1.0 / lerp(k0, k1, m) >= HDSceneZ(HDRayUV(s0, s1, m))) hi = m; else lo = m;
    }
    uv = HDRayUV(s0, s1, hi);
    // the edge a water ray leaves by is the top (or, looking down a slope,
    // the bottom): fade there only - never at the sides (item 10)
    float conf = saturate(min(uv.y, 1.0 - uv.y) / HD_SSR_EDGE);
    float u = hi * k1 / lerp(k0, k1, hi);
    conf *= 1.0 - smoothstep(HD_SSR_FADE_FROM, 1.0, u * len / HD_SSR_MAX_M);
    return conf;
}

// THE MARCH (part B, item 6). No per-pixel jitter: the steps are the same for
// neighbouring pixels, so a ray never flips between hit and miss from one
// pixel to the next. Each crossing (the ray now behind what the frame shows)
// is weighed by how far that surface stands in front of where the ray was
// one step before: within the thickness it is the hit; beyond twice it, it is
// something in front (a rail, a tree) and the march goes on; in between, it
// fades - and a partial first crossing blends with the next one, so nothing
// switches. Returns up to two hits (uv, weight x confidence).
void HDMarch(float3 wpos, float3 R, float eyeDist, out float2 uvA, out float wA, out float2 uvB, out float wB)
{
    uvA = uvB = float2(0.0, 0.0);
    wA = wB = 0.0;
    float3 p0 = mul(UNITY_MATRIX_V, float4(wpos + R * HD_SSR_START_M, 1.0)).xyz;
    float3 d = mul((float3x3)UNITY_MATRIX_V, R);
    float len = HD_SSR_MAX_M;
    float nearZ = -_ProjectionParams.y * 1.5;
    if (d.z > 1e-4) len = min(len, (nearZ - p0.z) / d.z);
    if (len <= 0.0) return;
    float3 p1 = p0 + d * len;
    float4 h0 = mul(UNITY_MATRIX_P, float4(p0, 1.0));
    float4 h1 = mul(UNITY_MATRIX_P, float4(p1, 1.0));
    float k0 = 1.0 / h0.w, k1 = 1.0 / h1.w;
    float2 s0 = h0.xy * k0, s1 = h1.xy * k1;
    float2 ds = s1 - s0;
    // the march ends where the ray leaves by the top or the bottom only
    float tEnd = 1.0;
    if (ds.y > 1e-6) tEnd = min(tEnd, (1.0 - s0.y) / ds.y);
    else if (ds.y < -1e-6) tEnd = min(tEnd, (-1.0 - s0.y) / ds.y);
    if (tEnd <= 0.0) return;
    // far water (the steps span more of the frame per metre) takes half
    int steps = eyeDist > HD_SSR_FAR_STEPS_M ? HD_SSR_STEPS / 2 : HD_SSR_STEPS;
    float dt = tEnd / steps;
    float farZ = _ProjectionParams.z * 0.999;
    float tPrev = 0.0, zPrev = 1.0 / k0;
    float left = 1.0;
    [loop]
    for (int s = 1; s <= steps; s++)
    {
        float tt = dt * s;
        float rayZ = 1.0 / lerp(k0, k1, tt);
        float seenZ = HDSceneZ(HDRayUV(s0, s1, tt));
        if (rayZ >= seenZ && seenZ < farZ)
        {
            float thick = HD_SSR_THICK_M + HD_SSR_THICK_K * seenZ;
            float gap = max(zPrev - seenZ, 0.0);
            float w = 1.0 - smoothstep(thick, 2.0 * thick, gap);
            if (w > 0.002)
            {
                float2 uv;
                float conf = HDRefine(s0, s1, k0, k1, len, tPrev, tt, uv);
                if (left > 0.999)
                {
                    uvA = uv; wA = w * conf; left = 1.0 - w;
                    if (left < 0.002) return;
                }
                else
                {
                    uvB = uv; wB = w * conf;
                    return;
                }
            }
        }
        tPrev = tt;
        zPrev = rayZ;
    }
}

fixed4 fragHD(v2f_hd i) : SV_Target
{
    float t = _Time.y;
    float3 toEye = _WorldSpaceCameraPos - i.wpos;
    float eyeDist = length(toEye);
    float3 V = toEye / max(eyeDist, 1e-4);
    float3 L = normalize(_PSXLightDir.xyz);
    float depthV = i.data.x;
    float white = saturate(i.data.z);
    float2 flowMs = i.flow * HD_RIVER_FLOW;
    float flowSpd = length(flowMs);
    float moving = saturate(flowSpd * 3.0);
    float seaK = saturate(_OceanWaves);
    bool scene = _PSXWaterTier.x > 0.5;
    float rainK = saturate(_PSXWaterRain);
    float clearSky = saturate(_PSXWaterWind.w);

    // the frame's own axes on the ground (item 16): the screen's right, and the
    // way that is "up the screen" (away from the eye) - a world x,z direction
    // turned into a screen direction whatever the heading
    float3 camR = UNITY_MATRIX_V[0].xyz, camU = UNITY_MATRIX_V[1].xyz, camF = -UNITY_MATRIX_V[2].xyz;
    float2 rgt = normalize(camR.xz + float2(1e-5, 0.0));
    float2 upH = normalize(camU.xz + camF.xz + float2(0.0, 1e-5));

    // ---- the wind, and its gusts drifting over the water ----
    float2 wdir = HDWindDir();
    float wspd = max(_PSXWaterWind.z, 0.0);
    float2 gp = i.wpos.xz / HD_GUST_TILE_M - wdir * (t * wspd * lerp(HD_GUST_DRIFT.x, HD_GUST_DRIFT.y, seaK) / HD_GUST_TILE_M);
    // three readings of the breakup field at unrelated scales and turns (item
    // 17: with two, the calm patches came back every 190 m)
    // (the sea keeps its two, as before)
    float gA = tex2D(_RippleTex, gp).a, gB = tex2D(_RippleTex, Rot(gp, 1.1) * 0.43 + 0.31).a;
    float gC = tex2D(_RippleTex, Rot(gp, -2.3) * 0.161 + 0.67).a;
    float gust = lerp(gA * 0.50 + gB * 0.30 + gC * 0.20, gA * 0.62 + gB * 0.38, seaK);
    float windK = lerp(HD_WIND_CALM, 1.0, saturate(wspd / HD_WIND_FULL));
    float patch = lerp(HD_GUST_MIN, 1.0, smoothstep(lerp(0.33, 0.30, seaK), lerp(0.72, 0.78, seaK), gust));
    // a strong wind ruffles everything; a light or moderate one only its gusts
    float ruffle = lerp(patch, 1.0, saturate((wspd - 5.0) / 7.0)) * windK;
    // running water is rough whatever the wind
    ruffle = max(ruffle, saturate(flowSpd * _WaterLook.w));
    // rain stirs every patch: no glassy calm under a downpour
    ruffle = max(ruffle, rainK * 0.85);
    ruffle = saturate(ruffle + white);

    // ---- the ripples: octaves downwind, advected down the flow ----
    float tile = max(_WaterLook.x, 0.5);
    float2 dxz = ddx(i.wpos.xz), dyz = ddy(i.wpos.xz);
    float2 rip = 0.0;
    float laceA = 0.0, laceB = 0.0, laceC = 0.0;
    float2 nA = 0.0, nB = 0.0, nC = 0.0, softA = 0.0, unusedSoft = 0.0;
    // each octave's drift: its waves' phase speed on the sea; on still water
    // (and under a river's flow) a slow creep, slower still in a light air
    float3 drift = sqrt(9.81 * tile * HD_OCT_SCALE * 0.2 / 6.2831853)
                 * lerp(HD_STILL_DRIFT * lerp(0.5, 1.0, saturate(wspd / HD_WIND_FULL)), 1.0, seaK);
    HDOctave(i.wpos.xz, dxz, dyz, Rot(wdir, HD_OCT_TURN.x), tile * HD_OCT_SCALE.x, drift.x, flowMs, t, seaK, true, nA, laceA, softA);
    HDOctave(i.wpos.xz, dxz, dyz, Rot(wdir, HD_OCT_TURN.y), tile * HD_OCT_SCALE.y, drift.y, flowMs, t, seaK, false, nB, laceB, unusedSoft);
    rip = nA * HD_OCT_GAIN.x + nB * HD_OCT_GAIN.y;
    // TOKSVIG (item 6): what each octave's mips have averaged away at this
    // pixel's footprint is not gone - it is roughness. The share lost, from the
    // sheet's own measured spectrum, as a ramp in the mip level.
    float foot = max(length(dxz), length(dyz)) * HD_SHEET_TEXELS;
    float3 lod = log2(max(foot / (tile * HD_OCT_SCALE), 1e-4));
    float3 lost = saturate((lod - 1.2) * 0.23);
    float3 g2 = HD_OCT_GAIN * HD_OCT_GAIN;
    float lostVar = lost.x * g2.x + lost.y * g2.y + g2.z;
    [branch] if (_PSXWaterTier.z > 0.5)
    {
        HDOctave(i.wpos.xz, dxz, dyz, Rot(wdir, HD_OCT_TURN.z), tile * HD_OCT_SCALE.z, drift.z, flowMs, t, seaK, false, nC, laceC, unusedSoft);
        // the finest octave is drawn only to 40 m and gone by 60 m (item 6):
        // beyond, its slopes are roughness
        float fC = saturate((60.0 - eyeDist) / 20.0);
        rip += nC * HD_OCT_GAIN.z * fC;
        lostVar -= g2.z * fC * (1.0 - lost.z);
    }
    float sv = _WaterLook.y * ruffle;
    rip *= sv;
    lostVar *= HD_SHEET_VAR * sv * sv;

    // ---- the swell and the chop (the sea), as the old water ----
    float2 slope = 0.0;
    {
        float3 dummy = 0.0;
        float2 ws = 0.0;
        float2 wd = _OceanWaves > 0.5 ? normalize(_WaveDir.xz + float2(1e-4, 0.0)) : wdir;
        float oc = i.swell.x;
        if (oc > 0.001)
        {
            float3 A = SWELL_A * oc;
            Gerstner(i.wpos.xz, Rot(wd, SWELL_SPREAD.x), A.x, SWELL_L.x, 0.0, t, dummy, ws);
            Gerstner(i.wpos.xz, Rot(wd, SWELL_SPREAD.y), A.y, SWELL_L.y, 0.0, t * 1.03, dummy, ws);
            Gerstner(i.wpos.xz, Rot(wd, SWELL_SPREAD.z), A.z, SWELL_L.z, 0.0, t * 0.97, dummy, ws);
            Gerstner(i.wpos.xz, Rot(wd, CHOP_SPREAD.x), CHOP_A.x * oc, CHOP_L.x, 0.0, t, dummy, ws);
            Gerstner(i.wpos.xz, Rot(wd, CHOP_SPREAD.y), CHOP_A.y * oc, CHOP_L.y, 0.0, t, dummy, ws);
        }
        // (the old water's single 7.5 m ripple train stays in the vertices
        // only: in the pixel it read as corduroy under a sharp mirror - the
        // spectral sheet is the ripple now)
        slope = ws;
    }
    // rain rings
    float rain = rainK * saturate(1.0 - eyeDist / HD_RAIN_FADE_M);
    float2 rings = 0.0;
    float ringCrest = 0.0;
    [branch] if (rain > 0.001) rings = HDRainRings(i.wpos.xz, t, rain, ringCrest);

    // n.xy of a normal is minus the slope
    float2 tilt = -slope + rip - rings;
    float3 N = normalize(float3(tilt.x, 1.0, tilt.y));
    // the march reads less of the ripples as their mips average them away
    // (a steady far mirror, item 6)
    float2 ripSoft = softA * (HD_OCT_GAIN.x * sv);
    float3 Nm = normalize(float3(-slope.x + ripSoft.x * HD_SSR_SMOOTH, 1.0, -slope.y + ripSoft.y * HD_SSR_SMOOTH));
    // how rough it looks: the ripples it draws; what the mips averaged away
    // (Toksvig); the normal's own spread across the pixel (screen-space
    // specular AA, capped); white water; and the rain, at every distance
    float3 dNx = ddx(N), dNy = ddy(N);
    float aaVar = lostVar + min(0.25 * (dot(dNx, dNx) + dot(dNy, dNy)) * HD_GEO_AA, HD_GEO_AA_MAX);
    // (the slopes drawn and the slopes averaged away add as variances, not
    // as a sum of their sizes: summed, a grazing river saturated to rough 1,
    // a mirror blurred to grey - the pale sheet at Bat Cave)
    float rough = saturate(sqrt(dot(rip, rip) + aaVar) * 2.2 + white + rainK * HD_RAIN_ROUGH);

    // ---- light ----
    float expo = PSXExposureGain();
    float adapt = PSXAdaptGain();
    float shadow = PSXSunShadow(i.wpos, float3(0, 1, 0), eyeDist, 0.0);
    float3 light = (i.amb + i.sun * shadow) * expo;
    float3 lampD, lampS, headD, headS;
    PSXLampsBoth(i.wpos, N, V, LAMP_POW, 1.0, lampD, lampS);
    PSXHeadlightsBoth(i.wpos, N, V, LAMP_POW, 1.0, headD, headS);
    lampD *= expo; lampS *= expo; headD *= expo; headS *= expo;
    float3 lightAll = light + lampD + headD;

    // ---- how deep, along the view and straight down ----
    float2 suv = i.spos.xy / max(i.spos.w, 1e-4);
    float surfZ = i.spos.w;
    float pathM, downM;
    float2 ruv = suv;
    if (scene)
    {
        float bedZ = HDSceneZ(suv);
        float dz = max(bedZ - surfZ, 0.0);
        // the ripples bend the bed, more where it is deeper; never onto
        // something in front of the water. The tilt as a screen direction
        // (item 16: the world's x,z went straight onto the screen's u,v)
        float2 nScr = float2(dot(N.xz, rgt), dot(N.xz, upH) * _ProjectionParams.x);
        ruv = suv + nScr * (HD_REFRACT_M / max(surfZ, 1.0)) * saturate(dz * 0.8);
        float bedZr = HDSceneZ(ruv);
        if (bedZr < surfZ) { ruv = suv; bedZr = bedZ; }
        float k = eyeDist / max(surfZ, 1e-3);
        pathM = max(bedZr - surfZ, 0.0) * k;
        downM = max(bedZ - surfZ, 0.0) * k * saturate(V.y);
        if (i.data.w > 0.5)
        {
            // the vertex depth is the better answer when the mesh has it and
            // the frame's depth ran past the ground into the sky
            downM = min(downM, max(depthV, 0.0) + 0.5);
            // ...and where the frame's bed lies far SHALLOWER than the mesh
            // says (item 11: Lake Lure's lidar flew the lake drawn down 3.5 m,
            // a flat floor that steep views saw through the water)
            float under = smoothstep(0.7, 1.3, depthV - downM);
            pathM = lerp(pathM, depthV / max(V.y, 0.08), under);
            downM = lerp(downM, depthV, under);
        }
    }
    else
    {
        downM = max(depthV, 0.0);
        pathM = downM / max(V.y, 0.08);
    }

    // ---- the body: what is under it, through it, and its own colour ----
    float3 T = exp(-_Absorb.rgb * (pathM + downM));
    float3 scatterLit = _Scatter.rgb * lightAll;
    // aerated (white) water scatters white
    scatterLit = lerp(scatterLit, HD_FOAM_COLOR * lightAll, white * 0.65);
    float3 bodyCheap = lerp(scatterLit, _SandColor.rgb * lightAll * 0.9, T);

    // ---- the mirror ----
    float NdV = saturate(dot(N, V));
    float fres = F0 + (1.0 - F0) * pow(1.0 - NdV, 5.0);
    // a rough surface cannot be a perfect mirror at grazing angles
    fres *= lerp(1.0, 0.72, rough);
    float3 R = reflect(-V, N);
    // the old water's roughness tilt (a choppy surface mirrors sky from
    // higher up, so open water is darker than its horizon), kept as a floor
    // on the sea and scaled by how rough the surface is everywhere
    R.y = max(R.y, 0.004) + ROUGH * 1.3 * (1.0 - R.y) * max(rough, 0.6 * saturate(i.data.y * 3.0));
    R = normalize(R);
    float skyLod = lerp(HD_SKY_LOD_CALM, HD_SKY_LOD_ROUGH, rough);
    float3 skyRefl = PSXSkyIn(R, skyLod) * (REFLECT_GAIN * adapt);
    float3 reflToned = PSXTone(skyRefl);
    float3 skyToned = reflToned;
    // THE WORLD ROUND THE CAR where the mirror finds nothing on screen (part
    // B): the panorama CarReflections draws from the car's roof (_PSXEnvPano,
    // already toned, as the car paint reads it) instead of the bare sky - a
    // river in a wooded gorge mirrors its dark banks and the ridges above it
    // (off the top of the frame), not the bright overcast horizon: the Bat
    // Cave river in rain was a pale sheet. The sky without it (REFLECTIONS off).
    [branch] if (_PSXEnvOn > 0.5) reflToned = PSXEnvIn(R, skyLod + HD_ENV_LOD) * HD_ENV_GAIN;
    float reflEmit = 0.0;
    float dbgConf = 0.0;
    // THE WORLD IN IT, except looking steeply down into the water (item 14:
    // a ramp, not a cut). Rough water keeps its mirror, BLURRED (HDMirrorTap):
    // a roughness cut-off showed the sky instead, a pale sheet in rain. Rain
    // takes part of the mirror away and breaks the rest up (item 8).
    float marchW = smoothstep(HD_SSR_FRES_ON.x, HD_SSR_FRES_ON.y, fres)
                 * (1.0 - HD_RAIN_MIRROR * rainK);
    [branch] if (_PSXWaterTier.y > 0.5 && scene && marchW > 0.01)
    {
        float3 Rm = reflect(-V, Nm);
        Rm.y = max(Rm.y, 0.004);
        Rm = normalize(Rm);
        float2 uvA, uvB;
        float wA, wB;
        HDMarch(i.wpos, Rm, eyeDist, uvA, wA, uvB, wB);
        // the ripples wobble what it found - less as they average away
        // the soft ripples (and the rain's rings) wobble what it found: the
        // shapes of the waves, never single-pixel detail (item 6)
        float2 dn = ripSoft * (1.0 - HD_SSR_SMOOTH) - rings;
        float2 wob = float2(dot(dn, rgt), dot(dn, upH) * _ProjectionParams.x) * (HD_SSR_WOBBLE * (1.0 + HD_RAIN_WOBBLE * rainK));
        float3 hit = reflToned;
        float he = 0.0;
        float blur = HD_SSR_BLUR * saturate((rough - 0.25) / 0.75);
        [branch] if (wB > 0.0)
        {
            float4 mb = HDMirrorTap(uvB + wob, blur);
            hit = lerp(hit, mb.rgb, wB);
            he = mb.a * wB;
        }
        [branch] if (wA > 0.0)
        {
            float4 ma = HDMirrorTap(uvA + wob, blur);
            hit = lerp(hit, ma.rgb, wA);
            he = lerp(he, ma.a, wA);
        }
        reflToned = lerp(reflToned, hit, marchW);
        reflEmit = he * marchW;
        dbgConf = max(wA, wB) * marchW;
    }

    // ---- the sun's (the moon's) highlight ----
    float3 H = normalize(L + V);
    // widened by what the pixel averages (Toksvig) and toward the rough
    // power with distance (item 6): a broad glitter path far out, not single
    // bright pixels; only under the sky's clear share (item 8: no glint
    // under overcast or rain)
    float basePow = lerp(lerp(HD_SPEC_CALM, HD_SPEC_ROUGH, rough), HD_SPEC_ROUGH, saturate(eyeDist / 250.0));
    float specPow = max(1.0 / (1.0 / basePow + 2.0 * aaVar), HD_SPEC_FLOOR);
    float spec = pow(saturate(dot(N, H)), specPow) * (specPow + 8.0) / 25.13;
    spec = min(spec, HD_SPEC_MAX) * saturate(L.y * 4.0) * lerp(1.0, GLINT_NIGHT, _PSXNight) * clearSky;
    float3 sunGlint = _PSXLightColor.rgb * spec * fres * 4.0 * shadow * expo;
    float3 lampGlint = (lampS + headS) * LAMP_GAIN * (0.3 + fres);

    // ---- foam: the shore, white water, whitecaps ----
    float swash = sin(t * (6.2831853 / FOAM_SWASH_S) + i.wpos.x * 0.05 + i.wpos.z * 0.03) * FOAM_RUN_M * i.data.y;
    // The foam keys off the mesh's own depth where it has one (the coast
    // bakes' bathymetry: smooth, where the frame's ground drops 4 m in one
    // terrain cell), off the frame's depth where it has none (the city).
    float foamD = i.data.w > 0.5 ? max(depthV, 0.0) : downM;
    float shoreD = max(foamD - swash, 0.0);
    float shore = saturate(1.0 - shoreD / (HD_FOAM_M * (1.0 + HD_FOAM_OPEN * saturate(i.data.y * 2.0))));
    float laceMix = laceA * 0.55 + laceB * 0.45;
    float cap = saturate((i.swell.y - WHITECAP) / (1.0 - WHITECAP));
    // THE SURF: where the open ocean's swell runs into water shallower than
    // HD_SURF_DEPTH_M its crests break - lines of foam just behind each
    // crest, running in with the swell toward the beach.
    float surf = 0.0;
    if (_OceanWaves > 0.5 && i.data.y > 0.05)
    {
        float2 wdS = normalize(_WaveDir.xz + float2(1e-4, 0.0));
        float kS = 6.2831853 / (SWELL_L.x * 0.55);           // shoaling: shorter in the shallows
        float ph = kS * dot(wdS, i.wpos.xz) - sqrt(9.81 * kS) * t * 0.8;
        float wob = (laceB - 0.5) * 1.2;                      // a ragged front, not a ruler
        float front = pow(saturate(sin(ph + wob) * 0.5 + 0.5), 5.0);
        float zone = saturate(1.0 - foamD / HD_SURF_DEPTH_M) * saturate(foamD / 0.3);
        surf = saturate(front * 1.4) * zone * saturate(i.data.y * 2.0);
    }
    float shoreFoam = shore * shore;
    // running water (item 12): the bank's foam capped well under white and
    // broken by the lace - lace where the current works the bank, never a
    // ribbon down it
    shoreFoam = lerp(shoreFoam, min(shoreFoam, 0.35) * smoothstep(0.45, 0.8, laceMix), moving);
    // still water (a lake): a shore foams only in a blow - no surf line on a
    // calm reservoir (the sea and running water as they were)
    shoreFoam *= lerp(saturate((wspd - 4.0) / 4.0), 1.0, max(seaK, moving));
    float foamWant = max(max(max(shoreFoam, white), cap * cap), surf) * _WaterLook.z;
    // close up the foam is the lace; further out the lace is under a pixel
    // and its mips average it toward grey, so it becomes the even band it
    // looks like from there (thresholding the averaged lace erased it)
    float foamNear = saturate((laceMix + foamWant - 1.0) * 2.4 + foamWant * 0.35);
    float foam = lerp(foamNear, foamWant * 0.85, saturate((eyeDist - 15.0) / 45.0));
    foam *= saturate(1.0 - eyeDist / 600.0);

    // ---- together ----
    float edge = saturate(downM / HD_SHORE_SOFT_M);
    float fog = i.swell.w;
    float3 fogCol = PSXFogTone(PSXFogTowardSun(_PSXFogColor.rgb * PSXFarHazeRing(PSXFogRing(-V, _PSXSkyRotation), -V, fog, 0), V));
    float3 col;
    float emit;
    if (scene)
    {
        // the water's own light: its in-scatter and its mirror (toned), and
        // the bed through it (already lit and toned in the frame's copy)
        float3 own = scatterLit * (1.0 - T) * (1.0 - fres);
        float3 lin = own + sunGlint + lampGlint;
        float3 w = PSXTone(lin) + reflToned * fres;
        float3 tau = T * (1.0 - fres);
        w = lerp(w, HD_FOAM_COLOR * lightAll, foam);
        tau *= 1.0 - foam;
        float3 bed = tex2Dlod(_CameraOpaqueTexture, float4(ruv, 0.0, 0.0)).rgb;
        col = w * (1.0 - fog) + fogCol * fog * (1.0 - tau) + bed * tau;
        // the waterline melts into the ground: no line where the plane cuts it
        float3 bedHere = tex2Dlod(_CameraOpaqueTexture, float4(suv, 0.0, 0.0)).rgb;
        col = lerp(bedHere, col, edge);
        emit = PSXEmitShare(sunGlint + lampS * (LAMP_GAIN * (0.3 + fres)), lin + reflToned * fres) * edge;
        emit = saturate(emit + reflEmit * fres);
    }
    else
    {
        float3 lin = bodyCheap * (1.0 - fres) + sunGlint + lampGlint;
        lin = lerp(lin, HD_FOAM_COLOR * lightAll, foam);
        col = PSXTone(lin) + reflToned * fres * (1.0 - foam);
        col = lerp(col, fogCol, fog);
        emit = PSXEmitShare(sunGlint + lampS * (LAMP_GAIN * (0.3 + fres)), lin);
    }
    // RAIN (item 8): the drops throw the sky's grey back off the whole
    // surface - everywhere a greying, and beyond the rings (under a pixel
    // there) the pale sheen of their pocks; never brighter than that sky
    float rainGrey = rainK * (HD_RAIN_GREY + HD_RAIN_POCK * saturate((eyeDist - 20.0) / 30.0)) * edge;
    col = lerp(col, lerp(skyToned, fogCol, fog), rainGrey);
    // ...and near, each drop's ring stands out of the surface round it: on
    // water darker than the sky its crest catches the sky's light, on water
    // that already mirrors the bright sky (a river at a grazing angle) it
    // throws back the darker world - the pocked look, both ways, readable
    // from the car (the rings' slopes alone were under a pixel)
    float lumW = dot(col, float3(0.2126, 0.7152, 0.0722)), lumS = dot(skyToned, float3(0.2126, 0.7152, 0.0722));
    float3 pockTo = lerp(skyToned, col * 0.45, saturate((lumW / max(lumS, 1e-3) - 0.45) * 3.0));
    col = lerp(col, pockTo, saturate(ringCrest) * rain * HD_RAIN_SPARK * (1.0 - fog) * edge);
    emit *= 1.0 - rainGrey;
    // the distant haze, as PSX/Lit gathers it (0 by day and outside the lit city)
    if (_PSXDistHaze.x > 0.0)
    {
        float hz = _PSXDistHaze.x * (1.0 - exp(-max(eyeDist - _PSXDistHaze.y, 0.0) * _PSXDistHaze.z));
        col = lerp(col, fogCol, hz);
        emit *= 1.0 - hz;
    }
    if (_PSXWaterDebug > 3.5) col = float3(saturate(rough), saturate(sqrt(aaVar) * 4.0), marchW);
    else if (_PSXWaterDebug > 2.5) col = tex2Dlod(_CameraOpaqueTexture, float4(suv, 0.0, 0.0)).rgb;
    else if (_PSXWaterDebug > 1.5) col = float3(saturate(HDSceneZ(suv) / 400.0), saturate(surfZ / 400.0), 0.0);
    else if (_PSXWaterDebug > 0.5) col = float3(scene ? 1.0 : 0.0, dbgConf, saturate(downM / 10.0));
    return fixed4(col, _PSXEmitWrite > 0.5 ? emit * (1.0 - fog) : 1.0);
}

#endif
