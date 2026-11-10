// lib3dep.mjs - USGS 3DEP ground for the mountain stages.
//
// Two products, both read as HTTP range requests of single 512 px tiles out of
// the USGS Cloud Optimized GeoTIFFs (tools/city/lib/cog.mjs does the TIFF):
//
//   1/3 arc-second (about 10 m), one file per 1 x 1 degree cell, NAD83
//   geographic, with overview levels (2x = ~19 m, 4x = ~37 m). The near grid
//   reads level 0, the 60 m far grid level 1.
//     .../StagedProducts/Elevation/13/TIFF/current/n36w083/USGS_13_n36w083.tif
//
//   1 m lidar DEM, 10 x 10 km tiles in NAD83 / UTM 17N (all eight stages are
//   inside NC_Phase5_2018_A18), level 0 = 1 m, level 1 = 2 m.
//     .../StagedProducts/Elevation/1m/Projects/NC_Phase5_2018_A18/TIFF/
//         USGS_1M_17_x39y397_NC_Phase5_2018_A18.tif
//
// Both are bare earth, metres NAVD88, public domain; credit "U.S. Geological
// Survey, 3D Elevation Program".
//
// THE CACHE. Every stored tile is kept as downloaded (its compressed bytes) in
// a folder of its own per source file:
//     <cache>/13_n36w083/L0_12_7.lzw      <cache>/1m_x39y397/L0_3_11.lzw
// with <cache>/<folder>/header.json (the parsed directories + HEAD) and one
// ledger <cache>/sources.json: URL, Last-Modified, ETag, file size, and every
// tile read with its byte count and the date. Nothing is fetched twice. The
// cache is %PSX_ROADS_CACHE%\3dep when PSX_ROADS_CACHE is set (the main
// checkout's tools/roads/cache), otherwise tools/roads/cache/3dep.
//
// DATUM. OSM is nominally WGS84 and these files NAD83; the two differ by about
// 1.0 m here (NAD83 = WGS84 + 0.6 m east, - 0.8 m north, epoch 2015). OSM's
// lines are traced from imagery whose own frame is not recorded, so no shift
// is applied: lat/lon are used as they stand and the audit reports the mean
// east/north offset it finds.

import { existsSync, mkdirSync, readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { makeFetcher, readIfds, decodeTile } from '../city/lib/cog.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
export const USER_AGENT = 'psx-racing-mountain-bake/1.0 (game map data; USGS 3DEP elevation)';
const BASE13 = 'https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/current';
const BASE1M = 'https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects';
export const PROJECT_1M = 'NC_Phase5_2018_A18';

export function cacheDir3dep() {
  return process.env.PSX_ROADS_CACHE ? join(process.env.PSX_ROADS_CACHE, '3dep') : join(HERE, 'cache', '3dep');
}

// ------------------------------------------------------------------ UTM
// Transverse Mercator on GRS80 by the Krueger series (sub-millimetre inside a
// zone). Zone 17: central meridian 81 W.
const A = 6378137, F = 1 / 298.257222101, K0 = 0.9996, FE = 500000;
const N_ = F / (2 - F);
const A_ = A / (1 + N_) * (1 + N_ ** 2 / 4 + N_ ** 4 / 64);
const AL = [N_ / 2 - 2 * N_ ** 2 / 3 + 5 * N_ ** 3 / 16, 13 * N_ ** 2 / 48 - 3 * N_ ** 3 / 5, 61 * N_ ** 3 / 240];
const BE = [N_ / 2 - 2 * N_ ** 2 / 3 + 37 * N_ ** 3 / 96, N_ ** 2 / 48 + N_ ** 3 / 15, 17 * N_ ** 3 / 480];
const DE = [2 * N_ - 2 * N_ ** 2 / 3 - 2 * N_ ** 3, 7 * N_ ** 2 / 3 - 8 * N_ ** 3 / 5, 56 * N_ ** 3 / 15];
export function toUtm(lat, lon, lon0 = -81) {
  const phi = lat * Math.PI / 180, lam = (lon - lon0) * Math.PI / 180;
  const e = 2 * Math.sqrt(N_) / (1 + N_);
  const t = Math.sinh(Math.atanh(Math.sin(phi)) - e * Math.atanh(e * Math.sin(phi)));
  const xi = Math.atan2(t, Math.cos(lam)), eta = Math.atanh(Math.sin(lam) / Math.sqrt(1 + t * t));
  let E = eta, Nn = xi;
  for (let j = 1; j <= 3; j++) {
    E += AL[j - 1] * Math.cos(2 * j * xi) * Math.sinh(2 * j * eta);
    Nn += AL[j - 1] * Math.sin(2 * j * xi) * Math.cosh(2 * j * eta);
  }
  return { e: FE + K0 * A_ * E, n: K0 * A_ * Nn };
}
export function fromUtm(e, n, lon0 = -81) {
  const xi = n / (K0 * A_), eta = (e - FE) / (K0 * A_);
  let xi1 = xi, eta1 = eta;
  for (let j = 1; j <= 3; j++) {
    xi1 -= BE[j - 1] * Math.sin(2 * j * xi) * Math.cosh(2 * j * eta);
    eta1 -= BE[j - 1] * Math.cos(2 * j * xi) * Math.sinh(2 * j * eta);
  }
  const chi = Math.asin(Math.sin(xi1) / Math.cosh(eta1));
  let phi = chi;
  for (let j = 1; j <= 3; j++) phi += DE[j - 1] * Math.sin(2 * j * chi);
  return { lat: phi * 180 / Math.PI, lon: lon0 + Math.atan2(Math.sinh(eta1), Math.cos(xi1)) * 180 / Math.PI };
}

// ---------------------------------------------------------------- cache
/// The tile store. `offline` refuses to touch the network (for --check and
/// for a bake that must be reproducible from what is already on disk).
export function open3dep({ dir = cacheDir3dep(), offline = false, maxDecoded = 260 } = {}) {
  mkdirSync(dir, { recursive: true });
  const net = makeFetcher(USER_AGENT);
  const ledgerPath = join(dir, 'sources.json');
  const ledger = existsSync(ledgerPath) ? JSON.parse(readFileSync(ledgerPath, 'utf8')) : {
    schema: 1,
    what: 'USGS 3DEP elevation read for the mountain stages: every source file, and every tile of it fetched by HTTP range request',
    credit: 'U.S. Geological Survey, 3D Elevation Program (public domain)',
    userAgent: USER_AGENT,
    sources: {},
  };
  let dirty = false, sessionBytes = 0;
  // Reconcile with the disk: a tile file the ledger does not list (a run that
  // was interrupted before it saved) is added with its size and file time.
  for (const folder of existsSync(dir) ? readdirSync(dir) : []) {
    const hp = join(dir, folder, 'header.json');
    if (!existsSync(hp)) continue;
    if (!ledger.sources[folder]) {
      const h = JSON.parse(readFileSync(hp, 'utf8'));
      ledger.sources[folder] = { url: h.url, lastModified: h.lastModified, etag: h.etag, fileBytes: h.bytes, headerBytes: h.headerBytes, bytesRead: h.headerBytes, firstRead: h.fetched, tiles: {} };
      dirty = true;
    }
    const s = ledger.sources[folder];
    for (const f of readdirSync(join(dir, folder))) {
      if (!f.endsWith('.lzw') || s.tiles[f.slice(0, -4)]) continue;
      const st = statSync(join(dir, folder, f));
      s.tiles[f.slice(0, -4)] = { bytes: st.size, at: st.mtime.toISOString().slice(0, 19) + 'Z' };
      dirty = true;
    }
    const sum = s.headerBytes + Object.values(s.tiles).reduce((a, t) => a + t.bytes, 0);
    if (sum !== s.bytesRead) { s.bytesRead = sum; dirty = true; }
  }
  const saveLedger = () => {
    if (!dirty) return;
    let total = 0, tiles = 0;
    for (const s of Object.values(ledger.sources)) { total += s.bytesRead; tiles += Object.keys(s.tiles).length; }
    total += ledger.otherBytes || 0;
    ledger.totalBytesRead = total; ledger.totalTiles = tiles; ledger.updated = new Date().toISOString();
    writeFileSync(ledgerPath, JSON.stringify(ledger, null, 1) + '\n');
    dirty = false;
  };
  const headers = new Map();   // folder -> promise of {url, ifds}
  function source(folder, url) {
    // ONE read per source however many tiles ask at once: six workers used to
    // each fetch the header, and each reset the folder's ledger entry.
    if (!headers.has(folder)) headers.set(folder, (async () => {
      const hp = join(dir, folder, 'header.json');
      if (existsSync(hp)) return JSON.parse(readFileSync(hp, 'utf8'));
      if (offline) throw new Error(`3DEP: ${folder} is not in the cache (${dir}) and this run is offline`);
      const hd = await net.head(url);
      const { ifds, headerBytes } = await readIfds(net.fetchRange, url);
      const h = { url, ...hd, headerBytes, fetched: new Date().toISOString(), ifds };
      mkdirSync(join(dir, folder), { recursive: true });
      writeFileSync(hp, JSON.stringify(h));
      ledger.sources[folder] = { url, lastModified: hd.lastModified, etag: hd.etag, fileBytes: hd.bytes, headerBytes, bytesRead: headerBytes, firstRead: h.fetched, tiles: {} };
      sessionBytes += headerBytes; dirty = true;
      return h;
    })());
    return headers.get(folder);
  }
  const decoded = new Map();   // key -> Float32Array (LRU by re-insertion)
  const pending = new Map();
  /// Tile (tx, ty) of overview `level` of a source, decoded. Synchronous use
  /// after `await need(...)`: tileNow().
  async function tile(folder, url, level, tx, ty) {
    const key = `${folder}/L${level}_${tx}_${ty}`;
    const hit = decoded.get(key);
    if (hit) { decoded.delete(key); decoded.set(key, hit); return hit; }
    if (pending.has(key)) return pending.get(key);
    const job = (async () => {
      const h = await source(folder, url);
      const d = h.ifds[level];
      const across = Math.ceil(d.width / d.tileW), down = Math.ceil(d.height / d.tileH);
      if (tx < 0 || ty < 0 || tx >= across || ty >= down) return null;
      const ti = ty * across + tx;
      const fp = join(dir, folder, `L${level}_${tx}_${ty}.lzw`);
      let raw;
      if (existsSync(fp)) raw = readFileSync(fp);
      else {
        if (offline) throw new Error(`3DEP: tile ${key} is not in the cache and this run is offline`);
        if (!d.counts[ti]) return null;
        raw = await net.fetchRange(h.url, d.offsets[ti], d.offsets[ti] + d.counts[ti] - 1);
        writeFileSync(fp, raw);
        const s = ledger.sources[folder];
        s.tiles[`L${level}_${tx}_${ty}`] = { bytes: raw.length, at: new Date().toISOString().slice(0, 19) + 'Z' };
        s.bytesRead += raw.length; sessionBytes += raw.length; dirty = true;
      }
      const px = decodeTile(d, raw);
      if (d.nodata !== null) for (let i = 0; i < px.length; i++) if (px[i] <= d.nodata + 1) px[i] = NaN;
      decoded.set(key, px);
      while (decoded.size > maxDecoded) decoded.delete(decoded.keys().next().value);
      pending.delete(key);
      return px;
    })();
    pending.set(key, job);
    return job;
  }
  const tileNow = (folder, level, tx, ty) => {
    const key = `${folder}/L${level}_${tx}_${ty}`;
    const hit = decoded.get(key);
    if (hit === undefined) throw new Error(`3DEP: tile ${key} was not loaded before a synchronous read`);
    return hit;
  };
  const isCached = (folder, level, tx, ty) => existsSync(join(dir, folder, `L${level}_${tx}_${ty}.lzw`));
  return { dir, ledger, source, tile, tileNow, isCached, saveLedger, sessionBytes: () => sessionBytes, net, headers, decoded };
}

/// Run async jobs `width` at a time.
async function pool(jobs, width = 6) {
  const work = jobs.slice();
  const worker = async () => { while (work.length) await work.shift()(); };
  await Promise.all(Array.from({ length: width }, worker));
}

// -------------------------------------------------------------- 1/3"
const RES13 = 1 / 10800;
const cell13 = (lat, lon) => {
  const latN = Math.ceil(lat), lonW = Math.floor(lon);      // the cell is named by its NW corner
  return { name: `n${String(latN).padStart(2, '0')}w${String(-lonW).padStart(3, '0')}`, latN, lonW };
};

/// A 1/3" reader at one overview level. `prepare(points)` (array of {lat, lon})
/// fetches what bilinear reads at those points need; `sample(lat, lon)` is
/// then synchronous. `plan(points)` only counts tiles (for a download quote).
export function dem13(store, level = 0) {
  const f = 1 << level;                       // full-res pixels per pixel of this level
  const want = new Map();
  const loaded = new Map();                   // "cell/tx_ty" -> Float32Array | null
  // file pixel coordinates: each file starts 6 px outside its cell's NW corner
  const locate = (lat, lon) => {
    const c = cell13(lat, lon);
    const fx = ((lon - c.lonW) / RES13 + 6) / f - 0.5, fy = ((c.latN - lat) / RES13 + 6) / f - 0.5;
    return { c, fx, fy };
  };
  function tilesFor(points) {
    const set = new Map();
    for (const p of points) {
      const { c, fx, fy } = locate(p.lat, p.lon);
      const x0 = Math.floor(fx), y0 = Math.floor(fy);
      for (const x of [x0, x0 + 1]) for (const y of [y0, y0 + 1])
        set.set(`${c.name}/${Math.floor(x / 512)}_${Math.floor(y / 512)}`, { cell: c.name, tx: Math.floor(x / 512), ty: Math.floor(y / 512) });
    }
    return [...set.values()];
  }
  const urlOf = cell => `${BASE13}/${cell}/USGS_13_${cell}.tif`;
  function plan(points) {
    const t = tilesFor(points);
    return { tiles: t.length, missing: t.filter(q => !store.isCached('13_' + q.cell, level, q.tx, q.ty)).length };
  }
  async function prepare(points) {
    const t = tilesFor(points).filter(q => !loaded.has(`${q.cell}/${q.tx}_${q.ty}`));
    await pool(t.map(q => async () => {
      loaded.set(`${q.cell}/${q.tx}_${q.ty}`, await store.tile('13_' + q.cell, urlOf(q.cell), level, q.tx, q.ty));
    }));
    store.saveLedger();
  }
  const px = (cell, x, y) => {
    const t = loaded.get(`${cell}/${Math.floor(x / 512)}_${Math.floor(y / 512)}`);
    if (t === undefined) throw new Error(`3DEP 1/3": pixel (${x}, ${y}) of ${cell} L${level} was not prepared`);
    return t ? t[(y & 511) * 512 + (x & 511)] : NaN;
  };
  function sample(lat, lon) {
    const { c, fx, fy } = locate(lat, lon);
    const x = Math.floor(fx), y = Math.floor(fy), tx = fx - x, ty = fy - y;
    return (px(c.name, x, y) * (1 - tx) + px(c.name, x + 1, y) * tx) * (1 - ty)
         + (px(c.name, x, y + 1) * (1 - tx) + px(c.name, x + 1, y + 1) * tx) * ty;
  }
  return { level, pixelDeg: RES13 * f, plan, prepare, sample, loaded };
}

// --------------------------------------------------------------- 1 m
/// A 1 m (level 0) or 2 m (level 1) lidar DEM reader over the UTM 17N tiles.
/// Same shape as dem13: plan / prepare / sample, points as {lat, lon}.
/// `sampleUtm(e, n)` skips the projection.
export function dem1m(store, level = 0, project = PROJECT_1M) {
  const f = 1 << level;
  const loaded = new Map();
  // Tile x39y397 covers E 390000..400000, N 3960000..3970000, with a 6 m
  // border: its first pixel's corner is (389994, 3970006).
  const locate = (e, n) => {
    const kx = Math.floor(e / 10000), ky = Math.floor(n / 10000) + 1;
    const fx = (e - (kx * 10000 - 6)) / f - 0.5, fy = ((ky * 10000 + 6) - n) / f - 0.5;
    return { name: `x${kx}y${ky}`, fx, fy };
  };
  const urlOf = name => `${BASE1M}/${project}/TIFF/USGS_1M_17_${name}_${project}.tif`;
  function tilesFor(pointsUtm) {
    const set = new Map();
    for (const p of pointsUtm) {
      const { name, fx, fy } = locate(p.e, p.n);
      const x0 = Math.floor(fx), y0 = Math.floor(fy);
      for (const x of [x0, x0 + 1]) for (const y of [y0, y0 + 1])
        set.set(`${name}/${Math.floor(x / 512)}_${Math.floor(y / 512)}`, { name, tx: Math.floor(x / 512), ty: Math.floor(y / 512) });
    }
    return [...set.values()];
  }
  const asUtm = points => points.map(p => (p.e !== undefined ? p : toUtm(p.lat, p.lon)));
  function plan(points) {
    const t = tilesFor(asUtm(points));
    return { tiles: t.length, missing: t.filter(q => !store.isCached('1m_' + q.name, level, q.tx, q.ty)).length };
  }
  async function prepare(points) {
    const t = tilesFor(asUtm(points)).filter(q => !loaded.has(`${q.name}/${q.tx}_${q.ty}`));
    await pool(t.map(q => async () => {
      loaded.set(`${q.name}/${q.tx}_${q.ty}`, await store.tile('1m_' + q.name, urlOf(q.name), level, q.tx, q.ty));
    }));
    store.saveLedger();
  }
  const px = (name, x, y) => {
    const t = loaded.get(`${name}/${Math.floor(x / 512)}_${Math.floor(y / 512)}`);
    if (t === undefined) throw new Error(`3DEP 1 m: pixel (${x}, ${y}) of ${name} L${level} was not prepared`);
    return t ? t[(y & 511) * 512 + (x & 511)] : NaN;
  };
  function sampleUtm(e, n) {
    const { name, fx, fy } = locate(e, n);
    const x = Math.floor(fx), y = Math.floor(fy), tx = fx - x, ty = fy - y;
    return (px(name, x, y) * (1 - tx) + px(name, x + 1, y) * tx) * (1 - ty)
         + (px(name, x, y + 1) * (1 - tx) + px(name, x + 1, y + 1) * tx) * ty;
  }
  const sample = (lat, lon) => { const u = toUtm(lat, lon); return sampleUtm(u.e, u.n); };
  return { level, pixelM: f, plan, prepare, sample, sampleUtm, loaded };
}

// --------------------------------------------------------------- stages
/// The eight mountain stages: bake prefix, art folder, TrackCatalog id, the
/// paved width the game builds (TrackCatalog roadWidth, metres), the bake that
/// made them and the Overpass cache file the line came from.
export const STAGES = {
  swa:     { paved: 6.1, id: 'SwissNC226A',       art: 'Swiss226A',   road: 'NC 226A, Little Switzerland',          baker: 'fetch_road.mjs swiss226a', osm: 'overpass_swiss226a.json' },
  gap:     { paved: 6.7, id: 'GillespieGap',      art: 'Gillespie',   road: 'NC 226 to Gillespie Gap',              baker: 'fetch_road.mjs gillespie', osm: 'overpass_gillespie.json' },
  mtm:     { paved: 6.1, id: 'MtMitchell',        art: 'MtMitchell',  road: 'NC 128, Mount Mitchell',               baker: 'fetch_road.mjs mtmitchell', osm: 'overpass_mtmitchell.json' },
  beech:   { paved: 6.1, id: 'BeechGap',          art: 'BeechGap',    road: 'NC 215 from Beech Gap',                baker: 'fetch_road.mjs nc215', osm: 'overpass_nc215.json' },
  chimney: { paved: 6.1, id: 'ChimneyRock',       art: 'ChimneyRock', road: 'Chimney Rock Park Road',               baker: 'fetch_road.mjs chimney', osm: 'overpass_chimney.json' },
  brp:     { paved: 6.4, id: 'BlueRidge',         art: 'BRP',         road: 'Blue Ridge Parkway, Linn Cove',        baker: 'tools/brp/fetch_brp.mjs', osm: '../../brp/cache/overpass_brp.json' },
  swiss:   { paved: 6.4, id: 'LittleSwitzerland', art: 'Switzerland', road: 'Little Switzerland - Gillespie Gap loop', baker: 'fetch_loop.mjs switzerland', osm: 'loopprobe_switzerland.json' },
  brock:   { paved: 6.4, id: 'BlowingRock',       art: 'BlowingRock', road: 'Blowing Rock - Moses Cone loop',       baker: 'fetch_loop.mjs blowingrock', osm: 'loopprobe_blowingrock.json' },
};

/// A baked stage as the tools want it: waypoints in metres, heights ASL, the
/// projection the bake used (lib.mjs makeProjection about lat0/lon0), and the
/// raw json for everything else.
export function loadStage(projectRoot, pre, { resDir = null, artDir = null } = {}) {
  const st = STAGES[pre];
  if (!st) throw new Error(`unknown stage prefix "${pre}" (${Object.keys(STAGES).join(', ')})`);
  const rd = resDir || join(projectRoot, 'Assets', 'PSXRacing', 'Resources');
  const ad = artDir || join(projectRoot, 'Assets', 'PSXRacing', 'Art', st.art);
  const json = JSON.parse(readFileSync(join(rd, pre + '_stage.json'), 'utf8'));
  const meta = JSON.parse(readFileSync(join(ad, pre + '_dem_meta.json'), 'utf8'));
  const n = json.xyz.length / 3;
  const wp = new Array(n), h = new Float64Array(n);
  for (let i = 0; i < n; i++) { wp[i] = { x: json.xyz[3 * i], z: json.xyz[3 * i + 2] }; h[i] = json.xyz[3 * i + 1] + json.baseM; }
  const grid = name => {
    const g = meta[name]; if (!g) return null;
    const b = readFileSync(join(ad, `${pre}_dem_${name}.bytes`));
    return { ...g, data: new Int16Array(b.buffer, b.byteOffset, g.cols * g.rows) };
  };
  return { pre, st, json, meta, wp, h, n, loop: !!json.loop, resDir: rd, artDir: ad, grid };
}
