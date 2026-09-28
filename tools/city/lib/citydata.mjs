// citydata.mjs - read the three Charlotte data files the game ships, the way
// the game reads them (CityMap.Parse, CityElevation.EnsureDem,
// CityMap.ParseFootprints), and describe them.
//
// Shared by the exporter's --check / fingerprint (export_osm.mjs) and by the
// metrics (metrics.mjs), so both describe the BYTES that ship rather than
// the exporter's intermediate arrays. Nothing here reads the Overpass cache.
//
// Layouts (version 1, little-endian):
//   PSXC  u32 magic 'PSXC' | i32 ver | str attribution | f32 uptownX, uptownZ
//         | u32 nodes { f32 x, z; u8 ctl }            ctl 4 signal, 2 stop, 1 give way
//         | u32 names { str }
//         | u32 edges { u32 a, b, name; u8 rank; u8 flags; u8 lanes; i8 level;
//                       f32 width, shl, shr; u8 speed; u32 wayId; u16 n; n x f32 x, z }
//         | u32 waters { u32 name; f32 width; u8 lake; u32 n; n x f32 x, z }
//         | u32 crossings { u32 over, under; f32 x, z; u8 forced }
//         | u32 wspans { u32 edge; f32 s0, s1 }
//         | u32 routes { str id, name; u8 loop, oneway; f32 roadWidth; u8 speed;
//                        f32 lengthM, startM, finishM; u32 n; n x (u32 edge; i8 dir) }
//   PDEM  u32 magic | i32 ver | u32 nx, nz | f32 x0, z0, cell, base | nx*nz u16 dm above base
//   PBLD  u32 magic | i32 ver | f32 x0, z0, x1, z1 | u32 n { u8 style|gable<<7; f32 h; u8 n; n x f32 x, z }
// str = C#'s BinaryReader string: 7-bit-encoded byte length, then UTF-8.
//
// Edge flags: 1 link, 2 oneway, 4 bridge, 8 tunnel, 16 centre turn lane, 32 roundabout.

export const LANE_M = 3.6576;
export const STATION_STEP = 10;          // CityElevation.StationStep
export const DEG = 180 / Math.PI;

class Reader {
  constructor(buf) { this.b = buf; this.p = 0; }
  u8() { const v = this.b.readUInt8(this.p); this.p += 1; return v; }
  i8() { const v = this.b.readInt8(this.p); this.p += 1; return v; }
  u16() { const v = this.b.readUInt16LE(this.p); this.p += 2; return v; }
  i32() { const v = this.b.readInt32LE(this.p); this.p += 4; return v; }
  u32() { const v = this.b.readUInt32LE(this.p); this.p += 4; return v; }
  f32() { const v = this.b.readFloatLE(this.p); this.p += 4; return v; }
  str() {
    let n = 0, shift = 0, byte;
    do { byte = this.u8(); n |= (byte & 0x7f) << shift; shift += 7; } while (byte & 0x80);
    const s = this.b.toString('utf8', this.p, this.p + n); this.p += n; return s;
  }
}

/// Parse charlotte_city.bytes. Also records each section's byte range, for
/// the SIZE section of the metrics.
export function parseCity(buf) {
  const r = new Reader(buf);
  const sections = {};
  let mark = 0;
  const cut = name => { sections[name] = [mark, r.p]; mark = r.p; };
  if (r.u32() !== 0x43585350) throw new Error('charlotte_city.bytes: bad magic');
  const version = r.i32();
  if (version !== 1) throw new Error('charlotte_city.bytes: version ' + version + ' (this reader knows 1)');
  const attribution = r.str();
  const uptown = [r.f32(), r.f32()];
  cut('HEAD');
  const nn = r.u32();
  const nodes = new Array(nn);
  for (let i = 0; i < nn; i++) nodes[i] = { x: r.f32(), z: r.f32(), ctl: r.u8() };
  cut('NODE');
  const ns = r.u32();
  const names = new Array(ns);
  for (let i = 0; i < ns; i++) names[i] = r.str();
  cut('NAME');
  const ne = r.u32();
  const edges = new Array(ne);
  let pointBytes = 0;
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
    const np = r.u16();
    const p0 = r.p;
    const pts = new Array(np);
    for (let k = 0; k < np; k++) pts[k] = [r.f32(), r.f32()];
    pointBytes += r.p - p0;
    e.pts = pts;
    const s = new Float64Array(np);
    for (let k = 1; k < np; k++) s[k] = s[k - 1] + Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]);
    e.s = s; e.length = np ? s[np - 1] : 0;
    const prof = profileFor(e.rank, e.link, e.oneway, e.lanes, e.turn);
    e.profile = prof; e.width = prof.width; e.hw = prof.width / 2;
    edges[i] = e;
  }
  cut('EDGE');
  const nw = r.u32();
  const waters = new Array(nw);
  for (let i = 0; i < nw; i++) {
    const w = { name: names[r.u32()], width: r.f32(), lake: r.u8() !== 0 };
    const np = r.u32(); w.pts = new Array(np);
    for (let k = 0; k < np; k++) w.pts[k] = [r.f32(), r.f32()];
    waters[i] = w;
  }
  cut('WATR');
  const nc = r.u32();
  const crossings = new Array(nc);
  for (let i = 0; i < nc; i++) crossings[i] = { over: r.u32(), under: r.u32(), x: r.f32(), z: r.f32(), forced: r.u8() !== 0 };
  cut('XING');
  const nws = r.u32();
  const wspans = new Array(nws);
  for (let i = 0; i < nws; i++) wspans[i] = { edge: r.u32(), s0: r.f32(), s1: r.f32() };
  cut('SPAN');
  const nr = r.u32();
  const routes = new Array(nr);
  for (let i = 0; i < nr; i++) {
    const rt = { id: r.str(), name: r.str(), loop: r.u8() !== 0, oneway: r.u8() !== 0, roadWidth: r.f32(), speed: r.u8(),
                 lengthM: r.f32(), startM: r.f32(), finishM: r.f32() };
    const n = r.u32(); rt.edges = new Array(n); rt.dirs = new Array(n);
    for (let k = 0; k < n; k++) { rt.edges[k] = r.u32(); rt.dirs[k] = r.i8(); }
    routes[i] = rt;
  }
  cut('ROUT');
  if (r.p !== buf.length) throw new Error(`charlotte_city.bytes: ${buf.length - r.p} trailing bytes`);
  // node -> incident edge ends
  const nodeEdges = Array.from({ length: nn }, () => []);
  for (const e of edges) { nodeEdges[e.a].push(e.index); nodeEdges[e.b].push(e.index); }
  return { version, attribution, uptown, nodes, names, edges, waters, crossings, wspans, routes, nodeEdges, sections, pointBytes };
}

export function parseDem(buf) {
  const r = new Reader(buf);
  if (r.u32() !== 0x4D454450) throw new Error('charlotte_dem.bytes: bad magic');
  const version = r.i32();
  if (version !== 1) throw new Error('charlotte_dem.bytes: version ' + version);
  const nx = r.u32(), nz = r.u32();
  const x0 = r.f32(), z0 = r.f32(), cell = r.f32(), base = r.f32();
  const h = new Float32Array(nx * nz);
  for (let i = 0; i < h.length; i++) h[i] = r.u16() * 0.1;     // metres above base
  if (r.p !== buf.length) throw new Error('charlotte_dem.bytes: trailing bytes');
  /// CityElevation.BaseY: bilinear, metres above the datum (add base for ASL).
  const at = (x, z) => {
    const fx = (x - x0) / cell, fz = (z - z0) / cell;
    const ix = Math.min(nx - 2, Math.max(0, Math.floor(fx))), iz = Math.min(nz - 2, Math.max(0, Math.floor(fz)));
    const tx = Math.min(1, Math.max(0, fx - ix)), tz = Math.min(1, Math.max(0, fz - iz));
    const a = h[iz * nx + ix], b = h[iz * nx + ix + 1], c = h[(iz + 1) * nx + ix], d = h[(iz + 1) * nx + ix + 1];
    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
  };
  return { version, nx, nz, x0, z0, cell, base, h, at, asl: (x, z) => at(x, z) + base };
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
           min_asl: r1(dmin + dem.base), max_asl: r1(dmax + dem.base) },
    bld: { version: bld.version, footprints: bld.footprints.length, points: bld.points,
           bbox: bld.bbox.map(v => Math.round(v)) },
  };
}
