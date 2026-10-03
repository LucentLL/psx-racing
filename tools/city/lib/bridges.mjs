// bridges.mjs - the two OSM facts the roads pass's twin-deck work reads
// (plan B1, 2026-10-02; Docs/CHARLOTTE.md "B1"):
//
//   OUTLINES  man_made=bridge areas (cache/bridges_mm.json, fetched by
//             fetch/fetch_ways.mjs). One outline round two carriageways is
//             ONE structure: West 5th Street's e1253/e5445 sit in w984482059
//             and are one bridge with a median, not two. Each bridge=yes edge
//             gets the outline holding >= 60% of its 4 m samples (its
//             structId; 0 = none), section BRST.
//   CULVERTS  waterway ways tagged tunnel=culvert / culvert=yes
//             (cache/layers/culverts.json, fetch/fetch_layers.mjs). A water
//             span (section SPAN: a creek line under a road) whose middle is
//             within 15 m of one is a creek OSM says is PIPED under the road
//             (owner Q8, 2026-10-02: follow OSM, build culverts). B1 kept them
//             decks and out of every twin-deck union (critic C2/D3); B2 builds
//             them as culverts by its own geometric test (lib/culverts.mjs,
//             section CULV) and writes BRST's culvert table empty.
//             culvertSpans() stays for deckpairs.mjs measuring a file without
//             BRST.
//
// Used by export_osm.mjs (writes BRST) and deckpairs.mjs (reads BRST, or
// computes it here when it measures an older file). Pure geometry, no I/O
// beyond reading the two cache files.

import { readFileSync, existsSync } from 'node:fs';

/// An outline's id as BRST stores it (u32): the OSM way id, or a
/// relation's id with the top bit set (no way id in the snapshot reaches 2^31).
export const RELATION_BIT = 0x80000000;
export const outlineName = id => (id & RELATION_BIT) ? 'r' + (id & ~RELATION_BIT >>> 0) : 'w' + id;

/// A share of an edge's samples one outline must hold to give it a structId.
export const STRUCT_SHARE = 0.6;
/// Samples along a bridge edge, metres apart.
export const STRUCT_STEP = 4;
/// A water span this close (its middle to a culvert line) is a culvert's.
export const CULVERT_NEAR_M = 15;

const box = pts => { let x0 = 1e18, z0 = 1e18, x1 = -1e18, z1 = -1e18; for (const [x, z] of pts) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (z < z0) z0 = z; if (z > z1) z1 = z; } return [x0, z0, x1, z1]; };

/// Join open member lines into closed rings (a multipolygon's outer ways).
function joinRings(lines) {
  const rings = [];
  const left = lines.map(l => l.slice());
  while (left.length) {
    let ring = left.shift();
    for (let guard = 0; guard < 1000; guard++) {
      const a = ring[0], b = ring[ring.length - 1];
      if (a.lat === b.lat && a.lon === b.lon) break;
      const k = left.findIndex(l => (l[0].lat === b.lat && l[0].lon === b.lon) || (l[l.length - 1].lat === b.lat && l[l.length - 1].lon === b.lon));
      if (k < 0) break;
      let l = left.splice(k, 1)[0];
      if (!(l[0].lat === b.lat && l[0].lon === b.lon)) l = l.reverse();
      ring = ring.concat(l.slice(1));
    }
    rings.push(ring);
  }
  return rings;
}

/// The outlines: [{ id, osm, pts: [[x, z]], box }], in file order (ways,
/// then each relation's outer rings). toX/toZ: the exporter's frame.
export function loadOutlines(path, toX, toZ) {
  if (!existsSync(path)) return null;
  const els = JSON.parse(readFileSync(path, 'utf8')).elements || [];
  const out = [];
  for (const el of els) {
    if (el.type === 'way' && el.geometry && el.geometry.length >= 4) {
      const pts = el.geometry.map(g => [toX(g.lon), toZ(g.lat)]);
      out.push({ id: el.id >>> 0, osm: 'w' + el.id, pts, box: box(pts) });
    } else if (el.type === 'relation' && el.members) {
      const outers = el.members.filter(m => m.role === 'outer' && m.geometry && m.geometry.length >= 2).map(m => m.geometry);
      for (const ring of joinRings(outers)) {
        if (ring.length < 4) continue;
        const pts = ring.map(g => [toX(g.lon), toZ(g.lat)]);
        out.push({ id: (RELATION_BIT | el.id) >>> 0, osm: 'r' + el.id, pts, box: box(pts) });
      }
    }
  }
  return out;
}

function inside(pts, x, z) {
  let c = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const [xi, zi] = pts[i], [xj, zj] = pts[j];
    if ((zi > z) !== (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) c = !c;
  }
  return c;
}

/// A 64 m grid over the outlines' boxes.
export function outlineIndex(outlines) {
  const C = 64, grid = new Map(), key = (i, j) => i * 100003 + j;
  outlines.forEach((o, k) => {
    for (let i = Math.floor(o.box[0] / C); i <= Math.floor(o.box[2] / C); i++)
      for (let j = Math.floor(o.box[1] / C); j <= Math.floor(o.box[3] / C); j++) {
        const kk = key(i, j); let l = grid.get(kk); if (!l) grid.set(kk, l = []); l.push(k);
      }
  });
  return { outlines, at(x, z) {
    const l = grid.get(key(Math.floor(x / C), Math.floor(z / C))) || [];
    const hits = [];
    for (const k of l) { const o = outlines[k]; if (x >= o.box[0] && x <= o.box[2] && z >= o.box[1] && z <= o.box[3] && inside(o.pts, x, z)) hits.push(o.id); }
    return hits;
  } };
}

/// Points every STRUCT_STEP m along a polyline from 1 m to length - 1 m
/// (its middle when shorter than 2 m).
function samples(pts) {
  const acc = [0];
  for (let k = 1; k < pts.length; k++) acc.push(acc[k - 1] + Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]));
  const L = acc[acc.length - 1];
  const at = s => {
    let k = 1; while (k < pts.length - 1 && acc[k] < s) k++;
    const t = (s - acc[k - 1]) / Math.max(1e-9, acc[k] - acc[k - 1]);
    return [pts[k - 1][0] + (pts[k][0] - pts[k - 1][0]) * t, pts[k - 1][1] + (pts[k][1] - pts[k - 1][1]) * t];
  };
  const out = [];
  if (L < 2) out.push(at(L / 2));
  else for (let s = 1; s < L - 1 + 1e-9; s += STRUCT_STEP) out.push(at(s));
  return out;
}

/// An edge's structId: the outline holding >= STRUCT_SHARE of its samples
/// (the most of them when two qualify: never, at 60%), else 0. Also the
/// share, for the report.
export function structIdOf(pts, index) {
  const S = samples(pts);
  const n = new Map();
  for (const [x, z] of S) for (const id of index.at(x, z)) n.set(id, (n.get(id) || 0) + 1);
  let best = 0, bn = 0;
  for (const [id, c] of n) if (c > bn || (c === bn && id < best)) { best = id; bn = c; }
  const share = S.length ? bn / S.length : 0;
  return { id: share >= STRUCT_SHARE ? best : 0, share, touched: n.size };
}

/// The culvert lines (tunnel=culvert or culvert=yes waterways) as segments
/// on a 50 m grid, with each one's way id and name.
export function loadCulverts(path, toX, toZ) {
  if (!existsSync(path)) return null;
  const j = JSON.parse(readFileSync(path, 'utf8'));
  const segs = [];
  for (const w of j.elements || j) {
    if (w.type !== 'way' || !w.geometry || !w.tags) continue;
    if (!(w.tags.tunnel === 'culvert' || w.tags.culvert === 'yes')) continue;
    for (let k = 1; k < w.geometry.length; k++)
      segs.push([toX(w.geometry[k - 1].lon), toZ(w.geometry[k - 1].lat), toX(w.geometry[k].lon), toZ(w.geometry[k].lat), w.id, w.tags.name || '']);
  }
  const C = 50, grid = new Map(), key = (a, b) => a * 100003 + b;
  for (const s of segs)
    for (let cx = Math.floor(Math.min(s[0], s[2]) / C); cx <= Math.floor(Math.max(s[0], s[2]) / C); cx++)
      for (let cz = Math.floor(Math.min(s[1], s[3]) / C); cz <= Math.floor(Math.max(s[1], s[3]) / C); cz++) {
        const k = key(cx, cz); let l = grid.get(k); if (!l) grid.set(k, l = []); l.push(s);
      }
  const segD = (px, pz, ax, az, bx, bz) => { const dx = bx - ax, dz = bz - az, L = dx * dx + dz * dz; let t = L > 0 ? ((px - ax) * dx + (pz - az) * dz) / L : 0; t = Math.max(0, Math.min(1, t)); return Math.hypot(px - (ax + t * dx), pz - (az + t * dz)); };
  return {
    count: segs.length,
    /// The nearest culvert line to (x, z) within the 3 x 3 cells round it:
    /// { d, way, name } or null.
    nearest(x, z) {
      let best = null;
      const cx0 = Math.floor(x / C), cz0 = Math.floor(z / C);
      for (let cx = cx0 - 1; cx <= cx0 + 1; cx++) for (let cz = cz0 - 1; cz <= cz0 + 1; cz++)
        for (const s of grid.get(key(cx, cz)) || []) {
          const d = segD(x, z, s[0], s[1], s[2], s[3]);
          if (!best || d < best.d) best = { d, way: s[4], name: s[5] };
        }
      return best;
    },
  };
}

/// Point at arc s of a polyline [[x, z]] (clamped).
export function pointAlong(pts, s) {
  let acc = 0;
  for (let k = 1; k < pts.length; k++) {
    const L = Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]);
    if (acc + L >= s || k === pts.length - 1) {
      const t = L > 0 ? Math.max(0, Math.min(1, (s - acc) / L)) : 0;
      return [pts[k - 1][0] + (pts[k][0] - pts[k - 1][0]) * t, pts[k - 1][1] + (pts[k][1] - pts[k - 1][1]) * t];
    }
    acc += L;
  }
  return pts[0];
}

/// The water spans a culvert claims: for each span { e, s0, s1 } (edge
/// index into `edgePts`), the nearest culvert line to the span's middle when
/// within CULVERT_NEAR_M. Returns [{ span, way, d, name }].
export function culvertSpans(spans, edgePts, culverts) {
  const out = [];
  if (!culverts) return out;
  spans.forEach((w, i) => {
    const [x, z] = pointAlong(edgePts(w.e), (w.s0 + w.s1) / 2);
    const n = culverts.nearest(x, z);
    if (n && n.d <= CULVERT_NEAR_M) out.push({ span: i, way: n.way, d: n.d, name: n.name });
  });
  return out;
}
