// THE LAMP CONTROL MAP - the owner's authored lit lamps, shared by
// PSX/CarPaint (the bodywork, the lamps as he painted them) and PSX/CarLens
// (the clear outer covers of a shell's lens variant).
//
// His method (Cars/R32_LightTest v01..v03, 2026-10-15/16): ONE linear map in
// the body atlas's UV layout per model, set per renderer by CarLights through
// a property block, never baked into a livery:
//   R = rear running, G = rear brake (v01),
//   B = headlight projector core, A = clear-lens scatter (v03).
// A legacy R/G map (<key>_lightmask_rg.png) never has its B/A read: CarLights
// leaves _HeadLights at 0 for it.
//
//   rear = max(R * run colour * run strength * running,
//              G * brake colour * brake strength * brake) * lens detail,
//   lens detail = lerp(0.55, 1, saturate(atlas.r * 2)),
//   head = (B * core + A * scatter * head detail) * low-beam colour * headlights,
//   head detail = lerp(0.3, 1, saturate(luminance(atlas) * 3)),
// so braking works with the running lights off and never doubles with them on.
//
// His strengths (running 0.8, brake 7, core 3, scatter 0.65) assume an HDR
// target and a bloom threshold of 1; this camera is LDR (allowHDR false) and
// the halo is PSX/Blit's HALATION, which glows by the excess over 0.57 linear
// times the emitter mask. So, retuned for LDR:
//   * the running ring peaks near 0.5 linear - a dim red glow under the
//     halation's knee, no halo;
//   * the brake clips the red with a little green in the core (a hot lamp),
//     so it halos;
//   * the projector core clips white-hot (and halos), the scatter is a soft
//     warm lens glow under the knee - his 4.6:1 core-to-scatter, about.
// An emitter: the eye's adaptation, no exposure, no curve (PSXTone.cginc),
// added after the paint's shoulder by the caller.
//
// THE LENS SPLIT (his lens test v04, B): on a shell whose lamps sit recessed
// behind separate clear covers, 25% of the headlamp scatter and 20% of the
// rear emission move onto the outer cover; the projector core stays on the
// backing; amber reflects and does not emit (its texels are 0 in R/G). The
// body side multiplies by (1 - share * _LensLayer), the cover by the share.
#ifndef PSX_LAMPMAP_INCLUDED
#define PSX_LAMPMAP_INCLUDED

#define LAMP_RUN_COLOR       float3(1.00, 0.025, 0.010)
#define LAMP_RUN_STRENGTH    0.60
#define LAMP_BRAKE_COLOR     float3(1.00, 0.050, 0.020)
#define LAMP_BRAKE_STRENGTH  1.80
#define LAMP_DETAIL_LO       0.55
#define LAMP_HEAD_COLOR      float3(1.00, 0.86, 0.64)   // his low beam (the beam's Halogen is 1, .86, .62)
#define LAMP_HEAD_CORE       1.60   // B: the projector core - white-hot, clipped, halos
#define LAMP_HEAD_SCATTER    0.45   // A: the clear-lens scatter - a soft glow under the knee
#define LAMP_HEAD_DETAIL_LO  0.30
#define LENS_HEAD_SHARE      0.25   // of the scatter, onto a headlamp cover
#define LENS_REAR_SHARE      0.20   // of the rear emission, onto a tail cover

/// The rear lamps (running/brake) at one texel, before any lens split.
inline float3 PSXLampRear(float2 rg, float3 atlas, float running, float brake)
{
    float lensDetail = lerp(LAMP_DETAIL_LO, 1.0, saturate(atlas.r * 2.0));
    float3 run = rg.x * LAMP_RUN_COLOR * (LAMP_RUN_STRENGTH * saturate(running));
    float3 stop = rg.y * LAMP_BRAKE_COLOR * (LAMP_BRAKE_STRENGTH * saturate(brake));
    return max(run, stop) * lensDetail;
}

/// The headlamp's scatter (A) at one texel, before any lens split.
inline float3 PSXLampScatter(float a, float3 atlas, float head)
{
    float detail = lerp(LAMP_HEAD_DETAIL_LO, 1.0, saturate(dot(atlas, float3(0.2126, 0.7152, 0.0722)) * 3.0));
    return a * (LAMP_HEAD_SCATTER * detail * saturate(head)) * LAMP_HEAD_COLOR;
}

/// The headlamp's projector core (B) at one texel: always on the backing.
inline float3 PSXLampCore(float b, float head)
{
    return b * (LAMP_HEAD_CORE * saturate(head)) * LAMP_HEAD_COLOR;
}

#endif
