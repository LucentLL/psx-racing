"""ROAD SURVEY stage 2: the spot table OLD | NEW from two CityIslandGate reports.

  py -I tools/city/survey/spottable.py OLD.txt NEW.txt TESTSET.csv OUT.txt
"""
import sys, re, csv, collections

K = ["RAISED", "SEETHRU", "OVERLAY", "LINEUNDER", "HOLES", "LAYERS", "STEP", "KINK"]

def read(p):
    o = {}
    for l in open(p, encoding="utf-8", errors="replace"):
        m = re.match(r"^SPOT (\S+) \[.*?\]: (.*?) =>", l)
        if m: o[m.group(1)] = {k: int(v) for k, v in re.findall(r"([A-Z]+) (\d+)", m.group(2))}
    return o

def main(old, new, ts, out):
    O, N = read(old), read(new); kinds = {r["id"]: r["kind"] for r in csv.DictReader(open(ts, encoding="utf-8"))}
    T = ["SPOT TABLE OLD | NEW (EDITOR: CityIslandGate.Run, tiles built live, radius 45 m about each junction's centre;",
         "OLD = PSX_CITY_ROADSURFACE=0 (all rule), NEW = the survey measures file; only the junctions that solved)", ""]
    T.append("%-9s %-24s " % ("id", "kind") + " ".join("%-11s" % k for k in K) + "  verdict")
    tot = collections.Counter(); worse = collections.Counter(); better = collections.Counter(); verdicts = collections.Counter()
    for id_ in N:
        if id_ not in O: continue
        o, n = O[id_], N[id_]
        cells = []; w = b = 0
        for k in K:
            ov, nv = o.get(k, 0), n.get(k, 0); tot[k + "_old"] += ov; tot[k + "_new"] += nv
            if nv > ov: worse[k] += 1; w += 1
            if nv < ov: better[k] += 1; b += 1
            cells.append("%3d|%-3d%s" % (ov, nv, "+" if nv > ov else "-" if nv < ov else " "))
        v = "WORSE" if w and not b else "BETTER" if b and not w else "MIXED" if w and b else "SAME"
        verdicts[v] += 1
        T.append("%-9s %-24s " % (id_, kinds.get(id_, "")[:24]) + " ".join("%-11s" % c for c in cells) + "  " + v)
    T.append("")
    T.append("%-34s " % "SUM old|new" + " ".join("%-11s" % ("%d|%d" % (tot[k + "_old"], tot[k + "_new"])) for k in K))
    T.append("%-34s " % "junctions worse on the gate" + " ".join("%-11d" % worse[k] for k in K))
    T.append("%-34s " % "junctions better on the gate" + " ".join("%-11d" % better[k] for k in K))
    T.append("VERDICTS: " + ", ".join("%s %d" % kv for kv in verdicts.items()) + "   (+ = worse than OLD, - = better)")
    open(out, "w", newline="\n", encoding="utf-8").write("\n".join(T) + "\n")
    print("\n".join(T))

if __name__ == "__main__":
    main(*sys.argv[1:5])
