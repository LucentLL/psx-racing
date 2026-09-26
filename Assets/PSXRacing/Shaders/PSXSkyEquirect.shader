// The DYNAMIC sky (PSXAtmosphere.cginc) laid out as an equirectangular panorama,
// 256 x 128, re-rendered every few seconds by DynamicSky so everything that
// reflects the sky through PSXSkyIn - the car paint, the wet road, the sea -
// reflects this sky, clouds and all, the way it reflects a photograph. The
// layout is the one PSXSkyIn reads at rotation 0:
//   u = atan2(z, x) / 2pi + 0.5,  v = 0.5 + asin(y) / pi.
Shader "Hidden/PSX/SkyEquirect"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PSXAtmosphere.cginc"

            fixed4 frag (v2f_img i) : SV_Target
            {
                float az = (i.uv.x - 0.5) * 6.2831853;
                float el = (i.uv.y - 0.5) * 3.14159265;
                float3 dir = float3(cos(el) * cos(az), sin(el), cos(el) * sin(az));
                return fixed4(PSXDynamicSky(dir), 1);
            }
            ENDCG
        }
    }
}
