/* How far (in semitones) a rig's engine note sits from the vendor rig's at the
   same steady rpm: render each rig holding the rpm, take log-frequency spectra
   (1/24 octave, 50 Hz-4 kHz) and find the shift that best aligns them.
   node tools/engine_pitch_shift.mjs <carId> [family] [throttle 0|1] */
import * as P from './audio_render_probe.mjs';
import { PREFAB_OF } from './res2_curves.mjs';
const [id, famArg, thrArg] = process.argv.slice(2);
const c = P.car(id), fam = famArg || c.engineFamily, thr = thrArg == null ? 1 : +thrArg;
const SR = P.SAMPLE_RATE, BINS = 24 * Math.log2(4000 / 50);
function logSpec(x) {
  const p = P.spectrum(x, 0.35, 1.0, 8192), df = SR / p.n, out = new Float64Array(Math.ceil(BINS));
  for (let b = 0; b < out.length; b++) {
    const f1 = 50 * 2 ** (b / 24), f2 = 50 * 2 ** ((b + 1) / 24);
    let s = 1e-20; for (let k = Math.floor(f1 / df); k <= Math.ceil(f2 / df); k++) s += p[k] || 0;
    out[b] = Math.log10(s);
  }
  const m = out.reduce((a, v) => a + v, 0) / out.length; for (let b = 0; b < out.length; b++) out[b] -= m;
  return out;
}
function bestShift(a, ref) {
  let best = 0, bs = -Infinity;
  for (let s = -36; s <= 36; s++) {                  // half-semitone steps, +-18 st
    let num = 0, na = 0, nb = 0;
    for (let b = 0; b < ref.length; b++) { const j = b + s; if (j < 0 || j >= a.length) continue; num += a[j] * ref[b]; na += a[j] * a[j]; nb += ref[b] * ref[b]; }
    const r = num / Math.sqrt(na * nb + 1e-20);
    if (r > bs) { bs = r; best = s; }
  }
  return [best / 2, bs];
}
const hold = (rpm) => Array.from({ length: 60 }, () => ({ rpm, thr, kmh: 60 }));
const kinds = {
  vendor: () => P.vendorRig(PREFAB_OF[fam], c), current: () => P.currentRig(fam, c), fixed: () => P.fixedRig(fam, c, { rateMul: 1 }),
};
console.log(`${c.name} ${fam} throttle ${thr}: shift of each rig's spectrum vs the vendor rig (semitones, + = higher), corr`);
for (let rpm = Math.max(c.idleRPM, 1000); rpm <= c.revLimit; rpm += 500) {
  const ref = logSpec(P.render(kinds.vendor(), hold(rpm)));
  const row = ['current', 'fixed'].map((k) => { const [s, r] = bestShift(logSpec(P.render(kinds[k](), hold(rpm))), ref); return `${k} ${s >= 0 ? '+' : ''}${s.toFixed(1)} (r ${r.toFixed(2)})`; });
  console.log(String(rpm).padStart(5), row.join('   '));
}
