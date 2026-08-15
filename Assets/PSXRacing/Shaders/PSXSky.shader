// Simple dusk gradient skybox for the PSX look.
Shader "PSX/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.18, 0.16, 0.38, 1)
        _HorizonColor ("Horizon", Color) = (0.95, 0.60, 0.40, 1)
        _BottomColor ("Bottom", Color) = (0.25, 0.20, 0.22, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(1, 16)) = 5
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

            fixed4 frag (v2f i) : SV_Target
            {
                float y = normalize(i.viewDir).y;
                float above = pow(saturate(y), 1.0 / _HorizonSharpness * 4.0);
                float below = pow(saturate(-y), 0.35);
                fixed3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, above);
                col = lerp(col, _BottomColor.rgb, below);
                // Quantize slightly for banding, a PSX skybox signature
                col = floor(col * 31.0 + 0.5) / 31.0;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
