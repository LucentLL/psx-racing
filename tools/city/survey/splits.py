"""ROAD SURVEY stage 2 (D): shallow splits and merges like the Wendover one, city-wide, from the graph alone.

  py -I tools/city/survey/splits.py GRAPH.json OUT.txt [STATIONS.pkl]

 D1  At every node, every pair of ONE-WAY edges that both LEAVE it (a split) or both ARRIVE at it (a merge).
 D2  Each is followed away from the node for up to 250 m, on through nodes where it is the only way on (degree 2).
 D3  The pair's angle = between their directions over the first 20 m; under 12 degrees is SHALLOW.
 D4  The RULE's ribbons are the class cross-section (width / 2 about each map line, no eases: the line model's gore split,
     LineModel.GoreSplitOn, is NOT applied here). Overlap at d = the two half-widths less the distance between the lines'
     points d metres out. A shallow pair COUNTS when its overlap exceeds 1 m for more than 20 m from the node; the overlap
     length is how far out it stays over 1 m.
 D5  With STATIONS: ONE PAVEMENT when, along the overlap (2 m to its end, at most 60 m), A's raw inner pavement edge
     reaches B's line at 80 % or more of the stations (no kerb or hole between them); else "kerb/hole"; "-" when A has
     fewer than 3 cut stations there.
"""
import sys, os, math, json, pickle, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import county, survey as SV

def main(graph, out, stp=None):
    g = json.load(open(graph)); E = g["edges"]; N = g["nodes"]
    adj = collections.defaultdict(list)
    for e in E: adj[e["a"]].append(e["i"]); adj[e["b"]].append(e["i"])
    R = pickle.load(open(stp, "rb")) if stp else None
    def chain(ei, node, maxL=250.0):
        """polyline leaving node along ei (and on through degree-2 nodes), and the edge list"""
        pts = []; es = []; cur, n = ei, node; tot = 0.0
        while True:
            e = E[cur]; P = e["pts"] if e["a"] == n else e["pts"][::-1]
            pts += P if not pts else P[1:]; es.append(cur); tot += SV.edge_len(P)
            far = e["b"] if e["a"] == n else e["a"]
            nxt = [x for x in adj[far] if x != cur]
            if tot >= maxL or len(nxt) != 1 or nxt[0] in es: break
            cur, n = nxt[0], far
        return pts, es
    rows = []
    for n in range(len(N)):
        if len(adj[n]) < 3: continue
        legs = []
        for ei in adj[n]:
            e = E[ei]
            if not e["ow"] or e["a"] == e["b"]: continue
            leaving = e["a"] == n
            legs.append((ei, leaving))
        for i in range(len(legs)):
            for j in range(i + 1, len(legs)):
                (ea, la), (eb, lb) = legs[i], legs[j]
                if la != lb: continue
                pa, esa = chain(ea, n); pb, esb = chain(eb, n)
                La, Lb = SV.edge_len(pa), SV.edge_len(pb)
                if min(La, Lb) < 20: continue
                Ca, Ta, _ = SV.polyline_frame(pa, np.array([10.0])); Cb, Tb, _ = SV.polyline_frame(pb, np.array([10.0]))
                ang = math.degrees(math.acos(max(-1, min(1, float(Ta[0] @ Tb[0])))))
                if ang >= 12: continue
                ds = np.arange(0, min(La, Lb, 250.0), 2.0)
                A_, _, _ = SV.polyline_frame(pa, ds); B_, _, _ = SV.polyline_frame(pb, ds)
                ov = E[ea]["w"] / 2 + E[eb]["w"] / 2 - np.hypot(*(A_ - B_).T)
                k = 0
                while k < len(ds) and ov[k] > 1.0: k += 1
                olen = float(ds[k - 1]) if k > 0 else 0.0
                if olen <= 20: continue
                top = max(E[ea]["rank"], E[eb]["rank"]); link = E[ea]["link"] or E[eb]["link"]
                cls = ["local", "tertiary", "secondary", "primary", "trunk", "motorway"][top] + ("+link" if link else "")
                one = None
                if R is not None:
                    # D5: the inner sides along the first 20 m of overlap: is there a surveyed kerb / hole between?
                    # which side of A faces B: the sign of B's offset from A's line
                    nA = np.array([-Ta[0][1], Ta[0][0]]); sgn = 1 if float((Cb[0] - Ca[0]) @ nA) > 0 else -1
                    ra = R.get(ea)
                    if ra is not None:
                        fwd = E[ea]["a"] == n; s = ra["s"] if fwd else ra["L"] - ra["s"]
                        m = (s >= 2) & (s <= min(olen, 60.0))
                        side_left = (sgn > 0) == fwd
                        raw = ra["left" if side_left else "right"][m]
                        # the distance from A's line to B's line at those stations
                        Pb, _, _ = SV.polyline_frame(pb, s[m]); Pa = ra["C"][m]
                        gap = np.hypot(*(Pb - Pa).T)
                        ok = np.isfinite(raw)
                        if ok.sum() >= 3:
                            # a kerb or hole BETWEEN them: the inner pavement edge ends short of B's line
                            between = (raw[ok] < gap[ok] - 0.5).mean()
                            one = bool(between < 0.2)
                rows.append(dict(node=n, a=ea, b=eb, kind="split" if la else "merge", ang=ang, olen=olen, maxov=float(ov[0]),
                                 cls=cls, x=N[n][0], z=N[n][1], na=E[ea]["name"], nb=E[eb]["name"], one=one))
    T = ["SHALLOW SPLITS AND MERGES (rules D1-D5 in tools/city/survey/splits.py): two one-way roads leaving or meeting a node",
         "at under 12 degrees whose class ribbons overlap by more than 1 m for more than 20 m (line model eases NOT applied)", ""]
    T.append("total: %d pairs at %d nodes (splits %d, merges %d)" % (len(rows), len(set(r["node"] for r in rows)),
             sum(r["kind"] == "split" for r in rows), sum(r["kind"] == "merge" for r in rows)))
    if R is not None:
        T.append("  of them, the survey shows ONE PAVEMENT between the two (D5): %d" % sum(1 for r in rows if r["one"]))
    T.append("")
    T.append("%-22s %6s %6s %6s %10s %10s" % ("class (top of the pair)", "pairs", "split", "merge", "overlap m", "one pave"))
    by = collections.defaultdict(list)
    for r in rows: by[r["cls"]].append(r)
    for c in sorted(by, key=lambda c: -len(by[c])):
        rs = by[c]
        T.append("%-22s %6d %6d %6d %10.0f %10s" % (c, len(rs), sum(r["kind"] == "split" for r in rs), sum(r["kind"] == "merge" for r in rs),
                 np.median([r["olen"] for r in rs]), sum(1 for r in rs if r["one"]) if R is not None else "-"))
    T.append("    (overlap m = median length over which the ribbons overlap by more than 1 m)")
    T.append("")
    T.append("THE 20 LONGEST OVERLAPS")
    T.append("%3s %-6s %6s %6s %6s %6s %9s %9s %10s %11s  %s" % ("#", "kind", "node", "angle", "len m", "max ov", "x", "z", "lat", "lon", "edges / names / one pavement"))
    for i, r in enumerate(sorted(rows, key=lambda r: -r["olen"])[:20]):
        la, lo = county.xz_to_ll(r["x"], r["z"])
        T.append("%3d %-6s %6d %6.1f %6.0f %6.1f %9.1f %9.1f %10.5f %11.5f  e%d %s / e%d %s / %s" % (i + 1, r["kind"], r["node"], r["ang"], r["olen"], r["maxov"],
                 r["x"], r["z"], la, lo, r["a"], (r["na"] or "-")[:24], r["b"], (r["nb"] or "-")[:24], {True: "one", False: "kerb/hole", None: "-"}[r["one"]]))
    w = [r for r in rows if r["node"] == 3583]
    T.append("")
    T.append("WENDOVER (node 3583): " + ("; ".join("e%d/e%d %s %.1f deg, overlap %.0f m (max %.1f m)" % (r["a"], r["b"], r["kind"], r["ang"], r["olen"], r["maxov"]) for r in w) or "not counted"))
    open(out, "w", newline="\n", encoding="utf-8").write("\n".join(T) + "\n")
    pickle.dump(rows, open(out + ".pkl", "wb"))
    print("\n".join(T))

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
