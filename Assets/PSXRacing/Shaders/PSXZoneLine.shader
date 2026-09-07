// The zone-boundary curtain: the translucent wall of light that stands across
// the road where one drivable area ends and the menu begins.
//
// A sibling of PSX/Glow, copied rather than branched, with exactly three
// differences, and every one of them is the reason it exists:
//
//   1. Blend SrcAlpha OneMinusSrcAlpha, not One One. The first line was
//      additive — nine glowing knots wearing the lamp shader — and additive
//      light can only BRIGHTEN. Over grey tarmac a knot read as a pale speck;
//      over the daylight sky and fog band it saturated to white and vanished;
//      over sunlit grass it lifted the green toward white instead of reading
//      as blue. Anything taller than the road silhouette (which a curtain is,
//      by definition) has to be able to DARKEN a bright sky, and only alpha
//      blending can.
//   2. The texture's RGB is read. PSX/Glow ignores it and takes only the mask
//      alpha, which is right for a lamp pool and wrong for a curtain that is
//      white at its base and blue above — the two colours live in the
//      texture, and _Color stays a tint hook at white.
//   3. The alpha is saturated after the strength multiply, because an
//      alpha-blended alpha over 1 is undefined where One One simply clamped.
//
// The vertex fog FADE is kept verbatim from PSX/Glow and is deliberately NOT
// PSX/LitTransparent's fog, which CLOSES the surface (alpha -> 1 at fogFar).
// That is right for a shop window seen through haze and wrong here: at Night
// (fogFar 190) it would stand an opaque fog-coloured wall across the street
// from 190 m out. This fades to nothing instead, and the opaque emissive posts
// and bar beside it — PSX/Lit with _Emission 1, fogged not faded — remain the
// far-distance cue.
//
// ZWrite off with the depth TEST on, as PSX/Glow: hidden by a hill or a
// building in front of it, never a hole in the car behind it. Cull Off because
// the same quad is seen from inside the zone on the way out and from outside
// on the way in.
Shader "PSX/ZoneLine"
{
    Properties
    {
        _MainTex ("Curtain", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Strength ("Strength", Range(0,4)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Strength;

            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed fade : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // Fade out into the fog band rather than tinting toward it —
                // the same fade PSX/Glow uses, for the reason above: a curtain
                // that closed to fog colour would be a wall at night.
                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fade = 1.0 - saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                return fixed4(t.rgb * _Color.rgb, saturate(t.a * _Strength * i.fade));
            }
            ENDCG
        }
    }
}
