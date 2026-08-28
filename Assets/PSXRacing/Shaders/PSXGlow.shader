// Additive glow quad: headlight lenses, the light pool they throw on the road,
// tail lights, and street lamps after dark.
//
// PSX/Lit is opaque-or-cutout by design, so there was no way to add light to
// what is already drawn — a "headlight" made from it would be a white rectangle
// stuck to the bumper. Additive blending is also what the era actually did:
// a PS1 headlight was a bright sprite and a stretched pool on the tarmac, not a
// light source, and it reads correctly precisely because it never shadows.
//
// ZWrite off with the depth TEST still on, so a glow is hidden by the wall in
// front of it but never punches a hole in the car behind it.
Shader "PSX/Glow"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Strength ("Strength", Range(0,4)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            // One One, with the colour already multiplied by the mask in the
            // fragment. SrcAlpha One would apply the mask a second time and
            // square the falloff, which turns a soft pool into a hard dot.
            Blend One One
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
                // Fade out into the fog band rather than tinting toward it:
                // adding fog colour to an additive pass would make distant
                // lamps BRIGHTER in daylight fog, which is backwards.
                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fade = 1.0 - saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a * _Strength * i.fade;
                return fixed4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
}
