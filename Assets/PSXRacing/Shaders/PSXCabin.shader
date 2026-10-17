// THE OWNER'S COCKPIT v08 (2026-10-16): his cabin, lit by OUR world.
//
// Owner: "Updated cockpit is ready." The v08 cabin is real PBR trim - pebbled
// vinyl, satin ABS, a woven headliner, leather - whose relief is all in tiled
// normal maps (SurfaceUV, 0.128 m tiles). PSX/Lit lights a vertex and has no
// normal map, so this is PSX/Lit's lighting done PER PIXEL on the normal-
// mapped normal, with a roughness-driven sheen:
//   * the hour's sun (with the car's own shadow map - a cabin is in its car's
//     shade - and the sky-open map), the sky's ambient, the street lamps and
//     any car's beams: the same globals and the same functions PSX/Lit uses,
//     so the cabin is as dark as the night outside it;
//   * the exposure and THE ONE CURVE (PSXTone), no fog (it is a metre away);
//   * alpha 0: the cabin is never a light source for the halation.
//
// _Mode picks the material (CockpitModel.cs makes them, one per draw):
//   0 SURFACE  the packed 512 tile (cockpit_v08_tex.py): A,G = normal X,Y;
//              R = roughness; B = base luminance / mean (0.75..1.25). The mean
//              colour is _BaseColor (linear). A vertex whose colour alpha is
//              1 takes its rgb (sRGB) instead - the vents are a flat dark on
//              the ABS grain.
//   1 ATLAS    _AtlasTex (sRGB) on the geometric normal, roughness _Rough
//              (seats, floor, pedals, column - and the hands, on their skin).
//   2 SOLID    every flat-coloured part in one draw: vertex rgb (sRGB) is
//              the colour, vertex alpha the roughness.
// No keywords: the three paths are a uniform branch.
Shader "PSX/Cabin"
{
    Properties
    {
        _SurfTex ("Surface tile (packed)", 2D) = "bump" {}
        _AtlasTex ("Atlas (sRGB)", 2D) = "white" {}
        _BaseColor ("Mean base colour (linear)", Color) = (0.02, 0.02, 0.02, 1)
        _Mode ("Mode", Float) = 0
        _Rough ("Roughness (atlas)", Range(0, 1)) = 0.8
        _Bump ("Normal strength", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PSXCameraRel.cginc"
            #include "PSXHeadlights.cginc"
            #include "PSXLamps.cginc"
            #include "PSXTone.cginc"

            sampler2D _SurfTex;
            sampler2D _AtlasTex;
            float4 _SurfTex_ST;
            fixed4 _BaseColor;
            float _Mode;
            float _Rough;
            float _Bump;

            float4 _PSXLightDir;
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXFogColor;
            float _PSXNight;
            float _PSXSunModel;

            #define WORLD_TOE  0.80
            #define WORLD_SPAN 0.50
            #define PSX_WORLD_RECEIVER
            #include "PSXSunShadow.cginc"
            #include "PSXSkyReflect.cginc"

            #define CABIN_F0       0.04   // plastic, vinyl, leather: a dielectric's face-on reflectance
            #define CABIN_SKY_KEEP 0.35   // the sky a cabin surface can see is mostly its own roof

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wpos : TEXCOORD1;
                float3 wnrm : TEXCOORD2;
                float4 wtan : TEXCOORD3;
                fixed4 color : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = PSXObjectToClipRel(v.vertex);
                o.uv = v.uv;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                // A mirrored (RHD) cabin flips the bitangent with the scale.
                o.wtan = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w * unity_WorldTransformParams.w);
                o.color = v.color;
                return o;
            }

            float3 FromSRGB(float3 c) { return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878); }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 Ng = normalize(i.wnrm);
                float3 toEye = _WorldSpaceCameraPos - i.wpos;
                float eyeDist = length(toEye);
                float3 V = toEye / max(eyeDist, 1e-4);
                float3 N = Ng;
                float3 base;
                float rough;
                if (_Mode < 0.5)
                {
                    float4 t = tex2D(_SurfTex, i.uv);
                    float2 nxy = (float2(t.a, t.g) * 2.0 - 1.0) * _Bump;
                    float nz = sqrt(saturate(1.0 - dot(nxy, nxy)));
                    float3 T = normalize(i.wtan.xyz - Ng * dot(i.wtan.xyz, Ng));
                    float3 B = cross(Ng, T) * i.wtan.w;
                    N = normalize(T * nxy.x + B * nxy.y + Ng * nz);
                    base = _BaseColor.rgb * lerp(0.75, 1.25, t.b);
                    base = i.color.a > 0.5 ? FromSRGB(i.color.rgb) : base;
                    rough = t.r;
                }
                else if (_Mode < 1.5)
                {
                    base = tex2D(_AtlasTex, i.uv).rgb;
                    rough = _Rough;
                }
                else
                {
                    base = FromSRGB(i.color.rgb);
                    rough = i.color.a;
                }

                // ---- PSX/Lit's light, per pixel ------------------------------
                float3 L = normalize(_PSXLightDir.xyz);
                float ndl = saturate(dot(N, L));
                float3 amb = lerp(_PSXAmbient.rgb, _PSXSkyAmbient.rgb, saturate(N.y));
                float3 sunL = _PSXLightColor.rgb * ndl;
                if (_PSXSunModel < 0.5) { amb = saturate(amb + sunL); sunL = 0; }
                float carVis;
                float sunVis = PSXSunShadowCar(i.wpos, Ng, eyeDist, 0.0, carVis);
                float3 sunAmb = amb * (PSXSkyOpen(i.wpos, Ng, 0.0) * PSXCarAmbientKeep(carVis, amb, sunL, Ng)) + sunL * sunVis;
                float3 over = max(sunAmb - WORLD_TOE, 0.0) * (_PSXSunModel * (1.0 - _PSXToneOn));
                sunAmb = sunAmb - over + WORLD_SPAN * (1.0 - exp(-over / WORLD_SPAN));
                float3 headD, headS, lampD, lampS;
                PSXHeadlightsBoth(i.wpos, N, V, 32.0, 0.0, headD, headS);
                PSXLampsBoth(i.wpos, N, V, 32.0, 0.0, lampD, lampS);
                float expo = PSXExposureGain();
                float3 light = (sunAmb + headD + lampD) * expo;
                float3 lit = base * light;

                // ---- the sheen: sun highlight + the sky's ambient glancing ----
                float gloss = saturate(1.0 - rough);
                float sp = exp2(gloss * 9.0 + 1.0);                       // 2 .. 1024
                float3 H = normalize(L + V);
                float fres = CABIN_F0 + (1.0 - CABIN_F0) * pow(1.0 - saturate(dot(N, V)), 5.0);
                float3 spec = _PSXLightColor.rgb * (sunVis * ndl * pow(saturate(dot(N, H)), sp) * (sp + 8.0) / 25.0 * CABIN_F0);
                float3 sky = PSXSkyIn(reflect(-V, N), 4.0 * rough + 1.0) * PSXAdaptGain();
                float3 sheen = sky * (fres * gloss * gloss * CABIN_SKY_KEEP * carVis + fres * gloss * 0.08);
                lit += (spec * _PSXSunModel * expo + sheen * (1.0 - saturate(_PSXNight)));
                return fixed4(PSXTone(lit), 0.0);
            }
            ENDCG
        }
    }
}
