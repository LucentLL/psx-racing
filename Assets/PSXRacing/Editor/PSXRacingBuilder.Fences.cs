using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE PARKWAY'S POST-AND-RAIL FENCES (owner 2026-11-08: "use the wood
    /// assets to make the iconic Blue Ridge Parkway wood fences"), from the
    /// Meadow pack's rustic fence spans (double post, two rails, ~3.2 m and
    /// ~1.85 m). PC scene pass with the store files only.
    ///
    /// Along OPEN roadside (no wall, no cut, no bridge), at the outer edge of
    /// the graded clear zone - where the mowed verge meets the field or the
    /// trees in the owner's frames - in runs of 30 m or more, about half of
    /// the eligible runs (the real fences come and go with the pastures).
    /// Spans follow the curve post to post, seated on the ground. No collider
    /// yet: they stand past the clear zone a car can recover in.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string MeadowFenceDir = "Assets/StoreAssets/Meadow/Fence";
        static readonly string[] FenceNames = { "wood_fence_type_03_A", "wood_fence_type_03_B", "wood_fence_type_03_C" };
        const float FenceRunMinM = 30f, FenceShare = 0.5f, FenceOutM = 0.8f;

        struct FenceInfo { public int v; public float length, axisYaw; }

        static List<FenceInfo> LoadFences(List<MeshForest.Variant> list)
        {
            var infos = new List<FenceInfo>();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MeadowFenceDir + "/" + FenceNames[0] + ".FBX") == null) return infos;
            var mat = NMLitMat("NM_meadow_fence", MeadowFenceDir, "T_Wood_fence_BC", "T_Wood_fence_N", null, false, Color.white);
            foreach (var name in FenceNames)
            {
                var v = LoadVariant(MeadowFenceDir, name, mat);
                if (v == null) continue;
                v.maxDist = 260f;
                v.thin = 0.85f;
                v.shadows = true;
                var b = v.lods[0].bounds;
                Vector3 ex = v.pre.MultiplyVector(new Vector3(b.size.x, 0f, 0f));
                Vector3 ez = v.pre.MultiplyVector(new Vector3(0f, 0f, b.size.z));
                Vector3 ey = v.pre.MultiplyVector(new Vector3(0f, b.size.y, 0f));
                // the longest horizontal extent of the three local axes is the span
                Vector3 best = ex;
                foreach (var e in new[] { ez, ey }) { var f = e; f.y = 0f; var g = best; g.y = 0f; if (f.sqrMagnitude > g.sqrMagnitude) best = e; }
                best.y = 0f;
                infos.Add(new FenceInfo { v = list.Count, length = best.magnitude, axisYaw = Mathf.Atan2(best.x, best.z) * Mathf.Rad2Deg });
                list.Add(v);
            }
            Log($"Fences: {infos.Count} Meadow post-and-rail spans ({(infos.Count > 0 ? infos[0].length : 0f):0.00} m).");
            return infos;
        }

        /// <summary>A span mid-run, for ForestShots' fence view (a marker object).</summary>
        static Vector3? fenceShotAt;

        struct FencePlace { public Vector3 pos; public float yaw, scale; public int v; }
        static readonly List<FencePlace> fencePlaces = new List<FencePlace>();

        /// <summary>The stage's fence runs (once per stage, before the forest's chunks).</summary>
        static int PlanFences(MeshTreeKit kit, List<Vector3> pts)
        {
            fencePlaces.Clear();
            if (kit == null || kit.fences == null || kit.fences.Count == 0 || rsKind == null || rsRight == null) return 0;
            int n = pts.Count, runs = 0;
            float half = RoadWidth * 0.5f;
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                var run = new List<Vector3>();
                for (int i = 0; i <= n; i++)
                {
                    bool ok = i < n && rsKind[s][i] == Roadside.Open && BridgeAt(i) < 0.15f;
                    Vector3 p = Vector3.zero;
                    if (ok)
                    {
                        float e = rsCatchE[s][i] + FenceOutM;
                        p = pts[i] + rsRight[i] * (side * (half + e));
                        p.y = StageLatticeY(p.x, p.z);
                        // not on a steep fall (the fence would hang in the air)
                        float g = Mathf.Abs(StageLatticeY(p.x + rsRight[i].x * side * 2f, p.z + rsRight[i].z * side * 2f) - p.y);
                        ok = g < 0.9f;
                    }
                    if (ok) { run.Add(p); continue; }
                    if (run.Count > 1)
                    {
                        float len = 0f;
                        for (int q = 1; q < run.Count; q++) len += Vector3.Distance(run[q - 1], run[q]);
                        if (len >= FenceRunMinM && RHash(runs, s, 331) < FenceShare) LayFence(kit, run, runs * 2 + s);
                        runs++;
                    }
                    run.Clear();
                }
            }
            Log($"Fences: {fencePlaces.Count} spans in the stage's open roadside runs.");
            if (fencePlaces.Count > 0) fenceShotAt = fencePlaces[fencePlaces.Count / 2].pos;
            return fencePlaces.Count;
        }

        static void LayFence(MeshTreeKit kit, List<Vector3> line, int seed)
        {
            // cumulative distance along the line
            var cum = new float[line.Count];
            for (int q = 1; q < line.Count; q++) cum[q] = cum[q - 1] + Vector3.Distance(line[q - 1], line[q]);
            float total = cum[line.Count - 1];
            Vector3 At(float d)
            {
                int q = 1;
                while (q < line.Count - 1 && cum[q] < d) q++;
                float t = Mathf.InverseLerp(cum[q - 1], cum[q], d);
                return Vector3.Lerp(line[q - 1], line[q], t);
            }
            float pos = 0f;
            int k = 0;
            while (true)
            {
                var fi = kit.fences[(int)(RHash(k, seed, 337) * 997f) % kit.fences.Count];
                if (pos + fi.length > total) break;
                Vector3 p0 = At(pos), p1 = At(pos + fi.length);
                Vector3 dir = p1 - p0; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    Vector3 mid = (p0 + p1) * 0.5f;
                    mid.y = Mathf.Min(p0.y, p1.y) - 0.05f;
                    fencePlaces.Add(new FencePlace
                    {
                        pos = mid, v = fi.v,
                        yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - fi.axisYaw,
                        scale = Mathf.Clamp(dir.magnitude / fi.length, 0.9f, 1.1f),
                    });
                }
                pos += fi.length;
                k++;
            }
        }

        static void AddFences(float ox, float oz, float size, List<Vector4> inst, List<Vector2> scaleVar,
                              ref int added, ref float ySum)
        {
            foreach (var f in fencePlaces)
            {
                if (f.pos.x < ox || f.pos.x >= ox + size || f.pos.z < oz || f.pos.z >= oz + size) continue;
                inst.Add(new Vector4(f.pos.x, f.pos.y, f.pos.z, f.yaw));
                scaleVar.Add(new Vector2(f.scale, f.v));
                ySum += f.pos.y;
                added++;
            }
        }
    }
}
