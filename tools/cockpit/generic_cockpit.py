# The GENERIC COCKPIT: one simple low-poly car interior shared by every car in
# COCKPIT view (owner, 2026-10-13: "make a generic model in blender for now.
# simplistic, don't spend too much time on it").
#
#   blender.exe --background --python tools/cockpit/generic_cockpit.py -- <repo root> [preview dir]
#
# Writes into Assets/PSXRacing/Resources/Cockpit/ (Resources, because the game
# loads it at runtime with no scene rebake - CockpitInterior.cs):
#   generic_cockpit.obj   the cabin, ONE object, in the EYE frame
#   generic_wheel.obj     rim, 3 spokes, hub, column, in the HUB frame: hub at
#                         the origin, rim in the XY plane, +Z down the column
#                         INTO the dash (the driver is at -Z)
#   generic_hands.obj     both hands gripping at nine and three, same frame
#   cockpit_sheet.png     the one 256x256 sheet, cut from the owner's PSX packs
#   hands_skin.png        the HandsPack's own skin texture, copied as-is
# and, with a preview dir, a render from the driver's eye at the game's lens.
#
# AUTHORED FRAME = the game's (Unity) frame: x right, y up, z forward, metres,
# the driver's EYE at the origin, the car's centreline at x = +XC (left-hand
# drive). It is a realistic cabin - windscreen base 0.78 m ahead of the eye -
# and the game SCALES IT UNIFORMLY about the eye so that base lands on the
# shell's measured cowl (ChaseCamera.CockpitEye sits only 0.46 m behind it).
# A uniform scale about the eye changes no angle, so every car gets the same
# picture; see CockpitInterior.cs for the fit. The constants marked SHARED are
# copied there and must move together.
#
# Blender is right-handed Z-up; nothing here uses that. Every point is built in
# game coordinates and written through G2B (x -> -x): with the OBJ exporter's
# identity axes (forward Y, up Z), Unity's importer flips x back, so Unity reads
# exactly the game coordinates written here. Normals are fixed in Blender space
# after the flip (closed parts recalculated, single quads turned to the eye).
import bpy, bmesh, math, os, shutil, sys
import numpy as np
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = ARGS[0] if ARGS else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PREVIEW = ARGS[1] if len(ARGS) > 1 else None
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "Cockpit")
PACKS = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets"
V3 = PACKS + r"\PSX Textures v3.1\PSX Textures\256\Color Maps"
V2 = PACKS + r"\PSX Textures II v1.6\PSX Textures II\256\Color Maps"
HANDS = PACKS + r"\HandsPack\HandsPack"
SKIN = HANDS + r"\SkinTones\2\base.png"

# ---- SHARED with CockpitInterior.cs ---------------------------------------
Z_REF = 0.78                       # eye -> windscreen base (dash front edge)
HUB = Vector((0.0, -0.263, 0.465))  # wheel hub, eye frame
TILT = 24.0                         # column tilt: Quaternion.Euler(TILT, 0, 0)
RIM_R = 0.18                        # rim radius (tube centre)
# ---------------------------------------------------------------------------
RIM_T = 0.020                       # rim tube radius
XC = 0.37                           # car centreline, right of the eye
XL, XR = XC - 0.70, XC + 0.70       # inner door faces (a 1.4 m cabin)
PITCH = 3.5                         # ChaseCamera.MountPitch(Cockpit), preview only
VFOV = 58.0                         # ChaseCamera.CockpitFOV, preview only

# Sheet regions (u0, v0, u1, v1), v up.
DASH = (0.0, 0.5, 0.5, 1.0)     # misc_pt_4: dark grained plastic
RUBBER = (0.5, 0.5, 1.0, 1.0)   # rubber_tsk_1: rim, hood, trim
FABRIC = (0.0, 0.0, 0.5, 0.5)   # carpet_pt_1_2: headliner, pillars, door cloth
METAL = (0.5, 0.25, 1.0, 0.5)   # misc_pt_5: spokes, rim marker
GLASS = (0.5, 0.0, 1.0, 0.25)   # rubber_tsk_1 at a quarter: mirror glass, dial recess
TILE_M = 0.6                    # metres of surface one region covers


def G2B(p):
    return Vector((-p[0], p[1], p[2]))


# ---------------------------------------------------------------------------
#  The texture sheet, from the packs (owner rule: never code-drawn)
# ---------------------------------------------------------------------------
def pack_px(path, w, h, gain=1.0, tint=(1.0, 1.0, 1.0)):
    img = bpy.data.images.load(path, check_existing=False)
    img.scale(w, w)
    a = np.array(img.pixels[:], dtype=np.float32).reshape(w, w, 4)[:h]
    bpy.data.images.remove(img)
    a = a.copy()
    a[..., :3] *= gain * np.array(tint, dtype=np.float32)
    np.clip(a, 0.0, 1.0, out=a)
    a[..., 3] = 1.0
    return a


def build_sheet():
    sheet = np.zeros((256, 256, 4), dtype=np.float32)

    def put(region, arr):
        x0, y0 = int(region[0] * 256), int(region[1] * 256)
        sheet[y0:y0 + arr.shape[0], x0:x0 + arr.shape[1]] = arr

    put(DASH, pack_px(V2 + r"\misc_pt_4.png", 128, 128, 0.85))
    put(RUBBER, pack_px(V3 + r"\rubber_tsk_1.png", 128, 128, 1.0))
    put(FABRIC, pack_px(V2 + r"\carpet_pt_1_2.png", 128, 128, 1.25))
    put(METAL, pack_px(V2 + r"\misc_pt_5.png", 128, 64, 1.1))
    put(GLASS, pack_px(V3 + r"\rubber_tsk_1.png", 128, 64, 0.30, (0.85, 0.95, 1.15)))
    img = bpy.data.images.new("cockpit_sheet", 256, 256, alpha=False)
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw = os.path.join(OUT, "cockpit_sheet.png")
    img.file_format = 'PNG'
    img.save()
    return img


# ---------------------------------------------------------------------------
#  Geometry helpers. Each part is an island of SHARED vertices so Blender can
#  recalculate its outward normals; `facing` faces are single quads that are
#  turned to look at the eye instead.
# ---------------------------------------------------------------------------
class Builder:
    def __init__(self, eye_game):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.solid, self.facing = [], []
        self.region = {}
        self.eye = G2B(eye_game)

    def verts(self, pts):
        return [self.bm.verts.new(G2B(p)) for p in pts]

    def face(self, vs, region, facing=False):
        f = self.bm.faces.new(vs)
        self.region[f] = region
        (self.facing if facing else self.solid).append(f)
        return f

    def prism(self, profile, axis, a0, a1, region):
        """Closed prism: a 2D profile extruded along x, y or z from a0 to a1.
        profile points are (the other two axes in x,y,z order)."""
        def at(p, a):
            if axis == 'x': return (a, p[0], p[1])
            if axis == 'y': return (p[0], a, p[1])
            return (p[0], p[1], a)
        lo = self.verts([at(p, a0) for p in profile])
        hi = self.verts([at(p, a1) for p in profile])
        n = len(profile)
        self.face(lo, region)
        self.face(list(reversed(hi)), region)
        for i in range(n):
            j = (i + 1) % n
            self.face([lo[i], lo[j], hi[j], hi[i]], region)

    def hexa(self, corners, region):
        """Closed hexahedron from 8 corners: 0-3 one end, 4-7 the other, same order."""
        v = self.verts(corners)
        for idx in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
            self.face([v[i] for i in idx], region)

    def box(self, centre, size, region, basis=None):
        c = Vector(centre)
        bx, by, bz = basis if basis else (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
        hx, hy, hz = bx * (size[0] / 2), by * (size[1] / 2), bz * (size[2] / 2)
        self.hexa([c - hx - hy - hz, c + hx - hy - hz, c + hx + hy - hz, c - hx + hy - hz,
                   c - hx - hy + hz, c + hx - hy + hz, c + hx + hy + hz, c - hx + hy + hz], region)

    def beam(self, p0, p1, width, depth, region, side_hint=(1, 0, 0)):
        p0, p1 = Vector(p0), Vector(p1)
        ax = (p1 - p0).normalized()
        side = Vector(side_hint)
        side = (side - ax * side.dot(ax)).normalized()
        up = ax.cross(side).normalized()
        s, u = side * (width / 2), up * (depth / 2)
        self.hexa([p0 - s - u, p0 + s - u, p0 + s + u, p0 - s + u,
                   p1 - s - u, p1 + s - u, p1 + s + u, p1 - s + u], region)

    def quad(self, centre, bx, by, w, h, region):
        c, bx, by = Vector(centre), Vector(bx) * (w / 2), Vector(by) * (h / 2)
        v = self.verts([c - bx - by, c + bx - by, c + bx + by, c - bx + by])
        self.face(v, region, facing=True)

    def finish(self, name):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=self.solid)
        for f in self.facing:
            f.normal_update()
            if f.normal.dot(self.eye - f.calc_center_median()) < 0:
                f.normal_flip()
        uv_faces(bm, self.uv, self.region)
        for f in bm.faces:
            f.smooth = False
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def uv_faces(bm, uv, region):
    """Box projection per face into that face's region of the sheet."""
    inset = 1.5 / 256.0
    for f in bm.faces:
        r = region.get(f, DASH)
        f.normal_update()
        n = f.normal
        k = max(range(3), key=lambda i: abs(n[i]))
        a, b = [i for i in range(3) if i != k]
        pts = [(l.vert.co[a], l.vert.co[b]) for l in f.loops]
        ua, ub = min(p[0] for p in pts), min(p[1] for p in pts)
        span = max(TILE_M, max(p[0] for p in pts) - ua, max(p[1] for p in pts) - ub)
        for l, p in zip(f.loops, pts):
            s, t = (p[0] - ua) / span, (p[1] - ub) / span
            l[uv].uv = (r[0] + inset + s * (r[2] - r[0] - 2 * inset),
                        r[1] + inset + t * (r[3] - r[1] - 2 * inset))


def facing_basis(centre, eye=Vector((0, 0, 0))):
    """Axes for a part whose local -Z looks at the eye (a mirror)."""
    z = (Vector(centre) - eye).normalized()          # away from the eye
    x = Vector((0, 1, 0)).cross(z).normalized()
    y = z.cross(x).normalized()
    return x, y, z


# ---------------------------------------------------------------------------
#  The cabin (eye frame)
# ---------------------------------------------------------------------------
def build_cabin():
    b = Builder(Vector((0, 0, 0)))

    # Dash: one prism across the car. The face the driver sees slopes from the
    # top edge (0.56, -0.20) down and away to the footwell; the front edge is
    # the windscreen base at Z_REF.
    b.prism([(-0.200, 0.56), (-0.215, Z_REF), (-0.52, Z_REF), (-0.52, 0.62)], 'x', XL - 0.02, XR + 0.02, DASH)

    # Binnacle hood: an arched visor over the dials, in six convex segments.
    segs, half, top, base = 6, 0.175, -0.118, -0.205
    def arch(t):
        return -half + 2 * half * t, base + (top - base) * math.sin(math.pi * t)
    for i in range(segs):
        o0, o1 = arch(i / segs), arch((i + 1) / segs)
        i0 = (o0[0] * 0.92, o0[1] - 0.014)
        i1 = (o1[0] * 0.92, o1[1] - 0.014)
        b.prism([o0, o1, i1, i0], 'z', 0.555, 0.70, RUBBER)
    # The dial recess under it: dark, the 2D cluster is drawn over it.
    b.quad((0.0, -0.215, 0.64), (1, 0, 0), (0, 1, 0), 0.31, 0.17, GLASS)

    # Header: the roof's front edge across the top of the windscreen.
    b.prism([(0.138, 0.46), (0.215, 0.52), (0.32, 0.30), (0.20, 0.12)], 'x', XL - 0.08, XR + 0.08, FABRIC)

    # A-pillars, base on the dash corner, top into the header.
    b.beam((XL - 0.01, -0.23, 0.80), (XL + 0.02, 0.20, 0.44), 0.055, 0.05, FABRIC, side_hint=(1, 0, 0.6))
    b.beam((XR + 0.01, -0.23, 0.80), (XR - 0.02, 0.20, 0.44), 0.055, 0.05, FABRIC, side_hint=(1, 0, -0.6))

    # Doors: the card (inner wall) and the sill ledge on top of it.
    for x, s in ((XL, -1), (XR, 1)):
        b.box((x + s * 0.02, -0.40, 0.23), (0.04, 0.46, 1.02), DASH)
        b.box((x + s * 0.035, -0.175, 0.24), (0.11, 0.04, 1.06), RUBBER)

    # The driver's door mirror, outside, at the pillar foot; glass faces the eye.
    mc = Vector((XL - 0.20, -0.095, 0.66))
    bx, by, bz = facing_basis(mc)
    b.box(mc, (0.16, 0.10, 0.06), DASH, (bx, by, bz))
    b.quad(mc - bz * 0.032, bx, by, 0.14, 0.08, GLASS)
    b.beam((XL - 0.02, -0.15, 0.74), mc + bx * 0.07, 0.03, 0.03, DASH)

    # Rear-view mirror on the centreline, hanging from the header.
    rc = Vector((XC, 0.105, 0.50))
    bx, by, bz = facing_basis(rc)
    b.box(rc, (0.20, 0.06, 0.035), DASH, (bx, by, bz))
    b.quad(rc - bz * 0.019, bx, by, 0.18, 0.045, GLASS)
    b.beam((XC, 0.13, 0.50), (XC, 0.20, 0.47), 0.022, 0.022, DASH)

    # Centre console under the dash.
    b.box((XC, -0.47, 0.38), (0.24, 0.26, 0.56), DASH)
    return b.finish("Cabin")


# ---------------------------------------------------------------------------
#  The wheel (hub frame)
# ---------------------------------------------------------------------------
def tilt_matrix():
    return Matrix.Rotation(math.radians(TILT), 3, 'X')   # hub frame -> eye frame


def eye_in_hub():
    return tilt_matrix().transposed() @ (Vector((0, 0, 0)) - HUB)


def build_wheel():
    b = Builder(eye_in_hub())
    segs, sides = 16, 6
    rim = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        r = Vector((math.cos(a), math.sin(a), 0))
        rim.append(b.verts([r * RIM_R + (r * math.cos(2 * math.pi * j / sides) + Vector((0, 0, 1)) * math.sin(2 * math.pi * j / sides)) * RIM_T
                            for j in range(sides)]))
    for i in range(segs):
        for j in range(sides):
            i2, j2 = (i + 1) % segs, (j + 1) % sides
            b.face([rim[i][j], rim[i2][j], rim[i2][j2], rim[i][j2]], RUBBER)
    # The marker at twelve o'clock, so a turned wheel reads as turned.
    b.box((0, RIM_R, -0.004), (0.03, 0.035, 0.046), METAL)
    # Three spokes, dished: the hub sits a few cm down the column.
    for d in (Vector((-1, 0, 0)), Vector((1, 0, 0)), Vector((0, -1, 0))):
        side = Vector((0, 0, 1)).cross(d)
        p0, p1 = d * 0.045 + Vector((0, 0, 0.03)), d * (RIM_R - 0.012)
        b.beam(p0, p1, 0.05, 0.012, METAL, side_hint=side)
    # Hub (octagon) and its plain cap, then the column into the dash.
    oct8 = lambda r: [(r * math.cos(2 * math.pi * (k + 0.5) / 8), r * math.sin(2 * math.pi * (k + 0.5) / 8)) for k in range(8)]
    b.prism(oct8(0.055), 'z', 0.005, 0.05, DASH)
    b.prism(oct8(0.035), 'z', -0.002, 0.006, RUBBER)
    b.prism(oct8(0.03), 'z', 0.05, 0.26, DASH)
    return b.finish("Wheel")


# ---------------------------------------------------------------------------
#  The hands: the owner's HandsPack hand.fbx, posed to grip, baked static
# ---------------------------------------------------------------------------
CURL = {"Finger2": 62, "Finger2.001": 78, "Finger3": 64, "Finger3.001": 80,
        "Finger4": 66, "Finger4.001": 80, "Finger5": 68, "Finger5.001": 80,
        "Finger1": 28, "Finger1.001": 34}
WRIST = 22          # flexion: the hand bent down over the rim
HAND_SCALE = 0.076  # pack units -> metres (wrist to fingertip ~0.19 m)


def posed_hand():
    """The right hand, posed and baked: (verts in pack space, faces, uvs).
    Pack space: fingers +Z, palm +Y, thumb -X (a RIGHT hand)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=HANDS + r"\hand.fbx")
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    mesh = next(o for o in new if o.type == 'MESH')
    for pb in arm.pose.bones:
        deg = CURL.get(pb.name, WRIST if pb.name == "Hand" else 0)
        if not deg:
            continue
        b = pb.bone
        # Curl toward the palm (+Y): about -X, in the bone's own rest frame.
        axis = b.matrix_local.to_3x3().inverted() @ Vector((-1, 0, 0))
        if pb.name.startswith("Finger1"):
            # The thumb folds across the palm instead.
            d = (b.tail_local - b.head_local).normalized()
            axis = b.matrix_local.to_3x3().inverted() @ d.cross(Vector((0, 1, 0))).normalized()
        pb.rotation_mode = 'AXIS_ANGLE'
        pb.rotation_axis_angle = (math.radians(deg), axis.x, axis.y, axis.z)
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    me = ev.to_mesh()
    mw = mesh.matrix_world
    verts = [mw @ v.co for v in me.vertices]
    uvl = me.uv_layers.active.data
    faces = [([me.loops[li].vertex_index for li in p.loop_indices],
              [tuple(uvl[li].uv) for li in p.loop_indices]) for p in me.polygons]
    groups = {g.index: g.name for g in mesh.vertex_groups}
    fing = [i for i, v in enumerate(me.vertices)
            if any(groups.get(g.group, "").startswith("Finger") and not groups[g.group].startswith("Finger1")
                   and g.weight > 0.5 for g in v.groups)]
    ev.to_mesh_clear()
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    grip = sum((verts[i] for i in fing), Vector()) / max(1, len(fing))
    return verts, faces, grip


def build_hands():
    verts, faces, grip = posed_hand()
    # LEFT hand at nine o'clock, in the hub frame. A det +1 map of the pack's
    # right hand reads as a LEFT hand in Unity (see the header), so:
    #   pack X (pinky side) -> down the rim, pack Y (palm) -> in toward the hub
    #   (the hand is on the rim's outer side), pack Z (fingers) -> forward,
    #   round the front of the rim, curling back in on its inner side.
    a = Vector((0.0, -1.0, 0.0))
    bvec = Vector((0.95, 0.0, -0.30)).normalized()
    c = a.cross(bvec)
    # Turned about the rim's radial line so the forearm drops toward the
    # driver's lap instead of running straight back up the column.
    roll = Matrix.Rotation(math.radians(-25), 3, Vector((1, 0, 0)))
    M = roll @ Matrix((a, bvec, c)).transposed()        # columns a, b, c
    P = Vector((-RIM_R, 0.0, 0.0))
    me = bpy.data.meshes.new("Hands")
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for side in (1, -1):                                 # 1 = left, -1 = right (mirror)
        vs = []
        for v in verts:
            g = P + M @ ((v - grip) * HAND_SCALE)
            g = Vector((g.x * side, g.y, g.z))
            vs.append(bm.verts.new(G2B(g)))
        # Total linear part: G2B (det -1) x mirror (det side) x M (det +1).
        flip = (-1 * side) < 0
        for idx, uvs in faces:
            order = list(range(len(idx)))
            if flip:
                order.reverse()
            try:
                f = bm.faces.new([vs[idx[k]] for k in order])
            except ValueError:
                continue
            for l, k in zip(f.loops, order):
                l[uv].uv = uvs[k]
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Hands", me)
    bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True
    return ob


# ---------------------------------------------------------------------------
def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True,
                          forward_axis='Y', up_axis='Z', export_materials=False,
                          export_uv=True, export_normals=True, export_triangulated_mesh=True,
                          apply_modifiers=True, export_smooth_groups=False)


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def material(name, img):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = 'Closest'
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    return m


def preview(cabin, wheel, hands, sheet, skin, path, aspect):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_backface_culling = True
    if sc.world is None:
        sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.50, 0.62, 0.78)
    sc.render.resolution_y = 360
    sc.render.resolution_x = int(360 * aspect)
    sc.render.film_transparent = False
    cam_d = bpy.data.cameras.new("eye")
    cam_d.sensor_fit = 'VERTICAL'
    vfov = VFOV if aspect <= 16 / 9 + 1e-3 else 2 * math.degrees(math.atan(math.tan(math.radians(VFOV / 2)) * (16 / 9) / aspect))
    cam_d.angle_y = math.radians(vfov)
    cam_d.clip_start = 0.05
    cam = bpy.data.objects.new("eye", cam_d)
    sc.collection.objects.link(cam)
    p = math.radians(PITCH)
    fwd = Vector((0, -math.sin(p), math.cos(p)))
    up = Vector((0, math.cos(p), math.sin(p)))
    cam.matrix_world = Matrix((Vector((-1, 0, 0)), up, -fwd)).transposed().to_4x4()
    sc.camera = cam
    m_sheet, m_skin = material("sheet", sheet), material("skin", skin)
    for o in (cabin, wheel):
        o.data.materials.clear(); o.data.materials.append(m_sheet)
    hands.data.materials.clear(); hands.data.materials.append(m_skin)
    hub_w = Matrix.Translation(G2B(HUB)) @ Matrix.Rotation(math.radians(TILT), 4, 'X')
    wheel.matrix_world = hub_w
    hands.matrix_world = hub_w
    # A ground plane and a bonnet stand-in so the aperture has something in it.
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, -1.1, 40), rotation=(-math.pi / 2, 0, 0))
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sheet = build_sheet()
    cabin = build_cabin()
    wheel = build_wheel()
    hands = build_hands()
    export([cabin], os.path.join(OUT, "generic_cockpit.obj"))
    # Separate files: Unity's OBJ importer merges every object of a file
    # into one mesh, and the hands wear their own texture.
    export([wheel], os.path.join(OUT, "generic_wheel.obj"))
    export([hands], os.path.join(OUT, "generic_hands.obj"))
    shutil.copyfile(SKIN, os.path.join(OUT, "hands_skin.png"))
    skin = bpy.data.images.load(SKIN)
    print("[Cockpit] cabin %d tris, wheel %d tris, hands %d tris, total %d"
          % (tris(cabin), tris(wheel), tris(hands), tris(cabin) + tris(wheel) + tris(hands)))
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        preview(cabin, wheel, hands, sheet, skin, os.path.join(PREVIEW, "blender_eye.png"), 16 / 9)


main()
