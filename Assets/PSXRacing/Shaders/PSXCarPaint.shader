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
// THE FOURTH CUT, 2026-09-19. The owner, over a frame of a white saloon on
// Mount Mitchell: "cars have this very unrealistic white glow. I understand
// reflections are supposed to be added, but this is not how real cars look."
// Three things were glowing, and none of them was the amount of reflection:
//
//   * THE SUN'S HIGHLIGHT WAS A FLOODLIGHT. pow(N.H, 36) at 0.9 is a lobe
//     fifteen degrees wide at mirror strength. From a chase camera with the
//     sun anywhere ahead, H is straight up - so the whole roof, boot and
//     bonnet sat inside it and took +0.4 of white. Lacquer shows the sun as
//     a GLINT a few degrees across (pow 160: a spark on a curve, nothing on a
//     flat roof); what is broad is the metallic flake under it, and that is
//     weak and the PAINT'S colour, not white.
//   * THE SILHOUETTE REFLECTED SKY WHERE A ROAD HAS LAND. The fresnel term
//     is right - lacquer does mirror at a grazing angle - but what a flank
//     mirrors at a grazing angle is the hillside, the trees and the wall
//     beside the road, which are DARK. The environment here was sky all the
//     way down to the horizon, blurred to one pale tone, so every edge that
//     turned away from the camera got a white rim: the textbook rim-light
//     glow. The reflection now has LAND in it between the horizon and ten
//     degrees up, with a crisp top, which is also what draws the bright line
//     along a real car's shoulder where the side turns up to meet the sky.
//   * NOTHING ROLLED OFF. White paint is 0.8 of 1.17 of light plus the sky
//     plus the highlight: over 1.0 across every sunlit panel, which the
//     framebuffer clips to one flat white with no shape left in it. The sum
//     now goes through a shoulder (hue kept: it scales by the brightest
//     channel) - white paint in full sun lands near 0.89 on the display, a
//     glint near 0.94, and nothing a car wears is ever 1.0.
//
// And the lacquer keeps its books: what it reflects it does not let through,
// so the reflection replaces that share of the paint instead of piling on it.
//
// THE SAME EVENING, over the glow check's own contact sheet: "Something is
// still wrong with the reflections. The taillights look like they're hidden
// under a layer of smoke" - and that the lamps and plates of the column with
// the sun BEHIND the car "look best". Measured (tools/paint/sheet_survey.py):
//
//   * GLASS WAS "ANYTHING DARK", and a tail lamp is dark. The pack's lamp
//     texels are sRGB (0.62, 0.16, 0.14) - 0.12 of linear luminance - so the
//     rule made them 30-47% GLASS and laid a window's worth of sky over
//     them: a grey film on a red lens. Glass is dark AND COLOURLESS now; a
//     saturated texel, however dark, is paint or a lens and takes lacquer's
//     four percent. (A colour-only LENS mask, to go further and light the
//     lamps, is not possible: it takes 77-83% of every red livery's sheet.)
//   * WHAT IS LEFT AS GLASS REFLECTED LIKE A MIRROR. The near-black liveries
//     ("midnight blue" is sRGB 0.10 0.13 0.13, the very colour other sheets
//     paint their windows) are glass all over by any rule a texel can
//     answer, and at 0.16 face-on with a sharp lookup the whole car wore a
//     mottled grey film of cloud. 0.10, softer.
//   * THE SHADE SIDE WAS AN EIGHTH OF THE LIT ONE. The third cut aimed for
//     the reference's four to one and then raised the sun's weight to 1.5
//     without raising the fill, so a panel out of the sun got 0.13 of light:
//     a white plate at 35% grey, a lamp near black. Vertical panels take
//     0.65 of the ambient now, which is four to one again.
//
// THE NIGHT PASS, 2026-09-21. The owner, on Need for Speed (2015): "how much
// the skybox effects the color and mood of the world and cars, how street
// lights bathe the road". Two things reach the paint, and neither is allowed
// to bring back the glow the fourth cut removed:
//
//   * THE STREET LAMPS LIGHT IT like the sun does. PSXLamps.cginc is a per-
//     pixel table of sodium street lamps (and every car's tail lamps); its
//     diffuse joins rawLight BEFORE the knee (LAMP_PAINT), so a car rolling
//     under a lamp takes an orange roof and bonnet with the same soft
//     roll-off as a sunlit one, and a car between lamps goes dark.
//   * THE LAMPS SHOW IN THE LACQUER AS GLINTS, and only as glints. The owner
//     has rejected "flour", "glazed" and "white glow" in turn, and each of
//     those was something BROAD laid over whole panels. So a lamp is a
//     spark: pow 180 (tighter than the sun's 160, doubled on glass like the
//     sun's), half the sun's gain, the same wheel/glass mask the sun glint
//     wears, and it goes through the same shoulder, so nothing it adds can
//     reach 1.0. Another
//     car's low beams glint the same way (a car behind you shows in your
//     boot lid), gated by the beam's own spread so a lamp aimed away from
//     the car shows nothing.
//
// The sky lookup moved to PSXSkyReflect.cginc the same day, verbatim, so the
// wet road in PSX/Lit reflects the very same sky; SkyIn() below is now a one-
// line wrapper and the LAND band #defines stay HERE (the self-test reads them
// from this file).
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
            // The street lamps and tail lamps, per pixel (the NIGHT PASS in
            // the header). Declares its own _PSXLamp* table; guarded.
            #include "PSXLamps.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            float _Emission;
            float _Affine;
            float _Dull;

            // THE LOOK. Linear-space numbers; see the header for what each
            // one is answering.
            #define SIDE_AMBIENT       0.65   // a vertical panel's share of the hour's ambient: with the sun
                                              //   at 1.5 this is the reference's four-to-one, lit to shade
            #define UNDER_AMBIENT      0.25   // the underside's share (sills, the underside of a wing)
            #define PAINT_AMBIENT      0.80   // the paint's share of all of it
            #define PAINT_SUN          1.5    // the sun's weight on the paint: the pack's sheets are mid
                                              //   greys (the "silver" RX-7 is 0.25 linear), and at 1.0 a lit
                                              //   side could never reach the reference's brightness
            #define SUN_KNEE           1.6    // soft clip with headroom: light = KNEE * (1 - exp(-raw / KNEE)),
                                              //   so 0.13 stays 0.125, 1.0 becomes 0.75, 2.2 becomes 1.2
            // THE LACQUER. A dielectric: four percent face-on, a mirror at
            // the silhouette - half, not all, because at 240 lines an edge
            // pixel is mostly not edge.
            #define COAT_F0            0.04
            #define COAT_FMAX          0.50
            #define PAINT_METALLIC     0.45   // how much of the reflection wears the paint's colour (the flake)
            #define GLASS_LUM          0.22   // sheet luminance under which a pixel is glass or trim...
            #define GLASS_GREY_FLOOR   0.08   // ...if it is also COLOURLESS: chroma over max(brightest channel, this),
                                              //   so the noise in a near-black texel is not mistaken for a hue
            #define GLASS_F0           0.10   // glass face-on: dark behind it, so the sky is most of what it shows
            #define GLASS_FMAX         0.70
            #define GLASS_LOD          1.5    // how soft the sky is in it (paint is 2.5)
            // THE SUN IN THE PAINT: a glint in the lacquer, a sheen in the flake.
            #define SUN_GLINT          0.70   // the sun's mirror image...
            #define SUN_GLINT_POW      160.0  // ...about six degrees across (doubled on glass)
            #define FLAKE_SHEEN        0.10   // the broad metallic lobe, in the paint's own colour
            #define FLAKE_POW          14.0
            #define DULL_REFLECT       0.10   // a wheel's reflection, as a fraction
            #define DULL_SPEC          0.20   // a wheel's highlight, as a fraction
            // THE WORLD IN THE PAINT. Sky above the land, land up to LAND_TOP
            // (the sine of ten degrees), a dark road below the horizon.
            #define LAND_TOP           0.17
            #define LAND_SOFT          0.045
            #define LAND_REFLECT       0.40   // how bright the land is in the paint (x fog colour)
            #define GROUND_REFLECT     0.25   // how bright the ground is in the paint (x fog colour)
            // THE SHOULDER. Below the toe the picture is untouched; above it
            // rolls off toward the ceiling and never arrives.
            #define PAINT_TOE          0.50
            #define PAINT_MAX          0.88
            // THE LAMPS (the NIGHT PASS in the header). Diffuse joins the
            // sun before the knee; the glints are sparks, never a sheen.
            #define LAMP_PAINT         1.0    // street/tail lamp diffuse on the paint, x the lamp table's light
            #define LAMP_GLINT_POW     180.0  // a street lamp in the lacquer: tighter than the sun (doubled on glass)
            #define LAMP_GLINT         0.35   // ...at half the sun's gain (SUN_GLINT), x the lamp's own light
            #define HEAD_GLINT_POW     120.0  // another car's low beam in the lacquer...
            #define HEAD_GLINT         0.25   // ...gated by the beam's own spread (PSXHeadlightsBoth)

            float4 _PSXLightDir;    // xyz = direction TO light (world)
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            // Bends the band so it closes late instead of evenly;
            // see PSXGlobals.fogCurve. Floored at 1 in the maths
            // below, so an unset global (0) is the old straight ramp.
            float _PSXFogCurve;
            float _PSXSnap;         // 1 = vertex snapping on
            float _PSXPaintDebug;   // 0 in the game

            // The hour's sky, as TimeOfDay.ApplySky hands it to the sky
            // material: the _PSXSky* globals and the lookup itself live in
            // PSXSkyReflect.cginc now (shared with the wet road in PSX/Lit).
            // Included HERE, after the fog/ambient/light uniforms it reads
            // and after this file's LAND_* #defines, which it keeps: its own
            // are #ifndef-guarded defaults.
            #include "PSXSkyReflect.cginc"

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
                float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                o.fog = pow(fogT, max(_PSXFogCurve, 1.0));
                return o;
            }

            /// What the sky looks like in direction R — the same lookup and
            /// the same hour tint PSXSky.shader applies, at a blur picked by
            /// the caller (paint is a soft reflection, glass a sharper one).
            /// The body moved verbatim to PSXSkyReflect.cginc (2026-09-21) so
            /// the wet road reflects the same sky; this keeps the name.
            float3 SkyIn(float3 R, float lod) { return PSXSkyIn(R, lod); }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uvw.xy / i.uvw.z) * _Color;
                clip(tex.a - _Cutoff);

                float3 N = normalize(i.wnrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 L = normalize(_PSXLightDir.xyz);

                // Glass and black trim: the dark AND COLOURLESS pixels of the
                // sheet. A tail lamp is dark too, and so is navy paint, but
                // they have a hue; a window does not.
                float lum = dot(tex.rgb, float3(0.30, 0.59, 0.11));
                float hi3 = max(tex.r, max(tex.g, tex.b)), lo3 = min(tex.r, min(tex.g, tex.b));
                float chroma = (hi3 - lo3) / max(hi3, GLASS_GREY_FLOOR);
                float glass = saturate((GLASS_LUM - lum) / GLASS_LUM) * (1.0 - saturate(chroma * 2.5 - 0.75));
                float dull = _Dull;

                // THE ONE LIGHT. A hemisphere ambient — the sky's colour from
                // above, less from the side, least from below — and the sun's
                // lambert with a real terminator: the side that faces the sun
                // is lit, the side that does not is not. The headlights of
                // whoever is behind, and the street lamps overhead, add to
                // the same light.
                float up = N.y;
                float3 sideA = _PSXAmbient.rgb * SIDE_AMBIENT;
                float3 amb = up >= 0.0 ? lerp(sideA, _PSXSkyAmbient.rgb, up)
                                       : lerp(sideA, _PSXAmbient.rgb * UNDER_AMBIENT, -up);
                amb *= PAINT_AMBIENT;
                float ndl = saturate(dot(N, L));
                // Each lamp table walked ONCE for both what it lights and
                // what it glints (the *Both functions share the distance and
                // attenuation between the two). The glint power doubles on
                // glass, exactly as the sun's does below. With no lamp lit
                // and no beam on - every daylight hour - both tables early-
                // out with exact zeros, and adding a zero changes nothing:
                // the paint is the signed-off paint.
                float3 headD, headS, lampD, lampS;
                PSXHeadlightsBoth(i.wpos, N, V, HEAD_GLINT_POW * (1.0 + glass), 1.0, headD, headS);
                PSXLampsBoth(i.wpos, N, V, LAMP_GLINT_POW * (1.0 + glass), 1.0, lampD, lampS);
                float3 rawLight = amb + _PSXLightColor.rgb * (ndl * PAINT_SUN) + headD;
                // The street lamps light the paint like the sun: before the
                // knee, so a bonnet under a sodium lamp rolls off the same way.
                rawLight += lampD * LAMP_PAINT;
                // The knee: the lit side keeps its gradient instead of
                // clipping to one tone, and can go a little over 1 where
                // the sun is full on it.
                float3 light = SUN_KNEE * (1.0 - exp(-rawLight / SUN_KNEE));
                float3 lit = tex.rgb * lerp(light, float3(1, 1, 1), _Emission);

                // THE WORLD IN THE LACQUER: four percent face-on, half at the
                // silhouette (Schlick), more on the glass, a tenth of it on a
                // wheel. What the lacquer reflects it does not let through,
                // so the paint under it gives up the same share.
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, 5.0);
                float k = lerp(COAT_F0 + (COAT_FMAX - COAT_F0) * fres,
                               GLASS_F0 + (GLASS_FMAX - GLASS_F0) * fres, glass);
                k *= lerp(1.0, DULL_REFLECT, dull);
                float3 R = reflect(-V, N);
                float3 sky = SkyIn(R, lerp(2.5, GLASS_LOD, glass));
                // The flake under the lacquer tints its share; glass does not.
                float3 body = saturate(tex.rgb * 1.6);
                float3 tint = lerp(float3(1, 1, 1), body, PAINT_METALLIC * (1.0 - glass));
                float3 refl = sky * tint * k;

                // THE SUN: a glint a few degrees across in the lacquer - a
                // spark on a curve, nothing at all across a flat roof - and
                // under it the flake's broad sheen, weak and paint-coloured.
                float3 H = normalize(L + V);
                float ndh = saturate(dot(N, H));
                float glint = pow(ndh, SUN_GLINT_POW * (1.0 + glass)) * SUN_GLINT * (1.0 + glass * 0.8);
                float sheen = pow(ndh, FLAKE_POW) * FLAKE_SHEEN * (1.0 - glass) * step(0.0001, ndl);
                float dullSpec = lerp(1.0, DULL_SPEC, dull);
                float3 hi = _PSXLightColor.rgb * (glint + sheen * body) * dullSpec;
                // THE LAMPS IN THE LACQUER: a spark per street lamp and per
                // beam aimed at the car, under the very mask the sun's glint
                // wears (glass shows it harder, a wheel hardly at all) and
                // added where the sun's is, so the shoulder below rolls it
                // off like everything else. No sheen term: a lamp is never
                // a wash over a panel (the "white glow" in the header).
                hi += (lampS * LAMP_GLINT + headS * HEAD_GLINT) * ((1.0 + glass * 0.8) * dullSpec);

                float3 col = lit * (1.0 - k) + refl + hi;

                // THE SHOULDER, on the brightest channel so the hue holds:
                // a white car in full sun keeps its shape instead of clipping.
                float top = max(col.r, max(col.g, col.b));
                if (top > PAINT_TOE)
                {
                    float span = PAINT_MAX - PAINT_TOE;
                    float rolled = PAINT_TOE + span * (1.0 - exp(-(top - PAINT_TOE) / span));
                    col *= rolled / top;
                }

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
