// export_charlotte.mjs — bake Racing-Game-2's Charlotte into charlotte_city.json for Unity.
//
// Sources (read-only):
//   RG2/fixtures/osm/charlotte_rows.json   roads: 3,076 rows, props, 2,389 intersections (OSM, ODbL)
//   RG2/src/config/world/baselineWater.ts  water: 30 creeks + 2 lake polygons (hand-traced frame)
//   RG2/src/config/world/baselineRoads.ts  legacy I-485, used ONLY to co-register the water
//
// The two scale currencies (RG2 convention, kept):
//   layout  : 1 tile = 17.212235 m of Charlotte  (RG2 ran this ÷6; Unity runs it 1:1)
//   section : 1 lane = 3.6576 m (12 ft)          (never compressed anywhere)
//
// Everything here is TOPOLOGY and PLAN geometry. Elevation is solved in Unity, in one
// place, so the carve and the deck can never disagree (the circuits' bridge lesson).
//
// Output: Assets/PSXRacing/Resources/charlotte_city.json  (+ debug SVG in tools/city/)
// Run:    node tools/city/export_charlotte.mjs

import { readFileSync, writeFileSync, mkdirSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const RG2 = 'C:/Users/mcgee/code/Racing-Game-2';

const MPT_LAYOUT = 17.212235294117647; // metres of Charlotte per tile (layout)
const LANE_M = 3.6576;                 // metres per lane (section, true scale)
const LANE_T = 1.275;                  // the same lane in tiles (section currency)
const CENTER = 1250;                   // grid centre tile; world origin
const WELD_T = 0.12;                   // endpoint weld radius, tiles (~2 m)
const TSNAP_T = 0.30;                  // dead-end -> mid-edge snap radius, tiles (~5 m)
const XDEDUP_T = 5.0;                  // crossings of one pair closer than this merge
const BANK_M = 6.0;                    // dry bank either side of water under a deck

// ---------------------------------------------------------------- load roads

const fx = JSON.parse(readFileSync(`${RG2}/fixtures/osm/charlotte_rows.json`, 'utf8'));
const rows = fx.rows, props = fx.props, isectRows = fx.intersections;
console.log(`rows ${rows.length}  props ${props.length}  isects ${isectRows.length}`);

const CLS_RANK = { motorway: 5, trunk: 4, primary: 3, secondary: 2, tertiary: 1 };

function classOf(p) {
  const c = p.class || 'tertiary';
  const link = c.endsWith('_link');
  return { base: link ? c.slice(0, -5) : c, link };
}

// RG2's lane ladder (crossingGeom.ts laneStandardizedWidth), resolved to metres.
// w is a CLASS INDEX, not a width; I-485 is keyed by name in the source, baked out here.
function profileFor(name, w) {
  let lps, medFrac = 0, divided = false;
  if (name === 'I-485') { lps = 3; medFrac = 0.25; divided = true; }
  else if (w === 11) { lps = 3; medFrac = 0.22; divided = true; }
  else if (w === 10) { lps = 3; medFrac = 0.25; divided = true; }
  else if (w >= 12) { lps = 4; medFrac = 0.02; divided = true; }
  else if (w >= 8) { lps = 3; }
  else if (w >= 6) { lps = 2; }
  else { lps = 1; }
  const laneCount = (w === 2) ? lps : lps * 2;
  const carriage = laneCount * LANE_M;
  const median = medFrac > 0 ? carriage * medFrac : 0;
  const shoulder = divided ? 0.5 * LANE_M : 0;
  // grass median on the w=10/I-485 profile, asphalt on w=11 and the w>=12 slab
  const medGrass = divided && medFrac >= 0.24;
  return { lanes: laneCount, widthM: carriage + median + 2 * shoulder, medianM: median, divided, medGrass };
}

// ------------------------------------------------------- parse the TS arrays

function tsArray(file, exportName) {
  const src = readFileSync(file, 'utf8');
  const at = src.indexOf(exportName);
  if (at < 0) throw new Error(`${exportName} not in ${file}`);
  const eq = src.indexOf('=', at); // the type annotation carries [] of its own
  const open = src.indexOf('[', eq);
  let depth = 0, i = open;
  for (; i < src.length; i++) {
    if (src[i] === '[') depth++;
    else if (src[i] === ']') { depth--; if (depth === 0) break; }
  }
  return new Function(`return ${src.slice(open, i + 1)};`)();
}

const RIVERS = tsArray(`${RG2}/src/config/world/baselineWater.ts`, 'BASELINE_RIVERS');
const LAKES = tsArray(`${RG2}/src/config/world/baselineWater.ts`, 'BASELINE_LAKES');
const LEGACY = tsArray(`${RG2}/src/config/world/baselineRoads.ts`, 'BASELINE_ROADS');
console.log(`water: ${RIVERS.length} rivers, ${LAKES.length} lakes; legacy rows ${LEGACY.length}`);

// -------------------------------------- co-register water: legacy -> OSM frame
// The one shape both datasets share is the I-485 loop. Fit a similarity
// (scale, rotation, translation) legacy->OSM by ICP against the OSM loop.

function rowPts(r, header) {
  const p = [];
  for (let i = header; i < r.length; i += 2) p.push([r[i], r[i + 1]]);
  return p;
}
function polyResample(pts, step) {
  const out = [pts[0].slice()];
  let carry = 0;
  for (let i = 1; i < pts.length; i++) {
    let [ax, ay] = pts[i - 1], [bx, by] = pts[i];
    let seg = Math.hypot(bx - ax, by - ay);
    let t = step - carry;
    while (t < seg) {
      out.push([ax + (bx - ax) * t / seg, ay + (by - ay) * t / seg]);
      t += step;
    }
    carry = (seg - (t - step)) % step;
  }
  return out;
}
function nearestOnPoly(pts, x, y) {
  let best = Infinity, bx = 0, by = 0;
  for (let i = 1; i < pts.length; i++) {
    const [ax, ay] = pts[i - 1], [cx, cy] = pts[i];
    const dx = cx - ax, dy = cy - ay;
    const L2 = dx * dx + dy * dy;
    let t = L2 > 0 ? ((x - ax) * dx + (y - ay) * dy) / L2 : 0;
    t = Math.max(0, Math.min(1, t));
    const px = ax + dx * t, py = ay + dy * t;
    const d = (x - px) * (x - px) + (y - py) * (y - py);
    if (d < best) { best = d; bx = px; by = py; }
  }
  return { d: Math.sqrt(best), x: bx, y: by };
}
function umeyama(src, dst) { // 2D similarity src->dst, uniform scale
  const n = src.length;
  let mx = 0, my = 0, ux = 0, uy = 0;
  for (let i = 0; i < n; i++) { mx += src[i][0]; my += src[i][1]; ux += dst[i][0]; uy += dst[i][1]; }
  mx /= n; my /= n; ux /= n; uy /= n;
  let sxx = 0, sxy = 0, syx = 0, syy = 0, varS = 0;
  for (let i = 0; i < n; i++) {
    const ax = src[i][0] - mx, ay = src[i][1] - my, bx = dst[i][0] - ux, by = dst[i][1] - uy;
    sxx += ax * bx; sxy += ax * by; syx += ay * bx; syy += ay * by;
    varS += ax * ax + ay * ay;
  }
  // closed form for 2D: rotation from the cross/dot sums (proper rotation, no reflection)
  const dot = sxx + syy, cross = sxy - syx;
  const th = Math.atan2(cross, dot);
  const c = Math.cos(th), s = Math.sin(th);
  const scale = (dot * c + cross * s) / varS;
  return { s: scale, c, sn: s, tx: ux - scale * (c * mx - s * my), ty: uy - scale * (s * mx + c * my) };
}
const applySim = (T, x, y) => [T.s * (T.c * x - T.sn * y) + T.tx, T.s * (T.sn * x + T.c * y) + T.ty];

const osm485 = rowPts(rows.find(r => r[2] === 'I-485'), 4);
const leg485row = LEGACY.find(r => r[2] === 'I-485');
if (!leg485row) throw new Error('legacy I-485 missing');
const leg485 = polyResample(rowPts(leg485row, 4), 8);

let T = { s: 1, c: 1, sn: 0, tx: 0, ty: 0 };
{ // init: centroid + RMS-radius match
  const cen = pts => pts.reduce((a, p) => [a[0] + p[0] / pts.length, a[1] + p[1] / pts.length], [0, 0]);
  const rms = (pts, c) => Math.sqrt(pts.reduce((a, p) => a + (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2, 0) / pts.length);
  const cl = cen(leg485), co = cen(osm485);
  const s0 = rms(osm485, co) / rms(leg485, cl);
  T = { s: s0, c: 1, sn: 0, tx: co[0] - s0 * cl[0], ty: co[1] - s0 * cl[1] };
}
let fitResid = Infinity;
for (let it = 0; it < 12; it++) {
  const src = [], dst = [];
  let sum = 0;
  for (const p of leg485) {
    const [x, y] = applySim(T, p[0], p[1]);
    const nb = nearestOnPoly(osm485, x, y);
    src.push(p); dst.push([nb.x, nb.y]); sum += nb.d;
  }
  fitResid = sum / leg485.length;
  T = umeyama(src, dst);
}
console.log(`water fit: scale ${T.s.toFixed(4)} rot ${(Math.atan2(T.sn, T.c) * 180 / Math.PI).toFixed(3)}deg mean resid ${fitResid.toFixed(2)} tiles (${(fitResid * MPT_LAYOUT).toFixed(0)} m)`);
if (fitResid > 6) throw new Error('water co-registration failed - residual too large');

// ------------------------------------------------------------ build the graph
// Nodes = welded row endpoints + T-snap points. Rows are then cut at every
// node event into final edges. All in tile space until emit.

const nodes = [];            // [x, y]
const nodeHash = new Map();  // cell -> node ids
const cellOf = (x, y, s) => `${Math.round(x / s)},${Math.round(y / s)}`;
function findNode(x, y, r) {
  let best = -1, bd = r * r;
  const cs = 1.0;
  const cx = Math.round(x / cs), cy = Math.round(y / cs);
  for (let ix = cx - 1; ix <= cx + 1; ix++) for (let iy = cy - 1; iy <= cy + 1; iy++) {
    const ids = nodeHash.get(`${ix},${iy}`);
    if (!ids) continue;
    for (const id of ids) {
      const d = (nodes[id][0] - x) ** 2 + (nodes[id][1] - y) ** 2;
      if (d < bd) { bd = d; best = id; }
    }
  }
  return best;
}
function addNode(x, y) {
  const id = nodes.length;
  nodes.push([x, y]);
  const k = cellOf(x, y, 1.0);
  if (!nodeHash.has(k)) nodeHash.set(k, []);
  nodeHash.get(k).push(id);
  return id;
}
const nodeAt = (x, y, r) => { const f = findNode(x, y, r); return f >= 0 ? f : addNode(x, y); };

const roads = rows.map((r, i) => {
  const p = props[i] || {};
  const { base, link } = classOf(p);
  const pts = rowPts(r, 4);
  return {
    i, name: r[2] || '', w: r[0], z: r[3] | 0, pts,
    base, link, rank: CLS_RANK[base] || 1,
    oneway: !!p.oneway, divided: !!p.divided, deck: !!p.deck,
    lanesProp: p.lanes || 0, splits: [],
  };
});

// endpoints -> nodes
for (const rd of roads) {
  rd.aNode = nodeAt(rd.pts[0][0], rd.pts[0][1], WELD_T);
  rd.bNode = nodeAt(rd.pts[rd.pts.length - 1][0], rd.pts[rd.pts.length - 1][1], WELD_T);
}
const degree = new Map();
for (const rd of roads) for (const n of [rd.aNode, rd.bNode]) degree.set(n, (degree.get(n) || 0) + 1);

// segment spatial hash for T-snapping and crossings
const SEGCELL = 4.0;
const segHash = new Map();
function hashSegs() {
  segHash.clear();
  for (const rd of roads) {
    for (let i = 1; i < rd.pts.length; i++) {
      const [ax, ay] = rd.pts[i - 1], [bx, by] = rd.pts[i];
      const x0 = Math.floor(Math.min(ax, bx) / SEGCELL), x1 = Math.floor(Math.max(ax, bx) / SEGCELL);
      const y0 = Math.floor(Math.min(ay, by) / SEGCELL), y1 = Math.floor(Math.max(ay, by) / SEGCELL);
    for (let cx = x0; cx <= x1; cx++) for (let cy = y0; cy <= y1; cy++) {
        const k = `${cx},${cy}`;
        if (!segHash.has(k)) segHash.set(k, []);
        segHash.get(k).push([rd.i, i]);
      }
    }
  }
}
hashSegs();

// T-snap: every degree-1 endpoint hunts a nearby segment of a DIFFERENT row.
let tsnaps = 0;
for (const rd of roads) {
  for (const [n, end] of [[rd.aNode, 0], [rd.bNode, 1]]) {
    if ((degree.get(n) || 0) > 1) continue;
    const [x, y] = nodes[n];
    let best = null, bd = TSNAP_T;
    const cx = Math.floor(x / SEGCELL), cy = Math.floor(y / SEGCELL);
    for (let ix = cx - 1; ix <= cx + 1; ix++) for (let iy = cy - 1; iy <= cy + 1; iy++) {
      const segs = segHash.get(`${ix},${iy}`);
      if (!segs) continue;
      for (const [ri, si] of segs) {
        if (ri === rd.i) continue;
        const o = roads[ri];
        const [ax, ay] = o.pts[si - 1], [bx, by] = o.pts[si];
        const dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy;
        if (L2 === 0) continue;
        let t = ((x - ax) * dx + (y - ay) * dy) / L2;
        t = Math.max(0, Math.min(1, t));
        const px = ax + dx * t, py = ay + dy * t;
        const d = Math.hypot(x - px, y - py);
        if (d < bd) { bd = d; best = { ri, si, t, px, py }; }
      }
    }
    if (best) {
      // land the ramp tip ON the host row: record a split there, sharing THIS node
      nodes[n] = [best.px, best.py];
      roads[best.ri].splits.push({ si: best.si, t: best.t, node: n });
      degree.set(n, (degree.get(n) || 0) + 2);
      tsnaps++;
    }
  }
}
console.log(`t-snapped ${tsnaps} dead ends onto host rows`);

// -------------------------------------------- crossings, BEFORE cutting
// Rows are WHOLE roads by design (H1319) and RDP smoothed the shared
// junction vertices away, so topology cannot say which crossings are
// junctions. The bake's own convention can (H1327/6b): a same-z geometric
// crossing IS an at-grade intersection (signals paint there); a different-z
// crossing is a grade separation. A motorway MAINLINE never meets anything
// at grade — same-z motorway crossings separate instead (over by z, then
// class rank, then length).

function segX(ax, ay, bx, by, cx, cy, dx, dy) {
  const r1x = bx - ax, r1y = by - ay, r2x = dx - cx, r2y = dy - cy;
  const den = r1x * r2y - r1y * r2x;
  if (Math.abs(den) < 1e-12) return null;
  const t = ((cx - ax) * r2y - (cy - ay) * r2x) / den;
  const u = ((cx - ax) * r1y - (cy - ay) * r1x) / den;
  if (t < 0 || t > 1 || u < 0 || u > 1) return null;
  return [ax + r1x * t, ay + r1y * t, t, u];
}

const separations = []; // { overRow, underRow, x, y } -> edge ids after cutting
let junctionCount = 0;
{
  const pairSeen = new Map(); // "a:b" -> [[x,y],...]
  const isMainline = rd => rd.rank === 5 && !rd.link;
  for (const segs of segHash.values()) {
    for (let m = 0; m < segs.length; m++) for (let n = m + 1; n < segs.length; n++) {
      const [ra, ia] = segs[m], [rb, ib] = segs[n];
      if (ra === rb) continue;
      const A = roads[ra], B = roads[rb];
      const hit = segX(A.pts[ia - 1][0], A.pts[ia - 1][1], A.pts[ia][0], A.pts[ia][1],
        B.pts[ib - 1][0], B.pts[ib - 1][1], B.pts[ib][0], B.pts[ib][1]);
      if (!hit) continue;
      const [px, py, t] = hit;
      const key = ra < rb ? `${ra}:${rb}` : `${rb}:${ra}`;
      const prior = pairSeen.get(key) || [];
      if (prior.some(q => Math.hypot(q[0] - px, q[1] - py) < XDEDUP_T)) continue;
      // a crossing right beside a node the two rows already share is the
      // shared node itself (T-snapped ramp tips graze their host)
      const sharedNear = [A.aNode, A.bNode, ...A.splits.map(s => s.node)]
        .filter(nn => [B.aNode, B.bNode, ...B.splits.map(s => s.node)].includes(nn))
        .some(nn => Math.hypot(nodes[nn][0] - px, nodes[nn][1] - py) < 3.0);
      if (sharedNear) continue;
      prior.push([px, py]); pairSeen.set(key, prior);
      if (A.z === B.z && !isMainline(A) && !isMainline(B)) {
        const node = addNode(px, py);
        const u = hit[3];
        A.splits.push({ pos: ia - 1 + t, node });
        B.splits.push({ pos: ib - 1 + u, node });
        junctionCount++;
      } else {
        let over, under;
        if (A.z !== B.z) [over, under] = A.z > B.z ? [A, B] : [B, A];
        else if (A.rank !== B.rank) [over, under] = A.rank > B.rank ? [A, B] : [B, A];
        else [over, under] = A.pts.length >= B.pts.length ? [A, B] : [B, A];
        separations.push({ overRow: over.i, underRow: under.i, x: px, y: py });
      }
    }
  }
  console.log(`at-grade junctions from crossings: ${junctionCount}; grade separations: ${separations.length}`);
}

// cut rows at their split events -> final edges.
// Events carry pos = index + t along the row's vertex walk; a pos landing on
// a vertex (t=0) cuts AT that vertex, one strictly inside a segment cuts at
// the interpolated point (which T-snap already wrote into nodes[]).
const edges = [];
for (const rd of roads) {
  const events = rd.splits
    .map(s => ({ pos: s.pos !== undefined ? s.pos : s.si - 1 + s.t, node: s.node }))
    .sort((a, b) => a.pos - b.pos);
  // dedupe events at the same position (two joins on one vertex)
  const evs = [];
  for (const ev of events)
    if (!evs.length || ev.pos - evs[evs.length - 1].pos > 1e-6 || ev.node !== evs[evs.length - 1].node) evs.push(ev);
  let curPts = [rd.pts[0].slice()], curStart = rd.aNode, ei = 0;
  const cutHere = (node, pt) => {
    curPts.push(pt);
    if (curPts.length >= 2) edges.push({ road: rd, a: curStart, b: node, pts: curPts });
    curPts = [pt.slice()];
    curStart = node;
  };
  for (let i = 1; i < rd.pts.length; i++) {
    // events strictly inside segment (i-1, i)
    while (ei < evs.length && evs[ei].pos < i - 1e-6) {
      if (evs[ei].pos > i - 1 + 1e-6) cutHere(evs[ei].node, [nodes[evs[ei].node][0], nodes[evs[ei].node][1]]);
      ei++;
    }
    // event exactly at vertex i (and not the row's last vertex, which aNode/bNode own)
    if (ei < evs.length && Math.abs(evs[ei].pos - i) <= 1e-6 && i < rd.pts.length - 1) {
      cutHere(evs[ei].node, rd.pts[i].slice());
      ei++;
    } else {
      curPts.push(rd.pts[i].slice());
    }
  }
  edges.push({ road: rd, a: curStart, b: rd.bNode, pts: curPts });
}
// drop zero-length slivers; collapse near-duplicate consecutive points
const finalEdges = edges.filter(e => {
  const clean = [e.pts[0]];
  for (let i = 1; i < e.pts.length; i++)
    if (Math.hypot(e.pts[i][0] - clean[clean.length - 1][0], e.pts[i][1] - clean[clean.length - 1][1]) > 0.02 || i === e.pts.length - 1)
      clean.push(e.pts[i]);
  e.pts = clean;
  let L = 0;
  for (let i = 1; i < e.pts.length; i++) L += Math.hypot(e.pts[i][0] - e.pts[i - 1][0], e.pts[i][1] - e.pts[i - 1][1]);
  e.lenT = L;
  return L > 0.05 && e.pts.length >= 2;
});
console.log(`edges ${finalEdges.length} (from ${roads.length} rows), nodes ${nodes.length}`);

// connectivity from the node nearest uptown
{
  const adj = new Map();
  const add = (a, b) => { if (!adj.has(a)) adj.set(a, []); adj.get(a).push(b); };
  for (const e of finalEdges) { add(e.a, e.b); add(e.b, e.a); }
  const seen = new Set();
  const start = finalEdges[0].a;
  const stack = [start];
  while (stack.length) {
    const n = stack.pop();
    if (seen.has(n)) continue;
    seen.add(n);
    for (const m of adj.get(n) || []) if (!seen.has(m)) stack.push(m);
  }
  let inMain = 0;
  for (const e of finalEdges) if (seen.has(e.a)) inMain++;
  console.log(`connectivity: ${inMain}/${finalEdges.length} edges in the component of edge0 (${(100 * inMain / finalEdges.length).toFixed(1)}%)`);
}

// ------------------------------------------- map separations to final edges

finalEdges.forEach((e, idx) => { e.id = idx; });
const rowEdges = new Map();
for (const e of finalEdges) {
  if (!rowEdges.has(e.road.i)) rowEdges.set(e.road.i, []);
  rowEdges.get(e.road.i).push(e);
}
function edgeNear(rowI, x, y) {
  let best = null, bd = Infinity;
  for (const e of rowEdges.get(rowI) || []) {
    for (let i = 1; i < e.pts.length; i++) {
      const [ax, ay] = e.pts[i - 1], [bx, by] = e.pts[i];
      const dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy;
      let t = L2 > 0 ? ((x - ax) * dx + (y - ay) * dy) / L2 : 0;
      t = Math.max(0, Math.min(1, t));
      const d = (x - (ax + dx * t)) ** 2 + (y - (ay + dy * t)) ** 2;
      if (d < bd) { bd = d; best = e; }
    }
  }
  return best;
}
const crossings = [];
for (const s of separations) {
  const over = edgeNear(s.overRow, s.x, s.y), under = edgeNear(s.underRow, s.x, s.y);
  if (over && under) crossings.push({ over: over.id, under: under.id, x: s.x, y: s.y });
}
{
  const zp = {};
  for (const c of crossings) {
    const zk = `${finalEdges[c.over].road.z}v${finalEdges[c.under].road.z}`;
    zp[zk] = (zp[zk] || 0) + 1;
  }
  console.log('separation z pairs:', JSON.stringify(zp));
}

// --------------------------------------------------------------- water + spans

const waters = [];
for (const rv of RIVERS) {
  const w = rv[0], name = rv[1];
  const pts = [];
  for (let i = 2; i < rv.length; i += 2) pts.push(applySim(T, rv[i], rv[i + 1]));
  waters.push({ name, widthM: Math.max(5, w * LANE_M / LANE_T * 1.0), lake: false, pts });
}
for (const lk of LAKES) {
  const name = lk[0];
  const pts = [];
  for (let i = 1; i < lk.length; i += 2) pts.push(applySim(T, lk[i], lk[i + 1]));
  waters.push({ name, widthM: 0, lake: true, pts });
}

function pointInPoly(pts, x, y) {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const [xi, yi] = pts[i], [xj, yj] = pts[j];
    if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

const wspans = [];
for (const e of finalEdges) {
  // arc positions along e
  const acc = [0];
  for (let i = 1; i < e.pts.length; i++)
    acc.push(acc[i - 1] + Math.hypot(e.pts[i][0] - e.pts[i - 1][0], e.pts[i][1] - e.pts[i - 1][1]));
  const spans = [];
  for (const w of waters) {
    if (!w.lake) {
      for (let i = 1; i < e.pts.length; i++) for (let j = 1; j < w.pts.length; j++) {
        const p = segX(e.pts[i - 1][0], e.pts[i - 1][1], e.pts[i][0], e.pts[i][1],
          w.pts[j - 1][0], w.pts[j - 1][1], w.pts[j][0], w.pts[j][1]);
        if (!p) continue;
        const segL = acc[i] - acc[i - 1];
        const t = segL > 0 ? Math.hypot(p[0] - e.pts[i - 1][0], p[1] - e.pts[i - 1][1]) / segL : 0;
        const s = acc[i - 1] + t * segL;
        const halfT = (w.widthM / 2 + BANK_M) / MPT_LAYOUT;
        spans.push([s - halfT, s + halfT]);
      }
    } else {
      // lake: inside-intervals of the polyline
      let prevIn = pointInPoly(w.pts, e.pts[0][0], e.pts[0][1]);
      let openAt = prevIn ? 0 : -1;
      for (let i = 1; i < e.pts.length; i++) {
        const nowIn = pointInPoly(w.pts, e.pts[i][0], e.pts[i][1]);
        if (nowIn !== prevIn) {
          const sMid = (acc[i - 1] + acc[i]) / 2; // segment straddles the shore
          if (nowIn) openAt = sMid;
          else if (openAt >= 0) { spans.push([openAt - BANK_M / MPT_LAYOUT, sMid + BANK_M / MPT_LAYOUT]); openAt = -1; }
          else spans.push([0, sMid + BANK_M / MPT_LAYOUT]);
          prevIn = nowIn;
        }
      }
      if (openAt >= 0) spans.push([openAt - BANK_M / MPT_LAYOUT, acc[acc.length - 1]]);
    }
  }
  if (!spans.length) continue;
  spans.sort((a, b) => a[0] - b[0]);
  const merged = [spans[0]];
  for (const s of spans.slice(1)) {
    const last = merged[merged.length - 1];
    if (s[0] <= last[1] + 2) last[1] = Math.max(last[1], s[1]);
    else merged.push(s);
  }
  for (const [s0, s1] of merged) {
    const total = acc[acc.length - 1];
    wspans.push({ e: e.id, s0: Math.max(0, s0) * MPT_LAYOUT, s1: Math.min(total, s1) * MPT_LAYOUT });
  }
}
console.log(`water bridge spans: ${wspans.length}`);

// ------------------------------------------------------------------- isects

const isects = [];
let isectMiss = 0;
for (const r of isectRows) {
  const control = r[1], x = r[7], y = r[8];
  const n = findNode(x, y, 1.5);
  if (n >= 0) isects.push({ n, c: control });
  else isectMiss++;
}
console.log(`isect controls matched ${isects.length}, unmatched ${isectMiss}`);

// --------------------------------------------------------------------- emit

const toX = t => (t - CENTER) * MPT_LAYOUT;
const toZ = t => (CENTER - t) * MPT_LAYOUT; // grid y is south+, Unity z is north+
const r1 = v => Math.round(v * 10) / 10;

// uptown (Trade & Tryon) from the bake's own geo anchor
const [LAT0, LON0] = fx.meta.center;
const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const uptownX = r1((-80.8431 - LON0) * M_LON);
const uptownZ = r1((35.2271 - LAT0) * M_LAT);

const out = {
  attribution: 'Road network data (c) OpenStreetMap contributors, ODbL 1.0',
  uptownX, uptownZ,
  nodes: nodes.flatMap(n => [r1(toX(n[0])), r1(toZ(n[1]))]),
  edges: finalEdges.map(e => {
    const rd = e.road;
    const prof = profileFor(rd.name, rd.w);
    return {
      a: e.a, b: e.b, name: rd.name,
      cls: rd.rank, link: rd.link ? 1 : 0, z: rd.z,
      lanes: prof.lanes, w: r1(rd.link ? Math.max(prof.widthM, 5.2) : prof.widthM),
      med: r1(prof.medianM), medGrass: prof.medGrass ? 1 : 0,
      oneway: rd.oneway ? 1 : 0, deck: rd.deck ? 1 : 0,
      pts: e.pts.flatMap(p => [r1(toX(p[0])), r1(toZ(p[1]))]),
    };
  }),
  isects,
  waters: waters.map(w => ({
    name: w.name, w: r1(w.widthM), lake: w.lake ? 1 : 0,
    pts: w.pts.flatMap(p => [r1(toX(p[0])), r1(toZ(p[1]))]),
  })),
  crossings: crossings.map(c => ({ over: c.over, under: c.under, x: r1(toX(c.x)), z: r1(toZ(c.y)) })),
  wspans: wspans.map(s => ({ e: s.e, s0: r1(s.s0), s1: r1(s.s1) })),
};

const outPath = join(UNITY, 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.json');
mkdirSync(dirname(outPath), { recursive: true });
const json = JSON.stringify(out);
writeFileSync(outPath, json);
console.log(`wrote ${outPath}  ${(json.length / 1024).toFixed(0)} KB`);

// ------------------------------------------------------------- debug SVG

const CLS_COLOR = { 5: '#e8442a', 4: '#f08c00', 3: '#e8b400', 2: '#8a8a8a', 1: '#c0c0c0' };
let svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 2500 2500" style="background:#fff">\n`;
for (const w of waters) {
  const d = w.pts.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join('');
  svg += w.lake
    ? `<path d="${d}Z" fill="#bfe0f2" stroke="#7ab8dc" stroke-width="1"/>\n`
    : `<path d="${d}" fill="none" stroke="#7ab8dc" stroke-width="${(w.widthM / MPT_LAYOUT).toFixed(1)}"/>\n`;
}
for (const e of finalEdges) {
  const rd = e.road;
  const d = e.pts.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join('');
  const wT = Math.max(0.8, profileFor(rd.name, rd.w).widthM / MPT_LAYOUT);
  svg += `<path d="${d}" fill="none" stroke="${rd.link ? '#9ad0a0' : CLS_COLOR[rd.rank]}" stroke-width="${wT.toFixed(1)}" stroke-linecap="round"/>\n`;
}
for (const c of crossings) svg += `<circle cx="${c.x.toFixed(1)}" cy="${c.y.toFixed(1)}" r="2.4" fill="#7a2aa0"/>\n`;
for (const s of wspans) {
  const e = finalEdges[s.e];
  svg += `<circle cx="${e.pts[0][0].toFixed(1)}" cy="${e.pts[0][1].toFixed(1)}" r="1.6" fill="#0a54c8"/>\n`;
}
svg += `</svg>\n`;
writeFileSync(join(HERE, 'charlotte_debug.svg'), svg);
console.log('wrote debug SVG');
