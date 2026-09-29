// THE 16-BIT TEXEL DECODE (the colour pass, C1b, 2026-09-29).
//
// What went wrong: the 2026-09-27 release budget (Editor/ReleaseBudget.cs)
// shipped ~926 opaque colour textures as RGB565 on WebGL to halve the
// download. WebGL2 has no sRGB 565 format, and the project renders in
// Linear, so the GPU handed each texel's GAMMA code to the shader as if it
// were linear light: a fresh-asphalt texel of code 30 (.012 linear) was lit
// as .118, x9. Every road, deck, car and building in the player went 1.5-9x
// too bright - the owner's "I'm not sure what happened to the color".
//
// What this does instead: those textures are imported as LINEAR DATA on
// every platform (sRGBTexture 0, asset label psx16), so no GPU decodes them
// anywhere, and the shader decodes them itself with the exact sRGB curve.
// The same bytes then mean the same colour on every build target: RGB24 on
// Standalone and in the editor, RGB565 on WebGL. The decode is EXACT (not
// an approximation of the hardware's) because every texture in the set is
// point-filtered with no mips: the texel the shader decodes is the texel
// the hardware would have decoded, so no filtering happens in the wrong
// space.
//
// The switch is per MATERIAL, _MainTexRaw (and _DeepTexRaw on the water):
// 1 when its texture is a Texture2D whose data the GPU does not decode
// (!Texture.isDataSRGB), 2 when that texture is also RGB565 (below).
//
// THE 565 BIN CENTRE (measured 2026-09-29). Unity quantizes a texture to
// RGB565 by TRUNCATION (c >> 3, c >> 2), so level L stands for the 8-bit
// codes 8L..8L+7, and the GPU returns L/31 - the BOTTOM of that bin. On
// the owner's fresh asphalt (texels ~28) that read 15% darker in linear
// than the texture it came from (WebGL frames: fresh asphalt 51 -> 49,
// concrete +1): measured ratios .86/.90-.95/.84-.88 per channel against a
// truncation model's .85/.94/.86. Flag 2 moves each texel to the middle of
// its bin (8L+3.5, 4G+1.5) before the decode: the model leaves under 2%
// on any road texture, a quarter of a display code on asphalt.
//
// ONE writer sets the flag, at runtime, from the texture itself:
// Scripts/PSXTexDecode.cs (and its editor hook for edit-mode renders). A
// .mat file always serialises 0 - a flag baked into an asset would freeze
// whatever import the baking machine happened to have.
//
// Readers of ALPHA only need nothing (the alpha channel is linear in both
// imports): PSX/Glow, PSX/Shadow, PSX/Rain, PSX/ShadowCaster.
//
// Cost: three pow() per pixel on the handful of shaders that sample a
// colour texture, at 480 lines. No pass, no draw call, no texture.
#ifndef PSX_TEXDECODE_INCLUDED
#define PSX_TEXDECODE_INCLUDED

// Set per material by PSXTexDecode.cs: 0 no decode (every .mat file, every
// material that never met a texture of the set), 1 decode, 2 decode from
// the centre of a 565 bin.
float _MainTexRaw;

// IEC 61966-2-1, per channel - the same curve as UnityCG's
// GammaToLinearSpaceExact, which is what a GPU's sRGB sampler implements.
// NOT the 3-term polynomial (GammaToLinearSpace): that is up to five display
// codes off at the darkest 565 levels, which are exactly the owner's
// asphalt.
inline float PSXSRGBToLinear1(float c)
{
    return c <= 0.04045 ? c * (1.0 / 12.92) : pow((c + 0.055) * (1.0 / 1.055), 2.4);
}

inline float3 PSXSRGBToLinear(float3 c)
{
    return float3(PSXSRGBToLinear1(c.r), PSXSRGBToLinear1(c.g), PSXSRGBToLinear1(c.b));
}

// A colour texel: decoded when the material says its texture is raw, as
// sampled otherwise; a 565 texel (raw 2) from the centre of its bin. Alpha
// is never touched.
inline float3 PSXTexDecode(float3 c, float raw)
{
    if (raw > 1.5) c = c * (float3(248.0, 252.0, 248.0) / 255.0) + float3(3.5, 1.5, 3.5) / 255.0;
    return raw > 0.5 ? PSXSRGBToLinear(c) : c;
}

// tex2D(_MainTex, uv) for a colour texture: call it BEFORE the tint
// (* _Color) and before any clip, so both see the same linear texel the
// hardware decode used to give them.
inline float4 PSXMainTex(sampler2D s, float2 uv)
{
    float4 t = tex2D(s, uv);
    t.rgb = PSXTexDecode(t.rgb, _MainTexRaw);
    return t;
}

#endif
