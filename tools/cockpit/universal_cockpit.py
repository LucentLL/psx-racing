# THE OWNER'S UNIVERSAL COCKPIT (2026-10-14), converted for the game.
#
#   blender.exe --background --python tools/cockpit/universal_cockpit.py -- <repo root>
#
# Owner: "Just as a proof of concept, I made a rough cockpit to be used
# universally." Source: his packed .blend in the art folder (opened READ ONLY,
# never saved):
#   ...\PSX Racing\Universal_Cockpit_RX7_FD\RX7_FD_Universal_Cockpit.blend
# and its atlas, Cockpit_Atlas_2048.png (imported at max 1024).
#
# Writes into Assets/PSXRacing/Resources/Cockpit/ (Resources, because
# CockpitInterior.cs loads it at runtime, so no scene needs a rebake):
#   universal_cabin.obj        every static part, ONE mesh, world-lit
#   universal_dials.obj        the five dial faces + the clock display, the
#                              part that glows at night (own material)
#   universal_wheel.obj        the wheel in its PIVOT frame (below)
#   universal_hands.obj        the HandsPack hands re-posed onto THIS rim at
#                              nine and three, same frame as the wheel
#   universal_needle_*.obj     each needle in its pivot frame, turned to rest
#                              pointing straight UP (angle 0)
#   universal_atlas.png        the owner's atlas, copied as-is
#   universal_layout.json      the markers, pivots, dial calibration and the
#                              measurements CockpitInterior fits with
#
# WHY OBJ AND NOT FBX. The game has no glTF importer, and a Blender FBX lands
# in Unity under a x100 node with a -90 X axis node on every object (see the
# cargo seat's hundredths trap) - pivots that then need un-picking at runtime.
# Here every part is written in a frame chosen on purpose and the markers
# travel in the JSON, so nothing about Blender's axes reaches the game.
#
# FRAMES. Unity's: x right, y up, z forward, metres. The MODEL frame is the
# owner's root (floor level, cabin centreline): glTF (+X = driver's LEFT,
# +Y up, +Z forward) -> Unity (-x, y, z); from Blender (+X left, -Y forward,
# +Z up): u = (-bx, bz, -by). The driver sits at -x (left-hand drive). A PIVOT
# frame has its origin on the pivot, z along the rotation axis INTO the dash
# (away from the driver), y up the face, x right; CockpitInterior turns it
# about local z, positive = anticlockwise as the driver sees it.
#
# OBJ: Unity's importer reads an OBJ vertex (x, y, z) as (-x, y, z), so each
# Unity point is written with x negated. Every Blender->OBJ map here is a
# proper rotation (det +1), so Blender's winding stays front-facing.
import bpy, bmesh, json, math, os, shutil, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = ARGS[0] if ARGS else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "Cockpit")
SRC = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Universal_Cockpit_RX7_FD"
BLEND = os.path.join(SRC, "RX7_FD_Universal_Cockpit.blend")
ATLAS = os.path.join(SRC, "Cockpit_Atlas_2048.png")
GENERIC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "generic_cockpit.py")
P = "universal_"

# ---- SHARED with the game (ChaseCamera / CockpitLook / PizzaCargo) --------
VFOV, ASPECT, PITCH = 58.0, 16.0 / 9.0, 3.5      # the cockpit lens
LOOK_YAW, LOOK_DOWN = 80.0, 28.5                 # CockpitLook.MaxYaw / DownAtFull
BOX_HALF = 0.205                                 # a 16-inch box, 0.41 m
SQUAB_GAP = 0.015                                # PizzaCargo.SquabGapM
HAND_REACH = 0.20                                # hand + wrist kept, metres from the grip
# ---------------------------------------------------------------------------

# The printed dial scales, READ OFF THE ATLAS ART: value at each end and the
# angle of each end's mark, degrees clockwise from the art's own "up". The
# angle each one makes in 3D is MEASURED below through the dial face's UVs.
DIALS = {
    "Needle_Tachometer": ("tach", 0.0, 9000.0, -135.0, 135.0),   # 0-9 x1000 r/min, red from 7
    "Needle_Speedometer": ("speedo", 0.0, 180.0, -135.0, 135.0),  # 0-180 MPH
    "Needle_Fuel": ("fuel", 0.0, 1.0, -120.0, 120.0),           # E - F
    "Needle_Coolant": ("coolant", 0.0, 1.0, -120.0, 120.0),     # C - H
    "Needle_Oil": ("oil", 0.0, 120.0, -120.0, 120.0),           # 0 - 120 (psi)
}
# Where the dial faces are on the 2048 sheet (u0, v0, u1, v1; v UP): the
# tach + speedo panel and the strip of three small dials. The art sits on a
# grid - tach and speedo centred at (256, 1024) and (768, 1024) px, the small
# three at (1152|1408|1664, 896) - and each needle's pivot is the centre of
# its face's UV disc, which the run prints as a check.
DIAL_UV = [(0.0, 1 - 1236 / 2048, 985 / 2048, 1 - 738 / 2048),
           (985 / 2048, 1 - 990 / 2048, 1731 / 2048, 1 - 738 / 2048)]
GLOW_MATERIALS = {"Display"}      # the clock's LCD glows with the dials
DIAL_MATERIAL = "Instruments"     # dial faces are this material AND in DIAL_UV
                                  # (the bezel trim borrows the panel's dark edge)


def B2U(v):
    return Vector((-v.x, v.z, -v.y))


def frame_of(o):
    """A Blender object's pivot frame in Unity axes: (origin, 3x3 rows->cols)."""
    m = o.matrix_world
    r = m.to_3x3().normalized()
    xb, yb, zb = r.col[0], r.col[1], r.col[2]
    # Rotation axis = Blender local +Y (toward the driver), so INTO the dash
    # is -Y; up the face is +Z; right is -X (Blender +X is the driver's left).
    x_u, y_u, z_u = B2U(-xb), B2U(zb), B2U(-yb)
    R = Matrix((x_u, y_u, z_u)).transposed()      # columns = local axes
    return B2U(m.translation), R


def mesh_tris(o, to_unity):
    """[(material, [p0,p1,p2], [uv..], [n..])] with p, n in whatever frame
    to_unity(point, is_dir) maps a WORLD Blender vector into."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    uvl = me.uv_layers.active.data
    try:
        cn = [c.vector.copy() for c in me.corner_normals]
    except AttributeError:
        cn = None
    mw = o.matrix_world
    mn = mw.to_3x3().inverted().transposed()
    names = [s.material.name if s.material else "" for s in o.material_slots]
    out = []
    for lt in me.loop_triangles:
        ps, uvs, ns = [], [], []
        for k, li in enumerate(lt.loops):
            ps.append(to_unity(mw @ me.vertices[me.loops[li].vertex_index].co, False))
            uvs.append(tuple(uvl[li].uv))
            nl = cn[li] if cn else Vector(lt.split_normals[k])
            ns.append(to_unity((mn @ nl).normalized(), True))
        out.append((names[lt.material_index] if names else "", ps, uvs, ns))
    ev.to_mesh_clear()
    return out


def write_obj(path, tris):
    vi, ti, ni = {}, {}, {}
    vs, ts, nsl, fs = [], [], [], []

    def idx(table, lines, key, fmt):
        if key not in table:
            table[key] = len(table) + 1
            lines.append(fmt % key)
        return table[key]

    for _, ps, uvs, ns in tris:
        f = []
        for p, uv, n in zip(ps, uvs, ns):
            a = idx(vi, vs, (round(-p.x, 5), round(p.y, 5), round(p.z, 5)), "v %.5f %.5f %.5f")
            b = idx(ti, ts, (round(uv[0], 5), round(uv[1], 5)), "vt %.5f %.5f")
            c = idx(ni, nsl, (round(-n.x, 4), round(n.y, 4), round(n.z, 4)), "vn %.4f %.4f %.4f")
            f.append("%d/%d/%d" % (a, b, c))
        fs.append("f " + " ".join(f))
    with open(path, "w", newline="\n") as fh:
        fh.write("# PSX Racing universal cockpit - tools/cockpit/universal_cockpit.py\n")
        fh.write("o " + os.path.splitext(os.path.basename(path))[0] + "\n")
        fh.write("\n".join(vs + ts + nsl + fs) + "\n")
    return len(tris)


def in_dial_uv(uvs):
    return any(all(r[0] <= u <= r[2] and r[1] <= v <= r[3] for u, v in uvs) for r in DIAL_UV)


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


def main():
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    obs = {o.name: o for o in bpy.data.objects}
    mark = {n: B2U(obs[n].matrix_world.translation) for n in
            ("Driver_Eye", "Passenger_Seat_Surface", "Passenger_Cargo_Spawn", "Passenger_Footwell", "Soda_Spawn")}
    eye = mark["Driver_Eye"]
    layout = {"eye": v3(eye)}
    for k, n in (("seatSurface", "Passenger_Seat_Surface"), ("cargoSpawn", "Passenger_Cargo_Spawn"),
                 ("footwell", "Passenger_Footwell"), ("sodaSpawn", "Passenger_Footwell")):
        layout[k] = v3(mark[n])
    layout["sodaSpawn"] = v3(mark["Soda_Spawn"])

    # ---- the static cabin, split into world-lit and glowing ----------------
    cabin, glow = [], []
    every = []                                     # every triangle, model frame
    for o in bpy.data.objects:
        if o.type != 'MESH' or o.name == "Steering_Wheel" or o.name.startswith("Needle_"):
            continue
        for t in mesh_tris(o, model_map):
            is_glow = t[0] in GLOW_MATERIALS or (t[0] == DIAL_MATERIAL and in_dial_uv(t[2]))
            (glow if is_glow else cabin).append(t)
    every += cabin + glow
    n_cabin = write_obj(os.path.join(OUT, P + "cabin.obj"), cabin)
    n_glow = write_obj(os.path.join(OUT, P + "dials.obj"), glow)
    dash = [B2U(obs["Dashboard"].matrix_world @ v.co) for v in obs["Dashboard"].data.vertices]
    layout["dashFrontZ"] = round(max(v.z for v in dash), 5)

    # ---- the needles: pivot frame, rest turned to UP, dial calibrated ------
    needles = []
    n_needle = 0
    for name, (key, vmin, vmax, amin, amax) in DIALS.items():
        o = obs[name]
        origin, R = frame_of(o)
        tris = mesh_tris(o, pivot_map(origin, R))
        pts = [p for _, ps, _, _ in tris for p in ps]
        tip = max(pts, key=lambda p: p.x * p.x + p.y * p.y)
        rest = math.degrees(math.atan2(tip.x, tip.y))       # clockwise from up
        c, s = math.cos(math.radians(rest)), math.sin(math.radians(rest))
        def turn(p):                                        # anticlockwise by rest
            return Vector((p.x * c - p.y * s, p.x * s + p.y * c, p.z))
        tris = [(m, [turn(p) for p in ps], uvs, [turn(n) for n in ns]) for m, ps, uvs, ns in tris]
        n_needle += write_obj(os.path.join(OUT, P + "needle_" + key + ".obj"), tris)

        # The dial face round this pivot, and the map from ITS UVs to the
        # needle's plane: so the art's printed angles become 3D angles.
        Ri = R.transposed()
        reach = math.hypot(tip.x, tip.y) * 1.6
        rows = []
        for m, ps, uvs, _ in glow:
            if m != DIAL_MATERIAL:
                continue
            for p, uv in zip(ps, uvs):
                q = Ri @ (p - origin)
                if abs(q.z) < 0.02 and math.hypot(q.x, q.y) < reach and in_dial_uv([uv]):
                    rows.append((uv[0], uv[1], q.x, q.y))
        # Least squares [u v 1] M = [x y]
        import numpy as np
        A = np.array([[r[0], r[1], 1.0] for r in rows])
        B = np.array([[r[2], r[3]] for r in rows])
        M, *_ = np.linalg.lstsq(A, B, rcond=None)
        L = M[:2, :]                                        # rows: d/du, d/dv

        def ang(a):
            d = np.array([math.sin(math.radians(a)), math.cos(math.radians(a))]) @ L
            return math.degrees(math.atan2(d[0], d[1]))
        b0, b1, bm = ang(amin), ang(amax), ang((amin + amax) * 0.5)
        sweep = (b1 - b0) % 360.0
        # The sweep that passes through the middle mark, whichever way round.
        if ((bm - b0) % 360.0) > sweep:
            sweep -= 360.0
        centre_uv = np.linalg.lstsq(L.T, -M[2, :], rcond=None)[0]
        det = float(np.linalg.det(L))
        print("[UC] needle %-7s pivot %s rest %.1f deg; dial %d pts, uv centre (%.0f, %.0f)px, %s, %+.1f -> %+.1f (sweep %+.1f)"
              % (key, tuple(round(x, 3) for x in origin), rest, len(rows), centre_uv[0] * 2048,
                 (1 - centre_uv[1]) * 2048, "mirrored" if det < 0 else "upright", b0, b0 + sweep, sweep))
        q = R.to_quaternion()
        needles.append({"key": key, "pivot": v3(origin),
                        "rot": {"x": round(q.x, 6), "y": round(q.y, 6), "z": round(q.z, 6), "w": round(q.w, 6)},
                        "minValue": vmin, "maxValue": vmax,
                        "minDeg": round(b0, 2), "sweepDeg": round(sweep, 2)})
    layout["needles"] = needles

    # ---- the wheel ---------------------------------------------------------
    w = obs["Steering_Wheel"]
    origin, R = frame_of(w)
    wt = mesh_tris(w, pivot_map(origin, R))
    n_wheel = write_obj(os.path.join(OUT, P + "wheel.obj"), wt)
    wp = [p for _, ps, _, _ in wt for p in ps]
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
    print("[UC] wheel pivot %s tilt %.2f rim r %.4f (tube %.4f) plane z %+.4f"
          % (tuple(round(x, 3) for x in origin), layout["wheel"]["tiltDeg"], rim_r, tube, rim_z))

    # ---- the hands: the generic script's HandsPack pose, on THIS rim -------
    src = open(GENERIC, encoding="utf-8").read()
    src = src[:src.rstrip().rfind("\nmain()")]          # its definitions only
    ns = {"__name__": "generic_cockpit", "__file__": GENERIC}
    exec(compile(src, GENERIC, "exec"), ns)
    ns["RIM_R"] = rim_r
    hands = ns["build_hands"]()
    # Its frame is the hub frame (x right, y up, z into the dash) written
    # through G2B (x negated): Unity = (-bx, by, bz), then onto the rim plane.
    hmap = lambda p, d: Vector((-p.x, p.y, p.z)) if d else Vector((-p.x, p.y, p.z + rim_z))
    ht = mesh_tris(hands, hmap)
    # FOREARMS CUT at HAND_REACH from each grip. The pack's forearms run back
    # from the rim to elbows splayed out over the console - out of frame
    # looking ahead (they leave through the bottom edge), but straight across
    # the middle of the picture at full look-over to the passenger seat, as a
    # grey band a hand's width from the eye. Cut there, the stub's end stays
    # under the frame ahead and outside it at full look.
    grips = [Vector((-rim_r, 0.0, rim_z)), Vector((rim_r, 0.0, rim_z))]
    whole = len(ht)
    ht = [t for t in ht if any(all((p - g).length < HAND_REACH for p in t[1]) for g in grips)]
    print("[UC] hands: %d of %d tris kept within %.2f m of the grips" % (len(ht), whole, HAND_REACH))
    n_hands = write_obj(os.path.join(OUT, P + "hands.obj"), ht)

    # ---- the passenger seat: where the stack comes to rest -----------------
    verts, polys = [], []
    def add(tris):
        for _, ps, _, _ in tris:
            base = len(verts)
            verts.extend(ps)
            polys.append((base, base + 1, base + 2))
    add(every)
    tree = BVHTree.FromPolygons(verts, polys)

    def ray(o, d, dist=3.0):
        hit = tree.ray_cast(o, d.normalized(), dist)
        return hit[0]
    seat = mark["Passenger_Seat_Surface"]
    # The SEAT alone for its own shape: from above, a 0.41 m footprint also
    # reaches the door's armrest, which is not what a box rests on.
    sv, sp = [], []
    for _, ps, _, _ in mesh_tris(obs["Seat_Passenger"], model_map):
        base = len(sv)
        sv.extend(ps)
        sp.append((base, base + 1, base + 2))
    seat_tree = BVHTree.FromPolygons(sv, sp)

    def seat_ray(o, d, dist=3.0):
        return seat_tree.ray_cast(o, d.normalized(), dist)[0]
    # Squab face where it meets the cushion: a box's back edge stops there.
    sq = [seat_ray(seat + Vector((0, h, 0)), Vector((0, 0, -1))) for h in (0.03, 0.06, 0.10)]
    squab_z = max(p.z for p in sq if p is not None)
    # The stack stands on the owner's marker. Not SQUAB_GAP off the squab, as
    # on the island: this seat's back bolsters wrap forward round the squab
    # (0.41-0.44 m high at its corners), and a 0.41 m box pushed back to the
    # squab would rest on their tips 17 cm over the cushion.
    cz = seat.z
    grid, n = [], 8
    for iz in range(-n, n + 1):
        for ix in range(-n, n + 1):
            dx, dz = ix * BOX_HALF / n, iz * BOX_HALF / n
            h = seat_ray(Vector((seat.x + dx, 0.70, cz + dz)), Vector((0, -1, 0)))
            if h is not None:
                grid.append((dx, dz, h.y))
    import numpy as np
    G = np.array(grid)
    (a, b, c0), *_ = np.linalg.lstsq(np.c_[G[:, 0], G[:, 1], np.ones(len(G))], G[:, 2], rcond=None)
    lift = max(g[2] - (a * g[0] + b * g[1] + c0) for g in grid)
    c_top = c0 + lift                                     # the plane resting on the highest points
    under = [c_top + a * g[0] + b * g[1] - g[2] for g in grid]
    normal = Vector((-a, 1.0, -b)).normalized()
    layout["stack"] = {"centre": v3(Vector((seat.x, c_top, cz))), "normal": v3(normal),
                       "squabZ": round(squab_z, 5)}
    print("[UC] seat: squab face z %.3f, stack centre z %.3f (marker %.3f), back edge %.3f m off the squab; rest plane y %.4f at the centre "
          "(marker %.3f), slope %.1f deg fwd / %.1f deg side; gap under the box: 0 at its highest support, "
          "centre %.4f, mean %.4f, max %.4f"
          % (squab_z, cz, seat.z, cz - BOX_HALF - squab_z, c_top, seat.y, math.degrees(math.atan(b)), math.degrees(math.atan(a)),
             under[len(under) // 2], sum(under) / len(under), max(under)))

    # The passenger's footwell and the walls a box must stay inside.
    foot = mark["Passenger_Footwell"]
    floor = ray(foot + Vector((0, 0.3, 0)), Vector((0, -1, 0)))
    fire = ray(foot + Vector((0, 0.06, 0)), Vector((0, 0, 1)))
    door = [ray(seat + Vector((0, h, 0)), Vector((1, 0, 0))) for h in (0.05, 0.10, 0.15, 0.20, 0.30)]
    door += [ray(foot + Vector((0, h, 0)), Vector((1, 0, 0))) for h in (0.05, 0.12)]
    cons = [ray(foot + Vector((0, h, 0)), Vector((-1, 0, 0))) for h in (0.05, 0.12)]
    layout["room"] = {"floorY": round(floor.y, 5), "firewallZ": round(fire.z, 5),
                      "doorX": round(min(p.x for p in door if p is not None), 5),
                      "consoleX": round(max(p.x for p in cons if p is not None), 5)}
    print("[UC] room:", layout["room"])

    # ---- what the near plane must clear -------------------------------------
    # The wheel and hands at rest are part of the forward view.
    wv = [origin + R @ p for p in wp]
    wf = [(origin + R @ p) for _, ps, _, _ in wt for p in ps]
    hf = [(origin + R @ p) for _, ps, _, _ in ht for p in ps]
    for arr in (wf, hf):
        for i in range(0, len(arr), 3):
            base = len(verts)
            verts.extend(arr[i:i + 3])
            polys.append((base, base + 1, base + 2))
    tree = BVHTree.FromPolygons(verts, polys)

    def nearest(yaw, pitch):
        # Unity's Euler(pitch, yaw, 0) = Ry(yaw) Rx(pitch): pitch DOWN, then
        # yaw RIGHT, applied to the lens's axes.
        th, ph = math.radians(yaw), math.radians(pitch)
        fwd = Vector((math.sin(th) * math.cos(ph), -math.sin(ph), math.cos(th) * math.cos(ph)))
        right = Vector((math.cos(th), 0.0, -math.sin(th)))
        up = Vector((math.sin(th) * math.sin(ph), math.cos(ph), math.cos(th) * math.sin(ph)))
        ty = math.tan(math.radians(VFOV * 0.5))
        tx = ty * ASPECT
        best = 9.0
        for iy in range(-20, 21):
            for ix in range(-36, 37):
                d = fwd + right * (tx * ix / 36) + up * (ty * iy / 20)
                hit = tree.ray_cast(eye, d.normalized(), 3.0)
                if hit[0] is not None:
                    best = min(best, (hit[0] - eye).dot(fwd))
        return best
    near_fwd = nearest(0.0, PITCH)
    near_right = nearest(LOOK_YAW, PITCH + LOOK_DOWN)
    near_left = nearest(-LOOK_YAW, PITCH)
    layout["nearForward"] = round(near_fwd, 4)
    layout["nearLook"] = round(min(near_right, near_left), 4)
    print("[UC] nearest visible surface (authored m): forward %.3f, look right %.3f, look left %.3f"
          % (near_fwd, near_right, near_left))

    shutil.copyfile(ATLAS, os.path.join(OUT, P + "atlas.png"))
    with open(os.path.join(OUT, P + "layout.json"), "w", newline="\n") as fh:
        json.dump(layout, fh, indent=1)
    print("[UC] tris: cabin %d, dials %d, wheel %d, needles %d, hands %d; eye %s; dash front z %.3f"
          % (n_cabin, n_glow, n_wheel, n_needle, n_hands, tuple(round(x, 3) for x in eye), layout["dashFrontZ"]))


main()
