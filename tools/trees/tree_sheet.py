"""Lay TreePreview's turntable shots out as one contact sheet:
one block per tree kind, a row per pass (before = backs culled, as shipped;
after = both faces drawn), eight compass points across."""
import os, sys
from PIL import Image, ImageDraw, ImageFont

src = sys.argv[1]
out = sys.argv[2]
kinds = [("circuit", "Circuit roadside tree"), ("forest", "Mountain forest tree"), ("station", "Gas-station pack tree")]
angles = [0, 45, 90, 135, 180, 225, 270, 315]
tw, th = 200, 150
pad, label_w, head_h = 6, 150, 26

try:
    font = ImageFont.truetype("arial.ttf", 15)
    small = ImageFont.truetype("arial.ttf", 12)
except Exception:
    font = small = ImageFont.load_default()

rows = []
for key, title in kinds:
    for tag, label in (("before", "BEFORE\n(backs culled)"), ("after", "AFTER\n(both faces +\ntrunk collider)")):
        files = [os.path.join(src, "%s_%s_%03d.png" % (key, tag, a)) for a in angles]
        if all(os.path.exists(f) for f in files):
            rows.append((title, label, files))

W = label_w + len(angles) * (tw + pad) + pad
# Tall enough for every row and a title per kind; cropped to what was drawn.
sheet = Image.new("RGB", (W, head_h + len(rows) * (th + pad) + pad + 20 * len(kinds)), (24, 24, 28))
d = ImageDraw.Draw(sheet)
for i, a in enumerate(angles):
    d.text((label_w + i * (tw + pad) + tw // 2 - 12, 6), "%d°" % a, fill=(230, 230, 230), font=font)
y = head_h
last = None
for title, label, files in rows:
    if title != last:
        d.text((8, y), title, fill=(255, 200, 90), font=font)
        y += 20
        last = title
    d.multiline_text((8, y + 40), label, fill=(220, 220, 220), font=small, spacing=3)
    for i, f in enumerate(files):
        im = Image.open(f).convert("RGB").resize((tw, th), Image.NEAREST)
        sheet.paste(im, (label_w + i * (tw + pad), y))
    y += th + pad
sheet = sheet.crop((0, 0, W, y + pad))
sheet.save(out)
print("wrote", out, sheet.size, len(rows), "rows")
