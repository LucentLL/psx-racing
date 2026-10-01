// fetch_3dep.mjs - the USGS 3DEP 1/3 arc-second ground under Charlotte (plan WP-04)
//
//   node tools/city/fetch/fetch_3dep.mjs [--force]
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

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = dem3depDir();
const BASE = 'https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/current';
const CELLS = ['n36w081', 'n36w082', 'n35w081', 'n35w082'];
const UA = 'psx-racing-city-bake/1.0 (game map data; https://github.com/LucentLL/psx-racing)';
/// [west, south, east, north] degrees: the DEM box (lat 34.9875..35.4815,
/// lon -81.1052..-80.5710) padded, so the 60 m area means of its edge nodes
/// and the creek beds near its rim are inside.
const BOX = [-81.12, 34.975, -80.555, 35.495];
const RES = 1 / 10800;          // one third of an arc-second
const force = process.argv.includes('--force');

const sleep = ms => new Promise(r => setTimeout(r, ms));
async function fetchRange(url, start, end) {
  let last;
  for (let attempt = 0; attempt < 6; attempt++) {
    try {
      const r = await fetch(url, { headers: { 'User-Agent': UA, Range: `bytes=${start}-${end}` } });
      if (r.status !== 206 && r.status !== 200) throw new Error(`HTTP ${r.status}`);
      const b = Buffer.from(await r.arrayBuffer());
      if (b.length !== end - start + 1) throw new Error(`short read ${b.length} of ${end - start + 1}`);
      return b;
    } catch (e) { last = e; await sleep(1500 * (attempt + 1)); }
  }
  throw new Error(`${url} [${start}-${end}]: ${last.message}`);
}
async function head(url) {
  const r = await fetch(url, { method: 'HEAD', headers: { 'User-Agent': UA } });
  if (!r.ok) throw new Error(`HEAD ${url}: HTTP ${r.status}`);
  return { bytes: +r.headers.get('content-length'), lastModified: r.headers.get('last-modified'), etag: r.headers.get('etag') };
}

// ---------------------------------------------------------------- TIFF
/// The first image directory of a classic little-endian TIFF: the tags this
/// needs, arrays read in full (fetching more of the header when an array lies
/// past what was read).
async function readIfd(url) {
  let buf = await fetchRange(url, 0, 65535);
  const ensure = async n => { if (n > buf.length) buf = Buffer.concat([buf, await fetchRange(url, buf.length, n + 65535)]); };
  if (buf.toString('latin1', 0, 2) !== 'II' || buf.readUInt16LE(2) !== 42) throw new Error(`${url}: not a classic little-endian TIFF`);
  const off = buf.readUInt32LE(4);
  await ensure(off + 2);
  const n = buf.readUInt16LE(off);
  await ensure(off + 2 + n * 12 + 4);
  const SIZE = { 1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 11: 4, 12: 8, 16: 8 };
  const tags = {};
  for (let k = 0; k < n; k++) {
    const p = off + 2 + k * 12;
    const tag = buf.readUInt16LE(p), type = buf.readUInt16LE(p + 2), count = buf.readUInt32LE(p + 4);
    const bytes = (SIZE[type] || 1) * count;
    let at = p + 8;
    if (bytes > 4) { at = buf.readUInt32LE(p + 8); await ensure(at + bytes); }
    const vals = [];
    for (let i = 0; i < count && i < 1e6; i++) {
      if (type === 3) vals.push(buf.readUInt16LE(at + 2 * i));
      else if (type === 4) vals.push(buf.readUInt32LE(at + 4 * i));
      else if (type === 12) vals.push(buf.readDoubleLE(at + 8 * i));
      else if (type === 16) vals.push(Number(buf.readBigUInt64LE(at + 8 * i)));
      else if (type === 2) { vals.push(buf.toString('latin1', at, at + count).replace(/\0+$/, '')); break; }
      else vals.push(buf[at + i]);
    }
    tags[tag] = vals;
  }
  const one = t => tags[t]?.[0];
  const d = {
    width: one(256), height: one(257), bits: one(258), compression: one(259), predictor: one(317) || 1,
    tileW: one(322), tileH: one(323), offsets: tags[324], counts: tags[325], format: one(339) || 1,
    scale: tags[33550], tie: tags[33922], nodata: tags[42113] ? parseFloat(tags[42113][0]) : null,
  };
  if (d.compression !== 5 || d.predictor !== 3 || d.bits !== 32 || d.format !== 3 || !d.tileW)
    throw new Error(`${url}: expected tiled float32 LZW with the floating-point predictor, got ${JSON.stringify({ c: d.compression, p: d.predictor, b: d.bits, f: d.format, t: d.tileW })}`);
  return d;
}

/// TIFF LZW (MSB-first codes, 9-12 bits, early change: the width grows when
/// the next free code reaches 2^width - 1, as libtiff decodes it).
function lzwDecode(src, expected) {
  const out = new Uint8Array(expected);
  const prefix = new Int32Array(4096), suffix = new Uint8Array(4096), first = new Uint8Array(4096), len = new Int32Array(4096);
  for (let i = 0; i < 256; i++) { prefix[i] = -1; suffix[i] = i; first[i] = i; len[i] = 1; }
  let next = 258, width = 9, old = -1, op = 0, bit = 0;
  const nbits = src.length * 8;
  const emit = code => {
    const L = len[code];
    if (op + L > out.length) throw new Error('LZW: output overrun');
    let c = code;
    for (let k = op + L - 1; k >= op; k--) { out[k] = suffix[c]; c = prefix[c]; }
    op += L;
  };
  for (;;) {
    if (bit + width > nbits) break;
    let v = 0, need = width, p = bit;
    while (need > 0) {
      const off = p & 7, avail = 8 - off, take = avail < need ? avail : need;
      v = (v << take) | ((src[p >> 3] >> (avail - take)) & ((1 << take) - 1));
      need -= take; p += take;
    }
    bit += width;
    const code = v;
    if (code === 257) break;
    if (code === 256) { next = 258; width = 9; old = -1; continue; }
    if (old < 0) {
      if (code > 255) throw new Error('LZW: bad first code');
      emit(code); old = code; continue;
    }
    if (code < next) {
      emit(code);
      if (next < 4096) { prefix[next] = old; suffix[next] = first[code]; first[next] = first[old]; len[next] = len[old] + 1; next++; }
    } else if (code === next) {
      if (next < 4096) { prefix[next] = old; suffix[next] = first[old]; first[next] = first[old]; len[next] = len[old] + 1; next++; }
      emit(code);
    } else throw new Error(`LZW: code ${code} past the table (${next})`);
    old = code;
    if (next + 1 >= (1 << width) && width < 12) width++;
  }
  if (op !== expected) throw new Error(`LZW: ${op} bytes, expected ${expected}`);
  return out;
}

/// Undo TIFF predictor 3 (floating point, TIFF Technical Note 3) on one tile:
/// per row, a byte-wise running sum, then the byte planes (most significant
/// first) re-interleaved into little-endian float32.
function undoFloatPredictor(bytes, w, h) {
  const out = new Float32Array(w * h);
  const le = new Uint8Array(out.buffer);
  const rowB = w * 4;
  for (let y = 0; y < h; y++) {
    const r = y * rowB;
    for (let i = 1; i < rowB; i++) bytes[r + i] = (bytes[r + i] + bytes[r + i - 1]) & 255;
    for (let i = 0; i < w; i++)
      for (let b = 0; b < 4; b++) le[r + 4 * i + b] = bytes[r + (3 - b) * w + i];
  }
  return out;
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
  what: 'USGS 3DEP 1/3 arc-second DEM (public domain), the Charlotte DEM box, float32 metres NAVD88, rows north to south',
  credit: 'U.S. Geological Survey, 3D Elevation Program',
  res_deg: RES, col0, row0, cols, rows,
  west: col0 * RES - 180, north: 90 - row0 * RES,
  pixel: 'pixel (c, r) covers lon west + [c, c+1] * res_deg, lat north - [r, r+1] * res_deg (PixelIsArea)',
  range_m: [lo, hi], sources, fetched: new Date().toISOString(), bytes_read: fetchedBytes,
  sha256: createHash('sha256').update(bytes).digest('hex'),
};
writeFileSync(jsonPath, JSON.stringify(meta, null, 2) + '\n');
console.log(`wrote ${f32Path} (${(bytes.length / 1e6).toFixed(1)} MB; ${lo.toFixed(1)}..${hi.toFixed(1)} m; read ${(fetchedBytes / 1e6).toFixed(0)} MB)`);
