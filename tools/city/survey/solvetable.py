"""ROAD SURVEY stage 2: the solve table from a RunMeasured log (rs_measured_TAG.txt) and the test set.

  py -I tools/city/survey/solvetable.py RS_MEASURED.txt TESTSET.csv OUT.txt
"""
import sys, re, csv, collections

def klass(c):
    """the class of a refusal or fault (the solver's words reduced)"""
    if "arc-did-not-fit (the measured radius" in c: return "R1 a MEASURED kerb-return radius does not fit between the two borders (refused)"
    if "arc-did-not-fit (no fillet" in c: return "R2 no fillet fits down to 0.12 x the class radius, no nose (refused)"
    if "outline crosses itself" in c and "measured nose" in c: return "R3 the outline crosses itself at a MEASURED nose's connector (refused)"
    if "outline crosses itself" in c: return "R4 the outline crosses itself between two arms (a shallow split: connector x through-taper) (refused)"
    if "crosses no row" in c: return "R5 a bay's line crosses no row of the junction (refused)"
    if "paved-not-found" in c: return "R6 a HAND paved corner no longer starts and ends on the (survey) borders (refused)"
    if c.startswith("kink"): return "F1 a kink on the outline at a paved corner (solved, check fails)"
    if "is not road" in c: return "F2 an arm's lane lies off the solved road (solved, check fails)"
    return "other: " + c[:100]

def main(log, ts, out):
    kinds = {r["id"]: r for r in csv.DictReader(open(ts, encoding="utf-8"))}
    lines = open(log, encoding="utf-8", errors="replace").read().split("\n")
    rows = {}; cur = None
    head = re.compile(r"^(t\d\d|s80|s81|wendover) nodes ([\d ]+): (.*)$")
    for l in lines:
        m = head.match(l)
        if m:
            name, rest = m.group(1), m.group(3)
            chk = rest.split("; corners")[0]
            g = lambda p: (re.search(p, rest).group(1) if re.search(p, rest) else "")
            cur = rows[name] = dict(chk=chk, outline=g(r"outline (\d+) vertices"), islands=g(r"(\d+) islands"), tris=g(r"fill (\d+) triangles"),
                                    solve=g(r"solve ([\d.]+) ms"), fill=g(r"fill ([\d.]+) ms"), all=g(r"in all ([\d.]+) ms"),
                                    corners=g(r"corners (.*?); outline"), lane=[], notes=[])
            continue
        if cur is not None and l.startswith("  "):
            t = l.strip()
            if t.startswith("LANE CHECK"): cur["lane"].append(t[len("LANE CHECK "):])
            elif not t.startswith("e") or "ribbon trim" not in t: cur["notes"].append(t)
        elif cur is not None and not l.startswith("  "): cur = None
    T = ["SOLVE TABLE (EDITOR: JunctionSurfaceTools.RunMeasured, edit-mode batch in PSXRoadA, the tile build's own solve;",
         "lane check: survey junctions have no picks = FIT only; s80 / s81 carry the hand picks)", ""]
    T.append("%-9s %-34s %-9s %6s %4s %6s %8s %8s  %s" % ("id", "kind", "result", "outl", "isl", "tris", "solve ms", "fill ms", "check / refusal; lane check"))
    cnt = collections.Counter(); ref = collections.defaultdict(list)
    for id_, k in kinds.items():
        r = rows.get(id_)
        if r is None: T.append("%-9s %-34s %-9s  (not in the log)" % (id_, k["kind"][:34], "MISSING")); cnt["missing"] += 1; continue
        res = "REFUSED" if r["chk"].startswith("refused") else "clean" if r["chk"] == "clean" else "FAULT"
        cnt[res] += 1
        if res != "clean": ref[klass(r["chk"])].append(id_)
        lane = "clear" if (not r["lane"] or r["lane"] == ["clear"]) else "%d conflict(s): %s" % (len(r["lane"]), r["lane"][0][:90])
        cnt["lane clear" if lane == "clear" else "lane conflict"] += 1
        T.append("%-9s %-34s %-9s %6s %4s %6s %8s %8s  %s; LANE %s" % (id_, k["kind"][:34], res, r["outline"], r["islands"], r["tris"], r["solve"], r["fill"], r["chk"][:110], lane))
    T.append("")
    T.append("TOTALS: " + ", ".join("%s %d" % kv for kv in cnt.items()))
    T.append("")
    T.append("REFUSALS AND FAULTS BY CLASS (the solver's own words):")
    for k, ids in sorted(ref.items(), key=lambda kv: -len(kv[1])): T.append("  %3d  %s   e.g. %s" % (len(ids), k[:150], ", ".join(ids[:6])))
    open(out, "w", newline="\n", encoding="utf-8").write("\n".join(T) + "\n")
    print("\n".join(T))
    return rows

if __name__ == "__main__":
    main(*sys.argv[1:4])
