// PSX/Lit for a car's bodywork: the same per-vertex lambert, snap, affine
// premultiply and manual fog as PSX/Lit — copied, which is the house style in
// this folder — plus the two things the reference games put on their cars
// and this one did not: a SUN HIGHLIGHT and the SKY IN THE PAINT.
//
// "The current paint and windows are very flat." They were: PSX/Lit is
// ambient plus one lambert term, and a car under that is a matte shape with
// no idea where the light is. Gran Turismo's cars on the same hardware were
// the shiniest thing on screen, and it did it with an environment map — a
// small sky texture looked up by the reflected view vector, added over the
// paint, strongest at grazing angles and on the glass. That is what this
// shader does, with two differences that fall out of this game already
// having the right ingredients:
//
//   * THE ENVIRONMENT IS THE ACTUAL SKY. TimeOfDay hangs a photographic
//     panorama for the hour and turns it to face the sun; the same texture,
//     the same rotation and the same hour tint reach this shader as globals
//     (_PSXSky*), so a sunset reflects orange along the flank and a noon sky
//     reflects blue across the roof. The reflection and the sky behind the
//     car can never disagree, because they are one texture.
//   * THE GLASS IS FOUND, NOT DECLARED. The pack paints a whole car from one
//     128 px sheet with the windows as near-black pixels, and there is no
//     glass submesh to give a second material to. Dark paint is mirror-like
//     and so is glass, so darkness IS the mask: the reflection is scaled up
//     where the sheet is dark (window glass, black trim, a black car), and
//     sampled from a sharper mip there so glass reads as glass and paint
//     reads as paint.
//
// Both terms are computed per PIXEL against the interpolated normal. The
// PS1 did its env-maps per vertex on hundreds of triangles; on three hundred
// the per-vertex version is a blotch that jumps between polygons as the car
// turns, and the whole picture is dithered down to 240 lines afterwards
// anyway. The DIFFUSE stays per vertex, exactly as PSX/Lit does it, so the
// flat-shaded body the game has always had is still underneath.
//
// Below the horizon the reflection is the fog colour — the ground is fog
// colour at any distance in this renderer, so that is what a bonnet
// reflects. With no panorama set (_PSXSkyAmount = 0: a scene that never
// applied an hour) the sky is a two-tone hemisphere built from the ambient
// and the sun, which is a dull sheen rather than nothing.
Shader "PSX/CarPaint"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
        _Emission ("Emission", Range(0,1)) = 0
        _Affine ("Affine Warping", Range(0,1)) = 0
        // How much sky the paint takes at a grazing angle. The face-on amount
        // is a quarter of this (see the fresnel below).
        _Reflect ("Reflection", Range(0,1)) = 0.34
        // Sheet luminance below which a pixel counts as glass or black trim.
        _GlassLum ("Glass Luminance", Range(0,1)) = 0.22
        // Extra reflection on those pixels, as a multiple of _Reflect.
        _GlassBoost ("Glass Boost", Range(0,4)) = 1.8
        // Blinn-Phong exponent and strength of the sun's highlight.
        _Gloss ("Gloss", Range(2,128)) = 28
        _SpecStrength ("Specular", Range(0,2)) = 0.55
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            float _Emission;
            float _Affine;
            float _Reflect;
            float _GlassLum;
            float _GlassBoost;
            float _Gloss;
            float _SpecStrength;

            float4 _PSXLightDir;    // xyz = direction TO light (world)
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            float _PSXSnap;         // 1 = vertex snapping on

            // The hour's sky, as TimeOfDay.ApplySky hands it to the sky
            // material — see PSXSky.shader for what each one means there.
            sampler2D _PSXSkyTex;
            float _PSXSkyAmount;    // 0 = no panorama this scene
            float _PSXSkyRotation;  // degrees
            float _PSXSkyTint;      // 0..1, how hard the photo wears the hour
            float _PSXSkyExposure;
            fixed4 _PSXSkyTop;
            fixed4 _PSXSkyHorizon;
            float _PSXSkySharpness;

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
                fixed4 light : COLOR0;
                fixed fog : TEXCOORD1;
                float3 wnrm : TEXCOORD2;
                float3 wpos : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 clipPos = UnityObjectToClipPos(v.vertex);

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
                float3 n = nl2 > 1e-8 ? rawN * rsqrt(nl2) : float3(0, 1, 0);
                float ndl = saturate(dot(n, normalize(_PSXLightDir.xyz)));
                fixed3 amb = lerp(_PSXAmbient.rgb, _PSXSkyAmbient.rgb, saturate(n.y));
                fixed3 lighting = amb + _PSXLightColor.rgb * ndl;
                o.light = fixed4(saturate(lighting), 1);
                o.wnrm = n;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;

                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fog = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            /// What the sky looks like in direction R — the same lookup and
            /// the same hour tint PSXSky.shader applies, at a blur picked by
            /// the caller (paint is a soft reflection, glass a sharper one).
            fixed3 SkyIn(float3 R, float lod)
            {
                float y = R.y;
                float above = pow(saturate(y), 1.0 / max(_PSXSkySharpness, 0.5) * 4.0);
                fixed3 grad = lerp(_PSXSkyHorizon.rgb, _PSXSkyTop.rgb, above);
                fixed3 col;
                if (_PSXSkyAmount > 0.001)
                {
                    float u = atan2(R.z, R.x) * (0.5 / UNITY_PI) + 0.5 + _PSXSkyRotation / 360.0;
                    float v = 0.5 + asin(clamp(y, -1.0, 1.0)) / UNITY_PI;
                    // Explicit LOD: no derivatives, so the equirect seam that
                    // PSXSky has to fight with tex2Dgrad cannot happen here.
                    fixed3 pano = tex2Dlod(_PSXSkyTex, float4(u, v, 0, lod)).rgb * _PSXSkyExposure;
                    fixed3 tinted = pano * grad * 2.0;
                    col = lerp(pano, tinted, _PSXSkyTint);
                }
                else
                {
                    // No panorama: a hemisphere from the lighting that IS set.
                    col = lerp(_PSXAmbient.rgb * 1.2, saturate(_PSXAmbient.rgb + _PSXLightColor.rgb * 0.5),
                               saturate(y * 1.5 + 0.3));
                }
                // The ground is fog colour in this renderer, so the ground a
                // bonnet reflects is too — and the horizon band meets it the
                // way the sky's own horizon does.
                float hz = saturate(1.0 - abs(y) * 8.0);
                col = lerp(col, _PSXFogColor.rgb, hz * hz);
                col = lerp(col, _PSXFogColor.rgb, saturate(-y * 4.0));
                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uvw.xy / i.uvw.z) * _Color;
                clip(tex.a - _Cutoff);
                fixed3 lit = tex.rgb * lerp(i.light.rgb, fixed3(1,1,1), _Emission);

                float3 N = normalize(i.wnrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 L = normalize(_PSXLightDir.xyz);

                // Glass and black trim: the dark pixels of the sheet.
                float lum = dot(tex.rgb, float3(0.30, 0.59, 0.11));
                float glass = saturate((_GlassLum - lum) / max(_GlassLum, 1e-3));

                // The sky, fresnel-weighted: a quarter face-on, all of it at
                // the silhouette, and more of it on the glass.
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, 3.0);
                float k = _Reflect * (0.25 + 0.75 * fres) * (1.0 + glass * _GlassBoost);
                float3 R = reflect(-V, N);
                fixed3 sky = SkyIn(R, lerp(2.5, 0.8, glass));
                fixed3 col = lerp(lit, sky, saturate(k));

                // The sun's highlight, per pixel, in the sun's colour. Tighter
                // and brighter on the glass than on the paint.
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _Gloss * (1.0 + glass * 1.5));
                col += _PSXLightColor.rgb * spec * _SpecStrength * (0.6 + 0.4 * fres) * (1.0 + glass * 0.8);

                col = lerp(col, _PSXFogColor.rgb, i.fog);
                return fixed4(col, tex.a);
            }
            ENDCG
        }
    }
}
