"""ROAD SURVEY: streets the county has and the map lacks (a fault class of its own; counted, not fixed).

  py -I tools/city/survey/missing.py GPKG GRAPH.json OUT.pkl

 M1  Every map edge is buffered by its class half-width + 4 m. Each Edge-of-Pavement polygon less those buffers is
     what no map line lies under.
 M2  A left-over piece counts as a STREET when it is street-shaped: its length L and width W from the rectangle of the
     same area and perimeter (L = (P + sqrt(P^2 - 16 A)) / 4, W = A / L), with 4 m <= W <= 20 m and L >= 30 m. Wider
     or shorter pieces (lots, aprons, bell-mouths, the corners of a junction) are not counted as streets.
 M4  ... and when half its area or more lies more than 15 m beyond the map's class half-width (the wide verges,
     shoulders and interchange aprons of a road the map HAS are not counted; a missing frontage street within 15 m of a
     mapped road is missed by this rule).
 M3  Only polygons whose centre lies inside the map's fetch box are read (fetch_ways.mjs BBOX: lat 35.03 - 35.42, lon
     -81.03 - -80.62; the map has no roads of any class beyond it but for the ends of ways that cross it). District = the
     county's 'layer' (the jurisdiction) and whether it lies inside the Uptown core box of the map's street fetch
     (x -5823 .. 2848, z 491 .. 8680: tools/city/fetch/fetch_ways.mjs; outside it the map has tertiary and up only).
"""
import sys, os, math, pickle, json
import numpy as np
import shapely
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import county

CORE = (-5823, 491, 2848, 8680)

def main(gpkg, graph, out):
    g = json.load(open(graph)); E = g["edges"]; Nn = np.array([n[:2] for n in g["nodes"]])
    # M3: the map's fetch box (fetch_ways.mjs BBOX 35.03,-81.03,35.42,-80.62) in the game frame
    x0, z0 = county.ll_to_xz(35.03, -81.03); x1, z1 = county.ll_to_xz(35.42, -80.62)
    x0, z0, x1, z1 = float(x0), float(z0), float(x1), float(z1)
    d = county.load(gpkg, os.path.join(os.path.dirname(os.path.abspath(graph)), "county_xz.pkl"))
    bufs = shapely.buffer(np.array([shapely.LineString(e["pts"]) for e in E]), np.array([e["w"] / 2 + 4 for e in E]), quad_segs=2)
    bt = shapely.STRtree(bufs)
    bufs2 = shapely.buffer(np.array([shapely.LineString(e["pts"]) for e in E]), np.array([e["w"] / 2 + 15 for e in E]), quad_segs=2)
    rows = []
    for gi, (poly, at) in enumerate(zip(d["geoms"], d["attrs"])):
        if at[1] != "Edge-of-Pavement": continue
        c = poly.centroid
        if not (x0 <= c.x <= x1 and z0 <= c.y <= z1): continue
        near = bt.query(poly)
        rem = shapely.difference(poly, shapely.unary_union(bufs[near])) if len(near) else poly
        for p in shapely.get_parts(rem):
            if p.geom_type != "Polygon" or p.area < 150: continue
            A, P = p.area, p.length; L = (P + math.sqrt(max(0.0, P * P - 16 * A))) / 4; W = A / L
            street = 4 <= W <= 20 and L >= 30
            if street:   # M4: half its area or more lies over 15 m beyond the map's pavement (not a wide verge of a mapped road)
                n2 = bt.query(p.buffer(12))
                far = shapely.difference(p, shapely.unary_union(bufs2[n2])).area if len(n2) else A
                street = far >= 0.5 * A
            cx, cz = p.centroid.x, p.centroid.y
            core = CORE[0] <= cx <= CORE[2] and CORE[1] <= cz <= CORE[3]
            rows.append(dict(oid=at[0], layer=at[2], name=at[3], asset=at[4], area=A, L=L, W=W, street=street, x=cx, z=cz, core=core,
                             whole=len(near) == 0))
    pickle.dump(rows, open(out, "wb"))
    print("left-over pieces:", len(rows), "street-shaped:", sum(r["street"] for r in rows))

if __name__ == "__main__":
    main(*sys.argv[1:4])
