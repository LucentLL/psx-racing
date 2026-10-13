// THE WORLD AROUND THE CAR, AS A PANORAMA (2026-10-12, the GT look plan:
// "reflections ... are the things I notice most"). CarReflections renders six
// 90-degree views from the player's roof into six small textures; this pass
// stitches them into one equirect, laid out exactly like the hour's sky
// panorama PSX/CarPaint already reads (PSXSkyReflect.cginc: u from
// atan2(z, x), v from asin(y)), so the paint samples the world the way it
// samples the sky - explicit LOD from the mips, no seam - and nothing about
// cubemap face conventions (which differ between the editor and WebGL) is
// ever relied on: each face's basis is the camera's own, passed in.
Shader "Hidden/PSX/EnvPano"
{
    Properties
    {
        _F0 ("+X", 2D) = "black" {}
        _F1 ("-X", 2D) = "black" {}
        _F2 ("+Y", 2D) = "black" {}
        _F3 ("-Y", 2D) = "black" {}
        _F4 ("+Z", 2D) = "black" {}
        _F5 ("-Z", 2D) = "black" {}
    }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _F0, _F1, _F2, _F3, _F4, _F5;
            // Each face camera's forward, right and up, world space.
            float4 _FaceF[6], _FaceR[6], _FaceU[6];

            float4 frag(v2f_img i) : SV_Target
            {
                float lon = (i.uv.x - 0.5) * 2.0 * UNITY_PI;
                float lat = (i.uv.y - 0.5) * UNITY_PI;
                float3 d = float3(cos(lat) * cos(lon), sin(lat), cos(lat) * sin(lon));
                int best = 0; float bd = -2.0;
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float dk = dot(d, _FaceF[k].xyz);
                    if (dk > bd) { bd = dk; best = k; }
                }
                float3 f = 0, r = 0, u = 0;
                [unroll] for (int j = 0; j < 6; j++)
                    if (j == best) { f = _FaceF[j].xyz; r = _FaceR[j].xyz; u = _FaceU[j].xyz; }
                float z = max(dot(d, f), 1e-4);
                float2 uv = saturate(0.5 + 0.5 * float2(dot(d, r), dot(d, u)) / z);
                float4 c;
                if (best == 0) c = tex2Dlod(_F0, float4(uv, 0, 0));
                else if (best == 1) c = tex2Dlod(_F1, float4(uv, 0, 0));
                else if (best == 2) c = tex2Dlod(_F2, float4(uv, 0, 0));
                else if (best == 3) c = tex2Dlod(_F3, float4(uv, 0, 0));
                else if (best == 4) c = tex2Dlod(_F4, float4(uv, 0, 0));
                else c = tex2Dlod(_F5, float4(uv, 0, 0));
                return float4(c.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
