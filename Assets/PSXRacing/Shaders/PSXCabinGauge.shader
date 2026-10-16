// THE v08 COCKPIT'S FOUR GAUGES, and its rear-view mirror (2026-10-16).
//
// Owner: the faces "should remain blank to be drawn in game". Each blank
// face (a disc whose UV0 is the full 0..1 square, U to the driver's right,
// V up) gets its own material: _FaceTex is the dial PAINTED ONCE for the car
// (CockpitGauges.cs: ticks, numerals, the red zone, the icon, the LCD slot),
// and everything that moves is drawn HERE from uniforms - no texture is
// written per frame:
//   _Needle  x = needle angle, radians clockwise from straight up
//            y = needle length (share of the face's radius), z = tail length,
//            w = half-width at the hub
//   _Gear    the LCD under the tach: -1 R, 0 N, 1..9 a digit, 99 blank
//   _Glow    the backlight: 1 with the lights on, DAY_GLOW by day
// The face is self-lit (a backlit dial reads in the cabin's shade by day and
// glows at night), toned with the exposure like everything else.
//
// _Mode 1 = the MIRROR face: _FaceTex is the mirror camera's render texture,
// U flipped as the variant needs (a mirror shows the scene the right way
// round only because the camera looking back is reversed).
Shader "PSX/CabinGauge"
{
    Properties
    {
        _FaceTex ("Dial face", 2D) = "black" {}
        _Needle ("Needle (angle, length, tail, half-width)", Vector) = (0, 0.82, 0.16, 0.022)
        _NeedleColor ("Needle colour", Color) = (1, 0.18, 0.05, 1)
        _Gear ("Gear (-1 R, 0 N, 99 off)", Float) = 99
        _Lcd ("LCD rect (centre uv, half size)", Vector) = (0.5, 0.27, 0.075, 0.06)
        _Glow ("Backlight", Range(0, 1)) = 1
        _FlipU ("Flip U", Float) = 0
        _Mode ("Mode (0 gauge, 1 mirror)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PSXTone.cginc"

            sampler2D _FaceTex;
            float4 _Needle;
            fixed4 _NeedleColor;
            float _Gear;
            float4 _Lcd;
            float _Glow;
            float _FlipU;
            float _Mode;
            float _PSXNight;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                if (_FlipU > 0.5) o.uv.x = 1.0 - o.uv.x;
                return o;
            }

            // Coverage of a box (half size h, centre c) at p, softened by w.
            float Box(float2 p, float2 c, float2 h, float w)
            {
                float2 d = abs(p - c) - h;
                float s = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
                return saturate(0.5 - s / w);
            }

            // A seven-segment glyph in a unit cell (x 0..1, y 0..1): segments
            // a..g on bits 0..6.
            float Seg7(float2 p, float bits, float w)
            {
                const float t = 0.09, hx = 0.30, hy = 0.21;
                float c = 0.0;
                float b = bits;
                // a (top), b (top right), c (bottom right), d (bottom), e (bottom left), f (top left), g (middle)
                float2 ctr[7] = { float2(0.5, 0.92), float2(0.86, 0.71), float2(0.86, 0.29), float2(0.5, 0.08),
                                  float2(0.14, 0.29), float2(0.14, 0.71), float2(0.5, 0.5) };
                float2 hs[7] = { float2(hx, t), float2(t, hy), float2(t, hy), float2(hx, t), float2(t, hy), float2(t, hy), float2(hx, t) };
                [unroll] for (int k = 0; k < 7; k++)
                {
                    float on = fmod(floor(b / exp2(k)), 2.0);
                    c = max(c, on * Box(p, ctr[k], hs[k], w));
                }
                return c;
            }

            float GearBits(float g)
            {
                // R as a lower-case r (e, g), N as n (c, e, g); digits 1..9.
                if (g < -0.5) return 80.0;
                if (g < 0.5) return 84.0;
                float d = floor(g + 0.5);
                return d < 1.5 ? 6.0 : d < 2.5 ? 91.0 : d < 3.5 ? 79.0 : d < 4.5 ? 102.0 : d < 5.5 ? 109.0
                     : d < 6.5 ? 125.0 : d < 7.5 ? 7.0 : d < 8.5 ? 127.0 : 111.0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                if (_Mode > 0.5)
                {
                    float3 m = tex2D(_FaceTex, uv).rgb * 0.82;
                    return fixed4(m, 0.0);
                }
                float3 face = tex2D(_FaceTex, uv).rgb;
                float px = max(fwidth(uv.x), 1e-4);
                float2 p = uv - 0.5;

                // The LCD gear digit, dark segments on the lit pane.
                if (_Gear < 50.0)
                {
                    float2 q = (uv - (_Lcd.xy - _Lcd.zw)) / (2.0 * _Lcd.zw);
                    float2 cell = (q - float2(0.28, 0.12)) / float2(0.44, 0.76);
                    if (all(cell >= -0.1) && all(cell <= 1.1))
                    {
                        float seg = Seg7(cell, GearBits(_Gear), px * 3.0);
                        face = lerp(face, float3(0.05, 0.06, 0.05), seg);
                    }
                }

                // The needle: a tapered blade from the tail through the hub to
                // the tip, then the black cap over the hub.
                float a = _Needle.x;
                float2 dir = float2(sin(a), cos(a));
                float2 nrm = float2(dir.y, -dir.x);
                float along = dot(p, dir) / 0.5;
                float across = abs(dot(p, nrm)) / 0.5;
                float len = _Needle.y, tail = _Needle.z, hw = _Needle.w;
                float taper = along > 0.0 ? lerp(hw, hw * 0.28, saturate(along / len)) : hw;
                float inBlade = step(-tail, along) * step(along, len);
                float blade = inBlade * saturate((taper - across) / (px * 2.0) + 0.5);
                float r = length(p) / 0.5;
                float cap = saturate((0.13 - r) / (px * 2.0) + 0.5);
                float3 col = lerp(face, _NeedleColor.rgb, blade);
                col = lerp(col, float3(0.012, 0.012, 0.014), cap);
                col *= _Glow * PSXAdaptGain();
                return fixed4(PSXTone(col), 0.0);
            }
            ENDCG
        }
    }
}
