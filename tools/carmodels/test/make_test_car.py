# THE SYNTHETIC 5A TEST CAR (carparts stage 2): a box-built car carrying every
# section 5A name, so the converter, the baker and the game can be proven before
# any real car has opening panels. NOT ART: flat boxes, a flat colour atlas.
#
#   CM_OUT=<dir> CM_DOORS=2|4 blender --background --python make_test_car.py
#
# Writes <dir>/<name>.glb (closed pose, panels pivoted, custom properties as
# glTF extras), <dir>/<name>_Parts.json (written from the AUTHORING numbers
# here, not read back from the GLB, so glb2parts.py's cross-check is a real
# one) and <dir>/textures/<name>_512x256.png.
#
# Authoring frame: Blender, metres, Z up, nose on +Y, +X = the car's right,
# Body origin on the ground midway between the axles (section 7). GLB space
# (Y up, nose -Z) = (x, z, -y) of it.
import bpy, bmesh, os, json, math
from mathutils import Matrix, Vector

OUT = os.environ["CM_OUT"]
DOORS = int(os.environ.get("CM_DOORS", "2"))
NAME = "test5a_%ddoor" % DOORS
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# ---- the atlas: flat swatches (u0, v0, u1, v1 in 0..1), one per surface class
SW = {
    "paint":  ((0.02, 0.52, 0.22, 0.98), (78, 108, 150)),
    "trim":   ((0.26, 0.52, 0.46, 0.98), (52, 52, 56)),
    "inner":  ((0.52, 0.52, 0.72, 0.98), (128, 118, 104)),
    "bay":    ((0.76, 0.52, 0.96, 0.98), (104, 104, 100)),
    "tyre":   ((0.02, 0.02, 0.22, 0.48), (34, 34, 36)),
    "lamp":   ((0.26, 0.02, 0.46, 0.48), (190, 40, 30)),
    "mirror": ((0.52, 0.02, 0.72, 0.48), (150, 160, 170)),
    "head":   ((0.76, 0.02, 0.96, 0.48), (230, 220, 180)),
}
W, H = 512, 256
img = bpy.data.images.new(NAME + "_512x256", W, H, alpha=True)
px = [0.0] * (W * H * 4)
for (u0, v0, u1, v1), rgb in SW.values():
    for y in range(int(v0 * H), int(v1 * H)):
        for x in range(int(u0 * W), int(u1 * W)):
            i = (y * W + x) * 4
            px[i:i + 4] = [rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, 1.0]
img.pixels = px
tex_path = os.path.join(OUT, "textures", NAME + "_512x256.png")
img.filepath_raw = tex_path
img.file_format = "PNG"
img.save()

def make_mat(name, image=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    if image is not None:
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        t = m.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = image
        m.node_tree.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    return m

ATLAS = make_mat("TestAtlas", img)
CLEAR = make_mat("TestClearCover")   # a clear cover material (export_models: /clear|cover|glass/)

def box_faces(bm, uv, lo, hi, cls):
    """An axis-aligned box in WORLD space, all six faces outward."""
    x0, y0, z0 = lo; x1, y1, z1 = hi
    c = [Vector((x, y, z)) for z in (z0, z1) for y in (y0, y1) for x in (x0, x1)]
    vs = [bm.verts.new(p) for p in c]
    quads = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    (u0, v0, u1, v1), _ = SW[cls]
    for q in quads:
        f = bm.faces.new([vs[i] for i in q])
        for k, loop in enumerate(f.loops):
            loop[uv].uv = ((u0, v0), (u1, v0), (u1, v1), (u0, v1))[k]
    return vs

def make_obj(name, boxes, parent=None, M=Matrix.Identity(4), material=None):
    """boxes: [(lo, hi, cls)] in WORLD space; the object's world matrix is M,
    so its mesh is authored as M^-1 of the world boxes."""
    me = bpy.data.meshes.new(name)
    ob = bpy.data.objects.new(name, me if boxes else None)
    sc.collection.objects.link(ob)
    if boxes:
        bm = bmesh.new()
        uv = bm.loops.layers.uv.new("UVMap")
        for lo, hi, cls in boxes:
            box_faces(bm, uv, lo, hi, cls)
        inv = M.inverted()
        for v in bm.verts:
            v.co = inv @ v.co
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        me.materials.append(material or ATLAS)
    else:
        ob.empty_display_type = "ARROWS"
        ob.empty_display_size = 0.15
    if parent is not None:
        ob.parent = parent
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.matrix_basis = parent.matrix_world.inverted() @ M
    else:
        ob.matrix_basis = M
    # (matrix_world is lazy: a child made next reads its parent's, so update now)
    bpy.context.view_layer.update()
    return ob

def frame(origin, xaxis, up_hint=Vector((0, 0, 1))):
    """A matrix whose origin is `origin` and whose local +X is `xaxis`."""
    x = Vector(xaxis).normalized()
    h = Vector(up_hint)
    if abs(x.dot(h)) > 0.9:
        h = Vector((0, 1, 0))
    y = h.cross(x).normalized()
    z = x.cross(y).normalized()
    M = Matrix.Identity(4)
    for r in range(3):
        M[r][0], M[r][1], M[r][2], M[r][3] = x[r], y[r], z[r], origin[r]
    return M

# ---- the car: 4.40 m long, 1.70 wide, wheelbase 2.50, roof 1.25 -----------
HW = 0.85         # half width
F_AX, R_AX = 1.25, -1.25
SILL, BELT, ROOF = 0.40, 0.80, 1.25
HOOD_Y0, HOOD_Y1 = 0.95, 2.02      # hood: hinge at the cowl (y0), front edge (y1)
HOOD_Z = 1.06                       # hood skin underside (stage 4: a bay deep enough for an engine)
TRUNK_Y0, TRUNK_Y1 = -2.02, -1.12  # trunk lid: rear edge (y0), hinge (y1)
if DOORS == 2:
    DOOR_SPANS = [("", 0.88, -0.62)]              # (suffix, hinge y, rear y)
else:
    DOOR_SPANS = [("", 0.88, 0.02), ("R", -0.04, -0.86)]

body_boxes = [
    ((-HW, -2.20, 0.25), (HW, 2.20, 0.33), "trim"),                 # floor pan
    ((-HW, 2.02, 0.33), (HW, 2.20, HOOD_Z + 0.02), "paint"),        # nose
    ((-HW, -2.20, 0.33), (HW, -2.02, 0.82), "paint"),               # tail
    ((-HW, HOOD_Y0 - 0.06, 0.33), (HW, HOOD_Y0, HOOD_Z + 0.02), "paint"),  # cowl / firewall
    ((-HW, TRUNK_Y1, 0.33), (HW, TRUNK_Y1 + 0.06, 0.84), "paint"),  # rear bulkhead
    ((-0.70, -1.06, ROOF - 0.05), (0.70, 0.62, ROOF), "paint"),     # roof
    ((-0.70, 0.62, BELT), (0.70, HOOD_Y0 - 0.06, BELT + 0.04), "trim"),       # windscreen base
    ((-0.70, TRUNK_Y1 + 0.06, 0.84), (0.70, -1.06, 0.88), "trim"),            # rear glass base
]
for s in (-1, 1):
    xo, xi = s * HW, s * (HW - 0.08)
    lo_x, hi_x = min(xo, xi), max(xo, xi)
    body_boxes += [
        ((lo_x, HOOD_Y0, 0.33), (hi_x, 2.02, HOOD_Z + 0.02), "paint"),          # front wing
        ((lo_x, -2.02, 0.33), (hi_x, DOOR_SPANS[-1][2] - 0.02, 0.84), "paint"),  # rear quarter
        ((lo_x, DOOR_SPANS[-1][2] - 0.02, 0.33), (hi_x, DOOR_SPANS[0][1] + 0.02, SILL), "paint"),  # sill
        ((s * 0.62 if s > 0 else -0.70, 0.56, BELT), (0.70 if s > 0 else -0.62, 0.66, ROOF), "trim"),    # A pillar
        ((s * 0.62 if s > 0 else -0.70, -1.10, 0.84), (0.70 if s > 0 else -0.62, -1.00, ROOF), "paint"),  # C pillar
    ]
    if DOORS == 4:
        body_boxes.append(((lo_x, -0.04, SILL), (hi_x, 0.02, ROOF - 0.05), "paint"))           # B pillar
body_boxes += [
    ((-0.80, 2.15, 0.55), (-0.55, 2.21, 0.68), "head"), ((0.55, 2.15, 0.55), (0.80, 2.21, 0.68), "head"),
    ((-0.80, -2.21, 0.60), (-0.55, -2.15, 0.72), "lamp"), ((0.55, -2.21, 0.60), (0.80, -2.15, 0.72), "lamp"),
]
body = make_obj("Body", body_boxes)

# head lamp covers on Body (clear: the covers file), a lid lamp cover on the trunk
make_obj("HeadLensCover", [((-0.81, 2.205, 0.54), (-0.54, 2.215, 0.69), "head"),
                           ((0.54, 2.205, 0.54), (0.81, 2.215, 0.69), "head")], body, material=CLEAR)

# wheels (boxes standing in for tyres)
for nm, x, y in (("Wheel_FL", -0.74, F_AX), ("Wheel_FR", 0.74, F_AX), ("Wheel_RL", -0.74, R_AX), ("Wheel_RR", 0.74, R_AX)):
    make_obj(nm, [((x - 0.10, y - 0.31, 0.0), (x + 0.10, y + 0.31, 0.62), "tyre")])

panels = []   # (name, parent, M, open_deg) for the JSON

# ---- HOOD: hinge across the car just ahead of the cowl, under the skin; front rises
hM = frame(Vector((0, HOOD_Y0 + 0.01, HOOD_Z - 0.005)), Vector((1, 0, 0)))
hood = make_obj("Hood", [((-0.76, HOOD_Y0 + 0.005, HOOD_Z), (0.76, HOOD_Y1, HOOD_Z + 0.02), "paint")], body, hM)
make_obj("Hood_Inner", [((-0.72, HOOD_Y0 + 0.03, HOOD_Z - 0.015), (0.72, HOOD_Y1 - 0.03, HOOD_Z - 0.002), "inner")], hood, hM)
panels.append(("Hood", hood, 55.0))

# ---- TRUNK: hinge across at the lid's front edge; rear edge rises -> local +X = -X world
tM = frame(Vector((0, TRUNK_Y1 - 0.01, 0.815)), Vector((-1, 0, 0)))
trunk = make_obj("Trunk", [((-0.76, TRUNK_Y0, 0.82), (0.76, TRUNK_Y1 - 0.005, 0.84), "paint")], body, tM)
make_obj("Trunk_Inner", [((-0.72, TRUNK_Y0 + 0.03, 0.805), (0.72, TRUNK_Y1 - 0.03, 0.818), "inner")], trunk, tM)
lens = make_obj("Trunk_Lenses", [((-0.40, TRUNK_Y0 - 0.006, 0.80), (0.40, TRUNK_Y0, 0.835), "lamp")], trunk,
                Matrix.Translation(Vector((0, TRUNK_Y0, 0.82))))
make_obj("Trunk_LensCover", [((-0.41, TRUNK_Y0 - 0.010, 0.795), (0.41, TRUNK_Y0 - 0.006, 0.840), "lamp")], lens,
         Matrix.Translation(Vector((0, TRUNK_Y0, 0.82))), material=CLEAR)
panels.append(("Trunk", trunk, 60.0))

# ---- DOORS: vertical hinge in the aperture's front face; the rear edge swings out
for suffix, y_hinge, y_rear in DOOR_SPANS:
    for s, side in ((-1, "L"), (1, "R")):
        nm = "Door_%s%s" % (suffix, side)
        # +Z rotation (right-hand) moves a point behind the hinge (-Y) toward +X:
        # the right door opens about +Z, the left about -Z
        M = frame(Vector((s * (HW - 0.01), y_hinge - 0.01, (SILL + ROOF) * 0.5)), Vector((0, 0, s)), Vector((0, 1, 0)))
        xo, xi = s * HW, s * (HW - 0.06)
        d = make_obj(nm, [((min(xo, xi), y_rear, SILL + 0.005), (max(xo, xi), y_hinge - 0.02, BELT), "paint"),
                          ((min(xo, xi) + (0.02 if s > 0 else 0.0), y_rear + 0.04, BELT), (max(xo, xi) - (0.0 if s > 0 else 0.02), y_hinge - 0.06, ROOF - 0.07), "trim")],
                     body, M)
        xi2 = s * (HW - 0.075)
        make_obj(nm + "_Inner", [((min(xi, xi2), y_rear + 0.02, SILL + 0.03), (max(xi, xi2), y_hinge - 0.04, BELT - 0.02), "inner")], d, M)
        if suffix == "":
            mM = Matrix.Translation(Vector((s * (HW + 0.06), y_hinge - 0.10, BELT + 0.06)))
            mirror = make_obj("Mirror_" + side, [((s * HW + (0 if s > 0 else -0.12), y_hinge - 0.16, BELT + 0.02),
                                                  (s * HW + (0.12 if s > 0 else 0), y_hinge - 0.06, BELT + 0.12), "paint")], d, mM)
            make_obj("MirrorGlass_" + side, [((s * HW + (0.01 if s > 0 else -0.11), y_hinge - 0.165, BELT + 0.03),
                                              (s * HW + (0.11 if s > 0 else -0.01), y_hinge - 0.160, BELT + 0.11), "mirror")], mirror, mM)
        panels.append((nm, d, 65.0))

# ---- open-only: jambs (each covers every aperture on its side, sill included), bay, well
for s, side in ((-1, "L"), (1, "R")):
    xa, xb = s * (HW - 0.09), s * (HW - 0.10)
    jb = [((min(xa, xb), DOOR_SPANS[-1][2] - 0.02, 0.33), (max(xa, xb), DOOR_SPANS[0][1] + 0.02, SILL), "inner")]
    for _, y_h, y_r in DOOR_SPANS:
        jb.append(((min(xa, xb), y_h, SILL), (max(xa, xb) + (0.0 if s > 0 else 0.0), y_h + 0.02, BELT), "inner"))
        jb.append(((min(xa, xb), y_r - 0.02, SILL), (max(xa, xb), y_r, BELT), "inner"))
    make_obj("Jamb_" + side, jb, body)
bay = make_obj("Bay", [((-0.74, HOOD_Y0, 0.36), (0.74, 2.00, 0.39), "bay"),
                       ((-0.76, HOOD_Y0, 0.39), (-0.72, 2.00, HOOD_Z - 0.06), "bay"), ((0.72, HOOD_Y0, 0.39), (0.76, 2.00, HOOD_Z - 0.06), "bay"),
                       ((-0.72, 1.96, 0.39), (0.72, 2.00, HOOD_Z - 0.06), "bay")], body)
make_obj("TrunkWell", [((-0.74, TRUNK_Y0 + 0.02, 0.36), (0.74, TRUNK_Y1, 0.39), "bay")], body)

# ---- Engine_Mount: on the crank at the flywheel face; +Y to the timing end, +Z up the bore
if DOORS == 2:
    layout = "longitudinal"
    # crank along the car, timing end forward, block slanted 15 degrees to the right
    mR = Matrix.Rotation(math.radians(15.0), 4, "Y")
    mount_o = Vector((0.0, 1.05, 0.58))
    clear = [0.90, 0.10, 0.70, 0.70, 0.46, 0.19]
    downpipe = [0.32, 1.00, 0.36]          # Body space
else:
    layout = "transverse"
    # crank across the car, timing end on the car's right (+X), bore up, leaned back 10
    mR = Matrix.Rotation(math.radians(-90.0), 4, "Z") @ Matrix.Rotation(math.radians(10.0), 4, "Y")
    mount_o = Vector((-0.30, 1.55, 0.58))
    clear = [1.00, 0.40, 0.40, 0.55, 0.46, 0.19]
    downpipe = [-0.10, 1.30, 0.34]
mM = Matrix.Translation(mount_o) @ mR
mount = make_obj("Engine_Mount", [], body, mM)
mount["layout"] = layout
mount["clear_box_m"] = clear
mount["downpipe_m"] = downpipe
dp_local = mM.inverted() @ Vector(downpipe)
side = "+X" if dp_local.x >= 0 else "-X"
mount["downpipe_side"] = side

kits = [("Kit_Battery", Vector((-0.52, 1.70, 0.39)), 12.0), ("Kit_Radiator", Vector((0.0, 1.93, 0.39)), 0.0)]
for nm, p, yaw in kits:
    make_obj(nm, [], bay, Matrix.Translation(p) @ Matrix.Rotation(math.radians(yaw), 4, "Z"))

for nm, ob, deg in panels:
    ob["open_deg"] = deg

bpy.context.view_layer.update()

# ---- the delivered Parts.json, in GLB space, from the authoring numbers ----
def g(v):          # Blender world -> GLB (Y up, nose -Z)
    return [round(v[0], 6), round(v[2], 6), round(-v[1], 6)]

OPEN_ONLY = lambda n: n.endswith("_Inner") or n in ("Jamb_L", "Jamb_R", "Bay", "TrunkWell")
def set_of(ob):
    while ob is not None:
        if OPEN_ONLY(ob.name): return "open"
        ob = ob.parent
    return "closed"

doc = {"vehicle": NAME, "space": "GLB: metres, Y up, nose -Z", "panels": [], "kits": [], "objects": []}
for nm, ob, deg in panels:
    M = ob.matrix_world
    doc["panels"].append({"name": nm, "parent": ob.parent.name, "origin": g(M.translation),
                          "axis": g(M.col[0].xyz.normalized()), "open_deg": deg, "set": "closed"})
M = mount.matrix_world
doc["engine_mount"] = {"name": "Engine_Mount", "parent": mount.parent.name, "origin": g(M.translation),
                       "axes": {"x": g(M.col[0].xyz.normalized()), "y": g(M.col[1].xyz.normalized()), "z": g(M.col[2].xyz.normalized())},
                       "layout": layout, "clear_box_m": clear, "downpipe_m": g(Vector(downpipe)), "downpipe_side": side}
for nm, p, yaw in kits:
    doc["kits"].append({"name": nm, "parent": "Bay", "origin": g(p), "yaw_deg": yaw})
pn = set(p[0] for p in panels) | {"Engine_Mount"} | set(k[0] for k in kits)
for ob in sc.objects:
    if ob.name in pn: continue
    doc["objects"].append({"name": ob.name, "parent": ob.parent.name if ob.parent else None, "set": set_of(ob)})
with open(os.path.join(OUT, NAME + "_Parts.json"), "w") as f:
    json.dump(doc, f, indent=1)

bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, NAME + ".glb"), export_format="GLB",
                          export_extras=True, export_yup=True, export_apply=False)
tris = {o.name: len(o.data.polygons) for o in sc.objects if o.type == "MESH"}
print("TESTCAR %s objects=%d tris=%s" % (NAME, len(sc.objects), json.dumps(tris)))
print("WROTE " + os.path.join(OUT, NAME + ".glb"))
