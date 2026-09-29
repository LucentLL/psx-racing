// THE SEA, 2026-09-26. The owner, with frames of Tidewater (dgreenheck/tidewater,
// MIT): "ideas to improve lighting, sky, night, street lights, beach (water and
// sand)". The sea was a flat plane of PSX/Lit wearing a still 64 px tile with its
// glints painted in: no reflection, no movement, no depth, and a hard line where
// it cut the beach. This is Tidewater's water rebuilt PlayStation-sized - its
// formulas, not its renderer:
//
//   * THE PACK'S WATER, MOVING. Two copies of the owner's PSX Textures water
//     sheet (water_2 teal over water_1 blue for the deep) scroll across each
//     other at incommensurate speeds and angles, so the surface never repeats a
//     frame and never stands still.
//   * THE SKY IN IT. Schlick fresnel (F0 0.02, water's), reflecting the hour's
//     own sky through PSXSkyIn - the lookup the car paint and the wet road use -
//     with Tidewater's roughness tilt: a choppy sea mirrors sky from higher up
//     (Rup = max(R.y, 0.004) + rough * 1.3 * (1 - R.y)), which is why real
//     water is darker than the sky at its horizon, not a copy of it.
//   * THE GLITTER PATH. Blinn-Phong at a high power on the scrolled normal is
//     the sun's (or at night the moon's) road across the water, and each texel
//     cell of it is kept or dropped by a hash that changes a few times a second:
//     not a smeared highlight but PS1 sparkles, dithered by the frame's own
//     quantizer.
//   * DEPTH. The builder writes each vertex's depth over the ground into its
//     colour (red: 0.5 at the waterline, +/- 12 m across the range). Shallow
//     water is the sand through a thin teal; deep is the dark blue sheet
//     (Tidewater's Beer-Lambert, one exponential per channel, retuned for the
//     green-grey water of Bogue Sound rather than a Caribbean lagoon).
//   * THE SHORE. A foam line where the depth crosses zero, breathing up and
//     down the beach on a swash period, dithered rather than blended.
//   * THE LAMPS. A street lamp on the causeway streaks down the water the way
//     it streaks down a wet road (PSXLamps' own spec term).
//
// Opaque, one pass, no extra draw call: it is the sea plane's material.
Shader "PSX/Water"
{
    Properties
    {
        _MainTex ("Water (shallow)", 2D) = "white" {}
        [HideInInspector] _MainTexRaw ("16-bit texel decode (set at runtime by PSXTexDecode.cs)", Float) = 0
        _DeepTex ("Water (deep)", 2D) = "white" {}
        [HideInInspector] _DeepTexRaw ("16-bit texel decode, deep (set at runtime by PSXTexDecode.cs)", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
        _SandColor ("Sand under the shallows", Color) = (0.62, 0.56, 0.42, 1)
        // THE SWELL (owner, 2026-09-26: "Ocean, specially the ocean, not the
        // sound under the bridges, could use proper waves. Lakes and rivers
        // with a much more subtle tide."). 1 on the sea, whose vertices say in
        // their GREEN how open to the ocean they are (0 in the sound behind
        // the island, 1 off the beach); 0 on a city's rivers and lakes, which
        // take only the ripple. _WaveDir: the way the swell travels, world xz.
        _OceanWaves ("Ocean swell", Float) = 0
        _WaveDir ("Swell direction (xz)", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // Colour texels of the 16-bit set arrive undecoded: PSXMainTex decodes them.
            #include "PSXTexDecode.cginc"
            #include "PSXHeadlights.cginc"
            #include "PSXLamps.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _DeepTex;
            float _DeepTexRaw;
            fixed4 _Color;
            fixed4 _SandColor;
            float _OceanWaves;
            float4 _WaveDir;

            // The PSX/Lit globals (PSXGlobals / TimeOfDay). See PSXLit.shader.
            float4 _PSXLightDir;
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            fixed4 _PSXSkyAmbient;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            float _PSXFogCurve;
            float _PSXSnap;
            float _PSXNight;
            float _PSXSunModel;
            #include "PSXSunShadow.cginc"
            // The sea's land band: a sea reflects the far shore's trees, but
            // at a grazing angle almost everything it mirrors is sky - the
            // band stops just over the horizon, dimmer than the road's.
            #define LAND_TOP           0.02
            #define LAND_SOFT          0.03
            #define LAND_REFLECT       0.55
            #define GROUND_REFLECT     0.35
            #include "PSXSkyReflect.cginc"
            #include "PSXFogRing.cginc"

            // ---- the look, in #defines like PSX/Lit's ----
            #define SCROLL_A      float2( 0.021,  0.013)  // tiles per second, the two sheets
            #define SCROLL_B      float2(-0.017,  0.019)
            #define SCALE_B       1.37                    // second sheet's tiling, incommensurate
            #define NORMAL_GAIN   0.55                    // how far the sheets' difference tilts the normal
            #define ROUGH         0.18                    // Tidewater's roughness tilt on the reflection
            #define F0            0.02                    // water, face-on
            #define SKY_LOD       2.0                     // a choppy sea's reflection is soft
            #define REFLECT_GAIN  0.85
            #define DEPTH_RANGE_M 12.0                    // vertex red: 0.5 +/- this
            #define SIGMA         float3(0.34, 0.16, 0.13) // per-metre extinction: red goes first, then green, blue last
            #define GLINT_POW     160.0                   // the glitter path's width
            #define GLINT_CELL    2.5                     // sparkle cells per metre
            #define GLINT_RATE    7.0                     // re-rolls per second
            #define GLINT_GAIN    2.2
            #define GLINT_NIGHT   0.6                     // the moon's path, as a share of the sun's
            #define FOAM_M        0.9                     // foam reaches this far either side of the line
            #define FOAM_SWASH_S  7.0                     // the swash period, seconds
            #define FOAM_RUN_M    0.7                     // how far up and down the beach it breathes
            #define FOAM_COLOR    float3(0.86, 0.88, 0.86)
            #define LAMP_POW      70.0
            #define LAMP_GAIN     1.4
            // THE SWELL: three Gerstner trains off the open ocean, spread +/-
            // 22 degrees about _WaveDir (amplitude m, wavelength m), shrinking
            // into the shallows so the sea meets the beach at rest and the
            // swash does the rest. Deep-water speed sqrt(g L / 2 pi).
            #define SWELL_A       float3(0.55, 0.30, 0.15)
            #define SWELL_L       float3(88.0, 47.0, 23.0)
            #define SWELL_SPREAD  float3(0.0, 0.38, -0.30)   // radians off _WaveDir
            #define SWELL_Q       0.55                       // Gerstner steepness share
            #define SWELL_SHOAL_M 3.0                        // full height past this depth
            // THE RIPPLE, everywhere (the sound, a river, a lake): a few
            // centimetres of a short train - "a much more subtle tide".
            #define RIPPLE_A      0.035
            #define RIPPLE_L      7.5
            #define WHITECAP      0.50                       // crest height share above which the swell breaks white
            // THE CHOP on the open ocean: short wind waves the mesh is too
            // coarse to move, so they live in the pixel's normal only - the
            // light and the sky break up across them, which is what makes
            // open water read as open water from a beach.
            #define CHOP_A        float2(0.16, 0.07)
            #define CHOP_L        float2(12.0, 5.5)
            #define CHOP_SPREAD   float2(0.85, -1.15)

            // One Gerstner train: adds its displacement, its normal's slopes
            // and its crest height (0..1 of its amplitude).
            void Gerstner(float2 xz, float2 dir, float A, float L, float Q, float t,
                          inout float3 disp, inout float2 slope)
            {
                float k = 6.2831853 / L;
                float w = sqrt(9.81 * k);
                float ph = k * dot(dir, xz) - w * t;
                float c = cos(ph), s = sin(ph);
                disp.xz += Q * A * dir * c;
                disp.y += A * s;
                slope += dir * (k * A * c);
            }

            float2 Rot(float2 v, float a) { float c = cos(a), s = sin(a); return float2(c * v.x - s * v.y, s * v.x + c * v.y); }

            float WaterHash(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed fog : TEXCOORD1;
                float3 wpos : TEXCOORD2;
                half3 amb : TEXCOORD3;
                half3 sun : TEXCOORD4;
                float depth : TEXCOORD5;
                // How open to the ocean (x), the swell's crest share (y), and
                // how much ripple the depth allows (z). The slopes themselves
                // are worked out per pixel.
                float3 swell : TEXCOORD6;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float depthM = (v.color.r - 0.5) * 2.0 * DEPTH_RANGE_M;
                // THE SWELL and THE RIPPLE, displacing the vertex.
                float t = _Time.y;
                float3 disp = 0.0;
                float2 slope = 0.0;
                float ocean = _OceanWaves * v.color.g * saturate(depthM / SWELL_SHOAL_M);
                float2 wd = normalize(_WaveDir.xz + float2(1e-4, 0.0));
                if (ocean > 0.001)
                {
                    float3 A = SWELL_A * ocean;
                    float Q = SWELL_Q / (dot(SWELL_A, 6.2831853 / SWELL_L) * 3.0);
                    Gerstner(wpos.xz, Rot(wd, SWELL_SPREAD.x), A.x, SWELL_L.x, Q, t, disp, slope);
                    Gerstner(wpos.xz, Rot(wd, SWELL_SPREAD.y), A.y, SWELL_L.y, Q, t * 1.03, disp, slope);
                    Gerstner(wpos.xz, Rot(wd, SWELL_SPREAD.z), A.z, SWELL_L.z, Q, t * 0.97, disp, slope);
                }
                Gerstner(wpos.xz, Rot(wd, 0.9), RIPPLE_A * saturate(depthM), RIPPLE_L, 0.3, t, disp, slope);
                float crest = ocean > 0.001 ? saturate(disp.y / max(dot(SWELL_A, float3(1, 1, 1)) * ocean, 1e-3)) : 0.0;
                o.swell = float3(ocean, crest, saturate(depthM));
                wpos += disp;
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                if (_PSXSnap > 0.5 && clipPos.w > 0.0)
                {
                    float2 grid = _ScreenParams.xy * 0.5;
                    float2 ndc = clipPos.xy / clipPos.w;
                    ndc = floor(ndc * grid + 0.5) / grid;
                    clipPos.xy = ndc * clipPos.w;
                }
                o.pos = clipPos;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wpos = wpos;
                // A sea faces up: the sky's ambient, and the sun at its
                // elevation. The same split as PSX/Lit (the shoulder is not
                // needed - a flat face under the sun never reaches the toe).
                float ndl = saturate(normalize(_PSXLightDir.xyz).y);
                half3 amb = _PSXSkyAmbient.rgb;
                half3 sunL = _PSXLightColor.rgb * ndl;
                if (_PSXSunModel < 0.5) { amb = saturate(amb + sunL); sunL = half3(0, 0, 0); }
                o.amb = amb;
                o.sun = sunL;
                o.depth = depthM;
                float dist = length(wpos - _WorldSpaceCameraPos);
                float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                o.fog = pow(fogT, max(_PSXFogCurve, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uvA = i.uv + SCROLL_A * t;
                float2 uvB = i.uv * SCALE_B + SCROLL_B * t;
                fixed3 a = PSXMainTex(_MainTex, uvA).rgb;
                fixed3 b = PSXMainTex(_MainTex, uvB).rgb;
                fixed3 deepTex = PSXTexDecode(tex2D(_DeepTex, uvA * 0.83 + uvB * 0.17).rgb, _DeepTexRaw);

                // A normal from the two sheets: where one is brighter than the
                // other the surface leans. Cheap, and it moves with them.
                float2 slope = float2(a.g - b.g, a.b - b.r) * NORMAL_GAIN;
                // Plus the swell's own slope: the faces toward the sun light,
                // the backs go dark, and the sky slides over the crests.
                {
                    float3 dummy = 0.0;
                    float2 ws = 0.0;
                    float2 wd = normalize(_WaveDir.xz + float2(1e-4, 0.0));
                    float oc = i.swell.x;
                    if (oc > 0.001)
                    {
                        float3 A = SWELL_A * oc;
                        Gerstner(i.wpos.xz, Rot(wd, SWELL_SPREAD.x), A.x, SWELL_L.x, 0.0, t, dummy, ws);
                        Gerstner(i.wpos.xz, Rot(wd, SWELL_SPREAD.y), A.y, SWELL_L.y, 0.0, t * 1.03, dummy, ws);
                        Gerstner(i.wpos.xz, Rot(wd, SWELL_SPREAD.z), A.z, SWELL_L.z, 0.0, t * 0.97, dummy, ws);
                        Gerstner(i.wpos.xz, Rot(wd, CHOP_SPREAD.x), CHOP_A.x * oc, CHOP_L.x, 0.0, t, dummy, ws);
                        Gerstner(i.wpos.xz, Rot(wd, CHOP_SPREAD.y), CHOP_A.y * oc, CHOP_L.y, 0.0, t, dummy, ws);
                    }
                    Gerstner(i.wpos.xz, Rot(wd, 0.9), RIPPLE_A * i.swell.z, RIPPLE_L, 0.0, t, dummy, ws);
                    slope += ws;
                }
                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));

                float3 toEye = _WorldSpaceCameraPos - i.wpos;
                float eyeDist = length(toEye);
                float3 V = toEye / max(eyeDist, 1e-4);
                float3 L = normalize(_PSXLightDir.xyz);

                // THE BODY OF THE WATER: sand seen through it, lost channel by
                // channel with depth (red first), toward the deep sheet.
                float d = max(i.depth, 0.0);
                float3 through = exp(-SIGMA * d);
                float3 shallow = _SandColor.rgb * (a + b);
                float3 deep = deepTex * _Color.rgb;
                float3 body = lerp(deep, shallow, through);
                float3 light = i.amb + i.sun * PSXSunShadow(i.wpos, float3(0, 1, 0), eyeDist, 0.0);
                float3 lampD, lampS, headD, headS;
                PSXLampsBoth(i.wpos, N, V, LAMP_POW, 1.0, lampD, lampS);
                PSXHeadlightsBoth(i.wpos, N, V, LAMP_POW, 1.0, headD, headS);
                float3 lit = body * (light + lampD + headD);

                // THE SKY IN IT, with the roughness tilt.
                float3 R = reflect(-V, N);
                R.y = max(R.y, 0.004) + ROUGH * 1.3 * (1.0 - R.y);
                R = normalize(R);
                float fres = F0 + (1.0 - F0) * pow(1.0 - saturate(dot(N, V)), 5.0);
                float3 sky = PSXSkyIn(R, SKY_LOD) * REFLECT_GAIN;
                float3 col = lerp(lit, sky, fres);

                // THE GLITTER PATH: the light's highlight, broken into cells
                // that each re-roll a few times a second.
                float3 H = normalize(L + V);
                float g = pow(saturate(dot(N, H)), GLINT_POW) * saturate(L.y * 4.0);
                float2 cell = floor(i.wpos.xz * GLINT_CELL);
                float roll = WaterHash(cell + floor(t * GLINT_RATE) * float2(17.0, 31.0));
                float sparkle = step(roll, g * 6.0);
                float glintGain = GLINT_GAIN * lerp(1.0, GLINT_NIGHT, _PSXNight);
                col += _PSXLightColor.rgb * sparkle * glintGain * PSXSunShadow(i.wpos, float3(0, 1, 0), eyeDist, 0.0);
                col += (lampS + headS) * LAMP_GAIN * (0.3 + fres);

                // THE SHORE: foam either side of the waterline, running up and
                // back on the swash, dithered by a hash rather than blended.
                float swash = sin(t * (6.2831853 / FOAM_SWASH_S) + i.wpos.x * 0.05 + i.wpos.z * 0.03) * FOAM_RUN_M;
                float foam = saturate(1.0 - abs(i.depth - swash) / FOAM_M);
                float grain = WaterHash(floor(i.wpos.xz * 6.0) + floor(t * 3.0));
                float foamOn = step(grain, foam * foam * 1.3);
                col = lerp(col, FOAM_COLOR * (light + lampD), foamOn);
                // WHITECAPS: the swell breaking white along its crests, dithered.
                float cap = saturate((i.swell.y - WHITECAP) / (1.0 - WHITECAP));
                float capGrain = WaterHash(floor(i.wpos.xz * 3.0) + floor(t * 2.0) * 7.0);
                col = lerp(col, FOAM_COLOR * light, step(capGrain, cap * cap * 0.9));

                float3 fogCol = PSXFogTowardSun(_PSXFogColor.rgb * PSXFogRing(-V, _PSXSkyRotation), V);
                col = lerp(col, fogCol, i.fog);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
