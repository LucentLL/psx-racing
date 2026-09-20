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

            float3 Grade(float3 c, float2 uv, float3 glow)
            {
                c += glow * GLOW_GAIN * GLOW_TINT;

                // Foliage greens toward olive.
                float greenness = saturate((c.g - max(c.r, c.b)) * 3.0);
                c.r += greenness * GRADE_OLIVE * c.g;
                c.b -= greenness * GRADE_OLIVE * 0.5 * c.g;

                // Colour fades, by how warm it is.
                float l = dot(c, float3(0.299, 0.587, 0.114));
                float warm = saturate((c.r - c.b) * 2.5);
                c = l + (c - l) * lerp(GRADE_SAT_COOL, GRADE_SAT_WARM, warm);
                c = saturate(c);

                // A little S in the middle, and the mids lean amber.
                c = lerp(c, c * c * (3.0 - 2.0 * c), GRADE_CONTRAST);
                l = dot(c, float3(0.299, 0.587, 0.114));
                float mid = 4.0 * l * (1.0 - l);
                c.r += GRADE_WARM_MID * mid;
                c.b -= GRADE_WARM_MID * mid;

                // The floor and the ceiling.
                c = GRADE_LIFT + (GRADE_CEIL - GRADE_LIFT) * saturate(c);

                // The lens falls off toward the corners - down to the floor,
                // not to black.
                float2 q = (uv - 0.5) * 2.0;
                float r2 = dot(q, q) * 0.5;
                float fall = saturate((r2 - 0.25) / 0.75);
                float vig = 1.0 - GRADE_VIGNETTE * fall * sqrt(fall);
                return GRADE_LIFT + (c - GRADE_LIFT) * vig;
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
