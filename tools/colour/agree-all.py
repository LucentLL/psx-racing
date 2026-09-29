"""Harness vs editor for every frame the harness captured.

    py tools/colour/agree-all.py <editor colour_WebGL dir> <player frames dir> [--tol 2] [--json out.json]

Pairs <player dir>/<name>.png with <editor dir>/<name>.png (make-manifest.py
names each player shot after its editor frame) and runs colour_stats agree
on each: every visible region's mean Ycode within --tol codes (plan C0b: 2).
A region that disagrees means the editor is not trusted for that metric.
"""
import glob
import json
import os
import sys
import io
import contextlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import colour_stats as cs


def main(argv):
    tol = 2.0
    js = None
    if "--tol" in argv:
        i = argv.index("--tol"); tol = float(argv[i + 1]); argv = argv[:i] + argv[i + 2:]
    if "--json" in argv:
        i = argv.index("--json"); js = argv[i + 1]; argv = argv[:i] + argv[i + 2:]
    ed, pl = argv[1], argv[2]
    summary, allrows = [], {}
    for p in sorted(glob.glob(os.path.join(pl, "*.png"))):
        e = os.path.join(ed, os.path.basename(p))
        if not os.path.exists(e):
            print("no editor twin for", os.path.basename(p)); continue
        buf = io.StringIO()
        tmp = os.path.join(pl, os.path.basename(p) + ".agree.json")
        with contextlib.redirect_stdout(buf):
            ok = cs.cmd_agree(e, p, tmp, tol)
        print(buf.getvalue().rstrip())
        d = json.load(open(tmp))
        os.remove(tmp)
        allrows[os.path.basename(p)] = d
        worst = max([abs(r["delta"]) for r in d["rows"]] or [0])
        summary.append((os.path.basename(p), ok, worst, sum(1 for r in d["rows"] if r["ok"]), len(d["rows"])))
    print("\nSUMMARY")
    for n, ok, worst, good, tot in summary:
        print(f"  {'AGREE' if ok else 'DIFFER'} {n}: {good}/{tot} regions within {tol} codes, worst {worst:.1f}")
    if js:
        json.dump(allrows, open(js, "w"), indent=1)
    return 0 if summary and all(s[1] for s in summary) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
