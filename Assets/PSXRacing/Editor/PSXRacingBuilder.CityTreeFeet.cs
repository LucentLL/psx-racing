using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CITY'S TREES STAND ON THE GROUND AT EVERY LEVEL (2026-11-16; the
    /// owner, on a Providence Rd frame: "Some even seem to be floating in air").
    ///
    /// A tree stands on the foot LoadVariant / LoadMergedVariant find in its
    /// LOD0 - the bark's 2nd-percentile height, past the root tips - and every
    /// level is drawn on that one foot. The packs' coarser levels have no
    /// roots: their trunks start higher, up to 1.03 m (Meadow 07_Poplar_skan
    /// LOD2); the pine stand-in 03_Poplar_skan_pine hangs 0.43 m over the
    /// ground at x1.5. On MEDIUM those levels draw from 102 m (LOD1) and 140 m
    /// (LOD2) to 240 m. Measured (CityRefSpots.RunTreeFeet's ground audit):
    /// along Providence Rd 1,467 of 7,463 planted trees float at LOD1 and 1,890
    /// at LOD2; West Trade St 202 and 306 of 3,922.
    ///
    /// Here each level's bark in its lowest 0.8 m is drawn down to the LOD0
    /// trunk's base; nothing above moves (the crown, and so the LOD hand-over,
    /// stays as it was). DEFAULT ON; PSX_CT5_FOOT=0 builds today's kit.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        static void CTFeet(List<MeshForest.Variant> list, List<string> names)
        {
            if (System.Environment.GetEnvironmentVariable("PSX_CT5_FOOT") == "0") { Log("City trees: feet as the packs give them (PSX_CT5_FOOT=0)"); return; }
            int fixedLods = 0, trees = 0; float worst = 0f; string worstName = "";
            var done = new Dictionary<(Mesh, int), Mesh>();
            float Base(MeshForest.Variant v, Mesh m)
            {
                float mn = float.MaxValue;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    if (v.leafSub != null && s < v.leafSub.Length && v.leafSub[s] && m.subMeshCount > 1) continue;
                    if (!TreeKit.Read(m, s, out var vv, out var tt)) continue;
                    foreach (int i in tt) mn = Mathf.Min(mn, v.pre.MultiplyPoint3x4(vv[i]).y);
                }
                return mn;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (v == null || v.lods == null || v.lods[0] == null) continue;
                float b0 = Base(v, v.lods[0]);
                if (b0 == float.MaxValue) continue;
                bool any = false;
                for (int l = 1; l < 3 && l < v.lods.Length; l++)
                {
                    var m = v.lods[l];
                    if (m == null || m == v.lods[0]) continue;
                    float bl = Base(v, m);
                    float lift = bl - b0;
                    if (bl == float.MaxValue || lift <= 0.05f) continue;
                    int key = Mathf.RoundToInt(b0 * 1000f);
                    if (!done.TryGetValue((m, key), out var fixedM))
                    {
                        fixedM = Object.Instantiate(ReadableCopy(m));
                        fixedM.name = "CTFeet_" + names[i] + "_L" + l;
                        var verts = fixedM.vertices;
                        var inv = v.pre.inverse;
                        var moved = new bool[verts.Length];
                        for (int s = 0; s < fixedM.subMeshCount; s++)
                        {
                            if (v.leafSub != null && s < v.leafSub.Length && v.leafSub[s] && fixedM.subMeshCount > 1) continue;
                            foreach (int vi in fixedM.GetTriangles(s))
                            {
                                if (moved[vi]) continue;
                                moved[vi] = true;
                                var q = v.pre.MultiplyPoint3x4(verts[vi]);
                                float t = (q.y - bl) / 0.8f;
                                if (t >= 1f) continue;
                                q.y -= lift * (1f - Mathf.Clamp01(t));
                                verts[vi] = inv.MultiplyPoint3x4(q);
                            }
                        }
                        fixedM.vertices = verts;
                        fixedM.RecalculateBounds();
                        fixedM = WorldKit.SaveMesh(fixedM, fixedM.name);
                        done[(m, key)] = fixedM;
                        fixedLods++;
                    }
                    v.lods[l] = fixedM;
                    any = true;
                    if (lift > worst) { worst = lift; worstName = names[i] + " LOD" + l; }
                }
                if (any) trees++;
            }
            Log($"City trees - FEET (PSX_CT5_FOOT, default on): {trees} variants, {fixedLods} levels drawn down to their LOD0 trunk base (worst lift {worst:0.00} m at scale 1: {worstName})");
        }
    }
}
