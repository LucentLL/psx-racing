// Final upscale shader for the 320x240 render target:
// 15-bit color quantization with a 4x4 Bayer dither, like PS1 output.
Shader "PSX/Blit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ColorDepth ("Bits per channel", Range(3,8)) = 5
        _DitherStrength ("Dither", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _ColorDepth;
            float _DitherStrength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            static const float bayer[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Dither in source-pixel space so the pattern is 1:1 with the low-res buffer
                float2 srcPixel = floor(i.uv * _MainTex_TexelSize.zw);
                int idx = (int)(fmod(srcPixel.x, 4.0)) + 4 * (int)(fmod(srcPixel.y, 4.0));
                float threshold = (bayer[idx] + 0.5) / 16.0 - 0.5;

                float levels = pow(2.0, _ColorDepth) - 1.0;
                col.rgb += threshold * (_DitherStrength / levels);
                col.rgb = floor(col.rgb * levels + 0.5) / levels;
                return fixed4(col.rgb, 1);
            }
            ENDCG
        }
    }
}
