using System.IO;
using UnityEngine.SceneManagement;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ONE STAGE, BUILT AND AUDITED, in minutes rather than a forty-minute
    /// verify: for shaping the stage builder against a road it does not yet
    /// handle (Chimney Rock's 6 m switchbacks, NC 226's cut banks - both in
    /// TrackCatalog.HeldBack). Builds each venue named in PSX_LAB_IDS (a held
    /// venue included), then runs the self-test's roadside checks and the
    /// obstacle audit's passes on that scene alone.
    ///
    ///   tools\stage-lab.ps1 ChimneyRock,GillespieGap  ->  PSXRacing_stage_lab.txt
    /// </summary>
    public static class StageLab
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            string ids = System.Environment.GetEnvironmentVariable("PSX_LAB_IDS") ?? "";
            try
            {
                foreach (var raw in ids.Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    TrackCatalog.TrackDef def = null;
                    foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                    foreach (var d in TrackCatalog.HeldBack) if (d.id == id) def = d;
                    if (def == null) { sb.AppendLine("no venue " + id); continue; }
                    // PSX_LAB_NOBUILD=1: look again at the scene the last run
                    // built (sections and plans only, seconds instead of minutes).
                    bool noBuild = System.Environment.GetEnvironmentVariable("PSX_LAB_NOBUILD") == "1";
                    string path = noBuild ? "Assets/PSXRacing/Scenes/" + id + ".unity" : PSXRacingBuilder.BuildOneForLab(def);
                    sb.AppendLine("=== " + id + " -> " + path);
                    if (!noBuild)
                    {
                        sb.AppendLine(LifeSimSelfTest.LabRoadside(def, path));
                        sb.AppendLine(TrackObstacleAudit.AuditForLab(def, path));
                        // And the terrain audit's pass on this one scene: the lattice
                        // under the shoulder, props showing daylight - the verify's
                        // third stage, in minutes.
                        var terrain = new StringBuilder();
                        TerrainAudit.AuditOne(def, terrain);
                        sb.AppendLine(terrain.ToString());
                        sb.AppendLine(DownFaces(path));
                    }
                    string probes = System.Environment.GetEnvironmentVariable("PSX_LAB_WP") ?? "";
                    foreach (var pr in probes.Split(','))
                        if (pr.Trim().Length > 1) sb.AppendLine(Section(path, pr.Trim()));
                    string plans = System.Environment.GetEnvironmentVariable("PSX_LAB_PLAN") ?? "";
                    foreach (var pl in plans.Split(','))
                        if (pl.Trim().Length > 0) sb.AppendLine(Plan(path, id, pl.Trim()));
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_stage_lab.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>Every roadside triangle that faces DOWN (a fold), with the
        /// nearest waypoint, the side and how far out it is.</summary>
        static string DownFaces(string scenePath)
        {
            var sb = new StringBuilder("down-facing roadside triangles:" + System.Environment.NewLine);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var tp = Object.FindFirstObjectByType<TrackPath>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                string nm = mf.gameObject.name;
                if (!(nm.StartsWith("RoadEdge") || nm.StartsWith("Kerb"))) continue;
                var m = mf.sharedMesh; if (m == null) continue;
                var v = m.vertices; var t = m.triangles; var xf = mf.transform;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = xf.TransformPoint(v[t[i]]), b = xf.TransformPoint(v[t[i + 1]]), c = xf.TransformPoint(v[t[i + 2]]);
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 1e-10f || n.normalized.y >= 0f) continue;
                    Vector3 cen = (a + b + c) / 3f;
                    int w = tp.NearestIndex(cen);
                    Vector3 r = Vector3.Cross(Vector3.up, tp.GetTangent(w)).normalized;
                    float lat = Vector3.Dot(cen - tp.GetPoint(w), r);
                    sb.AppendLine("  " + nm + " wp " + w + (lat < 0 ? " L " : " R ") + Mathf.Abs(lat).ToString("0.00") +
                                  " m out, normal.y " + n.normalized.y.ToString("0.00") + ", area " + (n.magnitude * 0.5f).ToString("0.000") +
                                  ", curvature R " + (tp.curvatures != null && tp.curvatures[w] > 1e-5f ? (1f / tp.curvatures[w]).ToString("0.0") : "-"));
                }
            }
            return sb.ToString();
        }

        /// <summary>A cross-section the way the audit takes one: straight down
        /// every 5 cm from just inside the tarmac edge outward, at waypoint N on
        /// side L or R ("1300L"). Prints where the surface or the collider
        /// changes, so a lip is a pair of lines.</summary>
        static string Section(string scenePath, string spec)
        {
            var sb = new StringBuilder("section " + spec + ":" + System.Environment.NewLine);
            if (EditorSceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var tp = Object.FindFirstObjectByType<TrackPath>();
            int w = int.Parse(spec.Substring(0, spec.Length - 1));
            float side = char.ToUpperInvariant(spec[spec.Length - 1]) == 'L' ? -1f : 1f;
            Vector3 c = tp.GetPoint(w);
            Vector3 r = Vector3.Cross(Vector3.up, tp.GetTangent(w)).normalized * side;
            float half = tp.roadWidth * 0.5f;
            string lastName = null; float lastY = float.NaN;
            for (float e = -0.1f; e <= 20f; e += 0.05f)
            {
                Vector3 o = c + r * (half + e) + Vector3.up * 30f;
                string name = "(none)"; float y = float.NaN;
                if (Physics.Raycast(o, Vector3.down, out var hit, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                { name = hit.collider.name; y = hit.point.y - c.y; }
                bool jump = !float.IsNaN(lastY) && !float.IsNaN(y) && Mathf.Abs(y - lastY) > 0.04f;
                if (name != lastName || jump || Mathf.Abs(e - Mathf.Round(e)) < 0.026f)
                    sb.AppendLine("  e " + e.ToString("0.00") + "  dy " + (float.IsNaN(y) ? "-" : y.ToString("0.000")) +
                                  "  " + name + (jump ? "   <-- step " + (y - lastY).ToString("0.000") : ""));
                lastName = name; lastY = y;
            }
            return sb.ToString();
        }

        /// <summary>A PLAN of the colliders round waypoint N ("1049" or
        /// "1049:80" for an 80 m square), straight down every 10 cm, written to
        /// PSXRacing_lab_plan_&lt;id&gt;_&lt;wp&gt;.png beside the project. Colour is
        /// WHAT is hit (road grey, kerb red, shoulder tan, ground green, rock top
        /// brown, bank collider orange, wall magenta, nothing black), shaded by
        /// height with a contour every 0.5 m; YELLOW is a step of more than
        /// 0.06 m to the next cell (a face a wheel meets). The centreline is
        /// white, every tenth waypoint cyan, the named one a blue cross.</summary>
        static string Plan(string scenePath, string id, string spec)
        {
            if (EditorSceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var tp = Object.FindFirstObjectByType<TrackPath>();
            var parts = spec.Split(':');
            int w = int.Parse(parts[0]);
            float size = parts.Length > 1 ? float.Parse(parts[1]) : 60f;
            const float cell = 0.1f;
            int N = Mathf.RoundToInt(size / cell);
            Vector3 c = tp.GetPoint(w);
            float x0 = c.x - size * 0.5f, z0 = c.z - size * 0.5f;
            var hy = new float[N * N];
            var kind = new byte[N * N];
            var names = new System.Collections.Generic.Dictionary<string, int>();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    var o = new Vector3(x0 + (i + 0.5f) * cell, c.y + 60f, z0 + (j + 0.5f) * cell);
                    int k = j * N + i;
                    if (!Physics.Raycast(o, Vector3.down, out var hit, 200f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    { hy[k] = float.NaN; kind[k] = 0; continue; }
                    hy[k] = hit.point.y - c.y;
                    string nm = hit.collider.name;
                    names[nm] = names.TryGetValue(nm, out int q) ? q + 1 : 1;
                    kind[k] = nm == "Road" ? (byte)1 : nm.StartsWith("Kerb") ? (byte)2 : nm.StartsWith("RoadEdge") ? (byte)3 :
                              nm.StartsWith("Ground") ? (byte)4 : nm.StartsWith("BankTop") ? (byte)5 :
                              nm.StartsWith("BankColl") || nm.StartsWith("Bank") ? (byte)6 : nm.StartsWith("Wall") ? (byte)7 : (byte)8;
                }
            var pal = new[] { new Color(0,0,0), new Color(.45f,.45f,.48f), new Color(.85f,.2f,.2f), new Color(.8f,.68f,.45f),
                              new Color(.3f,.62f,.3f), new Color(.5f,.33f,.2f), new Color(1f,.55f,.1f), new Color(.9f,.2f,.9f), new Color(.3f,.5f,.9f) };
            var px = new Color32[N * N];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    if (kind[k] == 0) { px[k] = new Color32(0, 0, 0, 255); continue; }
                    float y = hy[k];
                    float shade = 0.75f + 0.25f * Mathf.Repeat(y, 4f) / 4f;
                    bool contour = false;
                    Color col = pal[kind[k]] * shade;
                    // A step is a BREAK in the slope (a steep hillside is not
                    // one): the second difference across the cell.
                    float step = 0f;
                    if (i > 0 && i + 1 < N && !float.IsNaN(hy[k + 1]) && !float.IsNaN(hy[k - 1]))
                        step = Mathf.Max(step, Mathf.Abs(hy[k + 1] - 2f * y + hy[k - 1]));
                    if (j > 0 && j + 1 < N && !float.IsNaN(hy[k + N]) && !float.IsNaN(hy[k - N]))
                        step = Mathf.Max(step, Mathf.Abs(hy[k + N] - 2f * y + hy[k - N]));
                    if (i > 0 && j > 0)
                    {
                        // a 0.5 m contour where the cell crosses one
                        float yl = hy[k - 1], yd = hy[k - N];
                        if ((!float.IsNaN(yl) && Mathf.Floor(yl / 0.5f) != Mathf.Floor(y / 0.5f)) ||
                            (!float.IsNaN(yd) && Mathf.Floor(yd / 0.5f) != Mathf.Floor(y / 0.5f))) contour = true;
                    }
                    if (contour) col *= 0.6f;
                    if (step > 0.06f) col = step > 0.5f ? new Color(1f, 1f, 1f) : new Color(1f, .95f, 0f);
                    col.a = 1f;
                    px[k] = col;
                }
            void Dot(Vector3 p, Color32 col, int r)
            {
                int pi = Mathf.FloorToInt((p.x - x0) / cell), pj = Mathf.FloorToInt((p.z - z0) / cell);
                for (int dj = -r; dj <= r; dj++)
                    for (int di = -r; di <= r; di++)
                    {
                        int a = pi + di, b = pj + dj;
                        if (a >= 0 && b >= 0 && a < N && b < N) px[b * N + a] = col;
                    }
            }
            var sb = new StringBuilder("plan " + spec + " (" + size + " m, x " + x0.ToString("0.0") + ".." + (x0 + size).ToString("0.0") +
                                       ", z " + z0.ToString("0.0") + ".." + (z0 + size).ToString("0.0") + ", +z up):" + System.Environment.NewLine);
            int nWp = tp.waypoints.Length;
            for (int q = 0; q < nWp; q++)
            {
                Vector3 p = tp.GetPoint(q);
                if (Mathf.Abs(p.x - c.x) > size * 0.5f || Mathf.Abs(p.z - c.z) > size * 0.5f) continue;
                Dot(p, q % 10 == 0 ? new Color32(0, 255, 255, 255) : new Color32(255, 255, 255, 255), q % 10 == 0 ? 3 : 1);
                if (q % 10 == 0)
                    sb.AppendLine("  wp " + q + " at px (" + Mathf.FloorToInt((p.x - x0) / cell) + ", " + (N - 1 - Mathf.FloorToInt((p.z - z0) / cell)) +
                                  ")  y " + (p.y - c.y).ToString("0.00") + "  R " +
                                  (tp.curvatures != null && tp.curvatures[q] > 1e-5f ? (1f / tp.curvatures[q]).ToString("0.0") : "-"));
            }
            Dot(c, new Color32(40, 80, 255, 255), 5);
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_lab_plan_" + id + "_" + w + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            sb.Append("  hit:");
            foreach (var kv in names) if (kv.Value > N * N / 400) sb.Append(" " + kv.Key + " " + kv.Value);
            sb.AppendLine();
            sb.AppendLine("  -> " + file);
            return sb.ToString();
        }
    }
}
