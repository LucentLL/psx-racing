"""ROAD SURVEY: plan pictures (north up, metres, with a scale): county pavement polygons, the map's lines, the rule's
pavement (the class cross-section: width / 2 each side of the map line, no eases), survey stations, hand stations.

  py -I tools/city/survey/plans.py GPKG GRAPH.json OUTDIR NAME X0 Z0 X1 Z1 [--stations PKL] [--hand MEASURES] [--rule]
"""
import sys, os, pickle, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import shapely
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import county, survey as SV
from compare_gt import parse_hand

def offset_line(pts, d):
    P = np.asarray(pts, float); s = np.linspace(0, SV.edge_len(pts), max(2, int(SV.edge_len(pts) / 1.0) + 1))
    C, T, _ = SV.polyline_frame(pts, s); Nn = np.column_stack([-T[:, 1], T[:, 0]]); return C + Nn * d

def draw(gpkg, graph, outdir, name, box, stations=None, hand=None, rule=False, px=10.0, title=""):
    x0, z0, x1, z1 = box
    W, H = int((x1 - x0) * px), int((z1 - z0) * px)
    im = Image.new("RGB", (W, H + 60), (250, 250, 246)); d = ImageDraw.Draw(im)
    try: font = ImageFont.truetype("arial.ttf", 14); fsm = ImageFont.truetype("arial.ttf", 11)
    except Exception: font = fsm = ImageFont.load_default()
    P = lambda x, z: ((x - x0) * px, (z1 - z) * px + 60)
    data = county.load(gpkg, os.path.join(os.path.dirname(os.path.abspath(graph)), "county_xz.pkl"))
    G, A = data["geoms"], data["attrs"]; tree = shapely.STRtree(G)
    bx = shapely.box(x0, z0, x1, z1)
    for i in tree.query(bx):
        g = shapely.clip_by_rect(G[i], x0 - 5, z0 - 5, x1 + 5, z1 + 5)
        for poly in getattr(g, "geoms", [g]):
            if poly.geom_type != "Polygon" or poly.is_empty: continue
            if A[i][1] == "Edge-of-Pavement":
                d.polygon([P(*c) for c in poly.exterior.coords], fill=(205, 205, 200))
                for r in poly.interiors: d.polygon([P(*c) for c in r.coords], fill=(250, 250, 246))
    for i in tree.query(bx):
        g = shapely.clip_by_rect(G[i], x0 - 5, z0 - 5, x1 + 5, z1 + 5)
        for poly in getattr(g, "geoms", [g]):
            if poly.geom_type != "Polygon" or poly.is_empty: continue
            col = (200, 30, 30) if A[i][1] == "Edge-of-Pavement" else (0, 170, 210)
            for r in [poly.exterior] + list(poly.interiors): d.line([P(*c) for c in r.coords], fill=col, width=2)
    import json
    gr = json.load(open(graph)); E = gr["edges"]
    near = [e for e in E if any(x0 - 30 <= p[0] <= x1 + 30 and z0 - 30 <= p[1] <= z1 + 30 for p in e["pts"])]
    if rule:
        for e in near:
            for sd in (1, -1):
                q = offset_line(e["pts"], sd * e["w"] / 2); d.line([P(*c) for c in q], fill=(30, 80, 230), width=2)
    for e in near:
        d.line([P(*p) for p in e["pts"]], fill=(0, 0, 0), width=1)
        m = e["pts"][len(e["pts"]) // 2]; q = offset_line(e["pts"], 0)
        mid = q[len(q) // 2]; d.text(P(*mid), str(e["i"]), fill=(0, 0, 0), font=fsm)
        # arrow head: direction a -> b
        if len(q) > 3:
            a, b = q[len(q) // 2 - 2], q[len(q) // 2 + 1]; d.line([P(*a), P(*b)], fill=(0, 0, 0), width=3)
    if stations:
        R = pickle.load(open(stations, "rb"))
        for ei, r in R.items():
            Nn = np.column_stack([-r["T"][:, 1], r["T"][:, 0]])
            for side, key, col in ((1, "Lc", (0, 150, 0)), (-1, "Rc", (0, 150, 0))):
                v = r[key]
                for j in range(len(v)):
                    if np.isfinite(v[j]):
                        q = r["C"][j] + Nn[j] * side * v[j]; X, Y = P(*q); d.ellipse([X - 2.5, Y - 2.5, X + 2.5, Y + 2.5], fill=col)
    if hand:
        HM = parse_hand(hand)
        for ei, h in HM.items():
            e = E[ei]
            for (s, L, Rr) in h["st"]:
                C, T, _ = SV.polyline_frame(e["pts"], np.array([s])); Nn = np.array([-T[0, 1], T[0, 0]])
                for side, v in ((1, L), (-1, Rr)):
                    if v is None: continue
                    q = C[0] + Nn * side * v; X, Y = P(*q)
                    d.line([X - 5, Y - 5, X + 5, Y + 5], fill=(220, 0, 200), width=2); d.line([X - 5, Y + 5, X + 5, Y - 5], fill=(220, 0, 200), width=2)
    # scale bar + legend
    d.rectangle([0, 0, W, 58], fill=(20, 20, 20))
    d.text((8, 4), title or name, fill=(255, 255, 0), font=font)
    leg = "grey fill + red = county Edge-of-Pavement (Mecklenburg County GIS); cyan = sidewalk; black = map line (thick tick = a->b); "
    leg2 = ("blue = the RULE's pavement (class width / 2 each side); " if rule else "") + ("green dots = survey stations (cleaned); " if stations else "") + ("magenta x = hand measures; " if hand else "") + "north up"
    d.text((8, 22), leg, fill=(230, 230, 230), font=fsm); d.text((8, 38), leg2, fill=(230, 230, 230), font=fsm)
    bar = 20 if (x1 - x0) < 200 else 50
    X0, Y0 = 20, H + 40; d.rectangle([X0, Y0, X0 + bar * px, Y0 + 6], fill=(0, 0, 0)); d.text((X0, Y0 - 16), "%d m" % bar, fill=(0, 0, 0), font=font)
    d.line([W - 30, 120, W - 30, 80], fill=(0, 0, 0), width=3); d.text((W - 35, 64), "N", fill=(0, 0, 0), font=font)
    out = os.path.join(outdir, name + ".png"); im.save(out); return out

if __name__ == "__main__":
    a = sys.argv
    box = tuple(map(float, a[5:9]))
    st = a[a.index("--stations") + 1] if "--stations" in a else None
    hd = a[a.index("--hand") + 1] if "--hand" in a else None
    px = float(a[a.index("--px") + 1]) if "--px" in a else 10.0
    ti = a[a.index("--title") + 1] if "--title" in a else ""
    print(draw(a[1], a[2], a[3], a[4], box, st, hd, "--rule" in a, px, ti))
