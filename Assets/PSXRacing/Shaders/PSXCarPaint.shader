// PSX/Lit for a car's bodywork: the same snap, affine premultiply and manual
// fog as PSX/Lit — copied, which is the house style in this folder — with
// the lighting done PER PIXEL and built around ONE LIGHT SOURCE.
//
// THE THIRD CUT, and what the first two got wrong. The owner's verdicts were
// "matte blobs of polygons dipped in flour" and then "still covered in flour
// or glazed... this does not reflect real lighting", against a Gran Turismo 2
// frame where the sun is on the right of the sky, the right of the car is lit
// and the left is in shadow. That frame is the spec. What the second cut did
// instead, and why each of these is gone:
//
//   * IT LERPED THE SKY OVER THE PAINT. Half the sky at the silhouette,
//     replacing the diffuse — so every edge of every panel went pale sky
//     colour whichever side of the car the sun was on. That was the glaze.
//     A reflection is ADDED, the way the PS1 drew its environment maps (a
//     second additive pass), and it is small: a tenth of what it was face-on.
//   * IT HAD A BROAD SHEEN. pow(N.H, 10) at 22% is a pale wash over every
//     panel that faces halfway between the sun and the camera, which from a
//     chase camera is most of the car. That was the flour. Only the tight
//     highlight survives.
//   * ITS SHADOWS WERE NOT DARK. The ambient share was 0.7 of the hour's
//     ambient on the sides — and this project renders in LINEAR colour, where
//     0.19 of light is 0.47 on the display. A shadow side at 47% grey next to
//     a lit side at 90% is a two-to-one picture, and the reference is closer
//     to four. Vertical panels now take 0.4 of the ambient, the underside
//     0.14, and the paint takes 0.8 of all of it: a car in shadow is darker
//     than the road in shadow, as it is in every reference frame.
//   * ITS LIT SIDE CLIPPED. amb + sun at noon is 1.7 and saturate() made the
//     whole sun-facing half of the car one flat white-ish tone with no
//     gradient across it — the "polygon blob". A soft knee (1 - exp(-1.5x))
//     keeps the curve: 0.19 stays 0.25, 1.0 becomes 0.78, 1.7 becomes 0.92,
//     and a bonnet that curves away from the sun darkens as it curves.
//
// What is kept from the first cut, because it was right: the glass is FOUND
// from the sheet's dark pixels (there is no glass submesh in the pack), it
// reflects harder and sharper than paint and takes a tighter highlight; the
// environment is the actual hour's sky panorama through the _PSXSky* globals
// (TimeOfDay.ApplySky), turned to face the same sun the diffuse uses, so the
// reflection and the light can never disagree; below the horizon the
// reflection is a dark ground; metallic paint tints what it reflects.
//
// THE LOOK IS THE SHADER'S. The numbers are the #defines below, not material
// properties, so changing them never means rebaking three hundred livery
// materials; the one property a renderer can set is _Dull, which
// CarPaint.DullWheels puts on the wheels through a property block.
//
// The car also takes the headlights of the car behind it (PSXHeadlights.cginc),
// added to the light before the knee like the sun.
//
// _PSXPaintDebug (a global the screenshot tool sets) swaps the output for one
// term at a time: 1 = N.L, 2 = the normal, 3 = the light before the knee,
// 4 = the reflection alone, 5 = the sheet. When a picture disagrees with the
// arithmetic, this is how to find out which of them is lying.
Shader "PSX/CarPaint"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
        _Emission ("Emission", Range(0,1)) = 0
        _Affine ("Affine Warping", Range(0,1)) = 0
        // 1 turns the paint's shine down to a rim's dull sheen: the wheels
        // share the body's sheet and material, and a black tyre would
        // otherwise read as glass. Set per renderer by CarPaint.DullWheels.
        _Dull ("Dull (wheels)", Range(0,1)) = 0
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
            #include "PSXHeadlights.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            float _Emission;
            float _Affine;
            float _Dull;

            // THE LOOK. Linear-space numbers; see the header for what each
            // one is answering.
            #define SIDE_AMBIENT       0.32   // a vertical panel's share of the hour's ambient
            #define UNDER_AMBIENT      0.10   // the underside's share (sills, the underside of a wing)
            #define PAINT_AMBIENT      0.80   // the paint's share of all of it
            #define PAINT_SUN          1.5    // the sun's weight on the paint: the pack's sheets are mid
                                              //   greys (the "silver" RX-7 is 0.25 linear), and at 1.0 a lit
                                              //   side could never reach the reference's brightness
            #define SUN_KNEE           1.6    // soft clip with headroom: light = KNEE * (1 - exp(-raw / KNEE)),
                                              //   so 0.13 stays 0.125, 1.0 becomes 0.75, 2.2 becomes 1.2
            #define PAINT_REFLECT      0.45   // the sky in the paint at the silhouette...
            #define PAINT_REFLECT_FACE 0.12   // ...and this fraction of it face-on
            #define PAINT_METALLIC     0.70   // how much the paint tints what it reflects (a red car reflects red)
            #define GLASS_LUM          0.22   // sheet luminance under which a pixel is glass or trim
            #define GLASS_BOOST        2.2    // glass reflects (1 + this) times what paint does
            #define SUN_SPEC           0.90   // the sun's highlight...
            #define SUN_SPEC_POW       36.0   // ...and how tight it is (doubled on glass)
            #define DULL_REFLECT       0.10   // a wheel's reflection, as a fraction
            #define DULL_SPEC          0.20   // a wheel's highlight, as a fraction
            #define GROUND_REFLECT     0.25   // how bright the ground is in the paint (x fog colour)

            float4 _PSXLightDir;    // xyz = direction TO light (world)
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            float _PSXSnap;         // 1 = vertex snapping on
            float _PSXPaintDebug;   // 0 in the game

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
                o.wnrm = nl2 > 1e-8 ? rawN * rsqrt(nl2) : float3(0, 1, 0);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;

                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fog = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            /// What the sky looks like in direction R — the same lookup and
            /// the same hour tint PSXSky.shader applies, at a blur picked by
            /// the caller (paint is a soft reflection, glass a sharper one).
            float3 SkyIn(float3 R, float lod)
            {
                float y = R.y;
                float above = pow(saturate(y), 1.0 / max(_PSXSkySharpness, 0.5) * 4.0);
                float3 grad = lerp(_PSXSkyHorizon.rgb, _PSXSkyTop.rgb, above);
                float3 col;
                if (_PSXSkyAmount > 0.001)
                {
                    float u = atan2(R.z, R.x) * (0.5 / UNITY_PI) + 0.5 + _PSXSkyRotation / 360.0;
                    float v = 0.5 + asin(clamp(y, -1.0, 1.0)) / UNITY_PI;
                    // Explicit LOD: no derivatives, so the equirect seam that
                    // PSXSky has to fight with tex2Dgrad cannot happen here.
                    float3 pano = tex2Dlod(_PSXSkyTex, float4(u, v, 0, lod)).rgb * _PSXSkyExposure;
                    float3 tinted = pano * grad * 2.0;
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
                // way the sky's own horizon does...
                float hz = saturate(1.0 - abs(y) * 8.0);
                col = lerp(col, _PSXFogColor.rgb, hz * hz);
                // ...and below it the reflection goes DARK, the way the
                // reference games' environment maps did.
                col = lerp(col, _PSXFogColor.rgb * GROUND_REFLECT, saturate(-y * 4.0));
                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uvw.xy / i.uvw.z) * _Color;
                clip(tex.a - _Cutoff);

                float3 N = normalize(i.wnrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 L = normalize(_PSXLightDir.xyz);

                // Glass and black trim: the dark pixels of the sheet.
                float lum = dot(tex.rgb, float3(0.30, 0.59, 0.11));
                float glass = saturate((GLASS_LUM - lum) / GLASS_LUM);
                float dull = _Dull;

                // THE ONE LIGHT. A hemisphere ambient — the sky's colour from
                // above, less from the side, least from below — and the sun's
                // lambert with a real terminator: the side that faces the sun
                // is lit, the side that does not is not. The headlights of
                // whoever is behind add to the same light.
                float up = N.y;
                float3 sideA = _PSXAmbient.rgb * SIDE_AMBIENT;
                float3 amb = up >= 0.0 ? lerp(sideA, _PSXSkyAmbient.rgb, up)
                                       : lerp(sideA, _PSXAmbient.rgb * UNDER_AMBIENT, -up);
                amb *= PAINT_AMBIENT;
                float ndl = saturate(dot(N, L));
                float3 rawLight = amb + _PSXLightColor.rgb * (ndl * PAINT_SUN) + PSXHeadlights(i.wpos, N);
                // The knee: the lit side keeps its gradient instead of
                // clipping to one tone, and can go a little over 1 where
                // the sun is full on it.
                float3 light = SUN_KNEE * (1.0 - exp(-rawLight / SUN_KNEE));
                float3 lit = tex.rgb * lerp(light, float3(1, 1, 1), _Emission);

                // THE SKY IN THE PAINT, added: a little face-on, more at the
                // silhouette, most on the glass, a tenth of it on a wheel.
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, 4.0);
                float k = PAINT_REFLECT * (PAINT_REFLECT_FACE + (1.0 - PAINT_REFLECT_FACE) * fres);
                k *= 1.0 + glass * GLASS_BOOST;
                k *= lerp(1.0, DULL_REFLECT, dull);
                float3 R = reflect(-V, N);
                float3 sky = SkyIn(R, lerp(2.5, 0.8, glass));
                // Metallic paint tints what it reflects; glass does not.
                float3 tint = lerp(float3(1, 1, 1), saturate(tex.rgb * 1.6), PAINT_METALLIC * (1.0 - glass));
                float3 refl = sky * tint * k;

                // THE SUN'S HIGHLIGHT: tight, in the sun's colour, tighter
                // and brighter on the glass. No broad sheen.
                float3 H = normalize(L + V);
                float ndh = saturate(dot(N, H));
                float spec = pow(ndh, SUN_SPEC_POW * (1.0 + glass)) * SUN_SPEC * (1.0 + glass * 0.8);
                spec *= lerp(1.0, DULL_SPEC, dull);
                float3 hi = _PSXLightColor.rgb * spec;

                float3 col = lit + refl + hi;

                if (_PSXPaintDebug > 0.5)
                {
                    if (_PSXPaintDebug < 1.5) col = float3(ndl, ndl, ndl);
                    else if (_PSXPaintDebug < 2.5) col = N * 0.5 + 0.5;
                    else if (_PSXPaintDebug < 3.5) col = rawLight;
                    else if (_PSXPaintDebug < 4.5) col = refl;
                    else col = tex.rgb;
                    return fixed4(col, 1);
                }

                col = lerp(col, _PSXFogColor.rgb, i.fog);
                return fixed4(col, tex.a);
            }
            ENDCG
        }
    }
}
