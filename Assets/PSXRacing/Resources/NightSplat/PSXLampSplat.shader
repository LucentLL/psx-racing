// NIGHT 3 (the city-night round, 2026-11-23): the two light fields splatted on
// the GPU, top-down, into world-anchored render textures (StreetLights.Night3.cs,
// CarLights.Pools.cs). Loaded from Resources (never stripped). Additive.
//
// Pass 0, THE STATIC LAMP FIELD: one quad per lit city-street lamp, the same
// windowed discs as StreetLights.Field.cs' CPU splat, texel for texel:
//   R = pool x (1 - d^2/rp^2)^2, G = glow x (1 - d^2/rg^2)^2,
//   B = G x (head height - base), A = G of the mercury heads.
// Pass 1, THE CAR POOLS: one quad per headlamp of every lit car not (wholly) in
// the per-pixel table, the low beam of PSXHeadlights.cginc evaluated on the
// ground under the car (N up), x the share the table does not give it:
//   R = the beam's light (x the halogen, linear, in PSXLamps.cginc), G = R x (ground - base).
Shader "Hidden/PSX/LampSplat"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _SplatR;   // x 1/rp^2, y 1/rg^2, z the atlas width of the bearings (NIGHT 4)
            // NIGHT 4 (StreetLights.Night4.cs): what each lamp's head sees - per bearing, up to two
            // shadowed stretches of ground (from s.x to s.y, from s.z to s.w, plan metres from the head)
            sampler2D _LampVis;
            float _PSXN4Tight;   // (night5) StreetLights.N4Tight: the texel's whole footprint, a hard shadow edge
            // one point's visibility from the lamp's head (d: plan offset from the head)
            float LampVisAt(float2 d, float4 lamp, float zScale, float tight)
            {
                float a = frac(atan2(d.y, d.x) * 0.15915494);
                float4 s = tex2Dlod(_LampVis, float4(lamp.z + a * zScale, lamp.w, 0.0, 0.0));
                float r = length(d);
                float lo = lerp(0.4, 0.3, tight), hi = lerp(1.2, 0.3, tight);
                float sh = smoothstep(s.x - lo, s.x + hi, r) * (1.0 - smoothstep(s.y - 1.0, s.y + 0.5, r))
                         + smoothstep(s.z - lo, s.z + hi, r) * (1.0 - smoothstep(s.w - 1.0, s.w + 0.5, r));
                return saturate(1.0 - sh);
            }
            struct appdata { float3 vertex : POSITION; float4 lamp : TEXCOORD0; float4 amt : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 w : TEXCOORD0; float4 lamp : TEXCOORD1; float4 amt : TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, float4(v.vertex.xy, 0.0, 1.0)));
                o.w = v.vertex.xy; o.lamp = v.lamp; o.amt = v.amt;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 d = i.w - i.lamp.xy;
                float d2 = dot(d, d);
                float p = saturate(1.0 - d2 * _SplatR.x); p *= p;
                float g = saturate(1.0 - d2 * _SplatR.y); g *= g;
                // lamp.z >= 0: this lamp's view is in the atlas (u of its first bearing, v its row)
                if (i.lamp.z >= 0.0)
                {
                    float vis = LampVisAt(d, i.lamp, _SplatR.z, _PSXN4Tight);
                    // (night5) a surface reads this 2 m texel BILINEARLY: a lit texel beside a shadow's edge
                    // spread its light up to 2 m into the shadow (the pavement round a building's corner, the
                    // exe's +11). With N4Tight a texel is lit only as much as the least-lit corner of its own
                    // footprint, so the light ends within a metre of the edge, softly.
                    if (_PSXN4Tight > 0.5)
                    {
                        vis = min(vis, min(min(LampVisAt(d + float2(-0.9, -0.9), i.lamp, _SplatR.z, 1.0), LampVisAt(d + float2(0.9, -0.9), i.lamp, _SplatR.z, 1.0)),
                                           min(LampVisAt(d + float2(-0.9, 0.9), i.lamp, _SplatR.z, 1.0), LampVisAt(d + float2(0.9, 0.9), i.lamp, _SplatR.z, 1.0))));
                    }
                    p *= vis; g *= vis;
                }
                float pool = i.amt.x * p, glow = i.amt.y * g;
                return float4(pool, glow, glow * i.amt.z, glow * i.amt.w);
            }
            ENDCG
        }
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // PSXHeadlights.cginc's low beam, the same constants
            #define BEAM_NEAR          0.55
            #define BEAM_NEAR_FROM     3.0
            #define BEAM_NEAR_TO       10.0
            #define BEAM_FADE_M        45.0
            #define BEAM_CUT_LO        0.016
            #define BEAM_CUT_HI        0.032
            #define BEAM_SPILL         0.12
            #define BEAM_SPILL_COS     0.342
            #define BEAM_SPILL_FROM    10.0
            #define BEAM_SPILL_TO      25.0
            #define BEAM_SPILL_CUT_LO  0.05
            #define BEAM_SPILL_CUT_HI  0.25
            #define BEAM_FACE_LO       -0.05
            #define BEAM_FACE_HI       0.02
            float _SplatBaseY;
            float4 _SplatCos;   // x cos of the outer half-spread, y of the inner (P1: fwd.w / rgt.w carry the ground's slopes)
            // NIGHT 4 (CarLights.Occlusion.cs): per car, the beam's reach on 64 bearings (+-75 deg)
            sampler2D _PSXHeadVis;
            float _PSXN4Tight;
            // what of the beam reaches plan point w past the fan's origin o (F, Rt: the car's axes)
            float PoolReachAt(float2 w, float2 o, float2 F, float2 Rt, float v, float tight)
            {
                float2 d0 = w - o;
                float ox = dot(d0, Rt), oz = dot(d0, F);
                float u = 0.5 + atan2(ox, oz) * 0.38197186;
                float r = tex2Dlod(_PSXHeadVis, float4(u, v, 0.0, 0.0)).r;
                if (tight > 0.5)
                {
                    // (night5) the shorter of the two bins either side (a corner's clear bin no longer averages in)
                    float t0 = clamp(floor(u * 64.0 - 0.5), 0.0, 62.0);
                    r = min(tex2Dlod(_PSXHeadVis, float4((t0 + 0.5) / 64.0, v, 0.0, 0.0)).r, tex2Dlod(_PSXHeadVis, float4((t0 + 1.5) / 64.0, v, 0.0, 0.0)).r);
                }
                float dd = sqrt(ox * ox + oz * oz);
                return tight > 0.5 ? 1.0 - smoothstep(r + 0.15, r + 0.5, dd) : 1.0 - smoothstep(r + 0.3, r + 1.8, dd);
            }
            // vertex.xy = the quad corner (world xz); lamp: xyz the lamp (world), w range;
            // fwd: xyz axis, w cos outer; right: xyz, w cos inner; amt: x weight, y the ground's height under the lamp
            struct appdata { float3 vertex : POSITION; float4 lamp : TEXCOORD0; float4 fwd : TEXCOORD1; float4 rgt : TEXCOORD2; float4 amt : TEXCOORD3; };
            struct v2f { float4 pos : SV_POSITION; float2 w : TEXCOORD0; float4 lamp : TEXCOORD1; float4 fwd : TEXCOORD2; float4 rgt : TEXCOORD3; float4 amt : TEXCOORD4; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, float4(v.vertex.xy, 0.0, 1.0)));
                o.w = v.vertex.xy; o.lamp = v.lamp; o.fwd = v.fwd; o.rgt = v.rgt; o.amt = v.amt;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                // the ground under this texel: the plane under the car through the ground under the lamp (P1)
                float gyT = i.amt.y + i.fwd.w * (i.w.x - i.lamp.x) + i.rgt.w * (i.w.y - i.lamp.z);
                float3 wpos = float3(i.w.x, gyT, i.w.y);
                float3 d = wpos - i.lamp.xyz;
                float dist = length(d);
                float3 F = i.fwd.xyz, Rt = i.rgt.xyz, U = cross(F, Rt);
                float fz = dot(d, F), fx = dot(d, Rt), fy = dot(d, U);
                float hz = fz * rsqrt(fx * fx + fz * fz + 1e-4);
                float spread = smoothstep(_SplatCos.x, _SplatCos.y, hz);
                float slope = fy / max(fz, 0.25);
                float cut = 1.0 - smoothstep(BEAM_CUT_LO, BEAM_CUT_HI, slope);
                float front = saturate(fz * 1.5);
                float range = max(_SplatCos.z, 1.0);
                float att = lerp(BEAM_NEAR, 1.0, smoothstep(BEAM_NEAR_FROM, BEAM_NEAR_TO, dist))
                          * (1.0 - smoothstep(range - BEAM_FADE_M, range, dist));
                float spill = BEAM_SPILL * smoothstep(BEAM_SPILL_COS, _SplatCos.x, hz)
                            * (1.0 - smoothstep(BEAM_SPILL_CUT_LO, BEAM_SPILL_CUT_HI, slope))
                            * (1.0 - smoothstep(BEAM_SPILL_FROM, BEAM_SPILL_TO, dist));
                float nl = -d.y / max(dist, 0.05);   // N = up
                float facing = smoothstep(BEAM_FACE_LO, BEAM_FACE_HI, nl);
                float a = (spread * cut * att + spill) * front * facing * i.amt.x;
                if (i.amt.z >= 0.0)
                {
                    // (4b) the bearing and the distance from the fan's own origin, where the reaches were measured
                    float2 o0 = float2(i.lamp.w, i.amt.w);
                    float vr = PoolReachAt(i.w, o0, F.xz, Rt.xz, i.amt.z, _PSXN4Tight);
                    // (night5) the pool's texel is 2 m and read bilinearly: lit only as much as the least-lit
                    // corner of its footprint (the scalloped strip of grass behind a Jersey, editor and exe +40)
                    if (_PSXN4Tight > 0.5)
                    {
                        vr = min(vr, min(min(PoolReachAt(i.w + float2(-0.9, -0.9), o0, F.xz, Rt.xz, i.amt.z, 1.0), PoolReachAt(i.w + float2(0.9, -0.9), o0, F.xz, Rt.xz, i.amt.z, 1.0)),
                                         min(PoolReachAt(i.w + float2(-0.9, 0.9), o0, F.xz, Rt.xz, i.amt.z, 1.0), PoolReachAt(i.w + float2(0.9, 0.9), o0, F.xz, Rt.xz, i.amt.z, 1.0))));
                    }
                    a *= vr;
                }
                return float4(a, a * (gyT - _SplatBaseY), 0.0, 0.0);
            }
            ENDCG
        }
    }
}
