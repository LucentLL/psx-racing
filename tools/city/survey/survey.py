"""ROAD SURVEY stage 1: kerb positions for the game's map edges from the county's surveyed pavement polygons.

  py -I tools/city/survey/survey.py GPKG GRAPH.json OUTDIR [--edges 1,2,3 | --box x0 z0 x1 z1]

GPKG   the Mecklenburg County GIS Impervious_RightOfWay GeoPackage (opened read-only; untrusted data)
GRAPH  node tools/city/survey/graph_dump.mjs GRAPH.json (the shipped road graph)
OUTDIR scratch folder: county_xz.pkl (cache), stations.pkl (every station of every edge), measures_survey.txt
       (the socket's text format, provider survey: Scripts/City/JunctionMeasures.cs), junctions.pkl

THE RULES (HANDOFF.md "THE MEASURES: the spec for surveyed data", made concrete; each is numbered in the report):
 S1  Stations every 2 m along the map edge from its a end (and one at its b end); the tangent is the chord s-2 .. s+2.
 S2  The section is square to the map line, 40 m each way. It is cut through EVERY Edge-of-Pavement polygon near the
     edge; the pieces are merged as 1-D intervals along the section (the UNION of the polygons along that line).
     Pieces less than 0.3 m apart are one (a polygon seam or a sliver gap is not a kerb).
 S3  The carriageway is the interval that holds the station's point. If the map line lies off the pavement, the
     nearest interval within 3 m is taken and LEFT / RIGHT are signed (a measure may be negative). None within 3 m:
     no measure ("off").
 S4  LEFT = the interval's end left of a -> b, RIGHT = its end to the right. A side whose interval runs to the end of
     the 40 m section is OPEN (a junction's crossing, a lot): no measure there.
 S5  The next interval beyond a gap of 0.3 - 30 m on a side is the far carriageway: the gap is a MEDIAN (a hole in the
     union between two carriageways); its width is kept per station (median edges, noses).
 S6  A side's value that leaves its local level (the median over 62 m) by more than 1.0 m is an excursion. One of at
     most 12 m (a driveway, an apron, a seam notch) is dropped and BRIDGED from its neighbours. One longer than 12 m
     that reaches within 25 m of an edge end is a junction mouth: dropped, not bridged (the socket holds the last
     station). One of 12 - 40 m mid-edge that enters and leaves by a step of over 1.5 m between stations is a mouth of a
     street the map lacks (or a wide entrance): dropped and bridged. Anything longer is kept (a real widening: a bay).
 S7  Each side is smoothed with a running median of 3 stations (the spec's 5 m median at 2 m spacing), rounded to 1 cm;
     a station whose value a straight line between its kept neighbours gives within 2 cm is not written (the file
     stays small; the socket interpolates between stations).
 S9  A side whose pavement interval holds an OPPOSING map line (a two-way edge, or a one-way the other way, running
     within 45 degrees of parallel) is SHARED: the two carriageways lie in one pavement (a painted median, or a concrete
     one the layer does not separate). No measure on that side (the solver lays the two abreast, or a hand measure
     names the median); counted in the scorecard.
 S8  `hold a|b` on an end whose node lies on surveyed pavement; `blend 30` (the rule takes over beyond the last station
     only where the survey stops).
"""
import sys, os, math, pickle, json, time
import numpy as np
import shapely
from shapely import STRtree, LineString, Point
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import county

STEP, HALF, MERGE, NEAR, MEDMAX = 2.0, 40.0, 0.3, 3.0, 30.0

def polyline_frame(pts, s):
    """points and unit tangents at stations s along a polyline (chord s-2 .. s+2)."""
    P = np.asarray(pts, float); seg = np.hypot(*np.diff(P, axis=0).T); cum = np.concatenate([[0], np.cumsum(seg)])
    L = cum[-1]
    def at(q):
        q = np.clip(q, 0, L)
        x = np.interp(q, cum, P[:, 0]); z = np.interp(q, cum, P[:, 1]); return np.column_stack([x, z])
    C = at(s); T = at(s + 2.0) - at(s - 2.0); n = np.hypot(T[:, 0], T[:, 1]); n[n < 1e-9] = 1
    return C, T / n[:, None], L

def edge_len(pts):
    P = np.asarray(pts, float); return float(np.hypot(*np.diff(P, axis=0).T).sum())

class Survey:
    def __init__(self, gpkg, graph, outdir):
        t0 = time.time()
        self.g = json.load(open(graph)); self.E = self.g["edges"]; self.N = self.g["nodes"]
        d = county.load(gpkg, os.path.join(outdir, "county_xz.pkl"))
        self.eop = [g for g, a in zip(d["geoms"], d["attrs"]) if a[1] == "Edge-of-Pavement"]
        self.eop_attr = [a for a in d["attrs"] if a[1] == "Edge-of-Pavement"]
        self.tree = STRtree(self.eop)
        self.lines = [LineString(e["pts"]) for e in self.E]
        self.ltree = STRtree(self.lines)
        self.adj = {}
        for e in self.E:
            for n in (e["a"], e["b"]): self.adj.setdefault(n, []).append(e["i"])
        print("loaded %d pavement polygons, %d edges in %.1f s" % (len(self.eop), len(self.E), time.time() - t0))

    def pieces(self, x0, z0, x1, z1):
        """the pavement polygons over a box, clipped to it."""
        idx = self.tree.query(shapely.box(x0, z0, x1, z1))
        out = []
        for i in idx:
            c = shapely.clip_by_rect(self.eop[i], x0, z0, x1, z1)
            if not c.is_empty: out.append((i, c))
        return out

    def union(self, x0, z0, x1, z1):
        ps = [c for _, c in self.pieces(x0, z0, x1, z1)]
        return shapely.unary_union(ps) if ps else shapely.Polygon()

    def sections(self, ei):
        """S1-S5: raw intervals per station. Returns dict of arrays."""
        e = self.E[ei]; L = edge_len(e["pts"])
        s = np.arange(0.0, L, STEP)
        if len(s) == 0: s = np.array([0.0])
        if L - s[-1] > 0.5: s = np.append(s, L)
        C, T, _ = polyline_frame(e["pts"], s); Nn = np.column_stack([-T[:, 1], T[:, 0]])   # left normal
        k = len(s)
        a = C - Nn * HALF; b = C + Nn * HALF
        segs = shapely.linestrings(np.stack([a, b], axis=1))
        P = np.asarray(e["pts"]); x0, z0 = P.min(0) - HALF - 2; x1, z1 = P.max(0) + HALF + 2
        ivs = [[] for _ in range(k)]
        for pi, poly in self.pieces(x0, z0, x1, z1):
            hit = shapely.intersects(segs, poly)
            if not hit.any(): continue
            hi = np.nonzero(hit)[0]
            inter = shapely.intersection(segs[hi], poly)
            parts, owner = shapely.get_parts(inter, return_index=True)
            if len(parts) == 0: continue
            lines = shapely.get_type_id(parts) == 1
            parts, owner = parts[lines], owner[lines]
            if len(parts) == 0: continue
            p0 = shapely.get_coordinates(shapely.get_point(parts, 0)); p1 = shapely.get_coordinates(shapely.get_point(parts, -1))
            st = hi[owner]
            t0 = ((p0 - C[st]) * Nn[st]).sum(1); t1 = ((p1 - C[st]) * Nn[st]).sum(1)
            for j in range(len(st)): ivs[st[j]].append((min(t0[j], t1[j]), max(t0[j], t1[j]), pi))
        # S9: crossings of OPPOSING map lines (a two-way edge, or a one-way running against this one) on each section
        opp = [[] for _ in range(k)]
        for li in self.ltree.query(shapely.box(x0, z0, x1, z1)):
            if li == ei: continue
            o = self.E[li]
            hit = shapely.intersects(segs, self.lines[li])
            if not hit.any(): continue
            for j in np.nonzero(hit)[0]:
                q = shapely.get_coordinates(shapely.intersection(segs[j], self.lines[li]))
                if len(q) == 0: continue
                Co, To, _ = polyline_frame(o["pts"], np.array([float(self.lines[li].project(Point(q[0])))]))
                if e["ow"] and o["ow"] and (To[0] @ T[j]) > -0.5: continue      # same way (a bay, a slip): not opposing
                if abs(To[0] @ T[j]) < 0.7: continue                            # a crossing street, not a parallel one
                for qq in q: opp[j].append(float((qq - C[j]) @ Nn[j]))
        left = np.full(k, np.nan); right = np.full(k, np.nan); onpave = np.zeros(k, bool)
        shL = np.zeros(k, bool); shR = np.zeros(k, bool)
        openL = np.zeros(k, bool); openR = np.zeros(k, bool); off = np.zeros(k, bool)
        medL = np.full(k, np.nan); medR = np.full(k, np.nan); farL = np.full(k, np.nan); farR = np.full(k, np.nan)
        poly = np.full(k, -1)
        for j in range(k):
            iv = sorted(ivs[j])
            if not iv: off[j] = True; continue
            m = [list(iv[0])]
            for lo, hi_, pi in iv[1:]:
                if lo <= m[-1][1] + MERGE: m[-1][1] = max(m[-1][1], hi_)
                else: m.append([lo, hi_, pi])
            ci = None
            for q, (lo, hi_, pi) in enumerate(m):
                if lo <= 0 <= hi_: ci = q; onpave[j] = True; break
            if ci is None:
                dist = [min(abs(lo), abs(hi_)) for lo, hi_, _ in m]; q = int(np.argmin(dist))
                if dist[q] <= NEAR: ci = q
                else: off[j] = True; continue
            lo, hi_, pi = m[ci]; poly[j] = pi
            left[j] = hi_; right[j] = -lo
            for tq in opp[j]:
                if 0.5 < tq < hi_: shL[j] = True
                if lo < tq < -0.5: shR[j] = True
            openL[j] = hi_ >= HALF - 0.5; openR[j] = lo <= -HALF + 0.5
            if ci + 1 < len(m) and m[ci + 1][0] - hi_ <= MEDMAX: medL[j] = m[ci + 1][0] - hi_; farL[j] = m[ci + 1][1]
            if ci > 0 and lo - m[ci - 1][1] <= MEDMAX: medR[j] = lo - m[ci - 1][1]; farR[j] = -m[ci - 1][0]
        return dict(s=s, C=C, T=T, left=left, right=right, onpave=onpave, openL=openL, openR=openR, off=off,
                    medL=medL, medR=medR, farL=farL, farR=farR, poly=poly, L=L, shL=shL, shR=shR)

def clean_side(v, opn, s, L):
    """S6 + S7 on one side. Returns (smoothed values, flags) flags: 0 clean, 1 bridged apron, 2 mouth at end (dropped),
    3 mid-edge mouth (bridged), 4 open/off, 5 kept widening."""
    k = len(v); fl = np.zeros(k, np.int8)
    x = v.copy(); x[opn] = np.nan; fl[np.isnan(x)] = 4
    if np.isfinite(x).sum() < 2: return x, fl
    pad = 15; xp = np.concatenate([np.full(pad, np.nan), x, np.full(pad, np.nan)])
    w = np.lib.stride_tricks.sliding_window_view(xp, 2 * pad + 1)
    with np.errstate(all="ignore"):
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore"); base = np.nanmedian(w, axis=1)
    dev = x - base; exc = np.abs(dev) > 1.0
    j = 0
    while j < k:
        if not exc[j]: j += 1; continue
        a = j
        while j < k and exc[j]: j += 1
        b = j - 1; run = (b - a + 1) * STEP
        nearend = s[a] < 25 or s[b] > L - 25
        if run <= 12: x[a:b + 1] = np.nan; fl[a:b + 1] = 1
        elif nearend: x[a:b + 1] = np.nan; fl[a:b + 1] = 2
        else:
            ein = abs(x[a] - x[a - 1]) if a > 0 and np.isfinite(x[a - 1]) else 0
            eout = abs(x[b] - x[b + 1]) if b + 1 < k and np.isfinite(x[b + 1]) else 0
            if run <= 40 and ein > 1.5 and eout > 1.5: x[a:b + 1] = np.nan; fl[a:b + 1] = 3
            else: fl[a:b + 1] = 5
    # bridge flags 1 and 3 (both neighbours present)
    good = np.isfinite(x)
    for code in (1, 3):
        idx = np.nonzero(fl == code)[0]
        if len(idx) and good.sum() >= 2:
            gi = np.nonzero(good)[0]
            ok = (idx > gi.min()) & (idx < gi.max())
            x[idx[ok]] = np.interp(s[idx[ok]], s[gi], x[gi])
    # S7 running median of 3
    y = x.copy()
    for i in range(1, k - 1):
        tri = x[i - 1:i + 2]
        if np.isfinite(tri).all(): y[i] = np.median(tri)
    return np.round(y, 2), fl

def decimate(s, y, tol=0.02):
    """S7: keep a station unless a line between its kept neighbours gives it within tol."""
    idx = [i for i in range(len(y)) if np.isfinite(y[i])]
    if len(idx) <= 2: return idx
    # split into runs of consecutive finite stations; endpoints of runs are kept
    runs, cur = [], [idx[0]]
    for i in idx[1:]:
        if i == cur[-1] + 1: cur.append(i)
        else: runs.append(cur); cur = [i]
    runs.append(cur)
    keep = []
    for r in runs:
        if len(r) <= 2: keep += r; continue
        stack = [(0, len(r) - 1)]; kept = {0, len(r) - 1}
        while stack:
            a, b = stack.pop()
            if b - a < 2: continue
            sa, sb, ya, yb = s[r[a]], s[r[b]], y[r[a]], y[r[b]]
            seg = r[a + 1:b]
            pred = ya + (yb - ya) * (s[seg] - sa) / max(1e-9, sb - sa)
            err = np.abs(y[seg] - pred); m = int(np.argmax(err))
            if err[m] > tol: kept.add(a + 1 + m); stack += [(a, a + 1 + m), (a + 1 + m, b)]
        keep += [r[i] for i in sorted(kept)]
    return keep

def run(sv, edges):
    res = {}
    t0 = time.time()
    for n, ei in enumerate(edges):
        if edge_len(sv.E[ei]["pts"]) < 1.0: continue          # a sliver edge: no stations
        r = sv.sections(ei)
        r["Lc"], r["fL"] = clean_side(r["left"], r["openL"] | r["off"] | r["shL"], r["s"], r["L"])
        r["Rc"], r["fR"] = clean_side(r["right"], r["openR"] | r["off"] | r["shR"], r["s"], r["L"])
        res[ei] = r
        if n % 2000 == 0: print("  %d / %d edges, %.0f s" % (n, len(edges), time.time() - t0), flush=True)
    return res

def node_on_pavement(sv, n):
    x, z = sv.N[n][0], sv.N[n][1]; p = Point(x, z)
    for i in sv.tree.query(p):
        if sv.eop[i].contains(p): return True
    return False

def write_measures(sv, res, path, edges=None, title=""):
    out = ["# MEASURES for the junction outline solver. provider survey: kerb offsets computed from Mecklenburg County GIS",
           "# Edge-of-Pavement polygons (Impervious_RightOfWay) by tools/city/survey/survey.py. Numbers only: no county",
           "# geometry is in the game or the repository. " + title, "provider survey"]
    cache = {}
    for ei in [x for x in (edges if edges is not None else sorted(res)) if x in res]:
        r = res[ei]; e = sv.E[ei]
        kl = decimate(r["s"], r["Lc"]); kr = decimate(r["s"], r["Rc"])
        if len(kl) + len(kr) < 2: continue
        hold = ""
        for end, n in (("a", e["a"]), ("b", e["b"])):
            if n not in cache: cache[n] = node_on_pavement(sv, n)
            if cache[n] and len(sv.adj[n]) >= 3: hold += end
        out.append("edge %d blend 30%s   # length %.1f m, %s, %s" % (ei, (" hold " + hold) if hold else "", r["L"], e["cls"], e["name"] or "-"))
        ks = sorted(set(kl) | set(kr)); sl, sr = set(kl), set(kr)
        for i in ks:
            out.append("s %.2f %s %s" % (r["s"][i], "%.2f" % r["Lc"][i] if i in sl else "-", "%.2f" % r["Rc"][i] if i in sr else "-"))
    with open(path, "w", newline="\n") as f: f.write("\n".join(out) + "\n")
    return len(out)

if __name__ == "__main__":
    gpkg, graph, outdir = sys.argv[1:4]
    os.makedirs(outdir, exist_ok=True)
    sv = Survey(gpkg, graph, outdir)
    edges = list(range(len(sv.E)))
    if "--edges" in sys.argv: edges = [int(v) for v in sys.argv[sys.argv.index("--edges") + 1].split(",")]
    if "--box" in sys.argv:
        i = sys.argv.index("--box"); x0, z0, x1, z1 = map(float, sys.argv[i + 1:i + 5])
        edges = [e["i"] for e in sv.E if any(x0 <= p[0] <= x1 and z0 <= p[1] <= z1 for p in e["pts"])]
    res = run(sv, edges)
    tag = sys.argv[sys.argv.index("--tag") + 1] if "--tag" in sys.argv else "all"
    with open(os.path.join(outdir, "stations_%s.pkl" % tag), "wb") as f: pickle.dump(res, f)
    n = write_measures(sv, res, os.path.join(outdir, "measures_survey_%s.txt" % tag), edges)
    print("wrote %d lines, %d edges" % (n, len(res)))
