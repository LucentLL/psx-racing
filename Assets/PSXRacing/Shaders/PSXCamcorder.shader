// THE CAMCORDER (2026-11-16, owner: "replays could look like they're
// recorded on camcorders"). A 1999 Hi8 / VHS-C picture over the replay: a 4:3
// frame, soft luma and smeared chroma, a little tracking noise and the head
// switch at the foot of the frame, a soft scan, highlights that knee over,
// lifted blacks, an auto-exposure and an auto white balance that LAG behind
// the scene, and the deck's own trouble when a tape is paused or scanned.
//
// Run by SpeedBlurFeature's CamcorderPass (Scripts/Replay/CamcorderPass.cs) on
// the PSX camera's colour after the blur, the lens and the glare, before the
// stacked HUD camera - so the OSD stays crisp, as a viewfinder's does. Only
// while a camcorder replay or a tape is on screen (Camcorder.Camera).
//
// THREE PASSES:
//   0 THE EYE, one pixel: the frame's centre-weighted mean (48 jittered taps
//     inside the 4:3 window) and last frame's eye. R = sqrt(adapted luma),
//     G = sqrt(this frame's luma, lightly eased), B / A = the adapted
//     chromaticity (r and b shares). RGBA8 with a dithered round, so a slow
//     ease still converges (no float targets needed on a phone).
//   1 THE PICTURE, RGB only (the alpha is the emitter mask PSX/Blit reads).
//   2 COPY, as it is.
Shader "PSX/Camcorder"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #pragma target 3.0
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // x: clock (s); y: defocus 0..1; z: pan smear (uv across the frame);
        // w: tracking noise 0..1.
        float4 _PSXCam0;
        // x: the frame's aspect (w / h); y: 1 = frame the picture 4:3;
        // z: transport (0 play, 1 pause, 2 scan); w: scan rate (signed).
        float4 _PSXCam1;
        // x/y: the frame in pixels; z: frame counter; w: 1 = soft scan lines.
        float4 _PSXCam2;
        // Eye pass: x: luma ease this frame; y: chroma ease; z: 1 = start
        // over; w: where it starts, as a factor on the measured luma.
        float4 _PSXCamEyeK;
        // x: the static between two recordings (a cut to another camera), 0..1.
        float4 _PSXCam3;

        TEXTURE2D(_PSXCamEyePrev);
        TEXTURE2D(_PSXCamEye);

        static const float3 LumaW = float3(0.299, 0.587, 0.114);

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        // The 4:3 window inside the frame: x0, width (uv), y0, height (uv).
        float4 Window()
        {
            float aspect = max(_PSXCam1.x, 0.1);
            const float target = 4.0 / 3.0;
            if (_PSXCam1.y < 0.5) return float4(0, 1, 0, 1);
            if (aspect >= target)
            {
                float w = target / aspect;
                return float4(0.5 - 0.5 * w, w, 0, 1);
            }
            float h = aspect / target;
            return float4(0, 1, 0.5 - 0.5 * h, h);
        }
        ENDHLSL

        // 0 - the eye (one pixel)
        Pass
        {
            Name "Eye"
            ZWrite Off ZTest Always Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment EyeFrag

            float4 EyeFrag (Varyings input) : SV_Target
            {
                float4 last = LOAD_TEXTURE2D(_PSXCamEyePrev, int2(0, 0));
                float4 win = Window();
                float f = _PSXCam2.z;
                float2 jit = float2(Hash12(float2(f, 1.7)), Hash12(float2(f, 9.3)));
                float3 sum = 0;
                float wsum = 0;
                [unroll]
                for (int j = 0; j < 6; j++)
                {
                    [unroll]
                    for (int i = 0; i < 8; i++)
                    {
                        float2 g = (float2(i, j) + jit) / float2(8.0, 6.0);
                        // Centre-weighted, as a camcorder meters.
                        float2 c = g - 0.5;
                        float w = 1.0 - 0.6 * saturate(dot(c, c) * 3.0);
                        float2 uv = float2(win.x + g.x * win.y, win.z + g.y * win.w);
                        sum += w * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                        wsum += w;
                    }
                }
                float3 mean = sum / max(wsum, 1e-4);
                float luma = dot(mean, LumaW);
                float3 chroma = mean / max(mean.r + mean.g + mean.b, 1e-3);

                float adapted = last.r * last.r;
                float now = last.g * last.g;
                float2 shares = last.ba;
                // A pixel that has never been written reads 0: start there.
                bool fresh = _PSXCamEyeK.z > 0.5 || (last.r + last.g + last.b + last.a) < 1e-4;
                if (fresh)
                {
                    adapted = luma * max(_PSXCamEyeK.w, 0.05);
                    now = luma;
                    if ((last.b + last.a) < 1e-4) shares = chroma.rb;
                }
                else
                {
                    adapted = lerp(adapted, luma, _PSXCamEyeK.x);
                    now = lerp(now, luma, 0.5);
                    shares = lerp(shares, chroma.rb, _PSXCamEyeK.y);
                }
                // A dithered round: an ease of a fraction of an 8-bit step
                // still moves on average.
                float d = (Hash12(float2(f * 0.37, 4.1)) - 0.5) / 255.0;
                return saturate(float4(sqrt(max(adapted, 0)), sqrt(max(now, 0)), shares) + d);
            }
            ENDHLSL
        }

        // 1 - the picture
        Pass
        {
            Name "Picture"
            ZWrite Off ZTest Always Cull Off Blend Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment PictureFrag

            float3 ToYIQ(float3 c)
            {
                return float3(dot(c, LumaW),
                              dot(c, float3(0.596, -0.274, -0.322)),
                              dot(c, float3(0.211, -0.523, 0.312)));
            }
            float3 FromYIQ(float3 y)
            {
                return float3(y.x + 0.956 * y.y + 0.621 * y.z,
                              y.x - 0.272 * y.y - 0.647 * y.z,
                              y.x - 1.106 * y.y + 1.703 * y.z);
            }
            float3 Tap(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
            }

            half4 PictureFrag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 win = Window();
                float2 wuv = float2((uv.x - win.x) / win.y, (uv.y - win.z) / win.w);
                if (wuv.x < 0.0 || wuv.x > 1.0 || wuv.y < 0.0 || wuv.y > 1.0)
                    return half4(0, 0, 0, 1);

                float t = _PSXCam0.x;
                float frame = _PSXCam2.z;
                int transport = (int)round(_PSXCam1.z);
                float ln = floor(wuv.y * 480.0);
                float field = floor(frame);

                // ---- the time base: per-line wobble, the head switch at the
                // foot of the frame, a tracking band now and then ----
                float wobble = (Hash12(float2(ln, field)) - 0.5) * 0.0009
                             + sin(wuv.y * 37.0 + t * 1.3) * 0.0004;
                float foot = saturate((0.022 - wuv.y) / 0.022);
                float headSwitch = foot * foot * (0.018 + 0.010 * Hash12(float2(field, 3.0)));
                float stat = 0.0;

                float track = _PSXCam0.w;
                float bandY = frac(t * 0.11 + 0.3);
                float band = track * saturate(1.0 - abs(wuv.y - bandY) / 0.045);
                wobble += band * (Hash12(float2(ln * 0.5, field + 7.0)) - 0.5) * 0.03;
                stat += band * step(0.82, Hash12(float2(floor(wuv.x * 160.0), ln + field * 13.0)));

                // The deck: a paused tape holds a field with a bar across its
                // foot and a twitch; a scan tears through noise bars.
                float vshift = 0.0;
                if (transport == 1)
                {
                    vshift = (Hash12(float2(field, 5.0)) > 0.5 ? 1.0 : -1.0) / 480.0;
                    float pb = saturate(1.0 - abs(wuv.y - 0.045) / 0.022);   // under the date stamp
                    wobble += pb * (Hash12(float2(ln, field + 2.0)) - 0.5) * 0.05;
                    stat += pb * step(0.55, Hash12(float2(floor(wuv.x * 220.0), ln + field)));
                }
                else if (transport == 2)
                {
                    float dir = _PSXCam1.w >= 0.0 ? 1.0 : -1.0;
                    for (int b = 0; b < 2; b++)
                    {
                        float by = frac(0.18 + b * 0.5 + t * 0.21 * dir);
                        float sb = saturate(1.0 - abs(wuv.y - by) / 0.06);
                        wobble += sb * (Hash12(float2(ln, field + b)) - 0.5) * 0.08;
                        stat += sb * step(0.45, Hash12(float2(floor(wuv.x * 260.0), ln * 3.0 + field)));
                    }
                    wobble += 0.004 * dir * (1.0 - wuv.y);
                }

                float2 suv = uv;
                suv.x += (wobble + headSwitch) * win.y;
                suv.y += vshift * win.w;

                // ---- luma: ~250 lines across the 4:3 frame, softer when the
                // focus hunts, smeared along a fast pan ----
                float px = win.y / 320.0;
                float defocus = saturate(_PSXCam0.y);
                float2 dh = float2(px * (0.8 + defocus * 2.0), 0) + float2(_PSXCam0.z * 0.5, 0);
                float2 dv = float2(0, px * _PSXCam1.x / win.y * defocus * 1.8 * win.w);
                float3 c0 = Tap(suv);
                float3 lum = c0 * 0.36
                           + (Tap(suv + dh) + Tap(suv - dh)) * 0.22
                           + (Tap(suv + dv) + Tap(suv - dv)) * 0.10;
                float Y = dot(lum, LumaW);

                // ---- chroma: a quarter of the luma's width, and late ----
                float cs = px * 3.2;
                float2 cuv = suv + float2(px * 1.6, 0);
                float3 chr = (Tap(cuv + float2(-1.5 * cs, 0)) + Tap(cuv + float2(-0.5 * cs, 0)) +
                              Tap(cuv + float2(0.5 * cs, 0)) + Tap(cuv + float2(1.5 * cs, 0))) * 0.25;
                float3 yiq = ToYIQ(chr);

                // ---- the eye: exposure lagging the scene, a little gain in
                // the dark, and the white balance it settled on ----
                float4 eye = LOAD_TEXTURE2D(_PSXCamEye, int2(0, 0));
                float adapted = max(eye.r * eye.r, 0.002);
                float nowL = max(eye.g * eye.g, 0.002);
                float transient = clamp(pow(nowL / adapted, 0.65), 0.45, 2.3);
                // GAIN UP in the dark (round 2: night mountain tapes were too
                // black to enjoy): up to 3.2x, and the grain comes up with it.
                float gain = clamp(pow(0.17 / adapted, 0.42), 1.0, 3.2);
                float exposure = transient * gain;
                Y *= exposure;
                yiq.yz *= exposure;

                // Highlights knee over and wash out; the blacks sit a little up.
                float knee = 0.72;
                float over = max(Y - knee, 0.0);
                float Yk = Y > knee ? knee + (1.0 - knee) * (1.0 - exp(-over / (1.0 - knee) * 1.6)) : Y;
                float wash = 1.0 - 0.65 * smoothstep(0.7, 1.15, Y);
                Y = 0.022 + Yk * 0.96;

                // Colour noise (VHS's slow horizontal streaks) and grain,
                // both up with the gain.
                float g = gain - 1.0;
                float2 cn = float2(Hash12(float2(floor(wuv.x * 60.0), ln * 0.5 + field * 1.3)),
                                   Hash12(float2(floor(wuv.x * 60.0) + 17.0, ln * 0.5 + field * 1.7))) - 0.5;
                yiq.yz = yiq.yz * 0.9 * wash + cn * (0.01 + 0.012 * min(g, 1.2));
                Y += (Hash12(uv * _PSXCam2.xy + frame * 1.37) - 0.5) * (0.024 + 0.022 * min(g, 1.0));

                float3 rgb = FromYIQ(float3(Y, yiq.yz));

                // White balance, from where the eye has settled (a third of
                // the way to neutral, so sodium stays orange and dusk blue).
                float3 shares = float3(eye.b, 1.0 - eye.b - eye.a, eye.a);
                float3 wb = pow((1.0 / 3.0) / max(shares, 0.06), 0.33);
                wb /= max(dot(wb, LumaW), 1e-3);
                rgb *= wb;

                // Between two recordings: snow, rolling, a ghost of the picture under it.
                if (_PSXCam3.x > 0.001)
                {
                    float snow = Hash12(float2(floor(wuv.x * 180.0), ln) + field * 7.31);
                    float roll = 0.5 + 0.5 * sin(wuv.y * 9.0 + t * 31.0);
                    float3 st3 = float3(snow, snow, snow) * (0.35 + 0.5 * roll) + 0.05;
                    rgb = lerp(rgb, st3, saturate(_PSXCam3.x) * 0.92);
                }

                // Static in the bars and the band.
                rgb = lerp(rgb, float3(0.85, 0.85, 0.85) * (0.6 + 0.4 * Hash12(uv * 731.0 + field)), saturate(stat));
                // The head switch's dirty grey edge.
                rgb = lerp(rgb, rgb * 0.6 + 0.08, foot * 0.6);

                // A soft scan at a line count that can show it.
                if (_PSXCam2.w > 0.5)
                {
                    float row = floor(uv.y * _PSXCam2.y);
                    rgb *= (fmod(row, 2.0) < 1.0) ? 1.0 : 0.955;
                }
                // The lens's own falloff.
                float2 vc = (wuv - 0.5) * float2(1.0, 0.85);
                rgb *= 1.0 - 0.22 * pow(saturate(dot(vc, vc) * 2.2), 1.4);

                return half4(saturate(rgb), 1);
            }
            ENDHLSL
        }

        // 2 - copy
        Pass
        {
            Name "Copy"
            ZWrite Off ZTest Always Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CopyFrag

            half4 CopyFrag (Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0);
            }
            ENDHLSL
        }
    }
}
