// Parking decks (2026-10-05, part 1): which of OSM's multi-storey car parks
// the game builds as a DRIVABLE deck, and the facts it builds one from.
//
// A deck is drivable only when ALL of these hold (otherwise it stays the solid
// building it always was, and the reason is counted):
//  - its level count is KNOWN: building:levels / parking:levels, or derived
//    from its height (LEVELS_FROM_HEIGHT below, checked against the decks that
//    carry both);
//  - its footprint fills its oriented rectangle to FILL_MIN or better;
//  - the rectangle is wide enough for a layout: >= HELIX_W for the two-bay
//    sloped-floor helix (two 60 ft bays), >= SINGLE_W for a single bay with end
//    ramps (classified, not built in part 1), and long enough for the helix's
//    sloped bays at <= 6.5% plus a turning bay at each end (HELIX_L);
//  - it is on the WHITELIST (the owner, 2026-10-05: "5-10 iconic decks"). The
//    whitelist is keyed by OSM way id; the names in its comments are for us
//    only and never reach game text (Docs: NO BRANDS).
//
// Output: charlotte_decks.bytes, PDCK v1 (CityDecks.cs reads it):
//   u32 "PDCK", u32 1, u32 count, then per deck (every deck OSM has, so the
//   game can report the counts): i32 bld index (-1: not in the bld file),
//   u32 way id, u8 levels (0 unknown), u8 layout (0 solid, 1 helix, 2 single
//   bay), u8 reason (REASONS), u8 flags (bit0 enabled, bit1 levels tagged,
//   bit2 levels from height), f32 cx, cz, ux, uz (unit long axis), hu, hv
//   (half length, half width), f32 fill, f32 height (m, 0 untagged).

export const FLOOR1_M = 3.4, FLOOR_M = 3.05, PARAPET_M = 1.1;
export const FILL_MIN = 0.8, HELIX_W = 34, SINGLE_W = 18, HELIX_L = 52;
export const REASONS = ['drivable', 'levels unknown', 'odd shape (fill < 80%)', 'too narrow (< 18 m)',
  'single bay (end ramps not built in part 1)', 'too short for a helix (< 52 m)', 'not on the whitelist',
  'not in the bld file (dropped: a layer, a road through it, or tiny)', 'drawn by its building:parts'];

// The whitelist (OSM way id -> note). Real names live HERE ONLY.
export const WHITELIST = new Map([
  [120628264, 'Seventh Street Station car park'],
  [94405123, 'Mint Street parking deck'],
  [90480718, 'First Citizens Bank Plaza garage'],
  [90480727, 'Two Thirty South Tryon deck'],
  [255159816, 'the 7-level deck'],
]);
// More picked from the qualifying set nearest the uptown routes (filled in
// from the export's own candidate list, see the log line "decks: candidates").
export const WHITELIST_EXTRA = new Map([
  [394430321, 'uptown, 9 levels, 57 x 106 m, ~240 m from the centre'],
  [500204485, 'uptown, 9 levels, 46 x 57 m, ~300 m from the centre'],
  // coverage (2026-10-06, the owner: the campus deck by the engineering building)
  [1053094969, 'UNC Charlotte, the permit deck beside the EPIC building'],
  [1053094961, 'UNC Charlotte, Snyder Deck, west of the EPIC building'],
]);

/// Levels from a height in metres. Two conventions are possible (the roof
/// deck counted as a level, or not); the export logs both against the decks
/// tagged with levels AND height and uses the one that fits (ROOF_COUNTED).
export const levelsFromHeight = (h, roofCounted) =>
  roofCounted ? 2 + (h - FLOOR1_M - PARAPET_M) / FLOOR_M : 1 + (h - FLOOR1_M - PARAPET_M) / FLOOR_M;

function hull(pts) {
  const p = pts.slice().sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  const cr = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
  const lo = [], up = [];
  for (const q of p) { while (lo.length >= 2 && cr(lo[lo.length - 2], lo[lo.length - 1], q) <= 0) lo.pop(); lo.push(q); }
  for (let i = p.length - 1; i >= 0; i--) { const q = p[i]; while (up.length >= 2 && cr(up[up.length - 2], up[up.length - 1], q) <= 0) up.pop(); up.push(q); }
  return lo.slice(0, -1).concat(up.slice(0, -1));
}

/// The minimum-area oriented rectangle round a polygon (rotating calipers on
/// its hull): centre, unit long axis, half length, half width.
export function orientedRect(pts) {
  const h = hull(pts);
  let best = null;
  for (let i = 0; i < h.length; i++) {
    const a = h[i], b = h[(i + 1) % h.length];
    let ux = b[0] - a[0], uz = b[1] - a[1];
    const L = Math.hypot(ux, uz); if (L < 1e-6) continue;
    ux /= L; uz /= L;
    let u0 = 1e18, u1 = -1e18, v0 = 1e18, v1 = -1e18;
    for (const p of h) {
      const u = p[0] * ux + p[1] * uz, v = -p[0] * uz + p[1] * ux;
      u0 = Math.min(u0, u); u1 = Math.max(u1, u); v0 = Math.min(v0, v); v1 = Math.max(v1, v);
    }
    const area = (u1 - u0) * (v1 - v0);
    if (!best || area < best.area) {
      const cu = (u0 + u1) / 2, cv = (v0 + v1) / 2;
      best = { area, cx: cu * ux - cv * uz, cz: cu * uz + cv * ux, ux, uz, hu: (u1 - u0) / 2, hv: (v1 - v0) / 2 };
    }
  }
  if (best.hv > best.hu) {   // u along the LONG side
    best = { ...best, ux: -best.uz, uz: best.ux, hu: best.hv, hv: best.hu };
  }
  return best;
}

const polyArea = pts => { let s = 0; for (let i = 0; i < pts.length; i++) { const p = pts[i], q = pts[(i + 1) % pts.length]; s += p[0] * q[1] - q[0] * p[1]; } return Math.abs(s) / 2; };

/// Every OSM deck, classified. rawBld: the buildings cache's elements; all:
/// the bld file's entries in order (each with key 'w<id>' / 'r<id>', pts, h,
/// hidden). parseHeight: the exporter's. uptown: [x, z] for the candidate list.
export function buildDecks({ rawBld, rawCov = [], all, parseHeight, uptown, log = console.log }) {
  const isDeck = t => t && (t.parking === 'multi-storey' || t.building === 'parking');
  const bldOf = new Map();
  all.forEach((b, i) => { if (b.key && !b.part && !bldOf.has(b.key)) bldOf.set(b.key, i); });
  const raw = [];
  for (const el of rawBld) if (el.type === 'way' && isDeck(el.tags)) raw.push(el);
  // coverage (2026-10-06): the decks outside the core come after the core's
  // records, and the levels formula below stays the one the core's decks chose
  const nCore = raw.length;
  for (const el of rawCov) if (el.type === 'way' && isDeck(el.tags)) raw.push(el);
  // the levels formula, checked on the decks that carry both
  let errR = 0, errN = 0, nBoth = 0;
  const tagged = t => { const a = parseInt(t['building:levels'], 10), b = parseInt(t['parking:levels'], 10);
    return Number.isFinite(b) && b > 0 ? b : Number.isFinite(a) && a > 0 ? a : NaN; };
  const checks = [];
  for (const el of raw.slice(0, nCore)) {
    const L = tagged(el.tags), h = parseHeight(el.tags.height);
    if (!Number.isFinite(L) || !Number.isFinite(h) || h <= 0) continue;
    nBoth++;
    const r = Math.round(levelsFromHeight(h, true)), n = Math.round(levelsFromHeight(h, false));
    errR += Math.abs(r - L); errN += Math.abs(n - L);
    checks.push(`w${el.id} h ${h.toFixed(1)} tagged ${L} -> roof-counted ${r}, not ${n}`);
  }
  const roofCounted = errR <= errN;
  log(`decks: levels formula on ${nBoth} decks tagged with both: roof counted |err| ${errR}, roof not counted |err| ${errN} -> ${roofCounted ? 'ROOF COUNTED' : 'ROOF NOT COUNTED'}`);
  for (const c of checks) log('  ' + c);
  const out = [], counts = new Array(REASONS.length).fill(0), cands = [];
  let tagN = 0, hN = 0, noneN = 0;
  for (const el of raw) {
    const t = el.tags, key = 'w' + el.id;
    const bi = bldOf.has(key) ? bldOf.get(key) : -1;
    const b = bi >= 0 ? all[bi] : null;
    let L = tagged(t), how = 1;
    const h = parseHeight(t.height);
    if (!Number.isFinite(L)) {
      if (Number.isFinite(h) && h > 0) { L = Math.max(1, Math.round(levelsFromHeight(h, roofCounted))); how = 2; hN++; }
      else { L = 0; how = 0; noneN++; }
    } else tagN++;
    const d = { bi, id: el.id, levels: L, layout: 0, reason: 0, flags: how === 1 ? 2 : how === 2 ? 4 : 0,
      cx: 0, cz: 0, ux: 1, uz: 0, hu: 0, hv: 0, fill: 0, h: Number.isFinite(h) ? h : 0 };
    if (b) {
      const r = orientedRect(b.pts);
      Object.assign(d, { cx: r.cx, cz: r.cz, ux: r.ux, uz: r.uz, hu: r.hu, hv: r.hv, fill: polyArea(b.pts) / Math.max(1, r.area) });
    }
    const W = 2 * d.hv, Lm = 2 * d.hu;
    if (!b) d.reason = 7;
    else if (b.hidden) d.reason = 8;
    else if (L < 2) d.reason = 1;
    else if (d.fill < FILL_MIN) d.reason = 2;
    else if (W < SINGLE_W) d.reason = 3;
    else if (W < HELIX_W) { d.layout = 2; d.reason = 4; }
    else if (Lm < HELIX_L) { d.layout = 1; d.reason = 5; }
    else {
      d.layout = 1;
      const listed = WHITELIST.has(el.id) || WHITELIST_EXTRA.has(el.id);
      if (!listed) d.reason = 6;
      if (listed) d.flags |= 1;
      cands.push({ id: el.id, L, W, Lm, dist: Math.hypot(d.cx - uptown[0], d.cz - uptown[1]), listed });
    }
    if (d.layout === 1 && d.reason === 0) d.layout = 1;
    counts[d.reason]++;
    out.push(d);
  }
  log(`decks: ${raw.length} in OSM; levels tagged ${tagN}, from height ${hN}, unknown ${noneN}`);
  log('decks: ' + REASONS.map((r, i) => `${r} ${counts[i]}`).join('; '));
  cands.sort((a, b) => a.dist - b.dist);
  log('decks: candidates (helix-qualifying, nearest uptown first): ' + cands.slice(0, 16).map(c =>
    `${c.id}${c.listed ? '*' : ''} L${c.L} ${c.W.toFixed(0)}x${c.Lm.toFixed(0)} @${c.dist.toFixed(0)}m`).join(', '));
  for (const id of [...WHITELIST.keys(), ...WHITELIST_EXTRA.keys()]) {
    const d = out.find(o => o.id === id);
    log(`decks: whitelist ${id}: ${d ? `levels ${d.levels} layout ${d.layout} ${(2 * d.hv).toFixed(1)} x ${(2 * d.hu).toFixed(1)} m fill ${d.fill.toFixed(2)} -> ${REASONS[d.reason]}` : 'NOT A DECK IN OSM'}`);
  }
  return { decks: out, counts, roofCounted };
}

export function writeDecks(Writer, decks) {
  const w = new Writer();
  w.u32(0x4B434450); w.u32(1); w.u32(decks.length);
  for (const d of decks) {
    w.i32(d.bi); w.u32(d.id); w.u8(Math.min(255, d.levels)); w.u8(d.layout); w.u8(d.reason); w.u8(d.flags);
    w.f32(d.cx); w.f32(d.cz); w.f32(d.ux); w.f32(d.uz); w.f32(d.hu); w.f32(d.hv); w.f32(d.fill); w.f32(d.h);
  }
  return w.bytes();
}
