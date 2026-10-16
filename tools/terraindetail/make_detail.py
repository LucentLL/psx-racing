# THE TERRAIN DETAIL SHEET (the GT4 study, gap 4): four luminance-only detail
# tiles from the owner's PSX texture packs, one per CHANNEL of one RGBA PNG:
#
#   R  grass  PSX Textures v3.1  256/Color Maps/grass_1.png
#   G  dirt   PSX Textures v3.1  256/Color Maps/dirt_4.png
#   B  rock   PSX Textures v3.1  256/Color Maps/stone_1.png
#   A  snow   PSX Textures v3.1  256/Color Maps/snow_3.png
#
# Each tile is the texture's LUMINANCE (linear light, in log), BAND-PASSED:
# the texel-level noise is softened (sigma LO texels: point-sampled white
# noise only shimmers, and the mips average it away to nothing a few metres
# out) and every blotch larger than about HI texels is taken out (so the 2 m
# repeat never shows as a grid of light and dark tiles, and the base texture's
# own colour and the season's dress decide the large scale). It is then set to
# mean 0.5 and a fixed spread, so PSX/Lit can multiply the albedo by
# 1 + amount * (2 t - 1) and change nothing on average. The FFT blur wraps, so
# the tiles stay seamless.
#
#   py tools\terraindetail\make_detail.py      (writes Assets/PSXRacing/Resources/TerrainDetail.png)
import os
import numpy as np
from PIL import Image

PACK = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Textures v3.1\PSX Textures\256\Color Maps"
CHANNELS = ["grass_1", "dirt_4", "stone_1", "snow_3"]
LO, HI = 1.0, 20.0     # band edges, texels (gaussian sigma)
SPREAD = 0.15          # std of each channel around 0.5
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                   "Assets", "PSXRacing", "Resources", "TerrainDetail.png")


def wrapblur(a, sigma):
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    g = np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def tile(name):
    rgb = np.asarray(Image.open(os.path.join(PACK, name + ".png")).convert("RGB"), dtype=np.float64) / 255.0
    lin = rgb ** 2.2
    L = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    lg = np.log(np.maximum(L, 1e-3))
    b = wrapblur(lg, LO) - wrapblur(lg, HI)
    v = 0.5 + b / max(b.std(), 1e-6) * SPREAD
    for _ in range(4):                 # re-centre what the clip moved
        v = np.clip(v - (v.mean() - 0.5), 0.0, 1.0)
    return v


def main():
    chans = [tile(n) for n in CHANNELS]
    out = np.stack(chans, axis=-1)
    Image.fromarray(np.round(out * 255).astype(np.uint8), mode="RGBA").save(os.path.normpath(OUT))
    for n, c in zip(CHANNELS, chans):
        print(f"{n:10s} mean {c.mean():.3f} std {c.std():.3f}")
    print("wrote", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
