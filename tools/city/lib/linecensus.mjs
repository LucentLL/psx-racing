// linecensus.mjs - the 2026-09-28 line-wobble census (scratchpad census.mjs),
// kept inside linecheck.mjs so its headline numbers can be reproduced on any
// graph and any builder model: painted-line p2p wobble per 20 m, lane paint
// displaced, drawn-edge kinks over 2 degrees, taper counts, node line jumps,
// paint inside another ribbon, squeeze wander, edge-line flicker.
//
// These are the CENSUS's measures, with the census's own thresholds (5 / 15 /
// 30 cm p2p, 2 degree kinks). They are reported beside the gate, not gated:
// the gate's checks (linegate.mjs) are the ones measured against V.
//
// A port, not a rewrite: the arithmetic is the census's line for line, so
// `node linecheck.mjs --model asbuilt` reproduces census_asis.json and
// `--model nominalU` census_nominalU.json. Painted lines are at their NOMINAL
// positions here (the painter's layout), as the census measured them.
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { lineInSpan, slat } from './paintiso.mjs';
import { paintLinesNominal, LANE, dot, add, mul, sub, dist } from './linesim.mjs';

const CLASSES = ['motorway', 'motorway_link', 'trunk', 'trunk_link', 'primary', 'primary_link', 'secondary', 'secondary_link',
  'tertiary', 'tertiary_link', 'local', 'local_link'];
const DEG = 180 / Math.PI;
const r2 = v => Math.round(v * 100) / 100, r1 = v => Math.round(v * 10) / 10;
const pct = (arr, q) => { if (!arr.length) return 0; const a = Float64Array.from(arr).sort(); return a[Math.min(a.length - 1, Math.floor(q * (a.length - 1) + 0.5))]; };

/// Run the census over a built sim (linesim.createSim) whose edges already
/// carry `secs` (sectionsOf) - linecheck builds them once for both passes.
export function runCensus(S, { cacheDir = null, log = () => {} } = {}) {
  const { E, T, NODES, nodeEdges } = S;
  const routes = S.city.routes;
  const routeOf = new Map();
  for (const r of routes) for (const ei of r.edges) { const l = routeOf.get(ei) || []; if (!l.includes(r.id)) l.push(r.id); routeOf.set(ei, l); }
  const junctionNode = n => nodeEdges[n].length >= 3;
  const wayTags = new Map();
  if (cacheDir) for (const f of ['ways_all.json', 'streets_core.json']) {
    const p = join(cacheDir, f);
    if (!existsSync(p)) continue;
    for (const el of JSON.parse(readFileSync(p, 'utf8')).elements) if (el.type === 'way' && el.tags) wayTags.set(el.id, el.tags);
  }
  for (const e of E) e.lines = paintLinesNominal(e.profile);

  // ---------------------------------------------------------------- (c) paint lying inside another ribbon
  const TC = 16, triCells = new Map();
  const tris = [];
  const tkey = (cx, cz) => (cx + 8192) * 16384 + (cz + 8192);
  for (const e of E) {
    const secs = e.secs; if (!secs) continue;
    for (let i = 1; i < secs.length; i++) {
      const A = secs[i - 1], B = secs[i];
      if (A.collapsed && B.collapsed) continue;
      for (const [p, q, r] of [[A.L, B.R, B.L], [A.L, A.R, B.R]]) {
        const area = (q[0] - p[0]) * (r[1] - p[1]) - (r[0] - p[0]) * (q[1] - p[1]);
        if (Math.abs(area) < 1e-4) continue;
        const id = tris.length; tris.push([p[0], p[1], q[0], q[1], r[0], r[1], e.index]);
        const x0 = Math.floor(Math.min(p[0], q[0], r[0]) / TC), x1 = Math.floor(Math.max(p[0], q[0], r[0]) / TC);
        const z0 = Math.floor(Math.min(p[1], q[1], r[1]) / TC), z1 = Math.floor(Math.max(p[1], q[1], r[1]) / TC);
        for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) { const k = tkey(cx, cz); let l = triCells.get(k); if (!l) triCells.set(k, l = []); l.push(id); }
      }
    }
  }
  function insideBy(t, x, z) {
    const [ax, az, bx, bz, cx, cz] = t;
    const area = (bx - ax) * (cz - az) - (cx - ax) * (bz - az);
    const sg = area > 0 ? 1 : -1;
    let m = Infinity;
    for (const [px, pz, qx, qz] of [[ax, az, bx, bz], [bx, bz, cx, cz], [cx, cz, ax, az]]) {
      const ex = qx - px, ez = qz - pz, L = Math.hypot(ex, ez);
      const d = sg * (ex * (z - pz) - ez * (x - px)) / L;
      if (d < m) m = d;
    }
    return m;
  }
  function depthInEdge(oi, x, z) {
    const o = E[oi]; const secs = o.secs; if (!secs) return 0;
    const so = S.projectOn(o, [x, z]);
    let k = 1; while (k < secs.length - 1 && secs[k].s < so) k++;
    const A = secs[k - 1], B = secs[k];
    const t = B.s - A.s > 1e-6 ? Math.max(0, Math.min(1, (so - A.s) / (B.s - A.s))) : 0;
    const p = S.pointAt(o, so), tan = S.tangentAt(o, so), r = [-tan[1], tan[0]];
    const lat = (x - p[0]) * r[0] + (z - p[1]) * r[1];
    const latL = A.latL + (B.latL - A.latL) * t, latR = A.latR + (B.latR - A.latR) * t;
    return Math.max(0, Math.min(lat - latL, latR - lat));
  }
  function relation(e, o, x, z) {
    for (const n of [e.a, e.b]) if (n === o.a || n === o.b) {
      if (T.patch[n]) return 'fan arms (shared fan node)';
      if (T.mitre[n]) {
        const thr = (T.throughA[n] === e.index || T.throughB[n] === e.index) && (T.throughA[n] === o.index || T.throughB[n] === o.index);
        return thr ? 'through pair at a mitred node' : 'branch and host at a mitred node';
      }
      return 'arms of one node';
    }
    if (S.isClipPair(e.index, o.index)) return 'branch chain inside host chain';
    const se = S.projectOn(e, [x, z]), so = S.projectOn(o, [x, z]);
    const c = Math.abs(dot(S.tangentAt(e, se), S.tangentAt(o, so)));
    return c > 0.9 ? 'parallel roads (squeeze floor / band)' : 'roads crossing at grade (no shared node)';
  }
  const XC = 128, xCells = new Map();
  for (let i = 0; i < S.city.crossings.length; i++) {
    const c = S.city.crossings[i]; const k = tkey(Math.floor(c.x / XC), Math.floor(c.z / XC));
    let l = xCells.get(k); if (!l) xCells.set(k, l = []); l.push(i);
  }
  const nbrSet = e => { if (!e._nb) { e._nb = new Set([e.index, ...nodeEdges[e.a], ...nodeEdges[e.b]]); } return e._nb; };
  const sameRoad = (e, i) => nbrSet(e).has(i) || (e.name && E[i].name === e.name);
  function sepNear(e, o, x, z) {
    const cx = Math.floor(x / XC), cz = Math.floor(z / XC);
    for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) {
      const l = xCells.get(tkey(cx + dx, cz + dz)); if (!l) continue;
      for (const i of l) {
        const c = S.city.crossings[i];
        if (Math.hypot(c.x - x, c.z - z) > 80) continue;
        if ((sameRoad(e, c.over) && sameRoad(o, c.under)) || (sameRoad(e, c.under) && sameRoad(o, c.over))) return true;
      }
    }
    const se = S.projectOn(e, [x, z]), so = S.projectOn(o, [x, z]);
    return S.elevatedAt(e, se) !== S.elevatedAt(o, so);
  }
  const overlap = {};
  const overlapByEdge = new Map();
  function sampleOverlap(ei, x0, z0, x1, z1, col, kind) {
    const e = E[ei];
    const L = Math.hypot(x1 - x0, z1 - z0);
    if (L < 1e-3) return;
    const n = Math.max(1, Math.round(L / 1.0));
    for (let k = 0; k < n; k++) {
      const t = (k + 0.5) / n, x = x0 + (x1 - x0) * t, z = z0 + (z1 - z0) * t;
      const l = triCells.get(tkey(Math.floor(x / TC), Math.floor(z / TC)));
      if (!l) continue;
      let hit = -1;
      for (const id of l) {
        const tr = tris[id]; const oi = tr[6];
        if (oi === ei) continue;
        if (x < Math.min(tr[0], tr[2], tr[4]) || x > Math.max(tr[0], tr[2], tr[4]) || z < Math.min(tr[1], tr[3], tr[5]) || z > Math.max(tr[1], tr[3], tr[5])) continue;
        if (insideBy(tr, x, z) < 0.05) continue;
        if (S.isSeparatedPair(ei, oi) || sepNear(e, E[oi], x, z)) continue;
        hit = oi; break;
      }
      if (hit < 0) continue;
      const rel = relation(e, E[hit], x, z);
      overlap[rel] = (overlap[rel] || 0) + L / n;
      const key = ei + ':' + hit;
      let rec = overlapByEdge.get(key);
      if (!rec) overlapByEdge.set(key, rec = { e: ei, o: hit, m: 0, x, z, rel, depth: 0 });
      rec.m += L / n;
      const dd = depthInEdge(hit, x, z);
      if (dd > rec.depth) { rec.depth = dd; rec.x = x; rec.z = z; }
    }
  }

  // ---------------------------------------------------------------- helpers
  const BIN = 0.5, HALF = 20;
  function slidingP2P(v) {
    const n = v.length, out = new Float64Array(n).fill(NaN);
    const qMax = new Int32Array(n), qMin = new Int32Array(n);
    let hMax = 0, tMax = 0, hMin = 0, tMin = 0, next = 0;
    for (let k = 0; k < n; k++) {
      const hi = Math.min(n - 1, k + HALF);
      while (next <= hi) {
        if (!Number.isNaN(v[next])) {
          while (tMax > hMax && v[qMax[tMax - 1]] <= v[next]) tMax--; qMax[tMax++] = next;
          while (tMin > hMin && v[qMin[tMin - 1]] >= v[next]) tMin--; qMin[tMin++] = next;
        }
        next++;
      }
      const lo = k - HALF;
      while (hMax < tMax && qMax[hMax] < lo) hMax++;
      while (hMin < tMin && qMin[hMin] < lo) hMin++;
      if (hMax < tMax && !Number.isNaN(v[k])) out[k] = v[qMax[hMax]] - v[qMin[hMin]];
    }
    return out;
  }
  const hwChanges = (A, B) => Math.abs(A.hw - B.hw) > 0.005;
  function turnAt(a, b, c) {
    const ux = b[0] - a[0], uz = b[1] - a[1], vx = c[0] - b[0], vz = c[1] - b[1];
    return Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz);
  }
  function osmTurnAtS(e, s) {
    const [seg, t] = S.segmentAt(e, s);
    const atA = seg > 0 && t < 1e-3, atB = seg + 2 < e.s.length && t > 1 - 1e-3;
    if (!atA && !atB) return 0;
    const v = atA ? seg : seg + 1;
    return turnAt(e.pts[v - 1], e.pts[v], e.pts[v + 1]);
  }
  function nearJunction(e, s) {
    let d = Infinity;
    if (junctionNode(e.a) || T.patch[e.a]) d = Math.min(d, s);
    if (junctionNode(e.b) || T.patch[e.b]) d = Math.min(d, e.length - s);
    return d;
  }

  // ---------------------------------------------------------------- accumulators
  const byClass = {};
  for (const c of CLASSES) byClass[c] = {
    km: 0, kmPainted: 0, wob5: 0, wob15: 0, wob30: 0, wobMax: 0, stepMax: 0, zigMax: 0, disp30: 0, dispMax: 0,
    wobCause: { taper: 0, vertex: 0, crop: 0, mitre: 0, other: 0 },
    edgeVerts: 0, kinks: 0, kinksNearJ: 0, kinkCause: {}, hcpm: [], kinkDeg: [], clustered: 0, roundaboutKinks: 0,
    sqSideKm: 0, sqEdgeLineLostKm: 0, sqIntoLaneKm: 0, sqLaneLineLostKm: 0, sqEdgeWob15Km: 0, sqEdgeWob30Km: 0, sqFlicker: 0,
    clipSideKm: 0, zig5: 0, zig15: 0, drift15: 0,
  };
  const perEdge = new Map();
  const kinkList = [];
  const paintKm = {};
  const folds = [];
  const taperStats = { edges: 0, km: 0, diamonds: 0, withTurnLanes: 0 };
  const routeAgg = {};
  for (const r of routes) routeAgg[r.id] = { name: r.name, km: 0, wob5: 0, wob15: 0, wobMax: 0, zigMax: 0, kinks: 0, kinkMax: 0, nodeJumps20: 0 };

  // ---------------------------------------------------------------- per edge
  for (const e of E) {
    const cls = byClass[e.klass];
    cls.km += e.length / 1000;
    const secs = e.secs;
    if (!secs || secs.length < 2) continue;
    const sMin = secs[0].s, sMax = secs[secs.length - 1].s;
    cls.kmPainted += (sMax - sMin) / 1000;
    if (T.taperA[e.index] > 0 || T.taperB[e.index] > 0) {
      taperStats.edges++;
      taperStats.km += (Math.min(e.length, T.taperA[e.index] + T.taperB[e.index])) / 1000;
      if (T.taperA[e.index] > 0 && T.taperB[e.index] > 0 && T.taperA[e.index] + T.taperB[e.index] > e.length) taperStats.diamonds++;
      const t = wayTags.get(e.wayId) || {};
      if (t['turn:lanes'] || t['turn:lanes:forward'] || t['turn:lanes:backward'] || t['turn:lanes:both_ways']) taperStats.withTurnLanes++;
    }
    const nb = Math.max(1, Math.ceil((sMax - sMin) / BIN));
    const binS = k => sMin + (k + 0.5) * BIN;
    const worstBin = new Float64Array(nb);
    const zigBin = new Float64Array(nb);
    const driftBin = new Float64Array(nb);
    const spanCause = [];
    for (let i = 1; i < secs.length; i++) {
      const A = secs[i - 1], B = secs[i];
      let c = 0;
      const fwd = [B.p[0] - A.p[0], B.p[1] - A.p[1]];
      const fold = (fwd[0] * (B.L[0] - A.L[0]) + fwd[1] * (B.L[1] - A.L[1]) < 0) || (fwd[0] * (B.R[0] - A.R[0]) + fwd[1] * (B.R[1] - A.R[1]) < 0);
      if (fold && !(A.collapsed || B.collapsed)) {
        const d = Math.max(-(fwd[0] * (B.L[0] - A.L[0]) + fwd[1] * (B.L[1] - A.L[1])), -(fwd[0] * (B.R[0] - A.R[0]) + fwd[1] * (B.R[1] - A.R[1]))) / Math.max(1e-6, Math.hypot(fwd[0], fwd[1]));
        folds.push({ edge: e.index, s: (A.s + B.s) / 2, depth: d });
        c = 6;
      } else if (A.collapsed || B.collapsed) c = 5;
      else if (hwChanges(A, B)) c = 1;
      else if (A.kind === 'mitre' || B.kind === 'mitre') c = 4;
      else if (A.kind === 'vertex' || B.kind === 'vertex') c = 2;
      else if (A.sqL || A.sqR || B.sqL || B.sqR || A.clipped || B.clipped) c = 3;
      spanCause.push(c);
    }
    const PRI = [1, 5, 2, 3, 4, 0, 0], causeBin = new Uint8Array(nb);
    for (let i = 1; i < secs.length; i++) {
      const c = spanCause[i - 1], pr = PRI[c];
      const k0 = Math.max(0, Math.floor((secs[i - 1].s - 10 - sMin) / BIN)), k1 = Math.min(nb - 1, Math.ceil((secs[i].s + 10 - sMin) / BIN));
      for (let k = k0; k <= k1; k++) if (pr > PRI[causeBin[k]]) causeBin[k] = c;
    }
    let edgeZig = 0, edgeStep = 0, edgeDisp = 0, edgeZigAt = 0, edgeDrift = 0;
    e.lines.forEach((L) => {
      const lat = new Float64Array(nb).fill(NaN), chord = new Float64Array(nb).fill(NaN);
      const isEdgeLine = L.kind === 'edgeLeft' || L.kind === 'edgeRight';
      for (let i = 1; i < secs.length; i++) {
        const A = secs[i - 1], B = secs[i];
        if (A.collapsed && B.collapsed) continue;
        const wedge = A.collapsed || B.collapsed || spanCause[i - 1] === 6;
        const segs = lineInSpan(A, B, L.u);
        let pA = null, pB = null, pD = null;
        for (const sg of segs) {
          const q0 = slat(A, B, sg[0]), q1 = slat(A, B, sg[1]);
          for (const [pt, q] of [[sg[0], q0], [sg[1], q1]]) {
            if (pt.lab === 'A') pA = q; else if (pt.lab === 'B') pB = q; else if (pt.lab === 'D') pD = q;
          }
          sampleOverlap(e.index, sg[0].P[0], sg[0].P[1], sg[1].P[0], sg[1].P[1], L.col, L.kind);
          if (wedge) continue;
          const lo = Math.min(q0.s, q1.s), hi = Math.max(q0.s, q1.s);
          const k0 = Math.max(0, Math.ceil((lo - sMin) / BIN - 0.5)), k1 = Math.min(nb - 1, Math.floor((hi - sMin) / BIN - 0.5));
          for (let k = k0; k <= k1; k++) {
            const tt = hi - lo > 1e-9 ? (binS(k) - q0.s) / (q1.s - q0.s) : 0;
            if (Number.isNaN(lat[k])) lat[k] = q0.lat + (q1.lat - q0.lat) * tt;
          }
        }
        if (wedge) continue;
        if (pA && pB && B.s - A.s > 1e-6) {
          const k0 = Math.max(0, Math.ceil((A.s - sMin) / BIN - 0.5)), k1 = Math.min(nb - 1, Math.floor((B.s - sMin) / BIN - 0.5));
          for (let k = k0; k <= k1; k++) chord[k] = pA.lat + (pB.lat - pA.lat) * (binS(k) - A.s) / (B.s - A.s);
          if (pD) {
            const tD = (pD.s - A.s) / (B.s - A.s);
            const dev = Math.abs(pD.lat - (pA.lat + (pB.lat - pA.lat) * tD));
            if (dev > edgeZig) { edgeZig = dev; edgeZigAt = pD.s; }
          }
        }
      }
      const sig = new Float64Array(nb).fill(NaN);
      { let c = 0; for (let k = 0; k < nb; k++) if (!Number.isNaN(lat[k])) c++; const kk = isEdgeLine ? 'edge' : L.kind; paintKm[kk] = (paintKm[kk] || 0) + c * BIN / 1000; }
      for (let k = 0; k < nb; k++) {
        const z = lat[k] - chord[k];
        if (!Number.isNaN(z) && Math.abs(z) > zigBin[k]) zigBin[k] = Math.abs(z);
        sig[k] = isEdgeLine ? z : lat[k];
      }
      const p2p = slidingP2P(sig);
      for (let k = 0; k < nb; k++) if (p2p[k] > worstBin[k]) worstBin[k] = p2p[k];
      if (!isEdgeLine) {
        const dr = slidingP2P(chord);
        for (let k = 0; k < nb; k++) if (dr[k] > driftBin[k]) driftBin[k] = dr[k];
        let c = 0;
        for (let k = 0; k < nb; k++) {
          if (Number.isNaN(lat[k])) continue;
          const d = Math.abs(lat[k] - L.latNom);
          if (d > edgeDisp) edgeDisp = d;
          if (d > 0.3) c++;
        }
        cls.disp30 += c * BIN / 1000;
      }
      for (let k = 0; k + 2 < nb; k++) {
        if (Number.isNaN(sig[k]) || Number.isNaN(sig[k + 2])) continue;
        const st = Math.abs(sig[k + 2] - sig[k]);
        if (st > edgeStep) edgeStep = st;
      }
    });
    let w5 = 0, w15 = 0, w30 = 0, wMax = 0, wAt = 0, z5 = 0, z15 = 0, d15 = 0;
    for (let k = 0; k < nb; k++) {
      const w = worstBin[k];
      if (w >= 0.05) { w5++; const c = causeBin[k]; const key = c === 1 ? 'taper' : c === 2 ? 'vertex' : c === 3 ? 'crop' : c === 4 ? 'mitre' : 'other'; cls.wobCause[key] += BIN / 1000; }
      if (w >= 0.15) w15++;
      if (w >= 0.30) w30++;
      if (zigBin[k] >= 0.05) z5++;
      if (zigBin[k] >= 0.15) z15++;
      if (driftBin[k] >= 0.15) d15++;
      if (w > wMax) { wMax = w; wAt = binS(k); }
      if (driftBin[k] > edgeDrift) edgeDrift = driftBin[k];
    }
    cls.wob5 += w5 * BIN / 1000; cls.wob15 += w15 * BIN / 1000; cls.wob30 += w30 * BIN / 1000;
    cls.zig5 += z5 * BIN / 1000; cls.zig15 += z15 * BIN / 1000; cls.drift15 += d15 * BIN / 1000;
    cls.wobMax = Math.max(cls.wobMax, wMax); cls.stepMax = Math.max(cls.stepMax, edgeStep); cls.zigMax = Math.max(cls.zigMax, edgeZig);
    cls.dispMax = Math.max(cls.dispMax, edgeDisp);
    perEdge.set(e.index, { wMax, wAt, w5: w5 * BIN, w15: w15 * BIN, w30: w30 * BIN, zig: edgeZig, zigAt: edgeZigAt, step: edgeStep, disp: edgeDisp, drift: edgeDrift });
    for (const rid of routeOf.get(e.index) || []) {
      const ra = routeAgg[rid]; ra.km += e.length / 1000; ra.wob5 += w5 * BIN / 1000; ra.wob15 += w15 * BIN / 1000; ra.wobMax = Math.max(ra.wobMax, wMax);
      ra.zigMax = Math.max(ra.zigMax, edgeZig);
    }

    // ---- (b) the drawn edges
    {
      const n = secs.length;
      const side2 = { '-1': null, '1': null };
      for (const side of [-1, 1]) {
        const P = secs.map(c => side < 0 ? c.L : c.R);
        const th = new Float64Array(n).fill(NaN), la = new Float64Array(n), lb = new Float64Array(n), ia = new Int32Array(n), ib = new Int32Array(n);
        for (let k = 1; k + 1 < n; k++) {
          let jm = false;
          for (let q = k - 1; q <= k + 1; q++) { const c = secs[q]; if (c.collapsed || (c.clippedIn && c.innerSide === side)) jm = true; }
          if (jm) continue;
          let a = k - 1; while (a > 0 && dist(P[a], P[k]) < 0.01) a--;
          let b = k + 1; while (b < n - 1 && dist(P[b], P[k]) < 0.01) b++;
          const A_ = dist(P[a], P[k]), B_ = dist(P[b], P[k]);
          if (A_ < 0.01 || B_ < 0.01) continue;
          th[k] = turnAt(P[a], P[k], P[b]) * DEG; la[k] = A_; lb[k] = B_; ia[k] = a; ib[k] = b;
        }
        side2[side] = { P, th, la, lb, ia, ib };
      }
      let lastCluster = -1e9;
      for (let k = 1; k + 1 < n; k++) {
        let best = null;
        for (const side of [-1, 1]) {
          const d = side2[side]; if (Number.isNaN(d.th[k])) continue;
          let W = 0, M = 0; for (let j = 1; j + 1 < n; j++) if (!Number.isNaN(d.th[j]) && Math.abs(secs[j].s - secs[k].s) <= 2.5) { W += d.th[j]; M = Math.max(M, Math.abs(d.th[j])); }
          const rate = Math.max(Math.abs(d.th[k]), Math.abs(W)) / 5;
          if (!best || Math.abs(d.th[k]) > Math.abs(best.th)) best = { side, th: d.th[k], la: d.la[k], lb: d.lb[k], a: d.ia[k], b: d.ib[k], rate, W, M };
        }
        if (!best) continue;
        cls.edgeVerts++;
        if (Math.abs(best.th) > 0.05 || Math.abs(best.W) > 0.05) cls.hcpm.push(best.rate);
        const ad = Math.abs(best.th);
        if (ad <= 2) { if (best.M <= 2 && Math.abs(best.W) > 2 && !(lastCluster >= secs[k].s - 5)) { cls.clustered++; lastCluster = secs[k].s; } continue; }
        if (e.roundabout) { cls.roundaboutKinks++; continue; }
        const ct = osmTurnAtS(e, secs[k].s) * DEG;
        const { a, b, side } = best;
        const tap = hwChanges(secs[a], secs[k]) || hwChanges(secs[k], secs[b]);
        const sqz = [a, k, b].some(q => side < 0 ? secs[q].sqL : secs[q].sqR);
        let cause;
        if (Math.abs(ct) >= 1) cause = 'polyline vertex';
        else if (tap) cause = 'taper end';
        else if (sqz) cause = 'squeeze';
        else if ([a, k, b].some(q => secs[q].clipped)) cause = 'clip (outer side)';
        else cause = 'other';
        const nj = nearJunction(e, secs[k].s);
        cls.kinks++; if (nj <= 25) cls.kinksNearJ++;
        cls.kinkCause[cause] = (cls.kinkCause[cause] || 0) + 1;
        cls.kinkDeg.push(ad);
        kinkList.push({ edge: e.index, s: secs[k].s, deg: ad, cause, nj });
      }
    }

    // ---- (d) squeeze / crop
    for (let i = 1; i < secs.length; i++) {
      const A = secs[i - 1], B = secs[i], ds = (B.s - A.s) / 1000;
      for (const side of [-1, 1]) {
        const sqA = side < 0 ? A.sqL : A.sqR, sqB = side < 0 ? B.sqL : B.sqR;
        const clA = A.clippedIn && A.innerSide === side, clB = B.clippedIn && B.innerSide === side;
        if (clA || clB) cls.clipSideKm += ds;
        if (!sqA && !sqB) continue;
        cls.sqSideKm += ds;
        const sh = side < 0 ? e.profile.shr : e.profile.shl;
        const crop = c => c.hw - Math.abs(side < 0 ? c.latL : c.latR);
        const cr = Math.max(crop(A), crop(B));
        if (cr > sh + 0.12) cls.sqEdgeLineLostKm += ds;
        if (cr > sh + 0.5) cls.sqIntoLaneKm += ds;
        if (cr > sh + LANE + 0.06 && e.profile.lanes > 1) cls.sqLaneLineLostKm += ds;
      }
    }
    for (const side of [-1, 1]) {
      const vals = new Float64Array(nb).fill(NaN);
      let any = false;
      for (let i = 1; i < secs.length; i++) {
        const A = secs[i - 1], B = secs[i];
        const sq = side < 0 ? (A.sqL || B.sqL) : (A.sqR || B.sqR);
        if (!sq || hwChanges(A, B)) continue;
        any = true;
        const la = Math.abs(side < 0 ? A.latL : A.latR), lb = Math.abs(side < 0 ? B.latL : B.latR);
        const k0 = Math.max(0, Math.floor((A.s - sMin) / BIN)), k1 = Math.min(nb - 1, Math.floor((B.s - sMin) / BIN));
        for (let k = k0; k <= k1; k++) { const tt = (binS(k) - A.s) / Math.max(1e-6, B.s - A.s); if (tt >= 0 && tt <= 1) vals[k] = la + (lb - la) * tt; }
      }
      if (!any) continue;
      const p2p = slidingP2P(vals);
      let c15 = 0, c30 = 0;
      for (let k = 0; k < nb; k++) { if (p2p[k] >= 0.15) c15++; if (p2p[k] >= 0.30) c30++; }
      cls.sqEdgeWob15Km += c15 * BIN / 1000; cls.sqEdgeWob30Km += c30 * BIN / 1000;
    }
    for (const L of e.lines) {
      if (L.kind !== 'edgeLeft' && L.kind !== 'edgeRight') continue;
      let prev = null;
      for (const c of secs) {
        if (c.collapsed) { prev = null; continue; }
        const present = L.u >= Math.min(c.uL, c.uR) && L.u <= Math.max(c.uL, c.uR);
        const sq = L.kind === 'edgeLeft' ? c.sqR : c.sqL;
        if (prev !== null && present !== prev && (sq || prev === false)) cls.sqFlicker++;
        prev = present;
      }
    }
  }
  log('census: edges done');

  // ---------------------------------------------------------------- mitred nodes: node kinks, line jumps
  const MATCH = LANE / 2;
  const nodeStats = { mitred: 0, jump5: 0, jump20: 0, jump50: 0, jumpMax: 0, colourFlip: 0, linesEnding: 0, nodesWithEnds: 0, laneEndsMidLane: 0, nodeKinks: 0, hist: {} };
  let bendFans = 0;
  function nodeEndSections(e, n) {
    const secs = e.secs; if (!secs || secs.length < 2) return null;
    if (e.a === n && secs[0].s < 1e-6) return [secs[0], secs[1]];
    if (e.b === n && Math.abs(secs[secs.length - 1].s - e.length) < 1e-6) return [secs[secs.length - 1], secs[secs.length - 2]];
    return null;
  }
  for (let n = 0; n < NODES.length; n++) {
    if (T.patch[n] && nodeEdges[n].length === 2 && nodeEdges[n][0] !== nodeEdges[n][1]) bendFans++;
    if (!T.mitre[n]) continue;
    const e1 = E[T.throughA[n]], e2 = E[T.throughB[n]];
    if (!e1 || !e2 || e1 === e2) continue;
    const s1 = nodeEndSections(e1, n), s2 = nodeEndSections(e2, n);
    if (!s1 || !s2) continue;
    nodeStats.mitred++;
    const [c1, nb1] = s1, [c2, nb2] = s2;
    const hi = e1.cls >= e2.cls ? e1 : e2;
    const cls = byClass[hi.klass];
    {
      let best = null;
      for (const side of [-1, 1]) {
        const P1 = side < 0 ? c1.L : c1.R, Q1 = side < 0 ? nb1.L : nb1.R;
        const P2 = dist(c2.L, P1) < dist(c2.R, P1) ? c2.L : c2.R;
        const Q2 = P2 === c2.L ? nb2.L : nb2.R;
        if (c1.collapsed || c2.collapsed || dist(P1, P2) > 0.05) continue;
        const la = dist(Q1, P1), lb = dist(Q2, P2);
        if (la < 0.01 || lb < 0.01) continue;
        const th = turnAt(Q1, P1, Q2) * DEG;
        if (!best || Math.abs(th) > Math.abs(best.th)) best = { side, th, la, lb };
      }
      if (best) {
        const ad = Math.abs(best.th);
        cls.edgeVerts++;
        if (ad > 0.05) cls.hcpm.push(ad / 5);
        if (ad > 2) {
          const ct = Math.acos(Math.max(-1, Math.min(1, -dot(S.outDir(e1, n), S.outDir(e2, n))))) * DEG;
          const cause = ct >= 1 ? 'mitred node (way split)' : (Math.abs(c1.hw - nb1.hw) > 0.005 || Math.abs(c2.hw - nb2.hw) > 0.005) ? 'taper end' : 'other';
          cls.kinks++; nodeStats.nodeKinks++;
          cls.kinkCause[cause] = (cls.kinkCause[cause] || 0) + 1;
          cls.kinkDeg.push(ad);
          kinkList.push({ edge: hi.index, s: hi === e1 ? c1.s : c2.s, deg: ad, cause, nj: 0, node: n });
        }
      }
    }
    const r1v = c1.right, p1 = c1.p;
    const linesAt = (e, c) => e.lines.filter(L => L.u >= Math.min(c.uL, c.uR) - 1e-9 && L.u <= Math.max(c.uL, c.uR) + 1e-9)
      .map(L => { const lat = (0.5 - L.u) * 2 * c.hw; const P = add(c.p, mul(c.right, lat)); return { L, x: dot(sub(P, p1), r1v) }; });
    const A1 = linesAt(e1, c1), A2 = linesAt(e2, c2);
    let maxJ = 0, flips = 0, ends = 0, laneMid = 0;
    {
      const S1 = [...A1].sort((p, q) => p.x - q.x), S2 = [...A2].sort((p, q) => p.x - q.x);
      const n1 = S1.length, n2 = S2.length, G = MATCH / 2;
      const dp = Array.from({ length: n1 + 1 }, () => new Float64Array(n2 + 1));
      const ch = Array.from({ length: n1 + 1 }, () => new Int8Array(n2 + 1));
      for (let i = 1; i <= n1; i++) { dp[i][0] = i * G; ch[i][0] = 2; }
      for (let j = 1; j <= n2; j++) { dp[0][j] = j * G; ch[0][j] = 3; }
      for (let i = 1; i <= n1; i++) for (let j = 1; j <= n2; j++) {
        const take = dp[i - 1][j - 1] + Math.abs(S1[i - 1].x - S2[j - 1].x), up = dp[i - 1][j] + G, left = dp[i][j - 1] + G;
        if (take <= up && take <= left) { dp[i][j] = take; ch[i][j] = 1; } else if (up <= left) { dp[i][j] = up; ch[i][j] = 2; } else { dp[i][j] = left; ch[i][j] = 3; }
      }
      const matched = new Set(), matched1 = new Set();
      for (let i = n1, j = n2; i > 0 || j > 0;) {
        const c = ch[i][j];
        if (c === 1) { const d = Math.abs(S1[i - 1].x - S2[j - 1].x); maxJ = Math.max(maxJ, d); if (S1[i - 1].L.col !== S2[j - 1].L.col && d < 0.3) flips++; matched.add(j - 1); matched1.add(i - 1); i--; j--; }
        else if (c === 2) i--; else j--;
      }
      for (let i = 0; i < n1; i++) if (!matched1.has(i)) { ends++; if (S1[i].L.kind === 'lane' || S1[i].L.kind === 'centre') laneMid++; }
      for (let j = 0; j < n2; j++) if (!matched.has(j)) { ends++; if (S2[j].L.kind === 'lane' || S2[j].L.kind === 'centre') laneMid++; }
    }
    const b = maxJ <= 0.05 ? '0-5cm' : maxJ <= 0.2 ? '5-20cm' : maxJ <= 0.5 ? '20-50cm' : maxJ <= 1 ? '50-100cm' : maxJ <= 1.83 ? '1-1.83m' : '>1.83m';
    nodeStats.hist[b] = (nodeStats.hist[b] || 0) + 1;
    if (maxJ > 0.05) nodeStats.jump5++; if (maxJ > 0.2) nodeStats.jump20++; if (maxJ > 0.5) nodeStats.jump50++;
    nodeStats.jumpMax = Math.max(nodeStats.jumpMax, maxJ);
    nodeStats.colourFlip += flips; nodeStats.linesEnding += ends; if (ends) nodeStats.nodesWithEnds++; nodeStats.laneEndsMidLane += laneMid;
    for (const rid of new Set([...(routeOf.get(e1.index) || []), ...(routeOf.get(e2.index) || [])])) if (maxJ > 0.2) routeAgg[rid].nodeJumps20++;
  }
  for (const k of kinkList) for (const rid of routeOf.get(k.edge) || []) { routeAgg[rid].kinks++; routeAgg[rid].kinkMax = Math.max(routeAgg[rid].kinkMax, k.deg); }
  log('census: nodes done');

  // ---------------------------------------------------------------- summary
  const tot = {};
  const summaryByClass = {};
  for (const c of CLASSES) {
    const b = byClass[c];
    const o = { km: r1(b.km), kmPainted: r1(b.kmPainted),
      zigzag_km_ge5cm: r2(b.zig5), zigzag_km_ge15cm: r2(b.zig15), drift_km_ge15cm: r2(b.drift15),
      wobble_km_ge5cm: r2(b.wob5), wobble_km_ge15cm: r2(b.wob15), wobble_km_ge30cm: r2(b.wob30), wobble_max_m: r2(b.wobMax), step_per_m_max_m: r2(b.stepMax), zigzag_max_m: r2(b.zigMax),
      displaced_km_gt30cm: r2(b.disp30), displaced_max_m: r2(b.dispMax),
      edge_vertices: b.edgeVerts, kinks_gt2deg: b.kinks, kinks_within_25m_of_junction: b.kinksNearJ,
      kinks_ge5deg: b.kinkDeg.filter(d => d >= 5).length, kinks_ge10deg: b.kinkDeg.filter(d => d >= 10).length, kinks_ge25deg: b.kinkDeg.filter(d => d >= 25).length, kink_deg_max: r1(Math.max(0, ...b.kinkDeg)),
      clustered_kinks: b.clustered, roundabout_kinks: b.roundaboutKinks,
      squeezed_side_km: r2(b.sqSideKm), squeeze_edge_line_lost_km: r2(b.sqEdgeLineLostKm), squeeze_into_lane_km: r2(b.sqIntoLaneKm), squeeze_lane_line_lost_km: r2(b.sqLaneLineLostKm),
      squeeze_edge_wobble_km_ge15: r2(b.sqEdgeWob15Km), squeeze_edge_wobble_km_ge30: r2(b.sqEdgeWob30Km), edge_line_flicker: b.sqFlicker, clipped_side_km: r2(b.clipSideKm),
      wobble_cause_km: Object.fromEntries(Object.entries(b.wobCause).map(([k, v]) => [k, r2(v)])), kink_causes: b.kinkCause };
    summaryByClass[c] = o;
    for (const [k, v] of Object.entries(o)) { if (typeof v !== 'number') continue; if (/max/.test(k)) tot[k] = Math.max(tot[k] || 0, v); else tot[k] = (tot[k] || 0) + v; }
  }
  tot.kinks_per_km = tot.kinks_gt2deg / tot.kmPainted;
  { const all = CLASSES.flatMap(c => byClass[c].hcpm); tot.heading_deg_per_m_p50 = pct(all, 0.5); tot.heading_deg_per_m_p99 = pct(all, 0.99); }
  const causeTot = {};
  for (const k of kinkList) causeTot[k.cause] = (causeTot[k.cause] || 0) + 1;
  const wobCause = {};
  for (const c of CLASSES) for (const [k, v] of Object.entries(byClass[c].wobCause)) wobCause[k] = (wobCause[k] || 0) + v;
  return {
    sections: E.reduce((a, e) => a + (e.secs ? e.secs.length : 0), 0),
    totals: Object.fromEntries(Object.entries(tot).map(([k, v]) => [k, r2(v)])),
    byClass: summaryByClass,
    wobbleCauseKm: Object.fromEntries(Object.entries(wobCause).map(([k, v]) => [k, r2(v)])),
    taperStats: { ...taperStats, km: r2(taperStats.km) },
    kinkCount: kinkList.length, kinkCauses: causeTot,
    kinkGe5: kinkList.filter(k => k.deg >= 5).length, kinkGe10: kinkList.filter(k => k.deg >= 10).length, kinkGe25: kinkList.filter(k => k.deg >= 25).length,
    kinkNearJ: kinkList.filter(k => k.nj <= 25).length,
    nodeStats, bendFans,
    overlapM: Object.fromEntries(Object.entries(overlap).map(([k, v]) => [k, r1(v)])),
    overlapKm: r2(Object.values(overlap).reduce((a, b) => a + b, 0) / 1000),
    folds: { count: folds.length, gt10cm: folds.filter(f => f.depth > 0.1).length, gt50cm: folds.filter(f => f.depth > 0.5).length, max: r2(Math.max(0, ...folds.map(f => f.depth))) },
    paintKm: Object.fromEntries(Object.entries(paintKm).map(([k, v]) => [k, r1(v)])),
    routes: Object.fromEntries(Object.entries(routeAgg).map(([k, v]) => [k, { ...v, km: r1(v.km), wob5: r2(v.wob5), wob15: r2(v.wob15), wobMax: r2(v.wobMax), zigMax: r2(v.zigMax), kinkMax: r1(v.kinkMax) }])),
    creek: Object.fromEntries([12887, 549, 14582].map(i => [`e${i}`, perEdge.get(i) ? Object.fromEntries(Object.entries(perEdge.get(i)).map(([k, v]) => [k, Math.round(v * 1e4) / 1e4])) : null])),
  };
}
