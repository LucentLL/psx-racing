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
            float4 _WiperPhase_TexelSize;
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
            // RAINGLASS2 (2026-11-22, round 2): x the bank as his clip's (PSX_RAIN2_BANK2), y the parked
            // pane covered (PSX_RAIN2_COVER), z the wiped arcs clear at speed (PSX_RAIN2_CLEAR); 0 = fe3ce8a6
            float4 _PSXRain2Glass2;
            #define R2G_BANK2_M  0.055     // the bank ahead of the rubber, metres deep (his tunnel clip: 40-80 px of a 4K frame)
            #define R2G_DEPO_M   0.045     // the lump it drops beyond the arc's end, metres
            #define R2G_BANK_PX  15.0      // how far the bank's lip bends what is behind it, pixels of a 1080-line frame
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
            float4 Bead4(float2 q, float cell, float density, float seed, float rMul, float2 down, float irr)
            {
                float2 g = q / cell;
                float2 id = floor(g);
                float4 h = H42(id + seed);
                float2 c = (id + 0.5 + (h.xy - 0.5) * (irr > 0.5 ? 0.30 : 0.5)) * cell;
                float life = frac(_Time.y * (0.05 + 0.08 * h.w) + h.z);
                float alive = step(h.z, density) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
                float r = cell * lerp(0.12, 0.30, h.w) * rMul * (irr > 0.5 ? 0.80 : 1.0);
                float2 side = float2(down.y, -down.x);
                float2 d = q - c;
                float al = dot(d, down), ac = dot(d, side);
                float sag = 1.0 + 0.75 * h.y * smoothstep(0.35, 0.9, h.w);
                al /= al > 0.0 ? sag : 1.0;
                ac *= 1.0 + 0.18 * (h.x - 0.5);
                float2 n = (side * ac + down * al) / r;
                if (irr > 0.5)
                {
                    // (RAINGLASS2) his parked screen: no two drops alike - an outline that wanders
                    // (a smooth warp of about a third of the radius) and some drawn out into smears
                    float ang = h.x * 6.2832;
                    float2 ax = float2(cos(ang), sin(ang));
                    float st = 1.0 + 0.8 * step(0.6, frac(h.y * 13.7)) * frac(h.w * 5.1);
                    float da = dot(n, ax);
                    n += ax * (da / sqrt(st) - da);
                    n += (n - ax * dot(n, ax)) * (sqrt(st) - 1.0);
                    n += 0.30 * sin(n.yx * float2(2.9, 3.4) + h.zw * 6.2832);
                }
                float4 a = float4(saturate(1.0 - dot(n, n)), n, r);
                // two drops run together (about a third of the bigger ones; more than half parked)
                float mergeK = irr > 0.5 ? 0.62 * smoothstep(0.1, 0.55, h.w) : 0.35 * smoothstep(0.25, 0.7, h.w);
                if (frac(h.x * 7.31 + h.y * 3.17) < mergeK)
                {
                    float r2 = r * lerp(0.55, 0.85, frac(h.z * 5.3));
                    float2 c2 = c + (side * (frac(h.w * 9.1) - 0.5) * 1.6 + down * 0.55) * r;
                    float2 n2 = (q - c2) / r2;
                    if (irr > 0.5) n2 += 0.25 * sin(n2.yx * float2(3.1, 2.6) + h.wz * 6.2832);
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
            // (RAINGLASS2) `jerk` 1: a rivulet runs in bursts and stops between them, as water
            // on glass does (it waits until enough has gathered behind its head), and meanders.
            float Runners2(float2 q, float2 dir, float width, float span, float t, float density, float seed, float trailK, float jerk)
            {
                float2 side = float2(dir.y, -dir.x);
                float x = dot(q, side) / width;
                float col = floor(x);
                float4 h = H42(float2(col, seed));
                float speed = lerp(0.6, 1.4, h.x);
                float travel = t * speed;
                if (jerk > 0.5)
                {
                    float tj = travel * 3.0 + h.w * 5.0;
                    travel = (floor(tj) + smoothstep(0.0, 0.4, frac(tj)) - h.w * 5.0) / 3.0;
                    x += 0.18 * sin(dot(q, dir) * (40.0 + 30.0 * h.z) + h.y * 6.28);
                }
                float y = dot(q, dir) / span - travel + h.y * 7.0;
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
            float Runners(float2 q, float2 dir, float width, float span, float t, float density, float seed, float trailK)
            {
                return Runners2(q, dir, width, span, t, density, seed, trailK, 0.0);
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
                // (RAINGLASS2)
                bool bank2 = _PSXRain2Glass2.x > 0.5;
                float bankX = 0.0;      // across the bank: 0 at the rubber .. 1 its leading lip
                float2 bankPx = float2(0.0, 0.0);   // the stroke's direction (ahead) in pixels, unit
                float depo = 0.0;       // the lump the blade dropped beyond the arc's end, and how fresh it is
                float depoFresh = 0.0;
                float swept = 0.0;      // inside either arm's swept area
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
                            // (RAINGLASS2) THE LUMP AT THE ARC'S END. Water the bank carried is dropped
                            // where the rubber stops: at the top of the stroke the whole bank, along the
                            // tip's and the root's arcs a share all the way. A point just outside asks the
                            // phase map a few cm INSIDE (along the edge field's own gradient) when the
                            // rubber last reached there: that is when it was fed, and the lump is fresh
                            // for a second or two, then thins as it runs off.
                            float2 gEd = float2(ddx(dIn), ddy(dIn));
                            float2 uvx = ddx(i.uv), uvy = ddy(i.uv);
                            if (bank2 && dIn < 0.0 && dIn > -R2G_DEPO_M * 1.6)
                            {
                                float dt2 = uvx.x * uvy.y - uvx.y * uvy.x;
                                float2 gUv = float2(gEd.x * uvy.y - uvx.y * gEd.y, uvx.x * gEd.y - gEd.x * uvy.x) / (abs(dt2) > 1e-14 ? dt2 : 1e-14);
                                float2 duv = gUv * ((0.03 - dIn) / max(dot(gUv, gUv), 1e-12));
                                duv = clamp(duv, -0.25, 0.25);
                                float4 wi = tex2Dlod(_WiperPhase, float4(i.uv + duv, 0.0, 0.0));
                                float pin = k == 0 ? wi.r : wi.g;
                                if (pin > 0.002)
                                {
                                    float ui = acos(clamp(1.0 - 2.0 * pin, -1.0, 1.0)) * 0.318309886;
                                    float tUi = 0.5 * ui * D, tDi = (1.0 - 0.5 * ui) * D;
                                    float fed = tk >= tDi ? tk - tDi : tk >= tUi ? tk - tUi : tk + _CabinWipe.z - tDi;
                                    float lump = lerp(0.55, 1.0, smoothstep(0.80, 0.97, pin));
                                    float fresh = exp(-max(fed, 0.0) / 1.4) * lump;
                                    // the lump: thickest at the edge, its outer side ragged
                                    float wD = R2G_DEPO_M * (0.55 + 0.45 * fresh) * (0.80 + 0.20 * sin(dot(q, float2(37.0, 29.0))) + 0.15 * sin(dot(q, float2(-71.0, 53.0))));
                                    float prof = saturate(1.0 + dIn / wD);
                                    prof = prof * prof * (3.0 - 2.0 * prof);
                                    float dk = prof * (0.40 + 0.60 * fresh);
                                    if (dk > depo) { depo = dk; depoFresh = fresh; }
                                }
                            }
                            // the bank the moving rubber pushes ahead of it: points a little further
                            // along the stroke than the blade is now (in metres: the phase's own slope)
                            float phk = tk / D;
                            if (phk > 0.0 && phk < 1.0)
                            {
                                float uk = phk < 0.5 ? phk * 2.0 : 2.0 - phk * 2.0;
                                float s = 0.5 - 0.5 * cos(3.14159265 * uk);
                                float2 gpP = float2(ddx(p), ddy(p));
                                float gradP = max(length(gpP) / max(aa, 1e-6), 0.5);
                                float dm = (p - s) * (phk < 0.5 ? 1.0 : -1.0) / gradP;
                                if (bank2)
                                {
                                    // (RAINGLASS2) THE BANK AS HIS CLIP'S: a wedge of pooled water thickest
                                    // against the rubber, a rounded lip in front, as deep as a hand at full
                                    // load and ragged along its length. It gathers on the way up (the whole
                                    // screen's water); on the way back it starts again from what fell since.
                                    // The phase's slope from the map itself, two texels either side (a pixel's
                                    // own derivative steps at every texel and frayed the bank's lip into grit),
                                    // one-sided where a neighbour lies outside the swept area.
                                    float dirK = phk < 0.5 ? 1.0 : -1.0;
                                    if (inK > 0.0 && (p - s) * dirK > -0.02 && (p - s) * dirK < 0.30)
                                    {
                                        float2 tx = _WiperPhase_TexelSize.xy * 2.0;
                                        float4 sR = tex2Dlod(_WiperPhase, float4(i.uv + float2(tx.x, 0.0), 0.0, 0.0));
                                        float4 sL = tex2Dlod(_WiperPhase, float4(i.uv - float2(tx.x, 0.0), 0.0, 0.0));
                                        float4 sU = tex2Dlod(_WiperPhase, float4(i.uv + float2(0.0, tx.y), 0.0, 0.0));
                                        float4 sD = tex2Dlod(_WiperPhase, float4(i.uv - float2(0.0, tx.y), 0.0, 0.0));
                                        float pR = k == 0 ? sR.r : sR.g, pL = k == 0 ? sL.r : sL.g, pU = k == 0 ? sU.r : sU.g, pD = k == 0 ? sD.r : sD.g;
                                        float spanU = (pR > 0.002 ? 1.0 : 0.0) + (pL > 0.002 ? 1.0 : 0.0);
                                        float spanV = (pU > 0.002 ? 1.0 : 0.0) + (pD > 0.002 ? 1.0 : 0.0);
                                        float dpdu = ((pR > 0.002 ? pR : p) - (pL > 0.002 ? pL : p)) / (max(spanU, 1.0) * tx.x);
                                        float dpdv = ((pU > 0.002 ? pU : p) - (pD > 0.002 ? pD : p)) / (max(spanV, 1.0) * tx.y);
                                        float2 gM = float2(dpdu / max(_Metres.x, 1e-4), dpdv / max(_Metres.y, 1e-4));   // per metre on the pane
                                        float gradM = max(length(gM), 0.5);
                                        float dm2 = (p - s) * dirK / gradM;
                                        float carry = phk < 0.5 ? smoothstep(0.0, 0.25, s) : 0.35 + 0.40 * smoothstep(0.0, 0.6, 1.0 - s);
                                        float rag = 0.5 + 0.5 * sin(dot(q, float2(14.0, 19.0)) + 1.1 * sin(dot(q, float2(-27.0, 11.0)) + _CabinRun * 3.0));
                                        float wB = R2G_BANK2_M * (0.45 + 0.55 * carry) * (0.78 + 0.40 * rag);
                                        float x = dm2 / wB;
                                        float hb = inK * smoothstep(-0.004, 0.002, dm2) * sqrt(saturate(1.0 - x)) * (1.0 - smoothstep(0.96, 1.0, x))
                                                 * smoothstep(0.0, 0.08, s + (phk < 0.5 ? 0.0 : 0.2)) * (0.55 + 0.45 * carry);
                                        if (hb > ahead)
                                        {
                                            ahead = hb;
                                            bankX = saturate(x);
                                            // the stroke's direction in screen pixels (p grows along the upstroke)
                                            float2 gPix = float2(dpdu * uvx.x + dpdv * uvx.y, dpdu * uvy.x + dpdv * uvy.y);
                                            bankPx = dirK * gPix / max(length(gPix), 1e-8);
                                        }
                                    }
                                }
                                else
                                ahead = max(ahead, inK * smoothstep(-0.002, 0.004, dm) * (1.0 - smoothstep(0.010, R2G_BANK_M, dm))
                                                       * smoothstep(0.0, 0.15, s));
                            }
                        }
                        ridge = edgeBank * (1.0 - inside);
                        bank = ahead;
                        swept = inside;
                        // (RAINGLASS2, CLEAR) the film behind the blade lasts a moment, not a second
                        smear = inside * exp(-since * (_PSXRain2Glass2.z > 0.5 ? 9.0 : 4.0)) * step(since, 999.0);
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
                    fill = saturate(since / max(_CabinWipe2.x * (_PSXRain2Glass2.z > 0.5 ? 1.0 + 1.8 * _CabinWind : 1.0), 0.05));
                    fill *= fill;
                }

                // (RAINGLASS2) the lump's own slope, smooth (uniform flow: before any branch on it)
                float2 gDepo = float2(ddx(depo), ddy(depo));
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
                    // (RAINGLASS2, COVER) the parked windscreen covered edge to edge, as his: every drop its
                    // own outline, merged blobs and smears, a coarser size of big flat blobs, and rivulets
                    // that run in bursts and stop (fe3ce8a6's parked frame was sparser than the one before)
                    float cov = _PSXRain2Glass2.y > 0.5 && b2 && _Wiper.w > 0.001 ? smoothstep(0.2, 0.9, park) : 0.0;
                    float irr = _PSXRain2Glass2.y > 0.5 && b2 ? 1.0 : 0.0;
                    float4 bB;
                    if (b2)
                    {
                        bB = BeadMax(Bead4(q, 0.016 * bs, rainW * lerp(0.62, 0.25, wind) * dens * (1.0 + 0.35 * cov), 1.7, 1.0, _Down.xy, irr),
                                     Bead4(q, 0.028 * bs, rainW * lerp(0.42, 0.15, wind) * dens * (1.0 + 0.45 * cov), 9.3, 1.05 + 0.15 * cov, _Down.xy, irr));
                        if (cov > 0.01)
                            bB = BeadMax(bB, Bead4(q, 0.046 * bs, rainW * 0.55 * cov, 3.9, 1.0, _Down.xy, 1.0));
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
                    float hR = Runners2(q, _Down.xy, 0.026, 0.30, _CabinRun, rainW * (0.45 * dens + 0.25 * park + 0.35 * cov), 4.1, 0.55 * park, cov > 0.01 ? 1.0 : 0.0) * (1.0 - wind);
                    float hS = Runners(q, wdir, 0.020, 0.55, _CabinBlow, rainW * (b2 ? 0.95 : 0.75), 7.7, 0.85) * wind;
                    float hgt = max(hB, max(hR, hS));
                    // (RAIN 2) the bank a blade leaves, ragged
                    // (a thin film packed with small beads, not a smooth line)
                    if (ridge > 0.0) hgt = max(hgt, ridge * rain * (0.30 + 0.70 * Beads(q, 0.0075, 0.95, 13.1)));
                    // (RAINGLASS) the bank the blade drives before it is POOLED water (his clips): a
                    // thick wavering band, not beads - and what it leaves at the arcs' edges runs
                    // off in rivulets, down the screen parked, back along the air at speed
                    bool inBank = false;
                    float4 lump4 = float4(0.0, 0.0, 0.0, 1.0);
                    if (bank2)
                    {
                        // (RAINGLASS2) the bank swallows the beads it reaches; the lump at the arc's end
                        // is pooled water with a ragged, beaded outer edge
                        inBank = bank > 0.06;
                        if (inBank)
                        {
                            // the gathered water is lumpy - drops run together into blobs riding the
                            // band (his clip: a band of bright, bent blobs before the rubber)
                            lump4 = Bead4(q, 0.011, 1.0, 23.1, 1.35, _Down.xy, 1.0);
                            lump4.x *= smoothstep(0.06, 0.35, bank);
                            hgt = max(hgt * (1.0 - smoothstep(0.06, 0.25, bank)), bank * rain * (0.55 + 0.45 * lump4.x));
                        }
                        if (depo > 0.0) hgt = max(hgt, depo * rain * (0.70 + 0.30 * Beads(q, 0.0070, 0.95, 19.3)));
                    }
                    else if (bank > 0.0)
                        hgt = max(hgt, bank * rain * (0.62 + 0.18 * sin(dot(q, float2(61.0, 47.0)) + _CabinRun * 23.0)
                                                         + 0.20 * Beads(q, 0.0065, 0.9, 17.7)));
                    if (edgeZone > 0.0 && b2)
                    {
                        float2 rdir = normalize(lerp(_Down.xy, wdir, saturate(wind * 1.4)));
                        // (RAINGLASS2) a fresh lump runs off in more and heavier rivulets
                        float runK = bank2 ? 0.55 + 0.9 * depoFresh : 0.85;
                        hgt = max(hgt, edgeZone * Runners(q, rdir, bank2 ? 0.017 : 0.014, bank2 ? 0.24 : 0.18, wind > 0.3 ? _CabinBlow : _CabinRun * 2.0,
                                                          rain * runK, 21.3, 0.7));
                    }
                    bool beadTop = hB >= max(hR, hS) && hB > 0.0 && (!bank2 || (!inBank && hB >= hgt - 1e-4));
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
                        if (b2) sheet = max(rainW * fill * R2G_SHEET * lerp(1.0, 0.5, wind), 0.45 * smear * rain) * (1.0 - cover);
                        // (RAINGLASS2, CLEAR) at speed no film builds where the blade has been: his fast
                        // clip is a dark, clear centre with the water streaming outside the arcs - what
                        // lands there now is drops and streaks (above), and the blade's smear a moment
                        if (b2 && _PSXRain2Glass2.z > 0.5)
                            sheet = max(rainW * fill * R2G_SHEET * lerp(1.0, 0.5, wind) * (1.0 - swept * smoothstep(0.08, 0.40, wind)),
                                        0.45 * smear * rain) * (1.0 - cover);
                        float2 wob = float2(sin(q.y * 95.0 + sin(q.x * 37.0 + _CabinRun * 9.0) * 1.7),
                                            sin(q.x * 80.0 + sin(q.y * 41.0 - _CabinRun * 12.0) * 1.9));
                        dpx += wob * (R2G_WOBBLE * px1080 * sheet);
                        // (RAINGLASS2) THE BANK AND THE LUMP ARE LENSES: pooled water centimetres across
                        // bends what is behind it by tens of pixels - the scene ahead swims in the bank,
                        // most at its rounded lip, and wavers as it rolls (his tunnel clip: the lamps
                        // smeared and doubled in the band before the blade)
                        float lipL = 0.0, bankShade = 1.0, bankSheen = 0.0;
                        float2 bankBlur = float2(0.0, 0.0);
                        if (bank2 && !beadTop && (inBank || depo > 0.05))
                        {
                            float pxm = sqrt(m2);
                            float roll = 0.75 + 0.25 * sin(dot(q, float2(57.0, 43.0)) + _CabinRun * 19.0);
                            if (!inBank)
                            {
                                float gl = length(gDepo);
                                float slopeN = min(gl * (0.04 / max(pxm, 1e-6)), 1.5);
                                dpx += gDepo / max(gl, 1e-6) * (slopeN * R2G_BANK_PX * 0.7 * px1080 * roll);
                            }
                            if (inBank)
                            {
                                // a lens of the bank's own profile (thickness ~ sqrt(1 - x)): what is behind it
                                // is drawn out toward the lip - a magnifying band, not a shifted copy - and
                                // ripples along its length as it rolls; every blob in it bends its own bit
                                float2 alongPx = float2(-bankPx.y, bankPx.x);
                                float ripple = sin(dot(q, float2(131.0, 97.0)) + _CabinRun * 31.0) * 0.5
                                             + sin(dot(q, float2(-63.0, 149.0)) - _CabinRun * 23.0) * 0.5;
                                float bendK = 1.0 - sqrt(saturate(1.0 - bankX));
                                dpx = bankPx * (R2G_BANK_PX * px1080 * (bendK * 1.6 - 0.3) * roll) + alongPx * (ripple * 3.0 * px1080);
                                float2 dql = lump4.yz * lump4.w;
                                float dtl = qdx.x * qdy.y - qdx.y * qdy.x;
                                float2 dpl = float2(qdy.y * dql.x - qdy.x * dql.y, qdx.x * dql.y - qdx.y * dql.x) / (abs(dtl) > 1e-12 ? dtl : 1e-12);
                                dpx -= dpl * (2.5 * smoothstep(0.0, 0.3, lump4.x));
                                float lx = (bankX - 0.84) / 0.10, mx = (bankX - 0.99) / 0.035;
                                lipL = 0.30 * exp(-lx * lx) * smoothstep(0.06, 0.3, bank) + 0.12 * smoothstep(0.3, 0.8, lump4.x) * saturate(dot(lump4.yz, -_Down.xy));
                                // a thick water surface is a mirror: the whole band carries a wavering sheen of
                                // the sky and the lamps (what makes it read against a plain road), and it blurs
                                // what is seen through it along the rubber
                                float lumpRim = smoothstep(0.55, 0.95, length(lump4.yz)) * step(0.01, lump4.x);
                                // (11-22 17:40 at 4K it read as milky foam: clear water now - a faint sheen, the
                                // light caught on each blob's upper rim, a dark outline round each blob)
                                bankSheen = smoothstep(0.06, 0.30, bank) * (0.05 + 0.08 * ripple + 0.55 * lumpRim * saturate(dot(lump4.yz, -_Down.xy) + 0.2));
                                bankShade *= 1.0 - 0.40 * lumpRim * saturate(0.4 - dot(lump4.yz, -_Down.xy));
                                bankBlur = alongPx * (2.5 * px1080) + bankPx * (1.0 * px1080);
                                bankShade *= (0.78 + 0.37 * smoothstep(0.0, 0.25, bankX))
                                          * (1.0 - 0.45 * exp(-mx * mx));
                            }
                            else bankShade = 1.0 + 0.15 * depoFresh;
                            dpx = clamp(dpx, -R2G_MAXPX * px1080, R2G_MAXPX * px1080);
                        }
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
                        if (bankSheen > 0.0)
                        {
                            float2 bb = bankBlur / _ScreenParams.xy;
                            seen = (seen * 2.0 + tex2Dlod(_PSXSSRColour, float4(suv + bb, 0.0, 0.0)).rgb
                                               + tex2Dlod(_PSXSSRColour, float4(suv - bb, 0.0, 0.0)).rgb) * 0.25;
                        }
                        // (a bead by the pillar would show the pillar - a black fleck on
                        // the pane: it keeps most of what is straight behind it)
                        if (beadTop) seen = max(seen, tex2Dlod(_PSXSSRColour, float4(i.pos.xy / _ScreenParams.xy, 0.0, 0.0)).rgb * 0.75);
                        // (RAINGLASS) the picture in a bead is the scene's light, less its tint:
                        // a third of the way to grey (sodium-lit streets made every bead orange)
                        // (and, at night under an unlit sky, a bead's upside-down picture is mostly that
                        // black sky: half of it is the scene straight behind, so a bead reads as clear water
                        // with a rim, not a grey pebble)
                        if (b2 && beadTop) seen = lerp(lerp(seen, tex2Dlod(_PSXSSRColour, float4(i.pos.xy / _ScreenParams.xy, 0.0, 0.0)).rgb, 0.45),
                                                       dot(seen, float3(0.30, 0.59, 0.11)).xxx, 0.22);
                        through = seen * (wetA * (1.0 - rimK * rim * cover) * gather * bankShade);
                        // the glint: one bright point up and to the side of each bead's crown
                        float g2 = beadTop ? (1.0 - smoothstep(0.0, 0.32, length(bB.yz - float2(-0.30, 0.38)))) * cover : glint;
                        if (lipL > 0.0 || (bank2 && inBank)) g2 = 0.0;   // (RAINGLASS2: the bank's light is its lip's, below)
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
                              + sheet * R2G_VEIL * (b2 ? park + 0.4 * wind : park) * (ambient * 0.5 + sky * 0.25 + lamp)
                              + (b2 ? smear * rain * 0.18 * (lamp * 1.5 + sky * 0.2) : 0.0)
                              // (RAINGLASS2) the bank's lip catches the light: a bright wavering line
                              + lipL * rain * (lerp(lamp, dot(lamp, float3(0.30, 0.59, 0.11)).xxx, 0.5) * 0.9 + sky * 0.30 + ambient * 0.30 + _PSXLightColor.rgb * 0.20 * day)
                              // (a lamp's colour only half: a brake lamp beside the car tinted the whole band red)
                              + bankSheen * rain * (lerp(lamp, dot(lamp, float3(0.30, 0.59, 0.11)).xxx, 0.6) * 0.40 + sky * 0.14 + ambient * 0.16 + _PSXLightColor.rgb * 0.06 * day);
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
