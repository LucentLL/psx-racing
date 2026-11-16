// drags.mjs - find straight multi-lane runs on the shipped graph for 1/4-mile and 1/2-mile drags
import { city, dem, toLat, toLon, resample, angDiff, edgePts } from './lib.mjs';
const C = city(), D = dem();
const RUN = +(process.argv[2] || 402), LEAD = 60, SHUT = +(process.argv[3] || 250);
const laneDir = (e, dir) => e.oneway ? e.profile.lanes : e.lset ? (dir === 1 ? e.lset.nF : e.lset.nB) : Math.floor(e.profile.lanes / 2);
const hOut = (e, dir) => { const p = edgePts(e, dir); return Math.atan2(p[1][1] - p[0][1], p[1][0] - p[0][0]); };
const hIn = (e, dir) => { const p = edgePts(e, dir); const n = p.length; return Math.atan2(p[n - 1][1] - p[n - 2][1], p[n - 1][0] - p[n - 2][0]); };
const seen = new Set();
const found = [];
for (const e0 of C.edges) {
  if (e0.link || e0.rank < 2 || e0.length < 40) continue;
  for (const d0 of e0.oneway ? [1] : [1, -1]) {
    if (laneDir(e0, d0) < 2) continue;
    // follow the straightest continuation
    const chain = [{ ei: e0.index, dir: d0 }];
    let L = e0.length, cur = { e: e0, dir: d0 };
    while (L < LEAD + RUN + SHUT + 200) {
      const end = cur.dir === 1 ? cur.e.b : cur.e.a;
      let best = null, bt = 1e9;
      for (const nj of C.nodeEdges[end]) {
        const f = C.edges[nj];
        if (nj === cur.e.index || f.link) continue;
        const nd = f.a === end ? 1 : -1;
        if (nd === -1 && f.oneway) continue;
        const t = Math.abs(angDiff(hIn(cur.e, cur.dir), hOut(f, nd)));
        if (t < bt) { bt = t; best = { e: f, dir: nd }; }
      }
      if (!best || bt > 0.08) break;
      chain.push({ ei: best.e.index, dir: best.dir }); L += best.e.length; cur = best;
      if (chain.length > 80) break;
    }
    if (L < LEAD + RUN + SHUT) continue;
    const poly = [];
    for (const c of chain) { const p = edgePts(C.edges[c.ei], c.dir); for (let i = poly.length ? 1 : 0; i < p.length; i++) poly.push(p[i]); }
    const R = resample(poly, 5);
    const P = R.pts, N = P.length;
    // per-sample edge
    const eAt = []; { let acc = 0, k = 0; const b = chain.map(c => { const l = C.edges[c.ei].length; const r = [acc, acc + l]; acc += l; return r; }); const sc = R.length / acc; for (let i = 0; i < N; i++) { while (k < chain.length - 1 && R.s[i] / sc > b[k][1]) k++; eAt.push(chain[k]); } }
    const nRun = Math.round((LEAD + RUN) / 5), nAll = Math.round((LEAD + RUN + SHUT) / 5);
    for (let a = 0; a + nAll < N; a += 4) {
      const b = a + nRun;
      const [ax, az] = P[a], [bx, bz] = P[b];
      const ch = Math.hypot(bx - ax, bz - az);
      let dev = 0, minL = 9, bridge = 0, w = 99;
      for (let q = a; q <= b; q++) {
        dev = Math.max(dev, Math.abs(((P[q][0] - ax) * (bz - az) - (P[q][1] - az) * (bx - ax)) / ch));
        const e = C.edges[eAt[q].ei];
        minL = Math.min(minL, laneDir(e, eAt[q].dir)); if (e.bridge) bridge++;
        w = Math.min(w, e.oneway ? e.profile.width : e.profile.width);
      }
      if (dev > 1.5 || minL < 2) continue;
      // shutdown: allow gentle curvature
      let devS = 0; const [cx, cz] = P[a + nAll];
      for (let q = b; q <= a + nAll; q++) devS = Math.max(devS, Math.abs(((P[q][0] - ax) * (cz - az) - (P[q][1] - az) * (cx - ax)) / Math.hypot(cx - ax, cz - az)));
      if (devS > 12) continue;
      const z0 = D.asl(...P[a + LEAD / 5]), z1 = D.asl(...P[b]);
      const grade = (z1 - z0) / RUN * 100;
      const e = C.edges[eAt[a].ei];
      const key = (e.name || '?') + '|' + Math.round(P[a][0] / 400) + '|' + Math.round(P[a][1] / 400);
      if (seen.has(key)) continue;
      seen.add(key);
      // signals and side mouths inside the run
      let mouths = 0;
      found.push({ name: e.name, rank: e.rank, lanes: minL, width: +w.toFixed(1), divided: e.oneway, dev: +dev.toFixed(2), devS: +devS.toFixed(1), grade: +grade.toFixed(1), bridge: bridge * 5,
        lat: +toLat(P[a][1]).toFixed(5), lon: +toLon(P[a][0]).toFixed(5), latE: +toLat(P[b][1]).toFixed(5), lonE: +toLon(P[b][0]).toFixed(5),
        hdg: Math.round((Math.atan2(bx - ax, bz - az) * 180 / Math.PI + 360) % 360), ei: chain[0].ei, dir: chain[0].dir });
      a += 40;
    }
  }
}
// best per road name: prefer wide, straight, flat, no bridge
const score = f => f.lanes * 10 + (f.divided ? 4 : 0) - f.dev * 3 - Math.abs(f.grade) * 2 - f.bridge / 20 + f.rank * 2;
const byName = new Map();
for (const f of found) { const k = f.name; if (!byName.has(k) || score(f) > score(byName.get(k))) byName.set(k, f); }
const top = [...byName.values()].sort((p, q) => score(q) - score(p)).slice(0, 25);
for (const f of top) console.log(`${(f.name || '?').padEnd(34)} r${f.rank} lanes ${f.lanes} w ${f.width} ${f.divided ? 'div' : '2way'} dev ${f.dev} shut ${f.devS} grade ${f.grade}% bridge ${f.bridge}m  ${f.lat},${f.lon} -> ${f.latE},${f.lonE} hdg ${f.hdg}`);
