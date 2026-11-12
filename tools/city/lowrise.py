# THE LOW-RISE CLASSES' DATA (charlotte-buildings-1111 round 2 "types", 2026-11-11): what OpenStreetMap says a
# non-house building IS - a works shed, a shop, a church, a school, an apartment block, an old mill - and what of
# its height was mapped (a height tag, a levels tag), for CityMeshes.LowRise.cs's rules. charlotte_bld.bytes keeps
# only use/mat/style; this file carries the rest without re-exporting the city.
#
#   py -3 tools/city/lowrise.py [--cache <tools/city/cache>] [--check] [--list <csv>]
#
# Reads the cached Overpass snapshot (buildings_core.json + buildings_city.json, layers/landuse.json; set --cache or
# PSX_CITY_CACHE to the main checkout's cache from a worktree) and writes
# Assets/PSXRacing/Scripts/City/CityLowRise.Data.cs: one row a classified building - a point INSIDE its outline
# (game decimetres), its kind, its height flags and levels - packed as base64. No names (Docs: NO BRANDS).
# --list: also a CSV of every row with its OSM id, tags and area (the hand-back's lists; kept out of the repo).
import base64, json, math, os, re, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
cache = os.environ.get("PSX_CITY_CACHE") or os.path.join(HERE, "cache")
if "--cache" in sys.argv: cache = sys.argv[sys.argv.index("--cache") + 1]
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Scripts", "City", "CityLowRise.Data.cs")
LAT0, LON0 = 35.18456015184093, -80.81770185962013
MX = 111320.0 * math.cos(math.radians(35.18456)); MZ = 111132.0

# the kinds (CityMeshes.LowRise.cs holds the same numbers)
WORK, SHOP, WORSHIP, SCHOOL, HOMES, MILL, PLAIN = 1, 2, 3, 4, 5, 6, 7
NAMES = {WORK: "Work", SHOP: "Shop", WORSHIP: "Worship", SCHOOL: "School", HOMES: "Homes", MILL: "Mill", PLAIN: "Plain"}
F_HEIGHT, F_LEVELS, F_BRICK, F_METAL, F_OFFICEZONE, F_HEIGHT_INT = 1, 2, 4, 8, 16, 32

WORK_B = {"industrial", "warehouse", "manufacture", "service", "hangar", "data_center", "storage_tank"}
SHOP_B = {"retail", "commercial", "supermarket", "kiosk"}
WORSHIP_B = {"church", "chapel", "cathedral", "religious", "synagogue", "mosque", "temple", "shrine"}
SCHOOL_B = {"school", "kindergarten"}
HOMES_B = {"apartments", "dormitory"}
MILL_RE = re.compile(r"\b(mill|mills|knitting|hosiery|cotton|textile)\b", re.I)
NOT_MILL_RE = re.compile(r"fort mill|concord mills|mill grove|mills mart|gin mill|pizza|wedding|mill road|steel mill", re.I)
SKIP_B = {"house", "detached", "semidetached_house", "terrace", "residential", "bungalow", "cabin", "garage", "garages",
          "hut", "shed", "roof", "carport", "construction", "grandstand", "parking", "bridge", "hotel", "hospital",
          "office", "university", "college", "stadium", "civic", "public", "government", "train_station",
          "transportation", "fire_station", "sports_centre", "sports_hall", "museum", "library"}


def xz(lat, lon): return ((lon - LON0) * MX, (lat - LAT0) * MZ)


def area(poly):
    s = 0.0
    for i in range(len(poly)):
        a, b = poly[i - 1], poly[i]
        s += a[0] * b[1] - b[0] * a[1]
    return abs(s) * 0.5


def inside(poly, p):
    x, z = p; c = False
    for i in range(len(poly)):
        a, b = poly[i], poly[i - 1]
        if (a[1] > z) != (b[1] > z) and x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]: c = not c
    return c


def inner_point(poly):
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


def outers(e):
    if e.get("geometry"): yield [xz(g["lat"], g["lon"]) for g in e["geometry"]]
    for m in e.get("members", []) or []:
        if m.get("geometry") and m.get("role") == "outer": yield [xz(g["lat"], g["lon"]) for g in m["geometry"]]


# landuse polygons: a plain building in an industrial / retail / commercial zone takes the zone's class
landuse = []   # (minx, minz, maxx, maxz, poly, kind)
lp = os.path.join(cache, "layers", "landuse.json")
if os.path.exists(lp):
    for e in json.load(open(lp, encoding="utf-8")).get("elements", []):
        lu = e.get("tags", {}).get("landuse")
        # (an office park is landuse=commercial: its plain buildings are offices, never shops - PLAIN, flagged)
        k = WORK if lu == "industrial" else SHOP if lu == "retail" else PLAIN if lu == "commercial" else 0
        if not k: continue
        for poly in outers(e):
            if len(poly) < 4: continue
            xs = [p[0] for p in poly]; zs = [p[1] for p in poly]
            landuse.append((min(xs), min(zs), max(xs), max(zs), poly, k, lu))
LU_CELL = 500.0
lu_grid = {}
for li, (x0, z0, x1, z1, _, _, _) in enumerate(landuse):
    for gx in range(int(math.floor(x0 / LU_CELL)), int(math.floor(x1 / LU_CELL)) + 1):
        for gz in range(int(math.floor(z0 / LU_CELL)), int(math.floor(z1 / LU_CELL)) + 1):
            lu_grid.setdefault((gx, gz), []).append(li)


def zone_of(p):
    for li in lu_grid.get((int(math.floor(p[0] / LU_CELL)), int(math.floor(p[1] / LU_CELL))), []):
        x0, z0, x1, z1, poly, k, lu = landuse[li]
        if x0 <= p[0] <= x1 and z0 <= p[1] <= z1 and inside(poly, p): return k, lu
    return 0, ""


def kind_of(t, a, p):
    b = t.get("building", "")
    am = t.get("amenity", "")
    name = (t.get("name", "") + " " + t.get("old_name", "")).strip()
    if b in WORSHIP_B or am == "place_of_worship": return WORSHIP, ""
    if b in SCHOOL_B or am in ("school", "kindergarten"): return SCHOOL, ""
    if MILL_RE.search(name) and not NOT_MILL_RE.search(name) and b in ("industrial", "warehouse", "apartments", "commercial", "yes", "retail", "office", "residential") \
            and not t.get("shop") and am in ("", "parking"):
        return MILL, ""
    # an old works building kept as such (historic=*) is a mill, never a shed
    if t.get("historic") and b in ("industrial", "warehouse", "manufacture", "commercial", "yes") and not t.get("shop") and not am:
        return MILL, ""
    if b in SKIP_B: return 0, ""
    if b in HOMES_B: return HOMES, ""
    if b in WORK_B: return WORK, ""
    if b in SHOP_B or t.get("shop"): return SHOP, ""
    if b == "yes" and not am and not t.get("office") and not t.get("tourism") and not t.get("leisure"):
        zk, lu = zone_of(p)
        if zk and (zk != PLAIN or a >= 2000): return zk, lu
        if a >= 2000: return PLAIN, ""
    return 0, ""


seen = set(); rows = []; listing = []
for fn in ("buildings_core.json", "buildings_city.json"):
    fp = os.path.join(cache, fn)
    if not os.path.exists(fp): continue
    for e in json.load(open(fp, encoding="utf-8")).get("elements", []):
        key = (e["type"], e["id"])
        if key in seen: continue
        seen.add(key)
        t = e.get("tags", {})
        if "building" not in t: continue
        for poly in outers(e):
            if len(poly) < 4: continue
            if poly[0] == poly[-1]: poly = poly[:-1]
            a = area(poly)
            if a < 150: continue
            p = inner_point(poly)
            k, lu = kind_of(t, a, p)
            if not k: continue
            fl = 0
            hv = t.get("height", "")
            if hv and re.match(r"\s*[\d.]+", hv): fl |= F_HEIGHT
            if hv and re.fullmatch(r"\s*\d+\s*", hv): fl |= F_HEIGHT_INT   # an integer: the Charlotte import's feet (CityMeshes.LowRise.cs)
            lv = 0
            try: lv = int(float(t.get("building:levels", "")))
            except ValueError: lv = 0
            if lv > 0: fl |= F_LEVELS
            mat = (t.get("building:material") or "").lower()
            if mat == "brick": fl |= F_BRICK
            if mat in ("metal", "steel", "metal_plates", "aluminium", "aluminum"): fl |= F_METAL
            if lu == "commercial": fl |= F_OFFICEZONE
            rows.append((int(round(p[0] * 10)), int(round(p[1] * 10)), k, fl, min(lv, 255)))
            listing.append((e["type"][0] + str(e["id"]), NAMES[k], round(a), t.get("building", ""), t.get("height", ""), t.get("building:levels", ""),
                            lu, (t.get("name") or "").replace(",", " ")[:40], round(p[0], 1), round(p[1], 1)))

rows.sort()
blob = b"".join(struct.pack("<iiBBB", *r) for r in rows)
b64 = base64.b64encode(blob).decode("ascii")
L = []
L.append("// GENERATED by tools/city/lowrise.py from the cached OpenStreetMap snapshot (ODbL; the game's credits name it).")
L.append("// Do not edit: run the tool. A row is a place and a kind - no names (Docs: NO BRANDS).")
L.append("namespace PSXRacing.City")
L.append("{")
L.append("    public static partial class CityMeshes")
L.append("    {")
L.append("        /// <summary>The low-rise classes: rows of 11 bytes (int32 x, int32 z in decimetres - a point inside the outline;")
L.append("        /// kind; flags 1 height tagged, 2 levels tagged, 4 brick, 8 metal, 16 in an office zone (landuse=commercial), 32 the height an integer; levels), base64.</summary>")
L.append(f"        const int LrRowCount = {len(rows)};")
L.append("        static readonly string[] LrRows =")
L.append("        {")
for i in range(0, len(b64), 120): L.append(f'            "{b64[i:i + 120]}",')
L.append("        };")
L.append("    }")
L.append("}")
text = "\n".join(L) + "\n"
counts = {NAMES[k]: sum(1 for r in rows if r[2] == k) for k in NAMES}
print(f"low-rise classes: {len(rows)} rows {counts}, {len(blob)} bytes")
if "--list" in sys.argv:
    with open(sys.argv[sys.argv.index("--list") + 1], "w", encoding="utf-8") as fo:
        fo.write("id,kind,area,building,height,levels,landuse,name,x,z\n")
        for r in listing: fo.write(",".join(str(v) for v in r) + "\n")
if "--check" in sys.argv:
    old = open(OUT, encoding="utf-8").read().replace("\r\n", "\n") if os.path.exists(OUT) else ""
    sys.exit(0 if old == text else 1)
open(OUT, "w", encoding="utf-8", newline="\n").write(text)
print("wrote", OUT)
