# Before/after sheets for the volumetric clouds (Editor/DynamicSkyShots):
# one sheet per set (hour + sky), the four views across, OLD over NEW.
#   py tools/sky/cloud_sheets.py <screenshots dir> <out dir> [scale]
import glob
import os
import sys

from PIL import Image, ImageDraw

VIEWS = ["a_sunward", "b_side", "c_away", "d_up"]


def main():
    src, out = sys.argv[1], sys.argv[2]
    scale = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
    os.makedirs(out, exist_ok=True)
    sets = sorted({os.path.basename(p).split("_a_sunward_")[0] for p in glob.glob(os.path.join(src, "dsky_*_a_sunward_*.png"))})
    for s in sets:
        rows = []
        for mode in ("old", "new"):
            ims = []
            for v in VIEWS:
                p = os.path.join(src, "%s_%s_%s.png" % (s, v, mode))
                if os.path.exists(p):
                    im = Image.open(p).convert("RGB")
                    if scale != 1.0:
                        im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
                    ims.append(im)
            if ims:
                rows.append((mode, ims))
        if not rows:
            continue
        w, h = rows[0][1][0].size
        sheet = Image.new("RGB", (w * 4 + 5 * 4, (h + 4) * len(rows) + 22), (20, 20, 20))
        d = ImageDraw.Draw(sheet)
        d.text((6, 4), s.replace("dsky_", "") + "   (sunward | side | away | up 40 deg)   rows: " +
               " / ".join(m.upper() for m, _ in rows), fill=(230, 200, 120))
        for r, (mode, ims) in enumerate(rows):
            for c, im in enumerate(ims):
                x, y = 4 + c * (w + 5), 22 + r * (h + 4)
                sheet.paste(im, (x, y))
                d.text((x + 4, y + 4), mode.upper(), fill=(255, 255, 0))
        path = os.path.join(out, s.replace("dsky_", "clouds_") + ".jpg")
        sheet.save(path, quality=90)
        print(path, sheet.size)


if __name__ == "__main__":
    main()
