"""ROAD SURVEY: the city scorecard (one text file of tables).

  py -I tools/city/survey/score.py GRAPH.json STATIONS.pkl JUNCTIONS.pkl MISSING.pkl OUT.txt

Reads the outputs of survey.py, junctions.py and missing.py (and the county cache beside GRAPH.json for the layer of
each polygon). Every distance is metres; RULE - SURVEY: + = the rule's pavement edge lies further out than the kerb.
"""
import sys, os, math, pickle, json, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import county

CLS = ['motorway', 'motorway_link', 'trunk', 'trunk_link', 'primary', 'primary_link', 'secondary', 'secondary_link',
       'tertiary', 'tertiary_link', 'local']

def q(a, p): return float(np.percentile(a, p)) if len(a) else float("nan")

def main(graph, stp, jp, mp, out):
    g = json.load(open(graph)); E = g["edges"]; N = g["nodes"]
    R = pickle.load(open(stp, "rb")); J = pickle.load(open(jp, "rb")); M = pickle.load(open(mp, "rb"))
    d = county.load(None, os.path.join(os.path.dirname(os.path.abspath(graph)), "county_xz.pkl"))
    eop_attr = [a for a in d["attrs"] if a[1] == "Edge-of-Pavement"]
    deg = collections.Counter()
    for e in E: deg[e["a"]] += 1; deg[e["b"]] += 1
    T = []
    def P(s=""): T.append(s)
    P("ROAD SURVEY, STAGE 1 - CITY SCORECARD (survey = Mecklenburg County GIS Edge-of-Pavement polygons; rules = the game's")
    P("class cross-section, width / 2 each side of the map line, no eases; graph = Resources/charlotte_city.bytes of PC82)")
    P("")
    # ---------------- T1 coverage by class
    cov = {}; lay = {}
    for ei, r in R.items():
        n = len(r["s"]); on = r["onpave"].mean() if n else 0
        cov[ei] = on >= 0.5
        pi = r["poly"][r["poly"] >= 0]
        lay[ei] = collections.Counter(eop_attr[i][2] for i in pi).most_common(1)[0][0] if len(pi) else "(none)"
    P("T1. COVERAGE BY ROAD CLASS: an edge is covered when half or more of its 2 m stations lie on a county pavement polygon")
    P("%-15s %7s %9s %9s %9s %7s" % ("class", "edges", "km", "covered", "km cov", "share"))
    tot = [0, 0, 0, 0]
    for c in CLS:
        es = [e for e in E if e["cls"] == c and e["i"] in R]
        if not es: continue
        km = sum(R[e["i"]]["L"] for e in es) / 1000; cv = [e for e in es if cov[e["i"]]]; kc = sum(R[e["i"]]["L"] for e in cv) / 1000
        tot = [tot[0] + len(es), tot[1] + km, tot[2] + len(cv), tot[3] + kc]
        P("%-15s %7d %9.1f %9d %9.1f %6.0f%%" % (c, len(es), km, len(cv), kc, 100 * kc / max(km, 1e-9)))
    P("%-15s %7d %9.1f %9d %9.1f %6.0f%%" % ("ALL", tot[0], tot[1], tot[2], tot[3], 100 * tot[3] / max(tot[1], 1e-9)))
    P("")
    P("T2. COVERAGE BY COUNTY LAYER (the jurisdiction of the polygon under most of an edge's stations)")
    byl = collections.defaultdict(lambda: [0, 0.0])
    for ei, r in R.items():
        k = lay[ei] if cov[ei] else "(not covered: outside the county or off the polygons)"
        byl[k][0] += 1; byl[k][1] += r["L"] / 1000
    for k, (n, km) in sorted(byl.items(), key=lambda kv: -kv[1][1]): P("  %-58s %6d edges %8.1f km" % (k, n, km))
    jn = [n for n in range(len(N)) if deg[n] >= 3]
    P("")
    P("JUNCTION NODES (3+ edge ends): %d; junctions after joining (survey J1): %d" % (len(jn), len(J)))
    jon = [j for j in J if j.get("onpave")]
    P("  junctions whose centre lies on county pavement: %d (%.0f %%)" % (len(jon), 100 * len(jon) / max(1, len(J))))
    byc = collections.defaultdict(lambda: [0, 0])
    for j in J:
        if "arms" not in j: continue
        top = max((a["rank"] for a in j["arms"]), default=0); key = ["local", "tertiary", "secondary", "primary", "trunk", "motorway"][top]
        byc[key][0] += 1; byc[key][1] += 1 if j.get("onpave") else 0
    for k in ["motorway", "trunk", "primary", "secondary", "tertiary", "local"]:
        if k in byc: P("  top class %-10s %6d junctions, %6d on pavement (%.0f %%)" % (k, byc[k][0], byc[k][1], 100 * byc[k][1] / max(1, byc[k][0])))
    P("")
    # ---------------- T3 station cut quality
    P("T3. HOW THE STATIONS CUT (covered edges; per side): clean = a kerb read straight off the cut")
    names = ["clean", "apron bridged", "junction mouth", "street mouth bridged", "open/off", "widening kept", "shared"]
    P("%-15s %9s " % ("class", "sides") + " ".join("%9s" % n[:9] for n in names) + " %9s" % "bothclean")
    for c in CLS + ["ALL"]:
        cnt = np.zeros(7); n = 0; both = 0; nst = 0
        for e in E:
            if (c != "ALL" and e["cls"] != c) or e["i"] not in R or not cov[e["i"]]: continue
            r = R[e["i"]]
            for f, sh in ((r["fL"], r["shL"]), (r["fR"], r["shR"])):
                ff = f.astype(int).copy(); ff[(ff == 4) & sh] = 6
                cnt += np.bincount(ff, minlength=7)[:7]; n += len(ff)
            both += int(((r["fL"] == 0) & (r["fR"] == 0)).sum()); nst += len(r["s"])
        if n == 0: continue
        P("%-15s %9d " % (c, n) + " ".join("%8.1f%%" % (100 * v / n) for v in cnt) + " %8.1f%%" % (100 * both / max(1, nst)))
    P("  (shared = the side's pavement holds an opposing carriageway's map line: a painted median, or a concrete one")
    P("   the layer does not separate; open/off = the cut runs 40 m without a kerb, or the map line is off the pavement)")
    P("")
    # ---------------- T4 rule - survey by class
    P("T4. RULE - SURVEY, PAVEMENT EDGE PER SIDE, MID-BLOCK (clean stations 30 m or more from a junction node), metres")
    P("    + = the rule's edge lies outside the surveyed kerb (the rule paves too wide on that side)")
    P("%-15s %8s %7s %7s %7s %7s %8s %8s | %s" % ("class", "sides", "p10", "median", "p90", "mean|d|", "|d|>0.5", "|d|>1", "CARRIAGEWAY WIDTH rule - survey: n, median, p10, p90"))
    allD = {}
    for c in CLS:
        D = []; W = []
        for e in E:
            if e["cls"] != c or e["i"] not in R or not cov[e["i"]]: continue
            r = R[e["i"]]; s = r["s"]; L = r["L"]
            ends = []
            if deg[e["a"]] >= 3: ends.append(s)
            if deg[e["b"]] >= 3: ends.append(L - s)
            far = np.ones(len(s), bool)
            for x in ends: far &= x >= 30
            hw = e["w"] / 2
            mL = far & (r["fL"] == 0) & np.isfinite(r["Lc"]); mR = far & (r["fR"] == 0) & np.isfinite(r["Rc"])
            D += list(hw - r["Lc"][mL]) + list(hw - r["Rc"][mR])
            mb = mL & mR; W += list(e["w"] - (r["Lc"][mb] + r["Rc"][mb]))
        D = np.array(D); W = np.array(W); allD[c] = D
        if len(D) == 0: continue
        P("%-15s %8d %+7.2f %+7.2f %+7.2f %7.2f %7.0f%% %7.0f%% | %7d %+6.2f %+6.2f %+6.2f" % (c, len(D), q(D, 10), q(D, 50), q(D, 90), np.abs(D).mean(),
          100 * (np.abs(D) > 0.5).mean(), 100 * (np.abs(D) > 1).mean(), len(W), q(W, 50), q(W, 10), q(W, 90)))
    P("    (one station = one 2 m sample; a side counted once per station)")
    P("")
    # ---------------- T5 by kind of junction
    def kind(j):
        arms = j["arms"]; n = len(arms)
        k = "T/Y (3 arms)" if n == 3 else "cross (4 arms)" if n == 4 else "multi (5+ arms)" if n >= 5 else "2 arms"
        div = len(j["noses"]) > 0; lk = any(a["link"] for a in arms)
        top = max(a["rank"] for a in arms)
        grp = "motorway" if top == 5 else "arterial" if top >= 2 else "collector" if top == 1 else "local"
        return grp + ", " + k + (", divided" if div else "") + (", with link/slip" if lk else "")
    K = collections.defaultdict(list)
    for j in J:
        if "arms" in j and j["arms"]: K[kind(j)].append(j)
    P("T5. BY KIND OF JUNCTION (feature by feature; radius and median-end figures are RULE - SURVEY)")
    P("%-48s %6s %6s | %s | %s | %s" % ("kind", "junc", "onpav", "KERB RETURNS: n, fit, compound, open, noborder; rule-r median p10 p90",
                                        "MEDIAN ENDS: n, hole, end rule-survey median p10 p90", "ARM WIDTH rule-survey median (n)"))
    for k in sorted(K, key=lambda k: -len(K[k])):
        js = K[k]
        rets = [x for j in js for x in j["returns"]]; st = collections.Counter(x["status"] for x in rets)
        dr = np.array([x["rule"] - x["r"] for x in rets if x["status"] == "fit"])
        ns = [x for j in js for x in j["noses"]]; dm = np.array([x["dR"] - x["dS"] for x in ns if x["status"] == "hole" and np.isfinite(x["dR"])])
        aw = np.array([2 * a["hw"] - (a["outL"] + a["outR"]) for j in js for a in j["arms"] if np.isfinite(a["outL"]) and np.isfinite(a["outR"])])
        P("%-48s %6d %6d | %5d %5d %5d %5d %5d  %+6.1f %+6.1f %+6.1f | %4d %4d  %+6.1f %+6.1f %+6.1f | %+6.2f (%d)" % (
            k[:48], len(js), sum(1 for j in js if j.get("onpave")), len(rets), st["fit"], st["compound"], st["open"], st["noborder"] + st["parallel"],
            q(dr, 50), q(dr, 10), q(dr, 90), len(ns), sum(1 for x in ns if x["status"] == "hole"), q(dm, 50), q(dm, 10), q(dm, 90), q(aw, 50), len(aw)))
    P("    returns: fit = a circle within 0.3 m rms over 25 degrees or more; compound = two radii (not in the socket);")
    P("    open = no kerb within 15 m of the corner point (a lot, an apron, a street the map lacks); noborder = an arm with no clean survey border")
    P("    median ends: hole = the survey has a raised (grass) median ending there; the others are painted / concrete / none")
    P("    BAY OPENINGS: the survey finds where a carriageway widens for more than 40 m (kept 'widening' stations, T3); the")
    P("    rule's turn pockets live in the line model (a Unity job): NOT compared in this stage.")
    P("")
    # ---------------- T6 worst arterial junctions
    P("T6. THE 30 WORST ARTERIAL JUNCTIONS (top arm trunk / primary / secondary): mean |rule - survey| over the NON-LINK arms'")
    P("    borders (8 - 40 m out), at least 3 borders measured; with the worst kerb-return radius error and the median-end error.")
    P("    (Link arms are left out of the score: a ramp or slip that runs inside its parent's pavement with no hole reads the")
    P("    parent's far kerb as its own - S9 marks only OPPOSING carriageways as shared. Their radius errors stay in T5.)")
    rows = []
    for j in J:
        if "arms" not in j or not j["arms"]: continue
        if max(a["rank"] for a in j["arms"]) < 2 or max(a["rank"] for a in j["arms"]) == 5: continue
        b = [abs(a["hw"] - v) for a in j["arms"] if not a["link"] for v in (a["outL"], a["outR"]) if np.isfinite(v)]
        if len(b) < 3: continue
        rr = [abs(x["rule"] - x["r"]) for x in j["returns"] if x["status"] == "fit"]
        mm = [x["dR"] - x["dS"] for x in j["noses"] if x["status"] == "hole" and np.isfinite(x["dR"])]
        rows.append((float(np.mean(b)), j, max(rr) if rr else float("nan"), max(mm, key=abs) if mm else float("nan"), len(b)))
    rows.sort(key=lambda r: -r[0])
    P("%3s %8s %9s %9s %11s %10s %7s %5s %s" % ("#", "border", "x", "z", "lat", "lon", "radius", "mEnd", "nodes / arms"))
    for i, (sc, j, rr, mm, nb) in enumerate(rows[:30]):
        la, lo = county.xz_to_ll(j["x"], j["z"])
        nm = sorted(set(a["name"] or a["cls"] for a in j["arms"]))
        P("%3d %8.2f %9.1f %9.1f %11.6f %10.6f %7s %5s %s; %s" % (i + 1, sc, j["x"], j["z"], la, lo, "%.1f" % rr if np.isfinite(rr) else "-",
          "%+.0f" % mm if np.isfinite(mm) else "-", ",".join(map(str, j["nodes"][:4])), " / ".join(nm)[:80]))
    allsc = np.array([r[0] for r in rows])
    P("    arterial junctions scored: %d; mean |rule - survey| border median %.2f m, p90 %.2f m; over 1 m: %d, over 2 m: %d" % (
        len(rows), q(allsc, 50), q(allsc, 90), int((allsc > 1).sum()), int((allsc > 2).sum())))
    P("")
    # ---------------- T7 missing streets
    st = [m for m in M if m["street"]]
    P("T7. STREETS THE COUNTY HAS AND THE MAP LACKS (survey M1-M4: county pavement with no map line within the class width + 4 m,")
    P("    street-shaped). A fault class of the map's source (fetch_ways.mjs: tertiary and up outside the Uptown core box). Counted, not fixed.")
    P("    pieces: %d street-shaped (%.0f km), of them Public %d (%.0f km), Private %d (%.0f km)" % (
        len(st), sum(m["L"] for m in st) / 1000, sum(1 for m in st if m["asset"] == "Public"), sum(m["L"] for m in st if m["asset"] == "Public") / 1000,
        sum(1 for m in st if m["asset"] != "Public"), sum(m["L"] for m in st if m["asset"] != "Public") / 1000))
    P("    BY DISTRICT (county layer; core = inside the Uptown core box):")
    dd = collections.defaultdict(lambda: [0, 0.0, set()])
    for m in st:
        k = (m["layer"], "core" if m["core"] else "outside core")
        dd[k][0] += 1; dd[k][1] += m["L"] / 1000; dd[k][2].add(m["name"])
    for k, (n, km, nm) in sorted(dd.items(), key=lambda kv: -kv[1][1]): P("      %-14s %-13s %6d pieces %7.1f km %6d street names" % (k[0], k[1], n, km, len(nm)))
    byn = collections.defaultdict(lambda: [0.0, None, 0])
    for m in st:
        if m["asset"] != "Public" or m["name"] in ("", "0"): continue
        k = (m["name"], m["layer"]); byn[k][0] += m["L"]; byn[k][2] += 1
        if byn[k][1] is None or m["L"] > byn[k][1]["L"]: byn[k][1] = m
    P("    THE 30 LONGEST (public, by county street name; length summed over its pieces; position = its longest piece):")
    for i, ((nm, ly), (L, m, n)) in enumerate(sorted(byn.items(), key=lambda kv: -kv[1][0])[:30]):
        la, lo = county.xz_to_ll(m["x"], m["z"])
        P("     %2d %-32s %-12s %7.0f m in %3d pieces  at x %8.1f z %8.1f (%.5f, %.5f)" % (i + 1, nm[:32], ly, L, n, m["x"], m["z"], la, lo))
    with open(out, "w", newline="\n", encoding="utf-8") as f: f.write("\n".join(T) + "\n")
    print("\n".join(T))

if __name__ == "__main__":
    main(*sys.argv[1:6])
