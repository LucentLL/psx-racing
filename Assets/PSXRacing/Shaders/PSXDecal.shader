// Unlit, vertex-coloured, alpha-blended geometry: the tyre marks laid on the
// road and the smoke that comes off the tyres laying them.
//
// One shader for both, because they are the same drawing problem — a mesh
// built in world space every frame, tinted per vertex, with no lighting and
// no depth write. What separates them is set on the MATERIAL, not here:
//
//   marks  queue Transparent-200 (under the blob shadow, which is -100),
//          _Tint near-black, and the polygon offset below keeps them out of a
//          z-fight with the road surface they are one millimetre above.
//   smoke  queue Transparent, _Tint white, offset harmless on a billboard.
//
// PSX/Lit is opaque-or-cutout and PSX/Glow is additive, so neither could draw
// a soft grey puff or darken tarmac without lighting it up.
//
// Fog is a TINT toward the fog colour and not a fade to nothing, which is the
// same treatment PSX/Lit gives every surface: at full fog the road has become
// fog colour, and a mark on it must become fog colour too or it stays as a
// black line drawn across a wall of haze.
Shader "PSX/Decal"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            // Toward the camera in depth only. A decal sitting a millimetre
            // above the tarmac still z-fights it at 300 m, where a millimetre
            // is well under one step of the depth buffer.
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;

            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            float _PSXSnap;

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
                fixed4 color : COLOR0;
                fixed fog : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 clipPos = UnityObjectToClipPos(v.vertex);

                // The same NDC quantisation every other surface in the scene
                // gets. Without it a mark slides smoothly over a road that is
                // stepping, which reads as the mark floating above it.
                if (_PSXSnap > 0.5 && clipPos.w > 0.0)
                {
                    float2 grid = _ScreenParams.xy * 0.5;
                    float2 ndc = clipPos.xy / clipPos.w;
                    ndc = floor(ndc * grid + 0.5) / grid;
                    clipPos.xy = ndc * clipPos.w;
                }
                o.pos = clipPos;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Tint;

                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fog = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 c = tex * i.color;
                c.rgb = lerp(c.rgb, _PSXFogColor.rgb, i.fog);
                return c;
            }
            ENDCG
        }
    }
}
