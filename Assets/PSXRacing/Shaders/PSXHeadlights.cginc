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
#endif
