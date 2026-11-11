# THE SHOPFRONTS AT SOURCE RESOLUTION (tex4, 2026-11-11).
# city_shops_overhaul1_hd.png = 4096 x 1024: the four fictional storefronts,
# each WHOLE source image in a 1024 x 1024 cell, exactly where ComposeShops
# (Editor/PSXRacingBuilder.City.cs) put it in the 256 px cell (cell i = the
# whole source, stretched), so doors, windows and the night mask line up.
# _rough: glass (night mask R*A, nearest-upscaled) 0.30, the rest 0.85.
# Usage: py -I shops_hd.py <repo> <outdir>
import sys, os, hashlib
from PIL import Image
import numpy as np

repo, out = sys.argv[1], sys.argv[2]
src_dir = os.path.join(repo, "Assets/PSXRacing/Art/Buildings/Textures")
names = ["Shops.png", "Shops_05.png", "Shops_12.png", "Shops_23.png"]
CELL = 1024
atlas = Image.new("RGB", (CELL * 4, CELL))
for i, n in enumerate(names):
    im = Image.open(os.path.join(src_dir, n)).convert("RGB")
    print(n, im.size, "-> cell", i, f"x scale {CELL / im.size[0]:.3f}, y scale {CELL / im.size[1]:.3f}")
    atlas.paste(im.resize((CELL, CELL), Image.LANCZOS), (i * CELL, 0))
os.makedirs(out, exist_ok=True)
p = os.path.join(out, "city_shops_overhaul1_hd.png")
atlas.save(p, optimize=True)

# registration check against the shipped 1024 x 256 atlas
old = np.asarray(Image.open(os.path.join(repo, "Assets/PSXRacing/Art/City/city_shops.png")).convert("RGB"), dtype=np.float32)
dn = np.asarray(atlas.resize((1024, 256), Image.BILINEAR), dtype=np.float32)
print("registration: mean abs diff vs shipped city_shops.png (0-255):", float(np.abs(old - dn).mean()))
for dx in (-2, -1, 0, 1, 2):
    sh = np.roll(dn, dx, axis=1)
    print(f"  shift {dx:+d} px: {float(np.abs(old - sh).mean()):.2f}")

mask = Image.open(os.path.join(repo, "Assets/PSXRacing/Art/City/Night/city_shops_night.png")).convert("RGBA")
m = np.asarray(mask, dtype=np.float32) / 255.0
glass = m[..., 0] * m[..., 3]
rough = np.where(glass > 0.5, 0.30, 0.85)
Image.fromarray((rough * 255).astype(np.uint8), "L").save(os.path.join(out, "city_shops_overhaul1_hd_rough.png"))
for f in os.listdir(out):
    if f.startswith("city_shops"):
        fp = os.path.join(out, f)
        print(f, Image.open(fp).size, os.path.getsize(fp), hashlib.sha256(open(fp, "rb").read()).hexdigest()[:16])
