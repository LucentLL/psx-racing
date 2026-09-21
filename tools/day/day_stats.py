"""The day look, measured (2026-09-21, the Forza daylight pass).

    py tools/day/day_stats.py C:\\Users\\mcgee\\PSXBuild\\Screenshots

DayLookShots writes every frame twice: dl_*_shadow.png as the game draws it and
dl_*_flat.png with the sun's shadow map switched off and nothing else changed.
The PAIR is the measurement - the pixels that differ are the pixels the map put
in shade, so without knowing where anything is in the picture this reads off:

    shade %   how much of the frame the map darkened
    ratio     sunlit / shaded LINEAR light over those pixels (flat / shadow)
    r-b       how much bluer the shade is than the same pixels in the sun
              (display rgb, red minus blue, shadow minus flat: negative = bluer)

WHAT THE REFERENCE FRAMES MEASURE. The owner's five Forza Horizon frames are
not in the repository (a public repo, somebody else's frames); these numbers
were read off them with the same arithmetic:

    clear, low sun     road in sun  rgb .284 .258 .256   linear Y .057
                       car's shadow rgb .118 .134 .162   linear Y .016
                       -> 3.5 : 1, and the shade is BLUE (r-b -0.044 against
                          +0.028 in the sun)
                       sky by the sun 0.99, sky 40 degrees round .53
                       far hills toward the sun .93, near hills .45
    overcast, wet      no cast shadow anywhere; a contact patch under the car
                       (linear .003 against the road's .125); near hills .47,
                       far .58, both nearly colourless (sat .03)
    avenue, dappled    sun patch 1.3 : 1 over tree shade (petals on the road),
                       forest shade linear .007, the sky .99
    tunnel             wall deep inside linear .041, the day outside .98
                       (24 : 1); the wall by the mouth warm (.49 .41 .30)
    gallery            road in shade .002, in a shaft .037 (18 : 1), a lit
                       pillar warm (.67 .59 .49)
    every frame        floor .004-.019, ceiling 1.0, 2-9% of the frame over
                       .95, median .21-.45, mean saturation .22-.31

OURS BEFORE THE PASS (psx_hour_1..3): floor .12-.14, ceiling .93, NOTHING over
.95, saturation .13-.16, and no pixel anywhere in the shade of anything.

The film grade (the owner's, 2026-09-19) lifts the floor to ~.12 and holds the
ceiling at ~.93 on purpose, so the DISPLAY ratio here is smaller than Forza's
for the same light; the linear ratio is computed after undoing sRGB only, so it
is compressed by the grade too. The verdict bands below are for THAT number,
through the grade: a clear-day frame wants its shade between 1.3 and 4 to 1 and
bluer than its sun; an overcast frame wants almost none. (1.3, not the 1.5 it
started at: a frame whose only shade is a car's shadow on FRESH tarmac measures
1.3-1.5 however deep the shadow is, because that tarmac - the owner's own
#1e1e22 - sits on the grade's floor in full sun. And 4, not 3.2: the inside of
a tunnel is 3.3 by design, the sky's light gone as well as the sun's.)
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

W = np.array([0.2126, 0.7152, 0.0722])


def lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float64) / 255.0


def frame_line(a):
    Y = a @ W
    mx, mn = a.max(-1), a.min(-1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    q = np.quantile(Y, [0.001, 0.5, 0.999])
    return q[0], q[1], q[2], float(sat.mean()), float((Y > 0.95).mean())


def main():
    d = sys.argv[1] if len(sys.argv) > 1 else "."
    shadows = sorted(glob.glob(os.path.join(d, "dl_*_shadow.png")))
    if not shadows:
        print("no dl_*_shadow.png in", d)
        return 1
    print("DAY LOOK - each frame against itself with the shadow map off")
    print(f"{'frame':44s} {'floor':>6s} {'median':>6s} {'ceil':>5s} {'sat':>5s} {'shade%':>7s} {'ratio':>6s} {'r-b':>7s}  verdict")
    bad = 0
    for sp in shadows:
        fp = sp.replace("_shadow.png", "_flat.png")
        name = os.path.basename(sp)[3:-len("_shadow.png")]
        a = load(sp)
        floor, med, ceil, sat, hot = frame_line(a)
        if not os.path.exists(fp):
            print(f"{name:44s} {floor:6.3f} {med:6.3f} {ceil:5.2f} {sat:5.2f}   (no flat twin)")
            continue
        b = load(fp)
        # The HUD and the dials are identical in both, so they drop out here.
        Ya, Yb = lin(a) @ W, lin(b) @ W
        shaded = (Yb - Ya) > np.maximum(0.004, 0.06 * Yb)
        share = float(shaded.mean())
        if share > 0.0005:
            ratio = float(Yb[shaded].mean() / max(Ya[shaded].mean(), 1e-6))
            rb = float((a[shaded][:, 0] - a[shaded][:, 2]).mean() - (b[shaded][:, 0] - b[shaded][:, 2]).mean())
        else:
            ratio, rb = 1.0, 0.0
        rain = name.endswith("_rain")
        notes = []
        if rain:
            if share > 0.02 and ratio > 1.25:
                notes.append("overcast should cast almost nothing")
        else:
            if share < 0.003:
                notes.append("NOTHING is in shadow")
            else:
                if ratio < 1.3:
                    notes.append("shade too weak")
                if ratio > 4.0:
                    notes.append("shade too deep")
                if rb > 0.0:
                    notes.append("shade is not bluer than the sun")
        if notes:
            bad += 1
        print(f"{name:44s} {floor:6.3f} {med:6.3f} {ceil:5.2f} {sat:5.2f} {share * 100:6.1f}% {ratio:6.2f} {rb:+7.3f}  "
              + ("; ".join(notes) if notes else "ok"))
    print(f"\n{len(shadows) - bad} of {len(shadows)} day frames inside every band")
    return 0


if __name__ == "__main__":
    sys.exit(main())
