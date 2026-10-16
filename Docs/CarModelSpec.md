# PSX Racing — car model spec

For whoever builds car models for PSX Racing (the owner shares this with the
modelling assistant). The look is late-PS1 / early-PS2 — "Gran Turismo 2 with
cleaner surfaces" — running in a browser and on phones.

## Triangle budget

| Part | Target | Hard cap |
|---|---|---|
| Body, player / race car | 1,500 – 2,500 | 3,000 |
| Body, traffic / parked car | 800 – 1,500 | 2,000 |
| Each wheel (tyre + rim) | 60 – 120 | 160 |
| Mirrors (each) | 8 – 20 | 30 |
| **Whole car** | **≈ 2,000 – 3,000** | **3,500** |

Wheels: a 10–12-sided cylinder for the tyre, the rim as a flat textured disc
(or a shallow dish) on the outer face. 16 sides only for a close-up hero car.

## Where the triangles go (this is what makes it look smooth)

- **Silhouette first.** Spend triangles on what outlines the car: roofline,
  bumper corners, wheel arches (8–10 segments per arch), the curve of the
  bonnet and boot. Flat panels (doors, roof centre, bonnet centre) need very few.
- **Curved panels get even segments.** A rounded bumper corner or fender: 3–6
  evenly spaced segments across the curve, in a regular grid — not a fan of
  thin triangles.
- **No sliver triangles.** Keep triangles roughly even; nothing thinner than
  about 1:6. Irregular, randomly angled small triangles are what makes a
  flat-shaded bumper look crushed.
- **Quads, then triangulate.** Model in clean quads following the panel flow;
  triangulate on export.
- **Smoothing / normals.** Smooth shading within a panel, hard edges only at
  real creases (panel gaps, bumper lips, window frames). In Blender: Shade Auto
  Smooth at about 35–45°, or mark sharp edges by hand. Export the normals.
- **Details go in the texture, not the mesh.** Badges, wipers, door handles,
  grilles, vents, number plates, panel gaps: texture only.

## Geometry rules

- Closed and clean: no holes, no gaps between panels, no internal or
  duplicated faces, no faces stacked on top of each other.
- Normals face outward. The game culls back faces; a flipped face is invisible
  from outside.
- **Real wheel arches**: cut the arch out of the body and add a dark inner
  liner, rather than painting a black arch onto a flat panel.
- Tyres must not intersect the body at rest.
- Real-world scale in metres, matching the real car's length and wheelbase.

## Texture

- **One texture sheet per car, 256 × 256 PNG**, for body, glass, lights and
  wheels together. 512 × 512 only as an agreed exception. No other maps
  (no normal or specular maps).
- Pixel-art friendly: the game uses nearest-neighbour sampling, no mipmaps, no
  filtering. Aim for about 40–60 texels per metre on the body.
- Wheels and tyres on a **neutral** part of the sheet (not over the paint
  area), so a repaint does not recolour the tyres.
- **Glass** is dark and colourless (dark grey/blue-grey); the shader reflects
  the sky in dark, colourless texels.
- **Matte black** (tyres, arch liners, grille, bumper plastic) that should NOT
  reflect like glass: alpha = 128 on those texels. Everything else alpha = 255.
- Lights: tail lamps and headlamps painted on the sheet, clearly coloured
  (red/amber/clear) so the shader does not mistake them for glass. To make the
  lamps light up, add a lamp control map (next section).

## Lamp control map (lit lamps)

The owner's method, first used on the R32 (`gtr_89`: tail lamps 2026-10-15,
headlamps 2026-10-16). The lit lamp is authored art on the car's own atlas,
not a sprite or extra geometry.

- **One extra PNG per model**, the same size and UV layout as the body atlas.
  Every livery of the model shares it.
- **Channels (LINEAR data, not colour):**
  - R = rear running-light intensity, G = brake-light intensity;
  - B = headlamp projector core (the hot centre), A = clear-lens scatter (the
    soft glow across the headlamp glass). The alpha is DATA, not opacity.
  - 0 means "not a lamp". Paint only the lamp texels: no texel on any other
    surface may be non-zero (R/G only on the tail, B/A only on the nose).
- Paint the PATTERN of the bulb, not a flat lens. On the R32, R is a dim ring
  and G a brighter filled disc on each of the four round tail lamps; B is the
  projector disc and A the glass around it.
- Leave the body atlas unchanged. Its unlit lens art stays, and the shader
  multiplies the light by the lens texel (rear `lerp(0.55, 1, atlas.r × 2)`,
  head scatter `lerp(0.3, 1, luminance × 3)`) so the detail shows through.
- Name it `<modelKey>_lightmask.png` and put it in
  `Assets/PSXRacing/Resources/CarLights/`. The game finds it by the model key
  at runtime, so no car bake or material change is needed.
  - The first convention, `<modelKey>_lightmask_rg.png` (R/G only, B = 0,
    A = 255), still works: its B/A are never read, so it has no headlamps.
- Import it linear (sRGB off), point filter, clamp, no mipmaps, no
  compression, alpha source "from input" with "alpha is transparency" OFF.
  Copy the import settings from `gtr_89_lightmask.png.meta` (Resources is
  outside the builder's texture pass). The self-test fails a map imported any
  other way.
- What the game shows (`Shaders/PSXLampMap.cginc`):
  - rear = max(R × running colour × running strength × lights-on, G × brake
    colour × brake strength × brake) × lens detail. Braking works with the
    running lights off and does not double with them on. Running is on with
    the headlights; brake follows the brake pedal.
  - head = (B × core strength + A × scatter strength × lens detail) × low-beam
    colour × lights-on.
  - The colours and strengths are tuned once there (`LAMP_*`) for the game's
    LDR picture. The brake and the projector core clip and get the picture's
    halation (a halo); the running lamps and the lens scatter stay a glow
    without one.
- A car with a map does not draw the old additive tail-lamp sprites (nor the
  head-lamp sprites, when the map has B/A). Its red light on the road behind
  it and its headlamp beams stay.
- Reverse lights and indicators are not in the map yet.
- Check: `tools\r32lights-shots.ps1` (rear view off / running / braking /
  brake-only, at night and dusk; `-Mode lens` for the A/B lens pairs). The
  self-test checks each map for the right name, import settings and channels,
  that its R/G texels land only on rear-facing faces at the tail and its B/A
  texels only on forward-facing faces at the nose.

### Optional: separate lens covers (a lens variant)

The owner's R32 lens test (v04, 2026-10-16) adds a second build of the same
car beside the normal one, for comparison. A = the normal body (opaque
painted lamps). B = the lamp optics recessed into the body behind separate
clear covers:

- **Body:** the same body with each lamp face pushed in (R32: headlamp
  backings 22 mm, rear 12 mm) and dark cavity walls joining the old outline
  to the backing. Every other vertex and UV identical to A.
- **Two more objects:** `Headlight_Lenses` and `Taillight_Lenses`, thin outer
  shells following the lamp outlines (a little crown), UV'd onto the same
  lamp texels of the same atlas. Faces point outward. No glass thickness.
- Same atlas, same lamp control map, same frame (Y up, nose −Z), same wheels.
  It exceeds the triangle budget by design (R32: +460 triangles).
- In the game: `export_models.mjs` (`lens:`) writes `<key>_lens.obj`,
  `<key>_lens_head.obj` and `<key>_lens_tail.obj` beside the body;
  `CarModelBaker` puts them on the shell as its lens variant, and the covers
  get `PSX/CarLens` (premultiplied, no depth write, no shadow): opacity 0.06
  front / 0.09 rear, tint from the lens art 0.15 / 0.55, smoothness 0.90.
  25% of the head scatter and 20% of the rear light move onto the covers; the
  projector core stays on the backing; amber reflects and does not emit.
- B is a TEST: the player always sees A. In a debug career, the debug bench's
  CAR page offers LAMP LENSES: A OPAQUE / B COVERS on a shell that has a
  variant (key L flips it during a drive).

## File layout

- Export **OBJ + MTL + PNG** (FBX or GLB also fine).
- Objects: `Body`, `Wheel_FL`, `Wheel_FR`, `Wheel_RL`, `Wheel_RR`.
  Front/rear and left/right as the driver sees them.
- Each wheel's origin at its axle centre; it spins on its local X axis.
- Car origin at ground level, centred between the wheels.
- Y up, nose toward −Z in the OBJ (Blender: Z up, nose +Y, exported with
  forward −Z, up Y).
- One material, UVs inside 0–1, no overlapping UV islands except mirrored
  left/right halves.

## Checklist before handing over

- [ ] Triangle counts inside the table above
- [ ] No sliver triangles on bumpers or curved panels
- [ ] Auto-smooth / sharp edges set; normals exported
- [ ] No open edges, no duplicate or internal faces, normals outward
- [ ] Wheel arches cut out with liners; tyres clear of the body
- [ ] One 256 × 256 PNG; glass dark and neutral; matte black at alpha 128
- [ ] Objects named `Body`, `Wheel_FL/FR/RL/RR`; origins as above; real scale
- [ ] Previews: front ¾, rear ¾, side, and a plain untextured "clay" render of
      the rear, to check the bumper surfaces
