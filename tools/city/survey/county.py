"""ROAD SURVEY: the county's pavement polygons in the game's plan frame (x east, z north, metres).

Reads the Mecklenburg County GIS "Impervious_RightOfWay" GeoPackage READ-ONLY (sqlite3 mode=ro). The file is untrusted
downloaded data: run Python with `py -I`, never write next to it, never copy it into the repository. Only NUMBERS derived
from it may ship (credit: Mecklenburg County GIS).

Projection: EPSG:2264 (NC State Plane, NAD83, US survey feet; Lambert conformal conic, 2SP) inverted to lat / lon (NAD83
taken as WGS84), then the game's own equirectangular mapping (tools/city/export_osm.mjs: LAT0, LON0, M_LAT, M_LON).
"""
import math, sqlite3, pickle, os
import numpy as np
import shapely
from shapely import wkb

A = 6378137.0; F_ = 1 / 298.257222101; E2 = 2 * F_ - F_ * F_; E = math.sqrt(E2)
FT = 1200.0 / 3937.0
P1, P2, P0, L0 = map(math.radians, (36 + 10 / 60.0, 34 + 20 / 60.0, 33.75, -79.0))
FE = 2000000.002616 * FT
def _m(p): return math.cos(p) / math.sqrt(1 - E2 * math.sin(p) ** 2)
def _t(p): return math.tan(math.pi / 4 - p / 2) / ((1 - E * math.sin(p)) / (1 + E * math.sin(p))) ** (E / 2)
N = (math.log(_m(P1)) - math.log(_m(P2))) / (math.log(_t(P1)) - math.log(_t(P2)))
FF = _m(P1) / (N * _t(P1) ** N); R0 = A * FF * _t(P0) ** N

# the game's mapping (tools/city/export_osm.mjs, tools/city/lib/canopygrid.mjs)
LAT0, LON0, M_LAT = 35.18456015184093, -80.81770185962013, 111132.0
M_LON = 111320.0 * math.cos(LAT0 * math.pi / 180)

def sp_to_ll(xf, yf):
    """EPSG:2264 feet -> (lat, lon) degrees, numpy-vectorised."""
    x = np.asarray(xf) * FT - FE; y = R0 - np.asarray(yf) * FT
    r = np.sign(N) * np.hypot(x, y); t = (r / (A * FF)) ** (1 / N); lon = np.arctan2(x, y) / N + L0
    p = np.pi / 2 - 2 * np.arctan(t)
    for _ in range(8): p = np.pi / 2 - 2 * np.arctan(t * ((1 - E * np.sin(p)) / (1 + E * np.sin(p))) ** (E / 2))
    return np.degrees(p), np.degrees(lon)

def ll_to_sp(lat, lon):
    r = A * FF * _t(math.radians(lat)) ** N; th = N * (math.radians(lon) - L0)
    return (FE + r * math.sin(th)) / FT, (R0 - r * math.cos(th)) / FT

def ll_to_xz(lat, lon): return (np.asarray(lon) - LON0) * M_LON, (np.asarray(lat) - LAT0) * M_LAT
def xz_to_ll(x, z): return LAT0 + np.asarray(z) / M_LAT, LON0 + np.asarray(x) / M_LON

def _to_xz(c):
    la, lo = sp_to_ll(c[:, 0], c[:, 1]); x, z = ll_to_xz(la, lo)
    return np.column_stack([x, z])

def load(gpkg, cache=None):
    """All Edge-of-Pavement and Sidewalk polygons (each multipolygon exploded) in the game frame, with their
    attributes. Cached as a pickle in the caller's scratch folder (derived data, never the raw file)."""
    if cache and os.path.exists(cache):
        with open(cache, "rb") as f: return pickle.load(f)
    db = sqlite3.connect("file:" + gpkg.replace("\\", "/") + "?mode=ro", uri=True)
    geoms, attrs = [], []
    for oid, sub, lay, name, at, blob in db.execute(
            "select OBJECTID, subtheme, layer, ldplanname, assettype, shape from Impervious_RightOfWay"):
        if blob is None: continue
        env = (blob[3] >> 1) & 7
        g = wkb.loads(bytes(blob[8 + [0, 32, 48, 48, 64][env]:]))
        g = shapely.transform(g, _to_xz)
        for p in getattr(g, "geoms", [g]):
            if p.is_empty: continue
            if not p.is_valid: p = shapely.make_valid(p)
            for q in getattr(p, "geoms", [p]):
                if q.geom_type == "Polygon" and q.area > 0.5:
                    geoms.append(q); attrs.append((oid, sub, lay, name or "", at or ""))
    db.close()
    out = {"geoms": geoms, "attrs": attrs}
    if cache:
        with open(cache, "wb") as f: pickle.dump(out, f)
    return out
