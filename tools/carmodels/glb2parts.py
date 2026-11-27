# A section 5A car (opening panels) from its GLB: the OBJ exactly as glb2obj.py
# writes it (every transform baked, forward -Z, up Y: the OBJ frame IS the GLB
# frame), plus <out>_parts.json, the hinge data the OBJ cannot carry:
#   panels        each outer panel: origin (its hinge point), unit axis (its
#                 local +X), open_deg, parent, set
#   engine_mount  origin, x / y / z axes, layout, clear_box_m, downpipe_m,
#                 downpipe_side
#   kits          each Kit_ empty: origin, yaw_deg (about up)
#   objects       every other object: name, parent, set ("closed" or "open"),
#                 triangles, and its bounds in this frame
# All in GLB space: metres, Y up, nose -Z (the OBJ's frame, before
# export_models.mjs scales it).
#
# CM_PARTS = the delivered [Vehicle]_Parts.json: cross-checked here (1 mm,
# 0.1 degree; names, parents, sets, layout, side; the Kit_ empties by origin
# and yaw only) and the run FAILS (exit 3) on any mismatch.
#
# downpipe_m: the custom property is read as a point in the Body object's own
# Blender frame (Z up), as the artist authors it, and written in GLB space.
#
#   CM_SRC=<car.glb> CM_OUT=<car.obj> [CM_PARTS=<car_Parts.json>] \
#     blender --background --python glb2parts.py
import bpy, os, sys, json, math
from mathutils import Vector

src = os.environ["CM_SRC"]; out = os.environ["CM_OUT"]
delivered = os.environ.get("CM_PARTS", "")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
sc = bpy.context.scene
for o in sc.objects:
    print("OBJ_NAME %s type=%s" % (o.name, o.type))
bpy.ops.wm.obj_export(filepath=out, export_materials=True, export_selected_objects=False,
                      forward_axis='NEGATIVE_Z', up_axis='Y', export_triangulated_mesh=False)
print("WROTE " + out)

PANELS = ("Hood", "Trunk", "Hatch", "Door_L", "Door_R", "Door_RL", "Door_RR")
OPEN_ONLY = lambda n: n.endswith("_Inner") or n in ("Jamb_L", "Jamb_R", "Bay", "TrunkWell")

def g(v):   # Blender world -> GLB / OBJ (x, z, -y)
    return [v[0], v[2], -v[1]]

def set_of(ob):
    while ob is not None:
        if OPEN_ONLY(ob.name): return "open"
        ob = ob.parent
    return "closed"

def unit(v):
    v = Vector(v); return list(v.normalized())

doc = {"vehicle": os.path.splitext(os.path.basename(src))[0], "space": "GLB: metres, Y up, nose -Z",
       "panels": [], "kits": [], "objects": []}
problems = []
body = sc.objects.get("Body")
if body is None: problems.append("no Body object")
for ob in sc.objects:
    M = ob.matrix_world
    par = ob.parent.name if ob.parent else None
    if ob.name in PANELS:
        if "open_deg" not in ob: problems.append(ob.name + ": no open_deg custom property")
        doc["panels"].append({"name": ob.name, "parent": par, "origin": g(M.translation),
                              "axis": unit(g(M.col[0].xyz)), "open_deg": float(ob.get("open_deg", 0.0)), "set": "closed"})
    elif ob.name == "Engine_Mount":
        dp = ob.get("downpipe_m")
        dpw = (body.matrix_world if body else M.Identity(4)) @ Vector(list(dp)) if dp is not None else None
        doc["engine_mount"] = {"name": ob.name, "parent": par, "origin": g(M.translation),
                               "axes": {"x": unit(g(M.col[0].xyz)), "y": unit(g(M.col[1].xyz)), "z": unit(g(M.col[2].xyz))},
                               "layout": ob.get("layout"), "clear_box_m": list(ob.get("clear_box_m", [])),
                               "downpipe_m": g(dpw) if dpw is not None else None, "downpipe_side": ob.get("downpipe_side")}
        for k in ("layout", "clear_box_m", "downpipe_m", "downpipe_side"):
            if k not in ob: problems.append("Engine_Mount: no " + k)
    elif ob.name.startswith("Kit_"):
        x = M.col[0].xyz
        doc["kits"].append({"name": ob.name, "parent": par, "origin": g(M.translation),
                            "yaw_deg": math.degrees(math.atan2(x.y, x.x))})
    else:
        e = {"name": ob.name, "parent": par, "set": set_of(ob)}
        if ob.type == "MESH":
            e["tris"] = sum(len(p.vertices) - 2 for p in ob.data.polygons)
            pts = [g(M @ v.co) for v in ob.data.vertices]
            if pts:
                e["bounds"] = [[min(p[k] for p in pts) for k in range(3)], [max(p[k] for p in pts) for k in range(3)]]
        doc["objects"].append(e)

# the panels' carried objects and the triangle totals
names = {o["name"] for o in doc["objects"]}
closed = sum(o.get("tris", 0) for o in doc["objects"] if o["set"] == "closed")
openonly = sum(o.get("tris", 0) for o in doc["objects"] if o["set"] == "open")
doc["tris_closed"] = closed + 0   # (the panels' own triangles are objects too: counted below)
for p in doc["panels"]:
    ob = sc.objects[p["name"]]
    p["tris"] = sum(len(q.vertices) - 2 for q in ob.data.polygons) if ob.type == "MESH" else 0
    pts = [g(ob.matrix_world @ v.co) for v in ob.data.vertices] if ob.type == "MESH" else []
    if pts:
        p["bounds"] = [[min(q[k] for q in pts) for k in range(3)], [max(q[k] for q in pts) for k in range(3)]]
    doc["tris_closed"] += p["tris"]
doc["tris_open_only"] = openonly
doc["doors"] = 4 if any(p["name"] in ("Door_RL", "Door_RR") for p in doc["panels"]) else 2
print("PARTS panels=%s closed=%d open_only=%d" % ([p["name"] for p in doc["panels"]], doc["tris_closed"], openonly))

# ---- the cross-check against the delivered file --------------------------
def close(a, b, tol):
    return a is not None and b is not None and len(a) == len(b) and all(abs(x - y) <= tol for x, y in zip(a, b))

def angle(a, b):
    a, b = Vector(a).normalized(), Vector(b).normalized()
    return math.degrees(math.acos(max(-1.0, min(1.0, a.dot(b)))))

if delivered:
    with open(delivered) as f: d = json.load(f)
    def by(lst): return {e["name"]: e for e in lst}
    mine, theirs = by(doc["panels"]), by(d.get("panels", []))
    for n in sorted(set(mine) | set(theirs)):
        a, b = mine.get(n), theirs.get(n)
        if a is None or b is None: problems.append("panel %s only in the %s" % (n, "GLB" if b is None else "JSON")); continue
        if not close(a["origin"], b["origin"], 0.001): problems.append("panel %s origin %s vs JSON %s" % (n, a["origin"], b["origin"]))
        if angle(a["axis"], b.get("axis", b.get("hinge_axis", [0, 0, 0]))) > 0.1: problems.append("panel %s axis %s vs JSON %s" % (n, a["axis"], b.get("axis")))
        if abs(a["open_deg"] - float(b.get("open_deg", -999))) > 0.1: problems.append("panel %s open_deg %.2f vs JSON %s" % (n, a["open_deg"], b.get("open_deg")))
        if a["parent"] != b.get("parent"): problems.append("panel %s parent %s vs JSON %s" % (n, a["parent"], b.get("parent")))
        if b.get("set", "closed") != "closed": problems.append("panel %s set %s in the JSON" % (n, b.get("set")))
    a, b = doc.get("engine_mount"), d.get("engine_mount")
    if (a is None) != (b is None): problems.append("Engine_Mount only in the %s" % ("GLB" if b is None else "JSON"))
    elif a is not None:
        if not close(a["origin"], b["origin"], 0.001): problems.append("Engine_Mount origin %s vs JSON %s" % (a["origin"], b["origin"]))
        for k in ("x", "y", "z"):
            if angle(a["axes"][k], b["axes"][k]) > 0.1: problems.append("Engine_Mount %s axis %s vs JSON %s" % (k, a["axes"][k], b["axes"][k]))
        if a["layout"] != b.get("layout"): problems.append("Engine_Mount layout %s vs JSON %s" % (a["layout"], b.get("layout")))
        if not close(a["clear_box_m"], b.get("clear_box_m"), 0.001): problems.append("Engine_Mount clear_box_m %s vs JSON %s" % (a["clear_box_m"], b.get("clear_box_m")))
        if not close(a["downpipe_m"], b.get("downpipe_m"), 0.001): problems.append("Engine_Mount downpipe_m %s vs JSON %s" % (a["downpipe_m"], b.get("downpipe_m")))
        if a["downpipe_side"] != b.get("downpipe_side"): problems.append("Engine_Mount downpipe_side %s vs JSON %s" % (a["downpipe_side"], b.get("downpipe_side")))
    mine, theirs = by(doc["kits"]), by(d.get("kits", []))
    for n in sorted(set(mine) | set(theirs)):
        a, b = mine.get(n), theirs.get(n)
        if a is None or b is None: problems.append("kit %s only in the %s" % (n, "GLB" if b is None else "JSON")); continue
        if not close(a["origin"], b["origin"], 0.001): problems.append("kit %s origin %s vs JSON %s" % (n, a["origin"], b["origin"]))
        dy = (a["yaw_deg"] - float(b.get("yaw_deg", b.get("yaw", 0.0))) + 180.0) % 360.0 - 180.0
        if abs(dy) > 0.1: problems.append("kit %s yaw %.2f vs JSON %s" % (n, a["yaw_deg"], b.get("yaw_deg")))
    mine, theirs = by(doc["objects"]), by(d.get("objects", []))
    for n in sorted(set(mine) | set(theirs)):
        a, b = mine.get(n), theirs.get(n)
        if a is None or b is None: problems.append("object %s only in the %s" % (n, "GLB" if b is None else "JSON")); continue
        if a["parent"] != b.get("parent"): problems.append("object %s parent %s vs JSON %s" % (n, a["parent"], b.get("parent")))
        if a["set"] != b.get("set"): problems.append("object %s set %s vs JSON %s" % (n, a["set"], b.get("set")))

jpath = os.path.splitext(out)[0] + "_parts.json"
doc["problems"] = problems
with open(jpath, "w") as f: json.dump(doc, f, indent=1)
print("WROTE " + jpath)
for p in problems: print("PARTS MISMATCH " + p)
print("PARTS CHECK " + ("OK" if not problems else "FAILED (%d)" % len(problems)) + (" against " + delivered if delivered else " (no delivered JSON)"))
sys.stdout.flush()
if problems: sys.exit(3)
