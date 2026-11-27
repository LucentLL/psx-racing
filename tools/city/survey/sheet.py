"""ROAD SURVEY stage 2: picture sheets OLD | NEW (EDITOR plan40 frames, CityC7Probe) | COUNTY (the same 71.1 x 40 m box).

  py -I tools/city/survey/sheet.py GPKG GRAPH.json STATIONS.pkl OLD_SHOTS NEW_SHOTS POSES.tsv SOLVE_TABLE.txt OUTDIR [FAULTS]

FAULTS: "id:x,z,label;id:x,z,label" world points circled red on the NEW frame (a fault shown as a fault).
"""
import sys, os
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import plans

def main(gpkg, graph, stp, olddir, newdir, poses, solve, outdir, faults=""):
    os.makedirs(outdir, exist_ok=True)
    res = {}
    for l in open(solve, encoding="utf-8"):
        w = l.split()
        if w and (w[0].startswith("t") or w[0] in ("s80", "s81", "wendover")) and len(w) > 3: res[w[0]] = l.rstrip()
    F = {}
    for f in [x for x in faults.split(";") if x]:
        i, rest = f.split(":", 1); x, z, lab = rest.split(",", 2); F.setdefault(i, []).append((float(x), float(z), lab))
    try: font = ImageFont.truetype("arial.ttf", 15); fsm = ImageFont.truetype("arial.ttf", 12)
    except Exception: font = fsm = ImageFont.load_default()
    W, H = 640, 360
    for row in open(poses):
        c = row.rstrip("\n").split("\t")
        if len(c) < 4: continue
        id_, cx, cz = c[0], float(c[1]), float(c[2])
        cells = []
        for d, tag in ((olddir, "old"), (newdir, "new")):
            p = os.path.join(d, "%s_plan40_%s.png" % (id_, tag))
            im = Image.open(p).convert("RGB").resize((W, H)) if os.path.exists(p) else Image.new("RGB", (W, H), (60, 0, 0))
            dr = ImageDraw.Draw(im)
            if tag == "new":
                for (x, z, lab) in F.get(id_, []):
                    u = (x - cx) / 71.11 * W + W / 2; v = (cz - z) / 40.0 * H + H / 2
                    dr.ellipse([u - 26, v - 26, u + 26, v + 26], outline=(255, 0, 0), width=4); dr.text((u + 28, v - 8), lab, fill=(255, 0, 0), font=fsm)
            dr.rectangle([0, 0, W, 22], fill=(0, 0, 0)); dr.text((6, 3), "EDITOR %s (plan40, 71.1 x 40 m, north up)%s" % (tag.upper(), "" if os.path.exists(p) else ": NO FRAME"), fill=(255, 255, 0), font=font)
            cells.append(im)
        cp = plans.draw(gpkg, graph, outdir, "_county_" + id_, (cx - 35.555, cz - 20, cx + 35.555, cz + 20), stp, None, True, 9.0, "COUNTY + survey + RULE (blue)")
        ci = Image.open(cp).convert("RGB"); ci = ci.crop((0, 60, ci.width, ci.height)).resize((W, H)); os.remove(cp)
        dr = ImageDraw.Draw(ci); dr.rectangle([0, 0, W, 22], fill=(0, 0, 0)); dr.text((6, 3), "COUNTY polygons, survey stations (green), rule (blue)", fill=(255, 255, 0), font=font)
        cells.append(ci)
        sheet = Image.new("RGB", (W * 3, H + 44), (20, 20, 20)); d = ImageDraw.Draw(sheet)
        for i, im in enumerate(cells): sheet.paste(im, (i * W, 44))
        d.text((8, 4), "%s  x %.1f z %.1f" % (id_, cx, cz), fill=(255, 255, 255), font=font)
        d.text((8, 24), (res.get(id_, "")[:200]), fill=(255, 200, 120), font=fsm)
        sheet.save(os.path.join(outdir, "sheet_%s.png" % id_))
        print("sheet", id_)

if __name__ == "__main__":
    main(*sys.argv[1:10])
