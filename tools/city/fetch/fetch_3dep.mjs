// fetch_3dep.mjs - the USGS 3DEP 1/3 arc-second ground under Charlotte (plan WP-04)
//
//   node tools/city/fetch/fetch_3dep.mjs [--force] [--out <dir>] [--box w,s,e,n]
//
// Charlotte's ground used to be the AWS Terrain Tiles "skadi" 1" tiles, run
// through an opening, a closing and a blur that took out 66% of the core's
// relief (survey_flatness). This fetches its replacement: the USGS 3D
// Elevation Program's current 1/3 arc-second DEM (about 10 m; public domain,
// credit "U.S. Geological Survey, 3D Elevation Program"), cells n35w081,
// n35w082, n36w081 and n36w082, dated 2026-04-17 on the USGS staging bucket.
//
// The four files are 1.94 GB whole. They are Cloud Optimized GeoTIFFs (512 px
// tiles, LZW with the floating-point predictor), so this reads only the tiles
// over the DEM box with HTTP range requests (about 0.1 GB) and decodes them
// itself: a TIFF directory reader, an LZW decoder and the predictor, no npm
// packages. The four cells share one global 1/10800-degree pixel lattice
// (each file carries a 6 px border copied from its neighbours; each pixel is
// taken from the file whose own 1 x 1 degree cell holds it).
//
// Output (gitignored, ~140 MB): <dir>/box13.f32, the box as little-endian
// float32 rows north to south, and <dir>/box13.json, its georeference and the
// source files' Last-Modified / ETag / sizes, plus the sha256 of box13.f32.
// <dir> is %PSX_GIS_DIR%\3dep when PSX_GIS_DIR is set, otherwise
// tools/city/cache/3dep. export_osm.mjs reads it through lib/dem3dep.mjs.

import { writeFileSync, existsSync, mkdirSync, openSync, writeSync, closeSync, readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { dem3depDir } from '../lib/dem3dep.mjs';
import { makeFetcher, readIfds, assertFloatLzw, lzwDecode, undoFloatPredictor } from '../lib/cog.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const arg = n => { const i = process.argv.indexOf(n); return i >= 0 ? process.argv[i + 1] : null; };
// --out <dir> and --box w,s,e,n fetch another box into another folder (the
// defaults are Charlotte's, unchanged); the cells are worked out from the box.
const OUT = arg('--out') || dem3depDir();
const BASE = 'https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/current';
const CITY_CELLS = ['n36w081', 'n36w082', 'n35w081', 'n35w082'];
const UA = 'psx-racing-city-bake/1.0 (game map data; https://github.com/LucentLL/psx-racing)';
/// [west, south, east, north] degrees: the DEM box (lat 34.9875..35.4815,
/// lon -81.1052..-80.5710) padded, so the 60 m area means of its edge nodes
/// and the creek beds near its rim are inside.
const BOX = arg('--box') ? arg('--box').split(',').map(Number) : [-81.12, 34.975, -80.555, 35.495];
if (BOX.length !== 4 || BOX.some(v => !Number.isFinite(v)) || BOX[0] >= BOX[2] || BOX[1] >= BOX[3]) throw new Error('--box w,s,e,n in degrees');
const CELLS = arg('--box') ? (() => {
  const c = [];
  for (let lat = Math.ceil(BOX[3]); lat > Math.floor(BOX[1]); lat--)
    for (let lon = Math.floor(BOX[0]); lon < Math.ceil(BOX[2]); lon++) c.push(`n${String(lat).padStart(2, '0')}w${String(-lon).padStart(3, '0')}`);
  return c;
})() : CITY_CELLS;
const RES = 1 / 10800;          // one third of an arc-second
const force = process.argv.includes('--force');

// The range reader, the TIFF directory reader, the LZW decoder and the
// floating-point predictor live in lib/cog.mjs now, shared with the mountain
// stages' 3DEP reads (tools/roads/lib3dep.mjs). Same code, same bytes.
const { fetchRange, head } = makeFetcher(UA);
async function readIfd(url) {
  const d = (await readIfds(fetchRange, url, { firstOnly: true })).ifds[0];
  assertFloatLzw(url, d);
  return d;
}

// ---------------------------------------------------------------- mosaic
mkdirSync(OUT, { recursive: true });
const f32Path = join(OUT, 'box13.f32'), jsonPath = join(OUT, 'box13.json');
if (existsSync(f32Path) && existsSync(jsonPath) && !force) {
  console.log(`${f32Path} exists (use --force to fetch again)`);
  process.exit(0);
}
const col0 = Math.floor((BOX[0] + 180) / RES), col1 = Math.ceil((BOX[2] + 180) / RES);
const row0 = Math.floor((90 - BOX[3]) / RES), row1 = Math.ceil((90 - BOX[1]) / RES);
const cols = col1 - col0, rows = row1 - row0;
console.log(`box ${cols} x ${rows} px at 1/3" (${(cols * rows * 4 / 1e6).toFixed(0)} MB float32)`);
const grid = new Float32Array(cols * rows).fill(NaN);
const sources = [];
let fetchedBytes = 0;
for (const cell of CELLS) {
  const url = `${BASE}/${cell}/USGS_13_${cell}.tif`;
  const h = await head(url);
  const d = await readIfd(url);
  // the file's top-left corner, from its tie point; its pixels on the global lattice
  const lonTL = d.tie[3], latTL = d.tie[4];
  const gc0 = Math.round((lonTL + 180) / RES), gr0 = Math.round((90 - latTL) / RES);
  if (Math.abs(d.scale[0] - RES) > 1e-12 || Math.abs(d.scale[1] - RES) > 1e-11) throw new Error(`${cell}: pixel size ${d.scale}`);
  // the file's own 1 x 1 degree cell (not its 6 px border)
  const latN = +cell.slice(1, 3), lonW = -(+cell.slice(4, 7));
  const coreC0 = Math.round((lonW + 180) / RES), coreR0 = Math.round((90 - latN) / RES);
  const cA = Math.max(col0, coreC0), cB = Math.min(col1, coreC0 + 10800);
  const rA = Math.max(row0, coreR0), rB = Math.min(row1, coreR0 + 10800);
  if (cA >= cB || rA >= rB) { console.log(`${cell}: outside the box`); continue; }
  const tilesAcross = Math.ceil(d.width / d.tileW);
  const need = [];
  for (let ty = Math.floor((rA - gr0) / d.tileH); ty <= Math.floor((rB - 1 - gr0) / d.tileH); ty++)
    for (let tx = Math.floor((cA - gc0) / d.tileW); tx <= Math.floor((cB - 1 - gc0) / d.tileW); tx++) need.push([tx, ty]);
  console.log(`${cell}: ${d.width} x ${d.height}, ${need.length} tiles of ${d.tileW} over the box (Last-Modified ${h.lastModified})`);
  let done = 0, cellBytes = 0;
  const work = need.slice();
  const worker = async () => {
    while (work.length) {
      const [tx, ty] = work.shift();
      const ti = ty * tilesAcross + tx;
      const raw = await fetchRange(url, d.offsets[ti], d.offsets[ti] + d.counts[ti] - 1);
      cellBytes += raw.length;
      const px = undoFloatPredictor(lzwDecode(raw, d.tileW * d.tileH * 4), d.tileW, d.tileH);
      for (let y = 0; y < d.tileH; y++) {
        const gr = gr0 + ty * d.tileH + y;
        if (gr < rA || gr >= rB) continue;
        for (let x = 0; x < d.tileW; x++) {
          const gc = gc0 + tx * d.tileW + x;
          if (gc < cA || gc >= cB) continue;
          const v = px[y * d.tileW + x];
          grid[(gr - row0) * cols + (gc - col0)] = d.nodata !== null && v <= d.nodata + 1 ? NaN : v;
        }
      }
      if (++done % 20 === 0) console.log(`  ${cell}: ${done}/${need.length}`);
    }
  };
  await Promise.all([worker(), worker(), worker(), worker(), worker(), worker()]);
  fetchedBytes += cellBytes;
  sources.push({ cell, url, lastModified: h.lastModified, etag: h.etag, fileBytes: h.bytes, tilesRead: need.length, bytesRead: cellBytes });
}
let missing = 0, lo = Infinity, hi = -Infinity;
for (const v of grid) { if (Number.isNaN(v)) missing++; else { if (v < lo) lo = v; if (v > hi) hi = v; } }
if (missing) throw new Error(`${missing} pixels of the box have no data`);
const bytes = Buffer.from(grid.buffer);
const fd = openSync(f32Path, 'w');
writeSync(fd, bytes);
closeSync(fd);
const meta = {
  schema: 1,
  what: 'USGS 3DEP 1/3 arc-second DEM (public domain), ' + (arg('--box') ? 'box ' + BOX.join(',') : 'the Charlotte DEM box') + ', float32 metres NAVD88, rows north to south',
  credit: 'U.S. Geological Survey, 3D Elevation Program',
  res_deg: RES, col0, row0, cols, rows,
  west: col0 * RES - 180, north: 90 - row0 * RES,
  pixel: 'pixel (c, r) covers lon west + [c, c+1] * res_deg, lat north - [r, r+1] * res_deg (PixelIsArea)',
  range_m: [lo, hi], sources, fetched: new Date().toISOString(), bytes_read: fetchedBytes,
  sha256: createHash('sha256').update(bytes).digest('hex'),
};
writeFileSync(jsonPath, JSON.stringify(meta, null, 2) + '\n');
console.log(`wrote ${f32Path} (${(bytes.length / 1e6).toFixed(1)} MB; ${lo.toFixed(1)}..${hi.toFixed(1)} m; read ${(fetchedBytes / 1e6).toFixed(0)} MB)`);
