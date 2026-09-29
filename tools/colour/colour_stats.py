"""The colour protocol's numbers (the colour pass, 2026-09-29).

    py tools/colour/colour_stats.py stats  <png|dir>...          frame + region table
    py tools/colour/colour_stats.py compare <A.png> <B.png>        per-region deltas (same target only)
    py tools/colour/colour_stats.py gate   <webgl dir> <baseline dir>   the road-colour gate
    py tools/colour/colour_stats.py agree  <editor.png> <player.png>  harness vs editor, per region
    py tools/colour/colour_stats.py match  <candidate dir> <baseline dir> [tol]   every region AND pixel vs the decoded baseline
    ... --json out.json   also write every number as JSON

Every frame is a PNG at native resolution with its sidecar (<png>.json,
Editor/ShotSidecar.cs): activeBuildTarget, the probe texture's format, the
hour/weather/dress, and the protocol's measuring boxes (normalised, projected
from the world - Scripts/Dev/ColourSpots.cs). A frame without a sidecar is
refused, and so is a comparison of two frames drawn with different texture
imports (activeBuildTarget) - except the gate, whose baseline is by
definition the decoded import (StandaloneWindows64) and whose candidate is
the shipped one (WebGL).

DEFINITIONS (the plan's "Targets"):
  Ycode    Rec.709 Y of linearised sRGB, re-encoded to sRGB, 0-255.
  Box4     an exact 4x4 box mean at native resolution (the Bayer dither is
           4x4), applied before every region, local or colour statistic.
  logstd9  median std of log2(Ylin) in 9x9 windows at 480x270 on Box4
           pixels, windows stepped 3 px, pixels under Ycode 24 excluded.
  plateau  the share of the frame on the single most common exact colour
           among pixels at Ycode >= 200 (one quantizer level).
  percentiles and shares are over raw pixels; the HUD is not in a _world
  frame, and a _hud frame's HUD is masked by differencing it with its
  _world twin.
"""
import json
import os
import sys
import glob
import numpy as np
from PIL import Image

W709 = np.array([0.2126, 0.7152, 0.0722])
M_XYZ = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
WHITE = np.array([0.95047, 1.0, 1.08883])


def lin(a):
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def enc(y):
    y = np.clip(y, 0, 1)
    return np.where(y <= 0.0031308, 12.92 * y, 1.055 * np.power(np.maximum(y, 1e-12), 1 / 2.4) - 0.055)


def ycode_of_lin(y):
    return 255.0 * enc(y)


def lab(rgb_lin):
    X = rgb_lin @ M_XYZ.T
    t = X / WHITE
    f = np.where(t > 0.008856, np.cbrt(t), 7.787 * t + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def box4(a):
    """Exact 4x4 box mean, same size (edges use the nearest full window)."""
    h, w = a.shape[:2]
    c = np.cumsum(np.cumsum(np.pad(a, ((1, 0), (1, 0)) + ((0, 0),) * (a.ndim - 2)), 0), 1)
    s = (c[4:, 4:] - c[:-4, 4:] - c[4:, :-4] + c[:-4, :-4]) / 16.0   # (h-3, w-3)
    out = np.empty_like(a, dtype=float)
    out[1:h - 2, 1:w - 2] = s
    out[0, :] = out[1, :]; out[h - 2:, :] = out[h - 3, :]
    out[:, 0] = out[:, 1]; out[:, w - 2:] = out[:, [w - 3]]
    return out


def sidecar(png):
    p = png + ".json"
    if not os.path.exists(p):
        return None
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def load(png):
    a = np.asarray(Image.open(png).convert("RGB")).astype(float) / 255.0
    return a


def target_of(sc):
    return (sc or {}).get("activeBuildTarget", "?")


class Frame:
    def __init__(self, png, need_sidecar=True):
        self.png = png
        self.sc = sidecar(png)
        if need_sidecar and self.sc is None:
            raise SystemExit(f"REFUSED: {png} has no sidecar ({png}.json) - which texture import drew it is unknown")
        self.rgb = load(png)
        self.h, self.w = self.rgb.shape[:2]
        self.lin = lin(self.rgb)
        self.Y = self.lin @ W709
        self.Yc = ycode_of_lin(self.Y)
        self.b4 = box4(self.lin)
        self.Y4 = self.b4 @ W709
        self.Yc4 = ycode_of_lin(self.Y4)
        self.mask = np.ones((self.h, self.w), bool)
        # A _hud frame: mask the HUD by its _world twin.
        if png.endswith("_hud.png"):
            twin = png[:-len("_hud.png")] + "_world.png"
            if os.path.exists(twin):
                t = load(twin)
                if t.shape == self.rgb.shape:
                    d = np.abs(t - self.rgb).max(-1) > 3 / 255.0
                    from PIL import ImageFilter
                    dm = np.asarray(Image.fromarray((d * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0
                    self.mask = ~dm

    @property
    def target(self):
        return target_of(self.sc)

    def regions(self):
        return (self.sc or {}).get("regions", [])

    def frame_stats(self):
        m = self.mask
        yc = self.Yc[m]
        ylin = self.Y[m]
        n = yc.size
        pct = {f"p{p}": float(np.percentile(yc, p)) for p in (1, 5, 50, 95, 99)}
        lo, hi = np.percentile(ylin, 1), np.percentile(ylin, 99)
        stops = float(np.log2(max(hi, 1e-6) / max(lo, 1e-6)))
        # plateau: the most common exact colour among bright pixels
        bright = (self.Yc >= 200) & m
        plateau = 0.0
        if bright.any():
            q = (self.rgb[bright] * 255 + 0.5).astype(np.int32)
            key = q[:, 0] * 65536 + q[:, 1] * 256 + q[:, 2]
            _, counts = np.unique(key, return_counts=True)
            plateau = float(counts.max()) / n
        # HSV S on Box4 display pixels
        d4 = enc(self.b4)[m]
        mx, mn = d4.max(-1), d4.min(-1)
        sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
        st = {
            **pct,
            "share_ge235": float((yc >= 235).mean()),
            "share_le46": float((yc <= 46).mean()),
            "share_le32": float((yc <= 32).mean()),
            "share_le4": float((yc <= 4).mean()),
            "share_8_32": float(((yc >= 8) & (yc <= 32)).mean()),
            "plateau": plateau,
            "stops_p1_p99": stops,
            "logstd9": self.logstd9(),
            "sat_mean": float(sat.mean()),
        }
        # CIELAB of the darks
        L = lab(self.b4)
        for lo_, hi_ in ((4, 20), (20, 40), (40, 80), (4, 40)):
            k = m & (self.Yc4 >= lo_) & (self.Yc4 < hi_)
            if k.sum() < 200:
                st[f"dark_{lo_}_{hi_}"] = None
                continue
            a_, b_ = L[..., 1][k], L[..., 2][k]
            st[f"dark_{lo_}_{hi_}"] = {"share": float(k.mean()), "a": float(np.median(a_)), "b": float(np.median(b_)),
                                     "C": float(np.median(np.hypot(a_, b_)))}
        return st

    def logstd9(self):
        im = Image.fromarray(np.clip(enc(self.b4) * 255 + 0.5, 0, 255).astype(np.uint8)).resize((480, 270), Image.BOX)
        a = np.asarray(im).astype(float) / 255
        Y = lin(a) @ W709
        Yc = ycode_of_lin(Y)
        m = np.asarray(Image.fromarray(self.mask.astype(np.uint8) * 255).resize((480, 270), Image.NEAREST)) > 0
        Lg = np.log2(np.maximum(Y, 1e-5))
        out = []
        for y in range(4, 266, 3):
            for x in range(4, 476, 3):
                w = (slice(y - 4, y + 5), slice(x - 4, x + 5))
                k = m[w] & (Yc[w] >= 24)
                if k.sum() >= 40:
                    out.append(Lg[w][k].std())
        return float(np.median(out)) if out else None

    def region(self, r, inset=2):
        x0, y0, x1, y1 = r["box"]
        X0, X1 = int(round(x0 * self.w)), int(round(x1 * self.w))
        Y0, Y1 = int(round(y0 * self.h)), int(round(y1 * self.h))
        if X1 - X0 > 2 * inset + 1: X0, X1 = X0 + inset, X1 - inset
        if Y1 - Y0 > 2 * inset + 1: Y0, Y1 = Y0 + inset, Y1 - inset
        if X1 <= X0 or Y1 <= Y0:
            return None
        sl = (slice(Y0, Y1), slice(X0, X1))
        k = self.mask[sl]
        if k.sum() == 0:
            return None
        yc4 = self.Yc4[sl][k]
        rgb = self.b4[sl][k]
        mean_lin = rgb.mean(0)
        L = lab(mean_lin[None, :])[0]
        return {
            "px": int(k.sum()), "rect": [X0, Y0, X1, Y1],
            "Ycode_med": float(np.median(yc4)),
            "Ycode_mean": float(ycode_of_lin(float(mean_lin @ W709))),
            "Ycode_std": float(yc4.std()),
            "Ylin": float(mean_lin @ W709),
            "rgb_lin": [float(v) for v in mean_lin],
            "a": float(L[1]), "b": float(L[2]), "C": float(np.hypot(L[1], L[2])),
            "RminusG_code": float(255 * (enc(mean_lin[0]) - enc(mean_lin[1]))),
        }

    def region_table(self):
        out = {}
        for r in self.regions():
            v = self.region(r)
            out[r["name"]] = {"kind": r.get("kind"), "visible": r.get("visible", True), "inFrame": r.get("inFrame", True),
                              "onGround": r.get("onGround", True), "surface": r.get("surface", ""),
                              # "clean" (sidecars after 2026-09-29 09:00): one flat patch of one
                              # surface; None = an older sidecar, judged by the pixel count instead
                              "clean": r.get("clean"),
                              "occluder": r.get("occluder", ""), "m": v}
        return out


def expand(args):
    out = []
    for a in args:
        if os.path.isdir(a):
            out += sorted(glob.glob(os.path.join(a, "*.png")))
        else:
            out += sorted(glob.glob(a)) or [a]
    return out


def fmt(v, d=1):
    return "-" if v is None else (f"{v:.{d}f}" if isinstance(v, float) else str(v))


def cmd_stats(files, js):
    res = {}
    for p in expand(files):
        f = Frame(p)
        st = f.frame_stats()
        rt = f.region_table()
        sc = f.sc
        pr = sc.get("probe", {})
        res[os.path.basename(p)] = {"target": f.target, "probe": pr.get("graphicsFormat"), "hour": sc.get("hourName"),
                                    "weather": sc.get("weather"), "dress": sc.get("dressName"), "grade": sc.get("grade"),
                                    "frame": st, "regions": rt}
        print(f"\n{os.path.basename(p)}  [{f.target} probe {pr.get('graphicsFormat')} sRGB={pr.get('isDataSRGB')}] "
              f"{sc.get('hourName')} {sc.get('weather')} {sc.get('dressName')} grade {sc.get('grade')}")
        print(f"  p1 {st['p1']:.0f} p5 {st['p5']:.0f} p50 {st['p50']:.0f} p95 {st['p95']:.0f} p99 {st['p99']:.0f} | "
              f">=235 {100*st['share_ge235']:.2f}% plateau {100*st['plateau']:.2f}% <=46 {100*st['share_le46']:.1f}% "
              f"<=4 {100*st['share_le4']:.2f}% 8-32 {100*st['share_8_32']:.1f}% | stops {st['stops_p1_p99']:.2f} "
              f"logstd9 {fmt(st['logstd9'], 3)} S {st['sat_mean']:.3f}")
        d = st.get("dark_4_40")
        if d:
            print(f"  darks 4-40: share {100*d['share']:.1f}% a* {d['a']:+.1f} b* {d['b']:+.1f} C* {d['C']:.1f}")
        for n, r in rt.items():
            m = r["m"]
            flag = "" if (r["visible"] and r["inFrame"]) else f"  (hidden: {r['occluder'] or 'off frame'})"
            if r.get("clean") is False: flag += "  (straddles surfaces)"
            if m is None:
                print(f"    {n:14s} -{flag}")
                continue
            print(f"    {n:14s} Y med {m['Ycode_med']:6.1f} mean {m['Ycode_mean']:6.1f} std {m['Ycode_std']:5.1f} "
                  f"a* {m['a']:+5.1f} b* {m['b']:+5.1f}  R-G {m['RminusG_code']:+5.1f}  px {m['px']:5d}  "
                  f"[{r['surface']}]{flag}")
    if js:
        with open(js, "w") as fh:
            json.dump(res, fh, indent=1)
    return res


def cmd_compare(a, b, js):
    A, B = Frame(a), Frame(b)
    if A.target != B.target:
        raise SystemExit(f"REFUSED: {a} was drawn with {A.target} and {b} with {B.target} - different texture imports "
                         f"(use 'gate' for the baseline comparison)")
    ra, rb = A.region_table(), B.region_table()
    out = {}
    print(f"{os.path.basename(a)}  vs  {os.path.basename(b)}  ({A.target})")
    for n in ra:
        if n not in rb or ra[n]["m"] is None or rb[n]["m"] is None:
            continue
        d = rb[n]["m"]["Ycode_mean"] - ra[n]["m"]["Ycode_mean"]
        out[n] = d
        print(f"  {n:14s} {ra[n]['m']['Ycode_mean']:6.1f} -> {rb[n]['m']['Ycode_mean']:6.1f}  ({d:+.1f})")
    if js:
        with open(js, "w") as fh:
            json.dump(out, fh, indent=1)


ROAD_DAY = ("road_ahead_14", "road_left_4", "road_right_4")
ROAD_NIGHT_OFFBEAM = ("road_left_4", "road_right_4")


def cmd_gate(webgl_dir, base_dir, js):
    """The road-colour gate: the owner's road colours are never lighter than
    the decoded baseline. By day: WebGL <= baseline +2 and >= baseline -10;
    at night off the beam: within +-2. Pairs are matched by file name."""
    rows, fails, n = [], 0, 0
    for p in sorted(glob.glob(os.path.join(webgl_dir, "*_world.png"))):
        q = os.path.join(base_dir, os.path.basename(p))
        if not os.path.exists(q):
            continue
        W, B = Frame(p), Frame(q)
        if W.target != "WebGL":
            raise SystemExit(f"REFUSED: {p} is {W.target}, the gate's candidate must be the WebGL import")
        if B.target not in ("StandaloneWindows64", "StandaloneWindows"):
            raise SystemExit(f"REFUSED: {q} is {B.target}, the gate's baseline must be the decoded import (StandaloneWindows64)")
        night = (W.sc.get("night") or 0) > 0.5
        names = ROAD_NIGHT_OFFBEAM if night else ROAD_DAY
        rw, rb = W.region_table(), B.region_table()
        for nme in names:
            a, b = rw.get(nme), rb.get(nme)
            if not a or not b or a["m"] is None or b["m"] is None:
                continue
            if not (a["visible"] and a["inFrame"] and a["onGround"]) or a.get("clean") is False:
                continue
            n += 1
            d = a["m"]["Ycode_med"] - b["m"]["Ycode_med"]
            ok = (-2 <= d <= 2) if night else (-10 <= d <= 2)
            fails += 0 if ok else 1
            rows.append({"frame": os.path.basename(p), "region": nme, "surface": a["surface"], "night": night,
                         "webgl": a["m"]["Ycode_med"], "baseline": b["m"]["Ycode_med"], "delta": d, "ok": ok,
                         "ratio_lin": a["m"]["Ylin"] / max(b["m"]["Ylin"], 1e-6)})
    for r in rows:
        print(f"  {'ok  ' if r['ok'] else 'FAIL'} {r['frame'][:58]:58s} {r['region']:13s} "
              f"base {r['baseline']:6.1f} webgl {r['webgl']:6.1f} ({r['delta']:+6.1f}, x{r['ratio_lin']:.2f} linear) [{r['surface']}]")
    print(f"ROAD-COLOUR GATE: {n - fails}/{n} road regions pass" + ("" if fails == 0 else f" - {fails} FAIL (lighter than the owner's roads)"))
    if js:
        with open(js, "w") as fh:
            json.dump(rows, fh, indent=1)
    return fails == 0 and n > 0


def cmd_agree(editor_png, player_png, js, tol=2.0):
    E = Frame(editor_png)
    if E.target != "WebGL":
        raise SystemExit(f"REFUSED: {editor_png} is {E.target}; the harness is compared with WebGL-target editor frames only")
    P = Frame(player_png, need_sidecar=False)
    if P.sc is not None and P.sc.get("activeBuildTarget") not in (None, "WebGL", "WebGL-player"):
        raise SystemExit(f"REFUSED: the player frame's sidecar says {P.sc.get('activeBuildTarget')}")
    P.sc = {"regions": E.regions()}
    re_, rp = E.region_table(), P.region_table()
    rows, worst = [], 0.0
    for n, a in re_.items():
        b = rp.get(n)
        if a["m"] is None or b is None or b["m"] is None or not (a["visible"] and a["inFrame"]):
            continue
        d = b["m"]["Ycode_mean"] - a["m"]["Ycode_mean"]
        worst = max(worst, abs(d))
        rows.append({"region": n, "editor": a["m"]["Ycode_mean"], "player": b["m"]["Ycode_mean"], "delta": d, "ok": abs(d) <= tol})
    fe, fp = E.frame_stats(), P.frame_stats()
    for r in rows:
        print(f"  {'ok  ' if r['ok'] else 'OFF '} {r['region']:14s} editor {r['editor']:6.1f} player {r['player']:6.1f} ({r['delta']:+.1f})")
    print(f"  frame p50 editor {fe['p50']:.0f} player {fp['p50']:.0f}; p95 {fe['p95']:.0f} / {fp['p95']:.0f}")
    bad = [r for r in rows if not r["ok"]]
    print(f"AGREE {os.path.basename(editor_png)} vs {os.path.basename(player_png)}: {len(rows) - len(bad)}/{len(rows)} regions within {tol} codes (worst {worst:.1f})")
    if js:
        with open(js, "w") as fh:
            json.dump({"rows": rows, "editor": fe, "player": fp}, fh, indent=1)
    return not bad


def cmd_match(cand_dir, base_dir, js, tol=1.0):
    """C1b's proof (the 16-bit decode): a frame drawn with the set decoded in
    the shader matches the decoded BASELINE (StandaloneWindows64, the GPU's
    own sRGB decode, from before the pass) - every projected region within
    `tol` codes (mean Ycode on Box4 pixels), and the whole frame pixel by
    pixel (Box4 Ycode) reported as mean / p99 / share over 2 codes. Pairs
    are matched by file name; any target may be the candidate (a Standalone
    candidate proves the decode is exact, a WebGL one adds the 565 read)."""
    rows, frames, worst = [], [], (0.0, "")
    for p in sorted(glob.glob(os.path.join(cand_dir, "*_world.png"))):
        q = os.path.join(base_dir, os.path.basename(p))
        if not os.path.exists(q):
            continue
        C, B = Frame(p), Frame(q)
        if B.target not in ("StandaloneWindows64", "StandaloneWindows"):
            raise SystemExit(f"REFUSED: {q} is {B.target}, the baseline must be the decoded import (StandaloneWindows64)")
        if C.rgb.shape != B.rgb.shape:
            print(f"  SIZE {os.path.basename(p)} {C.rgb.shape} vs {B.rgb.shape}"); continue
        if not C.regions():
            # a player frame (tools/colour/shot-link.mjs) carries no boxes: the
            # same eye, so the baseline's projected boxes are its boxes
            C.sc = dict(C.sc or {}, regions=B.regions())
        d = np.abs(C.Yc4 - B.Yc4)
        fr = {"frame": os.path.basename(p), "cand": C.target, "mean_abs": float(d.mean()), "p99_abs": float(np.percentile(d, 99)),
              "share_gt2": float((d > 2).mean()), "max_abs": float(d.max())}
        frames.append(fr)
        rc, rb = C.region_table(), B.region_table()
        for n, a in rc.items():
            b = rb.get(n)
            if not b or a["m"] is None or b["m"] is None or not (a["visible"] and a["inFrame"]):
                continue
            dd = a["m"]["Ycode_mean"] - b["m"]["Ycode_mean"]
            ok = abs(dd) <= tol
            rows.append({"frame": fr["frame"], "region": n, "surface": a["surface"], "cand": a["m"]["Ycode_mean"],
                         "base": b["m"]["Ycode_mean"], "delta": dd, "ok": ok})
            if abs(dd) > worst[0]:
                worst = (abs(dd), f"{fr['frame']} {n} [{a['surface']}] {b['m']['Ycode_mean']:.1f} -> {a['m']['Ycode_mean']:.1f}")
    bad = [r for r in rows if not r["ok"]]
    for r in bad[:40]:
        print(f"  OFF  {r['frame'][:58]:58s} {r['region']:14s} base {r['base']:6.1f} cand {r['cand']:6.1f} ({r['delta']:+.2f}) [{r['surface']}]")
    frames.sort(key=lambda f: -f["mean_abs"])
    for f in frames[:12]:
        print(f"  frame {f['frame'][:62]:62s} mean|d| {f['mean_abs']:.2f}  p99 {f['p99_abs']:.1f}  >2: {100 * f['share_gt2']:.2f}%  max {f['max_abs']:.0f}")
    if frames:
        allm = np.array([f["mean_abs"] for f in frames])
        print(f"  {len(frames)} frames: mean|dYcode| median {np.median(allm):.3f}, worst {allm.max():.3f}")
    ds = np.array([r["delta"] for r in rows]) if rows else np.zeros(1)
    print(f"  regions: mean delta {ds.mean():+.3f}, mean |delta| {np.abs(ds).mean():.3f}, worst {worst[0]:.2f} ({worst[1]})")
    print(f"DECODE MATCH: {len(rows) - len(bad)}/{len(rows)} regions within {tol} code(s) of the decoded baseline")
    if js:
        with open(js, "w") as fh:
            json.dump({"rows": rows, "frames": frames}, fh, indent=1)
    return not bad and len(rows) > 0


def main(argv):
    js = None
    if "--json" in argv:
        i = argv.index("--json"); js = argv[i + 1]; argv = argv[:i] + argv[i + 2:]
    if len(argv) < 2:
        print(__doc__); return 2
    cmd, rest = argv[1], argv[2:]
    if cmd == "stats":
        cmd_stats(rest, js); return 0
    if cmd == "compare":
        cmd_compare(rest[0], rest[1], js); return 0
    if cmd == "gate":
        return 0 if cmd_gate(rest[0], rest[1], js) else 1
    if cmd == "agree":
        return 0 if cmd_agree(rest[0], rest[1], js) else 1
    if cmd == "match":
        return 0 if cmd_match(rest[0], rest[1], js, float(rest[2]) if len(rest) > 2 else 1.0) else 1
    print(__doc__); return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
