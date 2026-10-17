"""Facade structure (2026-10-16): the RHYTHM of each wall of the reference
model of uptown (City Pack 7 - measured, never shipped; README.md here).
For every wall triangle it samples the texture on a grid laid out in the
WALL's own metres (along it and up it), and sums how much the luminance
changes going ALONG the wall (vertical lines: piers, fins, mullions) and
going UP it (horizontal lines: spandrels, ribbons, belts), at two scales.
ref_looks.py turns the ratio per tower into vertical / horizontal / grid.
Nothing of the texture is written: two sums and a count per triangle.

    set SRC=...\\Low Poly City\\city_pack_7.glb
    set OUT=%TEMP%\\cp7_rhythm.json & blender --background --python blender_wallrhythm.py
"""
import bpy, os, json
import numpy as np

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.environ["SRC"])
IN = 0.0254
STEP = 0.8            # metres between samples on the wall
SCALES = (0.35, 0.9)  # metres: the luminance step is taken this far along / up
cache = {}


def img_of(mat):
    if not mat or not mat.use_nodes:
        return None
    for n in mat.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image:
            return n.image
    return None


def lum_of(im):
    if im.name not in cache:
        w, h = im.size
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        cache[im.name] = a[..., :3] @ np.array([0.299, 0.587, 0.114], np.float32)
    return cache[im.name]


def sample(L, uv):
    h, w = L.shape
    u = np.mod(uv[:, 0], 1.0); v = np.mod(uv[:, 1], 1.0)
    return L[np.minimum(h - 1, (v * h).astype(np.int64)), np.minimum(w - 1, (u * w).astype(np.int64))]


out = []
for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
    me = o.data
    if any(m and m.name == 'Merged_materials' for m in me.materials):
        continue
    me.calc_loop_triangles(); M = o.matrix_world; uvl = me.uv_layers.active
    if not uvl:
        continue
    for t in me.loop_triangles:
        n = (M.to_3x3() @ t.normal).normalized()
        if abs(n.z) > 0.3:
            continue
        mat = me.materials[t.material_index] if t.material_index < len(me.materials) else None
        im = img_of(mat)
        if im is None or im.size[0] == 0:
            continue
        L = lum_of(im)
        P = [M @ me.vertices[i].co for i in t.vertices]
        hx, hy = -n.y, n.x
        hl = (hx * hx + hy * hy) ** 0.5
        if hl < 1e-6:
            continue
        hx, hy = hx / hl, hy / hl
        S = np.array([(p.x * hx + p.y * hy) * IN for p in P])
        Z = np.array([p.z * IN for p in P])
        UV = np.array([[uvl.data[l].uv.x, uvl.data[l].uv.y] for l in t.loops])
        A = np.stack([S, Z, np.ones(3)], 1)
        if abs(np.linalg.det(A)) < 1e-6:
            continue
        coef = np.linalg.solve(A, UV)          # [s, z, 1] @ coef = uv
        s0, s1, z0, z1 = S.min(), S.max(), Z.min(), Z.max()
        ss = np.arange(s0 + STEP * 0.5, s1, STEP); zz = np.arange(z0 + STEP * 0.5, z1, STEP)
        if len(ss) == 0 or len(zz) == 0:
            continue
        gs, gz = np.meshgrid(ss, zz); gs = gs.ravel(); gz = gz.ravel()
        # inside the triangle (barycentric in the wall plane)
        bc = np.linalg.solve(A.T, np.stack([gs, gz, np.ones_like(gs)]))
        keep = (bc >= -1e-4).all(0)
        if not keep.any():
            continue
        gs, gz = gs[keep], gz[keep]
        def uv_at(s, z):
            return np.stack([s, z, np.ones_like(s)], 1) @ coef
        l0 = sample(L, uv_at(gs, gz))
        ev = eh = 0.0
        for d in SCALES:
            ev += float(np.abs(sample(L, uv_at(gs + d, gz)) - l0).sum())
            eh += float(np.abs(sample(L, uv_at(gs, gz + d)) - l0).sum())
        vs = [M @ me.vertices[i].co for i in t.vertices]
        cx = sum(v.x for v in vs) / 3 * IN; cy = sum(v.y for v in vs) / 3 * IN
        out.append([round(cx, 2), round(cy, 2), int(len(gs)), round(ev, 3), round(eh, 3), round(float(l0.mean()), 3)])
json.dump(out, open(os.environ["OUT"], "w"))
print("RHYTHM TRIS", len(out))
