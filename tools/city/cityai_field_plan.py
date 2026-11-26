# cityai_field_plan.py - the junction turns of a race, OFF | ON, from two perf-tour field traces (&trace=1). Pure ASCII.
#
#   python -I tools\city\cityai_field_plan.py <off_field.csv> <on_field.csv> <out_dir> [--label "CltC7 seed 0"]
#
# Each field csv is <tag>_<stage>_field.csv; its _turns.csv, _edges.csv and _kerbs.csv are read from beside it.
# Writes <out_dir>\turn_<k>.png (plan 100 m, north up: pavement grey, fan kerbs white; rivals in colour, the player
# white, traffic grey; circles = stood still) and <out_dir>\field_table.txt:
#   per turn and arm: rival passes, tightest driven radius (min / median), apex speed (median), stops, pivots;
#   per arm: every car (traffic included) standing still more than 5 s within 30 m of a turn: count and longest.
# Measures (as the editor's turn sheet): the driven radius is speed / yaw rate (heading change per second, from the
# trace's heading), the smallest over a pass (3-sample median); a stop is speed under 1 m/s; a pivot is a yaw rate
# over 0.5 rad/s under 4 m/s. A pass is a car's samples within 45 m of a turn centre whose heading turns 25 deg+.
# Needs PIL. Works on exe output (no editor file is read).
import sys, os, csv, math
from collections import defaultdict
from PIL import Image, ImageDraw

R_TURN, R_STAND, STAND_S = 45.0, 30.0, 5.0

def side(path, suffix):
    return path[: -len("_field.csv")] + suffix

def rd(p):
    if not os.path.exists(p): return []
    with open(p, newline="") as f: return list(csv.DictReader(f))

def wrapd(a):
    while a > 180: a -= 360
    while a < -180: a += 360
    return a

def load(path):
    rows = rd(path)
    by = defaultdict(list)
    for r in rows:
        by[(r["id"], r["kind"])].append((float(r["t"]), float(r["x"]), float(r["z"]), float(r["heading"]), float(r["speed"])))
    turns = [dict(k=int(t["k"]), x=float(t["x"]), z=float(t["z"]), deg=float(t["deg"]), x0=float(t["planx0"]), z0=float(t["planz0"]), m=float(t["planm"]))
             for t in rd(side(path, "_turns.csv"))]
    E = defaultdict(list)
    for r in rd(side(path, "_edges.csv")):
        E[(int(r["turn"]), int(r["edge"]))].append((float(r["x"]), float(r["z"]), float(r["padL"]), float(r["padR"])))
    K = defaultdict(list)
    for r in rd(side(path, "_kerbs.csv")):
        K[int(r["turn"])].append((float(r["x0"]), float(r["z0"]), float(r["x1"]), float(r["z1"])))
    return by, turns, E, K

def passes(by, tn):
    out = []
    for (cid, kind), s in by.items():
        cur = []
        def flush(cur):
            if len(cur) < 5: return
            tot = sum(wrapd(cur[i + 1][3] - cur[i][3]) for i in range(len(cur) - 1))
            if abs(tot) < 25: return
            rad, piv = [], False
            for i in range(1, len(cur) - 1):
                vals = []
                for j in (i - 1, i, i + 1):
                    a, b = cur[max(j - 1, 0)], cur[min(j + 1, len(cur) - 1)]
                    dt = b[0] - a[0]
                    yaw = abs(math.radians(wrapd(b[3] - a[3]))) / dt if dt > 1e-3 else 0.0
                    vals.append((cur[j][4], yaw))
                v, yaw = cur[i][4], abs(math.radians(wrapd(cur[i + 1][3] - cur[i - 1][3]))) / max(cur[i + 1][0] - cur[i - 1][0], 1e-3)
                if v > 1.5 and yaw > 0.02:
                    rr = sorted(vv / max(yy, 1e-3) for vv, yy in vals if vv > 1.5)
                    if rr: rad.append(rr[len(rr) // 2])
                if yaw > 0.5 and v < 4.0: piv = True
            near = min(cur, key=lambda p: (p[1] - tn["x"]) ** 2 + (p[2] - tn["z"]) ** 2)
            out.append(dict(id=cid, kind=kind, pts=cur, deg=tot, rmin=min(rad) if rad else float("nan"), vapex=near[4] * 3.6,
                            stop=min(p[4] for p in cur) < 1.0, pivot=piv))
        for p in s:
            if (p[1] - tn["x"]) ** 2 + (p[2] - tn["z"]) ** 2 <= R_TURN * R_TURN:
                if cur and p[0] - cur[-1][0] > 2.0: flush(cur); cur = []
                cur.append(p)
        flush(cur)
    return out

def stands(by, turns):
    res = []
    for (cid, kind), s in by.items():
        run0 = None
        for i, p in enumerate(s):
            inj = any((p[1] - t["x"]) ** 2 + (p[2] - t["z"]) ** 2 <= R_STAND * R_STAND for t in turns)
            still = p[4] < 0.5 and inj
            if still and run0 is None: run0 = p
            if (not still or i == len(s) - 1) and run0 is not None:
                d = (p[0] if not still else p[0]) - run0[0]
                if d > STAND_S: res.append((cid, kind, run0[0], d, run0[1], run0[2]))
                run0 = None
    return res

def med(xs):
    xs = sorted(x for x in xs if x == x)
    return xs[len(xs) // 2] if xs else float("nan")

def main():
    off, on, outd = sys.argv[1], sys.argv[2], sys.argv[3]
    label = sys.argv[sys.argv.index("--label") + 1] if "--label" in sys.argv else ""
    os.makedirs(outd, exist_ok=True)
    arms = [("OFF", load(off)), ("ON", load(on))]
    turns = arms[1][1][1] or arms[0][1][1]
    lines = [f"field plan {label}: off {os.path.basename(off)} | on {os.path.basename(on)}",
             "turn deg | arm | rival passes | r_min min / med m | apex med km/h | stops | pivots"]
    cols = [(255, 60, 60), (60, 200, 255), (255, 220, 40), (120, 255, 120), (255, 120, 255), (255, 160, 60), (160, 120, 255), (90, 255, 220)]
    for tn in turns:
        W = 640
        sheet = Image.new("RGB", (2 * W + 30, W + 40), (12, 12, 12))
        for ai, (arm, (by, _t, E, K)) in enumerate(arms):
            im = Image.new("RGB", (W, W), (34, 52, 30)); dr = ImageDraw.Draw(im)
            def px(x, z): return ((x - tn["x0"]) / tn["m"] * W, (1 - (z - tn["z0"]) / tn["m"]) * W)
            for (k, e), pts in E.items():
                if k != tn["k"]: continue
                for i in range(len(pts) - 1):
                    x0, z0, l0, r0 = pts[i]; x1, z1, l1, r1 = pts[i + 1]
                    dx, dz = x1 - x0, z1 - z0; L = math.hypot(dx, dz)
                    if L < 1e-3: continue
                    rx, rz = dz / L, -dx / L
                    dr.polygon([px(x0 + rx * r0, z0 + rz * r0), px(x1 + rx * r1, z1 + rz * r1), px(x1 - rx * l1, z1 - rz * l1), px(x0 - rx * l0, z0 - rz * l0)], fill=(78, 78, 82))
            for (x0, z0, x1, z1) in K[tn["k"]]:
                dr.line([px(x0, z0), px(x1, z1)], fill=(240, 240, 240), width=2)
            ps = passes(by, tn)
            rivals = sorted({p["id"] for p in ps if p["kind"] == "rival"})
            for p in ps:
                c = (150, 150, 150) if p["kind"] == "traffic" else (255, 255, 255) if p["kind"] == "player" else cols[rivals.index(p["id"]) % len(cols)]
                dr.line([px(q[1], q[2]) for q in p["pts"]], fill=c, width=2)
                for q in p["pts"]:
                    if q[4] < 1.0: x, y = px(q[1], q[2]); dr.ellipse([x - 4, y - 4, x + 4, y + 4], outline=c, width=2)
            rp = [p for p in ps if p["kind"] == "rival"]
            rm = [p["rmin"] for p in rp]
            txt = (f"T{tn['k']} {tn['deg']:.0f} | {arm} | {len(rp)} | {min(rm) if rm else float('nan'):.1f} / {med(rm):.1f} | {med([p['vapex'] for p in rp]):.0f} | "
                   f"{sum(p['stop'] for p in rp)} | {sum(p['pivot'] for p in rp)}")
            lines.append(txt)
            dr.text((6, 6), f"T{tn['k']} {tn['deg']:.0f} deg  {arm}", fill=(255, 255, 255))
            sheet.paste(im, (10 + ai * (W + 10), 30))
        ImageDraw.Draw(sheet).text((10, 8), f"{label}  T{tn['k']}: OFF | ON (rivals colour, player white, traffic grey; circles = stood still)", fill=(255, 255, 255))
        sheet.save(os.path.join(outd, f"turn_{tn['k']}.png"))
    for arm, (by, t, E, K) in arms:
        st = stands(by, turns)
        longest = max((s[3] for s in st), default=0.0)
        lines.append(f"{arm}: cars standing still over {STAND_S:.0f} s within {R_STAND:.0f} m of a turn: {len(st)}, longest {longest:.1f} s" +
                     "".join(f"\n    {s[1]} {s[0]} from t {s[2]:.1f} for {s[3]:.1f} s at ({s[4]:.0f},{s[5]:.0f})" for s in sorted(st, key=lambda q: -q[3])[:8]))
    txt = "\n".join(lines)
    open(os.path.join(outd, "field_table.txt"), "w").write(txt + "\n")
    print(txt)

if __name__ == "__main__":
    main()
