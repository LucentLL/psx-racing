import fs from 'fs';
import path from 'path';

export function parseObj(file) {
  const txt = fs.readFileSync(file, 'utf8');
  const verts = [];       // all v, 1-indexed later
  const uvs = [];
  const norms = [];
  const objects = [];     // {name, faces:[[{v,vt,vn}...]]}
  let cur = null;
  for (const raw of txt.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.startsWith('v ')) { const p = line.split(/\s+/); verts.push([+p[1], +p[2], +p[3]]); }
    else if (line.startsWith('vt ')) { const p = line.split(/\s+/); uvs.push([+p[1], +p[2]]); }
    else if (line.startsWith('vn ')) { const p = line.split(/\s+/); norms.push([+p[1], +p[2], +p[3]]); }
    else if (line.startsWith('o ')) { cur = { name: line.slice(2).trim(), faces: [] }; objects.push(cur); }
    else if (line.startsWith('f ')) {
      if (!cur) { cur = { name: 'default', faces: [] }; objects.push(cur); }
      const parts = line.split(/\s+/).slice(1);
      cur.faces.push(parts.map(p => {
        const [v, vt, vn] = p.split('/');
        return { v: +v, vt: vt ? +vt : 0, vn: vn ? +vn : 0 };
      }));
    }
  }
  return { verts, uvs, norms, objects };
}

export function bounds(objIndices, verts) {
  const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  for (const i of objIndices) {
    const v = verts[i - 1];
    for (let k = 0; k < 3; k++) { if (v[k] < mn[k]) mn[k] = v[k]; if (v[k] > mx[k]) mx[k] = v[k]; }
  }
  return { mn, mx, size: mx.map((m, k) => m - mn[k]), center: mx.map((m, k) => (m + mn[k]) / 2) };
}

export function objVertIndices(o) {
  const s = new Set();
  for (const f of o.faces) for (const c of f) s.add(c.v);
  return [...s];
}

if (process.argv[1].endsWith('analyze.mjs')) {
  for (const file of process.argv.slice(2)) {
    const m = parseObj(file);
    console.log('=== ' + path.basename(file));
    const all = bounds(m.verts.map((_, i) => i + 1), m.verts);
    console.log('  ALL size(x,y,z) = ' + all.size.map(n => n.toFixed(3)).join(', ') + '  min=' + all.mn.map(n=>n.toFixed(2)).join(',') + ' max=' + all.mx.map(n=>n.toFixed(2)).join(','));
    for (const o of m.objects) {
      const b = bounds(objVertIndices(o), m.verts);
      console.log('   - ' + o.name.padEnd(18) + ' tris~' + String(o.faces.reduce((a,f)=>a+f.length-2,0)).padStart(4) +
        '  size=' + b.size.map(n => n.toFixed(2)).join(',') + '  ctr=' + b.center.map(n => n.toFixed(3)).join(','));
    }
  }
}
