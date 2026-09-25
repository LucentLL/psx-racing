# Check a convert_rip.py output the way the game will draw it: its own atlas,
# nearest filtering, BACK FACES CULLED (an inside-out mesh shows at once), from
# the front three-quarter and the rear three-quarter. The nose is +Y after
# import, so the "front" shot must show headlights.
#
#   blender -b --factory-startup -P render_rip.py -- <model.obj> <out_prefix>
import bpy, os, sys
from mathutils import Vector
obj, prefix = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=obj, forward_axis='NEGATIVE_Z', up_axis='Y')
# convert_rip writes no usemtl (export_models.mjs does), so hang the atlas on
# every part here.
import glob
atlas = sorted(glob.glob(os.path.join(os.path.dirname(obj), os.path.basename(obj)[:-4] + "_atlas_*.png")))[-1]
mat = bpy.data.materials.new("atlas"); mat.use_nodes = True
tex = mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(atlas)
bsdf = mat.node_tree.nodes["Principled BSDF"]
mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
for o in bpy.context.scene.objects:
    if o.type == 'MESH':
        o.data.materials.clear(); o.data.materials.append(mat)
for m in bpy.data.materials:
    m.use_backface_culling = True
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE':
                n.interpolation = 'Closest'
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'FLAT'
sc.display.shading.color_type = 'TEXTURE'
sc.display.shading.show_backface_culling = True
sc.render.resolution_x = 640; sc.render.resolution_y = 400
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.25, 0.27, 0.31)
lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
for o in sc.objects:
    if o.type == 'MESH':
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
c = (lo + hi) / 2
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 45
for name, d in {"front": Vector((1.1, 1.5, 0.55)), "rear": Vector((-1.1, -1.5, 0.55)),
                "left": Vector((-1, 0, 0.15)), "right": Vector((1, 0, 0.15))}.items():
    cam.location = c + d.normalized() * 8.5
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = prefix + "_" + name + ".png"
    bpy.ops.render.render(write_still=True)
