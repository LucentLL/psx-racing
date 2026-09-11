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
// Everything is computed per PIXEL against the interpolated normal. The
// PS1 did its env-maps per vertex on hundreds of triangles; on three hundred
// the per-vertex version is a blotch that jumps between polygons as the car
// turns, and the whole picture is dithered down to 240 lines afterwards
// anyway. The diffuse is per pixel too, with a hemisphere ambient whose
// underside is half the ambient: a car reads as a solid when its sun side is
// bright, its shadow side dark and its sills darker still, and the first cut
// of this (vertex lambert, 8% of sky face-on, a pin-point highlight) came
// out as "matte blobs of polygons dipped in flour" -- which it was.
//
// THE LOOK IS THE SHADER'S. The numbers are the #defines below, not material
// properties, so changing them never means rebaking three hundred livery
// materials; the one property a renderer can set is _Dull, which
// CarPaint.DullWheels puts on the wheels through a property block.
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            float _Emission;
            float _Affine;
            float _Dull;

            // THE LOOK, tuned against the reference shots (psx_cam_*,
            // psx_hour_*) at 240 lines.
            #define PAINT_REFLECT      0.50   // sky in the paint at a grazing angle...
            #define PAINT_REFLECT_FACE 0.22   // ...and this fraction of it face-on (a tenth of the sky:
                                              //   a third made the cars "look like they're made out of glass")
            #define PAINT_METALLIC     0.65   // how much the paint tints what it reflects (a red car reflects red)
            #define GLASS_LUM          0.22   // sheet luminance under which a pixel is glass or trim
            #define GLASS_BOOST        1.6    // extra reflection on glass, as a multiple
            #define SUN_SHEEN          0.22   // broad sun sheen over the lit side (exponent 10)
            #define SUN_SPEC           1.00   // tight sun highlight (exponent 48)
            #define UNDER_AMBIENT      0.45   // the underside's share of the ambient
            #define PAINT_AMBIENT      0.70   // the paint's share of the ambient: its shadow side stays a shadow
            #define DULL_REFLECT       0.10   // a wheel's reflection, as a fraction
            #define DULL_SPEC          0.20   // a wheel's highlight, as a fraction
            #define GROUND_REFLECT     0.30   // how bright the ground is in the paint (x fog colour)

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
                // ...and below it the reflection goes DARK, the way the
                // reference games' environment maps did: a bright ground in
                // the paint washes the sills and the shadow side pale, and a
                // dark one is the top-to-bottom gradient that reads as gloss.
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

                // Diffuse, per pixel: a hemisphere ambient (the sky's colour
                // from above, half the ambient from below) and the sun's
                // lambert, so the car has a lit side and a shadow side.
                float up = saturate(N.y * 0.5 + 0.5);
                fixed3 amb = lerp(_PSXAmbient.rgb * UNDER_AMBIENT, _PSXSkyAmbient.rgb, up) * PAINT_AMBIENT;
                float ndl = saturate(dot(N, L));
                fixed3 lit = tex.rgb * lerp(amb + _PSXLightColor.rgb * ndl, fixed3(1,1,1), _Emission);

                // The sky in the paint: nearly half of it face-on, all of it
                // at the silhouette, more on the glass, a tenth on a wheel.
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, 3.0);
                float k = PAINT_REFLECT * (PAINT_REFLECT_FACE + (1.0 - PAINT_REFLECT_FACE) * fres);
                k *= 1.0 + glass * GLASS_BOOST;
                k *= lerp(1.0, DULL_REFLECT, dull);
                float3 R = reflect(-V, N);
                fixed3 sky = SkyIn(R, lerp(2.0, 0.6, glass));
                // Metallic paint tints what it reflects; glass does not.
                fixed3 tint = lerp(fixed3(1,1,1), saturate(tex.rgb * 1.6), PAINT_METALLIC * (1.0 - glass));
                fixed3 col = lerp(lit, sky * tint, saturate(k));

                // The sun: a broad sheen over the lit side and a tight
                // highlight, in the sun's colour, brighter toward the edges,
                // tighter and brighter on the glass.
                float3 H = normalize(L + V);
                float ndh = saturate(dot(N, H));
                float sheen = pow(ndh, 10.0) * SUN_SHEEN;
                float spec = pow(ndh, 48.0 * (1.0 + glass)) * SUN_SPEC * (1.0 + glass * 0.6);
                col += _PSXLightColor.rgb * (sheen + spec) * (0.6 + 0.4 * fres) * lerp(1.0, DULL_SPEC, dull);

                col = lerp(col, _PSXFogColor.rgb, i.fog);
                return fixed4(col, tex.a);
            }
            ENDCG
        }
    }
}
