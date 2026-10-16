# THE v08 COCKPIT'S TEXTURES, packed for the game (run after cockpit_v08.py).
#
#   py tools/cockpit/cockpit_v08_tex.py
#
# The owner's set is twelve 1024 PNGs: base colour + normal + ORM for the
# dash vinyl, console ABS and headliner weave, leather grain + normal, and
# the atlas. Measured 2026-10-16, the base colours are FLAT to within +-4
# codes (vinyl 31..39 of 255) and ORM is R=1, B=0 with only G (roughness)
# varying: all the surface relief is in the normal maps. So each surface
# ships as ONE 512 texture, RGBA, linear data, read by PSX/Cabin:
#   A = normal X, G = normal Y   (OpenGL +Y; Z rebuilt in the shader - the
#                                 DXT5nm layout: the best-kept channels)
#   R = roughness                (ORM G)
#   B = base luminance / mean, 0.75..1.25 mapped to 0..255 (the grain the
#       base colour does carry); the mean colour itself is a material value,
#       written into v08_layout.json "surfaces" (linear RGB).
# WHY 512: SurfaceUV repeats every 0.128 m; from the driver's eye the nearest
# trim (the door card at full look-over) shows a tile at about 260 px of a
# 720-line frame, the dash at ~130 px - 512 is still 2x that. 1024 would
# quadruple the bytes for detail no frame can show.
# The atlas (seats, floor, pedals, column, shifter base) stays 1024 sRGB.
import json, os, shutil
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "CockpitSrc")          # into cockpit.bundle
LAYOUT = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "Cockpit", "v08_layout.json")
SRC = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Universal_Cockpit_RX7_FD_v08"
SIZE = 512
SETS = {  # game key: (base, normal, ORM or a constant roughness)
    "vinyl": ("Dash_Vinyl_BaseColor.png", "Dash_Vinyl_Normal.png", "Dash_Vinyl_ORM.png"),
    "abs": ("Console_ABS_BaseColor.png", "Console_ABS_Normal.png", "Console_ABS_ORM.png"),
    "headliner": ("Headliner_Weave_BaseColor.png", "Headliner_Weave_Normal.png", "Headliner_Weave_ORM.png"),
    "leather": ("Leather_Grain_1024.png", "Leather_Normal_1024.png", 0.70),
}


def load(name):
    return np.asarray(Image.open(os.path.join(SRC, name)).convert("RGB")).astype(np.float64) / 255.0


def to_lin(s):
    return np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)


def box(a, k):
    h, w = a.shape[:2]
    return a.reshape(h // k, k, w // k, k, -1).mean(axis=(1, 3))


def main():
    surfaces = {}
    for key, (bn, nn, orm) in SETS.items():
        base = to_lin(load(bn))
        k = base.shape[0] // SIZE
        lum = base @ np.array([0.2126, 0.7152, 0.0722])
        mean = base.reshape(-1, 3).mean(0)
        ratio = box(lum[..., None], k)[..., 0] / max(lum.mean(), 1e-6)
        n = load(nn) * 2.0 - 1.0
        n = box(n, k)
        n /= np.linalg.norm(n, axis=2, keepdims=True)
        rough = box(load(orm)[..., 1:2], k)[..., 0] if isinstance(orm, str) else np.full(ratio.shape, orm)
        px = np.stack([rough,
                       n[..., 1] * 0.5 + 0.5,
                       np.clip((ratio - 0.75) / 0.5, 0.0, 1.0),
                       n[..., 0] * 0.5 + 0.5], axis=2)
        img = Image.fromarray(np.clip(np.round(px * 255), 0, 255).astype(np.uint8), "RGBA")
        img.save(os.path.join(OUT, "v08_%s.png" % key))
        surfaces[key] = {"r": round(mean[0], 5), "g": round(mean[1], 5), "b": round(mean[2], 5), "a": 1.0}
        print("[V08T] %-9s %dx%d  mean linear %s  rough %.2f..%.2f  lum ratio %.2f..%.2f"
              % (key, SIZE, SIZE, np.round(mean, 4), rough.min(), rough.max(), ratio.min(), ratio.max()))
    shutil.copyfile(os.path.join(SRC, "Cockpit_Atlas_1024.png"), os.path.join(OUT, "v08_atlas.png"))
    lp = LAYOUT
    lay = json.load(open(lp, encoding="utf-8"))
    lay["surfaces"] = surfaces
    with open(lp, "w", newline="\n") as fh:
        json.dump(lay, fh, indent=1)
    print("[V08T] atlas copied; surfaces written into", lp)


main()
