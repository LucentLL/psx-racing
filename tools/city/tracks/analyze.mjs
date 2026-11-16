// analyze.mjs - route every candidate on the shipped graph, measure it, write out/cands.json
import { writeFileSync, mkdirSync } from 'node:fs';
import { city, dem, toLat, toLon, toX, toZ, routeVia, chainPoly, resample, angDiff, edgePts, LANE_M } from './lib.mjs';
import { CANDS } from './cands.mjs';

const C = city(), D = dem();
const OUT = new URL('./out/', import.meta.url);
mkdirSync(OUT, { recursive: true });

// --------------------------------------------------------- known faults
// HANDOFF.md:2071 "NOT fixed - keep race routes off these for now" (charlotte-spui not merged into HEAD),
// and the owner's Beatties Ford spots (memory psx-racing-alignment-nov16, graph b2aa3453 = this graph).
const FAULT_AREAS = [
  { lat: 35.2478, lon: -80.8963, r: 220, label: 'SPUI deck I-85 x Freedom Dr (steps 22 cm, 5.5 m gap)' },
  { lat: 35.1630, lon: -80.8900, r: 220, label: 'SPUI deck I-77 x Tyvola Rd (steps 18 cm, slit)' },
  { lat: 35.2621, lon: -80.8783, r: 220, label: 'SPUI deck I-85 x Brookshire/NC 16 (steps 4 cm, Jersey stubs)' },
  { lat: 35.2414, lon: -80.9389, r: 300, label: 'airport ramps I-85 x Little Rock x Josh Birmingham (steps 7.5 cm)' },
];
const FAULT_EDGES = new Map([
  [386, 'I-77 ramp under W Arrowood (13 cm, 9.6 %)'], [1740, 'Harrisburg Rd bridge joint (10 cm)'],
  [1892, 'Freedom Dr deck start (5 cm, 6.8 %)'], [24016, 'service road by N Graham (7.5 cm)'],
  [6021, 'The Plaza joint (5.8 % break)'], [14137, 'N Davidson joint (3.7 cm, 4.8 %)'],
  [9994, 'South Blvd joint (3.7 cm)'], [11993, 'Billy Graham Pkwy joint (4 % break)'],
]);
const BF_NODES = [[10238, 'Beatties Ford island past LaSalle (owner spot 34)'], [10236, 'Beatties Ford island past LaSalle (34)'],
  [10229, 'Beatties Ford at Keller (35)'], [231, 'Beatties Ford x I-85 ramps jog (36)'], [10035, 'Beatties Ford north of Hoskins (37)']];
for (const [n, label] of BF_NODES) FAULT_AREAS.push({ lat: toLat(C.nodes[n].z), lon: toLon(C.nodes[n].x), r: 120, label });

function unwrap(h) { const o = [h[0]]; for (let i = 1; i < h.length; i++) o.push(o[i - 1] + angDiff(h[i - 1], h[i])); return o; }

function nearestS(P, S, lat, lon) {
  const x = toX(lon), z = toZ(lat);
  let best = 0, bd = 1e18;
  for (let i = 0; i < P.length; i++) { const d = (P[i][0] - x) ** 2 + (P[i][1] - z) ** 2; if (d < bd) { bd = d; best = i; } }
  return S[best];
}

function analyze(cand) {
  let fullChain;
  if (cand.route) {
    const rt = C.routes.find(r => r.id === cand.route);
    fullChain = rt.edges.map((ei, k) => ({ ei, dir: rt.dirs[k] }));
  } else {
    const wps = cand.via.map(f => f());
    fullChain = routeVia(wps, { loop: cand.kind === 'circuit', fwy: !!cand.fwy, ...(cand.opts || {}) });
  }
  const RS0 = resample(chainPoly(fullChain), 5);
  // per-sample edge + station along it (for lanes / bridges / names / measured heights)
  const N0 = RS0.pts.length;
  const edgeAt0 = new Int32Array(N0), dirAt0 = new Int8Array(N0), sOn0 = new Float64Array(N0);
  {
    let acc = 0, k = 0;
    const bounds = fullChain.map(c => { const L = C.edges[c.ei].length; const b = [acc, acc + L]; acc += L; return b; });
    const scale = RS0.length / acc;
    for (let i = 0; i < N0; i++) {
      const s = RS0.s[i] / scale;
      while (k < fullChain.length - 1 && s > bounds[k][1]) k++;
      edgeAt0[i] = fullChain[k].ei; dirAt0[i] = fullChain[k].dir;
      const along = Math.min(C.edges[fullChain[k].ei].length, Math.max(0, s - bounds[k][0]));
      sOn0[i] = fullChain[k].dir === 1 ? along : C.edges[fullChain[k].ei].length - along;
    }
  }
  // trim: cut = { from, to } each a number (m) or { lat, lon, d } (the route's nearest point, plus d metres)
  let i0 = 0, i1 = N0 - 1;
  const at = v => v == null ? null : typeof v === 'number' ? v : nearestS(RS0.pts, RS0.s, v.lat, v.lon) + (v.d || 0);
  if (cand.cut) {
    const a = at(cand.cut.from), b = at(cand.cut.to);
    if (a != null) i0 = Math.max(0, Math.round(a / 5));
    if (b != null) i1 = Math.min(N0 - 1, Math.round(b / 5));
  }
  if (cand.totalM) i1 = Math.min(N0 - 1, i0 + Math.round(cand.totalM / 5));
  const P = RS0.pts.slice(i0, i1 + 1), S = RS0.s.slice(i0, i1 + 1).map(s => s - RS0.s[i0]), N = P.length;
  const edgeAt = edgeAt0.slice(i0, i1 + 1), dirAt = dirAt0.slice(i0, i1 + 1), sOn = sOn0.slice(i0, i1 + 1);
  const length = S[N - 1];
  // the chain actually raced (edges touched by the trimmed samples, in order)
  const chain = [];
  for (let q = 0; q < N; q++) if (!chain.length || chain[chain.length - 1].ei !== edgeAt[q]) chain.push({ ei: edgeAt[q], dir: dirAt[q] });
  if (cand.kind === 'circuit' && chain.length > 1 && chain[0].ei === chain[chain.length - 1].ei) chain.pop();
  // headings + curvature
  const h = [];
  for (let i = 0; i < N - 1; i++) h.push(Math.atan2(P[i + 1][1] - P[i][1], P[i + 1][0] - P[i][0]));
  h.push(h[h.length - 1]);
  const hu = unwrap(h);
  const hs = hu.map((_, i) => { let a = 0, n = 0; for (let j = -2; j <= 2; j++) { const q = i + j; if (q >= 0 && q < N) { a += hu[q]; n++; } } return a / n; });
  const kappa = hs.map((_, i) => i === 0 ? 0 : (hs[i] - hs[i - 1]) / 5);
  // corners
  const corners = [];
  // a bend is a run where the heading turns at least 1/800 rad per metre measured over +-25 m (OSM draws a
  // sweeper as straight pieces between sparse vertices, so a 10 m window misses it); its sharpest radius is
  // read over +-10 m. Kept when it turns 20 degrees or more.
  const k50 = hu.map((_, q) => { const qa = Math.max(0, q - 5), qb = Math.min(N - 1, q + 5); return (hu[qb] - hu[qa]) / Math.max(5, S[qb] - S[qa]); });
  let i = 0;
  const KMIN = 1 / 800;
  while (i < N) {
    if (Math.abs(k50[i]) < KMIN) { i++; continue; }
    const sg = Math.sign(k50[i]);
    let j = i, gap = 0, last = i;
    while (j < N && gap <= 3) { if (Math.abs(k50[j]) >= KMIN && Math.sign(k50[j]) === sg) { last = j; gap = 0; } else gap++; j++; }
    const qa = Math.max(0, i - 3), qb = Math.min(N - 1, last + 3);
    const tot = hu[qb] - hu[qa];
    let kmax = 0;
    for (let q = qa; q <= qb; q++) kmax = Math.max(kmax, Math.abs(kappa[q]), Math.abs(k50[q]));
    const deg = Math.abs(tot) * 180 / Math.PI;
    if (deg >= 20) corners.push({ s: S[i], s1: S[last], deg: Math.round(deg), dir: sg > 0 ? 'L' : 'R', minR: Math.round(1 / kmax), at: [P[Math.floor((i + last) / 2)][0], P[Math.floor((i + last) / 2)][1]] });
    i = last + 1;
  }
  const tight = corners.filter(c => c.minR < 35).length, medium = corners.filter(c => c.minR >= 35 && c.minR < 120).length, fast = corners.filter(c => c.minR >= 120).length;
  // elevation
  // heights: the measured road profile (RPRF, 10 m stations, world y over the 97 m datum) where the edge has one
  const zs = P.map((p, q) => {
    const e = C.edges[edgeAt[q]];
    if (e.rprf && e.rprf.y.length > 1) {
      const f = Math.min(e.rprf.y.length - 1.0001, sOn[q] / 10), k = Math.floor(f), t = f - k;
      return e.rprf.y[k] * (1 - t) + e.rprf.y[k + 1] * t + D.base;
    }
    return D.asl(p[0], p[1]);
  });
  const zsm = zs.map((_, i) => { let a = 0, n = 0; for (let j = -5; j <= 5; j++) { const q = i + j; if (q >= 0 && q < N) { a += zs[q]; n++; } } return a / n; });
  let climb = 0, maxG = 0;
  for (let q = 1; q < N; q++) { const d = zsm[q] - zsm[q - 1]; if (d > 0) climb += d; }
  for (let q = 10; q < N; q++) { const g = Math.abs(zsm[q] - zsm[q - 10]) / (S[q] - S[q - 10]); if (g > maxG) maxG = g; }
  const zmin = Math.min(...zsm), zmax = Math.max(...zsm);
  // lanes, widths
  const laneDirOf = (e, dir) => {
    if (e.oneway) return e.profile.lanes;
    if (e.lset) return dir === 1 ? e.lset.nF : e.lset.nB;
    return Math.floor(e.profile.lanes / 2);
  };
  // per 5 m sample, so a trimmed route counts only what it races; a run shorter than 15 m does not set a minimum
  let lanesMin = 9, lanesMax = 0, twoPlus = 0, divided = 0, bridgeM = 0, wMin = 99, wMax = 0, rampM = 0, twoWayNarrow = 0;
  const names = [];
  for (let q = 0; q < N; q++) {
    const e = C.edges[edgeAt[q]];
    const ln = laneDirOf(e, dirAt[q]);
    if (C.edges[edgeAt[q]].length > 15) { lanesMin = Math.min(lanesMin, ln); wMin = Math.min(wMin, e.profile.width); }
    lanesMax = Math.max(lanesMax, ln); wMax = Math.max(wMax, e.profile.width);
    if (ln >= 2) twoPlus += 5;
    if (e.oneway && !e.link) divided += 5;
    if (!e.oneway && ln < 2) twoWayNarrow += 5;
    if (e.bridge) bridgeM += 5;
    if (e.link) rampM += 5;
    const nm = e.name || (e.link ? '(ramp)' : '(unnamed)');
    if (!names.length || names[names.length - 1].name !== nm) names.push({ name: nm, m: 5 }); else names[names.length - 1].m += 5;
  }
  const chainLen = Math.max(5, N * 5);
  // junction mouths + signals + turns at nodes
  let mouths = 0, signals = 0, stops = 0;
  const uturns = [];
  for (let q = 0; q < chain.length; q++) {
    const c = chain[q], e = C.edges[c.ei];
    const end = c.dir === 1 ? e.b : e.a;
    if (!cand.kind.startsWith('circuit') && q === chain.length - 1) break;
    const deg = C.nodeEdges[end].length;
    if (deg >= 3) mouths += deg - 2;
    if (C.nodes[end].ctl & 4) signals++;
    if (C.nodes[end].ctl & 2) stops++;
    const nx = chain[(q + 1) % chain.length], f = C.edges[nx.ei];
    const pa = edgePts(e, c.dir), pb = edgePts(f, nx.dir);
    const h1 = Math.atan2(pa[pa.length - 1][1] - pa[pa.length - 2][1], pa[pa.length - 1][0] - pa[pa.length - 2][0]);
    const h2 = Math.atan2(pb[1][1] - pb[0][1], pb[1][0] - pb[0][0]);
    const t = Math.abs(angDiff(h1, h2)) * 180 / Math.PI;
    if (t > 120) uturns.push({ node: end, deg: Math.round(t), lat: toLat(C.nodes[end].z), lon: toLon(C.nodes[end].x) });
  }
  // faults
  const faults = [];
  for (const F of FAULT_AREAS) {
    const fx = toX(F.lon), fz = toZ(F.lat);
    let dmin = 1e9;
    for (const p of P) dmin = Math.min(dmin, Math.hypot(p[0] - fx, p[1] - fz));
    if (dmin < F.r) faults.push({ label: F.label, d: Math.round(dmin), hard: true });
  }
  for (const c of chain) if (FAULT_EDGES.has(c.ei)) faults.push({ label: FAULT_EDGES.get(c.ei) + ' e' + c.ei, d: 0, hard: false });
  // start grid: best straight with >=2 lanes in the race direction
  const lanesAt = q => laneDirOf(C.edges[edgeAt[q]], dirAt[q]);
  const straightOK = (a, b) => { for (let q = a + 1; q <= b; q++) if (Math.abs(hs[q] - hs[a]) > 6 * Math.PI / 180) return false; return true; };
  let grid = null;
  if (cand.kind === 'circuit') {
    // longest straight run (heading within 6 deg of its start) with >= 2 lanes and no bridge
    let best = { len: 0 };
    for (let a = 0; a < N; a += 2) {
      if (lanesAt(a) < 2 || C.edges[edgeAt[a]].bridge) continue;
      let b = a;
      while (b + 1 < N && lanesAt(b + 1) >= 2 && !C.edges[edgeAt[b + 1]].bridge && Math.abs(hs[b + 1] - hs[a]) <= 6 * Math.PI / 180) b++;
      const len = S[b] - S[a];
      if (len > best.len) best = { len, a, b };
    }
    if (best.len > 0) {
      const q = Math.min(best.b, best.a + Math.round(Math.min(110, best.len * 0.7) / 5));
      grid = { len: Math.round(best.len), lineS: Math.round(S[q]), at: P[q], road: C.edges[edgeAt[q]].name, lanes: lanesAt(q), ok: best.len >= 90 };
    }
  } else {
    const lead = cand.leadM ?? 60;
    const a = 0, b = Math.min(N - 1, Math.round((lead + 80) / 5));
    let minL = 9; for (let q = a; q <= b; q++) minL = Math.min(minL, lanesAt(q));
    let run = 0; for (let q = 1; q < N && Math.abs(hs[q] - hs[0]) <= 6 * Math.PI / 180; q++) run = S[q];
    let twoWay = true; for (let q = a; q <= b; q++) if (C.edges[edgeAt[q]].oneway) twoWay = false;
    const today = !!cand.route && !(cand.cut && cand.cut.from && cand.cut.from !== 0);
    grid = { len: Math.round(run), lineS: lead, at: P[Math.round(lead / 5)], road: C.edges[edgeAt[Math.round(lead / 5)]].name, lanes: minL, twoWay, today,
      ok: today || (run >= lead + 25 && (minL >= 2 || twoWay)), closedOnly: minL < 2 };
  }
  // drag straightness: max lateral deviation from the chord over the run
  let dragDev = null;
  if (cand.kind === 'drag') {
    const lead = cand.leadM ?? 60, run = cand.runM;
    const a = Math.round(lead / 5), b = Math.min(N - 1, Math.round((lead + run) / 5));
    const [ax, az] = P[a], [bx, bz] = P[b];
    const L = Math.hypot(bx - ax, bz - az);
    let dev = 0;
    for (let q = a; q <= b; q++) dev = Math.max(dev, Math.abs(((P[q][0] - ax) * (bz - az) - (P[q][1] - az) * (bx - ax)) / L));
    let minL = 9; for (let q = a; q <= b; q++) minL = Math.min(minL, lanesAt(q));
    const grade = (zsm[b] - zsm[a]) / (S[b] - S[a]);
    dragDev = { dev: +dev.toFixed(1), minLanes: minL, grade: +(grade * 100).toFixed(1), chord: Math.round(L) };
  }
  return {
    id: cand.id, kind: cand.kind, name: cand.name, district: cand.district, fun: cand.fun, note: cand.note || '',
    length: Math.round(length), chainLen: Math.round(chainLen), edges: chain.length,
    corners: corners.length, tight, medium, fast, cornerList: corners,
    elev: { min: Math.round(zmin), max: Math.round(zmax), climb: Math.round(climb), maxGrade: +(maxG * 100).toFixed(1) },
    lanes: { min: lanesMin, max: lanesMax, twoPlusPct: Math.round(100 * twoPlus / chainLen) }, width: { min: +wMin.toFixed(1), max: +wMax.toFixed(1) },
    dividedPct: Math.round(100 * divided / chainLen), narrowPct: Math.round(100 * twoWayNarrow / chainLen), bridgeM: Math.round(bridgeM), rampM: Math.round(rampM),
    mouths, signals, stops, uturns, faults, grid, dragDev,
    names: names.filter(n => n.m >= 60).map(n => n.name),
    chain: chain.map(c => [c.ei, c.dir]),
    poly: P.filter((_, q) => q % 2 === 0).map(p => [+p[0].toFixed(1), +p[1].toFixed(1)]),
    startLL: [toLat(P[0][1]), toLon(P[0][0])],
    runM: cand.runM, leadM: cand.leadM, shutM: cand.shutM, cutOf: cand.cutOf, district2: cand.district,
  };
}

const only = process.argv[2] ? new Set(process.argv[2].split(',')) : null;
const res = [];
for (const c of CANDS) {
  if (only && !only.has(c.id)) continue;
  try {
    const r = analyze(c);
    res.push(r);
    const f = r.faults.length ? ' FAULTS: ' + r.faults.map(x => x.label + (x.hard ? ' (' + x.d + ' m)' : '')).join('; ') : '';
    const u = r.uturns.length ? ' UTURN: ' + r.uturns.map(x => x.deg + 'deg@n' + x.node).join(',') : '';
    console.log(`${r.id.padEnd(4)} ${r.kind.padEnd(7)} ${(r.length / 1000).toFixed(2)} km  corners ${r.corners} (t${r.tight}/m${r.medium}/f${r.fast})  lanes ${r.lanes.min}-${r.lanes.max} (${r.lanes.twoPlusPct}% 2+)  w ${r.width.min}-${r.width.max}  elev ${r.elev.min}-${r.elev.max} climb ${r.elev.climb} g${r.elev.maxGrade}%  sig ${r.signals} mouths ${r.mouths}  grid ${r.grid ? r.grid.len + 'm ' + (r.grid.ok ? 'ok' : 'SHORT') : '-'}${r.dragDev ? ' drag dev ' + r.dragDev.dev + 'm lanes ' + r.dragDev.minLanes + ' grade ' + r.dragDev.grade + '%' : ''}${u}${f}`);
    console.log('       ' + r.names.join(' > '));
  } catch (e) { console.log(`${c.id} FAILED: ${e.message}`); }
}
writeFileSync(new URL('cands.json', OUT), JSON.stringify(res));
