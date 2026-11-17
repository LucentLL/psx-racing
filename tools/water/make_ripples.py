"""PSX/Water ripple sheet (2026-11-16, the water round).

water_ripples.png, 512x512 RGBA, LINEAR (sRGB off):
  R,G  tangent-space normal x,y (0.5 + 0.5 n): a wind-wave height field from a
       directional (Phillips) spectrum, wind along +U. Made by inverse FFT on a
       periodic grid with integer wave numbers only, so it repeats exactly on
       U and V; the slopes are taken spectrally (i k h), so they repeat too.
  B    foam lace: periodic Worley (F2 - F1) cell walls at two scales.
  A    breakup field: a smooth periodic low-frequency noise (wind patches,
       foam breakup), equalised to about flat 0..1.

Every channel is built on the torus, so the sheet tiles on both axes by
construction; the check sheet shows it.
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw

N = 512
rng = np.random.default_rng(20261116)
out_dir = sys.argv[1] if len(sys.argv) > 1 else "."

# ---- R,G: the wave normal -------------------------------------------------
k1 = np.fft.fftfreq(N, d=1.0 / N)            # integer wave numbers -N/2..N/2-1
kx, ky = np.meshgrid(k1, k1)                  # x = U (columns), y = V (rows)
k = np.sqrt(kx * kx + ky * ky)
k[0, 0] = 1.0
# Phillips: |w.k|^2 / k^4 * exp(-1/(k L)^2), small waves cut by exp(-k^2 l^2).
# Peak around 6-10 waves per tile, energy down to ~120 per tile.
L = 5.0          # spectral peak (waves per tile)
lsmall = 1.0 / 75.0
wdir = np.array([1.0, 0.18]); wdir /= np.linalg.norm(wdir)
kd = (kx * wdir[0] + ky * wdir[1]) / k
P = np.exp(-1.0 / (k / L) ** 2) / k ** 4 * (kd * kd) ** 1.0 * np.exp(-(k * lsmall) ** 2)
# keep some cross-wind energy so it is not a corduroy
P = P * 0.82 + np.exp(-1.0 / (k / L) ** 2) / k ** 4 * np.exp(-(k * lsmall) ** 2) * 0.18
P[0, 0] = 0.0
xi = (rng.standard_normal((N, N)) + 1j * rng.standard_normal((N, N))) / np.sqrt(2.0)
H = xi * np.sqrt(P)
# a real field: H(-k) = conj(H(k)) enforced by taking the real part of the IFFT
h = np.real(np.fft.ifft2(H))
sx = np.real(np.fft.ifft2(1j * 2 * np.pi * kx * H))   # dh/du (per tile)
sy = np.real(np.fft.ifft2(1j * 2 * np.pi * ky * H))
# normalise the slopes: rms slope 0.22 (per unit of the shader's own scale)
rms = np.sqrt((sx * sx + sy * sy).mean())
sx *= 0.22 / rms; sy *= 0.22 / rms
n = np.stack([-sx, -sy, np.ones_like(sx)], -1)
n /= np.linalg.norm(n, axis=-1, keepdims=True)
R = np.clip(0.5 + 0.5 * n[..., 0], 0, 1)
G = np.clip(0.5 + 0.5 * n[..., 1], 0, 1)

# ---- B: the foam lace (periodic Worley) ---------------------------------
def worley_f2f1(cells, seed):
    r = np.random.default_rng(seed)
    pts = r.random((cells, cells, 2))
    yy, xx = np.mgrid[0:N, 0:N]
    u = (xx + 0.5) / N * cells
    v = (yy + 0.5) / N * cells
    cu = np.floor(u).astype(int); cv = np.floor(v).astype(int)
    f1 = np.full((N, N), 9.0); f2 = np.full((N, N), 9.0)
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            ci = (cu + di) % cells; cj = (cv + dj) % cells
            px = cu + di + pts[cj, ci, 0]
            py = cv + dj + pts[cj, ci, 1]
            d = np.sqrt((u - px) ** 2 + (v - py) ** 2)
            f2 = np.where(d < f1, f1, np.minimum(f2, d))
            f1 = np.minimum(f1, d)
    return f2 - f1

w1 = worley_f2f1(14, 7)
w2 = worley_f2f1(37, 11)
lace = np.clip(1.0 - w1 / 0.30, 0, 1) ** 1.6 * 0.75 + np.clip(1.0 - w2 / 0.22, 0, 1) ** 1.4 * 0.45
# bubbles: small dark holes inside the lace
lace = np.clip(lace, 0, 1)
B = lace

# ---- A: the breakup field ------------------------------------------------
Pa = np.exp(-(k / 5.0) ** 2) * (k > 0.5)
xa = (rng.standard_normal((N, N)) + 1j * rng.standard_normal((N, N)))
a = np.real(np.fft.ifft2(xa * np.sqrt(Pa)))
# equalise to flat 0..1 (rank)
order = np.argsort(a, axis=None)
ranks = np.empty(N * N); ranks[order] = np.arange(N * N) / (N * N - 1)
A = ranks.reshape(N, N)

img = np.stack([R, G, B, A], -1)
png = (np.round(img * 255)).astype(np.uint8)
path = os.path.join(out_dir, "water_ripples.png")
Image.fromarray(png, "RGBA").save(path)
print("wrote", path, png.shape)

# ---- report ----------------------------------------------------------------
dec = png.astype(np.float32) / 255.0
nx = dec[..., 0] * 2 - 1; ny = dec[..., 1] * 2 - 1
print("mean nx %.4f ny %.4f (0 = no tilt bias); rms slope %.3f" % (nx.mean(), ny.mean(), np.sqrt((nx**2+ny**2).mean())))
for c, name in enumerate("RGBA"):
    ch = dec[..., c]
    wrapu = np.abs(ch[:, 0] - ch[:, -1]).mean(); intu = np.abs(np.diff(ch, axis=1)).mean()
    wrapv = np.abs(ch[0, :] - ch[-1, :]).mean(); intv = np.abs(np.diff(ch, axis=0)).mean()
    print("%s: wrap U %.4f vs interior %.4f | wrap V %.4f vs interior %.4f" % (name, wrapu, intu, wrapv, intv))
