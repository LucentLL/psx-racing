// route.mjs — a small road-graph router over the cached Overpass extract.
//
// Blind endpoint-chaining (what the BRP bake does) works for the parkway
// because the parkway is one road with no junctions worth the name. Bogue
// Banks is not: NC-58 forks at Atlantic Beach — north over the bridge to
// Morehead City, east along Fort Macon Road — and a chainer picks whichever
// way happens to match an endpoint first. The first attempt confidently
// walked 27 km up the MAINLAND leg of NC-58 toward Jacksonville.
//
// So: build a graph, snap named anchors to it, and Dijkstra between them.
// Every venue on this island is then a list of anchors rather than a set of
// hand-tuned chaining rules, and the mainland return leg for the full
// circuit costs nothing extra.
//
// Shared by the probe and the bake.

import { readFileSync } from 'node:fs';

export const LATM = 111132.92;
export const lonMetres = lat => 111412.84 * Math.cos(lat * Math.PI / 180) - 93.5 * Math.cos(3 * lat * Math.PI / 180);

// Road classes we will route over, and what a metre on each COSTS. Routing
// on raw distance sends the line through beach-house cul-de-sacs whenever
// they cut a corner; the multipliers keep it on the highway unless the
// highway genuinely does not go there.
const CLASS_COST = {
  motorway: 1.0, trunk: 1.0, primary: 1.0,
  motorway_link: 1.2, trunk_link: 1.2, primary_link: 1.2,
  secondary: 1.15, secondary_link: 1.3, tertiary: 1.4,
  unclassified: 3.0, residential: 4.0,
};

export function loadGraph(cachePath, { lat0, lon0 }) {
  const op = JSON.parse(readFileSync(cachePath, 'utf8'));
  const mLon = lonMetres(lat0);
  const toXZ = (lat, lon) => ({ x: (lon - lon0) * mLon, z: (lat - lat0) * LATM });
  const toLL = (x, z) => ({ lat: lat0 + z / LATM, lon: lon0 + x / mLon });

  // Node identity is the rounded coordinate. `out geom` gives no node ids,
  // and 1e-7 deg is ~1 cm — far below OSM's own precision, so two ways that
  // share a junction share the key and two that merely pass close do not.
  const key = (lat, lon) => lat.toFixed(7) + ',' + lon.toFixed(7);
  const nodes = new Map();          // key -> index
  const nodeLL = [];                // index -> {lat,lon}
  const adj = [];                   // index -> [{to, cost, m, wayIdx}]
  const idOf = (lat, lon) => {
    const k = key(lat, lon);
    let i = nodes.get(k);
    if (i === undefined) { i = nodeLL.length; nodes.set(k, i); nodeLL.push({ lat, lon }); adj.push([]); }
    return i;
  };

  const ways = [];
  for (const e of op.elements) {
    if (e.type !== 'way' || !e.geometry || !e.tags) continue;
    const cost = CLASS_COST[e.tags.highway];
    if (cost === undefined) continue;
    const wayIdx = ways.length;
    ways.push(e);
    const g = e.geometry;
    for (let i = 1; i < g.length; i++) {
      const a = idOf(g[i - 1].lat, g[i - 1].lon), b = idOf(g[i].lat, g[i].lon);
      if (a === b) continue;
      const pa = toXZ(g[i - 1].lat, g[i - 1].lon), pb = toXZ(g[i].lat, g[i].lon);
      const m = Math.hypot(pb.x - pa.x, pb.z - pa.z);
      adj[a].push({ to: b, cost: m * cost, m, wayIdx });
      // Oneway is ignored on purpose: we are cutting race routes, not
      // giving driving directions, and NC-58's couplets would otherwise
      // make a westbound cut impossible.
      adj[b].push({ to: a, cost: m * cost, m, wayIdx });
    }
  }
  return { ways, nodeLL, adj, toXZ, toLL, mLon };
}

export function nearestNode(g, lat, lon, { filter } = {}) {
  const p = g.toXZ(lat, lon);
  let best = -1, bestD = Infinity;
  for (let i = 0; i < g.nodeLL.length; i++) {
    if (filter && !filter(i)) continue;
    const q = g.toXZ(g.nodeLL[i].lat, g.nodeLL[i].lon);
    const d = (q.x - p.x) ** 2 + (q.z - p.z) ** 2;
    if (d < bestD) { bestD = d; best = i; }
  }
  return { node: best, offM: Math.sqrt(bestD) };
}

/// Dijkstra. `via` forces the path through a way index (used to make a
/// bridge run actually use the bridge rather than an adjacent causeway).
export function shortestPath(g, from, to) {
  const N = g.nodeLL.length;
  const dist = new Float64Array(N).fill(Infinity);
  const prev = new Int32Array(N).fill(-1);
  const prevWay = new Int32Array(N).fill(-1);
  dist[from] = 0;
  // Binary heap; the graph is ~30k nodes so this is instant either way, but
  // a linear scan over 30k nodes per pop is 900M operations and is not.
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
    if (u === to) break;
    for (const e of g.adj[u]) {
      const nd = d + e.cost;
      if (nd < dist[e.to]) { dist[e.to] = nd; prev[e.to] = u; prevWay[e.to] = e.wayIdx; push(nd, e.to); }
    }
  }
  if (!done[to]) return null;
  const path = [];
  const wayOf = [];
  for (let u = to; u !== -1; u = prev[u]) { path.push(u); wayOf.push(prevWay[u]); }
  path.reverse(); wayOf.reverse();
  return { nodes: path, ways: wayOf };
}

/// Path node indices -> {lat,lon} plus the per-vertex way index, so bridge
/// tags survive the routing (the bake needs to know which waypoints are
/// on a deck).
export function pathGeometry(g, path) {
  return path.nodes.map((n, i) => ({
    lat: g.nodeLL[n].lat, lon: g.nodeLL[n].lon,
    // wayOf[i] is the way used to ENTER node i, so vertex 0 has none.
    way: path.ways[i] >= 0 ? path.ways[i] : path.ways[i + 1],
  }));
}

export function pathLength(g, geom) {
  let s = 0;
  for (let i = 1; i < geom.length; i++) {
    const a = g.toXZ(geom[i - 1].lat, geom[i - 1].lon);
    const b = g.toXZ(geom[i].lat, geom[i].lon);
    s += Math.hypot(b.x - a.x, b.z - a.z);
  }
  return s;
}
