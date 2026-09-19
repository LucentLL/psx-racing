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
Shader "PSX/SpeedBlur"
{
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
            ZWrite Off ZTest LEqual Cull Off Blend Off
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
    }
    Fallback Off
}
