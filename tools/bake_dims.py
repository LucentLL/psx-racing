"""
Bake every car's REAL WIDTH and TRACK into Resources/rg2_cars.json, from
GT4's own spec fields (RG2's gt4Database.ts: wid, trF, trR - millimetres).

Why (2026-09-27). The owner: "I want all cars to be realistic width." The
shells are shared - 317 catalog cars wear 23 models - and the models were
drawn chunky: most stand 10-18% wider than the car they are of (the 180SX
model is 2.00 m across a 1.69 m car). CarBody scales each car's shell ACROSS
to the width in its own catalog row (CarModelLibrary.WidthScale), so every
car on the grid is as wide as the real one.

Usage, from this directory:
    py bake_dims.py --check     # report, write nothing
    py bake_dims.py             # rewrite Resources/rg2_cars.json in place

Adds (or updates) "widthMm", "trackFMm" and "trackRMm" right after "kg" in
each car's block and touches nothing else. Idempotent. A car GT4 has no width
for gets 0, and CarModelLibrary falls back to its shell's own reference car.
"""
import io
import json
import re
import sys

UNITY = r"C:\Users\mcgee\PSX Racing\Assets\PSXRacing\Resources\rg2_cars.json"
RG2_DB = r"C:\Users\mcgee\code\Racing-Game-2\src\config\cars\gt4Database.ts"


def gt4_dims(path):
    src = io.open(path, encoding="utf-8").read()
    out = {}
    for m in re.finditer(r"^\s*'((?:[^'\\]|\\.)*)':\{([^\n]*)\}", src, re.M):
        body = m.group(2)
        def f(key):
            v = re.search(r"\b" + key + r":([\d.]+)", body)
            return int(round(float(v.group(1)))) if v else 0
        out[m.group(1).replace("\\'", "'")] = (f("wid"), f("trF"), f("trR"))
    return out


def main():
    check = "--check" in sys.argv
    dims = gt4_dims(RG2_DB)
    text = io.open(UNITY, encoding="utf-8").read()
    cars = json.loads(text)["cars"]
    missing = [c["name"] for c in cars if c["name"] not in dims or dims[c["name"]][0] <= 0]

    out, pos = [], 0
    id_re = re.compile(r'"id": "((?:[^"\\]|\\.)*)"')
    starts = [(m.start(), json.loads('"' + m.group(1) + '"')) for m in id_re.finditer(text)]
    for n, (start, cid) in enumerate(starts):
        end = starts[n + 1][0] if n + 1 < len(starts) else len(text)
        block = text[start:end]
        car = next(c for c in cars if c["id"] == cid)
        w, tf, tr = dims.get(car["name"], (0, 0, 0))
        block = re.sub(r'\s*"(widthMm|trackFMm|trackRMm)": -?\d+,', "", block)
        km = re.search(r'("kg": \d+,)(\s*)', block)
        if not km:
            raise SystemExit("no kg in the block for " + cid)
        ins = '%s%s"widthMm": %d,%s"trackFMm": %d,%s"trackRMm": %d,%s' % (
            km.group(1), km.group(2), w, km.group(2), tf, km.group(2), tr, km.group(2))
        block = block[:km.start()] + ins + block[km.end():]
        out.append(text[pos:start])
        out.append(block)
        pos = end
    out.append(text[pos:])
    result = "".join(out)
    parsed = json.loads(result)["cars"]

    ws = sorted((c["widthMm"], c["name"]) for c in parsed if c["widthMm"] > 0)
    print("%d cars; %d with a GT4 width, %d without: %s" % (len(parsed), len(ws), len(missing), missing[:6]))
    print("width %d .. %d mm (narrowest %s, widest %s)" % (ws[0][0], ws[-1][0], ws[0][1], ws[-1][1]))
    if check:
        return
    io.open(UNITY, "w", encoding="utf-8", newline="").write(result)
    print("wrote", UNITY)


if __name__ == "__main__":
    main()
