# THE UPTOWN GROUND FLOORS' DATA (charlotte-ground-floors, 2026-11-10): what OpenStreetMap says a building
# inside the freeway loop IS, for CityMeshes.GroundFloors.cs's rule - a parking deck, a hotel, a church, a
# museum or theatre, a shop, homes - and where a deck's driveway crosses its outline (its entrance).
#
#   py -3 tools/city/groundfloors.py [--cache <tools/city/cache>] [--check]
#
# Reads the cached Overpass snapshot (buildings_core.json, ways_all.json, streets_core.json; set --cache or
# PSX_CITY_CACHE to the main checkout's cache from a worktree) and writes
# Assets/PSXRacing/Scripts/City/CityGroundFloors.Data.cs: one row a tagged building - a point INSIDE its
# outline (game metres, x east, z north) and its kind - and one row a deck entrance. No names: a row is a
# place and a kind (Docs: NO BRANDS). --check: exit 1 when the file on disk differs from what would be written.
import json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
cache = os.environ.get("PSX_CITY_CACHE") or os.path.join(HERE, "cache")
if "--cache" in sys.argv: cache = sys.argv[sys.argv.index("--cache") + 1]
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Scripts", "City", "CityGroundFloors.Data.cs")
LAT0, LON0 = 35.18456015184093, -80.81770185962013
MX = 111320.0 * math.cos(math.radians(35.18456)); MZ = 111132.0
SQUARE = (35.22720, -80.84310)      # Trade and Tryon
REACH_M = 1900.0                    # past the loop every way; the game's own loop test (CityPoles.Uptown) decides

# the kinds (CityMeshes.GroundFloors.cs holds the same numbers)
DECK, HOTEL, WORSHIP, CIVIC, SHOP, HOMES = 1, 2, 3, 4, 5, 6
KIND_NAME = {DECK: "Deck", HOTEL: "Hotel", WORSHIP: "Worship", CIVIC: "Civic", SHOP: "Shop", HOMES: "Homes"}


def xz(lat, lon): return ((lon - LON0) * MX, (lat - LAT0) * MZ)


def kind_of(t):
    b = t.get("building", ""); a = t.get("amenity", "")
    if b == "parking" or t.get("parking") == "multi-storey" or (a == "parking" and b not in ("yes", "roof")): return DECK
    if a == "parking" and t.get("parking") in ("multi-storey", None) and b == "yes" and "building:levels" in t: return DECK
    if b == "hotel" or t.get("tourism") == "hotel": return HOTEL
    if b in ("church", "cathedral", "chapel") or a == "place_of_worship": return WORSHIP
    if (t.get("tourism") in ("museum", "gallery") or a in ("theatre", "cinema", "arts_centre", "courthouse", "library", "townhall",
        "conference_centre", "events_venue", "police", "fire_station", "bus_station", "post_office", "prison", "community_centre")
            or b in ("stadium", "civic", "public", "government", "jail", "fire_station", "train_station", "transportation", "sports_centre", "sports_hall")
            or t.get("leisure") in ("sports_centre", "sports_hall", "stadium") or t.get("office") == "telecommunication"): return CIVIC
    if b in ("retail", "supermarket") or "shop" in t or a in ("restaurant", "bar", "pub", "cafe", "fast_food", "bank", "ice_cream", "nightclub"): return SHOP
    if b in ("apartments", "residential", "dormitory") or t.get("tourism") == "apartment": return HOMES
    return 0


def inside(poly, p):
    x, z = p; c = False
    for i in range(len(poly)):
        a, b = poly[i], poly[i - 1]
        if (a[1] > z) != (b[1] > z) and x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]: c = not c
    return c


def inner_point(poly):
    """A point inside: the centroid, or the middle of the widest run of a scanline through it."""
    cx = sum(p[0] for p in poly) / len(poly); cz = sum(p[1] for p in poly) / len(poly)
    if inside(poly, (cx, cz)): return cx, cz
    zs = sorted(p[1] for p in poly)
    best = None
    for k in range(1, 8):
        z = zs[0] + (zs[-1] - zs[0]) * k / 8.0
        xs = []
        for i in range(len(poly)):
            a, b = poly[i], poly[i - 1]
            if (a[1] > z) != (b[1] > z): xs.append((b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0])
        xs.sort()
        for j in range(0, len(xs) - 1, 2):
            if best is None or xs[j + 1] - xs[j] > best[0]: best = (xs[j + 1] - xs[j], (xs[j] + xs[j + 1]) * 0.5, z)
    return (best[1], best[2]) if best else (cx, cz)


def seg_hit(p, q, a, b):
    """Where segment p-q crosses a-b, or None."""
    r = (q[0] - p[0], q[1] - p[1]); s = (b[0] - a[0], b[1] - a[1])
    den = r[0] * s[1] - r[1] * s[0]
    if abs(den) < 1e-9: return None
    t = ((a[0] - p[0]) * s[1] - (a[1] - p[1]) * s[0]) / den
    u = ((a[0] - p[0]) * r[1] - (a[1] - p[1]) * r[0]) / den
    if 0 <= t <= 1 and 0 <= u <= 1: return (p[0] + r[0] * t, p[1] + r[1] * t)
    return None


def geometry(e):
    if e.get("geometry"): return [xz(g["lat"], g["lon"]) for g in e["geometry"]]
    if e.get("members"):
        outer = [m for m in e["members"] if m.get("geometry") and m.get("role") != "inner"]
        if outer: return [xz(g["lat"], g["lon"]) for g in max(outer, key=lambda m: len(m["geometry"]))["geometry"]]
    return None


sx, sz = xz(*SQUARE)
rows = []; decks = []
for e in json.load(open(os.path.join(cache, "buildings_core.json"), encoding="utf-8")).get("elements", []):
    t = e.get("tags", {})
    if "building" not in t and t.get("amenity") != "parking": continue
    k = kind_of(t)
    if not k: continue
    poly = geometry(e)
    if not poly or len(poly) < 4: continue
    if poly[0] == poly[-1]: poly = poly[:-1]
    px, pz = inner_point(poly)
    if math.hypot(px - sx, pz - sz) > REACH_M: continue
    rows.append((round(px, 1), round(pz, 1), k, e["id"]))
    if k == DECK: decks.append((poly, e["id"]))

# a deck's entrances: where a service way (a driveway, an aisle) crosses its outline
doors = []
seen = set()
for fn in ("ways_all.json", "streets_core.json"):
    p = os.path.join(cache, fn)
    if not os.path.exists(p): continue
    for e in json.load(open(p, encoding="utf-8")).get("elements", []):
        t = e.get("tags", {})
        if e.get("type") != "way" or t.get("highway") != "service" or not e.get("geometry") or e["id"] in seen: continue
        seen.add(e["id"])
        line = [xz(g["lat"], g["lon"]) for g in e["geometry"]]
        if not any(math.hypot(q[0] - sx, q[1] - sz) < REACH_M for q in line): continue
        for poly, did in decks:
            for i in range(len(line) - 1):
                for j in range(len(poly)):
                    h = seg_hit(line[i], line[i + 1], poly[j], poly[j - 1])
                    if h and not any(d[3] == did and math.hypot(d[0] - h[0], d[1] - h[1]) < 12 for d in doors):
                        doors.append((round(h[0], 1), round(h[1], 1), e["id"], did))

rows.sort(); doors.sort()
L = []
L.append("// GENERATED by tools/city/groundfloors.py from the cached OpenStreetMap snapshot (ODbL; the game's credits name it).")
L.append("// Do not edit: run the tool. A row is a place and a kind - no names (Docs: NO BRANDS).")
L.append("namespace PSXRacing.City")
L.append("{")
L.append("    public static partial class CityMeshes")
L.append("    {")
L.append("        /// <summary>What OpenStreetMap tags a building inside the loop as: a point inside its outline (x east, z north, metres) and its kind.</summary>")
L.append("        static readonly float[] GfTagged =")
L.append("        {")
for x, z, k, i in rows: L.append(f"            {x:.1f}f, {z:.1f}f, {k},   // {KIND_NAME[k]}, way/relation {i}")
L.append("        };")
L.append("        /// <summary>Where a driveway crosses a parking deck's outline: its entrance (x, z).</summary>")
L.append("        static readonly float[] GfDeckDoors =")
L.append("        {")
for x, z, w, d in doors: L.append(f"            {x:.1f}f, {z:.1f}f,   // service way {w} into deck {d}")
L.append("        };")
L.append("    }")
L.append("}")
text = "\n".join(L) + "\n"
counts = {KIND_NAME[k]: sum(1 for r in rows if r[2] == k) for k in KIND_NAME}
print(f"ground floors: {len(rows)} tagged buildings {counts}, {len(doors)} deck entrances")
if "--check" in sys.argv:
    old = open(OUT, encoding="utf-8").read().replace("\r\n", "\n") if os.path.exists(OUT) else ""
    sys.exit(0 if old == text else 1)
open(OUT, "w", encoding="utf-8", newline="\n").write(text)
print("wrote", OUT)
