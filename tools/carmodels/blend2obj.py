# An owner .blend (or .fbx) to one OBJ, meshes only, in the frame glb2obj.py
# writes (forward -Z, up Y), so export_models.mjs takes it like any other car.
# Written for the R32 lens test (2026-10-16, Cars/R32_LensTest_v04/
# R32_Separate_Lenses.blend -> converted/gtr_89_lens/gtr_89_lens.obj): its
# lamps, cameras and the cover objects' own names ride through untouched.
#   CM_SRC=<file.blend|.fbx> CM_OUT=<file.obj> blender --background --python blend2obj.py
import bpy, os
src = os.environ["CM_SRC"]; out = os.environ["CM_OUT"]
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.import_scene.fbx(filepath=src)
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o, do_unlink=True)
for o in bpy.context.scene.objects:
    print("OBJ_NAME %s tris=%d" % (o.name, sum(len(p.vertices) - 2 for p in o.data.polygons)))
bpy.ops.wm.obj_export(filepath=out, export_materials=True, export_selected_objects=False,
                      forward_axis='NEGATIVE_Z', up_axis='Y', export_triangulated_mesh=False)
print("WROTE " + out)
