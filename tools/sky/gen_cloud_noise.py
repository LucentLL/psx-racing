# THE VOLUMETRIC CLOUDS' NOISE (2026-10-25, Shaders/PSXVolClouds.cginc).
#
# Three tileable textures, written as raw bytes into Resources/Clouds/ (a
# .bytes TextAsset needs no import settings; VolumetricClouds.cs makes the
# textures from them once, at the first dynamic sky):
#
#   CloudShape.bytes   128^3 R8   the cloud BODY: Perlin-Worley (Perlin
#                                 dilated by a Worley fbm, Schneider's Nubis
#                                 recipe) carved by a higher Worley fbm -
#                                 one channel, the two already combined.
#   CloudDetail.bytes   64^3 R8   the EROSION: a Worley fbm - billows on the
#                                 edges, wisps under the base.
#   CloudWeather.bytes 256^2 RGBA8 the WEATHER MAP, 2D: R where cloud may
#                                 stand (equalised to uniform, so a coverage
#                                 of 0.4 is 40% of the ground), G what kind
#                                 (flat humilis .. towering congestus), B a
#                                 base-height jitter, A spare (255).
#
# Deterministic (fixed seeds): rerunning writes the same bytes.
#   py tools/sky/gen_cloud_noise.py
import os
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "Clouds")


def fade(t):
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0)


def grid3(n, f):
    c = ((np.arange(n, dtype=np.float32) + 0.5) * (f / n)).astype(np.float32)
    z, y, x = np.meshgrid(c, c, c, indexing="ij")      # (z, y, x): x runs fastest in memory
    return x, y, z


def perlin3(n, f, rng):
    """Tileable 3D gradient noise, f lattice cells across the tile, ~[-1, 1]."""
    g = rng.normal(size=(f, f, f, 3)).astype(np.float32)
    g /= np.linalg.norm(g, axis=-1, keepdims=True)
    x, y, z = grid3(n, f)
    xi, yi, zi = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32), np.floor(z).astype(np.int32)
    xf, yf, zf = x - xi, y - yi, z - zi
    u, v, w = fade(xf), fade(yf), fade(zf)
    out = np.zeros_like(x)

    def corner(dx, dy, dz):
        gg = g[(xi + dx) % f, (yi + dy) % f, (zi + dz) % f]
        return gg[..., 0] * (xf - dx) + gg[..., 1] * (yf - dy) + gg[..., 2] * (zf - dz)

    for dz in (0, 1):
        for dy in (0, 1):
            for dx in (0, 1):
                wgt = (u if dx else 1 - u) * (v if dy else 1 - v) * (w if dz else 1 - w)
                out += wgt * corner(dx, dy, dz)
    return out * 1.2


def worley3(n, f, rng):
    """Tileable 3D cellular noise, 1 - distance to the nearest feature
    point (one a cell, f cells across): 1 at a point, 0 a cell away."""
    pts = rng.random((f, f, f, 3)).astype(np.float32)
    x, y, z = grid3(n, f)
    xi, yi, zi = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32), np.floor(z).astype(np.int32)
    xf, yf, zf = x - xi, y - yi, z - zi
    best = np.full(x.shape, 9.0, dtype=np.float32)
    for dz in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                p = pts[(xi + dx) % f, (yi + dy) % f, (zi + dz) % f]
                ddx = dx + p[..., 0] - xf
                ddy = dy + p[..., 1] - yf
                ddz = dz + p[..., 2] - zf
                np.minimum(best, ddx * ddx + ddy * ddy + ddz * ddz, out=best)
    return np.clip(1.0 - np.sqrt(best), 0.0, 1.0)


def perlin2(n, f, rng):
    g = rng.normal(size=(f, f, 2)).astype(np.float32)
    g /= np.linalg.norm(g, axis=-1, keepdims=True)
    c = ((np.arange(n, dtype=np.float32) + 0.5) * (f / n)).astype(np.float32)
    y, x = np.meshgrid(c, c, indexing="ij")
    xi, yi = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
    xf, yf = x - xi, y - yi
    u, v = fade(xf), fade(yf)
    out = np.zeros_like(x)
    for dy in (0, 1):
        for dx in (0, 1):
            gg = g[(yi + dy) % f, (xi + dx) % f]
            d = gg[..., 0] * (xf - dx) + gg[..., 1] * (yf - dy)
            out += (u if dx else 1 - u) * (v if dy else 1 - v) * d
    return out * 1.4


def worley2(n, f, rng):
    pts = rng.random((f, f, 2)).astype(np.float32)
    c = ((np.arange(n, dtype=np.float32) + 0.5) * (f / n)).astype(np.float32)
    y, x = np.meshgrid(c, c, indexing="ij")
    xi, yi = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
    xf, yf = x - xi, y - yi
    best = np.full(x.shape, 9.0, dtype=np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            p = pts[(yi + dy) % f, (xi + dx) % f]
            ddx = dx + p[..., 0] - xf
            ddy = dy + p[..., 1] - yf
            np.minimum(best, ddx * ddx + ddy * ddy, out=best)
    return np.clip(1.0 - np.sqrt(best), 0.0, 1.0)


def stretch(a, lo=0.5, hi=99.5):
    """Percentile stretch to 0..1 (the texture's whole range does work)."""
    a0, a1 = np.percentile(a, lo), np.percentile(a, hi)
    return np.clip((a - a0) / max(a1 - a0, 1e-6), 0.0, 1.0)


def equalise(a):
    """Rank to uniform 0..1: a threshold at 1 - c leaves exactly c of it."""
    flat = a.ravel()
    r = np.empty(flat.size, dtype=np.float32)
    r[np.argsort(flat, kind="stable")] = np.linspace(0.0, 1.0, flat.size, dtype=np.float32)
    return r.reshape(a.shape)


def to8(a):
    return np.round(np.clip(a, 0.0, 1.0) * 255.0).astype(np.uint8)


def shape_noise():
    n = 128
    rng = np.random.default_rng(20261025)
    per = perlin3(n, 4, rng) + 0.5 * perlin3(n, 8, rng) + 0.25 * perlin3(n, 16, rng)
    per = np.clip(per / 1.75 * 0.5 + 0.5, 0.0, 1.0)
    w4, w8, w16, w32 = worley3(n, 4, rng), worley3(n, 8, rng), worley3(n, 16, rng), worley3(n, 32, rng)
    wfbm = w4 * 0.625 + w8 * 0.25 + w16 * 0.125
    # Perlin-Worley: the Perlin dilated by the Worley fbm (billowy, connected).
    pw = wfbm + per * (1.0 - wfbm)
    # ...carved by the higher Worley octaves (Nubis' low-frequency fbm).
    hi = w8 * 0.625 + w16 * 0.25 + w32 * 0.125
    base = (pw - (1.0 - hi)) / np.maximum(hi, 1e-3)
    base = np.clip(base, 0.0, 1.0) * 0.35 + pw * 0.65
    return to8(stretch(base, 1.0, 99.7))


def detail_noise():
    n = 64
    rng = np.random.default_rng(4021)
    d = worley3(n, 4, rng) * 0.625 + worley3(n, 8, rng) * 0.25 + worley3(n, 16, rng) * 0.125
    return to8(stretch(d, 0.5, 99.5))


def weather_map():
    n = 256
    rng = np.random.default_rng(777)
    cov = (perlin2(n, 4, rng) + 0.55 * perlin2(n, 8, rng) + 0.3 * perlin2(n, 16, rng) +
           0.15 * perlin2(n, 32, rng))
    # Fields of cumulus come in cells, not smooth blobs: a little Worley in it.
    cov = cov + 0.45 * (worley2(n, 12, rng) - 0.5) + 0.2 * (worley2(n, 28, rng) - 0.5)
    kind = perlin2(n, 3, rng) + 0.5 * perlin2(n, 6, rng) + 0.25 * perlin2(n, 12, rng)
    jit = perlin2(n, 8, rng) + 0.5 * perlin2(n, 16, rng)
    out = np.zeros((n, n, 4), dtype=np.uint8)
    out[..., 0] = to8(equalise(cov))
    out[..., 1] = to8(equalise(kind))
    out[..., 2] = to8(equalise(jit))
    out[..., 3] = 255
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    s = shape_noise()
    d = detail_noise()
    w = weather_map()
    with open(os.path.join(OUT, "CloudShape.bytes"), "wb") as fh:
        fh.write(s.tobytes())
    with open(os.path.join(OUT, "CloudDetail.bytes"), "wb") as fh:
        fh.write(d.tobytes())
    with open(os.path.join(OUT, "CloudWeather.bytes"), "wb") as fh:
        fh.write(w.tobytes())
    print("shape  ", s.shape, "mean %.3f p10 %.3f p50 %.3f p90 %.3f" % (
        s.mean() / 255, *(np.percentile(s, [10, 50, 90]) / 255)))
    print("detail ", d.shape, "mean %.3f" % (d.mean() / 255))
    print("weather", w.shape, "R mean %.3f G mean %.3f" % (w[..., 0].mean() / 255, w[..., 1].mean() / 255))
    try:
        from PIL import Image
        prev = os.path.join(os.environ.get("PSX_NOISE_PREVIEW", ""), "") if os.environ.get("PSX_NOISE_PREVIEW") else None
        if prev:
            Image.fromarray(np.concatenate([s[0], s[64], s[:, 0, :]], axis=1)).save(prev + "shape_slices.png")
            Image.fromarray(np.concatenate([d[0], d[:, 0, :]], axis=1)).save(prev + "detail_slices.png")
            Image.fromarray(w[..., :3]).save(prev + "weather.png")
    except ImportError:
        pass


if __name__ == "__main__":
    main()
