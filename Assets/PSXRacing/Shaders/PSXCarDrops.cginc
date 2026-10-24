// THE RAIN AND SNOW ON A CAR'S BODY (2026-10-24; the owner, from the
// cockpit in the rain: "The water particle effects on windows in cockpit view
// would look nice on the exterior of cars in rain. Currently they are shiny,
// but with no water droplets. (Could be white and move differently when its
// snow)").
//
// PSX/CabinGlass's drops, cut down for the paint (PSX/CarPaint), on every
// car: the player's, the rivals', the traffic. They live in the BODY's own
// frame, in metres (the mesh axes scaled by the renderer's scale), so they
// ride the car and stay round; each panel is projected on its largest axis
// (a roof or a bonnet on x-z, a door on z-y, a bumper on x-y).
//   RAIN  beads that land and dry off, two sizes; runners DOWN the panel
//         (gravity in the body's frame) while the car is slow; streaks
//         blown along the AIR at speed (_PSXCarWind: xyz the air's
//         direction in the body's frame, w 0 parked .. 1 at speed, per car,
//         CarLights). Runners and streaks cross-fade on w. The streaks run
//         on the car's own clock (_PSXCarRun: x its value at time y, rising
//         z a second - faster the faster the car; CarLights re-bases it
//         whenever the speed band changes), like the windscreen's: they race
//         back along the body at speed and a change of speed never makes the
//         pattern jump (owner, same day: the beads "should similarly streak
//         during movement like they do on windshield").
//   SNOW  white flakes that SETTLE and stay - more as the snow goes on, most
//         where the panel faces up - and at speed blow back along the air in
//         long thin wisps; snow never runs down a panel.
// _PSXCarDrops (global, CarLights from WeatherFx): x rain 0..1, y snow 0..1,
// both eased in and 0 under cover. Zero = nothing is evaluated.
#ifndef PSX_CAR_DROPS_INCLUDED
#define PSX_CAR_DROPS_INCLUDED

float4 _PSXCarDrops;
float4 _PSXCarRun;

// The body is seen from metres away, not from half a metre like the
// cockpit's glass: its drops are drawn this much larger (the pattern's
// metres x this), so a bead is a few pixels from the chase lens.
#define CD_SCALE 0.45

float4 CDHash(float2 p)
{
    float4 p4 = frac(float4(p.xyxy) * float4(0.1031, 0.1030, 0.0973, 0.1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

// A dome of height 1 at the centre, 0 at radius r.
float CDDome(float2 d, float r) { return saturate(1.0 - dot(d, d) / (r * r)); }

// Beads on a grid of `cell` metres. `cycle` 1: each comes and goes (rain);
// 0: once there it stays (settled snow, more cells as `density` rises).
float CDBeads(float2 q, float cell, float density, float seed, float cycle)
{
    float2 g = q / cell;
    float2 id = floor(g);
    float4 h = CDHash(id + seed);
    float2 c = (id + 0.5 + (h.xy - 0.5) * 0.5) * cell;
    float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
    float alive = step(h.z, density) * lerp(1.0, smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life)), cycle);
    float r = cell * lerp(0.12, 0.30, h.w);
    return CDDome(q - c, r) * alive;
}

// Drops travelling along `dir` in columns `width` wide, `span` metres a
// cycle, on the clock `t`: a head bead and a thinning trail behind it.
float CDRunners(float2 q, float2 dir, float width, float span, float t, float density, float seed, float trailK)
{
    float2 side = float2(dir.y, -dir.x);
    float x = dot(q, side) / width;
    float col = floor(x);
    float4 h = CDHash(float2(col, seed));
    float speed = lerp(0.6, 1.4, h.x);
    float y = dot(q, dir) / span - t * speed + h.y * 7.0;
    float cyc = floor(y);
    float4 k = CDHash(float2(col, cyc + seed * 3.1));
    float on = step(k.x, density);
    float f = frac(y);
    float wob = sin(f * 9.0 + k.y * 6.28) * 0.12 * (1.0 - trailK);
    float dx = (frac(x) - 0.5 - (k.z - 0.5) * 0.4 - wob) * width;
    float rHead = width * lerp(0.14, 0.24, k.w);
    float head = CDDome(float2(dx, (f - 0.92) * span), rHead);
    float tr = saturate((f - 0.10) / 0.78) * step(f, 0.9);
    float beadsOn = step(0.45, frac(f * span / (rHead * 3.0) + k.y));
    // (thicker than the glass's 0.35: the body is seen from metres away)
    float trail = saturate(1.0 - abs(dx) / (rHead * 0.6 * tr + 1e-4)) * tr
                * lerp(beadsOn, 1.0, trailK) * 0.8;
    return max(head, trail) * on;
}

// The water's height (0..1) at a point of the body, and the settled snow's
// (`snowH`). `p` and `n` in the body's frame (metres, mesh axes), `down` and
// `air` directions in it, `up` how much the panel faces the sky (0..1),
// `wind` 0 parked .. 1 at speed.
float CarDropsH(float3 p, float3 n, float3 down, float3 air, float up, float wind, out float snowH)
{
    float3 a = abs(n);
    // the panel's largest axis picks the plane (as masks: no divergent flow)
    float topM = step(a.x, a.y) * step(a.z, a.y);
    float sideM = (1.0 - topM) * step(a.z, a.x);
    float endM = 1.0 - topM - sideM;
    float2 q = (p.xz * topM + p.zy * sideM + p.xy * endM) * CD_SCALE;
    float2 gd = down.xz * topM + down.zy * sideM + down.xy * endM;
    float2 ad = air.xz * topM + air.zy * sideM + air.xy * endM;
    float glen = length(gd), alen = length(ad);
    float2 gdir = gd / max(glen, 1e-4), adir = ad / max(alen, 1e-4);
    float rain = _PSXCarDrops.x, snow = _PSXCarDrops.y;
    // the streaks' clock, and how much of the water they have taken (from
    // walking pace up: a moving car's beads are blown back long before 40 mph)
    float blowT = _PSXCarRun.x + _PSXCarRun.z * (_Time.y - _PSXCarRun.y);
    float streakW = saturate(wind * 1.5);

    float hRain = 0.0;
    if (rain > 0.002)
    {
        float hB = max(CDBeads(q, 0.018, rain * lerp(0.55, 0.12, streakW), 1.7, 1.0),
                       CDBeads(q, 0.032, rain * lerp(0.35, 0.08, streakW), 9.3, 1.0));
        float hR = CDRunners(q, gdir, 0.030, 0.35, _Time.y * 0.35, rain * 0.45 * smoothstep(0.3, 0.7, glen), 4.1, 0.0) * (1.0 - streakW);
        float hS = CDRunners(q, adir, 0.045, 0.85, blowT, rain * smoothstep(0.15, 0.5, alen), 7.7, 0.9) * streakW;
        hRain = max(hB, max(hR, hS));
    }
    snowH = 0.0;
    if (snow > 0.002)
    {
        float hF = CDBeads(q, 0.022, snow * lerp(0.10, 0.80, up) * (1.0 - 0.6 * wind), 3.3, 0.0);
        float hW = CDRunners(q, adir, 0.030, 0.90, blowT * 1.3, snow * 0.35 * smoothstep(0.15, 0.5, alen), 5.9, 1.0) * streakW * 0.6;
        snowH = max(hF, hW);
    }
    return hRain;
}

#endif
