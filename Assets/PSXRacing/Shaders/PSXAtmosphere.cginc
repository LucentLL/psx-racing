// THE DYNAMIC SKY, 2026-09-26. The owner, after Tidewater (dgreenheck/tidewater,
// MIT, whose sky is a Hillaire atmosphere): "Are you able to attempt a dynamic
// skybox like the github repo?" - as a SWITCH beside the photo skies (SkyModePrefs),
// so the two can be compared on the live build.
//
// Tidewater renders its atmosphere through precomputed LUTs on a desktop GPU.
// This is the same physics sized for a phone at 240-480 lines: a single-
// scattering march (Nishita) through a Rayleigh + Mie atmosphere, eight steps
// along the view and three toward the sun, done ONLY for the sky's own pixels
// (and for a 256x128 reflection panorama every few seconds). Everything that
// makes it read as a real sky comes out of the one sun direction: blue overhead,
// white-blue haze at the horizon, the orange band and the purple opposite at a
// low sun, the grey-blue after it sets.
//
// Over it: a cloud deck at CLOUD_ALT, value-noise fbm drifting on the wind,
// lit from the sun's side with a self-shadow tap toward it and a silver edge;
// the sun's disc and glow; at night the moon (Tidewater's two-exponential
// aureole) and, from PSX/Sky, the stars.
//
// Globals (TimeOfDay.ApplySky, dynamic mode):
//   _PSXSunTrue   xyz = direction TO the sun (below the horizon at night)
//   _PSXMoonDir   xyz = direction to the moon, w = 1 when it is up
//   _PSXCloudCover 0..1, _PSXCloudWind xy (uv/s), _PSXDynExposure
#ifndef PSX_ATMOSPHERE_INCLUDED
#define PSX_ATMOSPHERE_INCLUDED

float4 _PSXSunTrue;
float4 _PSXMoonDir;
float _PSXCloudCover;
float4 _PSXCloudWind;
float _PSXDynExposure;

#define ATMO_R_GROUND   6360e3
#define ATMO_R_TOP      6420e3
#define ATMO_BETA_R     float3(5.8e-6, 13.5e-6, 33.1e-6)
#define ATMO_BETA_M     21e-6
#define ATMO_H_R        7994.0
#define ATMO_H_M        1200.0
#define ATMO_G          0.76
#define ATMO_SUN        22.0
#define ATMO_VIEW_STEPS 8
#define ATMO_SUN_STEPS  3
// THE CLOUDS HAVE VOLUME (owner, 2026-09-26, with a photograph of fair-weather
// cumulus: "Clouds could use more volume"). A flat deck of noise was a sheet
// of fog painted on a ceiling; a march through a slab (the first answer) drew
// its steps as horizontal slices - "stacks of grainy pancakes". Now each
// cumulus is a THICKNESS over a flat base (CLOUD_BASE), up toward CLOUD_TOP:
// met by the ray with a parallax offset, lit from its own slope, shadowed by
// what lies sunward - white lit flanks, grey-blue bases, a silver thin edge.
#define CLOUD_BASE      1400.0
#define CLOUD_TOP       2900.0
#define CLOUD_SCALE     0.00032   // coverage cells per metre
#define CLOUD_DETAIL    0.0021    // billow cells per metre

#define SUN_DISC_COS    0.99985   // ~1.0 degree radius: a PS1 sun is a big sun
#define MOON_DISC_COS   0.99992
#define NIGHT_FLOOR     float3(0.006, 0.009, 0.020)
#define SKY_PRE_SAT     1.35      // the scattered sky's saturation, before the grade

float2 AtmoRaySphere(float3 o, float3 d, float r)
{
    float b = dot(o, d);
    float c = dot(o, o) - r * r;
    float h = b * b - c;
    if (h < 0.0) return float2(-1.0, -1.0);
    h = sqrt(h);
    return float2(-b - h, -b + h);
}

/// Single-scattered sky radiance along dir, for a sun in sunDir.
float3 AtmoScatter(float3 dir, float3 sunDir)
{
    float3 o = float3(0.0, ATMO_R_GROUND + 2.0, 0.0);
    // Below the horizon the ray would hit the planet; march it as if it
    // grazed, which is the haze the fog band paints over anyway.
    dir.y = max(dir.y, 0.0);
    dir = normalize(dir);
    float tMax = AtmoRaySphere(o, dir, ATMO_R_TOP).y;
    float seg = tMax / ATMO_VIEW_STEPS;
    float mu = dot(dir, sunDir);
    float phaseR = 3.0 / (16.0 * 3.14159265) * (1.0 + mu * mu);
    float g2 = ATMO_G * ATMO_G;
    float phaseM = 3.0 / (8.0 * 3.14159265) * ((1.0 - g2) * (1.0 + mu * mu)) /
                   ((2.0 + g2) * pow(max(1.0 + g2 - 2.0 * ATMO_G * mu, 1e-4), 1.5));
    float odR = 0.0, odM = 0.0;
    float3 sumR = 0.0, sumM = 0.0;
    for (int i = 0; i < ATMO_VIEW_STEPS; i++)
    {
        float3 p = o + dir * (seg * (i + 0.5));
        float h = length(p) - ATMO_R_GROUND;
        float hr = exp(-h / ATMO_H_R) * seg, hm = exp(-h / ATMO_H_M) * seg;
        odR += hr; odM += hm;
        float tl = AtmoRaySphere(p, sunDir, ATMO_R_TOP).y;
        float segL = tl / ATMO_SUN_STEPS;
        float odRL = 0.0, odML = 0.0;
        bool lit = true;
        for (int j = 0; j < ATMO_SUN_STEPS; j++)
        {
            float3 q = p + sunDir * (segL * (j + 0.5));
            float hl = length(q) - ATMO_R_GROUND;
            if (hl < 0.0) { lit = false; break; }
            odRL += exp(-hl / ATMO_H_R) * segL;
            odML += exp(-hl / ATMO_H_M) * segL;
        }
        if (lit)
        {
            float3 tau = ATMO_BETA_R * (odR + odRL) + ATMO_BETA_M * 1.1 * (odM + odML);
            float3 att = exp(-tau);
            sumR += att * hr;
            sumM += att * hm;
        }
    }
    return ATMO_SUN * (sumR * ATMO_BETA_R * phaseR + sumM * ATMO_BETA_M * phaseM);
}

/// How much of the sun gets through the air to the ground: its colour.
float3 AtmoSunColor(float3 sunDir)
{
    float3 o = float3(0.0, ATMO_R_GROUND + 2.0, 0.0);
    float tl = AtmoRaySphere(o, sunDir, ATMO_R_TOP).y;
    float segL = tl / 6.0;
    float odR = 0.0, odM = 0.0;
    for (int j = 0; j < 6; j++)
    {
        float3 q = o + sunDir * (segL * (j + 0.5));
        float hl = max(length(q) - ATMO_R_GROUND, 0.0);
        odR += exp(-hl / ATMO_H_R) * segL;
        odM += exp(-hl / ATMO_H_M) * segL;
    }
    return exp(-(ATMO_BETA_R * odR + ATMO_BETA_M * 1.1 * odM)) * saturate(sunDir.y * 20.0 + 1.0);
}

float AtmoHash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float AtmoNoise(float2 p)
{
    float2 c = floor(p), f = p - c;
    float2 s = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(AtmoHash(c), AtmoHash(c + float2(1, 0)), s.x),
                lerp(AtmoHash(c + float2(0, 1)), AtmoHash(c + float2(1, 1)), s.x), s.y);
}

float AtmoFbm(float2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += AtmoNoise(p) * a; p = p * 2.03 + 11.7; a *= 0.5; }
    return v;
}

float AtmoFbm3(float2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 3; i++) { v += AtmoNoise(p) * a; p = p * 2.07 + 7.3; a *= 0.5; }
    return v;
}

/// How thick the cloud is over a point, 0..1 of the layer: the coverage
/// field decides where a cumulus stands and how tall, a finer noise eats
/// its edges into billows. A THICKNESS MAP, not a density in the volume -
/// see the cloud pass in PSXDynamicSky for why.
float AtmoCloudThickness(float2 xz)
{
    float2 wind = _PSXCloudWind.xy * _Time.y;
    float c = AtmoFbm3(xz * CLOUD_SCALE + wind);
    float cut = 1.0 - _PSXCloudCover;
    // A firm body: the coverage crosses into cloud over a shortish band, so
    // a cumulus is a mass with an edge, not a smear of haze.
    float t = saturate((c - cut * 0.82) / 0.26);
    float det = AtmoNoise(xz * CLOUD_DETAIL + wind * 3.1) * 0.6 +
                AtmoNoise(xz * CLOUD_DETAIL * 2.3 + wind * 4.7) * 0.4;
    // Billows only at the edges, and only ever eating in (never adding cloud
    // to clear sky - adding it turned the whole sky to marbled overcast):
    // the heart of a cloud stays whole.
    t -= det * 0.35 * (1.0 - smoothstep(0.25, 0.7, t));
    return saturate(t * 1.45);
}

/// The whole dynamic sky in direction dir (unit, world), before the horizon
/// band and the stars, which PSX/Sky adds as it does for a photograph.
/// Returned in the display's range (exposed, rolled off).
float3 PSXDynamicSky(float3 dir)
{
    float3 sunDir = normalize(_PSXSunTrue.xyz);
    float3 col = AtmoScatter(dir, sunDir);
    // Into the film grade a little over-coloured: PSX/Blit takes the whole
    // picture's saturation down to a faded print, and the scattered blue
    // went in as blue and came out steel grey. The clouds are not boosted.
    float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
    col = max(lerp(luma.xxx, col, SKY_PRE_SAT), 0.0);

    // The sun's disc and a tight glow, through the air it shines through.
    float3 sunCol = AtmoSunColor(sunDir);
    float mu = dot(dir, sunDir);
    col += sunCol * (step(SUN_DISC_COS, mu) * 40.0 + pow(saturate(mu), 900.0) * 6.0);

    // Night: a floor under the scattered light, and the moon.
    float night = saturate(-sunDir.y * 6.0);
    col += NIGHT_FLOOR * night * (1.0 + 0.8 * saturate(1.0 - dir.y * 2.0));
    if (_PSXMoonDir.w > 0.5)
    {
        float3 md = normalize(_PSXMoonDir.xyz);
        float mc = dot(dir, md);
        float a = acos(clamp(mc, -1.0, 1.0));
        float3 moon = float3(0.62, 0.70, 0.95);
        col += moon * (exp(-14.0 * a) * 0.10 + exp(-2.5 * a) * 0.025) * night;
        col += float3(0.95, 0.93, 0.85) * step(MOON_DISC_COS, mc) * 2.2 * night;
    }

    // THE CLOUDS, AS A LAYER WITH VOLUME. The march through the slab (the
    // first answer to "clouds could use more volume") drew each of its ten
    // steps as its own slice where the ray runs nearly flat toward the
    // horizon - "stacks of grainy pancakes", in the owner's words - and a
    // phone paid ~100 noise evaluations a sky pixel for it. Now: where a
    // cloud stands and how tall is a thickness map; the ray meets it at the
    // layer's base and once more half way up what stands there (parallax, so
    // a tall cloud rises over its own base); it is lit from the map's own
    // slope toward the sun, self-shadowed by what lies sunward, darker in its
    // thick heart and at its base, silver at a thin edge toward the sun.
    // Six evaluations, no steps, no slices.
    if (dir.y > 0.01 && _PSXCloudCover > 0.01)
    {
        float camY = 2.0;
        float2 cam = _WorldSpaceCameraPos.xz;
        float2 p0 = cam + dir.xz * ((CLOUD_BASE - camY) / dir.y);
        float th = AtmoCloudThickness(p0);
        float midY = CLOUD_BASE + th * (CLOUD_TOP - CLOUD_BASE) * 0.5;
        float2 p1 = cam + dir.xz * ((midY - camY) / dir.y);
        th = AtmoCloudThickness(p1);
        if (th > 0.002)
        {
            // Opacity: thicker is denser, and a cloud seen edge-on (low in
            // the sky) is looked through more of it.
            float path = th / max(dir.y * 3.0, 0.25);
            float alpha = smoothstep(0.0, 0.12, th) * (1.0 - exp(-path * 6.0));
            // The slope of the map is the cloud's surface.
            float e = 90.0;
            float gx = AtmoCloudThickness(p1 + float2(e, 0.0)) - th;
            float gz = AtmoCloudThickness(p1 + float2(0.0, e)) - th;
            float3 n = normalize(float3(-gx * 5.0, 0.3 + th * 0.35, -gz * 5.0));
            float sunUp = saturate(sunDir.y * 4.0 + 0.3);
            float shadow = exp(-AtmoCloudThickness(p1 + sunDir.xz * 380.0) * 1.7);
            float lambert = saturate(dot(n, sunDir) * 0.6 + 0.4);
            float silver = pow(saturate(mu), 10.0) * (1.0 - th) * 1.8;
            float3 skyTop = AtmoScatter(float3(0, 1, 0), sunDir) * 1.4 + NIGHT_FLOOR * 2.0 * night;
            float3 ambient = skyTop * lerp(1.0, 0.45, th);             // the thick heart and base go grey-blue
            float3 lum = sunCol * (2.2 * lambert * shadow + silver) * sunUp + ambient;
            // The far layer thins into the haze.
            float fade = saturate(dir.y * 6.0);
            col = lerp(col, lum, alpha * fade);
        }
    }

    // Exposure and a soft shoulder: the display's range.
    col *= _PSXDynExposure;
    return 1.0 - exp(-col);
}
#endif
