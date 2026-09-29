"""The colour protocol's numbers (the colour pass, 2026-09-29).

    py tools/colour/colour_stats.py stats  <png|dir>...          frame + region table
    py tools/colour/colour_stats.py compare <A.png> <B.png>        per-region deltas (same target only)
    py tools/colour/colour_stats.py gate   <webgl dir> <baseline dir>   the road-colour gate
    py tools/colour/colour_stats.py agree  <editor.png> <player.png>  harness vs editor, per region
    py tools/colour/colour_stats.py match  <candidate dir> <baseline dir> [tol]   every region AND pixel vs the decoded baseline
    py tools/colour/colour_stats.py pairs  <dir A> <dir B> [glob]  same-named frames A vs B (the A/B switch runs)
    py tools/colour/colour_stats.py beam   <dir>                   night _dark vs _lit/_biNNN/_brake: the low beam and tail lamps
    py tools/colour/colour_stats.py fx     <dir> [<unlit dir>]     particles vs their twins without (_noflk, _smoke): lit particles
                                                                   (with a PSX_LITFX=0 run: both on the unlit footprint)
    py tools/colour/colour_stats.py hud    <dir>                   _hud vs _world: label and dial contrast (WCAG), face luma
    py tools/colour/colour_stats.py shade  <dir>                   each frame vs its no-shadow twin (_flat): the car's own
                                                                   shadow on the road, sun:shade and the shade's b* (harsh sun)
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
    # A frame's emitter-mask picture (PSX_SHOT_ALPHA=1) is not a frame.
    return [p for p in out if not p.endswith("_alpha.png")]


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


def is_road(surface):
    """The owner's road colours are the ROAD's: the ribbon and the decks
    ('Road', 'Roads', a '...RoadDeck...'), never the shoulder beside it
    ('RoadEdge': the builder's batter, a verge texture) or a kerb."""
    s = (surface or "").lower()
    if "edge" in s or "kerb" in s:
        return False
    return s in ("road", "roads") or "roaddeck" in s or s.startswith("road_") or s.startswith("roads_")


def cmd_gate(webgl_dir, base_dir, js):
    """The road-colour gate: the owner's road colours are never LIGHTER than
    the decoded baseline, and a sunlit road is not darker than the plan's trim.

    The rule (the plan's "never-lighter gate", made exact on review 2026-09-29):
      * only the ROAD is judged (is_road): a box that lands on the shoulder
        (RoadEdge, the builder's verge batter) or a kerb is listed, not judged;
      * the ceiling is everywhere: WebGL <= baseline + 2 by day, and within
        +-2 at night off the beam;
      * the floor (baseline - 10: the exposure's trim of the retired 1.25
        light shoulder) is for the owner's colours IN SUNLIGHT on a clear day
        against an unclipped baseline. A road in shade by day (the sidecar's
        sunVis false - the harsh sun, C2 and the review's fill cut, darkens the
        shade on purpose: the plan's own model put a car's shadow on the
        ConcreteNew deck at 85 against a baseline near 140), a road under a
        snow, rain or fog sky (a different exposure), and a baseline that was
        itself on the grade's ceiling (Ycode >= 230: clipped, "blinding", no
        colour to hold) are reported as EXEMPT with the reason, not failed;
      * lit on purpose, reported apart: the car's own lamps AT NIGHT (the low
        beam's spill lights the road beside the car; the road's own colour is
        judged on the _dark frames), and the eye's adaptation in a tunnel.
        By day the car's lamps are no excuse: they are nothing against the sun."""
    rows, fails, n, info, exempt, notroad = [], 0, 0, 0, 0, 0
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
        var = W.sc.get("variant") or {}
        weather = (var.get("weather") or W.sc.get("weather") or "Clear")
        own_lamps = bool(var.get("lit"))
        eye = float(var.get("adapt", W.sc.get("adapt") or 1.0) or 1.0) > 1.01
        rw, rb = W.region_table(), B.region_table()
        sunvis = {r["name"]: r.get("sunVis") for r in W.regions()}
        for nme in names:
            a, b = rw.get(nme), rb.get(nme)
            if not a or not b or a["m"] is None or b["m"] is None:
                continue
            if not (a["visible"] and a["inFrame"] and a["onGround"]) or a.get("clean") is False:
                continue
            d = a["m"]["Ycode_med"] - b["m"]["Ycode_med"]
            base = b["m"]["Ycode_med"]
            row = {"frame": os.path.basename(p), "region": nme, "surface": a["surface"], "night": night,
                   "webgl": a["m"]["Ycode_med"], "baseline": base, "delta": d,
                   "ratio_lin": a["m"]["Ylin"] / max(b["m"]["Ylin"], 1e-6), "sunVis": sunvis.get(nme), "weather": weather}
            if not is_road(a["surface"]):
                row.update(ok=True, why="not road (" + a["surface"] + ")", tag="skip")
                notroad += 1
                rows.append(row)
                continue
            why, tag = "", ""
            if night:
                ok = -2 <= d <= 2
                if not ok and own_lamps:
                    why, tag = "own lamps at night (C5)", "info"
            else:
                over = d > 2
                under = d < -10
                ok = not over and not under
                if over and eye:
                    why, tag = "eye (C10)", "info"
                elif under:
                    reasons = []
                    if sunvis.get(nme) is False:
                        reasons.append("in shade (harsh sun)")
                    if weather != "Clear":
                        reasons.append(weather.lower() + " sky")
                    if base >= 230:
                        reasons.append("baseline clipped (%.0f)" % base)
                    if reasons:
                        why, tag = "darker, exempt: " + ", ".join(reasons), "exempt"
            if tag == "info":
                info += 1
            elif tag == "exempt":
                exempt += 1
            else:
                n += 1
                if not ok:
                    fails += 1
                    tag = "FAIL"
                    why = ("LIGHTER than the owner's road" if d > 0 else
                           "darker than the owner's road at night (+-2)" if night else
                           "darker than the floor (baseline -10) in sunlight")
            row.update(ok=ok, why=why, tag=tag or "ok")
            rows.append(row)
    for r in rows:
        tag = r["tag"]
        sv = "" if r.get("sunVis") is None else (" sun" if r["sunVis"] else " shade")
        print(f"  {tag:6s} {r['frame'][:58]:58s} {r['region']:13s} "
              f"base {r['baseline']:6.1f} webgl {r['webgl']:6.1f} ({r['delta']:+6.1f}, x{r['ratio_lin']:.2f} linear) [{r['surface']}{sv}]"
              + (f"  <- {r['why']}" if r["why"] else ""))
    print(f"ROAD-COLOUR GATE: {n - fails}/{n} road regions pass" + ("" if fails == 0 else f" - {fails} FAIL")
          + (f"; {exempt} darker and exempt (shade / weather / clipped baseline)" if exempt else "")
          + (f"; {info} lit on purpose (own lamps at night, the eye) reported apart" if info else "")
          + (f"; {notroad} boxes off the road (shoulder, kerb) not judged" if notroad else ""))
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


DIFFUSE_KINDS = ("road", "beam", "outside", "tail", "verge", "car")


def cmd_pairs(dir_a, dir_b, pattern, js):
    """Every frame of A with a same-named twin in B (the colour pass's A/B
    runs: tone off/on, halation off/on - same code, same sandbox session,
    only an environment switch apart): the frame numbers side by side, and
    each region's mean Ycode delta B - A. The diffuse regions' worst |delta|
    is the bloom test's number (halation on minus off <= 2 codes on any
    diffuse region); a lamp region's delta and b* are printed as well."""
    out, worst_all = [], 0.0
    for p in sorted(glob.glob(os.path.join(dir_a, pattern))):
        if p.endswith("_alpha.png"):
            continue
        q = os.path.join(dir_b, os.path.basename(p))
        if not os.path.exists(q):
            continue
        A, B = Frame(p), Frame(q)
        if A.target != B.target:
            raise SystemExit(f"REFUSED: {p} ({A.target}) and {q} ({B.target}) are different texture imports")
        fa, fb = A.frame_stats(), B.frame_stats()
        ra, rb = A.region_table(), B.region_table()
        rows, worst = [], (0.0, "")
        for n, a in ra.items():
            b = rb.get(n)
            if not b or a["m"] is None or b["m"] is None or not (a["visible"] and a["inFrame"]):
                continue
            d = b["m"]["Ycode_mean"] - a["m"]["Ycode_mean"]
            rows.append({"region": n, "kind": a["kind"], "surface": a["surface"], "a": a["m"]["Ycode_mean"], "b": b["m"]["Ycode_mean"],
                         "delta": d, "b_star_a": a["m"]["b"], "b_star_b": b["m"]["b"], "clean": a.get("clean")})
            if a["kind"] in DIFFUSE_KINDS and a.get("clean") is not False and abs(d) > worst[0]:
                worst = (abs(d), n)
        worst_all = max(worst_all, worst[0])
        d = np.abs(B.Yc4 - A.Yc4) if A.rgb.shape == B.rgb.shape else None
        print(f"\n{os.path.basename(p)}")
        for lab, f in (("A", fa), ("B", fb)):
            print(f"  {lab}: p1 {f['p1']:.0f} p5 {f['p5']:.0f} p50 {f['p50']:.0f} p95 {f['p95']:.0f} p99 {f['p99']:.0f} "
                  f">=235 {100*f['share_ge235']:.2f}% plateau {100*f['plateau']:.2f}% <=46 {100*f['share_le46']:.1f}% "
                  f"<=4 {100*f['share_le4']:.2f}% 8-32 {100*f['share_8_32']:.1f}% stops {f['stops_p1_p99']:.2f} "
                  f"logstd9 {fmt(f['logstd9'], 3)} S {f['sat_mean']:.3f}")
        if d is not None:
            print(f"  frame |dYcode| (Box4): mean {d.mean():.2f}  p99 {np.percentile(d, 99):.1f}  max {d.max():.0f}")
        for r in rows:
            tag = "" if r["clean"] is not False else "  (straddles)"
            print(f"    {r['region']:14s} {r['kind']:7s} {r['a']:6.1f} -> {r['b']:6.1f} ({r['delta']:+6.1f})  "
                  f"b* {r['b_star_a']:+5.1f} -> {r['b_star_b']:+5.1f}  [{r['surface']}]{tag}")
        print(f"  worst diffuse |delta| {worst[0]:.1f} ({worst[1]})")
        out.append({"frame": os.path.basename(p), "A": fa, "B": fb, "regions": rows, "worst_diffuse": worst[0],
                    "frame_mean_abs": float(d.mean()) if d is not None else None})
    print(f"\n{len(out)} pairs; worst diffuse |delta| over all: {worst_all:.1f}")
    if js:
        with open(js, "w") as fh:
            json.dump(out, fh, indent=1)


BEAM_BOXES = ("beam_22", "beam_31", "beam_40", "beam_49", "beam_58")
BEAM_NEAR = ("road_ahead_14", "road_left_4", "road_right_4", "outside_L", "outside_R", "verge_L", "verge_R")


def _usable(r, road_only=True):
    if r is None or r["m"] is None or not (r["visible"] and r["inFrame"]) or r.get("clean") is False:
        return False
    return (not road_only) or ("road" in (r.get("surface") or "").lower())


def _rcode(m):
    return 255.0 * float(enc(m["rgb_lin"][0]))


def cmd_beam(d, js):
    """THE LOW BEAM AND THE TAIL LAMPS (the colour pass, C5/C6), off the night
    frames of a colour-shots run: each spot's _dark frame (its own lamps off)
    against its _lit ones (the shipped intensity, and every _biNNN of a beam
    sweep) and its _lit_brake one, box by box on the same road.
      pool     Ylin lit / Ylin dark on each beam box that lies clean on road
               (the NFS reference: 2.3-3.6x the unlit road)
      flat     max / min of the lit Ylin over those boxes (<= 1.35), and of
               the beam's own share (lit - dark)
      reach    the 58 m box's pool (the road readable to ~55 m)
      spill    outside_L/R lit / dark (15 m ahead, 2 m past the 34 deg edge)
      tail     the road behind the car (tail_2): dim and braking against the
               dark frame, in Ycode and in the red channel's code, and R-G."""
    out = {}
    for dark in sorted(glob.glob(os.path.join(d, "cs_*_night_*_dark_g1_world.png"))):
        stem = os.path.basename(dark)[:-len("_dark_g1_world.png")]
        D = Frame(dark)
        rd = D.region_table()
        lits = [p for p in sorted(glob.glob(os.path.join(d, stem + "_lit*_g1_world.png")))
                if not any(k in os.path.basename(p) for k in ("_lens", "_onc", "_alpha"))]
        if not lits:
            continue
        print(f"\n{stem}  (dark: {os.path.basename(dark)})")
        res = {}
        for p in lits:
            L = Frame(p)
            if L.target != D.target:
                raise SystemExit(f"REFUSED: {p} and {dark} are different texture imports")
            rl = L.region_table()
            tag = os.path.basename(p)[len(stem):-len("_g1_world.png")]
            bi = ((L.sc or {}).get("variant") or {}).get("beamIntensity")
            row = {"beamIntensity": bi, "boxes": {}}
            if "_brake" not in tag:
                pools, lits_y, own = [], [], []
                for n in BEAM_BOXES + BEAM_NEAR:
                    a, b = rd.get(n), rl.get(n)
                    road = n not in ("verge_L", "verge_R", "outside_L", "outside_R")
                    if not (_usable(a, road) and _usable(b, road)):
                        continue
                    ya, yb = a["m"]["Ylin"], b["m"]["Ylin"]
                    ratio = yb / max(ya, 1e-6)
                    row["boxes"][n] = {"dark": a["m"]["Ycode_mean"], "lit": b["m"]["Ycode_mean"], "ratio": ratio,
                                       "b": b["m"]["b"], "a": b["m"]["a"], "surface": b["surface"]}
                    if n in BEAM_BOXES:
                        pools.append(ratio); lits_y.append(yb); own.append(max(yb - ya, 1e-6))
                if pools:
                    row["pool_min"], row["pool_max"], row["pool_med"] = min(pools), max(pools), float(np.median(pools))
                    row["flat"] = max(lits_y) / min(lits_y)
                    row["flat_own"] = max(own) / min(own)
                    row["boxes_used"] = len(pools)
                bx = row["boxes"]
                line = "  ".join(f"{n[5:] if n.startswith('beam_') else n} {bx[n]['dark']:.0f}->{bx[n]['lit']:.0f} x{bx[n]['ratio']:.2f}"
                                 for n in BEAM_BOXES + BEAM_NEAR if n in bx)
                head = (f"  {tag:14s} BI {bi if bi is not None else '?'}: "
                        + (f"pool x{row['pool_min']:.2f}-{row['pool_max']:.2f} (med {row['pool_med']:.2f}, {row['boxes_used']} boxes)  "
                           f"flat {row['flat']:.2f} (beam's own {row['flat_own']:.2f})" if pools else "no clean beam box on road"))
                print(head)
                print("      " + line)
                hb = [f"{n} b* {bx[n]['b']:+.1f} a* {bx[n]['a']:+.1f}" for n in BEAM_BOXES if n in bx]
                if hb:
                    print("      headlit colour: " + ", ".join(hb))
                # The beam's own irradiance at each box centre (CarLights.BeamLightAt,
                # the shader's arithmetic on the CPU): its shape apart from the road.
                model = {r["name"]: r.get("beamModel") for r in L.regions() if r.get("beamModel") is not None}
                mv = [(n, model[n]) for n in ("road_ahead_14",) + BEAM_BOXES + ("outside_L", "outside_R") if n in model]
                if mv and any(v > 0 for _, v in mv):
                    row["model"] = dict(mv)
                    bm = [v for n, v in mv if n in BEAM_BOXES and v > 0]
                    mflat = (max(bm) / min(bm)) if bm else None
                    row["model_flat"] = mflat
                    print("      beam model (lin): " + "  ".join(f"{n.replace('beam_', '').replace('road_ahead_', 'r')} {v:.3f}" for n, v in mv)
                          + (f"   flat {mflat:.2f}" if mflat else ""))
            a, b = rd.get("tail_2"), rl.get("tail_2")
            if _usable(a, True) and _usable(b, True):
                dy = b["m"]["Ycode_mean"] - a["m"]["Ycode_mean"]
                dr = _rcode(b["m"]) - _rcode(a["m"])
                row["tail"] = {"dY": dy, "dR": dr, "RminusG": b["m"]["RminusG_code"], "dark": a["m"]["Ycode_mean"], "lit": b["m"]["Ycode_mean"]}
                kind = "brake" if "_brake" in tag else "dim"
                lim = 40 if kind == "brake" else 15
                ok = dy <= lim and (kind == "brake" or dr <= 25) and b["m"]["RminusG_code"] >= 8
                print(f"  {tag:14s} tail_2 ({kind}): {a['m']['Ycode_mean']:.1f} -> {b['m']['Ycode_mean']:.1f}  dY {dy:+.1f} (<= +{lim})  "
                      f"dR {dr:+.1f}{' (<= +25)' if kind == 'dim' else ''}  R-G {b['m']['RminusG_code']:+.1f} (>= 8)  {'ok' if ok else 'MISS'}")
            res[tag] = row
        out[stem] = res
    if js:
        with open(js, "w") as fh:
            json.dump(out, fh, indent=1)
    return out


def cmd_fx(d, js, before_dir=None):
    """LIT PARTICLES (the colour pass, C7): every frame with particles against
    its twin without them - <x>_g1_world vs <x>_noflk_g1_world (falling snow)
    and <x>_smoke_g1_world vs <x>_g1_world (tyre smoke). The particle pixels
    are those the particles moved by more than 3 codes; on them, Ylin with /
    without: the median and p90, overall and where the background is dark
    (Ycode < 30 without: 'outside all light' - target p90 <= 1.5 at night)."""
    out = {}
    pairs = []
    for p in sorted(glob.glob(os.path.join(d, "*_noflk_g1_world.png"))):
        pairs.append((p.replace("_noflk_g1_world.png", "_g1_world.png"), p, "flakes"))
    for p in sorted(glob.glob(os.path.join(d, "*_smoke_g1_world.png"))):
        pairs.append((p, p.replace("_smoke_g1_world.png", "_g1_world.png"), "smoke"))
    for with_p, without_p, kind in pairs:
        if not (os.path.exists(with_p) and os.path.exists(without_p)):
            continue
        A, B = Frame(with_p), Frame(without_p)
        if A.target != B.target or A.rgb.shape != B.rgb.shape:
            continue
        diff = np.abs(A.Yc - B.Yc)
        mask = diff > 3.0
        share = float(mask.mean())
        row = {"kind": kind, "share": share}
        if mask.sum() >= 30:
            r = A.Y[mask] / np.maximum(B.Y[mask], 1e-5)
            row.update({"med": float(np.median(r)), "p90": float(np.percentile(r, 90)),
                        "with_code_med": float(np.median(A.Yc[mask])), "without_code_med": float(np.median(B.Yc[mask]))})
            dark = mask & (B.Yc < 30)
            if dark.sum() >= 30:
                rd = A.Y[dark] / np.maximum(B.Y[dark], 1e-5)
                row.update({"dark_share": float(dark.mean()), "dark_med": float(np.median(rd)), "dark_p90": float(np.percentile(rd, 90)),
                            "dark_with_code_med": float(np.median(A.Yc[dark]))})
            # the particles' colour where they are (display-coded mean of A over the mask)
            m = A.lin[mask].mean(0)
            L = lab(m[None, :])[0]
            row["a"], row["b"] = float(L[1]), float(L[2])
        # A before-run (PSX_LITFX=0, unlit particles) given: read BOTH on the
        # before-run's particle footprint - a lit flake in the dark is too
        # faint to pass the 3-code threshold, so a mask taken off the lit
        # frame would keep only the few it cannot darken.
        if before_dir:
            ub = os.path.join(before_dir, os.path.basename(with_p))
            if os.path.exists(ub):
                U = Frame(ub)
                if U.rgb.shape == B.rgb.shape:
                    um = np.abs(U.Yc - B.Yc) > 3.0
                    if um.sum() >= 30:
                        bg = float(B.Y[um].mean())
                        row["foot_px"] = int(um.sum())
                        row["foot_unlit_over_bg"] = float(U.Y[um].mean()) / max(bg, 1e-6)
                        row["foot_lit_over_bg"] = float(A.Y[um].mean()) / max(bg, 1e-6)
                        row["foot_codes"] = [float(ycode_of_lin(bg)), float(ycode_of_lin(U.Y[um].mean())), float(ycode_of_lin(A.Y[um].mean()))]
        name = os.path.basename(with_p)
        out[name] = row
        s = f"  {kind:6s} {name:58s} covers {100*share:5.2f}%"
        if "med" in row:
            s += (f"  with/without x{row['med']:.2f} (p90 {row['p90']:.2f})  codes {row['without_code_med']:.0f}->{row['with_code_med']:.0f}"
                  f"  a* {row['a']:+.1f} b* {row['b']:+.1f}")
            if "dark_med" in row:
                s += f"  | over dark ({100*row['dark_share']:.2f}%): x{row['dark_med']:.2f} p90 {row['dark_p90']:.2f} -> code {row['dark_with_code_med']:.0f}"
        if "foot_px" in row:
            c = row["foot_codes"]
            s += (f"\n         on the unlit particles' footprint ({row['foot_px']} px): behind {c[0]:.0f}, unlit {c[1]:.0f} "
                  f"(x{row['foot_unlit_over_bg']:.2f}), lit {c[2]:.0f} (x{row['foot_lit_over_bg']:.2f})")
        print(s)
    if js:
        with open(js, "w") as fh:
            json.dump(out, fh, indent=1)
    return out


def _dilate(m, r):
    out = m.copy()
    h, w = m.shape
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx == 0 and dy == 0:
                continue
            ys, ye = max(0, dy), h + min(0, dy)
            xs, xe = max(0, dx), w + min(0, dx)
            out[ys:ye, xs:xe] |= m[ys - dy:ye - dy, xs - dx:xe - dx]
    return out


def _wcag(lt, ls):
    hi, lo = max(lt, ls), min(lt, ls)
    return (hi + 0.05) / (lo + 0.05)


def cmd_hud(d, js):
    """THE HUD'S LEGIBILITY (the colour pass, C9), off each _hud frame and its
    _world twin (the HUD is what differs between them):
      text   the top band's labels: glyph pixels (HUD, brighter than the world
             under them, Ycode >= 140) against their 2-px surround (the ring
             round the glyphs: the outline and whatever is behind it) - WCAG
             (L1 + .05) / (L2 + .05) on linear luminance, target >= 4.5
      dials  the bottom band, left (tach) and right (speedo): the white
             numerals and ticks against the 2-px ring round them inside the
             face, target >= 4.5; and the smoked face's own display luma
             (target <= .45 at noon and in snow)."""
    out = {}
    for hp in sorted(glob.glob(os.path.join(d, "*_g1_hud.png"))):
        wp = hp[:-len("_hud.png")] + "_world.png"
        if not os.path.exists(wp):
            continue
        H, W = Frame(hp, need_sidecar=False), Frame(wp, need_sidecar=False)
        if H.rgb.shape != W.rgb.shape:
            continue
        h, w = H.h, H.w
        mask = np.abs(H.rgb - W.rgb).max(-1) > 3 / 255.0
        brighter = H.Y > W.Y
        # white (or the night bulb's amber) glyphs and marks - not the green
        # fuel bar, not the red needle and redline
        R, G, B = H.rgb[..., 0], H.rgb[..., 1], H.rgb[..., 2]
        grey = ~((G > R + 0.12) & (G > B + 0.12)) & ~(R > G + 0.30)
        row = {}
        # the labels, by thirds of the top band
        top = np.zeros_like(mask); top[: int(0.14 * h), :] = True
        for lab_, x0, x1 in (("text_left", 0, w // 3), ("text_mid", w // 3, 2 * w // 3), ("text_right", 2 * w // 3, w)):
            band = top.copy(); band[:, :x0] = False; band[:, x1:] = False
            glyph = mask & brighter & grey & (H.Yc >= 140) & band
            if glyph.sum() < 12:
                continue
            # The text's whole footprint (its anti-aliased fringe included):
            # every HUD pixel brighter than the world under it. The EDGE is
            # the 1-px ring outside that footprint; how much of it the HUD
            # darkened is the edge's coverage, and the contrast is the glyph
            # core against the darkened edge pixels.
            foot = mask & brighter & band
            ring = _dilate(foot, 1) & ~foot & band
            edge = ring & mask & ~brighter
            ring2 = _dilate(glyph, 2) & ~glyph & band
            lt, ls2 = float(np.median(H.Y[glyph])), float(np.median(H.Y[ring2]))
            ls = float(np.median(H.Y[edge])) if edge.sum() >= 6 else float(np.median(H.Y[ring])) if ring.sum() else 1.0
            row[lab_] = {"contrast": _wcag(lt, ls), "contrast2": _wcag(lt, ls2), "glyph": lt, "surround": ls,
                         "edge_cover": float(edge.sum() / max(ring.sum(), 1)), "px": int(glyph.sum())}
        # the dials
        bot = np.zeros_like(mask); bot[int(0.60 * h):, :] = True
        for lab_, x0, x1 in (("dial_left", 0, w // 2), ("dial_right", w // 2, w)):
            band = bot.copy(); band[:, :x0] = False; band[:, x1:] = False
            face = mask & ~brighter & band
            marks = mask & brighter & grey & (H.Yc >= 150) & band
            if marks.sum() < 12 or face.sum() < 50:
                continue
            foot = mask & brighter & band
            ring = _dilate(foot & _dilate(marks, 1), 1) & ~foot & band
            edge = ring & mask & ~brighter
            ring2 = _dilate(marks, 2) & ~marks & mask & band
            lt, ls2 = float(np.median(H.Y[marks])), float(np.median(H.Y[ring2]))
            ls = float(np.median(H.Y[edge])) if edge.sum() >= 6 else float(np.median(H.Y[ring])) if ring.sum() else 1.0
            row[lab_] = {"contrast": _wcag(lt, ls), "contrast2": _wcag(lt, ls2), "marks": lt, "surround": ls,
                         "edge_cover": float(edge.sum() / max(ring.sum(), 1)),
                         "face_luma": float(np.median(enc(H.Y[face]))), "behind_luma": float(np.median(enc(W.Y[face]))),
                         "px": int(marks.sum())}
        out[os.path.basename(hp)] = row
        parts = []
        for k, v in row.items():
            s = f"{k} {v['contrast']:.1f}:1 (edge {100*v.get('edge_cover', 0):.0f}%, {v['contrast2']:.1f} at 2px)"
            if "face_luma" in v:
                s += f" (face {v['face_luma']:.2f} over {v['behind_luma']:.2f})"
            parts.append(s)
        print(f"  {os.path.basename(hp):52s} " + "  ".join(parts))
    if js:
        with open(js, "w") as fh:
            json.dump(out, fh, indent=1)
    return out


def cmd_shade(d, js):
    """THE HARSH SUN'S NUMBERS (review, 2026-09-29): each daylight frame
    against its no-shadow twin (<x>_flat_<...>: the sun map and the sky map
    off, ColourShots' Variant.flat), pixel by pixel.

    Near the car - a window round its projected collider ("car_box" in the
    sidecar), one car-width to each side and a car-height below - and outside
    the car's own rectangle:
      shade   pixels at most 0.6 of their no-shadow twin's luminance: the
              car's shadow (and any other shadow that falls there)
      sun     pixels within 3% of their twin (lit), whose twin luminance is
              within 12% of the shade pixels' twin median - the same surface
              in the sun
    Reported: the shadow's Ycode (target 70-95 on the S1 ConcreteNew deck),
    sun:shade in linear (target >= 5), shade b* - sun b* (target <= -3), and
    the pixel counts. The twin differs only by the maps (same pose, hour,
    exposure), so a pixel's ratio IS its shadow."""
    out = {}
    for fp in sorted(glob.glob(os.path.join(d, "*_flat*_g1_world.png"))):
        name = os.path.basename(fp)
        twin = name.replace("_flat", "", 1)
        tp = os.path.join(d, twin)
        if not os.path.exists(tp) or "_field" in name:
            continue
        A, Fl = Frame(tp), Frame(fp)
        if A.target != Fl.target or A.rgb.shape != Fl.rgb.shape:
            continue
        box = next((r for r in A.regions() if r["name"] == "car_box"), None)
        if box is None:
            print(f"  {twin}: no car_box in the sidecar (an older run)"); continue
        h, w = A.h, A.w
        x0, y0, x1, y1 = box["box"]
        X0, X1, Y0, Y1 = int(x0 * w), int(x1 * w), int(y0 * h), int(y1 * h)
        cw, ch = X1 - X0, Y1 - Y0
        WX0, WX1 = max(0, X0 - cw), min(w, X1 + cw)
        WY0, WY1 = max(0, Y0 + ch // 3), min(h, Y1 + ch)
        win = np.zeros((h, w), bool)
        win[WY0:WY1, WX0:WX1] = True
        win[Y0:Y1, X0:X1] = False
        ya, yf = A.Y4, Fl.Y4          # Box4: the dither averaged out
        ratio = ya / np.maximum(yf, 1e-5)
        shade = win & (ratio <= 0.6) & (yf > 0.02)
        row = {"window": [WX0, WY0, WX1, WY1], "car": [X0, Y0, X1, Y1], "shade_px": int(shade.sum())}
        if shade.sum() >= 40:
            ref = float(np.median(yf[shade]))
            sun = win & (np.abs(ratio - 1) <= 0.03) & (np.abs(yf / ref - 1) <= 0.12)
            row["sun_px"] = int(sun.sum())
            if sun.sum() >= 40:
                ys, yu = float(np.median(ya[shade])), float(np.median(ya[sun]))
                ls = lab(A.b4[shade].mean(0)[None, :])[0]
                lu = lab(A.b4[sun].mean(0)[None, :])[0]
                row.update(shadow_code=float(ycode_of_lin(ys)), sun_code=float(ycode_of_lin(yu)),
                           sun_shade=yu / max(ys, 1e-6), db=float(ls[2] - lu[2]))
        out[twin] = row
        if "sun_shade" in row:
            ok = (70 <= row["shadow_code"] <= 95) and row["sun_shade"] >= 5 and row["db"] <= -3
            print(f"  {twin[:52]:52s} shadow {row['shadow_code']:.0f} (70-95)  sun {row['sun_code']:.0f}  "
                  f"sun:shade {row['sun_shade']:.2f} (>= 5)  shade b* - sun b* {row['db']:+.1f} (<= -3)  "
                  f"[{row['shade_px']} / {row['sun_px']} px]  {'ok' if ok else 'MISS'}")
        else:
            print(f"  {twin[:52]:52s} too few shadow / sun pixels near the car ({row.get('shade_px')} / {row.get('sun_px', 0)})")
    if js:
        with open(js, "w") as fh:
            json.dump(out, fh, indent=1)
    return out


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
    if cmd == "pairs":
        cmd_pairs(rest[0], rest[1], rest[2] if len(rest) > 2 else "*_world.png", js); return 0
    if cmd == "beam":
        cmd_beam(rest[0], js); return 0
    if cmd == "fx":
        cmd_fx(rest[0], js, rest[1] if len(rest) > 1 else None); return 0
    if cmd == "hud":
        cmd_hud(rest[0], js); return 0
    if cmd == "shade":
        cmd_shade(rest[0], js); return 0
    print(__doc__); return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
