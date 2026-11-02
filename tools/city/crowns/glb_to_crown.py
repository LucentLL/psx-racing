"""glb_to_crown.py - the owner's landmark crown models (Codex, Environment_Overhaul2_Uptown/
Landmark_Crowns) into the game's crown files.

    py tools/city/crowns/glb_to_crown.py <handoff dir>            write Resources/CityCrowns/*.bytes
    py tools/city/crowns/glb_to_crown.py <handoff dir> --check    verify only (base ring, bounds)

Each GLB (glTF 2.0, right-handed, +Y up, metres; local +X map east, -Z map north; origin
the footprint's area centroid at the mount height) becomes one CRWN file the city reads
(CityMeshes.CrownModels): triangles in the game's frame, x = gx, y = gy, z = -gz (map
north is +Z in the game), UV v flipped to Unity's bottom-left origin. The z mirror flips
handedness, and Bucket.Tri(a, b, c) draws (a, c, b), so a glTF triangle is written in its
own index order and comes out front-facing.

CRWN v1, little-endian: 'CRWN', int32 version, int32 vertex count V, int32 triangle
count T, V x (float32 x, y, z, u, v), T x 3 int32 indices.

--check: the base ring (vertices within 0.05 m of y = 0) must lie on placement_overhaul1.json's
mount ring (every ring corner within 0.75 m of a base vertex) - the test that catches a
mirrored or turned crown (C3's notched side).
"""
import json, os, struct, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(REPO, "Assets", "PSXRacing", "Resources", "CityCrowns")
FILES = {"C1_SpiredCrown_overhaul1.glb": "C1_SpiredCrown", "C3_SilverCrown_overhaul1.glb": "C3_SilverCrown"}
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_glb(path):
    b = open(path, "rb").read()
    jl = struct.unpack_from("<I", b, 12)[0]
    j = json.loads(b[20:20 + jl])
    bin_off = 20 + jl + 8
    def acc(i):
        a = j["accessors"][i]; bv = j["bufferViews"][a["bufferView"]]
        off = bin_off + bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        n, dt = NC[a["type"]], CT[a["componentType"]]
        stride = bv.get("byteStride", 0) or n * np.dtype(dt).itemsize
        raw = np.frombuffer(b, dtype=np.uint8, count=stride * (a["count"] - 1) + n * np.dtype(dt).itemsize, offset=off)
        out = np.lib.stride_tricks.as_strided(raw.view(dt) if stride == n * np.dtype(dt).itemsize else np.frombuffer(b, dt, a["count"] * n, off),
                                              shape=(a["count"], n)) if stride == n * np.dtype(dt).itemsize else \
              np.array([np.frombuffer(b, dt, n, off + k * stride) for k in range(a["count"])])
        return np.array(out, dtype=np.float64 if dt == np.float32 else np.int64).reshape(a["count"], n)
    return j, acc


def node_matrix(nd):
    if "matrix" in nd: return np.array(nd["matrix"], dtype=np.float64).reshape(4, 4).T
    t = np.array(nd.get("translation", [0, 0, 0])); s = np.array(nd.get("scale", [1, 1, 1]))
    x, y, z, w = nd.get("rotation", [0, 0, 0, 1])
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4); m[:3, :3] = r * s; m[:3, 3] = t
    return m


def convert(path):
    j, acc = read_glb(path)
    P, U, I = [], [], []
    def walk(ni, parent):
        nd = j["nodes"][ni]; m = parent @ node_matrix(nd)
        if "mesh" in nd:
            for pr in j["meshes"][nd["mesh"]]["primitives"]:
                if pr.get("mode", 4) != 4: raise SystemExit("non-triangle primitive in " + path)
                pos = acc(pr["attributes"]["POSITION"]); uv = acc(pr["attributes"]["TEXCOORD_0"])
                idx = acc(pr["indices"]).ravel() if "indices" in pr else np.arange(len(pos))
                w = (np.c_[pos, np.ones(len(pos))] @ m.T)[:, :3]
                if np.linalg.det(m[:3, :3]) < 0: idx = idx.reshape(-1, 3)[:, [0, 2, 1]].ravel()
                base = sum(len(p) for p in P)
                P.append(w); U.append(uv); I.append(idx + base)
        for c in nd.get("children", []): walk(c, m)
    for ni in j["scenes"][j.get("scene", 0)]["nodes"]: walk(ni, np.eye(4))
    P = np.vstack(P); U = np.vstack(U); I = np.concatenate(I).reshape(-1, 3)
    game = np.c_[P[:, 0], P[:, 1], -P[:, 2]]
    uv = np.c_[U[:, 0], 1.0 - U[:, 1]]
    return game, uv, I


def main():
    src = sys.argv[1]; check = "--check" in sys.argv
    place = json.load(open(os.path.join(src, "placement_overhaul1.json"), encoding="utf-8"))
    rings = {c["file"]: np.array(c["mountRing_relativeUnityXZ_m"]) for c in place["crowns"]}
    ok = True
    for f, key in FILES.items():
        game, uv, tri = convert(os.path.join(src, f))
        base = game[np.abs(game[:, 1]) < 0.05][:, [0, 2]]
        d = [np.min(np.hypot(*(base - q).T)) for q in rings[f]]
        lo, hi = game.min(0), game.max(0)
        print("%-16s %5d verts %5d tris  x %.2f..%.2f  y %.2f..%.2f  z %.2f..%.2f  uv %.3f..%.3f  ring worst %.2f m" %
              (key, len(game), len(tri), lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], uv.min(), uv.max(), max(d)))
        if max(d) > 0.75: ok = False; print("  FAIL: the base ring is off the mount ring (mirrored or turned?)")
        if not check:
            os.makedirs(OUT, exist_ok=True)
            with open(os.path.join(OUT, key + ".bytes"), "wb") as o:
                o.write(b"CRWN" + struct.pack("<iii", 1, len(game), len(tri)))
                o.write(np.c_[game, uv].astype("<f4").tobytes())
                o.write(tri.astype("<i4").tobytes())
            print("  wrote", os.path.join(OUT, key + ".bytes"))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
