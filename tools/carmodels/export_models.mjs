// Split the boggle vehicle pack into the shape PSX Racing wants: per model, a
// body OBJ and a wheels OBJ, plus a folder of colour skins with filesystem-safe
// names.
//
// Two files rather than one tagged file because Unity's OBJ importer splits on
// MATERIAL, not on object: the pack paints a whole car from a single 128x128
// sheet, so an OBJ carrying `o body` and `o wheel_FL` imports as one merged
// mesh named "default" and the axles are gone. Splitting at the file boundary
// is the only split the importer is guaranteed to honour.
//
// Nothing here moves geometry. Every vertex keeps the coordinate it was
// authored at, and the Unity-side baker (CarModelBaker) measures the imported
// mesh to find the axles - guessing at OBJ->Unity axis conventions in a text
// tool is how a car ends up driving backwards, and the editor can just look.
//
//   node tools/carmodels/export_models.mjs
//
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const project = path.resolve(here, '..', '..');
const pack = 'C:/Users/mcgee/OneDrive/Documents/Game Development/PSX Assets/psx_vehicles_by_boggle_V28062025/psx_vehicles_by_boggle';
const smallPickup = 'C:/Users/mcgee/OneDrive/Documents/Game Development/PSX Assets/small_pickup/small_pickup';
const outRoot = path.join(project, 'Assets/PSXRacing/Art/Car/Models');
const ownerCars = 'C:/Users/mcgee/OneDrive/Documents/Game Development/PSX Assets/PSX Racing/Cars';

// `parts` lists the body objects to keep, in source naming. Wheels are found by
// name and never listed. Leaving `parts` null keeps every non-wheel object,
// which is what all but the winged Charger want.
const MODELS = [
  { key: 'supra_a80',    obj: `${pack}/Japanese/grand_tourer/grand_tourer.obj` },
  { key: 'skyline_r32',  obj: `${here}/converted/4WD_Sport.obj`,
    tex: `${pack}/Japanese/4WD_Sport/textures` },
  { key: 'jdm_pickup',   obj: `${smallPickup}/small_pickup.obj` },

  { key: 'gto_66',       obj: `${pack}/American/2_door_coupe/2_door_coupe.obj` },
  { key: 'mustang_67',   obj: `${pack}/American/grand_tourer/grand_tourer.obj` },
  // One model, two cars: the nose cone and the tall wing are separate objects,
  // so dropping them gives the plain '69 Charger the Daytona was built from.
  // Both take the plain colours. The pack's stripe variants paint the tail
  // black or white on the sheet, and on the winged car that is the WING — a
  // slab of flat black hanging over the boot, which reads as a hole rather
  // than as a stripe.
  { key: 'charger_69',   obj: `${pack}/American/muscle/muscle.obj`, parts: ['2_seat_coupe'],
    skins: /^(?!.*_(black|white|no)_stripe$)/ },
  { key: 'daytona_69',   obj: `${pack}/American/muscle/muscle.obj`,
    skins: /^(?!.*_(black|white|no)_stripe$)/ },

  { key: 'bmw_e30',      obj: `${pack}/European/2door_saloon/2door_saloon.obj` },
  { key: 'audi_saloon',  obj: `${pack}/European/4_door_saloon/4_door_saloon.obj` },
  { key: 'euro_hatch',   obj: `${pack}/European/compact_hatchback/compact_hatchback.obj` },
  { key: 'volvo_estate', obj: `${pack}/European/estate/estate.obj` },
  { key: 'citroen_cx',   obj: `${pack}/European/executive/executive.obj` },
  { key: 'mb_pagoda',    obj: `${pack}/European/grand_tourer/grand_tourer.obj` },
  { key: 'landrover',    obj: `${pack}/European/pickup/pickup.obj` },
  { key: 'classic_van',  obj: `${pack}/European/van/van.obj` },

  // The owner's EG Civic (2026-09-25), from its own folder. One atlas, so
  // `skins` names it — the folder also holds reference shots and renders
  // that must not become liveries. Sized to GT4's own spec sheet for the
  // SiR-II (EG): "reference dimensions should be in GT4 specs for scaling".
  // The FAITHFUL revision (2026-09-25, "best and newest"): built to
  // Docs/CarModelSpec.md on the original Civic's proportions - 1,740 body
  // tris, 92 per wheel, sealed, matte alpha 128, red/amber/clear tail lamps,
  // stock-size rims. Replaced the repaired shell whose bumper looked "run
  // over by a train" (and the spec_rebuild / uv_corrected tries between).
  // 2026-10-02: the same body with an FF UNDERBODY - one 512x256 atlas, the
  // old 256 sheet untouched in its left half, the underside in the right
  // (the folder root's eg_civic.obj; "older cleaned/source_psx variants are
  // not this update"). PSXTextureCaps keeps a *_512x256 sheet at 512.
  // 2026-10-05: the owner's independent rebuild of the 1993 hatch
  // (Cars/EG_Independent_Textured_v01: 3,994 tris with the wheels, one red
  // 512x256 atlas, underside in the right half). GLB only -> glb2obj.py ->
  // converted/hatch_93_v01. `skinAs` gives the copied atlas a neutral name.
  // 2026-10-05 later: revision 03 (spoiler lowered 34 mm to meet the revised
  // rear glass, the painted strip between them gone; 3,970 tris, atlas
  // unchanged), converted/hatch_93_v03.
  { key: 'civic_eg',     obj: `${here}/converted/hatch_93_v03/hatch_93_v03.obj`,
    tex: `${ownerCars}/EG_Independent_Textured_v03`, skins: /^eg_red_512x256$/,
    skinAs: 'hatch_93_red_512x256', gt4LengthM: 4.070 },
  // The owner's S13 hatch (the 180SX that America sold as the 240SX) — GT4's
  // "Nissan 240SX `96": 4520 mm, 2475 mm wheelbase. The S14 is its own row.
  // 2026-10-04 ("NSX, 240SX, and RX7 were updated"): the independent 1989
  // S13 rebuild, Cars/S13_180SX_Independent_v07 - pop-up lamps with level
  // lenses in closed casings, stepped early tail lamps, 3,937 tris with the
  // wheels, one 512x256 atlas (underside in the right half). GLB only, so
  // glb2obj.py made converted/nissan_180sx_v07 from s13_1989_down.glb and
  // its _up twin from s13_1989_up.glb (same atlas, same frame), which
  // becomes the `lampsUp` body. Replaced Nissan_240SX_PSX/reference_v2.
  // 2026-10-05: revision 08 (rear-panel pass: hatch perimeter, bumper joint,
  // quarter seams; 3,997 tris), converted/fastback_89_v08, same layout as v07.
  { key: 'nissan_180sx', obj: `${here}/converted/fastback_89_v08/fastback_89_v08.obj`,
    lampsUp: `${here}/converted/fastback_89_v08/fastback_89_v08_up.obj`,
    tex: `${ownerCars}/S13_180SX_Independent_v08`, skins: /^s13_512x256$/,
    gt4LengthM: 4.520 },

  // The owner's FD RX-7 (2026-10-04), Cars/FD_Shared_Independent_v01/
  // Textured_08: one body in three spoilers, each with its lamps closed and
  // raised, on one shared 512x256 atlas. GT4's FD sheets: 4280 mm (4285-4295
  // on a few early rows), wb 2425. The spoilers are years, as the README
  // names them: the 1993 hoop spoiler on every FD up to `97, the 1999
  // adjustable wing on the `98 Type RS (the 280 PS series-5 car), and no
  // spoiler at all (no catalog FD is wingless - the shell is there for the
  // car that is). The built-in FD (rx7_fd, the scene's own) is unchanged.
  { key: 'rx7_fd_93', obj: `${here}/converted/rx7_fd_93/rx7_fd_93.obj`,
    lampsUp: `${here}/converted/rx7_fd_93/rx7_fd_93_up.obj`,
    tex: `${ownerCars}/FD_Shared_Independent_v01/Textured_08`, skins: /^fd_shared_512x256$/,
    gt4LengthM: 4.280 },
  { key: 'rx7_fd_99', obj: `${here}/converted/rx7_fd_99/rx7_fd_99.obj`,
    lampsUp: `${here}/converted/rx7_fd_99/rx7_fd_99_up.obj`,
    tex: `${ownerCars}/FD_Shared_Independent_v01/Textured_08`, skins: /^fd_shared_512x256$/,
    gt4LengthM: 4.280 },
  { key: 'rx7_fd_nowing', obj: `${here}/converted/rx7_fd_nowing/rx7_fd_nowing.obj`,
    lampsUp: `${here}/converted/rx7_fd_nowing/rx7_fd_nowing_up.obj`,
    tex: `${ownerCars}/FD_Shared_Independent_v01/Textured_08`, skins: /^fd_shared_512x256$/,
    gt4LengthM: 4.280 },

  // The owner's Viper GTS (2026-09-25, "I added Dodge Viper model"): the GT1
  // rip rebuilt as a game asset - reflection shell gone, levelled on its
  // wheels, arches cut, panel backs closed. It is GT4's "Dodge VIPER GTS `99"
  // (4488 mm, 2443 mm wheelbase), not traffic.
  { key: 'viper_gts',    obj: `${ownerCars}/Viper_GTS_PSX/viper_gts_psx.obj`,
    tex: `${ownerCars}/Viper_GTS_PSX`, skins: /^viper_atlas_256$/,
    gt4LengthM: 4.488 },

  // The owner's FlatSix Coupe (2026-09-26, "add FlatSixCoupe for all Porsche
  // 911's and/or RUF or the Yellowbird one"): an unbranded rear-engined coupe
  // built to Docs/CarModelSpec.md - 2,290 body tris, 92 per wheel, sealed,
  // one 256 atlas with matte alpha 128. Kept at its delivered, reference-
  // scaled 4.276 m (a G-body 911 is 4.29 m; the catalog carries no length).
  { key: 'flatsix_coupe', obj: `${ownerCars}/FlatSix_Coupe_PSX/flat_six_coupe.obj`,
    tex: `${ownerCars}/FlatSix_Coupe_PSX`, skins: /^coupe_atlas_256$/ },

  // The owner's 2026-10-01 set ("I made more"), same spec and layout as the
  // FlatSix Coupe (Body + Wheel_FL/FR/RL/RR, nose -Z, one 256 atlas, matte
  // alpha 128), each sized to its GT4 sheet:
  //   FlatSix Turbo 96 = RUF CTR2 `96 (993): 4290 mm, wb 2302.
  //   Midship Coupe    = Honda NSX `90: 4430 mm, wb 2530.
  //   Classic Roadster = Mazda MX-5 Miata (NA) `89: 3955 mm, wb 2265.
  { key: 'flatsix_turbo_96', obj: `${ownerCars}/FlatSix_Turbo_96_PSX/flat_six_turbo_96.obj`,
    tex: `${ownerCars}/FlatSix_Turbo_96_PSX`, skins: /^coupe_atlas_256$/,
    gt4LengthM: 4.290 },
  // 2026-10-03 ("I updated NSX and S2000 models"): v04 of the independent
  // NSX (Cars/NSX_Independent_Textured_v04) - lower wedge headlight lids,
  // the 1.55 m swept rear lens band, shallow painted side intakes, MR
  // underbody kept; 2,922 body tris + 4 x 124, one 512x256 atlas (yellow).
  // Delivered as GLB only: tools/carmodels/glb2obj.py turned the HEADLIGHTS-
  // DOWN variant (pop-ups closed) into converted/midship_coupe_v04, and
  // (owner 2026-10-03: "two models for NSX. One with headlights down and one
  // with headlights flipped-up") the same folder's coupe_v04_headlights_up.glb
  // into midship_coupe_v04_up - same atlas, same frame, lamps raised. It
  // becomes the shell's second BODY (`lampsUp`): no wheels of its own, and the
  // DOWN body's scale, so the two coincide everywhere but the lamps.
  { key: 'midship_coupe', obj: `${here}/converted/midship_coupe_v04/midship_coupe_v04.obj`,
    lampsUp: `${here}/converted/midship_coupe_v04/midship_coupe_v04_up.obj`,
    tex: `${ownerCars}/NSX_Independent_Textured_v04`, skins: /^midship_coupe_yellow_512x256$/,
    gt4LengthM: 4.430 },
  { key: 'classic_roadster', obj: `${ownerCars}/Classic_Roadster_PSX/classic_roadster.obj`,
    tex: `${ownerCars}/Classic_Roadster_PSX`, skins: /^roadster_atlas_256$/,
    gt4LengthM: 3.955 },
  // 2026-10-05, the owner's pop-up-lamp roadster rebuild (the first
  // generation, 1989-97; the fixed-lamp second generation keeps
  // classic_roadster): closed 3,837 / raised 3,987 tris, soft top up, red
  // 512x256. GLB -> converted/roadster_na_popup_v01. Its underside is darkened
  // by a 0.45 COLOR_0 vertex tint that neither the OBJ path nor PSX/CarPaint
  // keeps, so the tint is BAKED into the atlas copy there (every tinted
  // triangle samples only the right-half underside, no texel shared with an
  // untinted one - an exact equivalent). Same GT4 sheet as classic_roadster.
  { key: 'roadster_na_popup', obj: `${here}/converted/roadster_na_popup_v01/roadster_na_popup_v01.obj`,
    lampsUp: `${here}/converted/roadster_na_popup_v01/roadster_na_popup_v01_up.obj`,
    tex: `${here}/converted/roadster_na_popup_v01`, skins: /^roadster_na_red_512x256$/,
    gt4LengthM: 3.955 },
  // 2026-10-05, the owner's 1983 pop-up-lamp hatch (white over black, 3,992
  // tris either way, 512x256 with the underfloor on the right): the catalog's
  // one pop-up variant of that car; its fixed-lamp twin keeps its scored
  // shell. GT4: 4205 mm, wb 2400. GLB -> converted/hatch_83_popup_v02.
  // 2026-10-05 later: revision 07 (rebuilt hood / headlight / front-fascia
  // junction, raised housings 50 mm forward, black upper fascia; same stance
  // and paint; 3,988 tris either way), converted/hatch_83_popup_v07.
  { key: 'hatch_83_popup', obj: `${here}/converted/hatch_83_popup_v07/hatch_83_popup_v07.obj`,
    lampsUp: `${here}/converted/hatch_83_popup_v07/hatch_83_popup_v07_up.obj`,
    tex: `${ownerCars}/AE86_Independent_v07`, skins: /^ae86_white_black_512x256$/,
    skinAs: 'hatch_83_white_black_512x256', gt4LengthM: 4.205 },

  // The owner's 2026-10-02 set ("Integra, S2000, and Prelude have been
  // added"), same layout (Body + Wheel_FL/FR/RL/RR, nose -Z, matte alpha 128),
  // each sized to its GT4 sheet:
  //   Liftback 95 = Honda INTEGRA TYPE R (DC2): 4380 mm, wb 2570 (FF underbody,
  //                 512x256). Reference-derived from OUTPISTON's Sketchfab
  //                 Integra, CC BY-NC-SA 4.0 (Cars/Liftback_95_PSX/READ_ME_FIRST).
  //   Roadster 99 = Honda S2000 `99: 4135 mm, wb 2400. 2026-10-04: v06,
  //                 the final independent rebuild (Cars/S2000_Independent_
  //                 Textured_v06: 3,466 tris with the wheels, red 512x256
  //                 with an underbody), GLB -> converted/roadster_99_v06 by
  //                 glb2obj.py. Replaced v02 (2026-10-03), which had replaced
  //                 Roadster_99_Original/Revision_02.
  //   Coupe 99    = Honda PRELUDE (5th gen) `96-`98: 4520 mm, wb 2585
  //                 (Revision_04, FF underbody, 512x256).
  { key: 'liftback_95', obj: `${ownerCars}/Liftback_95_PSX/liftback95.obj`,
    tex: `${ownerCars}/Liftback_95_PSX`, skins: /^liftback95_atlas_512x256$/,
    gt4LengthM: 4.380 },
  { key: 'roadster_99', obj: `${here}/converted/roadster_99_v06/roadster_99_v06.obj`,
    tex: `${ownerCars}/S2000_Independent_Textured_v06`, skins: /^s2000_red_512x256$/,
    gt4LengthM: 4.135 },
  { key: 'coupe_99', obj: `${ownerCars}/Coupe_99_Original/Revision_04/coupe_99.obj`,
    tex: `${ownerCars}/Coupe_99_Original/Revision_04`, skins: /^coupe_512x256$/,
    gt4LengthM: 4.520 },

  // Ripped PS1-era cars from the owner's Cars folder (2026-09-25: "add these
  // vehicles as traffic"), converted by convert_rip.py - aligned, sized to
  // the real car, wheels split out, every source page baked onto one 512
  // atlas (the one exemption from the 256 clamp: ConfigureTextureImporters).
  // Configs in rips/.
  { key: 'crown_victoria', obj: `${here}/converted/crown_victoria/crown_victoria.obj`,
    tex: `${here}/converted/crown_victoria`, skins: /^crown_victoria_atlas_512$/ },
  { key: 'camry_2001',     obj: `${here}/converted/camry_2001/camry_2001.obj`,
    tex: `${here}/converted/camry_2001`, skins: /^camry_2001_atlas_512$/ },
  { key: 'ford_transit',   obj: `${here}/converted/ford_transit/ford_transit.obj`,
    tex: `${here}/converted/ford_transit`, skins: /^ford_transit_atlas_512$/ },
];

// `node export_models.mjs civic_eg` re-exports one model and leaves the rest
// of the folder exactly as it is.
const only = process.argv.slice(2);

// Not liveries: a specular map, a shading swatch, the pickup's UV template, and
// the one model that paints its wheels off a sheet of their own — copied
// alongside the liveries but never offered as a colour.
const NOT_A_SKIN = new Set(['metallic', 'shade', 'pickup', 'uv', 'windows', 'lines', 'details', 'wheel']);

function parseObj(file) {
  const txt = fs.readFileSync(file, 'utf8');
  const v = [], vt = [], vn = [], objects = [];
  let cur = null;
  for (const raw of txt.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.startsWith('v ')) { const p = line.split(/\s+/); v.push([+p[1], +p[2], +p[3]]); }
    else if (line.startsWith('vt ')) { const p = line.split(/\s+/); vt.push([+p[1], +p[2]]); }
    else if (line.startsWith('vn ')) { const p = line.split(/\s+/); vn.push([+p[1], +p[2], +p[3]]); }
    else if (line.startsWith('o ')) { cur = { name: line.slice(2).trim(), faces: [] }; objects.push(cur); }
    else if (line.startsWith('f ')) {
      if (!cur) { cur = { name: 'body', faces: [] }; objects.push(cur); }
      cur.faces.push(line.split(/\s+/).slice(1).map(c => {
        const [a, b, n] = c.split('/');
        return { v: +a, vt: b ? +b : 0, vn: n ? +n : 0 };
      }));
    }
  }
  return { v, vt, vn, objects };
}

/// Group name -> the game's wheel slot, or null for bodywork. The pack spells
/// these four different ways across its folders — WheelFL, wheel_FL, wheel.FR,
/// and one typo'd `wheell_.RL` — so match on letters only and read the corner
/// off the end rather than trying to enumerate the separators.
function wheelSlot(name) {
  const n = name.replace(/[^a-z]/gi, '').toLowerCase();
  if (!n.startsWith('wheel')) return null;
  const m = /(fl|fr|rl|rr)$/.exec(n);
  return m ? `wheel_${m[1].toUpperCase()}` : null;
}

/// Emit one OBJ. `groups` is [{name, faces}] over the shared vertex pools,
/// re-indexed so the file stands alone.
function writeObj(file, src, groups, mtlName) {
  const vMap = new Map(), tMap = new Map(), nMap = new Map();
  const vOut = [], tOut = [], nOut = [];
  const remap = (map, out, pool, i) => {
    if (i === 0) return 0;
    if (!map.has(i)) { out.push(pool[i - 1]); map.set(i, out.length); }
    return map.get(i);
  };
  const body = [];
  for (const g of groups) {
    body.push(`o ${g.name}`);
    body.push(`usemtl ${mtlName}`);
    for (const f of g.faces) {
      body.push('f ' + f.map(c =>
        `${remap(vMap, vOut, src.v, c.v)}/${remap(tMap, tOut, src.vt, c.vt) || ''}/${remap(nMap, nOut, src.vn, c.vn) || ''}`
          .replace(/\/+$/, '')).join(' '));
    }
  }
  const head = [`# PSX Racing car model — generated by tools/carmodels/export_models.mjs`,
                `mtllib ${path.basename(file, ".obj")}.mtl`];
  const lines = head
    .concat(vOut.map(p => `v ${p.map(n => n.toFixed(6)).join(' ')}`))
    .concat(tOut.map(p => `vt ${p.map(n => n.toFixed(6)).join(' ')}`))
    .concat(nOut.map(p => `vn ${p.map(n => n.toFixed(6)).join(' ')}`))
    .concat(body);
  fs.writeFileSync(file, lines.join('\n') + '\n');
  return { verts: vOut.length, faces: groups.reduce((a, g) => a + g.faces.length, 0) };
}

const safe = n => n.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');

let report = [];
for (const m of MODELS) {
  if (only.length && !only.includes(m.key)) continue;
  const src = parseObj(m.obj);
  // THE ONE EXCEPTION to "nothing here moves geometry": a model that comes
  // with a GT4 length is scaled, uniformly, to it. A car's length is its
  // longest extent whichever way the file is turned, so this needs no axis
  // convention — which is the reason the rule above exists.
  let s = 1;
  if (m.gt4LengthM) {
    let len = 0;
    for (let k = 0; k < 3; k++) {
      let lo = Infinity, hi = -Infinity;
      for (const p of src.v) { lo = Math.min(lo, p[k]); hi = Math.max(hi, p[k]); }
      len = Math.max(len, hi - lo);
    }
    s = m.gt4LengthM / len;
    for (const p of src.v) { p[0] *= s; p[1] *= s; p[2] *= s; }
    report.push(`${m.key.padEnd(14)} scaled x${s.toFixed(4)}: ${len.toFixed(3)} m -> GT4 ${m.gt4LengthM.toFixed(3)} m long`);
  }
  const texDir = m.tex || path.join(path.dirname(m.obj), 'textures');
  const dir = path.join(outRoot, m.key);
  fs.mkdirSync(path.join(dir, 'textures'), { recursive: true });

  const bodyFaces = [];
  const axle = { F: [], R: [] };
  const corners = new Set();
  for (const o of src.objects) {
    const slot = wheelSlot(o.name);
    if (slot) { corners.add(slot); axle[slot[6]].push(...o.faces); continue; }
    if (m.parts && !m.parts.includes(o.name)) continue;
    bodyFaces.push(...o.faces);
  }
  if (corners.size !== 4) throw new Error(`${m.key}: expected 4 wheels, got ${[...corners]}`);
  if (bodyFaces.length === 0) throw new Error(`${m.key}: no body faces`);

  const skins = fs.readdirSync(texDir).filter(f => /\.png$/i.test(f))
    .filter(f => !NOT_A_SKIN.has(safe(path.basename(f, path.extname(f)))))
    .filter(f => !m.skins || m.skins.test(path.basename(f, '.png')))
    .sort();
  // `skinAs` renames a model's ONE atlas on the way in (no brands in our
  // file names, whatever the source folder calls it).
  if (m.skinAs && skins.length !== 1) throw new Error(`${m.key}: skinAs needs exactly one skin`);
  const skinName = f => m.skinAs || safe(path.basename(f, '.png'));
  const first = skinName(skins[0]);

  // Front and rear axles go to separate files so the baker never has to GUESS
  // which end of an imported mesh is the nose. Overhang heuristics look sound
  // until a cab-over van turns up with its front axle further from the bumper
  // than its rear one is from the tailgate — and a car facing backwards is not
  // a subtle bug.
  const parts = [
    [`${m.key}.obj`, m.key, bodyFaces],
    [`${m.key}_wheels_f.obj`, m.key + '_wheels_f', axle.F],
    [`${m.key}_wheels_r.obj`, m.key + '_wheels_r', axle.R],
  ];
  const stats = {};
  for (const [file, mtl, faces] of parts) {
    stats[mtl] = writeObj(path.join(dir, file), src, [{ name: mtl, faces }], mtl);
    fs.writeFileSync(path.join(dir, file.replace('.obj', '.mtl')),
      `# PSX Racing car model\nnewmtl ${mtl}\nKa 1 1 1\nKd 1 1 1\nillum 1\nmap_Kd textures/${first}.png\n`);
  }

  // POP-UP LAMPS: the lamps-raised body, `${key}_lampsup.obj`. Scaled by the
  // DOWN body's factor, never its own: a raised lamp could stand proud of the
  // nose and change the longest extent, and then the two bodies would no
  // longer coincide. The baker hangs it on the same yaw and offsets.
  if (m.lampsUp) {
    const up = parseObj(m.lampsUp);
    for (const p of up.v) { p[0] *= s; p[1] *= s; p[2] *= s; }
    const upFaces = [];
    for (const o of up.objects)
      if (!wheelSlot(o.name) && (!m.parts || m.parts.includes(o.name))) upFaces.push(...o.faces);
    if (upFaces.length === 0) throw new Error(`${m.key}: no lamps-up body faces`);
    const mtl = m.key + '_lampsup';
    stats[mtl] = writeObj(path.join(dir, mtl + '.obj'), up, [{ name: mtl, faces: upFaces }], mtl);
    fs.writeFileSync(path.join(dir, mtl + '.mtl'),
      `# PSX Racing car model
newmtl ${mtl}
Ka 1 1 1
Kd 1 1 1
illum 1
map_Kd textures/${first}.png
`);
    report.push(`${m.key.padEnd(14)} lamps-up body=${stats[mtl].faces}f (x${s.toFixed(4)}, the body's scale)`);
  }

  for (const f of skins)
    fs.copyFileSync(path.join(texDir, f), path.join(dir, 'textures', skinName(f) + '.png'));
  // A dedicated wheel sheet, where the model has one. Not a livery, but the
  // baker needs it to keep the pickup's wheels from being painted body colour.
  if (fs.existsSync(path.join(texDir, 'wheel.png')))
    fs.copyFileSync(path.join(texDir, 'wheel.png'), path.join(dir, 'textures', 'wheel.png'));

  report.push(`${m.key.padEnd(14)} body=${String(stats[m.key].faces).padStart(4)}f ` +
              `wheels=${stats[m.key + '_wheels_f'].faces}+${stats[m.key + '_wheels_r'].faces}f ` +
              `skins=${String(skins.length).padStart(2)}  ${m.parts ? 'parts=' + m.parts.join('+') : ''}`);
}
console.log(report.join('\n'));
console.log(`\n${MODELS.length} models -> ${path.relative(project, outRoot)}`);
