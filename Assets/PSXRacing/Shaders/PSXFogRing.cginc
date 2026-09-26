// THE HORIZON RING, 2026-09-26 (the Tidewater pass). Tidewater colours its haze
// with "the sky's own colour just above the horizon in the view direction", and
// that is most of why its distance melts into its sky: orange toward a sunset,
// blue-grey behind you. Ours had ONE fog colour for the whole compass, so the
// land faded into the same paint east and west while the photograph above it
// was gold on one side and slate on the other.
//
// The ring is eight colours per panorama, measured by the builder just above
// the photograph's horizon (BakeSkyHorizonRings) and handed on as RATIOS to
// their own mean, so the hour's fog colour - the owner's signed-off value, and
// one half of the fog/horizon pair - stays the fog's AVERAGE colour; the ring
// only says how much warmer or cooler each bearing is than that average.
// TimeOfDay.ApplySky sets it with the panorama; PSX/Sky's horizon band takes
// the same ratio, so the fog and the sky it meets are still one paint in every
// direction. _PSXFogRingOn is 0 in any scene that never applied an hour: the
// ratio is then exactly 1 and every picture is the old one.
#ifndef PSX_FOG_RING_INCLUDED
#define PSX_FOG_RING_INCLUDED

float4 _PSXFogRing[8];   // rgb = the bearing's colour / the ring's mean, image-u order
float _PSXFogRingOn;     // 0..1, how much of it to take

/// The ring's colour ratio for a world direction, through the panorama's
/// rotation (degrees, as PSX/Sky samples it: u = atan2(z, x) / 2pi + 0.5 +
/// rotation / 360), blended between the two nearest of the eight bearings.
float3 PSXFogRing(float3 dir, float rotationDeg)
{
    if (_PSXFogRingOn <= 0.001) return float3(1, 1, 1);
    float u = frac(atan2(dir.z, dir.x) * (0.5 / UNITY_PI) + 0.5 + rotationDeg / 360.0);
    float x = u * 8.0 - 0.5;
    float f = frac(x);
    int i0 = (int)floor(x);
    i0 = i0 < 0 ? i0 + 8 : i0;
    int i1 = i0 + 1 > 7 ? 0 : i0 + 1;
    float3 r = lerp(_PSXFogRing[i0].rgb, _PSXFogRing[i1].rgb, f);
    return lerp(float3(1, 1, 1), r, _PSXFogRingOn);
}
#endif
