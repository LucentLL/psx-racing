// THE WATER'S DEPTH COPY (the water round, 2026-11-16; SpeedBlurFeature.
// WaterDepthPass draws it, only while the HD water is on). The camera's depth
// attachment, texel for texel, into _PSXWaterDepth for PSX/Water's HD variant.
// Its own hidden shader, not a pass of PSX/Water: a second pass on the water's
// shader changed the order the opaques draw in (a wet bridge deck's gutter
// read differently with the HD water off), and OFF must be today's picture.
// Always-included (GraphicsSettings): nothing references it but the feature.
Shader "Hidden/PSX/WaterDepthCopy"
{
    SubShader
    {
        Pass
        {
            Name "PSXWaterDepthCopy"
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vertCopy
            #pragma fragment fragCopy
            #pragma target 3.5
            Texture2D<float> _PSXWaterDepthSrc;
            float4 vertCopy (uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2.0 - 1.0, 0.5, 1.0);
            }
            float fragCopy (float4 pos : SV_POSITION) : SV_Target
            {
                return _PSXWaterDepthSrc.Load(int3(pos.xy, 0));
            }
            ENDCG
        }
    }
}