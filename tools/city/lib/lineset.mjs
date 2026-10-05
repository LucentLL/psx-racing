// lineset.mjs - THE LINE SET (roads pass L2, plan B3 minimal): what MUTCD
// paints on each edge, from its OSM tags, as section LSET of
// charlotte_city.bytes. The game draws from it (CityMap reads it,
// LineModel.LayoutOf(edge) lays the lines out); metrics LANES reads it back.
//
//   LSET  u32 n (= edges) | n x { u8 nF, nB; u8 centre; u8 flags;
//                                 u16 turnOnly; u8 subCls; u8 bike }
//   nF / nB  lanes in the edge's a->b direction / against it (a one-way: all
//            forward). nF + nB (+1 for a TWLTL) is the profile's lane count,
//            so the drawn width never changes.
//   centre   0 none (a two-way road with lanes=1, or a one-way), 1 double
//            yellow between the directions, 2 a centre two-way left-turn lane
//   flags    1 marked (Q1: residential / unclassified / living_street /
//            service streets are UNMARKED unless lane_markings=yes, lanes >= 3
//            or lanes:forward/backward say otherwise; lane_markings=no
//            unmarks any road), 2 a turn bay for forward traffic, 4 for
//            backward traffic (TAPR's turn-bay records), 8 lane_markings=yes,
//            16 lane_markings=no, 32 unpaved
//   turnOnly bit i = lane i (0 the leftmost in its travel direction) is
//            turn-only (left / right / slight_ / sharp_ without through):
//            bits 0-7 forward lanes, 8-15 backward lanes
//   subCls   0 none (tertiary and up), 1 residential, 2 unclassified,
//            3 living_street, 4 service, 5 parking_aisle, 6 driveway,
//            7 alley, 8 drive-through, 9 track
//   bike     1 bike lane left of a->b, 2 right, 4 bike shoulder left, 8 right
//
// THE TWLTL RULE (owner Q1 era, plan B3): a centre turn lane is painted only
// where the tags say so - lanes:both_ways >= 1, turn:lanes:both_ways,
// centre_turn_lane=yes - or where the count leaves one lane over that no
// direction claims (lanes:forward + lanes:backward + 1 = lanes, or an odd
// count with no forward/backward tags: those two are LISTED, 'implied' and
// 'odd-count'). Never where forward + backward = lanes: that road is an
// uneven split with the double yellow between its directions.

export const CENTRE_NONE = 0, CENTRE_DY = 1, CENTRE_TWLTL = 2;
export const LS_MARKED = 1, LS_BAY_F = 2, LS_BAY_B = 4, LS_LM_YES = 8, LS_LM_NO = 16, LS_UNPAVED = 32;
export const SUBCLS = ['', 'residential', 'unclassified', 'living_street', 'service', 'parking_aisle', 'driveway', 'alley', 'drive-through', 'track'];

const UNPAVED = new Set(['unpaved', 'gravel', 'fine_gravel', 'dirt', 'ground', 'earth', 'mud', 'sand', 'grass', 'compacted', 'pebblestone', 'woodchips']);
const has = (t, k) => t[k] !== undefined && t[k] !== '';

/// The tag facts keepWay stores on a way for the line set (rev: the way was
/// reversed to run with a oneway=-1 - its left and right swap).
export function lineTagsOf(t, base, rev) {
  const both = parseInt(t['lanes:both_ways'], 10);
  const twEv = (Number.isFinite(both) && both > 0) || has(t, 'turn:lanes:both_ways') || t.centre_turn_lane === 'yes';
  const lm = t.lane_markings === 'yes' ? 1 : t.lane_markings === 'no' ? -1 : 0;
  let sub = 0;
  if (base === 'residential') sub = 1;
  else if (base === 'unclassified') sub = 2;
  else if (base === 'living_street') sub = 3;
  else if (base === 'service') sub = ({ parking_aisle: 5, driveway: 6, alley: 7, 'drive-through': 8 })[t.service] || 4;
  else if (base === 'track') sub = 9;
  const unpaved = UNPAVED.has(t.surface || '');
  // bike lanes / shoulders, in the way's own left/right
  const side = v => v === 'lane' ? 1 : v === 'shoulder' ? 4 : 0;
  let L = side(t['cycleway:left']) | side(t['cycleway:both']) | side(t.cycleway);
  let R = side(t['cycleway:right']) | side(t['cycleway:both']) | side(t.cycleway);
  if (rev) [L, R] = [R, L];
  const bike = (L & 1 ? 1 : 0) | (R & 1 ? 2 : 0) | (L & 4 ? 4 : 0) | (R & 4 ? 8 : 0);
  return { twEv, lm, sub, unpaved, bike };
}

/// Turn-only lanes of one direction's turn:lanes value: bit i = lane i
/// (OSM order, left to right in travel) turns and never goes through.
export function turnOnlyBits(tl) {
  if (!tl) return 0;
  let bits = 0;
  tl.split('|').forEach((lane, i) => {
    if (i > 7) return;
    const v = lane.split(';').map(x => x.trim());
    const turns = v.some(x => /^(slight_|sharp_)?(left|right)$/.test(x));
    if (turns && !v.includes('through')) bits |= 1 << i;
  });
  return bits;
}

/// [nF, nB, centre, why] of a two-way edge whose profile has n lanes, from
/// way w's tags (count: the edge's lanes, its own or lent).
export function twoWaySplit(w, count, n) {
  if (count <= 1) return [1, 1, CENTRE_NONE, 'one-lane'];
  const lf = w.lf, lb = w.lb;
  if (Number.isFinite(lf) && Number.isFinite(lb) && lf >= 1 && lb >= 1) {
    if (w.lt.twEv && lf + lb + 1 === n) return [lf, lb, CENTRE_TWLTL, 'tag'];
    if (lf + lb === n) return [lf, lb, CENTRE_DY, 'fb'];
    if (lf + lb + 1 === n) return [lf, lb, CENTRE_TWLTL, 'implied'];
    // the profile cannot hold the tagged lanes exactly (a 7-lane street
    // drawn as six; a tagged TWLTL on an even count, padded to the next odd
    // profile): share the lanes the profile has in the tags' proportion,
    // never evening out an uneven split
    if (lf !== lb) {
      const tw = w.lt.twEv && n % 2 === 1 ? 1 : 0, avail = n - tw;
      let f = Math.min(avail - 1, Math.max(1, Math.round(avail * lf / (lf + lb)))), b = avail - f;
      if (f === b) { if (lf > lb) { f++; b--; } else { f--; b++; } }
      if (f >= 1 && b >= 1) return [f, b, tw ? CENTRE_TWLTL : CENTRE_DY, tw ? 'tag-fitted' : 'fitted'];
    }
  }
  if (n % 2) return [(n - 1) / 2, (n - 1) / 2, CENTRE_TWLTL, w.lt.twEv ? 'tag' : 'odd-count'];
  return [n / 2, n / 2, CENTRE_DY, 'even'];
}

/// The split of an edge in its own a->b frame, [nF, nB, centre, why] (a lent
/// count takes its donor's split): THE one rule, read by the line set below
/// and by lineClean's TAPR (roads pass L4: the side a lane opens on is the
/// side of the direction that gains it, so the paint and the ribbon agree).
export function makeSplitOf(profileFor) {
  const memo = new Map();
  const splitOf = (e, depth = 0) => {
    if (memo.has(e)) return memo.get(e);
    const w = e.way;
    const n = profileFor(w.rank, w.link, w.oneway, e.lanes, e.turn).lanes;
    let r;
    if (w.oneway) r = [n, 0, CENTRE_NONE, 'oneway'];
    else if (e.lsetFrom && depth < 50) {
      const d = splitOf(e.lsetFrom.e, depth + 1);
      const dn = profileFor(e.lsetFrom.e.way.rank, e.lsetFrom.e.way.link, e.lsetFrom.e.way.oneway, e.lsetFrom.e.lanes, e.lsetFrom.e.turn).lanes;
      r = dn === n && !e.lsetFrom.e.way.oneway
        ? (e.lsetFrom.flip ? [d[1], d[0], d[2], 'lent:' + d[3]] : [d[0], d[1], d[2], 'lent:' + d[3]])
        : twoWaySplit(w, e.lanes, n);
    } else r = twoWaySplit(w, e.lanes, n);
    memo.set(e, r);
    return r;
  };
  return splitOf;
}

/// Every edge's line set (the LSET rows), plus the list of TWLTLs painted
/// without tag evidence. profileFor: citydata's; tapr: lineClean's records.
export function buildLineSets(edges, profileFor, tapr) {
  const bay = new Uint8Array(edges.length);
  for (const t of tapr) if (t.flags & 1) {
    const e = edges[t.edge];
    if (!e) continue;
    bay[t.edge] |= e.way.oneway || t.end === 0 ? LS_BAY_F : LS_BAY_B;
  }
  const splitOf = makeSplitOf(profileFor);
  const rows = new Array(edges.length), noEvidence = [];
  for (let i = 0; i < edges.length; i++) {
    const e = edges[i], w = e.way, lt = w.lt;
    const [nF, nB, centre, why] = splitOf(e);
    const lfb = Number.isFinite(w.lf) && Number.isFinite(w.lb);
    const marked = lt.lm !== -1 && (w.rank >= 1 || lt.lm === 1 || (w.tagged && w.lanes >= 3) || lfb);
    const flags = (marked ? LS_MARKED : 0) | bay[i] | (lt.lm === 1 ? LS_LM_YES : 0) | (lt.lm === -1 ? LS_LM_NO : 0) | (lt.unpaved ? LS_UNPAVED : 0);
    let turnOnly = w.oneway ? turnOnlyBits(w.tl) : (turnOnlyBits(w.tlf) | (turnOnlyBits(w.tlb) << 8));
    // a blip given its neighbour's layout (lineclean's BLIP RULE, 2026-10-04)
    // paints that neighbour's turn arrows, not its own tags' (they name lanes
    // it no longer has)
    if (e.laneFix === 'blip' && e.lsetFrom) {
      const d = e.lsetFrom.e.way;
      if (d.oneway) turnOnly = turnOnlyBits(d.tl);
      else {
        const f = turnOnlyBits(d.tlf), b = turnOnlyBits(d.tlb);
        turnOnly = e.lsetFrom.flip ? (b | (f << 8)) : (f | (b << 8));
      }
    }
    rows[i] = { nF, nB, centre, flags, turnOnly, sub: lt.sub, bike: lt.bike, why };
    if (centre === CENTRE_TWLTL && !/tag(-fitted)?$/.test(why)) noEvidence.push({ edge: i, way: w.id, why, len: e.len });
  }
  return { rows, noEvidence };
}
