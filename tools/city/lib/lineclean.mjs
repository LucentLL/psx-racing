// lineclean.mjs - WP-10, THE LINE CLEAN-UP (plan amendment A4 WP-10 amended,
// A7, A8). The owner, 2026-09-29, after ten minutes on /city/: "Squiggly
// roads. A lot of roads where opposite direction merged into a single road.
// All sections of road that add an additional lane for a turn lane, instead
// expand a lane and widen on both sides, as if center aligned. Two lane to
// three lane to two lane. In reality it is just two lanes keeping consistent
// lanes with an additional lane added to left or right side for turn lane."
//
// Runs inside export_osm.mjs on the graph as cut (after the dead-end weld,
// before crossings), in this order:
//   1. TAGGED NODES FIRST (critic C3): every control node on a kept way is
//      recorded as (way id, node id, raw distance) before any vertex moves,
//      and projected to (edge, s) at the end -> section TAGN. Every vertex
//      another OSM highway way shares (kept or not: the toll ways, the
//      private and unnamed service roads) is pinned.
//   2. LANE-COUNT DATA, per chain: UNTAGGED A->B->A flickers under 100 m
//      and pieces shorter than two taper floors take their neighbours'
//      count; an untagged ground piece between two tagged ones of the same
//      count takes theirs ("inferred"); an untagged bridge keeps the default
//      and goes on the review list (the creek deck, way 16671358, first). A
//      piece with its OWN lanes= tag is never changed: a short tagged piece
//      is a turn bay, drawn on one side by TAPR.
//   3. SIMPLIFY: collapse vertices under 2 m apart, then Douglas-Peucker at
//      0.5 m, junction nodes, dead ends and pinned vertices fixed. Links and
//      roundabouts are left alone until WP-11 fillets them (critic C32).
//   4. DOGLEGS: two opposite turns of 8 degrees or more joined by 20 m or
//      less, at least 15 m from a junction, are straightened (the Z's two
//      vertices dropped, or a 2-arm node put back on the line).
//   5. TAPR, per chain, at every lane-count change: which SIDE the lane opens
//      or closes on (turn:lanes, lanes:forward/backward; untagged: the right
//      of the direction that gains it, a centre gain on a two-way road is a
//      left-turn bay for the direction that gains it - never both outer
//      edges; an untagged lane at a junction mouth opens on the side that
//      brings the ribbon back toward its OSM line), the run's lateral OFFSET
//      from its OSM line, held along the run (the through lanes keep their
//      position: A8 rules 1 and 3; no mid-block shift), the MUTCD length
//      (WS^2/60 to 40 mph, WS from 45, turn bays 30-55 m, floors 15/30/90 m)
//      and the room the chain has for it - never clamped to one OSM piece.
//      At a junction node the lane opens or ends full width at the mouth (no
//      taper where cars turn). -> section TAPR.
//   6. PARA: carriageways whose pavements (at game widths, with the TAPR
//      offsets and WP-11's lane centring, whichever is wider on the facing
//      side) overlap or leave less than the class gap are moved apart with a
//      smooth offset along their chains; where two chains really meet (a
//      shared node, out to where the mapped lines part; a junction box
//      across a short connector) they are exempt; what cannot be moved goes
//      on the review list. -> section PARA.
//   7. WP-11 FILLETS (lib/fillet.mjs): every 2-arm node, every free vertex and
//      the through pair of every mitred junction rounded with tangent arcs at
//      the class radius, densified by sagitta; the tagged nodes are projected
//      after it (1b), onto the lines the game draws.
// The per-class lane-width table (owner, 2026-09-27: real widths) is written
// as section LANW; the game keeps 3.6576 m until WP-11b draws paint by line
// (plan A3, "Lane widths").
import { profileFor } from './citydata.mjs';
import { filletGraph, emaxOf, mitredThrough } from './fillet.mjs';
import { writeFileSync } from 'node:fs';

export const SMOOTH = t => (t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t));
const DEG = 180 / Math.PI;
/// A chain's ribbon may sit up to one lane off its OSM line (the map's own
/// error on a widened road); past that it is re-anchored at a junction mouth.
const LANE_REANCHOR = 3.6576 + 0.05;
const dist = (p, q) => Math.hypot(p[0] - q[0], p[1] - q[1]);

/// Real US lane widths by class (AASHTO / CDOT): freeway and expressway 12 ft,
/// arterial and collector 11 ft, tertiary 10.5 ft, local 10 ft; freeway ramps
/// 12 ft, other links 11 ft. Index = rank (0 local .. 5 motorway).
export const LANE_W = [3.048, 3.2004, 3.3528, 3.3528, 3.6576, 3.6576];
export const LANE_W_LINK = [3.3528, 3.3528, 3.3528, 3.3528, 3.6576, 3.6576];

/// Taper floors (plan A4 WP-10 item 6): street 15 m, arterial 30 m, freeway 90 m.
const floorOf = w => (w.rank >= 5 || (w.rank === 4 && w.oneway && !w.link)) ? 90 : (w.link || w.rank >= 2) ? 30 : 15;
/// Speed in mph: maxspeed, or the class default.
const mphOf = w => w.speed > 0 ? w.speed / 1.609344 : w.link ? 35 : [25, 35, 40, 45, 55, 65][w.rank];
/// MUTCD taper length for a lateral change of W metres (Table 6C-3): WS^2/60
/// up to 40 mph, WS from 45 mph (feet), returned in metres.
function mutcdLen(Wm, mph) {
  const Wft = Wm / 0.3048;
  return (mph <= 40 ? Wft * mph * mph / 60 : Wft * mph) * 0.3048;
}
/// Shifting-taper rate (metres along per metre across), the same table.
const shiftRate = mph => Math.max(10, mph <= 40 ? mph * mph / 60 : mph);

function polyLen(P) { let L = 0; for (let k = 1; k < P.length; k++) L += dist(P[k], P[k - 1]); return L; }
function cumS(P) { const s = [0]; for (let k = 1; k < P.length; k++) s.push(s[k - 1] + dist(P[k], P[k - 1])); return s; }
function outDir(e, n) {
  const P = e.pts;
  const [p, q] = e.a === n ? [P[0], P[1]] : [P[P.length - 1], P[P.length - 2]];
  const d = Math.hypot(q[0] - p[0], q[1] - p[1]) || 1;
  return [(q[0] - p[0]) / d, (q[1] - p[1]) / d];
}
function signedTurn(a, b, c) {
  const ux = b[0] - a[0], uz = b[1] - a[1], vx = c[0] - b[0], vz = c[1] - b[1];
  return Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz);
}
function rdp(pts, eps) {
  if (pts.length < 3) return pts;
  const keep = new Uint8Array(pts.length);
  keep[0] = keep[pts.length - 1] = 1;
  const stack = [[0, pts.length - 1]];
  while (stack.length) {
    const [a, b] = stack.pop();
    let maxD = -1, maxI = -1;
    const [ax, ay] = pts[a], [bx, by] = pts[b];
    const dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy;
    for (let i = a + 1; i < b; i++) {
      let t = L2 > 0 ? ((pts[i][0] - ax) * dx + (pts[i][1] - ay) * dy) / L2 : 0;
      t = Math.max(0, Math.min(1, t));
      const d = Math.hypot(pts[i][0] - (ax + dx * t), pts[i][1] - (ay + dy * t));
      if (d > maxD) { maxD = d; maxI = i; }
    }
    if (maxD > eps) { keep[maxI] = 1; stack.push([a, maxI], [maxI, b]); }
  }
  return pts.filter((_, i) => keep[i]);
}
const profOf = e => profileFor(e.way.rank, e.way.link, e.way.oneway, e.lanes, e.turn);
/// An edge's TAPR offset at arc position s (see edgeOffset in lineClean).
export const offsetAt = (r, s) => !r ? 0 : r.o0 === r.o1 ? r.o0 : r.o0 + (r.o1 - r.o0) * SMOOTH((s - r.t0) / (r.t1 - r.t0));

// ------------------------------------------------------------------ graph
function nodeEdgesOf(edges, nn) {
  const ne = Array.from({ length: nn }, () => []);
  for (const e of edges) { ne[e.a].push(e); if (e.b !== e.a) ne[e.b].push(e); }
  return ne;
}

/// CHAINS: one road through its nodes. At each node the arms pair up as
/// continuations - same one-way-ness (one arriving, one leaving on a one-way
/// road), turning less than 45 degrees, and the same name unless it is a
/// plain 2-arm node - straightest pair first. A chain is a list of
/// { e, fwd } (fwd = drawn a->b); a one-way chain runs in travel order.
export function buildChains(edges, nodes) {
  const nodeEdges = nodeEdgesOf(edges, nodes.length);
  const contA = new Array(edges.length).fill(null), contB = new Array(edges.length).fill(null);
  for (let n = 0; n < nodes.length; n++) {
    const arms = nodeEdges[n].filter(e => e.a !== e.b && !e.way.roundabout).map(e => ({ e, atA: e.a === n, dir: outDir(e, n) }));
    if (arms.length < 2) continue;
    const pairs = [];
    for (let i = 0; i < arms.length; i++) for (let j = i + 1; j < arms.length; j++) {
      const p = arms[i], q = arms[j], wp = p.e.way, wq = q.e.way;
      if (wp.oneway !== wq.oneway) continue;
      if (wp.oneway && p.atA === q.atA) continue;          // one must arrive, one leave
      if (arms.length > 2 && !(wp.name && wp.name === wq.name) && !(wp.link && wq.link)) continue;
      const d = p.dir[0] * q.dir[0] + p.dir[1] * q.dir[1];
      if (d > -0.7) continue;
      pairs.push([d, p, q]);
    }
    pairs.sort((x, y) => x[0] - y[0]);
    const used = new Set();
    for (const [, p, q] of pairs) {
      if (used.has(p) || used.has(q)) continue;
      used.add(p); used.add(q);
      (p.atA ? contA : contB)[p.e.id] = { e: q.e, atA: q.atA };
      (q.atA ? contA : contB)[q.e.id] = { e: p.e, atA: p.atA };
    }
  }
  const chainOf = new Int32Array(edges.length).fill(-1);
  const chains = [];
  const walk = (e0, fwd0) => {
    const c = [];
    let e = e0, fwd = fwd0;
    while (e && chainOf[e.id] < 0) {
      chainOf[e.id] = chains.length;
      c.push({ e, fwd });
      const nx = fwd ? contB[e.id] : contA[e.id];
      if (!nx) break;
      e = nx.e; fwd = nx.atA;
    }
    chains.push(c);
  };
  // chain ends first (a one-way chain from its entry), then what is left (loops)
  for (const e of edges) {
    if (chainOf[e.id] >= 0 || e.way.roundabout || e.a === e.b) continue;
    if (!contA[e.id]) walk(e, true);
    else if (!contB[e.id] && !e.way.oneway) walk(e, false);
  }
  for (const e of edges) if (chainOf[e.id] < 0 && !e.way.roundabout && e.a !== e.b) walk(e, true);
  return { chains, chainOf, nodeEdges };
}

// ------------------------------------------------------------ turn:lanes
const splitLanes = s => (s || '').split('|').map(x => x.trim());
/// Which side of its direction a lane gained by `wide` over `narrow` opens
/// on, from turn:lanes; null when the tags do not say.
function tagSide(wideTl, narrowTl) {
  const W = splitLanes(wideTl), N = splitLanes(narrowTl);
  if (!wideTl) return null;
  const has = (a, k, w) => (a[k] || '').split(';').some(x => x === w || x === 'slight_' + w || x === 'sharp_' + w);
  const lW = has(W, 0, 'left'), rW = has(W, W.length - 1, 'right');
  const lN = narrowTl ? has(N, 0, 'left') : false, rN = narrowTl ? has(N, N.length - 1, 'right') : false;
  const mergeR = (W[W.length - 1] || '').includes('merge_to_left'), mergeL = (W[0] || '').includes('merge_to_right');
  const left = (lW && !lN) || mergeL, right = (rW && !rN) || mergeR;
  if (left && !right) return 'L';
  if (right && !left) return 'R';
  return null;
}

/// Lanes per direction of a two-way edge, in the edge's a->b frame:
/// [forward, backward, centre].
function splitTwoWay(e) {
  const w = e.way, n = profOf(e).lanes;
  const t = n % 2;
  if (e.lanes === w.lanes && Number.isFinite(w.lf) && Number.isFinite(w.lb) && w.lf + w.lb + t === n) return [w.lf, w.lb, t];
  return [(n - t) / 2, (n - t) / 2, t];
}

// ================================================================== main
export function lineClean(ctx) {
  const { ways, edges, nodes, rawWays, rawNodes, toX, toZ, uptown, log } = ctx;
  const stats = {};
  const review = [];   // { kind, edge, x, z, note }

  // ---------------------------------------------------- 1. tagged nodes
  const ctlKind = new Map();
  for (const n of rawNodes) {
    const h = (n.tags || {}).highway;
    const k = h === 'traffic_signals' ? 4 : h === 'stop' ? 2 : h === 'give_way' ? 1 : 0;
    if (k) ctlKind.set(n.id, k);
  }
  const tagged = [];
  for (const w of ways) {
    let s = 0;
    for (let k = 0; k < w.nodes.length; k++) {
      if (k) s += dist(w.geom[k], w.geom[k - 1]);
      const kind = ctlKind.get(w.nodes[k]);
      if (kind) tagged.push({ wayId: w.id, nodeId: w.nodes[k], kind, rawS: s, x: w.geom[k][0], z: w.geom[k][1], w });
    }
  }
  // pins: every raw vertex two OSM highway ways share, fetched and kept or not
  const useCount = new Map();
  const posOf = new Map();
  for (const w of rawWays) {
    if (!w.tags || !w.tags.highway || !w.nodes) continue;
    const seen = new Set();
    for (let k = 0; k < w.nodes.length; k++) {
      const id = w.nodes[k];
      if (seen.has(id)) continue;
      seen.add(id);
      useCount.set(id, (useCount.get(id) || 0) + 1);
      if (!posOf.has(id) && w.geometry && w.geometry[k]) posOf.set(id, [toX(w.geometry[k].lon), toZ(w.geometry[k].lat)]);
    }
  }
  const pinned = new Set();
  for (const [id, c] of useCount) if (c >= 2 && posOf.has(id)) { const p = posOf.get(id); pinned.add(p[0] + ',' + p[1]); }
  const isPinned = p => pinned.has(p[0] + ',' + p[1]);
  stats.tagged_nodes = tagged.length;
  stats.pinned_vertices = pinned.size;

  for (const e of edges) { e.lanes = e.way.lanes; e.turn = e.way.turn; }

  // ---------------------------------------------------- 2. lane counts
  let { chains, chainOf } = buildChains(edges, nodes);
  const laneFix = { flicker: 0, short: 0, inferred: 0 }, laneFixM = { flicker: 0, short: 0, inferred: 0 };
  let keptTagged = 0;
  const runsOf = chain => {
    const runs = [];
    for (let i = 0; i < chain.length; i++) {
      const e = chain[i].e, key = profOf(e).key;
      const last = runs[runs.length - 1];
      // own: the count is this piece's OWN lanes= tag (not a neighbour's, lent by a fix)
      const own = e.way.tagged && !e.laneFix;
      if (last && last.key === key) { last.items.push(i); last.len += e.len; last.tagged = last.tagged && e.way.tagged; last.own = last.own || own; last.bridge = last.bridge || e.way.bridge; }
      else runs.push({ key, items: [i], len: e.len, tagged: e.way.tagged, own, bridge: e.way.bridge, rep: e, repFwd: chain[i].fwd });
    }
    return runs;
  };
  for (const chain of chains) {
    for (let guard = 0; guard < 200; guard++) {
      const runs = runsOf(chain);
      let fixed = false;
      for (let i = 1; i + 1 < runs.length && !fixed; i++) {
        const A = runs[i - 1], B = runs[i], C = runs[i + 1];
        if (A.key !== C.key) continue;
        // A piece whose count is its OWN lanes= tag is data - a turn bay, an
        // auxiliary lane, a real lane drop - and TAPR draws it on ONE side;
        // it is never deleted (review 1: 2,194 tagged edges, 1,231 of them
        // naming the lane in turn:lanes, lost it). Only untagged flickers go.
        if (B.own) { if (B.len < 100 || B.len < 2 * floorOf(B.rep.way)) keptTagged++; continue; }
        let why = null;
        if (B.len < 100) why = 'flicker';
        else if (B.len < 2 * floorOf(B.rep.way)) why = 'short';
        else if (!B.tagged && !B.bridge && A.tagged && C.tagged) why = 'inferred';
        if (!why) continue;
        const srcRun = A.len >= C.len ? A : C, src = srcRun.rep;
        // the donor's line set goes with its count (roads pass L2, lib/lineset.mjs):
        // its lanes each way, in this piece's own a->b frame
        for (const k of B.items) { chain[k].e.lanes = src.lanes; chain[k].e.turn = src.turn; chain[k].e.laneFix = why; chain[k].e.lsetFrom = { e: src, flip: chain[k].fwd !== srcRun.repFwd }; }
        laneFix[why]++; laneFixM[why] += B.len;
        fixed = true;
      }
      if (!fixed) break;
    }
    // untagged bridges keep the default: listed, never changed
    for (const { e } of chain) if (e.way.bridge && !e.way.tagged) {
      const nb = chain.filter(c => c.e !== e && c.e.way.tagged).map(c => profOf(c.e).key);
      if (nb.length && !nb.includes(profOf(e).key))
        review.push({ kind: 'untagged-bridge', edge: e.id, way: e.way.id, x: e.pts[e.pts.length >> 1][0], z: e.pts[e.pts.length >> 1][1], note: `${profOf(e).key} (default) between ${[...new Set(nb)].join('/')}` });
    }
  }
  stats.lane_fixes = laneFix;
  stats.lane_kept_tagged = keptTagged;
  stats.lane_below_tag = edges.filter(e => e.way.tagged && e.laneFix && profOf(e).lanes < profileFor(e.way.rank, e.way.link, e.way.oneway, e.way.lanes, e.way.turn).lanes).length;
  stats.lane_fix_km = Object.fromEntries(Object.entries(laneFixM).map(([k, v]) => [k, +(v / 1000).toFixed(2)]));

  // ---------------------------------------------------- doglegs, counted
  const nodeDeg = new Int32Array(nodes.length);
  for (const e of edges) { nodeDeg[e.a]++; nodeDeg[e.b]++; }
  /// Strands: edges joined through plain 2-arm nodes (not links, not roundabouts).
  function strands() {
    const ne = nodeEdgesOf(edges, nodes.length);
    const seen = new Uint8Array(edges.length);
    const out = [];
    const ok = e => !e.way.roundabout && !e.way.link && e.a !== e.b;
    const through = n => ne[n].length === 2 && ne[n][0] !== ne[n][1] && ok(ne[n][0]) && ok(ne[n][1]);
    for (const e0 of edges) {
      if (seen[e0.id] || !ok(e0)) continue;
      // walk back to a strand end
      let e = e0, atStart = e0.a, guard = 0;
      while (through(atStart) && guard++ < 10000) {
        const o = ne[atStart][0] === e ? ne[atStart][1] : ne[atStart][0];
        if (o === e0) break;
        atStart = o.a === atStart ? o.b : o.a; e = o;
      }
      // e is the first edge; atStart its outer end
      const list = [];
      let n = atStart, cur = e;
      guard = 0;
      while (cur && !seen[cur.id] && guard++ < 10000) {
        seen[cur.id] = 1;
        const fwd = cur.a === n;
        list.push({ e: cur, fwd });
        n = fwd ? cur.b : cur.a;
        if (!through(n)) break;
        cur = ne[n][0] === cur ? ne[n][1] : ne[n][0];
      }
      if (list.length) out.push({ list, startNode: atStart, endNode: n });
    }
    return out;
  }
  /// The strand as one polyline with a back-reference per vertex.
  function strandPoly(st) {
    const V = [];
    st.list.forEach(({ e, fwd }, i) => {
      const P = e.pts, n = P.length;
      for (let q = 0; q < n; q++) {
        const k = fwd ? q : n - 1 - q;
        if (i > 0 && q === 0) continue;               // the shared node, already added
        const isNode = q === 0 || q === n - 1;
        V.push({ p: P[k], e, k, node: isNode ? (k === 0 ? e.a : e.b) : -1 });
      }
    });
    return V;
  }
  function findDoglegs(st, fix) {
    let found = 0;
    const jogs = [];
    const endJ = n => nodeDeg[n] >= 3;
    const d0 = endJ(st.startNode) ? 15 : 0, d1 = endJ(st.endNode) ? 15 : 0;
    /// The Z at vertices i, i+1: its jog in metres, or -1.
    const zAt = (V, s, L, i) => {
      if (s[i] < d0 || L - s[i + 1] < d1 || s[i + 1] - s[i] > 20) return -1;
      const t1 = signedTurn(V[i - 1].p, V[i].p, V[i + 1].p), t2 = signedTurn(V[i].p, V[i + 1].p, V[i + 2].p);
      if (Math.abs(t1) * DEG < 8 || Math.abs(t2) * DEG < 8 || Math.sign(t1) === Math.sign(t2)) return -1;
      if (isPinned(V[i].p) || isPinned(V[i + 1].p)) return -1;
      const ux = V[i].p[0] - V[i - 1].p[0], uz = V[i].p[1] - V[i - 1].p[1], ul = Math.hypot(ux, uz) || 1;
      const jog = Math.abs(((V[i + 1].p[0] - V[i].p[0]) * uz - (V[i + 1].p[1] - V[i].p[1]) * ux) / ul);
      return jog > 4 ? -1 : jog;       // over 4 m: a real S-bend or an offset crossing
    };
    for (let guard = 0; guard < 50; guard++) {
      const V = strandPoly(st);
      if (V.length < 4) break;
      const s = [0];
      for (let i = 1; i < V.length; i++) s.push(s[i - 1] + dist(V[i].p, V[i - 1].p));
      const L = s[s.length - 1];
      if (!fix) {
        for (let i = 1; i + 2 < V.length; i++) { const j = zAt(V, s, L, i); if (j >= 0) { found++; jogs.push(j); i++; } }
        return { found, jogs };
      }
      let hit = -1;
      for (let i = 1; i + 2 < V.length && hit < 0; i++) { const j = zAt(V, s, L, i); if (j >= 0) { hit = i; jogs.push(j); } }
      if (hit < 0) break;
      found++;
      // fix: drop the two vertices, or put a 2-arm node back on the line
      const a = V[hit - 1].p, b = V[hit + 2].p;
      const segL = s[hit + 2] - s[hit - 1] || 1;
      for (const j of [hit, hit + 1]) {
        const v = V[j];
        if (v.node < 0) { v.e.pts[v.k] = null; continue; }
        const t = (s[j] - s[hit - 1]) / segL;
        const np = [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
        nodes[v.node] = np;
        for (const { e } of st.list) {
          if (e.a === v.node) e.pts[0] = np.slice();
          if (e.b === v.node) e.pts[e.pts.length - 1] = np.slice();
        }
      }
      for (const { e } of st.list) { e.pts = e.pts.filter(Boolean); e.len = polyLen(e.pts); }
    }
    return { found, jogs };
  }
  const countDoglegs = () => {
    let n = 0, ge1 = 0;
    for (const st of strands()) { const r = findDoglegs(st, false); n += r.found; ge1 += r.jogs.filter(j => j >= 1).length; }
    return { all: n, jog_ge_1m: ge1 };
  };
  stats.doglegs_raw = countDoglegs();

  // ---------------------------------------------------- 3. simplify
  // Held: a deck or tunnel is left alone (its vertices place its rails and
  // approaches), and at an end where the lane count changes the first OSM
  // piece keeps its length. Until WP-11b draws TAPR the builder tapers a
  // change over the piece next to the node (0.9 of it), so dropping that
  // piece's far vertex lengthened the taper - a 49 m mw3-mw4-mw3 deck on
  // I-277 then never reached full width and its rails stood in the lanes
  // (the WP-10 review's audit).
  const holdEnd = new Uint8Array(edges.length);   // 1: a end, 2: b end, 4: whole edge
  for (const chain of chains) for (let i = 1; i < chain.length; i++) {
    if (profOf(chain[i].e).key === profOf(chain[i - 1].e).key) continue;
    const p = chain[i - 1], q = chain[i];
    holdEnd[p.e.id] |= p.fwd ? 2 : 1;
    holdEnd[q.e.id] |= q.fwd ? 1 : 2;
  }
  for (const e of edges) if (e.way.bridge || e.way.tunnel || e.way.level !== 0) holdEnd[e.id] |= 4;
  stats.simplify_held = { taper_ends: holdEnd.reduce((a, v) => a + ((v & 3) ? 1 : 0), 0), structure: holdEnd.reduce((a, v) => a + ((v & 4) ? 1 : 0), 0) };
  const DP_M = 0.5;   // the plan's SimplifyEpsM (SmoothRules: the gate's WAVE rule follows it)
  let ptsBefore = 0, ptsAfter = 0;
  for (const e of edges) {
    ptsBefore += e.pts.length;
    if ((holdEnd[e.id] & 4) || e.way.link || e.way.roundabout || e.pts.length <= 2) { ptsAfter += e.pts.length; continue; }
    const P = e.pts, n = P.length;
    const held = k => isPinned(P[k]) || (k === 1 && (holdEnd[e.id] & 1)) || (k === n - 2 && (holdEnd[e.id] & 2));
    const Q = [P[0]];
    for (let k = 1; k < n - 1; k++) {
      if (!held(k) && (dist(P[k], Q[Q.length - 1]) < 2 || dist(P[k], P[n - 1]) < 2)) continue;
      Q.push(P[k]);
    }
    Q.push(P[n - 1]);
    // Douglas-Peucker between pinned vertices
    const out = [Q[0]];
    let from = 0;
    const heldQ = new Set();
    if (holdEnd[e.id] & 1 && n > 2) heldQ.add(P[1]);
    if (holdEnd[e.id] & 2 && n > 2) heldQ.add(P[n - 2]);
    for (let k = 1; k < Q.length; k++) {
      if (k === Q.length - 1 || isPinned(Q[k]) || heldQ.has(Q[k])) {
        const part = DP_M > 0 ? rdp(Q.slice(from, k + 1), DP_M) : Q.slice(from, k + 1);
        for (let q = 1; q < part.length; q++) out.push(part[q]);
        from = k;
      }
    }
    e.pts = out;
    e.len = polyLen(out);
    ptsAfter += out.length;
  }
  stats.points = { before: ptsBefore, after_simplify: ptsAfter };

  // ---------------------------------------------------- 4. doglegs
  {
    let fixed = 0, jogs = [];
    for (const st of strands()) { const r = findDoglegs(st, true); fixed += r.found; jogs.push(...r.jogs); }
    stats.doglegs_fixed = { all: fixed, jog_ge_1m: jogs.filter(j => j >= 1).length, worst_jog_m: +Math.max(0, ...jogs).toFixed(2) };
    stats.doglegs_after = countDoglegs();
  }

  // ---------------------------------------------------- 5. TAPR
  ({ chains, chainOf } = buildChains(edges, nodes));
  const tapr = [];
  /// Each edge's ribbon centre off its OSM line (+ = left of a->b) as
  /// o0 + (o1 - o0) * smoothstep((s - t0) / (t1 - t0)), s the edge's own arc
  /// position (t0, t1 may lie beyond it: one shifting taper over several
  /// edges); null = on its line.
  const edgeOffset = new Array(edges.length).fill(null);
  const tStats = { transitions: 0, tagged: 0, bays: 0, atJunction: 0, absorbed: 0, shortened: 0, oneSided: 0, symmetric: 0, reanchored: 0, recentred: 0 };
  for (const chain of chains) {
    const runs = runsOf(chain);
    if (runs.length < 2) continue;
    // per transition: which ribbon edge of the WIDER run moves, in CHAIN frame
    const W = runs.map(r => profOf(r.rep).width);
    const sides = new Array(runs.length - 1).fill(null), srcs = new Array(runs.length - 1).fill(0), bayF = new Array(runs.length - 1).fill(false);
    for (let i = 0; i + 1 < runs.length; i++) {
      const X = runs[i], Y = runs[i + 1];
      const gain = W[i + 1] > W[i];
      const wideRun = gain ? Y : X, narrowRun = gain ? X : Y;
      const wideCI = gain ? Y.items[0] : X.items[X.items.length - 1];
      const narrowCI = gain ? X.items[X.items.length - 1] : Y.items[0];
      const we = chain[wideCI].e, weF = chain[wideCI].fwd, nEdge = chain[narrowCI].e;
      // direction INTO the wide run, relative to the chain: +1 along, -1 against
      const into = gain ? 1 : -1;
      let side = null, src = 0, bay = false;
      if (we.way.oneway) {
        // travel is chain order; a drop seen against travel is still judged in travel
        const ts = tagSide(we.way.tl, nEdge.way.tl);
        if (ts) { side = ts; src = 2; } else { side = 'R'; src = 0; }
        bay = !!ts && ts === 'L';
      } else {
        // two-way: lanes per direction in the CHAIN frame
        // lanes per direction in the INTO frame ("forward" = the direction
        // travelling from the narrow run into the wide one; its lanes are on
        // the right)
        const toInto = (e, fwd) => { const [f, b, t] = splitTwoWay(e); return (fwd === (into === 1)) ? [f, b, t] : [b, f, t]; };
        const [fN, bN, tN] = toInto(nEdge, chain[narrowCI].fwd), [fW, bW, tW] = toInto(we, weF);
        const gf = fW - fN, gb = bW - bN, gt = tW - tN;
        // tags of the wide way, in the into-frame
        const tlF = (into === 1) === weF ? we.way.tlf : we.way.tlb, tlB = (into === 1) === weF ? we.way.tlb : we.way.tlf;
        const bayFwd = (tlF || '').startsWith('left'), bayBwd = (tlB || '').startsWith('left');
        // sides in the INTO frame: the forward lanes are on the right
        if (gt > 0 && gf <= 0 && gb <= 0) {
          bay = true;
          // a centre left-turn bay: the direction that gains it keeps its
          // lanes straight, so the ribbon edge on the OTHER direction's side moves
          const owner = bayFwd && !bayBwd ? 'F' : bayBwd && !bayFwd ? 'B' : 'F';
          src = bayFwd || bayBwd ? 2 : 1;
          // the right edge fixed keeps the forward lanes; the left, the backward
          const movesIf = { L: bN, R: fN };     // through lanes that move
          side = owner === 'F' ? 'L' : 'R';
          const other = side === 'L' ? 'R' : 'L';
          if (movesIf[other] < movesIf[side]) side = other;    // fewest through lanes move
        } else if (gf > 0 && gb <= 0) { side = 'R'; src = Number.isFinite(we.way.lf) ? 3 : 0; }
        else if (gb > 0 && gf <= 0) { side = 'L'; src = Number.isFinite(we.way.lf) ? 3 : 0; }
        else { side = 'R'; src = 0; }       // both gain: one side only, the right of the direction into it
        if (into === -1) side = side === 'L' ? 'R' : 'L';   // into-frame -> chain frame
      }
      sides[i] = side; srcs[i] = src; bayF[i] = bay;
      tStats.transitions++; if (src >= 2) tStats.tagged++; if (bay) tStats.bays++;
    }
    // A run wider than BOTH neighbours opens and closes on ONE side (a bay
    // or a widening): the entry decides unless only the exit is tagged.
    const peak = i => i > 0 && i + 1 < runs.length && W[i] > W[i - 1] && W[i] > W[i + 1];
    const locked = new Array(runs.length - 1).fill(false);   // set by a bay's other, tagged end
    for (let i = 1; i + 1 < runs.length; i++) {
      if (!peak(i)) continue;
      const a = i - 1, b = i;
      if (srcs[b] >= 2 && srcs[a] < 2) { sides[a] = sides[b]; locked[a] = true; } else { sides[b] = sides[a]; locked[b] = true; }
    }
    // OFFSETS (the ribbon centre off the OSM line, chain-left +). Across a
    // transition the FIXED edge is continuous, so the run after it starts at
    // the run before's offset -+ dw/2, and the offset HOLDS along the run:
    // the through lanes keep their position (A8 rule 1). No mid-block
    // shifting taper (review 3: it moved every lane 1.8-3.7 m sideways in
    // the middle of 1,478 blocks - a swerve, not a lane). A bay closes on the
    // side it opened, so it comes back to where it started; only a road that
    // keeps widening one way carries more than one lane off its OSM line, and
    // that is re-anchored at the next junction mouth (inside the junction
    // box, where no lane line runs), never mid-block.
    // At a JUNCTION MOUTH a lane the tags do not place opens on the side that
    // brings the ribbon back toward its OSM line (A8 rule 2: the side chosen
    // per spot): the lane appears inside the junction, no taper, and every
    // through lane still lines up across it. So a carried offset only lives
    // from a mid-block change to the next junction.
    const offItem = new Array(chain.length).fill(0);
    {
      let cur = 0;
      const startNode = k => chain[k].fwd ? chain[k].e.a : chain[k].e.b;
      for (let i = 0; i < runs.length; i++) {
        if (i > 0) {
          const t = i - 1, dw = W[i] - W[i - 1];
          if (srcs[t] < 2 && !locked[t] && nodeDeg[startNode(runs[i].items[0])] >= 3) {
            const cR = cur - dw / 2, cL = cur + dw / 2;
            const pick = Math.abs(cR) < Math.abs(cL) - 0.01 ? 'R' : Math.abs(cL) < Math.abs(cR) - 0.01 ? 'L' : sides[t];
            if (pick !== sides[t]) { sides[t] = pick; tStats.recentred++; }
            if (peak(i) && locked[i]) sides[i] = pick;   // the bay closes on the side it opened
          }
          // the moving edge belongs to the wider run; the other edge is fixed
          cur = sides[t] === 'R' ? cur - dw / 2 : cur + dw / 2;
        }
        for (const k of runs[i].items) {
          if (Math.abs(cur) > LANE_REANCHOR && nodeDeg[startNode(k)] >= 3) { cur = 0; tStats.reanchored++; }
          offItem[k] = cur;
        }
      }
    }
    for (let k = 0; k < chain.length; k++) {
      const { e, fwd } = chain[k];
      const o = fwd ? offItem[k] : -offItem[k];     // to the edge's own a->b frame (left +)
      edgeOffset[e.id] = { o0: o, o1: o, t0: 0, t1: 1 };
    }
    // the records, one per transition, on the wide run's edge at the node
    for (let i = 0; i + 1 < runs.length; i++) {
      const gain = W[i + 1] > W[i];
      const wide = gain ? runs[i + 1] : runs[i];
      const ci = gain ? wide.items[0] : wide.items[wide.items.length - 1];
      const { e, fwd } = chain[ci];
      const dw = Math.abs(W[i + 1] - W[i]);
      if (dw < 0.05) continue;
      const node = gain ? (fwd ? e.a : e.b) : (fwd ? e.b : e.a);
      const end = e.a === node ? 0 : 1;
      const wideWider = (i + 2 < runs.length && gain && W[i + 2] < W[i + 1]) || (i > 0 && !gain && W[i - 1] < W[i]);
      let len = mutcdLen(dw, mphOf(e.way));
      if (bayF[i]) len = Math.min(55, Math.max(30, len));
      const floor = floorOf(e.way);
      len = Math.max(len, floor);
      const room = wideWider ? wide.len / 2 : wide.len;
      let flags = (bayF[i] ? 1 : 0) | (gain ? 0 : 4) | (srcs[i] < 2 ? 8 : 0);
      if (nodeDeg[node] >= 3) {
        // at a JUNCTION the lane opens or ends full width at the mouth - a
        // turn bay runs full width to the stop line (review 2: 1,010 changes
        // on a junction node carried a taper that pinched the bay to nothing
        // where cars turn). Tapers only at 2-arm nodes.
        flags |= 2; len = 0; tStats.atJunction++;
      } else if (len > room) {
        if (room >= floor) { len = room; tStats.shortened++; }
        else { flags |= 2; len = Math.max(0, room); tStats.absorbed++; }
      }
      // side in the wide EDGE's a->b frame
      const sideE = fwd ? sides[i] : (sides[i] === 'L' ? 'R' : 'L');
      tStats.oneSided++;
      tapr.push({ edge: e.id, end, node, side: sideE === 'L' ? 0 : 1, src: srcs[i], flags, dw, len, room, off: offsetAt(edgeOffset[e.id], end ? e.len : 0) });
    }
  }
  stats.tapr = tStats;

  // ---------------------------------------------------- 6. PARA
  const paraOut = paraResolve({ edges, nodes, chains, chainOf, edgeOffset, isPinned, stats, review, uptown });

  // ---------------------------------------------------- 7. WP-11 fillets
  // every 2-arm node and free vertex rounded at its class radius, arcs
  // densified by sagitta (lib/fillet.mjs has the rules)
  if (ctx.fillet) {
    let endGap = 0;
    for (const e of edges) endGap = Math.max(endGap, dist(e.pts[0], nodes[e.a]), dist(e.pts[e.pts.length - 1], nodes[e.b]));
    stats.fillet_end_gap_m = +endGap.toFixed(3);
    const RANK = ['local', 'tertiary', 'secondary', 'primary', 'trunk', 'motorway'];
    const hwOf = e => {
      const p = profOf(e), r = edgeOffset[e.id];
      return p.lanes * 3.6576 / 2 + Math.max(p.shl, p.shr) + (r ? Math.max(Math.abs(r.o0), Math.abs(r.o1)) : 0);
    };
    const F = ctx.fillet;
    if (process.env.PSX_FILLET_DUMP) {
      // the graph as the fillet step gets it, for iterating on lib/fillet.mjs alone
      writeFileSync(process.env.PSX_FILLET_DUMP, JSON.stringify({
        nodes, edges: edges.map(e => ({ id: e.id, a: e.a, b: e.b, pts: e.pts, len: e.len, hw: hwOf(e), way: { link: e.way.link, rank: e.way.rank, oneway: e.way.oneway }, pw: profOf(e).width,
          rf: Math.max(F.rMinFor(RANK[e.way.rank] + (e.way.link ? '_link' : '')), hwOf(e) + F.innerEdgeMinR), em: emaxOf(e.way) })) }));
    }
    const out = filletGraph(edges, nodes, {
      rFloor: e => Math.max(F.rMinFor(RANK[e.way.rank] + (e.way.link ? '_link' : '')), hwOf(e) + F.innerEdgeMinR),
      emax: e => emaxOf(e.way), hw: hwOf,
      eps: F.eps, chordCap: F.chordCap, collinearDeg: F.collinearDeg, innerMin: F.innerEdgeMinR,
      through: mitredThrough(edges, nodes, e => profOf(e).width / 2),
    });
    stats.fillet = out.stats;
    for (const r of out.review) review.push(r);
  }

  // ---------------------------------------------------- 1b. tagged nodes -> (edge, s)
  {
    const byWay = new Map();
    for (const e of edges) { let l = byWay.get(e.way.id); if (!l) byWay.set(e.way.id, l = []); l.push(e); }
    let lost = 0;
    for (const t of tagged) {
      const l = byWay.get(t.wayId) || [];
      let best = null;
      for (const e of l) {
        const P = e.pts; let acc = 0;
        for (let k = 1; k < P.length; k++) {
          const ax = P[k - 1][0], az = P[k - 1][1], dx = P[k][0] - ax, dz = P[k][1] - az, L2 = dx * dx + dz * dz;
          let u = L2 > 0 ? ((t.x - ax) * dx + (t.z - az) * dz) / L2 : 0; u = Math.max(0, Math.min(1, u));
          const d = Math.hypot(t.x - (ax + dx * u), t.z - (az + dz * u));
          if (!best || d < best.d) best = { d, e, s: acc + Math.sqrt(L2) * u };
          acc += Math.sqrt(L2);
        }
      }
      if (!best) { lost++; continue; }
      t.edge = best.e.id; t.s = best.s; t.moved = best.d;
    }
    stats.tagged_projected = { nodes: tagged.length - lost, lost, moved_max_m: +Math.max(0, ...tagged.filter(t => t.moved !== undefined).map(t => t.moved)).toFixed(2) };
  }

  log && log(stats);
  return { stats, review, tapr, para: paraOut, tagged: tagged.filter(t => t.edge !== undefined), edgeOffset };
}

// ================================================================== PARA
/// Two carriageways whose pavements overlap, or leave less than the class gap
/// (plan A8 rule 4: "never one merged wide road, never overlapping or
/// touching pavements"), are moved apart: each chain gets a lateral offset
/// U(s) - the deficit, max-filtered, rate-limited by the MUTCD shifting taper
/// for its speed and smoothed - that is zero at the nodes where the pair
/// really meets (a split, a merge: the gore and the median taper there are
/// WP-11's and WP-18b's). Three passes; what is left outside those attach
/// zones is the review list.
function paraResolve({ edges, nodes, chains, chainOf, edgeOffset, isPinned, stats, review, uptown }) {
  const STEP = 5, CELL = 32;
  const isToll = e => !!e.way.toll;
  const gapOf = (E, F) => (E.way.oneway && F.way.oneway && E.way.name && E.way.name === F.way.name) || isToll(E) !== isToll(F) ? 1.2 : 1.0;
  /// Facing half width as R4 draws it (WP-10, 11 and 11b ship together):
  /// the lanes centred on the OSM line with the shoulder of THAT side (a
  /// freeway's 1.2 m inside, 3 m outside), plus the TAPR offset toward it.
  const halfFacing = (e, left, s) => {
    const p = profOf(e);
    const h = p.lanes * 3.6576 / 2 + (left ? p.shl : p.shr);
    const o = offsetAt(edgeOffset[e.id], s);
    return h + Math.max(0, left ? o : -o);
  };
  // who moves: a ramp off its mainline, a lower class off a higher one;
  // otherwise both, half each (the express lanes sit between the general
  // lanes and the other direction's express lanes, so the general lanes
  // must give way too)
  const shareOf = (E, F) => {
    if (E.way.link !== F.way.link) return E.way.link ? 1 : 0;
    if (E.way.rank !== F.way.rank) return E.way.rank < F.way.rank ? 1 : 0;
    return 0.5;
  };
  /// The most a chain may be moved: mapping error is metres, not lanes. A
  /// pair needing more is structural and goes on the review list.
  const capOf = e => e.way.rank >= 4 ? 6.0 : 4.0;
  let convAny = new Set();
  const inCore = (x, z) => Math.abs(x - uptown[0]) <= 4000 && Math.abs(z - uptown[1]) <= 4000;

  // convergence nodes: two arms leaving one node within 50 degrees of each
  // other (a divided road's split, a ramp's merge, a fork). The pair is
  // exempt near it, out to where its pavements would part on their own.
  // Found ONCE, on the lines as mapped: a pass that pushed a split apart
  // would otherwise sharpen it past the threshold and lose its own pin.
  const chainIdx = new Int32Array(edges.length).fill(-1);
  chains.forEach(c => c.forEach((it, k) => { chainIdx[it.e.id] = k; }));
  /// The line out of node n along e and on along e's chain, up to maxLen metres.
  function walkOut(e0, n, maxLen) {
    const ci = chainOf[e0.id];
    const items = ci >= 0 ? chains[ci] : [{ e: e0, fwd: e0.a === n }];
    let k = ci >= 0 ? chainIdx[e0.id] : 0;
    const it = items[k];
    const dir = (it.fwd ? it.e.a : it.e.b) === n ? 1 : -1;
    const pts = []; let L = 0;
    for (; k >= 0 && k < items.length && L < maxLen; k += dir) {
      const { e, fwd } = items[k];
      const P = (fwd === (dir > 0)) ? e.pts : e.pts.slice().reverse();
      for (let q = pts.length ? 1 : 0; q < P.length && L < maxLen; q++) { if (pts.length) L += dist(P[q], pts[pts.length - 1]); pts.push(P[q]); }
    }
    return pts;
  }
  /// Arc distance along A (5 m steps) past which A stays `need` clear of B
  /// to the end of the walk; null if it is not clear there.
  function partAt(A, B, need) {
    let s = 0, lastClose = 0, clear = false;
    for (let k = 1; k < A.length; k++) {
      const segL = dist(A[k], A[k - 1]), m = Math.max(1, Math.ceil(segL / 5));
      for (let q = 1; q <= m; q++) {
        const t = q / m, x = A[k - 1][0] + (A[k][0] - A[k - 1][0]) * t, z = A[k - 1][1] + (A[k][1] - A[k - 1][1]) * t;
        let d = Infinity;
        for (let j = 1; j < B.length; j++) {
          const ax = B[j - 1][0], az = B[j - 1][1], dx = B[j][0] - ax, dz = B[j][1] - az, L2 = dx * dx + dz * dz;
          let u = L2 > 0 ? ((x - ax) * dx + (z - az) * dz) / L2 : 0; u = Math.max(0, Math.min(1, u));
          d = Math.min(d, Math.hypot(x - ax - dx * u, z - az - dz * u));
        }
        clear = d >= need;
        if (!clear) lastClose = s + segL * t;
      }
      s += segL;
    }
    return clear ? lastClose : null;
  }
  function convergences() {
    const ne = nodeEdgesOf(edges, nodes.length);
    const conv = [];
    for (let n = 0; n < nodes.length; n++) {
      const arms = ne[n].filter(e => e.a !== e.b);
      for (let i = 0; i < arms.length; i++) for (let j = i + 1; j < arms.length; j++) {
        const di = outDir(arms[i], n), dj = outDir(arms[j], n);
        const c = di[0] * dj[0] + di[1] * dj[1];
        if (chainOf[arms[i].id] === chainOf[arms[j].id]) continue;
        // every two chains that share a node are pinned there (moving the
        // node would drag the other along); the exemption reaches as far as
        // their pavements take to part: need / sin(angle), 10 m past a right angle
        const th = Math.acos(Math.max(-1, Math.min(1, c)));
        const offAbs = e => edgeOffset[e.id] ? Math.abs(edgeOffset[e.id].o0) : 0;   // a TAPR offset may face the other way
        const need = profOf(arms[i]).width / 2 + offAbs(arms[i]) + profOf(arms[j]).width / 2 + offAbs(arms[j]) + 1.2;
        const big = arms[i].way.rank >= 4 || arms[j].way.rank >= 4 || arms[i].way.link || arms[j].way.link;
        let R = th >= Math.PI / 2 ? 10 : Math.min(big ? 300 : 150, need / Math.sin(Math.max(th, 3 / DEG)) + 10);
        // ...measured, where the two lines as mapped CURVE together (a skewed
        // junction, a ramp's gore that bends in): walk both out of the node
        // and take where the pavements part for good, if they have parted by
        // the end of a junction's reach (200 m on a freeway or ramp, 120 m
        // else). A pair still close there keeps the angle's radius: it is a
        // carriageway pair running alongside, and PARA moves it.
        if (th < Math.PI / 2) {
          const reach = big ? 200 : 120;
          const A = walkOut(arms[i], n, reach), B = walkOut(arms[j], n, reach + 60);
          const part = partAt(A, B, need);
          if (part !== null) R = Math.max(R, Math.min(big ? 300 : 150, part + 10));
        }
        conv.push({ n, ci: chainOf[arms[i].id], cj: chainOf[arms[j].id], ei: arms[i].id, ej: arms[j].id, R });
      }
    }
    // A JUNCTION BOX: a divided road's two carriageways joined across it by
    // a short connector (the crossing street's median piece, <= 30 m) meet
    // there - inside the box there is no median, the junction cluster (WP-19)
    // paves it whole. The pair is exempt within the box (the connector's
    // length, at least 15 m, plus the crossing street's half width and a
    // 25 ft curb-return radius, round each end); pushing them apart there
    // would only tear the crossing street.
    const box = [];
    for (const c of edges) {
      if (c.a === c.b || c.len > 30 || c.way.roundabout) continue;
      const A = ne[c.a].filter(e => e !== c && e.a !== e.b), B = ne[c.b].filter(e => e !== c && e.a !== e.b);
      if (!A.length || !B.length) continue;
      const R = Math.max(15, c.len) + profOf(c).width / 2 + 7.6;   // + the crossing street's half width and a 25 ft curb return
      for (const ea of A) for (const eb of B) {
        if (chainOf[ea.id] === chainOf[eb.id] || chainOf[ea.id] === chainOf[c.id] || chainOf[eb.id] === chainOf[c.id]) continue;
        box.push({ n: c.a, ci: chainOf[ea.id], cj: chainOf[eb.id], R, box: true });
        box.push({ n: c.b, ci: chainOf[ea.id], cj: chainOf[eb.id], R, box: true });
      }
    }
    return conv.concat(box);
  }

  // arc position on F of the point u along its segment j (cumulative lengths cached per pass)
  let cumCache = new Map();
  const sOfPt = (F, j, u) => { let c = cumCache.get(F.id); if (!c) { c = cumS(F.pts); cumCache.set(F.id, c); } return c[j - 1] + (c[j] - c[j - 1]) * u; };
  function segGrid() {
    cumCache = new Map();
    const g = new Map();
    for (const e of edges) {
      const P = e.pts;
      for (let k = 1; k < P.length; k++) {
        const x0 = Math.floor(Math.min(P[k - 1][0], P[k][0]) / CELL), x1 = Math.floor(Math.max(P[k - 1][0], P[k][0]) / CELL);
        const z0 = Math.floor(Math.min(P[k - 1][1], P[k][1]) / CELL), z1 = Math.floor(Math.max(P[k - 1][1], P[k][1]) / CELL);
        for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) {
          const key = cx * 100003 + cz;
          let l = g.get(key); if (!l) g.set(key, l = []);
          l.push(e.id * 65536 + k);
        }
      }
    }
    return g;
  }

  /// Every deficit sample: { E, s (on E), u (signed lateral push, + = E's
  /// left), deficit, F, x, z, exempt }.
  function deficits(conv) {
    const grid = segGrid();
    const convBy = new Map();
    for (const c of conv) for (const k of [c.ci + ':' + c.cj, c.cj + ':' + c.ci]) { let l = convBy.get(k); if (!l) convBy.set(k, l = []); l.push(c); }
    const out = [];
    for (const E of edges) {
      if (E.way.roundabout) continue;
      const P = E.pts, S = cumS(P), L = S[S.length - 1];
      const n = Math.max(1, Math.ceil(L / STEP));
      let k = 1;
      for (let q = 0; q <= n; q++) {
        const s = Math.min(L, q * L / n);
        while (k < P.length - 1 && S[k] < s) k++;
        const segL = S[k] - S[k - 1] || 1, t = (s - S[k - 1]) / segL;
        const x = P[k - 1][0] + (P[k][0] - P[k - 1][0]) * t, z = P[k - 1][1] + (P[k][1] - P[k - 1][1]) * t;
        const tx = (P[k][0] - P[k - 1][0]) / segL, tz = (P[k][1] - P[k - 1][1]) / segL;
        const R = 36;
        const cx0 = Math.floor((x - R) / CELL), cx1 = Math.floor((x + R) / CELL), cz0 = Math.floor((z - R) / CELL), cz1 = Math.floor((z + R) / CELL);
        const best = new Map();   // F.id -> nearest
        for (let cx = cx0; cx <= cx1; cx++) for (let cz = cz0; cz <= cz1; cz++) {
          const l = grid.get(cx * 100003 + cz);
          if (!l) continue;
          for (const packed of l) {
            const fid = Math.floor(packed / 65536), j = packed % 65536;
            if (fid === E.id) continue;
            const F = edges[fid];
            if (F.way === E.way || F.way.roundabout || F.way.level !== E.way.level) continue;
            const A = F.pts[j - 1], B = F.pts[j];
            const dx = B[0] - A[0], dz = B[1] - A[1], L2 = dx * dx + dz * dz;
            let u = L2 > 0 ? ((x - A[0]) * dx + (z - A[1]) * dz) / L2 : 0; u = Math.max(0, Math.min(1, u));
            const qx = A[0] + dx * u, qz = A[1] + dz * u, d = Math.hypot(x - qx, z - qz);
            // beside, not ahead: a point past F's end (the next piece of the
            // same road, a junction arm) is no carriageway alongside
            const along = Math.abs((qx - x) * tx + (qz - z) * tz);
            if ((u <= 0 || u >= 1) && along > 1.0) continue;
            const cur = best.get(fid);
            if (!cur || d < cur.d) best.set(fid, { d, qx, qz, ux: dx / (Math.sqrt(L2) || 1), uz: dz / (Math.sqrt(L2) || 1), sF: sOfPt(F, j, u) });
          }
        }
        for (const [fid, b] of best) {
          const F = edges[fid];
          if (Math.abs(tx * b.ux + tz * b.uz) < Math.cos(30 / DEG)) continue;    // not parallel
          if (b.d < 0.5) continue;                                              // they cross here
          const lx = -tz, lz = tx;                                              // E's left
          const onLeft = (b.qx - x) * lx + (b.qz - z) * lz > 0;
          const fLeft = (x - b.qx) * -b.uz + (z - b.qz) * b.ux > 0;             // E on F's left?
          const need = halfFacing(E, onLeft, s) + halfFacing(F, fLeft, b.sF) + gapOf(E, F);
          const deficit = need - b.d;
          if (deficit <= 0.02) continue;
          // exempt near a node where the two really meet (their chains converge there)
          let exempt = false;
          const cl = convBy.get(chainOf[E.id] + ':' + chainOf[F.id]);
          // (either side of the pair inside the zone: the same spot is judged the same from E and from F)
          if (cl) for (const c of cl) if (Math.min(Math.hypot(nodes[c.n][0] - x, nodes[c.n][1] - z), Math.hypot(nodes[c.n][0] - b.qx, nodes[c.n][1] - b.qz)) < c.R) exempt = true;
          const sh = shareOf(E, F);
          // A ramp lying INSIDE its mainline's pavement (a lane-split exit, a
          // taper mapped on the lane line) is an attach zone, not a mapping
          // error: moving it metres sideways would break the interchange.
          // Where the two chains meet somewhere, it is the gore's (WP-18b).
          const cap = capOf(sh > 0 ? E : F);
          if (!exempt && (E.way.link || F.way.link) && deficit * Math.max(sh, 0.5) > cap && convAny.has(chainOf[E.id] + ':' + chainOf[F.id])) exempt = true;
          // A surface street's slip lane or median connector (a *_link below
          // trunk, under 200 m) is junction channelisation: an island between
          // it and the street, drawn by the junction cluster (WP-19).
          const slip = e => e.way.link && e.way.rank < 4 && e.len < 200;
          if (!exempt && (slip(E) || slip(F))) exempt = true;
          out.push({ E, s, x, z, u: (onLeft ? -1 : 1) * (deficit + 0.3) * sh, deficit, F, exempt, sh });
        }
      }
    }
    return out;
  }

  /// A pair within 25 cm of its class gap still has a real median (0.95 m
  /// or more): the review list starts past that.
  const REVIEW_TOL = 0.25;
  const measure = (defs) => {
    let bad = 0, badM = 0, core = 0, pairs = new Set(), exM = 0;
    for (const d of defs) {
      if (d.exempt) { exM += STEP; continue; }
      if (d.deficit <= REVIEW_TOL) continue;
      bad++; badM += STEP; pairs.add(Math.min(d.E.id, d.F.id) + ':' + Math.max(d.E.id, d.F.id));
      if (inCore(d.x, d.z)) core++;
    }
    return { samples: bad, km: +(badM / 1000 / 2).toFixed(2), pairs: pairs.size, core_samples: core, attach_zone_km: +(exM / 1000 / 2).toFixed(2) };
  };

  let conv = convergences();
  const convSet = () => { convAny = new Set(); for (const c of conv) { if (c.box) continue; convAny.add(c.ci + ':' + c.cj); convAny.add(c.cj + ':' + c.ci); } };
  convSet();
  let defs = deficits(conv);
  stats.para_before = measure(defs);
  const defs0 = defs;
  const moved = [];   // per chain: max |U|
  const usedCap = new Map();   // chain -> metres already moved (either way)
  for (let pass = 0; pass < 20; pass++) {   // (12 left short residuals at zone edges: the pushes had not converged)
    const byChain = new Map();
    for (const d of defs) {
      if (d.exempt || d.sh === 0) continue;
      const ci = chainOf[d.E.id];
      if (ci < 0) continue;
      let l = byChain.get(ci); if (!l) byChain.set(ci, l = []); l.push(d);
    }
    if (!byChain.size) break;
    const nodeDisp = new Map();   // node -> [dx, dz, chains]
    const ownDisp = new Map();    // chain:node -> [dx, dz]
    for (const [ci, list] of byChain) {
      const chain = chains[ci];
      // chain s of every edge start
      const s0 = []; let acc = 0;
      for (const c of chain) { s0.push(acc); acc += c.e.len; }
      const Ltot = acc, nb = Math.max(2, Math.ceil(Ltot / STEP) + 1);
      const pos = new Float64Array(nb), neg = new Float64Array(nb);
      const idxOf = new Map(chain.map((c, i) => [c.e.id, i]));
      for (const d of list) {
        const i = idxOf.get(d.E.id);
        const s = s0[i] + (chain[i].fwd ? d.s : d.E.len - d.s);
        const u = chain[i].fwd ? d.u : -d.u;                 // chain-left frame
        const j = Math.min(nb - 1, Math.max(0, Math.round(s / STEP)));
        if (u > 0) pos[j] = Math.max(pos[j], u); else neg[j] = Math.max(neg[j], -u);
      }
      const w0 = chain[0].e.way;
      const rate = shiftRate(mphOf(w0));
      const shape = arr => {
        // plateau fill (+-10 m), rate-limited cone, two box passes
        let a = Float64Array.from(arr, (_, j) => { let m = 0; for (let q = Math.max(0, j - 2); q <= Math.min(nb - 1, j + 2); q++) m = Math.max(m, arr[q]); return m; });
        const dropPerBin = STEP / rate;
        for (let j = 1; j < nb; j++) a[j] = Math.max(a[j], a[j - 1] - dropPerBin);
        for (let j = nb - 2; j >= 0; j--) a[j] = Math.max(a[j], a[j + 1] - dropPerBin);
        const h = Math.max(1, Math.round(Math.min(40, Math.max(15, rate * 0.5)) / STEP / 2));
        for (let pass2 = 0; pass2 < 2; pass2++) {
          const b = new Float64Array(nb);
          for (let j = 0; j < nb; j++) { let sum = 0, cnt = 0; for (let q = j - h; q <= j + h; q++) { const qq = Math.min(nb - 1, Math.max(0, q)); sum += a[qq]; cnt++; } b[j] = sum / cnt; }
          a = b;
        }
        return a;
      };
      const P = shape(pos), N = shape(neg);
      const U = new Float64Array(nb);
      for (let j = 0; j < nb; j++) U[j] = P[j] - N[j];
      // zero where this chain really meets a chain that pushes it (a split,
      // a merge), eased over that meeting's attach radius; a ramp merging
      // into it elsewhere just follows it
      const partners = new Set(list.map(d => chainOf[d.F.id]));
      for (const c of conv) {
        if (c.box) continue;   // a junction box exempts, it does not pin
        if (!((c.ci === ci && partners.has(c.cj)) || (c.cj === ci && partners.has(c.ci)))) continue;
        // chain s of the node
        for (let i = 0; i < chain.length; i++) {
          const e = chain[i].e;
          const sAt = e.a === c.n ? (chain[i].fwd ? s0[i] : s0[i] + e.len) : e.b === c.n ? (chain[i].fwd ? s0[i] + e.len : s0[i]) : -1;
          if (sAt < 0) continue;
          // (over 0.7 R: at the zone's edge the full offset already holds)
          for (let j = 0; j < nb; j++) U[j] *= SMOOTH(Math.abs(j * STEP - sAt) / (0.7 * c.R));
        }
      }
      let umax = 0; for (let j = 0; j < nb; j++) umax = Math.max(umax, Math.abs(U[j]));
      // never past the chain's cap at any point, over all passes. The cap is
      // on the RIBBON's move off its OSM line: a TAPR offset that carries the
      // ribbon toward the partner may be cancelled on top of it (the net
      // move stays within the map's error), never the other way.
      {
        const cap = capOf(chain[0].e);
        const offBin = new Float64Array(nb);
        for (let i = 0; i < chain.length; i++) {
          const r = edgeOffset[chain[i].e.id];
          if (!r || Math.abs(r.o0) < 1e-4) continue;
          const o = chain[i].fwd ? r.o0 : -r.o0;       // chain-left frame (the offset holds along an edge)
          for (let j = Math.max(0, Math.floor(s0[i] / STEP)); j <= Math.min(nb - 1, Math.ceil((s0[i] + chain[i].e.len) / STEP)); j++)
            offBin[j] = Math.abs(o) > Math.abs(offBin[j]) ? o : offBin[j];
        }
        let acc = usedCap.get(ci);
        if (!acc || acc.length !== nb) { const a2 = new Float64Array(nb); if (acc) for (let j = 0; j < Math.min(nb, acc.length); j++) a2[j] = acc[j]; acc = a2; usedCap.set(ci, acc); }
        umax = 0;
        for (let j = 0; j < nb; j++) {
          const lo = -cap - Math.max(0, offBin[j]), hi = cap + Math.max(0, -offBin[j]);
          const tgt = Math.max(lo, Math.min(hi, acc[j] + U[j]));
          U[j] = tgt - acc[j]; acc[j] = tgt;
          umax = Math.max(umax, Math.abs(U[j]));
        }
      }
      if (umax < 0.02) continue;
      const Uat = s => { const f = Math.min(nb - 1, Math.max(0, s / STEP)), j = Math.floor(f), t = f - j; return j + 1 < nb ? U[j] * (1 - t) + U[j + 1] * t : U[j]; };
      moved.push({ ci, umax, edges: chain.map(c => c.e.id) });
      // densify where U changes; then move every point of the chain, as ONE
      // polyline (a chain node takes the tangent across both its edges), along
      // the chain's left normal
      const outs = chain.map(({ e, fwd }, i) => {
        const Pts = e.pts, S = cumS(Pts);
        const out = [Pts[0]];
        for (let k = 1; k < Pts.length; k++) {
          const segL = S[k] - S[k - 1];
          const m = Math.floor(segL / STEP);
          for (let q = 1; q < m; q++) {
            const sl = S[k - 1] + q * segL / m;
            const sc = s0[i] + (fwd ? sl : e.len - sl);
            if (Math.abs(Uat(sc - STEP) - Uat(sc + STEP)) < 1e-4) continue;
            const t = q / m;
            out.push([Pts[k - 1][0] + (Pts[k][0] - Pts[k - 1][0]) * t, Pts[k - 1][1] + (Pts[k][1] - Pts[k - 1][1]) * t]);
          }
          out.push(Pts[k]);
        }
        return out;
      });
      const flat = [];   // chain order: { p, i, k }
      chain.forEach(({ fwd }, i) => {
        const o = outs[i], n2 = o.length;
        for (let q = 0; q < n2; q++) {
          if (i > 0 && q === 0) continue;
          const k = fwd ? q : n2 - 1 - q;
          flat.push({ p: o[k], i, k });
        }
      });
      let sc = 0;
      const newOuts = outs.map(o => o.map(p => p.slice()));
      for (let f = 0; f < flat.length; f++) {
        if (f) sc += dist(flat[f].p, flat[f - 1].p);
        const pa = flat[Math.max(0, f - 1)].p, pb = flat[Math.min(flat.length - 1, f + 1)].p;
        let tx = pb[0] - pa[0], tz = pb[1] - pa[1]; const tl = Math.hypot(tx, tz) || 1; tx /= tl; tz /= tl;
        const u = Uat(sc);
        const np = [flat[f].p[0] - tz * u, flat[f].p[1] + tx * u];
        const { i, k } = flat[f];
        newOuts[i][k] = np;
        // a chain node belongs to the edge before it too
        if (i > 0 && ((chain[i].fwd && k === 0) || (!chain[i].fwd && k === outs[i].length - 1))) {
          const pi = i - 1, pk = chain[pi].fwd ? outs[pi].length - 1 : 0;
          newOuts[pi][pk] = np;
        }
      }
      chain.forEach(({ e }, i) => {
        const o = outs[i], m2 = newOuts[i];
        for (const [k, nd] of [[0, e.a], [o.length - 1, e.b]]) {
          const key = ci + ':' + nd;
          if (!ownDisp.has(key)) {
            const dd = [m2[k][0] - o[k][0], m2[k][1] - o[k][1]];
            ownDisp.set(key, dd);
            const cur = nodeDisp.get(nd) || [0, 0];
            nodeDisp.set(nd, [cur[0] + dd[0], cur[1] + dd[1]]);
          }
        }
        const moved2 = m2.slice();
        moved2[0] = null; moved2[moved2.length - 1] = null;   // ends follow the node below
        e._newPts = moved2;
        e._chain = ci;
      });
    }
    // move nodes, then every edge end to its node; an end whose own chain did
    // not carry the whole move eases the rest in over its first metres
    for (const [nd, dd] of nodeDisp) nodes[nd] = [nodes[nd][0] + dd[0], nodes[nd][1] + dd[1]];
    for (const e of edges) {
      let P = e._newPts ? e._newPts.slice() : e.pts.slice();
      const own = end => (e._chain !== undefined ? ownDisp.get(e._chain + ':' + end) : null) || [0, 0];
      const extraA = nodeDisp.has(e.a) ? [nodeDisp.get(e.a)[0] - own(e.a)[0], nodeDisp.get(e.a)[1] - own(e.a)[1]] : [0, 0];
      const extraB = nodeDisp.has(e.b) ? [nodeDisp.get(e.b)[0] - own(e.b)[0], nodeDisp.get(e.b)[1] - own(e.b)[1]] : [0, 0];
      const mA = Math.hypot(...extraA), mB = Math.hypot(...extraB);
      if (!e._newPts && mA < 1e-4 && mB < 1e-4) continue;
      P[0] = nodes[e.a].slice(); P[P.length - 1] = nodes[e.b].slice();
      if (mA > 1e-3 || mB > 1e-3) {
        // densify the eased stretch, then ease
        const base = e.pts.map(p => p);   // original, for arc positions
        const S = cumS(P.map((p, k) => p || base[k]));
        const L = S[S.length - 1];
        const LA = Math.min(L * 0.45, Math.max(10, mA * 25)), LB = Math.min(L * 0.45, Math.max(10, mB * 25));
        const Q = [], QS = [];
        for (let k = 0; k < P.length; k++) {
          const p = P[k] || base[k];
          if (k > 0) {
            const segL = S[k] - S[k - 1], prev = P[k - 1] || base[k - 1];
            const m = Math.floor(segL / STEP);
            for (let q = 1; q < m; q++) {
              const sl = S[k - 1] + q * segL / m;
              if ((mA > 1e-3 && sl < LA) || (mB > 1e-3 && L - sl < LB)) { Q.push([prev[0] + (p[0] - prev[0]) * q / m, prev[1] + (p[1] - prev[1]) * q / m]); QS.push(sl); }
            }
          }
          Q.push(p.slice()); QS.push(S[k]);
        }
        for (let k = 1; k < Q.length - 1; k++) {
          const wa = mA > 1e-3 ? 1 - SMOOTH(QS[k] / LA) : 0, wb = mB > 1e-3 ? 1 - SMOOTH((L - QS[k]) / LB) : 0;
          Q[k][0] += extraA[0] * wa + extraB[0] * wb; Q[k][1] += extraA[1] * wa + extraB[1] * wb;
        }
        P = Q;
      } else {
        for (let k = 1; k < P.length - 1; k++) if (!P[k]) P[k] = e.pts[k];
      }
      e.pts = P.filter(Boolean);
      e.len = polyLen(e.pts);
    }
    for (const e of edges) { delete e._newPts; delete e._chain; }
    defs = deficits(conv);
  }
  stats.para_after = measure(defs);
  stats.para_moved = { chains: new Set(moved.map(m => m.ci)).size, max_offset_m: +Math.max(0, ...moved.map(m => m.umax)).toFixed(2) };
  // the review list: pairs still short of their gap outside an attach zone
  const pairs = new Map();
  for (const d of defs) {
    if (d.exempt || d.deficit <= REVIEW_TOL) continue;
    const key = Math.min(d.E.id, d.F.id) + ':' + Math.max(d.E.id, d.F.id);
    const cur = pairs.get(key);
    if (!cur || d.deficit > cur.deficit) pairs.set(key, { e1: d.E.id, e2: d.F.id, x: d.x, z: d.z, deficit: d.deficit, n: (cur ? cur.n : 0) + 1 });
    else cur.n++;
  }
  for (const p of pairs.values()) review.push({ kind: 'para', edge: p.e1, edge2: p.e2, x: p.x, z: p.z, deficit: p.deficit, metres: p.n * STEP / 2, note: `${edges[p.e1].way.name || 'ramp'} / ${edges[p.e2].way.name || 'ramp'}` });
  return { moved, pairs: [...pairs.values()], defs, defs0 };
}
