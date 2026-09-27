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
float4 _PSXZenith;       // the single-scattered zenith (DynamicSky, CPU): the light the
                         // multiple-scattering stand-in lends the low sky

#define ATMO_R_GROUND   6360e3
#define ATMO_R_TOP      6420e3
#define ATMO_BETA_R     float3(5.8e-6, 13.5e-6, 33.1e-6)
#define ATMO_BETA_M     21e-6
#define ATMO_H_R        7994.0
#define ATMO_H_M        1200.0
#define ATMO_G          0.76
#define ATMO_SUN        22.0
#define ATMO_VIEW_STEPS 8
// THE HORIZON IS NOT YELLOW (owner, 2026-09-27: "Sunset/sunrise shouldn't be
// 360"). Marched to the top of the atmosphere, a ray 3 degrees up crosses
// hundreds of km of air; single scattering along that path strips every
// bit of blue and has nothing to put it back, so the whole horizon came out
// gold at sunset and yellow-grey at NOON. Three stand-ins for what the real
// sky has and an 8-step march does not (Hillaire, whose sky Tidewater uses,
// has all three properly):
//   VIEW_CAP  - the march stops at this range: past it the light that reaches
//               us is multiply scattered, not the single-scattered sliver left.
//   MS        - that multiple scattering: the zenith's own light, lent to the
//               air a ray crosses in proportion to how much air it is.
//   OZONE     - absorbs orange-yellow (the Chappuis band) on the long sun paths
//               of a low sun: why a twilight sky away from the sun is blue-grey,
//               not green-grey. Carried on the Rayleigh column for cheapness.
#define ATMO_VIEW_CAP   60000.0
#define ATMO_MS         0.6
#define ATMO_BETA_O     float3(0.650e-6, 1.881e-6, 0.085e-6)
#define ATMO_OZONE      3.5
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
    float tMax = min(AtmoRaySphere(o, dir, ATMO_R_TOP).y, ATMO_VIEW_CAP);
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
            float3 tau = ATMO_BETA_R * (odR + odRL) + ATMO_BETA_M * 1.1 * (odM + odML) +
                         ATMO_BETA_O * ATMO_OZONE * (odR + odRL);
            float3 att = exp(-tau);
            sumR += att * hr;
            sumM += att * hm;
        }
    }
    float3 single = ATMO_SUN * (sumR * ATMO_BETA_R * phaseR + sumM * ATMO_BETA_M * phaseM);
    float air = 1.0 - exp(-(ATMO_BETA_R.g * odR + ATMO_BETA_M * odM));
    return single + ATMO_MS * _PSXZenith.rgb * air;
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
    return exp(-(ATMO_BETA_R * odR + ATMO_BETA_M * 1.1 * odM + ATMO_BETA_O * ATMO_OZONE * odR)) *
           saturate(sunDir.y * 20.0 + 1.0);
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

// ---- cumulus puffs --------------------------------------------------------
#define PUFF_CELL       2800.0    // one cloud slot per cell of this size (m)
#define PUFF_BASE       1500.0    // the flat base every cloud sits on
#define PUFF_COUNT      8         // most puffs a cell draws
#define PUFF_R_MIN      240.0
#define PUFF_R_MAX      560.0
#define PUFF_SQUASH     0.68      // a puff is this tall for its width: heaped, not a ball
#define PUFF_SOFT       0.62      // how far in from the rim the edge is soft (share of r)
#define PUFF_OCCUPY     0.72      // share of _PSXCloudCover that becomes cloud cells
// KINDS OF CLOUD (owner, 2026-09-27, with photographs of towering and broken
// cumulus: "There should be cloud variance, not just one type/size"). Every
// cell used to hold the same heap at the same scale, so the sky was one cloud
// and its copies shrinking toward the horizon. A cell now holds one of three:
#define KIND_HUMILIS    0.38      // under this: a SPRAY of small flat fair-weather clouds
#define KIND_CONGESTUS  0.80      // over this: one TOWERING heap, taller than it is wide
// ...and a thin high layer of CIRRUS streaks above them all, in patches.
#define CIRRUS_ALT      8500.0
#define CIRRUS_SCALE    0.00012
#define CIRRUS_MAX      0.55

/// Puff k of the cloud in a cell: its centre, radius, flat base and how tall it
/// is for its width, or false past the cell's own puff count. NOTHING REGULAR
/// (owner: "a bit too defined, symmetrical, and repetitive") and now NOT ONE
/// KIND (owner: "cloud variance, not just one type/size"):
///   HUMILIS   - two to four small flat clouds spread across the cell, each a
///               main puff and a smaller companion: the broken fair-weather sky.
///   MEDIOCRIS - the heap there was: 3-7 puffs about the biggest, stretched
///               along its own heading, 0.5-1.4 of the base size.
///   CONGESTUS - a big heap that GROWS UP: puffs stacked in tiers, shrinking
///               toward a cauliflower crown, taller for their width.
bool AtmoPuff(float2 cell, int k, float2 wind, out float3 c, out float r, out float baseY, out float squash)
{
    c = 0.0; r = 1.0; baseY = PUFF_BASE; squash = PUFF_SQUASH;
    float kind = AtmoHash(cell + 21.7);
    float lift = (AtmoHash(cell + 6.1) - 0.5) * 360.0;
    if (kind < KIND_HUMILIS)
    {
        int clouds = 2 + (int)(AtmoHash(cell + 12.3) * 2.99);          // 2..4
        int j = k / 2;
        if (j >= clouds) return false;
        float2 home = (cell + 0.08 + 0.84 * float2(AtmoHash(cell * 1.9 + j * 7.1), AtmoHash(cell * 2.7 + j * 3.9)))
                      * PUFF_CELL + wind;
        float rj = lerp(110.0, 280.0, AtmoHash(cell * 4.1 + j * 2.3));
        squash = 0.5;
        baseY = PUFF_BASE + lift * 0.5 + (AtmoHash(cell + j * 8.1) - 0.5) * 140.0;
        if (k == j * 2) { r = rj; c = float3(home.x, baseY + r * squash * 0.35, home.y); return true; }
        float ang = AtmoHash(cell * 5.3 + j * 1.7) * 6.2831853;
        r = rj * lerp(0.55, 0.8, AtmoHash(cell * 6.7 + j * 4.3));
        float2 off = float2(cos(ang), sin(ang)) * rj * 0.85;
        c = float3(home.x + off.x, baseY + r * squash * 0.3, home.y + off.y);
        return true;
    }
    bool tower = kind > KIND_CONGESTUS;
    int count = tower ? 6 + (int)(AtmoHash(cell + 12.3) * 2.99)       // 6..8
                      : 3 + (int)(AtmoHash(cell + 12.3) * 4.99);      // 3..7
    if (k >= count) return false;
    float2 ctr = (cell + 0.15 + 0.7 * float2(AtmoHash(cell + 7.7), AtmoHash(cell + 1.9))) * PUFF_CELL + wind;
    float size = tower ? lerp(1.15, 1.7, AtmoHash(cell + 4.4))
                       : lerp(0.5, 1.4, AtmoHash(cell + 4.4) * AtmoHash(cell + 8.8) + 0.2);
    float head = AtmoHash(cell + 2.6) * 6.2831853;
    float stretch = tower ? 1.0 : lerp(1.0, 2.2, AtmoHash(cell + 5.5));
    float2 ax = float2(cos(head), sin(head)), ay = float2(-ax.y, ax.x);
    float ang = AtmoHash(cell * 3.1 + k * 17.3) * 6.2831853;
    // ONE flat base for the whole cloud: cut per puff, every puff left a
    // shelf at its own height inside the cloud.
    baseY = PUFF_BASE + lift;
    if (tower)
    {
        // Tiers: the higher the puff, the nearer the axis and the smaller.
        float tier = k == 0 ? 0.0 : (k + AtmoHash(cell * 1.3 + k * 2.9) * 0.8) / count;
        float spread = k == 0 ? 0.0 : lerp(0.25, 0.75, AtmoHash(cell * 2.3 + k * 5.1)) * (1.0 - tier * 0.6);
        float2 off = (ax * cos(ang) + ay * sin(ang)) * spread * PUFF_R_MAX * 0.8 * size;
        r = PUFF_R_MAX * size * lerp(1.0, 0.55, tier) * (k == 0 ? 1.0 : lerp(0.8, 1.0, AtmoHash(cell * 1.7 + k * 9.7)));
        squash = 0.9;
        c = float3(ctr.x + off.x, baseY + r * squash * 0.5 + tier * 1250.0, ctr.y + off.y);
        return true;
    }
    float spread = k == 0 ? 0.0 : lerp(0.3, 1.2, AtmoHash(cell * 2.3 + k * 5.1));
    float2 off = (ax * cos(ang) * stretch + ay * sin(ang)) * spread * PUFF_R_MAX * 0.8 * size;
    r = lerp(PUFF_R_MIN, PUFF_R_MAX, k == 0 ? 1.0 : AtmoHash(cell * 1.7 + k * 9.7)) * size * (k == 0 ? 1.0 : 0.85);
    c = float3(ctr.x + off.x, baseY + r * squash * (k == 0 ? 0.5 : lerp(0.05, 0.4, AtmoHash(cell + k * 3.3))), ctr.y + off.y);
    return true;
}

/// Front-most soft puff along the view ray, among the clouds in the 3x3 cells
/// around where the ray crosses the layer; rgb lit colour, a coverage. Puffs
/// are squashed spheres with their bottoms cut flat at PUFF_BASE; the light
/// takes a normal BLENDED over every puff near the hit (a metaball's), so
/// where two puffs meet there is a soft valley and no crease.
float4 AtmoPuffs(float3 dir, float3 sunDir, float3 sunCol, float mu, float night)
{
    float3 cam = float3(_WorldSpaceCameraPos.x, 2.0, _WorldSpaceCameraPos.z);
    float2 wind = _PSXCloudWind.xy * _Time.y * 4000.0;      // metres drifted
    // Cells picked where the ray is at the clouds' middle height - a little
    // higher than it was, for the towers.
    float tMid = (PUFF_BASE + 550.0 - cam.y) / max(dir.y, 0.02);
    float2 cell0 = floor((cam.xz + dir.xz * tMid - wind) / PUFF_CELL);
    float bestT = 1e20, coverA = 0.0, bestBase = PUFF_BASE, bestTall = PUFF_R_MAX * PUFF_SQUASH;
    float3 bestP = 0.0;
    for (int cy = -1; cy <= 1; cy++)
    for (int cx = -1; cx <= 1; cx++)
    {
        float2 cell = cell0 + float2(cx, cy);
        if (AtmoHash(cell * 1.37 + 3.1) > _PSXCloudCover * PUFF_OCCUPY) continue;
        for (int k = 0; k < PUFF_COUNT; k++)
        {
            float3 c; float r; float baseY; float squash;
            if (!AtmoPuff(cell, k, wind, c, r, baseY, squash)) break;
            float3 sq = float3(1.0, 1.0 / squash, 1.0);          // into round space
            float3 d = dir * sq;
            float3 oc = (cam - c) * sq;
            float qa = dot(d, d), qb = dot(oc, d), qc = dot(oc, oc) - r * r;
            float disc = qb * qb - qa * qc;
            if (disc <= 0.0) continue;
            float t = (-qb - sqrt(disc)) / qa;
            if (t <= 0.0) continue;
            float3 p = cam + dir * t;
            if (p.y < baseY)
            {
                float tb = (baseY - cam.y) / max(dir.y, 1e-4);
                float3 pb = cam + dir * tb;
                float3 e = (pb - c) * sq;
                if (dot(e, e) > r * r) continue;
                t = tb; p = pb;
            }
            // A soft, broken rim: how far inside the silhouette the ray runs,
            // roughed by a noise so the edge is cotton, not a cut-out.
            float closest = sqrt(max(dot(oc, oc) - qb * qb / qa, 0.0));
            float rough = (AtmoNoise(p.xz * 0.006 + p.y * 0.004) * 0.6 + AtmoNoise(p.xz * 0.017) * 0.4 - 0.5) * 0.8;
            float a = saturate((r - closest) / (r * PUFF_SOFT) + rough);
            if (a <= 0.02) continue;
            // COVERAGE IS THE UNION: a cloud is as solid as its most solid
            // puff here. Taking the front puff's own rim alpha let the puffs
            // behind show through it - translucent shells stacked in a jar.
            coverA = max(coverA, a);
            if (t >= bestT) continue;
            bestT = t; bestP = p; bestBase = baseY; bestTall = max(c.y + r * squash - baseY, 1.0);
        }
    }
    if (coverA <= 0.0 || bestT >= 1e19) return float4(0, 0, 0, 0);
    float bestA = coverA;

    // The blended normal: every puff near the hit pulls it toward itself,
    // weighted by how deep in its reach the hit lies.
    float3 n = 0.0;
    for (int cy2 = -1; cy2 <= 1; cy2++)
    for (int cx2 = -1; cx2 <= 1; cx2++)
    {
        float2 cell = cell0 + float2(cx2, cy2);
        if (AtmoHash(cell * 1.37 + 3.1) > _PSXCloudCover * PUFF_OCCUPY) continue;
        for (int k = 0; k < PUFF_COUNT; k++)
        {
            float3 c; float r; float baseY; float squash;
            if (!AtmoPuff(cell, k, wind, c, r, baseY, squash)) break;
            float3 e = (bestP - c) * float3(1.0, 1.0 / squash, 1.0);
            float dist = length(e);
            float w = saturate(1.35 - dist / r);
            n += (e / max(dist, 1.0)) * w * w;
        }
    }
    n = normalize(n + float3(0, 0.05, 0));
    if (bestP.y <= bestBase + 0.5) n = normalize(n * 0.3 + float3(0, -1, 0));
    // Above the base nothing faces the ground: a tower's upper puffs stand out
    // sideways past the ones under them, and their bellies read as grey lenses
    // stuck to its flank. Only the real base is a dark underside.
    else n = normalize(float3(n.x, max(n.y, -0.12), n.z));
    // Cauliflower: a little noise in the normal.
    float3 np = bestP * 0.004;
    float3 bump = float3(AtmoNoise(np.xz + 3.0), AtmoNoise(np.zx + 7.0), AtmoNoise(np.xy + 11.0)) - 0.5;
    n = normalize(n + bump * 0.35);

    // How far up ITS OWN cloud the hit is: measured against the biggest puff
    // there could be, a 70 m fair-weather cumulus was all belly - tan and dull.
    float h = saturate((bestP.y - bestBase) / bestTall);
    float sunUp = saturate(sunDir.y * 4.0 + 0.3);
    float wrap = saturate(dot(n, sunDir) * 0.38 + 0.62);       // soft, wrapped light: cotton, not plastic
    float under = lerp(0.6, 1.0, h);                           // grey bellies, bright crowns
    float silver = pow(saturate(mu), 8.0) * (1.0 - bestA) * 1.6;
    float3 skyTop = AtmoScatter(float3(0, 1, 0), sunDir) * 1.45 + NIGHT_FLOOR * 2.0 * night;
    float3 lum = sunCol * (2.0 * wrap * under + silver) * sunUp + skyTop * lerp(0.65, 1.0, h);
    return float4(lum, bestA);
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

    // THE CLOUDS: CUMULUS MADE OF PUFFS. The owner, twice: "clouds could use
    // more volume" and then, of a noise layer shaded from its own slope,
    // "creepy looking clouds. Not fluffy or comforting, but sharp and
    // smeared". Noise at 240 lines is smeared by nature. So each cloud is a
    // cluster of soft ROUND puffs on a flat base (AtmoPuffs): each lit like a
    // dome - white where it faces the sun, soft grey underneath and in the
    // shade of its neighbours - with a fuzzy rim that goes silver toward the
    // sun. The fair-weather cumulus of the owner's photograph.
    // CIRRUS: thin streaks far above the cumulus, in patches - some days
    // a sky full, most of it none. Drawn first; the heaps go in front. Lit by
    // the sun through the air, so at a low sun they burn orange-pink.
    if (dir.y > 0.01 && _PSXCloudCover > 0.01)
    {
        float tc = (CIRRUS_ALT - 2.0) / max(dir.y, 0.03);
        float2 q = (_WorldSpaceCameraPos.xz + dir.xz * tc) * CIRRUS_SCALE + _PSXCloudWind.xy * _Time.y * 1.6;
        float2 cax = float2(0.92, 0.39);
        float2 qs = float2(dot(q, cax) * 0.28, dot(q, float2(-cax.y, cax.x)) * 1.7);   // long along, fine across
        float patchy = saturate((AtmoNoise(q * 0.21 + 5.0) - 0.42) * 3.2);
        if (patchy > 0.0)
        {
            float cir = saturate((AtmoFbm(qs) - 0.5) * 2.8) * patchy * CIRRUS_MAX;
            if (cir > 0.005)
            {
                float3 cirCol = sunCol * 1.25 * saturate(sunDir.y * 4.0 + 0.6) +
                                AtmoScatter(float3(0, 1, 0), sunDir) * 0.8 + NIGHT_FLOOR * night;
                col = lerp(col, cirCol, cir * saturate((dir.y - 0.02) * 6.0));
            }
        }
    }

    if (dir.y > 0.005 && _PSXCloudCover > 0.01)
    {
        float4 cl = AtmoPuffs(dir, sunDir, sunCol, mu, night);
        // The far rows thin into the haze (and stop reading as rows).
        float fade = saturate((dir.y - 0.015) * 7.0);
        col = lerp(col, cl.rgb, cl.a * fade);
    }

    // Exposure and a soft shoulder: the display's range.
    col *= _PSXDynExposure;
    return 1.0 - exp(-col);
}
#endif
