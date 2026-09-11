// Routing over an Overpass result: the oneway-respecting Dijkstra router and
// the verified-way-id chainer that tools/clt/fetch_clt.mjs was written with,
// lifted out so a mountain loop bake (tools/roads/fetch_loop.mjs) is not a
// second copy of either. Every behaviour here is fetch_clt.mjs's as shipped
// on 2026-09-07; the Charlotte bakes re-run byte-identical through it.
//
// Everything is pure over its arguments — the config the CLT script kept in a
// module-level CFG is passed in explicitly.

import { dist2 } from './lib.mjs';

// ---------------------------------------------------------------- tags
export const bridgeOf = w => !!(w.tags && (w.tags.bridge === 'yes' || w.tags.bridge === 'viaduct'));

/// OSM's ways of saying "one direction only". A motorway is one-way by
/// definition and mappers mostly leave the tag off it.
export const onewayOf = w => {
  const t = w.tags || {};
  if (t.oneway === '-1') return -1;
  if (t.oneway === 'yes' || t.oneway === '1' || t.oneway === 'true') return 1;
  if (t.junction === 'roundabout' || t.highway === 'motorway') return 1;
  return 0;
};

export const lanesOf = w => { const n = parseInt((w.tags || {}).lanes, 10); return Number.isFinite(n) ? n : 0; };

// -------------------------------------------------------- way-id chain
/// Chain the listed ways IN ORDER. Each way's geometry is flipped when its
/// far end is the one that touches the chain; a flip on a way OSM tags as
/// one-way is reported, because it means the list drives against traffic.
export function chainByIds(overpass, ids, proj) {
  const byId = new Map(overpass.elements.filter(e => e.type === 'way').map(w => [w.id, w]));
  const missing = ids.filter(id => !byId.has(id));
  if (missing.length) throw new Error('Overpass returned no geometry for way(s) ' + missing.join(','));
  const m = (a, b) => Math.sqrt(dist2(proj.toXZ(a.lat, a.lon), proj.toXZ(b.lat, b.lon)));

  const chain = [], flag = [], wayOf = [];
  let flippedOneway = 0, joins = 0, worstJoin = 0;
  for (let k = 0; k < ids.length; k++) {
    const w = byId.get(ids[k]);
    let g = w.geometry.slice();
    if (!chain.length) {
      // Orient the first way by the second: whichever end of it the next
      // way touches is its tail.
      if (ids.length > 1) {
        const nx = byId.get(ids[1]).geometry;
        const tailFits = Math.min(m(g.at(-1), nx[0]), m(g.at(-1), nx.at(-1)));
        const headFits = Math.min(m(g[0], nx[0]), m(g[0], nx.at(-1)));
        if (headFits < tailFits) g.reverse();
      }
    } else {
      const tail = chain.at(-1);
      const d0 = m(g[0], tail), d1 = m(g.at(-1), tail);
      if (d1 < d0) { g.reverse(); if (onewayOf(w) === 1) flippedOneway++; }
      else if (onewayOf(w) === -1) flippedOneway++;
      const gap = Math.min(d0, d1);
      if (gap > 30) throw new Error(`way ${w.id} (#${k}) does not join the chain: ${gap.toFixed(0)} m gap`);
      if (gap > 0.5) { joins++; worstJoin = Math.max(worstJoin, gap); }
      else g = g.slice(1);          // the shared node is already the tail
    }
    for (const p of g) { chain.push({ lat: p.lat, lon: p.lon }); flag.push(bridgeOf(w)); wayOf.push(w); }
  }
  if (flippedOneway)
    console.log(`  WARNING: ${flippedOneway} one-way way(s) are traversed against their tag`);
  if (joins) console.log(`  ${joins} join(s) had a gap (worst ${worstJoin.toFixed(1)} m) — the ends are bridged by a straight`);
  console.log(`chained ${ids.length} ways, ${chain.length} vertices`);
  return { chain, flag, wayOf };
}

// ------------------------------------------------------------- router
// Per-class cost per metre, from tools/bogue/route.mjs — the multipliers keep
// a route on the highway unless the highway genuinely does not go there.
export const CLASS_COST = {
  motorway: 1.0, trunk: 1.0, primary: 1.0,
  motorway_link: 1.2, trunk_link: 1.2, primary_link: 1.2,
  secondary: 1.15, secondary_link: 1.3, tertiary: 1.4,
  unclassified: 3.0, residential: 4.0,
};
/// Off-preference roads cost this much more per metre. Strong enough that
/// the router stays on I-277 round three quarters of a ring, weak enough
/// that a ramp it has to take is still cheaper than a 19 km detour.
export const OFF_PREFERENCE = 8;
export const ANCHOR_REACH_M = 400, ANCHOR_CANDIDATES = 12;

/// Does a way match a {names, refs} preference? Names by substring, refs
/// with the spacing stripped (OSM writes "NC 128", "NC128" and "NC 128;NC 80").
export function matchesPref(tags, prefer) {
  if (!tags || !prefer) return false;
  const name = (tags.name || '').toLowerCase();
  for (const n of prefer.names || []) if (n && name.includes(n)) return true;
  const refs = (tags.ref || '').toUpperCase().split(';').map(r => r.replace(/\s+/g, ''));
  for (const r of prefer.refs || []) if (refs.includes(r.replace(/\s+/g, '').toUpperCase())) return true;
  return false;
}

/// The routable graph. `classes` limits which highway classes are edges at
/// all (null = every class in CLASS_COST); `classCost` extends or overrides
/// the table (a mountain bake wants highway=construction on a closed
/// Parkway section to count as the secondary it is); `prefer` marks the
/// roads the route should want to stay on.
export function buildGraph(overpass, proj, { classes = null, prefer = null,
                                             classCost = CLASS_COST, offPreference = OFF_PREFERENCE,
                                             stitches = [] } = {}) {
  const key = (lat, lon) => lat.toFixed(7) + ',' + lon.toFixed(7);
  const nodes = new Map(), nodeLL = [], nodeXZ = [], adj = [], onPreferred = [];
  const idOf = (lat, lon) => {
    const k = key(lat, lon);
    let i = nodes.get(k);
    if (i === undefined) {
      i = nodeLL.length; nodes.set(k, i);
      nodeLL.push({ lat, lon }); nodeXZ.push(proj.toXZ(lat, lon)); adj.push([]); onPreferred.push(false);
    }
    return i;
  };
  const ways = [];
  for (const e of overpass.elements) {
    if (e.type !== 'way' || !e.geometry || !e.tags) continue;
    const cls = e.tags.highway;
    const cost = classCost[cls];
    if (cost === undefined) continue;
    if (classes && !classes.includes(cls)) continue;
    const pref = matchesPref(e.tags, prefer);
    const mult = cost * (pref ? 1 : offPreference);
    const dir = onewayOf(e);
    const wayIdx = ways.length;
    ways.push(e);
    const g = e.geometry;
    for (let i = 1; i < g.length; i++) {
      const a = idOf(g[i - 1].lat, g[i - 1].lon), b = idOf(g[i].lat, g[i].lon);
      if (a === b) continue;
      if (pref) { onPreferred[a] = onPreferred[b] = true; }
      const m = Math.sqrt(dist2(nodeXZ[a], nodeXZ[b]));
      if (dir >= 0) adj[a].push({ to: b, cost: m * mult, wayIdx });
      if (dir <= 0) adj[b].push({ to: a, cost: m * mult, wayIdx });
    }
  }
  // STITCHES: an edge OSM does not have. The Cone Manor access at Blowing
  // Rock ends 27 m short of the Parkway it plainly joins (the mapper never
  // connected the node), and without the edge the router walks nine
  // kilometres round through the village to cover those 27 m. Each end is
  // the nearest node of the way it names; the edge is two-way and priced as
  // a link.
  for (const st of stitches) {
    // An end may name a WAY: then it is that way's nearest node to the
    // coordinate, which is how a stitch lands on the through road rather
    // than on whichever link happens to be closer.
    const end = e => {
      let best = -1, bestD = Infinity;
      const p = proj.toXZ(e.lat, e.lon);
      if (e.wayId) {
        const w = ways.find(x => x.id === e.wayId);
        if (!w) throw new Error('stitch end names way ' + e.wayId + ', not in the fetched area');
        for (const g of w.geometry) {
          const i = nodes.get(key(g.lat, g.lon));
          if (i === undefined) continue;
          const d = Math.sqrt(dist2(nodeXZ[i], p));
          if (d < bestD) { bestD = d; best = i; }
        }
        return { i: best, d: bestD };
      }
      for (let i = 0; i < nodeLL.length; i++) {
        if (!adj[i].length) continue;
        const d = Math.sqrt(dist2(nodeXZ[i], p));
        if (d < bestD) { bestD = d; best = i; }
      }
      return { i: best, d: bestD };
    };
    const a = end(st.a), b = end(st.b);
    if (a.i < 0 || b.i < 0) continue;
    const m = Math.sqrt(dist2(nodeXZ[a.i], nodeXZ[b.i]));
    const wayIdx = ways.length;
    ways.push({ id: -1 - stitches.indexOf(st), tags: { highway: 'tertiary_link', name: st.name || 'stitch' }, geometry: [nodeLL[a.i], nodeLL[b.i]] });
    adj[a.i].push({ to: b.i, cost: m * 1.2, wayIdx });
    adj[b.i].push({ to: a.i, cost: m * 1.2, wayIdx });
    onPreferred[a.i] = onPreferred[b.i] = true;
    console.log(`  stitch ${st.name || ''}: ${m.toFixed(0)} m edge (ends snapped ${a.d.toFixed(0)} / ${b.d.toFixed(0)} m)`);
  }
  return { ways, nodeLL, nodeXZ, adj, onPreferred };
}

export function dijkstra(g, from) {
  const N = g.nodeLL.length;
  const dist = new Float64Array(N).fill(Infinity);
  const prev = new Int32Array(N).fill(-1);
  const prevWay = new Int32Array(N).fill(-1);
  dist[from] = 0;
  const heap = [[0, from]];
  const push = (d, n) => {
    heap.push([d, n]);
    let i = heap.length - 1;
    while (i > 0) { const p = (i - 1) >> 1; if (heap[p][0] <= heap[i][0]) break;
      [heap[p], heap[i]] = [heap[i], heap[p]]; i = p; }
  };
  const pop = () => {
    const top = heap[0], last = heap.pop();
    if (heap.length) { heap[0] = last; let i = 0;
      for (;;) { const l = i * 2 + 1, r = l + 1; let s = i;
        if (l < heap.length && heap[l][0] < heap[s][0]) s = l;
        if (r < heap.length && heap[r][0] < heap[s][0]) s = r;
        if (s === i) break; [heap[s], heap[i]] = [heap[i], heap[s]]; i = s; } }
    return top;
  };
  const done = new Uint8Array(N);
  while (heap.length) {
    const [d, u] = pop();
    if (done[u]) continue;
    done[u] = 1;
    for (const e of g.adj[u]) {
      const nd = d + e.cost;
      if (nd < dist[e.to]) { dist[e.to] = nd; prev[e.to] = u; prevWay[e.to] = e.wayIdx; push(nd, e.to); }
    }
  }
  return { dist, prev, prevWay };
}

/// Candidate graph nodes for an anchor: the nearest few ON A PREFERRED WAY
/// within reach, else the single nearest routable node. Several, because on
/// a divided road the nearest node is on one carriageway or the other and
/// only the whole route can say which was right.
export function anchorCandidates(g, proj, a, { reachM = ANCHOR_REACH_M, count = ANCHOR_CANDIDATES } = {}) {
  const p = proj.toXZ(a.lat, a.lon);
  const scored = [];
  let nearestAny = -1, nearestD = Infinity;
  for (let i = 0; i < g.nodeXZ.length; i++) {
    if (!g.adj[i].length) continue;
    const d = Math.sqrt(dist2(g.nodeXZ[i], p));
    if (d < nearestD) { nearestD = d; nearestAny = i; }
    if (g.onPreferred[i] && d <= reachM) scored.push([d, i]);
  }
  scored.sort((x, y) => x[0] - y[0]);
  const out = scored.slice(0, count).map(s => s[1]);
  return out.length ? out : [nearestAny];
}

/// Shortest route through `anchors` in order — and for `loop`, back to the
/// first anchor's own candidate so the ring closes on the carriageway it
/// left from. Returns the chain of {lat, lon}, the per-vertex bridge flag,
/// and the way each vertex came from.
export function routeByAnchors(overpass, proj, { anchors, loop = false, classes = null, prefer = null,
                                                 classCost = CLASS_COST, offPreference = OFF_PREFERENCE,
                                                 reachM = ANCHOR_REACH_M, candidates = ANCHOR_CANDIDATES,
                                                 stitches = [], quiet = false } = {}) {
  const g = buildGraph(overpass, proj, { classes, prefer, classCost, offPreference, stitches });
  if (!quiet) console.log(`graph: ${g.nodeLL.length} nodes, ${g.ways.length} ways`);
  // An anchor may carry its own reach: a snapped anchor (one placed ON a
  // node of the road it names) wants exactly that node, not the twelve
  // preferred nodes within 400 m, which on two parallel roads includes the
  // other one and lets the router ignore the leg the anchor was for.
  const cands = anchors.map(a => anchorCandidates(g, proj, a,
    { reachM: a.reachM ?? reachM, count: a.reachM != null ? 1 : candidates }));
  // One Dijkstra per candidate, then a DP over the anchor sequence.
  const runs = cands.map(cs => cs.map(c => dijkstra(g, c)));
  const K = cands.length;
  let best = null;
  for (let c0 = 0; c0 < cands[0].length; c0++) {
    // cost[k][j]: cheapest way to reach candidate j of anchor k from c0
    let cost = cands[0].map((_, j) => (j === c0 ? 0 : Infinity));
    let choice = [cands[0].map(() => -1)];
    for (let k = 1; k < K; k++) {
      const next = cands[k].map(() => Infinity), from = cands[k].map(() => -1);
      for (let i = 0; i < cands[k - 1].length; i++) {
        if (!Number.isFinite(cost[i])) continue;
        for (let j = 0; j < cands[k].length; j++) {
          const d = cost[i] + runs[k - 1][i].dist[cands[k][j]];
          if (d < next[j]) { next[j] = d; from[j] = i; }
        }
      }
      cost = next; choice.push(from);
    }
    // Close the ring, or stop at the last anchor.
    let endJ = -1, endCost = Infinity;
    for (let j = 0; j < cands[K - 1].length; j++) {
      const d = loop ? cost[j] + runs[K - 1][j].dist[cands[0][c0]] : cost[j];
      if (d < endCost) { endCost = d; endJ = j; }
    }
    if (endJ < 0 || !Number.isFinite(endCost)) continue;
    if (!best || endCost < best.cost) {
      // unwind the choices into a candidate index per anchor
      const picks = new Array(K);
      picks[K - 1] = endJ;
      for (let k = K - 1; k > 0; k--) picks[k - 1] = choice[k][picks[k]];
      best = { cost: endCost, picks, c0 };
    }
  }
  if (!best) throw new Error('no route joins the anchors (oneway or class filter too strict?)');

  // Reconstruct leg by leg from the stored searches.
  const legs = [];
  for (let k = 1; k < K; k++) legs.push([k - 1, best.picks[k - 1], cands[k][best.picks[k]]]);
  if (loop) legs.push([K - 1, best.picks[K - 1], cands[0][best.c0]]);
  const chain = [], flag = [], wayOf = [];
  const legInfo = [];
  for (const [k, i, to] of legs) {
    const r = runs[k][i];
    const path = [];
    for (let u = to; u !== -1; u = r.prev[u]) path.push(u);
    path.reverse();
    // What each leg cost in metres, for the record: a leg that should be
    // a hundred metres and comes out at six kilometres is a routing fault
    // the totals hide.
    let legM = 0;
    for (let n = 1; n < path.length; n++) legM += Math.sqrt(dist2(g.nodeXZ[path[n - 1]], g.nodeXZ[path[n]]));
    legInfo.push({ from: k, to: (k + 1) % K, metres: legM, vertices: path.length });
    for (let n = 0; n < path.length; n++) {
      if (chain.length && n === 0) continue;         // the leg starts where the last ended
      const u = path[n];
      const wi = r.prevWay[u] >= 0 ? r.prevWay[u] : (n + 1 < path.length ? r.prevWay[path[n + 1]] : -1);
      const w = wi >= 0 ? g.ways[wi] : null;
      chain.push({ lat: g.nodeLL[u].lat, lon: g.nodeLL[u].lon });
      flag.push(w ? bridgeOf(w) : false);
      wayOf.push(w);
    }
  }
  const used = new Set(wayOf.filter(Boolean).map(w => w.id));
  if (!quiet) {
    console.log(`routed ${chain.length} vertices over ${used.size} ways` +
                ` (cost ${(best.cost / 1000).toFixed(2)} km-equivalent)`);
    for (const l of legInfo)
      console.log(`  leg ${l.from} -> ${l.to}: ${(l.metres / 1000).toFixed(2)} km, ${l.vertices} vertices`);
  }
  return { chain, flag, wayOf, cost: best.cost, legs: legInfo };
}
