// canopy.mjs - build Charlotte's canopy grid, charlotte_canopy.bytes (plan WP-08)
//
//   node tools/city/canopy.mjs --check            rebuild in memory, compare with the shipped file
//   node tools/city/canopy.mjs --out <dir>        write <dir>/charlotte_canopy.bytes
//   (either way it prints the grid's numbers: the box, the core, and the
//   canopy within 25 m of the centreline by road class)
//
// Input: the USDA Forest Service NLCD Tree Canopy Cover raster that
// fetch/fetch_canopy.mjs caches (2024, 30 m, public domain), and the DEM's
// lattice off Resources/charlotte_dem.bytes, so each canopy cell is the same
// 60 m of ground as a DEM cell. Output: PCAN v1 (lib/canopygrid.mjs), one u8
// percent per cell, 0.74 MB raw. Its own file, not a section of
// charlotte_city.bytes: the road export is rewritten by the lines release
// (R4) and this is not derived from the roads, so the two never have to be
// re-made together. CityCanopy.cs reads it; CityTrees plants from it.

import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliCompressSync, constants as Z } from 'node:zlib';
import { loadCanopyRaster, demLattice, buildCanopyGrid, encodeCanopy, albers102008, toLat, toLon, LAT0, LON0, M_LAT, M_LON } from './lib/canopygrid.mjs';
import { parseCity, classOf } from './lib/citydata.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const RES = join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources');
const args = process.argv.slice(2);
const oi = args.indexOf('--out');
const outDir = oi >= 0 ? args[oi + 1] : null;
const check = args.includes('--check');
const YEAR = 2024;

const t0 = Date.now();
const raster = loadCanopyRaster(YEAR);
const lat = demLattice(readFileSync(join(RES, 'charlotte_dem.bytes')));
const grid = buildCanopyGrid(raster, lat);
const bytes = encodeCanopy(lat, grid, YEAR);
const br = brotliCompressSync(bytes, { params: { [Z.BROTLI_PARAM_QUALITY]: 11, [Z.BROTLI_PARAM_LGWIN]: 24, [Z.BROTLI_PARAM_SIZE_HINT]: bytes.length } }).length;

// ---- the numbers
const city = parseCity(readFileSync(join(RES, 'charlotte_city.bytes')));
const up = city.uptown;
const mean = f => { let s = 0, n = 0; for (let iz = 0; iz < lat.nz; iz++) for (let ix = 0; ix < lat.nx; ix++) { const x = lat.x0 + ix * lat.cell, z = lat.z0 + iz * lat.cell; if (f(x, z)) { s += grid[iz * lat.nx + ix]; n++; } } return n ? s / n : 0; };
const core = (x, z) => Math.abs(x - up[0]) <= 4000 && Math.abs(z - up[1]) <= 4000;
console.log(`canopy grid ${lat.nx} x ${lat.nz} at ${lat.cell} m (the DEM's lattice), ${YEAR}: box mean ${mean(() => true).toFixed(1)}%, 8 km core ${mean(core).toFixed(1)}%, within 1 km of uptown ${mean((x, z) => Math.hypot(x - up[0], z - up[1]) < 1000).toFixed(1)}%`);
console.log(`size ${(bytes.length / 1e3).toFixed(0)} KB raw, ${(br / 1e3).toFixed(0)} KB Brotli; built in ${((Date.now() - t0) / 1e3).toFixed(1)} s`);

// Canopy within 25 m of the centreline, by class, off the 30 m raster (the
// plan's WP-08 target is the planted crowns within +-5 points of this):
// samples every 10 m along each edge, at 0, +-5 .. +-25 m across.
const bands = { motorway: [], secondary: [], 'core residential': [], 'core arterials': [] };
for (const e of city.edges) {
  const c = classOf(e);
  const pts = e.pts;
  const mid = pts[pts.length >> 1];
  const inCore = Math.abs(mid[0] - up[0]) <= 4000 && Math.abs(mid[1] - up[1]) <= 4000;
  const want = [];
  if (c === 'motorway') want.push('motorway');
  if (c === 'secondary') want.push('secondary');
  if (inCore && c === 'local') want.push('core residential');
  if (inCore && (c === 'primary' || c === 'secondary')) want.push('core arterials');
  if (!want.length) continue;
  for (let k = 0; k + 1 < pts.length; k++) {
    const [ax, az] = pts[k], [bx, bz] = pts[k + 1], L = Math.hypot(bx - ax, bz - az);
    if (L < 1e-3) continue;
    const nx = -(bz - az) / L, nz = (bx - ax) / L;
    for (let s = 5; s < L; s += 10)
      for (let o = -25; o <= 25; o += 5) {
        const x = ax + (bx - ax) * s / L + nx * o, z = az + (bz - az) * s / L + nz * o;
        const [px, py] = albers102008(toLat(z), toLon(x));
        const v = raster.at(px, py);
        for (const w of want) bands[w].push(v);
      }
  }
}
const band = {};
for (const [k, v] of Object.entries(bands)) band[k] = v.length ? +(v.reduce((a, b) => a + b, 0) / v.length).toFixed(1) : null;
console.log('canopy within 25 m of the centreline (30 m raster): ' + Object.entries(band).map(([k, v]) => `${k} ${v}%`).join(', '));

// THE TREE AUDIT'S TRUTH. CityAudit's TreeAudit measures the canopy its
// planted crowns make within 25 m of the centreline, by class, over a fixed
// sample of tiles (the plan's shot spots, 3 x 3 each, and every 9th tile of
// a lattice over the network). The game ships the 60 m grid, not the 30 m
// raster, so the raster's own value over EXACTLY those samples is worked out
// here and written into the audit (CityAudit.Trees.cs, CanopyBandTruth):
// same tiles, same 10 m stations on each edge's own arc lattice, same
// offsets 0, +-5 .. +-25 m, inside the tile only. f32 like the C#.
{
  const f = Math.fround, TS = 256;
  const LL = (lat, lon) => [f((lon - LON0) * M_LON), f((lat - LAT0) * M_LAT)];
  const key = (tx, tz) => tx + ',' + tz;
  const tiles = [], seen = new Set();
  const add = (x, z) => { const tx = Math.floor(x / TS), tz = Math.floor(z / TS); if (!seen.has(key(tx, tz))) { seen.add(key(tx, tz)); tiles.push([tx, tz]); } };
  for (const [lat, lon] of [[35.19280, -80.83678], [35.20225, -80.84758], [35.22019, -80.80899], [35.16804, -80.74309], [35.06068, -80.76398], [35.22662, -80.84248]]) {
    const [cx, cz] = LL(lat, lon);
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) add(f(cx + dx * TS), f(cz + dz * TS));
  }
  let lx = Infinity, lz = Infinity, hx = -Infinity, hz = -Infinity;
  for (const n of city.nodes) { lx = Math.min(lx, n.x); lz = Math.min(lz, n.z); hx = Math.max(hx, n.x); hz = Math.max(hz, n.z); }
  for (let z = f(lz + 4 * TS); z < hz; z = f(z + 9 * TS)) for (let x = f(lx + 4 * TS); x < hx; x = f(x + 9 * TS)) add(x, z);
  const pointAt = (e, at) => {
    const pts = e.pts;
    for (let k = 1; k < pts.length; k++) if (e.s[k] >= at || k === pts.length - 1) {
      const L = e.s[k] - e.s[k - 1], t = L > 1e-9 ? Math.min(1, Math.max(0, (at - e.s[k - 1]) / L)) : 0;
      return [pts[k - 1][0] + (pts[k][0] - pts[k - 1][0]) * t, pts[k - 1][1] + (pts[k][1] - pts[k - 1][1]) * t];
    }
    return pts[0];
  };
  const inCoreOf = new Map();
  const acc = {};
  const tileSet = new Set(tiles.map(([a, b]) => key(a, b)));
  for (const e of city.edges) {
    if (e.tunnel) continue;
    const c = e.link ? null : e.rank >= 5 ? 'motorway' : e.rank === 2 ? 'secondary' : null;
    const m = pointAt(e, e.length * 0.5);
    const inCore = m[0] >= up[0] - 4000 && m[0] < up[0] + 4000 && m[1] >= up[1] - 4000 && m[1] < up[1] + 4000;
    const c2 = !inCore || e.link ? null : e.rank === 0 ? 'core residential' : (e.rank === 2 || e.rank === 3) ? 'core arterials' : null;
    if (!c && !c2) continue;
    for (let si = 0; si + 1 < e.pts.length; si++) {
      const [ax, az] = e.pts[si], [bx, bz] = e.pts[si + 1], L = Math.hypot(bx - ax, bz - az);
      if (L < 1e-3) continue;
      const dx = (bx - ax) / L, dz = (bz - az) / L, s0 = e.s[si];
      for (let st = Math.ceil((s0 - 5) / 10) * 10 + 5; st < s0 + L; st += 10) {
        if (st < s0) continue;
        const cx = ax + dx * (st - s0), cz = az + dz * (st - s0);
        for (let o = -25; o <= 25; o += 5) {
          const qx = cx - dz * o, qz = cz + dx * o;
          if (!tileSet.has(key(Math.floor(qx / TS), Math.floor(qz / TS)))) continue;
          const [px, py] = albers102008(toLat(qz), toLon(qx));
          const v = raster.at(px, py) / 100;
          for (const k of [c, c2]) if (k) { acc[k] ??= { n: 0, sum: 0 }; acc[k].n++; acc[k].sum += v; }
        }
      }
    }
  }
  console.log(`tree audit truth (the 30 m raster over the audit's ${tiles.length} tiles): ` +
    Object.entries(acc).map(([k, v]) => `${k} ${(100 * v.sum / v.n).toFixed(1)}% (${v.n} points)`).join(', '));
  console.log('  C#: ' + Object.entries(acc).map(([k, v]) => `("${k}", ${(100 * v.sum / v.n).toFixed(1)}f, ${v.n})`).join(', '));
}

if (outDir) {
  mkdirSync(outDir, { recursive: true });
  writeFileSync(join(outDir, 'charlotte_canopy.bytes'), bytes);
  console.log(`wrote ${join(outDir, 'charlotte_canopy.bytes')}`);
}
if (check) {
  const shipped = join(RES, 'charlotte_canopy.bytes');
  if (!existsSync(shipped)) { console.log('CHECK FAIL: no shipped charlotte_canopy.bytes'); process.exit(1); }
  const had = readFileSync(shipped);
  if (!had.equals(bytes)) { console.log('CHECK FAIL: charlotte_canopy.bytes differs from its rebuild'); process.exit(1); }
  console.log('CHECK OK: charlotte_canopy.bytes is the rebuild, byte for byte');
}
