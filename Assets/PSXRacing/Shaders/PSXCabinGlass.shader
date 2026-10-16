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
            #include "PSXLamps.cginc"
            #include "PSXTone.cginc"
            #include "PSXSkyReflect.cginc"

            fixed4 _Tint;
            float4 _Metres, _Down, _Wind, _Out;
            float _PaneRain;
            float _CabinRain, _CabinWind, _CabinRun, _CabinBlow;
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXLightColor;
            float4 _PSXLightDir;
            float _PSXNight;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 wnrm : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
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

                // The rain.
                float rain = _CabinRain * _PaneRain;
                if (rain > 0.002)
                {
                    float2 q = i.uv * _Metres.xy;
                    float wind = _CabinWind;
                    // The windscreen's streaks bend outward from its middle.
                    float2 wdir = normalize(_Wind.xy + _Out.xy * (dot(q, _Out.xy) - _Wind.w) * _Wind.z * wind);
                    float hB = max(Beads(q, 0.020, rain * lerp(0.55, 0.25, wind), 1.7),
                                   Beads(q, 0.034, rain * lerp(0.35, 0.15, wind), 9.3));
                    float hR = Runners(q, _Down.xy, 0.030, 0.30, _CabinRun, rain * 0.45, 4.1, 0.0) * (1.0 - wind);
                    float hS = Runners(q, wdir, 0.022, 0.55, _CabinBlow, rain * 0.75, 7.7, 0.85) * wind;
                    float hgt = max(hB, max(hR, hS));
                    float cover = smoothstep(0.0, 0.25, hgt);
                    if (cover > 0.001)
                    {
                        float2 grad = float2(ddx(hgt), ddy(hgt)) / max(fwidth(hgt), 1e-3);
                        float spec = saturate(-grad.y * 0.6 + grad.x * 0.25);
                        float rim = 1.0 - smoothstep(0.05, 0.45, hgt);
                        float3 lamp = PSXLampsAt(i.wpos) * 0.6;
                        float3 drop = (ambient * 1.1 + lamp + sky * 0.25) * (1.0 - 0.75 * rim)
                                    + (sky * 0.6 + _PSXLightColor.rgb * 0.3 * day + lamp) * spec * 0.8;
                        rgb = lerp(rgb, drop, cover * 0.85);
                        a = lerp(a, 0.55, cover * 0.85);
                    }
                }
                rgb = PSXTone(rgb);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
}
