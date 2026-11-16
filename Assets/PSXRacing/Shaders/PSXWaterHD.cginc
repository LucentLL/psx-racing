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
#ifndef PSX_WATER_HD_INCLUDED
#define PSX_WATER_HD_INCLUDED

sampler2D _RippleTex;        // RG wave normal, B foam lace, A breakup (linear)
float4 _Absorb;              // per-metre extinction, rgb
float4 _Scatter;             // the water's own colour (in-scatter), rgb
float4 _WaterLook;           // x ripple tile m, y ripple strength, z foam gain, w river roughness per m/s
float4 _PSXWaterTier;        // x frame + depth copies, y screen-space mirror, z third octave (WaterLook.cs)
float4 _PSXWaterWind;        // xy wind toward (world xz, unit), z speed m/s at the surface
float _PSXWaterRain;         // 0..1 how hard rain is falling on it
sampler2D_float _CameraDepthTexture;   // URP's copy after the opaques (no water in it)
sampler2D _CameraOpaqueTexture;        // URP's copy after the sky (no water in it)

// ---- the look ----
#define HD_GUST_TILE_M     190.0    // gust patches: the breakup field's repeat
#define HD_GUST_MIN        0.18     // the calmest patch keeps this share of the ripple
#define HD_WIND_FULL       6.0      // m/s at which the whole surface is ruffled
#define HD_WIND_CALM       0.35     // share of the ripple left at a dead calm
#define HD_OCT_SCALE       float3(1.0, 0.36, 0.125)   // x _WaterLook.x per octave
#define HD_OCT_TURN        float3(0.0, 0.62, -0.47)   // radians off the wind
#define HD_OCT_GAIN        float3(1.0, 0.62, 0.40)
#define HD_FLOW_PERIOD     1.6      // s, one flow phase
#define HD_REFRACT_M       0.55     // how far the ripples bend the bed (screen share x metres)
#define HD_SHORE_SOFT_M    0.06     // the waterline melts into the ground over this depth
#define HD_FOAM_M          0.55     // shore foam reaches this deep
#define HD_FOAM_TILE_M     3.4      // the lace's repeat
#define HD_FOAM_COLOR      float3(0.80, 0.83, 0.82)
#define HD_SKY_LOD_CALM    0.6
#define HD_SKY_LOD_ROUGH   3.0
#define HD_SPEC_CALM       1400.0   // sun/moon highlight power on glassy water...
#define HD_SPEC_ROUGH      220.0    // ...and on a choppy one
#define HD_SPEC_MAX        5.0
#define HD_RAIN_FADE_M     45.0     // rings beyond this are under a pixel
#define HD_SSR_STEPS       24
#define HD_SSR_REFINE      5
#define HD_SSR_START_M     0.15
#define HD_SSR_MAX_M       1600.0   // a lake mirrors ridges a kilometre off
#define HD_SSR_FADE_FROM   0.80
#define HD_SSR_THICK_M     0.6
#define HD_SSR_THICK_K     0.025
#define HD_SSR_EDGE        0.06
#define HD_SSR_SMOOTH      0.30     // the march reads the ripples at this share (a steadier mirror)...
#define HD_SSR_WOBBLE      0.035    //   ...and they wobble what it found by this much of the frame

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
    float white = has * v.color.b;
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
    // the slow ripple (centimetres), not on running water
    float still = 1.0 - saturate(length(flow) * 2.0);
    Gerstner(wpos.xz, Rot(wd, 0.9), RIPPLE_A * saturate(depthM) * still, RIPPLE_L, 0.3, t, disp, slope);
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
void HDOctave(float2 xz, float2 dxz, float2 dyz, float2 d, float scaleM, float drift, float2 flowMs, float t,
              out float2 nrm, out float lace)
{
    float2 side = float2(-d.y, d.x);
    float2 uv = float2(dot(xz, d), dot(xz, side)) / scaleM;
    float2 gx = float2(dot(dxz, d), dot(dxz, side)) / scaleM;
    float2 gy = float2(dot(dyz, d), dot(dyz, side)) / scaleM;
    uv.x -= drift * t / scaleM;
    // the flow in the octave's frame, tiles per second
    float2 f = float2(dot(flowMs, d), dot(flowMs, side)) / scaleM;
    float moving = saturate(length(flowMs) * 3.0);
    float ph = t / HD_FLOW_PERIOD;
    float p0 = frac(ph), p1 = frac(ph + 0.5);
    // On still water both phases read the same place (no pulsing); on running
    // water each phase restarts somewhere else, so no texel is dragged far.
    float2 j0 = float2(0.37, 0.21) * floor(ph) * moving;
    float2 j1 = (float2(0.37, 0.21) * floor(ph + 0.5) + 0.5) * moving;
    float4 s0 = tex2Dgrad(_RippleTex, uv - f * (p0 * HD_FLOW_PERIOD) + j0, gx, gy);
    float4 s1 = tex2Dgrad(_RippleTex, uv - f * (p1 * HD_FLOW_PERIOD) + j1, gx, gy);
    float w0 = 1.0 - abs(1.0 - 2.0 * p0);
    float w1 = 1.0 - w0;
    float2 n = (s0.rg * 2.0 - 1.0) * w0 + (s1.rg * 2.0 - 1.0) * w1;
    // the blend of two unrelated fields is flatter: give the contrast back
    n *= lerp(1.0, rsqrt(max(w0 * w0 + w1 * w1, 0.5)), moving);
    // the sheet's +U is the octave's heading: back to world x,z
    nrm = d * n.x + side * n.y;
    lace = s0.b * w0 + s1.b * w1;
}

// Rain on the water: rings spreading from drops, two layers of cells.
float2 HDRainRings(float2 xz, float t, float rate)
{
    float2 s = 0.0;
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
        float x = (r - age * 0.28) / 0.035;
        float dh = -2.0 * x * exp(-x * x) * (1.0 - age) * (1.0 - age);
        s += (dv / max(r, 1e-3)) * dh * drop;
    }
    return s * 0.22;
}

// THE WORLD IN THE WATER: the wet road's march (PSXWetSSR.cginc), against
// URP's depth copy. Returns the confidence of a hit, and its uv.
float HDSceneZ(float2 uv) { return LinearEyeDepth(tex2Dlod(_CameraDepthTexture, float4(uv, 0.0, 0.0)).r); }
float2 HDUV(float2 ndc) { return float2(ndc.x, ndc.y * _ProjectionParams.x) * 0.5 + 0.5; }

float HDMarch(float3 wpos, float3 R, float2 fragXY, out float2 hitUV)
{
    hitUV = float2(0.0, 0.0);
    float3 p0 = mul(UNITY_MATRIX_V, float4(wpos + R * HD_SSR_START_M, 1.0)).xyz;
    float3 d = mul((float3x3)UNITY_MATRIX_V, R);
    float len = HD_SSR_MAX_M;
    float nearZ = -_ProjectionParams.y * 1.5;
    if (d.z > 1e-4) len = min(len, (nearZ - p0.z) / d.z);
    if (len <= 0.0) return 0.0;
    float3 p1 = p0 + d * len;
    float4 h0 = mul(UNITY_MATRIX_P, float4(p0, 1.0));
    float4 h1 = mul(UNITY_MATRIX_P, float4(p1, 1.0));
    float k0 = 1.0 / h0.w, k1 = 1.0 / h1.w;
    float2 s0 = h0.xy * k0, s1 = h1.xy * k1;
    float2 ds = s1 - s0;
    float tEnd = 1.0;
    if (ds.x > 1e-6) tEnd = min(tEnd, (1.0 - s0.x) / ds.x);
    else if (ds.x < -1e-6) tEnd = min(tEnd, (-1.0 - s0.x) / ds.x);
    if (ds.y > 1e-6) tEnd = min(tEnd, (1.0 - s0.y) / ds.y);
    else if (ds.y < -1e-6) tEnd = min(tEnd, (-1.0 - s0.y) / ds.y);
    if (tEnd <= 0.0) return 0.0;
    float jitter = frac(52.9829189 * frac(dot(fragXY, float2(0.06711056, 0.00583715))));
    float dt = tEnd / HD_SSR_STEPS;
    float farZ = _ProjectionParams.z * 0.999;
    float tPrev = 0.0, zPrev = 1.0 / k0, tHit = -1.0;
    [loop]
    for (int s = 0; s < HD_SSR_STEPS; s++)
    {
        float tt = dt * (s + jitter);
        float rayZ = 1.0 / lerp(k0, k1, tt);
        float seenZ = HDSceneZ(HDUV(lerp(s0, s1, tt)));
        if (rayZ >= seenZ && seenZ < farZ
            && seenZ >= min(zPrev, rayZ) - (HD_SSR_THICK_M + HD_SSR_THICK_K * seenZ))
        {
            tHit = tt;
            break;
        }
        tPrev = tt;
        zPrev = rayZ;
    }
    if (tHit < 0.0) return 0.0;
    float lo = tPrev, hi = tHit;
    [unroll]
    for (int r = 0; r < HD_SSR_REFINE; r++)
    {
        float m = 0.5 * (lo + hi);
        if (1.0 / lerp(k0, k1, m) >= HDSceneZ(HDUV(lerp(s0, s1, m)))) hi = m; else lo = m;
    }
    hitUV = HDUV(lerp(s0, s1, hi));
    float2 e = min(hitUV, 1.0 - hitUV);
    float conf = saturate(min(e.x, e.y) / HD_SSR_EDGE);
    float u = hi * k1 / lerp(k0, k1, hi);
    conf *= 1.0 - smoothstep(HD_SSR_FADE_FROM, 1.0, u * len / HD_SSR_MAX_M);
    return conf;
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
    float2 flowMs = i.flow;
    float flowSpd = length(flowMs);
    bool scene = _PSXWaterTier.x > 0.5;

    // ---- the wind, and its gusts drifting over the water ----
    float2 wdir = HDWindDir();
    float wspd = max(_PSXWaterWind.z, 0.0);
    float2 gp = i.wpos.xz / HD_GUST_TILE_M - wdir * (t * wspd * 0.5 / HD_GUST_TILE_M);
    float gust = tex2D(_RippleTex, gp).a * 0.62 + tex2D(_RippleTex, Rot(gp, 1.1) * 0.43 + 0.31).a * 0.38;
    float windK = lerp(HD_WIND_CALM, 1.0, saturate(wspd / HD_WIND_FULL));
    float patch = lerp(HD_GUST_MIN, 1.0, smoothstep(0.30, 0.78, gust));
    // a strong wind ruffles everything; a light one only its gusts
    float ruffle = lerp(patch, 1.0, saturate((wspd - 3.0) / 6.0)) * windK;
    // running water is rough whatever the wind
    ruffle = max(ruffle, saturate(flowSpd * _WaterLook.w));
    ruffle = saturate(ruffle + white);

    // ---- the ripples: octaves downwind, advected down the flow ----
    float tile = max(_WaterLook.x, 0.5);
    float2 dxz = ddx(i.wpos.xz), dyz = ddy(i.wpos.xz);
    float2 rip = 0.0;
    float laceA = 0.0, laceB = 0.0, laceC = 0.0;
    float2 nA = 0.0, nB = 0.0, nC = 0.0;
    HDOctave(i.wpos.xz, dxz, dyz, Rot(wdir, HD_OCT_TURN.x), tile * HD_OCT_SCALE.x,
             sqrt(9.81 * tile * HD_OCT_SCALE.x * 0.2 / 6.2831853), flowMs, t, nA, laceA);
    HDOctave(i.wpos.xz, dxz, dyz, Rot(wdir, HD_OCT_TURN.y), tile * HD_OCT_SCALE.y,
             sqrt(9.81 * tile * HD_OCT_SCALE.y * 0.2 / 6.2831853), flowMs, t, nB, laceB);
    rip = nA * HD_OCT_GAIN.x + nB * HD_OCT_GAIN.y;
    [branch] if (_PSXWaterTier.z > 0.5)
    {
        HDOctave(i.wpos.xz, dxz, dyz, Rot(wdir, HD_OCT_TURN.z), tile * HD_OCT_SCALE.z,
                 sqrt(9.81 * tile * HD_OCT_SCALE.z * 0.2 / 6.2831853), flowMs, t, nC, laceC);
        // the finest octave fades out before it is under a pixel
        rip += nC * HD_OCT_GAIN.z * saturate(1.5 - eyeDist / 120.0);
    }
    rip *= _WaterLook.y * ruffle;

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
        float still = 1.0 - saturate(flowSpd * 2.0);
        Gerstner(i.wpos.xz, Rot(wd, 0.9), RIPPLE_A * i.swell.z * still, RIPPLE_L, 0.0, t, dummy, ws);
        slope = ws;
    }
    // rain rings
    float rain = saturate(_PSXWaterRain) * saturate(1.0 - eyeDist / HD_RAIN_FADE_M);
    float2 rings = 0.0;
    [branch] if (rain > 0.001) rings = HDRainRings(i.wpos.xz, t, rain);

    // n.xy of a normal is minus the slope
    float2 tilt = -slope + rip + rings;
    float3 N = normalize(float3(tilt.x, 1.0, tilt.y));
    float3 Nm = normalize(float3(-slope.x + rip.x * HD_SSR_SMOOTH, 1.0, -slope.y + rip.y * HD_SSR_SMOOTH));
    float rough = saturate(length(rip) * 2.2 + white);

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
        // something in front of the water
        ruv = suv + N.xz * (HD_REFRACT_M / max(surfZ, 1.0)) * saturate(dz * 0.8);
        float bedZr = HDSceneZ(ruv);
        if (bedZr < surfZ) { ruv = suv; bedZr = bedZ; }
        float k = eyeDist / max(surfZ, 1e-3);
        pathM = max(bedZr - surfZ, 0.0) * k;
        downM = max(bedZ - surfZ, 0.0) * k * saturate(V.y);
        // the vertex depth is the better answer when the mesh has it and the
        // frame's depth ran past the ground into the sky
        if (i.data.w > 0.5) downM = min(downM, max(depthV, 0.0) + 0.5);
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
    R.y = max(R.y, 0.004) + ROUGH * rough * 1.3 * (1.0 - R.y);
    R = normalize(R);
    float skyLod = lerp(HD_SKY_LOD_CALM, HD_SKY_LOD_ROUGH, rough);
    float3 skyRefl = PSXSkyIn(R, skyLod) * (REFLECT_GAIN * adapt);
    float3 reflToned = PSXTone(skyRefl);
    float reflEmit = 0.0;
    [branch] if (_PSXWaterTier.y > 0.5 && scene)
    {
        float3 Rm = reflect(-V, Nm);
        Rm.y = max(Rm.y, 0.004);
        Rm = normalize(Rm);
        float2 hitUV;
        float conf = HDMarch(i.wpos, Rm, i.pos.xy, hitUV);
        if (conf > 0.0)
        {
            float2 wob = (N.xz - Nm.xz) * HD_SSR_WOBBLE;
            float4 met = tex2Dlod(_CameraOpaqueTexture, float4(saturate(hitUV + wob), 0.0, 0.0));
            reflToned = lerp(reflToned, met.rgb, conf);
            reflEmit = met.a * conf;
        }
    }

    // ---- the sun's (the moon's) highlight ----
    float3 H = normalize(L + V);
    float specPow = lerp(HD_SPEC_CALM, HD_SPEC_ROUGH, rough);
    float spec = pow(saturate(dot(N, H)), specPow) * (specPow + 8.0) / 25.13;
    spec = min(spec, HD_SPEC_MAX) * saturate(L.y * 4.0) * lerp(1.0, GLINT_NIGHT, _PSXNight);
    float3 sunGlint = _PSXLightColor.rgb * spec * fres * 4.0 * shadow * expo;
    float3 lampGlint = (lampS + headS) * LAMP_GAIN * (0.3 + fres);

    // ---- foam: the shore, white water, whitecaps ----
    float swash = sin(t * (6.2831853 / FOAM_SWASH_S) + i.wpos.x * 0.05 + i.wpos.z * 0.03) * FOAM_RUN_M * i.data.y;
    float shoreD = max(downM - swash, 0.0);
    float shore = saturate(1.0 - shoreD / HD_FOAM_M);
    float laceMix = laceA * 0.55 + laceB * 0.45;
    float cap = saturate((i.swell.y - WHITECAP) / (1.0 - WHITECAP));
    float foamWant = max(max(shore * shore, white), cap * cap) * _WaterLook.z;
    float foam = saturate((laceMix + foamWant - 1.0) * 2.4 + foamWant * 0.35);
    foam *= saturate(1.0 - eyeDist / 600.0);

    // ---- together ----
    float edge = saturate(downM / HD_SHORE_SOFT_M);
    float3 fogCol = PSXFogTone(PSXFogTowardSun(_PSXFogColor.rgb * PSXFogRing(-V, _PSXSkyRotation), V));
    float fog = i.swell.w;
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
    return fixed4(col, _PSXEmitWrite > 0.5 ? emit * (1.0 - fog) : 1.0);
}

#endif
