"""Classic / Enhanced textures (overhaul1, 2026-10-11): copy the owner's
Enhanced_2x art into the project and write the manifest the editor tool
(Editor/TextureTierTool.cs) builds Resources/TextureTierMap.asset from.

    py -I tools/texenhanced/make_enhanced.py <handoff dir> <project root>

The registry (texture_registry_overhaul1.json in the handoff) is the source of
truth. Rules, from the handoff and the owner:
  * one binding per ORIGINAL asset (project_bindings), matched by GUID + bytes,
    never by file name;
  * a binding whose original no longer has the bytes the art was made from is
    SKIPPED (the storefronts the owner de-branded after the art was drawn:
    the Enhanced copies would bring the real businesses back);
  * the unmapped library trees have no binding and stay out;
  * SoilBeach: DRY (the wet one is a second choice for the same original);
  * names that share one artwork are imported ONCE.
Enhanced files land under EnhancedSrc/TexEnhanced/ in the ORIGINAL's folder
(relative to Art/), so path rules (ReleaseBudget's /Roads/ exclusion) treat
the copy as they treat the original. EnhancedSrc is outside Resources: the
twins ship as StreamingAssets/texenhanced.bundle (TextureTierTool.BuildBundle,
run before every player build) and load only when Enhanced is picked.
"""
import hashlib, json, os, shutil, sys
from PIL import Image

H, P = sys.argv[1], sys.argv[2]
ART = "Assets/PSXRacing/Art/"
DEST = "Assets/PSXRacing/EnhancedSrc/TexEnhanced/"
reg = json.load(open(os.path.join(H, "texture_registry_overhaul1.json"), encoding="utf-8"))
tex = {t["id"]: t for t in reg["textures"]}


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def guid(asset):
    with open(os.path.join(P, asset + ".meta"), encoding="utf-8") as f:
        for line in f:
            if line.startswith("guid:"):
                return line.split()[1]


rows, skipped, placed = [], [], {}
for b in reg["project_bindings"]:
    a = b["asset_path"]
    ids = b["candidate_ids"]
    if len(ids) > 1 and len({tex[i]["enhanced_sha256"] for i in ids}) > 1:
        dry = [i for i in ids if i.endswith("sanddry")]
        if not dry:
            skipped.append((a, "several distinct Enhanced choices, none picked"))
            continue
        ids = dry
    t = tex[ids[0]]
    if not os.path.exists(os.path.join(P, a)):
        skipped.append((a, "original missing from the project"))
        continue
    if guid(a) != b["guid"]:
        skipped.append((a, "GUID differs from the registry"))
        continue
    if sha(os.path.join(P, a)) != b["sha256"]:
        skipped.append((a, "original changed since the art was made (owner's de-brand edit, 7c0ef644)"))
        continue
    src = os.path.join(H, t["enhanced_path"])
    if sha(src) != t["enhanced_sha256"]:
        skipped.append((a, "Enhanced file hash differs from the registry"))
        continue
    im = Image.open(src)
    w, h = im.size
    if [w, h] != t["enhanced_dimensions"] or [w // 2, h // 2] != t["baseline_dimensions"]:
        skipped.append((a, f"Enhanced {w}x{h} is not 2x baseline {t['baseline_dimensions']}"))
        continue
    key = t["enhanced_sha256"]
    if key not in placed:
        sub = os.path.dirname(a[len(ART):]) if a.startswith(ART) else ""
        leaf = ids[0].split("/")[-1]
        placed[key] = (DEST + (sub + "/" if sub else "") + leaf + ".png").replace("//", "/")
        dst = os.path.join(P, placed[key])
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if not os.path.exists(dst) or sha(dst) != key:
            shutil.copyfile(src, dst)
    om = Image.open(os.path.join(P, a)).mode
    rows.append("\t".join([b["guid"], a, placed[key], str(w), str(h), ids[0], im.mode, om]))

for tid, t in tex.items():
    if not t.get("verified_project_sources"):
        skipped.append((tid, "unmapped library tree: no verified mesh/material/UV mapping"))

out = os.path.join(P, "Assets/PSXRacing/Editor/TextureTierManifest.txt")
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write("# original_guid\toriginal_path\tenhanced_path\tw\th\tregistry_id\tenh_mode\torig_mode\n")
    f.write("\n".join(rows) + "\n")
print(f"BOUND {len(rows)} originals -> {len(placed)} Enhanced files")
for a, why in skipped:
    print("SKIP", a, "|", why)
for r in rows:
    c = r.split("\t")
    if (c[6] == "RGBA") != (c[7] in ("RGBA", "LA", "P")):
        print("MODE", c[1], "orig", c[7], "enh", c[6])
