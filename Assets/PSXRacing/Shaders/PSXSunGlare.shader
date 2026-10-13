// THE SUN IN THE LENS (2026-10-13, the GT look plan, item 2). Gran Turismo
// 3's own effects list: "blinding sun glare hitting the windshield", and the
// eyes that "adjust" as the sun comes out from behind a building. SunGlareFx
// (Scripts/SpeedBlur.cs) says when; this says what.
//
// Run by SpeedBlurFeature's GlarePass on the PSX camera's colour at
// AfterRenderingTransparents + 2 - after the speed blur and the lens, before
// the stacked HUD camera - at FRAMEBUFFER resolution, so PSX/Blit grades,
// quantises and dithers the glare with everything else.
//
// TWO PASSES:
//
//   0 THE EYE, into ONE pixel. Twelve taps of the frame's ALPHA in a disc
//     round the sun (1.2 degrees across: inside the sky disc's full-strength
//     core). The alpha is the emitter mask (PSXTone.cginc): the sky writes
//     its sun disc there and every opaque surface writes 0, so the taps say
//     how much of the sun a tower, a hill, a tree's leaves or a car has
//     hidden. Taps off the edge of the picture count as hidden, so the glare
//     fades as the sun leaves the frame. The pixel keeps two numbers, each
//     packed 16 bits into a pair of RGBA8 channels (no float targets on
//     WebGL): the SEEN sun, eased a little (leaves flicker, they do not
//     strobe), and the EYE, which follows it - fast into the light, slowly
//     back out. Last frame's pixel is read, this frame's written.
//
//   1 THE GLARE, screen-blended into the colour (Blend One OneMinusSrcColor:
//     it washes toward the sun's colour and never clips). A hot bloom on the
//     sun, a wide glow round it, and a veil over the whole picture that is
//     strongest with the sun in the middle of it - all by how much of the sun
//     is seen. And THE FLASH, by how far the seen sun is ahead of the eye:
//     out of a tunnel, or round a building into a low sun, the frame blazes
//     and settles over a second. RGB only: the alpha is the emitter mask
//     PSX/Blit's halation still reads, and the sun's disc keeps its halo.
//
// Material vectors only, set every frame by SpeedBlurFeature (this material
// is made at runtime and nothing else draws with it).
Shader "PSX/SunGlare"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #pragma target 3.0
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // xy: the sun on the picture (viewport = this pass's uv); z: 1 when it
        // is in front of the lens; w: the meter's radius, in picture heights.
        float4 _PSXGlareSun;
        // x/y/z: this frame's blend toward the seen sun, the eye closing, the
        // eye opening (1 - exp(-dt / tau), SunGlareFx's taus); w: 1 = start
        // adapted (no flash).
        float4 _PSXGlareEye;
        // x: the hour's sun 0..1; y: the picture's aspect (width / height).
        float4 _PSXGlareParams;
        // The sun's colour, half way to warm white.
        float4 _PSXGlareTint;

        TEXTURE2D(_PSXGlarePrev);
        TEXTURE2D(_PSXGlareState);

        // 0..1 as two 8-bit channels: the whole 255ths, then what is left.
        float2 PackGlare(float v)
        {
            v = saturate(v) * 255.0;
            float hi = floor(v);
            return float2(hi / 255.0, v - hi);
        }
        float UnpackGlare(float2 p)
        {
            return saturate((round(p.x * 255.0) + p.y) / 255.0);
        }
        ENDHLSL

        // 0 - the eye (one pixel).
        Pass
        {
            Name "Eye"
            ZWrite Off ZTest Always Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment EyeFrag

            #define METER_TAPS 12

            float4 EyeFrag (Varyings input) : SV_Target
            {
                float4 last = LOAD_TEXTURE2D(_PSXGlarePrev, int2(0, 0));
                float seenLast = UnpackGlare(last.rg);
                float eyeLast = UnpackGlare(last.ba);

                float seen = 0.0;
                if (_PSXGlareSun.z > 0.5)
                {
                    float aspect = max(_PSXGlareParams.y, 0.01);
                    // A golden-angle spiral: even cover of the disc in twelve.
                    [unroll]
                    for (int k = 0; k < METER_TAPS; k++)
                    {
                        float r = sqrt((k + 0.5) / METER_TAPS) * _PSXGlareSun.w;
                        float a = k * 2.39996323;
                        float2 uv = _PSXGlareSun.xy + float2(cos(a) * r / aspect, sin(a) * r);
                        float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                        seen += inside * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).a;
                    }
                    seen /= METER_TAPS;
                }

                float reset = step(0.5, _PSXGlareEye.w);
                float s = lerp(lerp(seenLast, seen, _PSXGlareEye.x), seen, reset);
                float k2 = s > eyeLast ? _PSXGlareEye.y : _PSXGlareEye.z;
                float e = lerp(lerp(eyeLast, s, k2), s, reset);
                return float4(PackGlare(s), PackGlare(e));
            }
            ENDHLSL
        }

        // 1 - the glare. Screen blend, RGB only.
        Pass
        {
            Name "Glare"
            ZWrite Off ZTest Always Cull Off
            Blend One OneMinusSrcColor
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment GlareFrag

            // Linear light added (screen-blended) at full strength with the
            // whole sun seen. Radii are fractions of the picture's height.
            #define GLARE_CORE         0.85   // the hot bloom on the disc...
            #define GLARE_CORE_R       0.055  // ...about a tenth of the picture across
            #define GLARE_HALO         0.38   // the wide glow round it...
            #define GLARE_HALO_R       0.14   // ...falling off over this
            #define GLARE_VEIL         0.14   // the whole picture, with the sun dead centre...
            #define GLARE_VEIL_REACH   0.90   // ...gone with the sun this far from the middle
            #define GLARE_FLASH_HALO   0.45   // the eye not yet closed down: the glow...
            #define GLARE_FLASH_VEIL   0.35   // ...and the whole frame

            float4 GlareFrag (Varyings input) : SV_Target
            {
                float4 st = LOAD_TEXTURE2D(_PSXGlareState, int2(0, 0));
                float seen = UnpackGlare(st.rg);
                float eye = UnpackGlare(st.ba);
                float strength = saturate(_PSXGlareParams.x);
                float steady = strength * seen;
                float flash = strength * max(seen - eye, 0.0);

                float aspect = max(_PSXGlareParams.y, 0.01);
                float2 d = input.texcoord - _PSXGlareSun.xy;
                d.x *= aspect;
                float r = length(d);
                float core = exp(-(r * r) / (GLARE_CORE_R * GLARE_CORE_R));
                float halo = exp(-r / GLARE_HALO_R);
                float2 c = _PSXGlareSun.xy - 0.5;
                c.x *= aspect;
                float central = saturate(1.0 - length(c) / GLARE_VEIL_REACH);

                float g = steady * (GLARE_CORE * core + GLARE_HALO * halo + GLARE_VEIL * central * central)
                        + flash * (GLARE_FLASH_HALO * halo + GLARE_FLASH_VEIL * central);
                return float4(saturate(_PSXGlareTint.rgb * g), 0.0);
            }
            ENDHLSL
        }
    }
}
