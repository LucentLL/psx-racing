// Simple multiplicative blob shadow quad (the classic PSX car shadow).
Shader "PSX/Shadow"
{
    Properties
    {
        _MainTex ("Shadow Mask", 2D) = "white" {}
        _Strength ("Strength", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" }
        Pass
        {
            // Darkens the colour; the framebuffer's ALPHA is kept (Zero One):
            // it is the emitter mask (PSXTone.cginc), and a blend like the
            // colour's would have written the shadow's own a*a into it.
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Strength;
            // How much of the blob goes in full sun (SunShadows.Cars.cs): the
            // car's own shadow is the shadow then, and what is left is the
            // contact darkening under the body. 0 - never set, every scene
            // with no hour, dusk, night - is the blob as it always was.
            float _PSXBlobFade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a * _Strength * (1.0 - saturate(_PSXBlobFade));
                return fixed4(0, 0, 0, a);
            }
            ENDCG
        }
    }
}
