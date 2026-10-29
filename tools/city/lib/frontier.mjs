// frontier.mjs - where the map's road data stops but the real road goes on
// (road-logic topology pass, 2026-10-29, from the traffic census).
//
// The census (Docs/ROAD_LOGIC_TRAFFIC.md run 1) counted 56 one-way traps, 47
// one-ways nobody can enter, 30 class-2+ dead ends, 47 inescapable and 13
// unreachable parts, and 128 cars that "ran out of legal ways". Traced to OSM
// (tools/city/cache), nearly all of them are not faults of the network but
// the EDGES OF THE DATA:
//
//   WORLD   the arterials are fetched by the beltway box (fetch_ways.mjs,
//           35.03,-81.03,35.42,-80.62). Overpass returns every way that
//           touches the box WHOLE, so a road crossing the box line runs on
//           past it to wherever its OSM way happens to end, and stops there:
//           I-77 at the SC 160 exit (-13333,-17187), Albemarle Road east of
//           the box, I-85 north of it, Rea Road, Wilkinson Boulevard. Their
//           ends lie 2-1900 m outside the box, so the census's old test (400 m
//           from the outermost node) missed most of them.
//   MINOR   the residential / unclassified / service streets are fetched only
//           in the core box (fetch_bld.mjs, 35.190,-80.880,35.262,-80.790).
//           Outside it, a ramp or an arterial that hands over to a local
//           street (a superstreet U-turn, a Carowinds lot, a business park)
//           ends at an OSM node no fetched way shares: 40 one-way traps and
//           32 one-ways nobody can enter, every one of them a link or a short
//           arterial piece 10-50 m from the road it serves.
//   DROPPED the exporter drops ways on purpose (toll lanes held back, unnamed
//           service aisles, private and no-access ways, busways): a kept road
//           whose only way on is one of those ends in the data where it goes
//           on in Charlotte (Morehead Medical Drive into the hospital's
//           service drive, Berkeley Avenue's private exit, the I-77 Express
//           Lanes at the county line).
//   ONEWAY  inside the core a one-way that ends (or starts) at a node no other
//           fetched way shares cannot really end there: it goes on as an
//           alley, a driveway or a parking aisle, which the street query does
//           not fetch (West 6th Street's two one-way pieces 58 m apart).
//   ISLAND  a piece of road joined to nothing else in the data (Duls Lane, two
//           unnamed unclassified stubs in the core) is joined in Charlotte to
//           what the street query does not fetch: its ends go on too.
//
// A FRONTIER node is one where some arc is missing - a dead end, a one-way
// arriving with no legal way on, a one-way leaving with no legal way in - and
// one of the four reasons above holds. A two-way dead end inside the core
// that no dropped way shares is a REAL dead end (a cul-de-sac) and is not a
// frontier; neither is a node whose arcs are all kept roads (a street that
// turns one-way against you: the traffic's look-ahead keeps out of those).
//
// What reads it (section FRNT, citydata.mjs): CityMap.nodeFrontier; the
// traffic leaves the map there instead of stopping at the end of the data
// (CityTraffic), and the census counts a frontier as the data's edge, not as
// a trap. Nothing is removed and nothing is invented: the cure for MINOR and
// DROPPED is the missing streets themselves (a fetch of the ways that share
// these nodes), listed by the exporter's log line "frontier ... (fetch:)".

export const FR_WORLD = 1, FR_MINOR = 2, FR_DROPPED = 4, FR_ONEWAY = 8, FR_ISLAND = 16;
export const FR_NAMES = { [FR_WORLD]: 'world box', [FR_MINOR]: 'minor streets not fetched here', [FR_DROPPED]: 'a dropped way', [FR_ONEWAY]: 'a one-way into what the map lacks', [FR_ISLAND]: 'a road joined to nothing else in the data' };

/// nodes [x, z]; edges { a, b, way: { id, oneway, rank, link } }; nodeIndex
/// Map(osm id -> graph node); raw [{ id, nodes, tags }] every way fetched
/// (kept or not); keptIds Set(way id); dropWhy(raw way) -> reason or '';
/// world / core [south, west, north, east]; toLat(z), toLon(x).
/// Returns { bits: Uint8Array(nodes), rows: [{ n, osm, bits, why, x, z }], stats }.
/// drivable(edge): whether traffic may use it (a parking aisle, a driveway,
/// an unpaved track carries no car on: a ramp into one is still open).
export function findFrontier({ nodes, edges, nodeIndex, raw, keptIds, dropWhy, world, core, toLat, toLon, worldMarginDeg = 0.004, drivable = () => true }) {
  const nn = nodes.length;
  const deg = new Int32Array(nn), exitsOf = new Int32Array(nn), entriesOf = new Int32Array(nn);
  // legal ways out of / into each node, counted per arc (a two-way edge is
  // both an exit and an entry at each of its ends; a one-way an exit at a,
  // an entry at b)
  for (const e of edges) {
    if (e.a === e.b || !drivable(e)) continue;
    deg[e.a]++; deg[e.b]++;
    exitsOf[e.a]++; entriesOf[e.b]++;
    if (!e.way.oneway) { exitsOf[e.b]++; entriesOf[e.a]++; }
  }
  // OPEN: some arc into or out of the node has nowhere to go
  const open = new Uint8Array(nn);
  for (const e of edges) {
    if (e.a === e.b || !drivable(e)) continue;
    // arriving at b: any way on besides this edge back (a U-turn on a two-way)?
    const onB = exitsOf[e.b] - (e.way.oneway ? 0 : 1);
    if (onB <= 0) open[e.b] = 1;
    if (!e.way.oneway) { const onA = exitsOf[e.a] - 1; if (onA <= 0) open[e.a] = 1; }
    // leaving a: any way in besides this edge itself?
    const inA = entriesOf[e.a] - (e.way.oneway ? 0 : 1);
    if (inA <= 0) open[e.a] = 1;
    if (!e.way.oneway) { const inB = entriesOf[e.b] - 1; if (inB <= 0) open[e.b] = 1; }
  }
  // the dropped ways at each graph node
  const dropAt = new Map();
  for (const w of raw) {
    if (keptIds.has(w.id)) continue;
    const why = dropWhy(w);
    if (!why) continue;
    for (const id of w.nodes) {
      const n = nodeIndex.get(id);
      if (n === undefined) continue;
      let l = dropAt.get(n);
      if (!l) dropAt.set(n, l = []);
      if (!l.some(r => r.id === w.id)) l.push({ id: w.id, why, hw: w.tags.highway || '' });
    }
  }
  const osmOf = new Map();
  for (const [id, n] of nodeIndex) osmOf.set(n, id);
  const inBox = (b, lat, lon, m = 0) => lat >= b[0] + m && lat <= b[2] - m && lon >= b[1] + m && lon <= b[3] - m;
  // one-way arcs that dead-end or have no way in, per node (ONEWAY)
  const onewayOpen = new Uint8Array(nn);
  for (const e of edges) {
    if (e.a === e.b || !e.way.oneway || !drivable(e)) continue;
    if (exitsOf[e.b] <= 0) onewayOpen[e.b] = 1;
    if (entriesOf[e.a] <= 0) onewayOpen[e.a] = 1;
  }
  // the pieces of road joined to nothing else (ISLAND): every connected
  // part but the biggest
  const part = new Int32Array(nn).fill(-1), adj = Array.from({ length: nn }, () => []);
  for (const e of edges) if (e.a !== e.b) { adj[e.a].push(e.b); adj[e.b].push(e.a); }
  const partSize = [];
  for (let n0 = 0; n0 < nn; n0++) {
    if (part[n0] >= 0 || !adj[n0].length) continue;
    const id = partSize.length, stack = [n0];
    part[n0] = id; let size = 0;
    while (stack.length) { const n = stack.pop(); size++; for (const m of adj[n]) if (part[m] < 0) { part[m] = id; stack.push(m); } }
    partSize.push(size);
  }
  let mainPart = 0;
  for (let i = 1; i < partSize.length; i++) if (partSize[i] > partSize[mainPart]) mainPart = i;
  const bits = new Uint8Array(nn);
  const rows = [];
  const stats = { open: 0, [FR_WORLD]: 0, [FR_MINOR]: 0, [FR_DROPPED]: 0, [FR_ONEWAY]: 0, [FR_ISLAND]: 0, realDeadEnds: 0, frontier: 0 };
  for (let n = 0; n < nn; n++) {
    if (!open[n]) continue;
    stats.open++;
    const [x, z] = nodes[n];
    const lat = toLat(z), lon = toLon(x);
    let b = 0;
    const why = [];
    if (!inBox(world, lat, lon, worldMarginDeg)) { b |= FR_WORLD; why.push('outside the beltway box'); }
    const drops = dropAt.get(n);
    if (drops) { b |= FR_DROPPED; why.push('goes on as ' + drops.map(r => `way ${r.id} (${r.hw}, ${r.why})`).join(', ')); }
    if (!(b & FR_WORLD) && !inBox(core, lat, lon)) { b |= FR_MINOR; why.push('outside the core streets box'); }
    if (!b && onewayOpen[n]) { b |= FR_ONEWAY; why.push('a one-way that ends or starts on no fetched way'); }
    if (!b && part[n] !== mainPart) { b |= FR_ISLAND; why.push('a road joined to nothing else in the data'); }
    if (!b) { stats.realDeadEnds++; continue; }
    bits[n] = b;
    stats.frontier++;
    for (const k of [FR_WORLD, FR_MINOR, FR_DROPPED, FR_ONEWAY, FR_ISLAND]) if (b & k) stats[k]++;
    rows.push({ n, osm: osmOf.get(n), bits: b, why: why.join('; '), x, z, lat, lon });
  }
  return { bits, rows, stats };
}
