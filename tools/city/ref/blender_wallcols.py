import bpy, os, json, numpy as np
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.environ["SRC"])
IN=0.0254; cache={}
def img_of(mat):
    if not mat or not mat.use_nodes: return None
    for n in mat.node_tree.nodes:
        if n.type=='TEX_IMAGE' and n.image: return n.image
    return None
def arr(im):
    if im.name not in cache:
        w,h=im.size; a=np.array(im.pixels[:],dtype=np.float32).reshape(h,w,4); cache[im.name]=a
    return cache[im.name]
out=[]
for o in [o for o in bpy.context.scene.objects if o.type=='MESH']:
    me=o.data
    if any(m and m.name=='Merged_materials' for m in me.materials): continue
    me.calc_loop_triangles(); M=o.matrix_world; uv=me.uv_layers.active
    if not uv: continue
    for t in me.loop_triangles:
        n=(M.to_3x3()@t.normal).normalized()
        if abs(n.z)>0.3: continue
        mat=me.materials[t.material_index] if t.material_index<len(me.materials) else None
        im=img_of(mat)
        if im is None or im.size[0]==0: continue
        a=arr(im); h,w=a.shape[:2]
        uvs=[uv.data[l].uv for l in t.loops]
        # 25 samples over the triangle; the CLADDING is the bright half of
        # them (a punched window is dark glass: the mean of all of them made
        # a pink granite tower read muddy grey, 2026-10-12)
        cs=[]
        for i in range(5):
            for j in range(5 - i):
                wa=(i+0.33)/5.33; wb=(j+0.33)/5.33; wc=max(0.0,1-wa-wb)
                u=(wa*uvs[0].x+wb*uvs[1].x+wc*uvs[2].x)%1.0; v=(wa*uvs[0].y+wb*uvs[1].y+wc*uvs[2].y)%1.0
                cs.append(a[min(h-1,int(v*h)),min(w-1,int(u*w)),:3])
        cs=np.array(cs); lum=cs@np.array([0.299,0.587,0.114])
        c=cs[lum>=np.median(lum)].mean(axis=0)
        cm=cs.mean(axis=0)
        vs=[M@me.vertices[i].co for i in t.vertices]
        cx=sum(v.x for v in vs)/3*IN; cy=sum(v.y for v in vs)/3*IN
        area=t.area*IN*IN*(M.to_scale().x**2)
        out.append([round(cx,2),round(cy,2),round(area,2),round(float(c[0]),3),round(float(c[1]),3),round(float(c[2]),3),round(float(cm[0]),3),round(float(cm[1]),3),round(float(cm[2]),3)])
json.dump(out,open(os.environ["OUT"],"w")); print("FACADE TRIS",len(out))
