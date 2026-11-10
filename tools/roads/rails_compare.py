# Compare two roadside plan csvs (StageLab.Plan) and score them against the surveyed real road.
import csv, sys, math
S = r'C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\60efacf0-02f8-4d62-a240-63ce8066e70c\scratchpad\lidar'
def load(p):
    return list(csv.DictReader(open(p)))
b = load(sys.argv[1]); a = load(sys.argv[2])
def walled(rows, side): return [r['kind' + side] == 'Walled' for r in rows]
for name, rows in (('before', b), ('after', a)):
    for side in 'LR':
        w = walled(rows, side); c = [r['kind' + side] == 'Cut' for r in rows]
        print(f"{name} {side}: walled {sum(w) * 4} m, cut {sum(c) * 4} m, open {sum(1 for r in rows if r['kind' + side] == 'Open') * 4} m")
# real road, by AFTER station (the 3DEP line): side, station, rail?
truth = [('L', 264, 1), ('L', 300, 1), ('L', 1964, 0), ('L', 3604, 0), ('L', 3976, 0), ('R', 4428, 0), ('R', 4788, 0), ('R', 4920, 0), ('R', 5744, 0),
         ('R', 6536, 0), ('R', 8032, 0), ('R', 8592, 0), ('R', 9372, 0), ('R', 9464, 0), ('R', 10588, 0), ('R', 10216, 0), ('R', 6900, 0), ('R', 8416, 0)]
def near_old(i):
    x, z = float(a[i]['x']), float(a[i]['z'])
    lo, hi = max(0, i - 15), min(len(b), i + 15)
    return min(range(lo, hi), key=lambda k: (float(b[k]['x']) - x) ** 2 + (float(b[k]['z']) - z) ** 2)
okb = oka = 0
print('side station  real  before  after')
for side, m, rail in truth:
    i = m // 4; k = near_old(i)
    # a rail within +-12 m counts (a view shows 30 m of road)
    wb = any(b[j]['kind' + side] == 'Walled' for j in range(max(0, k - 3), min(len(b), k + 4)))
    wa = any(a[j]['kind' + side] == 'Walled' for j in range(max(0, i - 3), min(len(a), i + 4)))
    okb += (wb == bool(rail)); oka += (wa == bool(rail))
    print(f"  {side} {m:>6}   {'rail' if rail else 'none'}   {'rail' if wb else 'none'}{'  ' if wb == bool(rail) else ' X'}   {'rail' if wa else 'none'}{'  ' if wa == bool(rail) else ' X'}   ({a[i]['kind' + side]}, why {a[i]['why' + side]})")
print(f'right against the real road: before {okb} of {len(truth)}, after {oka} of {len(truth)}')
