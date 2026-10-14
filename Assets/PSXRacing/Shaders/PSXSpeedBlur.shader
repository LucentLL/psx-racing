// The sense-of-speed blur: a radial smear of the WORLD that grows with road
// speed, the way Need for Speed Carbon did it — and TUNNEL VISION with it: the
// clear part of the picture closes in on where the car is heading as the car
// gets faster, until at 140 mph only the road ahead and the player's own car
// are sharp and everything else is streaking past.
//
// Run by SpeedBlurFeature as a full-frame pass on the PSX camera's colour,
// AFTER the transparents (so tyre smoke, glass, lamp glows and skid marks
// smear with the world they belong to) and BEFORE the HUD camera draws, so
// the lap counter in the corner — where the smear is strongest — is never in
// it. It works on the low-res framebuffer, so the result goes through
// PSX/Blit's quantise and Bayer dither with the rest of the picture and reads
// as the game rather than as a modern post effect laid over it.
//
// The kernel is a ZOOM: every tap is the same pixel seen at a slightly
// different scale about the FOCUS — where the car is heading, as the lens
// sees it — so the smear length is a fraction of the distance from the focus
// and costs nothing to aim. It is CENTRED on the pixel (half the taps inward,
// half outward) so the picture does not appear to swell as the car
// accelerates. The tap offsets are jittered by the same 4x4 Bayer tile the
// blit dithers with: sixteen taps over a smear that is a hundred and sixty
// pixels long at 480 lines would otherwise print sixteen ghost copies of
// every lamp post, and ordered noise is the one kind this picture already
// has. Sixteen taps, sixteen jitter levels: between them every position along
// the smear is sampled by some pixel of each 4x4 tile.
//
// THE PLAYER'S CAR IS NEVER IN IT, either way round. It sits below the focus,
// and a tunnel that closes to a tenth of the frame closes straight over it.
// The frame is copied out with its alpha cleared (pass 2); the car's opaque
// renderers are drawn into that alpha (pass 1), depth-tested against the
// scene so a lamp post in front of the car still cuts it; and the smear
// (pass 0) both hands back every car pixel untouched — body, tail-light glow,
// the smoke in front of it, exactly as the camera drew them — and leaves the
// car out of every OTHER pixel's taps. That second half matters as much: a
// centred kernel gathers the car's colour into the road all round it, and
// the first build of this gave a pale car a glowing outline at 140 mph. The
// mask rides in the alpha of the texture the taps already read, so knowing
// it costs nothing.
//
// SPEED BLUR: MOTION (2026-10-13, pass 3) is the other look on the same row,
// after Touge Attack: no focus and no tunnel — each pixel smears along its OWN
// motion on the screen, rebuilt from the depth buffer and the lens's previous
// frame (MotionBlurFx, in SpeedBlur.cs, keeps it). Near roadside streaks, the
// sky stays sharp on a straight and slides sideways in a corner. The car mask
// and the Bayer-jittered, centred taps are the zoom's.
Shader "PSX/SpeedBlur"
{
    Properties
    {
        // The car mask's culling: both faces for the zoom (as it always was),
        // back faces culled for MOTION — see SpeedBlurFeature.AddMotion.
        [Enum(UnityEngine.Rendering.CullMode)] _PSXMaskCull ("Car mask cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // 0 — the smear. RGB only: the camera's own alpha is not this pass's
        // to change.
        Pass
        {
            Name "SpeedBlur"
            ZWrite Off ZTest Always Cull Off Blend Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x: smear length at the frame corner, as a fraction of the
            //    distance from the focus
            // y: radius inside which nothing smears (r = 1 at the corner)
            // z: radius at which the smear is full
            // w: frame aspect
            float4 _PSXSpeedBlur;
            // xy: the focus, in viewport units (0,0 bottom left)
            float4 _PSXSpeedBlurFocus;

            #define TAPS 16

            static const float bayer[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                // rgb: the frame. a: 1 where the player's car is.
                half4 here = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                if (here.a > 0.5) return here;

                // Distance from the focus measured on the SCREEN, not in the
                // stretched UV square, and normalised to the frame corner so
                // the clear zone is the same shape on a phone and a tablet.
                float2 focus = _PSXSpeedBlurFocus.xy;
                float2 d = uv - focus;
                float aspect = max(_PSXSpeedBlur.w, 0.01);
                float r = length(float2(d.x * aspect, d.y)) / (0.5 * sqrt(aspect * aspect + 1.0));
                float span = _PSXSpeedBlur.x * smoothstep(_PSXSpeedBlur.y, _PSXSpeedBlur.z, r);
                // Nothing to smear: hand back the pixel untouched, so the
                // tunnel is bit-for-bit what the camera drew.
                if (span < 0.0005) return here;

                float2 px = floor(input.positionCS.xy);
                int idx = (int)fmod(px.x, 4.0) + 4 * (int)fmod(px.y, 4.0);
                float jitter = (bayer[idx] + 0.5) / 16.0;

                half3 sum = 0;
                half weight = 0;
                for (int k = 0; k < TAPS; k++)
                {
                    float t = (k + jitter) / TAPS - 0.5;
                    float2 suv = saturate(focus + d * (1.0 + span * t));
                    half4 tap = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, suv, 0);
                    half w = 1.0 - tap.a;
                    sum += tap.rgb * w;
                    weight += w;
                }
                // Every tap on the car and this pixel not: a sliver between
                // two of its parts. It keeps what it had.
                return weight > 0.01 ? half4(sum / weight, 1) : here;
            }
            ENDHLSL
        }

        // 1 — the player's car, into the ALPHA of the copied frame. Drawn
        // with this material in place of the car's own, so it has to land on
        // the car's own depth: the offset pulls it a hair toward the lens,
        // because two vertex shaders that multiply the same matrices in a
        // different order do not agree to the last bit, and LEqual against
        // the car's own depth would speckle without it.
        Pass
        {
            Name "CarMask"
            ZWrite Off ZTest LEqual Cull [_PSXMaskCull] Blend Off
            ColorMask A
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 MaskVert (float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 MaskFrag () : SV_Target
            {
                return half4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // 2 — the frame, copied out with its alpha CLEARED, ready to be the
        // mask. Whatever the camera left in that channel (coverage, a glow's
        // blend factor) is not a statement about where the car is.
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
                half3 rgb = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
                return half4(rgb, 0);
            }
            ENDHLSL
        }

        // 3 — MOTION: the camera's own motion blur. RGB only, like pass 0.
        //
        // Each pixel's view-space position comes from the depth (the ray
        // through it at depth 1, times its linear depth); _PSXMotionReproj
        // takes it to where the PREVIOUS frame's lens saw it, and the
        // difference is its motion. The sky (the far plane: nothing drawn
        // there) is at infinity and only turning moves it, so it goes through
        // _PSXMotionRot, the same matrix with both lenses' positions taken
        // out. The motion is scaled to a 1/60 s shutter (x), clamped to y of
        // the frame width, and sampled with the zoom's centred, Bayer-jittered
        // taps. Car pixels are handed back untouched and car taps are dropped.
        Pass
        {
            Name "MotionBlur"
            ZWrite Off ZTest Always Cull Off Blend Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // This view space -> the previous frame's clip space; and the
            // same with no translation (the sky's).
            float4x4 _PSXMotionReproj;
            float4x4 _PSXMotionRot;
            // m00, m11, m02, m12 of THIS frame's (OpenGL-style) projection:
            // view space at depth 1 is ((ndc.x + m02) / m00, (ndc.y + m12) / m11, -1).
            float4 _PSXMotionProj;
            // x: exposure, frame motion -> smear (shutter x (1/60 s) / dt)
            // y: the longest smear, as a fraction of the frame width
            // z: frame aspect (width / height)
            // w: frame width in pixels
            float4 _PSXMotionParams;
            // x: taps; y: 1 = write the smear as a colour (the check tools)
            float4 _PSXMotionMisc;
            TEXTURE2D_X_FLOAT(_PSXMotionDepth);

            static const float bayer[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                // rgb: the frame. a: 1 where the player's car is.
                half4 here = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                bool debug = _PSXMotionMisc.y > 0.5;
                if (here.a > 0.5 && !debug) return here;

                float raw = SAMPLE_TEXTURE2D_X_LOD(_PSXMotionDepth, sampler_PointClamp, uv, 0).r;
                #if UNITY_REVERSED_Z
                    bool sky = raw <= 1e-7;
                #else
                    bool sky = raw >= 1.0 - 1e-7;
                #endif
                float2 ndc = uv * 2.0 - 1.0;
                float3 ray = float3((ndc.x + _PSXMotionProj.z) / _PSXMotionProj.x,
                                    (ndc.y + _PSXMotionProj.w) / _PSXMotionProj.y, -1.0);
                float depth = sky ? 0.0 : LinearEyeDepth(raw, _ZBufferParams);
                float4 prev = sky ? mul(_PSXMotionRot, float4(ray, 1.0))
                                  : mul(_PSXMotionReproj, float4(ray * depth, 1.0));
                // Behind last frame's lens: there is no meaningful motion
                // to read, so it gets the turn alone.
                if (prev.w < 1e-4) prev = mul(_PSXMotionRot, float4(ray, 1.0));
                float2 prevUv = prev.xy / max(prev.w, 1e-4) * 0.5 + 0.5;

                float aspect = max(_PSXMotionParams.z, 0.01);
                float2 v = (uv - prevUv) * _PSXMotionParams.x;
                // Measured in frame WIDTHS, both axes alike.
                float len = length(float2(v.x, v.y / aspect));
                if (len > _PSXMotionParams.y) v *= _PSXMotionParams.y / len;
                float lenPx = min(len, _PSXMotionParams.y) * _PSXMotionParams.w;

                if (debug)
                {
                    // The smear in pixels, 0.5 = none, one level per pixel
                    // over +-128; blue = depth in metres (255 = 255 m or sky).
                    float2 vPx = v * float2(_PSXMotionParams.w, _PSXMotionParams.w / aspect);
                    return half4(saturate(0.5 + vPx / 256.0), sky ? 1.0 : saturate(depth / 255.0), 1);
                }
                // Under half a pixel of smear: hand the pixel back untouched,
                // so a still sky is bit-for-bit what the camera drew.
                if (lenPx < 0.5) return here;

                float2 px = floor(input.positionCS.xy);
                int idx = (int)fmod(px.x, 4.0) + 4 * (int)fmod(px.y, 4.0);
                float jitter = (bayer[idx] + 0.5) / 16.0;

                int taps = (int)max(_PSXMotionMisc.x, 2.0);
                half3 sum = 0;
                half weight = 0;
                UNITY_LOOP
                for (int k = 0; k < taps; k++)
                {
                    float t = (k + jitter) / taps - 0.5;
                    half4 tap = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, saturate(uv + v * t), 0);
                    // The player's car is not part of anyone else's smear.
                    half w = 1.0 - tap.a;
                    sum += tap.rgb * w;
                    weight += w;
                }
                return weight > 0.01 ? half4(sum / weight, 1) : here;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
