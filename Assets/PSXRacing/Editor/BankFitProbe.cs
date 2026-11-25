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

        /// <summary>
        /// THE NOSE AGAINST THE ROCK (the proof sheet's side views): per PSX_BF_SHOTS = "Venue:wp:L|R;..." the scene's
        /// first car stood square to the cut at that waypoint and moved toward it 1 cm at a time until one of its
        /// colliders meets a solid; then a side view along the face (PNG beside the project, PSXRacing_bfshots/,
        /// PSX_BF_TAG in the name) and the gap from the car's nose to the first DRAWN surface ahead of it.
        /// </summary>
        public static void Shots()
        {
            var sb = new StringBuilder();
            string tag = System.Environment.GetEnvironmentVariable("PSX_BF_TAG") ?? "x";
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bfshots");
            Directory.CreateDirectory(outDir);
            try
            {
                foreach (var spec in (System.Environment.GetEnvironmentVariable("PSX_BF_SHOTS") ?? "").Split(new[] { ';', '|' }))
                {
                    var f = spec.Trim().Split(':');
                    if (f.Length < 3 || !int.TryParse(f[1], out int wp)) continue;
                    sb.AppendLine(Shot(f[0], wp, f[2].Trim().ToUpperInvariant() == "L" ? -1 : 1, tag, outDir));
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bfshots.txt"), sb.ToString());
            Debug.Log("[BF] " + sb);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static bool GroundName(string on) =>
            on.StartsWith("Road") || on.StartsWith("Kerb") || on.StartsWith("Verge") || on.StartsWith("Ground") || on.StartsWith("Shoulder") || on.StartsWith("Apron") || on.StartsWith("StandIn");

        static string Shot(string id, int wp, int sd, string tag, string outDir)
        {
            string scenePath = "Assets/PSXRacing/Scenes/" + id + ".unity";
            if (!File.Exists(scenePath)) return id + ": NO SCENE";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            CarController car = null;
            foreach (var c in Object.FindObjectsByType<CarController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { car = c; break; }
            if (path == null || car == null) return id + ": no path / car";
            car.gameObject.SetActive(true);
            Vector3 p = path.GetPoint(wp), tan = path.GetTangent(wp); tan.y = 0f; tan.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized, dir = right * sd;
            var carCols = new List<Collider>(car.GetComponentsInChildren<Collider>(true));
            var rb = car.GetComponent<Rigidbody>();
            const float lift = 0.45f;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            float half = path.roadWidth * 0.5f;
            Vector3 Ground(Vector3 q)
            {
                float best = float.MinValue; Vector3 at = q;
                foreach (var h in Physics.RaycastAll(q + Vector3.up * 5f, Vector3.down, 12f))
                    if (!carCols.Contains(h.collider) && !h.collider.isTrigger && GroundName(h.collider.name) && h.point.y > best) { best = h.point.y; at = h.point; }
                return at;
            }
            float e = half - 2.6f, touch = -1f;
            string hitName = "-";
            for (; e < half + 8f; e += 0.01f)
            {
                Vector3 g = Ground(p + dir * e);
                car.transform.SetPositionAndRotation(g + Vector3.up * lift, rot);
                if (rb != null) { rb.position = car.transform.position; rb.rotation = rot; }
                Physics.SyncTransforms();
                bool hit = false;
                foreach (var cc in carCols)
                {
                    if (cc == null || cc.isTrigger || !cc.enabled) continue;
                    foreach (var o in Physics.OverlapBox(cc.bounds.center, cc.bounds.extents + Vector3.one * 0.02f, Quaternion.identity))
                    {
                        if (o == null || carCols.Contains(o) || o.isTrigger || GroundName(o.name)) continue;
                        if (Physics.ComputePenetration(cc, cc.transform.position, cc.transform.rotation, o, o.transform.position, o.transform.rotation, out _, out float depth) && depth > 0.001f)
                        { hit = true; hitName = o.name; break; }
                    }
                    if (hit) break;
                }
                if (hit) { touch = e; break; }
            }
            if (touch < 0f) return $"{id} wp {wp} {(sd > 0 ? "R" : "L")}: nothing met within {half + 8f:0.0} m";
            e = touch - 0.01f;
            Vector3 g2 = Ground(p + dir * e);
            car.transform.SetPositionAndRotation(g2 + Vector3.up * lift, rot);
            if (rb != null) { rb.position = car.transform.position; rb.rotation = rot; }
            Physics.SyncTransforms();
            // the nose: the car's colliders' furthest point along dir
            float noseD = float.MinValue;
            foreach (var cc in carCols)
            {
                if (cc == null || cc.isTrigger) continue;
                Vector3 c0 = cc.bounds.center - car.transform.position;
                noseD = Mathf.Max(noseD, Vector3.Dot(c0, dir) + Mathf.Abs(dir.x) * cc.bounds.extents.x + Mathf.Abs(dir.z) * cc.bounds.extents.z);
            }
            // the drawn surface ahead of the nose, as the probe asks it
            var temp = new List<GameObject>();
            var near = new Bounds(car.transform.position + dir * 2f, new Vector3(12f, 8f, 12f));
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
                if (mf.gameObject.layer == 2 || mf.gameObject.layer == 10) continue;
                var ren = mf.GetComponent<MeshRenderer>();
                if (ren == null || !ren.enabled || mf.GetComponentInParent<CarController>() != null) continue;
                if (!ren.bounds.Intersects(near)) continue;
                var go = new GameObject("BFS") { layer = ProbeLayer, hideFlags = HideFlags.DontSave };
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                temp.Add(go);
            }
            Physics.SyncTransforms();
            bool hb = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = false;
            var gaps = new StringBuilder();
            foreach (float hy in new[] { 0.3f, 0.6f, 0.9f })
            {
                Vector3 o = car.transform.position + dir * (noseD - 0.3f); o.y = g2.y + hy;
                float d = Physics.Raycast(o, dir, out RaycastHit dh, 6f, 1 << ProbeLayer) ? dh.distance - 0.3f : 9.99f;
                gaps.Append($" {hy:0.0} m {d * 100f:0} cm;");
            }
            Physics.queriesHitBackfaces = hb;
            foreach (var g in temp) Object.DestroyImmediate(g);
            // the side view: along the face, from 4.5 m down the road
            Vector3 nose = car.transform.position + dir * noseD; nose.y = g2.y + 0.7f;
            const int W = 960, H = 540;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var camGo = new GameObject("BFShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.targetTexture = rt; cam.fieldOfView = 38f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
            cam.transform.position = nose + tan * 4.5f + Vector3.up * 0.5f - dir * 0.6f;
            cam.transform.rotation = Quaternion.LookRotation(nose + dir * 0.4f - cam.transform.position, Vector3.up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false);
            RenderTexture.active = null;
            string fn = $"{id}_{wp}{(sd > 0 ? "R" : "L")}_{tag}.png";
            File.WriteAllBytes(Path.Combine(outDir, fn), tex.EncodeToPNG());
            Object.DestroyImmediate(camGo); rt.Release();
            return $"{id} wp {wp} {(sd > 0 ? "R" : "L")}: the car stops with its centre {touch:0.00} m out from the centreline, on {hitName}; nose to the drawn surface ahead:{gaps} -> {fn}";
        }

        /// <summary>The textures the game samples, as imported (size, filter), read back through the GPU into PNGs
        /// beside the project (PSXRacing_texdump/): PSX_BF_TEX = asset paths, comma separated. For the tile proofs.</summary>
        public static void DumpTex()
        {
            var sb = new StringBuilder();
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_texdump");
            Directory.CreateDirectory(outDir);
            foreach (var raw in (System.Environment.GetEnvironmentVariable("PSX_BF_TEX") ?? "").Split(','))
            {
                string ap = raw.Trim();
                if (ap.Length == 0) continue;
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ap);
                if (t == null) { sb.AppendLine("no texture " + ap); continue; }
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var o = new Texture2D(t.width, t.height, TextureFormat.RGB24, false);
                o.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); o.Apply(false);
                RenderTexture.active = null; RenderTexture.ReleaseTemporary(rt);
                string fn = Path.GetFileNameWithoutExtension(ap) + "_imported.png";
                File.WriteAllBytes(Path.Combine(outDir, fn), o.EncodeToPNG());
                sb.AppendLine($"{ap}: imported {t.width}x{t.height}, {t.format}, filter {t.filterMode}, wrap {t.wrapModeU}/{t.wrapModeV}, mips {t.mipmapCount} -> {fn}");
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_texdump.txt"), sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>(stage3, D) Do the stage path's stored curvatures (what the AI plans its speed on) stand where the
        /// waypoints bend? Per PSX_BF_VENUES scene: the curvature measured again off the waypoints as they are
        /// (flattened, 5-tap as BuildWaypoints), the best index shift between the two and the error, and listed spots
        /// (PSX_BF_WPS = "a-b,c-d"). PSXRacing_curvprobe.txt.</summary>
        public static void Curv()
        {
            var sb = new StringBuilder();
            string ids = System.Environment.GetEnvironmentVariable("PSX_BF_VENUES");
            if (string.IsNullOrEmpty(ids)) ids = "LittleSwitzerland";
            foreach (var raw in ids.Split(','))
            {
                string id = raw.Trim(); if (id.Length == 0) continue;
                string scenePath = "Assets/PSXRacing/Scenes/" + id + ".unity";
                if (!File.Exists(scenePath)) { sb.AppendLine(id + ": no scene"); continue; }
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var path = Object.FindFirstObjectByType<TrackPath>();
                if (path == null || path.curvatures == null) { sb.AppendLine(id + ": no path / curvatures"); continue; }
                int n = path.Count;
                var w = path.waypoints;
                var k = new float[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = w[(i - 1 + n) % n], b = w[i], c = w[(i + 1) % n];
                    a.y = b.y = c.y = 0f;
                    k[i] = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad / path.spacing;
                }
                var ks = new float[n];
                for (int i = 0; i < n; i++) { float s = 0f; for (int o = -2; o <= 2; o++) s += k[(i + o + n) % n]; ks[i] = s / 5f; }
                var cv = path.curvatures;
                sb.AppendLine($"{id}: {n} waypoints, {cv.Length} stored curvatures, spacing {path.spacing}");
                int best = 0; double bestErr = double.MaxValue;
                for (int sh = -40; sh <= 40; sh++)
                {
                    double e = 0;
                    for (int i = 0; i < n; i++) { float d = cv[(i + sh + n) % n] - ks[i]; e += d * d; }
                    if (e < bestErr) { bestErr = e; best = sh; }
                }
                double e0 = 0; float worst = 0f; int worstI = 0;
                for (int i = 0; i < n; i++)
                {
                    float d = cv[i] - ks[i]; e0 += d * d;
                    if (Mathf.Abs(d) > Mathf.Abs(worst)) { worst = d; worstI = i; }
                }
                sb.AppendLine($"  rms stored - measured {System.Math.Sqrt(e0 / n):0.00000} /m; best shift {best} (rms {System.Math.Sqrt(bestErr / n):0.00000}); worst {worst:+0.0000;-0.0000} at wp {worstI}");
                int under = 0;
                for (int i = 0; i < n; i++) if (ks[i] > 1f / 120f && cv[i] < ks[i] * 0.7f) under++;
                sb.AppendLine($"  waypoints bending tighter than 120 m whose stored curvature is under 70 % of it: {under}");
                foreach (var rng in (System.Environment.GetEnvironmentVariable("PSX_BF_WPS") ?? "").Split(','))
                {
                    var ab = rng.Split('-');
                    if (ab.Length != 2 || !int.TryParse(ab[0], out int a0) || !int.TryParse(ab[1], out int a1)) continue;
                    var line = new StringBuilder($"  wp {a0}-{a1} stored/measured radius m:");
                    for (int i = a0; i <= a1 && i < n; i++)
                        line.Append($" {i}:{(cv[i] > 1e-5f ? 1f / cv[i] : 9999f):0}/{(ks[i] > 1e-5f ? 1f / ks[i] : 9999f):0}");
                    sb.AppendLine(line.ToString());
                }
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_curvprobe.txt"), sb.ToString());
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
