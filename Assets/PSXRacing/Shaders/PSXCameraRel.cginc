#ifndef PSX_CAMERA_REL_INCLUDED
#define PSX_CAMERA_REL_INCLUDED

// THE CABIN, 5 KM FROM THE ORIGIN (2026-10-17; owner: the mirror flickers,
// "the reflective layer seems to interfere with the modeled area it is
// placed on"). Charlotte's I-277 loop runs ~5.4 km from the world origin,
// where a float holds a position to 0.49 mm. UnityObjectToClipPos rounds
// every vertex to that in WORLD space first, each on its own, and the
// mirror's face stands only 0.5 mm proud of the housing's front plate
// (v08: 44 triangles, 130 cm2, 0.5-2.5 mm behind it) - so face and plate
// traded places as the car moved, a pattern the depth buffer could not
// help with however fine it was. Here the object's place is taken off the
// camera's FIRST (the two big numbers subtract exactly) and only the small
// rotated offset is added: every part of the cabin shares one transform,
// so they all shift together and keep their millimetres. Same clip space
// as UnityObjectToClipPos, to float rounding.
inline float4 PSXObjectToClipRel(float4 v)
{
    float3 rel = mul((float3x3)unity_ObjectToWorld, v.xyz)
               + (unity_ObjectToWorld._m03_m13_m23 - _WorldSpaceCameraPos);
    return mul(UNITY_MATRIX_P, float4(mul((float3x3)UNITY_MATRIX_V, rel), 1.0));
}

#endif
