// lm.mjs - an offline port (read-only analysis) of the parts of the game's
// line model that move lanes and painted lines SIDEWAYS, on the shipped
// charlotte_city.bytes:
//   LineModel.Init + FixMirrored              (LineModel.cs:170-249)
//   the mitre / through-pair decision          (CityMeshes.cs:1626-1804, ComputeTrimsCore; no MitreFan pass)
//   BuildEases: held / two-sided / crossed joins, relays, one-sided width
//   eases, the SPLT median tapers (with the refuge-pair exception), SHIFT
//   records                                    (LineModel.cs:417-648)
//   RoomAlong / PropagateShift / Propagate / LengthFor / FloorOf / ShiftRate
//   Relay / RelayPairs / PartnerLat / LinesAt / Ext / LaneCentre
// NOT ported (stated in the census): the refuge islands' own profile eases
// (17 accepted pairs: their carriageways are skipped by the split taper, as
// in the game, and flagged), BranchDropAt (one-way ramps at 9 listed nodes),
// merge-zone aux lanes, turn pockets (off by default), the MitreFan re-run.
import { load, pointAt, tangentAt } from './load.mjs';

export const LANE = 3.6576, PH = 0.06;
export const KEdgeP = 0, KEdgeM = 1, KYellow = 2, KYellowDash = 3, KWhiteDash = 4;
const TW = (l, t) => ({ oneway: false, lanes: l, turnLane: t, shl: 0.3, shr: 0.3, key: 'tw' + l + (t ? 't' : '') });
const OW = l => ({ oneway: true, lanes: l, turnLane: false, shl: 0.3, shr: 0.3, key: 'ow' + l });
const RP = l => ({ oneway: true, lanes: l, turnLane: false, shl: 0.6, shr: 1.8, key: 'ramp' + l });
const XW = l => ({ oneway: true, lanes: l, turnLane: false, shl: 0.9, shr: 2.4, key: 'xw' + l });
const MW = l => ({ oneway: true, lanes: l, turnLane: false, shl: 1.2, shr: 3.0, key: 'mw' + l });
export const PROFILES = [TW(2, false), TW(3, true), TW(4, false), TW(5, true), TW(6, false), OW(1), OW(2), OW(3), OW(4),
  RP(1), RP(2), RP(3), XW(2), XW(3), XW(4), MW(2), MW(3), MW(4), MW(5), MW(6)];
for (const p of PROFILES) p.W = p.lanes * LANE + p.shl + p.shr;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
export function indexFor(cls, link, oneway, lanes, turn) {
  if (!oneway) { let n = clamp(lanes, 2, 6); if (turn && n % 2 === 0 && n < 6) n++; if (n === 3 || n === 5) return n === 3 ? 1 : 3; return n <= 2 ? 0 : n === 4 ? 2 : 4; }
  if (link) { if (cls >= 4) return 9 + clamp(lanes, 1, 3) - 1; return 5 + clamp(lanes, 1, 4) - 1; }
  if (cls >= 4 && lanes <= 1) return 9;
  if (cls >= 5) return 15 + clamp(lanes, 2, 6) - 2;
  if (cls === 4) return 12 + clamp(lanes, 2, 4) - 2;
  return 5 + clamp(lanes, 1, 4) - 1;
}
function defaultSplit(pr) { if (pr.oneway) return [pr.lanes, 0, 0]; const t = pr.turnLane ? 1 : 0; const n = (pr.lanes - t) / 2 | 0; return [n, n, pr.turnLane ? 2 : 1]; }
function linesFor(pr, nF, nB, centre, marked) {
  const m = [], kind = [];
  if (marked) {
    const total = pr.W, h = PH, add = (at, k) => { m.push(at); kind.push(k); };
    add(pr.shl + h, KEdgeP); add(total - pr.shr - h, KEdgeM);
    if (pr.oneway) { for (let i = 1; i < pr.lanes; i++) add(pr.shl + LANE * i, KWhiteDash); }
    else {
      const medStart = pr.shl + nB * LANE;
      for (let i = 1; i < nB; i++) add(pr.shl + LANE * i, KWhiteDash);
      let farStart = medStart;
      if (centre === 2) { add(medStart - h * 2, KYellow); add(medStart + h * 2, KYellowDash); add(medStart + LANE - h * 2, KYellowDash); add(medStart + LANE + h * 2, KYellow); farStart = medStart + LANE; }
      else if (centre === 1) { add(medStart - h * 2, KYellow); add(medStart + h * 2, KYellow); }
      for (let i = 1; i < nF; i++) add(farStart + LANE * i, KWhiteDash);
    }
  }
  const idx = m.map((_, i) => i).sort((a, b) => m[a] - m[b]);
  return { W: pr.W, twoWay: !pr.oneway, m: idx.map(i => m[i]), kind: idx.map(i => kind[i]) };
}
const profLayouts = PROFILES.map(pr => { const [f, b, c] = defaultSplit(pr); const l = linesFor(pr, f, b, c, true); l.isDefault = true; return l; });
const sameLines = (a, b) => a.m.length === b.m.length && a.m.every((v, i) => a.kind[i] === b.kind[i] && Math.abs(v - b.m[i]) <= 1e-4);
const layCache = new Map();
export function layoutOf(e) {
  const tex = profLayouts[e.pi];
  if (!e.hasLset) return tex;
  const pr = PROFILES[e.pi]; const [dF, dB, dC] = defaultSplit(pr); const marked = (e.lsFlags & 1) !== 0;
  if (marked && e.nF === dF && e.nB === dB && e.centre === dC) return tex;
  const key = (e.pi << 24) | (e.nF << 16) | (e.nB << 8) | (e.centre << 1) | (marked ? 1 : 0);
  if (layCache.has(key)) return layCache.get(key);
  let l = linesFor(pr, e.nF, e.nB, e.centre, marked);
  if (sameLines(l, tex)) l = tex;
  layCache.set(key, l); return l;
}
const flipCache = new WeakMap();
function flipped(l) {
  if (flipCache.has(l)) return flipCache.get(l);
  const n = l.m.length, f = { W: l.W, twoWay: l.twoWay, m: new Array(n), kind: new Array(n) };
  for (let j = 0; j < n; j++) { const i = n - 1 - j; f.m[j] = l.W - l.m[i]; f.kind[j] = l.kind[i] === KEdgeP ? KEdgeM : l.kind[i] === KEdgeM ? KEdgeP : l.kind[i]; }
  flipCache.set(l, f); return f;
}
export const smooth = t => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
const ClassMph = [25, 35, 40, 45, 55, 65];
export const speedMph = e => e.speedKmh > 0 ? e.speedKmh / 1.609344 : e.link ? 35 : ClassMph[clamp(e.cls, 0, 5)];
export const shiftRate = e => { const mph = speedMph(e); return Math.max(10, mph <= 40 ? mph * mph / 60 : mph); };
export const floorOf = w => (w.cls >= 5 || (w.cls === 4 && w.oneway && !w.link)) ? 90 : (w.link || w.cls >= 2) ? 30 : 15;
const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
export const outD = (e, node) => { if (e.a === node) return tangentAt(e, 0); const t = tangentAt(e, e.length); return [-t[0], -t[1]]; };

// ---------------------------------------------------------------- the model
export function buildModel() {
  const map = load();
  const E = map.edges, NE = E.length, NN = map.nodes.length;
  for (const e of E) {
    e.pi = indexFor(e.cls, e.link, e.oneway, e.lanes, e.turn);
    const pr = PROFILES[e.pi]; e.W = pr.W; e.shl = pr.shl; e.shr = pr.shr; e.width = pr.W;
    e.hasLset = false;
    if (e.lset) {
      const { nF, nB, centre, flags } = e.lset;
      const fits = pr.oneway ? (nB === 0 && centre === 0 && nF === pr.lanes) : (nF >= 1 && nB >= 1 && centre <= 2 && nF + nB + (centre === 2 ? 1 : 0) === pr.lanes);
      e.lsFlags = flags; e.hasLset = true;
      if (fits) { e.nF = nF; e.nB = nB; e.centre = centre; } else { const d = defaultSplit(pr); e.nF = d[0]; e.nB = d[1]; e.centre = d[2]; }
    }
    const off = map.taprOff ? map.taprOff[e.index] || 0 : 0;
    const lh = (e.W - e.shl - e.shr) * 0.5;
    e.lmOff = off; e.lmPlus = lh + e.shl + off; e.lmMinus = lh + e.shr - off; e.ease = [];
  }
  // TAPR index (non-SHIFT records)
  const taprAt = new Map(); const TK = (edge, end, side) => edge * 4 + end * 2 + side;
  const tapr = map.tapr || [];
  for (let i = 0; i < tapr.length; i++) if ((tapr[i].flags & 16) === 0) taprAt.set(TK(tapr[i].edge, tapr[i].end, tapr[i].side), i);
  let mirroredFixed = 0;
  for (const t of tapr) {
    if ((t.flags & 16) !== 0 || t.edge < 0 || t.edge >= NE) continue;
    const W = E[t.edge]; const n = t.end === 0 ? W.a : W.b; const dW = outD(W, n);
    let N = null, bd = -0.85;
    for (const oi of map.nodeEdges[n]) { const o = E[oi]; if (o === W || o.a === o.b) continue; const d = dot(dW, outD(o, n)); if (d < bd) { bd = d; N = o; } }
    if (!N) continue;
    const same = (N.b === n) === (W.a === n);
    const nPlus = same ? N.lmPlus : N.lmMinus, nMinus = same ? N.lmMinus : N.lmPlus;
    const dPlus = W.lmPlus - nPlus, dMinus = W.lmMinus - nMinus;
    const moving = t.side === 0 ? dPlus : dMinus, fixd = t.side === 0 ? dMinus : dPlus;
    if (!(Math.abs(moving) <= 0.05 && Math.abs(fixd - t.dw) <= 0.3)) continue;
    const deltaW = t.side === 0 ? -fixd : fixd;
    const run = []; let cur = N, at = n, sign = same ? 1 : -1, bad = false;
    for (let g = 0; g < 64; g++) {
      run.push([cur, sign]);
      const far = cur.a === at ? cur.b : cur.a;
      if (map.nodeEdges[far].length !== 2) break;
      const nxI = map.nodeEdges[far][0] === cur.index ? map.nodeEdges[far][1] : map.nodeEdges[far][0];
      const nx = E[nxI]; if (nx === cur || nx.a === nx.b) break;
      if (nx.pi !== cur.pi || Math.abs(nx.lmOff * ((nx.a === far) === (cur.b === far) ? 1 : -1) - cur.lmOff) > 0.05) { bad = true; break; }
      sign *= ((nx.a === far) === (cur.b === far)) ? 1 : -1; cur = nx; at = far;
    }
    if (bad) continue;
    for (const [e, sg] of run) { const d = deltaW * sg; e.lmOff += d; e.lmPlus += d; e.lmMinus -= d; }
    mirroredFixed++;
  }
  // ---- the junction decision (mitre + through pair)
  const mitre = new Uint8Array(NN), thA = new Int32Array(NN).fill(-1), thB = new Int32Array(NN).fill(-1), kindOf = new Uint8Array(NN);
  // kindOf: 0 none/dead end, 1 mitred 2-arm, 2 mitred through w/ branches, 3 fan (patch), 4 2-arm bend fan
  const joins = [];
  for (let n = 0; n < NN; n++) {
    const list = map.nodeEdges[n]; if (list.length < 2) continue;
    const arms = [];
    for (const ei of list) { const e = E[ei]; if (e.a === e.b) continue; arms.push({ e, dir: outD(e, n), hw: e.W * 0.5 }); }
    if (arms.length < 2) continue;
    let tA = -1, tB = -1, best = 1;
    for (let pass = 0; pass < 2 && tA < 0; pass++)
      for (let i = 0; i < arms.length; i++) for (let j = i + 1; j < arms.length; j++) {
        if (pass === 0 && (arms[i].e.link || arms[j].e.link)) continue;
        const d = dot(arms[i].dir, arms[j].dir); if (d < best) { best = d; tA = i; tB = j; }
      }
    const through = tA >= 0 && best < -0.85;
    if (arms.length === 2) {
      if (best < -0.906) { mitre[n] = 1; thA[n] = arms[0].e.index; thB[n] = arms[1].e.index; joins.push([n, arms[0].e.index, arms[1].e.index]); kindOf[n] = 1; continue; }
    }
    const clipped = arms.map(() => -1), clipFirst = arms.map(() => -1);
    for (let i = 0; i < arms.length; i++) for (let j = i + 1; j < arms.length; j++) {
      if (dot(arms[i].dir, arms[j].dir) < 0.5) continue;
      const ai = arms[i], aj = arms[j]; let iClips;
      if (ai.e.link !== aj.e.link) iClips = ai.e.link; else if (ai.e.cls !== aj.e.cls) iClips = ai.e.cls < aj.e.cls;
      else if (Math.abs(ai.hw - aj.hw) > 0.05) iClips = ai.hw < aj.hw; else iClips = true;
      const iT = i === tA || i === tB, jT = j === tA || j === tB;
      if (through && iT && jT) continue;
      if (through && jT) iClips = true;
      if (through && iT) iClips = false;
      const c = iClips ? i : j, h = iClips ? j : i;
      if (clipFirst[c] < 0) clipFirst[c] = h; if (clipped[c] < 0) clipped[c] = h;
    }
    let crossing = false, onBridge = arms.some(a => a.e.bridge);
    if (through && !onBridge) {
      const fwd = [-arms[tA].dir[0], -arms[tA].dir[1]];
      for (let i = 0; i < arms.length && !crossing; i++) {
        if (i === tA || i === tB || clipFirst[i] !== tA) continue;
        for (let j = 0; j < arms.length; j++) {
          if (j === tA || j === tB || j === i || clipFirst[j] !== tB) continue;
          const linkX = arms[i].e.link && arms[j].e.link, streetX = !arms[i].e.link && !arms[j].e.link;
          if (!linkX && !streetX) continue;
          if (dot(arms[i].dir, arms[j].dir) > -0.7) continue;
          const si = fwd[0] * arms[i].dir[1] - fwd[1] * arms[i].dir[0], sj = fwd[0] * arms[j].dir[1] - fwd[1] * arms[j].dir[0];
          if (si * sj >= 0) continue;
          crossing = true; break;
        }
      }
      if (crossing) for (let i = 0; i < arms.length; i++) clipped[i] = clipFirst[i] = -1;
    }
    let allBranch = through;
    if (through) for (let i = 0; i < arms.length && allBranch; i++) if (i !== tA && i !== tB && clipped[i] < 0) allBranch = false;
    if (allBranch) { mitre[n] = 1; thA[n] = arms[tA].e.index; thB[n] = arms[tB].e.index; joins.push([n, arms[tA].e.index, arms[tB].e.index]); kindOf[n] = 2; continue; }
    kindOf[n] = arms.length === 2 ? 4 : 3;
  }
  const partner = (e, node) => {
    if (!mitre[node]) return -1;
    const o = thA[node] === e.index ? thB[node] : thB[node] === e.index ? thA[node] : -1;
    return o === e.index ? -1 : o;
  };
  // ---- helpers
  function roomAlong(e, fromNode, side, bothSides) {
    let total = e.length, cur = e, from = fromNode, sd = side;
    for (let g = 0; g < 12; g++) {
      const far = cur.a === from ? cur.b : cur.a; const pi = partner(cur, far); if (pi < 0) break;
      const p = E[pi]; if (p === cur || p.a === p.b) break;
      const sideT = cur.a === from ? sd : -sd, pSide = p.a === far ? sideT : -sideT;
      const c1 = sd > 0 ? cur.lmPlus : cur.lmMinus, p1 = pSide > 0 ? p.lmPlus : p.lmMinus;
      if (Math.abs(c1 - p1) > 0.05) break;
      if (bothSides) { const c2 = sd > 0 ? cur.lmMinus : cur.lmPlus, p2 = pSide > 0 ? p.lmMinus : p.lmPlus; if (Math.abs(c2 - p2) > 0.05) break; }
      total += p.length; cur = p; from = far; sd = pSide;
    }
    return total;
  }
  const shiftLog = [];
  function propagateShift(e, fromNode, shift, len, d0, depth, tag) {
    if (depth === 0) len = Math.max(0.5, Math.min(len, roomAlong(e, fromNode, 1, true)));
    e.ease.push({ shift: true, fromA: e.a === fromNode, dw: shift, len, d0, tag });
    if (d0 + e.length >= len || depth >= 12) return len;
    const far = e.a === fromNode ? e.b : e.a; const pi = partner(e, far); if (pi < 0) return len;
    const p = E[pi]; if (p === e || p.a === p.b) return len;
    const ps = ((e.a === fromNode) === (p.a === far)) ? shift : -shift;
    propagateShift(p, far, ps, len, d0 + e.length, depth + 1, tag);
    return len;
  }
  function centreD(l, fixedPlus) {
    if (!l.twoWay) return Number.MAX_VALUE;
    let lo = Infinity, hi = -Infinity;
    for (let i = 0; i < l.m.length; i++) if (l.kind[i] === KYellow || l.kind[i] === KYellowDash) { const d = fixedPlus ? l.m[i] : l.W - l.m[i]; lo = Math.min(lo, d); hi = Math.max(hi, d); }
    return lo <= hi ? 0.5 * (lo + hi) : l.W * 0.5;
  }
  const find = (l, kind, nth) => { for (let i = 0; i < l.kind.length; i++) if (l.kind[i] === kind && nth-- === 0) return i; return -1; };
  const yellowMid = l => { let lo = Infinity, hi = -Infinity; for (let i = 0; i < l.m.length; i++) if (l.kind[i] === KYellow || l.kind[i] === KYellowDash) { lo = Math.min(lo, l.m[i]); hi = Math.max(hi, l.m[i]); } return lo <= hi ? 0.5 * (lo + hi) : 0.5 * l.W; };
  function pairFromCentre(lay, k, nar, nLeft) {
    const kind = lay.kind[k];
    if (kind === KYellow || kind === KYellowDash) {
      const a = [], b = []; for (let i = 0; i < lay.m.length; i++) if (lay.kind[i] === kind) a.push(i); for (let i = 0; i < nar.m.length; i++) if (nar.kind[i] === kind) b.push(i);
      const r = a.indexOf(k); return a.length !== b.length || r < 0 ? NaN : nLeft - nar.m[b[r]];
    }
    const cw = yellowMid(lay), cn = yellowMid(nar), plusSide = lay.m[k] < cw;
    const wl = [], nl = [];
    for (let i = 0; i < lay.m.length; i++) if (lay.kind[i] === kind && (lay.m[i] < cw) === plusSide) wl.push(i);
    for (let i = 0; i < nar.m.length; i++) if (nar.kind[i] === kind && (nar.m[i] < cn) === plusSide) nl.push(i);
    wl.sort((x, y) => Math.abs(lay.m[x] - cw) - Math.abs(lay.m[y] - cw)); nl.sort((x, y) => Math.abs(nar.m[x] - cn) - Math.abs(nar.m[y] - cn));
    const rank = wl.indexOf(k); return rank < 0 || rank >= nl.length ? NaN : nLeft - nar.m[nl[rank]];
  }
  function partnerLat(e, lay, k, z) {
    const narL = z.narrowLay, nar = z.narrowFlip ? flipped(narL) : narL;
    const plus = e.lmPlus, minus = e.lmMinus;
    const nLeft = z.side > 0 ? -minus + nar.W : plus; const nLat = m => nLeft - m;
    const kind = lay.kind[k];
    if (kind === KEdgeP && z.side > 0) { const j = find(nar, KEdgeP, 0); return j < 0 ? NaN : (plus - z.dw) - nar.m[j]; }
    if (kind === KEdgeM && z.side < 0) { const j = find(nar, KEdgeM, 0); return j < 0 ? NaN : -(minus - z.dw) + (nar.W - nar.m[j]); }
    if (kind === KEdgeP) { const j = find(nar, KEdgeP, 0); return j < 0 ? NaN : nLat(nar.m[j]); }
    if (kind === KEdgeM) { const j = find(nar, KEdgeM, 0); return j < 0 ? NaN : nLat(nar.m[j]); }
    if (z.twoRole) { const dwPlus = z.side > 0 ? z.dw : z.dwOther; const t2 = pairFromCentre(lay, k, nar, plus - dwPlus); return z.twoRole === 2 && !isNaN(t2) ? plus - lay.m[k] : t2; }
    const fixedPlus = z.side < 0;
    const WideD = i => fixedPlus ? lay.m[i] : lay.W - lay.m[i], NarD = i => fixedPlus ? nar.m[i] : nar.W - nar.m[i];
    const wideCentre = centreD(lay, fixedPlus), narCentre = centreD(nar, fixedPlus);
    const yellow = kind === KYellow || kind === KYellowDash;
    const wBeyond = !yellow && lay.twoWay && WideD(k) > wideCentre;
    const wl = [], nl = [];
    for (let i = 0; i < lay.m.length; i++) if (lay.kind[i] === kind && (yellow || !lay.twoWay || (WideD(i) > wideCentre) === wBeyond)) wl.push(i);
    for (let i = 0; i < nar.m.length; i++) if (nar.kind[i] === kind && (yellow || !nar.twoWay || (NarD(i) > narCentre) === wBeyond)) nl.push(i);
    if (wBeyond) { wl.sort((a, b) => WideD(b) - WideD(a)); nl.sort((a, b) => NarD(b) - NarD(a)); } else { wl.sort((a, b) => WideD(a) - WideD(b)); nl.sort((a, b) => NarD(a) - NarD(b)); }
    const rank = wl.indexOf(k); if (rank < 0 || rank >= nl.length) return NaN;
    return nLat(nar.m[nl[rank]]);
  }
  function propagate(e, fromNode, side, dw, len, d0, narrow, flip, depth, dwOther = 0, twoRole = 0, tag = 0) {
    if (depth === 0) len = Math.max(0.5, Math.min(len, roomAlong(e, fromNode, side, false)));
    const z = { side, fromA: e.a === fromNode, dw, len, d0, narrowLay: layoutOf(narrow), narrowFlip: flip, dwOther, twoRole, tag };
    const lay = layoutOf(e); z.partner = lay.m.map((_, k) => partnerLat(e, lay, k, z));
    e.ease.push(z);
    if (d0 + e.length >= len || depth >= 12) return;
    const far = e.a === fromNode ? e.b : e.a; const pi = partner(e, far); if (pi < 0) return;
    const p = E[pi]; if (p === e || p.a === p.b) return;
    const sideT = e.a === fromNode ? side : -side, pSide = p.a === far ? sideT : -sideT;
    const extE = side > 0 ? e.lmPlus : e.lmMinus, extP = pSide > 0 ? p.lmPlus : p.lmMinus;
    if (Math.abs(extE - extP) > 0.05) return;
    const pFlip = flip !== ((e.a === fromNode) !== (p.a === far));
    propagate(p, far, pSide, dw, len, d0 + e.length, narrow, pFlip, depth + 1, dwOther, twoRole, tag);
  }
  function lengthFor(w, node, side, dw) {
    const k = TK(w.index, w.a === node ? 0 : 1, side > 0 ? 0 : 1);
    if (taprAt.has(k)) { const t = tapr[taprAt.get(k)]; const tSide = t.side === 0 ? 1 : -1; if (tSide === side && t.len >= 0.5) return t.len; if (tSide === side && (t.flags & 2)) return floorOf(w); }
    const mph = speedMph(w), rate = mph <= 40 ? mph * mph / 60 : mph; return Math.max(floorOf(w), dw * rate);
  }
  const twoSided = (w, node) => { const end = w.a === node ? 0 : 1; return taprAt.has(TK(w.index, end, 0)) && taprAt.has(TK(w.index, end, 1)); };
  function needsRelay(Ee, O, n) {
    if (Ee.oneway || O.oneway || Ee.a === Ee.b || O.a === O.b) return false;
    const eIn = Ee.b === n, oOut = O.a === n;
    const eP = eIn ? Ee.lmPlus : Ee.lmMinus, eM = eIn ? Ee.lmMinus : Ee.lmPlus, oP = oOut ? O.lmPlus : O.lmMinus, oM = oOut ? O.lmMinus : O.lmPlus;
    if (Math.abs(eP - oP) > 0.02 || Math.abs(eM - oM) > 0.02) return false;
    const lE = layoutOf(Ee), lO = layoutOf(O);
    if (lE.m.length === 0 || lO.m.length === 0 || Math.abs(lE.W - lO.W) > 0.02) return false;
    const oL = (Ee.a === n) === (O.a === n) ? flipped(lO) : lO;
    if (lE.m.length !== oL.m.length) return true;
    for (let k = 0; k < lE.m.length; k++) if (Math.abs(lE.m[k] - oL.m[k]) > 0.02) return true;
    return false;
  }
  function relayPairs(a, b) {
    const r = a.m.map(() => NaN); const ca = yellowMid(a), cb = yellowMid(b);
    for (let kind = 0; kind <= KWhiteDash; kind++) {
      if (kind === KWhiteDash) {
        for (let g = 0; g < 2; g++) {
          let la = [], lb = [];
          for (let i = 0; i < a.m.length; i++) if (a.kind[i] === kind && ((a.m[i] < ca) === (g === 0))) la.push(i);
          for (let i = 0; i < b.m.length; i++) if (b.kind[i] === kind && ((b.m[i] < cb) === (g === 0))) lb.push(i);
          if (g === 1) { la.reverse(); lb.reverse(); }
          for (let q = 0; q < la.length && q < lb.length; q++) r[la[q]] = b.m[lb[q]];
        }
        continue;
      }
      const xa = [], xb = [];
      for (let i = 0; i < a.m.length; i++) if (a.kind[i] === kind) xa.push(i);
      for (let i = 0; i < b.m.length; i++) if (b.kind[i] === kind) xb.push(i);
      if (xa.length !== xb.length) continue;
      for (let q = 0; q < xa.length; q++) r[xa[q]] = b.m[xb[q]];
    }
    return r;
  }
  const relays = [];
  function addRelay(x, n, lay, partnerM, f, len) {
    if (len < 0.5 || f <= 1e-4) return;
    const plus = x.lmPlus; const z = { relay: true, fromA: x.a === n, dw: 1, len, d0: 0, partner: [], span: 0 };
    for (let k = 0; k < lay.m.length; k++) { if (isNaN(partnerM[k])) { z.partner.push(NaN); continue; } const m = lay.m[k] + f * (partnerM[k] - lay.m[k]); z.partner.push(plus - m); z.span = Math.max(z.span, Math.abs(m - lay.m[k])); }
    x.ease.push(z);
  }
  let relayNoses = null;
  function relay(Ee, O, n, relayNode) {
    const lE = layoutOf(Ee), lO = layoutOf(O); const same = (Ee.a === n) !== (O.a === n);
    const pE = relayPairs(lE, same ? lO : flipped(lO)), pO = relayPairs(lO, same ? lE : flipped(lE));
    let move = 0, ends = false;
    for (let k = 0; k < pE.length; k++) { if (isNaN(pE[k])) ends ||= lE.kind[k] === KWhiteDash; else move = Math.max(move, Math.abs(pE[k] - lE.m[k])); }
    for (let k = 0; k < pO.length; k++) if (isNaN(pO[k])) ends ||= lO.kind[k] === KWhiteDash;
    if (move < 0.005 && !ends) return;
    const mph = Math.max(speedMph(Ee), speedMph(O)), rate = Math.max(10, mph <= 40 ? mph * mph / 60 : mph);
    const L = Math.max(Math.max(floorOf(Ee), floorOf(O)), Math.max(move, ends ? LANE : 0) * rate * (ends ? 1 : 0.5));
    const room = (x, keepNose) => { const far = x.a === n ? x.b : x.a; let r = x.length * (relayNode.has(far) ? 0.45 : 0.9); if (keepNose && relayNoses && relayNoses.has(far)) r = Math.min(r, Math.max(0.25 * x.length, x.length - 30)); return r; };
    const rE = room(Ee, true), rO = room(O, true);
    let lenE = Math.min(rE, Math.max(0.5 * L, L - rO)), lenO = Math.min(rO, L - lenE);
    { const rE0 = room(Ee, false), rO0 = room(O, false); let lenE0 = Math.min(rE0, Math.max(0.5 * L, L - rO0)), lenO0 = Math.min(rO0, L - lenE0);
      if (lenE0 < 0.5) lenE0 = 0; if (lenO0 < 0.5) lenO0 = 0;
      const a = (lenE < 0.5 ? 0 : lenE) + (lenO < 0.5 ? 0 : lenO); if (a < lenE0 + lenO0 - 0.01) { lenE = lenE0; lenO = lenO0; } }
    if (lenE < 0.5) lenE = 0; if (lenO < 0.5) lenO = 0;
    if (lenE + lenO < 0.5) return;
    const fE = lenE + lenO > 1e-3 ? lenE / (lenE + lenO) : 0.5;
    relays.push({ n, e: Ee.index, o: O.index, move, ends, L, lenE, lenO, short: lenE + lenO < L - 0.5, mph });
    addRelay(Ee, n, lE, pE, fE, lenE); addRelay(O, n, lO, pO, 1 - fE, lenO);
  }
  // ---- refuge plan (the compiled list; FNV of the file matches the list's)
  const splitAt = new Map(); for (const sp of map.splt) splitAt.set(sp.node, sp);
  const REFUGE_PK = new Set(['10187662436439', '15710990378529', '17016660430719', '30472792973916', '33011118647387', '40634685603484', '41725607296980', '43937515448313', '43941810415608', '44744969306889', '46110768903913', '49211735290422', '56826712306609', '58282706220291', '60292750915287', '60833916794709', '62182536525967'].map(BigInt));
  const barriered = e => !e.link && (e.cls >= 5 || (e.cls === 4 && e.oneway));
  function refugeChain(ei, node) {
    const chain = []; let far = -1, len = 0; if (ei < 0 || ei >= NE) return null;
    let e = E[ei], from = node;
    for (let g = 0; g < 30; g++) {
      if (e.a === e.b) return null;
      chain.push(e.index); len += e.length; far = e.a === from ? e.b : e.a;
      if (splitAt.has(far)) return far !== node && len <= 150 ? { chain, far, len } : null;
      if (len > 150) return null;
      const arrives = e.b === far; let next = null, cnt = 0;
      for (const i of map.nodeEdges[far]) { if (i === e.index) continue; const o = E[i]; if (o.name !== e.name || !o.oneway || o.link || o.a === o.b || (o.a === far) !== arrives) continue; next = o; cnt++; }
      if (cnt !== 1) return null; e = next; from = far;
    }
    return null;
  }
  const refugeCands = [], plan = [];
  { const done = new Set(); const bad = i => i < 0 || i >= NE || barriered(E[i]); const nose = s => E[s.u].W - E[s.a].W - E[s.b].W;
    for (const sp of splitAt.values()) {
      if (bad(sp.u) || bad(sp.a) || bad(sp.b)) continue;
      const ca = refugeChain(sp.a, sp.node); if (!ca) continue; const cb = refugeChain(sp.b, sp.node); if (!cb) continue;
      if (ca.far !== cb.far) continue; const s2 = splitAt.get(ca.far);
      if (bad(s2.u) || bad(s2.a) || bad(s2.b)) continue;
      if (nose(sp) < -1 || nose(s2) < -1) continue;
      const pk = (BigInt(Math.min(sp.node, ca.far)) << 32n) | BigInt(Math.max(sp.node, ca.far));
      if (done.has(pk)) continue; done.add(pk);
      const c = { pk, sp, s2, ca, cb, listed: REFUGE_PK.has(pk) }; refugeCands.push(c); if (c.listed) plan.push(c);
    } }
  const refugeKey = new Set(); relayNoses = new Set();
  for (const pl of plan) { relayNoses.add(pl.sp.node); relayNoses.add(pl.s2.node);
    for (const ch of [pl.ca, pl.cb]) { refugeKey.add(pl.sp.node + ':' + ch.chain[0]); refugeKey.add(pl.s2.node + ':' + ch.chain[ch.chain.length - 1]); } }
  const refugeEdges = new Set(); for (const pl of plan) for (const ch of [pl.ca, pl.cb]) for (const ei of ch.chain) refugeEdges.add(ei);
  const refugeUEdges = new Set(); for (const pl of plan) { refugeUEdges.add(pl.sp.u); refugeUEdges.add(pl.s2.u); }
  // ---- BuildEases
  const relayJoin = new Set(), relayNode = new Set();
  for (let j = 0; j < joins.length; j++) { const [n, ei, oi] = joins[j]; const sj = splitAt.get(n); if (ei === oi || (sj && (ei === sj.u || oi === sj.u))) continue; if (needsRelay(E[ei], E[oi], n)) { relayJoin.add(j); relayNode.add(n); } }
  const joinLog = [];
  for (let jn = 0; jn < joins.length; jn++) {
    const [n, ei, oi] = joins[jn]; const Ee = E[ei], O = E[oi]; if (Ee === O) continue;
    const spj = splitAt.get(n); if (spj && (ei === spj.u || oi === spj.u)) continue;
    const eIn = Ee.b === n, oOut = O.a === n;
    const DStep = sideT => { const es = eIn ? sideT : -sideT, os = oOut ? sideT : -sideT; return (es > 0 ? Ee.lmPlus : Ee.lmMinus) - (os > 0 ? O.lmPlus : O.lmMinus); };
    const dLt = DStep(1), dRt = DStep(-1);
    if ((dLt > 0.02 && dRt > 0.02) || (dLt < -0.02 && dRt < -0.02)) {
      const eWide = dLt > 0; const W = eWide ? Ee : O, N = eWide ? O : Ee;
      const nForward = N === Ee ? eIn : oOut, wForward = W === Ee ? eIn : oOut;
      let fixT;
      if (N.oneway !== W.oneway) { const ow = N.oneway ? N : W; const owF = ow === Ee ? eIn : oOut; fixT = owF ? -1 : 1; }
      else if (twoSided(W, n)) fixT = 0;
      else {
        const k0 = TK(W.index, W.a === n ? 0 : 1, 0), k1 = TK(W.index, W.a === n ? 0 : 1, 1);
        if (taprAt.has(k0) || taprAt.has(k1)) { const tiW = taprAt.has(k0) ? taprAt.get(k0) : taprAt.get(k1); const mW = tapr[tiW].side === 0 ? 1 : -1; fixT = -(wForward ? mW : -mW); }
        else fixT = Math.abs(dRt) <= Math.abs(dLt) ? -1 : 1;
      }
      if (fixT === 0) {
        const dl2 = eWide ? dLt : -dLt, dr2 = eWide ? dRt : -dRt; let len2 = 0;
        for (let sideT = -1; sideT <= 1; sideT += 2) { const d = sideT > 0 ? dl2 : dr2; if (d <= 0.005) continue; len2 = Math.max(len2, lengthFor(W, n, sideT * (wForward ? 1 : -1), d)); }
        len2 = Math.max(0.5, Math.min(len2, roomAlong(W, n, 1, true)));
        let lead = true;
        for (let sideT = 1; sideT >= -1; sideT -= 2) { const d = sideT > 0 ? dl2 : dr2, dO = sideT > 0 ? dr2 : dl2; if (d <= 0.005) continue; const wSd = sideT * (wForward ? 1 : -1);
          propagate(W, n, wSd, d, len2, 0, N, (W.a === n) === (N.a === n), 0, Math.max(0, dO), lead ? 1 : 2, 'j' + jn); lead = false; }
        joinLog.push({ n, kind: 'twosided', W: W.index, N: N.index, len: len2 });
        continue;
      }
      const dFix = Math.abs(fixT > 0 ? dLt : dRt), dMove = Math.abs(dLt) + Math.abs(dRt);
      const shiftN = fixT * dFix * (nForward ? 1 : -1);
      const shLen0 = Math.max(floorOf(N), dFix * shiftRate(N));
      const shLen = propagateShift(N, n, shiftN, shLen0, 0, 0, 'h' + jn);
      const wSide = -fixT * (wForward ? 1 : -1);
      const len = lengthFor(W, n, wSide, dMove);
      propagate(W, n, wSide, dMove, len, 0, N, (W.a === n) === (N.a === n), 0, 0, 0, 'h' + jn);
      joinLog.push({ n, kind: 'held', W: W.index, N: N.index, shift: shiftN, shLenWant: shLen0, shLen, dMove, owSplit: N.oneway !== W.oneway });
      continue;
    }
    if ((dLt > 0.02 && dRt < -0.02) || (dLt < -0.02 && dRt > 0.02)) {
      const eWider = Ee.lmPlus + Ee.lmMinus >= O.lmPlus + O.lmMinus; const W = eWider ? Ee : O, N = eWider ? O : Ee;
      const sg = eWider ? 1 : -1, dLw = sg * dLt, dRw = sg * dRt;
      const shT = Math.abs(dLw) <= Math.abs(dRw) ? dLw : -dRw, openT = Math.abs(dLw) <= Math.abs(dRw) ? -1 : 1;
      const nForward = N === Ee ? eIn : oOut, wForward = W === Ee ? eIn : oOut;
      const shiftN = shT * (nForward ? 1 : -1);
      const shLen0 = Math.max(floorOf(N), Math.abs(shT) * shiftRate(N));
      const shLen = propagateShift(N, n, shiftN, shLen0, 0, 0, 'c' + jn);
      const rest = dLw + dRw;
      if (rest > 0.005) { const wSide = openT * (wForward ? 1 : -1); const len = lengthFor(W, n, wSide, rest); propagate(W, n, wSide, rest, len, 0, N, (W.a === n) === (N.a === n), 0, 0, 0, 'c' + jn); }
      joinLog.push({ n, kind: 'crossed', W: W.index, N: N.index, shift: shiftN, shLenWant: shLen0, shLen });
      continue;
    }
    if (relayJoin.has(jn)) { relay(Ee, O, n, relayNode); continue; }
    for (let sideT = -1; sideT <= 1; sideT += 2) {
      const eSide = eIn ? sideT : -sideT, oSide = oOut ? sideT : -sideT;
      const extE = eSide > 0 ? Ee.lmPlus : Ee.lmMinus, extO = oSide > 0 ? O.lmPlus : O.lmMinus, d = extE - extO;
      if (Math.abs(d) <= 0.005) continue;
      const W = d > 0 ? Ee : O, N = d > 0 ? O : Ee, wSide = d > 0 ? eSide : oSide;
      const len = lengthFor(W, n, wSide, Math.abs(d)); if (len < 0.5) continue;
      propagate(W, n, wSide, Math.abs(d), len, 0, N, (W.a === n) === (N.a === n), 0, 0, 0, 'w' + jn);
    }
  }
  // ---- the SPLT median tapers
  const splitTapers = [];
  for (const sp of splitAt.values()) {
    if (sp.u < 0 || sp.u >= NE) continue;
    const U = E[sp.u]; let tu = U.a === sp.node ? tangentAt(U, 0).map(v => -v) : tangentAt(U, U.length); const leftU = [-tu[1], tu[0]];
    for (let c = 0; c < 2; c++) {
      const ci = c === 0 ? sp.a : sp.b, offC = c === 0 ? sp.offA : sp.offB; if (ci < 0 || ci >= NE) continue;
      const C = E[ci]; if (C.a !== sp.node && C.b !== sp.node) continue;
      const tc = C.a === sp.node ? tangentAt(C, 0) : tangentAt(C, C.length);
      const sign = dot(leftU, [-tc[1], tc[0]]) >= 0 ? 1 : -1;
      const out1 = Math.sign(offC * sign); if (out1 === 0) continue;
      const outIsLeftU = out1 * sign > 0, uPlusIsLeftU = U.b === sp.node;
      const uOut = outIsLeftU === uPlusIsLeftU ? U.lmPlus : U.lmMinus, cOut = out1 > 0 ? C.lmPlus : C.lmMinus;
      const shift0 = out1 * (uOut - cOut);
      const rec = { node: sp.node, u: sp.u, c: ci, which: c === 0 ? 'a' : 'b', shift0, rate: sp.rate, floor: floorOf(C), refuge: false, lenRule: 0, lenDrawn: 0, room: roomAlong(C, sp.node, 1, true) };
      if (Math.abs(shift0) < 0.02) { rec.lenRule = 0; splitTapers.push(rec); continue; }
      rec.lenRule = Math.max(floorOf(C), Math.max(10, sp.rate) * Math.abs(shift0));
      if (refugeKey.has(sp.node + ':' + ci)) { rec.refuge = true; splitTapers.push(rec); continue; }
      rec.lenDrawn = propagateShift(C, sp.node, shift0, rec.lenRule, 0, 0, 's' + sp.node);
      splitTapers.push(rec);
    }
  }
  // ---- SHIFT records
  let reShifts = 0;
  for (const t of tapr) {
    if ((t.flags & 16) === 0 || t.edge < 0 || t.edge >= NE) continue; const e = E[t.edge]; if (e.a === e.b) continue;
    const node = t.end === 0 ? e.a : e.b; if (partner(e, node) >= 0) continue;
    propagateShift(e, node, t.off, Math.max(0.5, t.len), 0, 0, 'r' + t.edge); reShifts++;
  }
  return { map, E, partner, mitre, kindOf, thA, thB, joins, splitAt, splitTapers, relays, joinLog, refugeCands, plan, refugeEdges, refugeUEdges, roomAlong, mirroredFixed, reShifts };
}

// ---------------------------------------------------------------- evaluation
export function reduction(e, z, s) { const d = z.d0 + (z.fromA ? s : e.length - s); if (d >= z.len) return 0; return z.dw * (1 - smooth(Math.max(0, d) / z.len)); }
export function shiftAt(e, s) { let sh = 0; for (const z of e.ease) if (z.shift) sh += reduction(e, z, s); return sh; }
export function ext(e, s) {
  let eP = e.lmPlus, eM = e.lmMinus, rp = 0, rm = 0, sh = 0;
  for (const z of e.ease) { if (z.relay) continue; const r = reduction(e, z, s); if (z.shift) { sh += r; continue; } if (z.side > 0) { if (r > rp) rp = r; } else if (r > rm) rm = r; }
  return [eM - rm - sh, eP - rp + sh];   // [eMinus, ePlus]
}
export function laneCentre(e, s) { const [eM, eP] = ext(e, s); return 0.5 * ((eP - e.shl) - (eM - e.shr)); }
/// the yellow lines' lateral (+ left of a->b) at s: the mean of the drawn yellow lines (NaN: none drawn)
export function yellowLat(e, s) {
  const lay = layoutOf(e); const n = lay.m.length; if (!n) return NaN;
  let zp = null, zm = null, rp = 0, rm = 0;
  for (const z of e.ease) { if (z.shift || z.relay) continue; const r = reduction(e, z, s); if (r <= 1e-4) continue; if (z.side > 0) { if (r > rp) { rp = r; zp = z; } } else if (r > rm) { rm = r; zm = z; } }
  const plus0 = e.lmPlus, sh = shiftAt(e, s);
  let sum = 0, cnt = 0;
  for (let k = n - 1; k >= 0; k--) {
    if (lay.kind[k] !== KYellow && lay.kind[k] !== KYellowDash) continue;
    const full = plus0 - lay.m[k]; let lat = full, ok = true;
    for (const z of e.ease) { if (!z.relay || !ok) continue; const r = reduction(e, z, s); if (r > 1e-4) { const tg = z.partner[k]; if (isNaN(tg)) ok = false; else lat += (tg - full) * (r / z.dw); } }
    const from = lat;
    if (ok && zp && !(zp.twoRole === 2)) { const tg = zp.partner[k]; if (isNaN(tg)) ok = false; else lat += (tg - from) * (rp / zp.dw); }
    if (ok && zm && !(zm.twoRole === 2)) { const tg = zm.partner[k]; if (isNaN(tg)) ok = false; else lat += (tg - from) * (rm / zm.dw); }
    if (ok) { sum += lat + sh; cnt++; }
  }
  return cnt ? sum / cnt : NaN;
}
/// a lateral offset point: the OSM line at s plus left-normal * lat (bisector at a vertex, as OwnLanePoint)
export function offsetPoint(e, s, lat) {
  const p = pointAt(e, s);
  const t1 = tangentAt(e, Math.max(0, s - 0.01)), t2 = tangentAt(e, Math.min(e.length, s + 0.01));
  let tx = t1[0] + t2[0], tz = t1[1] + t2[1]; const d = Math.hypot(tx, tz);
  if (d > 1e-6) { tx /= d; tz /= d; } else { tx = t1[0]; tz = t1[1]; }
  return [p[0] - tz * lat, p[1] + tx * lat];
}
