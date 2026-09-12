"""Dangling-reference audit for a Unity project, without Unity.

Every asset a player build ships (the scenes in EditorBuildSettings, everything
under a Resources folder, and every YAML asset those reference, transitively)
is scanned for `guid:` references whose .meta is nowhere in the project. A
dangling material reference renders with Unity's error shader: magenta.

That is exactly how the pizza boxes shipped pink on 2026-09-12. The sandbox's
scene build re-baked Resources/PizzaCargo against materials whose GUIDs it had
just minted; a later additive robocopy of Resources put the source's older
prefabs back over them; and `build-and-publish -SkipScenes` built the player
from the sandbox exactly as it stood. Nothing looked at a pixel.

    py tools/guid-audit.py [project_dir]     (default C:\\Users\\mcgee\\PSXBuild)

Exit 0 = clean, 1 = dangling references (listed), 2 = could not read the project.
Five seconds on this project.
"""
import os
import re
import sys

BUILTIN = {"0000000000000000e000000000000000", "0000000000000000f000000000000000",
           "0000000000000000d000000000000000"}
YAML_EXT = (".prefab", ".unity", ".mat", ".asset", ".controller", ".anim", ".overrideController",
            ".physicMaterial", ".physicsMaterial", ".lighting", ".renderTexture", ".cubemap", ".flare",
            ".guiskin", ".fontsettings", ".mask", ".mixer", ".playable", ".signal", ".terrainlayer",
            ".spriteatlas", ".brush")
GUID_RE = re.compile(rb"guid: ?([0-9a-f]{32})")


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\mcgee\PSXBuild"
    assets = os.path.join(root, "Assets")
    if not os.path.isdir(assets):
        print("no Assets folder under " + root)
        return 2

    # every GUID the project knows, and where its asset is
    known = {}
    for base in (assets, os.path.join(root, "Packages"), os.path.join(root, "Library", "PackageCache")):
        for dp, dn, fn in os.walk(base):
            for f in fn:
                if not f.endswith(".meta"):
                    continue
                p = os.path.join(dp, f)
                try:
                    with open(p, "r", encoding="utf-8", errors="replace") as h:
                        for line in h:
                            if line.startswith("guid:"):
                                known[line.split()[1].strip()] = p[:-5]
                                break
                except OSError:
                    pass

    # the roots of what ships
    todo = []
    settings = os.path.join(root, "ProjectSettings", "EditorBuildSettings.asset")
    try:
        with open(settings, "r", encoding="utf-8", errors="replace") as h:
            for m in re.finditer(r"path: (\S+\.unity)", h.read()):
                todo.append(os.path.join(root, m.group(1).replace("/", os.sep)))
    except OSError:
        print("could not read " + settings)
        return 2
    for dp, dn, fn in os.walk(assets):
        if os.sep + "Resources" not in dp + os.sep and not dp.endswith("Resources"):
            continue
        for f in fn:
            if f.endswith(YAML_EXT):
                todo.append(os.path.join(dp, f))

    seen = set()
    dangling = {}
    scanned = 0
    while todo:
        path = todo.pop()
        if path in seen or not os.path.isfile(path):
            continue
        seen.add(path)
        try:
            with open(path, "rb") as h:
                data = h.read()
        except OSError:
            continue
        if not data.startswith(b"%YAML"):
            continue            # binary serialization: not ours to read
        scanned += 1
        for m in GUID_RE.finditer(data):
            g = m.group(1).decode()
            if g in BUILTIN:
                continue
            target = known.get(g)
            if target is None or not os.path.exists(target):
                dangling.setdefault(os.path.relpath(path, root), set()).add(g)
            elif target.endswith(YAML_EXT) and target not in seen:
                todo.append(target)

    print(f"guid audit: {scanned} shipped YAML assets scanned, {len(known)} GUIDs known")
    if not dangling:
        print("GUID AUDIT OK - no shipped asset references a missing GUID")
        return 0
    print(f"GUID AUDIT FAILED - {len(dangling)} shipped assets reference GUIDs that do not exist:")
    for k in sorted(dangling):
        print("  " + k + "  " + ", ".join(sorted(dangling[k])))
    return 1


if __name__ == "__main__":
    sys.exit(main())
