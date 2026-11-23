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
// uv1.y < 0 marks a light ON A TOWER - a red aviation beacon or a steady red
// obstruction light on a roof corner (no LED strips: the 1990s city) - that must be seen from kilometres out, where the tower
// itself is drawn by CitySkyline beyond the far plane. Such a halo is moved
// in along its line of sight exactly as PSX/Lit's skyline switch moves the
// tower under it (the same squeeze, so it sits on the squeezed crown), hazed
// by its real distance instead of faded with the fog, and - for uv1.y <= -1 -
// pulses: a slow beacon flash, BEACON_PERIOD seconds, phase frac(-uv1.y), so
// the towers do not flash in step. uv1.y = -0.5 is a steady corner light.
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
// EVERY LAMP A POINT TO THE VANISHING POINT (the lightlook round, 2026-11-23;
// LightLook.cs, _PSXLightLook.x). The owner's reference shows every lamp of a
// road as a bright point with a soft halo, receding to the vanishing point.
// Ours thinned out: the core is 0.16 of the halo, so at the minimum size it was
// a pixel or less and the far ones read as faint smudges; the far city's lamp
// dots (CityFar, own depth) were a third the size of the streamed lamps', in
// the bulb's sRGB numbers read as linear (paler), and the streamed halos faded
// out by 0.92 of the far plane while the far dots came up only from the
// squeeze's start (0.997 of it, less 30 m): a dim ring of lamps at the hand-over.
// With the look on, for a LAMP (a streamed head, a signal, or a far lamp dot):
//   * the core never falls under LOOK_CORE_PX real framebuffer pixels of
//     radius (e^-1), with room for the halo round it (LOOK_CORE_ROOM);
//   * the halo's size is capped softly at LOOK_MAX_SHARE of the picture's
//     height, so a lamp beside the car is a lamp with mist, not a disc;
//   * a far lamp dot is the size, colour and halo of a streamed lamp;
//   * with the far city drawn, a streamed halo is held whole to the squeeze's
//     start, kept just inside the far plane (its pixel size unchanged), and
//     fades over exactly the band the far dot comes up over.
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
        // THE TOWER LIGHTS ON THEIR FITTINGS (CityBeacons V2, 2026-11-11): the
        // blend (One One, additive, by default; CityBeacons sets One
        // OneMinusSrcAlpha so a red lamp is drawn OVER a pale dusk sky rather
        // than added to it - added, it clamped pink), a tower light's largest
        // size in framebuffer pixels (0: no limit) and the least the eye's
        // adaptation may scale it (0: none, as before)
        [HideInInspector] _SrcBlend ("Blend src", Float) = 1
        [HideInInspector] _DstBlend ("Blend dst", Float) = 1
        [HideInInspector] _MaxPixels ("Tower light: largest size (framebuffer px)", Float) = 0
        [HideInInspector] _GainFloor ("Least adaptation gain", Float) = 0
        // (round 2) a steady tower light (uv1.y -0.5, not own-depth) fades out
        // between these real distances (0: never), and every tower light's
        // least size falls to _FarMinShare of itself (a beacon's extra size to
        // none) between the other two (share 1: as before)
        [HideInInspector] _CornerFadeFrom ("Corner light fade from (m)", Float) = 0
        [HideInInspector] _CornerFadeTo ("Corner light gone by (m)", Float) = 0
        [HideInInspector] _FarShrinkFrom ("Far shrink from (m)", Float) = 0
        [HideInInspector] _FarShrinkTo ("Far shrink to (m)", Float) = 1
        [HideInInspector] _FarMinShare ("Far least size share", Float) = 1
        // (round 3) a tower's TOP light (a flasher uv1.y <= -1, a steady top -0.6)
        // never smaller than this many real framebuffer pixels each way (a
        // flasher _TopFlashMul times it), with a flat core there so the point
        // reads at any distance (0: off)
        [HideInInspector] _TopFloorPx ("Top light floor (fb px, half)", Float) = 0
        [HideInInspector] _TopFlashMul ("Flasher floor x", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            // the colour by the material's blend (One One, as before, unless a
            // material says otherwise); the alpha - the emitter mask - always added
            Blend [_SrcBlend] [_DstBlend], One One
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
            float _MaxPixels;
            float _GainFloor;
            float _CornerFadeFrom, _CornerFadeTo, _FarShrinkFrom, _FarShrinkTo, _FarMinShare;
            float _TopFloorPx, _TopFlashMul;

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
            // THE CLOCK'S WINDOWS (the 1990s city, 2026-10-29; PSX/Lit's): x the
            // offices' mean lit share, y the home factor, z the shop factor
            float4 _PSXWinOccupancy;
            #define WIN_OCC_NIGHT  0.12     // the late night's office share (PSXGlobals.WinOccNight)
            #define FAR_OFFICE_CAP 3.0      // a far office stands at most 3x its late-night dots (CityFar.FarOfficeCap)
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
            // THE LOOK (the header): LightLook.cs
            float4 _PSXLightLook;
            #define LOOK_CORE_PX    1.5     // the core's least radius (e^-1), real framebuffer pixels
            #define LOOK_CORE_ROOM  4.0     // the quad's half-size at least this many core radii
            #define LOOK_MAX_SHARE  0.12    // the halo's half-size at most ~this share of the picture's height
            #define LOOK_FAR_SIZE   2.5     // a far lamp dot's size x this: a streamed lamp's (0.35 -> ~0.9)
            #define LOOK_FAR_HALO   0.35    // ...and its soft halo a streamed lamp's (NightGlow's material)
            #define LOOK_FAR_PLANE  0.985   // a streamed halo past this share of the far plane is drawn here

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
                float halo : TEXCOORD3;    // the soft halo's amount (_HaloAmt; the look's far lamps: a streamed lamp's)
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
                // THE LOOK (the header): which halos are lamps, and a streamed
                // lamp near the far plane drawn just inside it at its own pixel size
                bool lookLamp = _PSXLightLook.x > 0.5 && (!skyLight || (own && abs(v.uv1.y + 0.5) < 0.02));
                bool lookHand = lookLamp && !skyLight && _PSXFarWorld > 0.5;
                float lookK = 1.0;
                if (lookHand)
                {
                    float lim = _ProjectionParams.z * LOOK_FAR_PLANE;
                    float vd = length(vc);
                    if (vd > lim) { lookK = lim / vd; vc *= lookK; }
                }

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
                // (round 2: a tower light's least size shrinks with distance, so
                // the far skyline's points stay apart)
                float shrink = (skyLight && !own) ? saturate((realDist - _FarShrinkFrom) / max(_FarShrinkTo - _FarShrinkFrom, 1.0)) : 0.0;
                minM *= lerp(1.0, _FarMinShare, shrink);
                if (v.uv1.y <= -1.0) minM *= lerp(SKY_BEACON_PX, 1.0, shrink);
                float natural = _Size * v.uv1.x * (1.0 + HALO_WET_GROW * saturate(_PSXWetness));
                // (round 2) a squeezed tower light keeps the size its REAL distance
                // gives it (squeezed in, a metre of glow 5 km out read as one 500 m out)
                if (skyLight && !own && _FarMinShare < 0.999) natural *= dist / max(realDist, 1e-3);
                float size = max(natural, minM);
                // a tower light near the eye is a lamp with a little mist, not
                // a disc metres across: no larger than _MaxPixels each way
                if (skyLight && _MaxPixels > 0.0)
                {
                    float pxM = 2.0 * zScale / max(min(_ScreenParams.y, HALO_REF_LINES) * abs(UNITY_MATRIX_P[1][1]), 1e-3);
                    size = min(size, max(_MaxPixels * pxM, minM));
                }
                // a tower light held at its minimum size gets a soft core, so
                // the dot carries colour rather than one hot pixel
                o.core = skyLight ? lerp(HALO_CORE_K, SKY_CORE_K, saturate(minM / max(natural, 1e-3) - 0.5)) : HALO_CORE_K;
                o.halo = _HaloAmt;
                if (lookLamp)
                {
                    // the core a real point, the halo capped (the header)
                    float fbM = 2.0 * zScale / max(_ScreenParams.y * abs(UNITY_MATRIX_P[1][1]), 1e-3);
                    float nat = natural * lookK * (own ? LOOK_FAR_SIZE : 1.0);
                    float maxM = LOOK_MAX_SHARE * _ScreenParams.y * fbM;
                    nat = nat / sqrt(1.0 + (nat / maxM) * (nat / maxM));
                    float minL = 1.5 * 2.0 * zScale / max(min(_ScreenParams.y, HALO_REF_LINES) * abs(UNITY_MATRIX_P[1][1]), 1e-3);
                    float rc = max(nat / sqrt(HALO_CORE_K), LOOK_CORE_PX * fbM);
                    size = max(max(nat, minL), rc * LOOK_CORE_ROOM);
                    o.core = (size * size) / (rc * rc);
                    if (own) o.halo = LOOK_FAR_HALO;
                }
                // (round 3) a top light keeps a floor in REAL pixels and a flat
                // core there: a clear red point from 10 km and more
                bool topLight = v.uv1.y <= -1.0 || abs(v.uv1.y + 0.6) < 0.02;
                if (skyLight && !own && topLight && _TopFloorPx > 0.0)
                {
                    float fbM = 2.0 * zScale / max(_ScreenParams.y * abs(UNITY_MATRIX_P[1][1]), 1e-3);
                    float floorM = _TopFloorPx * fbM * (v.uv1.y <= -1.0 ? _TopFlashMul : 1.0);
                    // past the shrink's start the floor IS its size (a flat-cored
                    // point, never the faint soft dot the shrunk minimum drew)
                    if (realDist > _FarShrinkFrom && floorM >= natural) { size = floorM; o.core = 1.6; }
                    else if (floorM >= size) { size = floorM; o.core = 1.6; }
                }

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
                // THE LOOK: whole to the squeeze's start, then out over the band the far dot comes up over
                if (lookHand) fade = 1.0 - saturate((dist - sA) / FAR_FADE_IN);
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
                    // (round 2) a steady corner light is gone far off: one point a tower
                    if (_CornerFadeTo > 0.0 && abs(v.uv1.y + 0.5) < 0.02)
                        fade *= 1.0 - saturate((realDist - _CornerFadeFrom) / max(_CornerFadeTo - _CornerFadeFrom, 1.0));
                    if (v.uv1.y <= -1.0)
                    {
                        // the flash: an INCANDESCENT beacon's (the 1990s city,
                        // 2026-10-29) - the filament warms up and cools down,
                        // softer both ways than the LED's snap it replaced
                        float t = frac(_Time.y / BEACON_PERIOD + frac(-v.uv1.y));
                        fade *= smoothstep(0.0, 0.12, t) * (1.0 - smoothstep(BEACON_ON - 0.18, BEACON_ON, t));
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
                // THE CLOCK'S WINDOWS (CityFar's far window dots): a dot's class
                // by its mode - a footprint's office -0.75, home -0.70, shop
                // -0.80 (all handed over above); a frontage box's home -0.45,
                // shop -0.55 - and its RANK in its colour's alpha (x 4, in its
                // class's late-night shares): lit while the hour's share is over
                // it. A lamp (-0.5) and every other halo keep alpha 1, never compared.
                if (own)
                {
                    float m = v.uv1.y;
                    float cls = (m < -0.6 && m > -0.95) ? (m > -0.725 ? 1.0 : (m < -0.775 ? 2.0 : 0.0))
                              : (abs(m + 0.45) < 0.02 ? 1.0 : (abs(m + 0.55) < 0.02 ? 2.0 : -1.0));
                    float rel = cls < 0.5 ? min(_PSXWinOccupancy.x / WIN_OCC_NIGHT, FAR_OFFICE_CAP)
                              : (cls < 1.5 ? _PSXWinOccupancy.y : _PSXWinOccupancy.z);
                    if (cls > -0.5 && v.color.a * 4.0 >= rel) o.pos = float4(0.0, 0.0, 0.0, 1.0);
                }
                float3 vcol = v.color.rgb;
                // THE LOOK: a far lamp dot's colour is the bulb's sRGB x its gain; as a
                // streamed lamp's, linear x gain (gain = its brightest channel)
                if (lookLamp && own)
                {
                    float m = max(max(vcol.r, vcol.g), max(vcol.b, 1e-3));
                    vcol = pow(max(vcol, 0.0), 2.2) / pow(m, 1.2);
                }
                o.col = float4(_Color.rgb * vcol * (_Strength * fade * facing), fade * facing);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r2 = dot(i.q, i.q);
                // Windowed to exactly zero at r = 1 (see the header: an 8-bit
                // linear framebuffer shows a 0.004 corner as a square).
                float win = saturate(1.0 - r2);
                win *= win;
                float a = exp(-r2 * i.core) + exp(-r2 * HALO_SOFT_K) * i.halo * win;
                return fixed4(i.col.rgb * (a * max(PSXAdaptGain(), _GainFloor)), saturate(a * i.col.a));
            }
            ENDCG
        }
    }
}
