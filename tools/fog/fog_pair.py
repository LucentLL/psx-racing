"""A captioned before/after sheet from two fog shots, for the owner.

    py tools\fog\fog_pair.py <before.png> <after.png> <out.png> [before caption] [after caption]

Side by side rather than a pair of attachments, because the whole question is
"is the distance still white" and two frames of the same view answer it in one
glance. Captions are burned in so the sheet survives being saved, forwarded or
looked at a week later without the message that came with it.
"""
import sys
from PIL import Image, ImageDraw

BAR = 30
GAP = 8
INK = (235, 200, 120)
PAPER = (16, 16, 18)


def captioned(path, text):
    im = Image.open(path).convert("RGB")
    out = Image.new("RGB", (im.width, im.height + BAR), PAPER)
    out.paste(im, (0, BAR))
    ImageDraw.Draw(out).text((10, 9), text, fill=INK)
    return out


def sheet(panels, dest):
    cols = [captioned(p, c) for p, c in panels]
    width = sum(c.width for c in cols) + GAP * (len(cols) - 1)
    out = Image.new("RGB", (width, max(c.height for c in cols)), PAPER)
    x = 0
    for c in cols:
        out.paste(c, (x, 0))
        x += c.width + GAP
    out.save(dest)
    print(dest)


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(2)
    before, after, dest = sys.argv[1:4]
    cap_a = sys.argv[4] if len(sys.argv) > 4 else "BEFORE"
    cap_b = sys.argv[5] if len(sys.argv) > 5 else "AFTER"
    sheet([(before, cap_a), (after, cap_b)], dest)
