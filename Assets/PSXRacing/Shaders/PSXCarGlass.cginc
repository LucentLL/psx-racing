// DAMAGE ROUND 2 (2026-11-16): GLASS THAT READS AS BROKEN, AND WHEEL RASH, on PSX/CarPaint.
// The owner on PC48: "the broken glass effect is very poor and does not communicate the damage to me.
// I'd also like to see scuffing for wheels (hitting curbs, etc)". The first cut drew one noise-contour
// web over every dark texel above the belt: black panes covered in squiggles.
//
// Now each PANE breaks the way its glass does, where the incident struck it (DamageGlass.cs sets the
// pane states and the strike points per renderer, from the car's damage rows; seeded by the incident):
//   THE WINDSCREEN (laminated): a stone chip (a pit, a cone ring, short legs); a crack running from it;
//     a spider web (radial cracks and broken rings round the strike, crushed white at its centre, each
//     shard tilted its own way and the web dished in, so the reflections break up). The glass between
//     the cracks stays glass.
//   THE SIDE AND REAR GLASS (tempered): crazed (the whole pane gone to white cubes, runs out of the
//     strike); then shattered (a hole: jagged crazed remnants hang in the frame, the cabin shows
//     through - seat backs, head restraints, the far side's glass - with the parallax of a real one).
//   A BROKEN LAMP: its lens in shards, some of them gone.
// The panes are found as the paint finds its glass (the sheet's dark colourless texels: there is no
// glass submesh), told apart by the way they face, and kept off the seals by sampling the sheet round
// the pixel (how far the paint is). No per-car art.
//   _PSXGlsOn   1 = this path (PSX_DMG_GLASS2); 0 = the first cut's web
//   _PSXGlsS    pane states: x windscreen (1/3 chip, 2/3 crack, 1 web), y rear, z left, w right
//               (tempered: 0.5 crazed, 1 shattered)
//   _PSXGlsW    the windscreen's strike: x across, y up (m from the box centre), z seed, w the way its crack runs
//   _PSXGlsX    the rear glass's: x across, y up, z seed
//   _PSXGlsL T  the left / right glass's: x along, y up, z seed
//   _PSXGlsF    the cabin: x the belt (m above the box centre), y the front seat backs (m along),
//               z the roof (m above the centre), w the half width (m)
// WHEEL RASH (WheelScuff.cs; on a wheel's renderer only): scraped bright metal on the rim's lip and
// scuffed rubber on the sidewall, over an arc of the wheel that grows with the stage.
//   _PSXWheelRash  x amount (1/3, 2/3, 1), y where on the wheel (rad), z the tyre's radius (m), w seed
// Needs PSX_CAR_TAP(uv, ddxUv, ddyUv) = the sheet's colour there, and PSXCarDamage.cginc before it.
#ifndef PSX_CAR_GLASS_INCLUDED
#define PSX_CAR_GLASS_INCLUDED

#include "PSXGlassBreak.cginc"

float4 _PSXGlsS, _PSXGlsW, _PSXGlsX, _PSXGlsL, _PSXGlsT, _PSXGlsF, _PSXWheelRash;
// DAMAGE ROUND 3 (DamageCabin.cs, DamageGlass.cs; owner 11-17: "If shown, the cockpit model should be used"):
//   _PSXGlsDark  global, 1 = a hole shows a DARK cabin here (the lining, the floor, the far glass; no seat
//                boxes) and the cockpit model is drawn into it by DamageCabin within reach; 0 = round 2's boxes
//   _PSXGlsThin  global, 1 = a THIN dark strip with paint on both sides (a door mirror's sail outline, a pillar's
//                trim line: under 7 cm across) is not a pane, whatever its colour (round 2 crazed it white)
float _PSXGlsDark;
float _PSXGlsThin;

// The same, repeating every per cells along x (round a wheel: no seam where the angle wraps).
float GlsN2P(float2 p, float per)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float x0 = i.x - per * floor(i.x / per), x1 = (i.x + 1.0) - per * floor((i.x + 1.0) / per);
    return lerp(lerp(GlsH2(float2(x0, i.y)), GlsH2(float2(x1, i.y)), f.x),
                lerp(GlsH2(float2(x0, i.y + 1.0)), GlsH2(float2(x1, i.y + 1.0)), f.x), f.y);
}

// The paint's own glass rule: the sheet's dark, colourless texels.
float GlsIsGlass(float3 c)
{
    float lum = dot(c, float3(0.30, 0.59, 0.11));
    float hi3 = max(c.r, max(c.g, c.b)), lo3 = min(c.r, min(c.g, c.b));
    return saturate((0.22 - lum) / 0.22) * (1.0 - saturate((hi3 - lo3) / max(hi3, 0.08) * 2.5 - 0.75));
}



// A STONE CHIP: the pit, a broken cone ring, five short legs (6 cm across).
float GlsChip(float2 q, float seed, float px)
{
    float r = length(q), a = atan2(q.y, q.x) + seed * 6.2831853;
    float kh;
    float da = GlsRay(a, 5.0, seed, kh);
    float len = lerp(0.03, 0.062, GlsH1(kh * 3.7 + seed * 17.0));
    float m = 1.0 - smoothstep(0.007, 0.013 + px, r);
    m = max(m, GlsLine(r * da, 0.003, px) * step(r, len));
    m = max(m, GlsLine(r - 0.021, 0.003, px) * step(GlsH1(kh + seed * 5.0), 0.6));
    return m;
}

// A CRACK RUNNING from the strike along dir for len metres, wandering as it goes.
float GlsRunner(float2 q, float2 dir, float len, float seed, float px)
{
    float s = dot(q, dir), t = dot(q, float2(-dir.y, dir.x));
    float wob = (GlsN1(s * 4.0 + seed * 31.0) - 0.5) * 0.16 * saturate(s * 3.0)
              + (GlsN1(s * 26.0 + seed * 7.0) - 0.5) * 0.014 * saturate(s * 20.0);
    return GlsLine(t - wob, 0.0032, px) * step(0.0, s) * step(s, len);
}

// A SPIDER WEB of radius R: radial cracks (a few run on past it), rings broken between them, fine
// legs and crushed white at the strike. cell = which shard (for its tilt).
float GlsWeb(float2 q, float seed, float px, float R, out float2 cell)
{
    float r = length(q), a = atan2(q.y, q.x) + seed * 6.2831853;
    float kh;
    float da = GlsRay(a, 15.0, seed, kh);
    float h = GlsH1(kh * 5.17 + seed * 29.0);
    float len = R * (h > 0.78 ? lerp(1.7, 3.4, GlsH1(kh + seed * 3.0)) : lerp(0.7, 1.1, h / 0.78));
    float wig = (GlsN1(r * 18.0 + kh * 9.1 + seed * 13.0) - 0.5) * 0.10;
    float m = GlsLine(r * (da + wig * saturate(r * 8.0)), 0.0032, px) * step(r, len);
    float ringK = log(max(r, 1e-4) / 0.028) / log(1.42);
    float ri = floor(ringK);
    float rj = GlsH2(float2(kh, ri) + seed * 19.0);
    float rho = 0.028 * pow(1.42, ri + 0.5 + (rj - 0.5) * 0.6);
    float present = step(GlsH2(float2(ri, kh) + seed * 41.0), lerp(0.96, 0.35, saturate(r / R)));
    m = max(m, GlsLine(r - rho, 0.0026, px) * present * step(0.0, ri) * step(r, R));
    float kh2;
    float da2 = GlsRay(a + 1.7, 34.0, seed + 0.37, kh2);
    m = max(m, GlsLine(r * da2, 0.0022, px) * step(r, 0.05 + 0.07 * GlsH1(kh2 * 1.3 + seed)));
    m = max(m, 1.0 - smoothstep(0.012, 0.035 + px, r));
    cell = float2(kh, max(ri, -1.0));
    return m;
}



void GlsBox(float3 o, float3 d, float3 inv, float3 c, float3 h, float shade, inout float tBest, inout float3 col)
{
    float3 t0 = (c - h - o) * inv, t1 = (c + h - o) * inv;
    float3 tn3 = min(t0, t1), tf3 = max(t0, t1);
    float tn = max(max(tn3.x, tn3.y), tn3.z), tf = min(min(tf3.x, tf3.y), tf3.z);
    if (tn < tf && tn > 0.0 && tn < tBest)
    {
        tBest = tn;
        // the face it enters by, lit by the windows' light from above and ahead; darker toward the floor
        float3 nrm = -sign(d) * step(tn3.yzx, tn3.xyz) * step(tn3.zxy, tn3.xyz);
        float lit = 0.55 + 0.85 * saturate(dot(nrm, float3(0.12, 0.86, 0.49)));
        float low = saturate((o.y + d.y * tn - (c.y - h.y)) / max(h.y * 2.0, 0.05));
        col = shade * lit * lerp(0.6, 1.0, low) * float3(1.0, 1.0, 1.06);
    }
}

// THE CABIN THROUGH A HOLE: o the point in the pane, d the eye's ray into the car - car frame
// (x across, y up, z along), metres from the box centre. Dark trim; the two front seats with their
// head restraints, the rear seat's back, the dash; the roof lining; the far side's glass and the
// ends' (the daylight outside, dimmed). Returned as a surface colour: the cabin's light lights it.
float3 GlsCabin(float3 o, float3 d)
{
    float belt = _PSXGlsF.x, sz = _PSXGlsF.y, roof = _PSXGlsF.z, hx = _PSXGlsF.w;
    float gh = max(roof - belt, 0.25);
    float3 dd = float3(abs(d.x) < 1e-4 ? 1e-4 : d.x, abs(d.y) < 1e-4 ? 1e-4 : d.y, abs(d.z) < 1e-4 ? 1e-4 : d.z);
    float3 inv = 1.0 / dd;
    float3 glassC = float3(0.36, 0.40, 0.45), trim = float3(0.045, 0.045, 0.05);
    float t = 1e9;
    float3 col = float3(0.02, 0.02, 0.022);
    float tw = ((d.x > 0.0 ? 1.0 : -1.0) * hx * 0.94 - o.x) * inv.x;            // the far side
    if (tw > 0.0)
    {
        float3 pw = o + d * tw;
        float g = smoothstep(belt + 0.02, belt + 0.06, pw.y) * (1.0 - smoothstep(roof - 0.14, roof - 0.09, pw.y))
                * smoothstep(sz - 1.25, sz - 1.15, pw.z) * (1.0 - smoothstep(sz + 0.85, sz + 1.0, pw.z))
                * smoothstep(0.03, 0.06, abs(pw.z - (sz - 0.14)));               // its pillar
        col = lerp(trim, glassC, g);
        t = tw;
    }
    float ty = ((d.y > 0.0 ? roof - 0.10 : belt - 0.62) - o.y) * inv.y;          // the lining, the floor
    if (ty > 0.0 && ty < t) { t = ty; col = d.y > 0.0 ? float3(0.11, 0.11, 0.105) : float3(0.03, 0.03, 0.032); }
    float tz = ((d.z > 0.0 ? sz + 1.15 : sz - 1.65) - o.z) * inv.z;              // the windscreen, the rear glass
    if (tz > 0.0 && tz < t)
    {
        t = tz;
        col = lerp(trim, glassC, smoothstep(belt + 0.04, belt + 0.12, o.y + d.y * tz));
    }
    // round 3: the dark cabin (the cockpit model is drawn over it within reach, DamageCabin)
    if (_PSXGlsDark > 0.5) return col * lerp(1.0, 0.6, saturate(t / 1.6));
    for (int s = -1; s <= 1; s += 2)
    {
        float x = s * hx * 0.42;
        GlsBox(o, d, inv, float3(x, belt + gh * 0.5 - 0.34, sz - 0.06), float3(0.23, 0.34, 0.06), 0.11, t, col);         // the back
        GlsBox(o, d, inv, float3(x, belt + gh * 0.56, sz - 0.07), float3(0.02, gh * 0.08, 0.015), 0.05, t, col);         // the restraint's stalk
        GlsBox(o, d, inv, float3(x, belt + gh * 0.70, sz - 0.08), float3(0.12, gh * 0.12, 0.045), 0.12, t, col);         // the head restraint
        GlsBox(o, d, inv, float3(x, belt - 0.36, sz + 0.22), float3(0.24, 0.07, 0.26), 0.10, t, col);                    // the cushion
    }
    GlsBox(o, d, inv, float3(0.0, belt + gh * 0.4 - 0.32, sz - 0.92), float3(hx * 0.78, 0.32, 0.08), 0.075, t, col);     // the rear seat's back
    GlsBox(o, d, inv, float3(0.0, belt - 0.12, sz + 1.0), float3(hx * 0.9, 0.15, 0.14), 0.06, t, col);                   // the dash
    return col * lerp(1.0, 0.6, saturate(t / 1.6));
}

// THE GLASS AND THE LENSES. albedo and N as the paint has them; flat = how much of the mirror the
// break took here (a crack scatters, a hole reflects nothing); cave = how much of the pixel is the
// cabin seen through a hole (lit by the cabin's own light, not the sun on the pane).
// dbx dby uvx uvy: the screen derivatives of bpos and uv; wview: the eye's ray to the pixel, world.
void PSXCarGlass2(inout float3 albedo, inout float3 N, out float flat, out float cave, float3 bpos, float3 bnrm,
                  float2 uv, float3 dbx, float3 dby, float2 uvx, float2 uvy, float3 wview)
{
    flat = 0.0;
    cave = 0.0;
    float3 p = bpos - _PSXDmgC.xyz;
    float3 m = float3(dot(p, _PSXDmgR.xyz), dot(p, _PSXDmgU.xyz), dot(p, _PSXDmgF.xyz));
    // A BROKEN LAMP'S LENS: shards with cracks between them, some of them gone
    if (dot(_PSXLampOut, float4(1, 1, 1, 1)) > 0.0)
    {
        float dead = m.z > 0.0 ? (m.x < 0.0 ? _PSXLampOut.x : _PSXLampOut.y) : (m.x < 0.0 ? _PSXLampOut.z : _PSXLampOut.w);
        if (dead > 0.0)
        {
            float4 lm = tex2Dgrad(_LightMask, uv, uvx, uvy);
            float lens = saturate(max(max(lm.r, lm.g), max(lm.b, lm.a)) * 2.5);
            if (lens > 0.02)
            {
                float2 lq = float2(m.x, m.y);
                float lpx = max(length(float2(dot(dbx, _PSXDmgR.xyz), dot(dbx, _PSXDmgU.xyz))),
                                length(float2(dot(dby, _PSXDmgR.xyz), dot(dby, _PSXDmgU.xyz))));
                float2 lc = GlsCells(lq * 36.0 + _PSXDmgB.w * 3.0);
                float gone = step(lc.y, 0.42);
                float3 shard = lerp(albedo * 1.6 + 0.04, float3(0.03, 0.03, 0.035), gone);
                shard = lerp(shard, float3(0.70, 0.72, 0.74), GlsLine(lc.x / 72.0, 0.002, lpx) * 0.8);
                albedo = lerp(albedo, shard, lens);
                flat = lens * lerp(0.35, 1.0, gone);
            }
        }
    }
    if (dot(_PSXGlsS, float4(1, 1, 1, 1)) < 0.01) return;
    if (GlsIsGlass(albedo) < 0.25) return;
    if (m.y < _PSXGlsF.x - 0.03) return;                    // grilles, vents, shut lines and trim under the belt: no pane
    if (abs(m.x) > _PSXGlsF.w * 0.99) return;               // a door mirror's housing
    float3 nB = normalize(bnrm);
    float3 n = float3(dot(nB, _PSXDmgR.xyz), dot(nB, _PSXDmgU.xyz), dot(nB, _PSXDmgF.xyz));
    // WHICH PANE, by the way it faces; its two axes in the body's frame; the strike
    float state, lam = 0.0;
    float4 imp;
    float3 a1, a2 = _PSXDmgU.xyz;
    float2 q;
    float kRake = 1.0;
    if (abs(n.x) > 0.55)
    {
        state = n.x < 0.0 ? _PSXGlsS.z : _PSXGlsS.w;
        imp = n.x < 0.0 ? _PSXGlsL : _PSXGlsT;
        a1 = _PSXDmgF.xyz;
        q = float2(m.z - imp.x, m.y - imp.y);
    }
    else
    {
        bool front = n.z > 0.0;
        // the windscreen stands ahead of the seats, the rear glass behind them
        // (the cowl's own dark panel, a wiper trough, a parcel shelf: no pane)
        if (front ? m.z < _PSXGlsF.y + 0.25 : m.z > _PSXGlsF.y - 0.10) return;
        state = front ? _PSXGlsS.x : _PSXGlsS.y;
        imp = front ? _PSXGlsW : _PSXGlsX;
        lam = front ? 1.0 : 0.0;
        a1 = _PSXDmgR.xyz;
        kRake = 1.0 / clamp(sqrt(max(1.0 - n.y * n.y, 0.0)), 0.42, 1.0);   // metres up the raked pane per metre of height
        q = float2(m.x - imp.x, (m.y - imp.y) * kRake);
    }
    if (state < 0.1) return;
    float2 dqx = float2(dot(dbx, a1), dot(dbx, a2) * kRake), dqy = float2(dot(dby, a1), dot(dby, a2) * kRake);
    float px = max(max(length(dqx), length(dqy)), 1e-5);
    // HOW FAR THE PAINT IS (the frame): the sheet sampled on two rings round the pixel, in the pane's metres
    float dEst = 0.12;
    float det = dqx.x * dqy.y - dqy.x * dqx.y;
    if (abs(det) > 1e-11)
    {
        float2 ix = float2(dqy.y, -dqy.x) / det, iy = float2(-dqx.y, dqx.x) / det;
        float out1 = 0.0, out2 = 0.0;
        float near[6];
        for (int k = 0; k < 6; k++)
        {
            float a = k * 1.0471976 + imp.z * 3.0;
            float2 e = float2(cos(a), sin(a));
            float2 d1 = e * 0.035, d2 = float2(e.x * 0.866 - e.y * 0.5, e.x * 0.5 + e.y * 0.866) * 0.09;
            near[k] = 1.0 - step(0.25, GlsIsGlass(PSX_CAR_TAP(uv + uvx * dot(ix, d1) + uvy * dot(iy, d1), uvx, uvy)));
            out1 += near[k];
            out2 += 1.0 - step(0.25, GlsIsGlass(PSX_CAR_TAP(uv + uvx * dot(ix, d2) + uvy * dot(iy, d2), uvx, uvy)));
        }
        // round 3: paint 3.5 cm away on two OPPOSITE sides - a thin dark strip (a mirror sail's outline, a trim
        // line), not a pane (a pane's edge has paint on one side only)
        if (_PSXGlsThin > 0.5 && max(near[0] * near[3], max(near[1] * near[4], near[2] * near[5])) > 0.5) return;
        out1 /= 6.0;
        out2 /= 6.0;
        dEst = out1 > 0.0 ? 0.035 * cos(3.14159 * min(out1, 0.5)) : (out2 > 0.0 ? 0.09 * cos(3.14159 * min(out2, 0.5)) : 0.12);
    }
    if (lam < 0.5)
    {
        // a tempered pane's sill and head, from the cabin (a sheet whose glass is its own island tells nothing)
        float sill = abs(n.x) > 0.55 ? _PSXGlsF.x - 0.03 : _PSXGlsF.x;
        dEst = min(dEst, min(max(m.y - sill, 0.0), max(_PSXGlsF.z - 0.10 - m.y, 0.0) + 0.02));
    }
    float inPane = smoothstep(0.010, 0.028, dEst);          // the seal and the frame's edge stay as they are
    if (inPane < 0.01) return;
    float3 axisL = max(DmgAxes(), 1e-5);
    if (lam > 0.5)
    {
        const float R = 0.24;
        float2 cell = float2(0, 0);
        float h = GlsH1(imp.z * 77.0 + 3.0);
        float2 dir1 = normalize(float2(imp.w, 0.28 + 0.4 * h)), dir2 = normalize(float2(-imp.w * 0.5, 0.9));
        float mk;
        if (state > 0.84)
        {
            mk = GlsWeb(q, imp.z, px, R, cell);
            mk = max(mk, GlsRunner(q, dir1, 0.55 + 0.35 * h, imp.z, px));
            float r = length(q);
            float inW = 1.0 - smoothstep(R * 0.8, R * 1.3, r);
            // each shard tilts its own way, and the web is dished in toward the strike
            float2 tilt = (float2(GlsH2(cell + imp.z * 3.0), GlsH2(cell.yx + imp.z * 7.0)) - 0.5) * 0.24 * inW
                        - q / max(r, 0.02) * (0.10 * inW * smoothstep(0.0, 0.08, r));
            float3 gB = a1 * tilt.x + a2 * tilt.y;
            N = normalize(N + mul((float3x3)unity_ObjectToWorld, gB / axisL) * inPane);
            mk = max(mk, 0.22 * inW * (1.0 - smoothstep(0.0, R, r)));           // the haze of fine breaks near it
        }
        else
        {
            mk = GlsChip(q, imp.z, px);
            if (state > 0.5)
            {
                mk = max(mk, GlsRunner(q, dir1, 0.55 + 0.35 * h, imp.z, px));
                mk = max(mk, GlsRunner(q, dir2, 0.14 + 0.12 * h, imp.z + 0.5, px));
            }
        }
        mk *= inPane;
        albedo = lerp(albedo, float3(0.80, 0.85, 0.87), mk * 0.92);
        flat = max(flat, mk);
        return;
    }
    float3 cz = GlsCrazed(q, imp.z, px);
    if (state > 0.75)
    {
        // what still hangs in the frame: jagged, more of it low in the channel
        float teeth = 0.012 + 0.085 * pow(max(GlsN2(q * 13.0 + imp.z * 9.0), 1e-4), 1.6)
                    + 0.035 * (1.0 - smoothstep(0.0, 0.12, m.y - _PSXGlsF.x + 0.05));
        float rem = 1.0 - smoothstep(teeth, teeth + max(px, 0.003), dEst);
        cz = lerp(cz, float3(0.95, 0.99, 0.98), rem * (1.0 - smoothstep(0.0, 0.008, teeth - dEst)));   // the broken edge
        float3 vB = mul((float3x3)unity_WorldToObject, wview) * axisL;
        float3 d = normalize(float3(dot(vB, _PSXDmgR.xyz), dot(vB, _PSXDmgU.xyz), dot(vB, _PSXDmgF.xyz)));
        float3 cab = GlsCabin(m, d);
        albedo = lerp(albedo, lerp(cab, cz, rem), inPane);
        flat = max(flat, inPane);
        cave = inPane * (1.0 - rem);
    }
    else
    {
        albedo = lerp(albedo, cz, inPane * 0.95);
        flat = max(flat, inPane * 0.9);
    }
}

// WHEEL RASH: on a wheel's outer face, over an arc: the rim's lip (the sheet's bright texels with
// rubber just outside them) scraped to bright metal and dark gouges, the sidewall scuffed pale.
// The wheel's own frame: the axle is x, its outer face +x (CarModelLibrary.Holder flips the left).
void PSXWheelRash(inout float3 albedo, float3 bpos, float3 bnrm, float2 uv, float3 dbx, float3 dby, float2 uvx, float2 uvy)
{
    float amt = _PSXWheelRash.x, R = max(_PSXWheelRash.z, 0.1), seed = _PSXWheelRash.w;
    float3 nB = normalize(bnrm);
    if (nB.x < 0.15) return;
    float rho = length(bpos.yz);
    if (rho < R * 0.45) return;
    float ang = atan2(bpos.z, bpos.y);
    float arcHalf = amt < 0.5 ? 0.5 : (amt < 0.84 ? 1.3 : 3.3);
    float da = abs(atan2(sin(ang - _PSXWheelRash.y), cos(ang - _PSXWheelRash.y)));
    float arc = 1.0 - smoothstep(arcHalf * 0.75, arcHalf, da + (GlsN1(ang * 9.0 + seed * 5.0) - 0.5) * 0.25);
    if (arc < 0.01) return;
    float isTyre = step(0.25, GlsIsGlass(albedo));
    // streaks that run ROUND the wheel (long along the turn, thin across it), and where round the arc it bit
    float a01 = ang / 6.2831853 + 0.5;
    float streak = GlsN2P(float2(a01 * 32.0, rho * 420.0 + seed * 31.0), 32.0) * 0.65
                 + GlsN2P(float2(a01 * 72.0, rho * 900.0 + seed * 17.0), 72.0) * 0.35;
    float bite = GlsN2P(float2(a01 * 18.0, seed * 53.0), 18.0) * 0.6 + GlsN2P(float2(a01 * 40.0, seed * 29.0 + 7.0), 40.0) * 0.4;
    float t0 = 0.50 - 0.75 * amt;
    float cover = arc * smoothstep(t0, t0 + 0.18, bite);
    if (isTyre < 0.5)
    {
        // the lip: rubber within 2 cm outside this rim texel
        float2 dqx = dbx.yz, dqy = dby.yz;
        float det = dqx.x * dqy.y - dqy.x * dqx.y;
        if (abs(det) < 1e-11) return;
        float2 ix = float2(dqy.y, -dqy.x) / det, iy = float2(-dqx.y, dqx.x) / det;
        float2 e = bpos.yz / rho;
        float2 d1 = e * 0.009, d2 = e * 0.020;
        float t1 = step(0.25, GlsIsGlass(PSX_CAR_TAP(uv + uvx * dot(ix, d1) + uvy * dot(iy, d1), uvx, uvy)));
        float t2 = step(0.25, GlsIsGlass(PSX_CAR_TAP(uv + uvx * dot(ix, d2) + uvy * dot(iy, d2), uvx, uvy)));
        float lip = max(t1, t2 * lerp(0.6, 0.9, amt));
        // scraped to bright metal, the gouges dark between
        float3 metal = lerp(albedo * 0.5, float3(0.93, 0.93, 0.91), smoothstep(0.34, 0.56, streak));
        albedo = lerp(albedo, metal, lip * cover * 0.95);
    }
    else
    {
        // the sidewall: rubbed dull and pale in a band on its shoulder
        float wall = smoothstep(R * 0.72, R * 0.80, rho) * (1.0 - smoothstep(R * 0.93, R * 0.99, rho));
        float rub = smoothstep(0.40, 0.66, streak) * cover * wall;
        albedo = lerp(albedo, float3(0.24, 0.23, 0.21), rub * lerp(0.5, 0.9, amt));
        // a gouged tyre: the cut round it
        albedo *= 1.0 - step(0.84, amt) * arc * wall * GlsLine(rho - R * (0.86 + 0.04 * GlsN2P(float2(a01 * 12.0, seed), 12.0)), 0.004, 0.002) * 0.7;
    }
}

#endif
