# The night look, in numbers: the tone signature of each NightLookShots frame
# scored against what the Need for Speed (2015) reference frames measured.
#
#   py tools/night/night_stats.py C:\Users\mcgee\PSXBuild\Screenshots\nl_*.png
#   py tools/night/night_stats.py -v frame.png ...     (per-region detail too)
#
# (Wildcards are expanded here as well: PowerShell hands a native command its
# pattern unexpanded.)
#
# WHY NUMBERS. The owner's ask (2026-09-21): "I like how dark the night is,
# how much the skybox effects the color and mood of the world and cars, how
# street lights bathe the road." Dark is a SHARE of the picture, not a mean:
# a night that is 40% black with lit pools and lamps to 0.95 reads as night,
# and one that is uniformly 0.2 grey reads as fog, though the two can have the
# same average. So the verdict is on the floor, the median, how much of the
# frame is under 0.10 display luma, and saturation.
#
# THE REFERENCE FRAMES ARE NOT IN THIS REPOSITORY. The repository is public and
# they are copyrighted frames; what they measured is written down below instead
# (the same maths, <SCRATCH>/nfs_stats.py, run over them on 2026-09-21):
#   NIGHT, city (1, 2) and mountain (5):
#     display-luma floor (0.1th percentile) 0.008-0.015, median 0.09-0.17,
#     27-54% of the frame under 0.10, 60-94% under 0.20, highlights to 0.95+,
#     mean saturation 0.50-0.58.
#     City darks are WARM - a sodium murk, rgb ~(0.037, 0.026, 0.002).
#     Mountain darks are neutral to magenta under a blue-grey sky.
#   BLUE DUSK (3): mids rgb ~(0.20, 0.22, 0.31), sky mid ~(0.25, 0.39, 0.59),
#     and the road reflects the blue sky.
#   SODIUM FREEWAY (4): everything orange.
#   OURS BEFORE THE PASS (psx_hour_6_night): floor 0.108, median 0.226,
#     0% under 0.10, saturation 0.13 - a milky grey; the film grade's matte
#     lift (0.112) alone forbade anything darker than 0.11.
#
# Regions, as nfs_stats.py cut them (no letterbox crop: a night frame is dark
# everywhere and the stock cropper eats it):
#   whole = everything but the bottom 18% (the car and the gauges)
#   sky   = top 22% of the frame, middle 60% of the width
#   road  = rows 62-80%, middle 40% of the width
import glob
import os
import sys

import numpy as np
from PIL import Image

# ---- the measured targets -------------------------------------------------
NIGHT = {
    "floor":  (0.008, 0.015),   # 0.1th percentile display luma
    "median": (0.09, 0.17),
    "lt10":   (0.27, 0.54),     # share of the frame under 0.10
    "lt20":   (0.60, 0.94),     # share under 0.20
    "sat":    (0.50, 0.58),     # mean saturation
}
HIGHLIGHT_MIN = 0.95            # the 99.9th percentile should reach this
DUSK_MIDS = (0.20, 0.22, 0.31)  # whole-frame 40-60% band, rgb
DUSK_SKY = (0.25, 0.39, 0.59)   # sky region 40-60% band, rgb
DUSK_TOL = 0.08                 # per channel, for a "near" verdict
SODIUM_MURK = (0.037, 0.026, 0.002)


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32) / 255.0


def luma(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def saturation(a):
    mx, mn = a.max(-1), a.min(-1)
    return np.where(mx > 1e-3, (mx - mn) / np.maximum(mx, 1e-3), 0)


def band_rgb(a, Y, lo, hi):
    m = (Y >= np.percentile(Y, lo)) & (Y <= np.percentile(Y, hi))
    return a[m].mean(0) if m.any() else np.zeros(3)


def sig(name, a):
    """nfs_stats.py's per-region print, unchanged in substance."""
    Y = luma(a)
    sat = saturation(a)
    p = np.percentile(Y, [0.1, 1, 5, 25, 50, 75, 95, 99, 99.9])
    print(f"  {name:6s} Y .1/1/5/25/50/75/95/99/99.9 = " + " ".join(f"{v:.3f}" for v in p)
          + f"  mean {Y.mean():.3f}  sat {sat.mean():.3f}")
    for nm, lo, hi in (("dark5", 0, 5), ("mid", 40, 60), ("top3", 97, 100)):
        c = band_rgb(a, Y, lo, hi)
        s = (c.max() - c.min()) / max(c.max(), 1e-3)
        print(f"         {nm:5s} rgb {c[0]:.3f} {c[1]:.3f} {c[2]:.3f}  sat {s:.2f}  r-b {c[0]-c[2]:+.3f}")


def verdict(v, band):
    lo, hi = band
    return "ok" if lo <= v <= hi else ("HI" if v > hi else "LO")


def kind_of(path):
    n = os.path.basename(path).lower()
    hour = "dusk" if "_dusk" in n else ("night" if "_night" in n else "other")
    where = "mountain" if "blueridge" in n else "city"
    return hour, where


def measure(path, verbose):
    a = load(path)
    H, W, _ = a.shape
    whole = a[: int(H * 0.82)]
    sky = a[: int(H * 0.22), int(W * 0.2): int(W * 0.8)]
    road = a[int(H * 0.62): int(H * 0.80), int(W * 0.30): int(W * 0.70)]
    if verbose:
        print(f"{path}  {W}x{H}")
        sig("whole", whole)
        sig("sky", sky)
        sig("road", road)
    Y = luma(whole)
    r = {
        "floor": float(np.percentile(Y, 0.1)),
        "median": float(np.percentile(Y, 50)),
        "lt10": float(np.mean(Y < 0.10)),
        "lt20": float(np.mean(Y < 0.20)),
        "hi": float(np.percentile(Y, 99.9)),
        "sat": float(saturation(whole).mean()),
        "dark": band_rgb(whole, Y, 0, 5),
        "mid": band_rgb(whole, Y, 40, 60),
        "skymid": band_rgb(sky, luma(sky), 40, 60),
        "road": float(luma(road).mean()),
    }
    if verbose:
        print(f"  share Y<0.05 {np.mean(Y < 0.05):.2f}  Y<0.10 {r['lt10']:.2f}  Y<0.20 {r['lt20']:.2f}"
              f"  Y>0.5 {np.mean(Y > 0.5):.3f}  Y>0.8 {np.mean(Y > 0.8):.3f}")
    return r


def near(c, target, tol):
    return all(abs(float(c[i]) - target[i]) <= tol for i in range(3))


def main(argv):
    verbose = "-v" in argv
    paths = []
    for arg in argv:
        if arg == "-v":
            continue
        hits = sorted(glob.glob(arg)) if any(ch in arg for ch in "*?[") else [arg]
        paths.extend(h for h in hits if os.path.isfile(h))
    if not paths:
        print("night_stats: no frames (pass the nl_*.png files, or a pattern)")
        return 1

    rows = []
    for p in paths:
        rows.append((p, measure(p, verbose)))

    print()
    print("NIGHT LOOK vs the reference bands (floor .008-.015, median .09-.17, <0.10 27-54%, sat .50-.58)")
    print(f"{'frame':46s} {'floor':>6s} {'median':>6s} {'<0.10':>6s} {'<0.20':>6s} {'hi':>5s} {'sat':>5s}"
          f"  {'darks rgb':17s} verdict")
    in_band = 0
    judged = 0
    for p, r in rows:
        hour, where = kind_of(p)
        d = r["dark"]
        name = os.path.basename(p)
        head = (f"{name[:46]:46s} {r['floor']:6.3f} {r['median']:6.3f} {r['lt10']:6.2f} {r['lt20']:6.2f}"
                f" {r['hi']:5.2f} {r['sat']:5.2f}  {d[0]:.3f} {d[1]:.3f} {d[2]:.3f} ")
        if hour == "night":
            v = {k: verdict(r[k], NIGHT[k]) for k in ("floor", "median", "lt10", "sat")}
            notes = []
            if r["hi"] < HIGHLIGHT_MIN:
                notes.append("no highlight reaches .95")
            # City darks should be warm (sodium: r > g > b); a mountain's
            # neutral or faintly magenta, never orange (skyglow leaking out
            # of town).
            if where == "city" and not (d[0] >= d[2]):
                notes.append("city darks are not warm")
            if where == "mountain" and d[0] - d[2] > 0.02:
                notes.append("mountain darks are orange")
            judged += 1
            if all(x == "ok" for x in v.values()):
                in_band += 1
            print(head + " ".join(f"{k}:{x}" for k, x in v.items())
                  + ("  [" + "; ".join(notes) + "]" if notes else ""))
        elif hour == "dusk":
            mids_ok = near(r["mid"], DUSK_MIDS, DUSK_TOL) and r["mid"][2] > r["mid"][0]
            sky_ok = near(r["skymid"], DUSK_SKY, DUSK_TOL * 1.5) and r["skymid"][2] > r["skymid"][0]
            m, s = r["mid"], r["skymid"]
            print(head + f"dusk mids {m[0]:.2f},{m[1]:.2f},{m[2]:.2f}:{'ok' if mids_ok else 'OFF'}"
                  f"  sky {s[0]:.2f},{s[1]:.2f},{s[2]:.2f}:{'ok' if sky_ok else 'OFF'}")
        else:
            print(head + "(no hour in the name - not judged)")
    print()
    print(f"{in_band} of {judged} night frames inside every band "
          f"(before the pass: psx_hour_6_night floor 0.108, median 0.226, 0% under 0.10, sat 0.13)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
