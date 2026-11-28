using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CONIFER ROUND'S INSTRUMENTS (agent "conifers", 2026-11-27): the Codex conifer delivery against its brief,
    /// one stage built with PSX_CONIFER_MODELS, and that stage's conifers in the game - beside an NM beech, from the
    /// road (models | their old cards, MeshForest.ConiferCardsAB), the level-of-detail swaps on an approach, the
    /// feet, the road clearance, and the cost in alternating blocks in one process.
    ///   CS_OUT = output folder; CS_STAGE = the stage id (BeechGap)
    ///   Unity.exe -batchmode -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.ConiferShots.BuildStage -quit
    ///   Unity.exe -batchmode -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.ConiferShots.Run (no -quit)
    /// </summary>
    public static class ConiferShots
    {
        internal static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
        internal static string OutDir => Env("CS_OUT", Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "conifers"));
        internal static string Stage => Env("CS_STAGE", "BeechGap");
        const string Dir = "Assets/MountainConifers";
        internal static readonly Dictionary<string, Vector3[]> FoliageVerts = new Dictionary<string, Vector3[]>();

        /// <summary>The files against the brief, then the one stage built with the switch.</summary>
        public static void BuildStage()
        {
            Directory.CreateDirectory(OutDir);
            try { Measure(); } catch (System.Exception e) { File.WriteAllText(Path.Combine(OutDir, "measure_error.txt"), e.ToString()); }
            var sb = new StringBuilder();
            try
            {
                TrackCatalog.TrackDef def = null;
                foreach (var d in TrackCatalog.Scened) if (d.id == Stage) def = d;
                if (def == null) sb.AppendLine("no venue " + Stage);
                else
                {
                    var t0 = System.DateTime.Now;
                    string path = PSXRacingBuilder.BuildOneForLab(def);
                    string root = Path.GetDirectoryName(Application.dataPath);
                    string blog = Path.Combine(root, "PSXRacing_lab_build_log.txt");
                    if (File.Exists(blog)) File.Copy(blog, Path.Combine(OutDir, "buildlog_" + Stage + ".txt"), true);
                    sb.AppendLine($"built {Stage} -> {path} in {(System.DateTime.Now - t0).TotalMinutes:0.0} min, PSX_CONIFER_MODELS={System.Environment.GetEnvironmentVariable("PSX_CONIFER_MODELS")}");
                }
            }
            catch (System.Exception e) { sb.AppendLine("BUILD EXCEPTION " + e); }
            File.WriteAllText(Path.Combine(OutDir, "build_report.txt"), sb.ToString());
            Debug.Log("[ConiferShots] " + sb);
        }

        // ------------------------------------------------------------------
        //  THE FILES AGAINST THE BRIEF
        // ------------------------------------------------------------------
        public static void Measure()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CONIFER DELIVERY, MEASURED IN UNITY (" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            var atlasLabels = new Dictionary<string, int[]>();
            var atlasIslands = new Dictionary<string, int>();
            const int G = 512;
            int[] Labels(string texPath, out int count)
            {
                count = 0;
                if (atlasLabels.TryGetValue(texPath, out var have)) { count = atlasIslands[texPath]; return have; }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), texPath)));
                var px = t.GetPixels32();
                int W = t.width, H = t.height, k = W / G;
                // atlas stats: coverage, V (sRGB max channel) of the alpha >= 0.5 texels
                long cov = 0, dark = 0; double sv = 0, sv2 = 0;
                var occ = new bool[G * G];
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    if (c.a < 128) continue;
                    cov++;
                    float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
                    sv += v; sv2 += v * v; if (v < 0.25f) dark++;
                    int x = (i % W) / k, y = (i / W) / k;
                    occ[y * G + x] = true;
                }
                double mean = cov > 0 ? sv / cov : 0, sd = cov > 0 ? System.Math.Sqrt(System.Math.Max(0, sv2 / cov - mean * mean)) : 0;
                sb.AppendLine($"ATLAS {Path.GetFileName(texPath)}: {W}x{H}, coverage {100.0 * cov / px.Length:0.0} %, mean V {mean:0.000}, V<0.25 {100.0 * dark / System.Math.Max(1, cov):0.0} %, V SD {sd:0.000}");
                // islands (4-connected, at 512)
                var lab = new int[G * G];
                var stack = new Stack<int>();
                for (int i = 0; i < occ.Length; i++)
                {
                    if (!occ[i] || lab[i] != 0) continue;
                    count++; int id = count; lab[i] = id; stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int q = stack.Pop(), qx = q % G, qy = q / G;
                        for (int dir = 0; dir < 4; dir++)
                        {
                            int nx = qx + (dir == 0 ? 1 : dir == 1 ? -1 : 0), ny = qy + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= G || ny >= G) continue;
                            int n = ny * G + nx;
                            if (occ[n] && lab[n] == 0) { lab[n] = id; stack.Push(n); }
                        }
                    }
                }
                Object.DestroyImmediate(t);
                atlasLabels[texPath] = lab; atlasIslands[texPath] = count;
                return lab;
            }

            sb.AppendLine("form | H x W m | tris L0/L1/L2 (bark+foliage) | cards L0 | mean card m2 | area/plan | area/side-tri | islands used/atlas | vcol L0/L1/L2 | uv2 L0/L1/L2 | foliage R min/mean | bark R mean | foot y L0/L1/L2 (bark min) | trunk centre at y=0 L0/L1/L2 | foliage area L1/L0, L2/L0 | edge: foliage verts <4.6 m within 3 m, -X / +X | nodes | materials");
            foreach (var fbxPath in Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Application.dataPath), Dir, "Models"), "*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(fbxPath);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Models/" + name + ".fbx");
                if (asset == null) { sb.AppendLine(name + ": NOT IMPORTED"); continue; }
                var lods = new MeshFilter[3];
                var nodes = new List<string>();
                foreach (var mf in asset.GetComponentsInChildren<MeshFilter>(true))
                {
                    nodes.Add(mf.name);
                    for (int l = 0; l < 3; l++) if (mf.name.EndsWith("_LOD" + l)) lods[l] = mf;
                }
                var mats = new List<string>();
                if (lods[0] != null) foreach (var m in lods[0].GetComponent<MeshRenderer>().sharedMaterials) mats.Add(m != null ? m.name : "null");
                string tex = null;
                foreach (var m in mats) if (m.Contains("Needles") || m.Contains("Twigs")) tex = m;
                // the foliage atlas: the needle material's main texture
                string atlasPath = null;
                if (lods[0] != null)
                {
                    var rm = lods[0].GetComponent<MeshRenderer>().sharedMaterials;
                    if (rm.Length > 1 && rm[1] != null)
                    {
                        var mt = rm[1].HasProperty("_BaseMap") ? rm[1].GetTexture("_BaseMap") : rm[1].mainTexture;
                        if (mt == null && rm[1].HasProperty("_MainTex")) mt = rm[1].GetTexture("_MainTex");
                        if (mt != null) atlasPath = AssetDatabase.GetAssetPath(mt);
                    }
                }
                if (atlasPath == null)
                {
                    string sp = name.Split('_')[0];
                    atlasPath = name.EndsWith("Snag") ? Dir + "/Textures/Dead_Twigs_overhaul1_hd.png" : Dir + "/Textures/" + sp + "_Needles_overhaul1_hd.png";
                }
                int[] lab = null; int islands = 0;
                try { lab = Labels(atlasPath, out islands); } catch (System.Exception e) { sb.AppendLine("  atlas " + atlasPath + ": " + e.Message); }

                var tri = new string[3]; var vcol = new string[3]; var uv2 = new string[3]; var foot = new string[3]; var centre = new string[3];
                float[] folArea = new float[3];
                int cards = 0; float meanCard = 0f, ratioPlan = 0f, ratioSide = 0f, H = 0f, Wd = 0f, rMin = 1f, rMean = 0f, barkR = 0f;
                int usedIslands = 0, edgeNeg = 0, edgePos = 0;
                for (int l = 0; l < 3; l++)
                {
                    if (lods[l] == null) { tri[l] = "-"; continue; }
                    var mesh = lods[l].sharedMesh;
                    var M = lods[l].transform.localToWorldMatrix;
                    using (var arr = Mesh.AcquireReadOnlyMeshData(mesh))
                    {
                        var md = arr[0];
                        int vc = md.vertexCount;
                        var vs = new NativeArray<Vector3>(vc, Allocator.Temp); md.GetVertices(vs);
                        var uv = new NativeArray<Vector2>(vc, Allocator.Temp); md.GetUVs(0, uv);
                        bool hasC = md.HasVertexAttribute(VertexAttribute.Color), hasU2 = md.HasVertexAttribute(VertexAttribute.TexCoord1);
                        var col = new NativeArray<Color>(vc, Allocator.Temp); if (hasC) md.GetColors(col);
                        vcol[l] = hasC ? "yes" : "NO"; uv2[l] = hasU2 ? "yes" : "NO";
                        int subs = md.subMeshCount;
                        var parts = new List<string>();
                        float barkMinY = float.MaxValue; double cx = 0, cz = 0; int cn = 0;
                        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, minFY = float.MaxValue, maxY = float.MinValue;
                        var used = new HashSet<int>();
                        double rs = 0; int rn = 0; double brs = 0; int brn = 0;
                        for (int s = 0; s < subs; s++)
                        {
                            var idx = new NativeArray<int>(md.GetSubMesh(s).indexCount, Allocator.Temp);
                            md.GetIndices(idx, s);
                            parts.Add((idx.Length / 3).ToString());
                            bool foliage = s == 1;
                            for (int t = 0; t + 2 < idx.Length; t += 3)
                            {
                                Vector3 a = M.MultiplyPoint3x4(vs[idx[t]]), b = M.MultiplyPoint3x4(vs[idx[t + 1]]), c = M.MultiplyPoint3x4(vs[idx[t + 2]]);
                                if (!foliage)
                                {
                                    for (int q = 0; q < 3; q++)
                                    {
                                        var p = q == 0 ? a : q == 1 ? b : c;
                                        barkMinY = Mathf.Min(barkMinY, p.y);
                                        if (Mathf.Abs(p.y) < 0.05f) { cx += p.x; cz += p.z; cn++; }
                                        maxY = Mathf.Max(maxY, p.y);
                                    }
                                    if (l == 0 && hasC) { brs += col[idx[t]].r; brn++; }
                                    continue;
                                }
                                folArea[l] += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                                if (l != 0) continue;
                                foreach (var p in new[] { a, b, c })
                                {
                                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                                    minFY = Mathf.Min(minFY, p.y); maxY = Mathf.Max(maxY, p.y);
                                    float rr = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                                    if (rr < 3f && p.y < 4.6f) { if (p.x < 0f) edgeNeg++; else edgePos++; }
                                }
                                if (hasC) { float r0 = col[idx[t]].r; rs += r0; rn++; rMin = Mathf.Min(rMin, r0); }
                                if (lab != null)
                                {
                                    var uc = (uv[idx[t]] + uv[idx[t + 1]] + uv[idx[t + 2]]) / 3f;
                                    int gx = Mathf.Clamp((int)(uc.x * G), 0, G - 1), gy = Mathf.Clamp((int)(uc.y * G), 0, G - 1);
                                    int lb = lab[gy * G + gx];
                                    for (int rad = 1; lb == 0 && rad <= 4; rad++)
                                        for (int dy = -rad; dy <= rad && lb == 0; dy++)
                                            for (int dx = -rad; dx <= rad && lb == 0; dx++)
                                            {
                                                int nx = gx + dx, ny = gy + dy;
                                                if (nx >= 0 && ny >= 0 && nx < G && ny < G) lb = lab[ny * G + nx];
                                            }
                                    if (lb != 0) used.Add(lb);
                                }
                            }
                            if (l == 0 && foliage) cards = idx.Length / 6;
                            idx.Dispose();
                        }
                        tri[l] = string.Join("+", parts);
                        foot[l] = barkMinY.ToString("0.000");
                        centre[l] = cn > 0 ? $"({cx / cn:0.00},{cz / cn:0.00})" : "none";
                        if (l == 0)
                        {
                            H = maxY; Wd = Mathf.Max(maxX - minX, maxZ - minZ);
                            meanCard = cards > 0 ? folArea[0] / cards : 0f;
                            float plan = Mathf.PI * Wd * Wd * 0.25f, side = 0.5f * Wd * Mathf.Max(0.1f, maxY - minFY);
                            ratioPlan = folArea[0] / Mathf.Max(0.01f, plan); ratioSide = folArea[0] / Mathf.Max(0.01f, side);
                            rMean = rn > 0 ? (float)(rs / rn) : 0f; barkR = brn > 0 ? (float)(brs / brn) : 0f;
                            usedIslands = used.Count;
                        }
                        vs.Dispose(); uv.Dispose(); col.Dispose();
                    }
                }
                sb.AppendLine($"{name} | {H:0.0} x {Wd:0.0} | {tri[0]} / {tri[1]} / {tri[2]} | {cards} | {meanCard:0.000} | {ratioPlan:0.00} | {ratioSide:0.00} | {usedIslands}/{islands} | {vcol[0]}/{vcol[1]}/{vcol[2]} | {uv2[0]}/{uv2[1]}/{uv2[2]} | {rMin:0.00}/{rMean:0.00} | {barkR:0.00} | {foot[0]}/{foot[1]}/{foot[2]} | {centre[0]}/{centre[1]}/{centre[2]} | {(folArea[0] > 0 ? folArea[1] / folArea[0] : 0):0.00}, {(folArea[0] > 0 ? folArea[2] / folArea[0] : 0):0.00} | {edgeNeg}/{edgePos} | {string.Join(",", nodes)} | {string.Join(",", mats)}");
            }
            // the owned NM beech beside them (its own atlas through the same counter)
            File.WriteAllText(Path.Combine(OutDir, "measure.txt"), sb.ToString());
            Debug.Log("[ConiferShots] measure:\n" + sb);
        }

        // ------------------------------------------------------------------
        //  THE GAME
        // ------------------------------------------------------------------
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            int vi = TrackCatalog.IndexOf(Stage);
            var scenes = EditorBuildSettings.scenes;
            int s = vi >= 0 ? TrackCatalog.SceneIndex(vi) : -1;
            if (vi < 0 || s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Debug.LogError("[ConiferShots] no built scene for " + Stage);
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            // the conifers' LOD0 needle vertices, read now (edit mode) for the road-clearance check in play
            FoliageVerts.Clear();
            var mfo = Object.FindAnyObjectByType<MeshForest>();
            if (mfo != null && mfo.variants != null)
                foreach (var vr in mfo.variants)
                {
                    if (vr == null || !vr.conifer || vr.lods == null || vr.lods[0] == null) continue;
                    int leaf = System.Array.IndexOf(vr.leafSub, true);
                    if (leaf < 0 || FoliageVerts.ContainsKey(vr.lods[0].name)) continue;
                    using (var arr = Mesh.AcquireReadOnlyMeshData(vr.lods[0]))
                    {
                        var d0 = arr[0];
                        var vs = new NativeArray<Vector3>(d0.vertexCount, Allocator.Temp); d0.GetVertices(vs);
                        var ix = new NativeArray<int>(d0.GetSubMesh(leaf).indexCount, Allocator.Temp); d0.GetIndices(ix, leaf);
                        var used = new HashSet<int>(ix.ToArray());
                        var list = new List<Vector3>(); foreach (int k in used) list.Add(vs[k]);
                        FoliageVerts[vr.lods[0].name] = list.ToArray();
                        vs.Dispose(); ix.Dispose();
                    }
                }
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = false;
            RaceHandoff.TrackIndex = vi;
            RaceHandoff.Solo = true;
            RaceHandoff.TrafficLevel = TrafficLevels.None;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.WeatherOverride = (int)Weather.Clear;
            if (int.TryParse(Env("CS_DAY", "0"), out int day) && day > 0) RaceHandoff.CalendarDay = day;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("ConiferShotsRunner").AddComponent<ConiferShotsRunner>();
        }
    }

    public class ConiferShotsRunner : MonoBehaviour
    {
        readonly StringBuilder rep = new StringBuilder();
        float hardStop;
        int muted;
        string outDir;
        MeshForest forest;
        TrackPath path;
        Light sun;
        PSXGlobals globals;
        RainLookView view;
        Texture2D tex;
        const int W = 1280, H = 720;
        LabRow leafRow;
        Camera cam;
        CarController car;

        void Update() { if (hardStop > 0f && Time.realtimeSinceStartup > hardStop) { Say("TIMEOUT"); Flush(); EditorApplication.Exit(1); } }
        void Say(string s) { rep.AppendLine(s); Debug.Log("[ConiferShots] " + s); }
        void Flush() { try { File.WriteAllText(Path.Combine(outDir, "report.txt"), rep.ToString()); } catch { } }
        void OnLog(string msg, string stack, LogType type) { if (msg != null && msg.Contains("[MasterOut] test run: the sound is muted")) muted++; if (type == LogType.Exception && !failed && stack != null && stack.Contains("ConiferShots")) { failed = true; Say("EXCEPTION: " + msg + " " + stack); Flush(); EditorApplication.Exit(1); } }
        bool failed;
        void Awake() { Application.logMessageReceived += OnLog; }

        IEnumerator Settle() { for (int i = 0; i < 4; i++) yield return null; float t1 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t1 < 0.25f) yield return null; }
        Color32[] GrabPx()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = view.blit && view.blitMat != null ? view.screen : view.rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); tex.Apply(false);
            RenderTexture.active = prev;
            return tex.GetPixels32();
        }
        void SaveJpg(string name) { File.WriteAllBytes(Path.Combine(outDir, name + ".jpg"), tex.EncodeToJPG(88)); }
        void Hour(int h) { if (sun != null) TimeOfDay.Apply(h, sun); if (globals != null) globals.Apply(); }
        static string HourName(int h) => h == TimeOfDay.Noon ? "Noon" : h == TimeOfDay.Dusk ? "Dusk" : h == TimeOfDay.Night ? "Night" : "H" + h;

        Matrix4x4 InstMatrix(int i)
        {
            var p = forest.inst[i]; float s = forest.instScaleVar[i].x;
            return forest.transform.localToWorldMatrix * Matrix4x4.TRS(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, p.w, 0f), new Vector3(s, s, s))
                   * forest.variants[(int)forest.instScaleVar[i].y].pre;
        }
        Vector3 InstPos(int i) => forest.transform.localToWorldMatrix.MultiplyPoint3x4((Vector3)forest.inst[i]);
        string VName(int v) { var m = forest.variants[v].lods[0]; return m != null ? m.name.Replace("_LOD0", "") : "v" + v; }

        int NearestPath(Vector3 p, out float lateral)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < path.Count; i += 3) { float d = (path.GetPoint(i) - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            for (int i = Mathf.Max(0, best - 3); i <= Mathf.Min(path.Count - 1, best + 3); i++) { float d = (path.GetPoint(i) - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            var q = path.GetPoint(best); lateral = new Vector2(p.x - q.x, p.z - q.z).magnitude;
            return best;
        }

        float GroundY(Vector3 p, float fallback)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 300f, Vector3.down, 700f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.attachedRigidbody != null) continue;
                var n = h.collider.name.ToLowerInvariant();
                if (n.Contains("trunk") || n.Contains("tree")) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            hardStop = Time.realtimeSinceStartup + 1800f;
            outDir = ConiferShots.OutDir;
            Directory.CreateDirectory(outDir);
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 200f;)
            {
                if (RaceManager.Instance != null && RaceManager.Instance.playerCar != null) car = RaceManager.Instance.playerCar;
                if (car != null && Camera.main != null) break;
                yield return null;
            }
            forest = FindAnyObjectByType<MeshForest>();
            path = FindFirstObjectByType<TrackPath>();
            if (car == null || Camera.main == null || forest == null || path == null) { Say("FAIL: car / camera / forest / path missing"); Flush(); EditorApplication.Exit(1); yield break; }
            cam = Camera.main;
            sun = GameObject.Find("Sun")?.GetComponent<Light>();
            globals = FindFirstObjectByType<PSXGlobals>();
            foreach (var r in LabSwitches.Rows()) if (r.id == "leaflight") leafRow = r;
            var pin = car.GetComponent<PlayerCarInput>(); if (pin != null) { pin.inputEnabled = false; pin.enabled = false; }
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 8f;) { car.throttleInput = 0f; car.brakeInput = 1f; yield return null; }

            // the conifers in the scene
            var count = new Dictionary<int, int>();
            var conInst = new List<int>();
            int total = forest.inst.Length;
            for (int i = 0; i < total; i++)
            {
                int v = (int)forest.instScaleVar[i].y;
                if (v < 0 || v >= forest.variants.Length || forest.variants[v] == null || !forest.variants[v].conifer) continue;
                conInst.Add(i);
                count[v] = (count.TryGetValue(v, out int c) ? c : 0) + 1;
            }
            Say($"CONIFERS {ConiferShots.Stage}: {conInst.Count} conifer models of {total} plants; LOD {forest.lod0To:0}/{forest.lod1To:0}/{forest.meshRange:0} m, far to {forest.farRange:0} m, bands {forest.band0:0}/{forest.band1:0}/{forest.band2:0}; built {MeshForest.ConiferModelsBuilt}; A/B card chunks {(forest.coniferCardsAB != null ? forest.coniferCardsAB.Length : 0)}");
            foreach (var kv in count) Say($"  {VName(kv.Key)}: {kv.Value}, height {forest.variants[kv.Key].height:0.0} m, far card {(forest.variants[kv.Key].lods.Length > 3 && forest.variants[kv.Key].lods[3] != null ? "yes" : "NO")}");
            if (conInst.Count == 0) { Say("FAIL: no conifer models in this scene (built without PSX_CONIFER_MODELS?)"); Flush(); EditorApplication.Exit(1); yield break; }

            // the view
            var holder = new GameObject("ConiferView"); holder.SetActive(false);
            view = holder.AddComponent<RainLookView>();
            view.cam = cam;
            view.rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1 }; view.rt.Create();
            view.screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1 }; view.screen.Create();
            var outp = cam.GetComponent<PSXCameraOutput>();
            if (outp != null && outp.display != null) { view.blitMat = outp.display.material; view.blit = true; }
            holder.SetActive(true);
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            yield return Checks(conInst);
            Flush();
            if (ConiferShots.Env("CS_ROAD", "1") == "1") yield return RoadPoses(conInst);
            Flush();
            if (ConiferShots.Env("CS_APPROACH", "1") == "1") yield return Approach(conInst, count);
            Flush();
            if (ConiferShots.Env("CS_COST", "1") == "1") yield return Cost(conInst);
            Flush();
            if (ConiferShots.Env("CS_BESIDE", "1") == "1") yield return Beside(count);
            Say("muted line count: " + muted);
            Flush();
            Time.timeScale = 1f;
            yield return null;
            EditorApplication.Exit(0);
        }

        // ---- the feet on slopes and the road's clearance -------------------
        IEnumerator Checks(List<int> conInst)
        {
            int floatTrunk = 0, flareHigh = 0, checkedN = 0, steep = 0, steepFloat = 0;
            float worstFlare = 0f;
            foreach (int i in conInst)
            {
                var p = InstPos(i); float s = forest.instScaleVar[i].x;
                float r = 0.6f * s;
                float g0 = GroundY(p, float.NaN);
                if (float.IsNaN(g0)) continue;
                float gmin = g0, gmax = g0;
                foreach (var o in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    float g = GroundY(p + o * r, g0); gmin = Mathf.Min(gmin, g); gmax = Mathf.Max(gmax, g);
                }
                checkedN++;
                bool isSteep = (gmax - gmin) / (2f * r) > 0.6f;   // over ~31 degrees
                if (isSteep) steep++;
                float flare = p.y - gmin;                         // the flare's height over the downhill ground
                if (flare > 0.3f) flareHigh++;
                worstFlare = Mathf.Max(worstFlare, flare);
                if (p.y - 0.5f * s > gmin + 0.02f) { floatTrunk++; if (isSteep) steepFloat++; }
            }
            Say($"FEET: {checkedN} conifers checked against the ground's colliders; {steep} on slopes over 31 deg; trunk bottom (origin - 0.5 m x scale) above the downhill ground: {floatTrunk} ({steepFloat} of them steep); flare over 0.3 m above the downhill ground: {flareHigh}; worst flare height {worstFlare:0.00} m");

            // road clearance: LOD0 foliage of every conifer within 20 m of the centre line
            float half = path.roadWidth * 0.5f;
            int nearN = 0, intrude = 0; float lowest = 99f;

            foreach (int i in conInst)
            {
                var p = InstPos(i);
                int pi = NearestPath(p, out float lat);
                if (lat > 20f) continue;
                nearN++;
                int v = (int)forest.instScaleVar[i].y;
                var vr = forest.variants[v];
                var mesh = vr.lods[0];
                int leaf = System.Array.IndexOf(vr.leafSub, true);
                if (mesh == null || leaf < 0) continue;
                // (read in edit mode before play: a non-readable mesh's data is closed in play mode)
                if (!ConiferShots.FoliageVerts.TryGetValue(mesh.name, out var fv)) continue;
                var md = (v: fv, t: (int[])null);
                var M = InstMatrix(i);
                bool hit = false;
                for (int k = 0; k < md.v.Length; k += 2)
                {
                    var w = M.MultiplyPoint3x4(md.v[k]);
                    int qi = path.NearestIndex(w, pi, 12);
                    var q = path.GetPoint(qi);
                    float latW = new Vector2(w.x - q.x, w.z - q.z).magnitude;
                    if (latW < half && w.y < q.y + 4.6f) { hit = true; lowest = Mathf.Min(lowest, w.y - q.y); }
                }
                if (hit) intrude++;
            }
            Say($"ROAD CLEARANCE: {nearN} conifer models within 20 m of the centre line; {intrude} with LOD0 needles over the tarmac (half width {half:0.0} m) below 4.6 m; lowest such needle {(intrude > 0 ? lowest.ToString("0.00") + " m over the road" : "-")}");
            Say("COLLIDERS: each conifer model stands on the site its trunk collider was entered at (the builder adds the trunk at the same tx, tz as the instance; not measured in play)");
            yield return null;
        }

        // ---- from the road: models | cards -------------------------------
        IEnumerator Pose(Vector3 pos, Quaternion rot, ChaseCamera.View v, string name, int[] hours)
        {
            Time.timeScale = 1f; VolumetricClouds.Paused = false;
            var chase = cam.GetComponent<ChaseCamera>() ?? FindAnyObjectByType<ChaseCamera>();
            if (chase != null) chase.enabled = true;
            ChaseCamera.PreviewView(v);
            for (float t1 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t1 < 3f;) { car.ResetTo(pos, rot, seated: true); car.brakeInput = 1f; yield return null; }
            Time.timeScale = 0f; VolumetricClouds.Paused = true;
            foreach (int h in hours)
            {
                Hour(h); yield return Settle();
                forest.ApplyConiferCards(false); yield return Settle(); yield return Settle();
                GrabPx(); SaveJpg($"EDITOR_ROAD_{name}_{HourName(h)}_MODELS");
                forest.ApplyConiferCards(true); yield return Settle(); yield return Settle();
                GrabPx(); SaveJpg($"EDITOR_ROAD_{name}_{HourName(h)}_CARDS");
                forest.ApplyConiferCards(false);
            }
            Say($"ROAD {name}: car at {pos.x:0},{pos.y:0},{pos.z:0}, view {v}, eye {cam.transform.position.x:0.0},{cam.transform.position.y:0.0},{cam.transform.position.z:0.0}");
        }

        IEnumerator RoadPoses(List<int> conInst)
        {
            // HIS POSE: hood view, a tall conifer close on the right of a left-hand bend
            int bestI = -1, bestTree = -1; float bestScore = float.MaxValue;
            for (int i = 20; i < path.Count - 40; i += 2)
            {
                Vector3 t0 = path.GetTangent(i), t1 = path.GetTangent(i + 12);
                float turn = t0.z * t1.x - t0.x * t1.z;   // < 0: the road turns left
                if (turn > -0.12f) continue;
                var p = path.GetPoint(i);
                var right = Vector3.Cross(Vector3.up, t0).normalized;
                foreach (int k in conInst)
                {
                    var q = InstPos(k);
                    var rel = q - p;
                    float ahead = Vector3.Dot(rel, t0), side = Vector3.Dot(rel, right);
                    if (ahead < 12f || ahead > 35f || side < 5f || side > 12f) continue;
                    float hgt = forest.variants[(int)forest.instScaleVar[k].y].height * forest.instScaleVar[k].x;
                    if (hgt < 14f) continue;
                    float score = side + ahead * 0.2f - hgt * 0.3f + turn * 10f;
                    if (score < bestScore) { bestScore = score; bestI = i; bestTree = k; }
                }
            }
            int[] hours = { TimeOfDay.Noon, TimeOfDay.Dusk };
            if (bestI >= 0)
            {
                var p = path.GetPoint(bestI); var tg = path.GetTangent(bestI); tg.y = 0f; tg.Normalize();
                var right = Vector3.Cross(Vector3.up, tg).normalized;
                var pos = p + right * 1.6f + Vector3.up * 0.45f;
                Say($"HIS POSE: wp {bestI}, tree {VName((int)forest.instScaleVar[bestTree].y)} x{forest.instScaleVar[bestTree].x:0.00} at {InstPos(bestTree)}");
                yield return Pose(pos, Quaternion.LookRotation(tg), ChaseCamera.View.Hood, "his_hood", hours);
                yield return Pose(pos, Quaternion.LookRotation(tg), ChaseCamera.View.Chase, "his_chase", hours);
            }
            else Say("HIS POSE: no left-hand bend with a tall conifer 5-12 m to the right");
            // the densest conifer stretch, chase view
            int denseI = -1, denseN = -1;
            for (int i = 20; i < path.Count - 20; i += 10)
            {
                var p = path.GetPoint(i); int n = 0;
                foreach (int k in conInst) if ((InstPos(k) - p).sqrMagnitude < 80f * 80f) n++;
                if (n > denseN) { denseN = n; denseI = i; }
            }
            if (denseI >= 0)
            {
                var tg = path.GetTangent(denseI); tg.y = 0f; tg.Normalize();
                var pos = path.GetPoint(denseI) + Vector3.Cross(Vector3.up, tg).normalized * 1.6f + Vector3.up * 0.45f;
                Say($"DENSE: wp {denseI}, {denseN} conifer models within 80 m");
                denseAt = denseI;
                yield return Pose(pos, Quaternion.LookRotation(tg), ChaseCamera.View.Chase, "dense_chase", hours);
                yield return Pose(pos, Quaternion.LookRotation(tg), ChaseCamera.View.Hood, "dense_hood", hours);
            }
            // THE OUTLINE AT 50 m, per species: driving height, a tree of that species 45-60 m off the road
            var chase = cam.GetComponent<ChaseCamera>() ?? FindAnyObjectByType<ChaseCamera>();
            var done = new HashSet<string>();
            foreach (int k in conInst)
            {
                int v = (int)forest.instScaleVar[k].y;
                string sp = VName(v).Split('_')[0];
                if (done.Contains(sp) || VName(v).EndsWith("Snag")) continue;
                var q = InstPos(k);
                int pi = NearestPath(q, out float lat);
                if (lat < 40f || lat > 60f) continue;
                done.Add(sp);
                var eye = path.GetPoint(pi) + Vector3.up * 1.2f;
                if (chase != null) chase.enabled = false;
                Time.timeScale = 0f;
                float hgt = forest.variants[v].height * forest.instScaleVar[k].x;
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * hgt * 0.5f - eye, Vector3.up);
                cam.fieldOfView = 55f;
                foreach (int h in new[] { TimeOfDay.Noon, TimeOfDay.Dusk })
                {
                    Hour(h);
                    forest.ApplyConiferCards(false); yield return Settle(); yield return Settle();
                    GrabPx(); SaveJpg($"EDITOR_OUTLINE50_{sp}_{HourName(h)}_MODELS");
                    forest.ApplyConiferCards(true); yield return Settle(); yield return Settle();
                    GrabPx(); SaveJpg($"EDITOR_OUTLINE50_{sp}_{HourName(h)}_CARDS");
                    forest.ApplyConiferCards(false);
                }
                Say($"OUTLINE50 {sp}: {VName(v)} x{forest.instScaleVar[k].x:0.00} ({hgt:0.0} m) {lat:0} m off the road at wp {pi}");
            }
            if (chase != null) chase.enabled = true;
        }
        int denseAt = -1;

        // ---- the swaps: one conifer of each species from 400 m to 5 m ------
        IEnumerator Approach(List<int> conInst, Dictionary<int, int> count)
        {
            var chase = cam.GetComponent<ChaseCamera>() ?? FindAnyObjectByType<ChaseCamera>();
            if (chase != null) chase.enabled = false;
            Time.timeScale = 0f; VolumetricClouds.Paused = true;
            Hour(TimeOfDay.Noon);
            cam.fieldOfView = 55f;
            float[] edges = { forest.lod0To, forest.lod1To, forest.meshRange };
            float[] bands = { forest.band0, forest.band1, forest.band2 };
            var ds = new List<float> { 420, 380, 340, 300, 270, 240, 210, 180, 160, 140, 120, 100, 80, 60, 45, 30, 20, 12 };
            for (int e = 0; e < 3; e++) { ds.Add(edges[e] + 4f); ds.Add(edges[e] - bands[e] * 0.5f); ds.Add(edges[e] - bands[e] - 4f); }
            ds.Sort(); ds.Reverse();
            var done = new HashSet<string>();
            foreach (int k in conInst)
            {
                int v = (int)forest.instScaleVar[k].y;
                string vn = VName(v);
                string sp = vn.Split('_')[0];
                if (done.Contains(sp) || !vn.EndsWith("Mature")) continue;
                var q = InstPos(k);
                int pi = NearestPath(q, out float lat);
                if (lat < 8f || lat > 30f) continue;
                done.Add(sp);
                float hgt = forest.variants[v].height * forest.instScaleVar[k].x;
                var from = path.GetPoint(pi) - q; from.y = 0f; from.Normalize();
                var aim = q + Vector3.up * hgt * 0.5f;
                Say($"APPROACH {vn} x{forest.instScaleVar[k].x:0.00} ({hgt:0.0} m), {lat:0} m off the road at wp {pi}; swaps at {edges[0]:0} / {edges[1]:0} / {edges[2]:0} m (bands {bands[0]:0}/{bands[1]:0}/{bands[2]:0})");
                Say("  dist m | level | foliage cover in the tree's box | mean luma of foliage | step cover | step luma");
                float pc = -1f, pl = -1f;
                foreach (float dm in ds)
                {
                    float down = dm > 12f ? 10f : 0f;
                    var pos = aim + from * dm * Mathf.Cos(down * Mathf.Deg2Rad) + Vector3.up * dm * Mathf.Sin(down * Mathf.Deg2Rad);
                    cam.transform.position = pos;
                    cam.transform.rotation = Quaternion.LookRotation(aim - pos, Vector3.up);
                    yield return Settle(); yield return Settle();
                    var col = GrabPx();
                    bool keep = dm <= 20f || System.Array.Exists(edges, x => Mathf.Abs(x - dm) < bands[0] + 6f);
                    if (keep) SaveJpg($"EDITOR_AP_{sp}_{(int)dm:000}");
                    Shader.SetGlobalFloat("_PSXIdMask", 1f); yield return Settle();
                    var id = GrabPx();
                    Shader.SetGlobalFloat("_PSXIdMask", 0f);
                    // the tree's box on screen
                    Vector3 a = cam.WorldToScreenPoint(q - Vector3.up * 0.5f), b = cam.WorldToScreenPoint(q + Vector3.up * (hgt + 0.5f));
                    float hpx = Mathf.Abs(b.y - a.y), cxp = (a.x + b.x) * 0.5f, wpx = hpx * 0.45f;
                    int x0 = Mathf.Clamp((int)(cxp - wpx * 0.5f), 0, W - 1), x1 = Mathf.Clamp((int)(cxp + wpx * 0.5f), 0, W - 1);
                    int y0 = Mathf.Clamp((int)Mathf.Min(a.y, b.y), 0, H - 1), y1 = Mathf.Clamp((int)Mathf.Max(a.y, b.y), 0, H - 1);
                    int n = 0, f = 0; double l = 0;
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int ix = y * W + x; n++;
                            if (id[ix].r > 127) { f++; var c = col[ix]; l += (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255.0; }
                        }
                    float cov = n > 0 ? (float)f / n : 0f, lum = f > 0 ? (float)(l / f) : 0f;
                    string level = dm < edges[0] ? "LOD0" : dm < edges[1] ? "LOD1" : dm < edges[2] ? "LOD2" : "FAR CARD";
                    Say($"  {dm,6:0} | {level,-8} | {cov:0.000} | {lum:0.000} | {(pc < 0 ? "-" : (cov - pc).ToString("+0.000;-0.000"))} | {(pl < 0 ? "-" : (lum - pl).ToString("+0.000;-0.000"))}  ({n} px)");
                    pc = cov; pl = lum;
                }
            }
            if (chase != null) chase.enabled = true;
        }

        // ---- the cost: cards | models, alternating blocks, one process ----
        IEnumerator Cost(List<int> conInst)
        {
            int reps = int.TryParse(ConiferShots.Env("CS_REPS", "6"), out int r) ? r : 6;
            Time.timeScale = 1f; VolumetricClouds.Paused = false;
            var chase = cam.GetComponent<ChaseCamera>() ?? FindAnyObjectByType<ChaseCamera>();
            if (chase != null) chase.enabled = true;
            Hour(TimeOfDay.Noon);
            int at = denseAt >= 0 ? denseAt : path.Count / 2;
            var tg = path.GetTangent(at); tg.y = 0f; tg.Normalize();
            var pos = path.GetPoint(at) + Vector3.Cross(Vector3.up, tg).normalized * 1.6f + Vector3.up * 0.45f;
            foreach (var vw in new[] { ChaseCamera.View.Chase, ChaseCamera.View.Hood })
            {
                ChaseCamera.PreviewView(vw);
                for (float t1 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t1 < 3f;) { car.ResetTo(pos, Quaternion.LookRotation(tg), seated: true); car.brakeInput = 1f; yield return null; }
                var aMs = new List<float>(); var bMs = new List<float>();
                var aTri = new List<long>(); var bTri = new List<long>(); var aDc = new List<int>(); var bDc = new List<int>();
                for (int k = 0; k < reps; k++)
                    for (int side = 0; side < 2; side++)
                    {
                        bool models = (k + side) % 2 == 0;
                        forest.ApplyConiferCards(!models);
                        for (int i = 0; i < 6; i++) { car.ResetTo(pos, Quaternion.LookRotation(tg), seated: true); yield return null; }
                        for (int i = 0; i < 16; i++)
                        {
                            view.measure = true;
                            car.ResetTo(pos, Quaternion.LookRotation(tg), seated: true);
                            yield return null;
                            (models ? aMs : bMs).Add(view.lastMs);
                            (models ? aTri : bTri).Add(UnityStats.triangles);
                            (models ? aDc : bDc).Add(UnityStats.drawCalls);
                        }
                        view.measure = false;
                    }
                forest.ApplyConiferCards(false);
                float Mean(List<float> l) { double s = 0; foreach (var x in l) s += x; return l.Count > 0 ? (float)(s / l.Count) : 0f; }
                float Med(List<float> l) { l.Sort(); return l.Count > 0 ? l[l.Count / 2] : 0f; }
                double MeanL(List<long> l) { double s = 0; foreach (var x in l) s += x; return l.Count > 0 ? s / l.Count : 0; }
                double MeanI(List<int> l) { double s = 0; foreach (var x in l) s += x; return l.Count > 0 ? s / l.Count : 0; }
                Say($"COST {vw} at wp {at}: MODELS {Mean(aMs):0.000} ms (median {Med(aMs):0.000}), {MeanL(aTri) / 1e6:0.00} M tris, {MeanI(aDc):0} draws | CARDS {Mean(bMs):0.000} ms (median {Med(bMs):0.000}), {MeanL(bTri) / 1e6:0.00} M tris, {MeanI(bDc):0} draws | MODELS-CARDS {Mean(aMs) - Mean(bMs):+0.000;-0.000} ms ({reps} x 2 blocks of 16 frames, render + GPU wait of the main view at {W}x{H}; editor stats count every camera)");
            }
            Time.timeScale = 0f;
        }

        // ---- beside an NM beech: 3, 8, 30, 80 m, noon / dusk / night, SUN AND SHADE off / on ----
        IEnumerator Beside(Dictionary<int, int> count)
        {
            var chase = cam.GetComponent<ChaseCamera>() ?? FindAnyObjectByType<ChaseCamera>();
            if (chase != null) chase.enabled = false;
            Time.timeScale = 0f; VolumetricClouds.Paused = true;
            cam.fieldOfView = 55f;
            // the beech: the brief's beech_tree_00_A, and a canopy beech for scale
            int beechA = -1, beechBig = -1;
            for (int v = 0; v < forest.variants.Length; v++)
            {
                var m = forest.variants[v]?.lods[0]; if (m == null) continue;
                if (m.name.Contains("beech_tree_00_A") && beechA < 0) beechA = v;
                if (m.name.Contains("beech_tree_02") && beechBig < 0) beechBig = v;
            }
            var forms = new List<int>();
            for (int v = 0; v < forest.variants.Length; v++) if (forest.variants[v] != null && forest.variants[v].conifer) forms.Add(v);
            Say($"BESIDE: {forms.Count} forms; beech_tree_00_A variant {beechA} ({(beechA >= 0 ? forest.variants[beechA].height : 0):0.0} m), canopy beech_tree_02 {beechBig} ({(beechBig >= 0 ? forest.variants[beechBig].height : 0):0.0} m)");
            // the spot: 14 m right of the road at 30 % of the stage, on its own slope; the forest hidden
            int at = path.Count * 3 / 10;
            var tg = path.GetTangent(at); tg.y = 0f; tg.Normalize();
            var right = Vector3.Cross(Vector3.up, tg).normalized;
            var P = path.GetPoint(at) + right * 14f; P.y = GroundY(P, P.y);
            var hidden = new List<Renderer>();
            foreach (var rr in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                if (rr.enabled && rr.gameObject.layer == 10 && rr.GetComponentInParent<CarController>() == null) { rr.enabled = false; hidden.Add(rr); }
            forest.enabled = false;
            yield return Settle();
            int dress = Mathf.Clamp(SeasonDress.AppliedDress, 0, 4);
            GameObject Spawn(int v, Vector3 at2, float yaw, float scale)
            {
                var vr = forest.variants[v];
                var root = new GameObject("CS_" + v);
                root.transform.SetPositionAndRotation(at2, Quaternion.Euler(0f, yaw, 0f));
                root.transform.localScale = Vector3.one * scale;
                var child = new GameObject("mesh");
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = vr.pre.GetColumn(3);
                child.transform.localRotation = vr.pre.rotation;
                child.transform.localScale = vr.pre.lossyScale;
                child.AddComponent<MeshFilter>().sharedMesh = vr.lods[0];
                var mr = child.AddComponent<MeshRenderer>();
                var mats = new Material[vr.mats.Length];
                for (int s2 = 0; s2 < mats.Length; s2++) mats[s2] = vr.leafSub[s2] && vr.leafDress != null && vr.leafDress[dress] != null ? vr.leafDress[dress] : vr.mats[s2];
                mr.sharedMaterials = mats;
                return root;
            }
            float[] dists = { 3f, 8f, 30f, 80f };
            int[] hours = { TimeOfDay.Noon, TimeOfDay.Dusk, TimeOfDay.Night };
            foreach (int v in forms)
            {
                string vn = VName(v);
                float hgt = forest.variants[v].height;
                var cg = Spawn(v, P, 200f, 1f);
                var bpos = P + tg * 7f; bpos.y = GroundY(bpos, P.y);
                var bA = beechA >= 0 ? Spawn(beechA, bpos, 30f, 1f) : null;
                var bpos2 = P - tg * 12f; bpos2.y = GroundY(bpos2, P.y);
                var bB = beechBig >= 0 ? Spawn(beechBig, bpos2, 75f, 1f) : null;
                var mid = P + tg * 3.5f;
                foreach (int h in hours)
                {
                    Hour(h);
                    for (int ll = 0; ll < 2; ll++)
                    {
                        if (leafRow != null) LabSwitches.Set(leafRow, ll == 1, false);
                        foreach (float dm in dists)
                        {
                            var eyeXZ = mid - right * dm;
                            var eye = new Vector3(eyeXZ.x, GroundY(eyeXZ, mid.y) + 1.6f, eyeXZ.z);
                            float aimH = dm <= 3f ? 2f : dm <= 8f ? 4.5f : Mathf.Max(hgt, 12f) * 0.45f;
                            cam.transform.position = eye;
                            cam.transform.rotation = Quaternion.LookRotation(new Vector3(mid.x, P.y + aimH, mid.z) - eye, Vector3.up);
                            yield return Settle();
                            GrabPx(); SaveJpg($"EDITOR_BESIDE_{vn}_{HourName(h)}_LL{(ll == 1 ? "ON" : "OFF")}_{(int)dm:00}m");
                        }
                    }
                }
                Destroy(cg); if (bA != null) Destroy(bA); if (bB != null) Destroy(bB);
                yield return null;
            }
            if (leafRow != null) LabSwitches.Set(leafRow, leafRow.Default, false);
            foreach (var rr in hidden) if (rr != null) rr.enabled = true;
            forest.enabled = true;
            Say("BESIDE: done (frames EDITOR_BESIDE_<form>_<hour>_LL<ON|OFF>_<m>m.jpg; the conifer left of centre, beech_tree_00_A right of it, beech_tree_02 at left)");
        }
    }
}
