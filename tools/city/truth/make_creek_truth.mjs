// make_creek_truth.mjs - freeze the creek-bed truth for WP-04b's check.
//
//   node tools/city/truth/make_creek_truth.mjs [--force]
//
// Seven creek transects, sampled every 2 m on the USGS 3DEP 1 m bare-earth
// DEM through the 3DEP ImageServer (getSamples, bilinear; public domain).
// Writes creek_transects.json: each profile, and the bed with its height, its
// offset along the transect and its lat/lon, and the relief (max - min) of the
// whole line. The bed is the LOWEST sample of the line.
//
//   * The first five are the creek transects of survey_src_terrain section 1
//     (1.2 km lines across Little Sugar, Irwin, Stewart, "Briar" and
//     "McAlpine" Creeks). They were laid across each valley by eye, and two
//     miss the creek they are named for: the "Briar" line's lowest point is
//     Edwards Branch, 270 m off its centre, and the "McAlpine" line never
//     reaches McAlpine Creek (its lowest point is a small tributary 176 m off
//     centre). They are kept as the survey measured them.
//   * So two more cross the named creeks themselves (WP-04b, 2026-09-28):
//     laid from the county's own centreline (Mecklenburg Creeks and Streams),
//     250 m along the creek from where it passes under the named road (clear
//     of the road's own fill), perpendicular to the creek there, 600 m long.
//     The truth is still 3DEP's lowest point of the line, not the county line.
//
// Transects already in creek_transects.json are kept as they are (the truth is
// frozen); --force samples them all again.
import { writeFileSync, readFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'creek_transects.json');
const URL = 'https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/getSamples';
const LAT0 = 35.18456015184093, KX = 111320 * Math.cos(LAT0 * Math.PI / 180), KZ = 111132;
/// id, label, centre lat, lon, bearing (degrees), half length (m)
const T = [
  ['little_sugar', 'Little Sugar Creek @ 35.2486 (E-W)', 35.248572, -80.812851, 90, 600],
  ['irwin', 'Irwin Creek @ 35.2563 (E-W)', 35.256324, -80.841592, 90, 600],
  ['stewart', 'Stewart Creek @ 35.2465 (E-W)', 35.246464, -80.869493, 90, 600],
  ['briar', 'Briar Creek @ Independence (E-W; its low point is Edwards Branch)', 35.2045, -80.7925, 90, 600],
  ['mcalpine', 'McAlpine Creek @ Monroe Rd (NE-SW; its low point is a tributary)', 35.1680, -80.7480, 45, 600],
  // laid by WP-04b from the county centreline, 250 m along the creek from the named road
  ['briar_creek', 'Briar Creek 250 m S of Independence Blvd (E-W across the creek)', 35.208963, -80.802556, 92, 300],
  ['mcalpine_creek', 'McAlpine Creek 250 m W of Monroe Rd (N-S across the creek)', 35.148652, -80.748212, 177, 300],
];
const STEP = 2;
const force = process.argv.includes('--force');
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function samples(pts) {
  const out = [];
  for (let k = 0; k < pts.length; k += 40) {
    const chunk = pts.slice(k, k + 40);
    const body = new URLSearchParams({ geometry: JSON.stringify({ points: chunk.map(([la, lo]) => [lo, la]), spatialReference: { wkid: 4326 } }),
      geometryType: 'esriGeometryMultipoint', returnFirstValueOnly: 'true', interpolation: 'RSP_BilinearInterpolation', f: 'json' });
    let j;
    for (let a = 0; a < 6; a++) {
      try { const r = await fetch(URL, { method: 'POST', body }); j = await r.json(); if (j.samples) break; } catch (e) { j = { error: e.message }; }
      await sleep(2000 * (a + 1));
    }
    if (!j || !j.samples) throw new Error('3DEP getSamples failed: ' + JSON.stringify(j).slice(0, 200));
    const byId = new Map(j.samples.map(s => [s.locationId, s.value === 'NoData' ? NaN : +s.value]));
    for (let i = 0; i < chunk.length; i++) out.push(byId.has(i) ? byId.get(i) : NaN);
  }
  return out;
}
const old = existsSync(OUT) ? JSON.parse(readFileSync(OUT, 'utf8')) : { transects: [] };
const kept = new Map(old.transects.map(t => [t.id, t]));
const res = [];
for (const [id, label, la, lo, brg, half] of T) {
  if (!force && kept.has(id)) { res.push({ ...kept.get(id), label }); console.log(`${id}: kept (sampled ${kept.get(id).sampled || 'with the first five'})`); continue; }
  const pts = [];
  for (let s = -half; s <= half; s += STEP) pts.push([la + s * Math.cos(brg * Math.PI / 180) / KZ, lo + s * Math.sin(brg * Math.PI / 180) / KX]);
  const h = await samples(pts);
  let bi = -1;
  for (let i = 0; i < pts.length; i++) if (Number.isFinite(h[i]) && (bi < 0 || h[i] < h[bi])) bi = i;
  const fin = h.filter(Number.isFinite);
  res.push({ id, label, centre: [la, lo], bearing_deg: brg, half_m: half, step_m: STEP, sampled: new Date().toISOString().slice(0, 10),
             bed: { asl: +h[bi].toFixed(2), along_m: -half + bi * STEP, lat: +pts[bi][0].toFixed(6), lon: +pts[bi][1].toFixed(6) },
             relief_m: +(Math.max(...fin) - Math.min(...fin)).toFixed(2), profile: h.map(v => Number.isFinite(v) ? +v.toFixed(2) : null) });
  console.log(`${id}: bed ${h[bi].toFixed(2)} m at ${-half + bi * STEP} m, relief ${(Math.max(...fin) - Math.min(...fin)).toFixed(1)} m`);
}
writeFileSync(OUT, JSON.stringify({ note: 'USGS 3DEP 1 m bare earth (public domain) via the 3DEP ImageServer getSamples, bilinear, every 2 m along seven lines across Charlotte creeks (make_creek_truth.mjs says how each was laid). bed = the lowest sample of the line (the channel). The first five were sampled 2026-09-28 with the survey\'s lines; each later one carries its own date.', transects: res }) + '\n');
console.log('wrote creek_transects.json');
