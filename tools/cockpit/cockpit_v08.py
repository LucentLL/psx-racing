# THE OWNER'S COCKPIT v08 (2026-10-16), converted for the game.
#
#   blender.exe --background --python tools/cockpit/cockpit_v08.py -- <repo root>
#   py tools/cockpit/cockpit_v08_tex.py                      (the textures)
#
# Source, opened READ ONLY and never saved:
#   ...\PSX Racing\Universal_Cockpit_RX7_FD_v08\RX7_FD_Cockpit_v08_LHD.blend
# 65,731 tris, 22 materials, four BLANK gauge faces (drawn in game), four
# glass panes (rain is drawn on them in game), a mirror face for a render
# texture. Writes
#   Assets/PSXRacing/CockpitSrc/v08_geo.bytes   every part, quantized (format
#       below; CockpitModel.cs reads it) - OUTSIDE Resources: it ships in the
#       on-demand StreamingAssets/cockpit.bundle (CockpitBundle.cs), not in
#       the main WebGL.data, which is near GitHub's 100 MB wall
#   Assets/PSXRacing/Resources/Cockpit/v08_layout.json   (4 KB, in the main
#       data: the fit and near plane need it before the bundle arrives)
#                     markers, wheel pivot, the seat the cargo rests on, the
#                     gauge discs, the panes' UV directions, the near plane
#
# ONLY THE LHD FILE IS CONVERTED. The RHD file is its exact mirror in X (every
# mesh's vertices and UV sets checked equal, 2026-10-16, gauges included with
# Tach<->Speed and Temp<->Fuel trading places), so the game draws RHD as the
# LHD model under a -1 x scale - half the download - and swaps the gauge
# roles. CockpitInterior says how the rest (cargo, look-over) follows.
#
# FRAMES. Unity's: x right, y up, z forward, metres. The MODEL frame is the
# owner's root (floor level, cabin centreline): from Blender (+X = the
# driver's LEFT, -Y forward, +Z up) u = (-bx, bz, -by). That map has
# determinant -1, so every triangle is written with its winding REVERSED to
# stay front-facing in Unity. The driver of this LHD file sits at -x. The
# wheel's PIVOT frame has its origin on the pivot, z along the column INTO
# the dash, y up the face, x right; the game turns it about local z.
#
# MATERIALS -> the game's: Dash_Vinyl, Console_ABS (+ the vents, a flat dark
# colour on the same grain), Headliner_Weave sample their textures on
# SurfaceUV (TEXCOORD_1, 0.128 m tiles); Wheel_Leather and Cockpit_Trim on
# the atlas UV; every flat-coloured material folds into ONE "solid" draw with
# its colour (sRGB) and roughness in the vertex colour. Each vertex carries
# just the UV its material samples, so Unity's tangents (RecalculateTangents,
# UV channel 0) are the normal maps' own.
#
# v08_geo.bytes, little-endian: 'PSXC', int version 1, int parts; per part:
#   str name, int verts, int submeshes,
#   float3 posMin, float3 posSpan, float2 uvMin, float2 uvSpan,
#   ushort3 pos[verts], sbyte3 normal[verts], ushort2 uv[verts], byte4 rgba[verts],
#   per submesh: str material, int indexCount, (ushort|uint) index[...]
#   (str = byte length + ASCII; uint indices when verts > 65535)
import bpy, json, math, os, struct, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
import numpy as np

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = ARGS[0] if ARGS else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "Cockpit")
SRC_OUT = os.path.join(ROOT, "Assets", "PSXRacing", "CockpitSrc")
SRC = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Universal_Cockpit_RX7_FD_v08"
BLEND = os.path.join(SRC, "RX7_FD_Cockpit_v08_LHD.blend")
INTEG = os.path.join(SRC, "Integration", "integration_LHD.json")
GENERIC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "generic_cockpit.py")

# ---- SHARED with the game (CockpitLook / PizzaCargo) ----------------------
ASPECT = 16.0 / 9.0
LOOK_YAW, LOOK_DOWN = 80.0, 28.5                 # CockpitLook.MaxYaw / DownAtFull
BOX_HALF = 0.205                                 # a 16-inch box, 0.41 m
HAND_REACH = 0.20                                # hand + wrist kept, metres from the grip
# ---------------------------------------------------------------------------

SURFACE = {"Dash_Vinyl": "vinyl", "Console_ABS": "abs", "Console_ABS_Vent": "abs", "Headliner_Weave": "headliner"}
OTHER = {"Wheel_Leather": "leather", "Cockpit_Trim": "trim",
         "Gauge_Display_Replace_Material": "gauge", "Mirror_Replace_RenderTexture": "mirror"}
GAUGES = {"Gauge_Temp_Blank": "temp", "Gauge_Tach_Blank": "tach", "Gauge_Speed_Blank": "speed", "Gauge_Fuel_Blank": "fuel"}
GLASS = {"Glass_Windshield": "windshield", "Glass_Door_L": "door_l", "Glass_Door_R": "door_r", "Glass_RearWindow": "rear"}


def B2U(v):
    return Vector((-v.x, v.z, -v.y))


def srgb8(c):
    c = max(0.0, min(1.0, c))
    s = c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return int(round(s * 255))


def mat_info(m):
    """(game material, vertex rgba) for a Blender material."""
    if m is None:
        return "solid", (40, 40, 40, 200)
    n = m.name
    bsdf = next((x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED'), None) if m.use_nodes else None

    def flat():
        if bsdf is None:
            return (40, 40, 40, 200)
        b = bsdf.inputs['Base Color'].default_value
        r = bsdf.inputs['Roughness'].default_value
        return (srgb8(b[0]), srgb8(b[1]), srgb8(b[2]), int(round(r * 255)))
    if n.startswith("Glass_"):
        return "glass", (255, 255, 255, 255)
    if n == "Console_ABS_Vent":
        r, g, b, _ = flat()
        return "abs", (r, g, b, 255)          # alpha 255 = this colour, not the texture's
    if n in SURFACE:
        return SURFACE[n], (0, 0, 0, 0)
    if n in OTHER:
        return OTHER[n], (0, 0, 0, 0)
    return "solid", flat()


def frame_of(o):
    m = o.matrix_world
    r = m.to_3x3().normalized()
    xb, yb, zb = r.col[0], r.col[1], r.col[2]
    x_u, y_u, z_u = B2U(-xb), B2U(zb), B2U(-yb)
    R = Matrix((x_u, y_u, z_u)).transposed()
    return B2U(m.translation), R


def mesh_tris(o, to_unity, uv_for=None):
    """[(material key, rgba, [p0,p1,p2], [uv..], [n..])] in the frame
    to_unity(point, is_dir) maps a WORLD Blender vector into. The UV is the
    one the material samples (uv_for(key) names the layer)."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    layers = {L.name: L.data for L in me.uv_layers}
    active = me.uv_layers.active.data if me.uv_layers.active else None
    try:
        cn = [c.vector.copy() for c in me.corner_normals]
    except AttributeError:
        cn = None
    mw = o.matrix_world
    mn = mw.to_3x3().inverted().transposed()
    infos = [mat_info(s.material) for s in o.material_slots] or [("solid", (40, 40, 40, 200))]
    out = []
    for lt in me.loop_triangles:
        key, rgba = infos[lt.material_index]
        name = uv_for(key) if uv_for else None
        uvl = layers.get(name) if name else None
        if uvl is None:
            uvl = layers.get("AtlasUV", active)
        ps, uvs, ns = [], [], []
        for k, li in enumerate(lt.loops):
            ps.append(to_unity(mw @ me.vertices[me.loops[li].vertex_index].co, False))
            uvs.append(tuple(uvl[li].uv) if uvl is not None else (0.0, 0.0))
            nl = cn[li] if cn else Vector(lt.split_normals[k])
            ns.append(to_unity((mn @ nl).normalized(), True))
        out.append((key, rgba, ps, uvs, ns))
    ev.to_mesh_clear()
    return out


def uv_for(key):
    return "SurfaceUV" if key in ("vinyl", "abs", "headliner") else "AtlasUV"


def model_map(p, is_dir):
    return B2U(p)


def pivot_map(origin, R):
    Ri = R.transposed()

    def f(p, is_dir):
        u = B2U(p)
        return Ri @ u if is_dir else Ri @ (u - origin)
    return f


def v3(v):
    return {"x": round(v.x, 5), "y": round(v.y, 5), "z": round(v.z, 5)}


def v2(a, b):
    return {"x": round(float(a), 5), "y": round(float(b), 5)}


# ---------------------------------------------------------------------------
#  The binary writer
# ---------------------------------------------------------------------------

def sstr(s):
    b = s.encode("ascii")
    return struct.pack("<B", len(b)) + b


def pack_part(name, tris):
    """tris: [(key, rgba, ps, uvs, ns)] -> bytes. Welded, quantized, winding
    reversed (the Blender->Unity map is a reflection)."""
    P = np.array([[p.x, p.y, p.z] for t in tris for p in t[2]], dtype=np.float64)
    U = np.array([uv for t in tris for uv in t[3]], dtype=np.float64)
    pmin, pspan = P.min(0), np.maximum(P.max(0) - P.min(0), 1e-6)
    umin, uspan = U.min(0), np.maximum(U.max(0) - U.min(0), 1e-6)
    keys = []
    for t in tris:
        if t[0] not in keys:
            keys.append(t[0])
    vid, verts = {}, []
    idx = {k: [] for k in keys}
    for key, rgba, ps, uvs, ns in tris:
        tri = []
        for p, uv, n in zip(ps, uvs, ns):
            q = (tuple(int(round((c - lo) / sp * 65535)) for c, lo, sp in zip((p.x, p.y, p.z), pmin, pspan)),
                 tuple(int(round(max(-1.0, min(1.0, c)) * 127)) for c in (n.x, n.y, n.z)),
                 tuple(int(round((c - lo) / sp * 65535)) for c, lo, sp in zip(uv, umin, uspan)),
                 rgba)
            if q not in vid:
                vid[q] = len(verts)
                verts.append(q)
            tri.append(vid[q])
        idx[key] += [tri[0], tri[2], tri[1]]
    n = len(verts)
    b = bytearray(sstr(name))
    b += struct.pack("<ii", n, len(keys))
    b += struct.pack("<6f", *pmin, *pspan) + struct.pack("<4f", *umin, *uspan)
    b += b"".join(struct.pack("<3H", *v[0]) for v in verts)
    b += b"".join(struct.pack("<3b", *v[1]) for v in verts)
    b += b"".join(struct.pack("<2H", *v[2]) for v in verts)
    b += b"".join(struct.pack("<4B", *v[3]) for v in verts)
    fmt = "<%dI" if n > 65535 else "<%dH"
    for k in keys:
        b += sstr(k) + struct.pack("<i", len(idx[k])) + struct.pack(fmt % len(idx[k]), *idx[k])
    print("[V08] part %-16s %6d tris %6d verts  %s" % (name, len(tris), n, {k: len(idx[k]) // 3 for k in keys}))
    return bytes(b)


def uv_frame(tris):
    """Least squares P = P0 + u*Pu + v*Pv over a part's vertices."""
    rows = [(uv[0], uv[1], p) for t in tris for p, uv in zip(t[2], t[3])]
    A = np.array([[r[0], r[1], 1.0] for r in rows])
    B = np.array([[r[2].x, r[2].y, r[2].z] for r in rows])
    M, *_ = np.linalg.lstsq(A, B, rcond=None)
    return Vector(M[0]), Vector(M[1]), Vector(M[2])


def uv_dir(Pu, Pv, d):
    """A model-frame direction as a UV-space direction (unit, in metres-true
    proportions: divide by the metres per unit of each axis)."""
    J = np.array([[Pu.x, Pv.x], [Pu.y, Pv.y], [Pu.z, Pv.z]])
    s, *_ = np.linalg.lstsq(J, np.array([d.x, d.y, d.z]), rcond=None)
    l = math.hypot(s[0], s[1]) or 1.0
    return s[0] / l, s[1] / l


def main():
    os.makedirs(OUT, exist_ok=True)
    integ = json.load(open(INTEG, encoding="utf-8"))
    cam = integ["camera"]
    fwd = cam["forward_direction_gltf"]
    pitch = math.degrees(math.asin(-fwd[1]))
    vfov = cam["vertical_fov_degrees"]
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    obs = {o.name: o for o in bpy.data.objects}
    mark = {n: B2U(obs[n].matrix_world.translation) for n in
            ("Driver_Eye", "Passenger_Seat_Surface", "Passenger_Cargo_Spawn", "Passenger_Footwell", "Soda_Spawn")}
    eye = mark["Driver_Eye"]
    layout = {"eye": v3(eye), "seatSurface": v3(mark["Passenger_Seat_Surface"]),
              "cargoSpawn": v3(mark["Passenger_Cargo_Spawn"]), "footwell": v3(mark["Passenger_Footwell"]),
              "sodaSpawn": v3(mark["Soda_Spawn"]),
              "camPitch": round(pitch, 3), "camVFov": round(vfov, 4), "camNear": round(cam["znear"], 4)}

    parts = []
    cabin, mirror_face, every = [], [], []
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        if o.type != 'MESH' or o.name == "Steering_Wheel":
            continue
        tris = mesh_tris(o, model_map, uv_for)
        if o.name in GAUGES or o.name in GLASS:
            every += tris
            continue
        for t in tris:
            (mirror_face if t[0] == "mirror" else cabin).append(t)
    every += cabin + mirror_face
    parts.append(pack_part("cabin", cabin))
    parts.append(pack_part("mirror", mirror_face))

    # ---- gauges: discs, roles, which way their canvases run ---------------
    gauges = []
    for name, role in GAUGES.items():
        tris = mesh_tris(obs[name], model_map, uv_for)
        parts.append(pack_part("gauge_" + role, tris))
        pts = [p for t in tris for p in t[2]]
        c = sum(pts, Vector()) / len(pts)
        r = max((p - c).length for p in pts)
        nrm = sum((n for t in tris for n in t[4]), Vector()).normalized()
        Pu, Pv, P0 = uv_frame(tris)
        uvs = [uv for t in tris for uv in t[3]]
        gauges.append({"key": role, "part": "gauge_" + role, "centre": v3(c), "radius": round(r, 5),
                       "normal": v3(nrm), "uRight": 1 if Pu.x > 0 else -1, "vUp": 1 if Pv.y > 0 else -1})
        print("[V08] gauge %-5s centre %s r %.4f uv u[%.3f..%.3f] v[%.3f..%.3f] uRight %+d vUp %+d"
              % (role, tuple(round(x, 3) for x in c), r, min(u for u, _ in uvs), max(u for u, _ in uvs),
                 min(v for _, v in uvs), max(v for _, v in uvs), gauges[-1]["uRight"], gauges[-1]["vUp"]))
    layout["gauges"] = gauges

    # ---- glass: per pane, where UP and REARWARD point in its UV space ------
    panes = []
    for name, key in GLASS.items():
        tris = mesh_tris(obs[name], model_map, uv_for)
        parts.append(pack_part("glass_" + key, tris))
        Pu, Pv, P0 = uv_frame(tris)
        up = uv_dir(Pu, Pv, Vector((0, 1, 0)))
        rear = uv_dir(Pu, Pv, Vector((0, 0, -1)))
        out = uv_dir(Pu, Pv, Vector((1, 0, 0)))
        pts = [p for t in tris for p in t[2]]
        c = sum(pts, Vector()) / len(pts)
        panes.append({"key": key, "part": "glass_" + key, "up": v2(*up), "rear": v2(*rear), "right": v2(*out),
                      "metresU": round(Pu.length, 4), "metresV": round(Pv.length, 4), "centre": v3(c)})
        print("[V08] glass %-10s m/U %.3f m/V %.3f up(uv) %+.2f %+.2f rear(uv) %+.2f %+.2f"
              % (key, Pu.length, Pv.length, up[0], up[1], rear[0], rear[1]))
    layout["glass"] = panes
    Pu, Pv, P0 = uv_frame(mirror_face)
    mc = sum((p for t in mirror_face for p in t[2]), Vector()) / (3 * len(mirror_face))
    layout["mirror"] = {"part": "mirror", "centre": v3(mc), "uRight": 1 if Pu.x > 0 else -1, "vUp": 1 if Pv.y > 0 else -1,
                        "width": round(Pu.length, 4), "height": round(Pv.length, 4)}
    print("[V08] mirror face", layout["mirror"])

    dash = [B2U(obs["Dashboard"].matrix_world @ v.co) for v in obs["Dashboard"].data.vertices]
    layout["dashFrontZ"] = round(max(v.z for v in dash), 5)
    # The dash's top where the windscreen meets it, centre band (2026-10-16):
    # the line from his eye over it is the one ChaseCamera.CockpitEye stands
    # on the shell's cowl. Measured off the shipped file it is (0, 0.748, 0.95),
    # 8.7 deg under the eye. The flattest line over the dash near the glass base.
    wsv = [B2U(obs["Glass_Windshield"].matrix_world @ v.co) for v in obs["Glass_Windshield"].data.vertices]
    wsv = [p for p in wsv if abs(p.x) < 0.3]
    zb = max(p.z for p in wsv if p.y < min(q.y for q in wsv) + 0.01)
    lip = [p for p in dash if abs(p.x) < 0.3 and p.z > zb - 0.15 and p.y < eye.y - 0.05 and p.z - eye.z > 0.5]
    sb = min(lip, key=lambda p: (eye.y - p.y) / (p.z - eye.z)) if lip else min(wsv, key=lambda p: p.y)
    layout["screenBase"] = {"x": 0.0, "y": round(sb.y, 4), "z": round(sb.z, 4)}

    # ---- the wheel ---------------------------------------------------------
    w = obs["Steering_Wheel"]
    origin, R = frame_of(w)
    wt = mesh_tris(w, pivot_map(origin, R), uv_for)
    parts.append(pack_part("wheel", wt))
    wp = [p for t in wt for p in t[2]]
    rmax = max(math.hypot(p.x, p.y) for p in wp)
    rim = [p for p in wp if math.hypot(p.x, p.y) > rmax - 0.01]
    zlo, zhi = min(p.z for p in rim), max(p.z for p in rim)
    tube = (zhi - zlo) * 0.5
    rim_r, rim_z = rmax - tube, (zlo + zhi) * 0.5
    q = R.to_quaternion()
    layout["wheel"] = {"pivot": v3(origin),
                       "rot": {"x": round(q.x, 6), "y": round(q.y, 6), "z": round(q.z, 6), "w": round(q.w, 6)},
                       "tiltDeg": round(math.degrees(math.atan2(-R.col[2].y, R.col[2].z)), 3),
                       "rimRadius": round(rim_r, 5), "rimZ": round(rim_z, 5), "tube": round(tube, 5)}
    print("[V08] wheel pivot %s tilt %.2f rim r %.4f (tube %.4f) plane z %+.4f"
          % (tuple(round(x, 3) for x in origin), layout["wheel"]["tiltDeg"], rim_r, tube, rim_z))

    # ---- the hands: the generic script's HandsPack pose, on THIS rim -------
    src = open(GENERIC, encoding="utf-8").read()
    src = src[:src.rstrip().rfind("\nmain()")]
    ns = {"__name__": "generic_cockpit", "__file__": GENERIC}
    exec(compile(src, GENERIC, "exec"), ns)
    ns["RIM_R"] = rim_r
    hands = ns["build_hands"]()
    hmap = lambda p, d: Vector((-p.x, p.y, p.z)) if d else Vector((-p.x, p.y, p.z + rim_z))
    ht = [("skin", (0, 0, 0, 0), ps, uvs, nn) for _, _, ps, uvs, nn in mesh_tris(hands, hmap)]
    grips = [Vector((-rim_r, 0.0, rim_z)), Vector((rim_r, 0.0, rim_z))]
    whole = len(ht)
    ht = [t for t in ht if any(all((p - g).length < HAND_REACH for p in t[2]) for g in grips)]
    print("[V08] hands: %d of %d tris kept within %.2f m of the grips" % (len(ht), whole, HAND_REACH))
    parts.append(pack_part("hands", ht))

    # ---- the passenger seat: where the stack comes to rest -----------------
    verts, polys = [], []

    def add(tris):
        for t in tris:
            base = len(verts)
            verts.extend(t[2])
            polys.append((base, base + 1, base + 2))
    add([t for t in every if t[0] != "glass"])
    tree = BVHTree.FromPolygons(verts, polys)

    def ray(o, d, dist=3.0):
        return tree.ray_cast(o, d.normalized(), dist)[0]
    seat = mark["Passenger_Seat_Surface"]
    sv, sp = [], []
    for t in mesh_tris(obs["Seat_Passenger"], model_map, uv_for):
        base = len(sv)
        sv.extend(t[2])
        sp.append((base, base + 1, base + 2))
    seat_tree = BVHTree.FromPolygons(sv, sp)

    def seat_ray(o, d, dist=3.0):
        return seat_tree.ray_cast(o, d.normalized(), dist)[0]
    sq = [seat_ray(seat + Vector((0, h, 0)), Vector((0, 0, -1))) for h in (0.03, 0.06, 0.10)]
    squab_z = max(p.z for p in sq if p is not None)
    cz = seat.z
    grid, n = [], 8
    for iz in range(-n, n + 1):
        for ix in range(-n, n + 1):
            dx, dz = ix * BOX_HALF / n, iz * BOX_HALF / n
            h = seat_ray(Vector((seat.x + dx, 0.70, cz + dz)), Vector((0, -1, 0)))
            if h is not None:
                grid.append((dx, dz, h.y))
    G = np.array(grid)
    (a, b, c0), *_ = np.linalg.lstsq(np.c_[G[:, 0], G[:, 1], np.ones(len(G))], G[:, 2], rcond=None)
    lift = max(g[2] - (a * g[0] + b * g[1] + c0) for g in grid)
    c_top = c0 + lift
    normal = Vector((-a, 1.0, -b)).normalized()
    layout["stack"] = {"centre": v3(Vector((seat.x, c_top, cz))), "normal": v3(normal), "squabZ": round(squab_z, 5)}
    print("[V08] seat: squab z %.3f, stack centre (%.3f, %.3f, %.3f), slope %.1f fwd / %.1f side deg"
          % (squab_z, seat.x, c_top, cz, math.degrees(math.atan(b)), math.degrees(math.atan(a))))
    foot = mark["Passenger_Footwell"]
    floor = ray(foot + Vector((0, 0.3, 0)), Vector((0, -1, 0)))
    fire = ray(foot + Vector((0, 0.06, 0)), Vector((0, 0, 1)))
    door = [ray(seat + Vector((0, h, 0)), Vector((1, 0, 0))) for h in (0.05, 0.10, 0.15, 0.20, 0.30)]
    door += [ray(foot + Vector((0, h, 0)), Vector((1, 0, 0))) for h in (0.05, 0.12)]
    cons = [ray(foot + Vector((0, h, 0)), Vector((-1, 0, 0))) for h in (0.05, 0.12)]
    layout["room"] = {"floorY": round(floor.y, 5), "firewallZ": round(fire.z, 5),
                      "doorX": round(min(p.x for p in door if p is not None), 5),
                      "consoleX": round(max(p.x for p in cons if p is not None), 5)}
    print("[V08] room:", layout["room"])

    # ---- what the near plane must clear (glass excluded: it is see-through)
    for arr in ([origin + R @ p for t in wt for p in t[2]], [origin + R @ p for t in ht for p in t[2]]):
        for i in range(0, len(arr), 3):
            base = len(verts)
            verts.extend(arr[i:i + 3])
            polys.append((base, base + 1, base + 2))
    tree = BVHTree.FromPolygons(verts, polys)

    def nearest(yaw, pitch_deg):
        th, ph = math.radians(yaw), math.radians(pitch_deg)
        f = Vector((math.sin(th) * math.cos(ph), -math.sin(ph), math.cos(th) * math.cos(ph)))
        right = Vector((math.cos(th), 0.0, -math.sin(th)))
        up = Vector((math.sin(th) * math.sin(ph), math.cos(ph), math.cos(th) * math.sin(ph)))
        ty = math.tan(math.radians(vfov * 0.5))
        tx = ty * ASPECT
        best = 9.0
        for iy in range(-20, 21):
            for ix in range(-36, 37):
                d = f + right * (tx * ix / 36) + up * (ty * iy / 20)
                hit = tree.ray_cast(eye, d.normalized(), 3.0)
                if hit[0] is not None:
                    best = min(best, (hit[0] - eye).dot(f))
        return best
    near_fwd = nearest(0.0, pitch)
    near_pass = nearest(LOOK_YAW, pitch + LOOK_DOWN)
    near_door = nearest(-LOOK_YAW, pitch)
    layout["nearForward"] = round(near_fwd, 4)
    layout["nearLook"] = round(min(near_pass, near_door), 4)
    layout["nearPassenger"] = round(near_pass, 4)
    layout["nearDoor"] = round(near_door, 4)
    print("[V08] camera pitch %.2f vfov %.3f; nearest surface (authored m): forward %.3f, passenger %.3f, door %.3f"
          % (pitch, vfov, near_fwd, near_pass, near_door))

    blob = b"PSXC" + struct.pack("<ii", 1, len(parts)) + b"".join(parts)
    os.makedirs(SRC_OUT, exist_ok=True)
    with open(os.path.join(SRC_OUT, "v08_geo.bytes"), "wb") as fh:
        fh.write(blob)
    with open(os.path.join(OUT, "v08_layout.json"), "w", newline="\n") as fh:
        json.dump(layout, fh, indent=1)
    print("[V08] wrote v08_geo.bytes %d bytes; eye %s; dash front z %.3f"
          % (len(blob), tuple(round(x, 3) for x in eye), layout["dashFrontZ"]))


main()
