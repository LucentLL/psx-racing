// PSX-era surface shader: per-vertex lighting, vertex snapping to the
// low-res grid, affine texture mapping, and manual distance fog.
// Renders via the SRPDefaultUnlit pass so it works under URP.
//
// Global uniforms driven by PSXGlobals.cs:
//   _PSXLightDir, _PSXLightColor, _PSXAmbient,
//   _PSXFogColor, _PSXFogNear, _PSXFogFar, _PSXSnap,
//   _PSXWetness, _PSXNight (both 0 in any scene that never applied an hour)
// and by their own owners: the headlight table (CarLights), the lamp table
// (StreetLights) and the hour's sky (TimeOfDay.ApplySky).
//
// THE NIGHT PASS, 2026-09-21. The owner, on Need for Speed (2015): "I like how
// dark the night is, how much the skybox effects the color and mood of the
// world and cars, how street lights bathe the road." Three things were added
// here for it, and all three are exactly ZERO on a dry day (no lamp lit, no
// wetness, no night), so every daylight picture the owner signed off before
// is the same picture. That was checked in the compiled code (fxc), not
// assumed: the dry path is today's instruction sequence plus one select that
// only ever changes a zero-length normal (which used to be a NaN).
//
//   * STREET LAMPS, PER PIXEL (PSXLamps.cginc). A street light used to be an
//     additive 16 m disc laid on the road, which never lit the road - it lay
//     on top of it, lit the kerb and the wall behind it not at all, and at a
//     grazing angle read as a sticker. Now the lamp table is light that
//     multiplies the texture, like the headlights: a pool of sodium on the
//     tarmac, the kerb, the post's own foot and the car driving through it.
//   * WET SURFACES. NFS's night is always wet, and it is the wet that makes
//     the lamps "bathe" a road: the tarmac goes darker, mirrors the sky at a
//     grazing angle (the same sky the car paint reflects, PSXSkyReflect.cginc),
//     and every lamp and beam draws a streak down it. How wet is the
//     material's _Wet (which surfaces CAN be wet: roads, pavements, kerbs -
//     the builders set it) times the hour's _PSXWetness (rain, fog, snow, and
//     the licence that clear nights are damp: TimeOfDay). Only faces that
//     look UP take it, so a wall, a deck's soffit or a kerb's face stays dry
//     under the same material. The numbers are the WET_* #defines below.
//   * LIT WINDOWS on the city facades at night (_NightMask/_NightWin), one
//     texel mask per facade texture that says where the windows are.
Shader "PSX/Lit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
        _Emission ("Emission", Range(0,1)) = 0
        // 1 = full PS1 affine texture warping, 0 = perspective correct.
        //
        // DEFAULTS TO ZERO, on the owner's instruction: "textures stretch and
        // transform depending on the camera angle. this should not happen in
        // any part of the game." The warp is only ever right on small
        // triangles, and almost nothing in this game is made of small
        // triangles — a door leaf, a wall, a road ribbon and a building facade
        // are all one or two quads each, and on those it reads as a bug rather
        // than as a PlayStation. The look survives without it: the vertex
        // snapping below and the low-resolution framebuffer are what actually
        // carry it.
        //
        // The property stays so a specific mesh can opt back IN deliberately,
        // but nothing does today and nothing should without a reason.
        _Affine ("Affine Warping", Range(0,1)) = 0
        // 2 = Back (every material that never set it), 0 = Off.
        //
        // OFF FOR TREES, on the owner's word: "All trees need the x-pattern to
        // simulate 3D. Some trees are still 2 dimensions and flat." Every
        // tree the builders plant is two crossed quads, and under Cull Back a
        // crossed pair drawn one side only is an X from a quarter of the
        // compass, a single flat card from half of it, and NOTHING from the
        // last quarter — the reported bug exactly. Drawing both faces costs no
        // geometry, where emitting a mirrored twin of every quad would double
        // sixty thousand trees' worth of forest mesh against a build that is
        // already 86 MB of GitHub's 100.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        // How wet this surface CAN get (the NIGHT PASS in the header): 1 for
        // a road, less for a pavement or a kerb, 0 for everything else, which
        // is every material that never set it. The hour's wetness is the
        // global _PSXWetness and is deliberately NOT a property here: a
        // global named in a Properties block reads the material's 0 instead.
        // MakeMat writes this on every build, so a stale value on disk is
        // reset rather than kept.
        _Wet ("Wet", Range(0,1)) = 0
        // Lit windows at night: a mask sharing the facade's UVs (R = window,
        // G = a random id per window, B = shopfront, A = window) and a switch.
        // "black" and 0 by default, so a material that never set them draws
        // no windows and never samples the mask.
        _NightMask ("Night windows", 2D) = "black" {}
        _NightWin ("Night windows on", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // The per-pixel lights: the cars' headlights...
            #include "PSXHeadlights.cginc"
            // ...and the street lamps and tail lamps (the NIGHT PASS in the
            // header). Each declares its own table; both are guarded.
            #include "PSXLamps.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            float _Emission;
            float _Affine;
            float _Cull;
            float _Wet;
            sampler2D _NightMask;
            float _NightWin;

            float4 _PSXLightDir;    // xyz = direction TO light (world)
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            // The ambient for faces that look UP: the hour's own sky colour
            // at the ambient's brightness (PSXGlobals.skyAmbient). Equal to
            // _PSXAmbient in any scene that never applied an hour.
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            // Bends the band so it closes late instead of evenly;
            // see PSXGlobals.fogCurve. Floored at 1 in the maths
            // below, so an unset global (0) is the old straight ramp.
            float _PSXFogCurve;
            float _PSXSnap;         // 1 = vertex snapping on
            // How wet the world is this hour (rain 1, fog, snow, and damp
            // clear nights) and how night-time it is, both 0..1: pushed every
            // frame by PSXGlobals from the fields TimeOfDay writes. Uniforms
            // ONLY - never properties (see _Wet) - and 0 in any scene that
            // never applied an hour, which is what keeps indoors dry and dark-
            // windowed.
            float _PSXWetness;
            float _PSXNight;

            // THE WET ROAD (the NIGHT PASS in the header). Like the car
            // paint's, the look lives in these #defines, not in materials,
            // so tuning it never means rebuilding a scene. Linear-space.
            #define WET_MIN            0.001  // below this a surface is dry: no puddles, no mirror, no spec work
            #define WET_UP_GAIN        4.0    // only faces that look UP get wet: saturate(N.y * GAIN - BIAS)
            #define WET_UP_BIAS        2.8    //   is 0 at 45 degrees of slope and full by 18, so a wall, a
                                              //   kerb's face and a deck's soffit stay dry under a wet material
            #define PUDDLE_SCALE       0.28   // puddle noise frequency on world XZ: cells about 3.6 m
            #define PUDDLE_LO          0.30   // contrast on the two-octave noise, so it reads as puddles on
            #define PUDDLE_HI          0.70   //   damp tarmac rather than as an even grey film
            #define PUDDLE_MIN         0.45   // how wet the driest patch is, as a share of the hour's wet
            #define PUDDLE_FADE_M      60.0   // beyond this the pattern fades to its mean: a 3.6 m cell is
            #define PUDDLE_FAR         0.50   //   under a pixel out there at 240 lines, and would shimmer
            #define WET_DARKEN         0.32   // wet asphalt is darker: albedo x (1 - this x wet)
            #define WET_F0             0.03   // water's reflectance face-on (Schlick): the road is a mirror
                                              //   only at the grazing angle a chase camera sees it at
            #define WET_SKY_LOD        1.5    // how soft the sky is in the water (the paint's glass value)
            #define WET_SKY            1.0    // the sky's weight in it
            #define WET_LAMP_POW       80.0   // a street lamp's streak down the road: Blinn-Phong on a flat
            #define WET_LAMP_GAIN      1.6    //   plane seen at a grazing angle stretches TOWARD the eye by
                                              //   itself, which is the streak - no anisotropy needed
            #define WET_HEAD_POW       60.0   // an oncoming low beam's streak
            #define WET_HEAD_GAIN      0.9
            #define WET_SPEC_BASE      0.25   // the streaks' floor under the fresnel: a streak is there
                                              //   face-on too, only weaker
            // THE LAND BAND, for a road. PSXSkyReflect.cginc's defaults are
            // the car paint's (land up to ten degrees), and a road seen from a
            // chase camera reflects at R.y 0.03-0.15 - every bit of it inside
            // that band, so the wet road would mirror fog x 0.4 instead of the
            // blue dusk or the sodium skyglow it is there to show. The land
            // stops at the horizon here.
            #define LAND_TOP           0.0
            #define LAND_SOFT          0.03
            #define LAND_REFLECT       0.8
            #define GROUND_REFLECT     0.25
            // The hour's sky (the _PSXSky* globals and PSXSkyIn). AFTER the
            // fog/ambient/light uniforms it reads, and after the band above.
            #include "PSXSkyReflect.cginc"

            // THE LIT WINDOWS (the NIGHT PASS in the header). The facade's
            // mask says where the windows are; which of them are lit is a
            // hash, so a street is not a lit grid. Colours are linear.
            #define WIN_CELL_M         23.0   // world cell for the per-building variety, metres
            #define WIN_LIT_FRAC       0.42   // share of office windows lit at full night (x _PSXNight)
            #define WIN_WARM_SHARE     0.62   // share of the lit ones that are warm (tungsten) not cool (tube)
            #define WIN_WARM           float3(1.00, 0.74, 0.42)
            #define WIN_COOL           float3(0.70, 0.82, 1.00)
            #define WIN_SHOP           float3(1.00, 0.86, 0.62)   // shopfront glass: always lit at night
            #define WIN_DIM            0.55   // the dimmest lit window, as a share of the brightest
            #define WIN_GAIN           1.1
            #define WIN_FOG_CUT        0.6    // a lit window is fogged only 40% as hard as the wall: it
                                              //   punches through the night haze the way a lamp does
            // THE ROOM BEHIND THE GLASS. A flat emission made every lit window
            // one cream rectangle: the blinds, the lamp on the sill and the
            // shop's racks - the whole point of photographed facades - were
            // painted out (tools/night/window_masks_sheet.png, 'night'
            // column). So the glow is scaled by the facade texel under it:
            // lerp(LO, HI, sqrt(luminance)). sqrt of the LINEAR luminance is
            // roughly the texel's lightness as painted (sRGB), which is where
            // a photo keeps its detail - in linear, the mid facade's dark
            // interiors are all crushed under 0.1. LO/HI are set so the MEAN
            // is unchanged: measured over every glass texel of the five masks
            // at import size (sqrt-luminance mean 0.53), the displayed window
            // brightness moves -0.2% overall - tower +6%, mid and glass about
            // 1%, house and shopfronts -7%, the shop atlas's two office fronts
            // -11% (a shop interior photographs darker than office glass).
            // Shopfronts take it too: a lit shop window is the goods in it.
            #define WIN_DETAIL_LO      0.50   // glow over a black texel, as a share of the flat glow
            #define WIN_DETAIL_HI      1.50   // glow over a white texel

            // A sine-free hash (Dave Hoskins' hash12): sin() of a large world
            // coordinate loses its fraction on a mobile GPU, this does not.
            float PSXLitHash(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Value noise: a hash per lattice corner, smoothly blended.
            float PSXLitNoise(float2 p)
            {
                float2 c = floor(p);
                float2 f = p - c;
                float2 s = f * f * (3.0 - 2.0 * f);
                float a = PSXLitHash(c);
                float b = PSXLitHash(c + float2(1.0, 0.0));
                float d = PSXLitHash(c + float2(0.0, 1.0));
                float e = PSXLitHash(c + float2(1.0, 1.0));
                return lerp(lerp(a, b, s.x), lerp(d, e, s.x), s.y);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                // Affine mapping is emulated rather than declared: interpolation
                // qualifiers are compile-time, so `noperspective` could not be
                // varied per material. Premultiplying by w and dividing it back
                // out in the fragment gives the same warp, continuously dialled.
                float3 uvw : TEXCOORD0;
                fixed4 light : COLOR0;
                fixed fog : TEXCOORD1;
                // For the per-pixel work only - headlights, lamps, the wet
                // road and the lit windows. The sun stays per vertex.
                float3 wpos : TEXCOORD2;
                float3 wnrm : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 clipPos = UnityObjectToClipPos(v.vertex);

                // Vertex snapping: quantize NDC xy to the render target grid.
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

                // Per-vertex diffuse (half-lambert-ish so shaded sides stay readable).
                // Guarded: a zero-length normal makes normalize() produce NaN, and
                // GLSL ES leaves clamp(NaN) undefined, so a WebGL driver that
                // propagates it renders garbage rather than merely flat shading.
                float3 rawN = UnityObjectToWorldNormal(v.normal);
                float nl2 = dot(rawN, rawN);
                float3 n = nl2 > 1e-8 ? rawN * rsqrt(nl2) : float3(0, 1, 0);
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // A face drawn from both sides is lit from the side you SEE.
                // The vertex normal points out of the front, so from behind
                // every card would take the light of the face turned away —
                // the sunlit side of a tree drawn in shadow. Per vertex is
                // exact here: the camera is on one side of a flat card, so all
                // four corners agree. Only when culling is off; a closed mesh
                // never shows its back.
                if (_Cull < 0.5 && dot(n, _WorldSpaceCameraPos - wpos) < 0.0) n = -n;
                float ndl = saturate(dot(n, normalize(_PSXLightDir.xyz)));
                // Hemispheric: the sky's colour from above, the plain ambient
                // from the side and below. A cheap per-vertex lerp, and the
                // one thing that makes a roof under a blue sky read as being
                // under a blue sky.
                fixed3 amb = lerp(_PSXAmbient.rgb, _PSXSkyAmbient.rgb, saturate(n.y));
                fixed3 lighting = amb + _PSXLightColor.rgb * ndl;
                o.light = fixed4(saturate(lighting), 1);
                o.wpos = wpos;
                o.wnrm = n;

                // Manual fog by view distance: a linear band, bent by a curve
                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                o.fog = pow(fogT, max(_PSXFogCurve, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uvw.xy / i.uvw.z;
                // Where the facade texture repeats, for the lit windows below.
                // Taken HERE, before the clip and outside every branch: a
                // derivative after a discard or inside flow control is
                // undefined on GLES/Vulkan and an error on some D3D compilers.
                float3 gx = ddx(float3(i.wpos.xz, uv.x));
                float3 gy = ddy(float3(i.wpos.xz, uv.x));

                fixed4 tex = tex2D(_MainTex, uv) * _Color;
                clip(tex.a - _Cutoff);

                // The pixel's normal, guarded like the vertex's: two
                // interpolated normals can cancel, and normalize(0) is a NaN
                // that GLSL ES leaves undefined.
                float nl2 = dot(i.wnrm, i.wnrm);
                float3 N = nl2 > 1e-8 ? i.wnrm * rsqrt(nl2) : float3(0, 1, 0);
                float3 toEye = _WorldSpaceCameraPos - i.wpos;
                float eyeDist = length(toEye);
                float3 V = toEye / max(eyeDist, 1e-4);

                // Headlights and lamps are added to the vertex light, not to
                // the result: a beam on a texture lights the texture. Each
                // table is walked ONCE for what it lights and - on a wet
                // material - what it streaks down the water. specOn is this
                // material's _Wet times the hour's wetness, the same for every
                // pixel of a draw, so the spec branch inside is uniform and a
                // dry surface (every one in daylight) never pays for it.
                float wetMat = _Wet * _PSXWetness;
                float specOn = wetMat > WET_MIN ? 1.0 : 0.0;
                float3 headD, headS, lampD, lampS;
                PSXHeadlightsBoth(i.wpos, N, V, WET_HEAD_POW, specOn, headD, headS);
                PSXLampsBoth(i.wpos, N, V, WET_LAMP_POW, specOn, lampD, lampS);
                float3 light = i.light.rgb + headD + lampD;

                // THE WET ROAD, one: how wet THIS pixel is - only if it looks
                // up, more in the puddles - and the darker albedo of wet
                // tarmac. w <= wetMat, so wherever w is set, specOn was 1.
                float w = wetMat * saturate(N.y * WET_UP_GAIN - WET_UP_BIAS);
                if (w > WET_MIN)
                {
                    float2 p = i.wpos.xz * PUDDLE_SCALE;
                    float n = PSXLitNoise(p) * 0.65 + PSXLitNoise(p * 2.03 + 17.17) * 0.35;
                    float puddle = smoothstep(PUDDLE_LO, PUDDLE_HI, n);
                    puddle = lerp(puddle, PUDDLE_FAR, saturate(eyeDist / PUDDLE_FADE_M));
                    w *= lerp(PUDDLE_MIN, 1.0, puddle);
                    tex.rgb *= 1.0 - WET_DARKEN * w;
                }
                else w = 0.0;

                fixed3 lit = tex.rgb * lerp(light, float3(1,1,1), _Emission);
                fixed3 col = lerp(lit, _PSXFogColor.rgb, i.fog);

                // THE WET ROAD, two: the mirror. Water reflects what the car
                // paint reflects - the hour's own sky, through the same lookup
                // - weighted by Schlick, so the road is dark tarmac under the
                // car and a sheet of sky toward the horizon; what it reflects
                // it does not let through. The lamps' and beams' streaks ride
                // on top of it. Then the fog, as always.
                //
                // It REPLACES col rather than editing lit on purpose: with lit
                // reassigned in a branch, the compiler can no longer fold the
                // dry path's texture x light into the fog lerp (measured with
                // fxc: a mul + add instead of today's mad, a last-bit change
                // on every dry pixel). This way the dry path is today's
                // instruction sequence exactly, and only a wet pixel pays for
                // the second fog lerp.
                if (w > 0.0)
                {
                    float3 R = reflect(-V, N);
                    float fres = WET_F0 + (1.0 - WET_F0) * pow(1.0 - saturate(dot(N, V)), 5.0);
                    float3 refl = PSXSkyIn(R, WET_SKY_LOD) * WET_SKY;
                    float3 spec = lampS * WET_LAMP_GAIN + headS * WET_HEAD_GAIN;
                    float3 wetLit = lit * (1.0 - fres * w) + (refl * fres + spec * (WET_SPEC_BASE + fres)) * w;
                    col = lerp(wetLit, _PSXFogColor.rgb, i.fog);
                }

                // THE LIT WINDOWS. Both switches are uniforms, so the mask is
                // read only by a facade material, only at night; tex2Dlod
                // because the mask has no mips (LOD 0 is what tex2D would
                // pick) and needs no derivative inside this branch.
                if (_NightWin > 0.5 && _PSXNight > 0.01)
                {
                    float4 m = tex2Dlod(_NightMask, float4(uv, 0.0, 0.0));
                    // R = A = the window. Both, multiplied: right whether or
                    // not the importer kept the alpha channel (without it A
                    // reads 1 and R alone decides).
                    float cover = m.r * m.a;
                    // WHICH windows are lit must not change inside a window.
                    // A plain world cell would cut a window in half wherever
                    // a 23 m line crosses it (half lit, half dark). So the
                    // cell is taken at the ORIGIN of this texture repeat:
                    // on a wall, world XZ moves only with u, and ddx/ddy give
                    // metres per u exactly (least squares over both screen
                    // axes), so wpos - metresPerU * frac(u) is the same point
                    // for every pixel of the repeat. The repeat index itself
                    // joins the hash, so the repeats of one wall differ.
                    float du2 = gx.z * gx.z + gy.z * gy.z;
                    float2 perU = (gx.xy * gx.z + gy.xy * gy.z) / max(du2, 1e-10);
                    float2 anchor = i.wpos.xz - perU * frac(uv.x);
                    float2 cell = floor(anchor / WIN_CELL_M);
                    float h = frac(m.g * 7.13 + PSXLitHash(cell + floor(uv) * float2(131.0, 37.0)));
                    float shop = step(0.5, m.b);
                    float on = max(shop, step(h, WIN_LIT_FRAC * _PSXNight));
                    float3 hue = frac(h * 3.7) < WIN_WARM_SHARE ? WIN_WARM : WIN_COOL;
                    hue = lerp(hue, WIN_SHOP, shop);
                    float bright = lerp(WIN_DIM, 1.0, frac(h * 11.3)) * WIN_GAIN;
                    // The room behind the glass (WIN_DETAIL_*): the facade's
                    // own texel, as sampled above. A facade never takes the
                    // wet darkening (that needs a face looking up), so this
                    // is the painted texel x _Color.
                    float lumT = dot(tex.rgb, float3(0.2126, 0.7152, 0.0722));
                    float detail = lerp(WIN_DETAIL_LO, WIN_DETAIL_HI, sqrt(max(lumT, 0.0)));
                    float3 glow = hue * (bright * on * cover * detail * _PSXNight);
                    // Added after the fog, and fogged only 40% as hard.
                    col += glow * (1.0 - WIN_FOG_CUT * i.fog);
                }
                return fixed4(col, tex.a);
            }
            ENDCG
        }
    }
}
