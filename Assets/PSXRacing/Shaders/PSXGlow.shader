// Additive glow quad: headlight lenses, the light pool they throw on the road,
// tail lights, and street lamps after dark.
//
// PSX/Lit is opaque-or-cutout by design, so there was no way to add light to
// what is already drawn — a "headlight" made from it would be a white rectangle
// stuck to the bumper. Additive blending is also what the era actually did:
// a PS1 headlight was a bright sprite and a stretched pool on the tarmac, not a
// light source, and it reads correctly precisely because it never shadows.
//
// ZWrite off with the depth TEST still on, so a glow is hidden by the wall in
// front of it but never punches a hole in the car behind it.
//
// A CAR'S LENS AS A STEADY POINT (the lightlook round, 2026-11-23; LightLook.cs,
// _PSXLightLook.y). A lens is a quad of its own size on the bodywork: past
// ~150 m it is under a pixel and the rasteriser lights it on some frames and
// not others - far traffic twinkled. A material with _LensLook (CarLights'
// lens materials only; every other PSX/Glow is a merged mesh or a decal and
// keeps 0) is drawn, with the look on, as a camera-facing point round the
// lens's centre: a core the lens's own projected size, never under
// LENS_CORE_PX real pixels of radius (its light thinned to keep the lens's
// energy, but not under LENS_KEEP of it), and a soft halo _LensHaloM metres
// across in the world, so an oncoming car's lamps grow smoothly out of a
// point as it nears. Both by how squarely the lens faces the eye: a lamp seen
// from behind its car gives nothing. _LensLook 2: a lamp-map body's lens
// (its lamps are drawn on the paint) - only the far point, gone by the time
// the painted lamp is LENS_FAR_TO_PX pixels across. With the look off, mode 1
// is the quad as before and mode 2 draws nothing. NEAR, a lens is its own
// quad as before (its shape, no halo: the halation glows it): the point takes
// over only as the lens shrinks from LENS_NEAR_TO_PX to LENS_NEAR_FROM_PX
// pixels of half-size, the quad morphing into the point.
Shader "PSX/Glow"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Strength ("Strength", Range(0,4)) = 1
        [HideInInspector] _LensLook ("A car lens (1), a lamp-map body's far point (2)", Float) = 0
        [HideInInspector] _LensHaloM ("The lens's halo half-size (m)", Float) = 0
        [HideInInspector] _LensHaloAmt ("The lens's halo amount", Float) = 0
        [HideInInspector] _LensBirth ("A spawned car's lens, coming up (0..1)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            // One One, with the colour already multiplied by the mask in the
            // fragment. SrcAlpha One would apply the mask a second time and
            // square the falloff, which turns a soft pool into a hard dot.
            Blend One One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // THE COLOUR PASS (PSXTone.cginc): a lens is a light source - the
            // eye's adaptation and no exposure, no curve - and its coverage,
            // added into the framebuffer's alpha by the One One blend, is the
            // EMITTER MASK the halation and the lens dirt glow by.
            #include "PSXTone.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Strength;

            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            float _LensLook, _LensHaloM, _LensHaloAmt, _LensBirth;
            float4 _PSXLightLook;
            #define LENS_CORE_PX   1.6     // the point's least core radius (e^-1), real framebuffer pixels
            #define LENS_CORE_R    0.45    // the core's radius as a share of the lens's own half-size
            #define LENS_KEEP      0.30    // a far point keeps at least this share of its core's light
            #define LENS_ROOM      3.0     // the quad's half-size at least this many core radii
            #define LENS_PULL      0.25    // metres toward the eye (the point stands proud of the body)
            #define LENS_FAR_FROM_PX 2.0   // a lamp-map body's far point: whole under this lens radius (px)
            #define LENS_FAR_TO_PX   5.0   // ...gone by this
            #define LENS_HALO_K    4.5
            #define LENS_NEAR_FROM_PX 3.0  // the lens's half-size (px) under which it is all point...
            #define LENS_NEAR_TO_PX   7.0  // ...and over which it is all its own quad

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed fade : TEXCOORD1;
                float4 lens : TEXCOORD2;   // the point: x core K, y halo amount, z core gain, w 1 = the look (point x (1 - t) + quad x t)
                float2 q : TEXCOORD3;      // the point's -1..1; t (the quad's share) in... see lensT
                float lensT : TEXCOORD4;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // Fade out into the fog band rather than tinting toward it:
                // adding fog colour to an additive pass would make distant
                // lamps BRIGHTER in daylight fog, which is backwards.
                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fade = 1.0 - saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                o.lens = float4(0.0, 0.0, 0.0, 0.0);
                o.q = float2(0.0, 0.0);
                o.lensT = 1.0;
                if (_LensLook > 0.5)
                {
                    if (_PSXLightLook.y > 0.5)
                    {
                        // THE LOOK (the header): a camera-facing point round the lens
                        float3 cW = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;
                        float3 axW = mul((float3x3)unity_ObjectToWorld, float3(0.5, 0.0, 0.0));
                        float3 ayW = mul((float3x3)unity_ObjectToWorld, float3(0.0, 0.5, 0.0));
                        float3 zW = mul((float3x3)unity_ObjectToWorld, float3(0.0, 0.0, 1.0));
                        float3 toEye = _WorldSpaceCameraPos - cW;
                        float d = max(length(toEye), 0.05);
                        float facing = saturate(dot(normalize(zW), toEye / d));
                        float3 cv = mul(UNITY_MATRIX_V, float4(cW, 1.0)).xyz;
                        cv -= cv * (min(LENS_PULL, d * 0.3) / d);
                        float depth = max(-cv.z, 0.05);
                        float fbM = 2.0 * depth / max(_ScreenParams.y * abs(UNITY_MATRIX_P[1][1]), 1e-3);
                        float lensR = sqrt(length(axW) * length(ayW)) * sqrt(max(facing, 0.02)) * LENS_CORE_R;
                        float rc = max(lensR, LENS_CORE_PX * fbM);
                        float H = max(rc * LENS_ROOM, _LensHaloM);
                        float2 q = v.uv * 2.0 - 1.0;
                        // near: the lens's own quad; far: the point (the header)
                        float halfPx = lensR / LENS_CORE_R / fbM;
                        float t = _LensLook > 1.5 ? 0.0 : saturate((halfPx - LENS_NEAR_FROM_PX) / (LENS_NEAR_TO_PX - LENS_NEAR_FROM_PX));
                        float3 nv = mul(UNITY_MATRIX_V, mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0))).xyz;
                        o.pos = mul(UNITY_MATRIX_P, float4(lerp(cv + float3(q * H, 0.0), nv, t), 1.0));
                        o.q = q;
                        o.lensT = t;
                        float keep = max(saturate(lensR / rc), LENS_KEEP);
                        float face = smoothstep(0.02, 0.35, facing);
                        float farOnly = _LensLook > 1.5 ? 1.0 - saturate((lensR / fbM - LENS_FAR_FROM_PX) / (LENS_FAR_TO_PX - LENS_FAR_FROM_PX)) : 1.0;
                        o.lens = float4((H * H) / (rc * rc), _LensHaloAmt * face * (_LensLook > 1.5 ? 0.0 : 1.0), keep * face * farOnly, 1.0);
                    }
                    else if (_LensLook > 1.5) o.pos = float4(0.0, 0.0, 0.0, 1.0);   // the far point only with the look
                }
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (i.lens.w > 0.5)
                {
                    float r2 = dot(i.q, i.q);
                    float win = saturate(1.0 - r2);
                    win *= win;
                    float p = (exp(-r2 * i.lens.x) * i.lens.z + exp(-r2 * LENS_HALO_K) * i.lens.y * win) * (1.0 - i.lensT)
                            + tex2D(_MainTex, i.uv).a * i.lensT;
                    p *= _Strength * i.fade * _LensBirth;
                    return fixed4(_Color.rgb * (p * PSXAdaptGain()), saturate(p));
                }
                fixed a = tex2D(_MainTex, i.uv).a * _Strength * i.fade;
                return fixed4(_Color.rgb * (a * PSXAdaptGain()), a);
            }
            ENDCG
        }
    }
}
