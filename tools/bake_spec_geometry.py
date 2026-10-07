"""
Bake every car's SPEC GEOMETRY into Resources/rg2_cars.json, from the owner's
GT4 Specs.xlsx (decoded to tools/gt4/gt4_specs.json by tools/gt4/gt4_audit.py).

Why (2026-10-07). The owner: "The 3D models should match the wheelbases.
Everything should be designed to spec and scale." Until now the physics took
its wheelbase, track and tyre off the SHELL - and 317 cars wear 23 shells, so a
del Sol drove on a hatchback's 2581 mm wheelbase. CarModelLibrary.Fit now reads
these fields: the chassis is built to them and the shell is stretched to them.

Usage, from this directory:
    py bake_spec_geometry.py --check     # report, write nothing
    py bake_spec_geometry.py             # rewrite Resources/rg2_cars.json in place

Adds (or updates) "wheelbaseMm", "lengthMm", "tyreFDiaMm", "tyreRDiaMm",
"tyreFWidthMm" and "tyreRWidthMm" right after "trackRMm" in each car's block
(tools/bake_dims.py puts width and track there) and touches nothing else.
Idempotent. A blank sheet cell bakes as 0, and 0 means "keep the shell's own
value" to every reader.
"""
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
UNITY = os.path.join(HERE, "..", "Assets", "PSXRacing", "Resources", "rg2_cars.json")
SPECS = os.path.join(HERE, "gt4", "gt4_specs.json")

FIELDS = ("wheelbaseMm", "lengthMm", "tyreFDiaMm", "tyreRDiaMm", "tyreFWidthMm", "tyreRWidthMm")


def num(v):
    try:
        f = float(v)
    except (TypeError, ValueError):
        return 0
    return int(round(f)) if f > 0 else 0


def section(size):
    """'225/50 R16' -> 225. Blank or unreadable -> 0."""
    m = re.match(r"\s*(\d+(?:\.\d+)?)\s*/", str(size or ""))
    return int(round(float(m.group(1)))) if m else 0


def spec_geometry(row):
    return {
        "wheelbaseMm": num(row.get("Wheelbase")),
        "lengthMm": num(row.get("Length")),
        "tyreFDiaMm": num(row.get("Tyre Height (F)")),
        "tyreRDiaMm": num(row.get("Tyre Height (R)")),
        "tyreFWidthMm": section(row.get("Tyre Size (F)")),
        "tyreRWidthMm": section(row.get("Tyre Size (R)")),
    }


def main():
    check = "--check" in sys.argv
    sheet = json.load(io.open(SPECS, encoding="utf-8"))["cars"]
    text = io.open(UNITY, encoding="utf-8").read()
    cars = json.loads(text)["cars"]
    missing = [c["name"] for c in cars if c["name"] not in sheet]

    out, pos = [], 0
    id_re = re.compile(r'"id": "((?:[^"\\]|\\.)*)"')
    starts = [(m.start(), json.loads('"' + m.group(1) + '"')) for m in id_re.finditer(text)]
    blanks = {f: [] for f in FIELDS}
    for n, (start, cid) in enumerate(starts):
        end = starts[n + 1][0] if n + 1 < len(starts) else len(text)
        block = text[start:end]
        car = next(c for c in cars if c["id"] == cid)
        geo = spec_geometry(sheet.get(car["name"], {}))
        for f in FIELDS:
            if geo[f] <= 0:
                blanks[f].append(car["name"])
        block = re.sub(r'\s*"(' + "|".join(FIELDS) + r')": -?\d+,', "", block)
        km = re.search(r'("trackRMm": -?\d+,)(\s*)', block)
        if not km:
            raise SystemExit("no trackRMm in the block for " + cid + " (run bake_dims.py first)")
        ins = km.group(1) + km.group(2) + km.group(2).join('"%s": %d,' % (f, geo[f]) for f in FIELDS) + km.group(2)
        block = block[:km.start()] + ins + block[km.end():]
        out.append(text[pos:start])
        out.append(block)
        pos = end
    out.append(text[pos:])
    result = "".join(out)
    parsed = json.loads(result)["cars"]

    print("%d cars; %d not on the sheet: %s" % (len(parsed), len(missing), missing[:6]))
    for f in FIELDS:
        vals = sorted((c[f], c["name"]) for c in parsed if c[f] > 0)
        print("%-13s %4d baked, %3d blank %s  range %d (%s) .. %d (%s)" % (
            f, len(vals), len(blanks[f]), blanks[f][:3], vals[0][0], vals[0][1], vals[-1][0], vals[-1][1]))
    # The two fields bake_dims.py wrote must still agree with the sheet.
    off = [c["name"] for c in parsed if c["name"] in sheet and (
        num(sheet[c["name"]].get("Chassis Width")) not in (0, c["widthMm"]) or
        num(sheet[c["name"]].get("Chassis Tread (F)")) not in (0, c["trackFMm"]) or
        num(sheet[c["name"]].get("Chassis Tread (R)")) not in (0, c["trackRMm"]))]
    print("width/track disagreeing with the sheet: %d %s" % (len(off), off[:6]))
    if check:
        return
    io.open(UNITY, "w", encoding="utf-8", newline="").write(result)
    print("wrote", os.path.normpath(UNITY))


if __name__ == "__main__":
    main()
