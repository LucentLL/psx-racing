using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ARTERIALS PASS'S FRAME COST (2026-11-10). Stands the 3 x 3 tiles
    /// round a place up through CityWorld (as the game does) and reports the
    /// build time, the renderers, vertices and triangles in all and of the
    /// road, ground and tree meshes, and the driver's-eye frame time (editor,
    /// 1280 x 720, each frame read back: a comparison, not a player's frame
    /// time). Run it once with the pass's switches off and once on.
    /// PSX_COST_PROBE=lat,lon,heading;... (default: Brookshire Blvd by N
    /// Crigler St, Johnston Rd's tree row, Brookshire at N Hoskins Rd).
    /// To PSXRacing_arterial_cost.txt.
    /// Headless: -executeMethod PSXRacing.EditorTools.ArterialCostProbe.Run
    /// </summary>
    public static class ArterialCostProbe
    {
        const int Frames = 80, W = 1280, H = 720;

        [MenuItem("PSX Racing/Probe Arterial Cost")]
        public static void Run()
        {
            var sb = new StringBuilder();
            string outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_arterial_cost.txt");
            var map = CityMap.Get();
            if (map == null) { File.WriteAllText(outPath, "no city data\n"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", 500f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            string spec = System.Environment.GetEnvironmentVariable("PSX_COST_PROBE");
            if (string.IsNullOrWhiteSpace(spec)) spec = "35.26456,-80.88093,322;35.04955,-80.84885,334;35.26583,-80.88154,342";
            sb.AppendLine("ARTERIAL COST PROBE  " + CityMeshes.TurnPocketLine());
            sb.AppendLine("  " + CityMeshes.MedianDressLine());
            var go = new GameObject("~arterialProbe");
            var world = go.AddComponent<CityWorld>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                foreach (var part in spec.Split(';'))
                {
                    var f = part.Split(',');
                    if (f.Length < 3) continue;
                    double lat = double.Parse(f[0], ci), lon = double.Parse(f[1], ci);
                    float hdg = float.Parse(f[2], ci);
                    var at = CityRefSpots.LL(lat, lon);
                    if (!map.NearestRoadPoint(at, 200f, false, out int ei, out float s, out _)) { sb.AppendLine($"{part}: no road near"); continue; }
                    var e = map.edges[ei];
                    var p = e.PointAt(s);
                    var eye = new Vector3(p.x, e.YAt(s) + 1.2f, p.y);
                    CityMeshes.MedianDressReset();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    world.EnsureRing(eye, 1);
                    double buildMs = clock.Elapsed.TotalMilliseconds;
                    int renderers = 0; long verts = 0, tris = 0, roadT = 0, groundT = 0, treeT = 0, plantT = 0;
                    foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        renderers++;
                        var mf = mr.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var m = mf.sharedMesh;
                        long t = 0;
                        for (int k = 0; k < m.subMeshCount; k++) t += (long)m.GetIndexCount(k) / 3;
                        verts += m.vertexCount; tris += t;
                        string n = mr.gameObject.name;
                        if (n == "Roads") roadT += t; else if (n == "Ground") groundT += t; else if (n.StartsWith("Tree")) treeT += t; else if (n == "MedianPlants") plantT += t;
                    }
                    sb.AppendLine(string.Format(ci, "--- {0}: e{1} '{2}' at ({3:0},{4:0}); 3 x 3 tiles stood up in {5:0} ms; renderers {6}, vertices {7}, triangles {8} (roads {9}, ground {10}, trees {11}, median plants {12})",
                        part, ei, e.name, p.x, p.y, buildMs, renderers, verts, tris, roadT, groundT, treeT, plantT));
                    sb.AppendLine("  " + CityMeshes.MedianDressLine());
                    float h = hdg * Mathf.Deg2Rad;
                    var rot = Quaternion.LookRotation(new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * 50f + Vector3.down * 1.0f);
                    var camGO = new GameObject("~costCam");
                    var cam = camGO.AddComponent<Camera>();
                    cam.transform.SetPositionAndRotation(eye, rot);
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
                    cam.nearClipPlane = 0.3f; cam.farClipPlane = 500f; cam.fieldOfView = 58f;
                    var rt = new RenderTexture(W, H, 24);
                    cam.targetTexture = rt;
                    var tex = new Texture2D(8, 8, TextureFormat.RGB24, false);
                    double sum = 0; int nF = 0;
                    var times = new System.Collections.Generic.List<double>(Frames);
                    for (int k = 0; k < Frames + 8; k++)
                    {
                        var c = System.Diagnostics.Stopwatch.StartNew();
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(W / 2, H / 2, 8, 8), 0, 0);   // waits for the frame
                        RenderTexture.active = null;
                        if (k >= 8) { sum += c.Elapsed.TotalMilliseconds; nF++; times.Add(c.Elapsed.TotalMilliseconds); }
                    }
                    times.Sort();
                    sb.AppendLine(string.Format(ci, "  frame at the driver's eye, {0} x {1}, editor: {2:0.000} ms mean of {3}, median {4:0.000}, worst {5:0.000}", W, H, sum / Mathf.Max(1, nF), nF,
                        times.Count > 0 ? times[times.Count / 2] : 0.0, times.Count > 0 ? times[times.Count - 1] : 0.0));
                    cam.targetTexture = null;
                    Object.DestroyImmediate(tex); Object.DestroyImmediate(rt); Object.DestroyImmediate(camGO);
                    world.DropAll();
                }
            }
            catch (System.Exception ex) { sb.AppendLine("EXCEPTION " + ex); }
            finally
            {
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[ArterialCostProbe]\n" + sb);
        }
    }
}
