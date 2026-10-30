using System.Collections.Generic;
using System.IO;
using System.Text;
using PSXRacing.City;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD MESHES, HASHED (road-logic junctions, 2026-10-29): the proof
    /// that junction control changes nothing the road meshes draw. At fixed
    /// spots - the junction shots' two, uptown's signals and crosswalks, a
    /// warrant STOP on a short stub, a warrant all-way, a one-way pair's
    /// four-node signal square, an OSM STOP - the 3 x 3 tiles round each are
    /// built (CityWorld.EnsureRing, as play builds them) and every Roads,
    /// Kerbs, Barriers and Ground mesh is hashed (FNV-1a over vertices, each
    /// submesh's triangles, UVs, colours, the tile's place). Run with one
    /// code base, then the other, in one sandbox (the same data): equal
    /// hashes = equal meshes. The first spot is built twice (the build is
    /// deterministic, or the comparison means nothing).
    /// Only APIs the junctions branch's base (27082b65) also has, so the same
    /// file runs against both.
    ///   -executeMethod PSXRacing.EditorTools.CityRoadMeshHash.Run  -> PSXRacing_road_mesh_hash.txt
    /// </summary>
    public static class CityRoadMeshHash
    {
        static readonly (float x, float z, string label)[] Spots =
        {
            (-5832f, 1433f, "clanton_i77 (shots)"),
            (-393f, -3680f, "colony_carnegie (shots, warrant signal)"),
            (-2311f, 4728f, "uptown (signals, crosswalks)"),
            (-1350f, 5385f, "north_college_loop (warrant STOP, short stub)"),
            (-1360f, 1950f, "edgehill_queens (warrant STOP, 7 m pieces)"),
            (-1720f, 5602f, "poplar_12th (warrant all-way)"),
            (8100f, -6925f, "matthews_square (four-node signal cluster)"),
            (632f, 4661f, "thomas_chestnut (OSM STOP)"),
        };
        static readonly string[] Kinds = { "Roads", "Kerbs", "Barriers", "Ground" };

        const ulong Fnv0 = 1469598103934665603UL, FnvP = 1099511628211UL;

        public static void Run()
        {
            var sb = new StringBuilder();
            string file = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_road_mesh_hash.txt");
            try
            {
                var map = CityMap.Get();
                if (map == null) { sb.AppendLine("RUN FAILED: no city data"); return; }
                PSXRacingBuilder.EnsureCityTextures();
                var go = new GameObject("~roadMeshHash");
                var world = go.AddComponent<CityWorld>();
                try
                {
                    ulong all = Fnv0;
                    string first = null;
                    for (int i = 0; i <= Spots.Length; i++)
                    {
                        var s = Spots[i % Spots.Length];
                        world.DropAll();
                        world.EnsureRing(new Vector3(s.x, 0f, s.z), 1);
                        string line = HashSpot(go, out ulong h);
                        if (i < Spots.Length)
                        {
                            Mix(ref all, (long)h);
                            sb.AppendLine($"{s.label} at ({s.x:0},{s.z:0}): {line}");
                            if (i == 0) first = line;
                        }
                        else sb.AppendLine(line == first ? "DETERMINISTIC: the first spot built again hashes the same" : "NOT DETERMINISTIC: the first spot built again hashes differently - " + line);
                    }
                    sb.AppendLine($"ALL {all:x16}");
                }
                finally
                {
                    world.DropAll();
                    Object.DestroyImmediate(go);
                }
            }
            catch (System.Exception e)
            {
                sb.AppendLine("RUN FAILED: " + e);
                Debug.LogException(e);
            }
            finally
            {
                File.WriteAllText(file, sb.ToString());
                Debug.Log("[RoadMeshHash]\n" + sb);
            }
        }

        static void Mix(ref ulong h, long v)
        {
            for (int b = 0; b < 8; b++) { h ^= (ulong)((v >> (8 * b)) & 0xFF); h *= FnvP; }
        }

        static string HashSpot(GameObject root, out ulong spotHash)
        {
            var parts = new StringBuilder();
            spotHash = Fnv0;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var kind in Kinds)
            {
                var list = new List<MeshFilter>();
                foreach (var mf in filters) if (mf != null && mf.sharedMesh != null && mf.gameObject.name == kind) list.Add(mf);
                list.Sort((a, b) =>
                {
                    string pa = a.transform.parent != null ? a.transform.parent.name : "", pb = b.transform.parent != null ? b.transform.parent.name : "";
                    int c = string.CompareOrdinal(pa, pb);
                    return c != 0 ? c : a.sharedMesh.vertexCount.CompareTo(b.sharedMesh.vertexCount);
                });
                ulong h = Fnv0;
                long verts = 0, tris = 0;
                foreach (var mf in list)
                {
                    var m = mf.sharedMesh;
                    var p = mf.transform.position;
                    Mix(ref h, System.BitConverter.SingleToInt32Bits(p.x)); Mix(ref h, System.BitConverter.SingleToInt32Bits(p.y)); Mix(ref h, System.BitConverter.SingleToInt32Bits(p.z));
                    var v = m.vertices;
                    verts += v.Length;
                    foreach (var q in v)
                    {
                        Mix(ref h, ((long)System.BitConverter.SingleToInt32Bits(q.x) << 32) ^ (uint)System.BitConverter.SingleToInt32Bits(q.z));
                        Mix(ref h, System.BitConverter.SingleToInt32Bits(q.y));
                    }
                    Mix(ref h, m.subMeshCount);
                    for (int sm = 0; sm < m.subMeshCount; sm++)
                    {
                        var t = m.GetTriangles(sm);
                        tris += t.Length / 3;
                        Mix(ref h, t.Length);
                        foreach (int ix in t) Mix(ref h, ix);
                    }
                    foreach (var uv in m.uv) Mix(ref h, ((long)System.BitConverter.SingleToInt32Bits(uv.x) << 32) ^ (uint)System.BitConverter.SingleToInt32Bits(uv.y));
                    foreach (var c in m.colors32) Mix(ref h, (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a);
                }
                Mix(ref spotHash, (long)h);
                parts.Append($"{kind} {list.Count} meshes {verts} verts {tris} tris {h:x16}; ");
            }
            return parts.ToString().TrimEnd(' ', ';');
        }
    }
}
