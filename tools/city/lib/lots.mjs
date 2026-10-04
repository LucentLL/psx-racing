// lots.mjs - roads pass L8 (plan B9 lean, A17 data): the parking lots, their
// stall lines and entrances, and the turning circles, from the ONE Overpass
// fetch (tools/city/fetch/fetch_lots.mjs -> cache/lots_core.json).
//
// A LOT is an OSM amenity=parking area (ways, and multipolygons' closed outer
// rings) that is a surface lot: not multi-storey, underground, rooftop, and
// not street-side / lane parking (that is the road's own shoulder). Each is
// rasterised at 0.25 m and the road bands are taken out of it - the OSM line
// plus the ribbon's half width plus 1.0 m (the verge's shoulder and toe), so
// the lot meets the verge's foot and never lies under a lane. What is left is
// traced (marching squares, then RDP at 0.2 m) into rings; a piece under
// 40 m2, or a lot with under 30 % left, is dropped.
//
// STALLS: rows of 90 degree stalls, 2.74 x 5.49 m, double-loaded about a
// 7.32 m aisle (an 18.3 m module), laid along the lot's longest side. A stall
// is kept only wholly inside the lot inset 0.3 m and 0.6 m clear of every
// building; a row needs two. The lines are the stall separators (0.10 m
// white at run time), as runs: a row's first separator and its stall count.
//
// ENTRANCES: one per lot ring, on the nearest street of tertiary class or
// below (not a link) within 25 m of it and 15 m or more from either end of
// that street's edge: the edge, the arc position, the side, the half width
// (3.65 m; 2.15 m on a one-way). The game pours a concrete apron there.
//
// AISLES, ISLANDS, HOLES (leftover item 4, 2026-10-03): see buildLots.
//
// TURNING CIRCLES: highway=turning_circle / turning_loop nodes that are a
// graph node with one edge (a dead end): the node, the kind, the radius
// (OSM diameter/2 when tagged, else 12.2 m: AASHTO's 40 ft cul-de-sac).

const CELL = 0.25;
export const STALL_W = 2.74, STALL_D = 5.49, AISLE = 7.32, LINE_W = 0.10;

function ringArea(r) {
  let a = 0;
  for (let i = 0; i < r.length; i++) { const p = r[i], q = r[(i + 1) % r.length]; a += p[0] * q[1] - q[0] * p[1]; }
  return a / 2;
}

function rdp(pts, eps) {
  if (pts.length < 3) return pts.slice();
  const keep = new Uint8Array(pts.length); keep[0] = keep[pts.length - 1] = 1;
  const stack = [[0, pts.length - 1]];
  while (stack.length) {
    const [i0, i1] = stack.pop();
    const [ax, az] = pts[i0], [bx, bz] = pts[i1];
    const dx = bx - ax, dz = bz - az, L = Math.hypot(dx, dz) || 1e-9;
    let best = -1, bd = eps;
    for (let i = i0 + 1; i < i1; i++) {
      const d = Math.abs((pts[i][0] - ax) * dz - (pts[i][1] - az) * dx) / L;
      if (d > bd) { bd = d; best = i; }
    }
    if (best >= 0) { keep[best] = 1; stack.push([i0, best], [best, i1]); }
  }
  return pts.filter((_, i) => keep[i]);
}

/// A closed ring simplified: split at its two farthest-apart points so RDP
/// has fixed ends.
function rdpRing(r, eps) {
  if (r.length < 4) return r.slice();
  let i0 = 0, i1 = 0, bd = -1;
  for (let i = 0; i < r.length; i++) {
    const d = (r[i][0] - r[0][0]) ** 2 + (r[i][1] - r[0][1]) ** 2;
    if (d > bd) { bd = d; i1 = i; }
  }
  const a = rdp(r.slice(i0, i1 + 1), eps);
  const b = rdp(r.slice(i1).concat([r[0]]), eps);
  return a.concat(b.slice(1, -1));
}

/// Fill a polygon into a mask (cell centres inside, even-odd).
function fillPoly(mask, nx, nz, x0, z0, ring, value) {
  for (let iz = 0; iz < nz; iz++) {
    const z = z0 + (iz + 0.5) * CELL;
    const xs = [];
    for (let i = 0; i < ring.length; i++) {
      const [ax, az] = ring[i], [bx, bz] = ring[(i + 1) % ring.length];
      if ((az <= z) !== (bz <= z)) xs.push(ax + (z - az) / (bz - az) * (bx - ax));
    }
    xs.sort((p, q) => p - q);
    for (let k = 0; k + 1 < xs.length; k += 2) {
      const ia = Math.max(0, Math.ceil((xs[k] - x0) / CELL - 0.5)), ib = Math.min(nx - 1, Math.floor((xs[k + 1] - x0) / CELL - 0.5));
      for (let ix = ia; ix <= ib; ix++) mask[iz * nx + ix] = value;
    }
  }
}

/// Clear every cell within d of segment a-b.
function carveSeg(mask, nx, nz, x0, z0, ax, az, bx, bz, d, value = 0) {
  const ix0 = Math.max(0, Math.floor((Math.min(ax, bx) - d - x0) / CELL)), ix1 = Math.min(nx - 1, Math.ceil((Math.max(ax, bx) + d - x0) / CELL));
  const iz0 = Math.max(0, Math.floor((Math.min(az, bz) - d - z0) / CELL)), iz1 = Math.min(nz - 1, Math.ceil((Math.max(az, bz) + d - z0) / CELL));
  const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz || 1e-12, d2 = d * d;
  for (let iz = iz0; iz <= iz1; iz++)
    for (let ix = ix0; ix <= ix1; ix++) {
      const px = x0 + (ix + 0.5) * CELL, pz = z0 + (iz + 0.5) * CELL;
      const t = Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / L2));
      const ex = ax + t * dx - px, ez = az + t * dz - pz;
      if (ex * ex + ez * ez < d2) mask[iz * nx + ix] = value;
    }
}

/// Marching squares on a 0/1 mask (samples at cell centres): the boundary
/// loops, inside on the left (outer rings anticlockwise).
function traceLoops(mask, nx, nz, x0, z0) {
  const at = (ix, iz) => (ix >= 0 && iz >= 0 && ix < nx && iz < nz ? mask[iz * nx + ix] : 0);
  // edge midpoints keyed on a doubled lattice: the point between sample
  // (ix, iz) and its neighbour
  const next = new Map();
  const K = (u, v) => u * 1000003 + v;
  const add = (u0, v0, u1, v1) => next.set(K(u0, v0), [u1, v1, u0, v0]);
  for (let iz = -1; iz < nz; iz++)
    for (let ix = -1; ix < nx; ix++) {
      // square with corners (ix,iz) a, (ix+1,iz) b, (ix+1,iz+1) c, (ix,iz+1) d
      const a = at(ix, iz), b = at(ix + 1, iz), c = at(ix + 1, iz + 1), d = at(ix, iz + 1);
      const code = a | (b << 1) | (c << 2) | (d << 3);
      if (code === 0 || code === 15) continue;
      // midpoints on the doubled lattice: bottom (2ix+1, 2iz), right (2ix+2, 2iz+1), top (2ix+1, 2iz+2), left (2ix, 2iz+1)
      const Bm = [2 * ix + 1, 2 * iz], Rm = [2 * ix + 2, 2 * iz + 1], Tm = [2 * ix + 1, 2 * iz + 2], Lm = [2 * ix, 2 * iz + 1];
      const seg = (p, q) => add(p[0], p[1], q[0], q[1]);
      switch (code) {
        case 1: seg(Lm, Bm); break;          // a
        case 2: seg(Bm, Rm); break;          // b
        case 3: seg(Lm, Rm); break;          // a b
        case 4: seg(Rm, Tm); break;          // c
        case 5: seg(Lm, Tm); seg(Rm, Bm); break; // a c (saddle: joined through the centre)
        case 6: seg(Bm, Tm); break;          // b c
        case 7: seg(Lm, Tm); break;          // a b c
        case 8: seg(Tm, Lm); break;          // d
        case 9: seg(Tm, Bm); break;          // a d
        case 10: seg(Bm, Rm); seg(Tm, Lm); break; // b d (saddle: separate)
        case 11: seg(Tm, Rm); break;         // a b d
        case 12: seg(Rm, Lm); break;         // c d
        case 13: seg(Rm, Bm); break;         // a c d
        case 14: seg(Bm, Lm); break;         // b c d
      }
    }
  const loops = [];
  const used = new Set();
  for (const [k, s] of next) {
    if (used.has(k)) continue;
    const loop = [];
    let cur = k, guard = 0;
    while (!used.has(cur) && guard++ < 4e6) {
      used.add(cur);
      const sg = next.get(cur);
      if (!sg) break;
      loop.push([x0 + (sg[2] / 2 + 0.5) * CELL, z0 + (sg[3] / 2 + 0.5) * CELL]);
      cur = K(sg[0], sg[1]);
    }
    // the table keeps the inside on the RIGHT: turn it to the left (outer anticlockwise)
    if (loop.length >= 3) loops.push(loop.reverse());
  }
  return loops;
}

/// The footprint's oriented box as the game draws a gabled house
/// (CityMap.OrientedBox: the least-area box on one of its first 12 edges).
function obbOf(pts) {
  let best = null, bestA = Infinity;
  const n = pts.length;
  for (let i = 0; i < Math.min(n, 12); i++) {
    let dx = pts[(i + 1) % n][0] - pts[i][0], dz = pts[(i + 1) % n][1] - pts[i][1];
    const m = Math.hypot(dx, dz);
    if (m < 0.2) continue;
    dx /= m; dz /= m;
    const vx = -dz, vz = dx;
    let u0 = Infinity, u1 = -Infinity, v0 = Infinity, v1 = -Infinity;
    for (const p of pts) { const a = p[0] * dx + p[1] * dz, b = p[0] * vx + p[1] * vz; u0 = Math.min(u0, a); u1 = Math.max(u1, a); v0 = Math.min(v0, b); v1 = Math.max(v1, b); }
    const A = (u1 - u0) * (v1 - v0);
    if (A < bestA) { bestA = A; best = [[u0, v0], [u1, v0], [u1, v1], [u0, v1]].map(([a, b]) => [dx * a + vx * b, dz * a + vz * b]); }
  }
  return best || pts;
}

/// Point in polygon (even-odd).
function inPoly(ring, x, z) {
  let c = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [xi, zi] = ring[i], [xj, zj] = ring[j];
    if ((zi > z) !== (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) c = !c;
  }
  return c;
}

/// The cells (centres) of a (u, v) rectangle - origin o, axes u and v
/// (unit), u in [u0, u1], v in [v0, v1] - each passed to fn(index);
/// false from fn stops the walk (and the result is false).
function rectCells(nx, nz, x0, z0, o, u, v, u0, u1, v0, v1, fn) {
  const cs = [[u0, v0], [u1, v0], [u1, v1], [u0, v1]].map(([a, b]) => [o[0] + u[0] * a + v[0] * b, o[1] + u[1] * a + v[1] * b]);
  let bx0 = 1e18, bx1 = -1e18, bz0 = 1e18, bz1 = -1e18;
  for (const [x, z] of cs) { bx0 = Math.min(bx0, x); bx1 = Math.max(bx1, x); bz0 = Math.min(bz0, z); bz1 = Math.max(bz1, z); }
  const ix0 = Math.floor((bx0 - x0) / CELL), ix1 = Math.floor((bx1 - x0) / CELL), iz0 = Math.floor((bz0 - z0) / CELL), iz1 = Math.floor((bz1 - z0) / CELL);
  for (let iz = iz0; iz <= iz1; iz++)
    for (let ix = ix0; ix <= ix1; ix++) {
      const px = x0 + (ix + 0.5) * CELL - o[0], pz = z0 + (iz + 0.5) * CELL - o[1];
      const a = px * u[0] + pz * u[1], b = px * v[0] + pz * v[1];
      if (a < u0 || a > u1 || b < v0 || b > v1) continue;
      if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) { if (fn(-1) === false) return false; continue; }
      if (fn(iz * nx + ix) === false) return false;
    }
  return true;
}

// ---- leftover item 4 (2026-10-03): OSM's parking aisles, islands, holes
// AISLES: service=parking_aisle ways on the surface (not tunnel, covered,
// indoor, bridge, another layer or level) - two-way 7.32 m (24 ft), one-way
// 4.9 m (16 ft). An aisle any of whose length lies in a lot polygon is that
// lot's: its corridor is paved with the lot out to AISLE_REACH past the
// lot's box, and the lot's stall rows stand along its aisles - both sides,
// a run's stalls square to the aisle from its edge, kept only wholly inside
// the stall mask and clear of every aisle corridor and of the rows already
// laid (longest aisle segment first). A lot with no aisle keeps L8's grid.
// An aisle no lot owns is paved on its own (an aisle-only lot, no stalls).
// HOLES: the buildings inside a lot (grown 0.3 m: a lot never runs under a
// wall) and OSM's inner rings come out of it; an inner ring that is not a
// building and is at most ISLAND_MAX is an ISLAND (raised curbed grass at
// run time), anything else plain ground. ISLANDS at row ends: a run of 4+
// stalls whose next slot along the row leaves the lot (its edge, the road's
// band, a building) gives its end stall to an island (set back 0.6 m from
// the aisle, 0.15 m off the last separator), so at least three stalls stay.
// Lots are laid in order and each later one is cut clear of the earlier
// ones' rings (OSM's nested and overlapping lots, shared aisles).
// ENTRANCES: where a lot's aisle ends within reach of a street (tertiary or
// below, 15 m from its ends), else L8's nearest-point rule.
const AISLE_HW2 = 3.66, AISLE_HW1 = 2.45, AISLE_REACH = 30, ISLAND_MAX = 400;

export function buildLots({ raw, edges, buildings, nodeIndex, nodeCount, toX, toZ, laneM }) {
  const nodeDeg = new Int32Array(nodeCount);
  for (const e of edges) { nodeDeg[e.a]++; nodeDeg[e.b]++; }
  const stats = { osm: 0, skippedKind: 0, skippedOpen: 0, kept: 0, rings: 0, dropSmall: 0, dropMostlyRoad: 0, stalls: 0, lines: 0, runs: 0, points: 0,
                  entrances: 0, noEntrance: 0, areaM2: 0, turnTagged: 0, turnMatched: 0, turnNotEnd: 0,
                  aislesOsm: 0, aislesNotSurface: 0, aislesSurface: 0, aislesOwned: 0, aisleOnlyLots: 0, aisleOnlyRings: 0, aisleOnlyM2: 0,
                  aisleRuns: 0, fillRuns: 0, gridRuns: 0, lotsWithAisles: 0, islandsRowEnd: 0, islandsOsm: 0, holesBuilding: 0, holesOther: 0,
                  aisleEntrances: 0, aislesDrawn: 0, aislesKm: 0, aislesDrawnKm: 0, aislesOnRoadOnly: 0, rasterTooBig: 0 };
  const lots = [], entrances = [], turns = [];
  // ---- the road segments, gridded (64 m)
  const G = 64, grid = new Map();
  const gk = (cx, cz) => cx * 100003 + cz;
  const hwOf = e => 0.5 * (e.lanes * laneM + e.way.shl + e.way.shr);
  for (let ei = 0; ei < edges.length; ei++) {
    const e = edges[ei];
    for (let i = 1; i < e.pts.length; i++) {
      const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
      for (let cx = Math.floor(Math.min(ax, bx) / G); cx <= Math.floor(Math.max(ax, bx) / G); cx++)
        for (let cz = Math.floor(Math.min(az, bz) / G); cz <= Math.floor(Math.max(az, bz) / G); cz++) {
          const k = gk(cx, cz); let l = grid.get(k); if (!l) grid.set(k, l = []); l.push(ei * 4096 + i);
        }
    }
  }
  const segsNear = (x0, z0, x1, z1) => {
    const out = new Set();
    for (let cx = Math.floor(x0 / G); cx <= Math.floor(x1 / G); cx++)
      for (let cz = Math.floor(z0 / G); cz <= Math.floor(z1 / G); cz++) for (const p of grid.get(gk(cx, cz)) || []) out.add(p);
    return [...out].sort((a, b) => a - b);
  };
  // ---- the buildings, gridded
  const bgrid = new Map();
  buildings.forEach((b, bi) => {
    let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
    for (const p of b.pts) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
    b._box = [x0, z0, x1, z1];
    for (let cx = Math.floor(x0 / G); cx <= Math.floor(x1 / G); cx++)
      for (let cz = Math.floor(z0 / G); cz <= Math.floor(z1 / G); cz++) { const k = gk(cx, cz); let l = bgrid.get(k); if (!l) bgrid.set(k, l = []); l.push(bi); }
  });
  const bldNear = (x0, z0, x1, z1) => {
    const out = new Set();
    for (let cx = Math.floor(x0 / G); cx <= Math.floor(x1 / G); cx++)
      for (let cz = Math.floor(z0 / G); cz <= Math.floor(z1 / G); cz++) for (const i of bgrid.get(gk(cx, cz)) || []) out.add(i);
    return [...out].sort((a, b) => a - b);
  };
  // ---- the aisles, gridded
  const aisles = [];
  for (const el of raw) {
    if (el.type !== 'way' || !el.tags || el.tags.service !== 'parking_aisle' || !el.geometry) continue;
    stats.aislesOsm++;
    const t = el.tags;
    const off = v => v !== undefined && v !== 'no' && v !== '0';
    if (off(t.tunnel) || t.covered === 'yes' || off(t.layer) || off(t.level) || t.indoor === 'yes' || off(t.bridge) || t.location === 'underground') { stats.aislesNotSurface++; continue; }
    const pts = [];
    for (const p of el.geometry) {
      const q = [toX(p.lon), toZ(p.lat)];
      if (!pts.length || Math.hypot(q[0] - pts[pts.length - 1][0], q[1] - pts[pts.length - 1][1]) > 0.05) pts.push(q);
    }
    if (pts.length < 2) { stats.aislesNotSurface++; continue; }
    const ow = t.oneway === 'yes' || t.oneway === '1' || t.oneway === '-1' || t.oneway === 'true';
    let len = 0, x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
    for (let i = 0; i < pts.length; i++) {
      if (i) len += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]);
      x0 = Math.min(x0, pts[i][0]); x1 = Math.max(x1, pts[i][0]); z0 = Math.min(z0, pts[i][1]); z1 = Math.max(z1, pts[i][1]);
    }
    aisles.push({ id: el.id, pts, hw: ow ? AISLE_HW1 : AISLE_HW2, len, box: [x0, z0, x1, z1], owned: false });
    stats.aislesSurface++;
  }
  aisles.sort((a, b) => a.id - b.id);
  const agrid = new Map();
  aisles.forEach((a, ai) => {
    for (let cx = Math.floor(a.box[0] / G); cx <= Math.floor(a.box[2] / G); cx++)
      for (let cz = Math.floor(a.box[1] / G); cz <= Math.floor(a.box[3] / G); cz++) { const k = gk(cx, cz); let l = agrid.get(k); if (!l) agrid.set(k, l = []); l.push(ai); }
  });
  const aislesNear = (x0, z0, x1, z1) => {
    const out = new Set();
    for (let cx = Math.floor(x0 / G); cx <= Math.floor(x1 / G); cx++)
      for (let cz = Math.floor(z0 / G); cz <= Math.floor(z1 / G); cz++) for (const i of agrid.get(gk(cx, cz)) || []) out.add(i);
    return [...out].sort((a, b) => a - b).filter(i => { const b = aisles[i].box; return b[2] >= x0 && b[0] <= x1 && b[3] >= z0 && b[1] <= z1; });
  };
  // ---- the lots laid so far (each later one is cut clear of them)
  const claimed = [], cgrid = new Map();
  const claim = (ring, holes) => {
    let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
    for (const p of ring) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
    const ci = claimed.length;
    claimed.push({ ring, holes, box: [x0, z0, x1, z1] });
    for (let cx = Math.floor(x0 / G); cx <= Math.floor(x1 / G); cx++)
      for (let cz = Math.floor(z0 / G); cz <= Math.floor(z1 / G); cz++) { const k = gk(cx, cz); let l = cgrid.get(k); if (!l) cgrid.set(k, l = []); l.push(ci); }
  };
  const claimedNear = (x0, z0, x1, z1) => {
    const out = new Set();
    for (let cx = Math.floor(x0 / G); cx <= Math.floor(x1 / G); cx++)
      for (let cz = Math.floor(z0 / G); cz <= Math.floor(z1 / G); cz++) for (const i of cgrid.get(gk(cx, cz)) || []) out.add(i);
    return [...out].sort((a, b) => a - b).filter(i => { const b = claimed[i].box; return b[2] >= x0 && b[0] <= x1 && b[3] >= z0 && b[1] <= z1; });
  };
  const pavedAt = (x, z) => {
    for (const ci of claimedNear(x, z, x, z)) {
      const c = claimed[ci];
      if (!inPoly(c.ring, x, z)) continue;
      if (c.holes.some(h => inPoly(h.ring, x, z))) continue;
      return true;
    }
    return false;
  };
  // the street nearest a point within reach, for an entrance: tertiary or
  // below, not a link, bridge or tunnel, 15 m from the edge's ends
  const streetAt = (px, pz, reach, towards) => {
    let best = null;
    for (const packed of segsNear(px - reach - 20, pz - reach - 20, px + reach + 20, pz + reach + 20)) {
      const e = edges[Math.floor(packed / 4096)], i = packed % 4096;
      if (e.way.rank > 1 || e.way.link || e.way.bridge || e.way.tunnel) continue;
      const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
      const dx = bx - ax, dz = bz - az, L = Math.hypot(dx, dz);
      if (L < 1e-3) continue;
      const t = Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / (L * L)));
      const qx = ax + t * dx, qz = az + t * dz, d = Math.hypot(px - qx, pz - qz);
      if (d > reach + hwOf(e)) continue;
      let sBase = 0; for (let k = 1; k < i; k++) sBase += Math.hypot(e.pts[k][0] - e.pts[k - 1][0], e.pts[k][1] - e.pts[k - 1][1]);
      const s = sBase + t * L;
      if (s < 15 || s > e.len - 15) continue;
      const ei = Math.floor(packed / 4096);
      const key = d * 1e6 + ei * 10 + t;
      if (!best || key < best.key) {
        const side = (dx * (towards[1] - az) - dz * (towards[0] - ax)) > 0 ? 1 : -1;   // + left of a->b
        best = { key, e: ei, s, side };
      }
    }
    return best;
  };

  // ---- the OSM lots (outer rings, with the inner rings inside each)
  const polys = [];
  const bad = new Set(['multi-storey', 'underground', 'rooftop', 'street_side', 'lane', 'layby', 'carports', 'garage_boxes']);
  const els = raw.filter(e => e.tags && e.tags.amenity === 'parking').sort((a, b) => (a.type < b.type ? -1 : a.type > b.type ? 1 : a.id - b.id));
  const closedRing = g => {
    if (g.length < 4 || g[0].lat !== g[g.length - 1].lat || g[0].lon !== g[g.length - 1].lon) return null;
    const ring = g.slice(0, -1).map(p => [toX(p.lon), toZ(p.lat)]);
    if (ringArea(ring) < 0) ring.reverse();
    return ring;
  };
  for (const el of els) {
    stats.osm++;
    if (bad.has(el.tags.parking) || el.tags.location === 'underground' || (el.tags.level && Number(el.tags.level) < 0)) { stats.skippedKind++; continue; }
    const outers = [], inners = [];
    if (el.type === 'way' && el.geometry) outers.push(el.geometry);
    else if (el.type === 'relation' && el.members)
      for (const m of el.members) {
        if (!m.geometry) continue;
        if (m.role === 'outer') outers.push(m.geometry);
        else if (m.role === 'inner') { const r = closedRing(m.geometry); if (r) inners.push(r); }
      }
    for (const g of outers) {
      const ring = closedRing(g);
      if (!ring) { stats.skippedOpen++; continue; }
      polys.push({ id: el.id, ring, inners: inners.filter(r => inPoly(ring, r[0][0], r[0][1])) });
    }
  }

  /// One lot: its mask (pavement), stall rows, islands, rings, holes and
  /// entrances. P = { id, ring | null, inners, own: [aisle indices] }.
  const layLot = (P, aisleOnly) => {
    const own = P.own;
    const A0 = P.ring ? ringArea(P.ring) : 0;
    let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
    const grow = p => { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); };
    if (P.ring) { for (const p of P.ring) grow(p); }
    else for (const ai of own) for (const p of aisles[ai].pts) grow(p);
    const pad = P.ring ? (own.length ? AISLE_REACH : 1) : AISLE_HW2 + 1;
    x0 -= pad; z0 -= pad; x1 += pad; z1 += pad;
    const nx = Math.ceil((x1 - x0) / CELL), nz = Math.ceil((z1 - z0) / CELL);
    if (nx * nz > 16e6) { stats.rasterTooBig++; return false; }
    const mask = new Uint8Array(nx * nz);
    if (P.ring) fillPoly(mask, nx, nz, x0, z0, P.ring, 1);
    for (const ai of own) {
      const a = aisles[ai];
      for (let i = 1; i < a.pts.length; i++) carveSeg(mask, nx, nz, x0, z0, a.pts[i - 1][0], a.pts[i - 1][1], a.pts[i][0], a.pts[i][1], a.hw, 1);
    }
    // OSM's inner rings
    let imask = null;
    if (P.inners && P.inners.length) {
      imask = new Uint8Array(nx * nz);
      for (const r of P.inners) { fillPoly(mask, nx, nz, x0, z0, r, 0); fillPoly(imask, nx, nz, x0, z0, r, 1); }
    }
    // the road bands
    for (const packed of segsNear(x0 - 20, z0 - 20, x1 + 20, z1 + 20)) {
      const e = edges[Math.floor(packed / 4096)], i = packed % 4096;
      const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
      carveSeg(mask, nx, nz, x0, z0, ax, az, bx, bz, hwOf(e) + 1.0);
    }
    // the buildings (grown 0.45 m: the traced ring's half cell and RDP stay
    // off the wall; a gabled house is drawn as its oriented box, so that
    // too), and the stall mask's (grown 0.6 m)
    const bmask = new Uint8Array(nx * nz);
    const near = bldNear(x0, z0, x1, z1).filter(bi => { const b = buildings[bi]; return !(b._box[2] < x0 || b._box[0] > x1 || b._box[3] < z0 || b._box[1] > z1); });
    const shapes = b => b.gable ? [b.pts, obbOf(b.pts)] : [b.pts];
    for (const bi of near) {
      for (const sh of shapes(buildings[bi])) {
        fillPoly(bmask, nx, nz, x0, z0, sh, 1);
        for (let k = 0; k < sh.length; k++) { const p = sh[k], q = sh[(k + 1) % sh.length]; carveSeg(bmask, nx, nz, x0, z0, p[0], p[1], q[0], q[1], 0.45, 1); }
      }
    }
    for (let i = 0; i < mask.length; i++) if (bmask[i]) mask[i] = 0;
    // the lots laid before this one (grown 0.4 m, so no two traced rings overlap)
    for (const ci of claimedNear(x0 - 1, z0 - 1, x1 + 1, z1 + 1)) {
      const r = claimed[ci].ring;
      fillPoly(mask, nx, nz, x0, z0, r, 0);
      for (let k = 0; k < r.length; k++) { const p = r[k], q = r[(k + 1) % r.length]; carveSeg(mask, nx, nz, x0, z0, p[0], p[1], q[0], q[1], 0.4); }
    }
    let left = 0; for (let i = 0; i < mask.length; i++) left += mask[i];
    if (P.ring && left * CELL * CELL < 0.3 * A0) { stats.dropMostlyRoad++; return false; }
    if (!P.ring && left * CELL * CELL < 20) { stats.dropSmall++; return false; }
    // the stall mask: the lot less every building grown 0.6 m, eroded 0.3 m
    const er = new Uint8Array(mask.length);
    if (!aisleOnly) {
      const sm = mask.slice();
      for (const bi of near) {
        const b = buildings[bi];
        for (let k = 0; k < b.pts.length; k++) { const p = b.pts[k], q = b.pts[(k + 1) % b.pts.length]; carveSeg(sm, nx, nz, x0, z0, p[0], p[1], q[0], q[1], 0.6); }
      }
      const R = Math.ceil(0.3 / CELL);
      for (let iz = 0; iz < nz; iz++)
        for (let ix = 0; ix < nx; ix++) {
          if (!sm[iz * nx + ix]) continue;
          let ok = 1;
          for (let dz = -R; dz <= R && ok; dz++) for (let dx = -R; dx <= R && ok; dx++) {
            const jx = ix + dx, jz = iz + dz;
            if (jx < 0 || jz < 0 || jx >= nx || jz >= nz || !sm[jz * nx + jx]) ok = 0;
          }
          er[iz * nx + ix] = ok;
        }
    }
    // ---- the stall rows: a run = { x, z (first separator's foot), n, ux, uz }
    const runs = [];
    const occ = new Uint8Array(mask.length);
    const slotFree = (o, u, v, ua, ub, va, vb) => rectCells(nx, nz, x0, z0, o, u, v, ua, ub, va, vb, i => i >= 0 && er[i] === 1 && occ[i] === 0);
    const slotLot = (o, u, v, ua, ub, va, vb) => rectCells(nx, nz, x0, z0, o, u, v, ua, ub, va, vb, i => i >= 0 && mask[i] === 1);
    const stallOcc = new Uint8Array(mask.length);
    const markSlot = (o, u, v, ua, ub, va, vb) => rectCells(nx, nz, x0, z0, o, u, v, ua, ub, va, vb, i => { if (i >= 0) { occ[i] = 1; stallOcc[i] = 1; } });
    // the aisle a stall faces: paved, and no stall stands in it
    const aisleOpen = (o, u, v, ua, ub, va, vb) => rectCells(nx, nz, x0, z0, o, u, v, ua, ub, va, vb, i => i >= 0 && mask[i] === 1 && stallOcc[i] === 0);
    const islands = [];
    // a run of slots along u from origin o at v in [vA, vA + STALL_D]: runs of 2+, row-end islands
    const layRow = (o, u, v, vA, slots, kind, face = null) => {
      let cur = [];
      const flush = () => {
        if (cur.length >= 2) {
          let t0 = cur[0], n = cur.length;
          // islands where the row ends against the lot's edge (the next slot leaves the lot)
          const endOut = !slotLot(o, u, v, t0 + n * STALL_W, t0 + (n + 1) * STALL_W, vA, vA + STALL_D);
          const startOut = !slotLot(o, u, v, t0 - STALL_W, t0, vA, vA + STALL_D);
          const isl = (ua, ub) => {
            const c = [[ua, vA + 0.6], [ub, vA + 0.6], [ub, vA + STALL_D], [ua, vA + STALL_D]].map(([a, b]) => [o[0] + u[0] * a + v[0] * b, o[1] + u[1] * a + v[1] * b]);
            islands.push(c); stats.islandsRowEnd++;
          };
          if (endOut && n >= 4) { isl(t0 + (n - 1) * STALL_W + 0.15, t0 + n * STALL_W); n--; }
          if (startOut && n >= 4) { isl(t0, t0 + STALL_W - 0.15); t0 += STALL_W; n--; }
          const fx = o[0] + u[0] * t0 + v[0] * vA, fz = o[1] + u[1] * t0 + v[1] * vA;
          const m = Math.min(255, n);
          runs.push({ x: fx, z: fz, n: m, ux: u[0], uz: u[1], mx: fx + u[0] * 0.5 * m * STALL_W + v[0] * 0.5 * STALL_D, mz: fz + u[1] * 0.5 * m * STALL_W + v[1] * 0.5 * STALL_D });
          if (kind === 1) stats.aisleRuns++; else if (kind === 2) stats.fillRuns++; else stats.gridRuns++;
        }
        cur = [];
      };
      for (const t of slots) {
        if (slotFree(o, u, v, t, t + STALL_W, vA, vA + STALL_D) && (!face || aisleOpen(o, u, v, t, t + STALL_W, face[0], face[1]))) {
          if (cur.length && Math.abs(cur[cur.length - 1] + STALL_W - t) > 1e-3) flush();
          markSlot(o, u, v, t, t + STALL_W, vA, vA + STALL_D);
          cur.push(t);
        } else flush();
      }
      flush();
    };
    let ux = 1, uz = 0;
    if (!aisleOnly && own.length) {
      // every aisle near is a corridor no stall stands in (0.5 m inside its
      // edge: a row stands on the simplified line, RDP 0.5 m, from the edge)
      for (const ai of aislesNear(x0, z0, x1, z1)) {
        const a = aisles[ai];
        for (let i = 1; i < a.pts.length; i++) carveSeg(occ, nx, nz, x0, z0, a.pts[i - 1][0], a.pts[i - 1][1], a.pts[i][0], a.pts[i][1], a.hw - 0.5, 1);
      }
      const segs = [];
      for (const ai of own) {
        const a = aisles[ai];
        const pl = rdp(a.pts, 0.5);
        for (let i = 1; i < pl.length; i++) {
          const L = Math.hypot(pl[i][0] - pl[i - 1][0], pl[i][1] - pl[i - 1][1]);
          if (L >= 2 * STALL_W) segs.push({ a: pl[i - 1], b: pl[i], L, hw: a.hw, id: a.id, i });
        }
      }
      segs.sort((p, q) => q.L - p.L || p.id - q.id || p.i - q.i);
      if (segs.length) { ux = (segs[0].b[0] - segs[0].a[0]) / segs[0].L; uz = (segs[0].b[1] - segs[0].a[1]) / segs[0].L; }
      for (const sg of segs) {
        for (const side of [1, -1]) {
          const s0 = side > 0 ? sg.a : sg.b, s1 = side > 0 ? sg.b : sg.a;
          const u = [(s1[0] - s0[0]) / sg.L, (s1[1] - s0[1]) / sg.L], v = [-u[1], u[0]];
          const slots = [];
          for (let t = 0; t + STALL_W <= sg.L + 1e-6; t += STALL_W) slots.push(t);
          layRow(s0, u, v, sg.hw, slots, 1);
        }
      }
      // the rest of the lot (OSM maps the main aisles, not every one): L8's
      // double-loaded module on the main aisle's own lines - its rows square
      // to it, in phase with its stalls - where each stall faces an aisle
      // that is paved and free of stalls
      if (segs.length && P.ring) {
        const m0 = segs[0];
        const u = [ux, uz], v = [-uz, ux], hwA = 0.5 * AISLE, period = 2 * STALL_D + AISLE;
        const ua = m0.a[0] * u[0] + m0.a[1] * u[1], va = m0.a[0] * v[0] + m0.a[1] * v[1];
        let u0 = 1e18, u1 = -1e18, v0 = 1e18, v1 = -1e18;
        for (const p of P.ring) { const a = p[0] * u[0] + p[1] * u[1], b = p[0] * v[0] + p[1] * v[1]; u0 = Math.min(u0, a); u1 = Math.max(u1, a); v0 = Math.min(v0, b); v1 = Math.max(v1, b); }
        const slots = [];
        for (let t = ua + Math.ceil((u0 + 0.3 - ua) / STALL_W) * STALL_W; t + STALL_W <= u1; t += STALL_W) slots.push(t);
        for (let k = Math.floor((v0 - va) / period) - 1; k <= Math.ceil((v1 - va) / period) + 1; k++) {
          const c = va + k * period;
          layRow([0, 0], u, v, c + hwA, slots, 2, [c - hwA, c + hwA]);
          layRow([0, 0], u, v, c - hwA - STALL_D, slots, 2, [c - hwA, c + hwA]);
        }
      }
      stats.lotsWithAisles++;
    } else if (!aisleOnly) {
      // L8's grid: double-loaded about a 7.32 m aisle, along the OSM ring's longest side
      let bestL = -1;
      for (let i = 0; i < P.ring.length; i++) {
        const p = P.ring[i], q = P.ring[(i + 1) % P.ring.length];
        const L = Math.hypot(q[0] - p[0], q[1] - p[1]);
        if (L > bestL) { bestL = L; ux = (q[0] - p[0]) / L; uz = (q[1] - p[1]) / L; }
      }
      const u = [ux, uz], v = [-uz, ux];
      let u0 = 1e18, u1 = -1e18, v0 = 1e18, v1 = -1e18;
      for (const p of P.ring) { const a = p[0] * ux + p[1] * uz, b = p[0] * v[0] + p[1] * v[1]; u0 = Math.min(u0, a); u1 = Math.max(u1, a); v0 = Math.min(v0, b); v1 = Math.max(v1, b); }
      const period = 2 * STALL_D + AISLE;
      const slots = [];
      for (let t = u0 + 0.3; t + STALL_W <= u1; t += STALL_W) slots.push(t);
      for (let vb = v0 + 0.3; vb + STALL_D <= v1; vb += period)
        for (const rowV of [vb, vb + STALL_D + AISLE]) {
          if (rowV + STALL_D > v1 + 1e-6) continue;
          layRow([0, 0], u, v, rowV, slots, 0);
        }
    }
    // ---- the rings and their holes
    const loops = traceLoops(mask, nx, nz, x0, z0);
    const minOuter = aisleOnly ? 20 : 40;
    const outers = [], holes = [];
    for (const l of loops) {
      const a = ringArea(l);
      if (a >= minOuter) outers.push(l);
      else if (a <= -0.5) holes.push(l);
    }
    if (!outers.length) { stats.dropSmall++; return false; }
    const rings = outers.map(l => ({ ring: rdpRing(l, 0.2), holes: [], runs: [] })).filter(r => r.ring.length >= 3 && ringArea(r.ring) >= minOuter);
    if (!rings.length) { stats.dropSmall++; return false; }
    for (const h0 of holes) {
      const hr = rdpRing(h0, 0.2);
      if (hr.length < 3) continue;
      if (ringArea(hr) < 0) hr.reverse();
      const area = ringArea(hr);
      // what the hole is: building cells, OSM inner-ring cells
      let n = 0, nb = 0, ni = 0, hx0 = 1e18, hx1 = -1e18, hz0 = 1e18, hz1 = -1e18;
      for (const p of hr) { hx0 = Math.min(hx0, p[0]); hx1 = Math.max(hx1, p[0]); hz0 = Math.min(hz0, p[1]); hz1 = Math.max(hz1, p[1]); }
      for (let iz = Math.max(0, Math.floor((hz0 - z0) / CELL)); iz <= Math.min(nz - 1, Math.ceil((hz1 - z0) / CELL)); iz++)
        for (let ix = Math.max(0, Math.floor((hx0 - x0) / CELL)); ix <= Math.min(nx - 1, Math.ceil((hx1 - x0) / CELL)); ix++) {
          if (!inPoly(hr, x0 + (ix + 0.5) * CELL, z0 + (iz + 0.5) * CELL)) continue;
          n++; if (bmask[iz * nx + ix]) nb++; if (imask && imask[iz * nx + ix]) ni++;
        }
      if (!n) continue;
      const building = nb > 0.3 * n;
      if (!building && area < 2) continue;              // a sliver: paved over
      const kind = !building && ni > 0.5 * n && area <= ISLAND_MAX ? 1 : 0;
      const host = rings.find(r => inPoly(r.ring, hr[0][0], hr[0][1]));
      if (!host) continue;
      host.holes.push({ kind, ring: hr });
      if (kind) stats.islandsOsm++; else if (building) stats.holesBuilding++; else stats.holesOther++;
    }
    for (const c of islands) {
      const mx = 0.25 * (c[0][0] + c[1][0] + c[2][0] + c[3][0]), mz = 0.25 * (c[0][1] + c[1][1] + c[2][1] + c[3][1]);
      const host = rings.find(r => inPoly(r.ring, mx, mz));
      if (host) host.holes.push({ kind: 1, ring: ringArea(c) < 0 ? c.slice().reverse() : c });
    }
    for (const r of runs) {
      const host = rings.find(h => inPoly(h.ring, r.mx, r.mz));
      if (host) host.runs.push(r);
    }
    // ---- the entrances: where an aisle meets a street, else the nearest street point
    const ents = [];
    for (const ai of own) {
      const a = aisles[ai];
      for (const [pi, qi] of [[0, 1], [a.pts.length - 1, a.pts.length - 2]]) {
        const p = a.pts[pi], q = a.pts[qi];
        const best = streetAt(p[0], p[1], 6, q);
        if (!best) continue;
        if (ents.some(t => t.edge === best.e && Math.abs(t.s - best.s) < 10)) continue;
        ents.push({ edge: best.e, s: best.s, side: best.side, half: a.hw > 3 ? 3.65 : 2.45 });
      }
    }
    for (const t of ents) { entrances.push(t); stats.entrances++; stats.aisleEntrances++; }
    for (const R of rings) {
      const ring = R.ring;
      let rx0 = 1e18, rx1 = -1e18, rz0 = 1e18, rz1 = -1e18;
      for (const p of ring) { rx0 = Math.min(rx0, p[0]); rx1 = Math.max(rx1, p[0]); rz0 = Math.min(rz0, p[1]); rz1 = Math.max(rz1, p[1]); }
      for (const l of R.runs) { stats.runs++; stats.stalls += l.n; stats.lines += l.n + 1; }
      const area = ringArea(ring);
      stats.areaM2 += area; if (aisleOnly) stats.aisleOnlyM2 += area;
      stats.points += ring.length;
      if (!ents.length && !aisleOnly) {
        // L8: the nearest street point within 25 m
        let best = null;
        for (const packed of segsNear(rx0 - 25, rz0 - 25, rx1 + 25, rz1 + 25)) {
          const e = edges[Math.floor(packed / 4096)], i = packed % 4096;
          if (e.way.rank > 1 || e.way.link || e.way.bridge || e.way.tunnel) continue;
          const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
          const dx = bx - ax, dz = bz - az, L = Math.hypot(dx, dz);
          if (L < 1e-3) continue;
          let sBase = 0; for (let k = 1; k < i; k++) sBase += Math.hypot(e.pts[k][0] - e.pts[k - 1][0], e.pts[k][1] - e.pts[k - 1][1]);
          for (const p of ring) {
            const t = Math.max(0, Math.min(1, ((p[0] - ax) * dx + (p[1] - az) * dz) / (L * L)));
            const qx = ax + t * dx, qz = az + t * dz, d = Math.hypot(p[0] - qx, p[1] - qz);
            const s = sBase + t * L;
            if (d > 25 || s < 15 || s > e.len - 15) continue;
            const ei = Math.floor(packed / 4096);
            const key = d * 1e6 + ei * 10 + t;
            if (!best || key < best.key) {
              const side = (dx * (p[1] - az) - dz * (p[0] - ax)) > 0 ? 1 : -1;   // + left of a->b
              best = { key, e: ei, s, side, half: e.way.oneway ? 2.15 : 3.65 };
            }
          }
        }
        if (best) { entrances.push({ edge: best.e, s: best.s, side: best.side, half: best.half }); stats.entrances++; } else stats.noEntrance++;
      }
      lots.push({ id: P.id, ring, runs: R.runs, ux, uz, holes: R.holes, aisleOnly });
      claim(ring, R.holes);
      stats.rings++; if (aisleOnly) stats.aisleOnlyRings++;
    }
    return true;
  };

  for (const P of polys) {
    const A0 = ringArea(P.ring);
    if (A0 < 40) { stats.dropSmall++; continue; }
    let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
    for (const p of P.ring) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
    // its aisles: any 1 m of their length inside the ring
    P.own = aislesNear(x0, z0, x1, z1).filter(ai => {
      const a = aisles[ai];
      for (let i = 1; i < a.pts.length; i++) {
        const [ax, az] = a.pts[i - 1], [bx, bz] = a.pts[i];
        const L = Math.hypot(bx - ax, bz - az), k = Math.max(1, Math.ceil(L));
        for (let j = 0; j <= k; j++) if (inPoly(P.ring, ax + (bx - ax) * j / k, az + (bz - az) * j / k)) return true;
      }
      return false;
    });
    if (layLot(P, false)) { stats.kept++; for (const ai of P.own) aisles[ai].owned = true; }
  }
  for (const a of aisles) if (a.owned) stats.aislesOwned++;
  // how much of an aisle off the roads' bands (the band + 0.3 m) lies on a
  // lot's pavement (1 m samples)
  const drawnOf = a => {
    let off = 0, on = 0;
    for (let i = 1; i < a.pts.length; i++) {
      const [ax, az] = a.pts[i - 1], [bx, bz] = a.pts[i];
      const L = Math.hypot(bx - ax, bz - az), k = Math.max(1, Math.round(L));
      for (let j = (i === 1 ? 0 : 1); j <= k; j++) {
        const x = ax + (bx - ax) * j / k, z = az + (bz - az) * j / k;
        let road = false;
        for (const packed of segsNear(x - 1, z - 1, x + 1, z + 1)) {
          const e = edges[Math.floor(packed / 4096)], si = packed % 4096;
          const [px, pz] = e.pts[si - 1], [qx, qz] = e.pts[si];
          const dx = qx - px, dz = qz - pz, L2 = dx * dx + dz * dz || 1e-12;
          const t = Math.max(0, Math.min(1, ((x - px) * dx + (z - pz) * dz) / L2));
          if (Math.hypot(px + t * dx - x, pz + t * dz - z) < hwOf(e) + 1.3) { road = true; break; }
        }
        if (road) continue;
        off++;
        if (pavedAt(x, z)) on++;
      }
    }
    return { off, on, drawn: off === 0 || on >= 0.8 * off };
  };
  // the aisles no lot owns, and the owned ones their lots did not pave
  // (an aisle running on past AISLE_REACH): paved on their own
  aisles.forEach((a, ai) => {
    if (a.owned && drawnOf(a).drawn) return;
    if (layLot({ id: a.id, ring: null, inners: [], own: [ai] }, true)) stats.aisleOnlyLots++;
  });
  // AISLES DRAWN: 80 % of an aisle's length off the roads' bands on a lot's pavement
  for (const a of aisles) {
    const d = drawnOf(a);
    stats.aislesKm += a.len / 1000;
    if (d.off === 0) stats.aislesOnRoadOnly++;
    if (d.drawn) { stats.aislesDrawn++; stats.aislesDrawnKm += a.len / 1000; }
  }
  stats.aislesKm = Math.round(stats.aislesKm * 10) / 10; stats.aislesDrawnKm = Math.round(stats.aislesDrawnKm * 10) / 10;
  stats.aisleOnlyM2 = Math.round(stats.aisleOnlyM2);

  // ---- the turning circles
  for (const el of raw) {
    if (el.type !== 'node' || !el.tags || !/^(turning_circle|turning_loop)$/.test(el.tags.highway || '')) continue;
    stats.turnTagged++;
    const n = nodeIndex.get(el.id);
    if (n === undefined || nodeDeg[n] !== 1) { stats.turnNotEnd++; continue; }
    let r = 12.2;
    const dia = parseFloat(el.tags.diameter);
    if (Number.isFinite(dia) && dia >= 10 && dia <= 60) r = dia / 2;
    turns.push({ node: n, kind: el.tags.highway === 'turning_loop' ? 1 : 0, r });
    stats.turnMatched++;
  }
  turns.sort((a, b) => a.node - b.node);
  return { lots, entrances, turns, stats };
}
