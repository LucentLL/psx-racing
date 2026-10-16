/* Fundamental (firing frequency) of every take in a family, by harmonic sum
   over the clip's average spectrum. node tools/engine_take_f0.mjs <family> [--vendor Prefab]
   Exported for engine_audio_report.mjs: the produced note at a given rpm is
   f0(take) x playback rate, which is what the two rigs disagree about. */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spectrum, oursClipPath } from './audio_render_probe.mjs';
import { loadHQ } from './res2_curves.mjs';

export function f0Of(file, lo = 18, hi = 220) {
  const b = execFileSync('ffmpeg', ['-v', 'error', '-i', file, '-ac', '2', '-ar', '44100', '-f', 'f32le', '-'], { maxBuffer: 1 << 28 });
  const x = new Float32Array(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength));
  const n = 32768, dur = x.length / 2 / 44100;
  const p = spectrum(x, 0, dur, Math.min(n, 1 << Math.floor(Math.log2(x.length / 2))));
  const df = 44100 / p.n, L = Array.from(p, (v) => Math.log10(v + 1e-20));
  const at = (f) => { const k = f / df, i = Math.floor(k), t = k - i; return Math.max(L[i] ?? -20, L[i + 1] ?? -20) * 1 + 0 * t; };
  let best = 0, bestS = -Infinity;
  for (let f = lo; f <= hi; f += 0.25) {
    let s = 0;
    for (let h = 1; h <= 10; h++) if (h * f < 2500) s += at(h * f);
    // penalise half-harmonics so f/2 does not win by default
    let s2 = 0; for (let h = 1; h <= 10; h++) if ((h - 0.5) * f < 2500) s2 += at((h - 0.5) * f);
    const score = s - 0.85 * s2;
    if (score > bestS) { bestS = score; best = f; }
  }
  return best;
}

export function familyF0(fam, vendorPrefab = null) {
  const names = ['idle', 'idle_low_on', 'low_on', 'low_med_on', 'med_on', 'med_high_on', 'high_on', 'very_high_on', 'maxRPM', 'intake_on'];
  const out = {};
  if (vendorPrefab) {
    const p = loadHQ(vendorPrefab);
    const field = { idle: 'idleClip', maxRPM: 'maxRPMClip', intake_on: 'intakeOnClip' };
    for (const n of names) {
      const f = field[n] || n.replace(/_on$/, '') + 'OnClip';
      if (p.clips[f]) out[n] = f0Of(p.clips[f]);
    }
  } else for (const n of names) { const f = oursClipPath(fam, n); if (fs.existsSync(f)) out[n] = f0Of(f); }
  return out;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const a = process.argv.slice(2), vi = a.indexOf('--vendor');
  const r = familyF0(a[0], vi >= 0 ? a[vi + 1] : null);
  console.log(a[0], vi >= 0 ? a[vi + 1] : '', Object.entries(r).map(([k, v]) => `${k}=${v.toFixed(1)}`).join(' '));
}
