"""
Uptown REFERENCE LOOKS (2026-10-12). Not one of uptown's 93 towers carries
building:colour or building:material in OSM, so every one wears a facade
picked by hash (CityMeshes.PickFacade). The owner's model of uptown (City Pack
7 - reference only, never shipped: "views of Charlotte are public") shows what
each one is clad in. This MEASURES it: the mean colour of each outline's walls
in the model, and from that colour the family (glass, brick, stone), written
as building:colour / building:material for the exporter to read where OSM is
silent (truth/uptown_ref_looks.json). No pixel of the model is shipped: one
colour and one word per building.

Usage: py ref_looks.py <pack_facade_cols.json> <xform.json> [--check]
pack_facade_cols.json: [x, y, area, r, g, b] per wall triangle of the model
(sRGB 0..1, sampled from its textures), model metres.

RHYTHM (facade structure, 2026-10-16):
       py ref_looks.py --rhythm <cp7_rhythm.json> <xform.json> [--check]
adds "psx:rhythm" (vertical / horizontal / grid) to the towers ALREADY in
truth/uptown_ref_looks.json, leaving their colour and material as they are.
cp7_rhythm.json (ref/blender_wallrhythm.py): [x, y, samples, along, up, lum]
per wall triangle - how much the luminance changes going ALONG the wall
(vertical lines: piers, fins) and going UP it (horizontal lines: spandrels,
ribbons). A tower whose along/up ratio is high reads as piers, low as
ribbons, between as a punched grid (CityMeshes.PickFacade, PSX/Lit's
FACADE STRUCTURE).
"""
import colorsys, io, json, math, sys
from shapely.geometry import Polygon, Point
from shapely.strtree import STRtree

LAT0, LON0 = 35.18456015184093, -80.81770185962013      # export_osm.mjs
M_LAT = 111132.0
M_LON = 111320.0 * math.cos(LAT0 * math.pi / 180)
MIN_H = 20.0          # outlines this tall or taller get a measured look
BUFFER = 4.0
MIN_AREA = 150.0      # m2 of measured wall before a look is trusted
COVER = 0.25          # share of the outline's walls (perimeter x height) the model must hold


def ph(v):
    try:
        return float(str(v).split()[0].rstrip("m"))
    except Exception:
        return None


def family(mr, mg, mb):
    """glass / brick / stone from a wall's MEAN colour, windows and all (a
    curtain wall's bright texels are sky, so the cladding colour alone calls
    glass stone); the colour written is the cladding's (see main)."""
    h, s, v = colorsys.rgb_to_hsv(mr, mg, mb)
    hue = h * 360
    if 160 <= hue <= 250 and s >= 0.10:
        return "glass"
    if v < 0.30:
        return "glass"            # dark tinted curtain wall
    if (hue <= 35 or hue >= 345) and s >= 0.28 and v < 0.70:
        return "brick"
    return "stone"


RHYTHM_V = 1.30      # along/up at or above: vertical (piers, fins)
RHYTHM_H = 0.80      # at or below: horizontal (ribbons, spandrels)
RHYTHM_MIN = 400     # wall samples (0.8 m apart) before a rhythm is trusted


def outlines_core():
    outlines, keys = [], []
    for el in json.load(io.open("cache/buildings_core.json", encoding="utf-8"))["elements"]:
        t = el.get("tags") or {}
        if not t.get("building"):
            continue
        g = el.get("geometry")
        if not g:
            outer = [m for m in el.get("members", []) if m.get("role") == "outer" and m.get("geometry")]
            if not outer:
                continue
            g = outer[0]["geometry"]
        if len(g) < 3:
            continue
        poly = Polygon([((p["lon"] - LON0) * M_LON, (p["lat"] - LAT0) * M_LAT) for p in g]).buffer(0)
        if poly.is_empty:
            continue
        outlines.append(poly); keys.append(el["type"][0] + str(el["id"]))
    return outlines, keys


def rhythm_main(rh_path, xf_path, check):
    xf = json.load(open(xf_path))
    refl, s = xf["refl"], complex(*xf["s"])
    pa, qa = complex(*xf["pa"]), complex(*xf["qa"])
    doc = json.load(io.open("truth/uptown_ref_looks.json", encoding="utf-8"))
    looks = doc["looks"]
    outlines, keys = outlines_core()
    tree = STRtree(outlines)
    acc = {}
    for x, y, n, ev, eh, _ in json.load(open(rh_path)):
        q = qa + s * (complex(x, refl * y) - pa)
        c = Point(q.real, q.imag)
        best, bd = None, BUFFER
        for k in tree.query(c.buffer(BUFFER)):
            d = outlines[k].distance(c)
            if d < bd:
                best, bd = k, d
        if best is None or keys[best] not in looks:
            continue
        e = acc.setdefault(keys[best], [0, 0.0, 0.0])
        e[0] += n; e[1] += ev; e[2] += eh
    counts = {"vertical": 0, "horizontal": 0, "grid": 0}
    for key in sorted(looks):
        looks[key].pop("psx:rhythm", None)
        n, ev, eh = acc.get(key, (0, 0.0, 0.0))
        if n < RHYTHM_MIN or eh <= 0:
            print("%-14s %-6s %5d samples - no rhythm" % (key, looks[key]["building:material"], n))
            continue
        r = ev / eh
        rh = "vertical" if r >= RHYTHM_V else "horizontal" if r <= RHYTHM_H else "grid"
        looks[key]["psx:rhythm"] = rh
        counts[rh] += 1
        print("%-14s %-6s %6d samples  along/up %.2f  %s" % (key, looks[key]["building:material"], n, r, rh))
    print("rhythm for %d of %d measured towers: %s" % (sum(counts.values()), len(looks), counts))
    if not check:
        with io.open("truth/uptown_ref_looks.json", "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=1, sort_keys=True)
        print("wrote truth/uptown_ref_looks.json")


def main():
    if sys.argv[1] == "--rhythm":
        return rhythm_main(sys.argv[2], sys.argv[3], "--check" in sys.argv)
    cols_path, xf_path = sys.argv[1], sys.argv[2]
    check = "--check" in sys.argv
    xf = json.load(open(xf_path))
    refl, s = xf["refl"], complex(*xf["s"])
    pa, qa = complex(*xf["pa"]), complex(*xf["qa"])
    pts = []
    for row in json.load(open(cols_path)):
        x, y, area, r, g, b = row[:6]
        mr, mg, mb = row[6:9] if len(row) >= 9 else (r, g, b)
        q = qa + s * (complex(x, refl * y) - pa)
        pts.append((q.real, q.imag, max(area, 0.01), r, g, b, mr, mg, mb))

    outlines, keys, tall, walls = [], [], {}, {}
    for el in json.load(io.open("cache/buildings_core.json", encoding="utf-8"))["elements"]:
        t = el.get("tags") or {}
        if not t.get("building"):
            continue
        g = el.get("geometry")
        if not g:
            outer = [m for m in el.get("members", []) if m.get("role") == "outer" and m.get("geometry")]
            if not outer:
                continue
            g = outer[0]["geometry"]
        if len(g) < 3:
            continue
        poly = Polygon([((p["lon"] - LON0) * M_LON, (p["lat"] - LAT0) * M_LAT) for p in g]).buffer(0)
        if poly.is_empty:
            continue
        key = el["type"][0] + str(el["id"])
        outlines.append(poly); keys.append(key)
        h = ph(t.get("height"))
        if h is None and t.get("building:levels"):
            h = (ph(t["building:levels"]) or 0) * 3.4
        if h and h >= MIN_H and not (t.get("building:colour") or t.get("building:color") or t.get("building:material")):
            tall[key] = h
            walls[key] = poly.length * h

    tree = STRtree(outlines)
    acc = {}
    for x, y, a, r, g, b, mr, mg, mb in pts:
        c = Point(x, y)
        best, bd = None, BUFFER
        for k in tree.query(c.buffer(BUFFER)):
            d = outlines[k].distance(c)
            if d < bd:
                best, bd = k, d
        if best is None or keys[best] not in tall:
            continue
        e = acc.setdefault(keys[best], [0.0] * 7)
        e[0] += a; e[1] += r * a; e[2] += g * a; e[3] += b * a
        e[4] += mr * a; e[5] += mg * a; e[6] += mb * a

    looks, counts = {}, {"glass": 0, "brick": 0, "stone": 0}
    for key, (a, r, g, b, mr, mg, mb) in sorted(acc.items(), key=lambda kv: -tall[kv[0]]):
        # the model must hold most of this building's walls: a tower newer
        # than the model is otherwise coloured by a neighbour's podium
        if a < MIN_AREA or a < COVER * walls[key]:
            print("%-14s %5.0f m  walls %.0f%% measured - skipped" % (key, tall[key], 100 * a / walls[key]))
            continue
        r, g, b = r / a, g / a, b / a
        fam = family(mr / a, mg / a, mb / a)
        counts[fam] += 1
        hexc = "#%02x%02x%02x" % tuple(int(round(min(1, max(0, c)) * 255)) for c in (r, g, b))
        looks[key] = {"building:colour": hexc, "building:material": fam}
        print("%-14s %5.0f m  %s  %s" % (key, tall[key], hexc, fam))
    print("%d of %d tall outlines measured: %s" % (len(looks), len(tall), counts))
    if not check:
        with io.open("truth/uptown_ref_looks.json", "w", encoding="utf-8", newline="\n") as f:
            json.dump({"note": "measured from a reference model of uptown (not shipped); see ref_looks.py",
                       "looks": looks}, f, indent=1, sort_keys=True)
        print("wrote truth/uptown_ref_looks.json")


if __name__ == "__main__":
    main()
