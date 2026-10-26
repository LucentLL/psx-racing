# THE ROAD COVER SHEET (2026-10-26, the owner: "The roads look terrible during
# snow. I thought it was broken textures."): what PSX/Lit lays over a road under
# snow or slush, from the owner's own pack - one 256 px RGBA PNG, one tile per
# CHANNEL:
#
#   R  fresh snow's grain    PSX Textures v3.1  256/Color Maps/snow_2.png
#   G  slush (churned, wet)  PSX Textures v3.1  256/Color Maps/snow_melting_1.png
#   B  packed snow's grain   PSX Textures v3.1  256/Color Maps/snow_1.png
#   A  the cover's EXTENT    snow_melting_1 again, only its broad shapes
#
# R, G and B are the texture's LUMINANCE (linear light, in log), band-passed as
# the terrain detail sheet is (tools/terraindetail/make_detail.py): the
# texel-level noise softened, everything broader than HI texels taken out, set
# to mean 0.5 and a fixed spread - so the shader colours the cover itself
# (snow bright, slush grey-brown) and the pack gives it its grain, never its
# overall level. A is the slush tile's broad blotches only (blurred), EQUALISED
# to a flat histogram: PSX/Lit thresholds it to thin the cover where a patch's
# share runs out, and a flat histogram makes "share s" cover about s of the
# road, with soft edges at any share. The FFT blur wraps, so the tile stays
# seamless.
#
#   py tools\roadcover\make_cover.py     (writes Assets/PSXRacing/Resources/RoadCover.png)
import os
import numpy as np
from PIL import Image

PACK = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Textures v3.1\PSX Textures\256\Color Maps"
GRAIN = [("snow_2", 1.0, 24.0, 0.14), ("snow_melting_1", 0.8, 48.0, 0.17), ("snow_1", 1.0, 32.0, 0.14)]
FIELD = ("snow_melting_1", 9.0)       # the extent: this tile's shapes broader than sigma texels
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                   "Assets", "PSXRacing", "Resources", "RoadCover.png")


def wrapblur(a, sigma):
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    g = np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def loglum(name):
    rgb = np.asarray(Image.open(os.path.join(PACK, name + ".png")).convert("RGB"), dtype=np.float64) / 255.0
    lin = rgb ** 2.2
    L = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    return np.log(np.maximum(L, 1e-3))


def grain(name, lo, hi, spread):
    lg = loglum(name)
    b = wrapblur(lg, lo) - wrapblur(lg, hi)
    v = 0.5 + b / max(b.std(), 1e-6) * spread
    for _ in range(4):                 # re-centre what the clip moved
        v = np.clip(v - (v.mean() - 0.5), 0.0, 1.0)
    return v


def field(name, sigma):
    f = wrapblur(loglum(name), sigma)
    order = np.argsort(f, axis=None)
    ranks = np.empty(order.size)
    ranks[order] = (np.arange(order.size) + 0.5) / order.size
    return ranks.reshape(f.shape)


def main():
    chans = [grain(*g) for g in GRAIN] + [field(*FIELD)]
    out = np.stack(chans, axis=-1)
    Image.fromarray(np.round(out * 255).astype(np.uint8), mode="RGBA").save(os.path.normpath(OUT))
    for n, c in zip(["R " + GRAIN[0][0], "G " + GRAIN[1][0], "B " + GRAIN[2][0], "A extent"], chans):
        print(f"{n:18s} mean {c.mean():.3f} std {c.std():.3f}")
    print("wrote", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
