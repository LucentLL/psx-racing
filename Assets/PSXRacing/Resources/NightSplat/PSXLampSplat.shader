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
            float4 _SplatR;   // x 1/rp^2, y 1/rg^2
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
                float3 wpos = float3(i.w.x, i.amt.y, i.w.y);
                float3 d = wpos - i.lamp.xyz;
                float dist = length(d);
                float3 F = i.fwd.xyz, Rt = i.rgt.xyz, U = cross(F, Rt);
                float fz = dot(d, F), fx = dot(d, Rt), fy = dot(d, U);
                float hz = fz * rsqrt(fx * fx + fz * fz + 1e-4);
                float spread = smoothstep(i.fwd.w, i.rgt.w, hz);
                float slope = fy / max(fz, 0.25);
                float cut = 1.0 - smoothstep(BEAM_CUT_LO, BEAM_CUT_HI, slope);
                float front = saturate(fz * 1.5);
                float range = max(i.lamp.w, 1.0);
                float att = lerp(BEAM_NEAR, 1.0, smoothstep(BEAM_NEAR_FROM, BEAM_NEAR_TO, dist))
                          * (1.0 - smoothstep(range - BEAM_FADE_M, range, dist));
                float spill = BEAM_SPILL * smoothstep(BEAM_SPILL_COS, i.fwd.w, hz)
                            * (1.0 - smoothstep(BEAM_SPILL_CUT_LO, BEAM_SPILL_CUT_HI, slope))
                            * (1.0 - smoothstep(BEAM_SPILL_FROM, BEAM_SPILL_TO, dist));
                float nl = -d.y / max(dist, 0.05);   // N = up
                float facing = smoothstep(BEAM_FACE_LO, BEAM_FACE_HI, nl);
                float a = (spread * cut * att + spill) * front * facing * i.amt.x;
                return float4(a, a * (i.amt.y - _SplatBaseY), 0.0, 0.0);
            }
            ENDCG
        }
    }
}
