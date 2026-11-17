using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE APPROACH ON A STAGE (the tree-season round): the driver's eye down the
    /// road toward a place, PSX_TM_FROM (400) to PSX_TM_TO (20) metres of road
    /// short of it, every PSX_TM_STEP (10) m, in each dress asked for - the same
    /// trees all the way in. Each step is shot with the game's own levels (a)
    /// and, with PSX_TM_PAIR=1, with the levels as they stood one step earlier
    /// (b: MeshForest.LodEye) - the pop measure of CityRefSpots.RunTreeFeet's
    /// walk, counted offline off the pairs.
    ///   -executeMethod PSXRacing.EditorTools.TreeMatchAudit.RunStageWalk
    ///   PSX_TM_TAG=..  PSX_TM_VENUE=SwissNC226A  PSX_TM_M=7900,8400  PSX_TM_DRESSES=0,3  PSX_TM_DAY=288 (the weather is forced clear)
    /// Output: Screenshots/treeseason/&lt;tag&gt;/&lt;venue&gt;_&lt;m&gt;_&lt;DRESS&gt;_&lt;d&gt;_a|b.png, treeseason_&lt;tag&gt;_&lt;venue&gt;_log.txt
    /// </summary>
    public static partial class TreeMatchAudit
    {
        /// <summary>Set while RunMany drives the runners below: they leave the editor open for the next.</summary>
        internal static bool NoExit;

        /// <summary>
        /// SEVERAL RUNNERS IN ONE PROCESS (a sandbox shared by three agents: one lock, one editor start, for a whole
        /// set of checks made with the same switches). PSX_TM_RUN = "name|K=V&amp;K=V;name|..." in order; each item's
        /// variables are set before it runs and cleared after. Names: citycards (CityRefSpots.RunTreeCards),
        /// citywalk (RunTreeSeason), citycost (RunTreeMatchCost), stagecards (RunStageCards), stagewalk (RunStageWalk),
        /// stagecost (RunStageMatchCost). The process-wide switches (PSX_TREE_MATCH, PSX_TREE_MATCH_CARDS) are the
        /// job's own: read once when the editor starts.
        ///   -executeMethod PSXRacing.EditorTools.TreeMatchAudit.RunMany
        /// </summary>
        public static void RunMany()
        {
            NoExit = true;
            var done = new StringBuilder($"TREE SEASON RUNS {System.DateTime.Now:yyyy-MM-dd HH:mm}; PSX_TREE_MATCH={Env("PSX_TREE_MATCH", "(default)")}, PSX_TREE_MATCH_CARDS={Env("PSX_TREE_MATCH_CARDS", "(default)")}\n");
            try
            {
                foreach (var item in Env("PSX_TM_RUN", "").Split(';'))
                {
                    var parts = item.Split('|');
                    string name = parts[0].Trim();
                    if (name.Length == 0) continue;
                    var keys = new List<string>();
                    if (parts.Length > 1)
                        foreach (var kv in parts[1].Split('&'))
                        {
                            int eq = kv.IndexOf('=');
                            if (eq <= 0) continue;
                            string k = kv.Substring(0, eq).Trim();
                            System.Environment.SetEnvironmentVariable(k, kv.Substring(eq + 1).Trim());
                            keys.Add(k);
                        }
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        switch (name)
                        {
                            case "citycards": CityRefSpots.RunTreeCards(); break;
                            case "citywalk": CityRefSpots.RunTreeSeason(); break;
                            case "citycost": CityRefSpots.RunTreeMatchCost(); break;
                            case "stagecards": RunStageCards(); break;
                            case "stagewalk": RunStageWalk(); break;
                            case "stagecost": RunStageMatchCost(); break;
                            default: done.Append($"  {name}: no such runner\n"); continue;
                        }
                        done.Append($"  {name} ({(parts.Length > 1 ? parts[1] : "")}): ran, {clock.Elapsed.TotalSeconds:0} s\n");
                    }
                    catch (System.Exception ex) { done.Append($"  {name}: FAILED {ex.Message}\n"); Debug.LogException(ex); }
                    finally { foreach (var k in keys) System.Environment.SetEnvironmentVariable(k, null); }
                }
            }
            finally
            {
                NoExit = false;
                string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "treeseason_runmany_" + Env("PSX_TM_RUNTAG", "run") + ".txt"), done.ToString());
                Debug.Log("[TreeSeasonRuns]\n" + done);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void RunStageWalk()
        {
            // (PSX_TM_WALKS = "venue:m[,m];venue:m...": several stages in one process; else PSX_TM_VENUE + PSX_TM_M)
            string walks = Env("PSX_TM_WALKS", Env("PSX_TM_VENUE", "SwissNC226A") + ":" + Env("PSX_TM_M", "7900"));
            int code = 0;
            foreach (var spec in walks.Split(';'))
            {
                var q = spec.Split(':');
                if (q.Length != 2 || q[0].Trim().Length == 0) continue;
                code |= StageWalkOne(q[0].Trim(), q[1].Trim());
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }

        static int StageWalkOne(string id, string mList)
        {
            string tag = Env("PSX_TM_TAG", "walk");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(Path.Combine(root, tag));
            var log = new StringBuilder($"TREE SEASON WALK {id} ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; PSX_TREE_MATCH={Env("PSX_TREE_MATCH", "(default)")}\n");
            int code = 0, shots = 0;
            int keepDay = RaceHandoff.CalendarDay, keepWeather = RaceHandoff.WeatherOverride, keepDetail = ForestDetailPrefs.Override;
            bool keepDyn = SkyModePrefs.Dynamic;
            try
            {
                int ti = TrackCatalog.IndexOf(id);
                if (ti < 0) throw new System.Exception("no venue " + id);
                ForestDetailPrefs.Override = Mathf.Clamp(int.Parse(Env("PSX_TM_DETAIL", "1"), CultureInfo.InvariantCulture), 0, 2);
                if (!PSXScreenshotTool.Open(TrackCatalog.At(ti), out var cam, out var player)) throw new System.Exception("scene did not open");
                var path = Object.FindFirstObjectByType<TrackPath>();
                var sun = NightLookShots.FindSun();
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                var forest = Object.FindFirstObjectByType<MeshForest>();
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (path == null || forest == null) throw new System.Exception("no path or no mesh forest (a PC scene pass?)");
                if (output != null) output.height = 720;
                var sc = cam.GetComponent<StageCulling>();
                if (sc != null) sc.Configure(cam);
                cam.aspect = 16f / 9f;
                // the car out of the picture (the eye is the driver's)
                if (player != null) player.SetActive(false);
                SkyModePrefs.Dynamic = false;
                RaceHandoff.WeatherOverride = -1;
                RaceHandoff.CalendarDay = int.Parse(Env("PSX_TM_DAY", "0"), CultureInfo.InvariantCulture);
                Seasons.Forced = null; Seasons.Invalidate();
                var w = Seasons.Now;
                w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f; w.sky = SkyKind.Clear; w.scenery = SceneryKind.Dry;
                Seasons.Forced = w;
                int hour = TimeOfDay.Noon;
                string hourName = Env("PSX_TM_HOUR", "noon").ToLowerInvariant();
                for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name.ToLowerInvariant() == hourName) hour = h;
                if (sun != null) TimeOfDay.Apply(hour, sun);
                if (globals != null) globals.Apply();
                NightGlow.PreviewAll(false);
                if (dressing != null)
                    typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);
                float from = float.Parse(Env("PSX_TM_FROM", "400"), CultureInfo.InvariantCulture), to = float.Parse(Env("PSX_TM_TO", "20"), CultureInfo.InvariantCulture);
                float step = Mathf.Max(1f, float.Parse(Env("PSX_TM_STEP", "10"), CultureInfo.InvariantCulture));
                bool pair = Env("PSX_TM_PAIR", "1") == "1";
                // (PSX_TM_EYEUP: the eye's height over the road - 1.15 m is the driver's; 25 m a vista over the far slopes)
                float eyeUp = float.Parse(Env("PSX_TM_EYEUP", "1.15"), CultureInfo.InvariantCulture);
                bool first = true;
                foreach (var ds in Env("PSX_TM_DRESSES", "0,3").Split(','))
                {
                    if (!int.TryParse(ds.Trim(), out int dress) || dress < 0 || dress >= Seasons.DressCount) continue;
                    if (dressing != null) dressing.Apply(dress);
                    string dn = Seasons.DressNames[dress];
                    foreach (var ms in mList.Split(','))
                    {
                        if (!float.TryParse(ms.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) continue;
                        Vector3 target = At(path, m, out _) + Vector3.up * 3f;
                        Vector3 prevEye = Vector3.zero;
                        for (float d = from + step; d >= to - 0.01f; d -= step)
                        {
                            Vector3 p = At(path, Mathf.Max(2f * TrackCatalog.Spacing, m - d), out Vector3 fwd);
                            Vector3 eye = p + Vector3.up * eyeUp;
                            var rot = Quaternion.LookRotation((target - eye).normalized);
                            // (the first position is one step back: only the eye the b frame's levels come from)
                            if (d > from + 0.01f) { prevEye = eye; continue; }
                            cam.transform.SetPositionAndRotation(eye, rot);
                            MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                            forest.DrawNow(cam, true);
                            SunShadows.RenderFor(cam);
                            forest.DrawNow(cam, true);
                            if (first) { first = false; log.Append($"forest: levels {F(forest.lod0To, "0")} / {F(forest.lod1To, "0")} / {F(forest.meshRange, "0")} m, bands {F(forest.band0, "0")} / {F(forest.band1, "0")} / {F(forest.band2, "0")} m, FOREST DETAIL {ForestDetailPrefs.Label}, tree match species {forest.MatchCount}\n"); }
                            PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/{id}_{ms.Trim()}_{dn}_{d:000}_a", eye, rot);
                            shots++;
                            if (pair)
                            {
                                MeshForest.LodEyeOn = true; MeshForest.LodEye = prevEye;
                                forest.DrawNow(cam, true);
                                PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/{id}_{ms.Trim()}_{dn}_{d:000}_b", eye, rot);
                                MeshForest.LodEyeOn = false;
                                shots++;
                            }
                            prevEye = eye;
                        }
                        log.Append($"  {id} {ms.Trim()} m {dn}: {from:0} -> {to:0} m every {step:0} m\n");
                    }
                }
                log.Append($"{shots} frames\nTREE SEASON WALK OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE SEASON WALK FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                MeshForest.LodEyeOn = false;
                ForestDetailPrefs.Override = keepDetail;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather; RaceHandoff.CalendarDay = keepDay;
                SkyModePrefs.Dynamic = keepDyn;
                TimeOfDay.FarEdgeM = 0f; TimeOfDay.HorizonM = 0f;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_{id}_log.txt"), log.ToString());
                Debug.Log("[TreeSeasonWalk]\n" + log);
            }
            return code;
        }

        /// <summary>THE FOLIAGE ALONE in the camera's view as it stands (everything else left out, a key
        /// colour behind): the share of the frame it covers and its mean colour - for the same forest drawn
        /// two ways from one eye (its meshes and far cards; its old cards).</summary>
        internal static string FoliageStat(Camera cam, int layer, out float cover, out float hue, out float sat, out float luma)
        {
            var keepFlags = cam.clearFlags; var keepBg = cam.backgroundColor; int keepMask = cam.cullingMask;
            const int W = 1280, H = 720;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Bg; cam.cullingMask = 1 << layer;
            var rt = RenderTexture.GetTemporary(W, H, 32, RenderTextureFormat.ARGB32);
            cover = hue = sat = luma = 0f;
            try
            {
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (!UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request)) return "no render request";
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var px = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                double r = 0, g = 0, b = 0; int n = 0;
                foreach (var c in px)
                {
                    if (Mathf.Abs(c.r - Bg.r) <= 10 && c.g <= 10 && Mathf.Abs(c.b - Bg.b) <= 10) continue;
                    n++; r += c.r; g += c.g; b += c.b;
                }
                cover = n / (float)px.Length;
                if (n > 0)
                {
                    var mean = new Color((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(b / n / 255.0));
                    Color.RGBToHSV(mean, out hue, out sat, out _);
                    hue *= 360f;
                    luma = 0.2126f * mean.r + 0.7152f * mean.g + 0.0722f * mean.b;
                }
                return $"cover {F(cover * 100f, "0.0")} %, hue {F(hue, "0")}, sat {F(sat)}, luma {F(luma, "0.000")}";
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                cam.clearFlags = keepFlags; cam.backgroundColor = keepBg; cam.cullingMask = keepMask;
            }
        }

        /// <summary>The verdict of two ways of drawing one forest from one eye (the kit audit's limits).</summary>
        internal static string TwoWays(float coverA, float hueA, float satA, float lumaA, float coverB, float hueB, float satB, float lumaB)
        {
            var why = new List<string>();
            if (Mathf.Min(coverA, coverB) / Mathf.Max(1e-5f, Mathf.Max(coverA, coverB)) < AreaRatioMin) why.Add("LEAF");
            if (Mathf.Min(satA, satB) > SatFloor && Mathf.Abs(Mathf.DeltaAngle(hueA, hueB)) > HueTolDeg) why.Add("HUE");
            if (Mathf.Abs(satA - satB) > SatTol) why.Add("SAT");
            if (Mathf.Abs(lumaA - lumaB) / Mathf.Max(0.02f, Mathf.Max(lumaA, lumaB)) > LumaTol) why.Add("VALUE");
            return why.Count == 0 ? "ok" : "FAIL " + string.Join("+", why);
        }

        /// <summary>
        /// A STAGE'S OLD CARDS AGAINST ITS MESH FOREST: from a vista over the road at PSX_TM_M, each dress,
        /// the forest drawn as the game draws it in the view (meshes, far cards) and as the cards it shows
        /// where it does not (past the far range, outside the view's cone: the mirrors' far side).
        ///   -executeMethod PSXRacing.EditorTools.TreeMatchAudit.RunStageCards   PSX_TM_VENUE  PSX_TM_M  PSX_TM_DRESSES  PSX_TM_TAG
        /// Output: Screenshots/treeseason/&lt;tag&gt;/cards_&lt;venue&gt;_&lt;m&gt;_&lt;DRESS&gt;_mesh|cards.png, treeseason_&lt;tag&gt;_&lt;venue&gt;_cards.txt
        /// </summary>
        public static void RunStageCards()
        {
            string tag = Env("PSX_TM_TAG", "cards");
            string id = Env("PSX_TM_VENUE", "SwissNC226A");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(Path.Combine(root, tag));
            var log = new StringBuilder($"TREE CARDS v MESH FOREST {id} ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; PSX_TREE_MATCH={Env("PSX_TREE_MATCH", "(default)")}\n");
            int code = 0;
            int keepDay = RaceHandoff.CalendarDay, keepWeather = RaceHandoff.WeatherOverride;
            bool keepDyn = SkyModePrefs.Dynamic;
            try
            {
                int ti = TrackCatalog.IndexOf(id);
                if (ti < 0 || !PSXScreenshotTool.Open(TrackCatalog.At(ti), out var cam, out var player)) throw new System.Exception("no venue / scene " + id);
                var path = Object.FindFirstObjectByType<TrackPath>();
                var sun = NightLookShots.FindSun();
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                var forest = Object.FindFirstObjectByType<MeshForest>();
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (path == null || forest == null) throw new System.Exception("no path or no mesh forest");
                if (output != null) output.height = 720;
                var sc = cam.GetComponent<StageCulling>();
                if (sc != null) sc.Configure(cam);
                cam.aspect = 16f / 9f;
                if (player != null) player.SetActive(false);
                SkyModePrefs.Dynamic = false;
                RaceHandoff.WeatherOverride = -1; RaceHandoff.CalendarDay = 0;
                Seasons.Forced = null; Seasons.Invalidate();
                var w = Seasons.Now;
                w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f; w.sky = SkyKind.Clear; w.scenery = SceneryKind.Dry;
                Seasons.Forced = w;
                if (sun != null) TimeOfDay.Apply(TimeOfDay.Noon, sun);
                if (globals != null) globals.Apply();
                NightGlow.PreviewAll(false);
                if (dressing != null)
                    typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);
                // THE MEASURE'S LAYER: a stage forest's own object stands on Default with the terrain and every prop;
                // its cards are on Foliage. Both drawn on Foliage here, so the foliage stat of either way is the trees.
                const int FoliageL = 10;
                forest.gameObject.layer = FoliageL;
                log.Append($"  (the mesh forest drawn on the Foliage layer for the measure; old cards matched to the kit's trees: {(MeshForest.TreeMatch && MeshForest.MatchCardsOn && forest.matchCardAtlas != null ? "yes" : "no (PSX_TREE_MATCH_CARDS=0 or no atlas)")})\n");
                foreach (var ds in Env("PSX_TM_DRESSES", "0,3").Split(','))
                {
                    if (!int.TryParse(ds.Trim(), out int dress) || dress < 0 || dress >= Seasons.DressCount) continue;
                    if (dressing != null) dressing.Apply(dress);
                    string dn = Seasons.DressNames[dress];
                    foreach (var ms in Env("PSX_TM_M", "7900").Split(','))
                    {
                        if (!float.TryParse(ms.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) continue;
                        Vector3 p = At(path, m, out Vector3 fwd);
                        Vector3 eye = p + Vector3.up * 25f;
                        var rot = Quaternion.LookRotation((fwd + Vector3.down * 0.12f).normalized);
                        cam.transform.SetPositionAndRotation(eye, rot);
                        MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                        forest.DrawNow(cam, true);
                        SunShadows.RenderFor(cam);
                        forest.DrawNow(cam, true);
                        PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/cards_{id}_{ms.Trim()}_{dn}_mesh", eye, rot);
                        forest.DrawNow(cam, true);
                        // (the lists the cull filled: a dress in which the forest draws nothing shows here as 0)
                        long l0 = 0, l1 = 0, l2 = 0, l3 = 0;
                        foreach (var line in forest.DebugCounts().Split('\n'))
                        {
                            int c = line.IndexOf(':');
                            if (c < 0) continue;
                            var q = line.Substring(c + 1).Split(',');
                            if (q.Length < 4) continue;
                            long.TryParse(q[0], out long a0); long.TryParse(q[1], out long a1); long.TryParse(q[2], out long a2); long.TryParse(q[3], out long a3);
                            l0 += a0; l1 += a1; l2 += a2; l3 += a3;
                        }
                        log.Append($"  {id} {ms.Trim()} m {dn}: plants listed HIGH {l0}, MID {l1}, LOW {l2}, far cards {l3}{(l0 + l1 + l2 + l3 == 0 ? "  <-- THE FOREST DRAWS NOTHING" : "")}\n");
                        if (l0 + l1 + l2 + l3 == 0) code = 1;
                        string a = FoliageStat(cam, FoliageL, out float cA, out float hA, out float sA, out float lA);
                        forest.CardsNow();
                        PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/cards_{id}_{ms.Trim()}_{dn}_cards", eye, rot);
                        string b = FoliageStat(cam, FoliageL, out float cB, out float hB, out float sB, out float lB);
                        log.Append($"  {id} {ms.Trim()} m {dn}: mesh forest {a} | cards {b} -> {TwoWays(cA, hA, sA, lA, cB, hB, sB, lB)}\n");
                    }
                }
                log.Append("TREE CARDS OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE CARDS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather; RaceHandoff.CalendarDay = keepDay;
                SkyModePrefs.Dynamic = keepDyn;
                TimeOfDay.FarEdgeM = 0f; TimeOfDay.HorizonM = 0f;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_{id}_cards.txt"), log.ToString());
                Debug.Log("[TreeCards]\n" + log);
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }

        /// <summary>
        /// THE TREE MATCH'S COST in one process: the same forest with the packs' far sheets (mode 0,
        /// PSX_TREE_MATCH=0) and with the match cards (mode 1) at one eye, <paramref name="rounds"/> rounds
        /// each in ABBA order (the editor's noise shows in the spread); 1920 x 1080, every frame waited for
        /// with a one-pixel read-back; the forest's own counts (plants, triangles, draws) per mode.
        /// <paramref name="setMode"/> makes the forest of a mode current and returns it.
        /// </summary>
        internal static string CostAB(Camera cam, System.Func<int, MeshForest> setMode, int rounds, string what)
        {
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var ms = new[] { new List<double>(), new List<double>() };
            var tris = new double[2]; var draws = new double[2]; var plants = new double[2]; var far = new double[2];
            try
            {
                for (int k = 0; k < rounds * 2; k++)
                {
                    int m = (k + k / 2) & 1;   // off, on, on, off, off, on, on, off
                    var f = setMode(m);
                    MeshForest.MeshTreesOn = true; MeshForest.LodEyeOn = false;
                    for (int i = 0; i < 4; i++) { f.DrawNow(cam, true); cam.Render(); }
                    const int N = 24;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < N; i++)
                    {
                        f.DrawNow(cam, true); cam.Render();
                        RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                    }
                    sw.Stop();
                    ms[m].Add(sw.Elapsed.TotalMilliseconds / N);
                    MeshForest.StatsOn = true; MeshForest.StatsReset();
                    for (int i = 0; i < 6; i++) { f.DrawNow(cam, true); cam.Render(); }
                    UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                    int sf = Mathf.Max(1, MeshForest.StatFrames);
                    tris[m] = MeshForest.SumTris / sf; draws[m] = MeshForest.SumDraws / sf; plants[m] = MeshForest.SumPlants / sf;
                    far[m] = MeshForest.SumTrisLod[3] / sf;
                    MeshForest.StatsOn = false;
                }
            }
            finally
            {
                MeshForest.StatsOn = false;
                cam.targetTexture = old;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
            }
            double Med(List<double> l)
            {
                var c = new List<double>(l); c.Sort();
                return c.Count == 0 ? 0 : c.Count % 2 == 1 ? c[c.Count / 2] : 0.5 * (c[c.Count / 2 - 1] + c[c.Count / 2]);
            }
            string All(List<double> l) => string.Join(" ", l.ConvertAll(x => x.ToString("0.00", CultureInfo.InvariantCulture)));
            double d = Med(ms[1]) - Med(ms[0]);
            return $"  COST A/B {what}: frame (1920x1080, editor, render + read-back, {rounds} rounds each, ABBA): packs' far sheets {Med(ms[0]):0.00} ms [{All(ms[0])}], " +
                   $"tree match {Med(ms[1]):0.00} ms [{All(ms[1])}], {(d >= 0 ? "+" : "")}{d:0.00} ms; drawn {plants[0]:0} -> {plants[1]:0} plants, " +
                   $"{tris[0] / 1e6:0.00} -> {tris[1] / 1e6:0.00} M tris (far level {far[0] / 1e6:0.000} -> {far[1] / 1e6:0.000} M), {draws[0]:0} -> {draws[1]:0} draws\n";
        }

        /// <summary>
        /// THE TREE MATCH'S COST ON A STAGE: the scene's forest twice (a copy made before either is set up),
        /// one with the packs' far sheets and one with the match cards, measured ABBA from the driver's eye
        /// down the road at PSX_TM_M and from a vista 25 m over it.
        ///   -executeMethod PSXRacing.EditorTools.TreeMatchAudit.RunStageMatchCost   PSX_TM_VENUE  PSX_TM_M  PSX_TM_DRESSES (one)  PSX_TM_TAG
        /// Output: Screenshots/treeseason/treeseason_&lt;tag&gt;_&lt;venue&gt;_cost.txt
        /// </summary>
        public static void RunStageMatchCost()
        {
            string tag = Env("PSX_TM_TAG", "cost");
            string id = Env("PSX_TM_VENUE", "SwissNC226A");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(root);
            var log = new StringBuilder($"TREE MATCH COST {id} ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}\n");
            int code = 0;
            int keepDay = RaceHandoff.CalendarDay, keepWeather = RaceHandoff.WeatherOverride;
            bool keepDyn = SkyModePrefs.Dynamic, keepMatch = MeshForest.TreeMatch;
            GameObject copy = null;
            try
            {
                int ti = TrackCatalog.IndexOf(id);
                if (ti < 0 || !PSXScreenshotTool.Open(TrackCatalog.At(ti), out var cam, out var player)) throw new System.Exception("no venue / scene " + id);
                var path = Object.FindFirstObjectByType<TrackPath>();
                var sun = NightLookShots.FindSun();
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                var forest = Object.FindFirstObjectByType<MeshForest>();
                if (path == null || forest == null) throw new System.Exception("no path or no mesh forest");
                var sc = cam.GetComponent<StageCulling>();
                if (sc != null) sc.Configure(cam);
                if (player != null) player.SetActive(false);
                SkyModePrefs.Dynamic = false;
                RaceHandoff.WeatherOverride = -1; RaceHandoff.CalendarDay = 0;
                Seasons.Forced = null; Seasons.Invalidate();
                var w = Seasons.Now;
                w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f; w.sky = SkyKind.Clear; w.scenery = SceneryKind.Dry;
                Seasons.Forced = w;
                if (sun != null) TimeOfDay.Apply(TimeOfDay.Noon, sun);
                if (globals != null) globals.Apply();
                NightGlow.PreviewAll(false);
                if (dressing != null)
                    typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);
                int dress = int.Parse(Env("PSX_TM_DRESSES", "3").Split(',')[0], CultureInfo.InvariantCulture);
                if (dressing != null) dressing.Apply(dress);
                // the copy BEFORE either is set up: both hold the scene's own list of species
                copy = Object.Instantiate(forest.gameObject, forest.transform.parent);
                copy.name = forest.gameObject.name + " (tree match cost copy)";
                var forestB = copy.GetComponent<MeshForest>();
                MeshForest.TreeMatch = false; forest.DrawNow(cam, true);
                MeshForest.TreeMatch = true; forestB.DrawNow(cam, true);
                MeshForest.TreeMatch = keepMatch;
                log.Append($"forest A (packs' far sheets): {forest.MatchCount} match species; forest B (tree match): {forestB.MatchCount} match species; dress {Seasons.DressNames[dress]}\n");
                MeshForest Mode(int m)
                {
                    copy.SetActive(m == 1);
                    forest.gameObject.SetActive(m == 0);
                    return m == 1 ? forestB : forest;
                }
                int rounds = int.Parse(Env("PSX_TM_ROUNDS", "4"), CultureInfo.InvariantCulture);
                foreach (var ms in Env("PSX_TM_M", "7900").Split(','))
                {
                    if (!float.TryParse(ms.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mm)) continue;
                    Vector3 p = At(path, mm, out Vector3 fwd);
                    // the driver's eye down the road, then a vista over it
                    cam.transform.SetPositionAndRotation(p + Vector3.up * 1.15f, Quaternion.LookRotation((fwd + Vector3.down * 0.02f).normalized));
                    cam.aspect = 16f / 9f;
                    log.Append(CostAB(cam, Mode, rounds, $"{id} {ms.Trim()} m, driver's eye"));
                    cam.transform.SetPositionAndRotation(p + Vector3.up * 25f, Quaternion.LookRotation((fwd + Vector3.down * 0.12f).normalized));
                    log.Append(CostAB(cam, Mode, rounds, $"{id} {ms.Trim()} m, vista 25 m up"));
                }
                Mode(0);
                log.Append("TREE MATCH COST OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE MATCH COST FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                if (copy != null) Object.DestroyImmediate(copy);
                MeshForest.TreeMatch = keepMatch;
                MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather; RaceHandoff.CalendarDay = keepDay;
                SkyModePrefs.Dynamic = keepDyn;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_{id}_cost.txt"), log.ToString());
                Debug.Log("[TreeMatchCost]\n" + log);
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }

        /// <summary>The road's centre at <paramref name="m"/> metres along the path, and its heading.</summary>
        static Vector3 At(TrackPath path, float m, out Vector3 fwd)
        {
            float fi = m / TrackCatalog.Spacing;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(fi), 2, path.Count - 13);
            float t = Mathf.Clamp01(fi - i0);
            fwd = Vector3.Slerp(path.GetTangent(i0), path.GetTangent(i0 + 1), t); fwd.y = 0f; fwd.Normalize();
            return Vector3.Lerp(path.GetPoint(i0), path.GetPoint(i0 + 1), t);
        }
    }
}
