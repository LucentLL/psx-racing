import bpy, os, json
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.environ["SRC"])
IN=0.0254; out=[]
for o in [o for o in bpy.context.scene.objects if o.type=='MESH']:
    if any(m and m.name=='Merged_materials' for m in o.data.materials): continue
    me=o.data; me.calc_loop_triangles(); M=o.matrix_world
    for t in me.loop_triangles:
        vs=[M@me.vertices[i].co for i in t.vertices]
        out.append([round(c*IN,2) for v in vs for c in (v.x,v.y,v.z)])
json.dump(out,open(os.environ["OUT"],"w")); print("TRIS",len(out))
