// Final upscale shader for the 320x240 render target:
// 15-bit color quantization with a 4x4 Bayer dither, like PS1 output.
//
// And, before the quantizer, THE FILM GRADE (2026-09-19). The owner, over four
// frames of a car film: "a nostalgic color grade I would like added as a
// filter... the old, nostalgic, 90s feeling. Like playing this game is a
// dream." Those frames were MEASURED, not eyeballed, and what they share is:
//
//   * no black. The darkest half-percent of every frame sits at 0.10-0.18 on
//     the display, neutral to a touch warm: a faded print's matte floor;
//   * no white. The brightest tenth of a percent stops at 0.91-0.96, and it
//     is cream with a breath of green in it (r .895 g .927 b .914), never
//     paper;
//   * little colour, except the warm ones. Mean saturation 0.12-0.16, but
//     the orange Beetle and the yellow Golf are as loud as they ever were:
//     blues and greens fade, reds, oranges and yellows hold, and foliage
//     leans olive;
//   * light that BLEEDS. Tail lamps, windows and the sun's glint each sit in
//     a soft warm halo - the diffusion filter on the lens. That is the dream.
//
// It lives here rather than in a pass of its own because this is the one
// place every picture the game draws already goes through, on every pipeline
// asset, in the preview tools as well as the player - and BEFORE the dither,
// so the quantizer's pattern breaks up the grade's gradients the way it does
// every other one. _Grade 0 is the picture exactly as it was.
//
// THE NIGHT GRADE (2026-09-21, the NFS pass). The owner, on Need for Speed
// (2015): "I like how dark the night is, how much the skybox effects the color
// and mood of the world and cars". Measured off NFS night frames: the darkest
// 0.1% of the display at 0.008-0.015, the median at 0.09-0.17, a third to a
// half of the frame under 0.10, mean saturation 0.50-0.58. Ours at night had
// a floor of 0.108 and NOTHING under 0.10 - and the reason was here: the
// matte lift above is a floor of 0.11 under every pixel, so no night could be
// darker than a faded print's black. But a lift IS lens veiling glare, and
// veiling glare scales with how much light the scene has: a film shot at
// night does not have a grey floor. So, keyed by the global _PSXGradeNight
// (TimeOfDay.GradeNightFor; 1 at night, 0 all day):
//
//   * the lift fades - to a fifth of itself at full night
//     (GRADE_NIGHT_LIFT_CUT), and the vignette falls to that SAME lower floor
//     (one `lift` for both, or the corners would stop at a floor the middle
//     no longer has);
//   * the vignette deepens (GRADE_VIGNETTE_NIGHT): night lenses are shot wide
//     open, and a dark corner is where the NFS frames are darkest;
//   * blues and greens keep their colour (GRADE_SAT_COOL_NIGHT): the day
//     grade drains them to 0.72, which at night turns a blue sky and a
//     sodium street into one beige;
//   * and a SHADOW SPLIT-TONE from the global _PSXMood (TimeOfDay.MoodFor):
//     the darks lean to the hour's hue - sodium-brown in a city night,
//     blue-grey on a mountain, blue at dusk - the sky's mood reaching the
//     parts of the picture no lamp lights.
//
// With _PSXGradeNight 0 and _PSXMood.a 0 - every daylight hour, and every
// scene that never applies an hour, since PSXGlobals pushes zero defaults -
// the grade is BIT-IDENTICAL to the one the owner signed off: each new term is
// added to the day's untouched expression times exactly zero, or sits in a
// uniform branch that is not taken (see the note above Grade). Both are
// GLOBALS, declared below as plain uniforms and never in Properties (a
// property of the same name would shadow the global and read its own 0).
// Like the rest of the grade they need FILM GRADE on; without it there is no
// lift to remove, and the night presets are dark on their own.
//
// The lens (rain drops, bokeh) is NOT here: the race HUD is already inside
// this framebuffer, and a drop would refract the lap counter. It is its own
// URP pass, PSX/Lens, drawn before the HUD (see SpeedBlurFeature).
Shader "PSX/Blit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ColorDepth ("Bits per channel", Range(3,8)) = 5
        _DitherStrength ("Dither", Range(0,1)) = 1
        _Grade ("Film grade", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _ColorDepth;
            float _DitherStrength;
            float _Grade;
            // Globals (PSXGlobals pushes them every frame from the hour;
            // see the header). NOT in Properties, on purpose.
            float _PSXGradeNight;   // 0 day .. 1 night
            float4 _PSXMood;        // rgb = shadow hue at any brightness, a = amount

            // THE GRADE. Display-space numbers (the grade is done on gamma
            // values, like every grade); tools/grade/grade_proto.py is the
            // same arithmetic in numpy, and is how they were tuned against
            // the reference frames' measured floor, ceiling and saturation.
            #define GRADE_LIFT        float3(0.112, 0.108, 0.104)   // the matte floor
            #define GRADE_CEIL        float3(0.915, 0.935, 0.915)   // the cream ceiling
            #define GRADE_SAT_COOL    0.72    // what blues and greens keep of their colour...
            #define GRADE_SAT_WARM    0.96    // ...and what reds, oranges and yellows keep
            #define GRADE_CONTRAST    0.22    // how much of an S is mixed into the middle
            #define GRADE_WARM_MID    0.022   // the mids lean amber
            #define GRADE_OLIVE       0.10    // foliage greens pulled toward olive
            #define GRADE_VIGNETTE    0.20
            // The night end of the same grade, reached at _PSXGradeNight 1.
            #define GRADE_NIGHT_LIFT_CUT  0.80  // how much of the matte lift a full night takes away
            #define GRADE_VIGNETTE_NIGHT  0.34  // the corners at night
            #define GRADE_SAT_COOL_NIGHT  1.00  // what blues and greens keep at night
            // ...and the whole night picture is pushed a little PAST its own
            // colour. The day grade's faded print is 0.12-0.16 mean saturation;
            // the NFS night frames MEASURE 0.50-0.58, because at night almost
            // every lit pixel is lit by one coloured lamp (sodium orange, a red
            // tail light, a lit window) and the darks are that light's murk.
            // First shots of this pass with the day's fade still on: 0.30-0.49.
            // Multiplies `keep`, so it is exactly 1 - a no-op - by day.
            #define GRADE_SAT_NIGHT_BOOST 1.18
            #define GRADE_MOOD_LO         0.05  // the split-tone is full below this luma...
            #define GRADE_MOOD_HI         0.45  // ...and gone above this one: shadows only
            // The halation: what is brighter than the knee bleeds, warm. The
            // knee is on LINEAR light where the framebuffer is linear (0.57
            // there is 0.78 on the display).
            #define GLOW_KNEE_GAMMA   0.78
            #define GLOW_KNEE_LINEAR  0.57
            #define GLOW_GAIN         0.55
            #define GLOW_TINT         float3(1.00, 0.86, 0.66)
            #define GLOW_R1           0.011   // ring radii, as a fraction of the picture's height
            #define GLOW_R2           0.026

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            static const float bayer[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            // Eight directions; the ring is turned per source pixel by the
            // Bayer cell, so sixteen taps read as a soft disc through the
            // dither instead of as sixteen ghosts of every tail lamp.
            static const float2 ring[8] =
            {
                float2( 1.000,  0.000), float2( 0.707,  0.707),
                float2( 0.000,  1.000), float2(-0.707,  0.707),
                float2(-1.000,  0.000), float2(-0.707, -0.707),
                float2( 0.000, -1.000), float2( 0.707, -0.707)
            };

            float3 Halation(float2 srcPixel, float cell)
            {
                float2 px = _MainTex_TexelSize.xy;
                float2 centre = (srcPixel + 0.5) * px;
                float lines = _MainTex_TexelSize.w;
                float a = cell * (UNITY_PI * 0.25 / 16.0);
                float ca = cos(a), sa = sin(a);
                // the second ring sits half a sector round from the first
                float cb = cos(a + UNITY_PI * 0.125), sb = sin(a + UNITY_PI * 0.125);
                float3 sum = 0;
                for (int k = 0; k < 8; k++)
                {
                    float2 d = ring[k];
                    float2 o1 = float2(d.x * ca - d.y * sa, d.x * sa + d.y * ca) * (lines * GLOW_R1);
                    float2 o2 = float2(d.x * cb - d.y * sb, d.x * sb + d.y * cb) * (lines * GLOW_R2);
                    float3 s1 = tex2D(_MainTex, centre + o1 * px).rgb;
                    float3 s2 = tex2D(_MainTex, centre + o2 * px).rgb;
                    #ifndef UNITY_COLORSPACE_GAMMA
                    sum += max(s1 - GLOW_KNEE_LINEAR, 0.0) + max(s2 - GLOW_KNEE_LINEAR, 0.0) * 0.6;
                    #else
                    sum += max(s1 - GLOW_KNEE_GAMMA, 0.0) + max(s2 - GLOW_KNEE_GAMMA, 0.0) * 0.6;
                    #endif
                }
                // 12.8 = the taps' total weight. A linear excess is worth
                // about half of itself on the display near the knee.
                #ifndef UNITY_COLORSPACE_GAMMA
                return sum * (0.53 / 12.8);
                #else
                return sum * (1.0 / 12.8);
                #endif
            }

            // HOW THE NIGHT TERMS ARE WRITTEN. Each one is the day's own
            // expression, untouched, PLUS (or minus) a night term that is
            // multiplied by `night` (or, for the mood, a uniform branch that
            // is not taken). At night 0 each added term is an exact zero, so
            // a daylight frame runs the same arithmetic on the same constants
            // it always did - bit for bit, not merely "within a rounding".
            // Folding the night into the constants instead (a lerp of lerps,
            // `lift + (GRADE_CEIL - lift) * s`) turns a literal the compiler
            // folds into a subtraction done at run time, which can land an
            // ulp away and flip a pixel over a quantizer step.
            float3 Grade(float3 c, float2 uv, float3 glow)
            {
                // 0 all day, 1 at full night (TimeOfDay.GradeNightFor).
                float night = saturate(_PSXGradeNight);

                c += glow * GLOW_GAIN * GLOW_TINT;

                // Foliage greens toward olive.
                float greenness = saturate((c.g - max(c.r, c.b)) * 3.0);
                c.r += greenness * GRADE_OLIVE * c.g;
                c.b -= greenness * GRADE_OLIVE * 0.5 * c.g;

                // Colour fades, by how warm it is - and at night the cool
                // colours fade far less: the day grade's 0.72 would turn a
                // blue night sky and an orange sodium street into one beige.
                // (That is lerp(lerp(COOL, COOL_NIGHT, night), WARM, warm),
                // written as the day's lerp plus its night difference.)
                float l = dot(c, float3(0.299, 0.587, 0.114));
                float warm = saturate((c.r - c.b) * 2.5);
                float keep = lerp(GRADE_SAT_COOL, GRADE_SAT_WARM, warm)
                           + (GRADE_SAT_COOL_NIGHT - GRADE_SAT_COOL) * night * (1.0 - warm);
                keep *= 1.0 + (GRADE_SAT_NIGHT_BOOST - 1.0) * night;
                c = l + (c - l) * keep;
                c = saturate(c);

                // THE SHADOW SPLIT-TONE: the darks lean to the hour's hue
                // (_PSXMood, TimeOfDay.MoodFor) and the lights keep their own.
                // The hue is normalised to luminance 1 so the tint moves
                // colour, not brightness; `l` is the luma the desaturation
                // just preserved. Saturated after, because a blue hue of
                // luminance 1 carries a blue channel over 1.
                if (_PSXMood.a > 0.0)
                {
                    float3 hue = _PSXMood.rgb / max(dot(_PSXMood.rgb, float3(0.299, 0.587, 0.114)), 1e-3);
                    float sh = 1.0 - smoothstep(GRADE_MOOD_LO, GRADE_MOOD_HI, l);
                    c = saturate(lerp(c, c * hue, saturate(_PSXMood.a) * sh));
                }

                // A little S in the middle, and the mids lean amber.
                c = lerp(c, c * c * (3.0 - 2.0 * c), GRADE_CONTRAST);
                l = dot(c, float3(0.299, 0.587, 0.114));
                float mid = 4.0 * l * (1.0 - l);
                c.r += GRADE_WARM_MID * mid;
                c.b -= GRADE_WARM_MID * mid;

                // The floor and the ceiling. `lift` is the floor THIS hour
                // has: the matte lift is veiling glare, and at night there is
                // little light to veil with, so it fades toward a fifth of
                // itself. The ONE lift for both the remap and the vignette
                // below - a vignette falling to the day's floor would leave
                // the corners greyer than the middle of a night frame.
                // Written as the day's remap minus the lift night gives back:
                // it IS lift + (GRADE_CEIL - lift) * s.
                float3 lift = GRADE_LIFT * (1.0 - GRADE_NIGHT_LIFT_CUT * night);
                float3 given = GRADE_LIFT - lift;
                float3 s = saturate(c);
                c = GRADE_LIFT + (GRADE_CEIL - GRADE_LIFT) * s - given * (1.0 - s);

                // The lens falls off toward the corners - down to the floor,
                // not to black; deeper at night (GRADE_VIGNETTE_NIGHT), down
                // to the night's lower floor. The return IS
                // lift + (c - lift) * vig.
                float2 q = (uv - 0.5) * 2.0;
                float r2 = dot(q, q) * 0.5;
                float fall = saturate((r2 - 0.25) / 0.75);
                float vig = 1.0 - GRADE_VIGNETTE * fall * sqrt(fall)
                          - (GRADE_VIGNETTE_NIGHT - GRADE_VIGNETTE) * night * fall * sqrt(fall);
                return GRADE_LIFT + (c - GRADE_LIFT) * vig - given * (1.0 - vig);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 col = tex2D(_MainTex, i.uv).rgb;

                // QUANTIZE IN GAMMA SPACE. The project renders in linear
                // colour, so the framebuffer sample here is linear, and 31
                // steps of LINEAR light are nothing like the PS1's 31 steps:
                // the first step above black is already 19% grey on the
                // display and everything darker than that is a dither between
                // black and it. That crushed every shadow in the game to a
                // speckle - the dark side of a car, a night road, the sills -
                // and reads as "flat" because it IS flat: the darks have two
                // levels. The PS1's 15-bit framebuffer was gamma-encoded, so
                // its steps were perceptually even. Convert, quantize, convert
                // back; the display encode at the end of the pipeline then
                // lands the values where the console would have put them.
                #ifndef UNITY_COLORSPACE_GAMMA
                col = LinearToGammaSpace(col);
                #endif

                // Dither in source-pixel space so the pattern is 1:1 with the low-res buffer
                float2 srcPixel = floor(i.uv * _MainTex_TexelSize.zw);
                int idx = (int)(fmod(srcPixel.x, 4.0)) + 4 * (int)(fmod(srcPixel.y, 4.0));
                float threshold = (bayer[idx] + 0.5) / 16.0 - 0.5;

                // The grade goes in BEFORE the quantizer, so its gradients
                // are dithered like everything else's.
                if (_Grade > 0.001)
                    col = lerp(col, Grade(col, i.uv, Halation(srcPixel, bayer[idx])), _Grade);

                float levels = pow(2.0, _ColorDepth) - 1.0;
                col += threshold * (_DitherStrength / levels);
                col = floor(col * levels + 0.5) / levels;

                #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(col);
                #endif
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
