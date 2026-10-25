// THE VOLUMETRIC CLOUDS (2026-10-25). The owner, of the dynamic sky he plays
// with: "Those clouds need to be overhauled because they look strange and too
// repetitive" - the cumulus were clusters of SPHERES dealt onto a 2.8 km grid
// (AtmoPuff), the same few cotton balls in rows to the horizon. He chose real
// volumetric clouds: 3D cloud bodies ray-marched through a slab of layered
// noise, lit by the sun, drifting; cheap because they are drawn small and
// reused across frames.
//
// THE TECHNIQUE (Schneider's Nubis, sized for WebGL 2):
//   * WHERE CLOUD MAY STAND - a 2D weather map (tools/sky/gen_cloud_noise.py,
//     46 km tile): R coverage (equalised, so a coverage of 0.4 is 40% of the
//     ground), G kind (flat fair-weather humilis .. towering congestus), B a
//     base-height jitter. The day's weather (DynamicSky.Configure from
//     Seasons.Now's SKY layer) sets the threshold, the kind bias, the slab
//     and an overcast DECK.
//   * THE BODY - a 128^3 Perlin-Worley texture, cut by the coverage and a
//     height profile (round base, crown tapering later the taller the kind),
//     eroded by a 64^3 Worley fbm: wisps under the base, billows on top.
//   * THE LIGHT - Beer's law toward the light over five samples (930 m),
//     three scattering octaves (Wrenninge) through a two-lobe phase: a
//     silver lining toward a low sun, bright flanks, dark cores; ambient from
//     the sky above (crowns) and from the ground below (bases, and a lit
//     city's glow at night). Far clouds fade into the haze (aerial).
//   * THE COST - not marched per screen pixel. The clouds live in a DOME MAP
//     (direction -> cloud, 2048 x 512: azimuth across, sqrt(elevation) up so
//     the horizon gets the rows), and each frame marches ONE texel of every
//     4 x 4 block (65k rays, a compact 512 x 128 target - no divergence), the
//     other fifteen reprojected from the last frame by the eye's move and the
//     wind. Every view - the main camera, the mirrors, the reflection faces,
//     the car paint's panorama - reads the same map: one texture fetch.
//
// The map stores the cloud's response, not its colour: R light (sun or moon,
// phase and Beer included), G sky light from above, B light from below, A the
// transmittance of the sky behind. PSXDynamicSky multiplies in the hour's
// colours, so the same map serves any tint.
#ifndef PSX_VOLCLOUDS_INCLUDED
#define PSX_VOLCLOUDS_INCLUDED

sampler2D _PSXVolMap;
float _PSXVolOn;          // 1 = these clouds (VolumetricClouds.Active), 0 = the old puffs
float4 _PSXVolCov;        // x coverage, y kind bias (0 flat .. 1 towering), z overcast deck 0..1, w storm 0..1
float4 _PSXVolSlab;       // x cloud base (m over the eye's ground), y tallest cumulus (m over base), z deck thickness (m), w cirrus 0..1
float4 _PSXVolLightDir;   // xyz toward the light the clouds are lit by (the sun; the moon at night)
float4 _PSXVolEye;        // xz where the map was marched from (world)

// THE CITY'S GLOW, declared here so the clouds can take it on their bases;
// PSX/Sky draws it in the air (CityGlow) - see the note there.
float4 _PSXCityGlow;
float4 _PSXCityGlowCol;

#define VOL_PI          3.14159265
#define VOL_EYE_ALT     2.0
#define VOL_MAX_T       80000.0   // a cloud deck farther than this is haze
#define VOL_AERIAL      30000.0   // e-folding range of a cloud into the haze (m)

/// Dome map layout: u = azimuth (PSXSkyIn's: atan2(z, x)), v = sqrt of the
/// elevation's share of 90 degrees - a quarter of the rows for the lowest
/// 5.6 degrees, where the far clouds are thin lines.
float2 VolUV(float3 dir)
{
    float u = atan2(dir.z, dir.x) * (0.5 / VOL_PI) + 0.5;
    float el = asin(clamp(dir.y, 0.0, 1.0)) * (2.0 / VOL_PI);
    return float2(u, sqrt(el));
}

float3 VolDir(float2 uv)
{
    float az = (uv.x - 0.5) * (2.0 * VOL_PI);
    float el = uv.y * uv.y * (0.5 * VOL_PI);
    float ce = cos(el);
    return float3(ce * cos(az), sin(el), ce * sin(az));
}

/// The cloud along dir, as the map holds it (no mips: a plain fetch has no
/// seam at the azimuth wrap).
float4 VolFetch(float3 dir)
{
    return tex2D(_PSXVolMap, VolUV(dir));
}

/// Roughly how far the clouds are along dir: the range to the middle of the
/// base layer, over a round earth. Reprojection and the city's glow use it.
float VolCloudRange(float3 dir)
{
    float3 o = float3(0.0, 6360e3 + VOL_EYE_ALT, 0.0);
    float r = 6360e3 + _PSXVolSlab.x + 350.0;
    float b = dot(o, dir);
    float c = dot(o, o) - r * r;
    float t = -b + sqrt(max(b * b - c, 0.0));
    return clamp(t, 300.0, VOL_MAX_T);
}

/// A lit city lights the cloud over it: the glow's colour by how far the
/// cloud's base along dir lies from the city's heart, in its radii.
float3 VolCityUnder(float3 dir)
{
    if (_PSXCityGlow.w <= 0.0) return float3(0, 0, 0);
    float2 p = _PSXVolEye.xz + dir.xz * VolCloudRange(dir);
    float r = length(p - _PSXCityGlow.xz) / max(_PSXCityGlow.y, 1.0);
    return _PSXCityGlowCol.rgb * (_PSXCityGlow.w * exp(-r * r));
}

#ifdef PSX_VOLCLOUDS_MARCH
// ---- the march (Hidden/PSX/VolClouds only) -------------------------------
sampler3D _PSXVolShape;
sampler3D _PSXVolDetail;
sampler2D _PSXVolWeather;
float4 _PSXVolWind;       // xy the field's drift (m), z the shapes' slow rise (m), w frame
float4 _PSXVolSteps;      // x view steps, y step floor (m), z march span cap (m), w jitter on

#define VOL_WEATHER_TILE 46000.0
#define VOL_SHAPE_TILE   5750.0    // 46 km / 8: one cell of the body ~1.4 km
#define VOL_DETAIL_TILE  821.0     // the body's tile / 7
#define VOL_SHAPE_UP     0.77      // the body's noise stretched 1.3x upward: heaps, not layers
#define VOL_SIGMA        0.04      // extinction per metre at density 1
#define VOL_EROSION      0.30
#define VOL_MAX_STEPS    64

struct VolColumn
{
    float cov;      // 0..1 how much of a cloud field this column is
    float kind;     // 0 flat humilis .. 1 towering congestus
    float baseAlt;  // its base (m)
    float thick;    // base to crown (m)
};

VolColumn VolColumnAt(float2 xz)
{
    float4 wm = tex2Dlod(_PSXVolWeather, float4((xz + _PSXVolWind.xy) * (1.0 / VOL_WEATHER_TILE), 0, 0));
    VolColumn c;
    float C = _PSXVolCov.x;
    c.cov = smoothstep(1.0 - C - 0.12, 1.0 - C + 0.16, wm.r);
    c.kind = saturate(wm.g * 0.6 + _PSXVolCov.y * 0.7 - 0.15);
    c.baseAlt = _PSXVolSlab.x + (wm.b - 0.5) * 320.0;
    c.thick = lerp(400.0, max(_PSXVolSlab.y, 400.0), c.kind) * (0.6 + 0.4 * c.cov);
    return c;
}

float VolShapeAt(float2 xz, float alt)
{
    // The shapes ride a touch faster than their fields, and off the world's
    // axes (23.5 degrees), so no tile edge ever lines up with a road.
    float2 q = xz + _PSXVolWind.xy * 1.06;
    q = float2(q.x * 0.917 - q.y * 0.399, q.x * 0.399 + q.y * 0.917);
    return tex3Dlod(_PSXVolShape, float4(q.x * (1.0 / VOL_SHAPE_TILE), (alt + _PSXVolWind.z) * (VOL_SHAPE_UP / VOL_SHAPE_TILE),
                                         q.y * (1.0 / VOL_SHAPE_TILE), 0)).r;
}

/// A cumulus' density with height (hf = 0 base .. 1 crown): a round base
/// over the bottom 8%, a body, a crown that tapers - later for a tall kind.
float VolProfile(float hf, float kind)
{
    float b = saturate(hf * 12.0);
    float t = 1.0 - smoothstep(lerp(0.22, 0.6, kind), 1.0, hf);
    return b * (2.0 - b) * t;
}

/// Density at a point (no erosion), and how far up its cloud it is.
float VolDensity(VolColumn c, float alt, float shape, out float hf)
{
    hf = (alt - c.baseAlt) / c.thick;
    float d = 0.0;
    if (hf > 0.0 && hf < 1.0 && c.cov > 0.0)
    {
        // Coverage carves the noise: a thin field leaves only the noise's
        // peaks (small separate clouds), a full one merges them.
        float lo = 1.0 - c.cov * 0.58;
        d = saturate((shape * VolProfile(hf, c.kind) - lo) / (1.0 - lo));
    }
    // THE OVERCAST DECK: a layer with a lumpy base (the same noise), as
    // thick as the weather says, under or around the cumulus.
    if (_PSXVolCov.z > 0.0)
    {
        float db = _PSXVolSlab.x - 120.0 + (shape - 0.5) * 300.0;
        float dt = _PSXVolSlab.x + _PSXVolSlab.z;
        float deck = smoothstep(db, db + 160.0, alt) * (1.0 - smoothstep(dt - 280.0, dt + 60.0, alt));
        float dd = deck * _PSXVolCov.z * (0.22 + 0.5 * shape);
        if (dd > d) { d = dd; hf = saturate((alt - db) / max(dt - db, 1.0)); }
    }
    return d;
}

/// Henyey-Greenstein, scaled so isotropic is 1.
float VolHG(float c, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5);
}

/// The cloud along dir: rgb = (light, sky above, ground below) responses,
/// a = transmittance of what lies behind.
float4 VolMarch(float3 dir, float jitter)
{
    if (dir.y < 0.0) return float4(0, 0, 0, 1);
    float3 o = float3(0.0, 6360e3 + VOL_EYE_ALT, 0.0);
    float lo = _PSXVolSlab.x - 330.0;
    float hi = max(_PSXVolSlab.x + 170.0 + _PSXVolSlab.y, _PSXVolSlab.x + _PSXVolSlab.z + 80.0);
    float t0 = AtmoRaySphere(o, dir, 6360e3 + lo).y;
    float t1 = AtmoRaySphere(o, dir, 6360e3 + hi).y;
    if (t0 > VOL_MAX_T) return float4(0, 0, 0, 1);
    float steps = _PSXVolSteps.x;
    float tEnd = min(min(t1, t0 + _PSXVolSteps.z), VOL_MAX_T);
    float ds = max((tEnd - t0) / steps, _PSXVolSteps.y);
    float t = t0 + ds * jitter;

    float3 L = normalize(_PSXVolLightDir.xyz);
    float cosT = dot(dir, L);
    // Three octaves of scattering (Wrenninge): each dimmer, less peaked and
    // reaching deeper - the soft bright body a single Beer term cannot give.
    float ph0 = lerp(VolHG(cosT, 0.80), VolHG(cosT, -0.25), 0.25);
    float ph1 = lerp(VolHG(cosT, 0.40), VolHG(cosT, -0.12), 0.25);
    float ph2 = lerp(VolHG(cosT, 0.20), VolHG(cosT, -0.06), 0.25);
    float sigma = VOL_SIGMA * (1.0 + _PSXVolCov.w * 0.8);

    float T = 1.0;
    float4 acc = 0.0;
    [loop]
    for (int i = 0; i < VOL_MAX_STEPS; i++)
    {
        if (i >= (int)steps || t > tEnd || T < 0.02) break;
        float3 pw = o + dir * t;
        float alt = length(pw) - 6360e3;
        float2 xz = _PSXVolEye.xz + dir.xz * t;
        VolColumn c = VolColumnAt(xz);
        if (c.cov <= 0.0 && _PSXVolCov.z <= 0.0) { t += ds * 2.0; continue; }   // clear air: stride
        float hf;
        float d = VolDensity(c, alt, VolShapeAt(xz, alt), hf);
        if (d > 0.0)
        {
            float2 qd = (xz + _PSXVolWind.xy * 1.12) * (1.0 / VOL_DETAIL_TILE);
            float det = tex3Dlod(_PSXVolDetail, float4(qd.x, (alt + _PSXVolWind.z * 1.6) * (1.0 / VOL_DETAIL_TILE), qd.y, 0)).r;
            // wisps under the base, billows over the crown
            float m = lerp(det, 1.0 - det, saturate(hf * 5.0)) * VOL_EROSION;
            d = saturate((d - m) / (1.0 - m));
        }
        if (d > 0.002)
        {
            float sig = d * sigma;
            // Toward the light: five samples over 930 m, the body only.
            float tau = 0.0;
            [unroll]
            for (int j = 0; j < 5; j++)
            {
                float seg = j == 0 ? 40.0 : j == 1 ? 70.0 : j == 2 ? 130.0 : j == 3 ? 240.0 : 450.0;
                float mid = j == 0 ? 20.0 : j == 1 ? 75.0 : j == 2 ? 175.0 : j == 3 ? 360.0 : 705.0;
                float3 q = pw + L * mid;
                float altq = length(q) - 6360e3;
                float2 xzq = xz + L.xz * mid;
                float hq;
                tau += VolDensity(c, altq, VolShapeAt(xzq, altq), hq) * seg;
            }
            tau *= sigma;
            float e = ph0 * exp(-tau) + 0.5 * ph1 * exp(-tau * 0.35) + 0.25 * ph2 * exp(-tau * 0.12);
            // powder: a thin edge facing away from the light keeps less of it
            float powder = 1.0 - exp(-sig * 300.0);
            e *= lerp(powder, 1.0, saturate(cosT * 0.5 + 0.5));
            float h = saturate(hf);
            // The sky's light from above reaches in by diffusion, not by
            // Beer: dimmer under more cloud, never black (the light march's
            // depth stands in for the cloud over the sample).
            float ambTop = lerp(0.32, 1.0, h) * (0.4 + 0.6 / (1.0 + tau * 0.12));
            float ambBot = (1.0 - h) * (1.0 - h);
            float dT = exp(-sig * ds);
            float w = T * (1.0 - dT) * exp(-t * (1.0 / VOL_AERIAL));
            acc += w * float4(e, ambTop, ambBot, 1.0);
            T *= dT;
        }
        t += ds;
    }
    return float4(acc.rgb, 1.0 - acc.a);
}
#endif
#endif
