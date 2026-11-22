// THE v08 COCKPIT'S GLASS, AND THE RAIN ON IT (2026-10-16).
//
// Owner: "It has glass, so rain should stop at the windows, not on camera
// when in cockpit view." So in the cockpit the lens drops are off (SpeedBlur
// asks CockpitInterior.GlassShowing) and the rain is drawn HERE, on the four
// panes the owner gave their own full UV0 (V up the pane as Unity reads it).
//
// THE PANE: a faint tint (his 0.028 alpha, 0.72/0.80/0.84) and a little of
// the sky at a glancing angle. Premultiplied, ZWrite off, no shadow.
//
// THE RAIN, procedural, no texture, in metres ON the pane (UV x the pane's
// metres per unit, so a drop is round on a long windscreen):
//   * BEADS sit where they land and come and go - two sizes of cell;
//   * RUNNERS, at low speed, slide DOWN the pane in columns, a bead at the
//     head and a thinning trail of droplets behind it;
//   * STREAKS, at speed, the same columns in the WIND's direction: up and
//     outward on the windscreen, rearward on the side glass (the airflow
//     over a moving car), faster and longer.
// Runners and streaks cross-fade on _CabinWind (0 parked .. 1 at about
// 100 km/h); each layer has its own clock (_CabinRun, _CabinBlow), which
// CockpitInterior integrates, so a change of speed never makes the pattern
// jump. _CabinRain is the rain as the lens used to see it (WeatherFx.LensRain:
// eased in, 0 under cover), drying off slowly; _PaneRain how much of it
// each pane catches. A drop is a little lens: the sky (and at night a street
// lamp) in its dome, a bright spec where it faces up, a dark rim.
//
// RAIN 2 (2026-11-16; the owner's three rain clips: a windscreen covered in
// beads when parked, wipers sweeping arcs clear when driving, the drops
// building up again). All of it on uniforms that are zero without it
// (PSX_RAIN2=0, Rain2.Wipers / Rain2.Glass off): the pane is then the one
// above, to the bit.
//   THE WIPERS (_Wiper on the windscreen's material, _CabinWipe the clock):
//     two blades on the pane, drawn here - a dark blade and its arm - on a
//     stroke CockpitInterior times by the rain. Each pixel of the swept arcs
//     knows how long ago a blade last passed it (the stroke is a triangle
//     wave, so that is arithmetic, not a texture), and the water on it is
//     what has landed SINCE: clear behind the blade, beading up again over
//     _CabinWipe2.x seconds. The rest of the pane is as wet as ever.
//   THE WATER LOOKS THROUGH (_CabinRain2.x, PC with REFLECTIONS on): a drop
//     is a lens. Where water covers the pane the frame behind it
//     (_PSXSSRColour, the wet road's own copy of the opaque frame - no new
//     pass) is read through the water's slope, so every bead holds a small
//     bent picture of the street and its lamps, and a sheet of water wobbles
//     what is seen through it. Plus a third, finer size of bead while parked.
//
// DAMAGE ROUND 3 (DamageLook.CockpitPanes): a side or rear pane the shell has
// broken is broken here too (_PSXBreak: x 0.5 crazed / 1 shattered, y seed, zw
// the strike as a share of the pane). Crazed: the white mosaic of
// PSXGlassBreak.cginc, a small hole at the strike. Shattered: gone - only jagged
// crazed remnants in the frame, more of them low in the channel - and no rain.
Shader "PSX/CabinGlass"
{
    Properties
    {
        _Tint ("Tint (rgb) + alpha", Color) = (0.72, 0.80, 0.84, 0.028)
        _Metres ("Metres per U, V", Vector) = (1, 1, 0, 0)
        _Down ("Down the pane (metres frame)", Vector) = (0, -1, 0, 0)
        _Wind ("Wind direction (metres frame); z = outward bend", Vector) = (0, 1, 0, 0)
        _Out ("Outward (metres frame), for the bend", Vector) = (1, 0, 0, 0)
        _PaneRain ("This pane's share of the rain", Range(0, 1)) = 1
        [HideInInspector] _PSXBreak ("Break: x state, y seed, zw strike (share of the pane)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _WiperPhase ("Wiper phase maps (r, g phase; b, a edge distance)", 2D) = "black" {}
        [HideInInspector] _WiperDraw ("Draw the blades (no modelled wipers)", Float) = 1
        [HideInInspector] _WiperLag ("Each arm's lag, share of the stroke (x driver, y passenger)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha, Zero One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PSXCameraRel.cginc"
            #include "PSXLamps.cginc"
            #include "PSXTone.cginc"

            fixed4 _Tint;
            float4 _Metres, _Down, _Wind, _Out;
            float _PaneRain;
            float4 _PSXBreak;
            float _CabinRain, _CabinWind, _CabinRun, _CabinBlow;
            // RAIN 2 (all zero without it)
            float4 _Wiper;        // this pane's wipers, metres frame: pivot 1 x, pivot 2 x, pivots' y, blade reach (0 = none)
            float4 _WiperArc;     // parked angle, sweep (radians), the blade's inner end as a share of its reach, blade width (m)
            float4 _CabinWipe;    // seconds into this stroke, a stroke's seconds, the last cycle's seconds, 1 = wipers fitted
            float4 _CabinWipe2;   // x: seconds for a wiped pane to bead up again; y: the stroke value s now; z: 1 = the phase maps (rainglass)
            // RAINGLASS (2026-11-22): the wipe from two phase maps in this pane's UV0 (WiperPhase.cs;
            // the v10 modeller's maps, or placeholders made from the drawn arcs): r, g = the stroke
            // value s at which each arm's rubber crosses this point on the upstroke; b, a = signed
            // distance to the edge of each arm's swept area, 0.5 + d / (2 R2G_FIELD_M), inside +.
            sampler2D _WiperPhase;
            float _WiperDraw;     // 1: draw the blades here (the model has no Wiper_* nodes)
            float4 _WiperLag;     // each arm's lag behind the stroke, a share of its time
            #define R2G_FIELD_M 0.04       // = WiperPhase.FieldM
            #define R2G_BANK_M  0.045      // the bank of water a moving blade pushes ahead of it, metres deep
            float4 _CabinRain2;   // x: 1 = the water looks through (needs _PSXSSRLive); y: 1 = the third bead size; z: 1 = the rainglass beads (Rain2.Beads2)
            sampler2D _PSXSSRColour;
            float _PSXSSRLive;    // 1 while this camera's frame copy is bound (SpeedBlurFeature.WetPass)
            #define R2G_LENS    0.000016   // a drop's bend of what is behind it: metres squared (see the note at its use)
            #define R2G_MAXPX   46.0       // ...never further than this many pixels (of a 1080-line frame)
            #define R2G_SHEET   0.50       // how much of the pane a downpour's sheet covers while parked
            #define R2G_WOBBLE  2.2        // the sheet's wobble of what is seen through it, pixels of a 1080-line frame
            #define R2G_RIDGE_M 0.022      // the bank of water a blade leaves at the edges of its arc, metres wide
            #define R2G_FISH    5.0        // a bead shows the scene from this many of its own radii round it, upside down
            #define R2G_VEIL    0.10       // a parked pane's sheet of water scatters this much of the light on it
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXAmbient;
            fixed4 _PSXFogColor;
            fixed4 _PSXLightColor;
            float4 _PSXLightDir;
            float _PSXNight;
            #include "PSXSkyReflect.cginc"
            #include "PSXGlassBreak.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 wnrm : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = PSXObjectToClipRel(v.vertex);
                o.uv = v.uv;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 H42(float2 p)
            {
                float4 p4 = frac(float4(p.xyxy) * float4(0.1031, 0.1030, 0.0973, 0.1099));
                p4 += dot(p4, p4.wzxy + 33.33);
                return frac((p4.xxyz + p4.yzzw) * p4.zywx);
            }

            // A dome of height 1 at the centre, 0 at radius r.
            float Dome(float2 d, float r) { return saturate(1.0 - dot(d, d) / (r * r)); }

            // (RAIN 2) a bead's height (x) and where in it this point is (yz:
            // the offset from its middle over its radius, |yz| < 1 inside) -
            // the lens's direction, exact, where ddx of a 3-pixel bead is not;
            // w: its radius, metres
            float4 Bead3(float2 q, float cell, float density, float seed, float rMul)
            {
                float2 g = q / cell;
                float2 id = floor(g);
                float4 h = H42(id + seed);
                float2 c = (id + 0.5 + (h.xy - 0.5) * 0.5) * cell;
                float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
                float alive = step(h.z, density) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
                float r = cell * lerp(0.12, 0.30, h.w) * rMul;
                float2 n = (q - c) / r;
                return float4(saturate(1.0 - dot(n, n)) * alive, n, r);
            }
            float4 BeadMax(float4 a, float4 b) { return a.x >= b.x ? a : b; }

            // (RAINGLASS) A bead as his clip shows them: not a round dome but a drop
            // that SAGS - longer below its middle the bigger it is - and now and then
            // two drops run together into one blob. Same cell, same life as Bead3;
            // yz is still the lens direction in the pane's frame (|yz| < 1 inside).
            float4 Bead4(float2 q, float cell, float density, float seed, float rMul, float2 down)
            {
                float2 g = q / cell;
                float2 id = floor(g);
                float4 h = H42(id + seed);
                float2 c = (id + 0.5 + (h.xy - 0.5) * 0.5) * cell;
                float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
                float alive = step(h.z, density) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
                float r = cell * lerp(0.12, 0.30, h.w) * rMul;
                float2 side = float2(down.y, -down.x);
                float2 d = q - c;
                float al = dot(d, down), ac = dot(d, side);
                float sag = 1.0 + 0.75 * h.y * smoothstep(0.35, 0.9, h.w);
                al /= al > 0.0 ? sag : 1.0;
                ac *= 1.0 + 0.18 * (h.x - 0.5);
                float2 n = (side * ac + down * al) / r;
                float4 a = float4(saturate(1.0 - dot(n, n)), n, r);
                // two drops run together (about a third of the bigger ones)
                if (frac(h.x * 7.31 + h.y * 3.17) < 0.35 * smoothstep(0.25, 0.7, h.w))
                {
                    float r2 = r * lerp(0.55, 0.85, frac(h.z * 5.3));
                    float2 c2 = c + (side * (frac(h.w * 9.1) - 0.5) * 1.6 + down * 0.55) * r;
                    float2 n2 = (q - c2) / r2;
                    a = BeadMax(a, float4(saturate(1.0 - dot(n2, n2)), n2, r2));
                }
                a.x *= alive;
                return a;
            }

            // Beads on a grid of `cell` metres: present for a while, then gone.
            float Beads(float2 q, float cell, float density, float seed)
            {
                float2 g = q / cell;
                float2 id = floor(g);
                float4 h = H42(id + seed);
                float2 c = (id + 0.5 + (h.xy - 0.5) * 0.5) * cell;
                float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
                float alive = step(h.z, density) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
                float r = cell * lerp(0.12, 0.30, h.w);
                return Dome(q - c, r) * alive;
            }

            // Drops travelling along `dir` in columns `width` wide, `span`
            // metres a cycle, the clock `t`. Head bead + trail behind it.
            float Runners(float2 q, float2 dir, float width, float span, float t, float density, float seed, float trailK)
            {
                float2 side = float2(dir.y, -dir.x);
                float x = dot(q, side) / width;
                float col = floor(x);
                float4 h = H42(float2(col, seed));
                float speed = lerp(0.6, 1.4, h.x);
                float y = dot(q, dir) / span - t * speed + h.y * 7.0;
                float cyc = floor(y);
                float4 k = H42(float2(col, cyc + seed * 3.1));
                float on = step(k.x, density);
                float f = frac(y);                       // grows along dir: the head LEADS at the top end
                float wob = sin(f * 9.0 + k.y * 6.28) * 0.12 * (1.0 - trailK);
                float dx = (frac(x) - 0.5 - (k.z - 0.5) * 0.4 - wob) * width;
                float rHead = width * lerp(0.14, 0.24, k.w);
                float head = Dome(float2(dx, (f - 0.92) * span), rHead);
                // The trail: thin, thinning, broken into droplets behind the head.
                float tr = saturate((f - 0.10) / 0.78) * step(f, 0.9);
                float beadsOn = step(0.45, frac(f * span / (rHead * 3.0) + k.y));
                float trail = saturate(1.0 - abs(dx) / (rHead * 0.35 * tr + 1e-4)) * tr
                            * lerp(beadsOn, 1.0, trailK) * 0.8;
                return max(head, trail) * on;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.wnrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                if (dot(N, V) < 0.0) N = -N;
                float ndv = saturate(dot(N, V));
                float fres = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);
                float adapt = PSXAdaptGain();
                float day = 1.0 - saturate(_PSXNight);
                float3 sky = PSXSkyIn(reflect(-V, N), 2.0) * adapt;
                float3 ambient = _PSXSkyAmbient.rgb * PSXExposureGain();

                // The pane itself.
                float a = _Tint.a;
                float3 rgb = _Tint.rgb * ambient * a + sky * (fres * 0.10 * lerp(0.35, 1.0, day));
                a += fres * 0.06;

                // Round 3: the pane broken as the shell's is.
                float rainK = 1.0;
                float2 qm = i.uv * _Metres.xy;
                float bpx = max(max(length(ddx(qm)), length(ddy(qm))), 1e-5);
                if (_PSXBreak.x > 0.1)
                {
                    float2 qs = qm - _PSXBreak.zw * _Metres.xy;
                    float3 cz = GlsCrazed(qs, _PSXBreak.y, bpx);
                    float3 lightC = ambient * 1.4 + _PSXLightColor.rgb * (0.30 * day) * PSXExposureGain();
                    if (_PSXBreak.x > 0.75)
                    {
                        // shattered: only jagged crazed remnants in the frame, more low in the channel
                        float2 e2 = min(qm, _Metres.xy - qm);
                        float dEdge = min(e2.x, e2.y);
                        float2 upM = -_Down.xy;
                        float dLow = abs(upM.y) > abs(upM.x) ? (upM.y > 0.0 ? qm.y : _Metres.y - qm.y)
                                                             : (upM.x > 0.0 ? qm.x : _Metres.x - qm.x);
                        float teeth = 0.012 + 0.085 * pow(max(GlsN2(qm * 13.0 + _PSXBreak.y * 9.0), 1e-4), 1.6)
                                    + 0.035 * (1.0 - smoothstep(0.0, 0.12, dLow));
                        float rem = 1.0 - smoothstep(teeth, teeth + max(bpx, 0.003), dEdge);
                        a = rem * 0.94;
                        rgb = cz * lightC * a;
                        rainK = 0.0;
                    }
                    else
                    {
                        // crazed: a white mosaic you cannot see through, a small hole where it was struck
                        float hole = 1.0 - smoothstep(0.008, 0.02 + bpx, length(qs));
                        a = 0.88 * (1.0 - hole);
                        rgb = cz * lightC * a;
                        rainK = 0.5;
                    }
                }

                // The rain.
                float rain = _CabinRain * _PaneRain * rainK;
                float3 through = float3(0.0, 0.0, 0.0);     // (RAIN 2) the frame seen through the water, already toned
                float blade = 0.0;
                float2 q = i.uv * _Metres.xy;
                // (derivatives of the pane's own frame, before any branch)
                float2 qdx = ddx(q), qdy = ddy(q);

                // RAIN 2, THE WIPERS: where the blades are, and how long ago
                // one last passed this point.
                float fill = 1.0;
                float ridge = 0.0;      // (RAIN 2) the water a blade leaves piled along the edges of its arc
                float bank = 0.0;       // (RAINGLASS) the pooled water the moving blade pushes ahead of it
                float smear = 0.0;      // (RAINGLASS) the thin film a blade leaves for a moment behind it (lamps smear in it)
                float edgeZone = 0.0;   // (RAINGLASS) within 10 cm of a swept arc's edge: where the pooled water runs off
                if (_CabinWipe.w > 0.5 && _Wiper.w > 0.0)
                {
                    float tc = _CabinWipe.x, D = max(_CabinWipe.y, 0.05);
                    float ph = saturate(tc / D);
                    float sw = 1.0 - abs(ph * 2.0 - 1.0);            // 0 parked .. 1 at the top .. 0 parked
                    if (_CabinWipe2.z > 0.5) sw = _CabinWipe2.y;      // (rainglass) the eased stroke value s
                    float th = _WiperArc.x + sw * _WiperArc.y;
                    float2 bd = float2(cos(th), sin(th));
                    float r1 = _Wiper.w, r0 = _Wiper.w * _WiperArc.z;
                    float since = 1000.0;
                    float aa = max(length(qdx), length(qdy));         // a pixel, in the pane's metres
                    if (_CabinWipe2.z > 0.5)
                    {
                        // RAINGLASS: WHERE each rubber passes, from the phase maps. A point at phase p
                        // is crossed when the eased stroke s(t) = 0.5 - 0.5 cos(pi u) reaches p: on
                        // the way up at u = acos(1 - 2p) / pi of the half stroke, on the way back at
                        // the mirror of that. Each arm may lag the stroke (_WiperLag, the linkage).
                        float4 wm = tex2D(_WiperPhase, i.uv);
                        float inside = 0.0, edgeBank = 0.0, ahead = 0.0;
                        for (int k = 0; k < 2; k++)
                        {
                            float p = k == 0 ? wm.r : wm.g;
                            float dIn = ((k == 0 ? wm.b : wm.a) - 0.5) * (2.0 * R2G_FIELD_M);   // metres, + inside
                            float inK = smoothstep(-aa, aa, dIn);
                            float tk = tc - (k == 0 ? _WiperLag.x : _WiperLag.y) * D;
                            float u = acos(clamp(1.0 - 2.0 * p, -1.0, 1.0)) * 0.318309886;
                            float tUp = 0.5 * u * D, tDn = (1.0 - 0.5 * u) * D;
                            float sk = tk >= tDn ? tk - tDn : tk >= tUp ? tk - tUp : tk + _CabinWipe.z - tDn;
                            if (inK > 0.5) since = min(since, sk);
                            inside = max(inside, inK);
                            // the water the blades leave piled along the swept area's edge
                            edgeBank = max(edgeBank, dIn < 0.0 ? saturate(1.0 + dIn / R2G_RIDGE_M) : 0.0);
                            edgeZone = max(edgeZone, saturate(1.0 - abs(dIn) / 0.10));
                            // the bank the moving rubber pushes ahead of it: points a little further
                            // along the stroke than the blade is now (in metres: the phase's own slope)
                            float phk = tk / D;
                            if (phk > 0.0 && phk < 1.0)
                            {
                                float uk = phk < 0.5 ? phk * 2.0 : 2.0 - phk * 2.0;
                                float s = 0.5 - 0.5 * cos(3.14159265 * uk);
                                float gradP = max(length(float2(ddx(p), ddy(p))) / max(aa, 1e-6), 0.5);
                                float dm = (p - s) * (phk < 0.5 ? 1.0 : -1.0) / gradP;
                                ahead = max(ahead, inK * smoothstep(-0.002, 0.004, dm) * (1.0 - smoothstep(0.010, R2G_BANK_M, dm))
                                                       * smoothstep(0.0, 0.15, s));
                            }
                        }
                        ridge = edgeBank * (1.0 - inside);
                        bank = ahead;
                        smear = inside * exp(-since * 4.0) * step(since, 999.0);
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        float2 v = q - float2(k == 0 ? _Wiper.x : _Wiper.y, _Wiper.z);
                        float r = length(v);
                        if (_CabinWipe2.z < 0.5)
                        {
                            float sp = (atan2(v.y, v.x) - _WiperArc.x) / _WiperArc.y;
                            if (sp > 0.0 && sp < 1.0 && r > r0 && r < r1)
                            {
                                // the stroke passes this angle going up at tUp and coming back at tDn
                                float tUp = 0.5 * sp * D, tDn = (1.0 - 0.5 * sp) * D;
                                since = min(since, tc >= tDn ? tc - tDn : tc >= tUp ? tc - tUp : tc + _CabinWipe.z - tDn);
                            }
                            // just outside the swept arc (beyond the blade's tip, or past
                            // the top of the stroke): a ragged bank of pushed water
                            float outR = (r - r1) / R2G_RIDGE_M;
                            float outA = (sp - 1.0) * _WiperArc.y * r / R2G_RIDGE_M;
                            float rgk = 0.0;
                            if (sp > 0.0 && sp < 1.0 && outR > 0.0 && outR < 1.0) rgk = 1.0 - outR;
                            if (r > r0 && r < r1 && outA > 0.0 && outA < 1.0) rgk = max(rgk, 1.0 - outA);
                            ridge = max(ridge, rgk);
                        }
                        if (_WiperDraw > 0.5)
                        {
                            float al = dot(v, bd);
                            float dB = length(v - bd * clamp(al, r0, r1));
                            float dA = length(v - bd * clamp(al, 0.0, (r0 + r1) * 0.5));
                            blade = max(blade, 1.0 - smoothstep(_WiperArc.w * 0.5, _WiperArc.w * 0.5 + aa * 1.5, dB));
                            blade = max(blade, 1.0 - smoothstep(_WiperArc.w * 0.22, _WiperArc.w * 0.22 + aa * 1.5, dA));
                        }
                    }
                    fill = saturate(since / max(_CabinWipe2.x, 0.05));
                    fill *= fill;
                }

                if (rain > 0.002)
                {
                    float wind = _CabinWind;
                    // (RAIN 2) what has landed since the blade passed
                    float rainW = rain * fill;
                    // The windscreen's streaks bend outward from its middle.
                    float2 wdir = normalize(_Wind.xy + _Out.xy * (dot(q, _Out.xy) - _Wind.w) * _Wind.z * wind);
                    // (RAIN 2: a parked pane is COVERED - more of the two sizes'
                    // cells hold a bead, and a third, finer size sits between them)
                    float dens = 1.0 + 0.65 * _CabinRain2.y * (1.0 - wind);
                    // (and the WINDSCREEN's beads are drawn 1.6 x life size: it
                    // stands a full arm from the eye, where a 4 mm bead is three
                    // pixels of a 1080-line frame and the pane reads as grain)
                    float bs = 1.0 + 0.6 * _CabinRain2.y * step(0.001, _Wiper.w);
                    // (RAINGLASS) life size nearer: 1.3 x on the windscreen (it read coarse at 1.6), and
                    // the side and rear glass - a hand from the eye - at 0.46 of the old cells (they read
                    // as big as coins beside the driver's head)
                    bool b2 = _CabinRain2.z > 0.5;
                    if (b2) bs = _Wiper.w > 0.001 ? 1.0 + 0.3 * _CabinRain2.y : 0.40;
                    // (RAIN 2, parked: a fourth size - the screen is COVERED in small
                    // bright beads in his clip - and the fine one far denser)
                    float park = _CabinRain2.y * (1.0 - wind);
                    float4 bB;
                    if (b2)
                    {
                        bB = BeadMax(Bead4(q, 0.016 * bs, rainW * lerp(0.62, 0.25, wind) * dens, 1.7, 1.0, _Down.xy),
                                     Bead4(q, 0.028 * bs, rainW * lerp(0.42, 0.15, wind) * dens, 9.3, 1.05, _Down.xy));
                    }
                    else
                    {
                        bB = BeadMax(Bead3(q, 0.016 * bs, rainW * lerp(0.55, 0.25, wind) * dens, 1.7, 1.0),
                                     Bead3(q, 0.028 * bs, rainW * lerp(0.35, 0.15, wind) * dens, 9.3, 1.0));
                    }
                    if (park > 0.01)
                    {
                        float4 f1 = Bead3(q, 0.0085 * bs, rainW * 0.92 * park, 5.9, 1.15);
                        float4 f2 = Bead3(q, 0.0060 * bs, rainW * 0.85 * park, 2.3, 1.25);
                        f1.x *= 0.85; f2.x *= 0.75;
                        bB = BeadMax(bB, BeadMax(f1, f2));
                    }
                    float hB = bB.x;
                    // (RAIN 2, parked: more runners, their trails unbroken - the
                    // running sheets of his clip)
                    float hR = Runners(q, _Down.xy, 0.026, 0.30, _CabinRun, rainW * (0.45 * dens + 0.25 * park), 4.1, 0.55 * park) * (1.0 - wind);
                    float hS = Runners(q, wdir, 0.020, 0.55, _CabinBlow, rainW * (b2 ? 0.95 : 0.75), 7.7, 0.85) * wind;
                    float hgt = max(hB, max(hR, hS));
                    // (RAIN 2) the bank a blade leaves, ragged
                    // (a thin film packed with small beads, not a smooth line)
                    if (ridge > 0.0) hgt = max(hgt, ridge * rain * (0.30 + 0.70 * Beads(q, 0.0075, 0.95, 13.1)));
                    // (RAINGLASS) the bank the blade drives before it is POOLED water (his clips): a
                    // thick wavering band, not beads - and what it leaves at the arcs' edges runs
                    // off in rivulets, down the screen parked, back along the air at speed
                    if (bank > 0.0)
                        hgt = max(hgt, bank * rain * (0.62 + 0.18 * sin(dot(q, float2(61.0, 47.0)) + _CabinRun * 23.0)
                                                         + 0.20 * Beads(q, 0.0065, 0.9, 17.7)));
                    if (edgeZone > 0.0 && b2)
                    {
                        float2 rdir = normalize(lerp(_Down.xy, wdir, saturate(wind * 1.4)));
                        hgt = max(hgt, edgeZone * Runners(q, rdir, 0.014, 0.18, wind > 0.3 ? _CabinBlow : _CabinRun * 2.0,
                                                          rain * 0.85, 21.3, 0.7));
                    }
                    bool beadTop = hB >= max(hR, hS) && hB > 0.0;
                    // WATER, not paint: a drop is clear in the middle (a
                    // little of the sky it bends in), dark at its rim where it
                    // bends the scene away, with one small bright glint where
                    // its dome faces the sky. (Derivatives taken before any
                    // branch.)
                    float2 gpx = float2(ddx(hgt), ddy(hgt));
                    float2 grad = gpx / max(fwidth(hgt), 1e-3);
                    float cover = smoothstep(0.0, 0.12, hgt);
                    float edge = 1.0 - smoothstep(0.05, 0.55, hgt);
                    float glint = pow(saturate(-grad.y * 0.7 + grad.x * 0.3), 6.0) * smoothstep(0.25, 0.6, hgt);
                    float3 lamp = PSXLampsAt(i.wpos) * 0.6;
                    float dropA = cover * (0.10 + 0.50 * edge);
                    float3 dropC = cover * (sky * 0.10 + ambient * 0.06) * (1.0 - edge)
                                 + glint * (sky * 0.5 + _PSXLightColor.rgb * 0.35 * day + lamp * 1.5);
                    if (_CabinRain2.x > 0.5 && _PSXSSRLive > 0.5)
                    {
                        // RAIN 2, THE WATER LOOKS THROUGH. A dome of radius r
                        // bends its picture by about its own slope x r x r / 2
                        // (metres on the pane); the slope per PIXEL over the
                        // pane's metres-per-pixel squared is that in pixels,
                        // so R2G_LENS is r x r / 2 for a typical bead (5-6 mm).
                        // A BEAD is a little fisheye: it shows the scene from
                        // R2G_FISH of its radii round it, upside down, and so
                        // catches the lamps and the sky above it - his clip's
                        // screen of bright beads. Between the beads a parked
                        // pane in a downpour wears a sheet that wobbles what is
                        // seen through it and scatters a little light.
                        float m2 = max(abs(qdx.x * qdy.y - qdx.y * qdy.x), 1e-12);
                        float px1080 = _ScreenParams.y / 1080.0;
                        float2 dpx = clamp(gpx * (R2G_LENS / m2), -R2G_MAXPX * px1080, R2G_MAXPX * px1080);
                        if (beadTop)
                        {
                            // this point's offset from the bead's middle, pane metres ->
                            // pixels (the inverse of the pane's frame per pixel); the
                            // bead shows the scene R2G_FISH times as far out, upside down
                            float2 dq = bB.yz * bB.w;
                            float det = qdx.x * qdy.y - qdx.y * qdy.x;
                            float2 dp = float2(qdy.y * dq.x - qdy.x * dq.y, qdx.x * dq.y - qdx.y * dq.x) / (abs(det) > 1e-12 ? det : 1e-12);
                            dpx = clamp(-dp * ((R2G_FISH + 1.0) * cover), -R2G_MAXPX * px1080, R2G_MAXPX * px1080);
                        }
                        float sheet = rainW * R2G_SHEET * (1.0 - wind) * (1.0 - cover);
                        // (RAINGLASS) at speed the water the blades have not taken is a blown,
                        // streaming sheet (his fast clip: the screen's edges bright with it), so the
                        // swept arcs - where little has landed since - read clear against it
                        if (b2) sheet = max(rainW * fill * R2G_SHEET * lerp(1.0, 0.75, wind), 0.45 * smear * rain) * (1.0 - cover);
                        float2 wob = float2(sin(q.y * 95.0 + sin(q.x * 37.0 + _CabinRun * 9.0) * 1.7),
                                            sin(q.x * 80.0 + sin(q.y * 41.0 - _CabinRun * 12.0) * 1.9));
                        dpx += wob * (R2G_WOBBLE * px1080 * sheet);
                        float wetA = saturate(max(cover, sheet));
                        float2 suv = (i.pos.xy + dpx) / _ScreenParams.xy;
                        // (a bead gathers light: brighter in its middle, dark only at its very rim)
                        float rim = beadTop ? smoothstep(0.72, 0.98, length(bB.yz)) : edge * 0.35;   // (a runner's trail is a bright thread of water, not a dark scratch)
                        float gather = beadTop ? 1.0 + 0.9 * park + 0.6 * smoothstep(0.3, 0.9, hgt) : 1.0 + 0.5 * smoothstep(0.45, 0.95, hgt);
                        float rimK = 0.6;
                        if (b2)
                        {
                            // (RAINGLASS) his beads are clear water with a crisp dark outline (the
                            // edge bends the scene away) and the light they bend in, not glowing
                            // orange coins: a wider rim, darker, and much less gathered light
                            rim = beadTop ? smoothstep(0.60, 0.95, length(bB.yz)) : edge * 0.35;
                            rimK = 0.62;
                            gather = beadTop ? 1.0 + 0.35 * park + 0.25 * smoothstep(0.3, 0.9, hgt) : 1.0 + 0.3 * smoothstep(0.45, 0.95, hgt);
                        }
                        float3 seen = tex2Dlod(_PSXSSRColour, float4(suv, 0.0, 0.0)).rgb;
                        // (a bead by the pillar would show the pillar - a black fleck on
                        // the pane: it keeps most of what is straight behind it)
                        if (beadTop) seen = max(seen, tex2Dlod(_PSXSSRColour, float4(i.pos.xy / _ScreenParams.xy, 0.0, 0.0)).rgb * 0.75);
                        // (RAINGLASS) the picture in a bead is the scene's light, less its tint:
                        // a third of the way to grey (sodium-lit streets made every bead orange)
                        if (b2 && beadTop) seen = lerp(seen, dot(seen, float3(0.30, 0.59, 0.11)).xxx, 0.25);
                        through = seen * (wetA * (1.0 - rimK * rim * cover) * gather);
                        // the glint: one bright point up and to the side of each bead's crown
                        float g2 = beadTop ? (1.0 - smoothstep(0.0, 0.32, length(bB.yz - float2(-0.30, 0.38)))) * cover : glint;
                        // (RAINGLASS) a sharper, smaller glint and a thin bright crescent on the bead's
                        // upper edge (the sky and the lamps caught in its rim)
                        if (b2 && beadTop)
                        {
                            float2 up2 = -_Down.xy;
                            g2 = (1.0 - smoothstep(0.0, 0.20, length(bB.yz - (up2 * 0.42 + float2(-up2.y, up2.x) * 0.22)))) * cover;
                            g2 += 0.35 * rim * saturate(dot(bB.yz, up2)) * cover;
                        }
                        dropA = wetA;
                        dropC = cover * (sky * 0.05 + ambient * 0.03) * (1.0 - edge)
                              + g2 * (sky * 0.6 + _PSXLightColor.rgb * 0.35 * day + lamp * 2.0 + ambient * 0.25)
                              + sheet * R2G_VEIL * (b2 ? park + 0.8 * wind : park) * (ambient * 0.5 + sky * 0.25 + lamp)
                              + (b2 ? smear * rain * 0.18 * (lamp * 1.5 + sky * 0.2) : 0.0);
                    }
                    rgb = rgb * (1.0 - dropA) + dropC;
                    a = a + dropA * (1.0 - a);
                }
                if (blade > 0.0)
                {
                    // the blade and its arm: black rubber and painted steel, on the glass's far side
                    rgb = rgb * (1.0 - blade) + ambient * (0.035 * blade);
                    through *= 1.0 - blade;
                    a = a + blade * (1.0 - a);
                }
                rgb = PSXTone(rgb) + through;
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
}
