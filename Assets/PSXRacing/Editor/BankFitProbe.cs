using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CUT-FACE FIT PROBE (stage3, 2026-11-25; the self-test's "held off by 0.6 m of air" at every stage's rock
    /// cuts). Does a car that drives into a cut stop on the rock it SEES?
    ///
    /// On a built stage scene (edit mode, nothing saved): every 0.5 m along the road, each side, a level ray from
    /// the road toward the shoulder at every 0.1 m from 0.2 m to 1.6 m over that side's tarmac edge. Where it meets a
    /// cut's solid (BankColl) facing the road, the same ray is asked for the first DRAWN surface (every rendered
    /// mesh near the cuts, foliage and cars aside, as a temporary MeshCollider on a spare layer: drawn means seen
    /// from the road, so back faces do not count). GAP = drawn - solid along the ray: over 0.15 m the car stops in
    /// front of the rock it sees ("held off"); under -0.01 m the drawn rock stands in front of the solid (a car
    /// sinks into it). Writes PSXRacing_bankfit.txt beside the project: per stage, the spots (0.5 m positions)
    /// measured, over 0.15, under -0.01, the worst each way, by height band, the 12 worst with waypoint, and the cut
    /// solids' count and triangles with their cook time.
    ///
    /// PSX_BF_VENUES (default LittleSwitzerland,ChimneyRock,LakeLure).
    /// </summary>
    public static class BankFitProbe
    {
        const int ProbeLayer = 31;
        public const float Over = 0.15f, Under = -0.01f;

        public static void Run()
        {
            var sb = new StringBuilder();
            string ids = System.Environment.GetEnvironmentVariable("PSX_BF_VENUES");
            if (string.IsNullOrEmpty(ids)) ids = "LittleSwitzerland,ChimneyRock,LakeLure";
            try { foreach (var id in ids.Split(',')) if (id.Trim().Length > 0) sb.AppendLine(One(id.Trim())); }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bankfit.txt"), sb.ToString());
            Debug.Log("[BF] " + sb);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        struct Spot { public int wp; public float gap, h; public string coll; public Vector3 at; }

        public static string One(string id)
        {
            var sb = new StringBuilder();
            string scenePath = "Assets/PSXRacing/Scenes/" + id + ".unity";
            if (!File.Exists(scenePath)) return "bankfit " + id + ": NO SCENE";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count == 0) return "bankfit " + id + ": no TrackPath";

            // the cut solids
            var solids = new List<MeshCollider>();
            int solidTris = 0, cutN = 0, cutTris = 0;
            Bounds all = new Bounds(); bool any = false;
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                if (mc == null || mc.sharedMesh == null || !mc.enabled || !mc.gameObject.activeInHierarchy) continue;
                if (!mc.name.StartsWith("Bank") || mc.isTrigger) continue;   // BankColl and the rock top's face collider (BankTop)
                solids.Add(mc);
                int nt = mc.sharedMesh.triangles.Length / 3; solidTris += nt;
                if (mc.name.StartsWith("BankColl")) { cutN++; cutTris += nt; }
                if (!any) { all = mc.bounds; any = true; } else all.Encapsulate(mc.bounds);
            }
            if (solids.Count == 0) return "bankfit " + id + ": no BankColl";
            // the cook time of those meshes (what a load pays for them)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var mc in solids) Physics.BakeMesh(mc.sharedMesh.GetEntityId(), false);
            double cookMs = sw.Elapsed.TotalMilliseconds;

            // the drawn world near the cuts, as temporary colliders
            var temp = new List<GameObject>();
            var tempOwner = new Dictionary<Collider, string>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
                int layer = mf.gameObject.layer;
                if (layer == 2 || layer == 10) continue;
                var ren = mf.GetComponent<MeshRenderer>();
                if (ren == null || !ren.enabled) continue;
                if (mf.GetComponentInParent<CarController>() != null) continue;
                if (!ren.bounds.Intersects(new Bounds(all.center, all.size + Vector3.one * 6f))) continue;
                bool near = false;
                foreach (var s in solids) if (ren.bounds.Intersects(new Bounds(s.bounds.center, s.bounds.size + Vector3.one * 6f))) { near = true; break; }
                if (!near) continue;
                var go = new GameObject("BF_" + mf.name) { layer = ProbeLayer, hideFlags = HideFlags.DontSave };
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                var c = go.AddComponent<MeshCollider>();
                c.sharedMesh = mf.sharedMesh;
                temp.Add(go);
                tempOwner[c] = mf.name + " [" + mf.sharedMesh.name + "]";
            }
            Physics.SyncTransforms();
            bool hitBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = false;

            float half = path.roadWidth * 0.5f;
            int n = path.Count, chords = path.HasEnds ? n - 1 : n;
            int spots = 0, spotsOver = 0, spotsUnder = 0, samples = 0, sOver = 0, sUnder = 0;
            float worst = 0f, worstNeg = 0f;
            var bandOver = new int[3]; var bandN = new int[3];
            var worstList = new List<Spot>();
            var negList = new List<Spot>();
            var byColl = new Dictionary<string, int>();
            int mask = 1 << ProbeLayer;
            try
            {
                for (int i = 0; i < chords; i++)
                {
                    Vector3 a0 = path.GetPoint(i), a1 = path.GetPoint(i + 1);
                    Vector3 tan = a1 - a0; tan.y = 0f;
                    float len = tan.magnitude;
                    if (len < 1e-3f) continue;
                    tan /= len;
                    Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
                    int steps = Mathf.Max(1, Mathf.RoundToInt(len / 0.5f));
                    for (int q = 0; q < steps; q++)
                    {
                        float t = q / (float)steps;
                        Vector3 p = Vector3.Lerp(a0, a1, t);
                        for (int sd = -1; sd <= 1; sd += 2)
                        {
                            Vector3 dir = right * sd;
                            float edgeY = p.y;
                            if (path.HasBank)
                            {
                                float b0 = sd > 0 ? path.bankR[path.Wrap(i)] : path.bankL[path.Wrap(i)];
                                float b1 = sd > 0 ? path.bankR[path.Wrap(i + 1)] : path.bankL[path.Wrap(i + 1)];
                                edgeY += Mathf.Lerp(b0, b1, t) * half;
                            }
                            Vector3 o0 = p + dir * (half - 1.0f);
                            bool spot = false, spotOver = false, spotUnder = false;
                            float spotWorst = 0f, spotNeg = 0f, hW = 0f, hN = 0f; string cW = null;
                            for (int hi = 0; hi <= 14; hi++)
                            {
                                float h = 0.2f + hi * 0.1f;
                                var ray = new Ray(new Vector3(o0.x, edgeY + h, o0.z), dir);
                                float dc = float.MaxValue; MeshCollider hitS = null;
                                foreach (var s in solids)
                                {
                                    if (!s.bounds.IntersectRay(ray, out float bd) || bd > 12f) continue;
                                    if (s.Raycast(ray, out RaycastHit rh, 12f) && rh.distance < dc && Vector3.Dot(rh.normal, dir) < -0.2f)
                                    { dc = rh.distance; hitS = s; }
                                }
                                if (hitS == null) continue;
                                float dd = Physics.Raycast(ray, out RaycastHit dh, dc + 3f, mask) ? dh.distance : dc + 3f;
                                float gap = dd - dc;
                                spot = true; samples++;
                                int band = h < 0.65f ? 0 : h < 1.15f ? 1 : 2;
                                bandN[band]++;
                                if (gap > Over) { sOver++; bandOver[band]++; spotOver = true; if (gap > spotWorst) { spotWorst = gap; hW = h; cW = hitS.sharedMesh.name; } }
                                if (gap < Under) { sUnder++; spotUnder = true; if (gap < spotNeg) { spotNeg = gap; hN = h; } }
                            }
                            if (!spot) continue;
                            spots++;
                            if (spotOver)
                            {
                                spotsOver++;
                                byColl.TryGetValue(cW, out int cc); byColl[cW] = cc + 1;
                                worst = Mathf.Max(worst, spotWorst);
                                worstList.Add(new Spot { wp = i, gap = spotWorst, h = hW, coll = cW, at = p });
                            }
                            if (spotUnder)
                            {
                                spotsUnder++;
                                worstNeg = Mathf.Min(worstNeg, spotNeg);
                                negList.Add(new Spot { wp = i, gap = spotNeg, h = hN, at = p });
                            }
                        }
                    }
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = hitBack;
                foreach (var g in temp) Object.DestroyImmediate(g);
            }
            worstList.Sort((x, y) => y.gap.CompareTo(x.gap));
            negList.Sort((x, y) => x.gap.CompareTo(y.gap));
            sb.AppendLine($"bankfit {id}: {spots} spots (0.5 m, a side) where a level ray 0.2-1.6 m over the edge meets a cut solid; " +
                          $"held off > {Over:0.00} m: {spotsOver} ({100f * spotsOver / Mathf.Max(1, spots):0.0}%), worst {worst:0.00} m; " +
                          $"drawn in front of the solid (< {Under:0.00} m): {spotsUnder}, worst {worstNeg:0.00} m");
            sb.AppendLine($"  samples {samples}: over {sOver}, under {sUnder}; over by band 0.2-0.6 {bandOver[0]}/{bandN[0]}, 0.7-1.1 {bandOver[1]}/{bandN[1]}, 1.2-1.6 {bandOver[2]}/{bandN[2]}");
            sb.AppendLine($"  cut solids (BankColl): {cutN} MeshColliders, {cutTris} triangles; with the rock tops' colliders {solids.Count} / {solidTris}; cooked in {cookMs:0} ms (editor); drawn meshes asked: {temp.Count}");
            var cl = new List<KeyValuePair<string, int>>(byColl);
            cl.Sort((x, y) => y.Value.CompareTo(x.Value));
            var line = new StringBuilder("  held-off spots by solid:");
            for (int k = 0; k < cl.Count && k < 12; k++) line.Append($" {cl[k].Key} {cl[k].Value};");
            sb.AppendLine(line.ToString());
            for (int k = 0; k < worstList.Count && k < 12; k++)
            {
                var s = worstList[k];
                sb.AppendLine($"    over {s.gap:0.00} m at {s.h:0.0} m  wp {s.wp}  {s.coll}  at {s.at.x:0.0},{s.at.y:0.0},{s.at.z:0.0}");
            }
            for (int k = 0; k < negList.Count && k < 6; k++)
            {
                var s = negList[k];
                sb.AppendLine($"    under {s.gap:0.00} m at {s.h:0.0} m  wp {s.wp}  at {s.at.x:0.0},{s.at.y:0.0},{s.at.z:0.0}");
            }
            return sb.ToString();
        }
    }
}
