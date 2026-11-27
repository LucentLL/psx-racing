"""ROAD SURVEY: per-junction features from the county's pavement polygons, against the RULES.

  py -I tools/city/survey/junctions.py GPKG GRAPH.json STATIONS.pkl OUT.pkl [--nodes n,n,...]

THE RULES (numbered on from survey.py):
 J1  A junction is a map node where three or more edge ends meet. Junction nodes joined by an edge under 35 m, or
     within 30 m of each other, are one junction (a divided road's two crossings). Its ARMS are the edges with one end in it.
 J2  An arm's borders (outward left / right, looking away from the junction) are the median of its cleaned survey
     stations 8 - 40 m out; the rule's are the class cross-section (width / 2). Each border is a straight line through
     the arm's point 20 m out along its tangent there.
 J3  Arms are ordered anticlockwise. Two neighbouring arms that are one-ways of opposite flow, within 40 degrees, are a
     DIVIDED road's two carriageways: the corner between them is a MEDIAN END (J5). Neighbours more than 150 degrees
     apart are a straight run (no corner). Every other neighbour pair is a KERB RETURN (J4).
 J4  Return: C = where A's outward-left border meets B's outward-right border. The union boundary within 30 m of C is
     taken; its points more than 0.4 m off both straight borders, in the run nearest C, are fitted with a circle (least
     squares). Status: fit (rms <= 0.3 m, arc >= 25 degrees), compound (rms > 0.3: two radii; not in the socket),
     open (no kerb within 15 m of C: a lot, a driveway apron, a street the map lacks), noborder (no survey border).
     The rule's radius: link 15, primary+ 12, tertiary / secondary 9, local 6 (JunctionSurfaces.cs); the corner takes
     the smaller of its two arms'.
 J5  Median end: points at equal distance d out along both carriageways' map lines; their midpoint on pavement or not.
     The SURVEY's median end = the first d (0.5 m steps, to 80 m) from which the midpoint lies off the pavement for
     4 m on. None: no hole (painted or concrete median, or no median: the layer does not separate a concrete one).
     The RULE's median end = the first d where the two class ribbons (width / 2 about each map line) are 0.5 m apart.
     The nose's cap points are the two survey borders at the survey's median end; its radius = half the hole's width
     3 m beyond, at most 4 m; kind grass when that width is 1.8 m or more, else concrete (the spec's width rule).
"""
import sys, os, math, pickle, json
import numpy as np
import shapely
from shapely import Point, LineString
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import county, survey as SV

def rule_radius(e): return 15.0 if e["link"] else 12.0 if e["rank"] >= 3 else 9.0 if e["rank"] >= 1 else 6.0

def kasa(P):
    x, y = P[:, 0], P[:, 1]; A = np.column_stack([x, y, np.ones(len(x))]); b = x * x + y * y
    c, *_ = np.linalg.lstsq(A, b, rcond=None); cx, cy = c[0] / 2, c[1] / 2; r = math.sqrt(max(1e-9, c[2] + cx * cx + cy * cy))
    res = np.hypot(x - cx, y - cy) - r; return cx, cy, r, float(np.sqrt((res ** 2).mean()))

class Junctions:
    def __init__(self, gpkg, graph, stations):
        self.sv = SV.Survey(gpkg, graph, os.path.dirname(os.path.abspath(graph)))
        self.R = pickle.load(open(stations, "rb")) if stations else {}
        E = self.sv.E; self.E = E
        self.L = {e["i"]: SV.edge_len(e["pts"]) for e in E}
        deg = {n: len(v) for n, v in self.sv.adj.items()}
        J = [n for n, k in deg.items() if k >= 3]; Js = set(J)
        par = {n: n for n in J}
        def f(n):
            while par[n] != n: par[n] = par[par[n]]; n = par[n]
            return n
        for e in E:
            if e["a"] in Js and e["b"] in Js and self.L[e["i"]] < 35 and e["a"] != e["b"]: par[f(e["a"])] = f(e["b"])
        # J1: junction nodes within 30 m of each other are one junction even with no map edge between them (a divided
        # road crossed by a street the map lacks, as at spot 80)
        cell = {}
        for n in J:
            x, z = self.sv.N[n][0], self.sv.N[n][1]; cell.setdefault((int(x // 30), int(z // 30)), []).append(n)
        for n in J:
            x, z = self.sv.N[n][0], self.sv.N[n][1]; gx, gz = int(x // 30), int(z // 30)
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for m in cell.get((gx + dx, gz + dz), []):
                        if m != n and math.hypot(self.sv.N[m][0] - x, self.sv.N[m][1] - z) < 30: par[f(m)] = f(n)
        cl = {}
        for n in J: cl.setdefault(f(n), []).append(n)
        self.clusters = list(cl.values())

    def arm(self, ei, node):
        e = self.E[ei]; L = self.L[ei]; fwd = e["a"] == node
        def at(d):
            s = d if fwd else L - d; C, T, _ = SV.polyline_frame(e["pts"], np.array([float(np.clip(s, 0, L))]))
            t = T[0] if fwd else -T[0]; return C[0], t
        P0 = np.array(self.sv.N[node][:2]); p20, t20 = at(min(20.0, L * 0.6))
        lo = hi = np.nan; wl = wr = np.nan
        r = self.R.get(ei)
        if r is not None:
            dist = r["s"] if fwd else L - r["s"]; m = (dist >= 8) & (dist <= 40)
            a, b = (r["Lc"], r["Rc"]) if fwd else (r["Rc"], r["Lc"])
            if np.isfinite(a[m]).any(): lo = float(np.nanmedian(a[m]))
            if np.isfinite(b[m]).any(): hi = float(np.nanmedian(b[m]))
        d = p20 - P0; ang = math.atan2(d[1], d[0])
        return dict(e=ei, fwd=fwd, at=at, p20=p20, t20=t20, ang=ang, outL=lo, outR=hi, hw=e["w"] / 2, L=L,
                    ow=e["ow"], outbound=fwd, name=e["name"], rank=e["rank"], link=e["link"], cls=e["cls"])

    def onpave(self, pts, prep):
        return shapely.contains_xy(prep, pts[:, 0], pts[:, 1])

    def analyse(self, nodes):
        Ns = set(nodes); E = self.E
        arms = []; inner = []
        for n in nodes:
            for ei in self.sv.adj[n]:
                e = E[ei]
                if e["a"] in Ns and e["b"] in Ns: inner.append(ei); continue
                arms.append(self.arm(ei, n))
        cx = np.mean([self.sv.N[n][0] for n in nodes]); cz = np.mean([self.sv.N[n][1] for n in nodes])
        U = self.sv.union(cx - 130, cz - 130, cx + 130, cz + 130); shapely.prepare(U)
        # J3 order about the junction's centre
        for a in arms:
            d = a["p20"] - np.array([cx, cz]); a["ang"] = math.atan2(d[1], d[0])
        arms.sort(key=lambda a: a["ang"])
        out = dict(nodes=list(nodes), x=float(cx), z=float(cz), arms=arms, inner=inner, returns=[], noses=[],
                   onpave=bool(shapely.contains_xy(U, cx, cz)))
        n = len(arms)
        for i in range(n if n > 1 else 0):
            A, B = arms[i], arms[(i + 1) % n]
            gap = (B["ang"] - A["ang"]) % (2 * math.pi)
            if n == 2 and i == 1: break
            if A["ow"] and B["ow"] and A["outbound"] != B["outbound"] and gap < math.radians(40):
                out["noses"].append(self.median_end(A, B, U))
            elif gap > math.radians(150): continue
            else: out["returns"].append(self.ret(A, B, U))
        return out

    def ret(self, A, B, U):
        res = dict(a=A["e"], b=B["e"], rule=min(rule_radius(self.E[A["e"]]), rule_radius(self.E[B["e"]])), r=np.nan, rms=np.nan, status="noborder")
        if not (np.isfinite(A["outL"]) and np.isfinite(B["outR"])): return res
        nA = np.array([-A["t20"][1], A["t20"][0]]); nB = np.array([B["t20"][1], -B["t20"][0]])
        pA = A["p20"] + nA * A["outL"]; pB = B["p20"] + nB * B["outR"]
        M = np.column_stack([A["t20"], -B["t20"]])
        if abs(np.linalg.det(M)) < 0.1: res["status"] = "parallel"; return res
        t = np.linalg.solve(M, pB - pA); C = pA + A["t20"] * t[0]
        res["C"] = C.tolist()
        bd = shapely.intersection(U.boundary, Point(C).buffer(30))
        parts = [p for p in shapely.get_parts(bd) if p.geom_type == "LineString"]
        best = None
        for p in parts:
            q = shapely.get_coordinates(shapely.segmentize(p, 0.5))
            dA = np.abs((q - pA) @ nA); dB = np.abs((q - pB) @ nB)
            ok = (dA > 0.4) & (dB > 0.4)
            dc = np.hypot(*(q - C).T)
            j = 0
            while j < len(q):
                if not ok[j]: j += 1; continue
                k = j
                while k < len(q) and ok[k]: k += 1
                run = q[j:k]; m = dc[j:k].min()
                if best is None or m < best[0]: best = (m, run)
                j = k
        if best is None or best[0] > 15: res["status"] = "open"; return res
        run = best[1]
        if len(run) < 5: res["status"] = "open"; return res
        x, y, r, rms = kasa(run)
        ang = np.unwrap(np.arctan2(run[:, 1] - y, run[:, 0] - x)); span = math.degrees(abs(ang[-1] - ang[0]))
        res.update(r=r, rms=rms, span=span, centre=[x, y])
        res["status"] = "fit" if rms <= 0.3 and span >= 25 and r < 80 else "compound" if rms > 0.3 else "shallow"
        return res

    def median_end(self, A, B, U):
        res = dict(a=A["e"], b=B["e"], dS=np.nan, dR=np.nan, status="nohole")
        ds = np.arange(0, 80.5, 0.5)
        lim = min(A["L"], B["L"], 80.0); ds = ds[ds <= lim]
        PA = np.array([A["at"](d)[0] for d in ds]); PB = np.array([B["at"](d)[0] for d in ds])
        mid = (PA + PB) / 2; on = shapely.contains_xy(U, mid[:, 0], mid[:, 1])
        sep = np.hypot(*(PA - PB).T) - A["hw"] - B["hw"]
        jr = np.nonzero(sep > 0.5)[0]
        if len(jr): res["dR"] = float(ds[jr[0]])
        for j in range(len(ds)):
            k = j + 8
            if k <= len(ds) and not on[j:k].any():
                res["dS"] = float(ds[j]); res["status"] = "hole"
                d0 = ds[j]; pa, ta = A["at"](d0); pb, tb = B["at"](d0)
                if np.isfinite(A["outL"]) and np.isfinite(B["outR"]):
                    res["capA"] = (pa + np.array([-ta[1], ta[0]]) * A["outL"]).tolist()
                    res["capB"] = (pb + np.array([tb[1], -tb[0]]) * B["outR"]).tolist()
                d3 = min(d0 + 3, lim); pa3, _ = A["at"](d3); pb3, _ = B["at"](d3)
                seg = LineString([pa3, pb3]); hole = seg.length - shapely.intersection(seg, U).length
                res["holeW"] = float(hole); res["r"] = float(min(4.0, hole / 2)); res["kind"] = "grass" if hole >= 1.8 else "concrete"
                break
        return res

if __name__ == "__main__":
    gpkg, graph, st, out = sys.argv[1:5]
    J = Junctions(gpkg, graph, st)
    cl = J.clusters
    if "--nodes" in sys.argv:
        want = set(int(v) for v in sys.argv[sys.argv.index("--nodes") + 1].split(","))
        cl = [c for c in cl if want & set(c)]
    res = []
    import time; t0 = time.time()
    for i, c in enumerate(cl):
        try: r = J.analyse(c)
        except Exception as ex: r = dict(nodes=c, error=repr(ex))
        for a in r.get("arms", []): a.pop("at", None)
        res.append(r)
        if i % 2000 == 0: print("  %d / %d junctions %.0f s" % (i, len(cl), time.time() - t0), flush=True)
    pickle.dump(res, open(out, "wb"))
    print("junctions:", len(res))
