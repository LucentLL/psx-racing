import bpy, os
src = os.environ["CM_SRC"]; out = os.environ["CM_OUT"]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in bpy.context.scene.objects:
    print("OBJ_NAME %s type=%s" % (o.name, o.type))
bpy.ops.wm.obj_export(filepath=out, export_materials=True, export_selected_objects=False,
                      forward_axis='NEGATIVE_Z', up_axis='Y', export_triangulated_mesh=False)
print("WROTE " + out)
