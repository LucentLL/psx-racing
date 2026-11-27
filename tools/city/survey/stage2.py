"""ROAD SURVEY stage 2: kerb kinds, the kerb FACE, the test set and its measures file.

  py -I tools/city/survey/stage2.py GPKG GRAPH.json STATIONS.pkl JUNCTIONS.pkl HAND_MEASURES OUTDIR

Writes into OUTDIR: kerb_kinds.txt (the city's counts), testset.csv (the junctions, kinds, coordinates),
measures_stage2.txt (provider survey: the test set + spots 80 / 81 with the hand file's islands, picks and paved corners
+ the Wendover split), spots_stage2.csv (CityIslandGate's spot format), kinds.pkl.

THE COORDINATOR'S RULING (11-27): a measure is the KERB FACE. Kerbed side: survey - 0.15 m (the curb's top; the county
line is the back of the curb). Shoulder side: the survey edge as it is, the side's kind `shoulder M` (M = the class
profile's shoulder on that side: where the rule's edge line stands inside the pavement). A SHARED side: no measure.

KERB KIND, per edge side (the county layer does not say; these are the tests, in order):
 KK1 SIDEWALK: at 30 % or more of the side's cut stations a county Sidewalk polygon starts within 3 m outside the pavement
     edge -> KERBED.
 KK2 TOWN LOCAL: a local street (the map's lowest rank) whose pavement is a town layer's (not State) -> KERBED.
 KK3 WANDER: the side's wander = the median absolute departure of its raw cut from its own 62 m running median, on
     clean stations. W_KERB = the 90th percentile of the wander of the KK1 (sidewalk-kerbed) sides city-wide; W_SMOOTH
     = their median. On a State road with no sidewalk: wander > W_KERB -> SHOULDER; wander <= W_SMOOTH -> KERBED (an
     edge as smooth as a kerbed one; the weakest call, reported apart).
 KK4 NOT CALLED (fewer than 10 clean stations, or between W_SMOOTH and W_KERB, or a town non-local road with no
     sidewalk): KERBED in a town layer, SHOULDER on a State road (the coordinator's default).
"""
import sys, os, math, json, pickle, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import county, survey as SV
from compare_gt import parse_hand

INSET = 0.15
SPOTS = [(5587.78, 11806.86), (5564.06, 11839.02), (6246.80, 13380.12), (6355.66, 13212.55), (6600.40, 12318.50), (3970.7, 1763.0)]

def wander(raw, fl):
    x = raw.copy(); x[fl != 0] = np.nan
    if np.isfinite(x).sum() < 10: return np.nan
    pad = 15; xp = np.concatenate([np.full(pad, np.nan), x, np.full(pad, np.nan)])
    import warnings
    with warnings.catch_warnings():
        warnings.simplefilter("ignore"); base = np.nanmedian(np.lib.stride_tricks.sliding_window_view(xp, 2 * pad + 1), axis=1)
    return float(np.nanmedian(np.abs(x - base)))

def kinds(E, R, eop_attr):
    """per (edge, side): (kind 'kerb'|'shoulder', rule 'KK1'..'KK4', layer)"""
    pre = {}
    for ei, r in R.items():
        pi = r["poly"][r["poly"] >= 0]
        lay = collections.Counter(eop_attr[i][2] for i in pi).most_common(1)[0][0] if len(pi) else "(none)"
        for side in "LR":
            raw = r["left" if side == "L" else "right"]; fl = r["f" + side]; sw = r["sw" + side]
            cut = np.isfinite(raw) & ~r["sh" + side] & ~r["open" + side]
            swf = float(((sw <= 3.0) & cut).sum()) / max(1, cut.sum())
            pre[(ei, side)] = (lay, swf, wander(raw, fl), int((fl == 0).sum()))
    kw = [w for (ei, s), (lay, swf, w, n) in pre.items() if swf >= 0.3 and np.isfinite(w)]
    W_KERB, W_SMOOTH = float(np.percentile(kw, 90)), float(np.percentile(kw, 50))
    out = {}
    for (ei, side), (lay, swf, w, n) in pre.items():
        town = lay not in ("State", "(none)")
        if swf >= 0.3: k = ("kerb", "KK1")
        elif town and E[ei]["rank"] == 0: k = ("kerb", "KK2")
        elif lay == "State" and n >= 10 and np.isfinite(w) and w > W_KERB: k = ("shoulder", "KK3")
        elif lay == "State" and n >= 10 and np.isfinite(w) and w <= W_SMOOTH: k = ("kerb", "KK3s")
        else: k = ("kerb" if town else "shoulder", "KK4")
        out[(ei, side)] = (k[0], k[1], lay)
    return out, W_KERB, W_SMOOTH

def face(r, side, kind):
    v = r["Lc" if side == "L" else "Rc"].copy()
    return v - INSET if kind == "kerb" else v

def edge_block(sv, R, K, ei, hold=None, hand=None):
    """the survey's edge block (kerb face applied). hand: the hand file's edge (its stations and kinds win on a side
    the survey has no value for, and on a paint / line side)."""
    r = R[ei]; e = sv.E[ei]
    sides = {}
    hdr_kind = []
    for side, word in (("L", "left"), ("R", "right")):
        kind = K[(ei, side)][0]
        hk = hand["kind" + side] if hand else None
        if hand: kind = "kerb" if hk == "kerb" else "shoulder"   # at 80 / 81 the photo-read kind of the hand file wins
        v = face(r, side, kind)
        keep = SV.decimate(r["s"], np.round(v, 2))
        if hand and (hk in ("paint", "line") or not keep):
            sides[side] = ("hand", None); continue
        sides[side] = ("survey", (keep, np.round(v, 2)))
        if kind == "shoulder": hdr_kind.append("%s shoulder %.1f" % (word, e["shl"] if side == "L" else e["shr"]))
    return sides, hdr_kind

def main(gpkg, graph, stp, jp, hand_path, outdir):
    os.makedirs(outdir, exist_ok=True)
    sv = SV.Survey(gpkg, graph, os.path.dirname(os.path.abspath(graph)))
    R = pickle.load(open(stp, "rb")); J = pickle.load(open(jp, "rb")); E = sv.E
    K, WK, WS = kinds(E, R, sv.eop_attr)
    pickle.dump(K, open(os.path.join(outdir, "kinds.pkl"), "wb"))
    # ---- kerb-kind counts
    T = ["KERB KINDS (stage2.py KK1-KK4), every edge side the survey cut, city-wide",
         "W_KERB (90th pct of the sidewalk-kerbed sides' wander) = %.3f m; W_SMOOTH (their median) = %.3f m" % (WK, WS), ""]
    c = collections.Counter((v[0], v[1]) for v in K.values())
    T.append("%-10s %-6s %8s  %s" % ("kind", "test", "sides", "meaning"))
    mean = {"KK1": "a sidewalk within 3 m outside", "KK2": "a local street in a town layer", "KK3": "State road, edge wanders",
            "KK3s": "State road, edge as smooth as a kerb (weak call)", "KK4": "NOT CALLED: town -> kerb, State -> shoulder"}
    for (k, t), n in sorted(c.items(), key=lambda kv: (kv[0][1], kv[0][0])): T.append("%-10s %-6s %8d  %s" % (k, t, n, mean[t]))
    tot = len(K); nc = sum(n for (k, t), n in c.items() if t == "KK4")
    T.append("TOTAL %d sides: kerbed %d, shoulder %d; called %d (%.0f %%), not called %d (%.0f %%)" % (tot, sum(n for (k, t), n in c.items() if k == "kerb"),
             sum(n for (k, t), n in c.items() if k == "shoulder"), tot - nc, 100 * (tot - nc) / tot, nc, 100 * nc / tot))
    T.append("")
    T.append("BY CLASS (kerbed / shoulder / not called):")
    bc = collections.defaultdict(collections.Counter)
    for (ei, s), (k, t, lay) in K.items(): bc[E[ei]["cls"]][k] += 1; bc[E[ei]["cls"]]["nc"] += t == "KK4"
    for cl in sorted(bc): T.append("  %-15s kerb %7d  shoulder %7d  not called %7d" % (cl, bc[cl]["kerb"], bc[cl]["shoulder"], bc[cl]["nc"]))
    T.append("")
    T.append("A SECOND SOURCE: NCDOT's Road Characteristics table (not downloaded) carries per State-road segment the curb /")
    T.append("shoulder type and width and the median type - it would settle KK3 / KK4 on State roads (every 'not called' and weak")
    T.append("call there) and the median KIND the polygon cannot give (raised grass, concrete, painted: SHARED sides).")
    open(os.path.join(outdir, "kerb_kinds.txt"), "w", newline="\n").write("\n".join(T) + "\n")
    print("\n".join(T))
    # ---- the test set
    def near_spot(j): return min(math.hypot(j["x"] - x, j["z"] - z) for x, z in SPOTS) < 400
    def strict(j):
        return (j.get("onpave") and j.get("arms") and all(np.isfinite(a["outL"]) and np.isfinite(a["outR"]) for a in j["arms"])
                and all(x["status"] in ("fit", "parallel") for x in j["returns"]) and all(x["status"] == "hole" and "capA" in x for x in j["noses"]))
    cats = {"A arterial x arterial, grass median": [], "B arterial x local, T or cross": [], "C slip / link corner": [], "D local x local": []}
    def borders(j):   # every arm's two borders measured, every median a grass hole with caps (a corner may stay the rule's)
        return (j.get("onpave") and j.get("arms") and all(np.isfinite(a["outL"]) and np.isfinite(a["outR"]) for a in j["arms"])
                and all(x["status"] == "hole" and "capA" in x for x in j["noses"]))
    for j in J:
        if "x" not in j or near_spot(j) or not borders(j): continue
        if not strict(j):
            # only kind A is filled from the looser list (9 strict ones in the city): its corners that do not fit stay the rule's
            arms = j["arms"]; nl = [a for a in arms if not a["link"]]; art = [a for a in nl if 2 <= a["rank"] <= 4]
            if (not any(a["rank"] == 5 for a in arms) and len(art) >= 2 and len(set(a["name"] for a in art)) >= 2
                    and any(x["status"] == "hole" for x in j["noses"])):
                j["_loose"] = True; cats["A arterial x arterial, grass median"].append(j)
            continue
        arms = j["arms"]; n = len(arms); nl = [a for a in arms if not a["link"]]
        art = [a for a in nl if 2 <= a["rank"] <= 4]
        if any(a["rank"] == 5 for a in arms): continue
        if len(art) >= 2 and len(set(a["name"] for a in art)) >= 2 and any(x["status"] == "hole" for x in j["noses"]): cats["A arterial x arterial, grass median"].append(j)
        elif art and any(a["rank"] == 0 for a in nl) and 3 <= n <= 4 and not any(a["link"] for a in arms): cats["B arterial x local, T or cross"].append(j)
        elif any(a["link"] for a in arms) and any(x["status"] == "fit" and (E[x["a"]]["link"] or E[x["b"]]["link"]) for x in j["returns"]): cats["C slip / link corner"].append(j)
        elif all(a["rank"] == 0 for a in arms) and 3 <= n <= 4: cats["D local x local"].append(j)
    want = {"A arterial x arterial, grass median": 20, "B arterial x local, T or cross": 20, "C slip / link corner": 10, "D local x local": 10}
    pick = []
    for cat, js in cats.items():
        # spread: farthest-point sampling from the junction nearest the city centre
        if not js: continue
        P = np.array([[j["x"], j["z"]] for j in js]); ch = [int(np.argmin(np.hypot(P[:, 0], P[:, 1] - 4000)))]
        d = np.hypot(*(P - P[ch[0]]).T)
        while len(ch) < min(want[cat], len(js)):
            k = int(np.argmax(d)); ch.append(k); d = np.minimum(d, np.hypot(*(P - P[k]).T))
        for k in ch: pick.append((cat, js[k]))
        print(cat, "eligible", len(js), "picked", len(ch))
    # ---- the measures file
    H = parse_hand(hand_path); hand_text = open(hand_path, encoding="utf-8").read()
    L = ["# MEASURES, ROAD SURVEY STAGE 2 TEST SET. provider survey: kerb FACES (county back-of-curb line - 0.15 m on a kerbed",
         "# side; the edge as it is on a shoulder side) computed from Mecklenburg County GIS Edge-of-Pavement polygons by",
         "# tools/city/survey (survey.py S1-S9, junctions.py J1-J5, stage2.py KK1-KK4). Spots 80 / 81: the shipped HAND file's",
         "# islands, noses, paved corners, rows and lane picks over SURVEY kerbs (hand stations kept on a side the survey has",
         "# no value for and on a paint / line side). Numbers only.", "provider survey"]
    rows_csv = []; done_edges = set(); jblocks = []
    measured = set(n for _, j in pick for n in j["nodes"]) | {3583}
    def write_edge(ei, hand=None):
        if ei in done_edges or ei not in R: return
        sides, hk = edge_block(sv, R, K, ei, hand=hand)
        e = E[ei]
        if hand is not None:
            # keep the hand header's words (blend / hold / kinds); add a shoulder kind only where the hand named none
            hdr = [l for l in hand_text.split("\n") if l.split("#")[0].split()[:2] == ["edge", str(ei)]][0].split("#")[0].rstrip()
            L.append(hdr + "   # SURVEY stations on: " + ", ".join(s for s in "LR" if sides[s][0] == "survey") + "; hand kinds kept (the KK test said " + ", ".join("%s %s %s" % (s, K[(ei, s)][0], K[(ei, s)][1]) for s in "LR") + ")")
        else:
            hold = ""   # held only at an end that is a node of a MEASURED junction (blend back to the rule elsewhere)
            for end, n in (("a", e["a"]), ("b", e["b"])):
                if n in measured: hold += end
            L.append("edge %d blend 30%s%s   # %s, %s; kinds L %s (%s) R %s (%s)" % (ei, (" hold " + hold) if hold else "", (" " + " ".join(hk)) if hk else "",
                     e["cls"], e["name"] or "-", K[(ei, "L")][0], K[(ei, "L")][1], K[(ei, "R")][0], K[(ei, "R")][1]))
        r = R[ei]; st = {}
        for side in "LR":
            src, data = sides[side]
            if src == "survey":
                keep, v = data
                for i in keep: st.setdefault(round(float(r["s"][i]), 2), [None, None])["LR".index(side)] = "%.2f" % v[i]
            elif hand is not None:
                for (s, lv, rv) in hand["st"]:
                    val = lv if side == "L" else rv
                    if val is not None: st.setdefault(round(s, 2), [None, None])["LR".index(side)] = "%.2f" % val
        for s in sorted(st): L.append("s %.2f %s %s" % (s, st[s][0] or "-", st[s][1] or "-"))
        if hand is not None:
            # the hand's lane picks, arrows, stop: copied verbatim from its block
            blk = hand_text.split("\n"); i0 = [i for i, l in enumerate(blk) if l.split("#")[0].split()[:2] == ["edge", str(ei)]][0]
            for l in blk[i0 + 1:]:
                w = l.split("#")[0].split()
                if not w: continue
                if w[0] in ("edge", "junction"): break
                if w[0] in ("line", "p", "arrow", "stop"): L.append(l.rstrip())
        done_edges.add(ei)
    for k, (cat, j) in enumerate(pick):
        name = "t%02d" % (k + 1)
        for ei in [a["e"] for a in j["arms"]] + j.get("inner", []): write_edge(ei)
        b = ["junction %s nodes %s   # %s%s; centre x %.1f z %.1f" % (name, " ".join(map(str, j["nodes"])), cat, " (loose: a corner stays the rule's)" if j.get("_loose") else "", j["x"], j["z"])]
        for x in j["noses"]:
            if x["status"] == "hole" and "capA" in x:
                b.append("nose %d %d %.2f %.2f %.2f %.2f r %.1f kind %s" % (x["a"], x["b"], x["capA"][0], x["capA"][1], x["capB"][0], x["capB"][1], x["r"], x["kind"]))
        for x in j["returns"]:
            if x["status"] == "fit": b.append("return %d %d r %.1f   # rms %.2f (rule %.0f)" % (x["a"], x["b"], x["r"], x["rms"], x["rule"]))
        jblocks.append(b)
        la, lo = county.xz_to_ll(j["x"], j["z"])
        rows_csv.append((name, cat, j["x"], j["z"], la, lo, " ".join(map(str, j["nodes"])), " / ".join(sorted(set(a["name"] or a["cls"] for a in j["arms"])))))
    # spots 80 / 81: survey kerbs under the hand
    for ei in H: write_edge(ei, hand=H[ei])
    jtext = hand_text[hand_text.index("junction s80"):hand_text.index("# ---- spot 81")].rstrip("\n").split("\n")
    jtext += [l for l in hand_text[hand_text.index("junction s81"):].rstrip("\n").split("\n")]
    for j in J:
        if 4449 in j["nodes"]:
            for x in j["returns"]:
                if x["status"] == "fit": jtext.append("return %d %d r %.1f   # survey: rms %.2f (rule %.0f)" % (x["a"], x["b"], x["r"], x["rms"], x["rule"]))
    jblocks.append(jtext)
    rows_csv.append(("s80", "spot 80 (owner's): survey kerbs + hand islands / picks", 6355.66, 13212.55, *county.xz_to_ll(6355.66, 13212.55), "4435 4441", "E W T Harris Blvd"))
    rows_csv.append(("s81", "spot 81 (owner's): survey kerbs + hand islands / picks", 6600.40, 12318.50, *county.xz_to_ll(6600.40, 12318.50), "4449 512 8008 8009 8006 10771", "W T Harris / University City Blvd"))
    # Wendover: kerbs only
    for ei in (13593, 3521, 2370): write_edge(ei)
    jblocks.append(["junction wendover nodes 3583   # the Wendover split (kerbs only: the shared sides keep the rule)"])
    rows_csv.append(("wendover", "Wendover split (owner's, h6 cp7)", 3970.7, 1763.0, *county.xz_to_ll(3970.7, 1763.0), "3583", "E Independence Blvd / Albemarle Rd"))
    for b in jblocks: L += b
    open(os.path.join(outdir, "measures_stage2.txt"), "w", newline="\n").write("\n".join(L) + "\n")
    with open(os.path.join(outdir, "testset.csv"), "w", newline="\n") as f:
        f.write("id,kind,x,z,lat,lon,nodes,roads\n")
        for r in rows_csv: f.write('%s,"%s",%.1f,%.1f,%.6f,%.6f,%s,"%s"\n' % r)
    with open(os.path.join(outdir, "spots_stage2.csv"), "w", newline="\n") as f:
        f.write("id,x,z,heading,edges,nodes,kind,owner_date,owner_words,radius,must\n")
        for r in rows_csv:
            f.write('%s,%.2f,%.2f,0,,,"%s",,,45,RAISED=0;SEETHRU=0;OVERLAY=0;HOLES=0;LAYERS=0;STEP=0;KINK=0\n' % (r[0], r[2], r[3], r[1]))
    print("test set:", len(rows_csv), "junctions;", len(done_edges), "edges")

if __name__ == "__main__":
    main(*sys.argv[1:7])
