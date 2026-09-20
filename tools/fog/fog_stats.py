"""How much of a frame the distance fog is actually painting.

    py tools\fog\fog_stats.py CityCircuit MtMitchell

Reads the contact sheet tools\fog-shots.ps1 leaves in the sandbox's
Screenshots folder and, for every venue and station, compares each fog variant
against the no-fog control of the SAME view: mean absolute RGB delta, and the
share of pixels the fog moved by more than 12/255.

This exists because the first look at this was by eye and the eye was wrong.
At a driver's eye height on a road lined with walls and trees, almost nothing
in frame is even as far away as fogNear, and every variant measured under 1%
of pixels -- the fog is nearly invisible from the seat. It is the view with
DISTANCE in it (station 3, the camera 30 m up) that carries the complaint:
there the old band moved 25% of the frame. Tuning a band off the shots where
it does nothing would have produced a number that looked fine and fixed
nothing.

The HUD is masked out. The gauges, the race map and the fuel bar are drawn
over the picture and never fog; leaving them in only dilutes the number.
"""
import sys
import os
import re
import glob
import numpy as np
from PIL import Image

SHOTS = r"C:\Users\mcgee\PSXBuild\Screenshots"
CONTROL = "7nofog"
MOVED = 12          # a channel step a player could see, out of 255


def load(path):
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.int16)


def picture_mask(h, w):
    """True where the rendered world is, False over the HUD furniture."""
    m = np.ones((h, w), dtype=bool)
    m[int(h * 0.62):, :int(w * 0.42)] = False      # tachometer
    m[int(h * 0.62):, int(w * 0.66):] = False      # speedometer
    m[:int(h * 0.55), :int(w * 0.36)] = False      # race map
    m[:int(h * 0.12), int(w * 0.75):] = False      # fuel bar
    return m


def venue(name):
    pattern = os.path.join(SHOTS, "psx_fog_%s_*_%s.png" % (name, CONTROL))
    controls = sorted(glob.glob(pattern))
    if not controls:
        print("\n%s: no shots (run tools\\fog-shots.ps1 -Venue %s)" % (name, name))
        return
    for control in controls:
        station = re.search(r"_(\d+)_%s\.png$" % CONTROL, control).group(1)
        base = load(control)
        h, w, _ = base.shape
        mask = picture_mask(h, w)
        print("\n%s station %s  (%d x %d)" % (name, station, w, h))
        for shot in sorted(glob.glob(os.path.join(
                SHOTS, "psx_fog_%s_%s_*.png" % (name, station)))):
            label = re.search(r"_([0-9][A-Za-z0-9]*)\.png$", shot).group(1)
            if label == CONTROL:
                continue
            delta = np.abs(load(shot) - base).max(axis=2)[mask]
            print("   %-12s mean %5.2f   px>%d/255 %5.1f%%   worst %3d"
                  % (label, delta.mean(), MOVED,
                     100.0 * (delta > MOVED).mean(), delta.max()))


if __name__ == "__main__":
    for v in (sys.argv[1:] or ["CityCircuit"]):
        venue(v)
