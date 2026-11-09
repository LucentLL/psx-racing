// bake_wide.mjs - the real land round a mountain stage, out to the horizon
//
//   node tools/roads/bake_wide.mjs [prefix ...]      (default: every stage that has a road bake)
//
// A stage's road bake (fetch_road.mjs / fetch_loop.mjs) writes two height
// grids: the corridor at 12 m and the mountains round it at 60 m, 9 km past
// the route. The PC build draws its land much further than that (the stage
// horizon, PSXRacingBuilder.Horizon, 2026-11-09), and past the 60 m grid it
// had only that grid mirrored about its own edges - made-up ground. This
// writes the real ground there: <prefix>_dem_wide.bytes, 240 m cells, 36 km
// past the 60 m grid on every side, in the same frame and the same units
// (int16 decimetres above the bake's baseM, row-major from the south-west
// corner), and adds its "wide" entry to <prefix>_dem_meta.json.
//
// Elevation: the same SRTM 1-arc-second tiles the road bake used (USGS/NASA,
// public domain), from the same cache - so a ridge the 60 m grid ends on
// carries straight on. Most of the tiles are already on disk from the road
// bakes. NOTHING IS DOWNLOADED unless --fetch is given (a new download is the
// owner's call): where a tile is not in the cache, the ground there is the
// neighbouring tile mirrored about the shared edge, and the run says so.
// With --fetch a missing tile comes from the same mirror the road bakes use
// (about 10 MB each). The cache is tools/roads/cache, or PSX_ROADS_CACHE.
//
// The road, its corridor and the 60 m grid are not touched: nothing a car
// can reach moves.

import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gunzipSync } from 'node:zlib';
import { fetchCached, makeProjection } from './lib.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const projectRoot = join(here, '..', '..');
const cacheDir = process.env.PSX_ROADS_CACHE || join(here, 'cache');
const artRoot = join(projectRoot, 'Assets', 'PSXRacing', 'Art');
const resRoot = join(projectRoot, 'Assets', 'PSXRacing', 'Resources');
const WIDE_CELL = 240, WIDE_MARGIN = 36000, SNAP = 480;

const doFetch = process.argv.includes('--fetch');
const want = process.argv.slice(2).filter(a => !a.startsWith('--'));

// ---- the tiles: from the cache; a missing one is null (or fetched with --fetch)
const POSTS = 3601;
const tiles = new Map(), missing = new Set();
const tileKey = (latS, lonW) => `N${String(latS).padStart(2, '0')}W${String(-lonW).padStart(3, '0')}`;
async function tile(latS, lonW) {
  const key = tileKey(latS, lonW);
  if (tiles.has(key)) return tiles.get(key);
  let gz = null;
  const path = join(cacheDir, key + '.hgt.gz');
  if (existsSync(path)) gz = readFileSync(path);
  else if (doFetch) gz = await fetchCached(cacheDir, key + '.hgt.gz',
    `https://s3.amazonaws.com/elevation-tiles-prod/skadi/N${String(latS).padStart(2, '0')}/${key}.hgt.gz`);
  let t = null;
  if (gz) {
    const raw = gunzipSync(gz);
    if (raw.length !== POSTS * POSTS * 2) throw new Error(`${key}: unexpected size ${raw.length}`);
    t = { raw, latN: latS + 1, lonW };
  } else missing.add(key);
  tiles.set(key, t);
  return t;
}
function sampleTile(t, lat, lon) {
  const at = (r, c) => {
    r = Math.min(POSTS - 1, Math.max(0, r)); c = Math.min(POSTS - 1, Math.max(0, c));
    const v = t.raw.readInt16BE((r * POSTS + c) * 2);
    return v > -32000 ? v : 0;
  };
  const fr = (t.latN - lat) * 3600, fc = (lon - t.lonW) * 3600;
  const r0 = Math.floor(fr), c0 = Math.floor(fc), tr = fr - r0, tc = fc - c0;
  return (at(r0, c0) * (1 - tc) + at(r0, c0 + 1) * tc) * (1 - tr) + (at(r0 + 1, c0) * (1 - tc) + at(r0 + 1, c0 + 1) * tc) * tr;
}
/// Metres ASL; where the tile is not on disk, the nearest tile that is,
/// mirrored about the edge between them.
async function sampler(bbox) {
  for (let la = Math.floor(bbox.s); la <= Math.floor(bbox.n); la++)
    for (let lo = Math.floor(bbox.w); lo <= Math.floor(bbox.e); lo++) await tile(la, lo);
  const get = (lat, lon) => tiles.get(tileKey(Math.floor(lat), Math.floor(lon))) || null;
  return (lat, lon) => {
    let t = get(lat, lon);
    if (t) return sampleTile(t, lat, lon);
    const la = Math.floor(lat), lo = Math.floor(lon);
    // reflect about the nearest edge whose far side is on disk
    const tries = [[2 * la - lat, lon], [2 * (la + 1) - lat, lon], [lat, 2 * lo - lon], [lat, 2 * (lo + 1) - lon],
                   [2 * la - lat, 2 * (lo + 1) - lon], [2 * (la + 1) - lat, 2 * (lo + 1) - lon], [2 * la - lat, 2 * lo - lon], [2 * (la + 1) - lat, 2 * lo - lon]];
    tries.sort((a, b) => (Math.abs(a[0] - lat) + Math.abs(a[1] - lon)) - (Math.abs(b[0] - lat) + Math.abs(b[1] - lon)));
    for (const [a, b] of tries) { t = get(a, b); if (t) return sampleTile(t, a, b); }
    return 0;
  };
}
let done = 0;
for (const dir of readdirSync(artRoot, { withFileTypes: true })) {
  if (!dir.isDirectory()) continue;
  for (const f of readdirSync(join(artRoot, dir.name))) {
    const m = /^(.+)_dem_meta\.json$/.exec(f);
    if (!m) continue;
    const prefix = m[1];
    if (want.length && !want.includes(prefix)) continue;
    const stagePath = join(resRoot, prefix + '_stage.json');
    if (!existsSync(stagePath)) { console.log(`${prefix}: no ${prefix}_stage.json - skipped`); continue; }
    const stage = JSON.parse(readFileSync(stagePath, 'utf8'));
    if (typeof stage.lat0 !== 'number' || typeof stage.lon0 !== 'number') { console.log(`${prefix}: no lat0/lon0 - skipped`); continue; }
    const metaPath = join(artRoot, dir.name, f);
    const meta = JSON.parse(readFileSync(metaPath, 'utf8'));
    const far = meta.far;
    const proj = makeProjection(stage.lat0, stage.lon0);
    const fx1 = far.originX + (far.cols - 1) * far.cell, fz1 = far.originZ + (far.rows - 1) * far.cell;
    const ox = Math.floor((far.originX - WIDE_MARGIN) / SNAP) * SNAP, oz = Math.floor((far.originZ - WIDE_MARGIN) / SNAP) * SNAP;
    const cols = Math.ceil((fx1 + WIDE_MARGIN - ox) / WIDE_CELL) + 1, rows = Math.ceil((fz1 + WIDE_MARGIN - oz) / WIDE_CELL) + 1;
    const sw = proj.toLL(ox, oz), ne = proj.toLL(ox + (cols - 1) * WIDE_CELL, oz + (rows - 1) * WIDE_CELL);
    console.log(`${prefix}: ${cols} x ${rows} @ ${WIDE_CELL} m, lat ${sw.lat.toFixed(3)}..${ne.lat.toFixed(3)} lon ${sw.lon.toFixed(3)}..${ne.lon.toFixed(3)}`);
    missing.clear();
    const elevAt = await sampler({ s: sw.lat, n: ne.lat, w: sw.lon, e: ne.lon });
    if (missing.size) console.log(`  NOT ON DISK: ${[...missing].join(' ')} - mirrored from the neighbouring tile (run with --fetch to download)`);
    const buf = Buffer.alloc(cols * rows * 2);
    let lo = Infinity, hi = -Infinity;
    for (let r = 0; r < rows; r++)
      for (let c = 0; c < cols; c++) {
        const ll = proj.toLL(ox + c * WIDE_CELL, oz + r * WIDE_CELL);
        // the mean of the cell, not one post: a 240 m cell on a 30 m grid aliases its ridges
        let h = 0;
        for (let j = -1; j <= 1; j++)
          for (let i = -1; i <= 1; i++)
            h += elevAt(ll.lat + j * 80 / proj.mLat, ll.lon + i * 80 / proj.mLon);
        h /= 9;
        lo = Math.min(lo, h); hi = Math.max(hi, h);
        buf.writeInt16LE(Math.max(-32767, Math.min(32767, Math.round((h - meta.baseM) * 10))), (r * cols + c) * 2);
      }
    writeFileSync(join(artRoot, dir.name, prefix + '_dem_wide.bytes'), buf);
    meta.wide = { originX: ox, originZ: oz, cell: WIDE_CELL, cols, rows };
    writeFileSync(metaPath, JSON.stringify(meta, null, 2));
    console.log(`  ${prefix}_dem_wide.bytes ${(buf.length / 1024).toFixed(0)} KB, ${lo.toFixed(0)}..${hi.toFixed(0)} m ASL`);
    done++;
  }
}
console.log(`OK ${done} stage(s)`);
