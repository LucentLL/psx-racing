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
//     `cut` is that edge - full below 0.57 degrees under the lamp's own
//     level (the ECE low beam's cutoff), gone 0.34 degrees above it -
//     measured against the beam AXIS, which CarLights aims a degree and a
//     half down. It used to be full only 2.4 degrees down: on a level road
//     that is 16 m out from a lamp 0.65 m up, so the flat top was also a
//     distance falloff that took two thirds of the beam off the road at 58 m.
//   * It is WIDE. Halogen low beams spread about thirty degrees either side
//     and are brightest inside ten; `spread` is a smoothstep between the two
//     cosines CarLights packs into the w channels.
//   * It is FLAT, and it REACHES (the colour pass, C5, 2026-09-29). A low
//     beam is aimed so the ROAD it lights reads about evenly from the bumper
//     to fifty-odd metres: the hot spot is thrown at the far road, where it
//     arrives at a grazing angle, and little goes down at the tarmac under
//     the lamp. So the beam here is a plateau - BEAM_NEAR of it at the
//     bumper, full from BEAM_NEAR_TO metres, fading out over the last
//     BEAM_FADE_M of the range - where it used to be t * sqrt(t): three
//     times the plateau at 5 m and nearly gone by 50, the owner's
//     "headlights so bright ... they completely wash out anything in front
//     of the car". His NFS reference measures the headlit road 2.3-3.6x the
//     unlit road beside it, flat within 0.3 stop out to about 65 m.
//     THE DARK-NIGHT RETUNE (2026-09-29): real night drives and NFS Heat
//     (the owner's frames 12-15) have the pool 4-5 stops over a near-black
//     unlit road, and it TAILS OFF - the band 40-50 m out about a third of
//     the near pool, black by the edge of the range. So the plateau now
//     fades over the last BEAM_FADE_M = 45 m of the 75 m range (0.87 of
//     the plateau at 40 m, 0.61 at 49, 0.34 at 58) instead of holding
//     full to 55 m and dropping over 20.
//   * The ROAD lights at a grazing angle. A plain N.L on the tarmac twenty
//     metres out is 0.03 and the road would stay black under a beam that
//     visibly lights it in every night drive anyone has ever taken. Real
//     lamps get away with it by aiming their intensity there; here `facing`
//     is a smoothstep that saturates by 0.02 of N.L (a road 32 m out under a
//     lamp 0.65 m up), so the road takes the whole plateau, a wall facing
//     the car takes all of it, and the BACK of a sign takes none.
//   * It SPILLS. Outside the beam's 34 degrees a real lamp still throws a
//     wide, weak foreground light - the verge beside the car is dim, not
//     black. BEAM_SPILL of the plateau, out to 70 degrees, fading from 10 m
//     to 25 m, under a soft top of its own so it does not light the trees
//     overhead.
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

// THE LOW BEAM'S SHAPE (see the header). Distances in metres; the cut is the
// slope over the beam axis (fy / fz), the axis being BeamDipDeg (1.5) down.
#define BEAM_NEAR          0.55    // the tarmac at the bumper, as a share of the plateau...
#define BEAM_NEAR_FROM     3.0     // ...rising from here...
#define BEAM_NEAR_TO       10.0    // ...to the whole plateau here
#define BEAM_FADE_M        45.0    // the plateau fades out over the last this-many metres of the range (from 30 m at the 75 m range)
#define BEAM_CUT_LO        0.016   // the flat top: full below this slope over the axis (0.57 deg under level)...
#define BEAM_CUT_HI        0.032   // ...none above this one (0.34 deg over level)
#define BEAM_FACE_LO       -0.05   // N.L at which a surface starts to take the beam...
#define BEAM_FACE_HI       0.02    // ...and takes all of it (grazing road)
#define BEAM_SPILL         0.12    // the spill lobe, as a share of the plateau
#define BEAM_SPILL_COS     0.342   // cos 70 deg: where the spill ends sideways (whole inside the beam's own 34)
#define BEAM_SPILL_FROM    10.0    // the spill is whole to here...
#define BEAM_SPILL_TO      25.0    // ...and gone by here
#define BEAM_SPILL_CUT_LO  0.05    // its own soft top: whole below 3 deg over the axis...
#define BEAM_SPILL_CUT_HI  0.25    // ...gone by 14

// THE BEAM READS A ROAD AT A REAL ROAD'S REFLECTANCE, AT NIGHT (the dark-night
// retune, round two, 2026-09-29). The owner's roads are painted dark by day -
// fresh asphalt #1e1e22 is linear 0.013, the old asphalt 0.052, where a real
// fresh road reflects about 0.05 and an aged one about 0.10 - and under a
// linear beam that left the pool on fresh asphalt at Ycode 30 (drag strip,
// Sunset City GP, downtown) against the references' 55-80, and dimmer than a
// street lamp's pool on the same road. Raising the beam cannot fix it: the
// concrete of Samuel Street (0.25-0.48) whites out first. So at night the
// beam's diffuse on an UPWARD texel darker than BEAM_ALBEDO_FLOOR is taken as
// if the texel were sqrt(floor x albedo): fresh asphalt 0.013 -> 0.046, old
// asphalt 0.052 -> 0.091 (real roads), concrete and snow untouched. The
// square root keeps each texture's grain (its log contrast halves, and the
// code spread the eye reads holds: the strip's pool p10-p90 was 29-39 and
// is 60-70), and the gain is capped so nothing black is made grey. Measured
// (colour_stats.py night, round two): the pool at 14-22 m on fresh asphalt
// 30-33 -> 60-65, over Sunset City GP's street-lamp pool (53); Blue Ridge's
// old asphalt 65 -> 92; Samuel Street's concrete 188 -> 193. By day the gain is 1
// exactly (_PSXHeadNight is 0 through sunset and dawn - CarLights.BeamNight),
// so no daylight road gets lighter. The street lamps and the sun never take it.
#define BEAM_ALBEDO_FLOOR     0.16    // linear luminance the beam reads a darker upward texel toward
#define BEAM_ALBEDO_GAIN_MAX  4.0     // most it multiplies the beam's light on any texel by

float  _PSXHeadCount;
float  _PSXHeadNight;                      // 0 by day (sunset and dawn included) .. 1 at night: CarLights.BeamNight
float4 _PSXHeadPos[PSX_MAX_HEADLIGHTS];    // xyz lamp (world), w = range in metres
float4 _PSXHeadFwd[PSX_MAX_HEADLIGHTS];    // xyz beam axis (unit), w = cos of the outer half-spread
float4 _PSXHeadRight[PSX_MAX_HEADLIGHTS];  // xyz lamp right (unit), w = cos of the inner half-spread
float4 _PSXHeadColor[PSX_MAX_HEADLIGHTS];  // rgb = LINEAR colour x intensity (the plateau), w = the glints' multiplier on it

/// What the beam's diffuse is multiplied by on a texel of LINEAR colour
/// `albedo` whose normal is `N` (see BEAM_ALBEDO_FLOOR above): 1 by day, on
/// anything facing sideways or down, and on anything as bright as the floor.
float PSXBeamAlbedoGain(float3 albedo, float3 N)
{
    float a = dot(albedo, float3(0.2126, 0.7152, 0.0722));
    float g = min(sqrt(BEAM_ALBEDO_FLOOR / max(a, 1e-4)), BEAM_ALBEDO_GAIN_MAX);
    float up = saturate(N.y * 3.0 - 1.5);
    return 1.0 + max(g - 1.0, 0.0) * up * _PSXHeadNight;
}

// ---------------------------------------------------------------------------
//  THE SAME BEAMS SEEN IN A WET ROAD, AND IN THE RAIN (2026-09-21, the NFS
//  night pass). Every lit surface adds the diffuse; the shaders that also
//  want a GLINT (the wet road, the car paint) or a light with no surface
//  under it (rain streaks, lit smoke and snow) call the variants below.
//
//  The glint is the oncoming car's lamps smeared down a wet road toward the
//  camera: Blinn-Phong on each lamp, gated by the lamp FACING the point
//  (the same horizontal spread and the nothing-behind-the-lens `front` as
//  the diffuse) but NOT by the low-beam cutoff - the flat top is about what
//  the beam lights, and a mirror image of the lens is seen from above it,
//  which is exactly where a driver's eye is. It reaches 1.6 beam ranges,
//  fading, so a reflection is already there before the beam itself arrives.
//  And it is gated by the lamp being ABOVE the surface's plane (N.l >= 0):
//  a wet deck cannot mirror a car on the road beneath it. A glint is the
//  LENS seen in a mirror, so it takes the table's w on top of the plateau's
//  colour (CarLights.GlintIntensity): the C5 retune dimmed the road the lamp
//  lights, not the lamp.
//
//  One loop per table (the rule PSXLamps.cginc follows too): a shader that
//  wants diffuse AND glints calls PSXHeadlightsBoth and walks the eight slots
//  once; every variant is this one walk with literal switches.
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
        // Horizontal spread: the angle off the axis in the lamp's own
        // horizontal plane, as a cosine.
        float hz = fz * rsqrt(fx * fx + fz * fz + 1e-4);
        float spread = smoothstep(_PSXHeadFwd[j].w, _PSXHeadRight[j].w, hz);
        // The low-beam cutoff: a flat top just under the lamp's own level.
        float slope = fy / max(fz, 0.25);
        float cut = 1.0 - smoothstep(BEAM_CUT_LO, BEAM_CUT_HI, slope);
        // Nothing behind the lens.
        float front = saturate(fz * 1.5);
        float range = max(_PSXHeadPos[j].w, 1.0);
        // The plateau: BEAM_NEAR of it at the bumper, whole from
        // BEAM_NEAR_TO, gone at the range.
        float att = lerp(BEAM_NEAR, 1.0, smoothstep(BEAM_NEAR_FROM, BEAM_NEAR_TO, dist))
                  * (1.0 - smoothstep(range - BEAM_FADE_M, range, dist));
        // The spill: wide, weak, near the car, under a soft top of its own.
        float spill = BEAM_SPILL * smoothstep(BEAM_SPILL_COS, _PSXHeadFwd[j].w, hz)
                    * (1.0 - smoothstep(BEAM_SPILL_CUT_LO, BEAM_SPILL_CUT_HI, slope))
                    * (1.0 - smoothstep(BEAM_SPILL_FROM, BEAM_SPILL_TO, dist));
        // Grazing surfaces take the whole beam; back faces none.
        float nl = dot(N, -d) / max(dist, 0.05);
        float facing = facingOn > 0.5 ? smoothstep(BEAM_FACE_LO, BEAM_FACE_HI, nl) : 1.0;
        diff += _PSXHeadColor[j].rgb * ((spread * cut * att + spill) * front * facing);

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
            spec += _PSXHeadColor[j].rgb * (max(_PSXHeadColor[j].w, 0.0) * pow(nh, power) * spread * front * reach * seen);
        }
    }
}

/// Light arriving at world point `wpos` on a surface with normal `N` from
/// every headlight in the table. Zero-cost when the count is zero (one
/// uniform branch), which is every daylight hour.
float3 PSXHeadlights(float3 wpos, float3 N)
{
    float3 diff, spec;
    PSXHeadlightsCore(wpos, N, float3(0.0, 1.0, 0.0), 1.0, 0.0, 1.0, diff, spec);
    return diff;
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
