// STREET LAMPS (AND TAIL LAMPS), PER PIXEL, ON EVERY PSX SURFACE.
//
// The owner's ask, 2026-09-21, from Need for Speed (2015): "how street lights
// bathe the road". Until this file a street light was a 16 m additive disc
// laid flat 15 cm above the tarmac (the "Pool" quad): a flat yellow coin
// ADDED on top of whatever was under it. It never multiplied the road's own
// texture, so the lane paint and the asphalt grain under a lamp were washed
// out rather than lit; it never reached a kerb, a wall, a car parked under
// it or the car driving through it; it sat on top of the road and was
// depth-hidden the moment the road rose past it; and a city of them was a
// field of 16 m overdraw. The pool is retired; this is the light itself.
//
// What a street lamp pool IS, and what the numbers below say about it:
//   * It is HIGH-PRESSURE SODIUM. The game is set in 1999 USA and that is
//     the lamp on every American arterial of the decade: a warm orange-white,
//     nothing like the blue-white of a modern LED head. StreetLights owns the
//     colour (authored sRGB, pushed linear); this file only adds it, the same
//     split the halogen headlights have with CarLights.
//   * It LIGHTS DOWNWARD. A cobra-head luminaire throws its light into the
//     half-space under it and very little sideways. `cone` is the street
//     kind's shape: a surface the lamp is above takes all of it, one level
//     with the head a quarter, one above the head none - so a lamp does not
//     light the underside of the overpass it stands beside.
//   * It is a POOL, not a point. A 7-9 m pole lights a disc of road about
//     forty metres across and the light falls off smoothly to nothing at the
//     edge of that disc; (1 - x^2)^2 over the pool radius is the windowed
//     falloff every real-time engine uses for exactly this, because it
//     reaches zero AT the radius (a lamp dropped from the table cannot leave
//     a hard edge where it used to be) and it has no singularity under the
//     head.
//   * The WET ROAD reflects it. `PSXLampsSpec` is the Blinn-Phong glint of
//     each lamp in a glossy surface, reaching LAMP_SPEC_REACH pool radii -
//     the long orange streak a wet night road draws toward the camera from a
//     lamp sixty metres up the street, which is most of what "the street
//     lights bathe the road" looks like in the owner's reference. Its cone
//     has a softer floor (0.35) than the diffuse one: a reflection is seen
//     from far down the road, where the lamp is nearly level with the eye.
//     And a glint needs the lamp on the SEEN side of the surface (N.l >= 0),
//     as the diffuse always did: a wet deck does not mirror a lamp under it.
//   * A POINT lamp (kind 1) has no cone: a car's tail lamps, one entry per
//     car at the midpoint of its two lenses, a short red pool that lights
//     the rain spray, the wet road behind the car and the bonnet of the car
//     following it.
//
// WHY PER PIXEL. A road ribbon has a vertex every few metres and a lamp pool
// has an edge measured in centimetres; per vertex, a pool on a 10 m road
// segment is a triangle-shaped smear that crawls as the camera moves. It is
// also the one lighting term a wet surface needs per pixel, because a glint
// is a highlight a few pixels wide. At twelve lamps it is still cheap: a
// phone GPU does twelve dot products per pixel without noticing, and the
// early-out below makes it free in daylight.
//
// WHY TWELVE SLOTS. A street at night shows perhaps six or eight lamps near
// enough to light anything the camera can see; the rest are halos in the
// distance (PSX/Halo draws those; a halo costs nothing per pixel of road).
// StreetLights fills the table from the camera about to render: lamps whose
// pool can reach the frustum, nearest first, at most four tail lamps, and it
// dims the last one in so the one about to be swapped out is already dark -
// the swap never pops. Twelve is also comfortably inside the uniform budget
// of a WebGL2 fragment shader that already carries eight headlights.
//
// THE STALE-GLOBAL RULE, the same as the headlights': StreetLights pushes a
// count EVERY time a camera renders, zero when nothing is lit. A global
// nothing writes is a stale one, not an empty one - a scene that loaded
// after a night race would otherwise keep the last race's lamps lit on its
// floor. And a global array's length is locked the first time it is set, so
// the C# arrays are always exactly PSX_MAX_LAMPS long (StreetLights.MaxLamps
// must equal it).
//
// GLES/WebGL rules, copied from PSXHeadlights.cginc: a constant-bound loop
// with `if (j >= count) break;`, the count read from a float global, one
// uniform early-out, every division and normalise guarded (a NaN in a GLSL ES
// clamp is a black pixel on a phone and a white one on a desktop).
//
// These globals are UNIFORMS ONLY. A Properties entry of the same name would
// shadow the global with the material's own value (zero) - the lamps would
// light nothing and nothing would say why.
#ifndef PSX_LAMPS_INCLUDED
#define PSX_LAMPS_INCLUDED

#define PSX_MAX_LAMPS 12

// How far a lamp's REFLECTION reaches, in pool radii. The diffuse pool stops
// at one radius; a glint on a wet road is a mirror image and is still there
// four radii off (88 m for a 22 m street pool), which is what makes a wet
// street read as wet from the driver's seat.
#ifndef LAMP_SPEC_REACH
#define LAMP_SPEC_REACH 4.0
#endif

// A POINT lamp does not light its OWN car. The tail-lamp entry hangs a
// quarter of a metre behind the bumper, and a rear panel facing it at that
// range takes the full pool - a braking car would paint its own back end
// red. A real tail lamp's lens faces AWAY from the panel it is set into, so
// nothing within a lens-width of it is in its beam; the table carries no
// direction, so the same thing is said as distance: no light inside
// POINT_NEAR_M, full light a POINT_NEAR_RAMP further out. The road behind
// the car (a metre down and back) and the bonnet of the car following it
// are outside it; the car's own bodywork is not.
#define POINT_NEAR_M    0.9
#define POINT_NEAR_RAMP 0.8

float  _PSXLampCount;
float4 _PSXLampPos[PSX_MAX_LAMPS];    // xyz lamp head (world), w = pool radius (m)
float4 _PSXLampColor[PSX_MAX_LAMPS];  // rgb = LINEAR colour x intensity x edge fade, w = kind (0 street: aimed down, 1 point/omni)

/// THE ONE LOOP. Diffuse and specular from every lamp in the table, sharing
/// each lamp's L / distance / falloff, so a shader that wants both (the wet
/// road, the car paint) walks the table once rather than twice.
///   facingOn 1: the diffuse takes the surface's N.L (a surface).
///   facingOn 0: it does not (a raindrop, a particle, a volume - PSXLampsAt).
///   specOn   1: also the Blinn-Phong glints at `power`; 0 skips them. Both
///            flags are literals at every call site, so the branches fold
///            away at compile time; where they do not, they are uniform.
void PSXLampsCore(float3 wpos, float3 N, float3 V, float power, float specOn, float facingOn,
                  out float3 diff, out float3 spec)
{
    diff = float3(0.0, 0.0, 0.0);
    spec = float3(0.0, 0.0, 0.0);
    if (_PSXLampCount < 0.5) return;
    int count = (int)_PSXLampCount;
    for (int j = 0; j < PSX_MAX_LAMPS; j++)
    {
        if (j >= count) break;
        float4 P = _PSXLampPos[j];
        float4 C = _PSXLampColor[j];
        float radius = max(P.w, 0.5);
        float3 L = P.xyz - wpos;
        float d = max(length(L), 0.05);
        float3 l = L / d;
        // 1 for the street kind (w = 0), 0 for a point lamp (w = 1).
        float street = 1.0 - step(0.5, C.w);

        // The pool: windowed inverse-square-ish falloff, zero AT the radius.
        float x = saturate(d / radius);
        float att = 1.0 - x * x;
        att *= att;
        // Not its own car (see POINT_NEAR_M); street lamps are never that close.
        float nearCut = lerp(saturate((d - POINT_NEAR_M) / POINT_NEAR_RAMP), 1.0, street);
        att *= nearCut;
        // Aimed down: full under the head, a quarter level with it, none
        // above it. A point lamp lights every way.
        float cone = lerp(1.0, saturate(l.y * 1.5 + 0.25), street);
        float facing = facingOn > 0.5 ? saturate(dot(N, l)) : 1.0;
        diff += C.rgb * (att * cone * facing);

        if (specOn > 0.5)
        {
            float3 hv = l + V;
            float3 H = hv * rsqrt(max(dot(hv, hv), 1e-6));
            float nh = max(saturate(dot(N, H)), 1e-4);
            // The reflection outlives the pool by LAMP_SPEC_REACH radii.
            float reach = 1.0 - saturate(d / (radius * LAMP_SPEC_REACH));
            reach *= reach;
            float coneS = lerp(1.0, saturate(l.y * 1.5 + 0.35), street);
            // Only a lamp on the SEEN side of the surface can be mirrored in
            // it. Blinn-Phong alone does not know that: with the eye low over
            // a wet deck, H = normalize(l + V) still leans up toward N for a
            // lamp BELOW the deck (the sodium head under an overpass, the
            // tail lamp of a car on the road beneath a bridge), and the deck
            // printed a glint of a light that is on the far side of the
            // concrete. The diffuse has always been gated by `facing`; the
            // glint had nothing. A hard step, not a ramp, on purpose: the
            // grazing glints are the long streaks down the street, and every
            // lamp above the plane - however low - keeps its full streak; the
            // only light cut is one the surface itself stands in front of,
            // which is also where a real reflection ends (at the horizon of
            // the surface, sharply).
            float seen = step(0.0, dot(N, l));
            spec += C.rgb * (pow(nh, power) * reach * coneS * nearCut * seen);
        }
    }
}

/// Diffuse and glints in one walk of the table (see PSXLampsCore).
void PSXLampsBoth(float3 wpos, float3 N, float3 V, float power, float specOn,
                  out float3 diff, out float3 spec)
{
    PSXLampsCore(wpos, N, V, power, specOn, 1.0, diff, spec);
}

/// Light arriving at world point `wpos` on a surface with normal `N` from
/// every lamp in the table. Zero-cost when the count is zero (one uniform
/// branch), which is every clear daylight hour: the street lamps are the
/// hour's, and a car's tail lamp only lights the world while its running
/// lights are on (CarLights says why).
float3 PSXLamps(float3 wpos, float3 N)
{
    float3 diff, spec;
    PSXLampsCore(wpos, N, float3(0.0, 1.0, 0.0), 1.0, 0.0, 1.0, diff, spec);
    return diff;
}

/// The same light at a point with no surface: no N.L term. Rain streaks,
/// spray and anything else that is lit from every side at once.
float3 PSXLampsAt(float3 wpos)
{
    float3 diff, spec;
    PSXLampsCore(wpos, float3(0.0, 1.0, 0.0), float3(0.0, 1.0, 0.0), 1.0, 0.0, 0.0, diff, spec);
    return diff;
}

/// The lamps' glints in a glossy surface seen along `V` (unit, surface to
/// eye): Blinn-Phong at `power`, reaching LAMP_SPEC_REACH pool radii. Not
/// energy-normalised on purpose - the includer's gain is the whole look.
float3 PSXLampsSpec(float3 wpos, float3 N, float3 V, float power)
{
    float3 diff, spec;
    PSXLampsCore(wpos, N, V, power, 1.0, 1.0, diff, spec);
    return spec;
}
#endif
