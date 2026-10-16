# THE COCKPIT ARMS (2026-10-16): the owner's HandsPack hand, on a whole arm.
#
#   blender.exe --background --python tools/cockpit/cockpit_arms.py -- <repo root> [preview.png]
#
# Owner: "I really like the art style of the arms you drew. Like slightly
# realistic anime." Those were the HandsPack hand (hand.fbx, SkinTones/2) cut
# 20 cm past the grip. The HAND here is still exactly the pack's - its mesh
# from the wrist ring out, its rig, its skin - posed three ways on the pack's
# bones:
#   grip     the cockpit's pose since 2026-10-13 (CURL, WRIST)
#   open     fingers straight, slightly spread, flat
#   relaxed  a loose natural curl, fingers together (the reach, a crossing)
# The pack's own forearm is a straight ten-sided tube with its elbow crease
# painted where the stretched upper arm drags it down the forearm, so FROM
# THE WRIST RING BACK the arm is rebuilt: a 16-sided tube welded to the pack's
# wrist ring, tapered and curved like a forearm (wrist 5.4 cm, belly 6.9 cm
# toward the thumb side, elbow 6.3 cm, 25 cm long), an upper arm to a rounded
# shoulder end, wrapped cylindrically (seam on the little-finger side) in the
# pack's OWN forearm skin - the light-belly panel of base.png, ping-ponged at
# the elbow - so arm and hand are one skin and tone. The pack's wrist crease
# line is lifted out of our copy of the texture (CockpitSrc/hands_skin.png):
# it read as a bracelet seam.
#
# Writes Assets/PSXRacing/CockpitSrc/arms_geo.bytes (into the on-demand
# cockpit.bundle with the rest of v08 - never the main data), read by
# CockpitArms.cs, and CockpitSrc/hands_skin.png:
#   'PSXA', int version 2, float3 elbow, float upperLen, float3 shoulderEnd,
#   int verts, int indices, int shapes,
#   float3 pos[verts], float3 normal[verts], float2 uv[verts], float upper[verts],
#   ushort index[indices],
#   per shape: str name, float3 dpos[verts], float3 dnormal[verts]
#   then (v2) THE CONTACT DATA (owner: fingers never inside the wheel):
#   float3 tube (where the rim's tube centre sits in the gripping hand),
#   float footMin, footMax (the hand's reach along the rim, x), float skinGap,
#   int levels, int baseLevel, float level[levels] (finger curl as x GRIP),
#   int fingers; per finger: str name, int n, int vertex[n], per level but
#   the base: float3 dpos[n], float3 dnormal[n];
#   the wheel (v08 wheel mesh, its pivot frame): float rimCentreR, rimA
#   (radial half-section), rimB (axial), rimZ; float hubR, hubZ0, hubZ1;
#   int spokes; per spoke: float angleDeg, int segs; per seg: float r0, r1,
#   halfWidth, z0, z1
#   (str = byte length + ASCII)
# ONE arm is written, the driver's RIGHT (the pack's hand is a right hand);
# the game mirrors it (x -> -x, winding reversed) for the left.
#
# THE ARM FRAME (Unity axes, metres): origin = the GRIP pose's grip centre
# (the centroid of its curled fingers, which sits on the rim's tube centre),
# +Z along the forearm toward the fingers, +Y out of the palm, +X = Y x Z
# (the index side of this right hand). pos/normal/uv are the GRIP pose; the
# shapes are the other poses as deltas from it. `upper` is the vertex's
# weight on the upper-arm bone, whose pivot is `elbow`; at rest the upper
# arm runs straight back along -Z from it to `shoulderEnd`.
import bpy, math, os, struct, sys
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = ARGS[0] if ARGS else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PREVIEW = ARGS[1] if len(ARGS) > 1 else None
SRC_OUT = os.path.join(ROOT, "Assets", "PSXRacing", "CockpitSrc")
HANDS = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\HandsPack\HandsPack"
SKIN = HANDS + r"\SkinTones\2\base.png"

SCALE = 0.076        # pack units -> metres (generic_cockpit.HAND_SCALE: wrist to fingertip ~0.19 m)
AROUND = 16          # sides of the rebuilt arm
FOREARM = 0.255      # wrist ring -> elbow pivot, metres
# The arm's sections back from the wrist ring: (s metres, half-width across
# the hand's width (+X = little-finger side), half-depth palm-to-back (+Y =
# palm), centre offset x, y). The belly sits toward the thumb (-X) and the
# back of the forearm (-Y) - the long muscles that make a forearm read.
PROFILE = [
    (0.015, 0.0272, 0.0206, 0.000, 0.000),
    (0.045, 0.0292, 0.0222, -0.001, 0.000),
    (0.085, 0.0314, 0.0252, -0.002, -0.001),
    (0.125, 0.0332, 0.0283, -0.003, -0.002),
    (0.165, 0.0344, 0.0302, -0.004, -0.003),
    (0.205, 0.0336, 0.0298, -0.003, -0.002),
    (0.235, 0.0318, 0.0292, -0.001, -0.001),
    (0.255, 0.0314, 0.0300, 0.000, 0.000),
    (0.285, 0.0324, 0.0318, 0.000, 0.001),
    (0.335, 0.0352, 0.0350, 0.000, 0.003),
    (0.395, 0.0380, 0.0372, 0.000, 0.003),
    (0.455, 0.0398, 0.0388, 0.000, 0.002),
    (0.505, 0.0405, 0.0396, 0.000, 0.000),
    (0.540, 0.0345, 0.0338, 0.000, 0.000),
    (0.560, 0.0215, 0.0210, 0.000, 0.000),
]
END = 0.568          # the shoulder end's pole
UPPER_W = (0.205, 0.315)   # upper-arm weight ramps 0 -> 1 over these s (the elbow bend)
# The skin the arm wraps in (base.png UV): the pack's back-of-forearm panel,
# columns 70..108 and rows 153..66 of the 256 map - base skin tone at both
# edges, its soft painted highlight between. The wrap's seam is on the inner
# (palm) side, base tone against base tone like the palm's own wrist; the
# highlight runs down the BACK of the forearm into the back of the hand's.
U0, U1 = 70 / 256, 108 / 256
V_WRIST, V_ELBOW = 1 - 153 / 256, 1 - 66 / 256
SEAM = math.pi / 2     # the wrap's seam angle (pack: 0 = little-finger side, pi/2 = palm)
# Dark marks (the wrist crease) lifted out of this band of the 256 map.
CREASE_ROWS = (145, 180)
CREASE_COLS = (20, 112)
CREASE_DARK = 0.88     # darker than this x the base tone = a line
# The arm's panel, softened (two box passes of this radius, texels) so its
# painted highlight is a smooth gradient, not a wavy-edged patch.
SOFT_COLS, SOFT_ROWS, SOFT_R = (64, 114), (56, 162), 4

# Degrees per bone. Curl is toward the palm (+Y) about the bone's -X; the
# thumb (Finger1) folds across the palm; "spread" turns a finger's base about
# the palm normal (+ toward the pinky, pack +X).
GRIP = {"curl": {"Finger2": 62, "Finger2.001": 78, "Finger3": 64, "Finger3.001": 80,
                 "Finger4": 66, "Finger4.001": 80, "Finger5": 68, "Finger5.001": 80,
                 "Finger1": 28, "Finger1.001": 34},
        "wrist": 22, "spread": {}}
OPEN = {"curl": {"Finger1": -8, "Finger1.001": -4},
        "wrist": 0, "spread": {"Finger2": -5, "Finger3": 0, "Finger4": 4, "Finger5": 8}}
RELAXED = {"curl": {"Finger2": 24, "Finger2.001": 30, "Finger3": 26, "Finger3.001": 32,
                    "Finger4": 28, "Finger4.001": 34, "Finger5": 30, "Finger5.001": 36,
                    "Finger1": 12, "Finger1.001": 14},
           "wrist": 8, "spread": {"Finger2": 2, "Finger4": -2, "Finger5": -4}}
REST = {"curl": {}, "wrist": 0, "spread": {}}
# The grip's per-finger curl levels (fractions of GRIP's curl, wrist as GRIP):
# the game curls each finger as far as the wheel lets it (CockpitArms).
LEVELS = [0.0, 0.25, 0.5, 0.75, 1.0]
BASE_LEVEL = 4       # LEVELS[4] = 1.0 is GRIP itself (the mesh's base pose)
FINGERS = [("index", "Finger2"), ("middle", "Finger3"), ("ring", "Finger4"), ("pinky", "Finger5"), ("thumb", "Finger1")]
SKIN_GAP = 0.0025    # the skin's clearance off the wheel, metres
GEO = os.path.join(SRC_OUT, "v08_geo.bytes")


def measure_wheel():
    """The steering wheel, measured from the v08 mesh (its pivot frame: x
    right, y up the face, z along the column into the dash): the rim as a
    torus of oval section, the hub/pad as a disc, each spoke as boxes along
    its axis. Analytic, so the game can keep the hands outside it."""
    b = open(GEO, "rb").read()
    o = [4]
    def rd(fmt):
        v = struct.unpack_from(fmt, b, o[0]); o[0] += struct.calcsize(fmt); return v
    def rs():
        n = b[o[0]]; o[0] += 1; t = b[o[0]:o[0] + n].decode(); o[0] += n; return t
    _, nparts = rd("<ii")
    pts = tri = None
    for _ in range(nparts):
        name = rs()
        nv, ns = rd("<ii")
        pmin = rd("<3f"); pspan = rd("<3f"); rd("<4f")
        Q = [rd("<3H") for _ in range(nv)]
        o[0] += nv * 11
        idx = []
        for _ in range(ns):
            rs(); ni, = rd("<i")
            idx += rd(("<%dI" if nv > 65535 else "<%dH") % ni)
        if name == "wheel":
            pts = [tuple(pmin[k] + q[k] / 65535.0 * pspan[k] for k in range(3)) for q in Q]
            tri = idx
    S = []
    for i in range(0, len(tri), 3):
        a, c, d = pts[tri[i]], pts[tri[i + 1]], pts[tri[i + 2]]
        for w in ((1, 0, 0), (0, 1, 0), (0, 0, 1), (1 / 3, 1 / 3, 1 / 3), (.5, .5, 0), (0, .5, .5), (.5, 0, .5)):
            S.append(tuple(a[k] * w[0] + c[k] * w[1] + d[k] * w[2] for k in range(3)))
    def rho(p): return math.hypot(p[0], p[1])
    def ang(p): return math.degrees(math.atan2(p[1], p[0])) % 360.0
    rmax = max(rho(p) for p in S)
    mins = {}
    for p in S:
        if rho(p) > 0.6 * rmax:
            k = int(ang(p) // 3)
            mins[k] = min(mins.get(k, 9.0), rho(p))
    inner = sorted(mins.values())[len(mins) // 2]
    free = {k for k, v in mins.items() if v > inner - 0.002}
    rim = [p for p in S if rho(p) > inner - 0.0005 and int(ang(p) // 3) in free]
    z0, z1 = min(p[2] for p in rim), max(p[2] for p in rim)
    R = {"rc": (rmax + inner) / 2, "a": (rmax - inner) / 2, "zc": (z0 + z1) / 2, "b": (z1 - z0) / 2}
    # The hub: out to where the pad stops covering (nearly) every direction.
    hub_r = 0.03
    r = 0.03
    while r < inner - 0.02:
        occ = {int(ang(p) // 3) for p in S if abs(rho(p) - r) < 0.0025}
        if len(occ) < 0.85 * 120:
            break
        hub_r = r
        r += 0.005
    hub_r += 0.005
    hub = [p for p in S if rho(p) < hub_r]
    H = {"r": hub_r, "z0": min(p[2] for p in hub), "z1": max(p[2] for p in hub)}
    # The spokes: angular clusters just inside the rim.
    band = sorted(ang(p) for p in S if inner - 0.025 < rho(p) < inner - 0.004)
    cl, cur = [], []
    for a_ in band:
        if cur and a_ - cur[-1] > 3.0:
            cl.append(cur); cur = []
        cur.append(a_)
    if cur:
        cl.append(cur)
    if len(cl) > 1 and cl[0][0] < 3.0 and cl[-1][-1] > 357.0:
        cl[0] = [a_ - 360.0 for a_ in cl[-1]] + cl[0]; cl.pop()
    spokes = []
    for c in cl:
        cen = (c[0] + c[-1]) / 2
        cr, sr = math.cos(math.radians(cen)), math.sin(math.radians(cen))
        segs = []
        # Four radial bands, each its own box: the spoke flares toward the
        # hub, and one wide box would stop fingers short of the rim.
        edges = [hub_r - 0.005 + (inner + 0.004 - hub_r + 0.005) * k / 4 for k in range(5)]
        for r0, r1 in zip(edges, edges[1:]):
            sel = []
            for p in S:
                u = p[0] * cr + p[1] * sr
                v = -p[0] * sr + p[1] * cr
                if r0 <= u <= r1 and abs(v) < 0.06 and rho(p) < inner - 0.0015 and rho(p) > hub_r - 0.005:
                    sel.append((u, v, p[2]))
            if not sel:
                continue
            segs.append({"r0": r0, "r1": r1, "hw": max(abs(v) for _, v, _ in sel),
                         "z0": min(z for _, _, z in sel), "z1": max(z for _, _, z in sel)})
        spokes.append({"ang": cen % 360.0, "segs": segs})
    print("[ARMS] wheel: rim centre r %.4f, section %.4f radial x %.4f axial at z %+.4f; hub r %.3f z %+.4f..%+.4f"
          % (R["rc"], R["a"], R["b"], R["zc"], H["r"], H["z0"], H["z1"]))
    for sp in spokes:
        print("[ARMS]   spoke at %.1f deg: %s" % (sp["ang"], ", ".join(
            "r %.3f-%.3f half-width %.4f z %+.4f..%+.4f" % (g["r0"], g["r1"], g["hw"], g["z0"], g["z1"]) for g in sp["segs"])))
    return R, H, spokes

POSES = [("grip", GRIP), ("open", OPEN), ("relaxed", RELAXED)]


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=HANDS + r"\hand.fbx")
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
    return arm, mesh


def pose(arm, p):
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0)
    for pb in arm.pose.bones:
        b = pb.bone
        inv = b.matrix_local.to_3x3().inverted()
        q = Matrix.Identity(3)
        deg = p["curl"].get(pb.name, p["wrist"] if pb.name == "Hand" else 0)
        if deg:
            axis = inv @ Vector((-1, 0, 0))
            if pb.name.startswith("Finger1"):
                d = (b.tail_local - b.head_local).normalized()
                axis = inv @ d.cross(Vector((0, 1, 0))).normalized()
            q = Matrix.Rotation(math.radians(deg), 3, axis.normalized()) @ q
        sp = p["spread"].get(pb.name, 0)
        if sp:
            axis = inv @ Vector((0, 1, 0))
            q = Matrix.Rotation(math.radians(sp), 3, axis.normalized()) @ q
        pb.rotation_quaternion = q.to_quaternion()
    bpy.context.view_layer.update()


def evaluate(mesh):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    me = ev.to_mesh()
    mw = mesh.matrix_world
    P = [mw @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    return P


def clean_skin(path):
    """base.png with the wrist crease lifted out (a row blend across it,
    only where the line is darker than the skin either side)."""
    img = bpy.data.images.load(SKIN)
    w, h = img.size
    px = list(img.pixels)
    def at(x, y):
        i = ((h - 1 - y) * w + x) * 4
        return px[i:i + 4]
    base = at(90, 165)                      # the skin's flat tone
    dark = sum(base[:3]) * CREASE_DARK
    r0, r1 = CREASE_ROWS
    fixed = 0
    for x in range(*CREASE_COLS):
        y = r0
        while y < r1:
            if sum(at(x, y)[:3]) >= dark:
                y += 1
                continue
            y1 = y
            while y1 < r1 and sum(at(x, y1)[:3]) < dark:
                y1 += 1
            a, b = at(x, y - 1), at(x, y1)
            for yy in range(y, y1):
                t = (yy - y + 1) / (y1 - y + 1)
                i = ((h - 1 - yy) * w + x) * 4
                px[i:i + 4] = [a[k] * (1 - t) + b[k] * t for k in range(4)]
                fixed += 1
            y = y1
    print("[ARMS] skin: %d crease texels lifted" % fixed)
    x0, x1 = SOFT_COLS
    y0, y1 = SOFT_ROWS
    grid = {(x, y): at(x, y) for x in range(x0, x1) for y in range(y0, y1)}
    for _ in range(2):
        for axis in (0, 1):
            nxt = {}
            for (x, y) in grid:
                acc, n = [0.0] * 4, 0
                for d in range(-SOFT_R, SOFT_R + 1):
                    q = (min(x1 - 1, max(x0, x + d)), y) if axis == 0 else (x, min(y1 - 1, max(y0, y + d)))
                    c = grid[q]
                    acc = [acc[k] + c[k] for k in range(4)]
                    n += 1
                nxt[(x, y)] = [v / n for v in acc]
            grid = nxt
    for (x, y), c in grid.items():
        i = ((h - 1 - y) * w + x) * 4
        px[i:i + 4] = c
    out = bpy.data.images.new("hands_skin", w, h)
    out.pixels = px
    out.filepath_raw = path
    out.file_format = 'PNG'
    out.save()
    print("[ARMS] wrote", path)


def main():
    arm, mesh = load()
    me = mesh.data
    groups = {g.index: g.name for g in mesh.vertex_groups}
    nv = len(me.vertices)
    uvl = me.uv_layers.active.data

    pose(arm, REST)
    rest = evaluate(mesh)
    posed = {}
    for name, p in POSES:
        pose(arm, p)
        posed[name] = evaluate(mesh)

    # The pack's wrist ring: its highest ring of "Arm" vertices.
    def dominant(v):
        return max(((groups.get(g.group, ""), g.weight) for g in v.groups), key=lambda t: t[1])[0] if v.groups else ""
    arm_vs = [i for i, v in enumerate(me.vertices) if dominant(v) == "Arm"]
    zw = max(rest[i].z for i in arm_vs)
    ring = [i for i in arm_vs if rest[i].z > zw - 0.15]
    c0 = sum((rest[i] for i in ring), Vector()) / len(ring)
    ring.sort(key=lambda i: math.atan2(rest[i].y - c0.y, rest[i].x - c0.x) % (2 * math.pi))
    zw = c0.z
    # Hand faces: everything with a vertex beyond the wrist ring.
    hand_faces = [p for p in me.polygons if any(rest[v].z > zw + 0.05 for v in p.vertices)]
    used = sorted({v for p in hand_faces for v in p.vertices} | set(ring))

    # ---- the vertex table: pack vertices, then the new arm's ------------
    # Each entry: ("p", pack index) or ("g", fixed pack-space point, s).
    verts = [("p", i) for i in used]
    vid = {i: k for k, i in enumerate(used)}
    rings = []
    for s, a, b, dx, dy in PROFILE:
        cz = zw - s / SCALE
        rr = []
        for k in range(AROUND):
            th = 2 * math.pi * k / AROUND
            pt = Vector((c0.x + (dx + a * math.cos(th)) / SCALE, c0.y + (dy + b * math.sin(th)) / SCALE, cz))
            rr.append(len(verts))
            verts.append(("g", pt, s))
        rings.append((s, rr))
    pole = len(verts)
    verts.append(("g", Vector((c0.x, c0.y, zw - END / SCALE)), END))

    def rest_pos(e):
        return rest[e[1]] if e[0] == "p" else e[1]

    def s_of(e):
        return 0.0 if e[0] == "p" else e[2]

    # ---- faces: (vertex ids, per-corner uv) -----------------------------
    faces = []
    for p in hand_faces:
        faces.append(([vid[v] for v in p.vertices], [tuple(uvl[li].uv) for li in p.loop_indices]))

    def theta(e):
        q = rest_pos(e)
        return math.atan2(q.y - c0.y, q.x - c0.x) % (2 * math.pi)

    def wrap(e):
        return (theta(e) - SEAM) % (2 * math.pi)

    def v_of(s):
        f = min(s, FOREARM) / FOREARM
        g = max(0.0, s - FOREARM) / (END - FOREARM)
        return V_WRIST + (V_ELBOW - V_WRIST) * (f - g)

    def cyl(ids):
        th = [wrap(verts[i]) for i in ids]
        if max(th) - min(th) > math.pi:
            th = [t + 2 * math.pi if t < math.pi else t for t in th]
        return [(U0 + (U1 - U0) * min(t / (2 * math.pi), 1.0), v_of(s_of(verts[i]))) for t, i in zip(th, ids)]

    def add(ids):
        faces.append((ids, cyl(ids)))

    # Bridge the pack's 10-vertex wrist ring to the first 16-vertex ring by angle.
    A = [vid[i] for i in ring]
    B = rings[0][1]
    ta = [theta(verts[i]) for i in A]
    tb = [theta(verts[i]) for i in B]
    ia = ib = 0
    na, nb = len(A), len(B)
    while ia < na or ib < nb:
        a0, a1 = A[ia % na], A[(ia + 1) % na]
        b0, b1 = B[ib % nb], B[(ib + 1) % nb]
        na_t = ta[(ia + 1) % na] + (2 * math.pi if ia + 1 >= na else 0)
        nb_t = tb[(ib + 1) % nb] + (2 * math.pi if ib + 1 >= nb else 0)
        if ib >= nb or (ia < na and na_t <= nb_t):
            add([a0, a1, b0]); ia += 1
        else:
            add([a0, b1, b0]); ib += 1
    for (s0, r0), (s1, r1) in zip(rings, rings[1:]):
        for k in range(AROUND):
            add([r0[k], r0[(k + 1) % AROUND], r1[(k + 1) % AROUND], r1[k]])
    last = rings[-1][1]
    for k in range(AROUND):
        add([last[k], last[(k + 1) % AROUND], pole])

    # Outward winding (Blender's convention) for the new faces: away from
    # the arm's axis.
    def fix(face):
        ids, uvs = face
        P = [rest_pos(verts[i]) for i in ids]
        n = (P[1] - P[0]).cross(P[2] - P[0])
        cen = sum(P, Vector()) / len(P)
        axis_pt = Vector((c0.x, c0.y, cen.z))
        if cen.z < zw - (END - 0.02) / SCALE:
            axis_pt = Vector((c0.x, c0.y, zw - (END - 0.03) / SCALE))
        if n.dot(cen - axis_pt) < 0:
            return (ids[::-1], uvs[::-1])
        return face
    faces = faces[:len(hand_faces)] + [fix(f) for f in faces[len(hand_faces):]]

    # ---- per pose: positions and smooth normals ---------------------------
    def pose_positions(name):
        P = posed[name]
        return [P[e[1]] if e[0] == "p" else e[1] for e in verts]

    def normals(pos):
        acc = [Vector() for _ in pos]
        for ids, _ in faces:
            for j in range(1, len(ids) - 1):
                a, b, c = pos[ids[0]], pos[ids[j]], pos[ids[j + 1]]
                n = (b - a).cross(c - a)
                for i in (ids[0], ids[j], ids[j + 1]):
                    acc[i] += n
        return [n.normalized() if n.length > 1e-12 else Vector((0, 0, 1)) for n in acc]

    gP = posed["grip"]
    fing = [i for i, v in enumerate(me.vertices)
            if any(groups.get(g.group, "").startswith("Finger") and not groups[g.group].startswith("Finger1")
                   and g.weight > 0.5 for g in v.groups)]
    grip = sum((gP[i] for i in fing), Vector()) / len(fing)

    def to_arm(b):
        return Vector((-(b.x - grip.x), b.y - grip.y, b.z - grip.z)) * SCALE

    def nrm(n):
        return Vector((-n.x, n.y, n.z)).normalized()

    def ramp(s):
        t = min(1.0, max(0.0, (s - UPPER_W[0]) / (UPPER_W[1] - UPPER_W[0])))
        return t * t * (3 - 2 * t)

    # Weld corners on (vertex, uv).
    key, out_v, tris = {}, [], []
    for ids, uvs in faces:
        corners = []
        for i, uv in zip(ids, uvs):
            kk = (i, (round(uv[0], 5), round(uv[1], 5)))
            if kk not in key:
                key[kk] = len(out_v)
                out_v.append(kk)
            corners.append(key[kk])
        for j in range(1, len(corners) - 1):
            tris += [corners[0], corners[j + 1], corners[j]]   # reversed: the map is a reflection

    cols = {}
    for name, _ in POSES:
        P = pose_positions(name)
        N = normals(P)
        cols[name] = ([to_arm(P[i]) for i, _ in out_v], [nrm(N[i]) for i, _ in out_v])
    base_p, base_n = cols["grip"]
    upper = [ramp(s_of(verts[i])) for i, _ in out_v]

    # ---- the grip, fitted to the measured rim ---------------------------
    wheel_r, wheel_h, wheel_s = measure_wheel()
    tube = max(wheel_r["a"], wheel_r["b"])
    need = tube + SKIN_GAP

    def finger_of(pi):
        w = {}
        for g in me.vertices[pi].groups:
            nm = groups.get(g.group, "")
            for f, (_, bone) in enumerate(FINGERS):
                if nm == bone or nm == bone + ".001":
                    w[f] = w.get(f, 0.0) + g.weight
        if not w:
            return -1
        f = max(w, key=w.get)
        return f if w[f] > 0.05 else -1
    fid = [finger_of(verts[i][1]) if verts[i][0] == "p" else -1 for i, _ in out_v]
    hand = [k for k, (i, _) in enumerate(out_v) if verts[i][0] == "p"]
    palm = [k for k in hand if fid[k] < 0]
    four = [k for k in hand if 0 <= fid[k] <= 3]
    # Per-finger curl levels (each level curls every finger; each finger keeps its own).
    lev = []
    for c in LEVELS:
        pose(arm, {"curl": {k: v * c for k, v in GRIP["curl"].items()}, "wrist": GRIP["wrist"], "spread": {}})
        Pl = evaluate(mesh)
        P = [Pl[e[1]] if e[0] == "p" else e[1] for e in verts]
        N = normals(P)
        lev.append(([to_arm(P[i]) for i, _ in out_v], [nrm(N[i]) for i, _ in out_v]))
    # The tube's place in the fist (the arm frame's YZ plane; the rim runs
    # along X): the palm against it at the skin gap, and each finger curled
    # (0..1 x GRIP) until it touches - the same solve the game makes in 3D
    # against the whole wheel, here in section to choose where the palm sits.
    def d2(k, P, cy, cz):
        return math.hypot(P[k].y - cy, P[k].z - cz)
    def reach(ks, cy, cz):
        """(curl, gap) for a finger: the furthest curl clear of the tube."""
        best_c, best_g = 0.0, None
        prev = None
        for L, c in enumerate(LEVELS):
            P = lev[L][0]
            g = min(d2(k, P, cy, cz) for k in ks) - need
            if g < 0:
                if prev is None:
                    return 0.0, g
                lo, hi = prev, c
                for _ in range(7):
                    m = (lo + hi) / 2
                    j = max(i for i, v in enumerate(LEVELS) if v <= m)
                    t = (m - LEVELS[j]) / (LEVELS[j + 1] - LEVELS[j])
                    Pa, Pb = lev[j][0], lev[j + 1][0]
                    gm = min(math.hypot(Pa[k].y + (Pb[k].y - Pa[k].y) * t - cy, Pa[k].z + (Pb[k].z - Pa[k].z) * t - cz)
                             for k in ks) - need
                    if gm >= 0:
                        lo, best_g = m, gm
                    else:
                        hi = m
                return lo, best_g if best_g is not None else 0.0
            prev, best_c, best_g = c, c, g
        return best_c, best_g
    fk = [[k for k in hand if fid[k] == f] for f in range(5)]
    best = None
    for iy in range(-45, 46):
        for iz in range(-45, 46):
            cy, cz = iy * 0.001, iz * 0.001
            rp = min(d2(k, base_p, cy, cz) for k in palm)
            if rp < need or rp > need + 0.006:
                continue
            res = [reach(fk[f], cy, cz) for f in range(4)]
            score = sum(max(0.0, g) for _, g in res) + 0.5 * (rp - need) + sum(0.02 for c, _ in res if c < 0.35)
            if best is None or score < best[0]:
                best = (score, cy, cz, rp, res)
    _, cy, cz, rp, res = best
    ins = max(min(min(d2(k, base_p, iy * 0.001, iz * 0.001) for k in palm), min(d2(k, base_p, iy * 0.001, iz * 0.001) for k in four))
              for iy in range(-45, 46) for iz in range(-45, 46))
    tube_c = Vector((0.0, cy, cz))
    xs4 = [base_p[k].x for k in palm + four]
    print("[ARMS] grip fit: rim section r %.4f + gap %.4f = %.4f; the GRIP fist's largest inscribed circle r %.4f -> %s"
          % (tube, SKIN_GAP, need, ins, "the pack's fingers can wrap it" if ins >= need else
             "TOO TIGHT by %.1f mm" % ((need - ins) * 1000)))
    print("[ARMS] tube centre in the arm frame (0, %.4f, %.4f) (was the curled-finger centroid 0,0,0): palm %.1f mm off the rim; "
          "hand footprint along the rim x %.3f..%.3f" % (cy, cz, (rp - tube) * 1000, min(xs4), max(xs4)))
    th = reach(fk[4], cy, cz)
    for f, (c, g) in enumerate(res + [th]):
        print("[ARMS]   %-6s curls to %.2f x GRIP, then %.1f mm off the rim" % (FINGERS[f][0], c, (g + SKIN_GAP) * 1000))
    fingers = []
    for f, (fname, _) in enumerate(FINGERS):
        ks = [k for k in hand if fid[k] == f and any((lev[L][0][k] - base_p[k]).length > 1e-5 for L in range(len(LEVELS) - 1))]
        fingers.append((fname, ks))

    elbow = to_arm(Vector((c0.x, c0.y, zw - FOREARM / SCALE)))
    shoulder_end = to_arm(Vector((c0.x, c0.y, zw - END / SCALE)))
    xs = [p.x for p in base_p]; ys = [p.y for p in base_p]; zs = [p.z for p in base_p]
    print("[ARMS] hand %d pack verts, arm %d rings x %d; frame box x[%.3f %.3f] y[%.3f %.3f] z[%.3f %.3f]; "
          "elbow %s; shoulder end %s; wrist->grip %.3f m"
          % (len(used), len(rings), AROUND, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs),
             tuple(round(c, 3) for c in elbow), tuple(round(c, 3) for c in shoulder_end),
             (to_arm(c0)).length))

    def sstr(s):
        b = s.encode("ascii")
        return struct.pack("<B", len(b)) + b

    n = len(out_v)
    blob = bytearray(b"PSXA" + struct.pack("<i", 2))   # v2: + the contact data
    blob += struct.pack("<3f", *elbow) + struct.pack("<f", END - FOREARM) + struct.pack("<3f", *shoulder_end)
    blob += struct.pack("<iii", n, len(tris), 2)
    blob += b"".join(struct.pack("<3f", *p) for p in base_p)
    blob += b"".join(struct.pack("<3f", *q) for q in base_n)
    blob += b"".join(struct.pack("<2f", *uv) for _, uv in out_v)
    blob += b"".join(struct.pack("<f", w) for w in upper)
    blob += struct.pack("<%dH" % len(tris), *tris)
    for name in ("open", "relaxed"):
        sp, sn = cols[name]
        blob += sstr(name)
        blob += b"".join(struct.pack("<3f", *(a - b)) for a, b in zip(sp, base_p))
        blob += b"".join(struct.pack("<3f", *(a - b)) for a, b in zip(sn, base_n))
        print("[ARMS] shape %-8s max move %.3f m" % (name, max((a - b).length for a, b in zip(sp, base_p))))
    # v2: the fit, the curl levels, the wheel.
    blob += struct.pack("<3f", *tube_c) + struct.pack("<2f", min(xs4), max(xs4)) + struct.pack("<f", SKIN_GAP)
    blob += struct.pack("<ii", len(LEVELS), BASE_LEVEL) + struct.pack("<%df" % len(LEVELS), *LEVELS)
    blob += struct.pack("<i", len(fingers))
    for fname, ks in fingers:
        blob += sstr(fname) + struct.pack("<i", len(ks)) + struct.pack("<%di" % len(ks), *ks)
        for L in range(len(LEVELS)):
            if L == BASE_LEVEL:
                continue
            blob += b"".join(struct.pack("<3f", *(lev[L][0][k] - base_p[k])) for k in ks)
            blob += b"".join(struct.pack("<3f", *(lev[L][1][k] - base_n[k])) for k in ks)
    blob += struct.pack("<4f", wheel_r["rc"], wheel_r["a"], wheel_r["b"], wheel_r["zc"])
    blob += struct.pack("<3f", wheel_h["r"], wheel_h["z0"], wheel_h["z1"])
    blob += struct.pack("<i", len(wheel_s))
    for sp in wheel_s:
        blob += struct.pack("<fi", sp["ang"], len(sp["segs"]))
        for g in sp["segs"]:
            blob += struct.pack("<5f", g["r0"], g["r1"], g["hw"], g["z0"], g["z1"])
    os.makedirs(SRC_OUT, exist_ok=True)
    path = os.path.join(SRC_OUT, "arms_geo.bytes")
    with open(path, "wb") as fh:
        fh.write(blob)
    print("[ARMS] wrote %s: %d verts, %d tris, %d bytes" % (path, n, len(tris) // 3, len(blob)))
    clean_skin(os.path.join(SRC_OUT, "hands_skin.png"))
    if PREVIEW:
        preview(cols, out_v, tris)


def preview(cols, out_v, tris):
    """The three poses side by side, backs of the hands (Workbench,
    our cleaned skin), built from the written data."""
    sc = bpy.context.scene
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x, sc.render.resolution_y = 1200, 500
    if sc.world is None:
        sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.35, 0.38, 0.45)
    img = bpy.data.images.load(os.path.join(SRC_OUT, "hands_skin.png"))
    m = bpy.data.materials.new("skin")
    m.use_nodes = True
    tex = m.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    m.node_tree.links.new(tex.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    for k, (name, _) in enumerate(POSES):
        P, _ = cols[name]
        me = bpy.data.meshes.new(name)
        # Unity arm frame -> Blender (x mirrored back), laid out side by side.
        vs = [(-p.x + (k - 1) * 0.2, p.y, p.z) for p in P]
        fs = [(tris[i], tris[i + 2], tris[i + 1]) for i in range(0, len(tris), 3)]
        me.from_pydata(vs, [], fs)
        uvl = me.uv_layers.new()
        for poly in me.polygons:
            poly.use_smooth = True
            for li in poly.loop_indices:
                uvl.data[li].uv = out_v[me.loops[li].vertex_index][1]
        me.materials.append(m)
        o = bpy.data.objects.new(name, me)
        sc.collection.objects.link(o)
    cd = bpy.data.cameras.new("c")
    cd.type = 'ORTHO'
    cd.ortho_scale = 0.75
    cam = bpy.data.objects.new("c", cd)
    sc.collection.objects.link(cam)
    at = Vector((0.0, -1.0, -0.13))
    cam.location = at
    cam.rotation_euler = (Vector((0.0, 0.0, -0.13)) - at).to_track_quat('-Z', 'Z').to_euler()
    sc.camera = cam
    sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    print("[ARMS] preview", PREVIEW)


main()
