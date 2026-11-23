// PSX/LampConeAir - NIGHT 3 (the city-night round, 2026-11-23): the light cone
// under EVERY street lamp that shows a halo, in rain, fog and haze only.
//
// The owner, PC59: "I see their triangles of light appear as I drive closer.
// Otherwise, I can see their orbs of light clearly from half a mile away." The
// old cones (PSX/LampCone, LampCones.cs) were the 32 lamps nearest the eye, so
// a cone appeared on a lamp whose halo had shown for half a mile. His clear-night
// frames show no cone at all: in clear air there is none (the renderers are off).
// In rain, fog and haze every lamp's cone is drawn, from one merged mesh per tile
// (NightGlow.Air.cs; vertex data in world space), the same per-pixel chord as
// PSX/LampCone, thinned only by the air between it and the eye
// (exp(-d / _PSXNight3.z)) and x _PSXNight3.y (the weather's haze).
Shader "PSX/LampConeAir"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Blend One One, Zero One
            ZWrite Off
            ZTest LEqual
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/PSXTone.cginc"
            #define CONE_SAMPLES    4
            #define CONE_H0         4.0
            #define CONE_TOP_SOFT   0.6
            #define CONE_GROUND_LO  0.3
            #define CONE_GROUND_HI  3.5
            #define CONE_EYE_LO     0.5
            #define CONE_EYE_HI     4.0
            #define CONE_REF_M      1.0
            #define CONE_MAX_GAIN   2.5
            #define CONE_K          0.78   // tan 38 deg (LampCones.HalfAngleDeg)
            float4 _PSXNight3;   // x on, y the air's haze (0 = clear: nothing), z the air's extinction length (m)
            float _PSXNight;
            struct appdata
            {
                float4 vertex : POSITION;  // unit cone: rings at y 0 (head) and -1 (foot), radius 1
                float4 a : TEXCOORD0;      // xyz the head (world), w its height over the ground
                float4 b : TEXCOORD1;      // rgb the light (linear x strength), w the radius at the head
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float4 a : TEXCOORD1;
                float4 b : TEXCOORD2;
            };
            v2f vert (appdata v)
            {
                v2f o;
                float t = saturate(-v.vertex.y);
                float r = v.b.w + CONE_K * v.a.w * t;
                float3 w = v.a.xyz + float3(v.vertex.x * r, -t * v.a.w, v.vertex.z * r);
                o.pos = mul(UNITY_MATRIX_VP, float4(w, 1.0));
                o.wpos = w; o.a = v.a; o.b = v.b;
                if (_PSXNight3.y <= 0.0) o.pos = float4(0.0, 0.0, 0.0, 1.0);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float gate = saturate((_PSXNight - 0.75) * 4.0);
                float3 C = _WorldSpaceCameraPos;
                float3 v = normalize(i.wpos - C);
                float H = max(i.a.w, 0.5);
                float r0 = i.b.w;
                float K = CONE_K, K2 = K * K;
                float hTop = r0 / K;
                float3 o = C - (i.a.xyz + float3(0.0, hTop, 0.0));
                float t0 = 0.0, t1 = 1e5;
                if (abs(v.y) > 1e-5)
                {
                    float ta = (-hTop - o.y) / v.y;
                    float tb = (-(hTop + H) - o.y) / v.y;
                    t0 = max(t0, min(ta, tb));
                    t1 = min(t1, max(ta, tb));
                }
                else if (-o.y < hTop || -o.y > hTop + H) return fixed4(0, 0, 0, 0);
                float qa = dot(v.xz, v.xz) - K2 * v.y * v.y;
                float qb = dot(o.xz, v.xz) - K2 * o.y * v.y;
                float qc = dot(o.xz, o.xz) - K2 * o.y * o.y;
                float disc = qb * qb - qa * qc;
                if (qa > 1e-6)
                {
                    if (disc <= 0.0) return fixed4(0, 0, 0, 0);
                    float sq = sqrt(disc);
                    t0 = max(t0, (-qb - sq) / qa);
                    t1 = min(t1, (-qb + sq) / qa);
                }
                else if (qa < -1e-6 && disc > 0.0)
                {
                    float sq = sqrt(disc);
                    float ra = (-qb - sq) / qa, rb = (-qb + sq) / qa;
                    if (v.y < 0.0) t0 = max(t0, max(ra, rb));
                    else           t1 = min(t1, min(ra, rb));
                }
                if (t1 <= t0) return fixed4(0, 0, 0, 0);
                float len = t1 - t0;
                float sum = 0.0;
                [unroll]
                for (int s = 0; s < CONE_SAMPLES; s++)
                {
                    float ts = t0 + len * ((s + 0.5) / CONE_SAMPLES);
                    float3 p = C + v * ts;
                    float h = i.a.y - p.y;
                    float R = r0 + K * max(h, 0.0);
                    float2 dxz = p.xz - i.a.xz;
                    float x2 = saturate(dot(dxz, dxz) / max(R * R, 1e-4));
                    float hh = h / CONE_H0;
                    float spread = 1.0 / (1.0 + hh * hh);
                    float top = smoothstep(0.0, CONE_TOP_SOFT, h);
                    float ground = smoothstep(CONE_GROUND_LO, CONE_GROUND_HI, H - h);
                    float near = smoothstep(CONE_EYE_LO, CONE_EYE_HI, ts);
                    sum += (1.0 - x2) * spread * top * ground * near;
                }
                float k = min(sum * len / (CONE_SAMPLES * CONE_REF_M), CONE_MAX_GAIN);
                float air = _PSXNight3.y * exp(-(t0 + 0.5 * len) / max(_PSXNight3.z, 1.0));
                return fixed4(i.b.rgb * (k * gate * air * PSXAdaptGain()), 0.0);
            }
            ENDCG
        }
    }
}
