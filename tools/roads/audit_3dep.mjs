// audit_3dep.mjs - check a baked mountain stage against USGS 3DEP ground.
//
//   node tools/roads/audit_3dep.mjs <prefix|all> [--out <dir>] [--offline] [--plan]
//        [--res <Resources dir>] [--art <Art/<stage> dir>]
//
// Reads the stage as the game ships it (Resources/<pre>_stage.json and the
// Art/<stage>/<pre>_dem_* grids) and measures, against the 1 m lidar DEM along
// the road and the 1/3" DEM over the grids:
//   1. the centreline against the road bench in the lidar ground,
//   2. the height profile, climb, descent and grades,
//   3. the bench width (pavement plus shoulders: an upper bound on the paved
//      width) and the OSM width / lanes tags,
//   4. the near (12 m) and far (60 m) grids on their own lattices,
//   5. bridges and tunnels: OSM tags against the spans the stage carries.
// Writes <out>/<pre>_audit.json (numbers and the series the plots are drawn
// from; tools/roads/audit_3dep_plots.py draws them). Downloads only what is
// not in the cache (lib3dep.mjs); --plan prints the tile count and stops.

import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { makeProjection, srtmSampler } from './lib.mjs';
import { open3dep, dem13, dem1m, STAGES, loadStage } from './lib3dep.mjs';
import { ribbon, findBench, gaussSmooth, RIBBON } from './bench.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..', '..');
const args = process.argv.slice(2);
const flag = n => args.includes(n);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : d; };
const which = args[0];
if (!which || (which !== 'all' && !STAGES[which])) {
  console.error('usage: node audit_3dep.mjs <' + Object.keys(STAGES).join('|') + '|all> [--out dir] [--offline] [--plan]');
  process.exit(2);
}
const OUT = opt('--out', join(ROOT, 'Docs', 'lidar_audit'));
const roadsCache = process.env.PSX_ROADS_CACHE || join(HERE, 'cache');
const store = open3dep({ offline: flag('--offline') });

const pct = (a, p) => { if (!a.length) return NaN; const s = Float64Array.from(a).sort(); return s[Math.min(s.length - 1, Math.max(0, Math.round(p * (s.length - 1))))]; };
const mean = a => a.reduce((x, y) => x + y, 0) / Math.max(1, a.length);
const rms = a => Math.sqrt(a.reduce((x, y) => x + y * y, 0) / Math.max(1, a.length));
const r2 = v => Math.round(v * 100) / 100;

/// Runs of consecutive stations where `test(i)` holds, at least minLen long.
function runs(n, test, spacing, minLen = 0) {
  const out = []; let a = -1;
  for (let i = 0; i <= n; i++) {
    const on = i < n && test(i);
    if (on && a < 0) a = i;
    if (!on && a >= 0) { if ((i - a) * spacing >= minLen) out.push([a, i - 1]); a = -1; }
  }
  return out;
}

async function auditStage(pre) {
  const S = loadStage(ROOT, pre, { resDir: opt('--res', null), artDir: opt('--art', null) });
  const { wp, h, n, loop, json, meta } = S;
  const SP = json.spacing || 4;
  const proj = makeProjection(json.lat0, json.lon0);
  console.log(`=== ${pre}: ${S.st.road} (${S.st.id}), ${(n * SP / 1000).toFixed(2)} km${loop ? ', loop' : ''} ===`);

  // ---------------------------------------------------- what to fetch
  const lidar = dem1m(store, 0), near13 = dem13(store, 0), far13 = dem13(store, 2);
  const corridor = [];
  for (let i = 0; i < n; i++) for (const dx of [-60, 0, 60]) for (const dz of [-60, 0, 60]) corridor.push(proj.toLL(wp[i].x + dx, wp[i].z + dz));
  const lattice = g => { const p = []; for (let r = 0; r < g.rows; r++) for (let c = 0; c < g.cols; c++) p.push(proj.toLL(g.originX + c * g.cell, g.originZ + r * g.cell)); return p; };
  const nearPts = lattice(meta.near), farPts = lattice(meta.far);
  const plan = { lidar: lidar.plan(corridor), near: near13.plan(nearPts), far: far13.plan(farPts) };
  console.log(`  tiles: 1 m ${plan.lidar.tiles} (${plan.lidar.missing} to fetch), 1/3" near ${plan.near.tiles} (${plan.near.missing}), far L2 ${plan.far.tiles} (${plan.far.missing})`);
  if (flag('--plan')) return null;
  await lidar.prepare(corridor); await near13.prepare(nearPts); await far13.prepare(farPts);
  console.log(`  read so far this run: ${(store.sessionBytes() / 1e6).toFixed(1)} MB`);

  // ------------------------------------------------ 1. the road bench
  const bridgeSpans = [];
  for (let k = 0; k + 1 < (json.bridges || []).length; k += 2) bridgeSpans.push([json.bridges[k], json.bridges[k + 1]]);
  const tunnelSpans = [];
  for (let k = 0; k + 1 < (json.tunnels || []).length; k += 2) tunnelSpans.push([json.tunnels[k], json.tunnels[k + 1]]);
  const lap = n * SP;
  const sAt = i => i * SP;
  /// do [a, b] and [p, q] (metres along; a loop's span may run past the lap) overlap?
  const overlap = (a, b, p, q) => (p < b && q > a) || (loop && ((p - lap < b && q - lap > a) || (p + lap < b && q + lap > a)));
  const inSpans = (spans, s, pad = 0) => spans.some(([a, b]) => (s >= a - pad && s <= b + pad) || (loop && (s + lap >= a - pad && s + lap <= b + pad)));

  // --------------------------------------------- 5. OSM tags on the route
  const osmPath = join(roadsCache, S.st.osm);
  let osm = null;
  if (existsSync(osmPath)) {
    const ways = JSON.parse(readFileSync(osmPath, 'utf8')).elements.filter(e => e.type === 'way' && e.geometry && e.tags && e.tags.highway);
    // bin the route
    const bin = new Map(), B = 50;
    for (let i = 0; i < n; i++) { const k = Math.floor(wp[i].x / B) + ':' + Math.floor(wp[i].z / B); (bin.get(k) || bin.set(k, []).get(k)).push(i); }
    const nearest = (x, z) => {
      let best = -1, bd = 1e18; const cx = Math.floor(x / B), cz = Math.floor(z / B);
      for (let a = cx - 1; a <= cx + 1; a++) for (let b = cz - 1; b <= cz + 1; b++) for (const i of bin.get(a + ':' + b) || []) {
        const d = (wp[i].x - x) ** 2 + (wp[i].z - z) ** 2; if (d < bd) { bd = d; best = i; }
      }
      return { i: best, d: Math.sqrt(bd) };
    };
    const onRoute = [], tagAt = new Array(n).fill(null);
    for (const w of ways) {
      const g = w.geometry.map(p => proj.toXZ(p.lat, p.lon));
      // densify to 4 m and ask how much of the way lies on the line
      let on = 0, tot = 0; const idx = [];
      for (let k = 0; k + 1 < g.length; k++) {
        const L = Math.hypot(g[k + 1].x - g[k].x, g[k + 1].z - g[k].z), m = Math.max(1, Math.round(L / 4));
        for (let j = 0; j < m; j++) {
          const x = g[k].x + (g[k + 1].x - g[k].x) * j / m, z = g[k].z + (g[k + 1].z - g[k].z) * j / m;
          const q = nearest(x, z); tot++;
          if (q.i >= 0 && q.d < 3) { on++; idx.push(q.i); }
        }
      }
      if (tot < 2 || on / tot < 0.6 || on < 3) continue;
      const lo = Math.min(...idx), hi = Math.max(...idx);
      onRoute.push({ id: w.id, fromM: sAt(lo), toM: sAt(hi), tags: w.tags });
      for (const i of idx) tagAt[i] = w.tags;
    }
    const tally = key => { const t = {}; for (let i = 0; i < n; i++) { const v = tagAt[i] ? (tagAt[i][key] ?? '(untagged)') : '(no way matched)'; t[v] = (t[v] || 0) + SP; } return t; };
    const span = test => runs(n, i => tagAt[i] && test(tagAt[i]), SP).map(([a, b]) => ({ fromM: sAt(a), toM: sAt(b) + SP, lengthM: (b - a + 1) * SP, name: tagAt[a].name || tagAt[a]['bridge:name'] || tagAt[a]['tunnel:name'] || null, layer: tagAt[a].layer || null }));
    const isBridge = t => t.bridge && t.bridge !== 'no', isTunnel = t => t.tunnel && t.tunnel !== 'no';
    osm = {
      file: S.st.osm, waysOnRoute: onRoute.length,
      matchedPct: r2(100 * tagAt.filter(Boolean).length / n),
      lanes: tally('lanes'), widthTag: tally('width'), surface: tally('surface'), maxspeed: tally('maxspeed'), highway: tally('highway'), ref: tally('ref'),
      bridges: span(isBridge), tunnels: span(isTunnel),
      // where the tags say the road is not a plain two-lane: lanes other than 2, or one-way
      notTwoLane: runs(n, i => tagAt[i] && ((tagAt[i].lanes && tagAt[i].lanes !== '2') || tagAt[i].oneway === 'yes'), SP)
        .map(([a, b]) => ({ fromM: sAt(a), toM: sAt(b) + SP, lanes: tagAt[a].lanes || null, oneway: tagAt[a].oneway || null, name: tagAt[a].name || null, ref: tagAt[a].ref || null })),
    };
    for (const t of osm.tunnels) t.inGame = tunnelSpans.some(([p, q]) => overlap(t.fromM, t.toM, p, q)) ? 'yes' : 'NO';
  }
  const structureBase = i => inSpans(bridgeSpans, i * SP, 12) || inSpans(tunnelSpans, i * SP, 12);
  const osmSpans0 = osm ? [...osm.bridges, ...osm.tunnels].map(o => [o.fromM, o.toM]) : [];
  const structure = i => structureBase(i) || inSpans(osmSpans0, i * SP, 10);
  const rib = ribbon(wp, loop, (x, z) => { const ll = proj.toLL(x, z); return lidar.sample(ll.lat, ll.lon); });
  const bench = findBench(rib, { skip: structure });

  const offs = [], offAbs = [];
  for (let i = 0; i < n; i++) if (bench.conf[i]) { offs.push(bench.off[i]); offAbs.push(Math.abs(bench.off[i])); }
  // mean east / north offset of the bench from the line (the datum question)
  let me = 0, mn = 0, mk = 0;
  for (let i = 0; i < n; i++) if (bench.conf[i]) { me += bench.off[i] * rib.nx[i]; mn += bench.off[i] * rib.nz[i]; mk++; }
  const offRuns = runs(n, i => Math.abs(bench.offF[i]) > 2, SP, 12).map(([a, b]) => {
    let pk = 0, sum = 0; for (let i = a; i <= b; i++) { if (Math.abs(bench.offF[i]) > Math.abs(pk)) pk = bench.offF[i]; sum += bench.offF[i]; }
    return { fromM: sAt(a), toM: sAt(b), lengthM: (b - a + 1) * SP, peakM: r2(pk), meanM: r2(sum / (b - a + 1)) };
  }).sort((p, q) => Math.abs(q.peakM) - Math.abs(p.peakM));
  const line = {
    stations: n, confident: offs.length, confidentPct: r2(100 * offs.length / n),
    medianAbsM: r2(pct(offAbs, 0.5)), p95AbsM: r2(pct(offAbs, 0.95)), worstAbsM: r2(pct(offAbs, 1)),
    worstAtM: sAt(bench.off.findIndex((v, i) => bench.conf[i] && Math.abs(v) === pct(offAbs, 1))),
    meanSignedM: r2(mean(offs)), meanEastM: r2(me / Math.max(1, mk)), meanNorthM: r2(mn / Math.max(1, mk)),
    over2mPct: r2(100 * offAbs.filter(v => v > 2).length / Math.max(1, offAbs.length)),
    over2mRuns: offRuns.length, over2mRunsM: offRuns.reduce((a, r) => a + r.lengthM, 0), worstRuns: offRuns.slice(0, 12),
  };
  console.log(`  line: bench found at ${line.confidentPct}% of stations; |offset| median ${line.medianAbsM} m, p95 ${line.p95AbsM}, worst ${line.worstAbsM} at ${line.worstAtM} m; ${line.over2mRuns} stretches > 2 m (${line.over2mRunsM} m)`);

  // -------------------------------------------------- 2. the profile
  // The real road's height: the lidar ground on the bench (the filtered
  // offset), bridged linearly across decks and tunnels, then a short gaussian
  // (sigma 8 m) so a 4 m difference of two lidar posts is not read as grade.
  const GS = 8;
  const real = gaussSmooth(bench.hRoad, GS / SP, loop);
  const game = Float64Array.from(h);
  const gameS = gaussSmooth(game, GS / SP, loop);
  const elevSrtm = await srtmSampler((() => {
    let s = 90, w = 180, nn = -90, e = -180;
    for (const p of [proj.toLL(wp[0].x, wp[0].z), ...corridor]) { s = Math.min(s, p.lat); nn = Math.max(nn, p.lat); w = Math.min(w, p.lon); e = Math.max(e, p.lon); }
    return { s, w, n: nn, e };
  })(), roadsCache);
  const srtmRaw = new Float64Array(n), lidarCl = new Float64Array(n), third = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    const ll = proj.toLL(wp[i].x, wp[i].z);
    srtmRaw[i] = elevSrtm(ll.lat, ll.lon); lidarCl[i] = rib.H[i * rib.K + rib.K0]; third[i] = near13.sample(ll.lat, ll.lon);
  }
  const dh = [], dSrtm = [];
  for (let i = 0; i < n; i++) { dh.push(game[i] - real[i]); if (!structure(i)) dSrtm.push(srtmRaw[i] - real[i]); }
  const grade = a => { const g = new Float64Array(n); for (let i = 0; i < n; i++) { const p = loop ? (i - 1 + n) % n : Math.max(0, i - 1), q = loop ? (i + 1) % n : Math.min(n - 1, i + 1); g[i] = (a[q] - a[p]) / ((q === p ? 1 : (loop || (i > 0 && i < n - 1) ? 2 : 1)) * SP); } return g; };
  const gReal = grade(real), gGame = grade(gameS);
  const absG = a => Array.from(a, Math.abs);
  const climb = a => { let up = 0, dn = 0; const m = loop ? n : n - 1; for (let i = 0; i < m; i++) { const d = a[(i + 1) % n] - a[i]; if (d > 0) up += d; else dn -= d; } return { up: r2(up), down: r2(dn) }; };
  const bias = mean(dh);
  const dhAbs = dh.map(v => Math.abs(v)), dhDet = dh.map(v => Math.abs(v - bias));
  const worstH = dhAbs.indexOf(Math.max(...dhAbs));
  const gDiff = Array.from(gGame, (v, i) => v - gReal[i]);
  const gradeRuns = runs(n, i => Math.abs(gDiff[i]) > 0.01, SP, 20).map(([a, b]) => {
    let pk = 0, at = a; for (let i = a; i <= b; i++) if (Math.abs(gDiff[i]) > Math.abs(pk)) { pk = gDiff[i]; at = i; }
    return { fromM: sAt(a), toM: sAt(b), lengthM: (b - a + 1) * SP, peakDiffPct: r2(pk * 100), realPct: r2(gReal[at] * 100), gamePct: r2(gGame[at] * 100) };
  });
  const heightRuns = runs(n, i => Math.abs(dh[i] - bias) > 3, SP, 20).map(([a, b]) => {
    let pk = 0; for (let i = a; i <= b; i++) if (Math.abs(dh[i] - bias) > Math.abs(pk)) pk = dh[i] - bias;
    return { fromM: sAt(a), toM: sAt(b), lengthM: (b - a + 1) * SP, peakM: r2(pk) };
  });
  const maxAt = a => { let k = 0; for (let i = 1; i < n; i++) if (Math.abs(a[i]) > Math.abs(a[k])) k = i; return k; };
  const profile = {
    smoothingSigmaM: GS,
    startReal: r2(real[0]), endReal: r2(real[n - 1]), startGame: r2(game[0]), endGame: r2(game[n - 1]),
    minReal: r2(Math.min(...real)), maxReal: r2(Math.max(...real)), minGame: r2(Math.min(...game)), maxGame: r2(Math.max(...game)),
    biasM: r2(bias), rmsM: r2(rms(dh)), p95AbsM: r2(pct(dhAbs, 0.95)), worstM: r2(dh[worstH]), worstAtM: sAt(worstH),
    p95AfterBiasM: r2(pct(dhDet, 0.95)), worstAfterBiasM: r2(Math.max(...dhDet)),
    srtmRawBiasM: r2(mean(dSrtm)), srtmRawRmsM: r2(rms(dSrtm)), srtmRawWorstM: r2(dSrtm.reduce((a, v) => Math.abs(v) > Math.abs(a) ? v : a, 0)),
    climbReal: climb(real), climbGame: climb(gameS),
    gradeReal: { meanPct: r2(mean(absG(gReal)) * 100), p95Pct: r2(pct(absG(gReal), 0.95) * 100), maxPct: r2(Math.abs(gReal[maxAt(gReal)]) * 100), maxAtM: sAt(maxAt(gReal)) },
    gradeGame: { meanPct: r2(mean(absG(gGame)) * 100), p95Pct: r2(pct(absG(gGame), 0.95) * 100), maxPct: r2(Math.abs(gGame[maxAt(gGame)]) * 100), maxAtM: sAt(maxAt(gGame)) },
    gradeRmsDiffPct: r2(rms(gDiff) * 100),
    gradeOff1pctRuns: gradeRuns.length, gradeOff1pctM: gradeRuns.reduce((a, r) => a + r.lengthM, 0),
    gradeOff1pctShare: r2(100 * gradeRuns.reduce((a, r) => a + r.lengthM, 0) / (n * SP)),
    worstGradeRuns: gradeRuns.slice().sort((p, q) => Math.abs(q.peakDiffPct) - Math.abs(p.peakDiffPct)).slice(0, 12),
    realOver8p5pctM: runs(n, i => Math.abs(gReal[i]) > 0.085, SP).reduce((a, [p, q]) => a + (q - p + 1) * SP, 0),
    gameOver8p5pctM: runs(n, i => Math.abs(gGame[i]) > 0.085, SP).reduce((a, [p, q]) => a + (q - p + 1) * SP, 0),
    heightOff3mRuns: heightRuns.length, heightOff3mM: heightRuns.reduce((a, r) => a + r.lengthM, 0),
    worstHeightRuns: heightRuns.slice().sort((p, q) => Math.abs(q.peakM) - Math.abs(p.peakM)).slice(0, 12),
  };
  console.log(`  profile: game - real bias ${profile.biasM} m, rms ${profile.rmsM}, worst ${profile.worstM} at ${profile.worstAtM} m; climb real +${profile.climbReal.up}/-${profile.climbReal.down} game +${profile.climbGame.up}/-${profile.climbGame.down}`);
  console.log(`  grade: real mean ${profile.gradeReal.meanPct}% p95 ${profile.gradeReal.p95Pct}% max ${profile.gradeReal.maxPct}% (at ${profile.gradeReal.maxAtM} m); game ${profile.gradeGame.meanPct}/${profile.gradeGame.p95Pct}/${profile.gradeGame.maxPct}%; off by > 1% over ${profile.gradeOff1pctShare}% of the route`);

  // ---------------------------------------------------- 3. the width
  const wB = []; for (let i = 0; i < n; i++) if (bench.conf[i]) wB.push(bench.width[i]);
  const width = { benchMedianM: r2(pct(wB, 0.5)), benchP10M: r2(pct(wB, 0.1)), benchP90M: r2(pct(wB, 0.9)),
    note: 'bench = ground within 12% cross slope around the road in the 1 m DEM: pavement plus shoulders and any flat verge, so an upper bound on the paved width' };
  // wide spots: pull-offs and overlooks worth having (bench 5 m wider than the median for 24 m or more)
  width.wideSpots = runs(n, i => bench.conf[i] && bench.width[i] > width.benchMedianM + 5, SP, 24).map(([a, b]) => {
    let w = 0, side = 0; for (let i = a; i <= b; i++) { w = Math.max(w, bench.width[i]); side += bench.off[i]; }
    return { fromM: sAt(a), toM: sAt(b), lengthM: (b - a + 1) * SP, maxBenchM: r2(w), side: side > 0 ? 'left' : 'right' };
  });

  if (osm) {
    // the ground under each OSM span: how deep the lidar falls below the road line
    for (const b of osm.bridges) {
      let deep = 0; const a = Math.round(b.fromM / SP), z = Math.min(n - 1, Math.round(b.toM / SP));
      for (let i = a; i <= z; i++) { const lin = real[a] + (real[z] - real[a]) * (i - a) / Math.max(1, z - a); deep = Math.max(deep, lin - lidarCl[i]); }
      b.groundBelowDeckM = r2(deep);
      b.inGame = bridgeSpans.some(([p, q]) => overlap(b.fromM, b.toM, p, q)) ? 'yes' : 'NO';
    }
  }
  const structures = {
    gameBridges: bridgeSpans.map(([a, b]) => ({ fromM: a, toM: b, lengthM: r2(b - a), inOsm: osm ? (osm.bridges.some(o => overlap(o.fromM, o.toM, a, b)) ? 'yes' : 'NO') : '?' })),
    gameTunnels: tunnelSpans.map(([a, b]) => ({ fromM: a, toM: b, lengthM: r2(b - a), inOsm: osm ? (osm.tunnels.some(o => overlap(o.fromM, o.toM, a, b)) ? 'yes' : 'NO') : '?' })),
    osmBridges: osm ? osm.bridges : null, osmTunnels: osm ? osm.tunnels : null,
  };
  // places where the lidar ground drops away under the line and neither the stage nor OSM says bridge
  structures.unmappedDips = bench.dips.map(([a, b]) => {
    let deep = 0; for (let i = a; i <= b; i++) deep = Math.max(deep, real[i] - lidarCl[i]);
    return { fromM: sAt(a), toM: sAt(b), lengthM: (b - a + 1) * SP, groundBelowRoadM: r2(deep) };
  });

  // -------------------------------------------------- 4. the terrain
  const terrain = {};
  const distToRoad = (() => {
    const B = 250, bin = new Map();
    for (let i = 0; i < n; i += 2) { const k = Math.floor(wp[i].x / B) + ':' + Math.floor(wp[i].z / B); (bin.get(k) || bin.set(k, []).get(k)).push(i); }
    const cells = [...bin.keys()].map(k => k.split(':').map(Number));
    return (x, z) => {
      const cx = Math.floor(x / B), cz = Math.floor(z / B);
      let bd = 1e18, bi = -1;
      const scan = (a, b) => { for (const i of bin.get(a + ':' + b) || []) { const d = (wp[i].x - x) ** 2 + (wp[i].z - z) ** 2; if (d < bd) { bd = d; bi = i; } } };
      for (let ring = 0; ring < 4; ring++) {
        for (let a = cx - ring; a <= cx + ring; a++) for (let b = cz - ring; b <= cz + ring; b++) if (Math.max(Math.abs(a - cx), Math.abs(b - cz)) === ring) scan(a, b);
        if (bi >= 0 && Math.sqrt(bd) < ring * B) break;
      }
      if (bi < 0) for (const [a, b] of cells) { const dx = Math.max(0, Math.abs((a + 0.5) * B - x) - B / 2), dz = Math.max(0, Math.abs((b + 0.5) * B - z) - B / 2); if (dx * dx + dz * dz < bd) scan(a, b); }
      return { d: Math.sqrt(bd), i: bi };
    };
  })();
  const grids = {};
  for (const [name, dem] of [['near', near13], ['far', far13]]) {
    const g = S.grid(name);
    const diff = new Float32Array(g.cols * g.rows), dist = new Float32Array(g.cols * g.rows);
    const bands = name === 'near' ? [[0, 30], [30, 100], [100, 500], [500, 1e9]] : [[0, 100], [100, 500], [500, 2300], [2300, 1e9]];
    const acc = bands.map(() => ({ n: 0, s: 0, s2: 0, worst: 0, wx: 0, wz: 0 }));
    const all = { n: 0, s: 0, s2: 0, worst: 0, wx: 0, wz: 0 };
    for (let r = 0; r < g.rows; r++) for (let c = 0; c < g.cols; c++) {
      const x = g.originX + c * g.cell, z = g.originZ + r * g.cell, ll = proj.toLL(x, z);
      const d = g.data[r * g.cols + c] / 10 + meta.baseM - dem.sample(ll.lat, ll.lon);
      const q = distToRoad(x, z);
      diff[r * g.cols + c] = d; dist[r * g.cols + c] = q.d;
      for (const a of [all, acc[bands.findIndex(b => q.d >= b[0] && q.d < b[1])]]) {
        if (!Number.isFinite(d)) continue;
        a.n++; a.s += d; a.s2 += d * d; if (Math.abs(d) > Math.abs(a.worst)) { a.worst = d; a.wx = x; a.wz = z; }
      }
    }
    const fin = a => ({ points: a.n, biasM: r2(a.s / Math.max(1, a.n)), rmsM: r2(Math.sqrt(a.s2 / Math.max(1, a.n))), worstM: r2(a.worst), worstAt: [Math.round(a.wx), Math.round(a.wz)] });
    // the biggest wrong landforms: block means over 600 m (near: 240 m) squares
    const blk = name === 'near' ? 20 : 10, forms = [];
    for (let r0 = 0; r0 + blk <= g.rows; r0 += blk) for (let c0 = 0; c0 + blk <= g.cols; c0 += blk) {
      let s = 0, k = 0, dmin = 1e9, wi = 0;
      for (let r = r0; r < r0 + blk; r++) for (let c = c0; c < c0 + blk; c++) { const v = diff[r * g.cols + c]; if (Number.isFinite(v)) { s += v; k++; } if (dist[r * g.cols + c] < dmin) { dmin = dist[r * g.cols + c]; } }
      if (!k) continue;
      const x = g.originX + (c0 + blk / 2) * g.cell, z = g.originZ + (r0 + blk / 2) * g.cell;
      forms.push({ x: Math.round(x), z: Math.round(z), meanM: r2(s / k), sizeM: blk * g.cell, roadM: Math.round(dmin), nearestStationM: sAt(distToRoad(x, z).i) });
    }
    forms.sort((a, b) => Math.abs(b.meanM) - Math.abs(a.meanM));
    terrain[name] = {
      cellM: g.cell, cols: g.cols, rows: g.rows, source: name === 'near' ? '3DEP 1/3" level 0 (~9 m)' : '3DEP 1/3" level 2 (~37 m)',
      all: fin(all), bands: bands.map((b, k) => ({ fromM: b[0], toM: b[1] > 1e8 ? null : b[1], ...fin(acc[k]) })),
      worstBlocks: forms.slice(0, 8),
    };
    grids[name] = { diff, cols: g.cols, rows: g.rows, originX: g.originX, originZ: g.originZ, cell: g.cell };
    const t = terrain[name];
    console.log(`  ${name} grid: game - 3DEP bias ${t.all.biasM} m, rms ${t.all.rmsM}, worst ${t.all.worstM}; ` + t.bands.map(b => `${b.fromM}-${b.toM ?? 'edge'} m rms ${b.rmsM} worst ${b.worstM}`).join('; '));
  }

  // ---------------------------------------------------------- write
  mkdirSync(OUT, { recursive: true });
  const round = (a, d = 2) => Array.from(a, v => Number.isFinite(v) ? +v.toFixed(d) : null);
  const out = {
    schema: 1, stage: pre, id: S.st.id, road: S.st.road, baker: S.st.baker, audited: new Date().toISOString(),
    lengthM: n * SP, loop, spacingM: SP, baseM: json.baseM, lat0: json.lat0, lon0: json.lon0,
    startLineM: json.startLineM, finishM: json.finishM,
    sources: { lidar: 'USGS 3DEP 1 m DEM, NC_Phase5_2018_A18 (NAD83 / UTM 17N, NAVD88)', third: 'USGS 3DEP 1/3 arc-second DEM (NAD83, NAVD88)', srtm: 'SRTM 1 arc-second (the bake\'s own source, tools/roads/cache/*.hgt.gz)' },
    line, profile, width, osm, structures, terrain,
    series: {
      sM: Array.from({ length: n }, (_, i) => sAt(i)), x: round(wp.map(p => p.x)), z: round(wp.map(p => p.z)),
      game: round(game), real: round(real), lidarCentreline: round(lidarCl), third: round(third), srtmRaw: round(srtmRaw),
      gradeReal: round(gReal, 4), gradeGame: round(gGame, 4),
      benchOff: round(bench.off), benchOffFiltered: round(bench.offF), benchWidth: round(bench.width, 1), benchConf: Array.from(bench.conf),
      structure: Array.from({ length: n }, (_, i) => structure(i) ? 1 : 0),
    },
  };
  writeFileSync(join(OUT, pre + '_audit.json'), JSON.stringify(out));
  // the grid differences, for the terrain maps (float32, row-major from the SW corner)
  for (const name of ['near', 'far']) {
    const g = grids[name];
    writeFileSync(join(OUT, `${pre}_${name}_diff.f32`), Buffer.from(g.diff.buffer));
  }
  writeFileSync(join(OUT, `${pre}_ribbon.f32`), Buffer.from(rib.H.buffer));     // n x K heights across the line
  out.ribbon = { stations: rib.n, across: rib.K, stepM: rib.stepM, halfM: rib.halfM };
  writeFileSync(join(OUT, pre + '_audit.json'), JSON.stringify(out));
  console.log(`  wrote ${join(OUT, pre + '_audit.json')}`);
  return out;
}

const list = which === 'all' ? Object.keys(STAGES) : [which];
for (const pre of list) await auditStage(pre);
store.saveLedger();
console.log(`read this run: ${(store.sessionBytes() / 1e6).toFixed(1)} MB; cache ledger total ${((store.ledger.totalBytesRead || 0) / 1e6).toFixed(1)} MB in ${store.ledger.totalTiles || 0} tiles (${store.dir})`);
