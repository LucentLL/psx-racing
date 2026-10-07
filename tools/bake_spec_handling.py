"""
Bake every car's SPEC HANDLING into Resources/rg2_cars.json, from the owner's
GT4 Specs.xlsx (decoded to tools/gt4/gt4_specs.json by tools/gt4/gt4_audit.py).
Owner, 2026-10-07: "I want everything to spec." The sheet is the basis of all
stock car values; bake_spec_geometry.py did the dimensions, this does the
handling columns the game used to ignore:

  wdFront            "Weight Distribution" F : R -> % on the front axle
  springF / springR  Suspension "Stock Springs" F / R, kgf/mm
  rideFMm / rideRMm  Suspension "Stock Height" F / R, mm
  gripF / gripR      "Grip Modifier (F)" / "(R)"
  revLimit           Engine "Rev Limiter", rpm (never below the redline)
  lsdInit/Accel/Decel  the factory LSD on the DRIVEN axle (0 = open diff)
  peakTorqueNm       re-solved on the PS basis: the sheet's "Peak Power (ps)"
                     is METRIC horsepower, 735.5 W, not 745.7 W

WHICH STOCK VALUE. For a road car the sheet's Stock Min == Max (267 of 317
springs): the part is not adjustable and that IS the car. The other 50 are
race and rally cars whose stock suspension is a fully adjustable coilover
(4.0-20.0 / 3.0-20.0 kgf/mm; one R390 road car 10.3/8.6-13.4/11.1). GT4 lists
every part the same way - Sports, Semi-Racing and Custom all carry a Min/Max
range, and the road car's Stock is the degenerate range - so the range is the
part's adjustment, not two cars. The setting is not on the sheet; the centre
of the range is the defensible stock setting (the game's own spring slider
then spans 0.70x..1.45x around it). Heights the same way.

LSD. A cell written plain ("0 / 5") is a fitted differential; a cell written
in brackets ("(0 / 0)", or "5'(0 / 0)" on the FF Hondas) is GT4's "not
equipped" - those Civics and CR-Xs had open diffs. The game has one diff
model per driven axle, so a 4WD takes its rear axle's figures (the front's
when the rear has none).

    py bake_spec_handling.py --check   # report, write nothing
    py bake_spec_handling.py           # rewrite Resources/rg2_cars.json in place

Idempotent. Run tools/bake_topspeed.py after it (the power basis moves the
top speeds by about -0.5%).
"""
import io
import json
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
UNITY = os.path.join(HERE, "..", "Assets", "PSXRacing", "Resources", "rg2_cars.json")
SPECS = os.path.join(HERE, "gt4", "gt4_specs.json")
PS_W = 735.49875     # one metric horsepower, W
FIELDS_I = ("wdFront", "rideFMm", "rideRMm", "gripF", "gripR", "revLimit", "lsdInit", "lsdAccel", "lsdDecel")
FIELDS_F = ("springF", "springR")
ANCHOR = "tyreRWidthMm"


def pair(s):
    """'63 : 37' / '4.8 / 3.6' -> (63.0, 37.0); None when unreadable."""
    m = re.match(r"\s*([\d.]+)\s*[/:]\s*([\d.]+)\s*$", str(s or ""))
    return (float(m.group(1)), float(m.group(2))) if m else None


def stock(smin, smax):
    a, b = pair(smin), pair(smax)
    if a is None:
        return b
    if b is None:
        return a
    return ((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0)


def lsd_axle(cell, axle):
    """A plain 'F / R' cell's value on one axle; bracketed = not fitted."""
    s = str(cell or "").strip()
    if "(" in s or "'" in s:
        return 0
    p = pair(s)
    return int(round(p[axle])) if p else 0


def factory_lsd(row, drv):
    cols = ("LSD Initial (F / R)", "LSD Accel (F / R)", "LSD Decel (F / R)")
    if drv == "FF":
        return [lsd_axle(row.get(c), 0) for c in cols]
    rear = [lsd_axle(row.get(c), 1) for c in cols]
    if drv == "4WD" and not any(rear):
        return [lsd_axle(row.get(c), 0) for c in cols]
    return rear


def torque_at(rpms, nms, rpm):
    if rpm <= rpms[0]:
        return nms[0] * max(0.0, rpm / rpms[0])
    for i in range(len(rpms) - 1):
        if rpm <= rpms[i + 1]:
            return nms[i] + (nms[i + 1] - nms[i]) * (rpm - rpms[i]) / (rpms[i + 1] - rpms[i])
    return nms[-1]


def peak_power_w(car, peak_nm):
    """The curve's peak power over its SAMPLE points - the basis RG2 solved
    the torque on (every car's sample peak was hp x 745.7 W to 0.35%), and the
    one CarSpec.Decode normalises. Not the idle..redline window: six engines
    (the Chaparral 2D, the GALANT GTO MR...) peak past their redline, and a
    window basis would hand them up to 9% more torque than the sheet's curve."""
    rpms = [float(x) for x in car["tcRPMs"].split(";")]
    nms = [float(x) * peak_nm for x in car["tcNorm"].split(";")]
    return max(r * n * 2.0 * math.pi / 60.0 for r, n in zip(rpms, nms))


def handling(car, sheet):
    row, sus, eng = sheet["cars"][car["name"]], sheet["suspension"][car["name"]], sheet["engine"][car["name"]]
    wd = pair(row.get("Weight Distribution"))
    spr = stock(sus.get("Stock Springs Min"), sus.get("Stock Springs Max"))
    hgt = stock(sus.get("Stock Height Min"), sus.get("Stock Height Max"))
    rev = int(eng.get("Rev Limiter") or 0)
    lsd = factory_lsd(row, car["drv"])
    out = {
        "wdFront": int(round(100.0 * wd[0] / (wd[0] + wd[1]))) if wd else 0,
        "springF": round(spr[0], 2) if spr else 0.0,
        "springR": round(spr[1], 2) if spr else 0.0,
        "rideFMm": int(round(hgt[0])) if hgt else 0,
        "rideRMm": int(round(hgt[1])) if hgt else 0,
        "gripF": int(row.get("Grip Modifier (F)") or 0),
        "gripR": int(row.get("Grip Modifier (R)") or 0),
        "revLimit": max(rev, int(car["redline"])) if rev > 0 else 0,
        "lsdInit": lsd[0], "lsdAccel": lsd[1], "lsdDecel": lsd[2],
    }
    # Torque on the PS basis: scale the stored peak so the curve's peak power
    # is hp x 735.5 W (CarSpec.Decode then removes the last half-Nm of
    # rounding). Idempotent: a second run lands on the same int.
    peak = peak_power_w(car, car["peakTorqueNm"])
    out["peakTorqueNm"] = int(round(car["peakTorqueNm"] * car["hp"] * PS_W / peak)) if peak > 1 else car["peakTorqueNm"]
    return out, rev


def main():
    check = "--check" in sys.argv
    sheet = json.load(io.open(SPECS, encoding="utf-8"))
    text = io.open(UNITY, encoding="utf-8").read()
    cars = json.loads(text)["cars"]
    missing = [c["name"] for c in cars if c["name"] not in sheet["cars"] or c["name"] not in sheet["suspension"]
               or c["name"] not in sheet["engine"]]
    if missing:
        raise SystemExit("not on the sheet: %s" % missing[:6])

    out, pos, low_lim = [], 0, []
    id_re = re.compile(r'"id": "((?:[^"\\]|\\.)*)"')
    starts = [(m.start(), json.loads('"' + m.group(1) + '"')) for m in id_re.finditer(text)]
    baked = {}
    for n, (start, cid) in enumerate(starts):
        end = starts[n + 1][0] if n + 1 < len(starts) else len(text)
        block = text[start:end]
        car = next(c for c in cars if c["id"] == cid)
        h, rev = handling(car, sheet)
        if 0 < rev < car["redline"]:
            low_lim.append((car["name"], car["redline"], rev))
        baked[cid] = h
        block = re.sub(r'\s*"(' + "|".join(FIELDS_I + FIELDS_F) + r')": -?[\d.]+,', "", block)
        block, k = re.subn(r'"peakTorqueNm": \d+', '"peakTorqueNm": %d' % h["peakTorqueNm"], block, count=1)
        km = re.search(r'("' + ANCHOR + r'": -?\d+,)(\s*)', block)
        if not km or k != 1:
            raise SystemExit("no %s / peakTorqueNm in the block for %s (run bake_spec_geometry.py first)" % (ANCHOR, cid))
        items = ['"%s": %d,' % (f, h[f]) for f in FIELDS_I[:1]] + \
                ['"%s": %s,' % (f, repr(float(h[f]))) for f in FIELDS_F] + \
                ['"%s": %d,' % (f, h[f]) for f in FIELDS_I[1:]]
        ins = km.group(1) + km.group(2) + km.group(2).join(items) + km.group(2)
        block = block[:km.start()] + ins + block[km.end():]
        out.append(text[pos:start])
        out.append(block)
        pos = end
    out.append(text[pos:])
    result = "".join(out)
    parsed = json.loads(result)["cars"]

    wd = sorted((c["wdFront"], c["name"]) for c in parsed)
    print("%d cars. front weight %d%% (%s) .. %d%% (%s); %d at 60%%+" % (
        len(parsed), wd[0][0], wd[0][1], wd[-1][0], wd[-1][1], sum(1 for x in wd if x[0] >= 60)))
    sp = sorted((c["springF"], c["springR"], c["name"]) for c in parsed)
    print("springs F %.1f .. %.1f kgf/mm" % (sp[0][0], sp[-1][0]))
    print("factory LSD on the driven axle: %d of %d" % (
        sum(1 for c in parsed if c["lsdInit"] or c["lsdAccel"] or c["lsdDecel"]), len(parsed)))
    print("rev limiter below the redline on the sheet (held at the redline): %s" % low_lim)
    tq = [(c["name"], next(o for o in cars if o["id"] == c["id"])["peakTorqueNm"], c["peakTorqueNm"]) for c in parsed]
    print("peak torque re-solved on PS: %d changed, e.g. %s" % (sum(1 for t in tq if t[1] != t[2]), tq[:2]))
    if check:
        return
    io.open(UNITY, "w", encoding="utf-8", newline="").write(result)
    print("wrote", os.path.normpath(UNITY))


if __name__ == "__main__":
    main()
