// ONE EXPOSURE AND ONE TONE CURVE (the colour pass, C2, 2026-09-29).
//
// What was wrong: nothing in the picture had an exposure, and every kind of
// surface rolled its highlights off in a private way - PSX/Lit shouldered the
// LIGHT (toe 0.80, span 0.50: a noon road's 1.95 of light landed at 1.25),
// PSX/CarPaint had its own knee and its own ceiling, the dynamic sky its own
// 1 - exp(-x), and the headlights, the lamps and the lit windows none at all.
// All of it was then written into an 8-bit framebuffer that clips at 1, and
// the film grade maps 1.0 to its cream ceiling (code 239): the owner's noon
// deck and his night headlight blob were both a flat plateau of that one
// code - "blinding and washed out".
//
// What this is instead, in the order the object shaders apply it:
//
//   * THE EXPOSURE (_PSXExposure, TimeOfDay.ExposureFor): one number per hour
//     and weather that multiplies every LIGHT a surface receives - sun,
//     ambient, street lamps, headlights, the lamps' streaks on a wet road -
//     before it meets the albedo. It is the retired shoulder's own gain at
//     the hour's sunlit road, so a sunlit road keeps the light it had (the
//     owner's road colours) and everything else is honest about it: shade
//     darker (harsh sun), a face lit harder than the road rolled off by the
//     curve instead of by a flattened light. A clear noon 0.68, a snowy one
//     0.76, a rainy one 0.93, an afternoon 0.86, a morning 0.98; every
//     night, dusk, dawn and sunset exactly 1.
//   * THE SKY, THE FOG AND THE EMITTERS ARE NOT EXPOSED (C3): the sky and the
//     fog colour are where the owner signed them off, and a lamp head, a lens
//     and a lit window are light sources, not lit surfaces. They take
//     _PSXAdapt, the eye's adaptation (C10; exactly 1 until then). The sky's
//     photograph and the fog (the sky's own colour at the horizon: the two
//     take the one treatment or the land ends at a line) take the curve's
//     shoulder CHANNEL BY CHANNEL (PSXToneSky, below); emitters take none.
//   * THE CURVE (PSXTone): the Khronos PBR Neutral highlight compression on the
//     radiance of every LIT surface, before the fog and before the RGBA8
//     write - identity below 0.76, and above it the brightest channel rolls
//     toward 1.0 and never arrives, with a little desaturation (0.15) so a
//     highlight goes toward white the way film does, not toward a hue at 1.
//     WITHOUT the Neutral toe: that toe pulls the darks down (the owner's
//     fresh asphalt #1e1e22, 0.013 linear, would become 0.0011 - 3.6 stops),
//     and the owner's road colours are his.
//
// Why in the object shaders and not in a post pass: the framebuffer is 8-bit
// and clips at 1, so a curve applied afterwards would be applied to light
// that is already gone - a clipped 1.0 would become a flat grey, not a
// highlight. Here the curve sees the light unclipped. An HDR target would
// need EXT_color_buffer_float, which URP silently drops to LDR where it is
// missing, so a phone without it would get a different picture; this is the
// same picture on every device, for about six ALU per pixel and no pass.
//
// THE SWITCHES are globals, never Properties (a property of the same name
// would shadow the global and read its own 0), pushed every frame by
// PSXGlobals from the fields TimeOfDay.Apply writes:
//   _PSXToneOn   1 once an hour has been applied (outdoors). 0 in every scene
//                that never applies one - every interior - and in any scene
//                that never set it, and then NOTHING here does anything: the
//                gains are exactly 1 and the curve returns its input, so an
//                interior is the picture it always was. (PSX_TONE=0 in a
//                tool's environment is the before-picture of this pass.)
//   _PSXExposure the hour's exposure; read only when _PSXToneOn is 1.
//   _PSXAdapt    the eye's adaptation, 1 until C10; read only when on.
#ifndef PSX_TONE_INCLUDED
#define PSX_TONE_INCLUDED

float _PSXToneOn;
float _PSXExposure;
float _PSXAdapt;

// Khronos PBR Neutral, its shoulder only. START is 0.8 - 0.04: Khronos's
// constants, which put the knee where its (omitted) toe would have ended.
#define PSX_TONE_START   0.76
#define PSX_TONE_DESAT   0.15

// What every LIGHT a lit surface receives is multiplied by.
inline float PSXExposureGain()
{
    return _PSXToneOn > 0.5 ? _PSXExposure * _PSXAdapt : 1.0;
}

// What the sky, the fog and the emitters are multiplied by: the eye only.
inline float PSXAdaptGain()
{
    return _PSXToneOn > 0.5 ? _PSXAdapt : 1.0;
}

// The curve. Identity below PSX_TONE_START (bit for bit: the branch returns
// the input untouched), and an exact no-op whenever the tone is off.
inline float3 PSXTone(float3 c)
{
    if (_PSXToneOn < 0.5) return c;
    float peak = max(c.r, max(c.g, c.b));
    if (peak < PSX_TONE_START) return c;
    const float d = 1.0 - PSX_TONE_START;
    float newPeak = 1.0 - d * d / (peak + d - PSX_TONE_START);
    c *= newPeak / peak;
    float g = 1.0 - 1.0 / (PSX_TONE_DESAT * (peak - newPeak) + 1.0);
    return lerp(c, float3(newPeak, newPeak, newPeak), g);
}

// THE SAME SHOULDER, CHANNEL BY CHANNEL, for the sky's photograph and the fog
// (its horizon: the land fades into the fog and the sky's lowest band IS the
// fog colour, so the two must take the one treatment or the world ends at a
// line). The noon photograph's blue is over 1.0 across most of the sky (x1.08
// exposure, x1.11 from the hour's blue stop; measured, every pixel of the top
// fifth of the S1 noon frame), and its clouds over 1.0 in all three - the
// owner's near-white noon sky, flat on the grade's ceiling. The hue-keeping
// curve above would scale a whole sky pixel down with its blue (a noon sky
// 13% darker, which C3 forbids); left alone, the clouds stay a flat cream
// plateau (S1 noon p99 235, measured). Per channel, whatever was under 0.76
// is untouched and the clip becomes a roll-off.
inline float PSXToneChannel(float x)
{
    const float d = 1.0 - PSX_TONE_START;
    return x < PSX_TONE_START ? x : 1.0 - d * d / (x + d - PSX_TONE_START);
}

inline float3 PSXToneSky(float3 c)
{
    if (_PSXToneOn < 0.5) return c;
    return float3(PSXToneChannel(c.r), PSXToneChannel(c.g), PSXToneChannel(c.b));
}

// The fog colour as a lit surface fades into it: adapted (the eye), never
// exposed (it is the sky's colour, not a lit thing), and rolled off exactly as
// the sky's horizon band is.
inline float3 PSXFogTone(float3 fogCol)
{
    return PSXToneSky(fogCol * PSXAdaptGain());
}

// THE EMITTER MASK (C4). The framebuffer's alpha says how much of a pixel is
// a LIGHT SOURCE - a lamp head, a lens, a lit window, a glint, the sun - and
// PSX/Blit's halation and PSX/Lens's dirt glow only by that much. So a lit
// concrete deck, snow, or a noon sky can never halo or throw bokeh, however
// bright; a lamp always does. Opaque shaders WRITE it (0 for a lit surface);
// alpha-blended ones keep what is under them ("Blend ..., Zero One");
// additive emitters add their own coverage. _PSXEmitKey 0 (every interior,
// and PSX_EMITKEY=0 in a tool) is the old rule: anything bright glows.
//
// ONLY THE PSX CAMERA'S OWN FRAME carries the mask: _PSXEmitWrite is 1 while
// the camera that PSX/Blit shows is drawing (EmitterMask.cs sets it per
// camera). Every other camera - the rear-view mirror, the pizza cam, the car
// viewer in the menus - draws into a render texture that a RawImage BLENDS
// BY ITS ALPHA, so there the opaque shaders write the coverage they always
// wrote, or the mirror would turn transparent wherever a wall is.
float _PSXEmitKey;
float _PSXEmitWrite;

// How much of a pixel's radiance is its highlight (a glint, a streak): the
// emitter share an opaque shader writes for a specular it adds itself.
inline float PSXEmitShare(float3 highlight, float3 total)
{
    float h = dot(highlight, float3(0.2126, 0.7152, 0.0722));
    float t = dot(total, float3(0.2126, 0.7152, 0.0722));
    return saturate(h / max(t, 1e-4));
}

#endif
