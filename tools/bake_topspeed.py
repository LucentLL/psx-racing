"""
Bake every car's STOCK top speed into Resources/rg2_cars.json, from GT4's own
spec fields.

Why this exists (2026-09-21). The owner: "Something is very wrong with top
speeds. One car has over 300MPH." Two faults, one inherited and one ours:

  * Neither GT4's spec sheet (GT4 Specs.xlsx, the '- 99' / '00 - ' tabs) nor
    RG2's GT4_DB/GT4_SPECS carries a top speed. RG2 MADE one up:
    topKmh = min(300 or 340, (110 + hp*0.48) * dragFactor) - linear in power,
    with a flat wall that put a 469 hp RUF, a 456 hp Viper and a 485 hp Cobra
    on the same 300 km/h.
  * Our original catalog bake then turned RG2's world-pixel speed into metres
    per second with the wrong one of RG2's two scales (6.2746 wpx/m where the
    number had been built with 4.864): every car in PSX Racing was EXACTLY
    1.290x RG2's own figure. Checked on all 317 - not one car differed.

So this replaces the number with the one GT4's physics would produce from the
fields GT4 DOES have: the engine's own torque curve (peak power between idle
and redline - the rev range the gearbox can put at top speed), GT4's 'Wind
Drag' as a drag coefficient x100, the chassis width for frontal area, and the
mass for rolling resistance. No fudge factor. Against 14 cars in the catalog
whose real top speeds are well documented it lands within 6% on average
(worst +14%, the 427 Cobra), where RG2's formula ran 13% slow and ours 29%
fast.

Upgrades are NOT in this number - it is the STOCK figure. What a build adds is
a percentage on top of it, applied at runtime by CarTune.TopSpeedMult.

Usage, from this directory:
    py bake_topspeed.py --check     # report, write nothing
    py bake_topspeed.py             # rewrite Resources/rg2_cars.json in place

Reads RG2's gt4Database.ts for the wind drag and width (they are not in the
Unity catalog). Idempotent: run it twice and the second run changes nothing.
It edits only topSpeedMps and gearSpeeds, and keeps the file's 1-space JSON
formatting so the diff is only the fields it owns.
"""
import io
import json
import math
import re
import sys

UNITY = r"C:\Users\mcgee\PSX Racing\Assets\PSXRacing\Resources\rg2_cars.json"
RG2_DB = r"C:\Users\mcgee\code\Racing-Game-2\src\config\cars\gt4Database.ts"

RHO = 1.225          # air, kg/m^3
G = 9.81
CRR = 0.013          # rolling-resistance coefficient on tarmac
ETA = 0.88           # crank-to-wheel efficiency: CarController.DefaultDrivetrainEfficiency
BODY_HEIGHT = 1.30   # m - GT4 lists width and length, not height
AREA_FILL = 0.84     # frontal area as a share of width x height
DEFAULT_WDRAG, DEFAULT_WIDTH = 35.0, 1700.0   # RG2's own fallbacks


def gt4_fields(path):
    src = io.open(path, encoding="utf-8").read()
    out = {}
    for m in re.finditer(r"^\s*'((?:[^'\\]|\\.)*)':\{([^\n]*)\}", src, re.M):
        body = m.group(2)
        w = re.search(r"\bwDrag:([\d.]+)", body)
        wid = re.search(r"\bwid:([\d.]+)", body)
        out[m.group(1).replace("\\'", "'")] = (
            float(w.group(1)) if w else DEFAULT_WDRAG,
            float(wid.group(1)) if wid else DEFAULT_WIDTH)
    return out


def torque_at(rpms, nms, rpm):
    """CarController.RawTorqueAtRPM's walk: ramp from zero under the first
    sample, linear between samples, flat past the last."""
    if rpm <= rpms[0]:
        return nms[0] * max(0.0, rpm / rpms[0])
    for i in range(len(rpms) - 1):
        if rpm <= rpms[i + 1]:
            return nms[i] + (nms[i + 1] - nms[i]) * (rpm - rpms[i]) / (rpms[i + 1] - rpms[i])
    return nms[-1]


def peak_power_w(car):
    """Most power the engine makes between idle and redline - the same window
    CarSpec.PeakPowerRPM searches, because that is where the gearbox puts the
    engine at top speed."""
    rpms = [float(x) for x in car["tcRPMs"].split(";")]
    nms = [float(x) * car["peakTorqueNm"] for x in car["tcNorm"].split(";")]
    best = 0.0
    rpm = max(float(car["idleRPM"]), 500.0)
    while rpm <= car["redline"] + 1e-6:
        best = max(best, torque_at(rpms, nms, rpm) * rpm * 2.0 * math.pi / 60.0)
        rpm += 25.0
    return best


def top_speed_mps(power_w, wdrag, width_mm, kg):
    """Where the power at the wheels meets aero drag plus rolling resistance."""
    cd = wdrag / 100.0
    area = width_mm / 1000.0 * BODY_HEIGHT * AREA_FILL
    lo, hi = 1.0, 200.0
    for _ in range(80):
        v = (lo + hi) * 0.5
        need = (0.5 * RHO * cd * area * v * v + CRR * kg * G) * v
        if need < power_w * ETA:
            lo = v
        else:
            hi = v
    return lo


def main():
    check = "--check" in sys.argv
    gt4 = gt4_fields(RG2_DB)
    text = io.open(UNITY, encoding="utf-8").read()
    cars = json.loads(text)["cars"]

    new_top = {}
    missing = []
    for c in cars:
        fields = gt4.get(c["name"])
        if fields is None:
            missing.append(c["name"])
            fields = (DEFAULT_WDRAG, DEFAULT_WIDTH)
        new_top[c["id"]] = round(top_speed_mps(peak_power_w(c), fields[0], fields[1], c["kg"]), 4)

    # Rewrite in place, car by car, touching only the two fields.
    out, pos, changed = [], 0, 0
    id_re = re.compile(r'"id": "((?:[^"\\]|\\.)*)"')
    starts =[(m.start(), json.loads('"' + m.group(1) + '"')) for m in id_re.finditer(text)]
    for n, (start, cid) in enumerate(starts):
        end = starts[n + 1][0] if n + 1 < len(starts) else len(text)
        block = text[start:end]
        car = next(c for c in cars if c["id"] == cid)
        old = car["topSpeedMps"]
        v = new_top[cid]
        if abs(v - old) > 1e-6:
            changed += 1
        scale = v / old if old > 1e-6 else 1.0
        block, k1 = re.subn(r'"topSpeedMps": [-0-9.eE+]+', '"topSpeedMps": %s' % repr(v), block, count=1)
        gs = [float(x) for x in car["gearSpeeds"].split(";")] if car.get("gearSpeeds") else []
        if gs:
            new_gs = ";".join("%.2f" % (x * scale) for x in gs)
            block, k2 = re.subn(r'"gearSpeeds": "[^"]*"', '"gearSpeeds": "%s"' % new_gs, block, count=1)
        if k1 != 1:
            raise SystemExit("no topSpeedMps in the block for " + cid)
        out.append(text[pos:start])
        out.append(block)
        pos = end
    out.append(text[pos:])
    result = "".join(out)

    kmh = sorted((v * 3.6, cid) for cid, v in new_top.items())
    print("%d cars, %d without GT4 wind drag/width (RG2's fallbacks used): %s" %
          (len(cars), len(missing), missing[:5]))
    print("stock top speed: %.0f .. %.0f km/h (%.0f .. %.0f mph); %d cars change" %
          (kmh[0][0], kmh[-1][0], kmh[0][0] / 1.609344, kmh[-1][0] / 1.609344, changed))
    if check:
        for c in sorted(cars, key=lambda c: -new_top[c["id"]])[:8]:
            print("   %-46s %4.0f -> %4.0f km/h" % (c["name"][:46], c["topSpeedMps"] * 3.6, new_top[c["id"]] * 3.6))
        return
    json.loads(result)   # still valid JSON, or nothing is written
    io.open(UNITY, "w", encoding="utf-8", newline="").write(result)
    print("wrote", UNITY)


if __name__ == "__main__":
    main()
