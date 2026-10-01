// linesim.mjs - an offline replica of how CityMeshes cuts a Charlotte ribbon
// into cross-sections and maps its painted texture: the PLAN-VIEW half of a
// tile build, city-wide, in node. Used by linecheck.mjs (the smoothness gate's
// offline implementation, plan amendment A4 WP-G) and nothing in the game.
//
// Promoted from the 2026-09-28 line-wobble census replica (sim.mjs); every
// function cites the C# it replicates (Assets/PSXRacing/Scripts/City/
// CityMeshes.cs unless another file is named). Deliberate approximations are
// marked APPROX; they are the ones the census listed:
//   * no elevation solve: heights are never compared (AttachDy in
//     CutAgainstHost/SampleBranch, the squeeze's height band, ArmsApart);
//     vertical separation is taken from the XING table and tunnel flags;
//   * stElev (structure) is rebuilt from bridge tags, water spans and the
//     crossings' DeckReach only (it only moves sample positions: StructureEnds);
//   * clips are computed city-wide once (a tile computes them for branch nodes
//     within GoreReach + 20 m of itself, which covers every span it draws);
//   * MeetPavement is not run: it moves section HEIGHTS only.
//
// MODELS (what the builder is assumed to do; the gate's plan never reads them):
//   asbuilt   today's CityMeshes: linear taper, U against the TAPERED half width
//   nominalU  the census's variant: U against the profile's own half width
//   m0        the M0 stopgap (plan A4): nominalU + smoothstep taper in
//             HalfWidthAt + a section every 2 m inside a taper
//   dense2    the census's variant: a section every 2 m inside a taper only
import { classOf } from './citydata.mjs';

export const LANE = 3.6576;
export const RoadVTile = 12.192;           // CityMeshes.cs:68
export const MODELS = {
  asbuilt: { nominalU: false, dense: 0, taperShape: 'linear' },
  nominalU: { nominalU: true, dense: 0, taperShape: 'linear' },
  m0: { nominalU: true, dense: 2, taperShape: 'smooth' },
  dense2: { nominalU: false, dense: 2, taperShape: 'linear' },
};

// ------------------------------------------------------------ constants
const StationStep = 10;                    // CityElevation.cs:38
const ThroughCos = -0.85, BranchCos = 0.5, ContinueCos = -0.906;   // CityMeshes.cs:513-520
const TaperPerLane = 30, TaperMin = 25, TaperMax = 80;             // :521-522
const GoreStep = 6, GoreReach = 420, GoreMaxGap = 4.5;             // :937-948
const ClipLift = 0;                        // :965
export const ApproachRailM = 20;           // :140
const CorridorBlend = 26;                  // CityElevation.cs:54

// ------------------------------------------------------------ vector helpers
export const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
export const clamp01 = v => v < 0 ? 0 : v > 1 ? 1 : v;
const lerpC = (a, b, t) => a + (b - a) * clamp01(t);          // Mathf.Lerp clamps t
export const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
export const sub = (a, b) => [a[0] - b[0], a[1] - b[1]];
export const add = (a, b) => [a[0] + b[0], a[1] + b[1]];
export const mul = (a, k) => [a[0] * k, a[1] * k];
export const len = a => Math.hypot(a[0], a[1]);
export const dist = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1]);
export const normd = a => { const m = len(a); return m > 1e-5 ? [a[0] / m, a[1] / m] : [0, 0]; };   // Vector2.normalized
/// The M0 taper shape: 3t^2 - 2t^3 (Mathf.SmoothStep(0, 1, t)).
export const smooth01 = t => { t = clamp01(t); return t * t * (3 - 2 * t); };

// ------------------------------------------------------------ Edge accessors (CityMap.cs:101-127, 185-197)
export function segmentAt(e, at) {
  let lo = 0, hi = e.s.length - 2;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (e.s[mid] <= at) lo = mid; else hi = mid - 1; }
  const seg = e.s[lo + 1] - e.s[lo];
  return [lo, seg > 1e-6 ? (at - e.s[lo]) / seg : 0];
}
export function pointAt(e, at) {
  at = clamp(at, 0, e.length);
  const [i, t] = segmentAt(e, at);
  const a = e.pts[i], b = e.pts[i + 1];
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
}
export function tangentAt(e, at) {
  const [i] = segmentAt(e, clamp(at, 0, e.length));
  const a = e.pts[i], b = e.pts[i + 1];
  const dx = b[0] - a[0], dz = b[1] - a[1], m = Math.hypot(dx, dz);
  return m > 1e-5 ? [dx / m, dz / m] : [0, 1];
}
export function outDir(e, node) {   // CityMeshes.cs:510
  if (e.a === node) return tangentAt(e, 0);
  const t = tangentAt(e, e.length); return [-t[0], -t[1]];
}
// CityElevation.ProjectOn (CityElevation.cs:1254)
export function projectOn(e, p) {
  let best = Infinity, arcS = 0;
  for (let i = 0; i + 1 < e.pts.length; i++) {
    const a = e.pts[i], dx = e.pts[i + 1][0] - a[0], dz = e.pts[i + 1][1] - a[1];
    const L2 = dx * dx + dz * dz;
    const t = L2 > 1e-8 ? clamp01(((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L2) : 0;
    const qx = a[0] + dx * t, qz = a[1] + dz * t;
    const dd = (p[0] - qx) ** 2 + (p[1] - qz) ** 2;
    if (dd < best) { best = dd; arcS = e.s[i] + Math.sqrt(L2) * t; }
  }
  return arcS;
}

/// Build the replica for one parsed city (citydata.parseCity) and one model.
export function createSim(city, modelName = 'asbuilt') {
  const model = MODELS[modelName];
  if (!model) throw new Error(`linesim: unknown model '${modelName}' (${Object.keys(MODELS).join(', ')})`);
  const E = city.edges;
  const NODES = city.nodes.map(n => [n.x, n.z]);
  const nodeEdges = city.nodeEdges;
  for (const e of E) { e.cls = e.rank; e.klass = classOf(e); }

  function elevatedAt(e, at) {
    const S = e.stS; if (!S || !S.length) return false;
    at = clamp(at, 0, e.length);
    let lo = 0, hi = S.length - 2;
    if (hi < 0) return e.stElev[0];
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (S[mid] <= at) lo = mid; else hi = mid - 1; }
    return e.stElev[lo] || e.stElev[Math.min(lo + 1, e.stElev.length - 1)];
  }

  // ---------------------------------------------------------- stations (CityElevation.cs:255-266) + APPROX structure
  for (const e of E) {
    const n = Math.max(2, Math.ceil(e.length / StationStep) + 1);
    e.stS = new Float64Array(n); e.stElev = new Array(n).fill(false);
    for (let i = 0; i < n; i++) e.stS[i] = i === n - 1 ? e.length : i * e.length / (n - 1);
    if (e.bridge) e.stElev.fill(true);                                  // :272
  }
  for (const ws of city.wspans) {                                       // HoldWaterSpans, CityElevation.cs:990-1007
    const e = E[ws.edge];
    const s0 = clamp(ws.s0, 0, e.length), s1 = clamp(ws.s1, 0, e.length);
    if (s1 - s0 < 2) continue;
    for (let i = 0; i < e.stS.length; i++) if (e.stS[i] >= s0 && e.stS[i] <= s1) e.stElev[i] = true;
  }
  for (const c of city.crossings) {                                     // MarkStructure, CityElevation.cs:535-548 (all crossings on)
    const over = E[c.over], under = E[c.under];
    const at = [c.x, c.z];
    const sO = projectOn(over, at), sU = projectOn(under, at);
    const tO = tangentAt(over, sO), tU = tangentAt(under, sU);
    const cs = dot(tO, tU);
    const sin = Math.max(0.4, Math.sqrt(Math.max(0, 1 - cs * cs)));
    const reach = ((under.width * 0.5 + 6.5) + CorridorBlend * 0.85 + (over.width * 0.5 + 6.5)) / sin;   // DeckReach :870
    for (let i = 0; i < over.stS.length; i++) if (Math.abs(over.stS[i] - sO) <= reach) over.stElev[i] = true;
  }
  // APPROX vertical separation: a pair the XING table puts over/under, or a tunnel beside a surface road
  const sepPairs = new Set();
  const pk = (a, b) => a < b ? a * 65536 + b : b * 65536 + a;
  for (const c of city.crossings) sepPairs.add(pk(c.over, c.under));
  const separated = (i, j) => sepPairs.has(pk(i, j)) || (E[i].tunnel !== E[j].tunnel);

  // ---------------------------------------------------------- ComputeTrims (CityMeshes.cs:528-698), a straight port
  const T = (() => {
    const ne = E.length, nn = NODES.length;
    const t = {
      atA: new Float64Array(ne), atB: new Float64Array(ne), hwA: new Float64Array(ne), hwB: new Float64Array(ne),
      taperA: new Float64Array(ne), taperB: new Float64Array(ne), branchA: new Int32Array(ne).fill(-1), branchB: new Int32Array(ne).fill(-1),
      patch: new Uint8Array(nn), mitre: new Uint8Array(nn), throughA: new Int32Array(nn).fill(-1), throughB: new Int32Array(nn).fill(-1),
    };
    for (let i = 0; i < ne; i++) t.hwA[i] = t.hwB[i] = E[i].width * 0.5;
    for (let n = 0; n < nn; n++) {
      const list = nodeEdges[n];
      if (list.length < 2) continue;
      const arms = [];
      for (const ei of list) { const e = E[ei]; if (e.a === e.b) continue; arms.push({ e, dir: outDir(e, n), hw: e.width * 0.5 }); }
      if (arms.length < 2) continue;
      let tA = -1, tB = -1, best = 1;
      for (let pass = 0; pass < 2 && tA < 0; pass++)
        for (let i = 0; i < arms.length; i++)
          for (let j = i + 1; j < arms.length; j++) {
            if (pass === 0 && (arms[i].e.link || arms[j].e.link)) continue;
            const d = dot(arms[i].dir, arms[j].dir);
            if (d < best) { best = d; tA = i; tB = j; }
          }
      const through = tA >= 0 && best < ThroughCos;
      const taper = (a0, a1) => {
        if (Math.abs(a0.hw - a1.hw) <= 0.05) return;
        const wide = a0.hw > a1.hw ? a0 : a1, narrow = a0.hw > a1.hw ? a1 : a0;
        const dropped = (wide.hw - narrow.hw) * 2 / LANE;
        let L = clamp(dropped * TaperPerLane, TaperMin, TaperMax);
        L = Math.min(L, wide.e.length * 0.9);
        if (wide.e.a === n) { t.hwA[wide.e.index] = narrow.hw; t.taperA[wide.e.index] = L; }
        else { t.hwB[wide.e.index] = narrow.hw; t.taperB[wide.e.index] = L; }
      };
      if (arms.length === 2 && best < ContinueCos) {
        t.mitre[n] = 1; t.throughA[n] = arms[0].e.index; t.throughB[n] = arms[1].e.index;
        taper(arms[0], arms[1]);
        continue;
      }
      const clipped = new Array(arms.length).fill(-1);
      for (let i = 0; i < arms.length; i++)
        for (let j = i + 1; j < arms.length; j++) {
          if (dot(arms[i].dir, arms[j].dir) < BranchCos) continue;
          const ai = arms[i], aj = arms[j];
          let iClips;
          if (ai.e.link !== aj.e.link) iClips = ai.e.link;
          else if (ai.e.cls !== aj.e.cls) iClips = ai.e.cls < aj.e.cls;
          else if (Math.abs(ai.hw - aj.hw) > 0.05) iClips = ai.hw < aj.hw;
          else iClips = true;
          if (through && (i === tA || i === tB) && (j === tA || j === tB)) continue;
          if (through && (j === tA || j === tB)) iClips = true;
          if (through && (i === tA || i === tB)) iClips = false;
          const c = iClips ? i : j, h = iClips ? j : i;
          if (clipped[c] < 0) clipped[c] = h;
        }
      let allBranch = through;
      if (through) for (let i = 0; i < arms.length && allBranch; i++) if (i !== tA && i !== tB && clipped[i] < 0) allBranch = false;
      if (allBranch) {
        t.mitre[n] = 1; t.throughA[n] = arms[tA].e.index; t.throughB[n] = arms[tB].e.index;
        taper(arms[tA], arms[tB]);
        for (let i = 0; i < arms.length; i++) {
          if (clipped[i] < 0) continue;
          const e = arms[i].e;
          if (e.a === n) t.branchA[e.index] = arms[clipped[i]].e.index; else t.branchB[e.index] = arms[clipped[i]].e.index;
        }
        continue;
      }
      t.patch[n] = 1;
      let trimN = 0;
      for (let i = 0; i < arms.length; i++)
        for (let j = 0; j < arms.length; j++) {
          if (j === i) continue;
          const d = dot(arms[i].dir, arms[j].dir);
          if (d < ThroughCos) continue;
          if (clipped[i] === j || clipped[j] === i) continue;
          const sin = Math.max(0.5, Math.sqrt(Math.max(0, 1 - d * d)));
          trimN = Math.max(trimN, (arms[j].hw + arms[i].hw * Math.abs(d) + 0.6) / sin);
        }
      for (let i = 0; i < arms.length; i++) {
        const e = arms[i].e;
        const trim = Math.min(trimN, e.length * 0.49);
        if (e.a === n) t.atA[e.index] = trim; else t.atB[e.index] = trim;
        if (clipped[i] >= 0) {
          if (e.a === n) t.branchA[e.index] = arms[clipped[i]].e.index; else t.branchB[e.index] = arms[clipped[i]].e.index;
        }
      }
    }
    for (let i = 0; i < ne; i++) {
      const e = E[i], sum = t.atA[i] + t.atB[i];
      if (sum > e.length - 0.6 && sum > 0) { const k = e.length / sum; t.atA[i] *= k; t.atB[i] *= k; }
    }
    return t;
  })();

  // CHAIN CONTINUITY (CityMeshes.ChainContinuity, WP-11): texture V = (vOff + vDir s) / RoadVTile along each
  // chain of mitred through joints, so the dash phase runs on through way splits, decks and seams
  const chainV = (() => {
    const ne = E.length, vOff = new Float64Array(ne), vDir = new Float64Array(ne).fill(1), seen = new Uint8Array(ne);
    const rep = (t, L) => t - Math.floor(t / L) * L;
    const partner = (e, node) => {
      if (!T.mitre[node]) return -1;
      const o = T.throughA[node] === e.index ? T.throughB[node] : T.throughB[node] === e.index ? T.throughA[node] : -1;
      return o < 0 || o === e.index || E[o].a === E[o].b ? -1 : o;
    };
    // the phase must not depend on where the walk began: an open chain starts at its end with the lower (x, z),
    // a ring at its joint with the lower (x, z) (float32 points, as C# compares them)
    const lower = (p, q) => p[0] < q[0] || (p[0] === q[0] && p[1] < q[1]);
    const P2 = (e, k) => [Math.fround(e.pts[k][0]), Math.fround(e.pts[k][1])];
    const entryPt = (ei, f) => { const e = E[ei]; return f ? P2(e, 0) : P2(e, e.pts.length - 1); };
    const exitPt = (ei, f) => { const e = E[ei]; return f ? P2(e, e.pts.length - 1) : P2(e, 0); };
    for (let i = 0; i < ne; i++) {
      if (seen[i] || E[i].a === E[i].b) continue;
      let cur = i, entry = E[i].a, guard = 0, ring = false;
      for (;;) { const q = partner(E[cur], entry); if (q === i) { ring = true; break; } if (q < 0 || seen[q] || ++guard > ne) break; entry = E[q].a === entry ? E[q].b : E[q].a; cur = q; }
      if (ring) { cur = i; entry = E[i].a; }
      let chain = [], fwds = []; guard = 0;
      while (cur >= 0 && !seen[cur] && ++guard <= ne) {
        const e = E[cur]; seen[cur] = 1;
        const fwd = entry === e.a; chain.push(cur); fwds.push(fwd);
        const exit = fwd ? e.b : e.a; cur = partner(e, exit); entry = exit;
      }
      const n = chain.length;
      if (ring) {
        let best = 0;
        for (let k = 1; k < n; k++) if (lower(entryPt(chain[k], fwds[k]), entryPt(chain[best], fwds[best]))) best = k;
        chain = chain.map((_, k) => chain[(best + k) % n]); fwds = fwds.map((_, k) => fwds[(best + k) % n]);
      } else if (lower(exitPt(chain[n - 1], fwds[n - 1]), entryPt(chain[0], fwds[0]))) {
        chain.reverse(); fwds = fwds.reverse().map(f => !f);
      }
      let acc = 0;
      for (let k = 0; k < n; k++) {
        const ci = chain[k], e = E[ci], fwd = fwds[k];
        vDir[ci] = fwd ? 1 : -1; vOff[ci] = rep(fwd ? acc : acc + e.length, RoadVTile);
        acc = rep(acc + e.length, RoadVTile);
      }
    }
    return { vOff, vDir };
  })();

  // Trims.HalfWidthAt (CityMeshes.cs:498-506); M0 eases the taper (smoothstep)
  const shapeOf = model.taperShape === 'smooth' ? smooth01 : clamp01;
  function halfWidthAt(e, s) {
    const hw = e.width * 0.5; let ha = hw, hb = hw; const i = e.index;
    if (T.taperA[i] > 0 && s < T.taperA[i]) ha = T.hwA[i] + (hw - T.hwA[i]) * shapeOf(s / T.taperA[i]);
    if (T.taperB[i] > 0 && e.length - s < T.taperB[i]) hb = T.hwB[i] + (hw - T.hwB[i]) * shapeOf((e.length - s) / T.taperB[i]);
    return Math.min(ha, hb);
  }

  // ---------------------------------------------------------- segment grid (CityMap.cs:270, 636-650, 712-720)
  const CELL = 64;
  const cellKey = (cx, cz) => (cx + 4096) * 8192 + (cz + 4096);
  const segCells = new Map();
  const segOf = [];          // global seg id -> [edge, seg]
  for (const e of E) {
    for (let i = 0; i + 1 < e.pts.length; i++) {
      const id = segOf.length; segOf.push([e.index, i]);
      const a = e.pts[i], b = e.pts[i + 1];
      const x0 = Math.floor(Math.min(a[0], b[0]) / CELL), x1 = Math.floor(Math.max(a[0], b[0]) / CELL);
      const z0 = Math.floor(Math.min(a[1], b[1]) / CELL), z1 = Math.floor(Math.max(a[1], b[1]) / CELL);
      for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) {
        const k = cellKey(cx, cz); let l = segCells.get(k); if (!l) segCells.set(k, l = []); l.push(id);
      }
    }
  }
  const segStamp = new Int32Array(segOf.length); let stampNo = 0;
  function segsInRect(minx, minz, maxx, maxz) {
    stampNo++; const out = [];
    const x0 = Math.floor(minx / CELL), x1 = Math.floor(maxx / CELL), z0 = Math.floor(minz / CELL), z1 = Math.floor(maxz / CELL);
    for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) {
      const l = segCells.get(cellKey(cx, cz)); if (!l) continue;
      for (const id of l) if (segStamp[id] !== stampNo) { segStamp[id] = stampNo; out.push(id); }
    }
    return out;
  }

  // ---------------------------------------------------------- clip pairs, clips, gore gaps (filled below)
  const clipPairs = new Set();
  const clips = new Map();      // edge -> [{host, sFrom, sTo, side, innerSide}]
  const goreGaps = [];          // {edge, side, s0, s1}
  const goreByEdge = new Map();
  function clipAt(e, s) {              // :1120
    const list = clips.get(e.index); if (!list) return null;
    for (const c of list) if (s >= c.sFrom && s <= c.sTo) return c;
    return null;
  }
  const sharesNode = (e, o) => o.a === e.a || o.a === e.b || o.b === e.a || o.b === e.b;

  // Squeeze (CityMeshes.cs:3440-3497). APPROX: ArmsApart is false (no heights), the height band is `separated`.
  function squeeze(e, p, right, hw, hwL, hwR) {
    const r = { hwL, hwR, nbL: -1, nbR: -1, nbAtL: 0, nbAtR: 0, stripL: -1, stripR: -1 };
    const tan = [right[1], -right[0]];
    let dL = Infinity, dR = Infinity, eL = -1, eR = -1, atL = 0, atR = 0;
    for (const id of segsInRect(p[0] - 32, p[1] - 32, p[0] + 32, p[1] + 32)) {
      const [oi, si] = segOf[id];
      if (oi === e.index) continue;
      const o = E[oi];
      const a = o.pts[si], dx = o.pts[si + 1][0] - a[0], dz = o.pts[si + 1][1] - a[1];
      const L2 = dx * dx + dz * dz;
      if (L2 < 1e-6) continue;
      const t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L2;
      if (t <= 0 || t >= 1) continue;
      const L = Math.sqrt(L2);
      if (Math.abs((tan[0] * dx + tan[1] * dz) / L) < 0.9) continue;
      const q = [a[0] + dx * t, a[1] + dz * t];
      const at = o.s[si] + L * t;
      if (sharesNode(e, o)) continue;                 // APPROX: !ArmsApart (:3465)
      if (separated(e.index, oi)) continue;           // APPROX: height band (:3472)
      const d = dist(p, q);
      if (d >= hw + halfWidthAt(o, at) + 0.3) continue;
      const side = (q[0] - p[0]) * right[0] + (q[1] - p[1]) * right[1] >= 0 ? 1 : -1;
      if (side > 0) { if (d < dR) { dR = d; eR = oi; atR = at; } }
      else { if (d < dL) { dL = d; eL = oi; atL = at; } }
    }
    for (let side = -1; side <= 1; side += 2) {
      const oi = side > 0 ? eR : eL;
      if (oi < 0) continue;
      if (clipPairs.has(pk(e.index, oi))) continue;
      const o = E[oi];
      const at = side > 0 ? atR : atL, d = side > 0 ? dR : dL;
      const hwO = halfWidthAt(o, at);
      const mine = Math.max(1.2, d * hw / (hw + hwO) - 0.15);
      const theirs = Math.max(1.2, d * hwO / (hw + hwO) - 0.15);
      if (side > 0) { if (mine < r.hwR) r.hwR = mine; } else { if (mine < r.hwL) r.hwL = mine; }
      if (d - mine - theirs < 1.2) {
        const strip = Math.max(0, d - Math.min(mine, side > 0 ? r.hwR : r.hwL) - Math.min(theirs, hwO));
        if (side > 0) { r.nbR = oi; r.nbAtR = at; r.stripR = strip; } else { r.nbL = oi; r.nbAtL = at; r.stripL = strip; }
      }
    }
    return r;
  }

  // LaneExtents / ClipExtents (:3382-3405)
  let extentDepth = 0;
  function laneExtents(e, s) {
    const p = pointAt(e, s), tan = tangentAt(e, s), right = [-tan[1], tan[0]];
    const hw = halfWidthAt(e, s);
    const r = squeeze(e, p, right, hw, hw, hw);
    let hwL = r.hwL, hwR = r.hwR;
    if (extentDepth > 2) return [hwL, hwR];
    extentDepth++;
    try {
      const clip = clipAt(e, s);
      if (clip) {
        const c = cutAgainstHost(e, s, p, right, hw, clip);
        if (c) {
          if (c.collapsed) { hwL = 0; hwR = 0; }
          else if (clip.innerSide > 0) hwR = clamp(c.lam, 0, hwR); else hwL = clamp(-c.lam, 0, hwL);
        }
      }
    } finally { extentDepth--; }
    return [hwL, hwR];
  }

  // HostHalf (:1154-1161)
  function hostHalf(H, sM, rMChain, side) {
    const [hl, hr] = laneExtents(H, sM);
    const tH = tangentAt(H, sM);
    const same = rMChain[0] * -tH[1] + rMChain[1] * tH[0] >= 0;
    const sideH = same ? side : -side;
    return sideH > 0 ? hr : hl;
  }

  // CutAgainstHost (:3900-3922). APPROX: no AttachDy test.
  function cutAgainstHost(e, s, p, right, hw, clip) {
    const pr = clip.host.project(p);
    if (pr.atEnd) return null;
    const rM = [-pr.dir[1], pr.dir[0]];
    const side = clip.side;
    const mHalf = hostHalf(pr.H, pr.sM, rM, side) + ClipLift;
    const off = ((p[0] - pr.q[0]) * rM[0] + (p[1] - pr.q[1]) * rM[1]) * side;
    const denom = dot(right, rM) * side;
    if (Math.abs(denom) < 0.3) return null;
    let lam = (mHalf - off) / denom;
    const innerLam = denom > 0 ? -hw : hw, outerLam = -innerLam;
    const innerInside = denom > 0 ? lam > innerLam : lam < innerLam;
    if (!innerInside) return null;
    const outerInside = denom > 0 ? lam >= outerLam - 0.15 : lam <= outerLam + 0.15;
    let collapsed = false;
    if (outerInside) { collapsed = true; lam = clamp(lam, -hw - 6, hw + 6); }
    return { lam, collapsed, H: pr.H, sM: pr.sM };
  }

  // ---------------------------------------------------------- Chain (:977-1087), NextThrough (:1092)
  class Chain {
    constructor() { this.pts = []; this.seg = []; this.sA = []; this.sB = []; this.edges = []; this.cum = []; }
    get length() { return this.cum.length ? this.cum[this.cum.length - 1] : 0; }
    project(p) {
      let best = Infinity, bi = 0, bt = 0;
      for (let i = 0; i + 1 < this.pts.length; i++) {
        const a = this.pts[i], dx = this.pts[i + 1][0] - a[0], dz = this.pts[i + 1][1] - a[1];
        const L2 = dx * dx + dz * dz;
        const t = L2 > 1e-8 ? clamp01(((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L2) : 0;
        const dd = (p[0] - a[0] - dx * t) ** 2 + (p[1] - a[1] - dz * t) ** 2;
        if (dd < best) { best = dd; bi = i; bt = t; }
      }
      const a = this.pts[bi], b = this.pts[bi + 1];
      return {
        H: this.seg[bi], sM: this.sA[bi] + (this.sB[bi] - this.sA[bi]) * bt,
        q: [a[0] + (b[0] - a[0]) * bt, a[1] + (b[1] - a[1]) * bt], dir: normd(sub(b, a)),
        atEnd: bi === this.pts.length - 2 && bt > 0.999, arc: this.cum[bi] + (this.cum[bi + 1] - this.cum[bi]) * bt,
      };
    }
    pieceRange(piece) {
      let c0 = Infinity, c1 = -Infinity;
      for (let i = 0; i < this.seg.length; i++) { if (this.seg[i] !== piece) continue; c0 = Math.min(c0, this.cum[i]); c1 = Math.max(c1, this.cum[i + 1]); }
      return c1 >= c0 ? [c0, c1] : null;
    }
    walk(d) {
      if (this.pts.length < 2 || d > this.length) return null;
      let i = 0;
      while (i + 2 < this.pts.length && this.cum[i + 1] < d) i++;
      const segL = this.cum[i + 1] - this.cum[i];
      const t = segL > 1e-6 ? clamp01((d - this.cum[i]) / segL) : 0;
      const a = this.pts[i], b = this.pts[i + 1];
      return { E: this.seg[i], sE: this.sA[i] + (this.sB[i] - this.sA[i]) * t, p: [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t], dir: normd(sub(b, a)) };
    }
  }
  function nextThrough(e, node, allowLinks) {
    const dIn = mul(outDir(e, node), -1);
    let best = null, bd = -ThroughCos;
    for (const oi of nodeEdges[node]) {
      const o = E[oi];
      if (o === e || o.a === o.b) continue;
      if (o.link && !allowLinks) continue;
      const d = dot(dIn, outDir(o, node));
      if (d < bd) continue;
      if (best === null || d > bd) { best = o; bd = d; }
    }
    return best;
  }
  function buildChain(first, node, linkChain, reach, avoid) {
    const ch = new Chain();
    let cur = first, at = node, L = 0;
    for (let k = 0; k < 6; k++) {
      ch.edges.push(cur);
      const fromA = cur.a === at, n = cur.pts.length;
      for (let i = 0; i < n; i++) {
        const pi = fromA ? i : n - 1 - i;
        const p = cur.pts[pi], sOn = cur.s[pi];
        if (i === 0) { if (ch.pts.length === 0) { ch.pts.push(p); ch.cum.push(0); } continue; }
        const prevPi = fromA ? pi - 1 : pi + 1;
        ch.seg.push(cur); ch.sA.push(cur.s[prevPi]); ch.sB.push(sOn);
        ch.cum.push(ch.cum[ch.cum.length - 1] + dist(ch.pts[ch.pts.length - 1], p));
        ch.pts.push(p);
      }
      L += cur.length;
      if (L >= reach) break;
      const far = fromA ? cur.b : cur.a;
      const nx = nextThrough(cur, far, linkChain);
      if (!nx || ch.edges.includes(nx)) break;
      if (avoid && avoid.edges.includes(nx)) break;
      if (linkChain && (nx.link !== first.link || (!first.link && nx.cls !== first.cls))) break;
      cur = nx; at = far;
    }
    return ch;
  }
  const sharedNode = (e, prev) => (e.a === prev.a || e.a === prev.b) ? e.a : e.b;   // :1519

  // SampleBranch (:1184-1223). APPROX: attached ignores heights.
  function sampleBranch(host, br, travelled, sideRef) {
    const w = br.walk(travelled); if (!w) return null;
    const pr = host.project(w.p); if (pr.atEnd) return null;
    const rM = [-pr.dir[1], pr.dir[0]];
    const off = (w.p[0] - pr.q[0]) * rM[0] + (w.p[1] - pr.q[1]) * rM[1];
    const sideNow = off >= 0 ? 1 : -1;
    if (sideRef.v === 0 && Math.abs(off) > 0.4) sideRef.v = sideNow;
    const side = sideRef.v;
    const rL = [-w.dir[1], w.dir[0]];
    const mHalf = side === 0 ? halfWidthAt(pr.H, pr.sM) : hostHalf(pr.H, pr.sM, rM, side);
    const lHalf = halfWidthAt(w.E, w.sE);
    const outer = add(pr.q, mul(rM, side * mHalf));
    const lSign = dot(rL, rM) >= 0 ? -side : side;
    const inner = add(w.p, mul(rL, lSign * lHalf));
    const gap = side === 0 ? 0 : dot(sub(inner, outer), rM) * side;
    const outerV = sub(w.p, mul(rL, lSign * lHalf));
    const outerGap = side === 0 ? 0 : dot(sub(outerV, outer), rM) * side;
    const cross = Math.abs(dot(rL, rM));
    const outerArc = host.project(outerV).arc;
    const o = { E: w.E, H: pr.H, sE: w.sE, sM: pr.sM, hostArc: pr.arc, outerArc, gap, p: w.p, rM, rL, lSign, sideNow,
                collapsed: side === 0 || outerGap <= 0.15 * cross };
    o.attached = (gap <= GoreMaxGap && sideNow === side) || Math.abs(off) < 0.4;
    return o;
  }
  function refineCollapse(host, br, t0, t1, side, collapsedAt0) {   // :1239
    let arc1 = -1;
    for (let it = 0; it < 5; it++) {
      const tMid = (t0 + t1) * 0.5;
      const m = sampleBranch(host, br, tMid, { v: side }); if (!m) break;
      if (m.collapsed === collapsedAt0) t0 = tMid; else { t1 = tMid; arc1 = m.outerArc; }
    }
    if (arc1 < 0) { const e1 = sampleBranch(host, br, t1, { v: side }); if (e1) arc1 = e1.outerArc; }
    return arc1;
  }
  function arcOnPiece(host, H, c, p0, p1) {   // :1440
    if (c <= p0 || c >= p1) {
      const w = host.walk(clamp(c <= p0 ? p0 + 0.01 : p1 - 0.01, 0, host.length));
      return w.sE < H.length * 0.5 ? -1 : H.length + 1;
    }
    return host.walk(c).sE;
  }
  // EmitBranch (:1263-1434), the clip / gap bookkeeping only (no mesh)
  function emitBranch(L, M, node) {
    const host = buildChain(M, node, false, GoreReach + 60, null);
    const br = buildChain(L, node, true, GoreReach + 10, host);
    const sideRef = { v: 0 };
    const hostOpen = [];
    let open = false, prevCollapsed = true, openFrom = 0, openTo = 0, prevTravelled = 0, attachedTo = -1;
    const brOn = new Map();
    for (let k = 0; k <= 70; k++) {
      const travelled = k * GoreStep;
      if (travelled > GoreReach) break;
      const smp = sampleBranch(host, br, travelled, sideRef);
      if (!smp) break;
      if (!smp.attached) break;
      attachedTo = travelled;
      const side = sideRef.v;
      if (!smp.collapsed && !open) {
        openFrom = k === 0 ? 0 : refineCollapse(host, br, prevTravelled, travelled, side, prevCollapsed);
        if (openFrom < 0) openFrom = smp.outerArc;
        open = true;
      } else if (smp.collapsed && open) {
        const back = refineCollapse(host, br, prevTravelled, travelled, side, false);
        hostOpen.push([openFrom, back < 0 ? openTo : back, true]);
        open = false;
      }
      if (open) openTo = smp.hostArc;
      prevCollapsed = smp.collapsed; prevTravelled = travelled;
      const tE = tangentAt(smp.E, smp.sE);
      const innerE = side === 0 ? 0 : ((-tE[1] * smp.rM[0] + tE[0] * smp.rM[1]) * side < 0 ? 1 : -1);
      const b = brOn.get(smp.E.index) || { s0: smp.sE, s1: smp.sE, inner: innerE };
      brOn.set(smp.E.index, { s0: Math.min(b.s0, smp.sE), s1: Math.max(b.s1, smp.sE), inner: b.inner === 0 ? innerE : b.inner });
    }
    const side = sideRef.v;
    if (attachedTo < 0 || side === 0) return;
    if (open) hostOpen.push([openFrom, openTo, false]);
    let lastOn = -1;
    for (let i = 0; i < br.edges.length; i++) if (brOn.has(br.edges[i].index)) lastOn = i;
    for (let i = 0; i < br.edges.length; i++) {
      const Eb = br.edges[i];
      const b = brOn.get(Eb.index);
      if (!b || b.inner === 0) continue;
      let s0 = b.s0 - 1, s1 = b.s1 + 1;
      const nodeEnd = i === 0 ? node : sharedNode(Eb, br.edges[i - 1]);
      if (Eb.a === nodeEnd) s0 = -1; else if (Eb.b === nodeEnd) s1 = Eb.length + 1;
      let list = clips.get(Eb.index); if (!list) clips.set(Eb.index, list = []);
      list.push({ host, sFrom: s0, sTo: s1, side, innerSide: b.inner, node, hostEdge: M.index });
      const carriesOn = i < lastOn;
      const g0 = Eb.a === nodeEnd ? s0 - 1 : carriesOn ? -1 : b.s0;
      const g1 = Eb.b === nodeEnd ? s1 + 1 : carriesOn ? Eb.length + 1 : b.s1;
      goreGaps.push({ edge: Eb.index, side: b.inner, s0: g0, s1: g1 });
    }
    for (const [a0, a1, reclosed] of hostOpen) {
      const c0 = a0, c1 = reclosed ? a1 : a1 + 1;
      for (const H of host.edges) {
        const pr = host.pieceRange(H);
        if (!pr || pr[1] <= c0 || pr[0] >= c1) continue;
        const w = host.walk(clamp(0.5 * (Math.max(c0, pr[0]) + Math.min(c1, pr[1])), 0, host.length));
        if (!w) continue;
        const tH = tangentAt(H, w.sE);
        const sideH = side * (dot(w.dir, tH) >= 0 ? 1 : -1);
        const s0 = arcOnPiece(host, H, c0, pr[0], pr[1]), s1 = arcOnPiece(host, H, c1, pr[0], pr[1]);
        goreGaps.push({ edge: H.index, side: sideH, s0: Math.min(s0, s1), s1: Math.max(s0, s1) });
      }
    }
  }

  // BuildGores (:1127-1149): every branch end. clipPairs first (APPROX: all of them before any sampling).
  const branchEnds = [];
  for (let ei = 0; ei < E.length; ei++)
    for (let end = 0; end < 2; end++) {
      const h = end === 0 ? T.branchA[ei] : T.branchB[ei];
      if (h < 0) continue;
      branchEnds.push([E[ei], E[h], end === 0 ? E[ei].a : E[ei].b]);
    }
  for (const [L, M, n] of branchEnds) {
    const host = buildChain(M, n, false, GoreReach + 60, null);
    const br = buildChain(L, n, true, GoreReach + 10, host);
    for (const h of host.edges) for (const b of br.edges) clipPairs.add(pk(b.index, h.index));
  }
  for (const [L, M, n] of branchEnds) emitBranch(L, M, n);
  for (const g of goreGaps) { let l = goreByEdge.get(g.edge); if (!l) goreByEdge.set(g.edge, l = []); l.push(g); }

  // ---------------------------------------------------------- StructureEnds (:2508-2558)
  function internalStructureEnds(e, ends) {
    const n = e.stS.length;
    let prev = e.stElev[0] || e.stElev[1];
    for (let i = 1; i + 1 < n; i++) { const on = e.stElev[i] || e.stElev[i + 1]; if (on !== prev) ends.push(e.stS[i]); prev = on; }
  }
  function throughPartner(e, node) {
    const dOut = outDir(e, node);
    let best = null, bd = ThroughCos;
    for (const oi of nodeEdges[node]) {
      const o = E[oi]; if (o === e || o.a === o.b) continue;
      const d = dot(dOut, outDir(o, node));
      if (d < bd) { bd = d; best = o; }
    }
    return best;
  }
  function structureEnds(e) {
    const ends = [];
    if (e.stS.length < 2) return ends;
    internalStructureEnds(e, ends);
    for (let end = 0; end < 2; end++) {
      const node = end === 0 ? e.a : e.b;
      const eOn = elevatedAt(e, end === 0 ? 0 : e.length);
      const o = throughPartner(e, node);
      if (!o || o.stS.length < 2) continue;
      const fromA = o.a === node;
      if (!eOn && elevatedAt(o, fromA ? 0 : o.length)) { ends.push(end === 0 ? 0 : e.length); continue; }
      const pe = []; internalStructureEnds(o, pe);
      for (const so of pe) { const d = fromA ? so : o.length - so; if (d > 0 && d < ApproachRailM) ends.push(end === 0 ? -d : e.length + d); }
    }
    return ends;
  }

  // ---------------------------------------------------------- SamplePositions (:1689-1813)
  function isVertexArc(e, s) {   // Array.BinarySearch: an exact match
    let lo = 0, hi = e.s.length - 1;
    while (lo <= hi) { const m = (lo + hi) >> 1; if (e.s[m] === s) return m > 0 && m < e.s.length - 1; if (e.s[m] < s) lo = m + 1; else hi = m - 1; }
    return false;
  }
  function samplePositions(e, sMin, sMax, dense) {
    const S = [sMin];
    const inR = v => v > sMin + 0.6 && v < sMax - 0.6;
    for (const v of e.stS) if (inR(v)) S.push(v);
    for (let i = 1; i + 1 < e.s.length; i++) if (inR(e.s[i])) S.push(e.s[i]);
    const ei = e.index;
    if (T.taperA[ei] > 0 && inR(T.taperA[ei])) S.push(T.taperA[ei]);
    const tb = e.length - T.taperB[ei];
    if (T.taperB[ei] > 0 && inR(tb)) S.push(tb);
    const cl = clips.get(ei);
    if (cl) for (const c of cl) {
      for (const v of c.host.pts) {
        const sOn = projectOn(e, v);
        if (sOn < c.sFrom || sOn > c.sTo || sOn <= sMin + 0.6 || sOn >= sMax - 0.6) continue;
        const q = pointAt(e, sOn); if ((q[0] - v[0]) ** 2 + (q[1] - v[1]) ** 2 > 1600) continue;
        S.push(sOn);
      }
      for (const H of c.host.edges)
        for (let k = 1; k + 1 < H.stS.length; k++) {
          const v = pointAt(H, H.stS[k]);
          const sOn = projectOn(e, v);
          if (sOn < c.sFrom || sOn > c.sTo || sOn <= sMin + 0.6 || sOn >= sMax - 0.6) continue;
          const q = pointAt(e, sOn); if ((q[0] - v[0]) ** 2 + (q[1] - v[1]) ** 2 > 1600) continue;
          S.push(sOn);
        }
      const from = Math.max(sMin, c.sFrom), to = Math.min(sMax, c.sTo);
      let prevState = -1, prevLam = 0, prevS = from;
      for (let sc = from; sc <= to + 0.01; sc += 1) {
        const pc = pointAt(e, sc), tc = tangentAt(e, sc), rc = [-tc[1], tc[0]];
        let state = 0, lam = 0;
        const cut = cutAgainstHost(e, sc, pc, rc, halfWidthAt(e, sc), c);
        if (cut) { state = cut.collapsed ? 2 : 1; lam = cut.lam; }
        const change = prevState >= 0 && (state !== prevState || (state === 1 && Math.abs(lam - prevLam) > 0.8));
        if (change) { if (inR(prevS)) S.push(prevS); if (inR(sc)) S.push(sc); }
        prevState = state; prevLam = lam; prevS = sc;
      }
    }
    const gg = goreByEdge.get(ei) || [];
    for (const g of gg) { if (inR(g.s0)) S.push(g.s0); if (inR(g.s1)) S.push(g.s1); }
    const ends = structureEnds(e);
    for (const se of ends) for (let k = -1; k <= 1; k += 2) { const sr = se + k * ApproachRailM; if (inR(sr)) S.push(sr); }
    // MODEL (not today's builder): extra sections every `dense` metres inside a taper
    if (dense > 0) {
      if (T.taperA[ei] > 0) for (let v = sMin + dense; v < Math.min(T.taperA[ei], sMax); v += dense) if (inR(v)) S.push(v);
      if (T.taperB[ei] > 0) for (let v = Math.max(sMin, e.length - T.taperB[ei]) + dense; v < sMax; v += dense) if (inR(v)) S.push(v);
    }
    S.push(sMax);
    S.sort((a, b) => a - b);
    const isRunEnd = s => gg.some(g => Math.abs(g.s0 - s) < 1e-4 || Math.abs(g.s1 - s) < 1e-4)
      || ends.some(se => Math.abs(se - s) < 1e-4 || Math.abs(Math.abs(se - s) - ApproachRailM) < 1e-4);
    let w = 1;
    for (let i = 1; i < S.length; i++) {
      if (S[i] - S[w - 1] > 0.5 || i === S.length - 1) { S[w++] = S[i]; continue; }
      if (w > 1 && isVertexArc(e, S[i]) && !isVertexArc(e, S[w - 1]) && !isRunEnd(S[w - 1])) S[w - 1] = S[i];
    }
    S.length = w;
    return { S, structureEnds: ends };
  }

  // RightAt / MitreAt (:1819-1870)
  const isThrough = (e, node) => T.throughA[node] === e.index || T.throughB[node] === e.index;
  function mitreAt(e, node) {
    const oi = T.throughA[node] === e.index ? T.throughB[node] : T.throughA[node];
    const own = e.a === node ? tangentAt(e, 0) : tangentAt(e, e.length);
    if (oi < 0 || oi === e.index) return own;
    const o = E[oi];
    let m = sub(outDir(o, node), outDir(e, node));
    if (m[0] * m[0] + m[1] * m[1] < 1e-6) return own;
    m = normd(m);
    if (dot(m, own) < 0) m = [-m[0], -m[1]];
    return m;
  }
  function rightAt(e, s) {
    let tan, kind = 'seg';
    const [seg, t] = segmentAt(e, s);
    const atA = seg > 0 && t < 1e-3, atB = seg + 2 < e.s.length && t > 1 - 1e-3;
    if (atA || atB) {
      const v = atA ? seg : seg + 1;
      const t0 = normd(sub(e.pts[v], e.pts[v - 1])), t1 = normd(sub(e.pts[v + 1], e.pts[v]));
      tan = add(t0, t1);
      if (tan[0] * tan[0] + tan[1] * tan[1] < 1e-6) tan = t0; else tan = normd(tan);
      kind = 'vertex';
    } else if (s <= 1e-3 && T.mitre[e.a] && isThrough(e, e.a)) { tan = mitreAt(e, e.a); kind = 'mitre'; }
    else if (s >= e.length - 1e-3 && T.mitre[e.b] && isThrough(e, e.b)) { tan = mitreAt(e, e.b); kind = 'mitre'; }
    else tan = tangentAt(e, s);
    return [[-tan[1], tan[0]], kind];
  }

  // RawSectionsOf (:3544-3592) with ClipSection (:3865) and SqueezeSection (:3363).
  // opts overrides the model per call (the census variants).
  function sectionsOf(e, opts = model) {
    if (e.a === e.b && e.length < 1) return null;
    const sMin = T.atA[e.index], sMax = e.length - T.atB[e.index];
    if (sMax - sMin < 0.6) return null;
    const { S, structureEnds: ends } = samplePositions(e, sMin, sMax, opts.dense || 0);
    const out = [];
    for (const s of S) {
      const p = pointAt(e, s);
      const [right, kind] = rightAt(e, s);
      const hw = halfWidthAt(e, s);   // * widen (always 1)
      const sec = { s, p, right, hw, kind, elev: elevatedAt(e, s),
        L: [p[0] - right[0] * hw, p[1] - right[1] * hw], R: [p[0] + right[0] * hw, p[1] + right[1] * hw],
        clippedIn: false, collapsed: false, innerSide: 0, nbL: -1, nbR: -1, stripL: -1, stripR: -1, clipped: false,
        sqL: false, sqR: false,
        structEnd: ends.some(se => Math.abs(se - s) < 1e-3 || Math.abs(Math.abs(se - s) - ApproachRailM) < 1e-3),
        taper: (T.taperA[e.index] > 0 && s <= T.taperA[e.index] + 1e-3) || (T.taperB[e.index] > 0 && e.length - s <= T.taperB[e.index] + 1e-3) };
      const clip = clipAt(e, s);
      if (clip) {
        const c = cutAgainstHost(e, s, p, right, hw, clip);
        if (c) {
          sec.innerSide = clip.innerSide; sec.clipped = true;
          const v = add(p, mul(right, c.lam));
          if (c.collapsed) { sec.L = v; sec.R = v; sec.collapsed = true; sec.clippedIn = true; }
          else { if (clip.innerSide > 0) sec.R = v; else sec.L = v; sec.clippedIn = true; }
        }
      }
      if (!sec.collapsed) {
        const hwL = dist(sec.L, p), hwR = dist(sec.R, p);
        const r = squeeze(e, p, right, hw, hwL, hwR);
        sec.nbL = r.nbL; sec.nbR = r.nbR; sec.stripL = r.stripL; sec.stripR = r.stripR;
        if (r.hwR < hwR - 1e-3) { sec.R = add(p, mul(right, r.hwR)); sec.sqR = true; }
        if (r.hwL < hwL - 1e-3) { sec.L = sub(p, mul(right, r.hwL)); sec.sqL = true; }
      }
      sec.latL = dot(sub(sec.L, p), right); sec.latR = dot(sub(sec.R, p), right);
      sec.uL = hw > 0.05 ? clamp01(0.5 - sec.latL / (2 * hw)) : 1;
      sec.uR = hw > 0.05 ? clamp01(0.5 - sec.latR / (2 * hw)) : 0;
      if (opts.nominalU) {   // MODEL: U against the profile's own half width (crop, never compress)
        const hn = e.width * 0.5;
        sec.uL = clamp01(0.5 - sec.latL / (2 * hn)); sec.uR = clamp01(0.5 - sec.latR / (2 * hn));
      }
      sec.v = (chainV.vOff[e.index] + chainV.vDir[e.index] * s) / RoadVTile;   // BuildRoadsAndDecks: chain distance (WP-11)
      out.push(sec);
    }
    return out;
  }

  return {
    model: modelName, modelOpts: model, city, E, NODES, nodeEdges, T, halfWidthAt, sectionsOf, rightAt,
    isSeparatedPair: separated, isClipPair: (i, j) => clipPairs.has(pk(i, j)),
    clipsOf: ei => clips.get(ei) || [], goreGapsOf: ei => goreByEdge.get(ei) || [],
    elevatedAt, pointAt, tangentAt, projectOn, outDir, segmentAt,
  };
}

// ------------------------------------------------------------ the painter's layout (Editor/PSXRacingBuilder.City.cs:458-514, PaintHalf :621)
// NOMINAL line positions, as the census measured them. The gate itself reads
// the painted runs out of the PNGs (paintruns.mjs); this is kept for the
// census reproduction only.
const PaintHalf = 0.06;
export function paintLinesNominal(prof) {
  const total = prof.width, lines = [];
  const oneway = !prof.key.startsWith('tw');
  const turn = prof.key.endsWith('t');
  lines.push({ m: prof.shl + PaintHalf, col: oneway ? 'Y' : 'W', kind: 'edgeLeft', dashed: false });
  lines.push({ m: total - prof.shr - PaintHalf, col: 'W', kind: 'edgeRight', dashed: false });
  if (oneway) {
    for (let i = 1; i < prof.lanes; i++) lines.push({ m: prof.shl + LANE * i, col: 'W', kind: 'lane', dashed: true });
  } else {
    const perSide = (prof.lanes - (turn ? 1 : 0)) / 2;
    const medStart = prof.shl + perSide * LANE;
    for (let i = 1; i < perSide; i++) lines.push({ m: prof.shl + LANE * i, col: 'W', kind: 'lane', dashed: true });
    if (turn) {
      lines.push({ m: medStart - PaintHalf * 2, col: 'Y', kind: 'centre', dashed: false });
      lines.push({ m: medStart + PaintHalf * 2, col: 'Y', kind: 'centre', dashed: true });
      lines.push({ m: medStart + LANE - PaintHalf * 2, col: 'Y', kind: 'centre', dashed: true });
      lines.push({ m: medStart + LANE + PaintHalf * 2, col: 'Y', kind: 'centre', dashed: false });
      for (let i = 1; i < perSide; i++) lines.push({ m: medStart + LANE + LANE * i, col: 'W', kind: 'lane', dashed: true });
    } else {
      lines.push({ m: medStart - PaintHalf * 2, col: 'Y', kind: 'centre', dashed: false });
      lines.push({ m: medStart + PaintHalf * 2, col: 'Y', kind: 'centre', dashed: false });
      for (let i = 1; i < perSide; i++) lines.push({ m: medStart + LANE * i, col: 'W', kind: 'lane', dashed: true });
    }
  }
  for (const l of lines) { l.u = l.m / total; l.latNom = total / 2 - l.m; }   // lat > 0 = left of travel (the R vertex side)
  return lines;
}
