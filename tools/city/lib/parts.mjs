// Uptown B2 (2026-10-04): OSM building:part -> the tiers a building is
// really made of (setbacks, podiums, floating tiers, roof shapes).
//
// The Simple 3D Buildings rule, kept simply: a building whose parts carry
// heights and cover its outline is drawn BY ITS PARTS - the outline stays in
// the file (same index, so everything keyed by footprint index - the pack
// tower hash, lots, signs - is unchanged) but HIDDEN, its height raised to the
// tallest part's top. Parts are appended after every outline, each pointing
// back at its outline (whose ground and look hash it shares, so the tiers of
// one tower meet and wear one facade). A building with parts but no usable
// heights (the silver crown tower's 61 bare parts) keeps its outline.
//
// The landmark table keys real buildings by OSM element id under NEUTRAL
// names (owner rule: no brands, no real names in code, data or UI). It keeps
// them out of the random pack-tower swap and gives phase C its handle.

/// index + 1 = the landmark byte in PBLD v3 (0 = none). APPEND ONLY.
export const LANDMARKS = [
  ['w341587198', 'spired crown tower'],
  ['w1550692284', 'open-frame tower'],
  ['w131139746', 'silver crown tower'],
  ['w380092606', 'curved-top tower'],
  ['w90480703', 'pyramid-top tower'],
  ['w1047699840', '300 tower'],
  ['w1046738978', 'glass setback tower'],
  ['w1046848108', 'brick headquarters'],
  ['w498311840', 'slim residential tower'],
  ['w90480712', 'plaza tower'],
  ['w397372378', '1970s plaza tower'],
  ['w1047699852', '400 tower'],
  ['w502718740', 'the church'],
  ['r12346952', 'the stadium'],
  ['w715062087', 'pointed tower'],
  ['w1332536425', 'slanted tower'],
  ['w773909122', 'the arena'],
  ['w984135971', 'new office tower'],
];
const LANDMARK_OF = new Map(LANDMARKS.map(([k], i) => [k, i + 1]));
export const landmarkOf = key => LANDMARK_OF.get(key) || 0;

/// roof byte: 0 flat, 1 pyramidal (apex at the centroid), 2 dome (rings to
/// the centroid), 3 round (a barrel along the long axis), 4 skillion (one
/// plane), 5 gabled (a ridge along the long axis, ends hipped).
const ROOF = { flat: 0, pyramidal: 1, cone: 1, dome: 2, onion: 2, round: 3, skillion: 4, lean_to: 4,
  gabled: 5, hipped: 5, half_hipped: 5, side_hipped: 5, gambrel: 5, mansard: 5, saltbox: 5 };
export const roofShapeOf = t => ROOF[(t['roof:shape'] || '').trim().toLowerCase()] ?? 0;

const LEVEL_M = 3.4;

/// The oriented box (smallest area over the polygon's own edge directions).
export function obbOf(pts) {
  let best = null;
  for (let i = 0; i < Math.min(pts.length, 16); i++) {
    const p = pts[i], q = pts[(i + 1) % pts.length];
    let dx = q[0] - p[0], dz = q[1] - p[1];
    const m = Math.hypot(dx, dz);
    if (m < 0.2) continue;
    dx /= m; dz /= m;
    let u0 = 1e18, u1 = -1e18, v0 = 1e18, v1 = -1e18;
    for (const r of pts) {
      const u = r[0] * dx + r[1] * dz, v = -r[0] * dz + r[1] * dx;
      u0 = Math.min(u0, u); u1 = Math.max(u1, u); v0 = Math.min(v0, v); v1 = Math.max(v1, v);
    }
    const a = (u1 - u0) * (v1 - v0);
    if (!best || a < best.a) best = { a, hu: (u1 - u0) / 2, hv: (v1 - v0) / 2 };
  }
  if (!best) return { hu: 0.5, hv: 0.5 };
  return best.hu >= best.hv ? best : { hu: best.hv, hv: best.hu };
}

function inPoly(x, z, pts) {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const a = pts[i], b = pts[j];
    if ((a[1] > z) !== (b[1] > z) && x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]) inside = !inside;
  }
  return inside;
}
function centroid(pts) {
  let a = 0, cx = 0, cz = 0;
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i], q = pts[(i + 1) % pts.length];
    const c = p[0] * q[1] - q[0] * p[1];
    a += c; cx += (p[0] + q[0]) * c; cz += (p[1] + q[1]) * c;
  }
  if (Math.abs(a) < 1e-6) return [pts[0][0], pts[0][1]];
  return [cx / (3 * a), cz / (3 * a)];
}
const bboxOf = pts => {
  let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
  for (const p of pts) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
  return [x0, z0, x1, z1];
};

/// The roof a set of tags asks for, on a polygon whose box is hv across:
/// { roof, roofH, roofDir } with roofH clamped into the part's own height.
export function roofOf(t, parseHeight, hv, span) {
  const roof = roofShapeOf(t);
  if (!roof || span <= 0.5) return { roof: 0, roofH: 0, roofDir: -1 };
  let rh = parseHeight(t['roof:height']);
  if (!Number.isFinite(rh) || rh <= 0) {
    const rl = parseFloat(t['roof:levels']);
    rh = Number.isFinite(rl) && rl > 0 ? rl * 3 : roof === 4 ? Math.min(4, hv * 0.4) : roof === 5 ? hv * 0.6 : hv;
  }
  rh = Math.max(0.3, Math.min(rh, span));
  let dir = parseFloat(t['roof:direction']);
  if (!Number.isFinite(dir)) {
    const w = { N: 0, NE: 45, E: 90, SE: 135, S: 180, SW: 225, W: 270, NW: 315 }[(t['roof:direction'] || '').trim().toUpperCase()];
    dir = w ?? -1;
  }
  return { roof, roofH: rh, roofDir: dir < 0 ? -1 : Math.round(((dir % 360) + 360) % 360) };
}

/// buildings: the kept outlines (each with key, tags-derived h, style, look).
/// raw: parts_core.json's elements. Returns the parts to append, and stats.
export function applyParts({ raw, buildings, held = [], toX, toZ, rdp, polyArea, parseHeight, facadeLook }) {
  // held: outlines the exporter dropped for layer > 0 alone (no road through
  // them); one comes back, after every kept outline, when parts raise it
  const cand = buildings.concat(held.map(b => ({ ...b, held: true })));
  const stats = { partWays: 0, partsKept: 0, partsNoHeight: 0, partsOrphan: 0, outlinesWithParts: 0,
                  replaced: 0, keptBare: 0, keptPartial: 0, landmarks: 0, raised: [] };
  // outline grid (by bbox) for the containment search
  const CELL = 128, grid = new Map();
  const key = (cx, cz) => cx * 100003 + cz;
  cand.forEach((b, i) => {
    b.landmark = landmarkOf(b.key);
    if (b.landmark) { b.noSwap = true; stats.landmarks++; }
    const bb = b.bb = bboxOf(b.pts);
    for (let cx = Math.floor(bb[0] / CELL); cx <= Math.floor(bb[2] / CELL); cx++)
      for (let cz = Math.floor(bb[1] / CELL); cz <= Math.floor(bb[3] / CELL); cz++) {
        const k = key(cx, cz);
        if (!grid.has(k)) grid.set(k, []);
        grid.get(k).push(i);
      }
  });
  // every part polygon (ways, and the outer members of part relations)
  const parts = [];
  const addPart = (geom, t, k) => {
    let pts = geom.map(g => [toX(g.lon), toZ(g.lat)]);
    if (pts.length > 1 && Math.hypot(pts[0][0] - pts[pts.length - 1][0], pts[0][1] - pts[pts.length - 1][1]) < 0.05) pts.pop();
    pts = rdp(pts, 0.35);
    if (pts.length < 3) return;
    let area = polyArea(pts);
    if (area < 0) { pts.reverse(); area = -area; }
    if (area < 2) return;
    if (pts.length > 40) pts = rdp(pts, 1.2);
    if (pts.length < 3 || pts.length > 255) return;
    stats.partWays++;
    const minH = (() => {
      const m = parseHeight(t.min_height);
      if (Number.isFinite(m) && m > 0) return m;
      const ml = parseFloat(t['building:min_level']);
      return Number.isFinite(ml) && ml > 0 ? ml * LEVEL_M : 0;
    })();
    let h = parseHeight(t.height);
    if (!Number.isFinite(h) || h <= 0) {
      const lv = parseFloat(t['building:levels']);
      if (Number.isFinite(lv) && lv > 0) {
        h = lv * LEVEL_M;
        const rl = parseFloat(t['roof:levels']);
        const rh = parseHeight(t['roof:height']);
        if (roofShapeOf(t)) h += Number.isFinite(rh) && rh > 0 ? rh : Number.isFinite(rl) && rl > 0 ? rl * 3 : 0;
      } else h = NaN;
    }
    parts.push({ key: k, pts, area, t, minH, h: Number.isFinite(h) ? Math.min(h, 340) : NaN });
  };
  for (const el of raw) {
    if (!el.tags || !el.tags['building:part']) continue;
    if (el.type === 'way' && el.geometry) addPart(el.geometry, el.tags, 'w' + el.id);
    else if (el.type === 'relation' && el.members)
      for (const m of el.members) if (m.role === 'outer' && m.geometry) addPart(m.geometry, el.tags, 'r' + el.id);
  }
  // each part to the smallest kept outline holding its centroid
  const byOutline = new Map();
  for (const p of parts) {
    const [cx, cz] = centroid(p.pts);
    let best = -1, bestA = 1e18;
    for (const i of grid.get(key(Math.floor(cx / CELL), Math.floor(cz / CELL))) || []) {
      const b = cand[i];
      if (cx < b.bb[0] || cx > b.bb[2] || cz < b.bb[1] || cz > b.bb[3]) continue;
      if (b.area < bestA && p.area <= b.area * 1.15 && inPoly(cx, cz, b.pts)) { best = i; bestA = b.area; }
    }
    if (best < 0) {
      stats.partsOrphan++;
      if (process.env.PSX_PARTS_DEBUG && p.h >= 20) console.log('  orphan', p.key, p.h.toFixed(0), 'm', p.area.toFixed(0), 'm2 at', cx.toFixed(0), cz.toFixed(0));
      continue;
    }
    if (!byOutline.has(best)) byOutline.set(best, []);
    byOutline.get(best).push(p);
  }
  const out = [], rescued = [];
  for (const [ci, list] of [...byOutline].sort((a, b) => a[0] - b[0])) {
    const b = cand[ci];
    let oi = ci;
    stats.outlinesWithParts++;
    b.noSwap = true;
    const usable = list.filter(p => Number.isFinite(p.h) && p.h > p.minH + 0.5);
    stats.partsNoHeight += list.length - usable.length;
    const aAll = list.reduce((s, p) => s + p.area, 0), aUse = usable.reduce((s, p) => s + p.area, 0);
    if (!usable.length || aUse < aAll * 0.5) { if (!b.held) stats.keptBare++; continue; }
    if (b.held) { oi = buildings.length + rescued.length; rescued.push(b); b.noSwap = true; stats.rescued = (stats.rescued || 0) + 1; }
    // how much of the outline the usable parts stand on (sampled)
    const bb = b.bb;
    const step = Math.max(1.0, Math.sqrt(b.area) / 40);
    let inside = 0, covered = 0;
    for (let x = bb[0] + step / 2; x < bb[2]; x += step)
      for (let z = bb[1] + step / 2; z < bb[3]; z += step) {
        if (!inPoly(x, z, b.pts)) continue;
        inside++;
        for (const p of usable) if (inPoly(x, z, p.pts)) { covered++; break; }
      }
    const cover = inside ? covered / inside : 0;
    const top = Math.max(...usable.map(p => p.h));
    if (process.env.PSX_PARTS_DEBUG && (b.landmark || top >= 40)) console.log('  outline', b.key, b.landmark ? LANDMARKS[b.landmark - 1][1] : '', 'h', b.h.toFixed(0), 'top', top.toFixed(0), 'cover', cover.toFixed(2), 'parts', usable.length + '/' + list.length);
    if (cover >= 0.8) {
      b.hidden = true;
      stats.replaced++;
      if (top > b.h + 10) stats.raised.push(`${b.key} ${b.h.toFixed(0)} -> ${top.toFixed(0)} m`);
      b.h = Math.max(b.h, top);
    } else {
      // partly mapped: the outline stands as it was (no taller than its
      // tallest part) and the parts that rise above it are drawn on it
      stats.keptPartial++;
      b.h = Math.min(b.h, top);
    }
    // the class the whole building reads as (B1's look by height)
    // (no shopfront strips on parts: a part is bucketed by its own centre,
    // and a shop slot new to a tile is a draw call on the phone)
    const cls = b.h >= 55 ? 0 : b.h >= 20 ? 1 : 2;
    for (const p of usable) {
      if (!b.hidden && p.h <= b.h + 0.5) continue;   // under the kept outline
      const { hv } = obbOf(p.pts);
      const rf = roofOf(p.t, parseHeight, hv, p.h - p.minH);
      // the part's own colour and material, else its outline's
      const own = facadeLook(p.t), whole = b.look || { use: 0, mat: 0, rgb: null };
      const look = { use: whole.use, mat: own.mat || whole.mat, rgb: own.rgb || (own.mat ? null : whole.rgb) };
      out.push({ pts: p.pts, h: p.h, style: cls, gable: false, area: p.area, look, part: true, noSwap: true,
                 minH: b.hidden ? p.minH : Math.max(p.minH, b.h), roof: rf.roof, roofH: rf.roofH, roofDir: rf.roofDir, landmark: b.landmark || 0, outline: oi });
      stats.partsKept++;
    }
  }
  // an outline's own roof:shape (no parts, not a house: houses keep their gables)
  for (const b of buildings.concat(rescued)) {
    if (b.hidden || b.gable || b.style === 3 || !b.rt) continue;
    const rf = roofOf(b.rt, parseHeight, obbOf(b.pts).hv, b.h);
    if (rf.roof) { b.roof = rf.roof; b.roofH = rf.roofH; b.roofDir = rf.roofDir; stats.outlineRoofs = (stats.outlineRoofs || 0) + 1; }
  }
  return { parts: out, rescued, stats };
}
