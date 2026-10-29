// TWIN FOOTPRINTS (west-trade-flicker, 2026-10-29). The owner's video down
// West Trade Street: a long tan building "flickers probably 30 times while
// driving by". OSM draws some buildings twice - a building way AND a
// multipolygon relation whose outer ring is that same way, or a second way
// over the same nodes - and the exporter took both (addPoly per way and per
// relation outer). Two footprints over one outline are every wall twice, the
// same face at the same place: they fight in depth. While every building
// wore one facade nobody saw it; since the building pass (2026-10-28) the two
// copies wear the floors of their own heights (24.8 m and 28.2 m: storeys of
// 3.54 m against 4.03 m), and the fight is bands that jump at every wall.
//
// A footprint is a TWIN - redundant, flagged hidden (nothing of it drawn: no
// walls, roof, far building or skyline copy) - when a wall of it lies on a
// same-facing wall of another non-part footprint (within 5 cm, over 0.5 m of
// length and 0.5 m of shared height) and that other one holds all of it:
//   same      the two outlines are one (each lies within the other): the one
//             whose top is lower is the twin (equal: the later record);
//   inside    it lies wholly within the other's plan and its top is no more
//             than TOP_SLACK_M above the other's: nothing of it shows, only
//             its walls on the other's walls (and a roof on its roof).
// The keeper stands as it was: the building's silhouette and look are the
// keeper's, which is what was drawn wherever the two did not tie.
// What is NOT a twin (reported, left drawn): a building that rises out of the
// other (inside but taller) and two that only overlap - their shared walls
// need cutting, not removing (Docs/CITY_FLICKER.md, "Left").
// Used by export_osm.mjs on its records before they are written, and by
// bld_twins.mjs on the shipped charlotte_bld.bytes - one rule, one result.

export const WALL_TOL_M = 0.05;      // two walls within this of one line are one face
export const MIN_OVERLAP_M = 0.5;    // ...over at least this much length
export const MIN_SHARED_H_M = 0.5;   // ...and this much height (both from the ground)
export const TOP_SLACK_M = 0.3;      // an inner building's top may stand this far above the keeper's

const CELL = 20;

function area2(p) { let a = 0; for (let i = 0; i < p.length; i++) { const q = p[i], r = p[(i + 1) % p.length]; a += q[0] * r[1] - r[0] * q[1]; } return a; }
function dseg(px, pz, a, b) {
  const dx = b[0] - a[0], dz = b[1] - a[1], L = dx * dx + dz * dz;
  let t = L ? ((px - a[0]) * dx + (pz - a[1]) * dz) / L : 0; t = Math.max(0, Math.min(1, t));
  return Math.hypot(px - a[0] - t * dx, pz - a[1] - t * dz);
}
function onRing(x, z, p) { for (let i = 0; i < p.length; i++) if (dseg(x, z, p[i], p[(i + 1) % p.length]) < WALL_TOL_M) return true; return false; }
function inRing(x, z, p) {
  let c = false;
  for (let i = 0, j = p.length - 1; i < p.length; j = i++) {
    const a = p[i], b = p[j];
    if ((a[1] > z) !== (b[1] > z) && x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]) c = !c;
  }
  return c;
}
/** Does ring q lie within ring p (every corner and every wall's middle in it or on it)? */
function within(q, p) {
  for (let i = 0; i < q.length; i++) {
    const a = q[i], b = q[(i + 1) % q.length];
    for (const t of [0, 0.5]) {
      const x = a[0] + (b[0] - a[0]) * t, z = a[1] + (b[1] - a[1]) * t;
      if (!onRing(x, z, p) && !inRing(x, z, p)) return false;
    }
  }
  return true;
}

/**
 * fps: [{ pts: [[x, z], ...], h, part, hidden }] (records or parsed footprints).
 * groundAt(x, z): the ground's height (the footprint stands on its lowest
 * corner, as CityMeshes does); null compares heights alone.
 * Returns { twins: [{ i, keeper, kind, margin }], left: [{ a, b, kind, m2 }], pairs }.
 */
export function findTwins(fps, groundAt) {
  const segs = [];
  const grid = new Map();
  const live = i => { const f = fps[i]; return f && !f.part && !f.hidden && f.pts && f.pts.length >= 3; };
  for (let i = 0; i < fps.length; i++) {
    if (!live(i)) continue;
    const f = fps[i], n = f.pts.length, ccw = area2(f.pts) >= 0;
    for (let k = 0; k < n; k++) {
      let a = f.pts[k], b = f.pts[(k + 1) % n];
      if (!ccw) [a, b] = [b, a];
      const L = Math.hypot(b[0] - a[0], b[1] - a[1]);
      if (L < 0.3) continue;
      const id = segs.length;
      segs.push({ i, a, b, L, dx: (b[0] - a[0]) / L, dz: (b[1] - a[1]) / L });
      const key = Math.floor((a[0] + b[0]) / 2 / CELL) + ',' + Math.floor((a[1] + b[1]) / 2 / CELL);
      let l = grid.get(key); if (!l) grid.set(key, l = []); l.push(id);
    }
  }
  // the pairs with a shared face: same direction (same facing), on one line, overlapping
  const pairs = new Map();
  for (const [key, list] of grid) {
    const [cx, cz] = key.split(',').map(Number);
    for (const id of list) {
      const s = segs[id];
      for (let ox = -1; ox <= 1; ox++) for (let oz = -1; oz <= 1; oz++) {
        const l2 = grid.get((cx + ox) + ',' + (cz + oz));
        if (!l2) continue;
        for (const jd of l2) {
          if (jd <= id) continue;
          const t = segs[jd];
          if (t.i === s.i || s.dx * t.dx + s.dz * t.dz < 0.999) continue;
          const pa = (t.a[0] - s.a[0]) * -s.dz + (t.a[1] - s.a[1]) * s.dx;
          const pb = (t.b[0] - s.a[0]) * -s.dz + (t.b[1] - s.a[1]) * s.dx;
          if (Math.abs(pa) > WALL_TOL_M || Math.abs(pb) > WALL_TOL_M) continue;
          const u0 = (t.a[0] - s.a[0]) * s.dx + (t.a[1] - s.a[1]) * s.dz, u1 = (t.b[0] - s.a[0]) * s.dx + (t.b[1] - s.a[1]) * s.dz;
          const ov = Math.min(s.L, Math.max(u0, u1)) - Math.max(0, Math.min(u0, u1));
          if (ov < MIN_OVERLAP_M) continue;
          const vh = Math.min(fps[s.i].h, fps[t.i].h);
          if (vh < MIN_SHARED_H_M) continue;
          const lo = Math.min(s.i, t.i), hi = Math.max(s.i, t.i), k = lo * 1048576 + hi;
          const P = pairs.get(k) || { a: lo, b: hi, len: 0, m2: 0 };
          P.len += ov; P.m2 += ov * vh;
          pairs.set(k, P);
        }
      }
    }
  }
  const floor = new Map();
  const ground = i => {
    if (!groundAt) return 0;
    let g = floor.get(i);
    if (g === undefined) { g = Infinity; for (const p of fps[i].pts) g = Math.min(g, groundAt(p[0], p[1])); floor.set(i, g); }
    return g;
  };
  const top = i => ground(i) + fps[i].h;
  const twins = new Map(), left = [];
  const ordered = [...pairs.values()].sort((x, y) => x.a - y.a || x.b - y.b);
  for (const P of ordered) {
    const A = fps[P.a].pts, B = fps[P.b].pts;
    const bInA = within(B, A), aInB = within(A, B);
    let twin = -1, keeper = -1, kind = '', margin = 0;
    if (bInA && aInB) {
      kind = 'same';
      // the lower top is the twin; equal (to the centimetre): the later record
      const d = top(P.a) - top(P.b);
      if (Math.abs(d) < 0.01) { twin = P.b; keeper = P.a; } else if (d > 0) { twin = P.b; keeper = P.a; } else { twin = P.a; keeper = P.b; }
      margin = Math.abs(d);
    } else if (bInA || aInB) {
      const inner = bInA ? P.b : P.a, outer = bInA ? P.a : P.b;
      const over = top(inner) - top(outer);
      if (over <= TOP_SLACK_M) { kind = 'inside'; twin = inner; keeper = outer; margin = TOP_SLACK_M - over; }
      else { left.push({ a: outer, b: inner, kind: 'rises', m2: P.m2, over }); continue; }
    } else { left.push({ a: P.a, b: P.b, kind: 'overlap', m2: P.m2 }); continue; }
    if (!twins.has(twin)) twins.set(twin, { i: twin, keeper, kind, margin, m2: P.m2 });
    else twins.get(twin).m2 += P.m2;
  }
  return { twins: [...twins.values()].sort((x, y) => x.i - y.i), left, pairs: pairs.size };
}
