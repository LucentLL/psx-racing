/* Measurements behind the engine-audio fix: vendor rig vs our rigs, same profile.
   node tools/engine_audio_report.mjs <carId> [family] [--ref Rotary_x8_Free] [--mute regex] [--rig current|fixed]
   Prints: per-take pitch/volume vs rpm, voices audible at once, pitch clamping,
   energy per octave per segment, and steady (rpm-independent) spectral lines. */
import { evalCurve, loadHQ, PREFAB_OF, TAKES } from './res2_curves.mjs';
import * as P from './audio_render_probe.mjs';

const args = process.argv.slice(2);
const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
const pos = args.filter((a, i) => !a.startsWith('--') && !(i > 0 && args[i - 1].startsWith('--')));
const c = P.car(pos[0]), fam = pos[1] || c.engineFamily, ref = opt('--ref', PREFAB_OF[fam]);
const mute = opt('--mute', null) ? new RegExp(opt('--mute')) : null;
const fr = P.profile(c), seg = fr.seg;
const f1 = (v) => (v == null ? '  -  ' : v.toFixed(2).padStart(5));
console.log(`# ${c.name} | family ${fam} | idle ${c.idleRPM} redline ${c.redline} limiter ${c.revLimit} | voiceRateMul ${c.voiceRateMul} formant ${c.voicePeakHz} Hz +${c.voicePeakDb} dB shelf ${c.voiceShelfDb} dB`);

/* 1. per-take pitch and volume vs rpm (steady state, full throttle) ---------- */
const K = loadHQ(PREFAB_OF[fam]).curves;
const top = c.redline, S = top / c.idleRPM;
const BF = [0, 0.10, 0.22, 0.35, 0.48, 0.62, 0.75, 0.88];
const home = BF.map((f) => c.idleRPM * Math.pow(S, f));
console.log('\n## take pitch @ rpm  (vendor curve | current ladder; vol in brackets, full throttle; only takes with vol > 0.02)');
console.log('current ladder home rpm:', TAKES.map(([n], i) => `${n}=${home[i].toFixed(0)}`).join(' '));
const rpms = [c.idleRPM]; for (let r = 1000; r < c.revLimit; r += 500) rpms.push(r); rpms.push(c.revLimit);
for (const rpm of rpms) {
  const cv = rpm / c.revLimit, onL = evalCurve(K.onLoadVolumeByRPM, cv);
  const ven = TAKES.map(([n, , v, p]) => [n, evalCurve(K[v], cv) * onL, evalCurve(K[p], cv)]).filter((x) => x[1] > 0.02);
  const frac = Math.min(1, Math.log(Math.max(rpm, c.idleRPM) / c.idleRPM) / Math.log(S));
  let lo = 0, hi = 0; for (let i = 0; i < 7; i++) if (frac >= BF[i] && frac <= BF[i + 1]) { lo = i; hi = i + 1; break; }
  if (frac > BF[7]) lo = hi = 7;
  const t = lo === hi ? 0 : (frac - BF[lo]) / (BF[hi] - BF[lo]);
  const cur = [[lo, lo === hi ? 1 : 1 - t], [hi, t]].filter(([, g], j) => g > 0.02 && !(j === 1 && lo === hi))
    .map(([i, g]) => [TAKES[i][0], g, Math.min(1.6, Math.max(0.66, rpm / home[i] * (c.voiceRateMul || 1))), rpm / home[i] * (c.voiceRateMul || 1)]);
  console.log(String(rpm).padStart(5), '| V:', ven.map(([n, v, p]) => `${n} x${p.toFixed(2)}[${v.toFixed(2)}]`).join(' ').padEnd(78),
    '| C:', cur.map(([n, g, r, raw]) => `${n} x${r.toFixed(2)}${raw !== r ? '(CLAMP ' + raw.toFixed(2) + ')' : ''}[${g.toFixed(2)}]`).join(' '));
}

/* 2. renders + per-frame voice stats ------------------------------------------ */
function run(rig, label, ropt = {}) {
  const st = { aud: [], minP: [], maxP: [] };
  const x = P.render(rig, fr, {
    ...ropt,
    trace: (f, frame, r) => {
      if (mute) for (const v of r.voices) if (mute.test(v.label)) v.g = 0;
      const gs = r.voices.filter((v) => v.b && v.g > 0);
      const gmax = Math.max(1e-9, ...gs.map((v) => v.g));
      const aud = gs.filter((v) => v.g >= 0.1 * gmax && !/intake|agg/.test(v.label));
      st.aud.push(aud.length);
      st.minP.push(Math.min(...aud.map((v) => v.r))); st.maxP.push(Math.max(...aud.map((v) => v.r)));
    },
  });
  return { x, st, label };
}
const renders = [];
const vref = P.vendorRig(PREFAB_OF[fam], c);
renders.push(run(vref, `vendor ${PREFAB_OF[fam]}`, { acoustics: true }));
if (ref !== PREFAB_OF[fam]) renders.push(run(P.vendorRig(ref, c), `vendor ${ref}`, { acoustics: true }));
const cr = P.currentRig(fam, c);
renders.push(run(cr, 'current (+tone chain)', { tone: cr.tone }));
renders.push(run(P.currentRig(fam, c), 'current (dry)'));
const famFixed = P.canonical(fam);
const fx = run(P.fixedRig(famFixed, c), 'fixed ' + famFixed + ' (+new chain)');
fx.x = P.applyTone(fx.x, P.FLAT_VOICE, P.NEW_CHAIN);
renders.push(fx);
if (famFixed !== fam) renders.push(run(P.vendorRig(PREFAB_OF[famFixed], c), 'vendor ' + PREFAB_OF[famFixed], { acoustics: true }));

const segs = Object.entries(seg).filter(([n]) => n !== 'end').map(([n, a], i, arr) => [n, a, i + 1 < arr.length ? arr[i + 1][1] : seg.end]);
console.log('\n## voices audible at once (within 20 dB of the loudest band take) and pitch range of those voices');
for (const r of renders) {
  const row = segs.map(([n, a, b]) => {
    const i0 = Math.floor(a / P.FRAME_DT), i1 = Math.floor(b / P.FRAME_DT);
    const au = r.st.aud.slice(i0, i1), mn = Math.min(...r.st.minP.slice(i0, i1)), mx = Math.max(...r.st.maxP.slice(i0, i1));
    return `${n}: ${(au.reduce((s, v) => s + v, 0) / au.length).toFixed(1)}/${Math.max(...au)} x${mn.toFixed(2)}-${mx.toFixed(2)}`;
  });
  const two = r.st.aud.filter((v) => v >= 2).length / r.st.aud.length;
  console.log(r.label.padEnd(28), row.join('  '), ` >=2 voices ${(two * 100).toFixed(0)}% of frames`);
}

console.log('\n## energy per octave, dB re segment total (31.5 63 125 250 500 1k 2k 4k 8k 16k) and segment RMS');
for (const [n, a, b] of segs) {
  console.log(`-- ${n} ${a.toFixed(2)}-${b.toFixed(2)} s`);
  for (const r of renders) {
    const o = P.octaves(P.spectrum(r.x, a, b, 4096));
    console.log('  ' + r.label.padEnd(26), o.map((v) => v.toFixed(1).padStart(6)).join(''), '  rms', P.rmsDb(r.x, a, b).toFixed(1));
  }
}

console.log('\n## steady lines: prominence >8 dB over +-1/6 octave in EVERY window (windows at different rpm)');
const W = 0.38;
const wins = [[seg.idle + 0.2, seg.idle + 0.2 + W], [seg.pull2 + 0.3, seg.pull2 + 0.3 + W], [seg.pull2 + 1.4, seg.pull2 + 1.4 + W],
  [seg.pull3 + 0.6, seg.pull3 + 0.6 + W], [seg.coast + 0.5, seg.coast + 0.5 + W], [seg.end - W - 0.02, seg.end - 0.02]];
console.log('windows rpm:', wins.map(([a]) => fr[Math.floor(a / P.FRAME_DT)].rpm.toFixed(0)).join(' '));
for (const r of renders) {
  const L = P.steadyLines(r.x, wins);
  console.log(r.label.padEnd(28), L.length ? L.map((h) => `${h.hz}Hz(min+${h.minProm}dB per[${h.per}] lvl ${h.relDb}dB)`).join(' ') : 'none');
}
