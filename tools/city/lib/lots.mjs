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

export function buildLots({ raw, edges, buildings, nodeIndex, nodeCount, toX, toZ, laneM }) {
  const nodeDeg = new Int32Array(nodeCount);
  for (const e of edges) { nodeDeg[e.a]++; nodeDeg[e.b]++; }
  const stats = { osm: 0, skippedKind: 0, skippedOpen: 0, kept: 0, rings: 0, dropSmall: 0, dropMostlyRoad: 0, stalls: 0, lines: 0, runs: 0, points: 0,
                  entrances: 0, noEntrance: 0, areaM2: 0, turnTagged: 0, turnMatched: 0, turnNotEnd: 0 };
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

  // ---- the OSM lots
  const polys = [];
  const bad = new Set(['multi-storey', 'underground', 'rooftop', 'street_side', 'lane', 'layby', 'carports', 'garage_boxes']);
  const els = raw.filter(e => e.tags && e.tags.amenity === 'parking').sort((a, b) => (a.type < b.type ? -1 : a.type > b.type ? 1 : a.id - b.id));
  for (const el of els) {
    stats.osm++;
    if (bad.has(el.tags.parking) || el.tags.location === 'underground' || (el.tags.level && Number(el.tags.level) < 0)) { stats.skippedKind++; continue; }
    const rings = [];
    if (el.type === 'way' && el.geometry) rings.push(el.geometry);
    else if (el.type === 'relation' && el.members)
      for (const m of el.members) if (m.role === 'outer' && m.geometry) rings.push(m.geometry);
    for (const g of rings) {
      if (g.length < 4 || g[0].lat !== g[g.length - 1].lat || g[0].lon !== g[g.length - 1].lon) { stats.skippedOpen++; continue; }
      const ring = g.slice(0, -1).map(p => [toX(p.lon), toZ(p.lat)]);
      if (ringArea(ring) < 0) ring.reverse();
      polys.push({ id: el.id, ring });
    }
  }

  for (const P of polys) {
    const A0 = ringArea(P.ring);
    if (A0 < 40) { stats.dropSmall++; continue; }
    let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
    for (const p of P.ring) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
    x0 -= 1; z0 -= 1; x1 += 1; z1 += 1;
    const nx = Math.ceil((x1 - x0) / CELL), nz = Math.ceil((z1 - z0) / CELL);
    if (nx * nz > 16e6) { stats.dropSmall++; continue; }
    const mask = new Uint8Array(nx * nz);
    fillPoly(mask, nx, nz, x0, z0, P.ring, 1);
    // the road bands
    for (const packed of segsNear(x0 - 20, z0 - 20, x1 + 20, z1 + 20)) {
      const e = edges[Math.floor(packed / 4096)], i = packed % 4096;
      const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
      carveSeg(mask, nx, nz, x0, z0, ax, az, bx, bz, hwOf(e) + 1.0);
    }
    let left = 0; for (let i = 0; i < mask.length; i++) left += mask[i];
    if (left * CELL * CELL < 0.3 * A0) { stats.dropMostlyRoad++; continue; }
    // the stall mask: the lot less every building grown 0.6 m, then eroded 0.3 m
    const sm = mask.slice();
    for (const bi of bldNear(x0, z0, x1, z1)) {
      const b = buildings[bi];
      if (b._box[2] < x0 || b._box[0] > x1 || b._box[3] < z0 || b._box[1] > z1) continue;
      fillPoly(sm, nx, nz, x0, z0, b.pts, 0);
      for (let k = 0; k < b.pts.length; k++) { const p = b.pts[k], q = b.pts[(k + 1) % b.pts.length]; carveSeg(sm, nx, nz, x0, z0, p[0], p[1], q[0], q[1], 0.6); }
    }
    const er = new Uint8Array(sm.length);
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
    const inStall = (x, z) => { const ix = Math.floor((x - x0) / CELL), iz = Math.floor((z - z0) / CELL); return ix >= 0 && iz >= 0 && ix < nx && iz < nz && er[iz * nx + ix] === 1; };
    // the rings
    const loops = traceLoops(mask, nx, nz, x0, z0).filter(l => ringArea(l) >= 40);
    if (!loops.length) { stats.dropSmall++; continue; }
    // the stall grid: along the OSM ring's longest side
    let bestL = -1, ux = 1, uz = 0;
    for (let i = 0; i < P.ring.length; i++) {
      const p = P.ring[i], q = P.ring[(i + 1) % P.ring.length];
      const L = Math.hypot(q[0] - p[0], q[1] - p[1]);
      if (L > bestL) { bestL = L; ux = (q[0] - p[0]) / L; uz = (q[1] - p[1]) / L; }
    }
    const vx = -uz, vz = ux;
    let u0 = 1e18, u1 = -1e18, v0 = 1e18, v1 = -1e18;
    for (const p of P.ring) { const u = p[0] * ux + p[1] * uz, v = p[0] * vx + p[1] * vz; u0 = Math.min(u0, u); u1 = Math.max(u1, u); v0 = Math.min(v0, v); v1 = Math.max(v1, v); }
    const W = (u, v) => [u * ux + v * vx, u * uz + v * vz];
    const stallIn = (ua, va, ub, vb) => {
      for (const [u, v] of [[ua, va], [ub, va], [ub, vb], [ua, vb], [(ua + ub) / 2, (va + vb) / 2], [(ua + ub) / 2, va], [(ua + ub) / 2, vb], [ua, (va + vb) / 2], [ub, (va + vb) / 2]]) {
        const [x, z] = W(u, v);
        if (!inStall(x, z)) return false;
      }
      return true;
    };
    const lines = [];
    const period = 2 * STALL_D + AISLE;
    for (let vb = v0 + 0.3; vb + STALL_D <= v1; vb += period) {
      for (const rowV of [vb, vb + STALL_D + AISLE]) {
        if (rowV + STALL_D > v1 + 1e-6) continue;
        // runs of kept stalls along u
        let run = [];
        const flush = () => {
          if (run.length >= 2) {
            // a run: its first separator's foot and the stall count (the
            // separators stand STALL_W apart along u, STALL_D long along v)
            const n = Math.min(255, run.length);
            const [fx, fz] = W(run[0], rowV), [mx, mz] = W(run[0] + 0.5 * n * STALL_W, rowV + 0.5 * STALL_D);
            lines.push({ x: fx, z: fz, n, mx, mz });
          }
          run = [];
        };
        for (let u = u0 + 0.3; u + STALL_W <= u1; u += STALL_W) {
          if (stallIn(u, rowV, u + STALL_W, rowV + STALL_D)) {
            if (run.length && Math.abs(run[run.length - 1] + STALL_W - u) > 1e-3) flush();
            run.push(u);
          } else flush();
        }
        flush();
      }
    }
    for (const loop0 of loops) {
      const ring = rdpRing(loop0, 0.2);
      if (ring.length < 3 || ringArea(ring) < 40) { stats.dropSmall++; continue; }
      let rx0 = 1e18, rx1 = -1e18, rz0 = 1e18, rz1 = -1e18;
      for (const p of ring) { rx0 = Math.min(rx0, p[0]); rx1 = Math.max(rx1, p[0]); rz0 = Math.min(rz0, p[1]); rz1 = Math.max(rz1, p[1]); }
      // this ring's stall lines: those whose middle lies in it
      const inRing = (x, z) => {
        let c = false;
        for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
          const [xi, zi] = ring[i], [xj, zj] = ring[j];
          if ((zi > z) !== (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) c = !c;
        }
        return c;
      };
      const mine = lines.filter(l => inRing(l.mx, l.mz));
      for (const l of mine) { stats.runs++; stats.stalls += l.n; stats.lines += l.n + 1; }
      stats.areaM2 += ringArea(ring);
      stats.points += ring.length;
      // the entrance: the nearest street point within 25 m
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
      lots.push({ id: P.id, ring, runs: mine, ux, uz });
      stats.rings++;
    }
    stats.kept++;
  }

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
