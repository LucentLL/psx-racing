/* Which rotary pack should the RX-7s speak through? Ear-proxy distance of every
   rotary pack, each played through the vendor rig on the same car and profile,
   from the owner's reference (Rotary_x8_Free through the vendor rig).
   node tools/rotary_family_pick.mjs [carId] */
import * as P from './audio_render_probe.mjs';
const c = P.car(process.argv[2] || 'mazda_rx_7_type_r__fd__j___91');
const REF = 'Rotary_x8_Free', CANDS = ['Rotary_x8_Free', 'Rotary_x7', 'Rotary_x8_1', 'Rotary_x8_2', 'Rotary_x8_3', 'Rotary_x3', 'Rotary_4_Rotor'];
const SR = P.SAMPLE_RATE, fr = P.profile(c), seg = fr.seg;
const segs = ['idle', 'rev', 'lift', 'pull2', 'pull3', 'coast'].map((n) => { const ks = Object.keys(seg); return [n, seg[n], seg[ks[ks.indexOf(n) + 1]]]; });
function logSpec(x, a, b) {
  const p = P.spectrum(x, a, b, 8192), df = SR / p.n, nb = Math.ceil(24 * Math.log2(4000 / 50)), out = new Float64Array(nb);
  for (let i = 0; i < nb; i++) { const f1 = 50 * 2 ** (i / 24), f2 = 50 * 2 ** ((i + 1) / 24); let s = 1e-20; for (let k = Math.floor(f1 / df); k <= Math.ceil(f2 / df); k++) s += p[k] || 0; out[i] = Math.log10(s); }
  const m = out.reduce((s, v) => s + v, 0) / nb; return out.map((v) => v - m);
}
function align(a, ref) {
  let best = 0, bs = -Infinity;
  for (let s = -24; s <= 24; s++) { let num = 0, na = 0, nb = 0; for (let b = 0; b < ref.length; b++) { const j = b + s; if (j < 0 || j >= a.length) continue; num += a[j] * ref[b]; na += a[j] * a[j]; nb += ref[b] * ref[b]; } const r = num / Math.sqrt(na * nb + 1e-20); if (r > bs) { bs = r; best = s; } }
  return [best / 2, bs];
}
const hold = (rpm, thr) => Array.from({ length: 60 }, () => ({ rpm, thr, kmh: 80 }));
const R = {}, H = {};
for (const p of CANDS) {
  R[p] = P.render(P.vendorRig(p, c), fr, { acoustics: true });
  H[p] = [];
  for (let rpm = 1000; rpm <= c.revLimit; rpm += 1000) for (const thr of [1, 0]) H[p].push(logSpec(P.render(P.vendorRig(p, c), hold(rpm, thr)), 0.35, 1.0));
}
console.log(`${c.name}: each rotary pack through the vendor rig vs ${REF} (the owner's reference)`);
console.log('pack'.padEnd(16), 'octave-balance distance dB (idle rev lift pull2 pull3 coast | mean)', '  steady-rpm spectra: mean |shift| st, mean r');
for (const p of CANDS) {
  const d = segs.map(([, a, b]) => { const o = P.octaves(P.spectrum(R[p], a, b, 4096)), q = P.octaves(P.spectrum(R[REF], a, b, 4096)); return Math.sqrt(o.slice(1, 9).reduce((s, v, i) => s + (v - q[i + 1]) ** 2, 0) / 8); });
  const al = H[p].map((h, i) => align(h, H[REF][i]));
  console.log(p.padEnd(16), d.map((v) => v.toFixed(1).padStart(5)).join(''), ' |', (d.reduce((s, v) => s + v, 0) / d.length).toFixed(2).padStart(5),
    '        ', (al.reduce((s, [sh]) => s + Math.abs(sh), 0) / al.length).toFixed(1).padStart(5), (al.reduce((s, [, r]) => s + r, 0) / al.length).toFixed(2).padStart(6));
}
