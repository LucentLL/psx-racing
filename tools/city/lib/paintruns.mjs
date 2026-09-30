// paintruns.mjs - the painted layout of every road texture, READ FROM THE
// PNGs the game ships (gate spec 3.1), never from a copy of DrawProfileTex.
//
// Each city_road_<profile>_<surface>.png is scanned once for runs of the exact
// paint colours - Yellow (196,160,40), White (200,200,196) - and every run
// becomes a line: centre u, width du, colour, and its pattern:
//   solid   present in (Unity) row 32
//   dashed  present only in part of the 64 rows; its on-band [vOn0, vOn1) is
//           the rows it is painted in (today 0-15: the first quarter of each
//           12.192 m V repeat, 10 ft of a 40 ft cycle)
// Unity's texture rows run bottom-up (Texture2D.SetPixels32 row 0 is the
// bottom, EncodeToPNG writes it last), so Unity row r is PNG row H-1-r.
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { decodePng } from './png.mjs';

export const PAINT = { Y: [196, 160, 40], W: [200, 200, 196] };
export const SURFACES = ['asphalt_new', 'asphalt_old', 'concrete_new', 'concrete_old'];

export function scanRuns(png) {
  const { width: W, height: H, rgba } = png;
  const at = (x, row, c) => { const o = ((H - 1 - row) * W + x) * 4; return rgba[o] === c[0] && rgba[o + 1] === c[1] && rgba[o + 2] === c[2]; };
  const runsInRow = (row, col) => {
    const out = [];
    let x0 = -1;
    for (let x = 0; x <= W; x++) {
      const on = x < W && at(x, row, PAINT[col]);
      if (on && x0 < 0) x0 = x;
      if (!on && x0 >= 0) { out.push([x0, x - 1]); x0 = -1; }
    }
    return out;
  };
  const runs = [];
  const solidRow = Math.floor(H / 2);
  for (const col of ['Y', 'W']) {
    const solid = runsInRow(solidRow, col);
    for (const [x0, x1] of solid) runs.push({ x0, x1, col, dashed: false, vOn0: 0, vOn1: 1 });
    // dashed: a run somewhere in the texture that row 32 does not have
    const seen = new Set(solid.map(r => r[0] + ':' + r[1]));
    for (let row = 0; row < H; row++)
      for (const [x0, x1] of runsInRow(row, col)) {
        const k = x0 + ':' + x1;
        if (seen.has(k)) continue;
        seen.add(k);
        // its on-band: the contiguous rows its centre texel is painted in
        const xc = (x0 + x1) >> 1;
        let r0 = row, r1 = row;
        while (r1 + 1 < H && at(xc, r1 + 1, PAINT[col])) r1++;
        runs.push({ x0, x1, col, dashed: true, vOn0: r0 / H, vOn1: (r1 + 1) / H });
      }
  }
  runs.sort((a, b) => a.x0 - b.x0);
  for (const r of runs) { r.u = (r.x0 + r.x1 + 1) / (2 * W); r.du = (r.x1 - r.x0 + 1) / W; r.texW = W; }
  return runs;
}

/// Every profile key's runs, per surface; `same` says whether the four
/// surfaces of each profile paint identical runs (they should: only the
/// grain differs).
export function loadPaintLayouts(artDir, profileKeys) {
  const layouts = new Map(), notes = [];
  for (const key of profileKeys) {
    let first = null;
    for (const surf of SURFACES) {
      const f = join(artDir, `city_road_${key}_${surf}.png`);
      if (!existsSync(f)) { notes.push(`missing ${f}`); continue; }
      const runs = scanRuns(decodePng(readFileSync(f)));
      layouts.set(`${key}_${surf}`, runs);
      const sig = JSON.stringify(runs.map(r => [r.x0, r.x1, r.col, r.dashed, r.vOn0, r.vOn1]));
      if (first === null) first = sig;
      else if (sig !== first) notes.push(`${key}: ${surf} paints different runs from the other surfaces`);
    }
  }
  return { layouts, notes };
}
