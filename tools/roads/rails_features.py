# Roadside features from the 1 m lidar ribbon of the audit (n x K heights, -30..+30 m at 0.5 m; + = LEFT of travel).
import json, math, sys, numpy as np
S = r'C:\Users\mcgee\AppData\Local\Temp\claude\be91fcb74e64226b\60efacf0-02f8-4d62-a240-63ce8066e70c\scratchpad\lidar'
pre = sys.argv[1]; src = sys.argv[2] if len(sys.argv) > 2 else 'audit_after'
d = json.load(open(rf'{S}\{src}\{pre}_audit.json')); s = d['series']
n, K = d['ribbon']['stations'], d['ribbon']['across']; K0 = K // 2
H = np.fromfile(rf'{S}\{src}\{pre}_ribbon.f32', dtype=np.float32).reshape(n, K)
x = np.array(s['x']); z = np.array(s['z'])
def feats(i, side, halfw=3.05):
    """side +1 = left (ribbon +), -1 = right. Heights relative to the road at the centreline bench."""
    row = H[i]; road = np.median(row[K0 - 3:K0 + 4])
    def h(t): return row[int(round(K0 + side * t / 0.5))] - road      # t metres from the centreline toward `side`
    # shelf edge: first t past the pavement edge where the ground has left road level by 0.4 m, or the slope over 1 m passes 25%
    edge = None
    t = halfw
    while t <= 20:
        if abs(h(t)) > 0.4 or abs(h(t + 1) - h(t)) > 0.25: edge = t; break
        t += 0.5
    if edge is None: edge = 20.0
    def drop(run): return -(min(h(min(edge + r, 29.5)) for r in np.arange(0, run + 0.01, 0.5)))
    def rise(run): return max(h(min(edge + r, 29.5)) for r in np.arange(0, run + 0.01, 0.5))
    return dict(edge=edge - halfw, d5=drop(5), d10=drop(10), d20=drop(20), r5=rise(5), h6=h(halfw + 6))
def curv(i):
    a, b, c = max(0, i - 3), i, min(n - 1, i + 3)
    v1 = (x[b] - x[a], z[b] - z[a]); v2 = (x[c] - x[b], z[c] - z[b])
    cr = v1[0] * v2[1] - v1[1] * v2[0]; dot = v1[0] * v2[0] + v1[1] * v2[1]
    ang = math.atan2(cr, dot); L = (math.hypot(*v1) + math.hypot(*v2)) / 2
    return ang / L if L > 0 else 0          # + = turning LEFT; radius = 1/|k|
if __name__ == '__main__':
    spots = [a.split(':') for a in sys.argv[3].split(',')] if len(sys.argv) > 3 else []
    for sd, m in spots:
        i = int(m) // 4; side = 1 if sd == 'L' else -1
        f = feats(i, side); k = curv(i)
        out = 'outside' if k * side < -1e-3 else 'inside' if k * side > 1e-3 else 'straight'
        print(f"{m:>6} {sd}: shelf {f['edge']:.1f} m past the pavement, drop 5/10/20 m beyond it {f['d5']:.1f}/{f['d10']:.1f}/{f['d20']:.1f} m, rise5 {f['r5']:.1f}, ground 6 m out {f['h6']:+.1f} m, R {abs(1/k) if abs(k)>1e-4 else 9999:.0f} m ({out} of the bend)")
