// fetch_bogue.mjs — bake the Bogue Banks (Crystal Coast) stages for PSX Racing.
//
// Three venues off one island, all from real map data:
//
//   bogue_emerald   402 m on Emerald Drive — the longest genuine straight the
//                   island has (2.74 km), flat, a real-world quarter mile
//   bogue_langston  the B. Cameron Langston Bridge, 1409 m, over the crown
//   bogue_atlantic  the Atlantic Beach Bridge, 1141 m, likewise
//
// Emits per venue:
//   Assets/PSXRacing/Resources/<id>.json            waypoints at 4 m + bridges
//   Assets/PSXRacing/Art/Bogue/<id>_dem_near.bytes  12 m ground grid
//   Assets/PSXRacing/Art/Bogue/<id>_dem_far.bytes   60 m vista grid
//   Assets/PSXRacing/Art/Bogue/<id>_mask_near.bytes 1 byte/cell: land/sand/water
//   Assets/PSXRacing/Art/Bogue/<id>_dem_meta.json   grid origins/cells/dims
//
// ---------------------------------------------------------------------------
// WHY THIS IS NOT JUST fetch_brp WITH DIFFERENT COORDINATES
//
// 1. ROUTING, not chaining. The parkway is one road with no junctions worth
//    the name, so the BRP bake grows a chain by matching way endpoints. NC-58
//    forks at Atlantic Beach — north over the bridge, east along Fort Macon
//    Road — and a chainer takes whichever way matches first. The first attempt
//    at this walked 27 km up the MAINLAND leg of NC-58 toward Jacksonville.
//    So: build a graph and Dijkstra between anchors (see route.mjs).
//
// 2. THE DEM DOES NOT KNOW THE BRIDGES EXIST. SRTM is a radar return off the
//    water surface; both spans read as sea level, and the whole point of these
//    two venues is that they are 20 m in the air. Deck profiles are SYNTHESISED
//    against the real navigation clearance, with the crown placed where the
//    route actually crosses the Atlantic Intracoastal Waterway rather than at
//    the midpoint of the span — on a 1.4 km bridge those are not the same
//    place, and the difference is which half of the race is uphill.
//
// 3. A SURFACE MASK. A mountain is ground everywhere. A barrier island is
//    ocean, sound, sand and scrub, and the ground builder has to be told
//    which is which — so alongside the height grid this bakes a byte grid,
//    from OSM's coastline (land-on-left) and its beach polygons.
//
// Deliberately dependency-free. Downloads cache in tools/bogue/cache/.
//
// Data: (c) OpenStreetMap contributors (ODbL). Elevation: USGS/NASA SRTM.

import { mkdirSync, readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gunzipSync } from 'node:zlib';
import { loadGraph, nearestNode, shortestPath, pathGeometry, pathLength, LATM, lonMetres } from './route.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const cacheDir = join(here, 'cache');
const projectRoot = join(here, '..', '..');
mkdirSync(cacheDir, { recursive: true });

// ---------------------------------------------------------------- config
const BBOX = { s: 34.64, w: -77.14, n: 34.80, e: -76.65 };
const ORIGIN = { lat0: 34.69, lon0: -76.90 };   // graph frame; venues re-origin

const SPACING = 4;              // TrackCatalog.Spacing
const SMOOTH_SIGMA = 40;        // m — gentler than BRP: this land has no grades
                                //     to preserve, only SRTM noise to kill
const MAX_GRADE = 0.06;         // on LAND. Bridges set their own.

// Bridge decks. The Atlantic Intracoastal Waterway requires 65 ft of vertical
// clearance at fixed crossings, which is what makes both of these high-rises
// and is the only reason either is worth racing on.
const AIWW_CLEARANCE_M = 19.8;  // 65 ft
const DECK_DEPTH_M = 1.9;       // girder depth below the running surface
const ABUTMENT_M = 2.2;         // deck height where it meets the causeway
const SEABED_DROP_M = 4.0;      // how far the bed sits below the water surface

// Sea level in bake coordinates. Everything is measured from baseM, and a
// barrier island is at sea level, so baseM is chosen rather than derived:
// the water plane wants a fixed, KNOWN height that the mask grid, the pier
// footings and the ocean mesh can all agree on without re-deriving it.
const BASE_M = -6;              // => water surface sits at world y = 6
const WATER_Y = -BASE_M;

const NEAR_CELL = 12, NEAR_MARGIN = 900;
const FAR_CELL = 60, FAR_MARGIN = 7000;

// Surface mask values — must match PSXRacingBuilder.Stage's Surf enum.
//
// MARSH earns its own class rather than being folded into land or water. On
// this coast it is not a detail: aerial photographs of the Langston crossing
// are more than half salt marsh and tidal creek, and classifying it as open
// water (which is what the coastline test does with it) renders the dominant
// feature of the whole crossing as flat blue nothing.
const LAND = 0, SAND = 1, WATER = 2, MARSH = 3;
// Sand this far inland of any waterline. 55 m was the first guess and it put
// 3% sand on Emerald Isle — technically the wet strand, visually nothing, on
// an island made of sand. A barrier island's dune field runs well back from
// the water, and the beach polygons only cover the ocean side.
const SAND_BAND_M = 90;

// --------------------------------------------------------------- venues
const VENUES = [
  {
    id: 'bogue_emerald',
    name: 'Emerald Isle — Emerald Drive',
    // Both anchors sit ON the 2741 m straight probe3 found, so the routed
    // path between them cannot wander off it. Run west to east.
    anchors: [{ lat: 34.67100, lon: -76.99490 }, { lat: 34.67623, lon: -76.96567 }],
    runM: 402.336,              // a real-world quarter mile
    leadM: 60,
    shutdownM: 340,
    flat: true,                 // no bridge synthesis; this is a sand spit
  },
  {
    id: 'bogue_langston',
    name: 'B. Cameron Langston Bridge',
    // Mainland (Cape Carteret) to island (Emerald Isle) — you launch toward
    // the island. Endpoints are EXACT, off OSM way 42820377's own geometry.
    anchors: [{ lat: 34.67979, lon: -77.06710 }, { lat: 34.66750, lon: -77.06333 }],
    runM: null,                 // the whole span, measured from the route
    leadM: 150,
    shutdownM: 280,
  },
  {
    id: 'bogue_atlantic',
    name: 'Atlantic Beach Bridge',
    // Morehead City to Atlantic Beach. OSM way 16461727.
    anchors: [{ lat: 34.72111, lon: -76.73435 }, { lat: 34.71106, lon: -76.73684 }],
    runM: null,
    leadM: 150,
    shutdownM: 280,
  },
];

// ---------------------------------------------------------------- fetch
async function fetchCached(name, url, opts) {
  const path = join(cacheDir, name);
  if (existsSync(path)) return readFileSync(path);
  console.log('fetching ' + url);
  const res = await fetch(url, opts);
  if (!res.ok) throw new Error(url + ' -> HTTP ' + res.status);
  const buf = Buffer.from(await res.arrayBuffer());
  writeFileSync(path, buf);
  return buf;
}

async function overpass(name, q) {
  const mirrors = [
    'https://overpass-api.de/api/interpreter',
    'https://overpass.kumi.systems/api/interpreter',
    'https://overpass.private.coffee/api/interpreter',
    'https://maps.mail.ru/osm/tools/overpass/api/interpreter',
  ];
  let lastErr = null;
  for (let round = 0; round < 3; round++) {
    for (const url of mirrors) {
      try {
        const buf = await fetchCached(name, url, {
          method: 'POST', body: 'data=' + encodeURIComponent(q),
          headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'User-Agent': 'psx-racing-bogue-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)',
            'Accept': 'application/json',
          },
        });
        const parsed = JSON.parse(buf.toString('utf8'));
        if (!parsed.elements || !parsed.elements.length) throw new Error('empty result');
        return parsed;
      } catch (e) {
        lastErr = e; console.log('  mirror failed: ' + e.message);
        try { const p = join(cacheDir, name); if (existsSync(p)) unlinkSync(p); } catch {}
      }
    }
    if (round < 2) { console.log('  retrying in 15 s...'); await new Promise(r => setTimeout(r, 15000)); }
  }
  throw lastErr;
}

// ------------------------------------------------------------------ SRTM
// Bogue Banks straddles the W077/W078 tile boundary at lon -77, and the two
// bridges land on opposite sides of it, so this needs a multi-tile sampler
// where the BRP bake needed one file.
const tiles = new Map();
async function loadTile(latN, lonW) {
  const key = `N${String(latN).padStart(2, '0')}W${String(-lonW).padStart(3, '0')}`;
  if (tiles.has(key)) return tiles.get(key);
  const gz = await fetchCached(key + '.hgt.gz',
    `https://s3.amazonaws.com/elevation-tiles-prod/skadi/N${String(latN).padStart(2, '0')}/${key}.hgt.gz`);
  const raw = gunzipSync(gz);
  const posts = 3601;
  if (raw.length !== posts * posts * 2) throw new Error(`${key}: unexpected size ${raw.length}`);
  const t = { raw, posts, latN: latN + 1, lonW };
  tiles.set(key, t);
  console.log(`  SRTM ${key} loaded`);
  return t;
}

function sampleTile(t, lat, lon) {
  const { raw, posts, latN, lonW } = t;
  const at = (r, c) => {
    r = Math.min(posts - 1, Math.max(0, r)); c = Math.min(posts - 1, Math.max(0, c));
    return raw.readInt16BE((r * posts + c) * 2);
  };
  const fr = (latN - lat) * 3600, fc = (lon - lonW) * 3600;
  const r0 = Math.floor(fr), c0 = Math.floor(fc);
  const tr = fr - r0, tc = fc - c0;
  let h00 = at(r0, c0), h01 = at(r0, c0 + 1), h10 = at(r0 + 1, c0), h11 = at(r0 + 1, c0 + 1);
  const ok = v => v > -32000;
  // On the coast SRTM's voids ARE the sea. A void with no valid neighbour is
  // water, so it reads 0 rather than inheriting a dune's height.
  const fb = [h00, h01, h10, h11].find(ok) ?? 0;
  if (!ok(h00)) h00 = fb; if (!ok(h01)) h01 = fb;
  if (!ok(h10)) h10 = fb; if (!ok(h11)) h11 = fb;
  return (h00 * (1 - tc) + h01 * tc) * (1 - tr) + (h10 * (1 - tc) + h11 * tc) * tr;
}

let elevAt = null;
async function buildElevSampler() {
  // Cover the whole bbox: floor(lon) for the west edge through the east.
  for (let lat = Math.floor(BBOX.s); lat <= Math.floor(BBOX.n); lat++)
    for (let lon = Math.floor(BBOX.w); lon <= Math.floor(BBOX.e); lon++)
      await loadTile(lat, lon);
  elevAt = (lat, lon) => {
    const t = tiles.get(`N${String(Math.floor(lat)).padStart(2, '0')}W${String(-Math.floor(lon)).padStart(3, '0')}`);
    return t ? sampleTile(t, lat, lon) : 0;
  };
}

// ------------------------------------------------------- water & sand
// OSM does not tag the sea. It tags a COASTLINE, with land on the left of
// the way direction, and everything to the right is water by implication —
// so the land test is "which side of the nearest coastline segment am I on".
// A spatial hash keeps that from being 90k cells x 3k segments.
function buildSurface(waterOp, proj) {
  const segs = [];       // {ax,az,bx,bz}
  for (const e of waterOp.elements) {
    if (e.tags?.natural !== 'coastline' || !e.geometry) continue;
    const g = e.geometry;
    for (let i = 1; i < g.length; i++) {
      const a = proj.toXZ(g[i - 1].lat, g[i - 1].lon), b = proj.toXZ(g[i].lat, g[i].lon);
      segs.push({ ax: a.x, az: a.z, bx: b.x, bz: b.z });
    }
  }
  const CELL = 220;
  const hash = new Map();
  const put = (cx, cz, i) => {
    const k = cx + ',' + cz;
    let l = hash.get(k); if (!l) hash.set(k, l = []); l.push(i);
  };
  segs.forEach((s, i) => {
    const x0 = Math.floor(Math.min(s.ax, s.bx) / CELL), x1 = Math.floor(Math.max(s.ax, s.bx) / CELL);
    const z0 = Math.floor(Math.min(s.az, s.bz) / CELL), z1 = Math.floor(Math.max(s.az, s.bz) / CELL);
    for (let cz = z0; cz <= z1; cz++) for (let cx = x0; cx <= x1; cx++) put(cx, cz, i);
  });

  // Polygon sets, as {pts, minX, maxX, minZ, maxZ}, built once each.
  //
  // RELATIONS MATTER HERE. Overpass `out geom` hangs geometry on a relation's
  // MEMBERS, not on the relation itself, so `e.geometry` is undefined for
  // every multipolygon — and large marshes are almost always mapped as
  // multipolygons. Reading only ways found 15 km2 of wetland and silently
  // dropped 120 relations, which is why the first marsh bake came back at 6%
  // on a crossing that is more than half salt marsh in the photographs.
  const polysOf = kinds => {
    const out = [];
    const add = geom => {
      if (!geom || geom.length < 4) return;
      const pts = geom.map(p => proj.toXZ(p.lat, p.lon));
      const xs = pts.map(p => p.x), zs = pts.map(p => p.z);
      out.push({ pts, minX: Math.min(...xs), maxX: Math.max(...xs),
                 minZ: Math.min(...zs), maxZ: Math.max(...zs) });
    };
    for (const e of waterOp.elements) {
      if (!kinds.includes(e.tags?.natural)) continue;
      if (e.type === 'relation') {
        // Outer rings only. An inner ring is a pond inside the marsh, and
        // adding it as another marsh polygon would fill in the hole.
        for (const m of e.members || [])
          if (m.role !== 'inner') add(m.geometry);
      } else {
        add(e.geometry);
      }
    }
    return out;
  };
  const beaches = polysOf(['beach', 'sand']);
  const marshes = polysOf(['wetland']);

  const inPoly = (poly, x, z) => {
    if (x < poly.minX || x > poly.maxX || z < poly.minZ || z > poly.maxZ) return false;
    let inside = false;
    const p = poly.pts;
    for (let i = 0, j = p.length - 1; i < p.length; j = i++)
      if ((p[i].z > z) !== (p[j].z > z) &&
          x < (p[j].x - p[i].x) * (z - p[i].z) / (p[j].z - p[i].z) + p[i].x) inside = !inside;
    return inside;
  };

  /// Signed distance to the coastline: positive on land, negative at sea,
  /// |value| in metres. Returns +Infinity when no coastline is anywhere near
  /// (deep inland — the mainland behind Morehead City).
  function coastSigned(x, z) {
    const cx = Math.floor(x / CELL), cz = Math.floor(z / CELL);
    for (let ring = 0; ring <= 8; ring++) {
      let bestD2 = Infinity, bestSide = 1;
      for (let oz = -ring; oz <= ring; oz++)
        for (let ox = -ring; ox <= ring; ox++) {
          // only the new ring
          if (ring > 0 && Math.abs(ox) !== ring && Math.abs(oz) !== ring) continue;
          const l = hash.get((cx + ox) + ',' + (cz + oz));
          if (!l) continue;
          for (const i of l) {
            const s = segs[i];
            const ex = s.bx - s.ax, ez = s.bz - s.az;
            const len2 = ex * ex + ez * ez;
            let t = len2 > 1e-9 ? ((x - s.ax) * ex + (z - s.az) * ez) / len2 : 0;
            t = Math.max(0, Math.min(1, t));
            const qx = s.ax + ex * t, qz = s.az + ez * t;
            const d2 = (qx - x) ** 2 + (qz - z) ** 2;
            if (d2 < bestD2) {
              bestD2 = d2;
              // left of the way direction is LAND (OSM convention)
              bestSide = (ex * (z - s.az) - ez * (x - s.ax)) > 0 ? 1 : -1;
            }
          }
        }
      // Only trust the answer once we have searched a ring beyond the hit,
      // or a segment in a diagonal bucket can beat one we never looked at.
      if (bestD2 < Infinity && ring >= 1) return bestSide * Math.sqrt(bestD2);
    }
    return Infinity;
  }

  return { coastSigned, beaches, marshes, inPoly, segCount: segs.length };
}

// --------------------------------------------------------------- helpers
function splineResample(pts, spacing) {
  const dense = [];
  const P = i => pts[Math.max(0, Math.min(pts.length - 1, i))];
  const d2 = (a, b) => (a.x - b.x) ** 2 + (a.z - b.z) ** 2;
  for (let i = 0; i + 1 < pts.length; i++) {
    const p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
    const steps = Math.max(2, Math.ceil(Math.sqrt(d2(p1, p2))));
    for (let s = 0; s < steps; s++) {
      const t = s / steps, t2 = t * t, t3 = t2 * t;
      dense.push({
        x: 0.5 * (2 * p1.x + (-p0.x + p2.x) * t + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2
          + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3),
        z: 0.5 * (2 * p1.z + (-p0.z + p2.z) * t + (2 * p0.z - 5 * p1.z + 4 * p2.z - p3.z) * t2
          + (-p0.z + 3 * p1.z - 3 * p2.z + p3.z) * t3),
        srcI: i,
      });
    }
  }
  dense.push({ ...pts.at(-1), srcI: pts.length - 1 });
  const out = [dense[0]];
  let acc = 0;
  for (let i = 1; i < dense.length; i++) {
    const d = Math.sqrt(d2(dense[i - 1], dense[i]));
    acc += d;
    while (acc >= spacing) {
      const over = acc - spacing, t = d > 1e-9 ? over / d : 0;
      out.push({ x: dense[i].x + (dense[i - 1].x - dense[i].x) * t,
                 z: dense[i].z + (dense[i - 1].z - dense[i].z) * t, srcI: dense[i].srcI });
      acc = over;
    }
  }
  return out;
}

const smoothstep = u => u * u * (3 - 2 * u);

function gaussian(arr, sigma, spacing) {
  const win = Math.ceil(sigma * 3 / spacing);
  return arr.map((_, i) => {
    let sw = 0, sh = 0;
    for (let o = -win; o <= win; o++) {
      const j = Math.max(0, Math.min(arr.length - 1, i + o));
      const w = Math.exp(-((o * spacing) ** 2) / (2 * sigma * sigma));
      sw += w; sh += w * arr[j];
    }
    return sh / sw;
  });
}

// ------------------------------------------------------------------ main
console.log('loading road graph...');
const roadQ = `[out:json][timeout:180];
way["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|motorway_link|trunk_link|primary_link|secondary_link)$"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
out tags geom;`;
await overpass('overpass_bogue.json', roadQ);          // ensure cached
const g = loadGraph(join(cacheDir, 'overpass_bogue.json'), ORIGIN);
console.log(`  ${g.ways.length} routable ways, ${g.nodeLL.length} nodes`);

const waterQ = `[out:json][timeout:180];
(
  way["natural"="coastline"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["natural"="water"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["natural"="beach"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["natural"="sand"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["natural"="wetland"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["waterway"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["seamark:type"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  relation["natural"="water"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  relation["natural"="wetland"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
);
out body geom;`;
// `body geom`, not `tags geom`. The `tags` verbosity prints a relation's tags
// and NOTHING ELSE — no member list — so all 120 wetland multipolygons came
// back as a bare bounding box and were dropped. Ways were unaffected, which is
// what made it look like the data simply was not there.
//
// New cache key each time the query changes: a cached response is
// indistinguishable from a query that legitimately returned nothing.
const waterOp = await overpass('overpass_bogue_water3.json', waterQ);
console.log(`  ${waterOp.elements.length} water/coast elements`);

// The navigation channel, as polylines in graph coordinates. The crown of a
// bridge goes where the route crosses THIS, not where the span happens to be
// halfway across.
const aiww = waterOp.elements
  .filter(e => /Intracoastal/i.test(e.tags?.name || '') && e.geometry)
  .map(e => e.geometry);
console.log(`  Intracoastal Waterway: ${aiww.length} ways, ` +
  `${aiww.reduce((a, w) => a + w.length, 0)} vertices`);
if (!aiww.length) throw new Error('AIWW not found — bridge crowns cannot be placed');

console.log('loading SRTM...');
await buildElevSampler();

const outArt = join(projectRoot, 'Assets', 'PSXRacing', 'Art', 'Bogue');
const outRes = join(projectRoot, 'Assets', 'PSXRacing', 'Resources');
mkdirSync(outArt, { recursive: true });

for (const V of VENUES) {
  console.log(`\n================ ${V.id} — ${V.name}`);

  // ---- route it
  const snaps = V.anchors.map(a => {
    const s = nearestNode(g, a.lat, a.lon);
    return s;
  });
  console.log(`  anchors snapped: ${snaps.map(s => s.offM.toFixed(0) + ' m').join(', ')}`);
  let geom = [];
  for (let i = 0; i + 1 < snaps.length; i++) {
    const p = shortestPath(g, snaps[i].node, snaps[i + 1].node);
    if (!p) throw new Error(`${V.id}: no path between anchors ${i} and ${i + 1}`);
    const gg = pathGeometry(g, p);
    geom.push(...(i ? gg.slice(1) : gg));
  }
  const coreM = pathLength(g, geom);
  console.log(`  routed core: ${coreM.toFixed(0)} m over ${geom.length} vertices`);

  // ---- extend by LEAD before and SHUTDOWN after, along the road
  // The grid stands on the lead-in and a car crossing the traps at 250 km/h
  // has to have somewhere to stop, and neither is optional on a bridge whose
  // far abutment is a T-junction.
  //
  // The last edge is INTERPOLATED rather than taken whole. Out here the graph
  // has 400 m edges (the bridge itself is a single 1409 m two-node way), and
  // walking edge-at-a-time overshot a 90 m lead-in by 368 m — which put the
  // start line a quarter of a mile back down the causeway.
  //
  // It follows the STRAIGHTEST continuation, not the cheapest edge. Choosing
  // by road class alone turned the walk onto whatever side street left the
  // junction first: the Atlantic Beach lead-in came back onto the bridge at an
  // angle and left an 11 m-radius hairpin 48 m past the start line, on a
  // venue whose entire selling point is that it is straight. A lead-in is more
  // of the same road, so heading continuity decides it and class only breaks
  // ties.
  function extend(fromNodeIdx, awayFromNodeIdx, metres) {
    const out = [];              // [{lat,lon,segWay}] — segWay is the way of
                                 // the segment ARRIVING at this point
    const heading = (from, to) => {
      const a = g.nodeLL[from], b = g.nodeLL[to];
      const mLon = lonMetres(a.lat);
      return Math.atan2((b.lon - a.lon) * mLon, (b.lat - a.lat) * LATM);
    };
    const turnFrom = (inHdg, from, to) => {
      let t = Math.abs(heading(from, to) - inHdg);
      if (t > Math.PI) t = 2 * Math.PI - t;
      return t;
    };
    let cur = fromNodeIdx, prev = awayFromNodeIdx, run = 0;
    // Heading of the step we are continuing: from the route's second node back
    // to the anchor, which points OUT along the lead-in.
    let inHdg = heading(awayFromNodeIdx, fromNodeIdx);
    while (run < metres) {
      let bestE = null, bestScore = Infinity;
      for (const e of g.adj[cur]) {
        if (e.to === prev) continue;
        const turn = turnFrom(inHdg, cur, e.to);
        // A lead-in that has to turn 70 degrees is not a lead-in, it is a
        // different road. Better to come up short than to bend the venue.
        if (turn > 1.22) continue;
        const score = turn + 0.05 * (e.cost / e.m);
        if (score < bestScore) { bestScore = score; bestE = e; }
      }
      if (!bestE) break;
      const a = g.nodeLL[cur], b = g.nodeLL[bestE.to];
      inHdg = heading(cur, bestE.to);
      if (run + bestE.m > metres) {
        const t = (metres - run) / bestE.m;
        out.push({ lat: a.lat + (b.lat - a.lat) * t, lon: a.lon + (b.lon - a.lon) * t,
                   segWay: bestE.wayIdx });
        run = metres;
        break;
      }
      out.push({ lat: b.lat, lon: b.lon, segWay: bestE.wayIdx });
      run += bestE.m; prev = cur; cur = bestE.to;
    }
    return { pts: out, got: run };
  }
  const headNode = snaps[0].node, tailNode = snaps.at(-1).node;
  // second node along the route, so "away" is unambiguous
  const secondFromHead = nearestNode(g, geom[1].lat, geom[1].lon).node;
  const secondFromTail = nearestNode(g, geom.at(-2).lat, geom.at(-2).lon).node;
  const lead = extend(headNode, secondFromHead, V.leadM);
  const tail = extend(tailNode, secondFromTail, V.shutdownM);
  console.log(`  lead-in ${lead.got.toFixed(0)}/${V.leadM} m, shutdown ${tail.got.toFixed(0)}/${V.shutdownM} m`);

  // Assemble the full point list AND a parallel per-SEGMENT way list.
  //
  // Carrying the way on the point instead was the other half of the span bug:
  // pathGeometry hangs the ENTERING way on each node while extend() hangs the
  // LEAVING way, so the two halves disagreed about what segment a way
  // described, and testing `wayOf(i) || wayOf(i+1)` to paper over it flagged
  // the segments either side of a deck as deck too. segWay[k] is the way of
  // the segment from full[k] to full[k+1], and nothing has to guess.
  const full = [];
  const segWay = [];
  const leadFwd = lead.pts.slice().reverse();   // now runs INTO the route
  for (let i = 0; i < leadFwd.length; i++) {
    full.push(leadFwd[i]);
    // segment i -> i+1 is the one arriving at the NEXT point inward, which
    // for a reversed outward walk is this point's own segWay.
    segWay.push(leadFwd[i].segWay);
  }
  for (let i = 0; i < geom.length; i++) {
    full.push(geom[i]);
    if (i + 1 < geom.length) segWay.push(geom[i + 1].way);
  }
  for (let i = 0; i < tail.pts.length; i++) {
    segWay.push(tail.pts[i].segWay);
    full.push(tail.pts[i]);
  }
  const startOffset = lead.got;   // metres from waypoint 0 to the start line

  // ---- project about the venue centroid so the numbers stay small
  const cLat = full.reduce((a, p) => a + p.lat, 0) / full.length;
  const cLon = full.reduce((a, p) => a + p.lon, 0) / full.length;
  const mLon = lonMetres(cLat);
  const proj = {
    toXZ: (lat, lon) => ({ x: (lon - cLon) * mLon, z: (lat - cLat) * LATM }),
    toLL: (x, z) => ({ lat: cLat + z / LATM, lon: cLon + x / mLon }),
  };
  let xz = full.map(p => proj.toXZ(p.lat, p.lon));
  let segs = segWay.slice();

  // ---- densify before splining.
  //
  // Catmull-Rom with uniform parameterisation is only well behaved when
  // neighbouring segments are of comparable length. OSM models both of these
  // bridges as a SINGLE two-vertex way — 1288 m in one segment — so the point
  // list arriving at the deck reads 40 m, 40 m, 1288 m, and the tangent the
  // spline derives at that junction is enormous. The result was an 11 m-radius
  // hairpin 48 m past the Atlantic Beach start line: a kink the map data does
  // not contain, on the one venue whose entire premise is that it is straight.
  //
  // Splitting long segments into even pieces leaves straight lines exactly
  // straight and makes the parameterisation uniform, which is the actual fix.
  // (Centripetal Catmull-Rom would also do it; this keeps one spline routine
  // for both bake scripts.)
  {
    const MAXSEG = 25;
    const dx = [xz[0]], dw = [];
    for (let i = 1; i < xz.length; i++) {
      const a = xz[i - 1], b = xz[i];
      const d = Math.hypot(b.x - a.x, b.z - a.z);
      const n = Math.max(1, Math.ceil(d / MAXSEG));
      for (let k = 1; k <= n; k++) {
        dx.push({ x: a.x + (b.x - a.x) * k / n, z: a.z + (b.z - a.z) * k / n });
        dw.push(segs[i - 1]);      // every piece inherits its parent's way
      }
    }
    console.log(`  densified ${xz.length} -> ${dx.length} source points ` +
      `(max segment ${MAXSEG} m)`);
    xz = dx; segs = dw;
  }

  // ---- 4 m waypoints
  const wp = splineResample(xz, SPACING);
  console.log(`  waypoints: ${wp.length} (${(wp.length * SPACING / 1000).toFixed(2)} km)`);

  // ---- which waypoints are on a bridge deck (from the OSM way tags).
  // srcI is the index of the SOURCE SEGMENT the waypoint was resampled from,
  // so this is a straight lookup — no neighbour test, no OR.
  const onBridge = wp.map(p => {
    const s = Math.max(0, Math.min(segs.length - 1, p.srcI));
    return !!g.ways[segs[s]]?.tags?.bridge;
  });
  const spans = [];
  for (let i = 0, open = -1; i < onBridge.length; i++) {
    if (onBridge[i] && open < 0) open = i;
    if ((!onBridge[i] || i === onBridge.length - 1) && open >= 0) {
      if ((i - open) * SPACING >= 40) spans.push([open, i]);
      open = -1;
    }
  }
  console.log(`  bridge spans: ${spans.map(s =>
    `${(s[0] * SPACING).toFixed(0)}-${(s[1] * SPACING).toFixed(0)} m ` +
    `(${((s[1] - s[0]) * SPACING).toFixed(0)} m)`).join(', ') || 'none'}`);

  // ---- heights: DEM on land, synthesised on a deck.
  // MINUS BASE_M, in the same breath as the sample — the terrain grid below
  // is baked in baseM-relative metres, and a road left in raw ASL sits a
  // clean BASE_M under its own ground for the whole run.
  const rawH = wp.map(p => {
    const ll = proj.toLL(p.x, p.z);
    return elevAt(ll.lat, ll.lon) - BASE_M;
  });
  let h = gaussian(rawH, SMOOTH_SIGMA, SPACING);
  // Anything the DEM claims is below the water plane on LAND is SRTM noise
  // over the marsh; clamp it up so the causeway does not dip into the sound.
  for (let i = 0; i < h.length; i++) h[i] = Math.max(h[i], WATER_Y + 0.6);

  for (const [i0, i1] of spans) {
    // Where does the route cross the navigation channel? That is the crown.
    let crown = -1, bestD = Infinity;
    for (let i = i0; i <= i1; i++) {
      const ll = proj.toLL(wp[i].x, wp[i].z);
      for (const way of aiww)
        for (let k = 1; k < way.length; k++) {
          const a = proj.toXZ(way[k - 1].lat, way[k - 1].lon);
          const b = proj.toXZ(way[k].lat, way[k].lon);
          const ex = b.x - a.x, ez = b.z - a.z, len2 = ex * ex + ez * ez;
          if (len2 < 1e-9) continue;
          let t = ((wp[i].x - a.x) * ex + (wp[i].z - a.z) * ez) / len2;
          t = Math.max(0, Math.min(1, t));
          const d = Math.hypot(a.x + ex * t - wp[i].x, a.z + ez * t - wp[i].z);
          if (d < bestD) { bestD = d; crown = i; }
        }
    }
    const mid = Math.round((i0 + i1) / 2);
    if (crown < 0 || bestD > 400) {
      console.log(`  ! channel not found near this span (nearest ${bestD.toFixed(0)} m) — crown at midpoint`);
      crown = mid;
    }
    const deckY = WATER_Y + AIWW_CLEARANCE_M + DECK_DEPTH_M;
    const abut = WATER_Y + ABUTMENT_M;
    const abutA = Math.max(h[i0], abut), abutB = Math.max(h[i1], abut);
    // Smoothstep each side: zero grade at both abutments AND at the crown, so
    // there is no kink anywhere on the deck, and max grade is exactly 1.5x the
    // average — which for these two spans lands at a realistic 4-5%.
    for (let i = i0; i <= i1; i++) {
      let u, a0;
      if (i <= crown) { u = crown === i0 ? 1 : (i - i0) / (crown - i0); a0 = abutA; }
      else { u = crown === i1 ? 1 : (i1 - i) / (i1 - crown); a0 = abutB; }
      h[i] = a0 + (deckY - a0) * smoothstep(Math.max(0, Math.min(1, u)));
    }
    const climbM = (crown - i0) * SPACING, dropM = (i1 - crown) * SPACING;
    const gUp = climbM > 1 ? 1.5 * (deckY - abutA) / climbM : 0;
    const gDn = dropM > 1 ? 1.5 * (deckY - abutB) / dropM : 0;
    console.log(`    crown at ${(crown * SPACING).toFixed(0)} m ` +
      `(channel ${bestD.toFixed(0)} m away, midpoint would be ${(mid * SPACING).toFixed(0)} m)`);
    console.log(`    deck ${deckY.toFixed(1)} m; climb ${climbM.toFixed(0)} m @ ${(gUp * 100).toFixed(1)}% max, ` +
      `descent ${dropM.toFixed(0)} m @ ${(gDn * 100).toFixed(1)}% max`);
  }

  // Grade clamp on the LAND only — a clamp across a deck would flatten the
  // profile that is the entire reason the venue exists.
  const onDeck = new Array(h.length).fill(false);
  for (const [i0, i1] of spans) for (let i = i0; i <= i1; i++) onDeck[i] = true;
  for (let pass = 0; pass < 2; pass++) {
    for (let i = 1; i < h.length; i++)
      if (!onDeck[i] && !onDeck[i - 1] && h[i] - h[i - 1] > MAX_GRADE * SPACING)
        h[i] = h[i - 1] + MAX_GRADE * SPACING;
    for (let i = h.length - 2; i >= 0; i--)
      if (!onDeck[i] && !onDeck[i + 1] && h[i] - h[i + 1] > MAX_GRADE * SPACING)
        h[i] = h[i + 1] + MAX_GRADE * SPACING;
  }

  // A LIGHT smooth over the finished profile, decks included.
  //
  // The deck curve is zero-grade at each abutment by construction, but the
  // LAND it lands on is whatever the DEM said, and abutA/abutB floor that at
  // the causeway height — so the deck can end 0.4 m above the next waypoint
  // and leave a 10% cliff in the last 4 m of a bridge that is otherwise a
  // clean 4.5%. Sigma 12 m spreads that over ~36 m; against the deck's own
  // ~4 km vertical radius it costs under 2 cm of crown height.
  h = gaussian(h, 12, SPACING);

  let maxGrade = 0, gAt = 0;
  for (let i = 1; i < h.length; i++) {
    const gr = Math.abs(h[i] - h[i - 1]) / SPACING;
    if (gr > maxGrade) { maxGrade = gr; gAt = i * SPACING; }
  }
  // Crest radius at the crown, which is what decides whether a car goes
  // light over the top. R = 1 / |d2h/ds2|, sampled over a 40 m chord so a
  // single noisy waypoint cannot dominate it.
  let minR = Infinity, rAt = 0;
  const step = Math.round(20 / SPACING);
  for (let i = step; i + step < h.length; i++) {
    const d2 = (h[i + step] - 2 * h[i] + h[i - step]) / ((step * SPACING) ** 2);
    if (Math.abs(d2) < 1e-9) continue;
    const R = 1 / Math.abs(d2);
    if (R < minR) { minR = R; rAt = i * SPACING; }
  }
  console.log(`  elevation ${Math.min(...h).toFixed(1)}..${Math.max(...h).toFixed(1)} m, ` +
    `max grade ${(maxGrade * 100).toFixed(1)}% at ${gAt.toFixed(0)} m, ` +
    `min crest radius ${minR > 1e5 ? 'flat' : minR.toFixed(0) + ' m'} at ${rAt.toFixed(0)} m`);

  // ---- the run: start line, finish line
  //
  // A bridge run is the whole deck, and the deck begins where the OSM bridge
  // way does — EXCEPT that the approach can still be turning onto it there.
  // Morehead City's is: a pair of 165 m-radius jogs that end 52 m past the
  // first deck waypoint. They are only 4-degree kinks, and on any other venue
  // nobody would look twice, but this one is a standing-start drag race and
  // the start line has no business being inside a bend. So the line advances
  // to the first station with STRAIGHT_CHECK metres of straight road ahead of
  // it, and says how far it moved.
  const STRAIGHT_R = 400, STRAIGHT_CHECK = 140;
  const planR = i => {
    if (i < 2 || i + 2 >= wp.length) return Infinity;
    const a = wp[i - 2], b = wp[i], c = wp[i + 2];
    const ab = Math.hypot(b.x - a.x, b.z - a.z), bc = Math.hypot(c.x - b.x, c.z - b.z);
    const ca = Math.hypot(a.x - c.x, a.z - c.z);
    const area = Math.abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) / 2;
    return area < 1e-9 ? Infinity : ab * bc * ca / (4 * area);
  };
  let startIdx = V.runM != null ? Math.round(startOffset / SPACING)
    : (spans.length ? spans[0][0] : Math.round(startOffset / SPACING));
  const spanEnd = spans.length ? spans.at(-1)[1] : wp.length - 1;
  {
    const need = Math.round(STRAIGHT_CHECK / SPACING);
    const limit = Math.min(spanEnd - need, startIdx + Math.round(400 / SPACING));
    const was = startIdx;
    while (startIdx < limit) {
      let ok = true;
      for (let i = startIdx; i <= startIdx + need; i++)
        if (planR(i) < STRAIGHT_R) { ok = false; break; }
      if (ok) break;
      startIdx++;
    }
    if (startIdx !== was)
      console.log(`  start line advanced ${((startIdx - was) * SPACING).toFixed(0)} m ` +
        `past a bend in the approach`);
  }
  const startLineM = startIdx * SPACING;
  const runM = V.runM != null ? V.runM : (spanEnd - startIdx) * SPACING;
  console.log(`  start line ${startLineM.toFixed(0)} m, finish ${(startLineM + runM).toFixed(0)} m ` +
    `(run ${runM.toFixed(1)} m)`);
  if (startLineM + runM > (wp.length - 1) * SPACING)
    throw new Error(`${V.id}: finish past the end of the route — raise shutdownM`);

  // ---- surface + DEM grids
  const surf = buildSurface(waterOp, proj);
  console.log(`  coastline segments in frame: ${surf.segCount}, beach polys: ${surf.beaches.length}`);

  function bakeGrid(name, cell, margin, withMask) {
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const p of wp) {
      minX = Math.min(minX, p.x); maxX = Math.max(maxX, p.x);
      minZ = Math.min(minZ, p.z); maxZ = Math.max(maxZ, p.z);
    }
    minX -= margin; maxX += margin; minZ -= margin; maxZ += margin;
    const cols = Math.ceil((maxX - minX) / cell) + 1;
    const rows = Math.ceil((maxZ - minZ) / cell) + 1;
    const buf = Buffer.alloc(cols * rows * 2);
    const mask = withMask ? Buffer.alloc(cols * rows) : null;
    let nW = 0, nS = 0, nL = 0, nM = 0;
    for (let r = 0; r < rows; r++)
      for (let c = 0; c < cols; c++) {
        const x = minX + c * cell, z = minZ + r * cell;
        const ll = proj.toLL(x, z);
        const sd = surf.coastSigned(x, z);
        const marsh = surf.marshes.some(b => surf.inPoly(b, x, z));
        const isWater = sd < 0 && !marsh;
        let hh;
        if (isWater) {
          // The seabed, not the radar return off the surface. Flat and just
          // deep enough that the water plane covers it everywhere.
          hh = WATER_Y - SEABED_DROP_M;
        } else if (marsh) {
          // Salt marsh sits at about the high-water line: a hand above the
          // sea plane, so it reads as land you could not drive on rather than
          // as either open water or a field. Flat, because it is — the DEM
          // over marsh is the radar return off the grass and is pure noise.
          hh = WATER_Y + 0.35;
        } else {
          hh = Math.max(elevAt(ll.lat, ll.lon) - BASE_M, WATER_Y + 0.4);
        }
        buf.writeInt16LE(Math.max(-32000, Math.min(32000, Math.round(hh * 10))), (r * cols + c) * 2);
        if (mask) {
          let m;
          // MARSH FIRST. Almost all of it is seaward of the coastline, so any
          // test that asks "is this water?" before "is this marsh?" classifies
          // the entire tidal flat as open sound — which is exactly what the
          // first bake did.
          if (marsh) { m = MARSH; nM++; }
          else if (isWater) { m = WATER; nW++; }
          else if (sd < SAND_BAND_M || surf.beaches.some(b => surf.inPoly(b, x, z))) { m = SAND; nS++; }
          else { m = LAND; nL++; }
          mask[r * cols + c] = m;
        }
      }
    writeFileSync(join(outArt, `${V.id}_${name}.bytes`), buf);
    if (mask) writeFileSync(join(outArt, `${V.id}_mask_near.bytes`), mask);
    const tot = nW + nS + nL + nM;
    console.log(`  ${name}: ${cols}x${rows} @ ${cell} m (${(buf.length / 1024).toFixed(0)} KB)` +
      (mask ? ` — ${(100 * nW / tot).toFixed(0)}% water, ${(100 * nM / tot).toFixed(0)}% marsh, ` +
        `${(100 * nS / tot).toFixed(0)}% sand, ${(100 * nL / tot).toFixed(0)}% land` : ''));
    return { originX: minX, originZ: minZ, cell, cols, rows };
  }

  const near = bakeGrid('dem_near', NEAR_CELL, NEAR_MARGIN, true);
  const far = bakeGrid('dem_far', FAR_CELL, FAR_MARGIN, false);

  // ---- surface self-check.
  // The one thing the land/water classifier can get catastrophically wrong is
  // its SIGN, and an inverted coastline test does not throw — it produces a
  // beautiful bake of an island that is entirely underwater. So: walk the
  // route. Tarmac may only be over water where there is a deck over it.
  {
    let wet = 0, wetOffDeck = 0, firstBad = -1;
    for (let i = 0; i < wp.length; i++) {
      // Marsh counts as dry for this test. A causeway crossing tidal flats on
      // low fill is normal here and is not the failure this guards against —
      // which is an INVERTED coastline sign putting the whole island at sea.
      if (surf.coastSigned(wp[i].x, wp[i].z) >= 0) continue;
      if (surf.marshes.some(b => surf.inPoly(b, wp[i].x, wp[i].z))) continue;
      wet++;
      if (!onDeck[i]) { wetOffDeck++; if (firstBad < 0) firstBad = i; }
    }
    const pct = (100 * wet / wp.length).toFixed(0);
    console.log(`  route over water: ${wet}/${wp.length} waypoints (${pct}%), ` +
      `${wetOffDeck} of them with no deck`);
    if (wetOffDeck > wp.length * 0.05)
      throw new Error(`${V.id}: ${wetOffDeck} waypoints are on open water with no bridge ` +
        `(first at ${(firstBad * SPACING).toFixed(0)} m) — the coastline sign test is wrong`);
  }
  writeFileSync(join(outArt, `${V.id}_dem_meta.json`),
    JSON.stringify({ baseM: BASE_M, waterY: WATER_Y, near, far }, null, 2));

  // ---- stage json
  const xyz = [];
  for (let i = 0; i < wp.length; i++)
    xyz.push(+wp[i].x.toFixed(2), +h[i].toFixed(2), +wp[i].z.toFixed(2));
  const stage = {
    name: V.name,
    attribution: 'Route data (c) OpenStreetMap contributors. Elevation: USGS/NASA SRTM.',
    lat0: cLat, lon0: cLon, baseM: BASE_M, waterY: WATER_Y,
    spacing: SPACING,
    startLineM,
    finishM: startLineM + runM,
    bridges: spans.flatMap(([a, b]) => [a * SPACING, b * SPACING]),
    xyz,
  };
  const p = join(outRes, `${V.id}.json`);
  writeFileSync(p, JSON.stringify(stage));
  console.log(`  ${V.id}.json: ${wp.length} pts, ${(readFileSync(p).length / 1024).toFixed(0)} KB`);
}

console.log('\nOK');
