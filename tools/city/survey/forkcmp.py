"""ROAD SURVEY stage 4: the fork check old | new (CityForkCheck outputs), places deduplicated across overlapping spots.

  py -I tools/city/survey/forkcmp.py OLD.txt OLD.tsv NEW.txt NEW.tsv SPOTS.csv OUT.txt
"""
import sys, re, csv, collections

CL = ["FOREIGN", "COPLANAR", "LAYERED", "MATERIAL"]

def spots(txt):
    o = {}
    for l in open(txt, encoding="utf-8", errors="replace"):
        m = re.match(r"SPOT (\S+) \[.*?\]: FOREIGN (\d+) .*?COPLANAR (\d+) LAYERED (\d+) MATERIAL (\d+) .*?DECKEND (\d+)", l)
        if m: o[m.group(1)] = [int(m.group(i)) for i in range(2, 7)]
    return o

def places(tsv, keep=None):
    s = set()
    for r in csv.DictReader(open(tsv, encoding="utf-8"), delimiter="\t"):
        if r["class"] in CL and (keep is None or r["spot"] in keep): s.add((r["class"], r["x"], r["z"]))
    return s

def decks(txt):
    return [l.strip() for l in open(txt, encoding="utf-8", errors="replace") if l.startswith("  deck ")]

def main(ot, otsv, nt, ntsv, sp, out):
    O, N = spots(ot), spots(nt)
    T = ["FORK CHECK OLD | NEW (EDITOR: CityForkCheck.Run, tiles built live; OLD = PSX_CITY_SHALLOWFORK unset, NEW = 1)", ""]
    groups = {"all 649 pairs + 17 nodes + Wendover": None,
              "the 649 shallow pairs": {k for k in O if k.startswith("p")},
              "the 17 SF1 nodes": {k for k in O if k.startswith("n")}, "Wendover (w3583)": {"w3583"}}
    T.append("PLACES (a 4 m cell; each counted once however many spots cover it):")
    T.append("%-40s " % "" + " ".join("%-16s" % c for c in CL))
    for g, keep in groups.items():
        po, pn = places(otsv, keep), places(ntsv, keep)
        T.append("%-40s " % g + " ".join("%-16s" % ("%d | %d" % (sum(1 for p in po if p[0] == c), sum(1 for p in pn if p[0] == c))) for c in CL))
    do, dn = decks(ot), decks(nt)
    T.append("")
    T.append("(c) DECK END IN A ROAD, city-wide (line model): OLD %d | NEW %d" % (len(do), len(dn)))
    T.append("    " + [l for l in open(nt, encoding="utf-8") if l.startswith("(c)")][0].strip())
    T.append("")
    T.append("PER SPOT, the 17 nodes and Wendover (FOREIGN COPLANAR LAYERED MATERIAL DECKEND, old | new):")
    for k in sorted(groups["the 17 SF1 nodes"] | {"w3583"}):
        o, n = O.get(k), N.get(k)
        if o is None or n is None: continue
        worse = [CL[i] if i < 4 else "DECKEND" for i in range(5) if n[i] > o[i]]
        T.append("  %-8s %s | %s%s" % (k, " ".join(map(str, o)), " ".join(map(str, n)), ("   WORSE: " + ", ".join(worse)) if worse else ""))
    ch = [(k, O[k], N[k]) for k in groups["the 649 shallow pairs"] if k in N and O[k] != N[k]]
    T.append("")
    T.append("THE 649 PAIRS: %d spots changed; worse on some class at %d" % (len(ch), sum(1 for k, o, n in ch if any(n[i] > o[i] for i in range(5)))))
    T.append("")
    T.append("COST (the check's own tile builds, the same tiles): " + " | ".join(l.strip() for l in (open(ot).read().split("\n") + open(nt).read().split("\n")) if l.startswith("cost")))
    open(out, "w", newline="\n").write("\n".join(T) + "\n"); print("\n".join(T))

if __name__ == "__main__":
    main(*sys.argv[1:7])
