"""
Uptown REFERENCE TIERS (2026-10-12). The owner has a low-poly model of uptown
Charlotte (City Pack 7) with no rights to ship it: "I'd like them used as
reference ... views of Charlotte are public". So nothing of it goes into the
game - no mesh, no texture, no UV. What we take is MEASUREMENTS of public
buildings: for each tall OSM outline, the heights its roofs step at and the
outline of each step, read off the model's flat roof faces, and the height of
anything that rises above the last flat roof (a crown). They are written as
building:part-shaped records keyed to the OSM outline (truth/uptown_ref_parts.json)
and drawn by our own exporter + mesher exactly like OSM's own parts.
2026-10-16 (skyline tops): tiers are clipped to OSM's outline, and a crown is
measured for its shape (measure_crown) instead of always becoming a pyramid;
the export uses these tiers by default (PSX_CITY_REFTIERS=0 leaves them out).

Usage (the model is the owner's, outside the repo):
    blender --background --python <export script> -> pack triangles in metres
    py ref_tiers.py <pack_tris.json> <xform.json> [--check]

xform.json: the 2D similarity fitted from the model's towers to OSM's (refl,
s = complex scale+rotation, pa -> qa), local metres (export_osm.mjs toX/toZ).
"""
import io, json, math, sys
from shapely.geometry import Polygon, Point, MultiPolygon
from shapely.ops import unary_union

LAT0, LON0 = 35.18456015184093, -80.81770185962013      # export_osm.mjs
M_LAT = 111132.0
M_LON = 111320.0 * math.cos(LAT0 * math.pi / 180)
# the five landmarks drawn by hand (CityMeshes.Landmarks.cs Heroes): left alone
HEROES = {"w341587198", "w1550692284", "w131139746", "w90480703", "r12346952"}
MIN_H = 40.0          # outlines this tall or taller are measured
BUFFER = 4.0          # m round an outline the model may stand off it
LEVEL = 0.75          # roof faces within this many metres are one level
MIN_AREA = 25.0       # m2: smaller flat tops are rooftop plant, not a tier
CROWN_MIN = 4.0       # m above the last flat roof that reads as a crown
AGREE = 0.15          # model top vs OSM height, share of OSM


def ph(v):
    try:
        return float(str(v).split()[0].rstrip("m"))
    except Exception:
        return None


def largest(g):
    """The biggest polygon of a shapely result, holes filled (None if none)."""
    if g is None or g.is_empty:
        return None
    polys = [x for x in getattr(g, "geoms", [g]) if isinstance(x, Polygon) and not x.is_empty]
    return Polygon(max(polys, key=lambda x: x.area).exterior) if polys else None


SLOPED = 0.5          # share of a crown's plan that must slope to be a roof shape
ROUND_AT_HALF = 0.72  # height at half the half-width (of the rise): a circle 0.87, a gable 0.5


def measure_crown(mine, base, last, poly):
    """What rises above the last flat roof, measured: a barrel ("round": the
    highest points a ridge down the middle, the section a circle), a gable
    (the same ridge, straight slopes), a skillion (the ridge on one edge) or
    a pyramid (the highest points a point), over the footprint the crown's
    own sloped faces cover. None when it is none of those (a mast, a sign,
    plant smaller than a tier): such a crown is left off, never guessed."""
    from shapely.geometry import MultiPoint, LineString
    # (the zone is the tower's own outline: the last flat level can be a
    # strip of terrace beside the crown, not a roof under it)
    lvl = last["h"]
    zone = poly.buffer(1.0)
    tris = []
    for pts, z, nz in mine:
        zr = [zz - base for zz in z]
        if max(zr) <= lvl + 0.5:
            continue
        # the piece of the face above the last roof (a low-poly model's faces
        # run on down past it): clipped at lvl, Sutherland-Hodgman
        poly3 = []
        for i in range(3):
            a, b = (pts[i], zr[i]), (pts[(i + 1) % 3], zr[(i + 1) % 3])
            if a[1] >= lvl:
                poly3.append(a)
            if (a[1] >= lvl) != (b[1] >= lvl):
                f = (lvl - a[1]) / (b[1] - a[1])
                poly3.append(((a[0][0] + f * (b[0][0] - a[0][0]), a[0][1] + f * (b[0][1] - a[0][1])), lvl))
        if len(poly3) < 3:
            continue
        cx, cy = sum(p[0][0] for p in poly3) / len(poly3), sum(p[0][1] for p in poly3) / len(poly3)
        if zone.contains(Point(cx, cy)):
            tris.append(([p[0] for p in poly3], [p[1] for p in poly3], nz))
    flat = [Polygon(p) for p, zr, nz in tris if nz > 0.05 and Polygon(p).area > 0.01]
    if not flat:
        return None
    fp = largest(unary_union(flat).buffer(0.3).buffer(-0.3))
    fp = largest(fp.simplify(0.6, preserve_topology=True).intersection(poly)) if fp else None
    if fp is None or fp.area < MIN_AREA:
        return None
    sloped = sum(Polygon(p).area for p, zr, nz in tris if 0.05 < nz < 0.95)
    if sloped < SLOPED * fp.area:
        return None
    ztop = max(max(zr) for p, zr, nz in tris)
    rise_all = ztop - lvl
    # the crown's footprint box and the box of its highest points
    def box(geom):
        r = geom.minimum_rotated_rectangle
        c = list(r.exterior.coords)[:4]
        e0 = (c[1][0] - c[0][0], c[1][1] - c[0][1]); e1 = (c[2][0] - c[1][0], c[2][1] - c[1][1])
        l0, l1 = math.hypot(*e0), math.hypot(*e1)
        ax = e0 if l0 >= l1 else e1
        m = math.hypot(*ax) or 1.0
        return r.centroid, (ax[0] / m, ax[1] / m), max(l0, l1), min(l0, l1)
    fc, fax, fl, fw = box(fp)
    hi = [p for pts, zr, nz in tris for p, zz in zip(pts, zr) if zz >= ztop - 0.3]
    if len(hi) < 2:
        return None
    hm = MultiPoint(hi)
    if hm.minimum_rotated_rectangle.geom_type != "Polygon":
        # collinear: the farthest pair is its length
        tl = max(math.hypot(a[0] - b[0], a[1] - b[1]) for a in hi for b in hi)
        tw, tc = 0.0, hm.centroid
    else:
        tc, _, tl, tw = box(hm)
    out = {"minH": round(lvl, 1), "h": round(ztop, 1), "pg": fp, "dir": None}
    if tl <= max(3.0, 0.25 * fl) and tw <= max(3.0, 0.25 * fw):
        out["roof"], out["roofH"] = "pyramidal", rise_all
        return out
    if tl >= 0.5 * fl and tw <= max(3.0, 0.35 * fw):
        # a ridge: across the footprint's short axis, where does it stand?
        nx, ny = -fax[1], fax[0]
        off = (tc.x - fc.x) * nx + (tc.y - fc.y) * ny
        if abs(off) > 0.3 * fw:
            # on one edge: a skillion falling away from it (OSM roof:direction
            # is the compass bearing downhill; x east, z north)
            dx, dy = (-nx, -ny) if off > 0 else (nx, ny)
            out["roof"], out["roofH"] = "skillion", rise_all
            out["dir"] = int(round(math.degrees(math.atan2(dx, dy)))) % 360
            return out
        # centred: the section's height at half the half-width says circle or gable
        half = fw / 2
        ridge = LineString([(tc.x - fax[0] * fl, tc.y - fax[1] * fl), (tc.x + fax[0] * fl, tc.y + fax[1] * fl)])
        hs = [zz for pts, zr, nz in tris if 0.05 < nz < 0.95 for p, zz in zip(pts, zr)
              if 0.35 * half <= ridge.distance(Point(p)) <= 0.65 * half]
        r = min(rise_all, half)
        spring = ztop - r
        if hs and r > 0.5 and sum((zz - spring) / r for zz in hs) / len(hs) >= ROUND_AT_HALF:
            out["roof"], out["roofH"] = "round", r
        else:
            out["roof"], out["roofH"] = "gabled", rise_all
        return out
    return None


def main():
    tris_path, xf_path = sys.argv[1], sys.argv[2]
    check = "--check" in sys.argv
    xf = json.load(open(xf_path))
    refl, s = xf["refl"], complex(*xf["s"])
    pa, qa = complex(*xf["pa"]), complex(*xf["qa"])

    def to_local(x, y):
        q = qa + s * (complex(x, refl * y) - pa)
        return q.real, q.imag

    tris = []
    for t in json.load(open(tris_path)):
        a = to_local(t[0], t[1]); b = to_local(t[3], t[4]); c = to_local(t[6], t[7])
        z = (t[2], t[5], t[8])
        ux, uy, uz = t[3] - t[0], t[4] - t[1], t[5] - t[2]
        vx, vy, vz = t[6] - t[0], t[7] - t[1], t[8] - t[2]
        nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        m = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
        tris.append(((a, b, c), z, abs(nz) / m))
    xs = [p[0] for t in tris for p in t[0]]; zs = [p[1] for t in tris for p in t[0]]
    ext = (min(xs), min(zs), max(xs), max(zs))

    targets, others = [], []
    for el in json.load(io.open("cache/buildings_core.json", encoding="utf-8"))["elements"]:
        t = el.get("tags") or {}
        key = el["type"][0] + str(el["id"])
        if not t.get("building"):
            continue
        g = el.get("geometry")
        if not g:
            outer = [m for m in el.get("members", []) if m.get("role") == "outer" and m.get("geometry")]
            if not outer:
                continue
            g = outer[0]["geometry"]
        h = ph(t.get("height"))
        if h is None and t.get("building:levels"):
            h = (ph(t["building:levels"]) or 0) * 3.4
        pts = [((p["lon"] - LON0) * M_LON, (p["lat"] - LAT0) * M_LAT) for p in g]
        if len(pts) < 3:
            continue
        poly = Polygon(pts).buffer(0)
        c = poly.centroid
        if poly.is_empty or not (ext[0] - 50 < c.x < ext[2] + 50 and ext[1] - 50 < c.y < ext[3] + 50):
            continue
        others.append((key, poly))
        if key in HEROES or not h or h < MIN_H:
            continue
        targets.append((key, poly, h))

    # every triangle to ONE outline: the one holding its centre, else the
    # nearest within BUFFER (a neighbour's tower must not be read as a crown)
    from shapely.strtree import STRtree
    tree = STRtree([o[1] for o in others])
    owner = {}
    for ti, t in enumerate(tris):
        c = Point(sum(p[0] for p in t[0]) / 3, sum(p[1] for p in t[0]) / 3)
        cand = tree.query(c.buffer(BUFFER))
        best, bd = None, BUFFER
        for k in cand:
            d = others[k][1].distance(c)
            if d < bd or (d == 0 and best is None):
                best, bd = k, d
            if d == 0:
                break
        if best is not None:
            owner.setdefault(others[best][0], []).append(t)

    out, report = [], []
    nid = 0
    for key, poly, osm_h in targets:
        zone = poly.buffer(BUFFER)
        mine = owner.get(key, [])
        if len(mine) < 6:
            report.append((key, osm_h, None, "no model geometry"))
            continue
        base = min(min(t[1]) for t in mine)
        top = max(max(t[1]) for t in mine)
        # the model and OSM must be the same building: OSM keeps the final
        # word on height, so a model that disagrees by more than AGREE is a
        # different (older/newer/merged) building and is not used
        if abs((top - base) - osm_h) > max(AGREE * osm_h, 8.0):
            report.append((key, osm_h, round(top - base), "height disagrees - skipped"))
            continue
        # flat roof faces by level
        levels = {}
        for pts, z, nz in mine:
            if nz < 0.95 or max(z) - min(z) > 0.3:
                continue
            zl = round((sum(z) / 3) / LEVEL) * LEVEL
            if zl - base < 3.0:
                continue                      # ground / plaza
            tri = Polygon(pts)
            if tri.area > 0.01:
                levels.setdefault(zl, []).append(tri)
        tiers = []
        for zl in sorted(levels):
            u = unary_union(levels[zl]).buffer(0.3).buffer(-0.3).intersection(zone)
            geoms = list(u.geoms) if isinstance(u, MultiPolygon) else [u]
            for gm in geoms:
                if gm.is_empty or gm.area < MIN_AREA:
                    continue
                # 2026-10-16: clipped to OSM's outline (the model is fitted to
                # ~7 m rms; OSM keeps the final word on where the walls stand)
                filled = largest(Polygon(gm.exterior).simplify(0.6, preserve_topology=True).intersection(poly))
                if filled is not None and filled.area >= MIN_AREA:
                    tiers.append([zl - base, filled])
        if not tiers:
            report.append((key, osm_h, round(top - base), "no flat roof"))
            continue
        # min height: the highest lower tier the part stands on
        parts = []
        for i, (hh, pg) in enumerate(tiers):
            rp = pg.representative_point()
            under = [h2 for h2, p2 in tiers[:i] if h2 < hh - 1 and p2.contains(rp)]
            parts.append({"h": hh, "minH": max(under) if under else 0.0, "pg": pg})
        last = max(parts, key=lambda p: p["h"])
        crown = (top - base) - last["h"]
        what = ""
        if crown >= CROWN_MIN:
            # 2026-10-16: the crown is MEASURED for its shape (it was a
            # pyramid whatever stood there): its own part over its own
            # footprint, or left off when it is no shape a roof can name
            cp = measure_crown(mine, base, last, poly)
            if cp:
                # it stands on the highest tier under its middle (the last
                # flat level can be a strip of terrace beside it: a crown
                # from there would float over the core between the strips)
                rp = cp["pg"].representative_point()
                under = [p["h"] for p in parts if p["h"] < cp["minH"] + 0.5 and p["pg"].contains(rp)]
                cp["minH"] = round(max(under), 1) if under else 0.0
                parts.append(cp)
                what = ", crown %.0f m %s" % (crown, cp["roof"])
            else:
                what = ", crown %.0f m left off (no roof shape)" % crown
        for p in parts:
            nid += 1
            ring = list(p["pg"].exterior.coords)[:-1]
            tags = {"building:part": "yes", "height": "%.1f" % p["h"],
                    "min_height": "%.1f" % p["minH"], "psx:ref": key}
            if "roof" in p:
                tags["roof:shape"] = p["roof"]
                tags["roof:height"] = "%.1f" % p["roofH"]
                if p.get("dir") is not None:
                    tags["roof:direction"] = "%d" % p["dir"]
            out.append({"type": "way", "id": 9000000000 + nid, "tags": tags,
                        "geometry": [{"lat": round(LAT0 + y / M_LAT, 7), "lon": round(LON0 + x / M_LON, 7)} for x, y in ring]})
        report.append((key, osm_h, round(top - base), "%d tiers%s" % (len(parts), what)))

    for r in sorted(report, key=lambda r: -(r[1] or 0)):
        print("%-14s osm %5.0f  model %5s  %s" % (r[0], r[1], r[2], r[3]))
    print("%d outlines measured, %d parts" % (len({e['tags']['psx:ref'] for e in out}), len(out)))
    if not check:
        with io.open("truth/uptown_ref_parts.json", "w", encoding="utf-8", newline="\n") as f:
            json.dump({"note": "measured from a reference model of uptown (not shipped); see ref_tiers.py",
                       "elements": out}, f, indent=0)
        print("wrote truth/uptown_ref_parts.json")


if __name__ == "__main__":
    main()
