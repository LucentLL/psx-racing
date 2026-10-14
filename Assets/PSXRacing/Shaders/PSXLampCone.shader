// PSX/LampCone - the light cone under a street lamp at night (LampCones.cs).
//
// Touge Attack's signature (2026-10-13): a lone lamp on a black mountain road
// seen through a thin haze, the cone of its light hanging in the air down to
// the road. A cheap fake, not volumetrics: one static mesh of CONE_MAX unit
// truncated cones (uv.x = the slot), placed in the vertex shader from two
// uniform arrays StreetLights fills for the nearest lamps - ONE draw call for
// all of them; an empty slot is all zeros and collapses to a point.
//
// THE LIGHT, PER PIXEL, FROM THE RAY. The pass draws the cone's FAR side (Cull
// Front: the eye inside a cone still sees it), and the fragment solves where
// the eye's ray is inside the cone - the lower nappe of the infinite cone
// whose sides pass through the head's ring (TopRadius) at the half-angle,
// cut by the slab between the head and the lamp's ground - then integrates
// the haze along that chord in CONE_SAMPLES steps:
//   * radial 1 - (d/R)^2 - zero at the rim, so a ray that grazes the cone
//     (the edge of its silhouette) has nothing: the view-angle fade, no line;
//   * 1 / (1 + (h/CONE_H0)^2) under the head - the light spreading out, so the
//     cone is brightest just under the lamp, as through real haze;
//   * soft in over CONE_TOP_SOFT under the head, and soft OUT over the last
//     metres above the ground (CONE_GROUND_*) - the foot never meets the road
//     in an edge. There is no depth texture on the phone renderer, so the
//     fade is against the lamp's own ground height (found once by a ray).
//   * thinned within a few metres of the eye, so driving under a lamp is a
//     brief haze, not a white screen.
// x _PSXConeB.rgb (the lamp's linear colour x its gain x the strength, the
// haze of the weather, the lit/unlit mix and the cap's hand-over fade, all
// in C#), x the night gate (full NIGHT only) and the eye's adaptation, like
// the halo over it.
//
// ALPHA IS UNTOUCHED (Blend One One, Zero One): on the PSX camera's frame the
// alpha is the emitter mask (PSXTone.cginc), and a cone is scattered light,
// not a source - the halation and the sun glare must not bloom it.
Shader "PSX/LampCone"
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
            #include "PSXTone.cginc"

            #define CONE_MAX        32     // == StreetLights.ConeMax
            #define CONE_SAMPLES    4
            #define CONE_H0         4.0    // m under the head where the haze is at half
            #define CONE_TOP_SOFT   0.6    // m under the head over which it comes in
            #define CONE_GROUND_LO  0.3    // m above the ground: nothing below this
            #define CONE_GROUND_HI  3.5    // m above the ground: all of it above this
            #define CONE_EYE_LO     0.5    // m from the eye: nothing nearer
            #define CONE_EYE_HI     4.0
            #define CONE_REF_M      1.0    // m of full haze that reads as the lamp's strength
            #define CONE_MAX_GAIN   2.5    // looking up the axis: capped

            float4 _PSXConeA[CONE_MAX];    // xyz = the head, w = its height over the ground
            float4 _PSXConeB[CONE_MAX];    // rgb = the light, w = the radius at the head
            float _PSXConeK;               // tan of the half-angle
            float _PSXNight;

            struct appdata
            {
                float4 vertex : POSITION;  // unit cone: rings at y 0 (head) and -1 (foot), radius 1
                float2 uv : TEXCOORD0;     // x = the slot
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float4 a : TEXCOORD1;      // the slot's A (the same on every vertex of a cone)
                float4 b : TEXCOORD2;      // and B
            };

            v2f vert (appdata v)
            {
                v2f o;
                int i = (int)(v.uv.x + 0.5);
                float4 A = _PSXConeA[i];
                float4 B = _PSXConeB[i];
                float t = saturate(-v.vertex.y);
                float r = B.w + _PSXConeK * A.w * t;
                float3 w = A.xyz + float3(v.vertex.x * r, -t * A.w, v.vertex.z * r);
                o.pos = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, float4(w, 1.0)));
                o.wpos = w;
                o.a = A;
                o.b = B;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float gate = saturate((_PSXNight - 0.75) * 4.0);
                float3 C = _WorldSpaceCameraPos;
                float3 v = normalize(i.wpos - C);
                float H = max(i.a.w, 0.5);
                float r0 = i.b.w;
                float K = max(_PSXConeK, 0.05);
                float K2 = K * K;
                // The virtual apex, r0 / K over the head; hv = depth under it.
                float hTop = r0 / K;
                float3 o = C - (i.a.xyz + float3(0.0, hTop, 0.0));

                // The slab: hv = -(o.y + t v.y) between hTop (the head) and
                // hTop + H (the ground), in front of the eye.
                float t0 = 0.0, t1 = 1e4;
                if (abs(v.y) > 1e-5)
                {
                    float ta = (-hTop - o.y) / v.y;
                    float tb = (-(hTop + H) - o.y) / v.y;
                    t0 = max(t0, min(ta, tb));
                    t1 = min(t1, max(ta, tb));
                }
                else if (-o.y < hTop || -o.y > hTop + H) return fixed4(0, 0, 0, 0);

                // The cone: f(t) = |xz|^2 - K^2 y^2 <= 0 (both nappes; the slab
                // above already keeps only the lower one).
                float qa = dot(v.xz, v.xz) - K2 * v.y * v.y;
                float qb = dot(o.xz, v.xz) - K2 * o.y * v.y;
                float qc = dot(o.xz, o.xz) - K2 * o.y * o.y;
                float disc = qb * qb - qa * qc;
                if (qa > 1e-6)
                {
                    // Shallower than the cone's side: inside between the roots.
                    if (disc <= 0.0) return fixed4(0, 0, 0, 0);
                    float sq = sqrt(disc);
                    t0 = max(t0, (-qb - sq) / qa);
                    t1 = min(t1, (-qb + sq) / qa);
                }
                else if (qa < -1e-6 && disc > 0.0)
                {
                    // Steeper: inside OUTSIDE the roots, and the lower nappe is
                    // the half-line on the downhill side.
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
                    float h = i.a.y - p.y;                       // under the head
                    float R = r0 + K * max(h, 0.0);
                    float2 dxz = p.xz - i.a.xz;
                    float x2 = saturate(dot(dxz, dxz) / max(R * R, 1e-4));
                    float radial = 1.0 - x2;
                    float hh = h / CONE_H0;
                    float spread = 1.0 / (1.0 + hh * hh);
                    float top = smoothstep(0.0, CONE_TOP_SOFT, h);
                    float ground = smoothstep(CONE_GROUND_LO, CONE_GROUND_HI, H - h);
                    float near = smoothstep(CONE_EYE_LO, CONE_EYE_HI, ts);
                    sum += radial * spread * top * ground * near;
                }
                float k = min(sum * len / (CONE_SAMPLES * CONE_REF_M), CONE_MAX_GAIN);
                return fixed4(i.b.rgb * (k * gate * PSXAdaptGain()), 0.0);
            }
            ENDCG
        }
    }
}
