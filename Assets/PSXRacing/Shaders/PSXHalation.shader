// THE HALATION'S BLUR (2026-10-16). PSX/Blit glows round lights: whatever is
// brighter than the knee bleeds, warm, weighted by the EMITTER MASK in the
// framebuffer's alpha (see the header there). It used to gather that glow
// itself, per screen pixel, from sixteen taps on two rings (0.011 and 0.026
// of the picture's height) turned by the Bayer cell. A small, very bright
// emitter close to the lens - an R32's braking lamp, its owner-authored disc
// clipping at 1.8x - then printed a ring of faint COPIES of itself round the
// real one: a sparse kernel shows its taps whenever the thing it blurs is
// smaller than the gaps between them. The owner: "Brake-light halo: this
// should be corrected ... All cars will get the similar texture overhaul".
//
// Now the glow is a real blur, built once per frame in a small pyramid
// (Halation.cs runs these passes) and read by PSX/Blit with one tap:
//
//   0  PREFILTER  the framebuffer's excess over the knee, times the emitter
//                 mask, averaged over each f x f block of source pixels
//                 (f = 1..4: about 120 lines whatever the picture's height).
//                 Taken per source pixel before any averaging, exactly as the
//                 old taps took it, so a 4-pixel lamp still counts in full.
//   1  BLUR       nine taps of a Gaussian along one axis (bilinear, so a
//                 step of two texels reading a finer level is also a 2x2
//                 box down). With _PSXHalMix.y it also adds _PSXHalAux, the
//                 coarser level, in the last pass.
//
// The kernel keeps the old one's energy where it was: 62.5% of it in a
// Gaussian as wide (RMS) as the inner ring, 37.5% as wide as the outer one -
// the old taps' weights, 8 x 1 and 8 x 0.6 of 12.8 - and it is normalised,
// so a lit area glows exactly as much as before and only the shape round a
// small one changes: a smooth round halo, no copies.
Shader "Hidden/PSX/Halation"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        // the prefilter's source: 1/width, 1/height, width, height (set by
        // Halation.cs; a command-buffer blit of a render target is not
        // promised to fill _MainTex_TexelSize)
        float4 _PSXHalSrc;
        // The coarser level, added in by the last blur.
        sampler2D _PSXHalAux;
        // xy: one tap's step in the source's uv; z: the Gaussian's sigma, in taps
        float4 _PSXHalStep;
        // x: the weight of this blur; y: the weight of _PSXHalAux
        float4 _PSXHalMix;
        // the prefilter's block: source pixels per glow pixel, 1..4
        float _PSXHalFactor;
        // 1 = glow by the emitter mask (alpha); PSXGlobals / TimeOfDay set it
        float _PSXEmitKey;

        // PSX/Blit's knee (keep the two together).
        #define GLOW_KNEE_GAMMA   0.78
        #define GLOW_KNEE_LINEAR  0.57

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        v2f vert (appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }
        ENDCG

        // 0 - PREFILTER
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment Prefilter
            #pragma target 3.0

            float4 Prefilter (v2f i) : SV_Target
            {
                float f = clamp(floor(_PSXHalFactor + 0.5), 1.0, 4.0);
                float2 px = _PSXHalSrc.xy;
                // The block's first source pixel: glow texel g covers source
                // pixels g*f .. g*f + f - 1.
                float2 base = floor(i.uv * _PSXHalSrc.zw - 0.5 * f + 0.5);
                float key = saturate(_PSXEmitKey);
                float3 sum = 0;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        if (x < f && y < f)
                        {
                            float4 s = tex2Dlod(_MainTex, float4((base + float2(x, y) + 0.5) * px, 0, 0));
                            // lerp(1, a, 0) is exactly 1: the old rule with the key off.
                            float w = lerp(1.0, s.a, key);
                            #ifndef UNITY_COLORSPACE_GAMMA
                            sum += max(s.rgb - GLOW_KNEE_LINEAR, 0.0) * w;
                            #else
                            sum += max(s.rgb - GLOW_KNEE_GAMMA, 0.0) * w;
                            #endif
                        }
                    }
                }
                return float4(sum / (f * f), 1);
            }
            ENDCG
        }

        // 1 - BLUR (one axis), and the last one adds the coarser level
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment Blur
            #pragma target 3.0

            float4 Blur (v2f i) : SV_Target
            {
                float sigma = max(_PSXHalStep.z, 0.05);
                float k2 = -0.5 / (sigma * sigma);
                float3 sum = 0;
                float wsum = 0;
                for (int k = -4; k <= 4; k++)
                {
                    float w = exp(k * k * k2);
                    sum += tex2Dlod(_MainTex, float4(i.uv + k * _PSXHalStep.xy, 0, 0)).rgb * w;
                    wsum += w;
                }
                float3 c = sum / wsum * _PSXHalMix.x;
                if (_PSXHalMix.y > 0.0) c += tex2Dlod(_PSXHalAux, float4(i.uv, 0, 0)).rgb * _PSXHalMix.y;
                return float4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
