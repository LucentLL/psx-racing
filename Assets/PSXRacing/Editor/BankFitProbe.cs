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
        /// <summary>(stage4) the round's target line: drawn cut rock more than 5 cm in front of its solid.</summary>
        public const float Under5 = -0.05f;
        /// <summary>(stage4) a POCKET: the solid's surface stands this much deeper than BOTH sides of it within
        /// PocketHalfM along the road (a slot narrower than a car), or than the surface up to PocketUpM over it (an
        /// undercut a nose goes under), between 0.2 and 1.0 m over the edge.</summary>
        public const float PocketDepth = 0.25f, PocketHalfM = 0.9f, PocketUpM = 0.4f;

        /// <summary>(stage4) PSX_BF_SOLIDSET on a scene built with PSX_BANKSOLID_AB=1: "old" measures the 77f4abf8 slab
        /// (the inactive BankSolidOld holders on, the band solids and their partial face colliders off), "new" or unset
        /// the scene as it ships. Edit mode, nothing saved.</summary>
        public static string ApplySolidSet()
        {
            string set = (System.Environment.GetEnvironmentVariable("PSX_BF_SOLIDSET") ?? "new").Trim().ToLowerInvariant();
            if (set != "old") return "new";
            int on = 0, off = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "BankSolidOld" && !t.gameObject.activeSelf) { t.gameObject.SetActive(true); on++; }
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                if (mc == null || mc.sharedMesh == null) continue;
                bool inOld = false;
                for (var t = mc.transform; t != null; t = t.parent) if (t.name == "BankSolidOld") { inOld = true; break; }
                if (inOld) continue;
                string mn = mc.sharedMesh.name;
                if (mn.Contains("CollBand") || mn.Contains("BankLipColl")) { mc.enabled = false; off++; }
            }
            Physics.SyncTransforms();
            return on == 0 ? "old (NO BankSolidOld in this scene: not built with PSX_BANKSOLID_AB=1)" : $"old ({on} holders on, {off} new colliders off)";
        }

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
                var mcc = cc as MeshCollider;
                if (mcc != null && !mcc.convex) continue;
                Vector3 far = cc.ClosestPoint(car.transform.position + dir * 20f + Vector3.up * 0.3f);
                noseD = Mathf.Max(noseD, Vector3.Dot(far - car.transform.position, dir));
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
                Vector3 o = car.transform.position + dir * (noseD - 1.0f); o.y = g2.y + hy;
                float d = Physics.Raycast(o, dir, out RaycastHit dh, 6f, 1 << ProbeLayer) ? dh.distance - 1.0f : float.NaN;
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
            cam.transform.position = nose + tan * 5.5f + Vector3.up * 0.5f - dir * 1.6f;
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

        /// <summary>(stage4) The obstacle audit (GHOST BARRIERS and the rest) on each PSX_BF_VENUES scene as it stands,
        /// without a build: PSXRacing_obstacles.txt.</summary>
        public static void Obstacles()
        {
            var sb = new StringBuilder();
            string ids = System.Environment.GetEnvironmentVariable("PSX_BF_VENUES") ?? "";
            try
            {
                foreach (var raw in ids.Split(','))
                {
                    string id = raw.Trim(); if (id.Length == 0) continue;
                    TrackCatalog.TrackDef def = null;
                    foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                    if (def == null) { sb.AppendLine("no venue " + id); continue; }
                    sb.AppendLine("=== " + id);
                    sb.AppendLine(TrackObstacleAudit.AuditForLab(def, "Assets/PSXRacing/Scenes/" + id + ".unity"));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_obstacles.txt"), sb.ToString());
                }
            }
            catch (System.Exception ex) { sb.AppendLine("THREW " + ex); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_obstacles.txt"), sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>(stage4, B) THE BOULDERS NEAR THE ROAD: per PSX_BF_VENUES scene, every solid "Boulder" (a convex hull
        /// under Track/Banks/Boulders) within PSX_BF_BOULDER_E m (default 8) of a tarmac edge: is the stone it stands for
        /// drawn (a MeshForest instance within 5 cm), and along level rays from the road at 0.2-1.4 m, how far the hull
        /// stands in front of the drawn stone (the worst). Lists the ones over 0.15 m, the undrawn, and every one within
        /// 10 waypoints of PSX_BF_WP. PSXRacing_boulders.txt.</summary>
        public static void Boulders()
        {
            var sb = new StringBuilder();
            string ids = System.Environment.GetEnvironmentVariable("PSX_BF_VENUES");
            if (string.IsNullOrEmpty(ids)) ids = "ChimneyRock";
            float maxE = 8f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_BF_BOULDER_E") ?? "8", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out maxE);
            int wpAt = -1;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_BF_WP") ?? "-1", out wpAt);
            try
            {
                foreach (var raw in ids.Split(','))
                {
                    string id = raw.Trim(); if (id.Length == 0) continue;
                    string scenePath = "Assets/PSXRacing/Scenes/" + id + ".unity";
                    if (!File.Exists(scenePath)) { sb.AppendLine(id + ": no scene"); continue; }
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var path = Object.FindFirstObjectByType<TrackPath>();
                    float half = path.roadWidth * 0.5f;
                    var temp = new List<GameObject>();
                    var instAt = new List<(Vector3 p, string mesh)>();
                    foreach (var mfo in Object.FindObjectsByType<MeshForest>(FindObjectsSortMode.None))
                    {
                        if (mfo.inst == null || mfo.variants == null || mfo.instScaleVar == null) continue;
                        var l2w = mfo.transform.localToWorldMatrix;
                        for (int i = 0; i < mfo.inst.Length && i < mfo.instScaleVar.Length; i++)
                        {
                            int v = (int)mfo.instScaleVar[i].y;
                            if (v < 0 || v >= mfo.variants.Length) continue;
                            var vr = mfo.variants[v];
                            var m0 = vr != null && vr.lods != null && vr.lods.Length > 0 ? vr.lods[0] : null;
                            if (m0 == null) continue;
                            string nm = m0.name.ToLowerInvariant();
                            if (!(nm.Contains("stone") || nm.Contains("rock") || nm.Contains("boulder"))) continue;
                            Vector3 lp = new Vector3(mfo.inst[i].x, mfo.inst[i].y, mfo.inst[i].z);
                            Vector3 wpos = l2w.MultiplyPoint3x4(lp);
                            int ni = path.NearestIndex(wpos);
                            Vector3 r0 = Vector3.Cross(Vector3.up, path.GetTangent(ni)).normalized;
                            if (Mathf.Abs(Vector3.Dot(wpos - path.GetPoint(ni), r0)) - half > maxE + 4f) continue;
                            instAt.Add((wpos, m0.name));
                            float sc = mfo.instScaleVar[i].x;
                            var tl = mfo.instTilt != null && i < mfo.instTilt.Length ? mfo.instTilt[i] : Vector2.zero;
                            var go = new GameObject("BFB") { layer = ProbeLayer, hideFlags = HideFlags.DontSave };
                            go.transform.SetParent(mfo.transform, false);
                            go.transform.localPosition = lp;
                            go.transform.localRotation = Quaternion.Euler(tl.x, 0f, tl.y) * Quaternion.Euler(0f, mfo.inst[i].w, 0f);
                            go.transform.localScale = new Vector3(sc, sc, sc);
                            var hull = new GameObject("BFB") { layer = ProbeLayer, hideFlags = HideFlags.DontSave };
                            hull.transform.SetParent(go.transform, false);
                            hull.transform.localPosition = vr.pre.GetColumn(3);
                            hull.transform.localRotation = vr.pre.rotation;
                            hull.transform.localScale = vr.pre.lossyScale;
                            hull.AddComponent<MeshCollider>().sharedMesh = m0;
                            temp.Add(go);
                        }
                    }
                    Physics.SyncTransforms();
                    int n = 0, over = 0, undrawn = 0;
                    bool hb = Physics.queriesHitBackfaces;
                    Physics.queriesHitBackfaces = false;
                    try
                    {
                        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                        {
                            if (t.name != "Boulder" || t.parent == null || t.parent.name != "Boulders") continue;
                            var col = t.GetComponentInChildren<Collider>();
                            if (col == null) continue;
                            Vector3 c0 = col.bounds.center;
                            int wi = path.NearestIndex(c0);
                            Vector3 p = path.GetPoint(wi), tan = path.GetTangent(wi); tan.y = 0f; tan.Normalize();
                            Vector3 r = Vector3.Cross(Vector3.up, tan).normalized;
                            float lat = Vector3.Dot(c0 - p, r);
                            float sd = Mathf.Sign(lat);
                            if (Mathf.Abs(lat) - half - col.bounds.extents.magnitude > maxE) continue;
                            n++;
                            bool drawn = false; string dm = "-";
                            foreach (var ia in instAt) if ((ia.p - t.position).sqrMagnitude < 0.0025f) { drawn = true; dm = ia.mesh; break; }
                            if (!drawn) undrawn++;
                            float worst = 0f, worstH = 0f; int hits = 0;
                            Vector3 dir = r * sd;
                            Vector3 foot = c0 - dir * Vector3.Dot(c0 - p, dir);
                            for (float h = 0.2f; h <= 1.41f; h += 0.2f)
                                for (float s = -0.6f; s <= 0.61f; s += 0.3f)
                                {
                                    Vector3 o = foot + tan * s + dir * (half - 1f);
                                    o.y = p.y + h;
                                    var ray = new Ray(o, dir);
                                    if (!col.Raycast(ray, out RaycastHit ch, 30f)) continue;
                                    hits++;
                                    float dd = Physics.Raycast(ray, out RaycastHit dh2, ch.distance + 4f, 1 << ProbeLayer) ? dh2.distance : ch.distance + 4f;
                                    if (dd - ch.distance > worst) { worst = dd - ch.distance; worstH = h; }
                                }
                            bool bad = worst > 0.15f;
                            if (bad) over++;
                            if (bad || !drawn || (wpAt >= 0 && Mathf.Abs(wi - wpAt) <= 10))
                                sb.AppendLine($"  {id} boulder wp {wi} {(sd > 0 ? "R" : "L")} {Mathf.Abs(lat) - half:0.0} m past the edge, at {t.position.x:0.0},{t.position.y:0.0},{t.position.z:0.0}: " +
                                              $"drawn {(drawn ? dm : "NO - no MeshForest stone within 5 cm")}; hull in front of the drawn stone by up to {worst:0.00} m (at {worstH:0.0} m, {hits} rays meet the hull)");
                        }
                    }
                    finally
                    {
                        Physics.queriesHitBackfaces = hb;
                        foreach (var g in temp) Object.DestroyImmediate(g);
                    }
                    sb.AppendLine($"{id}: {n} boulders within {maxE:0} m of the edge; not drawn {undrawn}; hull > 0.15 m in front of its drawn stone {over}; stones drawn near the road {instAt.Count}");
                }
            }
            catch (System.Exception ex) { sb.AppendLine("THREW " + ex); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_boulders.txt"), sb.ToString());
            Debug.Log("[BF] " + sb);
            if (Application.isBatchMode) EditorApplication.Exit(0);
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

        /// <summary>The timber rail from the road (proof sheet): PSX_BF_RAIL = "Venue:wp:L|R" - an eye 1.0 m up in the
        /// lane beside the rail, one frame 2 m from the rail looking along it, one 20 m back. PNGs in
        /// PSXRacing_bfshots/ (PSX_BF_TAG in the name).</summary>
        public static void RailShots()
        {
            var sb = new StringBuilder();
            string tag = System.Environment.GetEnvironmentVariable("PSX_BF_TAG") ?? "x";
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bfshots");
            Directory.CreateDirectory(outDir);
            try
            {
                foreach (var spec in (System.Environment.GetEnvironmentVariable("PSX_BF_RAIL") ?? "").Split('|'))
                {
                    var f = spec.Trim().Split(':');
                    if (f.Length < 3 || !int.TryParse(f[1], out int wp)) continue;
                    string scenePath = "Assets/PSXRacing/Scenes/" + f[0] + ".unity";
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != scenePath)
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var path = Object.FindFirstObjectByType<TrackPath>();
                    float sd = f[2].Trim().ToUpperInvariant() == "L" ? -1f : 1f;
                    if (wp < 0)
                    {
                        // the first timber rail drawn: its middle vertex, the waypoint nearest it and its side
                        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                        {
                            int si = System.Array.FindIndex(mr.sharedMaterials, m => m != null && m.name.Contains("TimberRail"));
                            var mf = mr.GetComponent<MeshFilter>();
                            if (si < 0 || mf == null || mf.sharedMesh == null) continue;
                            var tri = mf.sharedMesh.GetTriangles(si);
                            if (tri.Length == 0) continue;
                            Vector3 v = mr.transform.TransformPoint(mf.sharedMesh.vertices[tri[tri.Length / 2]]);
                            wp = path.NearestIndex(v);
                            Vector3 tt = path.GetTangent(wp);
                            sd = Vector3.Dot(v - path.GetPoint(wp), Vector3.Cross(Vector3.up, tt)) >= 0f ? 1f : -1f;
                            f[2] = sd > 0f ? "R" : "L";
                            break;
                        }
                        if (wp < 0) { sb.AppendLine("no timber rail in " + f[0]); continue; }
                    }
                    float half = path.roadWidth * 0.5f;
                    foreach (float dist in new[] { 2f, 20f })
                    {
                        Vector3 p = path.GetPoint(wp), t = path.GetTangent(wp); t.y = 0f; t.Normalize();
                        Vector3 r = Vector3.Cross(Vector3.up, t).normalized * sd;
                        Vector3 rail = p + r * (half + 0.6f) + Vector3.up * 0.6f;
                        Vector3 eye = dist < 5f ? p + r * (half - 1.4f) - t * 1.4f + Vector3.up * 1.0f
                                                : p + r * (half - 1.4f) - t * dist + Vector3.up * 1.0f;
                        Vector3 look = dist < 5f ? rail + t * 2.5f : rail + t * 2f;
                        const int W = 960, H = 540;
                        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                        var camGo = new GameObject("BFRailCam") { hideFlags = HideFlags.HideAndDontSave };
                        var cam = camGo.AddComponent<Camera>();
                        cam.enabled = false; cam.targetTexture = rt; cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
                        cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false);
                        RenderTexture.active = null;
                        string fn = $"rail_{f[0]}_{wp}{f[2].Trim()}_{dist:0}m_{tag}.png";
                        File.WriteAllBytes(Path.Combine(outDir, fn), tex.EncodeToPNG());
                        Object.DestroyImmediate(camGo); rt.Release();
                        sb.AppendLine("rail " + fn);
                    }
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bfshots.txt"), sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        struct Spot { public int wp; public float gap, h; public string coll; public Vector3 at; }

        static string Owner(Collider c, Dictionary<Collider, string> owner) =>
            c != null && owner.TryGetValue(c, out string o) ? o : (c != null ? c.name : "-");

        /// <summary>A name without its numbers and venue: "BankLip12 [StageBankLip12]" -> "BankLip".</summary>
        static string Kind(string n)
        {
            if (string.IsNullOrEmpty(n)) return "-";
            int b = n.IndexOf(' '); if (b > 0) n = n.Substring(0, b);
            int u = n.IndexOf('_'); if (u > 0 && u < n.Length - 1 && !char.IsDigit(n[u + 1])) n = n.Substring(u + 1);
            var o = new StringBuilder();
            foreach (char ch in n) if (!char.IsDigit(ch) && ch != '_') o.Append(ch);
            return o.Length == 0 ? n : o.ToString();
        }

        public static string One(string id)
        {
            var sb = new StringBuilder();
            string scenePath = "Assets/PSXRacing/Scenes/" + id + ".unity";
            if (!File.Exists(scenePath)) return "bankfit " + id + ": NO SCENE";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count == 0) return "bankfit " + id + ": no TrackPath";
            string solidSet = ApplySolidSet();

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
            // (stage4) THE ROCK MESHFOREST DRAWS: the outcrops (Meadow cliffs) and talus stones PlaceCliffs sets into
            // the cuts have no MeshFilter in the scene - they are instances in MeshForest's table, drawn at run time.
            // Drawn rock all the same: each one near a cut solid as a temporary collider ("BF_BankCliff",
            // "BF_BankStone"), as MeshForest places it (LocalOf). A boulder with its own solid is left out.
            int rockInst = 0;
            {
                var boulderAt = new List<Vector3>();
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name == "Boulder" && t.parent != null && t.parent.name == "Boulders") boulderAt.Add(t.position);
                var nearAll = new Bounds(all.center, all.size + Vector3.one * 6f);
                foreach (var mfo in Object.FindObjectsByType<MeshForest>(FindObjectsSortMode.None))
                {
                    if (mfo.inst == null || mfo.variants == null || mfo.instScaleVar == null) continue;
                    var kindOf = new string[mfo.variants.Length];
                    for (int v = 0; v < mfo.variants.Length; v++)
                    {
                        var vv = mfo.variants[v];
                        var m0 = vv != null && vv.lods != null && vv.lods.Length > 0 ? vv.lods[0] : null;
                        string nm = m0 != null ? m0.name.ToLowerInvariant() : "";
                        kindOf[v] = nm.Contains("cliff") ? "Cliff" : (nm.Contains("stone") || nm.Contains("rock") || nm.Contains("boulder")) ? "Stone" : null;
                    }
                    var l2w = mfo.transform.localToWorldMatrix;
                    for (int i = 0; i < mfo.inst.Length && i < mfo.instScaleVar.Length; i++)
                    {
                        int v = (int)mfo.instScaleVar[i].y;
                        if (v < 0 || v >= kindOf.Length || kindOf[v] == null) continue;
                        Vector3 lp = new Vector3(mfo.inst[i].x, mfo.inst[i].y, mfo.inst[i].z);
                        Vector3 wp = l2w.MultiplyPoint3x4(lp);
                        if (!nearAll.Contains(wp)) continue;
                        bool nearS = false;
                        foreach (var s in solids) if (new Bounds(s.bounds.center, s.bounds.size + Vector3.one * 4f).Contains(wp)) { nearS = true; break; }
                        if (!nearS) continue;
                        bool isBoulder = false;
                        foreach (var b in boulderAt) if ((b - wp).sqrMagnitude < 0.09f) { isBoulder = true; break; }
                        if (isBoulder) continue;
                        var vr = mfo.variants[v];
                        float sc = mfo.instScaleVar[i].x;
                        var tl = mfo.instTilt != null && i < mfo.instTilt.Length ? mfo.instTilt[i] : Vector2.zero;
                        var go = new GameObject("BF_Bank" + kindOf[v]) { layer = ProbeLayer, hideFlags = HideFlags.DontSave };
                        go.transform.SetParent(mfo.transform, false);
                        go.transform.localPosition = lp;
                        go.transform.localRotation = Quaternion.Euler(tl.x, 0f, tl.y) * Quaternion.Euler(0f, mfo.inst[i].w, 0f);
                        go.transform.localScale = new Vector3(sc, sc, sc);
                        var hull = new GameObject("BF_Bank" + kindOf[v]) { layer = ProbeLayer, hideFlags = HideFlags.DontSave };
                        hull.transform.SetParent(go.transform, false);
                        hull.transform.localPosition = vr.pre.GetColumn(3);
                        hull.transform.localRotation = vr.pre.rotation;
                        hull.transform.localScale = vr.pre.lossyScale;
                        var hc = hull.AddComponent<MeshCollider>();
                        hc.sharedMesh = vr.lods[0];
                        temp.Add(go);
                        tempOwner[hc] = "Bank" + kindOf[v] + " [" + vr.lods[0].name + "]";
                        rockInst++;
                    }
                }
            }
            Physics.SyncTransforms();
            bool hitBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = false;

            float half = path.roadWidth * 0.5f;
            int n = path.Count, chords = path.HasEnds ? n - 1 : n;
            int spots = 0, spotsOver = 0, spotsUnder = 0, samples = 0, sOver = 0, sUnder = 0, sUnderOther = 0;
            int spotsUnder5 = 0, sUnder5 = 0, pocketsAlong = 0, pocketsUnder = 0;
            var under5By = new Dictionary<string, int>();
            var under5List = new List<Spot>();
            // (stage4) the solid's distance along every ray, per side, in walk order: the pocket test
            var dcRows = new List<float[]>[2] { new List<float[]>(), new List<float[]>() };
            var dnRows = new List<string[]>[2] { new List<string[]>(), new List<string[]>() };
            var pocketBy = new Dictionary<string, int>();
            float worst = 0f, worstNeg = 0f;
            var bandOver = new int[3]; var bandN = new int[3]; var hist = new int[5]; int histTop = 0;
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
                            bool spot = false, spotOver = false, spotUnder = false, spotUnder5 = false;
                            float spotWorst = 0f, spotNeg = 0f, hW = 0f, hN = 0f; string cW = null, dN = null, sN = null;
                            var dcRow = new float[15];
                            var dnRow = new string[15];
                            for (int z = 0; z < 15; z++) dcRow[z] = float.NaN;
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
                                dcRow[hi] = dc;
                                dnRow[hi] = hitS.sharedMesh.name;
                                bool hitD = Physics.Raycast(ray, out RaycastHit dh, dc + 3f, mask); float dd = hitD ? dh.distance : dc + 3f;
                                bool rockHit = hitD && dh.collider.name.StartsWith("BF_Bank");
                                float gap = dd - dc;
                                spot = true; samples++;
                                int band = h < 0.65f ? 0 : h < 1.15f ? 1 : 2;
                                bandN[band]++;
                                if (gap > Over) { hist[gap < 0.25f ? 0 : gap < 0.5f ? 1 : gap < 1f ? 2 : gap < 2.99f ? 3 : 4]++; if (hitS.name.StartsWith("BankTop")) histTop++; sOver++; bandOver[band]++; spotOver = true; if (gap > spotWorst) { spotWorst = gap; hW = h; cW = hitS.sharedMesh.name; } }
                                if (gap < Under && !rockHit) sUnderOther++;
                                if (gap < Under && rockHit) { sUnder++; spotUnder = true; if (gap < spotNeg) { spotNeg = gap; hN = h; dN = Owner(dh.collider, tempOwner); sN = hitS.sharedMesh.name; } }
                                if (gap < Under5 && rockHit) { sUnder5++; spotUnder5 = true; }
                            }
                            dcRows[sd > 0 ? 1 : 0].Add(spot ? dcRow : null);
                            dnRows[sd > 0 ? 1 : 0].Add(dnRow);
                            if (!spot) continue;
                            spots++;
                            if (spotUnder5)
                            {
                                spotsUnder5++;
                                string key = Kind(dN) + " vs " + Kind(sN);
                                under5By.TryGetValue(key, out int u5); under5By[key] = u5 + 1;
                                under5List.Add(new Spot { wp = i, gap = spotNeg, h = hN, at = p, coll = dN + " | solid " + sN });
                            }
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
            under5List.Sort((x, y) => x.gap.CompareTo(y.gap));
            // POCKETS in the solid a car meets (0.2-1.0 m over the edge): along = deeper than the solid on BOTH sides
            // within PocketHalfM; under = deeper than the solid up to PocketUpM over it (an overhang)
            int half5 = Mathf.RoundToInt(PocketHalfM / 0.5f), upN = Mathf.RoundToInt(PocketUpM / 0.1f);
            for (int sdI = 0; sdI < 2; sdI++)
            {
                var rows = dcRows[sdI];
                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r]; if (row == null) continue;
                    bool along = false, under = false;
                    string pn = null;
                    for (int hi = 0; hi <= 8; hi++)
                    {
                        float d0 = row[hi]; if (float.IsNaN(d0)) continue;
                        float lo = float.NaN, hiS = float.NaN;
                        for (int o = 1; o <= half5; o++)
                        {
                            if (r - o >= 0 && rows[r - o] != null && !float.IsNaN(rows[r - o][hi])) lo = float.IsNaN(lo) ? rows[r - o][hi] : Mathf.Min(lo, rows[r - o][hi]);
                            if (r + o < rows.Count && rows[r + o] != null && !float.IsNaN(rows[r + o][hi])) hiS = float.IsNaN(hiS) ? rows[r + o][hi] : Mathf.Min(hiS, rows[r + o][hi]);
                        }
                        if (!float.IsNaN(lo) && !float.IsNaN(hiS) && d0 - lo > PocketDepth && d0 - hiS > PocketDepth) { along = true; pn = pn ?? "along " + Kind(dnRows[sdI][r][hi]); }
                        for (int o = 1; o <= upN && hi + o < 15; o++)
                            if (!float.IsNaN(row[hi + o]) && d0 - row[hi + o] > PocketDepth) { under = true; pn = pn ?? "under " + Kind(dnRows[sdI][r][hi]) + " / over it " + Kind(dnRows[sdI][r][hi + o]); }
                    }
                    if (pn != null) { pocketBy.TryGetValue(pn, out int pc); pocketBy[pn] = pc + 1; }
                    if (along) pocketsAlong++;
                    if (under) pocketsUnder++;
                }
            }
            sb.AppendLine($"bankfit {id}: {spots} spots (0.5 m, a side) where a level ray 0.2-1.6 m over the edge meets a cut solid; " +
                          $"held off > {Over:0.00} m: {spotsOver} ({100f * spotsOver / Mathf.Max(1, spots):0.0}%), worst {worst:0.00} m; " +
                          $"cut rock drawn in front of the solid (< {Under:0.00} m): {spotsUnder}, worst {worstNeg:0.00} m; other drawn things in front (rails, walls, plants): {sUnderOther} samples");
            sb.AppendLine($"  [stage4] solid set: {solidSet}; drawn cut rock in front of the solid by > 0.05 m: {spotsUnder5} spots ({100f * spotsUnder5 / Mathf.Max(1, spots):0.00}%), {sUnder5} samples; " +
                          $"pockets (0.2-1.0 m): along {pocketsAlong} spots ({100f * pocketsAlong / Mathf.Max(1, spots):0.00}%), under an overhang {pocketsUnder} ({100f * pocketsUnder / Mathf.Max(1, spots):0.00}%)");
            {
                var ul = new List<KeyValuePair<string, int>>(under5By);
                ul.Sort((x, y) => y.Value.CompareTo(x.Value));
                var pl = new List<KeyValuePair<string, int>>(pocketBy);
                pl.Sort((x, y) => y.Value.CompareTo(x.Value));
                var pb = new StringBuilder("  [stage4] pockets by solid (deeper one / the one over it):");
                for (int k = 0; k < pl.Count && k < 8; k++) pb.Append($" {pl[k].Key} {pl[k].Value};");
                sb.AppendLine(pb.ToString());
                var u5 = new StringBuilder("  [stage4] > 0.05 m in front, by drawn vs solid:");
                for (int k = 0; k < ul.Count && k < 10; k++) u5.Append($" {ul[k].Key} {ul[k].Value};");
                sb.AppendLine(u5.ToString());
                for (int k = 0; k < under5List.Count && k < 12; k++)
                {
                    var s5 = under5List[k];
                    sb.AppendLine($"    in front {s5.gap:0.00} m at {s5.h:0.0} m  wp {s5.wp}  {s5.coll}  at {s5.at.x:0.0},{s5.at.y:0.0},{s5.at.z:0.0}");
                }
            }
            sb.AppendLine($"  samples {samples}: over {sOver}, under {sUnder}; over by band 0.2-0.6 {bandOver[0]}/{bandN[0]}, 0.7-1.1 {bandOver[1]}/{bandN[1]}, 1.2-1.6 {bandOver[2]}/{bandN[2]}");
            sb.AppendLine($"  over samples by gap: 0.15-0.25 {hist[0]}, 0.25-0.5 {hist[1]}, 0.5-1 {hist[2]}, 1-3 {hist[3]}, nothing drawn within 3 m {hist[4]}; on the rock top's face collider {histTop}");
            sb.AppendLine($"  cut solids (BankColl): {cutN} MeshColliders, {cutTris} triangles; with the rock tops' colliders {solids.Count} / {solidTris}; cooked in {cookMs:0} ms (editor); drawn meshes asked: {temp.Count} (of them outcrops and talus stones drawn by MeshForest: {rockInst})");
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
