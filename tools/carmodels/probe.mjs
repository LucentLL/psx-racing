import { parseObj, bounds, objVertIndices } from './analyze.mjs';
import path from 'path';
for (const file of process.argv.slice(2)) {
  const m = parseObj(file);
  const used = new Set();
  for (const o of m.objects) for (const f of o.faces) for (const c of f) used.add(c.v);
  const b = bounds([...used], m.verts);
  // roof centroid on body objects only (exclude wheels)
  const bodyIdx = new Set();
  for (const o of m.objects) if (!/wheel/i.test(o.name)) for (const f of o.faces) for (const c of f) bodyIdx.add(c.v);
  const bb = bounds([...bodyIdx], m.verts);
  let roofZ = 0, n = 0;
  const thr = bb.mn[1] + (bb.mx[1] - bb.mn[1]) * 0.85;
  for (const i of bodyIdx) { const v = m.verts[i - 1]; if (v[1] > thr) { roofZ += v[2]; n++; } }
  roofZ /= Math.max(n, 1);
  // nose overhang test: distance from front axle to body front vs rear axle to body rear
  const wf = m.objects.filter(o => /wheel.?f/i.test(o.name));
  const wr = m.objects.filter(o => /wheel.?r(?!.?f)/i.test(o.name));
  const fz = wf.length ? bounds(objVertIndices(wf[0]), m.verts).center[2] : null;
  const rz = wr.length ? bounds(objVertIndices(wr[0]), m.verts).center[2] : null;
  console.log(path.basename(path.dirname(file)).padEnd(24),
    'usedBox=' + b.size.map(x=>x.toFixed(2)).join('x'),
    'bodyBox=' + bb.size.map(x=>x.toFixed(2)).join('x'),
    'bodyCtrZ=' + bb.center[2].toFixed(2),
    'roofZ=' + roofZ.toFixed(2),
    'axleF=' + (fz===null?'-':fz.toFixed(2)), 'axleR=' + (rz===null?'-':rz.toFixed(2)),
    'yMin=' + bb.mn[1].toFixed(2));
}
