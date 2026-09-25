# Turn a ripped PS1-era car (a Sketchfab GLB of a Gran Turismo model, say)
# into the shape export_models.mjs takes: ONE OBJ, a body object and four
# objects named Wheel_FL/FR/RL/RR, on ONE atlas (256, or "atlasPx").
#
# Why a bake rather than reusing the rip's UVs: a PS1 car is a VRAM page drawn
# through several palettes (the GT1 Viper is one 256 page under twelve CLUTs,
# one per mesh, and 14% of the texels they use are the same texel under
# different colours), the GT2 rips are 1024x256 sheets, and some parts are
# untextured flat colours. The game's car shader takes ONE sheet, clamped to
# 256 px (512 for "*_atlas_512"), so every source is baked onto fresh UVs in
# one atlas - the only route that treats all of those the same way.
#
#   blender -b --factory-startup -P convert_rip.py -- <config.json> [probe]
#
# config: { "key", "src", "out", "lengthM",
#           "yaw": "pca" | degrees,          rotate about up so length runs along Y
#           "front": "+Y" | "-Y",            after the yaw, which end is the nose
#           "wheels": {"objects": {"<parent or object name>": "FL", ...}}
#                   | {"split": ["<mesh>", ...], "maxDimM": 0.9},
#           "drop": [mesh names that are not bodywork],
#           "recalcNormals": true   make winding consistent after the weld,
#           "matteBelowM": 0.75 }   black texels below this height are matte
# "probe" prints the aligned loose parts instead of exporting, to choose by.
import bpy, bmesh, json, math, os, sys
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
cfg = json.load(open(argv[0]))
probe = len(argv) > 1 and argv[1] == "probe"
out = cfg["out"]
os.makedirs(out, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=cfg["src"])
scene = bpy.context.scene


def top_named(o):
    # The wheel's own name lives on an EMPTY above the mesh in these rips.
    names = []
    while o is not None:
        names.append(o.name)
        o = o.parent
    return names


# ---- bake every transform into the vertices, one mesh per object -----------
meshes = []
# "drop": meshes that are not bodywork. GT1 draws its reflection pass as a
# second shell over the paint (the Viper's Object_6: 281 triangles on the
# starfield page, 267 coincident with painted faces, the rest the same panels
# split differently). Kept, it fights the paint face for face and paints dark
# panels where it wins; kept in part, its "unique" faces blacked out a rear
# quarter. Dropped whole it leaves nothing missing (with recalcNormals on).
for o in list(scene.objects):
    if o.type != 'MESH' or o.name in cfg.get("drop", []):
        continue
    me = o.data.copy()
    mw = o.matrix_world.copy()
    me.transform(mw)
    if mw.determinant() < 0:
        me.flip_normals()          # a negative scale turns the mesh inside out
    n = bpy.data.objects.new(o.name + "_w", me)
    n["chain"] = "|".join(top_named(o))
    scene.collection.objects.link(n)
    meshes.append(n)
for o in list(scene.objects):
    if o not in meshes:
        bpy.data.objects.remove(o, do_unlink=True)

allv = [v.co.copy() for m in meshes for v in m.data.vertices]

# ---- up is whichever axis is shortest: glTF import is Z-up already ---------
# ---- yaw: principal axis of the plan onto Y ---------------------------------
if cfg.get("yaw", 0) == "pca":
    mx = sum(v.x for v in allv) / len(allv); my = sum(v.y for v in allv) / len(allv)
    sxx = sum((v.x - mx) ** 2 for v in allv); syy = sum((v.y - my) ** 2 for v in allv)
    sxy = sum((v.x - mx) * (v.y - my) for v in allv)
    ang = 0.5 * math.atan2(2 * sxy, sxx - syy)          # major axis angle from +X
    yaw = math.pi / 2 - ang                              # turn it onto +Y
else:
    yaw = math.radians(float(cfg.get("yaw", 0)))
R = Matrix.Rotation(yaw, 4, 'Z')
if cfg.get("front", "+Y") == "-Y":
    R = Matrix.Rotation(math.pi, 4, 'Z') @ R
for m in meshes:
    m.data.transform(R)

# ---- scale to the real car's length, sit on z=0, centre in plan ------------
def bbox(objs):
    lo = Vector((1e18,) * 3); hi = Vector((-1e18,) * 3)
    for m in objs:
        for v in m.data.vertices:
            for i in range(3):
                lo[i] = min(lo[i], v.co[i]); hi[i] = max(hi[i], v.co[i])
    return lo, hi
lo, hi = bbox(meshes)
s = cfg["lengthM"] / (hi.y - lo.y)
T = Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))
for m in meshes:
    m.data.transform(T)
lo, hi = bbox(meshes)
print("ALIGNED size=(%.3f, %.3f, %.3f) scale=%.5f yaw=%.1f" % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, s, math.degrees(yaw)))


# ---- wheels ------------------------------------------------------------------
def corner(c):
    return ("F" if c.y > 0 else "R") + ("L" if c.x < 0 else "R")
# Left is -X with the nose on +Y and Z up (right-handed: right = fwd x up = +X).

wheel_objs = {k: [] for k in ("FL", "FR", "RL", "RR")}
body_objs = []
wcfg = cfg["wheels"]
if "objects" in wcfg:
    for m in meshes:
        hit = None
        for name in m["chain"].split("|"):
            if name in wcfg["objects"]:
                hit = name
        if hit:
            c = sum((v.co for v in m.data.vertices), Vector()) / len(m.data.vertices)
            wheel_objs[corner(c)].append(m)   # measured corner, not the rip's label
        else:
            body_objs.append(m)
else:
    split = set(wcfg["split"])
    maxd = wcfg.get("maxDimM", 0.9)
    for m in meshes:
        if m.name[:-2] not in split:
            body_objs.append(m)
            continue
        bpy.ops.object.select_all(action='DESELECT')
        m.select_set(True); bpy.context.view_layer.objects.active = m
        bpy.ops.mesh.separate(type='LOOSE')
        parts = list(bpy.context.selected_objects)
        # A PS1 tyre is not one shell: two flat sidewall DISCS and a ring of
        # loose tread quads. Find the discs (thin across the car, round in
        # side view, touching the ground), then a part belongs to a wheel when
        # its centre lies inside that disc's circle in side view and within a
        # tyre's width of it across the car.
        info = []
        for p in parts:
            vs = [v.co for v in p.data.vertices]
            a = Vector((min(v[i] for v in vs) for i in range(3))); b = Vector((max(v[i] for v in vs) for i in range(3)))
            info.append((p, a, b, b - a, (a + b) / 2))
        discs = [(c, max(d.y, d.z) / 2) for p, a, b, d, c in info
                 if d.x < 0.06 and 0.4 < d.y <= maxd and abs(d.y - d.z) < 0.08 and a.z < 0.1]
        for p, a, b, d, c in info:
            hit = None
            for dc, r in discs:
                if (math.hypot(c.y - dc.y, c.z - dc.z) <= r + 0.01 and abs(c.x - dc.x) <= 0.4
                        and (c.x > 0) == (dc.x > 0)):
                    hit = dc
            print("PART %-22s tris=%4d c=(%.2f,%.2f,%.2f) d=(%.2f,%.2f,%.2f) %s" % (
                p.name, len(p.data.polygons), c.x, c.y, c.z, d.x, d.y, d.z, "WHEEL " + corner(hit) if hit else ""))
            (wheel_objs[corner(hit)] if hit else body_objs).append(p)

for k, v in wheel_objs.items():
    print("WHEEL %s parts=%d" % (k, len(v)))
if probe:
    sys.exit(0)
for k, v in wheel_objs.items():
    if not v:
        raise SystemExit("no wheel at " + k)

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    return o

body = join(body_objs, "Body")
wheels = [join(v, "Wheel_" + k) for k, v in wheel_objs.items()]
parts = [body] + wheels

# WINDING. The GT1 Viper is wound clockwise throughout (its GLB is drawn
# double-sided, so nothing showed it); the game culls back faces, and would
# draw the inside of the far side. A negative signed volume on a part - even
# an open one - says most of its faces point in: flip the lot.
for o in parts:
    bm = bmesh.new(); bm.from_mesh(o.data)
    # WELD first. A PS1 mesh stores every triangle with vertices of its own
    # (a vertex carries one UV and one CLUT), so to the unwrapper each
    # triangle is an island - the Viper's atlas came out as a spray of dots
    # with most of the sheet empty. Coincident corners welded, the unwrapper
    # sees panels, and the hairline cracks between triangles close too.
    n0 = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    print("WELD %s %d -> %d verts" % (o.name, n0, len(bm.verts)))
    if cfg.get("recalcNormals"):
        # Winding made consistent across each welded panel, pointing out.
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bm.calc_volume(signed=True) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        print("FLIPPED " + o.name)
    bm.to_mesh(o.data)
    bm.free()

# ---- one atlas ---------------------------------------------------------------
# "atlasPx": 512 for these (owner, 2026-09-25). A GT2 1024x256 sheet is four
# PS1 pages; 512x512 holds the same texels, the original car's own density.
# The builder lets "*_atlas_512" sheets under Art/Car/Models through its 256
# clamp (ConfigureTextureImporters) and nothing else.
N = int(cfg.get("atlasPx", 256))
ATLAS = "%s_atlas_%d" % (cfg["key"], N)
atlas = bpy.data.images.new(ATLAS, N, N, alpha=True)
atlas.generated_color = (0.05, 0.05, 0.05, 1)
for o in parts:
    o.data.uv_layers.new(name="atlas")
# Crisp sources: a PS1 page is meant to be read texel by texel.
for img in bpy.data.images:
    pass
for mat in bpy.data.materials:
    if not mat.use_nodes:
        continue
    nt = mat.node_tree
    for n in nt.nodes:
        if n.type == 'TEX_IMAGE':
            n.interpolation = 'Closest'
    # Bake the colour the player should see, lit by nothing: emit the base
    # colour (image or flat) and bake EMIT.
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    outn = next((n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'), None)
    if bsdf and outn:
        em = nt.nodes.new('ShaderNodeEmission')
        bc = bsdf.inputs['Base Color']
        if bc.is_linked:
            nt.links.new(bc.links[0].from_socket, em.inputs['Color'])
        else:
            em.inputs['Color'].default_value = bc.default_value
        nt.links.new(em.outputs['Emission'], outn.inputs['Surface'])
    tgt = nt.nodes.new('ShaderNodeTexImage')
    tgt.image = atlas; tgt.interpolation = 'Closest'
    nt.nodes.active = tgt

# Lay the new UVs out on every part at once, sharing the one sheet.
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
    o.data.uv_layers.active = o.data.uv_layers["atlas"]
bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=0.006, area_weight=0.0, scale_to_bounds=True)
bpy.ops.object.mode_set(mode='OBJECT')

# The bake READS the source UVs and WRITES the atlas ones: render with the
# source map, bake into the active (atlas) one.
for o in parts:
    src = [u for u in o.data.uv_layers if u.name != "atlas"]
    (src[0] if src else o.data.uv_layers["atlas"]).active_render = True
    o.data.uv_layers.active = o.data.uv_layers["atlas"]
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 4
scene.render.bake.margin = 3
scene.render.bake.use_clear = True
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.bake(type='EMIT')

# MATTE FLAG. These cars have no cut-out wheel arches: the wells, sills and
# intakes are BLACK PAINT on the body panels, and PSX/CarPaint reads dark
# colourless texels as glass - every arch became a grey sky mirror. Texels
# that are near-black under body faces low on the car (every window is above
# MatteBelowM) get alpha 0.5, which the shader takes as "never glass".
import numpy as np
MatteBelowM = cfg.get("matteBelowM", 0.75)
px = np.array(atlas.pixels[:], dtype=np.float32).reshape(N, N, 4)
mask = np.zeros((N, N), bool)
me = body.data
uvl = me.uv_layers["atlas"].data
me.calc_loop_triangles()
for lt in me.loop_triangles:
    zc = sum(me.vertices[v].co.z for v in lt.vertices) / 3.0
    if zc >= MatteBelowM:
        continue
    t = [uvl[l].uv * N for l in lt.loops]
    x0 = int(max(0, math.floor(min(p.x for p in t)))); x1 = int(min(N - 1, math.ceil(max(p.x for p in t))))
    y0 = int(max(0, math.floor(min(p.y for p in t)))); y1 = int(min(N - 1, math.ceil(max(p.y for p in t))))
    (ax, ay), (bx, by), (cx, cy) = [(p.x, p.y) for p in t]
    den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(den) < 1e-9:
        continue
    ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
    X = xs + 0.5; Y = ys + 0.5
    l1 = ((by - cy) * (X - cx) + (cx - bx) * (Y - cy)) / den
    l2 = ((cy - ay) * (X - cx) + (ax - cx) * (Y - cy)) / den
    inside = (l1 >= -0.02) & (l2 >= -0.02) & (1 - l1 - l2 >= -0.02)
    mask[y0:y1 + 1, x0:x1 + 1] |= inside
rgb = px[..., :3]
lum = rgb @ np.array([0.30, 0.59, 0.11], dtype=np.float32)
hi_ = rgb.max(-1); lo_ = rgb.min(-1)
chroma = (hi_ - lo_) / np.maximum(hi_, 0.08)
matte = mask & (lum < 0.12) & (chroma < 0.5)
px[..., 3] = np.where(matte, 0.5, 1.0)
atlas.pixels[:] = px.ravel().tolist()
print("MATTE %d texels flagged (%.1f%% of the sheet)" % (matte.sum(), 100.0 * matte.sum() / (N * N)))
atlas_path = os.path.join(out, ATLAS + ".png")
atlas.filepath_raw = atlas_path; atlas.file_format = 'PNG'; atlas.save()

# ---- keep only the atlas UVs and one material ---------------------------------
mat = bpy.data.materials.new("PSX")
for o in parts:
    for uvl in [u for u in o.data.uv_layers if u.name != "atlas"]:
        o.data.uv_layers.remove(uvl)
    o.data.materials.clear()
    o.data.materials.append(mat)

# Closed-ness and winding, reported rather than assumed.
for o in parts:
    bm = bmesh.new(); bm.from_mesh(o.data)
    vol = bm.calc_volume(signed=True)
    openE = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    print("PARTSTAT %-9s tris=%d open=%d vol=%.4f" % (o.name, sum(len(f.verts) - 2 for f in bm.faces), openE, vol))
    bm.free()

obj_path = os.path.join(out, cfg["key"] + ".obj")
bpy.ops.object.select_all(action='SELECT')
bpy.ops.wm.obj_export(filepath=obj_path, export_selected_objects=True, export_materials=False,
                      forward_axis='NEGATIVE_Z', up_axis='Y', export_normals=True, export_uv=True,
                      export_triangulated_mesh=False)
with open(os.path.join(out, cfg["key"] + ".mtl"), "w") as f:
    f.write("newmtl PSX\nKd 1 1 1\nillum 1\nmap_Kd %s.png\n" % ATLAS)
# Blender only writes mtllib when it writes materials; add the line so the OBJ
# names its sheet like every other source.
txt = open(obj_path).read()
open(obj_path, "w").write("mtllib %s.mtl\n" % cfg["key"] + txt)
print("WROTE " + obj_path)
