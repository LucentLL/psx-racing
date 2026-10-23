// THE WORLD AROUND THE CAR, AS A PANORAMA (2026-10-12, the GT look plan:
// "reflections ... are the things I notice most"). CarReflections renders six
// 90-degree views from the player's roof into six small textures; this pass
// stitches them into one equirect, laid out exactly like the hour's sky
// panorama PSX/CarPaint already reads (PSXSkyReflect.cginc: u from
// atan2(z, x), v from asin(y)), so the paint samples the world the way it
// samples the sky - explicit LOD from the mips, no seam - and nothing about
// cubemap face conventions (which differ between the editor and WebGL) is
// ever relied on: each face's basis is the camera's own, passed in.
//
// ONE FACE A PASS (2026-10-23; the owner, a fourth time: the reflections move
// "horizontally (right to left) across the car instead of front to back").
// The stitch used to be one pass reading all six faces - the face picked
// through uniform ARRAYS (_FaceF[6]...) and six samplers behind a branch -
// and in the WebGL player (ReflTour, ANGLE over D3D11) that pass drew the +X
// face where +X belongs AND where -Z belongs and black everywhere else, while
// all six faces themselves were right: the paint reflected one sideways view
// of the world, sliding across the body as the car drove. The editor (GL Core,
// D3D11) never showed it. Now CarReflections draws this pass six times, one
// face each: one sampler, plain vectors, and a clip to the texels whose
// largest axis is this face's.
Shader "Hidden/PSX/EnvPano"
{
    Properties
    {
        _Face ("Face", 2D) = "black" {}
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

            sampler2D _Face;
            float4 _FaceF, _FaceR, _FaceU;   // this face's camera basis

            float4 frag(v2f_img i) : SV_Target
            {
                float lon = (i.uv.x - 0.5) * 2.0 * UNITY_PI;
                float lat = (i.uv.y - 0.5) * UNITY_PI;
                float3 d = float3(cos(lat) * cos(lon), sin(lat), cos(lat) * sin(lon));
                // only where this face's axis is the direction's largest
                float3 a = abs(d);
                float z = dot(d, _FaceF.xyz);
                clip(z - max(a.x, max(a.y, a.z)) + 1e-4);
                float2 uv = saturate(0.5 + 0.5 * float2(dot(d, _FaceR.xyz), dot(d, _FaceU.xyz)) / max(z, 1e-4));
                return float4(tex2Dlod(_Face, float4(uv, 0, 0)).rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
