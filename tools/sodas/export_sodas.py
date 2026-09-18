# Cut the four 2 litre bottles the owner circled out of the "All" props pack and
# write them where the pizza cargo baker can find them.
#
#   blender --background --python tools/sodas/export_sodas.py
#   py tools/sodas/build_atlas.py            (the texture half; Blender has no PIL)
#
# WHY THIS EXISTS. The cargo's bottles were first a small glass cola bottle out
# of the pizzeria pack, and then - when the owner circled something else - a
# twelve-sided lathe with a painted label, because every drink in every pack IN
# THE UNITY PROJECT had been measured and none of them was a PET two-litre. The
# ones he circled were never in the project. They are in his art folder, in a
# household pack called "All" (All/Models/All.fbx, 672 objects), as Soda ..
# Soda_15: four rows of four on a grocery shelf. The second row from the front
# - Soda_08 to Soda_11, lemon-lime twice, cola, citrus - is the row he named,
# and they are the four uniform 0.199 x 0.590 bottles of the sixteen.
#
# WHAT IT WRITES
#   Assets/PSXRacing/Art/LifeSim/Groceries/Sodas2L.fbx   four meshes, soda_2l_0..3
#   tools/sodas/soda_uv.json                             each one's window in Foods_01.jpg
#
# Each bottle's UVs are remapped out of the pack's 1024 px food atlas into its
# own 64 px column of a 256 x 256 sheet (the renderer's texture ceiling), with a
# two pixel gutter. build_atlas.py reads the json and cuts that sheet.
#
# Closed meshes, all four: no boundary edges, eight faces on the base. The lathe
# bottle's base was wound the wrong way round and culled - "hollow and
# transparent on the bottom" - and these have a bottom because a modeller gave
# them one.
import bpy, json, os
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(os.path.dirname(HERE))
SRC = os.environ.get("SODA_PACK",
    r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\All\All\Models\All.fbx")
OUT_DIR = os.path.join(PROJ, "Assets", "PSXRacing", "Art", "LifeSim", "Groceries")
# IN THE ORDER AN ORDER USES THEM. Most orders carry one bottle or two, and the
# runtime hands out look 0 then look 1 - so the shelf's own left-to-right put
# two GREEN bottles on every two-bottle order, which reads as one thing twice.
# Cola first, lemon-lime second: the pair a pizza actually comes with.
NAMES = ["Soda_10", "Soda_09", "Soda_11", "Soda_08"]
SHEET, COL, GUTTER = 256, 64, 2

os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)

keep = [bpy.data.objects[n] for n in NAMES]
for o in list(bpy.data.objects):
    if o not in keep:
        bpy.data.objects.remove(o, do_unlink=True)

mat = bpy.data.materials.new("Sodas2L")
windows = {}
for i, ob in enumerate(keep):
    me = ob.data
    # Bake the pack's placement into the vertices, then stand the bottle on the
    # origin: base at z = 0, centred in plan. The baker measures and corrects
    # this again - "measure, don't assume" - but a model that arrives already
    # seated is one fewer way for a bottle to hang off the seat.
    #
    # AND TURNED RIGHT SIDE OUT. The pack places every one of these with a
    # NEGATIVE scale on all three axes (-0.097, -0.097, -0.087): a mirror. Baking
    # a mirror into the vertices reverses which way round every triangle runs,
    # and mesh.transform() - unlike the UI's Apply Scale - does not flip them
    # back. The first export skipped this and shipped four bottles INSIDE OUT:
    # the renderer culled the wall nearest the camera and drew the inside of the
    # far one, reported as "these bottles look hollow like they're missing a
    # side". It survived my own check render because that render had culling on
    # and the texture is a planar projection, the same picture on the inside of
    # the back as on the outside of the front. So it is MEASURED below, not
    # looked at: a closed mesh has a signed volume, and its sign is the answer.
    mirrored = ob.matrix_world.determinant() < 0
    me.transform(ob.matrix_world)
    if mirrored:
        me.flip_normals()
    ob.matrix_world = Matrix.Identity(4)
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    shift = Vector((-(min(xs) + max(xs)) / 2, -(min(ys) + max(ys)) / 2, -min(zs)))
    me.transform(Matrix.Translation(shift))

    uv = me.uv_layers.active.data
    u0 = min(l.uv.x for l in uv); u1 = max(l.uv.x for l in uv)
    v0 = min(l.uv.y for l in uv); v1 = max(l.uv.y for l in uv)
    windows["soda_2l_%d" % i] = dict(source=ob.name, u0=u0, v0=v0, u1=u1, v1=v1)
    lo_u = (i * COL + GUTTER) / SHEET; hi_u = ((i + 1) * COL - GUTTER) / SHEET
    lo_v = GUTTER / SHEET; hi_v = (SHEET - GUTTER) / SHEET
    for l in uv:
        l.uv.x = lo_u + (l.uv.x - u0) / max(1e-6, u1 - u0) * (hi_u - lo_u)
        l.uv.y = lo_v + (l.uv.y - v0) / max(1e-6, v1 - v0) * (hi_v - lo_v)

    me.materials.clear()
    me.materials.append(mat)
    ob.name = "soda_2l_%d" % i
    me.name = ob.name
    ob.location = Vector((i * 0.3, 0.0, 0.0))

def signed_volume(me):
    """Positive when a closed mesh's faces point OUT (Blender: counter-clockwise
    fronts, right-handed). Negative is inside out."""
    me.calc_loop_triangles()
    total = 0.0
    for t in me.loop_triangles:
        a, b, c = (me.vertices[i].co for i in t.vertices)
        total += a.dot(b.cross(c)) / 6.0
    return total

for ob in keep:
    vol = signed_volume(ob.data)
    windows[ob.name]["litres_at_pack_scale"] = round(vol * 1000.0, 3)
    if vol <= 0.0:
        raise SystemExit("SODAS FAILED: %s is INSIDE OUT (signed volume %.5f) - refusing to export" % (ob.name, vol))

with open(os.path.join(HERE, "soda_uv.json"), "w") as f:
    json.dump(dict(sheet=SHEET, col=COL, gutter=GUTTER, atlas="Foods_01.jpg", bottles=windows), f, indent=2)

for o in bpy.data.objects:
    o.select_set(o in keep)
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT_DIR, "Sodas2L.fbx"),
    use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    mesh_smooth_type='FACE', path_mode='STRIP', add_leaf_bones=False, bake_anim=False)
print("SODAS wrote", os.path.join(OUT_DIR, "Sodas2L.fbx"), json.dumps(windows))
