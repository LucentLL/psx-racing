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
  raceSusp           1 = a race / rally car's adjustable stock suspension
  springMinF..MaxR   on a raceSusp car only: the sheet's Stock Springs Min..Max
                     F / R, kgf/mm - the spring slider's fence (CarSetupRanges)
  rideMinMm/MaxMm    on a raceSusp car only: Stock Height Min..Max, mm (mean of
                     F and R) - the ride-height slider's fence
  peakTorqueNm       re-solved on the PS basis: the sheet's "Peak Power (ps)"
                     is METRIC horsepower, 735.5 W, not 745.7 W

WHICH STOCK VALUE. For a road car the sheet's Stock Min == Max (267 of 317
springs): the part is not adjustable and that IS the car. The other 50 are
race and rally cars whose stock suspension is a fully adjustable coilover
(4.0-20.0 / 3.0-20.0 kgf/mm; one R390 road car 10.3/8.6-13.4/11.1). GT4 lists
every part the same way - Sports, Semi-Racing and Custom all carry a Min/Max
range, and the road car's Stock is the degenerate range - so the range is the
part's adjustment, not two cars. The setting is not on the sheet.

RANGED SPRINGS (owner, 2026-10-07: "Start them all with most realistic and/or
setting in the middle of high/low values so they can be adjust equally in
either direction"). A race or rally car's default spring is the rate that
gives a realistic TARMAC ride frequency (all racing here is on tarmac):
RACE_HZ 3.2 for a circuit race car, RALLY_HZ 2.7 for a rally car on a tarmac
setup, per axle, from the car's weight and the sheet's weight split:
    k = (2 pi f)^2 x m_corner / MR^2,   m_corner = kg x axle share / 2
The game hangs the whole car on its four ray springs (no unsprung mass is
simulated) and mounts the rate AT the wheel (CarController's spring is a
wheel rate), so the sprung corner mass is the full corner mass and the
motion ratio MR is 1.0: the frequency named is the one the car rides at in
the game. Clamped inside the sheet's range; a default in the outer quarter of
its range is reported. The game's spring slider then spans 0.70x..1.45x
around it. A ranged ROAD car (the R390 GT1 Road Car) keeps the centre of its
range, and so does every ranged HEIGHT (no clear realistic value).

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
FIELDS_I = ("wdFront", "rideFMm", "rideRMm", "gripF", "gripR", "revLimit", "lsdInit", "lsdAccel", "lsdDecel",
            "raceSusp")
FIELDS_F = ("springF", "springR")
# Written only on a raceSusp car (the adjustable stock coilover's range).
FIELDS_RANGE_F = ("springMinF", "springMaxF", "springMinR", "springMaxR")
FIELDS_RANGE_I = ("rideMinMm", "rideMaxMm")
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


RACE_HZ, RALLY_HZ = 3.2, 2.7   # tarmac ride frequency, Hz (see RANGED SPRINGS)
MOTION_RATIO = 1.0             # the game's spring acts at the wheel
KGF_PER_MM = 9806.65           # N/m
RANGED = []                    # (name, lo, hi, default, hz) for the report


def ride_hz_of(name):
    """A ranged (adjustable) stock suspension that is not a road car's: a
    rally or dirt car rides at RALLY_HZ, every circuit car (Race Car, Touring
    Car, JGTC, the 155 TI) at RACE_HZ."""
    if "Road Car" in name:
        return None
    if "Rally" in name or "Dirt" in name:
        return RALLY_HZ
    return RACE_HZ


def ride_rate(kg, share, hz):
    """kgf/mm for this corner to ride at hz."""
    m = kg * share / 2.0
    return (2.0 * math.pi * hz) ** 2 * m / (MOTION_RATIO ** 2) / KGF_PER_MM


def springs(car, sus, wd):
    lo, hi = pair(sus.get("Stock Springs Min")), pair(sus.get("Stock Springs Max"))
    spr = stock(sus.get("Stock Springs Min"), sus.get("Stock Springs Max"))
    if not (lo and hi) or lo == hi:
        return spr
    hz = ride_hz_of(car["name"])
    if hz is not None and wd:
        sf = wd[0] / (wd[0] + wd[1])
        spr = tuple(min(max(ride_rate(car["kg"], sh, hz), lo[i]), hi[i])
                    for i, sh in enumerate((sf, 1.0 - sf)))
    RANGED.append((car["name"], lo, hi, spr, hz))
    return spr


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
    spr = springs(car, sus, wd)
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
        # 1 = the factory suspension is an adjustable race / rally coilover
        # (a ranged Stock Springs on a car that is not a road car): the car
        # is BUILT TO BE TUNED (CarSpec.IsBuiltToTune).
        "raceSusp": 1 if (RANGED and RANGED[-1][0] == car["name"] and RANGED[-1][4]) else 0,
    }
    if out["raceSusp"]:
        lo, hi = pair(sus.get("Stock Springs Min")), pair(sus.get("Stock Springs Max"))
        hlo, hhi = pair(sus.get("Stock Height Min")), pair(sus.get("Stock Height Max"))
        out.update({"springMinF": lo[0], "springMaxF": hi[0], "springMinR": lo[1], "springMaxR": hi[1],
                    "rideMinMm": int(round((hlo[0] + hlo[1]) / 2.0)) if hlo else 0,
                    "rideMaxMm": int(round((hhi[0] + hhi[1]) / 2.0)) if hhi else 0})
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
        block = re.sub(r'\s*"(' + "|".join(FIELDS_I + FIELDS_F + FIELDS_RANGE_F + FIELDS_RANGE_I) + r')": -?[\d.]+,',
                       "", block)
        block, k = re.subn(r'"peakTorqueNm": \d+', '"peakTorqueNm": %d' % h["peakTorqueNm"], block, count=1)
        km = re.search(r'("' + ANCHOR + r'": -?\d+,)(\s*)', block)
        if not km or k != 1:
            raise SystemExit("no %s / peakTorqueNm in the block for %s (run bake_spec_geometry.py first)" % (ANCHOR, cid))
        items = ['"%s": %d,' % (f, h[f]) for f in FIELDS_I[:1]] + \
                ['"%s": %s,' % (f, repr(float(h[f]))) for f in FIELDS_F] + \
                ['"%s": %d,' % (f, h[f]) for f in FIELDS_I[1:]]
        if h["raceSusp"]:
            items += ['"%s": %s,' % (f, repr(float(h[f]))) for f in FIELDS_RANGE_F] +                      ['"%s": %d,' % (f, h[f]) for f in FIELDS_RANGE_I]
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
    print("ranged springs (car | range F, R | default F / R kgf/mm | Hz | outer quarter):")
    for name, lo, hi, d, hz in RANGED:
        outer = [ax for i, ax in enumerate("FR")
                 if not (lo[i] + 0.25 * (hi[i] - lo[i]) <= d[i] <= hi[i] - 0.25 * (hi[i] - lo[i]))]
        print("  %s | %.1f-%.1f, %.1f-%.1f | %.2f / %.2f | %s | %s" % (
            name, lo[0], hi[0], lo[1], hi[1], d[0], d[1], "%.1f" % hz if hz else "centre (road car)",
            "OUTER " + "".join(outer) if outer else "-"))
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
