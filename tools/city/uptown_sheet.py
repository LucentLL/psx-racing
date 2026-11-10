# THE UPTOWN BLOCK-FACE SHEET (charlotte-uptown-street-dress, 2026-11-09):
# triples - the real place at road level | the game before | the game after,
# same pose - one row a block face, from Editor/CityRefSpots.Uptown.cs's shots.
#
#   py -3 tools/city/uptown_sheet.py <shots dir> <real dir> <out.png> [--only id,id]
#
# <shots dir>  Screenshots/City/uptown of the sandbox (up_<id>_before.png / _after.png, uptown_spots.txt)
# <real dir>   the reference frames, <id without up_>.jpg/.png - REFERENCE ONLY: they are looked at and
#              laid beside the game's frames here; they never go into the repo or the game.
import os, sys
from PIL import Image, ImageDraw, ImageFont

shots, real, out = sys.argv[1], sys.argv[2], sys.argv[3]
only = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else None
CW, CH, PAD, HEAD = 800, 450, 6, 30


def font(size):
    for f in ("arial.ttf", "DejaVuSans.ttf"):
        try: return ImageFont.truetype(f, size)
        except Exception: pass
    return ImageFont.load_default()


def fit(im):
    """Crop to 16:9 about the middle (a letterboxed reference frame loses its bars), then to the cell."""
    im = im.convert("RGB")
    # trim uniform black bars
    bbox = im.point(lambda v: 255 if v > 26 else 0).getbbox()
    if bbox: im = im.crop(bbox)
    w, h = im.size
    if w * 9 > h * 16: nw = h * 16 // 9; im = im.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
    else: nh = w * 9 // 16; im = im.crop((0, (h - nh) // 2, w, (h - nh) // 2 + nh))
    return im.resize((CW, CH), Image.LANCZOS)


rows = []
for line in open(os.path.join(shots, "uptown_spots.txt"), encoding="utf-8"):
    c = line.rstrip("\n").split("\t")
    if len(c) < 7 or not c[0].startswith("up_"): continue
    if only and c[0] not in only: continue
    rows.append((c[0], c[6]))
F, FS = font(20), font(15)
sheet = Image.new("RGB", (3 * CW + 4 * PAD, len(rows) * (CH + HEAD + PAD) + PAD + 28), (18, 18, 18))
d = ImageDraw.Draw(sheet)
d.text((PAD, 4), "REAL (road level, reference only)", fill=(255, 220, 120), font=FS)
d.text((PAD * 2 + CW, 4), "GAME BEFORE (origin/main look: dress off)", fill=(255, 220, 120), font=FS)
d.text((PAD * 3 + CW * 2, 4), "GAME AFTER (charlotte-uptown-street-dress)", fill=(255, 220, 120), font=FS)
y = 28
for sid, what in rows:
    d.text((PAD, y + 3), f"{sid[3:]}  -  {what}", fill=(235, 235, 235), font=F)
    y += HEAD
    cells = []
    rp = None
    for ext in (".png", ".jpg", ".jpeg"):
        q = os.path.join(real, sid[3:] + ext)
        if os.path.exists(q): rp = q; break
    cells.append(fit(Image.open(rp)) if rp else None)
    for k in ("_before", "_after"):
        q = os.path.join(shots, sid + k + ".png")
        cells.append(fit(Image.open(q)) if os.path.exists(q) else None)
    for i, im in enumerate(cells):
        x = PAD + i * (CW + PAD)
        if im is None: d.rectangle([x, y, x + CW, y + CH], outline=(90, 90, 90)); d.text((x + 10, y + 10), "(no frame)", fill=(160, 160, 160), font=FS)
        else: sheet.paste(im, (x, y))
    y += CH + PAD
sheet.save(out)
print("wrote", out, sheet.size)
