// My simplified replica of the pieces of CityMeshes.ComputeTrimsCore (mitre / through pair) and
// LineModel (SPLT median taper, RoomAlong) needed to check the census's (a) numbers.
import { load, pointAt, tangentAt } from './rd.mjs';
export const ThroughCos = -0.85, BranchCos = 0.5, ContinueCos = -0.906;
export const Smooth = t => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
export function build() {
  const { map } = load();
  const E = map.edges, nn = map.nodes.length;
  const outDir = (e, n) => { if (e.a === n) return tangentAt(e, 0); const t = tangentAt(e, e.length); return [-t[0], -t[1]]; };
  const mitre = new Uint8Array(nn), thA = new Int32Array(nn).fill(-1), thB = new Int32Array(nn).fill(-1), fan = new Uint8Array(nn);
  for (let n = 0; n < nn; n++) {
    const arms = map.nodeEdges[n].map(i => E[i]).filter(e => e.a !== e.b).map(e => ({ e, dir: outDir(e, n) }));
    if (arms.length < 2) continue;
    let tA = -1, tB = -1, best = 1;
    for (let pass = 0; pass < 2 && tA < 0; pass++)
      for (let i = 0; i < arms.length; i++) for (let j = i + 1; j < arms.length; j++) {
        if (pass === 0 && (arms[i].e.link || arms[j].e.link)) continue;
        const d = arms[i].dir[0] * arms[j].dir[0] + arms[i].dir[1] * arms[j].dir[1];
        if (d < best) { best = d; tA = i; tB = j; }
      }
    const through = tA >= 0 && best < ThroughCos;
    if (arms.length === 2) { if (best < ContinueCos) { mitre[n] = 1; thA[n] = arms[0].e.index; thB[n] = arms[1].e.index; continue; } }
    // clipping as ComputeTrimsCore: per shallow pair one arm clips onto the other
    const clipped = arms.map(() => -1);
    for (let i = 0; i < arms.length; i++) for (let j = i + 1; j < arms.length; j++) {
      const d = arms[i].dir[0] * arms[j].dir[0] + arms[i].dir[1] * arms[j].dir[1]; if (d < BranchCos) continue;
      const ai = arms[i], aj = arms[j]; let iClips;
      if (ai.e.link !== aj.e.link) iClips = ai.e.link; else if (ai.e.cls !== aj.e.cls) iClips = ai.e.cls < aj.e.cls; else if (Math.abs(ai.e.width - aj.e.width) * 0.5 > 0.05) iClips = ai.e.width < aj.e.width; else iClips = true;
      if (through && (i === tA || i === tB) && (j === tA || j === tB)) continue;
      if (through && (j === tA || j === tB)) iClips = true;
      if (through && (i === tA || i === tB)) iClips = false;
      const c = iClips ? i : j, h = iClips ? j : i; if (clipped[c] < 0) clipped[c] = h;
    }
    let allBranch = through;
    if (through) for (let i = 0; i < arms.length; i++) if (i !== tA && i !== tB && clipped[i] < 0) allBranch = false;
    if (allBranch) { mitre[n] = 1; thA[n] = arms[tA].e.index; thB[n] = arms[tB].e.index; } else fan[n] = 1;
  }
  const partner = (e, node) => { if (!mitre[node]) return -1; const o = thA[node] === e.index ? thB[node] : thB[node] === e.index ? thA[node] : -1; return o === e.index ? -1 : o; };
  function roomAlong(e, fromNode, side, both) {
    let total = e.length, cur = e, from = fromNode, sd = side;
    for (let g = 0; g < 12; g++) {
      const far = cur.a === from ? cur.b : cur.a; const pi = partner(cur, far); if (pi < 0) break;
      const p = E[pi]; if (p === cur || p.a === p.b) break;
      const sideT = cur.a === from ? sd : -sd; const pSide = p.a === far ? sideT : -sideT;
      const c1 = sd > 0 ? cur.lmPlus : cur.lmMinus, p1 = pSide > 0 ? p.lmPlus : p.lmMinus; if (Math.abs(c1 - p1) > 0.05) break;
      if (both) { const c2 = sd > 0 ? cur.lmMinus : cur.lmPlus, p2 = pSide > 0 ? p.lmMinus : p.lmPlus; if (Math.abs(c2 - p2) > 0.05) break; }
      total += p.length; cur = p; from = far; sd = pSide;
    }
    return total;
  }
  const floorOf = w => (w.cls >= 5 || (w.cls === 4 && w.oneway && !w.link)) ? 90 : (w.link || w.cls >= 2) ? 30 : 15;
  return { map, E, mitre, fan, thA, thB, partner, roomAlong, floorOf, outDir };
}
// the SPLT taper per carriageway end, as LineModel.cs:595-627
export function splitTapers(M) {
  const { map, E } = M; const out = [];
  for (const sp of map.splt) {
    const U = E[sp.u]; if (!U) continue;
    const tu = U.a === sp.node ? tangentAt(U, 0).map(v => -v) : tangentAt(U, U.length);
    const leftU = [-tu[1], tu[0]];
    for (let c = 0; c < 2; c++) {
      const ci = c === 0 ? sp.a : sp.b, offC = c === 0 ? sp.offA : sp.offB; const C = E[ci];
      if (!C || (C.a !== sp.node && C.b !== sp.node)) continue;
      const tc = C.a === sp.node ? tangentAt(C, 0) : tangentAt(C, C.length);
      const sign = (leftU[0] * -tc[1] + leftU[1] * tc[0]) >= 0 ? 1 : -1;
      const out1 = Math.sign(offC * sign); if (out1 === 0) { out.push({ sp, ci, C, U, skip: 'out1=0' }); continue; }
      const outIsLeftU = out1 * sign > 0, uPlusIsLeftU = U.b === sp.node;
      const uOut = outIsLeftU === uPlusIsLeftU ? U.lmPlus : U.lmMinus; const cOut = out1 > 0 ? C.lmPlus : C.lmMinus;
      const shift0 = out1 * (uOut - cOut);
      const lenRule = Math.max(M.floorOf(C), Math.max(10, sp.rate) * Math.abs(shift0));
      const room = M.roomAlong(C, sp.node, 1, true);
      const lenDrawn = Math.max(0.5, Math.min(lenRule, room));
      out.push({ sp, ci, C, U, out1, uOut, cOut, shift0, lenRule, room, lenDrawn });
    }
  }
  return out;
}
