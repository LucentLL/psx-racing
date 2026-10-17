// PSX/CarLens - THE CLEAR OUTER LAMP COVERS of a shell's lens variant (the
// owner's R32 lens test v04, "B", 2026-10-16: recessed opaque optics behind
// separate transparent covers). Our style, his numbers:
//   * premultiplied (Blend One OneMinusSrcAlpha), depth-tested, no depth
//     write, outward faces only (Cull Back), Queue Transparent; it keeps the
//     framebuffer's ALPHA (", Zero One"): that is the emitter mask the PSX
//     camera's halation reads and the coverage a mirror's RawImage blends by,
//     and the lit lamp under the cover already wrote it;
//   * no shadow: SunShadows only ever casts PSX/Lit and PSX/CarPaint, and
//     CarBody turns the renderer's own casting off;
//   * coverage = lerp(_Opacity, 1, fresnel^5) (front 0.06, rear 0.09), tint =
//     the lens artwork's hue at _TintStrength (0.15 / 0.55), smoothness 0.90;
//   * what it reflects is what PSX/CarPaint's glass reflects: the hour's sky
//     (PSXSkyReflect.cginc), or CarReflections' world panorama when that is
//     on (_PSXEnvPano), plus the sun's glint and every lamp's;
//   * what it emits is ITS SHARE of the lamp (PSXLampMap.cginc: 25% of the
//     headlamp scatter, 20% of the rear emission; the core stays on the
//     backing, the amber reflects and does not emit), set by CarLights on
//     the same property block inputs as the body.
// The lighting is PSX/CarPaint's (vertex snap, hemisphere ambient, the sun's
// lambert through the shadow map, the beams and street lamps), cut down to
// what a thin clear shell shows.
Shader "PSX/CarLens"
{
    Properties
    {
        _MainTex ("Body atlas (the lens artwork's hue)", 2D) = "white" {}
        _Opacity ("Cover opacity face-on", Range(0,1)) = 0.06
        _TintStrength ("Lens tint from the atlas", Range(0,1)) = 0.15
        _Smoothness ("Smoothness", Range(0,1)) = 0.90
        _Affine ("Affine Warp Amount", Range(0,1)) = 0
        [NoScaleOffset] _LightMask ("Lamp control map (linear: R running, G brake, B core, A scatter)", 2D) = "black" {}
        [HideInInspector] _RunningLights ("Running lights 0/1", Float) = 0
        [HideInInspector] _Brake ("Brake 0..1", Float) = 0
        [HideInInspector] _HeadLights ("Headlamps 0/1", Float) = 0
        // 1 on the PLAYER'S renderers only (a property block, CabinClip): cut at
        // the windscreen for a camera inside the car (PSXCabinClip.cginc).
        [HideInInspector] _PSXCabinOwn ("Cut for a camera inside 0/1", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha, Zero One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PSXTexDecode.cginc"
            #include "PSXHeadlights.cginc"
            #include "PSXLamps.cginc"
            #include "PSXTone.cginc"
            // The player's own shell, cut at its windscreen for a camera inside it.
            #include "PSXCabinClip.cginc"
            #include "PSXLampMap.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Opacity;
            float _TintStrength;
            float _Smoothness;
            float _Affine;
            sampler2D _LightMask;
            float _RunningLights;
            float _Brake;
            float _HeadLights;

            // PSX/CarPaint's numbers where the cover shares them (the light it
            // receives, the glass's world), its own where it is a thinner,
            // clearer thing than a window.
            #define SIDE_AMBIENT       0.65
            #define UNDER_AMBIENT      0.25
            #define PAINT_AMBIENT      0.80
            #define PAINT_SUN          1.5
            #define SUN_KNEE           1.6
            #define LENS_F0            0.05   // polycarbonate face-on...
            #define LENS_FMAX          0.85   // ...nearly a mirror at the rim
            #define LENS_LOD_SPAN      6.0    // sky blur = (1 - smoothness) x this (0.6 mips at 0.90)
            #define LENS_HAZE          0.25   // the cover's own diffuse, of his coverage: a clear cover is
                                              //   mostly reflection. At 1 (his studio number) a noon tail lamp
                                              //   - a dark red texel in our light - went grey-brown under it
                                              //   (saturation 0.77 -> 0.37): the "smoke" the owner rejected
            #define LENS_GROUND_KEEP   0.30   // ...and the ground in it, as the paint keeps it (PAINT_LOW_KEEP):
                                              //   a lamp looks back and down at the road, and the road at full
                                              //   strength was the rest of that grey film; the sky keeps its gloss
            #define ENV_GLASS_LOD      -1.0
            #define ENV_MIX            1.0
            #define GLASS_CONTRAST     2.20
            #define GLASS_GAIN         1.20
            #define ENV_HDR            1.60
            #define ENV_HDR_LO         0.55
            #define DAY_SKY_EXPOSE     0.50
            #define SUN_GLINT          0.70
            #define SUN_GLINT_POW      320.0  // the glass's doubled glint
            #define LAMP_GLINT_POW     360.0
            #define LAMP_GLINT         0.35
            #define HEAD_GLINT_POW     120.0
            #define HEAD_GLINT         0.25
            #define PAINT_TOE          0.50
            #define PAINT_MAX          0.88
            #define LAND_TOP           0.17
            #define LAND_SOFT          0.045
            #define LAND_REFLECT       0.40
            #define GROUND_REFLECT     0.25

            float4 _PSXLightDir;
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            float _PSXFogCurve;
            float _PSXSnap;
            float4 _PSXPaintWorld;

            #include "PSXSkyReflect.cginc"
            #include "PSXSunShadow.cginc"
            #include "PSXFogRing.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uvw : TEXCOORD0;
                fixed fog : TEXCOORD1;
                float3 wnrm : TEXCOORD2;
                float3 wpos : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 clipPos = UnityObjectToClipPos(v.vertex);
                // Snapped exactly as the body under it is, or the cover would
                // swim off its own lamp whenever snapping is on.
                if (_PSXSnap > 0.5 && clipPos.w > 0.0)
                {
                    float2 grid = _ScreenParams.xy * 0.5;
                    float2 ndc = clipPos.xy / clipPos.w;
                    ndc = floor(ndc * grid + 0.5) / grid;
                    clipPos.xy = ndc * clipPos.w;
                }
                o.pos = clipPos;
                float k = lerp(1.0, max(clipPos.w, 1e-4), _Affine);
                o.uvw = float3(TRANSFORM_TEX(v.uv, _MainTex) * k, k);
                float3 rawN = UnityObjectToWorldNormal(v.normal);
                float nl2 = dot(rawN, rawN);
                o.wnrm = nl2 > 1e-8 ? rawN * rsqrt(nl2) : float3(0, 1, 0);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                o.fog = pow(fogT, max(_PSXFogCurve, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                PSXCabinClip(i.wpos);
                float2 uv = i.uvw.xy / i.uvw.z;
                float3 art = PSXMainTex(_MainTex, uv).rgb;
                float3 N = normalize(i.wnrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 L = normalize(_PSXLightDir.xyz);

                // HOW MUCH OF THE COVER THERE IS: his coverage, thin face-on,
                // solid at the rim.
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, 5.0);
                float alpha = lerp(saturate(_Opacity), 1.0, fres);

                // The tint: the lens artwork's hue (never its darkness), at
                // the cover's strength - a red cover over a red lamp.
                float top = max(art.r, max(art.g, art.b));
                float3 tint = lerp(float3(1, 1, 1), art / max(top, 0.05), saturate(_TintStrength));

                // The light it gets: PSX/CarPaint's, without the sills.
                float up = N.y;
                float3 sideA = _PSXAmbient.rgb * SIDE_AMBIENT;
                float3 amb = up >= 0.0 ? lerp(sideA, _PSXSkyAmbient.rgb, up)
                                       : lerp(sideA, _PSXAmbient.rgb * UNDER_AMBIENT, -up);
                float skyOpen = PSXSkyOpen(i.wpos, N, 0.0);
                amb *= PAINT_AMBIENT * skyOpen;
                float sunVis = PSXSunShadow(i.wpos, N, length(_WorldSpaceCameraPos - i.wpos), 0.0);
                float ndl = saturate(dot(N, L)) * sunVis;
                float3 headD, headS, lampD, lampS;
                PSXHeadlightsBoth(i.wpos, N, V, HEAD_GLINT_POW, 1.0, headD, headS);
                PSXLampsBoth(i.wpos, N, V, LAMP_GLINT_POW, 1.0, lampD, lampS);
                float expo = PSXExposureGain();
                float3 rawLight = (amb + _PSXLightColor.rgb * (ndl * PAINT_SUN) + headD + lampD) * expo;
                float3 light = SUN_KNEE * (1.0 - exp(-rawLight / SUN_KNEE));

                // WHAT IT REFLECTS: the glass's world.
                float F = LENS_F0 + (LENS_FMAX - LENS_F0) * fres;
                float3 R = reflect(-V, N);
                float lod = (1.0 - saturate(_Smoothness)) * LENS_LOD_SPAN;
                float3 sky = PSXSkyIn(R, lod) * PSXAdaptGain();
                if (_PSXEnvOn > 0.5)
                {
                    float3 env = PSXEnvIn(R, ENV_GLASS_LOD + lod);
                    float em = max(env.r, max(env.g, env.b));
                    float hdr = 1.0 + ENV_HDR * smoothstep(ENV_HDR_LO, 1.0, em);
                    float lumE = dot(env, float3(0.2126, 0.7152, 0.0722));
                    float3 envGlass = env * (pow(saturate(lumE), GLASS_CONTRAST - 1.0) * GLASS_GAIN * hdr);
                    sky = lerp(sky, envGlass, ENV_MIX);
                }
                float dayW = saturate(_PSXPaintWorld.x);
                if (dayW > 0.001 && _PSXToneOn > 0.5)
                {
                    float skyPart = smoothstep(LAND_TOP - LAND_SOFT, LAND_TOP + LAND_SOFT, R.y);
                    sky *= lerp(1.0, lerp(1.0, _PSXExposure, DAY_SKY_EXPOSE), skyPart * dayW);
                }
                float upW = smoothstep(-0.05, 0.25, R.y);
                float3 refl = sky * (F * (_PSXEnvOn > 0.5 ? 1.0 : skyOpen) * lerp(LENS_GROUND_KEEP, 1.0, upW));

                // The sun's glint and every lamp's, sharp as on glass.
                float3 H = normalize(L + V);
                float glint = pow(saturate(dot(N, H)), SUN_GLINT_POW) * SUN_GLINT * 1.8 * saturate(_Smoothness / 0.9);
                float3 hi = (_PSXLightColor.rgb * (glint * sunVis) + (lampS * LAMP_GLINT + headS * HEAD_GLINT) * 1.8) * expo;

                // Premultiplied: the cover's own colour by its coverage, what
                // it reflects over everything.
                float3 col = tint * light * (alpha * (1.0 - F) * LENS_HAZE) + refl + hi;
                float peak = max(col.r, max(col.g, col.b));
                if (peak > PAINT_TOE)
                {
                    float span = PAINT_MAX - PAINT_TOE;
                    col *= (PAINT_TOE + span * (1.0 - exp(-(peak - PAINT_TOE) / span))) / peak;
                }

                // ITS SHARE OF THE LAMP (PSXLampMap.cginc), only while
                // CarLights has lit something on this cover.
                if (_RunningLights > 0.001 || _Brake > 0.001)
                {
                    float4 m = tex2D(_LightMask, uv);
                    float3 e = PSXLampRear(m.rg, art, _RunningLights, _Brake) * LENS_REAR_SHARE;
                    if (_HeadLights > 0.001)
                        e += PSXLampScatter(m.a, art, _HeadLights) * LENS_HEAD_SHARE;
                    col += e * PSXAdaptGain();
                }

                // Into the haze as the body under it goes: the fog colour at
                // this cover's coverage, so the two together are the fogged car.
                float3 fogC = PSXFogTone(PSXFogTowardSun(_PSXFogColor.rgb * PSXFogRing(-V, _PSXSkyRotation), V));
                col = lerp(col, fogC * alpha, i.fog);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
