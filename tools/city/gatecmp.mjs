// gatecmp.mjs - THE TWO GATES, RUN BY RUN (the agreement instrument; Docs/CHARLOTTE.md, "Smoothness gate").
// Editor/CitySmooth.cs (the built meshes) and linecheck.mjs (the plan-view replica) must agree on every check both
// measure. A run one gate has and the other lacks is a gate bug until it is put down to a MECHANISM AT THE RUN'S OWN
// SECTIONS - what each gate was given there, and what each read there - never to what merely lies near it.
//
//   node tools/city/gatecmp.mjs places <gate.csv> <places.csv>
//       the places of one gate's runs (idx,check,edge,line,s0,s1), for the OTHER gate's trace:
//       CitySmooth FULL -Trace <offline places> -TraceOut <mesh reads>; linecheck --trace <mesh places>
//       --trace-out <offline reads>
//   node tools/city/gatecmp.mjs compare --mesh <city_smooth.csv> --offline <linecheck.csv>
//       [--mesh-reads <csv>] [--offline-reads <csv>] [--tap <dump.gz>] [--out <dir>]
//       --tap: CitySmooth FULL -TapDump (the builder's own sections and flags, tile by tile)
//
// The match (as before): a run agrees when the other gate has one of the same check on the same edge and line class
// whose arc overlaps it within 2 m, or else one of the same check within 3 m whose ratio is within 10% (or 0.1x).
// Every run left is put down to the FIRST of these that holds:
//   design      B4s (a seam between two tiles: the mesh gate only), a junction fan's perimeter (FAN: the mesh gate
//               only), a bend fan's mouth or a bend fan inside the check's window along the line (the mesh walks the
//               slab's perimeter, the replica bridges it), D1 inside a gore quad or a junction fan (the replica draws
//               neither)
//   agrees      the other gate reads 1x or more at the run's own place on the same line (its reads): both fail there,
//               and cut or key the run differently
//   ring        the check's window reaches the joint where the offline gate cuts a closed ring (its chains start at
//               their lowest edge; the mesh gate walks through a big ring), or lies on a small ring both gates cut,
//               each at its own joint
//   level       D1: the replica's plan approximation of level (a crossing pair, a crossing within 80 m, structure)
//               separates the two roads and the builder's surfaces do not, or the builder's surfaces are more than
//               CrossDyM apart where the replica (no heights) has them on one level
//   input       what the two gates were GIVEN differs at the run's own sections, along its line (and its chain) for
//               the check's window (B1 2.25 m, B2 KinkViewM, B3 CurveHalfM, C3 a dash and a gap, else 1 m) - or, for
//               D1, the other road's ribbon: a clip, squeeze or collapse flag, the builder's PER-TILE tables
//               disagreeing with themselves, a section only one of them cuts, a ribbon corner or U more than InputTolM
//               apart, the surface (on structure or not)
//   threshold   the same input, and the other gate reads 0.9x-1x there while this one reads under 1.1x
//   kept        (B1-B3, with --strands <mesh dump> <offline dump>) the same sections to 1 mm, but the collinear drop
//               kept different vertices within the check's window: a sub-millimetre difference flipped its 0.02 deg test
//   float       (B2, with --strands) the same kept vertices, and lib/kink.mjs run on each gate's own dumped coordinates
//               reproduces that gate's own score: the rules are the same, a discrete step flips on the sub-mm difference
//   UNEXPLAINED the same input read differently: a gate bug, to be fixed (in both) or explained
import { readFileSync, writeFileSync, mkdirSync, createReadStream } from 'node:fs';
import { createInterface } from 'node:readline';
import { gunzipSync } from 'node:zlib';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity } from './lib/citydata.mjs';
import { createSim } from './lib/linesim.mjs';
import { readSmoothRules } from './lib/smoothrules.mjs';
import { lineClass } from './lib/linegate.mjs';
import { kinkScores } from './lib/kink.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const ARGS = process.argv.slice(2);
const argVal = (n, d = null) => { const i = ARGS.indexOf(n); return i >= 0 && ARGS[i + 1] && !ARGS[i + 1].startsWith('--') ? ARGS[i + 1] : d; };
const R = readSmoothRules(join(UNITY, 'Assets/PSXRacing/Editor/SmoothRules.cs'));
export const InputTolM = 0.001;

function parseLine(line) {
  const out = []; let cur = '', q = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (q) { if (ch === '"') { if (line[i + 1] === '"') { cur += '"'; i++; } else q = false; } else cur += ch; }
    else if (ch === '"') q = true; else if (ch === ',') { out.push(cur); cur = ''; } else cur += ch;
  }
  out.push(cur); return out;
}
async function loadRuns(path) {
  const rl = createInterface({ input: createReadStream(path), crlfDelay: Infinity });
  let head = null; const rows = [];
  for await (const line of rl) {
    if (!line) continue;
    const f = parseLine(line);
    if (!head) { head = f; continue; }
    const o = {}; head.forEach((h, i) => { o[h] = f[i]; });
    const r = { idx: rows.length, check: o.check, lineId: o.lineId, edge: +(o.edge ?? o.e), s: +o.s, s0: +o.s0, s1: +o.s1, ratio: +o.ratio,
      x: +o.x, z: +o.z, cause: o.cause || '', what: o.what || '', reportOnly: o.reportOnly || '', name: o.name || '', len: +o.len,
      e0: o.e0 !== undefined && o.e0 !== '' ? +o.e0 : NaN, e1: o.e1 !== undefined && o.e1 !== '' ? +o.e1 : NaN };
    // the run's arc on its worst edge: s, and s0 / s1 where they lie on that edge
    let lo = r.s, hi = r.s;
    if (r.e0 === r.edge || Number.isNaN(r.e0)) { lo = Math.min(lo, r.s0); hi = Math.max(hi, r.s0); }
    if (r.e1 === r.edge || Number.isNaN(r.e1)) { lo = Math.min(lo, r.s1); hi = Math.max(hi, r.s1); }
    r.lo = lo; r.hi = hi;
    rows.push(r);
  }
  return rows;
}
function loadReads(path) {
  const m = new Map();
  if (!path) return null;
  for (const l of readFileSync(path, 'utf8').split(/\r?\n/).slice(1)) {
    if (!l) continue;
    const [idx, maxE, nE, maxC, nC] = l.split(',').map(Number);
    m.set(idx, { maxE, nE, maxC, nC });
  }
  return m;
}

// ------------------------------------------------------------------ places
if (ARGS[0] === 'places') {
  const rows = await loadRuns(resolve(ARGS[1]));
  const out = ['idx,check,edge,line,s0,s1'];
  for (const r of rows) {
    // the checks a trace can read: the ones whose samples go through a RunBuilder (not the events B4, B4s, C1-C3)
    if (r.check === 'B4s' || r.check === 'C1' || r.check === 'C2' || r.check === 'C3' || r.check === 'B4' || r.check === 'E1' || r.check === 'A0') continue;
    // the run's own arc on its worst edge, 1 m either side; a run reported across a joint, 3 m about its worst
    const one = r.e0 === r.edge && r.e1 === r.edge;
    const a = one ? r.lo - 1 : r.s - 3, b = one ? r.hi + 1 : r.s + 3;
    out.push(`${r.idx},${r.check},${r.edge},${r.lineId},${a.toFixed(3)},${b.toFixed(3)}`);
  }
  writeFileSync(resolve(ARGS[2]), out.join('\n') + '\n');
  console.log(`${out.length - 1} places of ${rows.length} runs -> ${ARGS[2]}`);
  process.exit(0);
}
if (ARGS[0] !== 'compare') { console.error('usage: gatecmp.mjs places <gate.csv> <places.csv> | compare --mesh <csv> --offline <csv> [...]'); process.exit(2); }

// ------------------------------------------------------------------ compare: match
const t0 = Date.now();
const M = await loadRuns(resolve(argVal('--mesh'))), O = await loadRuns(resolve(argVal('--offline')));
const readsOfOffline = loadReads(argVal('--offline-reads') && resolve(argVal('--offline-reads')));   // offline gate at the MESH runs
const readsOfMesh = loadReads(argVal('--mesh-reads') && resolve(argVal('--mesh-reads')));            // mesh gate at the OFFLINE runs
const byKey = rows => { const m = new Map(); for (const r of rows) { const k = r.check + '|' + r.edge + '|' + lineClass(r.lineId); let l = m.get(k); if (!l) m.set(k, l = []); l.push(r); } return m; };
const KM = byKey(M), KO = byKey(O);
const overlaps = (r, x) => Math.max(r.lo, x.lo) - 2 <= Math.min(r.hi, x.hi) + 2;
const stage1 = (r, K) => (K.get(r.check + '|' + r.edge + '|' + lineClass(r.lineId)) || []).some(x => overlaps(r, x));
const grid = rows => { const g = new Map(); for (const r of rows) { const k = r.check + '|' + Math.floor(r.x / 10) + '|' + Math.floor(r.z / 10); let l = g.get(k); if (!l) g.set(k, l = []); l.push(r); } return g; };
const GM = grid(M), GO = grid(O);
const byPlace = (r, G) => {
  const cx = Math.floor(r.x / 10), cz = Math.floor(r.z / 10);
  for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) for (const x of G.get(r.check + '|' + (cx + dx) + '|' + (cz + dz)) || [])
    if (Math.hypot(x.x - r.x, x.z - r.z) <= 3 && (Math.abs(x.ratio - r.ratio) <= 0.1 || Math.abs(x.ratio - r.ratio) / Math.max(1, r.ratio) <= 0.1)) return true;
  return false;
};
const left = [];
// the instrument checks itself: a run the other gate PARTNERS (stage 1) should read 1x or more in that gate's reads
const sanity = { mesh: [0, 0], offline: [0, 0] };
for (const [rows, K, G, side, reads] of [[M, KO, GO, 'mesh', readsOfOffline], [O, KM, GM, 'offline', readsOfMesh]])
  for (const r of rows) {
    const s1 = stage1(r, K);
    if (s1 && reads && reads.has(r.idx)) { sanity[side][0]++; if (reads.get(r.idx).maxE >= 0.999) sanity[side][1]++; }
    if (!s1 && !byPlace(r, G)) { r.side = side; left.push(r); }
  }

// ------------------------------------------------------------------ compare: what each gate was given
const city = parseCity(readFileSync(join(UNITY, 'Assets/PSXRacing/Resources/charlotte_city.bytes')));
const S = createSim(city, 'asbuilt');
const { E, T, nodeEdges } = S;
const secsCache = new Map();
const repSecs = e => { let s = secsCache.get(e); if (s === undefined) { s = S.sectionsOf(E[e]) || []; secsCache.set(e, s); } return s; };
// the window of the line a check's reading depends on, along the line either side of the run
const WIN = { B1: R.JitterHalfM + R.JitterStepM, B2: R.KinkViewM, B3: R.CurveHalfM, C2: 3, C3: R.DashM + R.DashGapM };
const winOf = c => WIN[c] ?? 1;

// the chains both gates walk: through mitred joints and bend fans (linegate.mjs jointAt / bendAt)
const hasRibbon = e => repSecs(e.index).length >= 2;
const endsAt = (e, n) => { const sc = repSecs(e.index); return sc.length >= 2 && (e.a === n ? sc[0].s < 1e-6 : Math.abs(sc[sc.length - 1].s - e.length) < 1e-6); };
const jointAt = (e, n) => {
  if (!T.mitre[n] || e.a === e.b) return -1;
  const o = T.throughA[n] === e.index ? T.throughB[n] : T.throughB[n] === e.index ? T.throughA[n] : -1;
  if (o < 0 || o === e.index || E[o].a === E[o].b) return -1;
  return endsAt(e, n) && endsAt(E[o], n) ? o : -1;
};
const armsOf = n => nodeEdges[n].filter(i => E[i].a !== E[i].b);
const isBendFan = n => !!T.patch[n] && armsOf(n).length === 2;
const bendAt = (e, n) => { if (e.a === e.b || !isBendFan(n) || !hasRibbon(e)) return -1; const arms = armsOf(n), o = arms[0] === e.index ? arms[1] : arms[1] === e.index ? arms[0] : -1; return o >= 0 && o !== e.index && hasRibbon(E[o]) ? o : -1; };
const linkAt = (e, n) => { const o = jointAt(e, n); if (o >= 0) return { o, kind: 'mitre' }; const b = bendAt(e, n); return b >= 0 ? { o: b, kind: 'bend' } : null; };
/// The line's window [a, b] on edge e, carried on along the chain across its joints: [[edge, a, b, via]] (via: the
/// links crossed, 'mitre' / 'bend').
function windowOn(e, a, b, depth = 0, via = []) {
  const out = [[e, a, b, via]];
  if (depth > 6) return out;
  const E0 = E[e];
  if (a < 0) { const l = linkAt(E0, E0.a); if (l) { const o = E[l.o], d = -a; out.push(...(o.a === E0.a ? windowOn(l.o, 0, d, depth + 1, [...via, l.kind]) : windowOn(l.o, o.length - d, o.length, depth + 1, [...via, l.kind]))); } }
  if (b > E0.length) { const l = linkAt(E0, E0.b); if (l) { const o = E[l.o], d = b - E0.length; out.push(...(o.a === E0.b ? windowOn(l.o, 0, d, depth + 1, [...via, l.kind]) : windowOn(l.o, o.length - d, o.length, depth + 1, [...via, l.kind]))); } }
  return out;
}
// closed rings of links: the offline gate starts a chain at its lowest edge, so it cuts a ring at the joint before that
// edge; the mesh gate cuts a ring it can walk round inside its 3x3 tiles wherever its walk started, and a larger one not
const ringOf = new Map();   // edge -> { cutNode, small, name }
{
  const seen = new Uint8Array(E.length);
  for (const e0 of E) {
    if (seen[e0.index] || !hasRibbon(e0)) continue;
    let cur = e0, enter = e0.a, ring = false, guard = 0;
    for (;;) { const l = linkAt(cur, enter); if (!l) break; if (l.o === e0.index) { ring = true; break; } if (seen[l.o] || ++guard > 100000) break; const oe = E[l.o]; enter = oe.a === enter ? oe.b : oe.a; cur = oe; }
    const list = []; let at = cur, from = enter;
    for (;;) { if (seen[at.index]) break; seen[at.index] = 1; list.push(at); const exit = at.a === from ? at.b : at.a; const l = linkAt(at, exit); if (!l) break; at = E[l.o]; from = exit; }
    if (!ring) continue;
    const L = list.reduce((s, x) => s + x.length, 0), info = { cutNode: enter, small: L < 2 * 256, length: L, name: list.map(x => x.name).find(Boolean) || '', edges: list.length };
    for (const x of list) ringOf.set(x.index, info);
  }
}

// the builder's tap: every ribbon span, gore quad and fan, tile by tile ('TAP2' with heights; 'TAP1' without)
const tap = new Map();   // edge -> [span]
const GC = 16, gkey = (cx, cz) => `${cx},${cz}`;
const spanGrid = new Map(), goreGrid = new Map(), fanGrid = new Map();
const gridAdd = (G, obj, xs, zs) => {
  const x0 = Math.floor(Math.min(...xs) / GC), x1 = Math.floor(Math.max(...xs) / GC), z0 = Math.floor(Math.min(...zs) / GC), z1 = Math.floor(Math.max(...zs) / GC);
  for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) { const k = gkey(cx, cz); let l = G.get(k); if (!l) G.set(k, l = []); l.push(obj); }
};
const gridAt = (G, x, z) => G.get(gkey(Math.floor(x / GC), Math.floor(z / GC))) || [];
let tapTiles = 0, tapSpans = 0, tapGores = 0, tapFans = 0, tapHeights = false;
const tapPath = argVal('--tap');
// the strands as each gate's shape checks kept them (CitySmooth PSX_SMOOTH_STRANDS, linecheck --strands): where the
// two gates were given the same sections and read a line differently, whether they KEPT different vertices
const strandSets = [];
{
  const i = ARGS.indexOf('--strands');
  if (i >= 0) for (const f of [ARGS[i + 1], ARGS[i + 2]]) {
    const grid = new Map();
    let cur = null;
    for (const l of readFileSync(resolve(f), 'utf8').split(/\r?\n/)) {
      if (l.startsWith('S ')) { cur = []; cur.kind = l.split(' ')[1]; continue; }
      if (!l || !cur) continue;
      const [e, sv, x, z, ex, b2] = l.split(' ').map(Number);
      const v = { e, s: sv, x, z, ex, b2, st: cur }; cur.push(v);
      const k = gkey(Math.floor(x / GC), Math.floor(z / GC)); let g = grid.get(k); if (!g) grid.set(k, g = []); g.push(v);
    }
    strandSets.push(grid);
  }
}
/// B2 re-scored with lib/kink.mjs on the strand through (x, z) as one gate dumped it (its own coordinates, exemptions and
/// the strand's half width): [the dumped score, the re-score] at that vertex; null without a strand there.
function rescoreAt(grid, x, z) {
  for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) for (const v of grid.get(gkey(Math.floor(x / GC) + dx, Math.floor(z / GC) + dz)) || []) {
    if (Math.hypot(v.x - x, v.z - z) >= 0.01) continue;
    const V = v.st, n = V.length, X = V.map(q => q.x), Z = V.map(q => q.z), C = new Float64Array(n), TH = new Float64Array(n);
    for (let i = 1; i < n; i++) C[i] = C[i - 1] + Math.hypot(X[i] - X[i - 1], Z[i] - Z[i - 1]);
    for (let i = 1; i + 1 < n; i++) { const ux = X[i] - X[i - 1], uz = Z[i] - Z[i - 1], vx = X[i + 1] - X[i], vz = Z[i + 1] - Z[i]; TH[i] = Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz); }
    let hw = 0; if (V.kind === 'edge' || V.kind === 'paint') for (const q of V) hw = Math.max(hw, E[q.e].width / 2);
    const sc = kinkScores(X, Z, C, TH, R, m => V[m].ex === 1, hw);
    return [v.b2, sc[V.indexOf(v)]];
  }
  return null;
}
/// The kept vertices of the strand(s) through (x, z) within w of it, in one gate: a set of 'e:s' (s to 1 mm); null when
/// the dump holds no strand there.
function keptNear(grid, x, z, w) {
  const hits = new Set();
  for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) for (const v of grid.get(gkey(Math.floor(x / GC) + dx, Math.floor(z / GC) + dz)) || [])
    if (Math.hypot(v.x - x, v.z - z) < 0.01) hits.add(v.st);
  if (!hits.size) return null;
  const out = new Set();
  for (const st of hits) for (const v of st) if (Math.hypot(v.x - x, v.z - z) <= w) out.add(`${v.e}:${v.s.toFixed(3)}`);
  return out;
}
if (tapPath) {
  const buf = gunzipSync(readFileSync(resolve(tapPath)));
  let o = 0;
  const i32 = () => { const v = buf.readInt32LE(o); o += 4; return v; }, f32 = () => { const v = buf.readFloatLE(o); o += 4; return v; };
  const u16 = () => { const v = buf.readUInt16LE(o); o += 2; return v; };
  const magic = i32();
  if (magic !== 0x31504154 && magic !== 0x32504154) throw new Error('not a TAP1/TAP2 dump');
  tapHeights = magic === 0x32504154;
  const Tsz = 256;
  while (o < buf.length) {
    const tx = i32(), tz = i32(), nq = i32(), ng = i32(), nf = i32(), ox = tx * Tsz, oz = tz * Tsz;
    tapTiles++;
    for (let k = 0; k < nq + ng; k++) {
      const edge = i32(), sA = f32(), sB = f32(), fA = u16(), fB = u16();
      const P = []; for (let j = 0; j < 4; j++) { const x = f32(), z = f32(); P.push([ox + x, oz + z, 0]); }
      const uAL = f32(), uBL = f32(), uBR = f32(), uAR = f32();
      if (tapHeights) for (let j = 0; j < 4; j++) P[j][2] = f32();
      const q = { tile: tx + ',' + tz, edge, sA, sB, fA, fB, AL: P[0], BL: P[1], BR: P[2], AR: P[3], uAL, uBL, uBR, uAR };
      if (k >= nq) { tapGores++; gridAdd(goreGrid, q, P.map(p => p[0]), P.map(p => p[1])); continue; }
      tapSpans++;
      let l = tap.get(edge); if (!l) tap.set(edge, l = []);
      l.push(q);
      gridAdd(spanGrid, q, P.map(p => p[0]), P.map(p => p[1]));
    }
    for (let k = 0; k < nf; k++) {
      const node = i32(), n = i32(); o += 8;
      const C = []; for (let j = 0; j < n; j++) { const x = f32(), z = f32(); C.push([ox + x, oz + z, tapHeights ? f32() : 0]); }
      tapFans++;
      gridAdd(fanGrid, { node, C }, C.map(p => p[0]), C.map(p => p[1]));
    }
  }
}
// point in a triangle (barycentric), point to segment
const bary = (a, b, c, x, z) => {
  const det = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1]);
  if (Math.abs(det) < 1e-12) return null;
  const wa = ((b[1] - c[1]) * (x - c[0]) + (c[0] - b[0]) * (z - c[1])) / det, wb = ((c[1] - a[1]) * (x - c[0]) + (a[0] - c[0]) * (z - c[1])) / det, wc = 1 - wa - wb;
  return wa >= -1e-9 && wb >= -1e-9 && wc >= -1e-9 ? [wa, wb, wc] : null;
};
const segDist = (a, b, x, z) => { const dx = b[0] - a[0], dz = b[1] - a[1], L2 = dx * dx + dz * dz; const t = L2 > 1e-12 ? Math.max(0, Math.min(1, ((x - a[0]) * dx + (z - a[1]) * dz) / L2)) : 0; return Math.hypot(a[0] + dx * t - x, a[1] + dz * t - z); };
/// The builder's ribbon of edge e under (x, z): its height there and how far inside its drawn edges (CitySmooth QuadDepth).
function tapUnder(e, x, z) {
  let best = null;
  for (const q of gridAt(spanGrid, x, z)) {
    if (q.edge !== e) continue;
    for (const [a, b, c] of [[q.AL, q.BR, q.BL], [q.AL, q.AR, q.BR]]) {
      const w = bary(a, b, c, x, z); if (!w) continue;
      const y = a[2] * w[0] + b[2] * w[1] + c[2] * w[2], d = Math.min(segDist(q.AL, q.BL, x, z), segDist(q.AR, q.BR, x, z));
      if (!best || d > best.d) best = { y, d };
    }
  }
  return best;
}
/// The replica's ribbon of edge e under (x, z): how far inside (linegate.mjs D1's triangles).
function repUnder(e, x, z) {
  const sc = repSecs(e); let best = 0;
  for (let i = 1; i < sc.length; i++) {
    const A = sc[i - 1], B = sc[i];
    if (A.collapsed && B.collapsed) continue;
    for (const [a, b, c] of [[A.L, B.R, B.L], [A.L, A.R, B.R]]) if (bary(a, b, c, x, z)) best = Math.max(best, Math.min(segDist(A.L, B.L, x, z), segDist(A.R, B.R, x, z)));
  }
  return best;
}
const FL = [[1, 'squeezed L'], [2, 'squeezed R'], [4, 'clipped L'], [8, 'clipped R'], [16, 'collapsed'], [32, 'on structure']];
const repFlags = c => (c.sqL ? 1 : 0) | (c.sqR ? 2 : 0) | (c.clippedIn && c.innerSide < 0 ? 4 : 0) | (c.clippedIn && c.innerSide > 0 ? 8 : 0) | (c.collapsed ? 16 : 0) | (c.elev ? 32 : 0);
const flagText = f => FL.filter(([b]) => f & b).map(([, t]) => t).join('+') || 'none';
/// What the two gates were given on edge e over [a, b]: the first difference, or null when they were given the same.
function inputDiff(e, a, b) {
  // every span that reaches into [a, b], and both of its sections (a span's far end shapes the line inside the window);
  // the replica's sections from the last one at or before a to the first one at or after b
  const spans = (tap.get(e) || []).filter(q => Math.max(q.sA, q.sB) >= a && Math.min(q.sA, q.sB) <= b);
  const rs = repSecs(e); let i0 = 0, i1 = rs.length - 1;
  while (i0 + 1 < rs.length && rs[i0 + 1].s <= a) i0++;
  while (i1 - 1 >= 0 && rs[i1 - 1].s >= b) i1--;
  const secs = rs.slice(i0, i1 + 1);
  if (!spans.length && !secs.length) return null;
  if (!spans.length) return secs.every(c => c.collapsed) ? null : { kind: 'sections', text: `e${e}: the replica has ${secs.length} section(s) at s ${a.toFixed(1)}-${b.toFixed(1)} the builder drew no span through` };
  const ends = [];
  for (const q of spans) {
    ends.push({ s: q.sA, f: q.fA, L: q.AL, R: q.AR, uL: q.uAL, uR: q.uAR, tile: q.tile });
    ends.push({ s: q.sB, f: q.fB, L: q.BL, R: q.BR, uL: q.uBL, uR: q.uBR, tile: q.tile });
  }
  const all = repSecs(e);
  const find = s => { let lo = 0, hi = all.length - 1; while (lo < hi) { const m = (lo + hi) >> 1; if (all[m].s < s) lo = m + 1; else hi = m; }
    for (const k of [lo - 1, lo, lo + 1]) if (all[k] && Math.abs(all[k].s - s) < 2e-3 + 1e-6 * s) return all[k]; return null; };
  const byS = new Map();
  for (const x of ends) { const k = x.s.toFixed(3); const w = byS.get(k); if (!w) byS.set(k, x); else if (w.f !== x.f && w.tile !== x.tile)
    return { kind: 'per-tile', text: `e${e} s ${x.s.toFixed(2)} is ${flagText(w.f)} in tile ${w.tile} and ${flagText(x.f)} in tile ${x.tile}` }; }
  let dPos = 0, dU = 0, at = null, atU = null;
  for (const x of ends) {
    const c = find(x.s);
    if (!c) return { kind: 'sections', text: `e${e}: a section at s ${x.s.toFixed(2)} (tile ${x.tile}) the replica does not cut` };
    const rf = repFlags(c);
    if ((rf & ~32) !== (x.f & ~32)) return { kind: 'flag', text: `e${e} s ${x.s.toFixed(2)}: the builder ${flagText(x.f & ~32)}, the replica ${flagText(rf & ~32)}` };
    const d = Math.max(Math.hypot(x.L[0] - c.L[0], x.L[1] - c.L[1]), Math.hypot(x.R[0] - c.R[0], x.R[1] - c.R[1]));
    if (d > dPos) { dPos = d; at = x.s; }
    const du = Math.max(Math.abs(x.uL - c.uL), Math.abs(x.uR - c.uR)) * E[e].width;
    if (du > dU) { dU = du; atU = x.s; }
  }
  for (const c of secs) if (!c.collapsed && !ends.some(x => Math.abs(x.s - c.s) < 2e-3 + 1e-6 * c.s))
    return { kind: 'sections', text: `e${e}: a replica section at s ${c.s.toFixed(2)} the builder does not cut` };
  if (dPos > 0.005) return { kind: 'geometry', text: `e${e}: a ribbon corner ${(dPos * 100).toFixed(1)} cm apart at s ${at.toFixed(2)} (the flags agree)` };
  if (dPos > InputTolM) return { kind: 'float', text: `e${e}: a ribbon corner ${(dPos * 1000).toFixed(1)} mm apart at s ${at.toFixed(2)} (the flags agree)` };
  if (dU > InputTolM) return { kind: 'u', text: `e${e}: U ${(dU * 100).toFixed(1)} cm apart (as paint across the width) at s ${atU.toFixed(2)}` };
  const elev = ends.find(x => find(x.s) && (repFlags(find(x.s)) & 32) !== (x.f & 32));
  if (elev) return { kind: 'structure', text: `e${e} s ${elev.s.toFixed(2)}: on structure in ${elev.f & 32 ? 'the builder' : 'the replica'} only (the surface, and so the paint PNG)` };
  return null;
}
const INPUT_WHY = { 'per-tile': "the builder's per-tile clip / squeeze tables", flag: 'a clip / squeeze / collapse flag', sections: 'a section only one of them cuts',
  geometry: 'a ribbon corner more than 5 mm apart (the flags agree)', float: "a ribbon corner 1-5 mm apart (the builder's float32 section math against the replica's doubles)",
  u: "the texture's U", structure: 'on structure (the surface)' };

// the offline gate's same-level test for D1 (linegate.mjs sepNear: the census's plan approximation, no heights)
const XC = 128, xCells = new Map();
S.city.crossings.forEach((c, i) => { const k = gkey(Math.floor(c.x / XC), Math.floor(c.z / XC)); let l = xCells.get(k); if (!l) xCells.set(k, l = []); l.push(i); });
const nbrSet = e => e._nb || (e._nb = new Set([e.index, ...nodeEdges[e.a], ...nodeEdges[e.b]]));
const sameRoad = (e, i) => nbrSet(e).has(i) || (e.name && E[i].name === e.name);
function sepNear(e, o, x, z) {
  const cx = Math.floor(x / XC), cz = Math.floor(z / XC);
  for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) for (const i of xCells.get(gkey(cx + dx, cz + dz)) || []) {
    const c = S.city.crossings[i];
    if (Math.hypot(c.x - x, c.z - z) > 80) continue;
    if ((sameRoad(e, c.over) && sameRoad(o, c.under)) || (sameRoad(e, c.under) && sameRoad(o, c.over))) return true;
  }
  return S.elevatedAt(e, S.projectOn(e, [x, z])) !== S.elevatedAt(o, S.projectOn(o, [x, z]));
}
/// Paint of edge e at (x, z) that one gate found inside another road X (`what`: 'inside eX', a gore quad, a fan) and
/// the other did not: why, from what each was given there.
function d1Why(r) {
  const m = /inside (?:e(\d+)|(a gore quad)|the fan at node (\d+))/.exec(r.what || '');
  if (!m) return null;
  if (m[2]) return ['design', 'D1 inside a gore quad (the replica draws none)'];
  if (m[3]) return ['design', 'D1 inside a junction fan (the replica draws none)'];
  const X = +m[1], e = E[r.edge];
  if (!E[X]) return null;
  const own = tapUnder(r.edge, r.x, r.z), oth = tapUnder(X, r.x, r.z), rep = repUnder(X, r.x, r.z);
  if (r.side === 'mesh') {
    if (rep <= R.CrossM) { const d = inputDiff(X, S.projectOn(E[X], [r.x, r.z]) - 5, S.projectOn(E[X], [r.x, r.z]) + 5);
      r.input = d ? d.text : `e${X}'s replica ribbon ${(rep * 100).toFixed(0)} cm inside here, the builder's ${oth ? (oth.d * 100).toFixed(0) : '-'} cm`;
      return ['input', 'D1: the other road\'s ribbon reaches here in the builder, not in the replica']; }
    if (S.isSeparatedPair(r.edge, X)) { r.input = own && oth ? `the builder's surfaces ${Math.abs(own.y - oth.y).toFixed(2)} m apart` : ''; return ['level', 'D1: the replica separates the two roads (a crossing pair); the builder draws them on one level']; }
    if (sepNear(e, E[X], r.x, r.z)) { r.input = own && oth ? `the builder's surfaces ${Math.abs(own.y - oth.y).toFixed(2)} m apart` : ''; return ['level', "D1: the replica's same-level test (a crossing within 80 m, or structure) separates them; the builder draws them on one level"]; }
    return null;
  }
  // offline-only: the offline found e's paint inside X's replica ribbon
  if (!oth || oth.d <= R.CrossM) { const s0 = S.projectOn(E[X], [r.x, r.z]), d = inputDiff(X, s0 - 5, s0 + 5);
    r.input = d ? d.text : `e${X}'s builder ribbon ${oth ? (oth.d * 100).toFixed(0) : 0} cm inside here, the replica's ${(rep * 100).toFixed(0)} cm`;
    return ['input', 'D1: the other road\'s ribbon reaches here in the replica, not in the builder']; }
  if (own && tapHeights && Math.abs(own.y - oth.y) > R.CrossDyM) { r.input = `the builder's surfaces ${Math.abs(own.y - oth.y).toFixed(2)} m apart`; return ['level', "D1: the builder's two surfaces are more than CrossDyM apart here (the mesh gate's level test); the replica has no heights"]; }
  return null;
}

// ------------------------------------------------------------------ compare: classify
function classify(r) {
  if (r.check === 'B4s') return ['design', 'a seam between two tiles (the mesh gate only)'];
  if (/^FAN/.test(r.lineId) || /(^|\+)FAN(\+|$)/.test(r.cause)) return ['design', "a junction fan's perimeter (the mesh gate only)"];
  if (/BEND-FAN/.test(r.cause)) return ['design', "a bend fan's mouth (the mesh walks the slab's perimeter, the replica bridges it)"];
  const reads = r.side === 'mesh' ? readsOfOffline : readsOfMesh;
  const rd = reads ? reads.get(r.idx) : null;
  r.other = rd || null;
  if (rd && rd.maxE >= 1) return ['agrees', 'the other gate reads 1x or more here on the same line: both fail, the run is cut or keyed differently'];
  // the line's window, along its chain
  const w = winOf(r.check);
  const win = windowOn(r.edge, r.lo - w, r.hi + w);
  if (r.e0 >= 0 && r.e0 !== r.edge) win.push(...windowOn(r.e0, r.s0 - w, r.s0 + w));
  if (r.e1 >= 0 && r.e1 !== r.edge) win.push(...windowOn(r.e1, r.s1 - w, r.s1 + w));
  if (win.some(([, , , via]) => via.includes('bend'))) return ['design', "a bend fan within the check's window (the mesh walks the slab's perimeter, the replica bridges it)"];
  for (const [e] of win) { const rg = ringOf.get(e); if (rg && (rg.small || win.some(([ee, a, b]) => (E[ee].a === rg.cutNode && a <= 0) || (E[ee].b === rg.cutNode && b >= E[ee].length))))
    return ['ring', rg.small ? `a closed ring of ${rg.edges} edges, ${rg.length.toFixed(0)} m: both gates cut it at a joint, each at its own` : `the offline gate cuts the closed ring ${rg.name} (${(rg.length / 1000).toFixed(1)} km) at this joint; the mesh gate walks through it`]; }
  if (r.check === 'D1') { const d = d1Why(r); if (d) return d; }
  if (tapPath) {
    for (const [e, a, b] of win) {
      const d = inputDiff(e, a, b);
      if (d) { r.input = d.text; return ['input', INPUT_WHY[d.kind]]; }
    }
  }
  if (rd && rd.maxE >= 0.9 && r.ratio < 1.1) return ['threshold', 'the same input; the other gate reads 0.9-1x here and this one under 1.1x'];
  if (strandSets.length === 2 && /^B[123]$/.test(r.check)) {
    const a = keptNear(strandSets[0], r.x, r.z, w), b = keptNear(strandSets[1], r.x, r.z, w);
    if (a && b) {
      const onlyM = [...a].filter(k => !b.has(k)), onlyO = [...b].filter(k => !a.has(k));
      if (onlyM.length || onlyO.length) {
        r.input = `kept by the mesh gate only: ${onlyM.slice(0, 4).join(' ') || '-'}; by the offline gate only: ${onlyO.slice(0, 4).join(' ') || '-'}`;
        return ['kept', "the same sections to 1 mm; the collinear drop (CollinearDeg) kept different vertices within the check's window (the builder's float32 positions against the replica's doubles, under 1 mm apart)"];
      }
      r.input = `the same ${a.size} kept vertices within ${w} m`;
      if (r.check === 'B2') {
        // the kink rules run on each gate's own coordinates: do they give each gate's own reading?
        const m = rescoreAt(strandSets[0], r.x, r.z), o = rescoreAt(strandSets[1], r.x, r.z);
        if (m && o && Math.abs(m[0] - m[1]) < 1e-4 && Math.abs(o[0] - o[1]) < 1e-4 && Math.abs(m[0] - o[0]) > 1e-4) {
          r.input += `; lib/kink.mjs on the mesh gate's coordinates reads ${m[1].toFixed(4)} m (it dumped ${m[0]}), on the offline gate's ${o[1].toFixed(4)} m (it dumped ${o[0]})`;
          return ['float', "the same kept vertices under 1 mm apart; the kink rules run on EITHER gate's coordinates reproduce that gate's reading exactly: a discrete step of the rules flips on the sub-millimetre difference (the builder's float32 sections against the replica's doubles)"];
        }
        if (m && o) r.input += `; re-scored mesh ${m[1].toFixed(4)} (dumped ${m[0]}), offline ${o[1].toFixed(4)} (dumped ${o[0]})`;
      }
    }
  }
  if (rd && rd.nE === 0 && rd.nC === 0) return ['UNEXPLAINED', 'the same input; the other gate took no sample here'];
  if (!rd) return ['UNEXPLAINED', 'the same input; no reading from the other gate (an event check, or no reads file)'];
  return ['UNEXPLAINED', 'the same input read differently'];
}

const tally = new Map(), perCheck = new Map();
for (const r of left) {
  const [cls, why] = classify(r);
  r.cls = cls; r.why = why;
  const k = cls + ' | ' + why;
  tally.set(k, (tally.get(k) || 0) + 1);
  const pk = r.check + ' ' + r.side; let p = perCheck.get(pk); if (!p) perCheck.set(pk, p = new Map()); p.set(cls, (p.get(cls) || 0) + 1);
}
const hist = rs => { const h = { '<1.1x': 0, '1.1-1.5x': 0, '1.5-3x': 0, '>=3x': 0 }; for (const r of rs) h[r.ratio < 1.1 ? '<1.1x' : r.ratio < 1.5 ? '1.1-1.5x' : r.ratio < 3 ? '1.5-3x' : '>=3x']++; return h; };
const out = [];
const P = s => out.push(s);
P(`THE TWO GATES, RUN BY RUN: mesh ${M.length} runs, offline ${O.length}; unpartnered ${left.filter(r => r.side === 'mesh').length} / ${left.filter(r => r.side === 'offline').length} (mesh / offline)`);
P(`tap: ${tapTiles} tiles, ${tapSpans} ribbon spans, ${tapGores} gore quads, ${tapFans} fans${tapHeights ? " (with heights)" : ""}; reads: offline at mesh runs ${readsOfOffline ? readsOfOffline.size : 'none'}, mesh at offline runs ${readsOfMesh ? readsOfMesh.size : 'none'}`);
for (const side of ['mesh', 'offline']) if (sanity[side][0])
  P(`  the reads, checked on the partnered runs: ${sanity[side][1]} of ${sanity[side][0]} ${side} runs with a partner read 1x or more in the other gate (${(100 * sanity[side][1] / sanity[side][0]).toFixed(2)}%)`);
P('');
P('per check (mesh-only / offline-only): classes');
const checks = [...new Set(left.map(r => r.check))].sort();
for (const c of checks) for (const side of ['mesh', 'offline']) {
  const rs = left.filter(r => r.check === c && r.side === side); if (!rs.length) continue;
  const p = perCheck.get(c + ' ' + side);
  P(`  ${c.padEnd(4)} ${side.padEnd(8)} ${String(rs.length).padStart(6)}: ${[...p.entries()].sort((a, b) => b[1] - a[1]).map(([k, n]) => `${k} ${n}`).join('; ')}`);
}
P('');
P('by mechanism (all checks):');
for (const [k, n] of [...tally.entries()].sort((a, b) => b[1] - a[1])) {
  const rs = left.filter(r => r.cls + ' | ' + r.why === k), h = hist(rs);
  P(`  ${String(n).padStart(6)}  ${k}   [${Object.entries(h).map(([a, b]) => `${a} ${b}`).join(', ')}]`);
}
const un = left.filter(r => r.cls === 'UNEXPLAINED').sort((a, b) => b.ratio - a.ratio);
P('');
P(`UNEXPLAINED ${un.length}: ${Object.entries(hist(un)).map(([a, b]) => `${a} ${b}`).join(', ')}; by check ${[...new Set(un.map(r => r.check))].map(c => `${c} ${un.filter(r => r.check === c).length}`).join(', ')}`);
for (const r of un.slice(0, 60))
  P(`  ${r.side === 'mesh' ? 'M' : 'O'} ${r.check} e${r.edge} ${r.name} ${r.lineId} s ${r.lo.toFixed(1)}-${r.hi.toFixed(1)} ${r.ratio.toFixed(2)}x ${r.cause} ${r.what} (${r.x.toFixed(1)}, ${r.z.toFixed(1)}) other: ${r.other ? `${r.other.maxE.toFixed(3)}x/${r.other.nE} (class ${r.other.maxC.toFixed(3)}x/${r.other.nC})` : '-'} - ${r.why}`);
P('');
P(`(${((Date.now() - t0) / 1000).toFixed(0)} s)`);
console.log(out.join('\n'));
const dir = argVal('--out');
if (dir) {
  mkdirSync(resolve(dir), { recursive: true });
  writeFileSync(join(resolve(dir), 'gatecmp.txt'), out.join('\n') + '\n');
  writeFileSync(join(resolve(dir), 'gatecmp.json'), JSON.stringify(left.map(r => ({ side: r.side, check: r.check, e: r.edge, name: r.name, line: r.lineId, lo: +r.lo.toFixed(3), hi: +r.hi.toFixed(3),
    ratio: r.ratio, x: r.x, z: r.z, cause: r.cause, what: r.what, cls: r.cls, why: r.why, input: r.input, other: r.other })), null, 0));
}
