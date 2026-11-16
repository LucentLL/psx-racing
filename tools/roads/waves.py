"""waves.py - waves in a mountain stage's plan line, measured on the builder's own curve (owner, 2026-11-16: "an
otherwise straight road will wave back and forth abruptly in a way that DOT would never approve").

    python -I tools/roads/waves.py report  [--lines new,pc45,mapped] [--stages swa,gap] [--line name=<dir>]
                                          [--shelf <dir>] [--out <dir>]
    python -I tools/roads/waves.py refits  --line smooth=<dir> --shelf <dir>      (writes tools/roads/plan_refits.json)

A line set is a folder of <pre>_stage.json. Built in: mapped = tools/roads/srtm_bakes/<pre>_srtm (the mapped line
before the lidar round moved it), pc45 = tools/roads/pc45_lines (what PC45 shipped), new = Assets/PSXRacing/Resources.
--shelf names a folder of tools/roads/shelf_edges.mjs outputs, <pre>_<set>.json, measured on that set's own line.

Everything is measured on the line the builder draws: PSXRacingBuilder.Ribbon.cs RibbonFrame, a Hermite cubic per gap
with chord-length-scaled tangents, sampled every 1 m. The detectors are the align investigation's (2026-11-16,
verify-mountain/vm.py and mountain/wavelib.py), with their thresholds:

  S-PLACE  two opposite lobes of the line against a centred 100 m quadratic fit, both over 0.25 m, peaks under 80 m
           apart, the fit near-straight (R > 300 m) at both peaks and between, the 8 m mean curvature reversing over
           1/1000 m each way between them. REAL when both lidar shelf edges carry it (regression slope of each edge's
           residual on the line's, beta >= 0.7, shelf found at 60 % of the stations); BRIDGE on a mapped bridge.
  JOG      consecutive curves the opposite ways (10 m mean curvature beyond 1/1090 m, the radius where 60 km/h needs
           more than normal crown; each 2-15 degrees) with a tangent between them under 2/3 of the two superelevation
           runoffs (AASHTO Method 5, e max 8 %, half the paved width rotated, relative gradient 0.6 %).
           MAPPED when the mapped line has both curves too (same sign, overlapping within 8 m).
  MOVES    each waypoint against the mapped line's same waypoint (plan_smooth.mjs keeps every index): the largest move
           on gentle stretches (mapped R over 120 m at +-12 m), and each refit's move W against its taper L:
           L >= max(45 W, 61 m), L >= 1.5 W S (S = the posted mph, TrackCatalog), the whole move longer than 2 L.
  OFF-SHELF  stations where a road edge (half the paved width either side) is more than 1 m past the shelf's edge.
"""
import json, math, os, sys
import numpy as np
from numpy.lib.stride_tricks import sliding_window_view as swv

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
PRES = ['swa', 'gap', 'mtm', 'beech', 'chimney', 'brp', 'swiss', 'brock']
PAVED = dict(swa=6.1, gap=6.7, mtm=6.1, beech=6.1, chimney=6.1, brp=6.4, swiss=6.4, brock=6.4)   # lib3dep.mjs STAGES
MPH = dict(swa=35, gap=45, mtm=35, beech=45, chimney=25, brp=45, swiss=35, brock=35)            # TrackCatalog speedLimitKmh
OSM = dict(swa='overpass_swiss226a.json', gap='overpass_gillespie.json', mtm='overpass_mtmitchell.json',
           beech='overpass_nc215.json', chimney='overpass_chimney.json', brp='../../brp/cache/overpass_brp.json',
           swiss='loopprobe_switzerland.json', brock='loopprobe_blowingrock.json')
BUILTIN = {'mapped': lambda p: os.path.join(HERE, 'srtm_bakes', p + '_srtm', p + '_stage.json'),
           'pc45': lambda p: os.path.join(HERE, 'pc45_lines', p + '_stage.json'),
           'new': lambda p: os.path.join(ROOT, 'Assets', 'PSXRacing', 'Resources', p + '_stage.json')}


def load(path):
    j = json.load(open(path))
    a = np.array(j['xyz'], float).reshape(-1, 3)
    return a[:, [0, 2]].copy(), bool(j.get('loop', False)), j


# ------------------------------------------------------------------ the builder's curve
def _ctl(P, i, loop):
    n = len(P)
    if loop:
        return P[i % n]
    if i < 0:
        return 2 * P[0] - P[1]
    if i >= n:
        return 2 * P[n - 1] - P[n - 2]
    return P[i]


def hermite(P, loop, sub=16):
    """RibbonFrame (PSXRacingBuilder.Ribbon.cs) at `sub` points per gap: pos, d/dt, d2/dt2, gap index, t."""
    n = len(P)
    gaps = n if loop else n - 1
    g = np.arange(gaps)
    A = P[g % n]; B = P[(g + 1) % n]
    p0 = np.array([_ctl(P, k - 1, loop) for k in g]); p3 = np.array([_ctl(P, k + 2, loop) for k in g])
    d0 = np.hypot(*(p0 - A).T); d1 = np.hypot(*(B - A).T); d2 = np.hypot(*(p3 - B).T)
    ka = d1 / np.maximum(d0 + d1, 1e-4); kb = d1 / np.maximum(d1 + d2, 1e-4)
    MA = (B - p0) * ka[:, None]; MB = (p3 - A) * kb[:, None]
    t = np.arange(sub) / sub
    t2, t3 = t * t, t * t * t
    h10, h01, h11 = t3 - 2 * t2 + t, -2 * t3 + 3 * t2, t3 - t2
    d10, d01, d11 = 3 * t2 - 4 * t + 1, -6 * t2 + 6 * t, 3 * t2 - 2 * t
    e10, e01, e11 = 6 * t - 4, -12 * t + 6, 6 * t - 2
    BA = B - A
    pos = A[:, None, :] + h10[None, :, None] * MA[:, None, :] + h01[None, :, None] * BA[:, None, :] + h11[None, :, None] * MB[:, None, :]
    der = d10[None, :, None] * MA[:, None, :] + d01[None, :, None] * BA[:, None, :] + d11[None, :, None] * MB[:, None, :]
    dd = e10[None, :, None] * MA[:, None, :] + e01[None, :, None] * BA[:, None, :] + e11[None, :, None] * MB[:, None, :]
    pos = pos.reshape(-1, 2); der = der.reshape(-1, 2); dd = dd.reshape(-1, 2)
    gi = np.repeat(g, sub).astype(float); tt = np.tile(t, gaps)
    if not loop:
        pos = np.vstack([pos, P[-1]]); der = np.vstack([der, MB[-1]]); dd = np.vstack([dd, 2 * MA[-1] - 6 * BA[-1] + 4 * MB[-1]])
        gi = np.append(gi, gaps - 1); tt = np.append(tt, 1.0)
    else:
        pos = np.vstack([pos, pos[:1]]); der = np.vstack([der, der[:1]]); dd = np.vstack([dd, dd[:1]])
        gi = np.append(gi, gaps); tt = np.append(tt, 0.0)
    return pos, der, dd, gi, tt


def drawn(P, loop, ds=1.0, sub=16):
    """The drawn line every ds m of arc: s, x, z, signed curvature (+ = left), station index (fractional)."""
    pos, der, dd, gi, tt = hermite(P, loop, sub)
    s = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(pos, axis=0).T))])
    sp = np.hypot(*der.T)
    k = (der[:, 0] * dd[:, 1] - der[:, 1] * dd[:, 0]) / np.maximum(sp, 1e-9) ** 3
    sq = np.arange(0, s[-1] + 1e-9, ds)
    return dict(s=sq, x=np.interp(sq, s, pos[:, 0]), z=np.interp(sq, s, pos[:, 1]), k=np.interp(sq, s, k),
                st=np.interp(sq, s, gi + tt), L=s[-1], loop=loop)


def movmean(a, w, loop=False):
    w = int(w)
    if w <= 1:
        return a.copy()
    h = w // 2
    ap = np.concatenate([a[-h:], a, a[:h]]) if loop else np.concatenate([np.full(h, a[0]), a, np.full(h, a[-1])])
    c = np.concatenate([[0], np.cumsum(ap)])
    out = (c[w:] - c[:-w]) / w
    return out[:len(a)] if len(out) >= len(a) else np.pad(out, (0, len(a) - len(out)), 'edge')


def movfit(D, half=50):
    """Centred quadratic over +-half m at every sample: residual at the centre (+ = left), fit radius, valid."""
    x, z, loop = D['x'], D['z'], D['loop']
    n = len(x); W = 2 * half + 1
    if loop:
        xp = np.concatenate([x[-half:], x, x[:half]]); zp = np.concatenate([z[-half:], z, z[:half]])
    else:
        xp = np.concatenate([np.full(half, np.nan), x, np.full(half, np.nan)]); zp = np.concatenate([np.full(half, np.nan), z, np.full(half, np.nan)])
    X = swv(xp, W)[:n]; Z = swv(zp, W)[:n]
    ux = X[:, -1] - X[:, 0]; uz = Z[:, -1] - Z[:, 0]; L = np.hypot(ux, uz); ux = ux / L; uz = uz / L
    dx = X - x[:, None]; dz = Z - z[:, None]
    a = dx * ux[:, None] + dz * uz[:, None]; b = -dx * uz[:, None] + dz * ux[:, None]
    valid = np.isfinite(a).all(axis=1)
    a = np.nan_to_num(a); b = np.nan_to_num(b)
    S0 = np.full(n, float(W)); S1 = a.sum(1); S2 = (a ** 2).sum(1); S3 = (a ** 3).sum(1); S4 = (a ** 4).sum(1)
    T0 = b.sum(1); T1 = (a * b).sum(1); T2 = (a * a * b).sum(1)
    M = np.stack([np.stack([S0, S1, S2], -1), np.stack([S1, S2, S3], -1), np.stack([S2, S3, S4], -1)], 1)
    T = np.stack([T0, T1, T2], -1)
    M[~valid] = np.eye(3)
    c = np.linalg.solve(M, T[..., None])[..., 0]
    res = -c[:, 0]
    R = (1 + c[:, 1] ** 2) ** 1.5 / np.maximum(np.abs(2 * c[:, 2]), 1e-12)
    res[~valid] = 0.0; R[~valid] = 0.0
    return res, R, valid


def lobes(r):
    sg = np.sign(r); out = []; i = 0; n = len(r)
    while i < n:
        if sg[i] == 0:
            i += 1; continue
        j = i
        while j + 1 < n and sg[j + 1] == sg[i]:
            j += 1
        ip = i + int(np.argmax(np.abs(r[i:j + 1])))
        out.append((i, j, ip, r[ip])); i = j + 1
    return out


def swaves(D, amp=0.25, maxsep=80, Rmin=300, krev=1e-3, half=50):
    """S-places (see the header). Each: i0..i1 (lobe ends), ia (first peak), ibl (last peak), amp, sep, kmax."""
    res, R, valid = movfit(D, half)
    k8 = movmean(D['k'], 9, D['loop'])
    lb = lobes(res); pairs = []
    for q in range(len(lb) - 1):
        a, b = lb[q], lb[q + 1]
        if np.sign(a[3]) == np.sign(b[3]) or abs(a[3]) <= amp or abs(b[3]) <= amp:
            continue
        ia, ib = a[2], b[2]
        if ib - ia >= maxsep:
            continue
        im = (ia + ib) // 2
        if not (valid[ia] and valid[ib] and R[ia] > Rmin and R[ib] > Rmin and R[im] > Rmin):
            continue
        seg = k8[ia:ib + 1]
        if not (seg.max() > krev and seg.min() < -krev):
            continue
        pairs.append(dict(i0=a[0], i1=b[1], ia=ia, ibl=ib, amp=max(abs(a[3]), abs(b[3])), sep=ib - ia, kmax=float(np.abs(seg).max())))
    places = []
    for p in pairs:
        if places and p['i0'] <= places[-1]['i1']:
            P_ = places[-1]
            P_['i1'] = max(P_['i1'], p['i1']); P_['ibl'] = max(P_['ibl'], p['ibl'])
            P_['amp'] = max(P_['amp'], p['amp']); P_['kmax'] = max(P_['kmax'], p['kmax']); P_['sep'] = min(P_['sep'], p['sep']); P_['n'] += 1
        else:
            places.append(dict(p, n=1))
    for P_ in places:
        P_['s0'] = float(D['s'][P_['i0']]); P_['s1'] = float(D['s'][P_['i1']])
        P_['st0'] = float(D['st'][P_['i0']]) * 4; P_['st1'] = float(D['st'][P_['i1']]) * 4
        P_['pkA'] = float(D['st'][P_['ia']]) * 4; P_['pkB'] = float(D['st'][P_['ibl']]) * 4
    return places, res, R, valid


def e_method5(R, V=60.0, VR=55.0, emax=0.08, fmax=0.17):
    x = 1.0 / np.asarray(R, float)
    xPI = 127 * emax / VR ** 2; fPI = V ** 2 * xPI / 127 - emax; xmin = 127 * (emax + fmax) / V ** 2
    ta1 = fPI / xPI; ta2 = (fmax - fPI) / (xmin - xPI); MO = xPI * (xmin - xPI) * (ta2 - ta1) / (2 * xmin)
    f = np.where(x <= xPI, MO * (x / xPI) ** 2 + x * ta1, MO * ((xmin - x) / (xmin - xPI)) ** 2 + fPI + (x - xPI) * ta2)
    return np.clip(V ** 2 * x / 127 - f, 0.02, emax)


def curve_runs(D, R_nc=1090.0, mindefl=2.0):
    k10 = movmean(D['k'], 11, D['loop']); th = 1.0 / R_nc
    sg = np.where(k10 > th, 1, np.where(k10 < -th, -1, 0)); ds = D['s'][1] - D['s'][0]
    runs = []; i, n = 0, len(sg)
    while i < n:
        if sg[i] == 0:
            i += 1; continue
        j = i
        while j + 1 < n and sg[j + 1] == sg[i]:
            j += 1
        defl = np.degrees(D['k'][i:j + 1].sum() * ds)
        if abs(defl) >= mindefl:
            Lc = (j - i + 1) * ds
            runs.append(dict(i0=i, i1=j, sgn=int(sg[i]), defl=float(defl), Rc=Lc / max(abs(np.radians(defl)), 1e-9),
                             st0=float(D['st'][i]) * 4, st1=float(D['st'][j]) * 4))
        i = j + 1
    return runs


def jogs(D, lane, delta=0.006, maxdefl=15.0, **kw):
    runs = curve_runs(D, **kw); out = []; ds = D['s'][1] - D['s'][0]
    for q in range(len(runs) - 1):
        a, b = runs[q], runs[q + 1]
        if a['sgn'] == b['sgn'] or abs(a['defl']) >= maxdefl or abs(b['defl']) >= maxdefl:
            continue
        tan = (b['i0'] - a['i1']) * ds
        need = 2.0 / 3.0 * (lane * float(e_method5(a['Rc'])) / delta + lane * float(e_method5(b['Rc'])) / delta)
        if tan < need:
            out.append(dict(a=a, b=b, tan=tan, need=need, st0=a['st0'], st1=b['st1'], R1=a['Rc'], R2=b['Rc'], d1=a['defl'], d2=b['defl']))
    return out, runs


def reversals(P, loop, s0=None, s1=None, sp=4):
    """plan_wiggle.py's ABRUPT REVERSALS on the waypoints (lakelure 7a9f964d): bends under 250 m one way and then the
    other (circle through the waypoints 12 m either side), peaks under 40 m apart, the line under 3 m off the chord
    between them. Returns [(fromM, toM, R1, R2, amp)]."""
    n = len(P); X, Z = P[:, 0], P[:, 1]
    i0 = max(3, int((s0 or 0) / sp)); i1 = min(n - 4, int((s1 if s1 is not None else (n - 1) * sp) / sp))
    K = np.zeros(n)
    for i in range(3, n - 3):
        a, b, c = P[i - 3], P[i], P[i + 3]
        cr = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        l = math.hypot(*(b - c)) * math.hypot(*(a - c)) * math.hypot(*(a - b))
        K[i] = 0.0 if l == 0 else 2 * cr / l
    peaks = [i for i in range(i0, i1) if abs(K[i]) >= 1 / 250 and abs(K[i]) >= abs(K[i - 1]) and abs(K[i]) >= abs(K[i + 1])]
    out = []
    for a, b in zip(peaks, peaks[1:]):
        if K[a] * K[b] >= 0 or (b - a) * sp > 40:
            continue
        lo, hi = max(0, a - 5), min(n - 1, b + 5)
        ux, uz = X[hi] - X[lo], Z[hi] - Z[lo]; L = math.hypot(ux, uz) or 1.0
        amp = max(abs(-(X[k] - X[lo]) * uz / L + (Z[k] - Z[lo]) * ux / L) for k in range(lo, hi + 1))
        if amp < 3.0:
            out.append((a * sp, b * sp, 1 / abs(K[a]), 1 / abs(K[b]), amp))
    return out


def has_curve(runs, c, tol=8.0):
    return any(r['sgn'] == c['sgn'] and r['st0'] <= c['st1'] + tol and r['st1'] >= c['st0'] - tol for r in runs)


def tightest3(P, loop):
    n = len(P); idx = np.arange(n) if loop else np.arange(1, n - 1)
    a = P[(idx - 1) % n]; b = P[idx]; c = P[(idx + 1) % n]
    ab = np.hypot(*(b - a).T); bc = np.hypot(*(c - b).T); ca = np.hypot(*(a - c).T)
    cross = np.abs((b - a)[:, 0] * (c - a)[:, 1] - (b - a)[:, 1] * (c - a)[:, 0])
    R = ab * bc * ca / np.maximum(2 * cross, 1e-12); i = int(np.argmin(R))
    return float(R[i]), int(idx[i]) * 4


def radius12(P, loop):
    """|R| from the circle through the waypoints 12 m either side (plan_smooth.mjs's own measure)."""
    n = len(P); i = np.arange(n)
    a = P[(i - 3) % n] if loop else P[np.clip(i - 3, 0, n - 1)]; c = P[(i + 3) % n] if loop else P[np.clip(i + 3, 0, n - 1)]; b = P
    cr = (b - a)[:, 0] * (c - a)[:, 1] - (b - a)[:, 1] * (c - a)[:, 0]
    l = np.hypot(*(b - c).T) * np.hypot(*(a - c).T) * np.hypot(*(a - b).T)
    k = np.where(l > 0, np.abs(2 * cr / np.maximum(l, 1e-12)), 0)
    if not loop:
        k[:3] = 0; k[-3:] = 0
    return 1 / np.maximum(k, 1e-9)


# ------------------------------------------------------------------ mapped bridges (bake_3dep.mjs osmSpans)
def osm_bridges(pre, P, j):
    sp = j.get('spacing', 4)
    spans = [(j['bridges'][k], j['bridges'][k + 1]) for k in range(0, len(j.get('bridges') or []) - 1, 2)]
    cache = os.environ.get('PSX_ROADS_CACHE') or os.path.join(HERE, 'cache')
    path = os.path.normpath(os.path.join(cache, OSM[pre]))
    if os.path.exists(path):
        phi = math.radians(j['lat0'])
        mLat = 111132.92 - 559.82 * math.cos(2 * phi) + 1.175 * math.cos(4 * phi); mLon = 111412.84 * math.cos(phi) - 93.5 * math.cos(3 * phi)
        for e in json.load(open(path, encoding='utf-8'))['elements']:
            t = e.get('tags') or {}
            if e.get('type') != 'way' or not e.get('geometry') or not t.get('highway'):
                continue
            if not ((t.get('bridge') and t['bridge'] != 'no') or (t.get('tunnel') and t['tunnel'] != 'no')):
                continue
            g = np.array([[(q['lon'] - j['lon0']) * mLon, (q['lat'] - j['lat0']) * mLat] for q in e['geometry']])
            idx = []; tot = 0
            for k in range(len(g) - 1):
                L = math.hypot(*(g[k + 1] - g[k])); m = max(1, round(L / 4))
                for jj in range(m + 1):
                    p = g[k] + (g[k + 1] - g[k]) * jj / m; tot += 1
                    d = ((P - p) ** 2).sum(1); bi = int(np.argmin(d))
                    if d[bi] < 9:
                        idx.append(bi)
            if len(idx) >= 2 and len(idx) / tot >= 0.6:
                spans.append((min(idx) * sp, max(idx) * sp))
    return spans


# ------------------------------------------------------------------ the lidar shelf
def med3(v):
    out = v.copy()
    for i in range(len(v)):
        w = v[max(0, i - 1):i + 2]; w = w[np.isfinite(w)]
        out[i] = np.median(w) if len(w) >= 2 else np.nan
    return out


def load_shelf(path):
    pr = json.load(open(path))
    a = np.array([np.nan if v is None else v for v in pr['a']]); z = np.array([np.nan if v is None else v for v in pr['z']])
    return a, z, np.stack([pr['nx'], pr['nz']], 1)


def edge_D(pts, loop):
    ok = np.isfinite(pts).all(1); pts = pts.copy(); idx = np.arange(len(pts))
    for c in range(2):
        pts[~ok, c] = np.interp(idx[~ok], idx[ok], pts[ok, c])
    s = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(pts, axis=0).T))]); sq = np.arange(0, s[-1], 1.0)
    return dict(s=sq, x=np.interp(sq, s, pts[:, 0]), z=np.interp(sq, s, pts[:, 1]), k=np.zeros_like(sq), loop=loop), np.interp(sq, s, idx)


def shelf_betas(P, loop, D, res, shelf, places):
    """For each place: beta of the right and left shelf edges (1 = the edge has the line's S, 0 = it is smooth)."""
    a, z, N = shelf
    Ea = P + N * med3(a)[:, None]; Ez = P + N * med3(z)[:, None]
    Da, sta = edge_D(Ea, loop); Dz, stz = edge_D(Ez, loop)
    ra, _, _ = movfit(Da); rz, _, _ = movfit(Dz)
    for pl in places:
        m = (D['s'] >= pl['s0'] - 8) & (D['s'] <= pl['s1'] + 8)
        stw = D['st'][m]; rp = res[m]; den = (rp ** 2).sum()
        pl['betaR'] = float((np.interp(stw, sta, ra) * rp).sum() / den) if den > 0 else 0.0
        pl['betaL'] = float((np.interp(stw, stz, rz) * rp).sum() / den) if den > 0 else 0.0
        i0, i1 = int(stw.min()), int(stw.max()) + 1
        pl['found'] = float(np.isfinite(a[i0:i1]).mean())
        pl['real'] = pl['found'] >= 0.6 and min(pl['betaR'], pl['betaL']) >= 0.7


def off_shelf(pre, P, shelf, thr=1.0):
    """Runs of stations where a road edge is more than thr past the shelf edge: (fromM, toM, side, worst m)."""
    a, z, _ = shelf; h = PAVED[pre] / 2
    outR = np.where(np.isfinite(a), a + h, np.nan); outL = np.where(np.isfinite(z), h - z, np.nan)
    runs = []
    for side, o in (('R', outR), ('L', outL)):
        bad = np.nan_to_num(o, nan=-9) > thr; i = 0; n = len(bad)
        while i < n:
            if not bad[i]:
                i += 1; continue
            j = i
            while j + 1 < n and bad[j + 1]:
                j += 1
            runs.append((i * 4, j * 4, side, float(np.nanmax(o[i:j + 1]))))
            i = j + 1
    found = float(np.isfinite(a).mean())
    return sorted(runs), found


# ------------------------------------------------------------------ measuring one line
def measure(pre, path, shelf_path=None):
    P, loop, j = load(path)
    D = drawn(P, loop, 1.0)
    places, res, R, valid = swaves(D)
    bridges = osm_bridges(pre, P, j)
    for pl in places:
        pl['bridge'] = any(a - 10 <= pl['st1'] and b + 10 >= pl['st0'] for a, b in bridges)
        pl['real'] = False
    shelf = load_shelf(shelf_path) if shelf_path and os.path.exists(shelf_path) else None
    if shelf is not None:
        shelf_betas(P, loop, D, res, shelf, places)
    jg, runs = jogs(D, PAVED[pre] / 2)
    revs = []
    for (a, b, r1, r2, amp) in reversals(P, loop, 0 if loop else j.get('startLineM', 0), None if loop else j.get('finishM'), j.get('spacing', 4)):
        ix = np.nonzero((D['st'] * 4 >= a - 8) & (D['st'] * 4 <= b + 8))[0]
        revs.append(dict(st0=a, st1=b, pkA=a, pkB=b, R1=r1, R2=r2, amp=amp, s0=float(D['s'][ix[0]]), s1=float(D['s'][ix[-1]]), real=False,
                         bridge=any(x - 10 <= b and y + 10 >= a for x, y in bridges)))
    if shelf is not None and revs:
        shelf_betas(P, loop, D, res, shelf, revs)
    return dict(pre=pre, P=P, loop=loop, j=j, D=D, places=places, jogs=jg, runs=runs, bridges=bridges, revs=revs,
                tight=tightest3(P, loop), ns_km=float(((R > 300) & valid).sum()) / 1000.0, shelf=shelf)


def line_path(spec, name, pre):
    if name in spec:
        return os.path.join(spec[name], pre + '_stage.json')
    return BUILTIN[name](pre)


def main():
    args = sys.argv[1:]
    cmd = args[0] if args else 'report'
    opt = lambda k, d=None: args[args.index(k) + 1] if k in args else d
    spec = {}
    for i, a in enumerate(args):
        if a == '--line':
            k, v = args[i + 1].split('=', 1); spec[k] = v
    stages = opt('--stages', ','.join(PRES)).split(',')
    shelf_dir = opt('--shelf')
    sh = lambda pre, name: os.path.join(shelf_dir, f'{pre}_{name}.json') if shelf_dir else None

    if cmd == 'refits':
        name = opt('--from', 'smooth')
        out = {'what': 'Local refits (tools/roads/plan_smooth.mjs localRefits) at the S-places tools/roads/waves.py finds on the '
                       'gentle-stretch-smoothed mapped line, less the S-places both lidar shelf edges carry (the real road) and '
                       'those on a mapped bridge. fromM / toM: the S\'s first and last lobe peak, metres along the stage '
                       '(waypoint index x 4). Written by: python -I tools/roads/waves.py refits --from ' + name,
               'stages': {}}
        for pre in stages:
            m = measure(pre, line_path(spec, name, pre), sh(pre, name))
            lst = []
            for pl in m['places'] + (m['revs'] if '--reversals' in args else []):
                tag = 'REAL' if pl['real'] else 'BRIDGE' if pl['bridge'] else ''
                print(f"{pre:8s} S {pl['st0']:7.0f}-{pl['st1']:7.0f} m  peaks {pl['pkA']:7.0f}/{pl['pkB']:7.0f}  amp {pl['amp']:.2f}  "
                      f"beta R {pl.get('betaR', float('nan')):+.2f} L {pl.get('betaL', float('nan')):+.2f} found {pl.get('found', float('nan')):.2f} {tag}")
                if not tag:
                    lst.append({'fromM': int(round(pl['pkA'])), 'toM': int(round(pl['pkB'])),
                                'why': f"S {pl['amp']:.2f} m, beta R {pl.get('betaR', 0):+.2f} L {pl.get('betaL', 0):+.2f}"})
            out['stages'][pre] = lst
        dst = opt('--write', os.path.join(HERE, 'plan_refits.json'))
        json.dump(out, open(dst, 'w'), indent=1)
        print('wrote', dst)
        return

    names = opt('--lines', 'new,pc45,mapped').split(',')
    outdir = opt('--out')
    tot = {n: dict(S=0, S5=0, Sx=0, jogs=0, jogsNew=0) for n in names}
    detail = []
    mapped_runs = {}
    for pre in stages:
        mm = measure(pre, line_path(spec, 'mapped', pre))
        mapped_runs[pre] = mm['runs']
        for name in names:
            m = mm if name == 'mapped' and not sh(pre, 'mapped') else measure(pre, line_path(spec, name, pre), sh(pre, name))
            S = m['places']; ex = [p for p in S if p['real'] or p['bridge']]
            jnew = [g for g in m['jogs'] if not (has_curve(mapped_runs[pre], g['a']) and has_curve(mapped_runs[pre], g['b']))]
            t = tot[name]; t['S'] += len(S); t['S5'] += sum(p['amp'] > 0.5 for p in S); t['Sx'] += len(S) - len(ex); t['jogs'] += len(m['jogs']); t['jogsNew'] += len(jnew)
            rv = m['revs']; rvx = [r for r in rv if not (r['real'] or r['bridge'])]
            mrev = [] if name == 'mapped' else measure(pre, line_path(spec, 'mapped', pre))['revs']
            rvm = [r for r in rv if any(abs(r['st0'] - q['st0']) <= 20 for q in mrev)]
            t['rev'] = t.get('rev', 0) + len(rv); t['revx'] = t.get('revx', 0) + len(rvx)
            print(f"{pre:8s} {name:7s} km {m['D']['L'] / 1000:6.2f}  S {len(S):3d} (>0.5 m {sum(p['amp'] > 0.5 for p in S)}, not real/bridge {len(S) - len(ex)})  "
                  f"jogs {len(m['jogs']):3d} (not on the mapped line {len(jnew)})  reversals {len(rv)} (not real/bridge {len(rvx)}, on the mapped line {len(rvm) if name != 'mapped' else '-'})  tightest {m['tight'][0]:5.1f} m @{m['tight'][1]}")
            for r in rv:
                detail.append(f"{pre},{name},rev,{r['st0']},{r['st1']},R {r['R1']:.0f}/{r['R2']:.0f} amp {r['amp']:.2f},{r.get('betaR', float('nan')):.2f},{r.get('betaL', float('nan')):.2f},{'REAL' if r['real'] else 'BRIDGE' if r['bridge'] else 'jog'}{' mapped' if r in rvm else ''}")
            for p in S:
                tag = 'REAL' if p['real'] else 'BRIDGE' if p['bridge'] else 'jog'
                detail.append(f"{pre},{name},S,{p['st0']:.0f},{p['st1']:.0f},{p['amp']:.2f},{p.get('betaR', float('nan')):.2f},{p.get('betaL', float('nan')):.2f},{tag}")
            for g in m['jogs']:
                detail.append(f"{pre},{name},jog,{g['st0']:.0f},{g['st1']:.0f},R {g['R1']:.0f}/{g['R2']:.0f},tan {g['tan']:.0f}<{g['need']:.0f},,{'NEW' if g in jnew else 'mapped'}")
            if m['shelf'] is not None:
                runs, found = off_shelf(pre, m['P'], m['shelf'])
                stn = sum((b - a) // 4 + 1 for a, b, _, _ in runs)
                print(f"           off-shelf (edge > 1 m past the shelf): {len(runs)} run(s), {stn} station(s); shelf found at {found:.0%}")
                for a, b, sd, w in runs:
                    detail.append(f"{pre},{name},offshelf,{a},{b},{w:.2f},,,{sd}")
    for name, t in tot.items():
        print(f"TOTAL {name:7s} S {t['S']} (>0.5 m {t['S5']}; not real/bridge {t['Sx']})  jogs {t['jogs']} (not on the mapped line {t['jogsNew']})  reversals {t.get('rev', 0)} (not real/bridge {t.get('revx', 0)})")
    if outdir:
        os.makedirs(outdir, exist_ok=True)
        open(os.path.join(outdir, 'waves_detail.csv'), 'w').write('stage,line,kind,fromM,toM,a,b,c,tag\n' + '\n'.join(detail) + '\n')


if __name__ == '__main__':
    main()
