// tracks/lib.mjs - load the shipped Charlotte graph (the bytes the game reads) and route on it.
import { readFileSync } from 'node:fs';
import { pathToFileURL, fileURLToPath } from 'node:url';

export const WT = fileURLToPath(new URL('../../../', import.meta.url)).replace(/[\/]$/, '');
const cd = await import(pathToFileURL(WT + '/tools/city/lib/citydata.mjs').href);
export const { parseCity, parseDem, profileFor, lineSetOf, LANE_M } = cd;

export const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;
export const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
export const toX = lon => (lon - LON0) * M_LON;
export const toZ = lat => (lat - LAT0) * M_LAT;
export const toLon = x => LON0 + x / M_LON;
export const toLat = z => LAT0 + z / M_LAT;

let _city = null, _dem = null;
export function city() {
  if (_city) return _city;
  _city = parseCity(readFileSync(WT + '/Assets/PSXRacing/Resources/charlotte_city.bytes'));
  return _city;
}
export function dem() {
  if (_dem) return _dem;
  _dem = parseDem(readFileSync(WT + '/Assets/PSXRacing/Resources/charlotte_dem.bytes'));
  return _dem;
}

const norm = s => (s || '').toLowerCase();
export function edgesNamed(name) {
  const C = city(), n = norm(name);
  return C.edges.filter(e => norm(e.name) === n);
}
export function edgesLike(name) {
  const C = city(), n = norm(name);
  return C.edges.filter(e => norm(e.name).includes(n));
}

/// nodes where an edge named A and an edge named B meet (exact names, case-insensitive), nearest the hint
export function junction(A, B, nearLat, nearLon, maxD = 1500) {
  const C = city();
  const a = norm(A), b = norm(B);
  const hx = nearLat != null ? toX(nearLon) : 0, hz = nearLat != null ? toZ(nearLat) : 0;
  let best = -1, bd = Infinity;
  for (let i = 0; i < C.nodes.length; i++) {
    const es = C.nodeEdges[i];
    if (es.length < 2) continue;
    let ha = false, hb = false;
    for (const ei of es) { const nm = norm(C.edges[ei].name); if (nm === a) ha = true; if (nm === b) hb = true; }
    if (!ha || !hb) continue;
    const d = nearLat != null ? Math.hypot(C.nodes[i].x - hx, C.nodes[i].z - hz) : 0;
    if (d < bd) { bd = d; best = i; }
  }
  if (best < 0 || bd > maxD) throw new Error(`no junction ${A} x ${B} near ${nearLat},${nearLon} (best ${bd.toFixed(0)} m)`);
  return best;
}

/// nearest node that an edge named `name` touches (or any edge if name null)
export function nodeNear(lat, lon, name = null, maxD = 800) {
  const C = city();
  const x = toX(lon), z = toZ(lat), n = norm(name);
  let best = -1, bd = Infinity;
  for (let i = 0; i < C.nodes.length; i++) {
    if (name) {
      let ok = false;
      for (const ei of C.nodeEdges[i]) if (norm(C.edges[ei].name) === n) { ok = true; break; }
      if (!ok) continue;
    }
    const d = Math.hypot(C.nodes[i].x - x, C.nodes[i].z - z);
    if (d < bd) { bd = d; best = i; }
  }
  if (best < 0 || bd > maxD) throw new Error(`no node of ${name} near ${lat},${lon} (best ${bd.toFixed(0)} m)`);
  return best;
}

// ------------------------------------------------------------------ routing
// Edge-based Dijkstra (state = arrival edge + dir) so a turn penalty can be charged.
function heading(pts, atStart) {
  const n = pts.length;
  if (atStart) { const [a, b] = [pts[0], pts[Math.min(1, n - 1)]]; return Math.atan2(b[1] - a[1], b[0] - a[0]); }
  const [a, b] = [pts[Math.max(0, n - 2)], pts[n - 1]]; return Math.atan2(b[1] - a[1], b[0] - a[0]);
}
export function edgePts(e, dir) { return dir === 1 ? e.pts : e.pts.slice().reverse(); }
function angDiff(a, b) { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; }

/// the class cost multiplier: prefer arterials, avoid ramps and locals unless forced
export function classMul(e, opts = {}) {
  if (opts.allowRamps === false && e.link) return 50;
  // a freeway lap: freeways and their ramps are the road; anything else is the long way round
  if (opts.fwy) return e.link || e.rank >= 4 ? 1 : 4;
  let m = e.rank >= 2 ? 1 : e.rank === 1 ? 1.25 : 2.5;
  if (e.link) m *= opts.rampMul ?? 4;
  if (e.rank >= 4 && opts.avoidFreeway) m *= 6;
  return m;
}

export function route(fromNode, toNode, opts = {}) {
  const C = city();
  const turnPen = opts.turnPen ?? 60;          // metres charged for a 90-degree turn
  const uturnMax = opts.uturnMax ?? 2.6;      // radians: refuse sharper
  const ban = opts.banEdges || new Set();
  const banNodes = opts.banNodes || new Set();
  // states: key = edgeIndex*2 + (dir===1?0:1)
  const dist = new Map(), prev = new Map();
  const heap = [];
  const push = (d, k) => { heap.push([d, k]); let i = heap.length - 1; while (i > 0) { const p = (i - 1) >> 1; if (heap[p][0] <= heap[i][0]) break; [heap[p], heap[i]] = [heap[i], heap[p]]; i = p; } };
  const pop = () => { const top = heap[0]; const last = heap.pop(); if (heap.length) { heap[0] = last; let i = 0; for (;;) { const l = 2 * i + 1, r = l + 1; let m = i; if (l < heap.length && heap[l][0] < heap[m][0]) m = l; if (r < heap.length && heap[r][0] < heap[m][0]) m = r; if (m === i) break; [heap[m], heap[i]] = [heap[i], heap[m]]; i = m; } } return top; };
  const startEdges = opts.startEdge != null ? [opts.startEdge] : C.nodeEdges[fromNode].map(ei => ({ ei, dir: C.edges[ei].a === fromNode ? 1 : -1 }));
  const hArr = opts.arrive ? heading(edgePts(C.edges[opts.arrive.ei], opts.arrive.dir), false) : null;
  for (const s of startEdges) {
    const e = C.edges[s.ei];
    if (opts.arrive && (s.ei === opts.arrive.ei || Math.abs(angDiff(hArr, heading(edgePts(e, s.dir), true))) > uturnMax)) continue;
    if (ban.has(s.ei)) continue;
    if (s.dir === -1 && e.oneway) continue;
    if (e.a === e.b) continue;
    const k = s.ei * 2 + (s.dir === 1 ? 0 : 1);
    const d = e.length * classMul(e, opts);
    if (d < (dist.get(k) ?? Infinity)) { dist.set(k, d); prev.set(k, -1); push(d, k); }
  }
  let goal = -1;
  while (heap.length) {
    const [d, k] = pop();
    if (d > (dist.get(k) ?? Infinity)) continue;
    const ei = k >> 1, dir = (k & 1) ? -1 : 1, e = C.edges[ei];
    const end = dir === 1 ? e.b : e.a;
    if (end === toNode) { goal = k; break; }
    if (banNodes.has(end)) continue;
    if (d > (opts.maxCost ?? 60000)) break;
    const hIn = heading(edgePts(e, dir), false);
    for (const nj of C.nodeEdges[end]) {
      const f = C.edges[nj];
      const ndir = f.a === end ? 1 : -1;
      if (f.a === f.b) continue;
      if (ndir === -1 && f.oneway) continue;
      if (nj === ei) continue;
      if (ban.has(nj)) continue;
      const hOut = heading(edgePts(f, ndir), true);
      const turn = Math.abs(angDiff(hIn, hOut));
      if (turn > uturnMax) continue;
      const nk = nj * 2 + (ndir === 1 ? 0 : 1);
      const nd = d + f.length * classMul(f, opts) + turnPen * (turn / (Math.PI / 2)) ** 2 + (C.nodeEdges[end].length > 2 ? (opts.junctionPen ?? 0) : 0);
      if (nd < (dist.get(nk) ?? Infinity)) { dist.set(nk, nd); prev.set(nk, k); push(nd, nk); }
    }
  }
  if (goal < 0) return null;
  const out = [];
  for (let k = goal; k !== -1; k = prev.get(k)) out.push({ ei: k >> 1, dir: (k & 1) ? -1 : 1 });
  return out.reverse();
}

/// the edge named `name` nearest (lat, lon) that can be driven heading `hdg` (compass degrees, 0 = north)
export function edgeNear(lat, lon, name, hdg, maxD = 600) {
  const C = city();
  const x = toX(lon), z = toZ(lat), n = norm(name);
  let best = null, bd = Infinity;
  for (const e of C.edges) {
    if (name && norm(e.name) !== n) continue;
    if (e.a === e.b || e.pts.length < 2) continue;
    // distance from the point to the polyline
    let dmin = Infinity, segH = 0;
    for (let k = 1; k < e.pts.length; k++) {
      const [ax, az] = e.pts[k - 1], [bx, bz] = e.pts[k];
      const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
      let t = L2 > 0 ? ((x - ax) * dx + (z - az) * dz) / L2 : 0; t = Math.max(0, Math.min(1, t));
      const d = Math.hypot(x - (ax + dx * t), z - (az + dz * t));
      if (d < dmin) { dmin = d; segH = Math.atan2(dx, dz) * 180 / Math.PI; }   // compass heading of the a->b direction
    }
    for (const dir of e.oneway ? [1] : [1, -1]) {
      const hh = dir === 1 ? segH : segH + 180;
      let dh = Math.abs(((hh - hdg) % 360 + 540) % 360 - 180);
      if (dh > 60) continue;
      const score = dmin + dh * 0.5;
      if (score < bd) { bd = score; best = { ei: e.index, dir }; }
    }
  }
  if (!best || bd > maxD) throw new Error(`no edge ${name} heading ${hdg} near ${lat},${lon}`);
  return best;
}

/// route a chain of waypoints (a node index, or {ei, dir} = an edge to drive); returns [{ei, dir}]
/// (a loop closes back to the first waypoint)
export function routeVia(wps, opts = {}) {
  const C = city();
  const chain = [];
  const legs = opts.loop ? [...wps, wps[0]] : wps;
  const startOf = w => typeof w === 'number' ? w : (w.dir === 1 ? C.edges[w.ei].a : C.edges[w.ei].b);
  const endOf = w => typeof w === 'number' ? w : (w.dir === 1 ? C.edges[w.ei].b : C.edges[w.ei].a);
  if (typeof legs[0] !== 'number') chain.push({ ei: legs[0].ei, dir: legs[0].dir });
  for (let i = 1; i < legs.length; i++) {
    const closing = opts.loop && i === legs.length - 1;
    const from = endOf(legs[i - 1]), to = startOf(legs[i]);
    if (from !== to) {
      const leg = routeFrom(chain.length ? chain[chain.length - 1] : null, from, to, opts);
      if (!leg) throw new Error(`no route leg ${i - 1}->${i} (node ${from} -> ${to})`);
      chain.push(...leg);
    }
    if (typeof legs[i] !== 'number' && !closing) chain.push({ ei: legs[i].ei, dir: legs[i].dir });
  }
  return chain;
}

function routeFrom(last, from, to, opts) {
  const C = city();
  return route(from, to, last ? { ...opts, arrive: last } : opts);
  // seed with the edges leaving `from` that do not U-turn off `last`
  const e = C.edges[last.ei];
  const hIn = heading(edgePts(e, last.dir), false);
  const seeds = [];
  for (const nj of C.nodeEdges[from]) {
    const f = C.edges[nj];
    const ndir = f.a === from ? 1 : -1;
    if (ndir === -1 && f.oneway) continue;
    if (nj === last.ei) continue;
    const turn = Math.abs(angDiff(hIn, heading(edgePts(f, ndir), true)));
    if (turn > (opts.uturnMax ?? 2.6)) continue;
    seeds.push({ ei: nj, dir: ndir });
  }
  let best = null, bl = Infinity;
  for (const s of seeds) {
    const f = C.edges[s.ei];
    const end = s.dir === 1 ? f.b : f.a;
    if (end === to) { if (f.length < bl) { best = [s]; bl = f.length; } continue; }
    const r = route(end, to, { ...opts, startEdge: undefined, seedPrev: s });
    if (!r) continue;
    // r must not U-turn off s: check the first turn
    const h1 = heading(edgePts(f, s.dir), false), h2 = heading(edgePts(C.edges[r[0].ei], r[0].dir), true);
    if (Math.abs(angDiff(h1, h2)) > (opts.uturnMax ?? 2.6)) continue;
    const L = [s, ...r].reduce((a, c) => a + C.edges[c.ei].length * classMul(C.edges[c.ei], opts), 0);
    if (L < bl) { bl = L; best = [s, ...r]; }
  }
  return best;
}

/// dense polyline of a chain
export function chainPoly(chain) {
  const C = city();
  const poly = [];
  for (const c of chain) {
    const pts = edgePts(C.edges[c.ei], c.dir);
    for (let i = poly.length ? 1 : 0; i < pts.length; i++) poly.push(pts[i]);
  }
  return poly;
}

/// resample a polyline every `step` metres; returns {pts, s}
export function resample(poly, step = 5) {
  const out = [poly[0]], S = [0];
  let acc = 0, carry = 0;
  for (let i = 1; i < poly.length; i++) {
    const [ax, az] = poly[i - 1], [bx, bz] = poly[i];
    const seg = Math.hypot(bx - ax, bz - az);
    if (seg < 1e-6) continue;
    let t = step - carry;
    while (t <= seg) { out.push([ax + (bx - ax) * t / seg, az + (bz - az) * t / seg]); S.push(acc + t); t += step; }
    carry = seg - (t - step);
    acc += seg;
  }
  const last = poly[poly.length - 1];
  if (Math.hypot(last[0] - out[out.length - 1][0], last[1] - out[out.length - 1][1]) > 0.5) { out.push(last); S.push(acc); }
  return { pts: out, s: S, length: acc };
}

export { angDiff };
