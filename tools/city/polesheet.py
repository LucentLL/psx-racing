"""Contact sheet for the POLE SHOTS (Charlotte WP-15, tools/city-pole-shots.ps1):
one row a spot - before by day, after by day, before at night, after at night -
from <spot>_<label>_<day|night>.png in a folder that holds a "before" run and an
"after" run.

    py tools/city/polesheet.py <folder> [--before before] [--after after]

Writes <folder>/poles_sheet.png.
"""
import os
import sys
from PIL import Image, ImageDraw


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 2
    folder = args[0]
    before = args[args.index("--before") + 1] if "--before" in args else "before"
    after = args[args.index("--after") + 1] if "--after" in args else "after"
    spots = sorted({f.split("_" + after + "_")[0] for f in os.listdir(folder) if ("_" + after + "_") in f and f.endswith(".png")})
    if not spots:
        print("no '%s' shots in %s" % (after, folder))
        return 1
    cols = [(before, "day"), (after, "day"), (before, "night"), (after, "night")]
    w, h = 640, 360
    label_h = 18
    sheet = Image.new("RGB", (w * len(cols), (h + label_h) * len(spots)), (24, 24, 24))
    d = ImageDraw.Draw(sheet)
    for r, spot in enumerate(spots):
        for c, (lab, hour) in enumerate(cols):
            path = os.path.join(folder, "%s_%s_%s.png" % (spot, lab, hour))
            x, y = c * w, r * (h + label_h)
            d.text((x + 4, y + 3), "%s  %s  %s" % (spot, lab, hour), fill=(230, 200, 120))
            if os.path.exists(path):
                im = Image.open(path).convert("RGB")
                if im.size != (w, h):
                    im = im.resize((w, h))
                sheet.paste(im, (x, y + label_h))
            else:
                d.text((x + 10, y + label_h + 10), "missing", fill=(255, 80, 80))
    out = os.path.join(folder, "poles_sheet.png")
    sheet.save(out)
    print("wrote", out, sheet.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
