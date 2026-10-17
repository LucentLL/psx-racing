// THE GLOW OF A LAMP HEAD, SEEN FROM ANYWHERE.
//
// PSXLamps.cginc lights the road UNDER a street lamp. This is the lamp itself
// as the eye sees it at night: a hot core where the bulb is and a soft halo
// of lit mist around it - the string of orange beads running up a boulevard
// that half of any Need for Speed night frame is made of, and the thing the
// owner meant by "light" in "the particle effects on screen for rain and
// light". It replaced a 2.2 m additive quad laid FLAT under each head: seen
// from the driver's seat, a flat quad eight metres up is a sliver edge-on,
// and a lamp fifty metres away was not there at all.
//
// HOW IT IS DRAWN. NightGlow merges every lamp head of a scene (a circuit's
// street lamps, a city tile's) into ONE mesh: four vertices per halo, all
// four AT the head's centre, told apart by uv0 (the corner) - so a hundred
// lamps are one draw call, and the quad is built here, in view space, where
// it can face the camera exactly:
//   * the centre is pulled 0.6 m toward the eye, so the lamp's own head box
//     (drawn with depth) does not eat the half of the halo behind its face;
//   * the corner goes out by max(_Size x uv1.x, the size that keeps the
//     quad's half-width _MinPixels framebuffer pixels at that depth) - a lamp
//     four hundred metres off is still a spark, never a sub-pixel that
//     twinkles in and out as the camera moves;
//   * the size grows with the global wetness (x 1.5 at 1): rain and night
//     damp put mist round every lamp, and a lamp in the rain is bigger.
//
// It FADES SLOWER THAN THE FOG. Glow/Beam fade out across the fog band; a
// halo stays whole to the fog's far end and is gone by 2.2 times it (never
// cut by the far plane: 92% of it at most). A lamp punches through night
// haze - that is the whole reason a distant street reads as a street at
// night - and a halo that faded with the fog would put the horizon out.
//
// Additive (One One) with the fragment already multiplied by its own
// profile, like PSX/Glow: SrcAlpha One would apply the profile twice and
// square the falloff into a hard dot. The halo is WINDOWED to exactly zero
// at the quad's edge: the framebuffer is 8-bit and the colour space linear,
// so a stray 0.004 of linear light at a corner is 13/255 once encoded, and
// every lamp would wear a faint square.
//
// ZWrite Off with the depth TEST on: a halo behind a building is hidden by
// it, and one in front of a car never punches a hole in the car.
//
// The globals (_PSXWetness, _PSXFogFar) are uniforms ONLY, never Properties:
// a property of the same name would shadow the global and read 0.
//
// THE SKYLINE'S LIGHTS (the uptown night pass, 2026-10-16; CityBeacons):
// uv1.y < 0 marks a light ON A TOWER - a red aviation beacon or a bead of a
// crown's LED strip - that must be seen from kilometres out, where the tower
// itself is drawn by CitySkyline beyond the far plane. Such a halo is moved
// in along its line of sight exactly as PSX/Lit's skyline switch moves the
// tower under it (the same squeeze, so it sits on the squeezed crown), hazed
// by its real distance instead of faded with the fog, and - for uv1.y <= -1 -
// pulses: a slow beacon flash, BEACON_PERIOD seconds, phase frac(-uv1.y), so
// the towers do not flash in step. uv1.y = -0.5 is a steady strip bead.
//
// PSX/Halo is created at runtime only (NightGlow: Shader.Find), never by a
// saved .mat, so it is in ProjectSettings' Always Included Shaders or a
// WebGL build would not have it.
Shader "PSX/Halo"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 0.72, 0.40, 1)
        _Strength ("Strength", Range(0, 4)) = 1
        _Size ("Halo half-size (m)", Float) = 2.6
        _HaloAmt ("Soft halo amount", Range(0, 1)) = 0.35
        _MinPixels ("Minimum size (framebuffer px)", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // THE COLOUR PASS (PSXTone.cginc): a lamp head is a light source -
            // the eye's adaptation and no exposure, no curve - and the alpha
            // it adds is the EMITTER MASK the halation and lens dirt glow by.
            #include "PSXTone.cginc"

            // THE SHAPE. A tight core (the bulb and its refractor) and a wide
            // soft halo (the lit mist), both gaussians in the quad's own
            // radius r (0 at the centre, 1 at the edge midpoints).
            #define HALO_CORE_K   40.0   // core: exp(-r^2 * 40), e^-1 at r = 0.16
            #define HALO_SOFT_K   4.5    // halo: exp(-r^2 * 4.5), e^-1 at r = 0.47
            // How far the centre comes toward the eye, metres.
            #define HALO_PULL     0.6
            // Mist: size multiplier per unit of global wetness.
            #define HALO_WET_GROW 0.5
            // Distance fade: whole to min(fogFar, HALO_FULL x end), gone by
            // end = min(fogFar x HALO_FAR, far plane x HALO_CLIP).
            #define HALO_FAR      2.2
            #define HALO_FULL     0.6
            #define HALO_CLIP     0.92

            float4 _Color;
            float _Strength;
            float _Size;
            float _HaloAmt;
            float _MinPixels;

            float _PSXWetness;
            float _PSXFogFar;
            float _PSXFogNear;

            // THE SKYLINE'S LIGHTS (the header): PSXLit.shader's SKYLINE_FAR /
            // SKYLINE_BAND, the same numbers (CitySkyline.FarShare / BandShare)
            #define SKY_FAR        0.997
            #define SKY_BAND       0.70
            #define SKY_HAZE_M     24000.0  // CitySkyline.HazeClearM
            #define BEACON_PERIOD  2.0      // seconds: 30 flashes a minute (an L-864's slow end)
            #define BEACON_ON      0.50     // share of the period the lamp is up
            #define SKY_BEACON_PX  1.8      // a beacon's minimum size, x _MinPixels
            #define SKY_CORE_K     8.0      // the soft core of a tower light at its minimum size

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;     // the corner: (0,0) (1,0) (1,1) (0,1)
                float2 uv1 : TEXCOORD1;    // x = this halo's size multiplier; y = 0, or 10 + the yaw a
                                           // one-way lens faces (a signal head: CitySignals)
                float4 color : COLOR;      // white; a per-halo tint if ever wanted
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 q : TEXCOORD0;      // -1..1 across the quad
                float4 col : TEXCOORD1;    // rgb = tint x strength x fade
                float core : TEXCOORD2;    // the core's sharpness (HALO_CORE_K; softer for a far tower light)
            };

            v2f vert (appdata v)
            {
                v2f o;
                // All four corners share the centre; the quad is made here.
                float4 wc = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                bool skyLight = v.uv1.y < -0.25;
                float realDist = length(wc.xyz - _WorldSpaceCameraPos);
                if (skyLight)
                {
                    float sF = _ProjectionParams.z * SKY_FAR;
                    float sA = min(_PSXFogNear, sF - 30.0);
                    sA = sA < 50.0 ? min(50.0, sF * 0.5) : sA;
                    float span = max((sF - sA) * SKY_BAND, 1.0);
                    float sdc = realDist <= sA ? realDist : sA + span * (1.0 - exp(-(realDist - sA) / span));
                    wc.xyz = _WorldSpaceCameraPos + (wc.xyz - _WorldSpaceCameraPos) * (sdc / max(realDist, 1e-3));
                }
                float3 vc = mul(UNITY_MATRIX_V, wc).xyz;
                float dist = length(vc);

                // Toward the eye, but never through it.
                float pull = min(HALO_PULL, dist * 0.5);
                vc -= vc * (pull / max(dist, 1e-3));

                // The smallest half-size that still reaches _MinPixels of the
                // framebuffer out from the centre at this depth (a quad twice
                // that across): a half-size h at depth z spans
                // h * |P11| * H / (2z) pixels. |P11| because it is negative
                // when rendering into a texture on D3D. An orthographic camera
                // has no depth in that sum.
                float depth = max(-vc.z, 1e-3);
                float zScale = lerp(depth, 1.0, unity_OrthoParams.w);
                float minM = _MinPixels * 2.0 * zScale
                           / max(_ScreenParams.y * abs(UNITY_MATRIX_P[1][1]), 1e-3);
                // a far beacon is a dot of a few pixels, never a sub-pixel spark
                if (v.uv1.y <= -1.0) minM *= SKY_BEACON_PX;
                float natural = _Size * v.uv1.x * (1.0 + HALO_WET_GROW * saturate(_PSXWetness));
                float size = max(natural, minM);
                // a tower light held at its minimum size gets a soft core, so
                // the dot carries colour rather than one hot pixel
                o.core = skyLight ? lerp(HALO_CORE_K, SKY_CORE_K, saturate(minM / max(natural, 1e-3) - 0.5)) : HALO_CORE_K;

                float2 q = v.uv * 2.0 - 1.0;
                vc.xy += q * size;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1.0));
                o.q = q;

                // Slower than the fog, never clipped by the far plane.
                float end = min(_PSXFogFar * HALO_FAR, _ProjectionParams.z * HALO_CLIP);
                float start = min(_PSXFogFar, HALO_FULL * end);
                float fade = 1.0 - saturate((dist - start) / max(end - start, 1.0));
                if (skyLight)
                {
                    // hazed by the real distance, never cut by the fog band
                    fade = exp(-realDist / SKY_HAZE_M);
                    if (v.uv1.y <= -1.0)
                    {
                        // the flash: up fast, held, down slower (an LED beacon's soft edges)
                        float t = frac(_Time.y / BEACON_PERIOD + frac(-v.uv1.y));
                        fade *= smoothstep(0.0, 0.06, t) * (1.0 - smoothstep(BEACON_ON - 0.10, BEACON_ON, t));
                    }
                }

                // A LENS THAT FACES ONE WAY (a traffic signal's, uv1.y > 0): lit
                // from in front, nothing from behind or edge-on - the head and
                // its backplate are in the way. A street lamp (uv1.y = 0) glows
                // all round, exactly as before (x 1).
                float facing = 1.0;
                if (v.uv1.y > 0.5)
                {
                    float yaw = v.uv1.y - 10.0;
                    float2 toEye = _WorldSpaceCameraPos.xz - wc.xz;
                    facing = smoothstep(0.05, 0.6, dot(float2(sin(yaw), cos(yaw)), toEye) / max(length(toEye), 1e-3));
                }
                o.col = float4(_Color.rgb * v.color.rgb * (_Strength * fade * facing), fade * facing);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r2 = dot(i.q, i.q);
                // Windowed to exactly zero at r = 1 (see the header: an 8-bit
                // linear framebuffer shows a 0.004 corner as a square).
                float win = saturate(1.0 - r2);
                win *= win;
                float a = exp(-r2 * i.core) + exp(-r2 * HALO_SOFT_K) * _HaloAmt * win;
                return fixed4(i.col.rgb * (a * PSXAdaptGain()), saturate(a * i.col.a));
            }
            ENDCG
        }
    }
}
