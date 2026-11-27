"""ROAD SURVEY: the survey's stations against the HAND measures (ground truth), station by station.

  py -I tools/city/survey/compare_gt.py HAND_MEASURES STATIONS.pkl OUT.txt

For every hand station (s, LEFT, RIGHT) the survey's value at the same station is read from the cleaned 2 m series
(linear between its two neighbours), with the reason when it has none. The hand file's side KIND is reported with it:
a 'paint M' side is a painted median's middle (pavement goes on: the survey has no kerb there), a 'line' side is a
lane line (likewise), 'shoulder M' is the pavement's edge (comparable)."""
import sys, pickle, math
import numpy as np

FLAG = {0: "clean", 1: "apron bridged", 2: "junction mouth", 3: "street mouth bridged", 4: "open/off/shared", 5: "widening kept"}

def parse_hand(path):
    E = {}; cur = None
    for raw in open(path, encoding="utf-8"):
        line = raw.split("#")[0].strip()
        if not line: continue
        t = line.split()
        if t[0] == "edge":
            cur = {"st": [], "kindL": "kerb", "kindR": "kerb"}; E[int(t[1])] = cur
            for i in range(2, len(t)):
                if t[i] in ("left", "right") and i + 1 < len(t):
                    cur["kind" + ("L" if t[i] == "left" else "R")] = t[i + 1]
        elif t[0] == "junction": cur = None
        elif t[0] == "s" and cur is not None:
            cur["st"].append((float(t[1]), None if t[2] == "-" else float(t[2]), None if t[3] == "-" else float(t[3])))
    return E

def at(r, side, s):
    v = r["Lc" if side == "L" else "Rc"]; fl = r["fL" if side == "L" else "fR"]; S = r["s"]
    j = int(np.clip(np.searchsorted(S, s), 1, len(S) - 1)); a, b = j - 1, j
    if abs(S[a] - s) < 1e-6: b = a
    va, vb = v[a], v[b]
    k = a if abs(S[a] - s) <= abs(S[b] - s) else b
    why = FLAG[int(fl[k])]
    if fl[k] == 4:
        why = "shared (opposing carriageway in one pavement)" if r["shL" if side == "L" else "shR"][k] else ("open" if r["openL" if side == "L" else "openR"][k] else "off pavement")
    raw = r["left" if side == "L" else "right"][k]
    if not (np.isfinite(va) and np.isfinite(vb)): return (va if np.isfinite(va) else vb if np.isfinite(vb) else float("nan")), why, raw
    t = 0 if b == a else (s - S[a]) / (S[b] - S[a])
    return va + (vb - va) * t, why, raw

def main(hand, pkl, out):
    H = parse_hand(hand); R = pickle.load(open(pkl, "rb"))
    rows = []; lines = []
    lines.append("SURVEY vs HAND, station by station (metres; + = survey wider than the hand reading). 'raw' = the survey's")
    lines.append("uncleaned cut at the nearest 2 m station. kind = the hand file's side kind.")
    lines.append("%-6s %7s %-4s %-9s %7s %7s %7s %7s  %s" % ("edge", "s", "side", "kind", "hand", "survey", "diff", "raw", "survey note"))
    for ei, h in H.items():
        if ei not in R: continue
        r = R[ei]
        for (s, L, Rr) in h["st"]:
            for side, hv in (("L", L), ("R", Rr)):
                if hv is None: continue
                sv, why, raw = at(r, side, s)
                d = sv - hv if np.isfinite(sv) else float("nan")
                kind = h["kind" + side]
                rows.append((ei, s, side, kind, hv, sv, d, raw, why))
                lines.append("%-6d %7.2f %-4s %-9s %7.2f %7s %7s %7s  %s" % (ei, s, side, kind, hv, "%.2f" % sv if np.isfinite(sv) else "-",
                             "%+.2f" % d if np.isfinite(d) else "-", "%.2f" % raw if np.isfinite(raw) else "-", why))
    kerb = [r for r in rows if r[3] in ("kerb", "shoulder")]
    have = [r for r in kerb if np.isfinite(r[6])]
    ok = [r for r in have if abs(r[6]) <= 0.5]
    lines.append("")
    lines.append("KERB / SHOULDER SIDES: %d hand readings, %d with a survey value, %d within 0.5 m (%.0f %%); median |diff| %.2f m, mean diff %+.2f m"
                 % (len(kerb), len(have), len(ok), 100.0 * len(ok) / max(1, len(have)), np.median([abs(r[6]) for r in have]) if have else float("nan"),
                    np.mean([r[6] for r in have]) if have else float("nan")))
    oth = [r for r in rows if r[3] not in ("kerb", "shoulder")]
    lines.append("PAINT / LINE SIDES (not a kerb: the survey cannot give them): %d readings" % len(oth))
    with open(out, "w", newline="\n") as f: f.write("\n".join(lines) + "\n")
    return rows

if __name__ == "__main__":
    main(*sys.argv[1:4])
