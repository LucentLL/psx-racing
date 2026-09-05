// The sky, and the one thing in this game allowed to look better than the rest
// of it.
//
// That is not a slip in the art direction, it is the art direction: a PS1 or
// N64 game spent almost nothing on its skybox at runtime (a handful of polygons
// at infinity, no lighting, no z, no overdraw worth the name) and so could
// afford to hang a photograph up there while the road underneath it was a 64 px
// tile. Ridge Racer, Gran Turismo and Wave Race all look like this. The owner's
// words: "skyboxes and distant art around maps on PSX and N64 normally looked
// better than the 3D textures... part of driving is enjoying the view."
//
// So this samples a real equirectangular sky, and everything else in here is
// about making a photograph belong to a scene that is not one:
//
//   * IT WEARS THE HOUR'S COLOURS. _Tint pulls the photo toward the same three
//     gradient stops this shader used to be built from, so the seven hours are
//     still seven hours rather than seven photographs.
//   * IT MEETS THE FOG. The band either side of the horizon fades to
//     _HorizonColor, which the hour table keeps in step with the fog colour.
//     Without it the world ends at a hard line where the terrain stops.
//   * IT TURNS TO FACE THE SUN. _Rotation spins the panorama about Y so its
//     baked sun lands where the scene's directional light actually is.
//     TimeOfDay computes it FROM the light, so the two cannot drift apart.
//   * IT DOES NOT QUANTIZE. PSXBlit already does the 15-bit cut with a 4x4
//     Bayer dither over the whole frame, which is what a PS1 did and is why its
//     gradients did not band. The `floor(col * 31) / 31` that used to be at the
//     bottom of this file was a SECOND quantize with no dither: invisible on a
//     flat gradient, and tree rings on a photographed sky.
//
// With no panorama assigned it is the old three-stop gradient, exactly.
Shader "PSX/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.18, 0.16, 0.38, 1)
        _HorizonColor ("Horizon", Color) = (0.95, 0.60, 0.40, 1)
        _BottomColor ("Bottom", Color) = (0.25, 0.20, 0.22, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(1, 16)) = 5
        [NoScaleOffset] _MainTex ("Panorama (equirect)", 2D) = "white" {}
        _PanoAmount ("Panorama Amount", Range(0, 1)) = 0
        _Rotation ("Rotation (deg)", Float) = 0
        _Tint ("Hour Tint", Range(0, 1)) = 0.55
        _Exposure ("Exposure", Range(0.1, 3)) = 1
        _HorizonFade ("Horizon Fade", Range(0.005, 0.5)) = 0.10
        _Stars ("Stars", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor;
            fixed4 _HorizonColor;
            fixed4 _BottomColor;
            float _HorizonSharpness;
            sampler2D _MainTex;
            float _PanoAmount;
            float _Rotation;
            float _Tint;
            float _Exposure;
            float _HorizonFade;
            float _Stars;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.viewDir = v.vertex.xyz;
                return o;
            }

            // One star field, cheap: chop the sphere into cells and put at most
            // one point in each, at a hashed offset inside it. Sizing the cells
            // off the DIRECTION rather than off screen space is what nails a
            // star to the sky while the car turns underneath it.
            float StarField(float3 dir)
            {
                float3 p = dir * 190.0;
                float3 cell = floor(p);
                float3 f = p - cell;
                float h = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                // Most cells are empty. A star in one cell in fourteen is a
                // country sky; a star in every cell is white noise.
                if (h > 0.072) return 0.0;
                float3 at = float3(frac(h * 137.13), frac(h * 311.7), frac(h * 71.9));
                float d = length(f - at);
                // Magnitude varies per star, and the faint ones are most of them.
                float mag = 0.35 + frac(h * 953.7) * 0.65;
                return saturate(1.0 - d * 6.0) * mag;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 dir = normalize(i.viewDir);
                float y = dir.y;

                // The three-stop gradient. Still the whole shader when no
                // panorama is assigned, and the tint target when one is.
                float above = pow(saturate(y), 1.0 / _HorizonSharpness * 4.0);
                float below = pow(saturate(-y), 0.35);
                fixed3 grad = lerp(_HorizonColor.rgb, _TopColor.rgb, above);
                grad = lerp(grad, _BottomColor.rgb, below);

                fixed3 col = grad;
                if (_PanoAmount > 0.001)
                {
                    // Equirectangular lookup. u wraps with azimuth; v is the
                    // SINE of the elevation, so the photographed horizon lands
                    // at exactly v = 0.5 whatever the camera is doing.
                    float u = atan2(dir.z, dir.x) * (0.5 / UNITY_PI) + 0.5 + _Rotation / 360.0;
                    float v = 0.5 + asin(clamp(y, -1.0, 1.0)) / UNITY_PI;
                    // Sampled with EXPLICIT gradients, because u is built from
                    // atan2 and therefore jumps from 1 back to 0 down one
                    // meridian of the sky. The hardware reads that jump as a
                    // whole texture's worth of detail in one pixel, picks the
                    // smallest mip, and draws a blurred vertical stripe from
                    // horizon to zenith — the classic equirect seam, and it
                    // lands in a different place every time the panorama is
                    // rotated. Subtracting the wrap turns the spike back into
                    // the real one-texel step it is.
                    float2 uv = float2(u, v);
                    float2 gx = ddx(uv), gy = ddy(uv);
                    if (abs(gx.x) > 0.5) gx.x -= sign(gx.x);
                    if (abs(gy.x) > 0.5) gy.x -= sign(gy.x);
                    fixed3 pano = tex2Dgrad(_MainTex, uv, gx, gy).rgb * _Exposure;

                    // Wear the hour. The photograph keeps its structure and its
                    // luminance; the palette supplies the colour. Times two
                    // because a mid-grey palette stop would otherwise halve the
                    // brightness of the sky every time the tint went up.
                    fixed3 tinted = pano * grad * 2.0;
                    pano = lerp(pano, tinted, _Tint);

                    // Stars go UNDER the cloud, not over it: scaling them by how
                    // dark the photograph is here means a moonlit bank of cloud
                    // occludes them, which is most of why a real night sky reads
                    // as having depth at all. Added AFTER the tint, because the
                    // tint is what darkens the night sky and a star it dimmed
                    // with everything else would be no star.
                    if (_Stars > 0.001)
                    {
                        float lum = dot(pano, float3(0.299, 0.587, 0.114));
                        float clear = saturate(1.0 - lum * 3.2);
                        // None below the horizon, and none in the haze just
                        // above it, where a real star is extinguished.
                        float high = saturate((y - 0.04) * 5.0);
                        pano += StarField(dir) * _Stars * clear * high;
                    }


                    col = lerp(grad, pano, _PanoAmount);
                }

                // Into the fog at the horizon, from both sides. This is what
                // stops the terrain ending at a visible line: the last band of
                // sky IS the fog colour, so the two meet in the same paint.
                float hz = saturate(1.0 - abs(y) / _HorizonFade);
                col = lerp(col, _HorizonColor.rgb, hz * hz);
                // And below is the ground colour, not a mirror of the sky —
                // these panoramas render the lower hemisphere as a reflection,
                // which seen from a bridge deck is a lake hanging in the air.
                col = lerp(col, _BottomColor.rgb, saturate(-y * 3.0 - 0.15));

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
