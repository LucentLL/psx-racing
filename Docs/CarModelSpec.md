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
  tail lamps light up, add a lamp control map (next section).

## Lamp control map (lit tail lamps)

The owner's method, first used on the R32 (`gtr_89`, 2026-10-15). The lit lamp
is authored art on the car's own atlas, not a sprite or extra geometry.

- **One extra PNG per model**, the same size and UV layout as the body atlas.
  Every livery of the model shares it.
- **Channels (LINEAR data, not colour):** R = running-light intensity,
  G = brake-light intensity, B = 0, A = 255. 0 means "not a lamp".
  Paint only the lamp texels: no texel on any other surface may be non-zero.
- Paint the PATTERN of the bulb, not a flat lens. On the R32, R is a dim ring
  and G a brighter filled disc, on each of the four round lamps.
- Leave the body atlas unchanged. Its unlit lens art stays, and the shader
  multiplies the light by the lens texel (`lerp(0.55, 1, atlas.r × 2)`) so
  the detail shows through.
- Name it `<modelKey>_lightmask_rg.png` and put it in
  `Assets/PSXRacing/Resources/CarLights/`. The game finds it by the model key
  at runtime, so no car bake or material change is needed.
- Import it linear (sRGB off), point filter, clamp, no mipmaps, no
  compression. Copy the import settings from `gtr_89_lightmask_rg.png.meta`
  (Resources is outside the builder's texture pass). The self-test fails a map
  imported any other way.
- The game shows emission = max(R × running colour × running strength ×
  lights-on, G × brake colour × brake strength × brake) × lens detail.
  - Braking works with the running lights off and does not double with them on.
  - Running is on with the headlights; brake follows the brake pedal.
  - The colours and strengths are tuned once in `PSX/CarPaint` (`LAMP_*`) for
    the game's LDR picture. Bright brake texels get the picture's halation (a
    halo); running lamps stay a dim glow without one.
- A car with a map does not draw the old additive tail-lamp sprites. Its red
  light on the road behind it stays.
- Reverse lights and indicators are not in the map yet (B is reserved).
- Check: `tools\r32lights-shots.ps1` (rear view off / running / braking /
  brake-only, at night and dusk). The self-test checks each map for the right
  name, import settings and channels, and that its lit texels land only on
  rear-facing faces at the car's tail.

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
