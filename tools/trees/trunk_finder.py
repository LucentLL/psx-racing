# Where does each billboard PAINT its trunk, and how wide is what a car meets?
#
#   py tools/trees/trunk_finder.py <Art/BRP/Trees dir> <out_sheet.png>
#
# The prototype of TreeKit.FindTrunk (the builder's C# is a port of this, and the
# sheet is how the rule was checked against every billboard the five dresses use).
# The forest's trunk collider stands where a tree's two cards CROSS - the middle
# of the cell. The pack paints its trunks wherever the photograph had them: up to
# two metres off that line. So the atlas composer slides each billboard sideways
# until the painted trunk is under the crossing, and this finds how far.
#
# Red = the cell's middle (where the collider is). Green = the trunk this finds.
# Yellow bracket = what is opaque between 0.3 and 1.3 m up (what a bumper meets).
import sys, os
import numpy as np
from PIL import Image, ImageDraw

FALL   = ["tree016","tree017","tree019","tree020","tree021","tree022","tree025","tree028",
          "tree030","tree018","tree027","tree112","tree066","tree057","tree061","tree008"]
WINTER = ["tree001","tree002","tree003","tree005","tree006","tree007","tree009","tree047",
          "tree012","tree013","tree015","tree004","tree057","tree061","tree063","tree008"]
SPRING = ["tree084","tree118","tree080","tree085","tree083","tree114","tree087","tree092",
          "tree098","tree018","tree093","tree106","tree066","tree057","tree061","tree010"]
SUMMER = ["tree116","tree099","tree110","tree097","tree100","tree088","tree106","tree103",
          "tree107","tree018","tree027","tree112","tree066","tree057","tree061","tree082"]
SNOW   = WINTER[:12] + ["tree065","tree056","tree062","tree008"]

def find_trunk(alpha):
    """alpha: HxW bool, row 0 = TOP of the image. Returns (centre_col, width_cols, base_row)."""
    H, W = alpha.shape
    rows = np.where(alpha.any(1))[0]
    if len(rows) == 0: return (W - 1) / 2.0, 0, H - 1
    base = rows.max()                       # lowest opaque row
    band = alpha[max(0, base - 5):base + 1] # the bottom six rows: the foot of the tree
    count = band.sum(0)
    cols = count >= 2
    # contiguous groups of columns that are opaque at the foot
    groups, start = [], None
    for x in range(W + 1):
        on = x < W and cols[x]
        if on and start is None: start = x
        if not on and start is not None: groups.append((start, x - 1)); start = None
    if not groups: return (W - 1) / 2.0, 0, base
    # The trunk is the group nearest the crown's own centre of mass: a drooping
    # branch that reaches the ground at the card's edge is not the trunk.
    xs = np.arange(W)
    mass = alpha.sum(0).astype(np.float64)
    centroid = (mass * xs).sum() / max(mass.sum(), 1)
    def score(g):
        c = (g[0] + g[1]) / 2.0
        return abs(c - centroid) - 0.25 * min(g[1] - g[0] + 1, 12)   # nearer wins, wider breaks ties
    g = min(groups, key=score)
    # Follow that group up a few rows for a steadier centre (a root flare is lopsided).
    lo, hi = g
    cs = []
    for y in range(base, max(0, base - 10), -1):
        seg = np.where(alpha[y, max(0, lo - 2):min(W, hi + 3)])[0]
        if len(seg): cs.append(max(0, lo - 2) + (seg.min() + seg.max()) / 2.0)
    centre = float(np.median(cs)) if cs else (lo + hi) / 2.0
    return centre, hi - lo + 1, base

def bumper_span(alpha, base, card_h_m):
    """Columns opaque between 0.3 and 1.3 m above the foot."""
    H, W = alpha.shape
    px_per_m = H / card_h_m
    y1 = int(round(base - 0.3 * px_per_m)); y0 = int(round(base - 1.3 * px_per_m))
    band = alpha[max(0, y0):max(1, y1)]
    cols = np.where(band.any(0))[0]
    return (cols.min(), cols.max()) if len(cols) else None

def main():
    src, out = sys.argv[1], sys.argv[2]
    names = []
    for d in (FALL, WINTER, SPRING, SUMMER, SNOW):
        for n in d:
            if n not in names: names.append(n)
    cell, scale, per_row = 128, 2, 10
    rows = (len(names) + per_row - 1) // per_row
    sheet = Image.new("RGB", (per_row * cell * scale, rows * (cell * scale + 14)), (90, 120, 160))
    dr = ImageDraw.Draw(sheet)
    print(f"{'file':8s} {'trunk off-centre':>17s} {'trunk width':>12s}")
    worst = 0
    for i, n in enumerate(names):
        im = Image.open(os.path.join(src, n + ".png")).convert("RGBA").resize((cell, cell), Image.NEAREST)
        a = np.asarray(im)[..., 3] > 127
        c, w, base = find_trunk(a)
        off = c - (cell - 1) / 2.0
        worst = max(worst, abs(off))
        span = bumper_span(a, base, 10.5)
        print(f"{n:8s} {off:+8.1f} px ({off * 10.5 / cell:+5.2f} m) {w:6d} px ({w * 10.5 / cell:4.2f} m)"
              + (f"   bumper span {span[0] - 63.5:+6.1f}..{span[1] - 63.5:+6.1f} px" if span else ""))
        ox, oy = (i % per_row) * cell * scale, (i // per_row) * (cell * scale + 14)
        bg = Image.new("RGBA", (cell, cell), (90, 120, 160, 255)); bg.alpha_composite(im)
        sheet.paste(bg.convert("RGB").resize((cell * scale, cell * scale), Image.NEAREST), (ox, oy))
        dr.line([(ox + cell * scale // 2, oy), (ox + cell * scale // 2, oy + cell * scale)], fill=(255, 0, 0))
        gx = ox + int((c + 0.5) * scale)
        dr.line([(gx, oy + cell * scale - 40), (gx, oy + cell * scale)], fill=(0, 255, 0), width=2)
        if span:
            yb = oy + int((base - 0.8 * cell / 10.5) * scale)
            dr.line([(ox + span[0] * scale, yb), (ox + (span[1] + 1) * scale, yb)], fill=(255, 255, 0), width=2)
        dr.text((ox + 3, oy + cell * scale), n + f"  {off:+.0f}px", fill=(255, 255, 255))
    sheet.save(out)
    print(f"worst offset {worst:.1f} px = {worst * 10.5 / cell:.2f} m on a 10.5 m card; sheet {out}")

if __name__ == "__main__":
    main()
