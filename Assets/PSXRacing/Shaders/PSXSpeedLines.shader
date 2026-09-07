// Radial speed streaks for the HUD canvas.
//
// The texture is POLAR: u runs round the frame (angle), v runs out from the
// centre (radius). A dash drawn as a short vertical bar in the texture is a
// radial streak on screen, and scrolling v moves every streak outward at once
// — that is the whole trick, and it costs one texture fetch per pixel of a
// 240-line frame. Sampled with point filtering it stays hard-edged, and
// because the RawImage carrying it sits on the ScreenSpaceCamera HUD canvas it
// is rasterised INSIDE the low-res target and dithered by PSX/Blit with the
// rest of the picture, rather than drawn over it as crisp modern post.
//
// The streaks live in a ring: nothing inside _InnerR so the road ahead — the
// part of the frame the player steers by — is never covered, ramping to full
// at _OuterR, which is measured against the FRAME CORNER (r = 1 there) so the
// ring's shape follows the screen, not a circle inscribed in it.
//
// ZTest Always for the same reason HudOnTop exists: the canvas plane is a
// metre in front of the lens and a bonnet is closer than that.
Shader "PSX/SpeedLines"
{
    Properties
    {
        _MainTex ("Streaks (u = angle, v = radius)", 2D) = "black" {}
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Scroll ("Scroll (radial repeats)", Float) = 0
        _Aspect ("Frame aspect", Float) = 1.7778
        _Spokes ("Angular repeats", Float) = 3
        _Repeat ("Radial repeats", Float) = 3
        _InnerR ("Inner radius", Range(0, 1)) = 0.45
        _OuterR ("Outer radius", Range(0, 1)) = 0.95
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "RenderType"="Transparent"
            "IgnoreProjector"="True" "PreviewType"="Plane"
        }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;
            float _Scroll;
            float _Aspect;
            float _Spokes;
            float _Repeat;
            float _InnerR;
            float _OuterR;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Centre the frame and undo the aspect, so "radius" is a
                // distance on screen rather than in a stretched UV square.
                float2 d = i.uv - 0.5;
                d.x *= _Aspect;
                // r = 1 at the frame corner.
                float r = length(d) / (0.5 * sqrt(_Aspect * _Aspect + 1.0));
                float a = atan2(d.y, d.x) / 6.2831853 + 0.5;
                float m = tex2D(_MainTex, float2(a * _Spokes, r * _Repeat - _Scroll)).r;
                float ring = smoothstep(_InnerR, _OuterR, r);
                return fixed4(1.0, 1.0, 1.0, m * ring * _Intensity * i.color.a);
            }
            ENDCG
        }
    }
}
