"""ROAD SURVEY stage 3: OLD | NEW editor frame sheets with faults circled.

  py -I tools/city/survey/sheet3.py OUT.png TITLE OLD.png NEW.png [CIRCLES]
CIRCLES: "u,v,r,label;..." in the NEW frame's own pixels (1920 x 1080), drawn red on NEW.
"""
import sys
from PIL import Image, ImageDraw, ImageFont

def main(out, title, old, new, circles=""):
    try: f = ImageFont.truetype("arial.ttf", 26); fs = ImageFont.truetype("arial.ttf", 20)
    except Exception: f = fs = ImageFont.load_default()
    a = Image.open(old).convert("RGB"); b = Image.open(new).convert("RGB")
    W, H = 960, 540
    a = a.resize((W, H)); b = b.resize((W, H)); sx = W / 1920.0
    d = ImageDraw.Draw(b)
    for c in [x for x in circles.split(";") if x]:
        u, v, r, lab = c.split(",", 3); u, v, r = float(u) * sx, float(v) * sx, float(r) * sx
        d.ellipse([u - r, v - r, u + r, v + r], outline=(255, 0, 0), width=4); d.text((u + r + 4, v - 12), lab, fill=(255, 40, 40), font=fs)
    s = Image.new("RGB", (2 * W, H + 80), (15, 15, 15)); s.paste(a, (0, 80)); s.paste(b, (W, 80))
    d = ImageDraw.Draw(s); d.text((10, 6), title, fill=(255, 255, 255), font=f)
    d.text((10, 46), "EDITOR OLD (PSX_CITY_SHALLOWFORK unset)", fill=(255, 220, 0), font=fs)
    d.text((W + 10, 46), "EDITOR NEW (PSX_CITY_SHALLOWFORK=1)", fill=(255, 220, 0), font=fs)
    s.save(out); print(out)

if __name__ == "__main__":
    main(*sys.argv[1:6])
