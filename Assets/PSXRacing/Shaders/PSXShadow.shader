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
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Strength;

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
                fixed a = tex2D(_MainTex, i.uv).a * _Strength;
                return fixed4(0, 0, 0, a);
            }
            ENDCG
        }
    }
}
