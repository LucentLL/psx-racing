// HEADLIGHT BEAMS, PER PIXEL, ON EVERY PSX SURFACE.
//
// Until this file existed a headlight was a lens sprite and one additive disc
// laid flat on the road seven metres ahead: a yellow puddle that neither
// reached down the road nor stopped at the kerb, and that lit a wall it was
// pointed at not at all. The owner's ask was the real thing - "projecting
// actual beams to light the road and volume in front of them" - and at 240
// lines the real thing is affordable: a hundred thousand pixels times eight
// lamps is nothing to a phone GPU, and it is the one lighting effect the
// per-vertex world genuinely cannot fake, because a road ribbon has a vertex
// every few metres and a beam has structure every few centimetres.
//
// So the world shaders (PSX/Lit, PSX/LitTransparent, PSX/CarPaint) each add
// this term to their per-vertex light before multiplying by the texture. It
// is the ONLY per-pixel light in the game; the sun stays per vertex, which is
// the look.
//
// What a low beam IS, and what the numbers below say about it:
//   * It is HALOGEN. The game is set in 1999 and every lamp on the grid is a
//     tungsten-halogen bulb: warm, slightly yellow, nothing like the blue-
//     white of an LED. CarLights owns the colour; this file only adds it.
//   * It has a FLAT TOP. A low beam is cut off just under the horizontal so
//     it does not dazzle oncoming traffic: on a garage door it is a bright
//     band at bumper height with a sharp upper edge, on a wall ahead the same.
//     `cut` is that edge - full below the lamp's own level, gone a couple of
//     degrees above it - measured against the beam AXIS, which CarLights aims
//     a degree and a half down.
//   * It is WIDE. Halogen low beams spread about thirty degrees either side
//     and are brightest inside ten; `spread` is a smoothstep between the two
//     cosines CarLights packs into the w channels.
//   * It REACHES. Useful light to seventy-odd metres, brightest close in and
//     fading rather than stopping: t * sqrt(t) over the range.
//   * The ROAD lights at a grazing angle. A plain N.L on the tarmac twenty
//     metres out is 0.03 and the road would stay black under a beam that
//     visibly lights it in every night drive anyone has ever taken. Real
//     lamps get away with it through sheer intensity; here `facing` is a
//     smoothstep that saturates by 0.08 of N.L, so the road takes most of
//     the beam, a wall facing the car takes all of it, and the BACK of a sign
//     takes none.
//
// Eight slots. CarLights fills them nearest-camera-first, two per car (the
// two lamps are two lobes that merge a few metres out, which is what the
// pattern looks like from behind), and pushes a count of zero the moment the
// last car with lights on goes away - a global that nothing writes is a
// stale one, not an empty one, and a stale count would leave phantom beams
// on the garage floor.
#ifndef PSX_HEADLIGHTS_INCLUDED
#define PSX_HEADLIGHTS_INCLUDED

#define PSX_MAX_HEADLIGHTS 8

float  _PSXHeadCount;
float4 _PSXHeadPos[PSX_MAX_HEADLIGHTS];    // xyz lamp (world), w = range in metres
float4 _PSXHeadFwd[PSX_MAX_HEADLIGHTS];    // xyz beam axis (unit), w = cos of the outer half-spread
float4 _PSXHeadRight[PSX_MAX_HEADLIGHTS];  // xyz lamp right (unit), w = cos of the inner half-spread
float4 _PSXHeadColor[PSX_MAX_HEADLIGHTS];  // rgb = colour x intensity

/// Light arriving at world point `wpos` on a surface with normal `N` from
/// every headlight in the table. Zero-cost when the count is zero (one
/// uniform branch), which is every daylight hour.
float3 PSXHeadlights(float3 wpos, float3 N)
{
    float3 sum = float3(0.0, 0.0, 0.0);
    if (_PSXHeadCount < 0.5) return sum;
    int count = (int)_PSXHeadCount;
    for (int j = 0; j < PSX_MAX_HEADLIGHTS; j++)
    {
        if (j >= count) break;
        float3 d = wpos - _PSXHeadPos[j].xyz;
        float dist = length(d);
        float3 F = _PSXHeadFwd[j].xyz;
        float3 R = _PSXHeadRight[j].xyz;
        float3 U = cross(F, R);
        float fz = dot(d, F);
        float fx = dot(d, R);
        float fy = dot(d, U);
        // Horizontal spread: the angle off the axis in the lamp's own
        // horizontal plane, as a cosine.
        float hz = fz * rsqrt(fx * fx + fz * fz + 1e-4);
        float spread = smoothstep(_PSXHeadFwd[j].w, _PSXHeadRight[j].w, hz);
        // The low-beam cutoff: a flat top a hair above the axis.
        float slope = fy / max(fz, 0.25);
        float cut = 1.0 - smoothstep(-0.015, 0.035, slope);
        // Nothing behind the lens.
        float front = saturate(fz * 1.5);
        float t = saturate(1.0 - dist / max(_PSXHeadPos[j].w, 1.0));
        float att = t * sqrt(t);
        // Grazing surfaces still light; back faces do not.
        float nl = dot(N, -d) / max(dist, 0.05);
        float facing = smoothstep(-0.05, 0.08, nl);
        sum += _PSXHeadColor[j].rgb * (spread * cut * front * att * facing);
    }
    return sum;
}

// ---------------------------------------------------------------------------
//  THE SAME BEAMS SEEN IN A WET ROAD, AND IN THE RAIN (2026-09-21, the NFS
//  night pass). PSXHeadlights above is untouched and still what every lit
//  surface adds; these are for the shaders that also want a GLINT (the wet
//  road, the car paint) or a light with no surface under it (rain streaks).
//
//  The glint is the oncoming car's lamps smeared down a wet road toward the
//  camera: Blinn-Phong on each lamp, gated by the lamp FACING the point
//  (the same horizontal spread and the nothing-behind-the-lens `front` as
//  the diffuse) but NOT by the low-beam cutoff - the flat top is about what
//  the beam lights, and a mirror image of the lens is seen from above it,
//  which is exactly where a driver's eye is. It reaches 1.6 beam ranges,
//  fading, so a reflection is already there before the beam itself arrives.
//  And it is gated by the lamp being ABOVE the surface's plane (N.l >= 0):
//  a wet deck cannot mirror a car on the road beneath it.
//
//  One loop per table (the rule PSXLamps.cginc follows too): a shader that
//  wants diffuse AND glints calls PSXHeadlightsBoth and walks the eight slots
//  once. Its diffuse is the same expression as PSXHeadlights, term for term.
// ---------------------------------------------------------------------------

/// The shared walk. facingOn 0 drops the surface's facing term (a point in
/// the air); specOn 0 skips the glints. Literals at every call site, so both
/// branches fold at compile time.
void PSXHeadlightsCore(float3 wpos, float3 N, float3 V, float power, float specOn, float facingOn,
                       out float3 diff, out float3 spec)
{
    diff = float3(0.0, 0.0, 0.0);
    spec = float3(0.0, 0.0, 0.0);
    if (_PSXHeadCount < 0.5) return;
    int count = (int)_PSXHeadCount;
    for (int j = 0; j < PSX_MAX_HEADLIGHTS; j++)
    {
        if (j >= count) break;
        float3 d = wpos - _PSXHeadPos[j].xyz;
        float dist = length(d);
        float3 F = _PSXHeadFwd[j].xyz;
        float3 Rt = _PSXHeadRight[j].xyz;
        float3 U = cross(F, Rt);
        float fz = dot(d, F);
        float fx = dot(d, Rt);
        float fy = dot(d, U);
        float hz = fz * rsqrt(fx * fx + fz * fz + 1e-4);
        float spread = smoothstep(_PSXHeadFwd[j].w, _PSXHeadRight[j].w, hz);
        float slope = fy / max(fz, 0.25);
        float cut = 1.0 - smoothstep(-0.015, 0.035, slope);
        float front = saturate(fz * 1.5);
        float range = max(_PSXHeadPos[j].w, 1.0);
        float t = saturate(1.0 - dist / range);
        float att = t * sqrt(t);
        float nl = dot(N, -d) / max(dist, 0.05);
        float facing = facingOn > 0.5 ? smoothstep(-0.05, 0.08, nl) : 1.0;
        diff += _PSXHeadColor[j].rgb * (spread * cut * front * att * facing);

        if (specOn > 0.5)
        {
            float3 l = -d / max(dist, 0.05);
            float3 hv = l + V;
            float3 H = hv * rsqrt(max(dot(hv, hv), 1e-6));
            float nh = max(saturate(dot(N, H)), 1e-4);
            float reach = 1.0 - saturate(dist / (range * 1.6));
            reach *= reach;
            // Only a lamp on the seen side of the surface is mirrored in it
            // (PSXLampsCore says it at length): without this, the low eye
            // over a wet bridge deck saw the glint of a car's headlights on
            // the road BENEATH the deck, because H still leans toward N for
            // a lamp just under the plane. A hard step - every lamp above the
            // plane keeps its whole grazing streak.
            float seen = step(0.0, dot(N, l));
            spec += _PSXHeadColor[j].rgb * (pow(nh, power) * spread * front * reach * seen);
        }
    }
}

/// Diffuse (identical to PSXHeadlights) and glints at `power` in one walk.
/// `V` is the unit vector from the surface to the eye.
void PSXHeadlightsBoth(float3 wpos, float3 N, float3 V, float power, float specOn,
                       out float3 diff, out float3 spec)
{
    PSXHeadlightsCore(wpos, N, V, power, specOn, 1.0, diff, spec);
}

/// The glare of each lamp seen in a glossy surface (see the block above).
float3 PSXHeadlightsSpec(float3 wpos, float3 N, float3 V, float power)
{
    float3 diff, spec;
    PSXHeadlightsCore(wpos, N, V, power, 1.0, 1.0, diff, spec);
    return spec;
}

/// The beam at a point, with no surface: spread x cutoff x front x falloff,
/// no facing. A raindrop inside a low beam is lit; one above its flat top
/// is not, which is what draws the beam's shape in the rain.
float3 PSXHeadlightsAt(float3 wpos)
{
    float3 diff, spec;
    PSXHeadlightsCore(wpos, float3(0.0, 1.0, 0.0), float3(0.0, 1.0, 0.0), 1.0, 0.0, 0.0, diff, spec);
    return diff;
}
#endif
