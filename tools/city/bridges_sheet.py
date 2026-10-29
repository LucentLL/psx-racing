# The bridges pass's sheet: the owner's two I-77 / Brookshire Freeway photos
# beside our view at the same place, before and after, then the I-277 flyover,
# the viaduct and a ramp guardrail close-up (CityBridgesPass shots,
# <sandbox>\Screenshots\City\bridges\<name>_<tag>.png).
#
#   py -3 tools/city/bridges_sheet.py <shots dir> <out.png> [photo1 photo2]
import os, sys
from PIL import Image, ImageDraw, ImageFont

shots, out = sys.argv[1], sys.argv[2]
photos = sys.argv[3:5]
H = 432
SW = int(H * 16 / 9)
rows = [
    ("br_own1", "I-77 NB before the left exit 11B (owner photo 1)", 0),
    ("br_own2", "I-77 NB at the 11B gantry (owner photo 2)", 1),
    ("br_flyover", "I-77 NB under the I-277 / I-77 flyover", None),
    ("br_viaduct277", "I-277 viaduct over NC Music Factory Blvd", None),
    ("br_ramp", "Exit ramp to NC 16: guardrail and bridge end", None),
    ("br_ictop", "The I-77 / I-277 / NC 16 interchange from above", None),
]
PW = int(H * 2252 / 2800)
W = PW + 2 * SW + 4 * 6
try:
    font = ImageFont.truetype("arial.ttf", 18)
    small = ImageFont.truetype("arial.ttf", 15)
except Exception:
    font = small = ImageFont.load_default()
sheet = Image.new("RGB", (W, len(rows) * (H + 30) + 40), (24, 24, 26))
d = ImageDraw.Draw(sheet)
d.text((8, 8), "Charlotte bridges + guardrails pass (city-bridges-rails): owner photo | BEFORE (main) | AFTER", font=font, fill=(255, 220, 90))
y = 40
for name, title, pi in rows:
    d.text((8, y + 4), title, font=small, fill=(230, 230, 230))
    y0 = y + 26
    x = 6
    if pi is not None and pi < len(photos) and os.path.exists(photos[pi]):
        ph = Image.open(photos[pi]).convert("RGB")
        ph = ph.crop((0, 0, ph.width, int(ph.height * 0.70)))   # the dashboard out
        ph = ph.resize((int(H * ph.width / ph.height), H), Image.LANCZOS)
        sheet.paste(ph, (x + (PW - ph.width) // 2, y0))
    x += PW + 6
    for tag in ("before", "after"):
        p = os.path.join(shots, f"{name}_{tag}.png")
        if os.path.exists(p):
            im = Image.open(p).convert("RGB").resize((SW, H), Image.LANCZOS)
            sheet.paste(im, (x, y0))
            d.text((x + 6, y0 + 6), tag.upper(), font=small, fill=(255, 255, 0))
        else:
            d.text((x + 6, y0 + 6), f"(no {tag} shot)", font=small, fill=(200, 80, 80))
        x += SW + 6
    y = y0 + H + 4
sheet.save(out)
print("wrote", out, sheet.size)
