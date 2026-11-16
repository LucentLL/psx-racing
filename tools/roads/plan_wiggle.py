"""plan_wiggle.py - waves in a stage's plan line (owner, 2026-11-16: "an otherwise
straight road will wave back and forth abruptly in a way that DOT would never approve").

    python tools/roads/plan_wiggle.py Assets/PSXRacing/Resources/<pre>_stage.json [--from M --to M]

Two numbers per stage, over the timed section (start line to finish):

1. WIGGLE. Each waypoint against a cubic fitted (in its own chord frame) to
   the 40 m around it - a cubic follows a straight, an arc and a spiral
   transition, but not a sideways jog and back - reported as the worst per
   100 m stretch. A clean line stays within a few tenths of a metre at its
   tightest bends; an S laid into a straight (a line moved 2 m over 24 m)
   leaves a metre or more.

2. ABRUPT REVERSALS. Signed curvature from the circle through the waypoints
   12 m either side; a reversal is a bend under 250 m radius one way and the
   other, their peaks under 40 m apart, with the line under 3 m off the chord
   between them: a jog, not one of the road's own S-bends.
"""
import json, math, sys

args = sys.argv[1:]
path = args[0]
st = json.load(open(path))
xyz = st['xyz']; n = len(xyz) // 3; SP = st.get('spacing', 4)
X = [xyz[3 * i] for i in range(n)]; Z = [xyz[3 * i + 2] for i in range(n)]
def opt(name, d):
    return float(args[args.index(name) + 1]) if name in args else d
s0 = opt('--from', st.get('startLineM', 0)); s1 = opt('--to', st.get('finishM', (n - 1) * SP))
i0 = max(3, int(s0 / SP)); i1 = min(n - 4, int(s1 / SP))

def solve(A, b):
    m = len(b); M = [row[:] + [b[k]] for k, row in enumerate(A)]
    for c in range(m):
        p = max(range(c, m), key=lambda r: abs(M[r][c])); M[c], M[p] = M[p], M[c]
        if abs(M[c][c]) < 1e-12: return [0.0] * m
        for r in range(m):
            if r != c:
                f = M[r][c] / M[c][c]
                for k in range(c, m + 1): M[r][k] -= f * M[c][k]
    return [M[r][m] / M[r][r] for r in range(m)]

# a cubic fitted to the 40 m (11 waypoints) around each waypoint, read at its centre
def cubic_res(c, h=5):
    a, b = c - h, c + h
    ux, uz = X[b] - X[a], Z[b] - Z[a]; L = math.hypot(ux, uz) or 1.0; ux /= L; uz /= L
    t = [((X[k] - X[a]) * ux + (Z[k] - Z[a]) * uz) for k in range(a, b + 1)]
    y = [(-(X[k] - X[a]) * uz + (Z[k] - Z[a]) * ux) for k in range(a, b + 1)]
    A = [[sum(tt ** (p + q) for tt in t) for q in range(4)] for p in range(4)]
    rhs = [sum(yy * tt ** p for tt, yy in zip(t, y)) for p in range(4)]
    c4 = solve(A, rhs); tt = t[h]
    return abs(y[h] - (c4[0] + c4[1] * tt + c4[2] * tt * tt + c4[3] * tt ** 3))
res = [0.0] * n
for c in range(max(5, i0 - 5), min(n - 5, i1 + 5)): res[c] = cubic_res(c)
W = int(100 / SP)
wig = []
for a in range(i0, i1, W):
    b = min(i1, a + W); m = max(res[a:b]); wig.append((m, (a + res[a:b].index(m)) * SP))
vals = sorted(w for w, _ in wig)
q = lambda f: vals[min(len(vals) - 1, int(f * len(vals)))]
print(f'{path.split("/")[-1].split(chr(92))[-1]}: timed {s0:.0f}-{s1:.0f} m, {len(wig)} stretches of 100 m')
print(f'  wiggle (each waypoint against a cubic through its 40 m; worst per 100 m): median {q(0.5):.3f} m, p95 {q(0.95):.3f} m, worst {vals[-1]:.3f} m')
for w, s_ in sorted(wig, reverse=True)[:5]:
    print(f'    {w:.2f} m at {s_:.0f} m')

# signed curvature, 12 m either side
def kAt(i, h=3):
    ax, az, bx, bz, cx, cz = X[i - h], Z[i - h], X[i], Z[i], X[i + h], Z[i + h]
    cr = (bx - ax) * (cz - az) - (bz - az) * (cx - ax)
    la = math.hypot(bx - cx, bz - cz); lb = math.hypot(ax - cx, az - cz); lc = math.hypot(ax - bx, az - bz)
    return 0.0 if la * lb * lc == 0 else 2 * cr / (la * lb * lc)
K = [0.0] * n
for i in range(3, n - 3): K[i] = kAt(i)
def heading(i): return math.atan2(Z[i + 1] - Z[i], X[i + 1] - X[i])
# an ABRUPT reversal: bends under 250 m one way and then the other, their
# peaks under 40 m apart, with the line straying under 3 m from the chord
# between them - a sideways jog, not one of the road's own S-bends
rev = []
peaks = [i for i in range(i0, i1) if abs(K[i]) >= 1 / 250 and abs(K[i]) >= abs(K[i - 1]) and abs(K[i]) >= abs(K[i + 1])]
for a, b in zip(peaks, peaks[1:]):
    if K[a] * K[b] >= 0 or (b - a) * SP > 40: continue
    lo, hi = max(0, a - 5), min(n - 1, b + 5)
    ux, uz = X[hi] - X[lo], Z[hi] - Z[lo]; L = math.hypot(ux, uz) or 1.0
    amp = max(abs(-(X[k] - X[lo]) * uz / L + (Z[k] - Z[lo]) * ux / L) for k in range(lo, hi + 1))
    if amp < 3.0: rev.append((a * SP, b * SP, 1 / abs(K[a]), 1 / abs(K[b]), amp))
print(f'  abrupt reversals (R < 250 m each way, peaks < 40 m apart, < 3 m off the chord): {len(rev)}')
for r in rev[:8]:
    print(f'    {r[0]:.0f} m R {r[2]:.0f} -> {r[1]:.0f} m R {r[3]:.0f} the other way, {r[4]:.2f} m off the chord')
