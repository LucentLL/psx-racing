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
  { key: 'civic_eg',     obj: `${ownerCars}/EG_Civic_Source_PSX/faithful/eg_civic.obj`,
    tex: `${ownerCars}/EG_Civic_Source_PSX/faithful`, skins: /^civic_atlas_256$/,
    gt4LengthM: 4.070 },
  // The owner's S13 hatch (the 180SX that America sold as the 240SX) — GT4's
  // "Nissan 240SX `96": 4520 mm, 2475 mm wheelbase. The S14 is its own row.
  // The REBUILT shell (2026-09-25, "it should seal the gaps"): the first one
  // was the decimated source with 733 open edges, and the road showed through
  // them; this one was re-lofted closed from the source's cross-sections, on
  // a new atlas of its own.
  { key: 'nissan_180sx', obj: `${ownerCars}/Nissan_240SX_PSX/rebuilt/nissan_240sx_psx.obj`,
    tex: `${ownerCars}/Nissan_240SX_PSX/rebuilt`, skins: /^nissan_atlas_256$/,
    gt4LengthM: 4.520 },

  // The owner's Viper GTS (2026-09-25, "I added Dodge Viper model"): the GT1
  // rip rebuilt as a game asset - reflection shell gone, levelled on its
  // wheels, arches cut, panel backs closed. It is GT4's "Dodge VIPER GTS `99"
  // (4488 mm, 2443 mm wheelbase), not traffic.
  { key: 'viper_gts',    obj: `${ownerCars}/Viper_GTS_PSX/viper_gts_psx.obj`,
    tex: `${ownerCars}/Viper_GTS_PSX`, skins: /^viper_atlas_256$/,
    gt4LengthM: 4.488 },

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
  if (m.gt4LengthM) {
    let len = 0;
    for (let k = 0; k < 3; k++) {
      let lo = Infinity, hi = -Infinity;
      for (const p of src.v) { lo = Math.min(lo, p[k]); hi = Math.max(hi, p[k]); }
      len = Math.max(len, hi - lo);
    }
    const s = m.gt4LengthM / len;
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
  const first = safe(path.basename(skins[0], '.png'));

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

  for (const f of skins)
    fs.copyFileSync(path.join(texDir, f), path.join(dir, 'textures', safe(path.basename(f, '.png')) + '.png'));
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
