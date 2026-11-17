// tracks/checkpoints.mjs - CHECKPOINT CHAINS for the Charlotte city races (owner 2026-11-16 ~21:00: "the Midnight
// Club approach ... with checkpoints and allow players to pathfind"; no barricades anywhere). Generates, audits and
// dumps the ordered gate chain of one route, so the race can let every car take ANY road between gates while no leg
// can be cut. Design: scratchpad checkpoints/DESIGN_v2.md section 1.2 (stage 1).
//
//   node tools/city/tracks/checkpoints.mjs <routeId|candId> [--rev] [--write] [--dump <file.json>]
//        --rev: the reverse twin's chain too ("rev", same file; cpfix 11-17)
//        one route: the gates, a one-line-per-leg audit; --write puts Resources/CityCheckpoints/<routeId>.json
//   node tools/city/tracks/checkpoints.mjs all [--table <file.txt>]
//        every candidate (31) and the 3 export routes: the freedom table (offline, nothing written)
//
// THE RULES (in order):
//  1. Mandatory gates: the lap line (a circuit's startM; also the last gate of every lap), a sprint's start line
//     (cps[0], never a target) and its finish; every TURNAROUND (a run of non-freeway, non-ramp road between two ramp
//     runs: the cross street between an off-ramp and the on-ramp back, so nobody can U-turn on the freeway); every
//     APEX (two route points 150 m+ apart along the route but within min(100 m, 0.4 x along) in plan: a lawn, an
//     infield or a lot could cut it, so a gate goes at the route point farthest from that straight line; the plan
//     test has no heights, so it is conservative: a pair on a bridge edge or on another level is skipped).
//  2. Optional candidates: every 100 m along the chain (decision points after turns and mid-straights fall on it).
//  3. Prune: repeatedly drop the optional gate whose merged leg still passes, preferring the shortest merged leg
//     (even spacing), then the one with the most freedom:
//     (a) NO CUT in plain metres: the legal metre-shortest path (one-ways, the U-turn rule; no class weights, no turn
//         penalty: the ruler the positions use) >= 0.88 x the suggested metres;
//     (b) the suggested leg costs <= 1.06 x the cost optimum (lib.mjs classMul + 60 m turn penalty: the GPS agrees);
//     (c) the leg is <= 800 m, or <= 1,500 m when it has a real alternative (6);
//     (e) the cost optimum's metres <= 1.04 x the metre optimum (the AI's road and the ranking ruler agree).
//  4. Snap each optional survivor to a clean spot within +-60 m (30 m off any junction node of degree 3+, straight
//     over +-15 m, no tunnel or short bridge, few other roads within 15 m); the lap line and finish never move.
//  5. Spans: the gate line (perpendicular to the route at the gate) also counts every same-direction carriageway it
//     crosses within 60 m whose metric distance to the NEXT gate is within 10 % of the route's (a parallel
//     collector road is one wide gate, not a trap).
//  6. Freedom: per leg, near-optimal alternatives by the penalty method (the metre optimum's edges x1.6; accepted
//     when <= 1.12 x the metre optimum in metres and >= 40 % different by length).
// The file stores the anchors (edge, s, direction), the plan point and heading, the spans, and the audit. Heights,
// the lane-faithful centre and the kerb-to-kerb extents are the game's (Edge.YAt / PaveEdgeM, at load).
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { execSync } from 'node:child_process';
import { city, WT, classMul } from './lib.mjs';

const args = process.argv.slice(2);
const opt = k => { const i = args.indexOf('--' + k); return i >= 0 ? args[i + 1] : null; };
const flag = k => args.includes('--' + k);
const C = city();
const NE = C.edges.length, NS = NE * 2;
const GRAPH = 'b2aa3453';

// ---------------------------------------------------------------- the directed-edge graph (CSR)
const hd = (ax, az, bx, bz) => Math.atan2(bz - az, bx - ax);
const angDiff = (a, b) => { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; };
const len = new Float64Array(NS), legal = new Uint8Array(NS), hStart = new Float64Array(NS), hEnd = new Float64Array(NS);
const sNode = new Int32Array(NS), eNode = new Int32Array(NS);
for (let i = 0; i < NE; i++) {
  const e = C.edges[i], p = e.pts, n = p.length;
  for (let d = 0; d < 2; d++) {
    const st = i * 2 + d;
    len[st] = e.length;
    legal[st] = (e.a !== e.b && n >= 2 && e.length > 0.01 && !(d === 1 && e.oneway)) ? 1 : 0;
    if (n < 2) continue;
    const h0 = hd(p[0][0], p[0][1], p[1][0], p[1][1]), h1 = hd(p[n - 2][0], p[n - 2][1], p[n - 1][0], p[n - 1][1]);
    if (d === 0) { hStart[st] = h0; hEnd[st] = h1; sNode[st] = e.a; eNode[st] = e.b; }
    else { hStart[st] = h1 + Math.PI; hEnd[st] = h0 + Math.PI; sNode[st] = e.b; eNode[st] = e.a; }
  }
}
// successors: legal, not the same edge back (except at a dead end), turn <= 2.6 rad
const UTURN = 2.6;
const succStart = new Int32Array(NS + 1), succTmp = [], turnTmp = [];
for (let st = 0; st < NS; st++) {
  succStart[st] = succTmp.length;
  if (!legal[st]) continue;
  const n = eNode[st], ei = st >> 1, deg = C.nodeEdges[n].length;
  for (const nj of C.nodeEdges[n]) {
    const f = C.edges[nj];
    if (f.a === f.b) continue;
    const nd = f.a === n ? 0 : 1, nst = nj * 2 + nd;
    if (!legal[nst]) continue;
    if (nj === ei && deg > 1) continue;
    const turn = Math.abs(angDiff(hEnd[st], hStart[nst]));
    if (turn > UTURN && !(nj === ei && deg === 1)) continue;
    succTmp.push(nst); turnTmp.push(turn);
  }
}
succStart[NS] = succTmp.length;
const succ = Int32Array.from(succTmp), succTurn = Float64Array.from(turnTmp);
// predecessors (reverse CSR)
const predCount = new Int32Array(NS + 1);
for (let k = 0; k < succ.length; k++) predCount[succ[k] + 1]++;
for (let i = 0; i < NS; i++) predCount[i + 1] += predCount[i];
const pred = new Int32Array(succ.length), predTurn = new Float64Array(succ.length), fillAt = predCount.slice(0, NS);
for (let st = 0; st < NS; st++) for (let k = succStart[st]; k < succStart[st + 1]; k++) {
  const y = succ[k], at = fillAt[y]++;
  pred[at] = st; predTurn[at] = succTurn[k];
}

// the cost profiles: metric (metres, no turn charge) and the authoring router's cost (lib.mjs route())
function mulTable(opts, penal) {
  const m = new Float64Array(NS);
  for (let st = 0; st < NS; st++) {
    m[st] = opts ? classMul(C.edges[st >> 1], opts) : 1;
    if (penal && penal.has(st >> 1)) m[st] *= penal.get(st >> 1);
  }
  return m;
}
const TURNPEN = 60;
const turnCost = (turn, costMode) => costMode ? TURNPEN * (turn / (Math.PI / 2)) ** 2 : 0;

// ---------------------------------------------------------------- reverse fields
// D[st] = cost from the START of state st to the gate (Infinity if it cannot get there). A seed state (the gate on
// it at offset g along the travel direction) holds g x mul. The cost from a position (st, t) is: the gate ahead on
// the same state -> (g - t) x mul; otherwise (len - t) x mul + E[st], E[st] = min over successors (turn + D[next]).
const heapK = new Float64Array(NS * 6), heapV = new Int32Array(NS * 6);
function field(seeds, mul, costMode) {
  const D = new Float64Array(NS).fill(Infinity);
  const isSeed = new Uint8Array(NS);
  let hn = 0;
  const push = (k, v) => { let i = hn++; heapK[i] = k; heapV[i] = v; while (i > 0) { const p = (i - 1) >> 1; if (heapK[p] <= heapK[i]) break; const tk = heapK[p], tv = heapV[p]; heapK[p] = heapK[i]; heapV[p] = heapV[i]; heapK[i] = tk; heapV[i] = tv; i = p; } };
  const pop = () => { const k = heapK[0], v = heapV[0]; hn--; if (hn > 0) { heapK[0] = heapK[hn]; heapV[0] = heapV[hn]; let i = 0; for (;;) { const l = 2 * i + 1, r = l + 1; let m = i; if (l < hn && heapK[l] < heapK[m]) m = l; if (r < hn && heapK[r] < heapK[m]) m = r; if (m === i) break; const tk = heapK[m], tv = heapV[m]; heapK[m] = heapK[i]; heapV[m] = heapV[i]; heapK[i] = tk; heapV[i] = tv; i = m; } } return [k, v]; };
  for (const s of seeds) {
    if (!legal[s.st]) continue;
    const v = s.g * mul[s.st];
    if (v < D[s.st]) { D[s.st] = v; isSeed[s.st] = 1; push(v, s.st); }
  }
  while (hn > 0) {
    const [d, y] = pop();
    if (d > D[y]) continue;
    for (let k = predCount[y]; k < predCount[y + 1]; k++) {
      const x = pred[k];
      if (isSeed[x]) continue;
      const c = len[x] * mul[x] + turnCost(predTurn[k], costMode) + d;
      if (c < D[x]) { D[x] = c; push(c, x); }
    }
  }
  return { D, isSeed, seeds, mul, costMode };
}
function endCost(F, st) {
  let best = Infinity;
  for (let k = succStart[st]; k < succStart[st + 1]; k++) {
    const c = turnCost(succTurn[k], F.costMode) + F.D[succ[k]];
    if (c < best) best = c;
  }
  return best;
}
function seedG(F, st) { let g = -1; for (const s of F.seeds) if (s.st === st && (g < 0 || s.g < g)) g = s.g; return g; }
/// cost from position (st, t) to the field's gate
function costFrom(F, st, t) {
  if (F.isSeed[st]) { const g = seedG(F, st); if (g >= t - 1e-6) return (g - t) * F.mul[st]; }
  return (len[st] - t) * F.mul[st] + endCost(F, st);
}
/// walk downhill from (st, t) to the gate: [{st, t0, t1}] pieces and the metres driven
function descend(F, st, t) {
  const out = [];
  if (F.isSeed[st]) { const g = seedG(F, st); if (g >= t - 1e-6) { out.push({ st, t0: t, t1: g }); return { pieces: out, m: g - t }; } }
  out.push({ st, t0: t, t1: len[st] });
  let m = len[st] - t, x = st;
  for (let guard = 0; guard < 6000; guard++) {
    let best = Infinity, by = -1;
    for (let k = succStart[x]; k < succStart[x + 1]; k++) {
      const c = turnCost(succTurn[k], F.costMode) + F.D[succ[k]];
      if (c < best) { best = c; by = succ[k]; }
    }
    if (by < 0 || !isFinite(best)) return null;
    if (F.isSeed[by]) { const g = seedG(F, by); out.push({ st: by, t0: 0, t1: g }); m += g; return { pieces: out, m }; }
    out.push({ st: by, t0: 0, t1: len[by] }); m += len[by]; x = by;
  }
  return null;
}

// ---------------------------------------------------------------- geometry along an edge
function pointOn(st, t) {
  const e = C.edges[st >> 1], s = (st & 1) ? e.length - t : t;
  const p = e.pts, S = e.s;
  let k = 1; while (k < p.length - 1 && S[k] < s) k++;
  const u = S[k] > S[k - 1] ? (s - S[k - 1]) / (S[k] - S[k - 1]) : 0;
  const x = p[k - 1][0] + (p[k][0] - p[k - 1][0]) * u, z = p[k - 1][1] + (p[k][1] - p[k - 1][1]) * u;
  let hx = p[k][0] - p[k - 1][0], hz = p[k][1] - p[k - 1][1]; const L = Math.hypot(hx, hz) || 1; hx /= L; hz /= L;
  if (st & 1) { hx = -hx; hz = -hz; }
  return { x, z, hx, hz, s };
}

// ---------------------------------------------------------------- a route: chain, arcs, positions
function loadRoute(id) {
  const tf = `${WT}/Assets/PSXRacing/Resources/CityTracks/${id}.json`;
  const J = JSON.parse(readFileSync(`${WT}/tools/city/tracks/candidates_2026-11-16.json`, 'utf8'));
  let r = null, cand = null, kind, fwy = false;
  if (existsSync(tf)) {
    const f = JSON.parse(readFileSync(tf, 'utf8'));
    r = { id: f.route.id, loop: !!f.route.loop, startM: f.route.startM, finishM: f.route.finishM, edges: f.route.edges, dirs: f.route.dirs };
    cand = J.courses.find(c => c.id === f.candidate);
  } else if ((cand = J.courses.find(c => c.id.toLowerCase() === id.toLowerCase()))) {
    const loop = cand.kind === 'circuit';
    r = { id: cand.id.toLowerCase(), loop, startM: loop ? cand.gridLineM : (cand.leadM ?? 60),
          finishM: loop ? 0 : (cand.kind === 'drag' && cand.runM ? (cand.leadM ?? 60) + cand.runM : cand.lengthM - (cand.shutM ?? 250)),
          edges: cand.chain.map(c => c[0]), dirs: cand.chain.map(c => c[1]) };
  } else {
    const er = C.routes.find(q => q.id === id);
    if (!er) throw new Error('no route ' + id);
    r = { id: er.id, loop: er.loop, startM: er.startM, finishM: er.finishM, edges: er.edges.map(x => x.edge ?? x[0] ?? x), dirs: er.edges.map(x => x.dir ?? x[1] ?? 1) };
    if (er.dirs) r.dirs = Array.from(er.dirs);
    if (er.edges.length && typeof er.edges[0] === 'number') r.edges = Array.from(er.edges);
  }
  kind = cand ? cand.kind : (r.loop ? 'circuit' : 'sprint');
  // a freeway-and-ramp lap routes on the fwy profile (cands.mjs fwy: the H courses)
  fwy = cand ? cand.id.startsWith('H') : false;
  const st = r.edges.map((e, i) => e * 2 + (r.dirs[i] === 1 ? 0 : 1));
  const cum = [0]; for (const s of st) cum.push(cum[cum.length - 1] + len[s]);
  return { ...r, kind, fwy, cand: cand ? cand.id : r.id, st, cum, L: cum[cum.length - 1] };
}
/// THE REVERSE TWIN's route (cpfix 11-17: TryonSprintRev, CltX1Rev): the chain backwards. A sprint run backwards
/// starts at the forward finish and ends on the forward START LINE (TrackCatalog: "Reversed, the race ends on the
/// forward START LINE"); a loop keeps its lap line where it is.
function reverseRoute(F) {
  const n = F.edges.length;
  const edges = [], dirs = [];
  for (let i = n - 1; i >= 0; i--) { edges.push(F.edges[i]); dirs.push(F.dirs[i] === 1 ? -1 : 1); }
  for (let i = 0; i < n; i++) {
    const e = C.edges[edges[i]];
    if (e.oneway && dirs[i] !== 1) throw new Error(`${F.id} has a one-way road (edge ${edges[i]}): no reverse chain`);
  }
  const st = edges.map((e, i) => e * 2 + (dirs[i] === 1 ? 0 : 1));
  const cum = [0]; for (const s of st) cum.push(cum[cum.length - 1] + len[s]);
  const L = cum[cum.length - 1];
  const startM = F.loop ? ((L - F.startM) % L + L) % L : L - F.finishM;
  const finishM = F.loop ? 0 : L - F.startM;
  return { ...F, edges, dirs, st, cum, L, startM, finishM, rev: true };
}
/// a position on the chain at arc m (wrapped on a loop): {i, st, t, m}
function at(R, m) {
  if (R.loop) m = ((m % R.L) + R.L) % R.L; else m = Math.max(0, Math.min(R.L - 1e-3, m));
  let lo = 0, hi = R.st.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (R.cum[mid] <= m) lo = mid; else hi = mid - 1; }
  return { i: lo, st: R.st[lo], t: m - R.cum[lo], m };
}
/// the suggested route's metres and cost from chain arc a to b (b > a; unwrapped on a loop)
function sugLeg(R, a, b, mul, costMode) {
  let m = 0, c = 0, prev = -1;
  const A = at(R, a), steps = [];
  let i = A.i, t = A.t, left = b - a;
  for (let guard = 0; left > 1e-6 && guard < 100000; guard++) {
    const st = R.st[i % R.st.length];
    if (prev >= 0) {
      const turn = Math.abs(angDiff(hEnd[prev], hStart[st]));
      c += turnCost(turn, costMode);
    }
    const take = Math.min(len[st] - t, left);
    m += take; c += take * mul[st]; left -= take; steps.push(st);
    prev = st; t = 0; i++;
    if (!R.loop && i >= R.st.length) break;
  }
  return { m, c, steps };
}

// ---------------------------------------------------------------- mandatory gates
function turnarounds(R) {
  // runs of chain edges that are neither ramps nor freeway (rank < 4), with a ramp run on both sides
  const out = [];
  const n = R.st.length, isRamp = i => C.edges[R.st[((i % n) + n) % n] >> 1].link;
  const isFwy = i => { const e = C.edges[R.st[((i % n) + n) % n] >> 1]; return !e.link && e.rank >= 4; };
  const isCross = i => !isRamp(i) && !isFwy(i);
  for (let i = 0; i < n; i++) {
    if (!isCross(i) || isCross(i - 1)) continue;
    if (!R.loop && i === 0) continue;
    let j = i; while (j - i < n && isCross(j + 1)) j++;
    if (!R.loop && j >= n - 1) continue;
    if (isRamp(i - 1) && isRamp(j + 1)) {
      const a = R.cum[i], b = R.cum[j] + len[R.st[j % n]];
      out.push({ m: (a + b) / 2, lo: a + 20, hi: b - 20, name: C.edges[R.st[i] >> 1].name || '?' });
    }
  }
  return out;
}
/// plan-pair shortcut candidates along the chain (no heights in node: conservative)
function chainSamples(R, step = 5) {
  const P = [];
  for (let i = 0; i < R.st.length; i++) {
    const st = R.st[i], e = C.edges[st >> 1];
    for (let t = 0; t < len[st]; t += step) { const p = pointOn(st, t); P.push({ x: p.x, z: p.z, m: R.cum[i] + t, br: e.bridge || e.tunnel, lv: e.level, ei: st >> 1 }); }
  }
  return P;
}
function apexGates(R, gatesM) {
  const P = chainSamples(R, 5), out = [];
  const gs = gatesM.slice();
  const between = (a, b) => gs.some(g => { let gg = g; if (R.loop && gg < a) gg += R.L; return gg > a + 1 && gg < b - 1; });
  const pairs = [];
  for (let i = 0; i < P.length; i += 2) for (let j = i + 1; j < P.length; j += 2) {
    const along = P[j].m - P[i].m;
    if (along < 150 || along > 1500) continue;
    if (R.loop && R.L - along < 150) continue;
    const d = Math.hypot(P[i].x - P[j].x, P[i].z - P[j].z);
    if (d > Math.min(100, 0.4 * along)) continue;
    if (P[i].br || P[j].br || P[i].lv !== P[j].lv || P[i].ei === P[j].ei) continue;
    pairs.push({ i, j, along, d });
  }
  pairs.sort((a, b) => (b.along - b.d) - (a.along - a.d));
  for (const q of pairs) {
    if (between(P[q.i].m, P[q.j].m)) continue;
    // the apex: the route point farthest from the straight line between the pair
    const ax = P[q.i].x, az = P[q.i].z, bx = P[q.j].x, bz = P[q.j].z, L = Math.hypot(bx - ax, bz - az) || 1;
    let best = -1, bd = -1;
    for (let k = q.i + 1; k < q.j; k++) {
      const dd = Math.abs((bx - ax) * (az - P[k].z) - (ax - P[k].x) * (bz - az)) / L;
      if (dd > bd) { bd = dd; best = k; }
    }
    if (best < 0) continue;
    out.push({ m: P[best].m, from: P[q.i].m, to: P[q.j].m, across: q.d, along: q.along });
    gs.push(P[best].m);
  }
  return out;
}

// ---------------------------------------------------------------- leg evaluation
function makeCtx(R) {
  const prof = R.fwy ? { fwy: true } : {};
  const mulM = mulTable(null), mulC = mulTable(prof);
  const fcache = new Map();
  function gateSeeds(m) {
    const A = at(R, m), e = C.edges[A.st >> 1];
    const seeds = [{ st: A.st, g: A.t }];
    if (!e.oneway) seeds.push({ st: A.st ^ 1, g: len[A.st] - A.t });   // a two-way anchor: from either side
    return seeds;
  }
  function fieldsAt(m, extraSeeds) {
    const key = Math.round(at(R, m).m * 10) + (extraSeeds ? ':' + extraSeeds.map(s => s.st + '@' + s.g.toFixed(1)).join(',') : '');
    if (fcache.has(key)) return fcache.get(key);
    const seeds = gateSeeds(m).concat(extraSeeds || []);
    const v = { met: field(seeds, mulM, false), cost: field(seeds, mulC, true), seeds };
    fcache.set(key, v);
    if (fcache.size > 400) fcache.delete(fcache.keys().next().value);
    return v;
  }
  function alternatives(A, Fb, optPieces, metM, seeds) {
    // penalty method on the metric field: the found routes' edges x1.6, at most 2 alternatives
    const penal = new Map(); let found = 0; const routes = [];
    let base = optPieces;
    for (let round = 0; round < 2; round++) {
      for (const p of base) penal.set(p.st >> 1, (penal.get(p.st >> 1) || 1) * 1.6);
      const F = field(seeds, mulTable(null, penal), false);
      const d = descend(F, A.st, A.t);
      if (!d) break;
      if (d.m > 1.12 * metM) break;
      const optSet = new Set(optPieces.map(p => p.st >> 1));
      let shared = 0; for (const p of d.pieces) if (optSet.has(p.st >> 1)) shared += p.t1 - p.t0;
      if (shared / Math.max(1, d.m) <= 0.6) { found++; routes.push(d); }
      base = d.pieces;
    }
    return { n: found, routes };
  }
  function evalLeg(a, b, wantAlts = false, cap = 800, capFree = 1500) {
    const A = at(R, a), F = fieldsAt(b);
    const sugM_ = sugLeg(R, a, b, mulM, false), sugC = sugLeg(R, a, b, mulC, true);
    const metM = costFrom(F.met, A.st, A.t), costOpt = costFrom(F.cost, A.st, A.t);
    const dc = descend(F.cost, A.st, A.t), dm = descend(F.met, A.st, A.t);
    const costOptM = dc ? dc.m : Infinity;
    const r = { a, b, sugM: sugM_.m, sugC: sugC.c, metM, optC: costOpt, optM: costOptM, metPieces: dm ? dm.pieces : null, optPieces: dc ? dc.pieces : null };
    r.ta = r.metM >= 0.88 * r.sugM - 0.5;
    r.tb = r.sugC <= 1.06 * r.optC + 0.5;
    r.te = r.optM <= 1.04 * r.metM + 0.5;
    r.alts = 0;
    if ((wantAlts || (r.sugM > 800 && r.sugM <= 1500)) && dm && isFinite(metM)) r.alts = alternatives(A, F, dm.pieces, metM, F.seeds).n;
    r.tc = r.sugM <= cap || (r.sugM <= capFree && r.alts >= 1);
    r.pass = r.ta && r.tb && r.te && r.tc && isFinite(metM);
    return r;
  }
  return { evalLeg, fieldsAt, mulM, mulC, gateSeeds };
}

// ---------------------------------------------------------------- clean spots
const nodeDeg = C.nodeEdges.map(l => l.length);
// a plan grid of edge segments for the "other road nearby" and the span search
const CELL = 64, grid = new Map();
for (let i = 0; i < NE; i++) {
  const p = C.edges[i].pts;
  for (let k = 1; k < p.length; k++) {
    const x0 = Math.min(p[k - 1][0], p[k][0]), x1 = Math.max(p[k - 1][0], p[k][0]);
    const z0 = Math.min(p[k - 1][1], p[k][1]), z1 = Math.max(p[k - 1][1], p[k][1]);
    for (let cx = Math.floor(x0 / CELL); cx <= Math.floor(x1 / CELL); cx++)
      for (let cz = Math.floor(z0 / CELL); cz <= Math.floor(z1 / CELL); cz++) {
        const key = cx * 100003 + cz; let l = grid.get(key); if (!l) grid.set(key, l = []); l.push(i * 4096 + k);
      }
  }
}
function segsNear(x, z, r) {
  const out = new Set();
  for (let cx = Math.floor((x - r) / CELL); cx <= Math.floor((x + r) / CELL); cx++)
    for (let cz = Math.floor((z - r) / CELL); cz <= Math.floor((z + r) / CELL); cz++) {
      const l = grid.get(cx * 100003 + cz); if (l) for (const v of l) out.add(v);
    }
  return out;
}
function cleanPenalty(R, m) {
  const A = at(R, m), p = pointOn(A.st, A.t), e = C.edges[A.st >> 1];
  let pen = 0;
  // junction nodes of degree 3+ within 30 m
  for (const v of segsNear(p.x, p.z, 40)) {
    const ei = Math.floor(v / 4096); const f = C.edges[ei];
    for (const n of [f.a, f.b]) if (nodeDeg[n] >= 3) { const d = Math.hypot(C.nodes[n].x - p.x, C.nodes[n].z - p.z); if (d < 30) pen += (30 - d) * 2; }
  }
  // straight over +-15 m
  const pa = pointOn(at(R, m - 15).st, at(R, m - 15).t), pb = pointOn(at(R, m + 15).st, at(R, m + 15).t);
  const dh = Math.abs(angDiff(Math.atan2(pa.hz, pa.hx), Math.atan2(pb.hz, pb.hx))) * 180 / Math.PI;
  if (dh > 10) pen += (dh - 10) * 3;
  if (e.tunnel) pen += 200;
  if (e.bridge && e.length < 40) pen += 120;
  // another road within 15 m that is not this edge (no heights in node: a small charge only)
  const near = new Set();
  for (const v of segsNear(p.x, p.z, 16)) {
    const ei = Math.floor(v / 4096), k = v % 4096; if (ei === (A.st >> 1)) continue;
    const q = C.edges[ei].pts, ax = q[k - 1][0], az = q[k - 1][1], bx = q[k][0], bz = q[k][1];
    const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz || 1;
    const u = Math.max(0, Math.min(1, ((p.x - ax) * dx + (p.z - az) * dz) / L2));
    if (Math.hypot(ax + dx * u - p.x, az + dz * u - p.z) < 15) near.add(ei);
  }
  pen += near.size * 6;
  return pen;
}

// ---------------------------------------------------------------- spans (wide gates)
// a span must not be a shortcut from the gate before: reached in plain metres no sooner than 0.96 x the anchor
// (cpfix 11-17: C2's gate 7 counted a parallel road 600 m from gate 6 where the route's anchor is 678 m)
const MUL_MET = mulTable(null);
function spansAt(R, ctx, m, nextM, prevM) {
  const A = at(R, m), p = pointOn(A.st, A.t);
  const rx = p.hz, rz = -p.hx;                      // right of the heading
  const ox = p.x - rx * 60, oz = p.z - rz * 60, ex = p.x + rx * 60, ez = p.z + rz * 60;
  const own = { e: A.st >> 1, d: (A.st & 1) ? -1 : 1, s: p.s, l: 0 };
  const out = [own];
  if (nextM == null) return out;
  const Fn = ctx.fieldsAt(nextM).met;
  const ref = costFrom(Fn, A.st, A.t);
  const seen = new Set([A.st >> 1]);
  for (const v of segsNear(p.x, p.z, 64)) {
    const ei = Math.floor(v / 4096), k = v % 4096;
    if (seen.has(ei)) continue;
    const f = C.edges[ei], q = f.pts;
    const ax = q[k - 1][0], az = q[k - 1][1], bx = q[k][0], bz = q[k][1];
    // segment-segment intersection with the gate line
    const d1x = ex - ox, d1z = ez - oz, d2x = bx - ax, d2z = bz - az;
    const den = d1x * d2z - d1z * d2x; if (Math.abs(den) < 1e-9) continue;
    const u = ((ax - ox) * d2z - (az - oz) * d2x) / den, w = ((ax - ox) * d1z - (az - oz) * d1x) / den;
    if (u < 0 || u > 1 || w < 0 || w > 1) continue;
    const sAt = f.s[k - 1] + w * (f.s[k] - f.s[k - 1]);
    const tl = Math.hypot(d2x, d2z) || 1, tx = d2x / tl, tz = d2z / tl;
    for (const dir of f.oneway ? [1] : [1, -1]) {
      const dot = (tx * p.hx + tz * p.hz) * dir;
      if (dot < 0.8) continue;
      const st = ei * 2 + (dir === 1 ? 0 : 1); if (!legal[st]) continue;
      const t = dir === 1 ? sAt : f.length - sAt;
      const via = costFrom(Fn, st, t);
      if (!isFinite(via) || Math.abs(via - ref) > 0.1 * Math.max(ref, 1)) continue;
      if (prevM != null) {
        const P = at(R, prevM);
        const seedsS = [{ st, g: t }];
        if (!f.oneway) seedsS.push({ st: st ^ 1, g: len[st] - t });
        const toSpan = costFrom(field(seedsS, MUL_MET, false), P.st, P.t);
        const toAnchor = costFrom(ctx.fieldsAt(m).met, P.st, P.t);
        if (isFinite(toAnchor) && toSpan < 0.96 * toAnchor) continue;
      }
      seen.add(ei);
      out.push({ e: ei, d: dir, s: sAt, l: (u - 0.5) * 120 });
    }
  }
  return out;
}

// ---------------------------------------------------------------- generate one route
function generate(id, quiet = false, rev = false) {
  const R0 = loadRoute(id), R = rev ? reverseRoute(R0) : R0, ctx = makeCtx(R);
  const loop = R.loop;
  const s0 = R.startM, s1 = loop ? R.startM + R.L : R.finishM;
  // unwrapped arcs from the start line
  const U = m => { if (!loop) return m; let u = m; while (u < s0) u += R.L; while (u >= s0 + R.L) u -= R.L; return u; };
  const mand = [{ m: s0, kind: 'line', fixed: true }];
  if (!loop) mand.push({ m: s1, kind: 'finish', fixed: true });
  for (const t of turnarounds(R)) { const u = U(t.m); if (u > s0 + 30 && u < s1 - 30) mand.push({ m: u, kind: 'turn', lo: U(t.lo), hi: U(t.hi), name: t.name }); }
  const apex = apexGates(R, mand.map(g => g.m));
  for (const a of apex) { const u = U(a.m); if (u > s0 + 30 && u < s1 - 30) mand.push({ m: u, kind: 'apex', note: `${a.along.toFixed(0)} m along, ${a.across.toFixed(0)} m across` }); }
  mand.sort((a, b) => a.m - b.m);
  // THE PRUNE, per stretch between two mandatory gates: the FEWEST evenly spaced optional gates whose every leg
  // passes (a), (b), (e) and the spacing (c) - the fewest gates make the longest legs, which is where route choice
  // lives (freedom-aware: a leg over 740 m passes only with a real alternative). The cap leaves 60 m for the snap.
  let gates = mand.slice();
  const end = loop ? s0 + R.L : s1;
  const legOf = new Map();
  const nextOf = i => i + 1 < gates.length ? gates[i + 1].m : end;
  const ev = (a, b) => { const k = a.toFixed(1) + '>' + b.toFixed(1); if (!legOf.has(k)) legOf.set(k, ctx.evalLeg(a, b, false, 740, 1440)); return legOf.get(k); };
  const fills = [];
  for (let i = 0; i < mand.length; i++) {
    const a = mand[i].m, b = i + 1 < mand.length ? mand[i + 1].m : end, span = b - a;
    let placed = null;
    for (let k = Math.max(1, Math.ceil(span / 1440)); k <= Math.max(1, Math.ceil(span / 150)); k++) {
      const pos = [a]; for (let q = 1; q < k; q++) pos.push(a + span * q / k); pos.push(b);
      let ok = true;
      for (let q = 0; q < k && ok; q++) ok = ev(pos[q], pos[q + 1]).pass;
      if (ok) { placed = pos.slice(1, -1); break; }
    }
    if (!placed) { placed = []; for (let m = a + 400; m < b - 200; m += 400) placed.push(m); }   // reported as failing legs
    for (const m of placed) fills.push({ m, kind: 'fill' });
  }
  gates = gates.concat(fills).sort((a, b) => a.m - b.m);
  // snap the optional survivors to a clean spot (+-60 m), keeping order and 200 m spacing
  for (let i = 0; i < gates.length; i++) {
    const g = gates[i];
    if (g.fixed) continue;
    const lo = Math.max(g.lo ?? g.m - (g.kind === 'apex' ? 20 : 60), (i > 0 ? gates[i - 1].m : s0) + 200);
    const hi = Math.min(g.hi ?? g.m + (g.kind === 'apex' ? 20 : 60), (i + 1 < gates.length ? gates[i + 1].m : end) - (i + 1 < gates.length ? 200 : 60));
    let bm = g.m, bp = cleanPenalty(R, g.m) + 0;
    for (let m = lo; m <= hi; m += 5) {
      const p = cleanPenalty(R, m) + Math.abs(m - g.m) * 0.05;
      if (p < bp) { bp = p; bm = m; }
    }
    // the moved legs must still pass
    if (bm !== g.m) {
      const a = i > 0 ? gates[i - 1].m : null, b = nextOf(i);
      const ok = (a == null || ctx.evalLeg(a, bm).pass || !ctx.evalLeg(a, g.m).pass) && (ctx.evalLeg(bm, b).pass || !ctx.evalLeg(g.m, b).pass);
      if (ok) g.m = bm;
    }
    g.pen = cleanPenalty(R, g.m);
  }
  // final audit, with the freedom report on every leg
  const legs = [];
  for (let i = 0; i < gates.length; i++) {
    const a = gates[i].m, b = nextOf(i);
    if (!loop && i === gates.length - 1) break;
    legs.push({ from: i, to: (i + 1) % gates.length, ...ctx.evalLeg(a, b, true) });
  }
  // spans: each gate's line vs the next gate's field
  const cps = gates.map((g, i) => {
    const A = at(R, g.m), p = pointOn(A.st, A.t);
    const nm = i + 1 < gates.length ? gates[i + 1].m : (loop ? end : null);
    const pm = i > 0 ? gates[i - 1].m : (loop ? gates[gates.length - 1].m - R.L : null);
    const spans = spansAt(R, ctx, g.m, nm, pm);
    const leg = legs.find(l => l.to === i && (i > 0 || loop));
    return {
      kind: g.kind === 'fill' ? 'split' : g.kind, routeM: +(((g.m % R.L) + R.L) % R.L).toFixed(1),
      e: A.st >> 1, s: +p.s.toFixed(2), d: (A.st & 1) ? -1 : 1, x: +p.x.toFixed(2), y: 0, z: +p.z.toFixed(2),
      hx: +p.hx.toFixed(4), hz: +p.hz.toFixed(4), depth: 6,
      spans: spans.map(s => ({ e: s.e, d: s.d, s: +s.s.toFixed(2), l: +s.l.toFixed(2) })),
      legM: leg ? +leg.sugM.toFixed(1) : 0, optM: leg ? +leg.optM.toFixed(1) : 0, metM: leg ? +leg.metM.toFixed(1) : 0,
      name: C.edges[A.st >> 1].name || '', pen: +(g.pen ?? 0).toFixed(1),
    };
  });
  const auditLegs = legs.map(l => ({
    from: l.from, to: l.to, sugM: +l.sugM.toFixed(1), optM: +l.optM.toFixed(1), metM: +l.metM.toFixed(1),
    metRatio: +(l.metM / l.sugM).toFixed(3), sugCostRatio: +(l.sugC / l.optC).toFixed(3), optOverMet: +(l.optM / l.metM).toFixed(3),
    alts: l.alts, pass: l.pass, tests: (l.ta ? '' : 'a') + (l.tb ? '' : 'b') + (l.tc ? '' : 'c') + (l.te ? '' : 'e'),
  }));
  const lm = legs.map(l => l.sugM);
  const audit = { legs: auditLegs, minLegM: +Math.min(...lm).toFixed(0), maxLegM: +Math.max(...lm).toFixed(0),
                  freeLegs: +(legs.filter(l => l.alts >= 1).length / Math.max(1, legs.length)).toFixed(2),
                  apex: apex.length, turnarounds: mand.filter(g => g.kind === 'turn').length, failing: auditLegs.filter(l => !l.pass).length };
  let git = '';
  try { git = execSync('git rev-parse --short HEAD', { cwd: WT }).toString().trim(); } catch { }
  const file = { version: 1, graph: GRAPH, route: R.id, candidate: R.cand, generator: `checkpoints.mjs ${new Date().toISOString().slice(0, 10)} ${git}`,
                 cost: R.fwy ? 'fwy' : 'city', loop: loop ? 1 : 0, lengthM: +R.L.toFixed(1), startM: R.startM, finishM: R.finishM,
                 chains: [{ dir: rev ? 'rev' : 'fwd', loop: loop ? 1 : 0, cps }], audit };
  if (!quiet) {
    console.log(`${rev ? 'REVERSE ' : ''}${R.id} (${R.cand}, ${R.kind}, ${R.fwy ? 'fwy' : 'city'} cost) L ${R.L.toFixed(0)} m, ${cps.length} gates, legs ${audit.minLegM}-${audit.maxLegM} m, freeLegs ${audit.freeLegs}, apex ${apex.length}, turnarounds ${audit.turnarounds}, failing legs ${audit.failing}`);
    cps.forEach((c, i) => console.log(`  gate ${String(i).padStart(2)} ${c.kind.padEnd(6)} @ ${c.routeM.toFixed(0).padStart(6)} m  e${c.e}${c.d > 0 ? '+' : '-'} s ${c.s.toFixed(0).padStart(4)}  spans ${c.spans.length}  clean-pen ${c.pen}  ${c.name}`));
    auditLegs.forEach(l => console.log(`  leg ${String(l.from).padStart(2)}->${String(l.to).padStart(2)} sug ${l.sugM.toFixed(0).padStart(5)} met ${l.metM.toFixed(0).padStart(5)} (${l.metRatio.toFixed(2)}) costOpt ${l.optM.toFixed(0).padStart(5)} m (opt/met ${l.optOverMet.toFixed(2)}, sug/opt cost ${l.sugCostRatio.toFixed(2)}) alts ${l.alts} ${l.pass ? 'PASS' : 'FAIL ' + l.tests}`));
    for (const a of apex) console.log(`  apex at ${a.m.toFixed(0)} m: ${a.from.toFixed(0)} -> ${a.to.toFixed(0)} m (${a.along.toFixed(0)} m along, ${a.across.toFixed(0)} m across in plan)`);
  }
  return { file, R, legs, ctx, gates };
}

/// reachability in both cost profiles, every leg (and the lap closing): the router reaches every gate
function reach(gen) {
  const { file, ctx, R } = gen; const cps = file.chains[0].cps; let bad = 0;
  for (let i = 0; i < cps.length; i++) {
    if (!R.loop && i === cps.length - 1) break;
    const a = cps[i], b = cps[(i + 1) % cps.length];
    const F = ctx.fieldsAt(b.routeM + (R.loop || b.routeM > a.routeM ? 0 : R.L));
    const st = a.e * 2 + (a.d === 1 ? 0 : 1), t = a.d === 1 ? a.s : len[st] - a.s;
    const m = costFrom(F.met, st, t), c = costFrom(F.cost, st, t);
    if (!isFinite(m) || !isFinite(c)) { bad++; console.log(`  UNREACHABLE gate ${i} -> ${(i + 1) % cps.length}`); }
  }
  return bad;
}

if (args[0] === 'all') {
  const J = JSON.parse(readFileSync(`${WT}/tools/city/tracks/candidates_2026-11-16.json`, 'utf8'));
  const ids = J.courses.filter(c => c.kind !== 'drag').map(c => c.id).concat(C.routes.map(r => r.id));
  const lines = ['route        kind     cost  length  gates  legs(min-max m)  freeLegs  apex  turnarounds  failingLegs'];
  for (const id of ids) {
    try {
      const g = generate(id, true), a = g.file.audit;
      lines.push(`${id.padEnd(12)} ${g.R.kind.padEnd(8)} ${g.file.cost.padEnd(5)} ${(g.R.L / 1000).toFixed(1).padStart(5)} km ${String(g.file.chains[0].cps.length).padStart(5)}  ${String(a.minLegM).padStart(5)}-${String(a.maxLegM).padEnd(5)}      ${a.freeLegs.toFixed(2).padStart(5)}  ${String(a.apex).padStart(4)}  ${String(a.turnarounds).padStart(11)}  ${String(a.failing).padStart(11)}`);
      console.log(lines[lines.length - 1]);
    } catch (err) { lines.push(`${id.padEnd(12)} ERROR ${err.message}`); console.log(lines[lines.length - 1]); }
  }
  if (opt('table')) writeFileSync(opt('table'), lines.join('\n') + '\n');
} else if (args[0]) {
  const g = generate(args[0]);
  let bad = reach(g);
  console.log(bad ? `REACH FAIL: ${bad} legs` : 'REACH OK: every gate reachable from the one before (metric and cost)');
  // --rev: the reverse twin's chain too, in the same file (CheckpointFile.ChainFor picks "rev" for a Rev venue)
  if (flag('rev')) {
    const gr = generate(args[0], false, true);
    const br = reach(gr);
    console.log(br ? `REVERSE REACH FAIL: ${br} legs` : 'REVERSE REACH OK');
    bad += br;
    g.file.chains.push(gr.file.chains[0]);
    g.file.auditRev = gr.file.audit;
  }
  if (flag('write')) {
    if (bad || g.file.audit.failing || (g.file.auditRev && g.file.auditRev.failing)) { console.log('NOT WRITTEN: failing legs or an unreachable gate'); process.exit(1); }
    const dir = `${WT}/Assets/PSXRacing/Resources/CityCheckpoints`;
    mkdirSync(dir, { recursive: true });
    writeFileSync(`${dir}/${g.R.id}.json`, JSON.stringify(g.file, null, 1) + '\n');
    console.log('wrote ' + `${dir}/${g.R.id}.json`);
  }
  if (opt('dump')) {
    // for the sheet: the roads round the course, the route, the gates, each leg's cost optimum and alternative
    const xs = [], zs = [];
    for (const st of g.R.st) { const e = C.edges[st >> 1]; for (const p of e.pts) { xs.push(p[0]); zs.push(p[1]); } }
    const bb = [Math.min(...xs) - 300, Math.min(...zs) - 300, Math.max(...xs) + 300, Math.max(...zs) + 300];
    const roads = [];
    for (const e of C.edges) {
      if (!e.pts.some(p => p[0] > bb[0] && p[0] < bb[2] && p[1] > bb[1] && p[1] < bb[3])) continue;
      roads.push({ r: e.rank, l: e.link ? 1 : 0, p: e.pts.map(p => [+p[0].toFixed(1), +p[1].toFixed(1)]) });
    }
    const poly = pieces => pieces.flatMap(pc => { const o = []; for (let t = pc.t0; t <= pc.t1 + 0.01; t += 10) { const p = pointOn(pc.st, Math.min(t, pc.t1)); o.push([+p.x.toFixed(1), +p.z.toFixed(1)]); } return o; });
    const route = []; for (const st of g.R.st) for (let t = 0; t <= len[st]; t += 10) { const p = pointOn(st, Math.min(t, len[st])); route.push([+p.x.toFixed(1), +p.z.toFixed(1)]); }
    writeFileSync(opt('dump'), JSON.stringify({ id: g.R.id, bb, roads, route, cps: g.file.chains[0].cps, audit: g.file.audit,
      opt: g.legs.map(l => l.optPieces ? poly(l.optPieces) : []) }));
    console.log('dumped ' + opt('dump'));
  }
}
