// bake_3dep.mjs - re-bake a mountain stage from USGS 3DEP lidar ground.
//
//   node tools/roads/bake_3dep.mjs <prefix> [--check] [--offline] [--keep-line] [--hold-ends <m>] [--out <dir>]
//
// The stages were baked from OpenStreetMap lines and "SRTM" heights
// (fetch_road.mjs / fetch_loop.mjs / tools/brp). This reads that bake and
// writes the same four files in the same formats, so the Unity builder and
// TrackCatalog read them unchanged:
//
//   Resources/<pre>_stage.json           the 4 m waypoints: x, y above baseM, z
//   Art/<stage>/<pre>_dem_near.bytes     12 m grid, int16 decimetres above baseM
//   Art/<stage>/<pre>_dem_far.bytes      60 m grid, the same
//   Art/<stage>/<pre>_dem_meta.json      the lattices (unchanged) + "source"
//
// WHAT CHANGES
//   THE LINE moves onto the road bench in the 1 m lidar ground, but only where
//   the mapped line is more than 2 m off it for 16 m or more, the bench is
//   confidently found and is not a junction or a pull-off (bench.mjs). The
//   shift fades in and out over 24 m. Everywhere else the plan is the mapped
//   line as it was: the bench's middle is not the pavement's middle to better
//   than about a metre, so smaller "corrections" would only add wobble.
//   --keep-line leaves the plan alone (loops are always left alone: their
//   closing seam and crossings are fetch_loop.mjs's business). Use it where
//   the shelf is much wider than the pavement (the Parkway: 11 m of shelf
//   around 6.4 m of road, grass shoulders that are not the same width on
//   both sides), because there the shelf's middle does not say where the
//   pavement is to better than a couple of metres.
//
//   THE HEIGHTS are the lidar ground on the bench under each waypoint (median
//   of the ground within 1.5 m of the bench's middle), straight across bridges,
//   tunnels and anywhere the bare earth drops away under a deck. No grade
//   clamp. One gaussian along the road, the smallest sigma (from 8 m, in 2 m
//   steps) at which the tightest vertical curve is over 230 m: the game's
//   self-test refuses under 200 m (LifeSimSelfTest.CheckElevation: the ground
//   lattice would cut through a sharper crest), so this is the least smoothing
//   the game can take, and it leaves the real road's grades alone (the SRTM
//   bakes used sigma 45-85 m, three more 22 m passes and a clamp).
//
//   THE GRIDS are sampled on the lattices the stage already has: near from the
//   3DEP 1/3" DEM at full resolution (about 9 m), far from its 4x overview
//   (about 37 m, the right size for 60 m cells). baseM is kept, so the world
//   does not move. <pre>_dem_wide.bytes (240 m, bake_wide.mjs) is not touched.
//
// THE BEFORE-PICTURE. The first run copies the bake it found to
// tools/roads/srtm_bakes/<pre>_srtm/ (outside Assets: nothing ships twice) and
// every run starts from that copy, so re-running never compounds a correction.
//   node tools/roads/audit_3dep.mjs <pre> --res tools/roads/srtm_bakes/<pre>_srtm --art tools/roads/srtm_bakes/<pre>_srtm
// audits the old bake at any time.
//
// --check re-derives everything from the tile cache (no network) and compares
// it with the files on disk; exit 1 on any difference.

import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { makeProjection, arcPositions, splineResample, tightestPlan, profileStats } from './lib.mjs';
import { open3dep, dem13, dem1m, STAGES, loadStage, PROJECT_1M } from './lib3dep.mjs';
import { ribbon, findBench, gaussSmooth } from './bench.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..', '..');
const args = process.argv.slice(2);
const flag = n => args.includes(n);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : d; };
const pre = args[0];
if (!pre || !STAGES[pre]) { console.error('usage: node bake_3dep.mjs <' + Object.keys(STAGES).join('|') + '> [--check] [--offline] [--keep-line] [--out dir]'); process.exit(2); }
const CHECK = flag('--check');
const st = STAGES[pre];
const roadsCache = process.env.PSX_ROADS_CACHE || join(HERE, 'cache');
const resDir = join(ROOT, 'Assets', 'PSXRacing', 'Resources'), artDir = join(ROOT, 'Assets', 'PSXRacing', 'Art', st.art);
const outRes = opt('--out', null) || resDir, outArt = opt('--out', null) || artDir;
const keepDir = join(HERE, 'srtm_bakes', pre + '_srtm');
const FILES = [[resDir, `${pre}_stage.json`], [artDir, `${pre}_dem_near.bytes`], [artDir, `${pre}_dem_far.bytes`], [artDir, `${pre}_dem_meta.json`]];

// ------------------------------------------------- the before-picture
if (!existsSync(join(keepDir, `${pre}_stage.json`))) {
  const cur = JSON.parse(readFileSync(join(resDir, `${pre}_stage.json`), 'utf8'));
  if (!/SRTM/.test(cur.attribution || '')) throw new Error(`${pre}_stage.json is not the SRTM bake (attribution "${cur.attribution}") and ${keepDir} has no copy of it`);
  if (CHECK) throw new Error(`--check: ${keepDir} is missing (the SRTM bake this stage was re-baked from)`);
  mkdirSync(keepDir, { recursive: true });
  for (const [d, f] of FILES) copyFileSync(join(d, f), join(keepDir, f));
  console.log(`kept the SRTM bake in ${keepDir}`);
}
const S = loadStage(ROOT, pre, { resDir: keepDir, artDir: keepDir });
const { json, meta, n, loop } = S;
const SP = json.spacing || 4;
const proj = makeProjection(json.lat0, json.lon0);
console.log(`=== ${pre}: ${st.road} (${st.id}), ${(n * SP / 1000).toFixed(2)} km${loop ? ', loop' : ''}: re-bake from USGS 3DEP ===`);

const store = open3dep({ offline: flag('--offline') || CHECK });
const lidar = dem1m(store, 0), near13 = dem13(store, 0), far13 = dem13(store, 2);
const sampleXZ = (x, z) => { const ll = proj.toLL(x, z); return lidar.sample(ll.lat, ll.lon); };
const corridorOf = wp => { const p = []; for (const q of wp) for (const dx of [-60, 0, 60]) for (const dz of [-60, 0, 60]) p.push(proj.toLL(q.x + dx, q.z + dz)); return p; };
const lattice = g => { const p = []; for (let r = 0; r < g.rows; r++) for (let c = 0; c < g.cols; c++) p.push(proj.toLL(g.originX + c * g.cell, g.originZ + r * g.cell)); return p; };
await lidar.prepare(corridorOf(S.wp));

// ------------------------------------------------ structures on the route
const spansOf = a => { const o = []; for (let k = 0; k + 1 < (a || []).length; k += 2) o.push([a[k], a[k + 1]]); return o; };
const bridgeSpans = spansOf(json.bridges), tunnelSpans = spansOf(json.tunnels);
/// OSM bridge / tunnel ways lying on the route (the bake dropped spans under 56 m; the ground still falls away under them)
function osmSpans(wp) {
  const p = join(roadsCache, st.osm);
  if (!existsSync(p)) { console.log(`  (no ${st.osm} in the cache: OSM bridges are not bridged; ground dips still are)`); return []; }
  const ways = JSON.parse(readFileSync(p, 'utf8')).elements.filter(e => e.type === 'way' && e.geometry && e.tags && e.tags.highway &&
    ((e.tags.bridge && e.tags.bridge !== 'no') || (e.tags.tunnel && e.tags.tunnel !== 'no')));
  const out = [];
  for (const w of ways) {
    const idx = []; let tot = 0;
    const g = w.geometry.map(q => proj.toXZ(q.lat, q.lon));
    for (let k = 0; k + 1 < g.length; k++) {
      const L = Math.hypot(g[k + 1].x - g[k].x, g[k + 1].z - g[k].z), m = Math.max(1, Math.round(L / 4));
      for (let j = 0; j <= m; j++) {
        const x = g[k].x + (g[k + 1].x - g[k].x) * j / m, z = g[k].z + (g[k + 1].z - g[k].z) * j / m; tot++;
        let bi = -1, bd = 9;
        for (let i = 0; i < wp.length; i++) { const d = (wp[i].x - x) ** 2 + (wp[i].z - z) ** 2; if (d < bd) { bd = d; bi = i; } }
        if (bi >= 0) idx.push(bi);
      }
    }
    if (idx.length >= 2 && idx.length / tot >= 0.6) out.push([Math.min(...idx) * SP, Math.max(...idx) * SP]);
  }
  return out;
}
const lapOf = wp => wp.length * SP;
const inSpans = (spans, s, pad, lap) => spans.some(([a, b]) => (s >= a - pad && s <= b + pad) || (loop && s + lap >= a - pad && s + lap <= b + pad));

// --------------------------------------------------------- 1. the line
const osm0 = osmSpans(S.wp);
const skip0 = i => inSpans(bridgeSpans, i * SP, 12, lapOf(S.wp)) || inSpans(tunnelSpans, i * SP, 12, lapOf(S.wp)) || inSpans(osm0, i * SP, 10, lapOf(S.wp));
const rib0 = ribbon(S.wp, loop, sampleXZ);
const b0 = findBench(rib0, { skip: skip0 });
const moves = [];
let wp = S.wp.map(p => ({ x: p.x, z: p.z }));
// --check bakes the way the files on disk were baked: a stage baked with --keep-line says so in its meta.
const metaPath = join(outArt, `${pre}_dem_meta.json`);
const bakedKeepLine = CHECK && existsSync(metaPath) && /^as mapped/.test(JSON.parse(readFileSync(metaPath, 'utf8')).source?.line || '');
const KEEP_LINE = flag('--keep-line') || loop || bakedKeepLine;
// --hold-ends <m>: no stretch is moved if it, or its 24 m fade, comes within <m> of the start or finish line. The grid
// and the flag then stand on the road exactly as they did. (NC 226 to Gillespie Gap: a 16 m stretch moved 2.2 m, 36 m
// before the flag on the bend at the gap, left an S in the approach and finish-play-check stopped passing.) Recorded in
// the meta and honoured by --check.
const bakedHold = CHECK && existsSync(metaPath) ? (JSON.parse(readFileSync(metaPath, 'utf8')).source?.holdEndsM || 0) : 0;
const HOLD_ENDS = +opt('--hold-ends', bakedHold) || 0;
if (!KEEP_LINE) {
  const widths = []; for (let i = 0; i < n; i++) if (b0.conf[i]) widths.push(b0.width[i]);
  widths.sort((a, b) => a - b);
  const wMed = widths[widths.length >> 1] ?? 8;
  // HOW FAR OFF IS OFF. The shelf is pavement plus shoulders, and the
  // shoulders need not be the same width on both sides, so the shelf's middle
  // can sit (shelf - pavement) / 2 from the pavement's middle with the mapped
  // line dead right. A stretch is moved when the filtered offset is over 2 m
  // AND over that slack plus a metre (NC 226A: 7 m shelf, 6.1 m road, so 2 m;
  // NC 215: 9 m shelf, so 2.45 m).
  const moveOverM = Math.max(2, (wMed - st.paved) / 2 + 1);
  const weight = new Float64Array(n);
  const TAPER = 6;                                       // stations: 24 m
  let a = -1;
  for (let i = 0; i <= n; i++) {
    const on = i < n && Math.abs(b0.offF[i]) > moveOverM;
    if (on && a < 0) a = i;
    if (!on && a >= 0) {
      const z = i - 1, len = (z - a + 1) * SP;
      let conf = 0, pk = 0; const w = [];
      for (let k = a; k <= z; k++) { if (b0.conf[k]) { conf++; w.push(b0.width[k]); } if (Math.abs(b0.offF[k]) > Math.abs(pk)) pk = b0.offF[k]; }
      w.sort((p, q) => p - q);
      const wRun = w.length ? w[w.length >> 1] : NaN;
      const nearLine = HOLD_ENDS > 0 && [json.startLineM, json.finishM].some(m => Number.isFinite(m) && m > 0 && a * SP - 24 < m + HOLD_ENDS && z * SP + 24 > m - HOLD_ENDS);
      const why = nearLine ? `within ${HOLD_ENDS} m of the start or finish line (--hold-ends)` : len < 16 ? 'shorter than 16 m' : conf / (z - a + 1) < 0.5 ? 'bench found at under half its stations' : !(wRun <= wMed + 4) ? `bench ${wRun} m wide against ${wMed} m (a junction or a pull-off)` : null;
      moves.push({ fromM: a * SP, toM: z * SP, lengthM: len, peakM: +pk.toFixed(2), moved: !why, why });
      if (!why) for (let k = a - TAPER; k <= z + TAPER; k++) {
        if (k < 0 || k >= n) continue;
        const t = k < a ? (k - (a - TAPER)) / TAPER : k > z ? ((z + TAPER) - k) / TAPER : 1;
        weight[k] = Math.max(weight[k], t * t * (3 - 2 * t));
      }
      a = -1;
    }
  }
  const shifted = S.wp.map((p, i) => ({ x: p.x + rib0.nx[i] * b0.offF[i] * weight[i], z: p.z + rib0.nz[i] * b0.offF[i] * weight[i] }));
  // keep the first point exactly (waypoint 0 is the stage's own origin for the grid); re-space to 4 m through a spline
  const done = moves.filter(m => m.moved);
  if (done.length) wp = splineResample(shifted, SP).map(p => ({ x: p.x, z: p.z }));
  console.log(`  line: shelf ${wMed} m around ${st.paved} m of road, so a stretch moves when it is over ${moveOverM.toFixed(2)} m off`);
  console.log(`  line: ${done.length} stretch(es) moved onto the bench (${done.reduce((s, m) => s + m.lengthM, 0)} m of road), ${moves.length - done.length} left alone`);
  for (const m of moves) console.log(`    ${String(m.fromM).padStart(6)}-${String(m.toM).padEnd(6)} ${String(m.lengthM).padStart(4)} m  ${m.peakM > 0 ? 'left ' : 'right'} ${Math.abs(m.peakM).toFixed(1)} m  ${m.moved ? 'MOVED' : 'left alone: ' + m.why}`);
} else {
  // say what was NOT moved, so the choice is on record
  let a = -1; const over = [];
  for (let i = 0; i <= n; i++) { const on = i < n && Math.abs(b0.offF[i]) > 2; if (on && a < 0) a = i; if (!on && a >= 0) { if ((i - a) * SP >= 16) over.push(`${a * SP}-${(i - 1) * SP} m`); a = -1; } }
  console.log(`  line: left as mapped (${loop ? 'a loop' : '--keep-line'}); the bench reads over 2 m off it for 16 m or more at ${over.length} place(s)${over.length ? ': ' + over.join(', ') : ''}`);
}
const N = wp.length;
const t0 = tightestPlan(S.wp, 12, { loop }), t1 = tightestPlan(wp, 12, { loop });
const len0 = arcPositions(S.wp).at(-1), len1 = arcPositions(wp).at(-1);
console.log(`  plan: ${len0.toFixed(0)} m -> ${len1.toFixed(0)} m, tightest corner ${t0.minR.toFixed(1)} m -> ${t1.minR.toFixed(1)} m (at ${t1.at * SP} m)`);
if (t1.minR < t0.minR - 0.75) throw new Error(`moving the line tightened the tightest corner from ${t0.minR.toFixed(1)} m to ${t1.minR.toFixed(1)} m: look at the stretch near ${t1.at * SP} m before baking`);

/// A place on the old line (metres along it) as metres along the new one: the nearest new waypoint within 120 m of the same distance.
const wpS = arcPositions(wp);
function remap(sOld) {
  const i = Math.min(n - 1, Math.max(0, sOld / SP)), i0 = Math.floor(i), f = i - i0, i1 = Math.min(n - 1, i0 + 1);
  const x = S.wp[i0].x + (S.wp[i1].x - S.wp[i0].x) * f, z = S.wp[i0].z + (S.wp[i1].z - S.wp[i0].z) * f;
  let best = Infinity, bs = sOld;
  for (let k = Math.max(0, i0 - 30); k + 1 < Math.min(N, i0 + 32); k++) {
    const ex = wp[k + 1].x - wp[k].x, ez = wp[k + 1].z - wp[k].z, L2 = ex * ex + ez * ez;
    const t = Math.max(0, Math.min(1, ((x - wp[k].x) * ex + (z - wp[k].z) * ez) / L2));
    const d = (wp[k].x + ex * t - x) ** 2 + (wp[k].z + ez * t - z) ** 2;
    if (d < best) { best = d; bs = wpS[k] + Math.sqrt(L2) * t; }
  }
  return bs;
}
const moved = wp.length !== n || wp.some((p, i) => Math.abs(p.x - S.wp[i].x) > 1e-9 || Math.abs(p.z - S.wp[i].z) > 1e-9);
const mapS = s => (moved ? remap(s) : s);
const bridges1 = bridgeSpans.map(([a, b]) => [Math.round(mapS(a) / SP) * SP, Math.round(mapS(b) / SP) * SP]);
const tunnels1 = tunnelSpans.map(([a, b]) => [Math.round(mapS(a) / SP) * SP, Math.round(mapS(b) / SP) * SP]);

// ------------------------------------------------------- 2. the heights
if (moved) await lidar.prepare(corridorOf(wp));
const osm1 = moved ? osmSpans(wp) : osm0;
const skip1 = i => inSpans(bridges1, i * SP, 12, lapOf(wp)) || inSpans(tunnels1, i * SP, 12, lapOf(wp)) || inSpans(osm1, i * SP, 10, lapOf(wp));
const rib1 = moved ? ribbon(wp, loop, sampleXZ) : rib0;
const b1 = moved ? findBench(rib1, { skip: skip1 }) : b0;
const VERT_R = 230;
let sigma = 8, heights, stats;
for (; sigma <= 60; sigma += 2) {
  heights = gaussSmooth(b1.hRoad, sigma / SP, loop);
  stats = profileStats(Array.from(heights), SP, { circular: loop });
  // the self-test's own window: on a route with ends, the first and last three stations are not measured
  let minR = Infinity;
  for (let i = loop ? 0 : 3; i < (loop ? N : N - 3); i++) {
    const a = heights[(i - 3 + N) % N], c = heights[(i + 3) % N];
    const dg = Math.abs((c - heights[i]) - (heights[i] - a)) / (3 * SP) / (3 * SP);
    if (dg > 1e-6) minR = Math.min(minR, 1 / dg);
  }
  stats.minVertR = minR;
  if (minR >= VERT_R) break;
}
if (stats.minVertR < 200) throw new Error(`no smoothing up to sigma 60 m brings the tightest vertical curve over 200 m (${stats.minVertR.toFixed(0)} m): the road height has a step in it`);
if (stats.maxGrade >= 0.15) throw new Error(`the real road reaches ${(stats.maxGrade * 100).toFixed(1)}% here, over the game's 15% limit (LifeSimSelfTest.CheckElevation)`);
const baseM = json.baseM;
let dSum = 0, dMax = 0, dAt = 0;
if (!moved) for (let i = 0; i < N; i++) { const d = heights[i] - S.h[i]; dSum += d; if (Math.abs(d) > Math.abs(dMax)) { dMax = d; dAt = i; } }
console.log(`  heights: gaussian sigma ${sigma} m (tightest vertical curve ${stats.minVertR.toFixed(0)} m), ${stats.minH.toFixed(1)}..${stats.maxH.toFixed(1)} m ASL, max grade ${(stats.maxGrade * 100).toFixed(1)}%` +
  (moved ? '' : `; against the SRTM bake: mean ${(dSum / N).toFixed(2)} m, largest ${dMax.toFixed(2)} m at ${dAt * SP} m`));
console.log(`  bridged: ${bridges1.length} stage span(s), ${tunnels1.length} tunnel(s), ${osm1.length} OSM bridge way(s), ${b1.dips.length} unmapped dip(s)` +
  (b1.dips.length ? ' at ' + b1.dips.map(([a, b]) => `${a * SP}-${b * SP} m`).join(', ') : ''));
if (Math.min(...heights) - baseM < 5) throw new Error(`the road comes within 5 m of baseM ${baseM}: the int16 grids would need a new base`);

// --------------------------------------------------------- 3. the grids
const nearPts = lattice(meta.near), farPts = lattice(meta.far);
await near13.prepare(nearPts); await far13.prepare(farPts);
function bakeGrid(g, pts, dem, name) {
  const buf = Buffer.alloc(g.cols * g.rows * 2);
  let bad = 0;
  for (let k = 0; k < pts.length; k++) {
    let h = dem.sample(pts[k].lat, pts[k].lon);
    if (!Number.isFinite(h)) { bad++; h = baseM; }
    buf.writeInt16LE(Math.max(-32768, Math.min(32767, Math.round((h - baseM) * 10))), k * 2);
  }
  if (bad) throw new Error(`${name}: ${bad} lattice points have no 3DEP data`);
  return buf;
}
const nearBuf = bakeGrid(meta.near, nearPts, near13, 'near'), farBuf = bakeGrid(meta.far, farPts, far13, 'far');
const gridDiff = (buf, g, name) => {
  const old = S.grid(name).data; let s2 = 0, worst = 0;
  for (let k = 0; k < old.length; k++) { const d = (buf.readInt16LE(k * 2) - old[k]) / 10; s2 += d * d; if (Math.abs(d) > Math.abs(worst)) worst = d; }
  return `${g.cols}x${g.rows} @ ${g.cell} m, against the SRTM bake rms ${Math.sqrt(s2 / old.length).toFixed(2)} m, largest ${worst.toFixed(1)} m`;
};
console.log(`  near grid: ${gridDiff(nearBuf, meta.near, 'near')}`);
console.log(`  far grid:  ${gridDiff(farBuf, meta.far, 'far')}`);

// ------------------------------------------------------------- write
const xyz = [];
for (let i = 0; i < N; i++) xyz.push(+wp[i].x.toFixed(2), +(heights[i] - baseM).toFixed(2), +wp[i].z.toFixed(2));
const stage = {};
for (const [k, v] of Object.entries(json)) {
  if (k === 'xyz') continue;
  stage[k] = k === 'attribution' ? 'Route data (c) OpenStreetMap contributors. Elevation: U.S. Geological Survey, 3D Elevation Program.'
    : k === 'startLineM' ? mapS(v) : k === 'finishM' ? mapS(v)
    : k === 'bridges' ? bridges1.flat() : k === 'tunnels' ? tunnels1.flat()
    : k === 'crossings' && moved ? v.map(c => Math.round(mapS(c) / SP) * SP) : v;
}
stage.xyz = xyz;
const stageText = JSON.stringify(stage);
const tilesOf = prefix => Object.entries(store.ledger.sources).filter(([k]) => k.startsWith(prefix)).map(([k, v]) => ({ file: v.url.split('/').pop(), lastModified: v.lastModified }));
const metaOut = {
  baseM: meta.baseM, near: meta.near, far: meta.far, ...(meta.wide ? { wide: meta.wide } : {}),
  source: {
    baked: 'tools/roads/bake_3dep.mjs ' + pre,
    credit: 'U.S. Geological Survey, 3D Elevation Program (public domain); route (c) OpenStreetMap contributors',
    road: `USGS 3DEP 1 m lidar DEM ${PROJECT_1M} (NAD83 / UTM 17N, NAVD88): ground on the road bench, gaussian sigma ${sigma} m along the road, no grade clamp`,
    near: 'USGS 3DEP 1/3 arc-second DEM, full resolution (about 9 m), bilinear on the 12 m lattice',
    far: 'USGS 3DEP 1/3 arc-second DEM, 4x overview (about 37 m), bilinear on the 60 m lattice',
    wide: meta.wide ? 'unchanged: SRTM 1 arc-second, tools/roads/bake_wide.mjs' : undefined,
    line: moved ? `${moves.filter(m => m.moved).length} stretch(es) moved onto the lidar bench (${moves.filter(m => m.moved).reduce((s, m) => s + m.lengthM, 0)} m of road)` : 'as mapped' + (KEEP_LINE && !loop ? ' (--keep-line)' : ''),
    ...(HOLD_ENDS > 0 ? { holdEndsM: HOLD_ENDS } : {}),
    before: `tools/roads/srtm_bakes/${pre}_srtm/ (the SRTM bake this replaced)`,
    files1m: tilesOf('1m_'), files13: tilesOf('13_'),
  },
};
const metaText = JSON.stringify(metaOut, null, 2);
const outputs = [[outRes, `${pre}_stage.json`, Buffer.from(stageText)], [outArt, `${pre}_dem_near.bytes`, nearBuf], [outArt, `${pre}_dem_far.bytes`, farBuf], [outArt, `${pre}_dem_meta.json`, Buffer.from(metaText)]];
if (CHECK) {
  let bad = 0;
  for (const [d, f, buf] of outputs) {
    const p = join(d, f);
    const have = existsSync(p) ? readFileSync(p) : null;
    let same = !!have && have.equals(buf);
    // the meta lists the cache's tiles: later fetches for other stages add to it without changing this bake
    if (!same && have && f.endsWith('_meta.json')) { const a = JSON.parse(have), b = JSON.parse(metaText); delete a.source.files1m; delete a.source.files13; delete b.source.files1m; delete b.source.files13; same = JSON.stringify(a) === JSON.stringify(b); }
    console.log(`  check ${f}: ${same ? 'same' : 'DIFFERENT'}`);
    if (!same) bad++;
  }
  console.log(bad ? `CHECK FAILED: ${bad} file(s) differ from a fresh bake` : 'CHECK OK: the files on disk are this bake');
  process.exit(bad ? 1 : 0);
}
for (const [d, f, buf] of outputs) { mkdirSync(d, { recursive: true }); writeFileSync(join(d, f), buf); }
store.saveLedger();
console.log(`  wrote ${outputs.map(o => o[1]).join(', ')} to ${outRes === resDir ? 'Assets' : outRes}`);
console.log(`  start line ${stage.startLineM.toFixed(1)} m, finish ${stage.finishM.toFixed(1)} m of ${(N * SP)} m; read this run ${(store.sessionBytes() / 1e6).toFixed(1)} MB`);
console.log('OK');
