import bpy, sys, os, math, glob
argv = [os.environ["CM_SRC"], os.environ["CM_TEX"], os.environ["CM_OUT"]]
src, tex, out = argv[0], argv[1], argv[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith('.glb'):
    bpy.ops.import_scene.gltf(filepath=src)
else:
    bpy.ops.wm.obj_import(filepath=src)

objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not objs:
    print("NO MESH"); sys.exit(1)

# unified material with the chosen texture
mat = bpy.data.materials.new("preview")
mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 0.6
if tex and os.path.exists(tex):
    img = mat.node_tree.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(tex)
    img.interpolation = 'Closest'
    mat.node_tree.links.new(bsdf.inputs["Base Color"], img.outputs["Color"])
for o in objs:
    o.data.materials.clear(); o.data.materials.append(mat)

# bounds
import mathutils
mn = mathutils.Vector((1e9,1e9,1e9)); mx = mathutils.Vector((-1e9,-1e9,-1e9))
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(c)
        for i in range(3):
            mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
ctr = (mn+mx)/2; size = max((mx-mn)[i] for i in range(3))
print("BOUNDS min=%s max=%s" % (["%.3f"%v for v in mn], ["%.3f"%v for v in mx]))

cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
d = size * 2.1
cam.location = (ctr.x + d*0.62, ctr.y - d*0.72, ctr.z + d*0.42)
dirv = ctr - mathutils.Vector(cam.location)
cam.rotation_euler = dirv.to_track_quat('-Z','Y').to_euler()
cam_data.lens = 55
bpy.context.scene.camera = cam

sun_d = bpy.data.lights.new("sun", 'SUN'); sun_d.energy = 4.0
sun = bpy.data.objects.new("sun", sun_d); bpy.context.scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(35))
bpy.context.scene.world = bpy.data.worlds.new("w")
bpy.context.scene.world.use_nodes = True
bpy.context.scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.25,0.27,0.32,1)

sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 640; sc.render.resolution_y = 420
sc.render.film_transparent = False
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("WROTE " + out)
