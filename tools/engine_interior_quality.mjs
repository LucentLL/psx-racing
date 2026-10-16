/* How much bitrate the engine takes need on the web, measured rather than guessed.

   Unity WebGL ships every AudioClip as AAC, encoded by the HOST's encoder
   (Unity manual, "Audio in Web": on Windows that is Media Foundation, the
   same encoder ffmpeg exposes as aac_mf, which only offers 96/128/160/192 kbps).
   For a sample of exterior and interior (cockpit) takes this encodes each
   WAV original with aac_mf at every rate, decodes it, aligns it (encoder
   delay) and reports:
     - snr       whole-band signal-to-error ratio, dB
     - snrHi     the same above 4 kHz (where an AAC encoder spends its savings)
     - hf10      the share of the ORIGINAL's energy above 10 kHz, dB re total
                 (an interior take with almost nothing up there loses nothing
                 audible when the encoder drops it)
     - kB        encoded size
   plus the libvorbis q settings for the repo's source .ogg files.

   Usage: node tools/engine_interior_quality.mjs [family ...]   (needs ffmpeg) */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { RES2, PREFAB_OF } from './res2_curves.mjs';

const SR = 44100;
const TMP = fs.mkdtempSync(path.join(os.tmpdir(), 'engq-'));
const pcm = (file) => {
  const b = execFileSync('ffmpeg', ['-v', 'error', '-i', file, '-ac', '1', '-ar', String(SR), '-f', 'f32le', '-'], { maxBuffer: 1 << 28 });
  return new Float32Array(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength));
};
function encode(src, args, ext) {
  const out = path.join(TMP, 'x.' + ext);
  execFileSync('ffmpeg', ['-v', 'error', '-y', '-i', src, ...args, out]);
  return { bytes: fs.statSync(out).size, x: pcm(out) };
}
/** One-pole-pair high-pass at fc (biquad), for the >4 kHz error. */
function hp(x, fc) {
  const w = 2 * Math.PI * fc / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * 0.707), a0 = 1 + al;
  const b0 = (1 + c) / 2 / a0, b1 = -(1 + c) / a0, b2 = b0, a1 = -2 * c / a0, a2 = (1 - al) / a0;
  const y = new Float32Array(x.length); let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  for (let i = 0; i < x.length; i++) { const v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v; }
  return y;
}
/** Best lag (decoded vs original) in 0..4096 samples, by correlation on a window. */
function lagOf(a, b) {
  let best = 0, bs = -Infinity;
  const n = Math.min(a.length, b.length) - 4200, w = Math.min(n, SR / 2);
  for (let L = 0; L <= 4096; L++) {
    let s = 0; for (let i = 1000; i < 1000 + w; i += 3) s += a[i] * b[i + L];
    if (s > bs) { bs = s; best = L; }
  }
  return best;
}
function snr(a, b, lag) {
  let sig = 0, err = 0;
  const n = Math.min(a.length, b.length - lag) - 2048;
  for (let i = 2048; i < n; i++) { const d = a[i] - b[i + lag]; sig += a[i] * a[i]; err += d * d; }
  return 10 * Math.log10(sig / Math.max(err, 1e-20));
}
function hfShare(x, fc) {
  const h = hp(hp(x, fc), fc); let e = 0, t = 0;
  for (let i = 0; i < x.length; i++) { e += h[i] * h[i]; t += x[i] * x[i]; }
  return 10 * Math.log10(e / Math.max(t, 1e-20) + 1e-12);
}

const fams = process.argv.slice(2).length ? process.argv.slice(2) : ['rotary_x7', 'i6_japanese_2', 'v8_american_classic_1', 'i4_japanese_2'];
const TAKES = ['idle', 'low_on', 'med_on', 'high_on', 'very_high_off'];
const RATES = [96, 128, 160, 192];
const rows = [];
for (const fam of fams) {
  for (const set of ['ext', 'int']) {
    for (const t of TAKES) {
      const dir = path.join(RES2, 'Sounds', PREFAB_OF[fam] === 'Rotary_x8_Free' ? 'Rotary_x8_free' : PREFAB_OF[fam]);
      const src = set === 'int' ? path.join(dir, 'Interior', 'int_' + t + '.wav') : path.join(dir, t + '.wav');
      if (!fs.existsSync(src)) continue;
      const ref = pcm(src), hiRef = hp(hp(ref, 4000), 4000);
      const row = { fam, set, take: t, hf10: +hfShare(ref, 10000).toFixed(1) };
      for (const r of RATES) {
        const e = encode(src, ['-c:a', 'aac_mf', '-b:a', r + 'k'], 'm4a');
        const lag = lagOf(ref, e.x);
        row['aac' + r] = { snr: +snr(ref, e.x, lag).toFixed(1), snrHi: +snr(hiRef, hp(hp(e.x, 4000), 4000), lag).toFixed(1), kB: Math.round(e.bytes / 1024) };
      }
      for (const q of [4, 5, 6]) {
        const e = encode(src, ['-c:a', 'libvorbis', '-q:a', String(q)], 'ogg');
        row['ogg' + q] = { kB: Math.round(e.bytes / 1024) };
      }
      rows.push(row);
    }
  }
}
const avg = (set, key, f) => { const r = rows.filter((x) => x.set === set); return (r.reduce((s, x) => s + f(x[key]), 0) / r.length).toFixed(1); };
console.log('per take (snr / snrHi dB, kB):');
for (const r of rows) console.log(`${r.fam.padEnd(22)} ${r.set} ${r.take.padEnd(14)} hf>10k ${String(r.hf10).padStart(6)} dB  ` +
  RATES.map((k) => `${k}k ${r['aac' + k].snr}/${r['aac' + k].snrHi} ${r['aac' + k].kB}kB`).join('  ') +
  `  ogg q4/5/6 ${r.ogg4.kB}/${r.ogg5.kB}/${r.ogg6.kB} kB`);
console.log('\nmeans:');
for (const set of ['ext', 'int']) {
  console.log(`  ${set}: hf>10k ${avg(set, 'hf10', (v) => v)} dB; ` + RATES.map((k) =>
    `aac ${k}k snr ${avg(set, 'aac' + k, (v) => v.snr)} hi ${avg(set, 'aac' + k, (v) => v.snrHi)} (${avg(set, 'aac' + k, (v) => v.kB)} kB)`).join('; '));
}
fs.rmSync(TMP, { recursive: true, force: true });
