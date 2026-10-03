// citydata.mjs - read the three Charlotte data files the game ships, the way
// the game reads them (CityMap.Parse, CityElevation.EnsureDem,
// CityMap.ParseFootprints), and describe them.
//
// Shared by the exporter's --check / fingerprint (export_osm.mjs) and by the
// metrics (metrics.mjs), so both describe the BYTES that ship rather than
// the exporter's intermediate arrays. Nothing here reads the Overpass cache.
//
// Layouts (little-endian). PSXC version 2 (WP-02, 2026-09-28) is a section
// table; version 1 (everything up to WP-01) was one sequential record. Both
// are read here, so a v1 file and its v2 re-export can be compared (the
// graph hash below is the same for both).
//
//   PSXC v2  u32 magic 'PSXC' | i32 ver 2 | u32 nsec
//            | nsec x { u8[4] tag; u32 offset (from the file start); u32 length }
//            | the sections, each starting on a 4-byte boundary (zero pad)
//     META  str attribution | f32 uptownX, uptownZ
//     NODE  u32 nodes { f32 x, z; u8 ctl }            ctl 4 signal, 2 stop, 1 give way
//     NAME  u32 names { str }
//     EDGE  u32 edges { u32 a, b, name; u8 rank; u8 flags; u8 lanes; i8 level;
//                       f32 width, shl, shr; u8 speed; u32 wayId; u16 n }
//     PNTS  u32 points; points x f32 x, z   every edge's n points, in edge order
//     WATR  u32 waters { u32 name; f32 width; u8 kind; u32 n; n x f32 x, z }
//           kind 0 creek, 1 lake (a closed ring), 2 ravine (WP-04b: a small
//           stream the ground is carved for, with no water and no span);
//           before WP-04b only 0 and 1
//     WBED  u32 waters (= WATR's) | f32 base | f32 scale                (WP-04b)
//           | per water, in WATR's order: u32 m; f32 step;
//             if m > 0: u16 first; (m - 1) x i16 step to the next
//           Heights are base + units * scale metres ASL (centimetres above
//           the datum): a creek's or ravine's bed every `step` metres along
//           its WATR line from its first point (the last sample at its end),
//           a lake's level (m = 1, step 0). From USGS 3DEP 1/3". Absent
//           before WP-04b: a reader without it carves creeks a fixed 3.6 m.
//     XING  u32 crossings { u32 over, under; f32 x, z; u8 forced }
//     SPAN  u32 wspans { u32 edge; f32 s0, s1 }
//     ROUT  u32 routes { str id, name; u8 loop, oneway; f32 roadWidth; u8 speed;
//                        f32 lengthM, startM, finishM; u32 n; n x (u32 edge; i8 dir) }
//     GHSH  u32 graph hash (graphHash below)
//     (LANW TAPR PARA TAGN SPLT: export_osm.mjs documents them where it writes them)
//     BRST  u32 n | n x { u32 edge; u32 outline }                      (plan B1, 2026-10-02)
//           every bridge=yes edge an OSM man_made=bridge outline holds (>= 60%
//           of its 4 m samples): the way id, or a relation's id | 0x80000000
//           (lib/bridges.mjs). Two edges in one outline are ONE structure.
//           | u32 m | m x { u32 span; u32 way; f32 dist }   the water spans
//           (SPAN's index) whose middle is within 15 m of an OSM culvert line
//           (tunnel=culvert): owner Q8, a pipe and not a bridge (B2 builds it)
//           | u32 k | k x { u32 wayA, wayB; u8 force }       twin-deck
//           overrides by way pair (tools/city/deckpairs_overrides.json): 1 FORCE
//           one structure, 0 NEVER. From B2 on the culvert table is empty: a
//           creek OSM pipes under the road has no span (CULV), and a span left
//           is a bridge.
//     CULV  u32 n | n x { u32 culvertWay; u32 water; f32 ws;              (plan B2, owner Q8)
//                         u32 edge; f32 s; f32 halfAlong; f32 bedASL }
//           the road crossings of a creek OSM pipes under the road
//           (lib/culverts.mjs): no span - the creek line (WATR's index, its arc
//           there) runs on under the road's fill, the game finds the pipe's ends
//           where the fill meets the channel (CityCulverts) and holds the road
//           (the arc, the half length along it) over the pipe's cover (the
//           creek's bed there, metres ASL).
//   A reader takes the sections it knows by tag and skips the rest.
//
//   PSXC v1  u32 magic | i32 ver 1 | META's fields | NODE | NAME
//            | EDGE with each edge's n x f32 x, z inline after its u16 n
//            | WATR | XING | SPAN | ROUT              (no table, no hash)
//
//   PDEM v3  the v2 header, then u32 B, nbx, nbz, a block index and the grid
//            in delta-coded blocks (lib/pdem3.mjs; WP-13, the 30 m grid)
//   PDEM v2  u32 magic | i32 ver 2 | u32 nx, nz | f32 x0, z0, cell, base, scale
//            | nx*nz u16 (u16 * scale = metres above base)
//   PDEM v1  the same without scale (it was 0.1, hard-coded in the reader)
//   PBLD     u32 magic | i32 ver 1 | f32 x0, z0, x1, z1 | u32 n { u8 style|gable<<7; f32 h; u8 n; n x f32 x, z }
// str = C#'s BinaryReader string: 7-bit-encoded byte length, then UTF-8.
//
// Edge flags: 1 link, 2 oneway, 4 bridge, 8 tunnel, 16 centre turn lane, 32 roundabout.

import { crc32 } from 'node:zlib';
import { decodePdem3 } from './pdem3.mjs';

export const LANE_M = 3.6576;
export const STATION_STEP = 10;          // CityElevation.StationStep
export const DEG = 180 / Math.PI;

class Reader {
  constructor(buf) { this.b = buf; this.p = 0; }
  u8() { const v = this.b.readUInt8(this.p); this.p += 1; return v; }
  i8() { const v = this.b.readInt8(this.p); this.p += 1; return v; }
  u16() { const v = this.b.readUInt16LE(this.p); this.p += 2; return v; }
  i16() { const v = this.b.readInt16LE(this.p); this.p += 2; return v; }
  i32() { const v = this.b.readInt32LE(this.p); this.p += 4; return v; }
  u32() { const v = this.b.readUInt32LE(this.p); this.p += 4; return v; }
  f32() { const v = this.b.readFloatLE(this.p); this.p += 4; return v; }
  str() {
    let n = 0, shift = 0, byte;
    do { byte = this.u8(); n |= (byte & 0x7f) << shift; shift += 7; } while (byte & 0x80);
    const s = this.b.toString('utf8', this.p, this.p + n); this.p += n; return s;
  }
}

/// The section tags of PSXC v2, in file order (a reader skips any other).
export const CITY_SECTIONS = ['META', 'NODE', 'NAME', 'EDGE', 'PNTS', 'WATR', 'WBED', 'XING', 'SPAN', 'ROUT', 'GHSH', 'LANW', 'TAPR', 'PARA', 'TAGN', 'SPLT', 'BRST', 'CULV'];
/// Sections a file may lack: added after the version-2 layout first shipped,
/// so a file exported before them still parses (WBED: WP-04b; LANW, TAPR,
/// PARA and TAGN: WP-10, lib/lineclean.mjs; SPLT: WP-11, lib/splits.mjs;
/// BRST: the roads pass's B1, lib/bridges.mjs; CULV: B2, lib/culverts.mjs).
export const CITY_OPTIONAL = new Set(['WBED', 'LANW', 'TAPR', 'PARA', 'TAGN', 'SPLT', 'BRST', 'CULV']);

/// THE ROADS PASS'S TIERS (plan P0/B1), by the shipped rank (0 local ..
/// 5 motorway; a link keeps its base): 1 motorway/trunk/primary and links,
/// 2 secondary/tertiary and links, 3 local, service, parking. The mirror of
/// Scripts/City/CityTier.cs OfClass - the two must agree.
export const tierOf = rank => rank >= 3 ? 1 : rank >= 1 ? 2 : 3;

/// THE GRAPH HASH (WP-02): what derived data keyed by (edge, s) is stamped
/// with, so data made for one graph is refused by another. CRC-32 (zlib's,
/// the IEEE polynomial) over the little-endian stream
///   u32 edge count | per edge: u32 a, u32 b, u32 length in cm
/// where the length is the edge's polyline length summed in double precision
/// from its stored float32 points, sqrt(dx*dx + dz*dz) per segment (never
/// Math.hypot, which rounds differently), rounded half up to a centimetre.
/// CityMap.GraphHashOf computes the same number in C#. It does not depend on
/// the file layout: a v1 file and its v2 re-export hash the same.
export function graphHash(edges) {
  const b = Buffer.alloc(4 + edges.length * 12);
  b.writeUInt32LE(edges.length, 0);
  let o = 4;
  for (const e of edges) {
    let len = 0;
    const P = e.pts;
    for (let k = 1; k < P.length; k++) {
      const dx = P[k][0] - P[k - 1][0], dz = P[k][1] - P[k - 1][1];
      len += Math.sqrt(dx * dx + dz * dz);
    }
    b.writeUInt32LE(e.a >>> 0, o); b.writeUInt32LE(e.b >>> 0, o + 4);
    b.writeUInt32LE(Math.floor(len * 100 + 0.5) >>> 0, o + 8);
    o += 12;
  }
  return crc32(b) >>> 0;
}
export const hashHex = h => (h >>> 0).toString(16).padStart(8, '0');

/// Parse charlotte_city.bytes (v1 or v2). Also records each section's byte
/// range, for the SIZE section of the metrics (v1 has no table: its ranges
/// are where each part of the one record starts and ends).
export function parseCity(buf) {
  const r = new Reader(buf);
  const sections = {};
  if (r.u32() !== 0x43585350) throw new Error('charlotte_city.bytes: bad magic');
  const version = r.i32();
  if (version !== 1 && version !== 2) throw new Error('charlotte_city.bytes: version ' + version + ' (this reader knows 1 and 2)');
  let table = null;
  if (version === 2) {
    const nsec = r.u32();
    table = new Map();
    for (let i = 0; i < nsec; i++) {
      const tag = buf.toString('latin1', r.p, r.p + 4); r.p += 4;
      const offset = r.u32(), length = r.u32();
      if (offset + length > buf.length) throw new Error(`charlotte_city.bytes: section ${tag} runs past the end`);
      if (table.has(tag)) throw new Error(`charlotte_city.bytes: section ${tag} twice`);
      table.set(tag, { offset, length });
    }
    sections.HEAD = [0, r.p];
    for (const [tag, s] of table) sections[tag] = [s.offset, s.offset + s.length];
    for (const tag of CITY_SECTIONS) if (!table.has(tag) && !CITY_OPTIONAL.has(tag)) throw new Error(`charlotte_city.bytes: no ${tag} section`);
  }
  // v2: jump to a section, and check afterwards that it was read exactly
  const open = tag => { if (table) r.p = table.get(tag).offset; return r.p; };
  let mark = 0;
  const close = tag => {
    if (table) {
      const s = table.get(tag);
      if (r.p !== s.offset + s.length) throw new Error(`charlotte_city.bytes: section ${tag} read ${r.p - s.offset} of ${s.length} bytes`);
    } else { sections[tag === 'META' ? 'HEAD' : tag] = [mark, r.p]; mark = r.p; }   // v1: magic..uptown was 'HEAD'
  };

  open('META');
  const attribution = r.str();
  const uptown = [r.f32(), r.f32()];
  close('META');
  open('NODE');
  const nn = r.u32();
  const nodes = new Array(nn);
  for (let i = 0; i < nn; i++) nodes[i] = { x: r.f32(), z: r.f32(), ctl: r.u8() };
  close('NODE');
  open('NAME');
  const ns = r.u32();
  const names = new Array(ns);
  for (let i = 0; i < ns; i++) names[i] = r.str();
  close('NAME');
  open('EDGE');
  const ne = r.u32();
  const edges = new Array(ne);
  const counts = new Array(ne);
  let pointBytes = 0;
  const readPts = np => { const p0 = r.p; const pts = new Array(np); for (let k = 0; k < np; k++) pts[k] = [r.f32(), r.f32()]; pointBytes += r.p - p0; return pts; };
  for (let i = 0; i < ne; i++) {
    const e = { index: i, a: r.u32(), b: r.u32(), name: names[r.u32()] };
    e.rank = r.u8();
    const f = r.u8();
    e.flags = f;
    e.link = (f & 1) !== 0; e.oneway = (f & 2) !== 0; e.bridge = (f & 4) !== 0;
    e.tunnel = (f & 8) !== 0; e.turn = (f & 16) !== 0; e.roundabout = (f & 32) !== 0;
    e.lanes = Math.max(1, r.u8()); e.level = r.i8();
    e.exportWidth = r.f32(); e.shlExport = r.f32(); e.shrExport = r.f32();
    e.speed = r.u8(); e.wayId = r.u32();
    counts[i] = r.u16();
    if (version === 1) e.pts = readPts(counts[i]);
    edges[i] = e;
  }
  close('EDGE');
  if (version === 2) {
    open('PNTS');
    const total = r.u32();
    let sum = 0; for (const n of counts) sum += n;
    if (total !== sum) throw new Error(`charlotte_city.bytes: PNTS holds ${total} points, the edges ${sum}`);
    for (const e of edges) e.pts = readPts(counts[e.index]);
    close('PNTS');
  }
  for (const e of edges) {
    const pts = e.pts, np = pts.length;
    const s = new Float64Array(np);
    for (let k = 1; k < np; k++) s[k] = s[k - 1] + Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]);
    e.s = s; e.length = np ? s[np - 1] : 0;
    const prof = profileFor(e.rank, e.link, e.oneway, e.lanes, e.turn);
    e.profile = prof; e.width = prof.width; e.hw = prof.width / 2;
  }
  open('WATR');
  const nw = r.u32();
  const waters = new Array(nw);
  for (let i = 0; i < nw; i++) {
    const w = { name: names[r.u32()], width: r.f32(), kind: r.u8() };
    w.lake = w.kind === 1; w.ravine = w.kind === 2;
    const np = r.u32(); w.pts = new Array(np);
    for (let k = 0; k < np; k++) w.pts[k] = [r.f32(), r.f32()];
    waters[i] = w;
  }
  close('WATR');
  let bedSamples = 0;
  if (table && table.has('WBED')) {
    open('WBED');
    const nb = r.u32();
    if (nb !== nw) throw new Error(`charlotte_city.bytes: WBED has ${nb} beds for ${nw} waters`);
    const base = r.f32(), scale = r.f32();
    for (let i = 0; i < nw; i++) {
      const m = r.u32(), step = r.f32(), bed = new Float64Array(m);
      let u = 0;
      for (let k = 0; k < m; k++) { u = k ? u + r.i16() : r.u16(); bed[k] = base + u * scale; }
      waters[i].bed = bed; waters[i].bedStep = step; bedSamples += m;
    }
    close('WBED');
  }
  open('XING');
  const nc = r.u32();
  const crossings = new Array(nc);
  for (let i = 0; i < nc; i++) crossings[i] = { over: r.u32(), under: r.u32(), x: r.f32(), z: r.f32(), forced: r.u8() !== 0 };
  close('XING');
  open('SPAN');
  const nws = r.u32();
  const wspans = new Array(nws);
  for (let i = 0; i < nws; i++) wspans[i] = { edge: r.u32(), s0: r.f32(), s1: r.f32() };
  close('SPAN');
  open('ROUT');
  const nr = r.u32();
  const routes = new Array(nr);
  for (let i = 0; i < nr; i++) {
    const rt = { id: r.str(), name: r.str(), loop: r.u8() !== 0, oneway: r.u8() !== 0, roadWidth: r.f32(), speed: r.u8(),
                 lengthM: r.f32(), startM: r.f32(), finishM: r.f32() };
    const n = r.u32(); rt.edges = new Array(n); rt.dirs = new Array(n);
    for (let k = 0; k < n; k++) { rt.edges[k] = r.u32(); rt.dirs[k] = r.i8(); }
    routes[i] = rt;
  }
  close('ROUT');
  const hash = graphHash(edges);
  let storedHash = null;
  if (version === 2) {
    open('GHSH');
    storedHash = r.u32();
    close('GHSH');
    if (storedHash !== hash) throw new Error(`charlotte_city.bytes: GHSH says ${hashHex(storedHash)}, the graph hashes to ${hashHex(hash)}`);
    // nothing may hide between the sections but alignment padding
    const spans = [...table.values()].sort((p, q) => p.offset - q.offset);
    let at = sections.HEAD[1];
    for (const s of spans) {
      if (s.offset < at || s.offset - at > 3) throw new Error('charlotte_city.bytes: sections overlap or leave a gap');
      at = s.offset + s.length;
    }
    if (buf.length - at > 3) throw new Error(`charlotte_city.bytes: ${buf.length - at} trailing bytes`);
  } else if (r.p !== buf.length) throw new Error(`charlotte_city.bytes: ${buf.length - r.p} trailing bytes`);
  // TAPR's per-edge ribbon offsets (LineModel.Init's taprOff: the mean of
  // o0 and o1) and BRST (plan B1), when the file has them
  let taprOff = null, brst = null, tapr = null;
  if (table && table.has('TAPR')) {
    r.p = table.get('TAPR').offset;
    const nt = r.u32();
    tapr = new Array(nt);
    for (let i = 0; i < nt; i++) tapr[i] = { edge: r.u32(), end: r.u8(), side: r.u8(), src: r.u8(), flags: r.u8(), dw: r.f32(), len: r.f32(), room: r.f32(), off: r.f32() };
    const no = r.u32();
    taprOff = new Float64Array(ne);
    for (let i = 0; i < no; i++) { const ei = r.u32(), o0 = r.f32(), o1 = r.f32(); r.f32(); r.f32(); if (ei < ne) taprOff[ei] = 0.5 * (o0 + o1); }
  }
  if (table && table.has('BRST')) {
    open('BRST');
    const structId = new Uint32Array(ne);
    const n = r.u32();
    for (let i = 0; i < n; i++) { const ei = r.u32(), id = r.u32(); if (ei < ne) structId[ei] = id; }
    const m = r.u32(), culvert = new Array(m);
    for (let i = 0; i < m; i++) culvert[i] = { span: r.u32(), way: r.u32(), d: r.f32() };
    const k = r.u32(), overrides = new Array(k);
    for (let i = 0; i < k; i++) overrides[i] = { wayA: r.u32(), wayB: r.u32(), force: r.u8() !== 0 };
    close('BRST');
    brst = { structId, culvert, overrides };
  }
  let culv = null;
  if (table && table.has('CULV')) {
    open('CULV');
    const n = r.u32();
    culv = new Array(n);
    for (let i = 0; i < n; i++) culv[i] = { way: r.u32(), water: r.u32(), ws: r.f32(), edge: r.u32(), s: r.f32(), half: r.f32(), bed: r.f32() };
    close('CULV');
  }
  // node -> incident edge ends
  const nodeEdges = Array.from({ length: nn }, () => []);
  for (const e of edges) { nodeEdges[e.a].push(e.index); nodeEdges[e.b].push(e.index); }
  return { version, attribution, uptown, nodes, names, edges, waters, crossings, wspans, routes, nodeEdges, sections, pointBytes,
           graphHash: hash, storedHash, bedSamples, taprOff, tapr, brst, culv };
}

export function parseDem(buf) {
  if (buf.length >= 8 && buf.readUInt32LE(0) === 0x4D454450 && buf.readInt32LE(4) === 3) return demFromGrid(decodePdem3(buf));
  const r = new Reader(buf);
  if (r.u32() !== 0x4D454450) throw new Error('charlotte_dem.bytes: bad magic');
  const version = r.i32();
  if (version !== 1 && version !== 2) throw new Error('charlotte_dem.bytes: version ' + version);
  const nx = r.u32(), nz = r.u32();
  const x0 = r.f32(), z0 = r.f32(), cell = r.f32(), base = r.f32();
  // v1 hard-coded decimetres; v2 says so in the header. The header's f32 0.1
  // is taken as the double 0.1 the v1 reader used, so a v1 file and its v2
  // re-export give the metrics identical heights.
  const scale = version >= 2 ? r.f32() : 0.1;
  const mul = scale === Math.fround(0.1) ? 0.1 : scale;
  const h = new Float32Array(nx * nz);
  for (let i = 0; i < h.length; i++) h[i] = r.u16() * mul;     // metres above base
  if (r.p !== buf.length) throw new Error('charlotte_dem.bytes: trailing bytes');
  /// CityElevation.BaseY: bilinear, metres above the datum (add base for ASL).
  const at = (x, z) => {
    const fx = (x - x0) / cell, fz = (z - z0) / cell;
    const ix = Math.min(nx - 2, Math.max(0, Math.floor(fx))), iz = Math.min(nz - 2, Math.max(0, Math.floor(fz)));
    const tx = Math.min(1, Math.max(0, fx - ix)), tz = Math.min(1, Math.max(0, fz - iz));
    const a = h[iz * nx + ix], b = h[iz * nx + ix + 1], c = h[(iz + 1) * nx + ix], d = h[(iz + 1) * nx + ix + 1];
    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
  };
  return { version, nx, nz, x0, z0, cell, base, scale, h, at, asl: (x, z) => at(x, z) + base };
}

/// A decoded PDEM v3 grid as parseDem returns every version: heights in
/// metres above the base (the f32 scale taken as the double the v1/v2 reader
/// uses for 0.1), and CityElevation.BaseY's bilinear read.
function demFromGrid(g) {
  const { nx, nz, x0, z0, cell, base, scale, q } = g;
  const mul = scale === Math.fround(0.1) ? 0.1 : scale;
  const h = new Float32Array(nx * nz);
  for (let i = 0; i < h.length; i++) h[i] = q[i] * mul;
  const at = (x, z) => {
    const fx = (x - x0) / cell, fz = (z - z0) / cell;
    const ix = Math.min(nx - 2, Math.max(0, Math.floor(fx))), iz = Math.min(nz - 2, Math.max(0, Math.floor(fz)));
    const tx = Math.min(1, Math.max(0, fx - ix)), tz = Math.min(1, Math.max(0, fz - iz));
    const a = h[iz * nx + ix], b = h[iz * nx + ix + 1], c = h[(iz + 1) * nx + ix], d = h[(iz + 1) * nx + ix + 1];
    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
  };
  return { version: g.version, nx, nz, x0, z0, cell, base, scale, block: g.B, h, q, at, asl: (x, z) => at(x, z) + base };
}

/// The 60 m grid the ROADS read (CityElevation.BuildRoadDem, WP-13): the 30 m
/// nodes weighted [1/4, 1/2, 1/4] each way onto every second node, in the
/// stored steps, edges divided by the weight they got; float32 like the game.
/// A grid already at 60 m comes back as its steps.
export function roadGridSteps(dem, roadCell = 60) {
  const q = dem.q || Uint16Array.from(dem.h, v => Math.round(v / (dem.scale === Math.fround(0.1) ? 0.1 : dem.scale)));
  const f = Math.max(1, Math.round(roadCell / dem.cell)) === 2 ? 2 : 1;
  const nx = Math.floor((dem.nx - 1) / f) + 1, nz = Math.floor((dem.nz - 1) / f) + 1;
  const out = new Float32Array(nx * nz);
  if (f === 1) { for (let i = 0; i < out.length; i++) out[i] = q[i]; return { nx, nz, cell: dem.cell * f, steps: out }; }
  const F = Math.fround;
  for (let iz = 0; iz < dem.nz; iz++) {
    const z0 = iz >> 1, zOdd = iz & 1;
    for (let ix = 0; ix < dem.nx; ix++) {
      const v = q[iz * dem.nx + ix], x0 = ix >> 1, xOdd = ix & 1;
      const add = (zz, xx, w) => { if (zz < nz && xx < nx) out[zz * nx + xx] = F(out[zz * nx + xx] + F(w * v)); };
      if (!zOdd) { if (!xOdd) add(z0, x0, 0.25); else { add(z0, x0, 0.125); add(z0, x0 + 1, 0.125); } }
      else for (let zz = z0; zz <= z0 + 1; zz++) { if (!xOdd) add(zz, x0, 0.125); else { add(zz, x0, 0.0625); add(zz, x0 + 1, 0.0625); } }
    }
  }
  for (let z = 0; z < nz; z++) for (let x = 0; x < nx; x++)
    out[z * nx + x] = F(out[z * nx + x] / F((z === 0 || z === nz - 1 ? 0.75 : 1) * (x === 0 || x === nx - 1 ? 0.75 : 1)));
  return { nx, nz, cell: dem.cell * f, steps: out };
}

export function parseBld(buf) {
  const r = new Reader(buf);
  if (r.u32() !== 0x444C4250) throw new Error('charlotte_bld.bytes: bad magic');
  const version = r.i32();
  if (version !== 1) throw new Error('charlotte_bld.bytes: version ' + version);
  const bbox = [r.f32(), r.f32(), r.f32(), r.f32()];
  const n = r.u32();
  const fp = new Array(n);
  let points = 0;
  for (let i = 0; i < n; i++) {
    const sb = r.u8(); const f = { style: sb & 0x7f, gable: (sb & 0x80) !== 0, h: r.f32() };
    const np = r.u8(); f.pts = new Array(np); points += np;
    for (let k = 0; k < np; k++) f.pts[k] = [r.f32(), r.f32()];
    fp[i] = f;
  }
  if (r.p !== buf.length) throw new Error('charlotte_bld.bytes: trailing bytes');
  return { version, bbox, footprints: fp, points };
}

// ------------------------------------------------------------ road profile
/// RoadProfiles.IndexFor + Width (Scripts/City/RoadProfiles.cs): the painted
/// width the game draws, which replaces the exporter's own width at load
/// (CityMap.cs, "superseded by the profile").
export function profileFor(cls, link, oneway, lanes, turn) {
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const row = (key, n, shl, shr) => ({ key, lanes: n, shl, shr, width: n * LANE_M + shl + shr });
  if (!oneway) {
    let n = clamp(lanes, 2, 6);
    if (turn && n % 2 === 0 && n < 6) n++;
    return row('tw' + n + (n % 2 ? 't' : ''), n, 0.3, 0.3);
  }
  if (link) {
    if (cls >= 4) return row('ramp' + clamp(lanes, 1, 3), clamp(lanes, 1, 3), 0.6, 1.8);
    return row('ow' + clamp(lanes, 1, 4), clamp(lanes, 1, 4), 0.3, 0.3);
  }
  // a ONE-lane freeway carriageway (an express lane, a lane-drop stub) is a
  // ramp's section, not a two-lane motorway's (WP-10)
  if (cls >= 4 && lanes <= 1) return row('ramp1', 1, 0.6, 1.8);
  if (cls >= 5) return row('mw' + clamp(lanes, 2, 6), clamp(lanes, 2, 6), 1.2, 3.0);
  if (cls === 4) return row('xw' + clamp(lanes, 2, 4), clamp(lanes, 2, 4), 0.9, 2.4);
  return row('ow' + clamp(lanes, 1, 4), clamp(lanes, 1, 4), 0.3, 0.3);
}

const RANK_NAME = ['local', 'tertiary', 'secondary', 'primary', 'trunk', 'motorway'];
/// The class key metrics are reported by: the OSM highway class, with every
/// street below tertiary (residential, unclassified, living, named service)
/// as "local" because the shipped rank does not tell them apart.
export const classOf = e => RANK_NAME[e.rank] + (e.link ? '_link' : '');
export const CLASSES = ['motorway', 'motorway_link', 'trunk', 'trunk_link', 'primary', 'primary_link',
  'secondary', 'secondary_link', 'tertiary', 'tertiary_link', 'local', 'local_link'];

// ------------------------------------------------------------------ kinks
/// Signed turn at b, radians (+ = left).
export function signedTurn(a, b, c) {
  const ux = b[0] - a[0], uz = b[1] - a[1], vx = c[0] - b[0], vz = c[1] - b[1];
  return Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz);
}

/// Every FREE vertex of the graph - a point where the road bends and no
/// third road joins: each edge's interior polyline points, and every node
/// where exactly two edge ends meet (the exporter cuts an edge there; the
/// mesh mitres it, or fans it past 25 degrees). Roundabouts are left out:
/// a roundabout is meant to turn. Each vertex carries the turn angle, the
/// half width painted there, the class, the two legs, and the inner-edge
/// FOLD (survey_kinks_lanes.md, Method): the inside edge of a ribbon at a
/// vertex that turns th folds back when the nearest neighbouring
/// cross-section (a 10 m elevation station, or the next vertex) is closer
/// than hw*sin(th/2), with a same-direction neighbour's own term added.
export function freeVertices(city) {
  const out = [];
  for (const e of city.edges) {
    if (e.roundabout) continue;
    const P = e.pts, n = P.length;
    if (n < 3) continue;
    const th = new Float64Array(n);
    for (let k = 1; k < n - 1; k++) th[k] = signedTurn(P[k - 1], P[k], P[k + 1]);
    // the cross-sections the mesh draws: every station and every vertex
    const nst = Math.max(2, Math.ceil(e.length / STATION_STEP) + 1);
    const st = new Float64Array(nst);
    for (let q = 0; q < nst; q++) st[q] = q === nst - 1 ? e.length : q * e.length / (nst - 1);
    for (let k = 1; k < n - 1; k++) {
      const t = th[k], at = Math.abs(t), sk = e.s[k];
      // nearest station either side (not coincident with this vertex)
      let sb = -Infinity, sa = Infinity;
      for (let q = 0; q < nst; q++) {
        if (st[q] < sk - 1e-3) sb = st[q];
        else if (st[q] > sk + 1e-3) { sa = st[q]; break; }
      }
      let fold = 0;
      const nb = [];
      nb.push({ d: sk - sb, a: 0 });
      nb.push({ d: sa - sk, a: 0 });
      // neighbouring vertices carry their own inner-edge term
      nb.push({ d: sk - e.s[k - 1], a: k - 1 > 0 ? (Math.sign(th[k - 1]) === Math.sign(t) ? 1 : -1) * e.hw * Math.sin(Math.abs(th[k - 1]) / 2) : 0 });
      nb.push({ d: e.s[k + 1] - sk, a: k + 1 < n - 1 ? (Math.sign(th[k + 1]) === Math.sign(t) ? 1 : -1) * e.hw * Math.sin(Math.abs(th[k + 1]) / 2) : 0 });
      // only the NEAREST neighbour each side counts (a station or a vertex)
      const before = nb[0].d <= nb[2].d ? nb[0] : nb[2];
      const after = nb[1].d <= nb[3].d ? nb[1] : nb[3];
      for (const q of [before, after]) if (Number.isFinite(q.d)) fold = Math.max(fold, e.hw * Math.sin(at / 2) + q.a - q.d);
      out.push({ kind: 'shape', cls: classOf(e), edge: e.index, x: P[k][0], z: P[k][1], th: t, deg: at * DEG,
                 legIn: e.s[k] - e.s[k - 1], legOut: e.s[k + 1] - e.s[k], hw: e.hw, fold });
    }
  }
  // 2-arm joins between two different edges (or one edge's two ends: a loop)
  for (let ni = 0; ni < city.nodes.length; ni++) {
    const inc = city.nodeEdges[ni];
    if (inc.length !== 2) continue;
    const ends = [];
    for (let j = 0; j < 2; j++) {
      const e = city.edges[inc[j]];
      // which end of e is at this node (a loop edge has both: take one each)
      const atA = e.a === ni && !(j === 1 && inc[0] === inc[1] && e.b === ni);
      const P = e.pts, n = P.length;
      const node = atA ? P[0] : P[n - 1], inner = atA ? P[1] : P[n - 2];
      ends.push({ e, node, inner, leg: atA ? e.s[1] : e.length - e.s[n - 2] });
    }
    if (ends[0].e.roundabout || ends[1].e.roundabout) continue;
    const t = signedTurn(ends[0].inner, ends[0].node, ends[1].inner);
    const hi = ends[0].e.rank >= ends[1].e.rank ? ends[0].e : ends[1].e;
    const hw = Math.max(ends[0].e.hw, ends[1].e.hw);
    const at = Math.abs(t);
    out.push({ kind: 'join', cls: classOf(hi), edge: hi.index, node: ni, x: ends[0].node[0], z: ends[0].node[1], th: t, deg: at * DEG,
               legIn: ends[0].leg, legOut: ends[1].leg, hw, fold: hw * Math.sin(at / 2) - Math.min(ends[0].leg, ends[1].leg) });
  }
  return out;
}

/// Length of the graph in km by class (roundabouts included).
export function kmByClass(city) {
  const km = {};
  for (const c of CLASSES) km[c] = 0;
  for (const e of city.edges) km[classOf(e)] += e.length / 1000;
  return km;
}

// ------------------------------------------------------------ fingerprint
const r1 = v => Math.round(v * 10) / 10;
const r3 = v => Math.round(v * 1000) / 1000;

/// The data's identity in numbers: what a later export must reproduce (or
/// explain) - counts that do not depend on the Node version's floating
/// point, plus the kinks per km that the lines phase exists to lower.
export function fingerprint(city, dem, bld) {
  let points = 0, km = 0, kmFree = 0;
  for (const e of city.edges) { points += e.pts.length; km += e.length / 1000; if (!e.roundabout) kmFree += e.length / 1000; }
  const ctl = { signal: 0, stop: 0, give_way: 0 };
  for (const n of city.nodes) { if (n.ctl === 4) ctl.signal++; else if (n.ctl === 2) ctl.stop++; else if (n.ctl === 1) ctl.give_way++; }
  const deg = new Array(8).fill(0);
  for (const l of city.nodeEdges) deg[Math.min(7, l.length)]++;
  const fv = freeVertices(city);
  let ge5 = 0, ge10 = 0, ge25 = 0, maxDeg = 0, folds = 0;
  for (const v of fv) {
    if (v.deg >= 5) ge5++; if (v.deg >= 10) ge10++; if (v.deg >= 25) ge25++;
    if (v.deg > maxDeg) maxDeg = v.deg;
    if (v.fold > 0.1) folds++;
  }
  let dmin = Infinity, dmax = -Infinity;
  for (const v of dem.h) { if (v < dmin) dmin = v; if (v > dmax) dmax = v; }
  let waterPts = 0; for (const w of city.waters) waterPts += w.pts.length;
  return {
    city: {
      version: city.version,
      graph_hash: hashHex(city.graphHash),
      nodes: city.nodes.length,
      edges: city.edges.length,
      points,
      node_degrees: deg,
      km: r1(km),
      bridges: city.edges.filter(e => e.bridge).length,
      tunnels: city.edges.filter(e => e.tunnel).length,
      links: city.edges.filter(e => e.link).length,
      oneway: city.edges.filter(e => e.oneway).length,
      roundabout_edges: city.edges.filter(e => e.roundabout).length,
      names: city.names.length,
      waters: city.waters.length,
      water_points: waterPts,
      ...(city.bedSamples ? { water_bed_samples: city.bedSamples, lakes: city.waters.filter(w => w.lake).length, ravines: city.waters.filter(w => w.ravine).length } : {}),
      ...(city.brst ? { bridge_outline_edges: city.brst.structId.filter(v => v).length, bridge_outlines: new Set(city.brst.structId.filter(v => v)).size,
                        culvert_water_spans: city.brst.culvert.length, deck_overrides: city.brst.overrides.length } : {}),
      ...(city.culv ? { culvert_crossings: city.culv.length } : {}),
      crossings: city.crossings.length,
      crossings_forced: city.crossings.filter(c => c.forced).length,
      water_spans: city.wspans.length,
      controls: ctl,
      routes: city.routes.map(r => ({ id: r.id, edges: r.edges.length, length_m: r1(r.lengthM) })),
      free_vertices: fv.length,
      kinks_per_km: { ge5: r3(ge5 / kmFree), ge10: r3(ge10 / kmFree), ge25: r3(ge25 / kmFree) },
      kinks: { ge5, ge10, ge25, max_deg: r1(maxDeg), folds_over_10cm: folds },
    },
    dem: { version: dem.version, nx: dem.nx, nz: dem.nz, cell: dem.cell, x0: r1(dem.x0), z0: r1(dem.z0), datum_m: dem.base,
           scale_m: Math.round(dem.scale * 1e6) / 1e6,
           min_asl: r1(dmin + dem.base), max_asl: r1(dmax + dem.base) },
    bld: { version: bld.version, footprints: bld.footprints.length, points: bld.points,
           bbox: bld.bbox.map(v => Math.round(v)) },
  };
}
