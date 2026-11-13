// R1a PART 2a (2026-11-13): THE CAR SHOWS THE HIT, on PSX/CarPaint.
// Per renderer from DamageLook.cs (a MaterialPropertyBlock on the player's career car only:
// AI and traffic never get one, so _PSXDmgOn is 0 and none of this runs - the paint is the
// signed-off paint bit for bit). It shows the car's damage rows: panel_damage per zone (1/3 a
// scrape and a cracked cover, 2/3 a dent, 1 crumpled; a crushed bar only deepens a cover that
// already shows), roof_glass (1/3 cracked glass, 2/3 a dented roof, 1 crushed) and lamp_out (a
// dark lens and no glow). Included after _LightMask is declared.
//   _PSXDmgA     x front, y rear, z left side, w right side (0..1)
//   _PSXDmgB     x roof and glass, y / z where on the nose / tail it was struck (-1 left .. 1 right), w seed
//   _PSXDmgF R U xyz the car's forward / right / up in the body's frame, w 1 / the box's half extent
//   _PSXDmgC     xyz the collider box's centre in the body's frame (m)
//   _PSXLampOut  x head L, y head R, z tail L, w tail R (1 = broken)
#ifndef PSX_CAR_DAMAGE_INCLUDED
#define PSX_CAR_DAMAGE_INCLUDED

float _PSXDmgOn;
float4 _PSXDmgA, _PSXDmgB, _PSXDmgF, _PSXDmgR, _PSXDmgU, _PSXDmgC, _PSXLampOut;

float DmgHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float DmgNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(DmgHash(i), DmgHash(i + float3(1, 0, 0)), f.x);
    float b = lerp(DmgHash(i + float3(0, 1, 0)), DmgHash(i + float3(1, 1, 0)), f.x);
    float c = lerp(DmgHash(i + float3(0, 0, 1)), DmgHash(i + float3(1, 0, 1)), f.x);
    float d = lerp(DmgHash(i + float3(0, 1, 1)), DmgHash(i + float3(1, 1, 1)), f.x);
    return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
}

float3 DmgAxes()
{
    return float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21),
                  length(unity_ObjectToWorld._m02_m12_m22));
}

// A body-frame point in the car's box: x across (-1 left .. 1 right), y up, z along (-1 tail .. 1 nose).
float3 DmgCar(float3 bpos)
{
    float3 p = bpos - _PSXDmgC.xyz;
    return float3(dot(p, _PSXDmgR.xyz) * _PSXDmgR.w, dot(p, _PSXDmgU.xyz) * _PSXDmgU.w, dot(p, _PSXDmgF.xyz) * _PSXDmgF.w);
}

// How struck the body is here (0..1: the worst zone that covers it), and the roof's share.
float DmgAmount(float3 c, out float roof)
{
    float low = 1.0 - smoothstep(0.30, 0.55, c.y);           // under the beltline
    float nose = smoothstep(0.42, 0.78, c.z) * saturate(1.3 - abs(c.x - _PSXDmgB.y) * 0.85);
    float tail = smoothstep(0.42, 0.78, -c.z) * saturate(1.3 - abs(c.x - _PSXDmgB.z) * 0.85);
    float doors = 1.0 - smoothstep(0.55, 0.85, abs(c.z));
    float left = smoothstep(0.45, 0.85, -c.x) * doors;
    float right = smoothstep(0.45, 0.85, c.x) * doors;
    roof = smoothstep(0.22, 0.48, c.y) * _PSXDmgB.x;
    return max(max(nose * _PSXDmgA.x, tail * _PSXDmgA.y), max(left * _PSXDmgA.z, right * _PSXDmgA.w)) * low;
}

// THE SKIN PUSHED IN: a dent a centimetre or two, crumple up to ~8, a crushed roof ~15
// (the collider does not move). Object space in, object space out.
float3 PSXCarDamageVert(float3 v)
{
    if (_PSXDmgOn < 0.5) return v;
    float3 axisL = max(DmgAxes(), 1e-5);
    float3 bpos = v * axisL;
    float roof;
    float a = DmgAmount(DmgCar(bpos), roof);
    if (a + roof < 0.01) return v;
    float n = DmgNoise(bpos * 3.1 + _PSXDmgB.w);
    float push = a * a * 0.05 * (0.3 + 1.4 * n) + roof * roof * 0.12 * (0.6 + 0.8 * n);
    float3 toC = _PSXDmgC.xyz - bpos;
    bpos += toC * (push / max(length(toC), 0.05));
    return bpos / axisL;
}

// THE PIXEL: scrapes to the primer, a dented or crumpled panel's waves in its reflections and
// its creases, cracked glass, a broken lamp's dark lens. Returns how much of the lamp here
// still lights (0 on a broken one); matte = how much of the lacquer's mirror the damage took.
float PSXCarDamage(inout float3 albedo, inout float3 N, out float matte, float3 bpos, float2 uv, float dull)
{
    matte = 0.0;
    float3 c = DmgCar(bpos);
    float seed = _PSXDmgB.w;
    float alive = 1.0;
    if (dot(_PSXLampOut, float4(1, 1, 1, 1)) > 0.0)
    {
        float dead = c.z > 0.0 ? (c.x < 0.0 ? _PSXLampOut.x : _PSXLampOut.y)
                               : (c.x < 0.0 ? _PSXLampOut.z : _PSXLampOut.w);
        alive = 1.0 - dead;
        if (dead > 0.0)
        {
            float4 m = tex2D(_LightMask, uv);
            float lens = saturate(max(max(m.r, m.g), max(m.b, m.a)) * 2.5);
            float shard = DmgNoise(bpos * 55.0 + seed);
            albedo *= lerp(1.0, lerp(0.10, 0.40, shard * shard), lens);
        }
    }
    float roof;
    float a = DmgAmount(c, roof) * (1.0 - dull);
    roof *= 1.0 - dull;
    if (a + roof < 0.004) return alive;
    float3 axisL = max(DmgAxes(), 1e-5);
    // glass: the sheet's dark, colourless texels (the paint's own test, roughly)
    float lum = dot(albedo, float3(0.30, 0.59, 0.11));
    float hi3 = max(albedo.r, max(albedo.g, albedo.b)), lo3 = min(albedo.r, min(albedo.g, albedo.b));
    float glassM = saturate((0.16 - lum) / 0.16) * (1.0 - saturate((hi3 - lo3) / max(hi3, 0.04) * 2.5 - 0.75));
    // SCRAPES: streaks along the panel, down to grey primer and bare steel, over more of it as it worsens
    float along = dot(bpos, _PSXDmgF.xyz), across = dot(bpos, _PSXDmgR.xyz);
    float streak = DmgNoise(float3((along + across) * 1.3, bpos.y * 24.0, seed));
    float blot = DmgNoise(bpos * 2.2 + seed * 1.7);
    float thr = 0.25 + 0.5 * a;
    float cover = 1.0 - smoothstep(thr - 0.12, thr + 0.12, blot);
    float scrape = smoothstep(0.48, 0.74, streak) * cover * saturate(a * 3.0) * (1.0 - glassM);
    float3 primer = lerp(float3(0.36, 0.36, 0.35), float3(0.62, 0.62, 0.60), DmgNoise(bpos * 37.0 + seed));
    albedo = lerp(albedo, primer, scrape * 0.9);
    matte = scrape;
    // CRACKS AND CREASES: thin dark lines - a cracked cover, then the folds of crushed metal
    float r0 = DmgNoise(bpos * 6.5 + seed * 3.1);
    float crease = 1.0 - smoothstep(0.0, 0.05, abs(r0 - 0.5));
    albedo *= 1.0 - crease * saturate(a * 2.0) * 0.6 * (1.0 - glassM);
    // DENTS: the panel waves (its reflections show it), finer and deeper as it crumples
    float dent = max(saturate(a * 1.5 - 0.3), saturate(roof * 1.5 - 0.45));
    if (dent > 0.0)
    {
        float f = lerp(3.0, 6.5, saturate(max(a, roof) * 1.5 - 0.5));
        float3 q = bpos * f + seed;
        float n0 = DmgNoise(q);
        float3 g = float3(DmgNoise(q + float3(0.3, 0, 0)), DmgNoise(q + float3(0, 0.3, 0)), DmgNoise(q + float3(0, 0, 0.3))) - n0;
        float3 gW = mul((float3x3)unity_ObjectToWorld, g / axisL);
        N = normalize(N - gW * (dent * 5.0));
        matte = max(matte, dent * 0.35);
    }
    // CRACKED GLASS: a web of pale lines across the windows of a struck roof
    float crack = saturate(roof * 3.0) * glassM;
    if (crack > 0.0)
    {
        float w1 = DmgNoise(bpos * 9.0 + seed * 5.0), w2 = DmgNoise(bpos * 23.0 + seed * 7.0);
        float web = max(1.0 - smoothstep(0.0, 0.035, abs(w1 - 0.5)), (1.0 - smoothstep(0.0, 0.05, abs(w2 - 0.5))) * 0.6);
        albedo = lerp(albedo, float3(0.55, 0.57, 0.58), web * crack * 0.85);
        matte = max(matte, web * crack);
    }
    return alive;
}

#endif
