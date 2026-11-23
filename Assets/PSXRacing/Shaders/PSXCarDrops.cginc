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

// ---- CARDROPS 2 (sprayfix, 2026-11-23; the owner, PC61: "these water droplets are MASSIVE. It makes
// the car look like a Hot Wheel by scale. Similarly, these water streaks are MASSIVE. And it doesn't
// make sense that there are streaks moving in a straight light past droplets that aren't moving").
// The pattern above ran in metres x CD_SCALE 0.45, so a bead's 0.12-0.30 of a 4-7 cm cell was a
// 1-4 cm DOME and a runner a 2.4 cm head on a straight 10 cm column, drawn on top of beads that did not
// know it was there. Now (PSX_CARDROPS2, default on; _PSXCarDrops2Off.x 1 = a99a8ef1's body rain):
//   * BEADS in the body's own metres, real size: 2-6 mm mixed and a few merged ones up to 10 mm. They
//     keep that size on every car and panel; a bead under a pixel is drawn as its AVERAGE (a wet sheen,
//     no crawl), so nothing grows with distance.
//   * RIVULETS 2-5 mm wide, of varied length, wandering (two sines along their run), along the panel's
//     own fall line parked and swung back along the air over that panel as the car goes (gravity and
//     air projected on the panel, so a roof, a door and a boot lid each run their own way); parked they
//     creep, pause and go.
//   * ONE WATER: a rivulet's track is wiped of beads behind its head (it took them with it) and the
//     beads come back further up the track as it dries - no rivulet runs through a standing bead.
//   * Moving, the larger beads creep back along the air with the car's clock; the small ones stay.
// Snow is unchanged.
float4 _PSXCarDrops2Off;   // x 1: a99a8ef1's body rain; y 1: a99a8ef1's lens drop size (PSXLens)
static float cd2Wipe;      // the rivulets' wipe at this pixel (CarDropsH -> CarBeadLens, same invocation)

#define CD2_CELL_S   0.0070   // small beads: cell (m) ...
#define CD2_RS_MIN   0.150    // ... radius as a share of it: 1.05 - 2.9 mm (2 - 6 mm across)
#define CD2_RS_MAX   0.415
#define CD2_CELL_L   0.0160   // the merged ones: 3.8 - 8.3 mm across on a flat panel (10 at a 35-degree slant), fewer
#define CD2_RL_MIN   0.120
#define CD2_RL_MAX   0.260
#define CD2_COL_W    0.0140   // rivulet columns (m)
#define CD2_HEAD_MIN 0.0012   // rivulet head radius (m): 2.4 - 5 mm across
#define CD2_HEAD_MAX 0.0025
#define CD2_TRAIL    0.55     // the track's half-width as a share of the head's (under 3 mm wide)

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

// (CARDROPS 2) one bead layer in metres, its sub-pixel average blended in by the pixel's footprint
// (`fp`, metres per pixel). `creep`: the layer's offset (m) along `adir` (the larger beads blown back).
float CD2Beads(float2 q, float cell, float rMin, float rMax, float density, float seed, float fp, float2 creep)
{
    q -= creep;
    float2 g = q / cell;
    float2 id = floor(g);
    float4 h = CDHash(id + seed);
    float2 c = (id + 0.5 + (h.xy - 0.5) * 0.5) * cell;
    float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
    float alive = step(h.z, density) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
    float r = cell * lerp(rMin, rMax, h.w);
    float dome = CDDome(q - c, r) * alive;
    // the average: a dome's mean over its cell (half its disc's share), the alive share ~0.75
    float rM = cell * 0.5 * (rMin + rMax);
    float avg = saturate(density) * 0.75 * 0.5 * 3.14159 * rM * rM / (cell * cell);
    float resolved = saturate((rM / max(fp, 1e-6) - 0.6) / 0.9);
    return lerp(avg, dome, resolved);
}

// (CARDROPS 2) the small bead layer as a lens (CDBead4's form: x height, yz where in the bead, w radius m)
float4 CD2Bead4(float2 q, float cell, float rMin, float rMax, float density, float seed)
{
    float2 g = q / cell;
    float2 id = floor(g);
    float4 h = CDHash(id + seed);
    float2 c = (id + 0.5 + (h.xy - 0.5) * 0.5) * cell;
    float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
    float alive = step(h.z, density) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
    float r = cell * lerp(rMin, rMax, h.w);
    float2 n = (q - c) / r;
    return float4(saturate(1.0 - dot(n, n)) * alive, n, r);
}

// (CARDROPS 2) Rivulets along `dir` (unit, in the panel's plane), on the clock `t` (s): returns the water
// height; `wipe` = how much of the beads the passing rivulets have taken here (0..1).
float CD2Rivulets(float2 q, float2 dir, float t, float density, float seed, float fp, float parked, out float wipe)
{
    float2 side = float2(dir.y, -dir.x);
    float along = dot(q, dir);
    float x = dot(q, side) / CD2_COL_W;
    float col = floor(x);
    float4 h = CDHash(float2(col, seed));
    // varied length per column and per run: 6 - 30 cm a cycle
    float span = lerp(0.06, 0.30, h.z);
    float speed = lerp(0.6, 1.4, h.x) * lerp(0.5, 0.2, parked);
    float tt = t * speed / span;
    // parked: it creeps, pauses and goes (a smooth stair), moving: steadily
    tt = lerp(tt, floor(tt * 3.0) / 3.0 + smoothstep(0.25, 0.75, frac(tt * 3.0)) / 3.0, parked);
    float y = along / span - tt + h.y * 7.0;
    float cyc = floor(y);
    float4 k = CDHash(float2(col, cyc + seed * 3.1));
    float on = step(k.x, density);
    float f = frac(y);
    // the wander: two sines along the run, a few mm
    float wob = (sin(along * 41.0 + k.y * 6.28) * 0.6 + sin(along * 97.0 + k.z * 6.28) * 0.4) * 0.0022;
    float dx = (frac(x) - 0.5 - (k.z - 0.5) * 0.4) * CD2_COL_W - wob;
    float rHead = lerp(CD2_HEAD_MIN, CD2_HEAD_MAX, k.w);
    float headAt = 0.92;
    float head = CDDome(float2(dx, (f - headAt) * span), rHead);
    // the track behind the head: a thin film, thinning with distance back, ending at a varied length
    float back = (headAt - f) * span;                    // metres behind the head (negative ahead)
    float len = span * lerp(0.35, 0.85, k.y);
    float tr = step(0.0, back) * (1.0 - smoothstep(len * 0.6, len, back));
    float halfW = max(rHead * CD2_TRAIL * lerp(1.0, 0.55, saturate(back / max(len, 1e-4))), 1e-4);
    float trail = saturate(1.0 - abs(dx) / halfW) * tr * 0.55;
    // the wipe: the head's own width along its track, fading as the beads come back
    // (whole across the head's width and a little more, so no bead pixel is left inside the water)
    float wHalf = rHead * 1.2;
    wipe = on * (1.0 - smoothstep(wHalf, wHalf + fp + 0.0008, abs(dx))) * step(-rHead * 1.2, back)
         * (1.0 - smoothstep(len * 0.8, len * 1.6, back));
    float hgt = max(head, trail) * on;
    // under a pixel wide: its average, not a crawling line
    float res = saturate((rHead / max(fp, 1e-6) - 0.5) / 1.0);
    return lerp(hgt * saturate(rHead * 2.0 / max(fp, 1e-6)), hgt, res);
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
    cd2Wipe = 0.0;
    if (rain > 0.002 && _PSXCarDrops2Off.x < 0.5)
    {
        // (CARDROPS 2) real size, one water
        float2 qm = p.xz * topM + p.zy * sideM + p.xy * endM;
        float fp = max(length(fwidth(qm)), 1e-6);
        // the rivulets' way on THIS panel: its fall line parked, swung back along the air with speed
        // two runs, cross-faded on the speed (each on its own clock, so a change of speed never
        // sweeps the pattern): PARKED down this panel's fall line, creeping and pausing; MOVING back
        // along the air over this panel, bent by its fall line
        float2 md = adir * smoothstep(0.15, 0.5, alen) * 1.6 + gdir * glen * 0.5;
        float ml = length(md);
        float wipeP, wipeM;
        float hRP = CD2Rivulets(qm, gdir, _Time.y, rain * 0.10 * smoothstep(0.3, 0.7, glen), 4.1, fp, 1.0, wipeP);
        float hRM = CD2Rivulets(qm, md / max(ml, 1e-4), blowT * 2.0, rain * 0.40 * smoothstep(0.05, 0.3, ml), 7.7, fp, 0.0, wipeM);
        float hR = max(hRP * (1.0 - streakW), hRM * streakW);
        float wipe = max(wipeP * (1.0 - streakW), wipeM * streakW);
        cd2Wipe = wipe;
        float2 creep = adir * (blowT * 0.004 * streakW);
        float hS = CD2Beads(qm, CD2_CELL_S, CD2_RS_MIN, CD2_RS_MAX, rain * lerp(0.60, 0.45, streakW), 1.7, fp, float2(0, 0));
        float hL = CD2Beads(qm, CD2_CELL_L, CD2_RL_MIN, CD2_RL_MAX, rain * lerp(0.30, 0.08, streakW), 9.3, fp, creep);
        float hB = max(hS, hL) * (1.0 - wipe);
        hRain = max(hB, hR);
    }
    else if (rain > 0.002)
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

// (RAINGLASS 2026-11-22) THE BEADS AS LITTLE LENSES, read from the chase
// camera. A bead on a car is a tiny convex mirror: whatever lights the scene -
// the sky by day, every street lamp and headlamp at night - shows in it as one
// bright point near its upper edge, and its rim is a dark ring. The old look
// lit a bead only where its SCREEN-space slope faced the light, which a bead of
// two or three pixels does not have, so from the chase view the glass carried
// next to nothing. This gives the bead layer's own dome (x height, yz where in
// the bead, w its radius) in the same plane as CarDropsH, and how many pixels
// across it is, for the caller's highlight and rim. More beads on GLASS
// (`glass` 0..1: the paint's own glass mask), which sheds less of them.
float4 CDBead4(float2 q, float cell, float density, float seed)
{
    float2 g = q / cell;
    float2 id = floor(g);
    float4 h = CDHash(id + seed);
    float2 c = (id + 0.5 + (h.xy - 0.5) * 0.5) * cell;
    float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
    float alive = step(h.z, density) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
    float r = cell * lerp(0.12, 0.30, h.w);
    float2 n = (q - c) / r;
    return float4(saturate(1.0 - dot(n, n)) * alive, n, r);
}

// The bead layer at a point (see above); `up2` = the plane's up (against
// gravity, unit), `pxR` the bead's radius in pixels.
float4 CarBeadLens(float3 p, float3 n, float3 down, float wind, float glass, out float2 up2, out float pxR)
{
    float3 a = abs(n);
    float topM = step(a.x, a.y) * step(a.z, a.y);
    float sideM = (1.0 - topM) * step(a.z, a.x);
    float endM = 1.0 - topM - sideM;
    float2 q = (p.xz * topM + p.zy * sideM + p.xy * endM) * CD_SCALE;
    float2 gd = down.xz * topM + down.zy * sideM + down.xy * endM;
    up2 = -gd / max(length(gd), 1e-4);
    float rain = _PSXCarDrops.x;
    float streakW = saturate(wind * 1.5);
    float dens = rain * lerp(1.0, 1.45, glass);
    if (_PSXCarDrops2Off.x < 0.5)
    {
        // (CARDROPS 2) the same small beads, in metres (the larger ones creep and are left to CarDropsH),
        // none in a rivulet's wiped track
        float2 qm = q / CD_SCALE;
        float4 bs = CD2Bead4(qm, CD2_CELL_S, CD2_RS_MIN, CD2_RS_MAX, rain * lerp(0.60, 0.45, streakW), 1.7);
        bs.x *= 1.0 - cd2Wipe;
        float fpm = max(length(fwidth(qm)), 1e-6);
        pxR = bs.w / fpm;
        return bs;
    }
    float4 b = CDBead4(q, 0.018, dens * lerp(0.55, 0.12, streakW), 1.7);
    float4 b2 = CDBead4(q, 0.032, dens * lerp(0.35, 0.08, streakW), 9.3);
    b = b.x >= b2.x ? b : b2;
    float fp = max(length(fwidth(q)), 1e-6);
    pxR = b.w / fp;
    return b;
}

// (RAINGLASS2 2026-11-22, Rain2.CarBeads, PSX_RAIN2_CARBEADS: _PSXRain2Glass3.x) A bead on dark glass by
// day is NOT a white point: it is a little convex mirror of the SKY, so it shows a soft highlight over its
// upper half and a darker lower rim, and only a real point light (the sun when it shows, a street lamp, a
// headlamp) makes the sharp bright point. fe3ce8a6's bead put the whole sky into one point - the rear glass
// read as white speckle on black. `pointL` = those point lights' colour, already exposed.
float4 _PSXRain2Glass3;
float3 CarBeadShade2(float3 col, float4 bl, float2 up2, float lens, float resolved, float glass, float3 sky, float3 pointL)
{
    float rN = length(bl.yz);
    float upK = dot(bl.yz, up2);
    float inB = step(0.001, lens);
    float ring = smoothstep(0.62, 0.97, rN) * inB * (0.55 + 0.45 * saturate(0.3 - upK));
    float dome = saturate(0.40 + 0.70 * upK) * (1.0 - smoothstep(0.72, 1.0, rN)) * smoothstep(0.0, 0.3, lens);
    float pt = (1.0 - smoothstep(0.0, 0.24, length(bl.yz - up2 * 0.5))) * inB;
    float3 res = sky * (dome * lerp(0.16, 0.30, glass)) + pointL * pt;
    float3 unres = (sky * 0.08 + pointL * 0.12) * smoothstep(0.0, 0.2, lens);
    col *= 1.0 - ring * resolved * lerp(0.22, 0.50, glass);
    return col + lerp(unres, res, resolved) * lerp(0.5, 1.0, glass);
}

#endif
