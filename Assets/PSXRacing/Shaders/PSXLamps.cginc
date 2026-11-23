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
//   * It is a WARM YELLOW BULB. It was high-pressure sodium orange until the
//     owner asked for "natural yellow like bulbs from the 90's, similar to
//     car headlights" (2026-09-21): nothing like the blue-white of a modern
//     LED head either way. StreetLights owns the colour (authored sRGB,
//     pushed linear); this file only adds it, the same split the halogen
//     headlights have with CarLights.
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
#include "PSXSpecBend.cginc"

#define PSX_MAX_LAMPS 20   // (20 since the lightpop round: sixteen street lamps on PC; today's table and phones fill twelve)

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

// THE LAMP FIELD (the city-night pass, 2026-10-05; StreetLights.Field.cs
// says why). A lit city street's lamps are not in the table above: every one
// within 250 m of the eye is splatted top-down into one camera-centred
// RGBAHalf texture - R the pool, G the wide scattered glow, B/G the
// glow-weighted head height above _PSXLampFieldY.x, A the MERCURY-VAPOUR
// lamps' part of G (the 1990s city, 2026-10-29: by street) - read here with
// ONE fetch, at any number of lamps. _PSXLampFieldST.w is the switch: only
// Charlotte's tiles mark lamps, so on every other venue it is 0 (pushed 0
// every frame) and this is one uniform branch - the owner's dark night on
// the rural, mountain and Town roads is untouched. While it is on, the
// table's STREET entries give only their wet glints (their diffuse is the
// field's); the tail lamps (points) are unchanged.
// (lightlook) ONE sampler state for every field texture (the near field, the far field and the
// two being faded from): PSX/Lit's HD variant is at D3D11's 16 sampler states (its own header,
// THE SAMPLER BUDGET), so the far and fading textures share the near field's (all bilinear, clamp)
// (NIGHT 3: the same block in PSXHeadlights.cginc, which is included first, with the car pools'
// texture beside them on the same sampler state; whichever comes first declares them)
#ifndef PSX_FIELD_TEX_DECLARED
#define PSX_FIELD_TEX_DECLARED
#ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
Texture2D _PSXLampField, _PSXLampFar, _PSXLampFieldPrev, _PSXLampFarPrev, _PSXCarPool, _PSXHeadVis;
SamplerState sampler_PSXLampField;
#define PSX_FIELD_LOD(t, uv) t.SampleLevel(sampler_PSXLampField, uv, 0.0)
#else
sampler2D _PSXLampField, _PSXLampFar, _PSXLampFieldPrev, _PSXLampFarPrev, _PSXCarPool, _PSXHeadVis;
#define PSX_FIELD_LOD(t, uv) tex2Dlod(t, float4(uv, 0.0, 0.0))
#endif
#endif
#ifndef PSX_NIGHT4_DECLARED
#define PSX_NIGHT4_DECLARED
// NIGHT 4 (StreetLights.Night4.cs): x the lit city's night-sky weight (eased), y the sky's mix to
// its gradient, z the most a lamp-lit surface's effective albedo reads, w on (0: NIGHT 3 as it was)
float4 _PSXNight4;
#endif
float4 _PSXLampFieldST;   // xy window origin (world xz), z 1/span, w on
float4 _PSXLampFieldY;    // x base height, yz uptown centre (xz), w 1/(outer-inner)
float4 _PSXFieldWhite;    // rgb a mercury-vapour head (linear, the bulb's luminance), w uptown outer radius (m)
float4 _PSXFieldWarm;     // rgb the bulb (linear), w uptown's gain (its heads were white LED: the same light, warm)

// How far under the heads the pool and the glow reach: a street is 6-12 m
// under its heads; the ground under a lit deck (6-8 m of clearance, the
// heads 9 m over the deck: 15-17 m) is not lit by it - at 22 m it was, the
// owner 2026-10-31: "lights go through bridges to the ground below". Whole
// to 12 m under, gone by 16. A surface above the heads (a tower's upper
// storeys, the deck over a lit street) takes none of the pool and only the
// glow's lowest fringe.
#define FIELD_UNDER_M    16.0
#define FIELD_UNDER_RAMP 0.25   // 1 / the metres the reach fades over
#define FIELD_GLOW_UP_M 8.0

// NO LIGHT POPS (the lightpop round, 2026-11-22; StreetLights.NoPop.cs). The
// field used to fade over the outer tenth of its WINDOW - a square that steps
// 32 m at a time - so a pool 200 m up the street jumped between a third and
// nine tenths of itself every 32 m of driving. With _PSXLampFieldST.w = 2 it
// fades by its distance from the EYE instead: whole to FIELD_FULL_M, gone by
// FIELD_END_M, the same whichever window is shown (StreetLights keeps the eye
// within FIELD_SLACK of the shown window's centre, so the circle is always
// inside it).
#define FIELD_END_M  200.0
#define FIELD_FULL_M 160.0

// THE ROW OF POOLS TO THE DISTANCE (the lightlook round, 2026-11-23;
// StreetLights.FarField.cs): the same splat of the same lamps in a 2 km window
// of 8 m texels, taken exactly where the near field lets go (the complement of
// its eye fade) and gone by _PSXLampFarY.y. _PSXLampFarST.w = 0: off (one
// uniform branch; and only for a pixel past FIELD_FULL_M).
float4 _PSXLampFarST;     // xy window origin (world xz), z 1/span, w on
float4 _PSXLampFarY;      // x base height, y end (m), z 1/(end - full)
// A LAMP CHANGE IS FADED (StreetLights.Blend.cs): the texture a rebuild replaced, its window
// origin (xy), its base height (z) and its remaining weight (w; 0 = no fade running)
float4 _PSXLampFieldPrevST;
float4 _PSXLampFarPrevST;

/// The lit street's light at `wpos` on a surface facing `N` (facingOn 0: no
/// facing term - rain, decals laid on the road). Zero off the city.
float3 PSXLampField(float3 wpos, float3 N, float facingOn)
{
    if (_PSXLampFieldST.w < 0.5) return float3(0.0, 0.0, 0.0);
    float2 uv = (wpos.xz - _PSXLampFieldST.xy) * _PSXLampFieldST.z;
    float4 f = PSX_FIELD_LOD(_PSXLampField, uv);
    float fBaseY = _PSXLampFieldY.x;
    // NIGHT 3 (the city-night round, StreetLights.Night3.cs; w = 3): the STATIC field - world
    // texels splatted once, a window bigger than the far plane, and no fade by the eye's
    // distance: a patch of street is lit the same from the far plane to the bumper
    if (_PSXLampFieldST.w > 2.5)
    {
        float hY3 = fBaseY + f.b / max(f.g, 1e-4);
        float un3 = hY3 - wpos.y;
        float vP3 = saturate((un3 + 1.0) * 0.5) * saturate((FIELD_UNDER_M - un3) * FIELD_UNDER_RAMP);
        float vG3 = saturate((un3 + FIELD_GLOW_UP_M) / FIELD_GLOW_UP_M) * saturate((FIELD_UNDER_M - un3) * FIELD_UNDER_RAMP);
        // NIGHT 4: a face is lit near its lamp's height and falls off above it - the glow whole
        // to the head and gone two metres over it (it reached eight: lit faces to the roof),
        // and a face (not the ground) takes the pool only up to the head
        if (_PSXNight4.w > 0.5)
        {
            vG3 = saturate((un3 + 2.0) * 0.5) * saturate((FIELD_UNDER_M - un3) * FIELD_UNDER_RAMP);
            vP3 *= lerp(saturate(un3 * 2.0), 1.0, saturate(N.y * 2.0 - 0.6));
        }
        float upP3 = facingOn > 0.5 ? saturate(0.55 + 0.45 * N.y) : 1.0;
        float upG3 = facingOn > 0.5 ? saturate(0.75 + 0.25 * N.y) : 1.0;
        float2 dc3 = wpos.xz - _PSXLampFieldY.yz;
        float upt3 = saturate((_PSXFieldWhite.w - length(dc3)) * _PSXLampFieldY.w);
        float3 hue3 = lerp(_PSXFieldWarm.rgb, _PSXFieldWhite.rgb, saturate(f.a / max(f.g, 1e-4)))
                    * (1.0 + (_PSXFieldWarm.w - 1.0) * upt3);
        // the window's own outer 64 m (past the far plane by construction) and nothing outside it
        float2 e3 = saturate(min(uv, 1.0 - uv) / max(64.0 * _PSXLampFieldST.z, 1e-6));
        return hue3 * ((max(f.r, 0.0) * vP3 * upP3 + max(f.g, 0.0) * vG3 * upG3) * (e3.x * e3.y));
    }
    if (_PSXLampFieldPrevST.w > 0.0)
    {
        // (lightlook) cross-fading from the texture a lamp change replaced: each re-based to
        // absolute head heights (B + G x its base), mixed
        float4 fp = PSX_FIELD_LOD(_PSXLampFieldPrev, (wpos.xz - _PSXLampFieldPrevST.xy) * _PSXLampFieldST.z);
        f.b += f.g * fBaseY; fp.b += fp.g * _PSXLampFieldPrevST.z;
        f = lerp(f, fp, _PSXLampFieldPrevST.w);
        fBaseY = 0.0;
    }
    // fade out over the outer tenth of the window (beyond ~200 m the halos
    // stand for the lamps), and never a NaN from a texel nothing reached;
    // NO LIGHT POPS (w = 2): by the distance from the eye
    float2 b = saturate(min(uv, 1.0 - uv) * 10.0);
    float eyeR = length(wpos.xz - _WorldSpaceCameraPos.xz);
    float edge = lerp(b.x * b.y, saturate((FIELD_END_M - eyeR) / (FIELD_END_M - FIELD_FULL_M)), step(1.5, _PSXLampFieldST.w));
    float headY = fBaseY + f.b / max(f.g, 1e-4);
    float under = headY - wpos.y;
    float vertP = saturate((under + 1.0) * 0.5) * saturate((FIELD_UNDER_M - under) * FIELD_UNDER_RAMP);
    float vertG = saturate((under + FIELD_GLOW_UP_M) / FIELD_GLOW_UP_M) * saturate((FIELD_UNDER_M - under) * FIELD_UNDER_RAMP);
    // the pool falls from above: a road takes it all, a wall or a car's side
    // 0.55, an underside none; the glow comes from every side
    float upP = facingOn > 0.5 ? saturate(0.55 + 0.45 * N.y) : 1.0;
    float upG = facingOn > 0.5 ? saturate(0.75 + 0.25 * N.y) : 1.0;
    float2 dc = wpos.xz - _PSXLampFieldY.yz;
    float uptown = saturate((_PSXFieldWhite.w - length(dc)) * _PSXLampFieldY.w);
    float3 hue = lerp(_PSXFieldWarm.rgb, _PSXFieldWhite.rgb, saturate(f.a / max(f.g, 1e-4)))
               * (1.0 + (_PSXFieldWarm.w - 1.0) * uptown);
    float3 lit = hue * ((max(f.r, 0.0) * vertP * upP + max(f.g, 0.0) * vertG * upG) * edge);
    // the lightlook round: the far field past the near one (NO LIGHT POPS' eye fade only)
    if (_PSXLampFarST.w > 0.5 && _PSXLampFieldST.w > 1.5 && eyeR > FIELD_FULL_M)
    {
        float2 uvF = (wpos.xz - _PSXLampFarST.xy) * _PSXLampFarST.z;
        float4 g = PSX_FIELD_LOD(_PSXLampFar, uvF);
        float gBaseY = _PSXLampFarY.x;
        if (_PSXLampFarPrevST.w > 0.0)
        {
            float4 gp = PSX_FIELD_LOD(_PSXLampFarPrev, (wpos.xz - _PSXLampFarPrevST.xy) * _PSXLampFarST.z);
            g.b += g.g * gBaseY; gp.b += gp.g * _PSXLampFarPrevST.z;
            g = lerp(g, gp, _PSXLampFarPrevST.w);
            gBaseY = 0.0;
        }
        float hY = gBaseY + g.b / max(g.g, 1e-4);
        float uF = hY - wpos.y;
        float vP = saturate((uF + 1.0) * 0.5) * saturate((FIELD_UNDER_M - uF) * FIELD_UNDER_RAMP);
        float vG = saturate((uF + FIELD_GLOW_UP_M) / FIELD_GLOW_UP_M) * saturate((FIELD_UNDER_M - uF) * FIELD_UNDER_RAMP);
        float3 hueF = lerp(_PSXFieldWarm.rgb, _PSXFieldWhite.rgb, saturate(g.a / max(g.g, 1e-4)))
                    * (1.0 + (_PSXFieldWarm.w - 1.0) * uptown);
        float wF = (1.0 - edge) * saturate((_PSXLampFarY.y - eyeR) * _PSXLampFarY.z);
        lit += hueF * ((max(g.r, 0.0) * vP * upP + max(g.g, 0.0) * vG * upG) * wF);
    }
    return lit;
}

/// The street level under `wpos` as the field knows it (the heads' mean
/// height less a cobra-head's 8.5 m): the facades' storeys are counted from
/// it. Off the field, the eye's base less the eye height.
float PSXFieldStreetY(float3 wpos)
{
    float2 uv = (wpos.xz - _PSXLampFieldST.xy) * _PSXLampFieldST.z;
    float4 f = PSX_FIELD_LOD(_PSXLampField, uv);
    return f.g > 0.008 ? _PSXLampFieldY.x + f.b / f.g - 8.5 : _PSXLampFieldY.x - 1.2;
}

// THE MIRROR IMAGE OF A LAMP (the car paint, specOn 2 below): see the block
// over the same function in PSXHeadlights.cginc. Defined in both, guarded.
#ifndef PSX_LAMP_IMAGE_DEFINED
#define PSX_LAMP_IMAGE_DEFINED
float PSXLampImage(float3 R, float3 l, float dist, float lensR, float rough)
{
    float aL = lensR / max(dist, 0.05);
    float a2 = max(aL * aL + rough * rough, 1e-7);
    float3 dv = R - l;
    float q = 1.0 + dot(dv, dv) / a2;
    return (aL * aL / a2) / (q * q);
}
#endif
// The lens a coat mirrors (m): a cobra head's bowl, a tail lamp's lens.
#define LAMP_LENS_STREET 0.25
#define LAMP_LENS_POINT  0.12

float  _PSXLampCount;
// THE LEADING TAIL LAMPS (the lightpop round, 2026-11-22): how many of the
// table's first entries are point lamps when every point comes before every
// street lamp, else the whole count. With the lamp field on, a street entry
// gives a surface NO diffuse (the field's pool is its light), so a
// diffuse-only walk (specOn 0) stops after the points - the same picture for
// a fraction of the loop, which pays for the PC's sixteen street slots.
float  _PSXLampPoints;
float4 _PSXLampPos[PSX_MAX_LAMPS];    // xyz lamp head (world), w = pool radius (m)
float4 _PSXLampColor[PSX_MAX_LAMPS];  // rgb = LINEAR colour x intensity x edge fade, w = kind (0 street: aimed down, 1 point/omni)

/// THE ONE LOOP. Diffuse and specular from every lamp in the table, sharing
/// each lamp's L / distance / falloff, so a shader that wants both (the wet
/// road, the car paint) walks the table once rather than twice.
///   facingOn 1: the diffuse takes the surface's N.L (a surface).
///   facingOn 0: it does not (a raindrop, a particle, a volume - PSXLampsAt).
///   specOn   1: also the Blinn-Phong glints at `power`; 0 skips them; 2
///            the LENS'S MIRROR IMAGE (PSXLampImage) with `power` read as the
///            coat's roughness in radians and `V` as the mirror ray itself -
///            the car paint. All the flags are
///            literals at every call site, so the branches fold away at
///            compile time; where they do not, they are uniform.
void PSXLampsCore(float3 wpos, float3 N, float3 V, float power, float specOn, float facingOn,
                  out float3 diff, out float3 spec)
{
    diff = PSXLampField(wpos, N, facingOn);
    spec = float3(0.0, 0.0, 0.0);
    float fieldOn = step(0.5, _PSXLampFieldST.w);
    if (_PSXLampCount < 0.5) return;
    int count = (int)_PSXLampCount;
    if (specOn < 0.5 && fieldOn > 0.5) count = (int)_PSXLampPoints;
    for (int j = 0; j < PSX_MAX_LAMPS; j++)
    {
        if (j >= count) break;
        float4 P = _PSXLampPos[j];
        float4 C = _PSXLampColor[j];
        float radius = max(P.w, 0.5);
        float3 L = P.xyz - wpos;
        float d = max(length(L), 0.05);
        // (parkwaytunnel) past everything this lamp gives - its pool (zero AT
        // the radius) and, on a wet surface, its glint's reach - it adds
        // exactly nothing: skip the rest (the tunnels' wall lamps burn by
        // day, and every pixel on screen walked them all)
        if (d >= radius * (specOn > 0.5 ? LAMP_SPEC_REACH : 1.0)) continue;
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
        // a street lamp's diffuse is the field's while the field is on
        diff += C.rgb * (att * cone * facing * (1.0 - street * fieldOn));

        if (specOn > 0.5)
        {
            float nh;
            if (specOn > 1.5)
                nh = PSXLampImage(V, l, d, lerp(LAMP_LENS_POINT, LAMP_LENS_STREET, street), power);   // (V IS the mirror ray here)
            else
            {
                float3 hv = l + V;
                float3 H = hv * rsqrt(max(dot(hv, hv), 1e-6));
                nh = pow(max(saturate(dot(N + _PSXSpecBend, H)), 1e-4), power);   // (+ the water's own tilt: RAIN 2's rings, PSXSpecBend.cginc)
            }
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
            spec += C.rgb * (nh * reach * coneS * nearCut * seen);
        }
    }
}

/// Diffuse and glints in one walk of the table (see PSXLampsCore).
void PSXLampsBoth(float3 wpos, float3 N, float3 V, float power, float specOn,
                  out float3 diff, out float3 spec)
{
    PSXLampsCore(wpos, N, V, power, specOn, 1.0, diff, spec);
}

/// The car paint's walk: the diffuse on `N`, and each lamp's mirror image
/// along the mirror ray `R` in a coat of roughness `rough` (radians) - see
/// PSXLampImage.
void PSXLampsPaint(float3 wpos, float3 N, float3 R, float rough, out float3 diff, out float3 spec)
{
    PSXLampsCore(wpos, N, R, rough, 2.0, 1.0, diff, spec);
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
