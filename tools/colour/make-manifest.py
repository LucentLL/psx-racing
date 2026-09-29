"""Turn WebGL-target editor frames into the harness's shot list.

    py tools/colour/make-manifest.py <colour_WebGL dir> <out shots.json> [glob ...]

For every editor frame (cs_*_g1_world.png by default, or the globs given)
the list gets one player shot with the SAME venue, spot, pose, hour,
weather, dress and lights, the grade on, the lens off and the HUD hidden -
the conditions the editor frame was drawn under - named after the editor
frame, so tools/colour/colour_stats.py agree can pair them by name.
"""
import glob
import json
import os
import sys


def main(argv):
    if len(argv) < 3:
        print(__doc__); return 2
    src, out = argv[1], argv[2]
    pats = argv[3:] or ["cs_*_g1_world.png"]
    files = []
    for p in pats:
        files += sorted(glob.glob(os.path.join(src, p)))
    shots = []
    for f in files:
        sc_path = f + ".json"
        if not os.path.exists(sc_path):
            print("skip (no sidecar):", f); continue
        sc = json.load(open(sc_path, encoding="utf-8"))
        if sc.get("activeBuildTarget") != "WebGL":
            print("skip (not WebGL):", f); continue
        v = sc.get("variant", {})
        if v.get("lensDirt", 0) > 0:
            print("skip (lens frame; the player's lens clock is live):", f); continue
        if v.get("brake"):
            print("skip (brake frame; the harness does not hold the pedal):", f); continue
        if not v.get("shadowMap", True) or v.get("field"):
            print("skip (no shadow map / field kept: an editor-only frame):", f); continue
        pose = sc.get("pose", {})
        p, q = pose.get("pos"), pose.get("rot")
        season = v.get("season", "Fall")
        shots.append({
            "name": os.path.basename(f)[:-4],
            "venue": sc.get("venue"), "spot": sc.get("spot"),
            "hour": sc.get("hourName"), "weather": sc.get("weather", "Clear"),
            "season": "Baked" if season == "BAKED" else season,
            "cam": "chase",
            "lights": {"On": "on", "Off": "off"}.get(v.get("lights"), "on" if v.get("lit") else "off"),
            "lens": 0, "grade": 1, "hud": 0,
            "pose": (p + q) if p and q else [],
        })
    with open(out, "w") as fh:
        json.dump({"shots": shots}, fh, indent=1)
    print(f"{len(shots)} shots -> {out}")
    for s in shots:
        print(f"  {s['name']}: {s['venue']} {s['spot']} {s['hour']} {s['weather']} {s['season']} lights {s['lights']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
