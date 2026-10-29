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
// THE FAR CITY'S LIGHTS (CityFarLights, 2026-10-24; the owner's NFS night
// frame: "Distant building lights are visible from a mile away"). With
// _OwnDepth on, a tower light (uv1.y < 0) is NOT squeezed: it is drawn in
// the far skyline's OWN depth (PSXLit.shader, THE SKYLINE'S OWN DEPTH - the
// same mapping), between the skyline's colour draw and its depth reset, so
// the far towers and the far land (CityFarLand) hide it exactly where they
// stand in front of it, kilometres out. It takes no pull toward the eye,
// none inside the squeeze's start (the streamed world's own lamps stand
// there) and dims with its real distance past _FarFall metres.
// _PSXFarWorld (a global, 1 while the far city is drawn) holds the streamed
// world's own halos whole nearly to their end, so the hand-over to the far
// lights is not a dim ring of lamps.
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
        [HideInInspector] _OwnDepth ("Far lights: the skyline's own depth", Float) = 0
        [HideInInspector] _FarFall ("Far lights: dim past (m)", Float) = 0
        [HideInInspector] _FarLead ("Far lights: depth lead (m)", Float) = 0
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
            float _OwnDepth;
            float _FarFall;
            float _FarLead;

            float _PSXWetness;
            float _PSXFogFar;
            float _PSXFogNear;
            float _PSXFarWorld;     // a global: 1 while the far city is drawn (the header)
            // a global (CityProxies): how far round the eye every far building
            // is drawn; a far WINDOW dot (uv1.y -0.75, CityFar) stands only
            // past it, so a lit window is never drawn twice. 0: no proxies.
            float _PSXProxyFarM;
            #define WIN_HANDOVER_M 250.0    // metres past that a window dot comes up over
            float4 _PSXDistHaze;    // NIGHT-LOOK PASS 5: the distant haze (PSX/Lit's; x 0 = off, the default)
            #define HALO_FULL_FAR  0.96     // ...when the streamed lamps hold whole this far into their end
            #define SKY_OWN_NEAR   5.0      // PSXLit.shader's SKYLINE_OWN_NEAR / FAR, the same numbers
            #define SKY_OWN_FAR    40000.0
            #if defined(UNITY_REVERSED_Z)
                #define SKY_FAR_CLIP   0.0
            #else
                #define SKY_FAR_CLIP   1.0
            #endif
            #define FAR_FADE_IN    40.0     // metres past the squeeze's start a far light comes up over

            // THE SKYLINE'S LIGHTS (the header): PSXLit.shader's SKYLINE_FAR /
            // SKYLINE_BAND, the same numbers (CitySkyline.FarShare / BandShare)
            #define SKY_FAR        0.997
            #define SKY_BAND       0.70
            #define SKY_TAIL       0.30     // PSXLit.shader's far tail (SKYLINE_TAIL*), the same numbers
            #define SKY_TAIL_FROM  6.0
            #define SKY_TAIL_C     400.0
            #define SKY_TAIL_TO    9000.0
            #define SKY_HAZE_M     24000.0  // CitySkyline.HazeClearM
            #define BEACON_PERIOD  2.0      // seconds: 30 flashes a minute (an L-864's slow end)
            #define BEACON_ON      0.50     // share of the period the lamp is up
            #define SKY_BEACON_PX  1.8      // a beacon's minimum size, x _MinPixels
            #define SKY_CORE_K     8.0      // the soft core of a tower light at its minimum size
            // _MinPixels is in SHARP's pixels: a taller framebuffer (PICTURE:
            // HD) keeps a far lamp the size on screen it has at 480 lines, not
            // 2.25x smaller (and five times dimmer in the halation)
            #define HALO_REF_LINES 480.0

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
                bool own = skyLight && _OwnDepth > 0.5;
                float realDist = length(wc.xyz - _WorldSpaceCameraPos);
                float sF = _ProjectionParams.z * SKY_FAR;
                float sA = min(_PSXFogNear, sF - 30.0);
                sA = sA < 50.0 ? min(50.0, sF * 0.5) : sA;
                if (skyLight && !own)
                {
                    float span = max((sF - sA) * SKY_BAND, 1.0);
                    float spanE = span * (1.0 - SKY_TAIL);
                    float sx = max(realDist - sA, 0.0);
                    float sT0 = SKY_TAIL_FROM * spanE;
                    float sTail = saturate(log(1.0 + max(sx - sT0, 0.0) / SKY_TAIL_C)
                                         / log(1.0 + max(SKY_TAIL_TO - sA - sT0, 1.0) / SKY_TAIL_C));
                    float sdc = realDist <= sA ? realDist : sA + spanE * (1.0 - exp(-sx / spanE)) + span * SKY_TAIL * sTail;
                    wc.xyz = _WorldSpaceCameraPos + (wc.xyz - _WorldSpaceCameraPos) * (sdc / max(realDist, 1e-3));
                }
                float3 vc = mul(UNITY_MATRIX_V, wc).xyz;
                float dist = length(vc);

                // Toward the eye, but never through it (a far light: not at all).
                float pull = own ? 0.0 : min(HALO_PULL, dist * 0.5);
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
                           / max(min(_ScreenParams.y, HALO_REF_LINES) * abs(UNITY_MATRIX_P[1][1]), 1e-3);
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
                if (own)
                {
                    // THE FAR CITY'S LIGHTS (the header): the skyline's own
                    // depth, and none inside the squeeze's start
                    // _FarLead metres nearer: a lamp in a wooded street shows
                    // through its own block's crowns (the far land's 128 m
                    // canopy is solid, a real one is not), never through a hill
                    float zr = clamp(depth - _FarLead, SKY_OWN_NEAR, SKY_OWN_FAR);
                    float d01 = (1.0 / SKY_OWN_NEAR - 1.0 / zr) / (1.0 / SKY_OWN_NEAR - 1.0 / SKY_OWN_FAR);
                    o.pos.z = lerp(UNITY_NEAR_CLIP_VALUE, SKY_FAR_CLIP, d01) * o.pos.w;
                    if (realDist < sA) o.pos = float4(0.0, 0.0, 0.0, 1.0);
                }

                // Slower than the fog, never clipped by the far plane.
                float end = min(_PSXFogFar * HALO_FAR, _ProjectionParams.z * HALO_CLIP);
                float start = min(_PSXFogFar, lerp(HALO_FULL, HALO_FULL_FAR, saturate(_PSXFarWorld)) * end);
                float fade = 1.0 - saturate((dist - start) / max(end - start, 1.0));
                if (own)
                {
                    // hazed by the real distance, dimmer past _FarFall (its
                    // dot keeps its pixels, so its light must thin), up over
                    // FAR_FADE_IN from the squeeze's start
                    float ff = _FarFall > 0.0 ? realDist / _FarFall : 0.0;
                    fade = exp(-realDist / SKY_HAZE_M) / (1.0 + ff * ff) * saturate((realDist - sA) / FAR_FADE_IN);
                    // a window dot (-0.75; a lamp or bead is -0.5) hands over
                    // to the proxies' own lit windows inside _PSXProxyFarM
                    if (v.uv1.y < -0.6 && v.uv1.y > -0.95)
                        fade *= saturate((realDist - _PSXProxyFarM) / WIN_HANDOVER_M);
                }
                else if (skyLight)
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
                // NIGHT-LOOK PASS 5: THE DISTANT HAZE (LookChoices.DistantHaze,
                // OFF by default) dims a light by its real distance as PSX/Lit
                // fades the world behind it - never cut, only fainter
                fade *= 1.0 - _PSXDistHaze.x * (1.0 - exp(-max(realDist - _PSXDistHaze.y, 0.0) * _PSXDistHaze.z));

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
                // A CROWN LINE'S BEAD THAT FACES ONE WAY (2026-10-29, the
                // open-frame tower; CityBeacons): a tower light whose colour
                // alpha is under 1 carries the compass yaw its member faces -
                // 0..126 a face's top edge, 127..253 a corner (seen from three
                // quarters round) - and is hidden from behind its own tower, as
                // the tower's glass hides the real one: a far tower's squeezed
                // copy cannot hide its own back by depth (its lines crossed it
                // as an X). Every other light keeps alpha 1 (x 1).
                else if (skyLight && v.color.a < 0.998)
                {
                    float code = floor(v.color.a * 255.0 + 0.5);
                    float corner = step(126.5, code);
                    float yaw = (code - 127.0 * corner) * (6.2831853 / 127.0);
                    float2 toEye = _WorldSpaceCameraPos.xz - wc.xz;
                    float lim = lerp(-0.12, -0.72, corner);
                    facing = smoothstep(lim, lim + 0.2, dot(float2(sin(yaw), cos(yaw)), toEye) / max(length(toEye), 1e-3));
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
