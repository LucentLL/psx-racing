// THE VOLUMETRIC CLOUDS' DOME MAP (PSXVolClouds.cginc; VolumetricClouds.cs
// drives it, one Blit pair a frame from DynamicSky's driver).
//
//   pass 0 MARCH   - into the compact target (map / 4 each way): each texel
//                    marches the one map texel of its 4 x 4 block whose turn
//                    it is this frame (_PSXVolPhase.xy). With _PSXVolPhase.z
//                    = 1 it marches every texel of a full-size target instead
//                    (a tool's still frame).
//   pass 1 RESOLVE - the full map: every texel is last frame's, reprojected
//                    by how the clouds moved against the eye (_PSXVolMove);
//                    the texels marched this frame blend in their fresh value
//                    (_PSXVolPhase.w of it). _PSXVolMove.w = 1 fills the whole
//                    map from the compact march instead (a reset: blurry for
//                    the 16 frames it takes the march to sharpen it).
Shader "Hidden/PSX/VolClouds"
{
    Properties
    {
        _MainTex ("History", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #define PSX_VOLCLOUDS_MARCH
            #include "PSXAtmosphere.cginc"

            float4 _PSXVolPhase;   // xy this frame's texel in each 4x4 block, z 1 = march every texel, w blend
            float4 _PSXVolSize;    // xy the full map's size

            float4 frag (v2f_img i) : SV_Target
            {
                float2 uv;
                float2 px;
                if (_PSXVolPhase.z > 0.5)
                {
                    uv = i.uv;
                    px = floor(i.uv * _PSXVolSize.xy);
                }
                else
                {
                    float2 block = floor(i.uv * (_PSXVolSize.xy * 0.25));
                    px = block * 4.0 + _PSXVolPhase.xy;
                    uv = (px + 0.5) / _PSXVolSize.xy;
                }
                // A new start offset each frame: the march's slices become
                // grain, and the grain averages away (white noise - the
                // interleaved gradient's diagonals showed through the 4 x 4
                // stride as hatching on the cloud edges).
                float jitter = _PSXVolSteps.w > 0.5 ? AtmoHash(px + _PSXVolWind.w * float2(17.13, 7.71)) : 0.5;
                return VolMarch(VolDir(uv), jitter);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "PSXAtmosphere.cginc"

            sampler2D _MainTex;
            sampler2D _PSXVolFresh;
            float4 _PSXVolPhase;
            float4 _PSXVolSize;
            float4 _PSXVolMove;    // xyz how far the clouds moved AGAINST the eye since last frame (m), w 1 = reset

            float4 frag (v2f_img i) : SV_Target
            {
                float2 px = floor(i.uv * _PSXVolSize.xy);
                float2 cuv = (floor(px * 0.25) + 0.5) / (_PSXVolSize.xy * 0.25);
                if (_PSXVolMove.w > 0.5) return tex2Dlod(_PSXVolFresh, float4(i.uv, 0, 0));
                float3 dir = VolDir(i.uv);
                // Where this texel's cloud was a frame ago, seen from where
                // the eye was then.
                float3 was = normalize(dir * VolCloudRange(dir) + _PSXVolMove.xyz);
                float4 hist = tex2Dlod(_MainTex, float4(VolUV(was), 0, 0));
                float2 sub = px - floor(px * 0.25) * 4.0;
                if (abs(sub.x - _PSXVolPhase.x) < 0.5 && abs(sub.y - _PSXVolPhase.y) < 0.5)
                    return lerp(hist, tex2Dlod(_PSXVolFresh, float4(cuv, 0, 0)), _PSXVolPhase.w);
                return hist;
            }
            ENDCG
        }
    }
}
