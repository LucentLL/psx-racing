// THE LENS (2026-09-21): rain on the glass, and dirt on it that catches the
// lights. The owner, on what to take from Need for Speed (2015): "I like the
// particle effects on screen for rain and light." Two things, both on the
// LENS rather than in the world:
//
//   * DROPS while it rains. Each one is a little lens of its own: it shows the
//     scene AROUND it, shrunk and upside down (an inverted fisheye), softened,
//     with a darker rim and a glint at its top left. Nothing about it knows
//     where the lights are - and it does not need to: a drop sitting in front
//     of a street lamp or a tail light is filled with that light, and THAT is
//     the NFS bokeh, for free, and in the right colour.
//   * DIRT at night (and in the rain). Soft discs of grime that are invisible
//     until something bright is near them, then glow with it: the faint bokeh
//     a real windscreen or lens throws around every lamp at night.
//
// Run by SpeedBlurFeature's LensPass as a full-frame pass on the PSX camera's
// colour, at AfterRenderingTransparents + 1:
//
//   * AFTER the speed blur, because the drops sit on the glass and the world
//     smears BEHIND them - a drop must not be streaked with the road.
//   * BEFORE the stacked HUD camera draws. This is why the lens is a pass here
//     and not a few lines in PSX/Blit, which was the first plan: by the time
//     the Blit runs, the lap counter, the fuel bar and the map are already IN
//     the picture it reads (a ScreenSpaceCamera canvas, or the HUD camera
//     writing the same target), so a drop there would refract the lap counter
//     and the dirt would bloom every white HUD glyph. SpeedBlur hands the HUD
//     to its own stacked camera whenever LENS FX is on, as it does for the
//     blur, and a stacked camera draws after every pass of its base.
//
// AT FRAMEBUFFER RESOLUTION, because this pass IS the framebuffer (240, 360
// or 480 lines). A drop is made of the same fat pixels as the car it sits in
// front of, and goes through PSX/Blit's quantise and Bayer dither with
// everything else - it reads as the game, not as a modern effect laid over
// it. It is also about a fifth of the cost of doing it per device pixel.
//
// LDR, like everything this camera draws: HDR is off and the buffer is 8-bit,
// so nothing is brighter than 1. The dirt keys on the EXCESS over the same
// knee PSX/Blit's halation uses (0.57 linear, 0.78 on the display), which is
// lamps, tail lights, headlights and lit windows - never the road or a grey
// sky.
//
// Bit-exact where there is nothing on the glass: a pixel in no drop and no
// dirt disc is handed back exactly as the camera drew it (a point sample at
// its own centre), so a dry day with LENS FX on is the picture it always was.
//
// Two passes, the blur's shape: 1 copies the frame out, 0 draws it back
// through the lens. Both read _BlitTexture with the Blit.hlsl full-screen
// triangle (SpeedBlurFeature sets it through a property block).
Shader "PSX/Lens"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // 0 - the lens. RGB only: the camera's own alpha is not this pass's to
        // change.
        Pass
        {
            Name "Lens"
            ZWrite Off ZTest Always Cull Off Blend Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Material vectors, set every frame by SpeedBlurFeature from LensFx
            // (never globals, never in a Properties block - this material is
            // made at runtime and nothing else draws with it).
            // x: rain on the lens 0..1 (WeatherFx.LensRain, gated by LENS FX)
            // y: dirt 0..1 (LensFx.DirtFor: 0.7 x the DARK half of night -
            //    none at sunset or dawn, 0.35 at dusk, 0.7 at night - + 0.5 x rain)
            // z: flow 0..1 (road speed / 160 km/h: the air going over the glass)
            // w: the DROP CLOCK, seconds. Not Time.time: SpeedBlur integrates it
            //    at (1 + 2.5 x flow) times game time, so a drop lives faster at
            //    speed. Doing that multiply HERE (time x rate x (1 + 2.5 flow))
            //    would jump every drop to a random point of its life whenever
            //    the speedometer moved: at ten minutes in, a change of flow of
            //    one part in a thousand is half a life.
            float4 _PSXLens;
            // x: frame aspect (width / height), y: its reciprocal
            float4 _PSXLensAspect;

            // Below this a term is not drawn at all (LensFx.MinDrawn).
            #define LENS_MIN          0.001
            #define LENS_TAU          6.28318530718

            // ---- THE DROPS --------------------------------------------------
            // Everything is measured in PICTURE HEIGHTS on an aspect-corrected
            // plane (x runs 0..aspect), so a drop is round and the same size on
            // a phone held either way and on a monitor.
            //
            // Two layers of cells, big drops and small ones; one drop per cell
            // at most, re-rolled every life (a new place, a new size), so no
            // cell keeps a drop in the same spot for more than a few seconds.
            #define DROP_CELL_A       0.085   // cell size, big drops
            #define DROP_CELL_B       0.055   // cell size, small drops
            #define DROP_JITTER_MIN   0.25    // the drop's centre, within its cell
            #define DROP_JITTER_MAX   0.75
            #define DROP_R_MIN        0.12    // radius, as a fraction of the cell
            #define DROP_R_MAX        0.24
            #define DROP_DENSITY      0.55    // share of cells holding a drop at full rain
            #define DROP_POP          12.0    // 1 / the soft edge of that share: as the
                                              // rain eases in, drops fade in instead of
                                              // popping in mid-life
            #define DROP_RATE_MIN     0.16    // lives per second of the drop clock
            #define DROP_RATE_MAX     0.42    // (2.4 - 6 s a drop, standing still)
            #define DROP_FADE_IN      0.06    // share of a life spent appearing...
            #define DROP_FADE_OUT     0.75    // ...and where it starts to shrink and go
            #define DROP_SHRINK       0.45    // radius left at the very end of a life
            #define DROP_SLIDE        0.60    // cells a drop is blown OUTWARD over its
                                              // life at full flow (air over the lens)
            #define DROP_REFRACT      2.4     // the drop shows what is within 2.4 r of it,
                                              // inverted
            #define DROP_BLUR         0.35    // the 5-tap soften, as a fraction of r
            #define DROP_GAIN         1.08    // a drop is a lens: a touch brighter
            #define DROP_RIM          0.30    // the dark ring at its edge...
            #define DROP_RIM_FROM     0.72    // ...from here out (|q|, 1 = the edge)
            #define DROP_HL           float2(-0.35, 0.40)  // the glint, top left
            #define DROP_HL_AMT       0.18
            #define DROP_HL_SHARP     38.0
            #define DROP_EDGE         0.92    // anti-aliased edge, |q| 0.92..1

            // ---- THE DIRT ---------------------------------------------------
            // What is bright near a pixel - a ring of taps round it plus the
            // pixel itself, the excess over the knee averaged - lit through a
            // mask of soft grime discs. A disc near a lamp glows the lamp's
            // colour; a disc near nothing is invisible.
            #define DIRT_RING         0.055   // ring radius, picture heights
            #define DIRT_TAPS         12
            #ifndef UNITY_COLORSPACE_GAMMA
            #define DIRT_KNEE         0.57    // linear (PSX/Blit's GLOW_KNEE_LINEAR)
            #else
            #define DIRT_KNEE         0.78
            #endif
            #define DIRT_CELL_A       0.07    // disc cells, picture heights
            #define DIRT_CELL_B       0.11
            #define DIRT_FILL         0.60    // share of cells holding a disc
            #define DIRT_R_MIN        0.25    // disc radius, as a fraction of the cell
            #define DIRT_R_MAX        0.45
            #define DIRT_EDGE         0.80    // soft edge, d 0.80..1
            #define DIRT_RIM          0.35    // the rim is this much brighter than the
                                              // middle: that is what makes a disc read
                                              // as BOKEH rather than as a smudge
            #define DIRT_DIM          0.55    // the faintest disc, against the strongest
            #define DIRT_GAIN         1.6

            static const float bayer[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            // Sine-free hash (Dave Hoskins, "Hash without Sine", MIT): four
            // numbers 0..1 from a 2D point. The usual frac(sin(x) * 43758)
            // falls apart in the low-precision sin() of some phone GPUs and
            // prints the cell grid as bands.
            float4 LensHash42(float2 p)
            {
                float4 p4 = frac(p.xyxy * float4(0.1031, 0.1030, 0.0973, 0.1099));
                p4 += dot(p4, p4.wzxy + 33.33);
                return frac((p4.xxyz + p4.yzzw) * p4.zywx);
            }

            float3 LensTap(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
            }

            // The drop covering this pixel best, so far.
            struct LensDrop
            {
                float  a;        // coverage x life, 0 = none
                float2 q;        // pixel offset from its centre / its radius
                float2 centre;   // picture heights
                float  r;
            };

            // One cell's drop, as this pixel sees it. Kept only if it covers
            // the pixel more than the best so far: where two drops overlap,
            // the one the pixel is deeper inside wins, and the expensive part
            // (the refraction) runs once per pixel, not once per candidate.
            void LensCellDrop(float2 cell, float2 p, float size, float2 seed, float2 frameCentre,
                              float density, float flow, float clock, inout LensDrop best)
            {
                // Per cell, for ever: how fast its drops live and where in a
                // life this cell started.
                float4 h0 = LensHash42(cell + seed);
                float life = clock * lerp(DROP_RATE_MIN, DROP_RATE_MAX, h0.x) + h0.y;
                float cycle = floor(life);
                float t = life - cycle;
                // Per LIFE: whether there is a drop at all, where and how big.
                // The life count is wrapped so the hash's input stays small
                // (float precision); 251 lives of one cell is a quarter of an
                // hour, and no two cells share a rate.
                float wrapped = fmod(cycle, 251.0);
                float4 h1 = LensHash42(cell + seed + float2(17.17 + wrapped * 7.13, 29.29 + wrapped * 3.71));
                float alive = saturate((density - h1.w) * DROP_POP);
                if (alive <= 0.0) return;

                float2 base = (cell + DROP_JITTER_MIN + (DROP_JITTER_MAX - DROP_JITTER_MIN) * h1.xy) * size;
                float r = lerp(DROP_R_MIN, DROP_R_MAX, h1.z) * size;
                float end = smoothstep(DROP_FADE_OUT, 1.0, t);
                r *= lerp(1.0, DROP_SHRINK, end);

                // Blown outward, away from the middle of the frame, slowly at
                // first and then running (t squared): a drop clings, then goes.
                float2 away = base - frameCentre;
                away /= max(length(away), 1e-4);
                float2 centre = base + away * (DROP_SLIDE * size * flow * t * t);

                float2 q = (p - centre) / r;
                float a = alive * saturate(t / DROP_FADE_IN) * (1.0 - end)
                        * (1.0 - smoothstep(DROP_EDGE, 1.0, length(q)));
                if (a > best.a)
                {
                    best.a = a;
                    best.q = q;
                    best.centre = centre;
                    best.r = r;
                }
            }

            // A layer of drops. Standing still a drop never leaves its cell
            // (centre 0.25..0.75, radius under 0.25), so one cell is enough.
            // Blown outward it can travel DROP_SLIDE (0.6) of a cell - into
            // the next cell OUTWARD, never further, never inward. So a pixel
            // also asks the three cells between it and the middle of the frame
            // (`up`, the upstream quadrant), and nothing a drop does can reach
            // a pixel that did not ask its cell.
            void LensDropLayer(float2 p, float size, float2 seed, float2 up, float2 frameCentre,
                               float density, float flow, float clock, inout LensDrop best)
            {
                float2 cell = floor(p / size);
                LensCellDrop(cell, p, size, seed, frameCentre, density, flow, clock, best);
                if (flow > 0.001)
                {
                    LensCellDrop(cell + float2(up.x, 0.0), p, size, seed, frameCentre, density, flow, clock, best);
                    LensCellDrop(cell + float2(0.0, up.y), p, size, seed, frameCentre, density, flow, clock, best);
                    LensCellDrop(cell + up, p, size, seed, frameCentre, density, flow, clock, best);
                }
            }

            // What the drop shows: the scene around it, inverted and shrunk,
            // softened by five taps, darker at the rim, with a glint whose
            // strength follows what the drop is showing (a drop against the
            // night sky barely glints).
            float3 LensDropColour(LensDrop d, float invAspect)
            {
                float qd = length(d.q);
                float2 look = d.centre - d.q * (d.r * DROP_REFRACT);
                float2 luv = float2(look.x * invAspect, look.y);
                float o = d.r * DROP_BLUR;
                float2 ox = float2(o * invAspect, 0.0);
                float2 oy = float2(0.0, o);
                float3 seen = LensTap(luv) + LensTap(luv + ox) + LensTap(luv - ox)
                            + LensTap(luv + oy) + LensTap(luv - oy);
                seen *= 0.2 * DROP_GAIN;
                seen *= 1.0 - DROP_RIM * smoothstep(DROP_RIM_FROM, 1.0, qd);
                float lum = dot(seen, float3(0.2126, 0.7152, 0.0722));
                float2 h = d.q - DROP_HL;
                seen += DROP_HL_AMT * exp(-DROP_HL_SHARP * dot(h, h)) * (lum + 0.1);
                return seen;
            }

            // One layer of grime discs. A disc sits wholly inside its cell
            // (centre r..1-r), so one cell is all a pixel has to ask.
            float LensDirtLayer(float2 p, float size, float2 seed)
            {
                float2 cell = floor(p / size);
                float4 h = LensHash42(cell + seed);
                if (h.w >= DIRT_FILL) return 0.0;
                float R = lerp(DIRT_R_MIN, DIRT_R_MAX, h.z);
                float2 c = cell + R + h.xy * (1.0 - 2.0 * R);
                float d = length(p / size - c) / R;
                float disc = 1.0 - smoothstep(DIRT_EDGE, 1.0, d);
                float rim = 1.0 + DIRT_RIM * smoothstep(0.50, 0.90, d);
                return disc * rim * lerp(DIRT_DIM, 1.0, frac(h.x * 7.3 + h.y * 3.1));
            }

            // Two layers, the second's grid shifted so the two never line up.
            float LensDirtMask(float2 p)
            {
                return LensDirtLayer(p, DIRT_CELL_A, float2(101.0, 43.0))
                     + LensDirtLayer(p + float2(0.37, 0.61), DIRT_CELL_B, float2(59.0, 211.0));
            }

            // What is bright near this pixel: the excess over the knee, on a
            // ring of DIRT_TAPS round it plus the pixel itself, averaged. The
            // ring is turned per pixel by the same 4x4 Bayer cell PSX/Blit
            // dithers and haloes with, so twelve taps read as a soft disc
            // through the dither instead of as twelve ghosts of every lamp.
            float3 LensBright(float2 uv, float2 pixel, float invAspect)
            {
                float2 px = floor(pixel);
                int idx = (int)fmod(px.x, 4.0) + 4 * (int)fmod(px.y, 4.0);
                float a = (bayer[idx] + 0.5) * (LENS_TAU / (16.0 * DIRT_TAPS));
                float2 dir = float2(cos(a), sin(a));
                float cs = cos(LENS_TAU / DIRT_TAPS), sn = sin(LENS_TAU / DIRT_TAPS);
                float3 sum = max(LensTap(uv) - DIRT_KNEE, 0.0);
                for (int k = 0; k < DIRT_TAPS; k++)
                {
                    sum += max(LensTap(uv + float2(dir.x * DIRT_RING * invAspect, dir.y * DIRT_RING)) - DIRT_KNEE, 0.0);
                    dir = float2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
                }
                return sum / (DIRT_TAPS + 1.0);
            }

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;

                float rain = _PSXLens.x;
                float dirt = _PSXLens.y;
                float flow = saturate(_PSXLens.z);
                float clock = _PSXLens.w;
                float aspect = max(_PSXLensAspect.x, 0.01);
                float invAspect = 1.0 / aspect;
                // The aspect-corrected plane: picture heights, 0..aspect across.
                float2 p = float2(uv.x * aspect, uv.y);

                if (rain > LENS_MIN)
                {
                    LensDrop best;
                    best.a = 0.0;
                    best.q = float2(0.0, 0.0);
                    best.centre = float2(0.0, 0.0);
                    best.r = 1.0;
                    float2 frameCentre = float2(0.5 * aspect, 0.5);
                    // Toward the middle of the frame, per axis: where a drop
                    // blown outward onto this pixel came from.
                    float2 up = float2(p.x < frameCentre.x ? 1.0 : -1.0,
                                       p.y < frameCentre.y ? 1.0 : -1.0);
                    float density = DROP_DENSITY * rain;
                    LensDropLayer(p, DROP_CELL_A, float2(0.0, 0.0), up, frameCentre, density, flow, clock, best);
                    LensDropLayer(p, DROP_CELL_B, float2(113.0, 57.0), up, frameCentre, density, flow, clock, best);
                    if (best.a > 0.002)
                        col = lerp(col, LensDropColour(best, invAspect), best.a);
                }

                // The dirt goes on AFTER the drops, over them: it is glare on
                // the glass, and a drop does not wash the glass under it clean.
                // The mask first - it is cheap, and where it is zero (most of
                // the frame) the thirteen taps are never read.
                if (dirt > LENS_MIN)
                {
                    float mask = LensDirtMask(p);
                    if (mask > 0.001)
                        col += LensBright(uv, input.positionCS.xy, invAspect) * (mask * DIRT_GAIN * dirt);
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // 1 - the frame, copied out as it is, for pass 0 to read while it
        // writes the camera's own colour. (Reading and writing one texture in
        // one draw is not something every GPU forgives.)
        Pass
        {
            Name "Copy"
            ZWrite Off ZTest Always Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CopyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 CopyFrag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
