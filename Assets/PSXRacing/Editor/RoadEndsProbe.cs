using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT THE WHEELS STAND ON (owner 2026-11-11, PC26: "Mountain tracks
    /// roughly first and last 800m has no road. Its like I'm driving on
    /// dirt"). For each venue in PSX_ENDS_IDS, on its built scene: a ray down
    /// at the centre and at each wheel track (+-2.5 m) every 8 m, the
    /// first collider it meets, its layer (8 = Road: the car's grip is by
    /// layer, CarController), and how far over the tarmac it stands; and the
    /// renderers whose bounds stand over the tarmac there. Writes
    /// PSXRacing_road_ends.txt.
    /// </summary>
    public static class RoadEndsProbe
    {
        public static void Run()
        {
            string ids = System.Environment.GetEnvironmentVariable("PSX_ENDS_IDS") ?? "BlueRidge";
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            try
            {
                foreach (var raw in ids.Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    string path = "Assets/PSXRacing/Scenes/" + id + ".unity";
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    Physics.SyncTransforms();
                    var tp = Object.FindFirstObjectByType<TrackPath>();
                    if (tp == null) { sb.AppendLine(id + ": no TrackPath"); continue; }
                    var road = GameObject.Find("Road");
                    var roadCol = road != null ? road.GetComponent<MeshCollider>() : null;
                    float sp = Mathf.Max(0.5f, tp.spacing);
                    int n = tp.Count;
                    sb.AppendLine($"== {id}: {n} stations, {n * sp:0} m, road collider {(roadCol != null ? roadCol.sharedMesh.name : "NONE")}");
                    var rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                    // the road's own mesh: format, materials, and its baked colour along it
                    var rmf = road != null ? road.GetComponent<MeshFilter>() : null;
                    var rmr = road != null ? road.GetComponent<MeshRenderer>() : null;
                    if (rmf != null && rmf.sharedMesh != null)
                    {
                        var rm = rmf.sharedMesh;
                        sb.AppendLine($"  road mesh {rm.name}: {rm.vertexCount} verts, index {rm.indexFormat}, submeshes {rm.subMeshCount}, colours {rm.colors32?.Length ?? 0}");
                        if (rmr != null) foreach (var mat in rmr.sharedMaterials)
                                sb.AppendLine($"    mat {(mat != null ? mat.name + " shader " + mat.shader.name + " kw " + string.Join(" ", mat.shaderKeywords) + " tex " + (mat.mainTexture != null ? mat.mainTexture.name : "none") : "null")}");
                        for (int s = 0; s < rm.subMeshCount; s++)
                        {
                            var d = rm.GetSubMesh(s);
                            sb.AppendLine($"    submesh {s}: {d.indexCount / 3} tris, verts {d.firstVertex}..{d.firstVertex + d.vertexCount}");
                        }
                        var rv = rm.vertices; var rc = rm.colors32; var ruv = rm.uv;
                        var M = road.transform.localToWorldMatrix;
                        int vstep = Mathf.Max(1, Mathf.RoundToInt(40f / sp));
                        var cl = new StringBuilder("    centre-vertex sky colour (m: r, v-uv, vertex):");
                        for (int i = 0; i < n; i += vstep)
                        {
                            Vector3 p = tp.GetPoint(i);
                            int bi = -1; float bd = 9f;
                            for (int q = 1; q < rv.Length; q += 3)
                            {
                                var w = M.MultiplyPoint3x4(rv[q]);
                                float dd = (w.x - p.x) * (w.x - p.x) + (w.z - p.z) * (w.z - p.z);
                                if (dd < bd) { bd = dd; bi = q; }
                            }
                            if (bi < 0) { cl.Append($" {i * sp:0}: none;"); continue; }
                            cl.Append($" {i * sp:0}: {(rc != null && rc.Length > bi ? rc[bi].r.ToString() : "-")}, {(ruv != null && ruv.Length > bi ? ruv[bi].y.ToString("0.0", ci) : "-")}, {bi};");
                        }
                        sb.AppendLine(cl.ToString());
                    }
                    int offRuns = 0, offTotal = 0, lastOff = -99, runStart = -1;
                    var runs = new List<string>();
                    int step = Mathf.Max(1, Mathf.RoundToInt(8f / sp));
                    var hits = new RaycastHit[16];
                    for (int i = 0; i < n; i += step)
                    {
                        Vector3 p = tp.GetPoint(i);
                        Vector3 t = tp.GetTangent(i); t.y = 0f; t.Normalize();
                        Vector3 rt = Vector3.Cross(Vector3.up, t);
                        string line = "";
                        bool off = false;
                        foreach (float lat in new[] { -2.5f, 0f, 2.5f })
                        {
                            Vector3 o = p + rt * lat + Vector3.up * 40f;
                            int k = Physics.RaycastNonAlloc(o, Vector3.down, hits, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                            float best = float.MaxValue; RaycastHit first = default; float roadY = float.NaN;
                            for (int q = 0; q < k; q++)
                            {
                                if (hits[q].distance < best) { best = hits[q].distance; first = hits[q]; }
                                if (hits[q].collider == roadCol) roadY = hits[q].point.y;
                            }
                            if (k == 0) { line += $" [{lat:+0.0;-0.0;0}: nothing]"; off = true; continue; }
                            // and the land under the tarmac: the first thing that is not the road layer
                            int k2 = Physics.RaycastNonAlloc(o, Vector3.down, hits, 80f, ~((1 << 2) | (1 << 8)), QueryTriggerInteraction.Ignore);
                            float gy = float.NaN; string gn = "-";
                            for (int q = 0; q < k2; q++) if (float.IsNaN(gy) || hits[q].point.y > gy) { gy = hits[q].point.y; gn = hits[q].collider.name; }
                            k = Physics.RaycastNonAlloc(o, Vector3.down, hits, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                            if (!float.IsNaN(gy) && !float.IsNaN(0f)) line += $" <land {gn} {gy - first.point.y:+0.00;-0.00}>";
                            bool isRoad = first.collider.gameObject.layer == 8;
                            if (!isRoad) off = true;
                            line += $" [{lat:+0.0;-0.0;0}: {first.collider.name}/L{first.collider.gameObject.layer} y{first.point.y:0.00}" +
                                    (float.IsNaN(roadY) ? " road MISSED" : $" over road {first.point.y - roadY:+0.00;-0.00}") + "]";
                        }
                        // renderers over the tarmac here (not the road's own)
                        var over = new List<string>();
                        foreach (var r in rends)
                        {
                            if (!r.enabled || r.gameObject == road) continue;
                            var b = r.bounds;
                            if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                            if (b.min.y > p.y + 0.6f || b.max.y < p.y - 0.1f) continue;
                            string nm = r.name;
                            if (nm.StartsWith("Kerb") || nm.StartsWith("Wall") || nm.StartsWith("Posts") || nm.StartsWith("Stage")) continue;
                            if (over.Count < 6 && !over.Contains(nm)) over.Add(nm + "(" + (r.sharedMaterial != null ? r.sharedMaterial.name : "-") + ")");
                        }
                        float m = i * sp;
                        bool nearEnd = m < 1200f || m > n * sp - 1200f;
                        if (off) { offTotal++; if (i - lastOff > step) { runStart = i; offRuns++; } lastOff = i; }
                        else if (runStart >= 0 && lastOff == i - step) { runs.Add($"{runStart * sp:0}-{lastOff * sp:0} m"); runStart = -1; }
                        if (nearEnd && (i % (step * 5) == 0 || off))
                            sb.AppendLine($"  {m,6:0} m{(off ? " OFF" : "    ")}{line}  renderers: {string.Join(", ", over)}");
                    }
                    if (runStart >= 0) runs.Add($"{runStart * sp:0}-{lastOff * sp:0} m");
                    sb.AppendLine($"  {id}: samples off the road layer {offTotal}; runs: {string.Join("; ", runs)}");
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_road_ends.txt"), sb.ToString());
            Debug.Log("[RoadEndsProbe]\n" + sb);
        }
    }
}
