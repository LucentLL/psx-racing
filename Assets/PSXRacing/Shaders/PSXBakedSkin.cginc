// THE BAKED-BONE SKIN (people, Stage 1 cost test, 2026-11-26): technique B of
// Characters_Unity_Plan Stage 1. A figure drawn with Graphics.RenderMeshInstanced
// is skinned HERE, in PSX/Lit's own vertex shader (keyword PSX_BAKEDSKIN), from a
// texture of bone matrices baked from the clips: one row a frame, three texels a
// bone (the 3x4 skin matrix, bone-to-model x bind pose). The vertex carries its
// four bone indices in uv3 and their weights in uv4. Each instance carries one
// float4 (_PSXSkinFrame): x the row of this frame, y the next row, z the blend
// between them, w unused. Everything after the skin - the sun, the street lamps
// (PSXLamps.cginc), the headlights (PSXHeadlights.cginc), the fog - is PSX/Lit's
// own path, unchanged, exactly as for a car or a wall.
#ifndef PSX_BAKEDSKIN_INCLUDED
#define PSX_BAKEDSKIN_INCLUDED

sampler2D _PSXSkinTex;
float4 _PSXSkinTex_TexelSize;   // x 1/width, y 1/height

UNITY_INSTANCING_BUFFER_START(PSXSkinProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _PSXSkinFrame)
UNITY_INSTANCING_BUFFER_END(PSXSkinProps)

float3x4 PSXSkinBone(float row, float bone)
{
    float v = (row + 0.5) * _PSXSkinTex_TexelSize.y;
    float u0 = (bone * 3.0 + 0.5) * _PSXSkinTex_TexelSize.x;
    float4 r0 = tex2Dlod(_PSXSkinTex, float4(u0, v, 0, 0));
    float4 r1 = tex2Dlod(_PSXSkinTex, float4(u0 + _PSXSkinTex_TexelSize.x, v, 0, 0));
    float4 r2 = tex2Dlod(_PSXSkinTex, float4(u0 + 2.0 * _PSXSkinTex_TexelSize.x, v, 0, 0));
    return float3x4(r0, r1, r2);
}

float3x4 PSXSkinRow(float row, float4 idx, float4 w)
{
    return PSXSkinBone(row, idx.x) * w.x + PSXSkinBone(row, idx.y) * w.y +
           PSXSkinBone(row, idx.z) * w.z + PSXSkinBone(row, idx.w) * w.w;
}

void PSXBakedSkin(inout float4 vertex, inout float3 normal, float4 idx, float4 w)
{
    float4 f = UNITY_ACCESS_INSTANCED_PROP(PSXSkinProps, _PSXSkinFrame);
    float3x4 m = lerp(PSXSkinRow(f.x, idx, w), PSXSkinRow(f.y, idx, w), f.z);
    vertex = float4(mul(m, float4(vertex.xyz, 1.0)), 1.0);
    normal = normalize(mul((float3x3)m, normal));
}
#endif
