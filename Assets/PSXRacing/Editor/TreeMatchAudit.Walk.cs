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
                            case "stagering": RunStageRing(); break;
                            case "cityring": CityRefSpots.RunCityRing(); break;
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
            foreach (var spec in walks.Split(new[] { ';', '+' }))   // ('+' inside RunMany's PSX_TM_RUN, whose items ';' separates)
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
            // (PSX_TM_RES=WxH: the frame's size - the owner's display is 3832 x 2054; what grows with it is fill)
            var resq = Env("PSX_TM_RES", "1920x1080").Split('x');
            int resW = resq.Length == 2 && int.TryParse(resq[0], out int rw) ? rw : 1920, resH = resq.Length == 2 && int.TryParse(resq[1], out int rh) ? rh : 1080;
            var rt = new RenderTexture(resW, resH, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var ms = new[] { new List<double>(), new List<double>() };
            var tris = new double[2]; var draws = new double[2]; var plants = new double[2]; var far = new double[2];
            // (tree-cost round: the counts read back AT ONCE after a cull from this very eye - MeshForest.DebugTally -
            // not GpuStats' sampled late callbacks, which gave mode A the same count from two different eyes)
            var lodP = new long[2][] { new long[4], new long[4] }; var lodT = new long[2][] { new long[4], new long[4] };
            var perV = new string[2];
            var names = new List<string>[2];
            int frames = int.Parse(Env("PSX_TM_FRAMES", "32"), CultureInfo.InvariantCulture);
            // (PSX_TM_ONLYFOREST=1: the camera draws the Foliage layer alone - the forest, its cards and the
            // city's tile cards - with the forest put on it: the editor's frame noise of everything else gone)
            bool only = Env("PSX_TM_ONLYFOREST", "0") == "1";
            int keepMask = cam.cullingMask;
            const int FoliageL = 10;
            if (only) cam.cullingMask = 1 << FoliageL;
            try
            {
                for (int k = 0; k < rounds * 2; k++)
                {
                    int m = (k + k / 2) & 1;   // off, on, on, off, off, on, on, off
                    var f = setMode(m);
                    if (only) f.gameObject.layer = FoliageL;
                    MeshForest.MeshTreesOn = true; MeshForest.LodEyeOn = false;
                    for (int i = 0; i < 4; i++) { f.DrawNow(cam, true); cam.Render(); }
                    int N = frames;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < N; i++)
                    {
                        f.DrawNow(cam, true); cam.Render();
                        RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                    }
                    sw.Stop();
                    ms[m].Add(sw.Elapsed.TotalMilliseconds / N);
                    f.DrawNow(cam, true); cam.Render();
                    perV[m] = f.DebugTally(lodP[m], lodT[m]);
                    names[m] = new List<string>();
                    foreach (var vr in f.variants) names[m].Add(vr != null && vr.lods != null && vr.lods.Length > 0 && vr.lods[0] != null ? vr.lods[0].name : "?");
                    plants[m] = lodP[m][0] + lodP[m][1] + lodP[m][2] + lodP[m][3];
                    tris[m] = lodT[m][0] + lodT[m][1] + lodT[m][2] + lodT[m][3];
                    far[m] = lodT[m][3];
                    draws[m] = 0;
                }
            }
            finally
            {
                MeshForest.StatsOn = false;
                cam.cullingMask = keepMask;
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
            // the PAIRED differences (each B round against the A round beside it: 1-0, 2-3, 5-4, 6-7): the editor's
            // drift over a run cancels within a pair
            var pd = new List<double>();
            for (int p = 0; p < rounds && p < ms[0].Count && p < ms[1].Count; p++) pd.Add(ms[1][p] - ms[0][p]);
            double pm = 0, ps = 0;
            foreach (var x in pd) pm += x;
            pm /= Mathf.Max(1, pd.Count);
            foreach (var x in pd) ps += (x - pm) * (x - pm);
            ps = pd.Count > 1 ? System.Math.Sqrt(ps / (pd.Count - 1) / pd.Count) : 0;   // (the standard error of the mean)
            // (the MEDIAN of the pairs: other editors on the machine throw single rounds out by 10-35 ms)
            double pmed = Med(pd);
            var sb = new StringBuilder();
            sb.Append($"  COST A/B {what}{(only ? " [FOLIAGE LAYER ONLY]" : "")}: frame ({resW}x{resH}, editor, render + read-back, {rounds} rounds x {frames} frames each, ABBA): packs' far sheets {Med(ms[0]):0.00} ms [{All(ms[0])}], " +
                   $"tree match {Med(ms[1]):0.00} ms [{All(ms[1])}], {(d >= 0 ? "+" : "")}{d:0.00} ms (paired: median {(pmed >= 0 ? "+" : "")}{pmed:0.00} ms, mean {(pm >= 0 ? "+" : "")}{pm:0.00} +- {ps:0.00} ms [{All(pd)}]); drawn {plants[0]:0} -> {plants[1]:0} plants, " +
                   $"{tris[0] / 1e6:0.000} -> {tris[1] / 1e6:0.000} M tris (far level {far[0] / 1e6:0.000} -> {far[1] / 1e6:0.000} M)\n");
            sb.Append($"    by level, plants A/B: HIGH {lodP[0][0]}/{lodP[1][0]}, MID {lodP[0][1]}/{lodP[1][1]}, LOW {lodP[0][2]}/{lodP[1][2]}, FAR {lodP[0][3]}/{lodP[1][3]}; " +
                      $"tris M A/B: HIGH {lodT[0][0] / 1e6:0.000}/{lodT[1][0] / 1e6:0.000}, MID {lodT[0][1] / 1e6:0.000}/{lodT[1][1] / 1e6:0.000}, LOW {lodT[0][2] / 1e6:0.000}/{lodT[1][2] / 1e6:0.000}, FAR {lodT[0][3] / 1e6:0.000}/{lodT[1][3] / 1e6:0.000}\n");
            // the species whose lists differ most (A -> B by level)
            var a = ParseTally(perV[0]); var b = ParseTally(perV[1]);
            var diffs = new List<(long d, string line)>();
            var keys = new HashSet<int>(a.Keys); keys.UnionWith(b.Keys);
            foreach (int v in keys)
            {
                a.TryGetValue(v, out var ca); b.TryGetValue(v, out var cb);
                ca ??= new long[4]; cb ??= new long[4];
                long dd = 0;
                for (int l = 0; l < 4; l++) dd += System.Math.Abs(cb[l] - ca[l]);
                if (dd == 0) continue;
                string nm = names[1] != null && v < names[1].Count ? names[1][v] : v.ToString();
                diffs.Add((dd, $"      {nm}: HIGH {ca[0]}->{cb[0]}, MID {ca[1]}->{cb[1]}, LOW {ca[2]}->{cb[2]}, FAR {ca[3]}->{cb[3]}"));
            }
            diffs.Sort((x, y) => y.d.CompareTo(x.d));
            sb.Append($"    species whose lists differ: {diffs.Count}\n");
            for (int i = 0; i < Mathf.Min(12, diffs.Count); i++) sb.Append(diffs[i].line).Append('\n');
            return sb.ToString();
        }

        /// <summary>The forest's process-wide knobs read again from THIS item's environment (RunMany sets each
        /// item's variables after the statics were first read): the ring's ranges, the new far levels. The
        /// returned action puts them back.</summary>
        internal static System.Action RunKnobs()
        {
            float r = MeshForest.StageRingM, c = MeshForest.CityRingM, o = MeshForest.OtherRangeM;
            bool nf = MeshForest.NewFarLevels;
            float F(string k, float d) => float.TryParse(Env(k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && x >= 0f ? x : d;
            MeshForest.StageRingM = F("PSX_TM_RING_M", r);
            MeshForest.CityRingM = F("PSX_TM_CITYRING_M", c);
            MeshForest.OtherRangeM = F("PSX_TM_OTHER_M", o);
            MeshForest.NewFarLevels = Env("PSX_TM_NEWFAR", nf ? "1" : "0") != "0";
            return () => { MeshForest.StageRingM = r; MeshForest.CityRingM = c; MeshForest.OtherRangeM = o; MeshForest.NewFarLevels = nf; };
        }

        /// <summary>The switches for one side of an A/B (set before that forest's Init; put back by the caller):
        ///   match - A the packs' far sheets, B the tree match (cards as before: the merged default of ba49c506);
        ///   full  - A the packs' far sheets, B the tree match with PSX_TREE_MATCH_CARDS (this branch);
        ///   ring  - A the tree match, B with PSX_TREE_MATCH_CARDS;
        ///   atlas - both with PSX_TREE_MATCH_CARDS, A with no tree out of the cone in the other cameras' list
        ///           (OtherRangeM 0: the cards in the kit's atlas alone), B with them (PSX_TM_OTHER_M).
        /// <paramref name="control"/>: B is A.</summary>
        internal static void AbSet(string kind, int side, bool control, float otherKnob)
        {
            int m = control ? 0 : side;
            MeshForest.OtherRangeM = otherKnob;
            switch (kind)
            {
                case "full": MeshForest.TreeMatch = m == 1; MeshForest.MatchCardsOn = m == 1; break;
                case "ring": MeshForest.TreeMatch = true; MeshForest.MatchCardsOn = m == 1; break;
                case "atlas": MeshForest.TreeMatch = true; MeshForest.MatchCardsOn = true; if (m == 0) MeshForest.OtherRangeM = 0f; break;
                default: MeshForest.TreeMatch = m == 1; MeshForest.MatchCardsOn = false; break;
            }
        }

        /// <summary>THE OTHER CAMERA'S COST, A against B: the camera (a mirror, a reflection face) rendered into its own
        /// target after each forward cull, ABBA, at each "W x H x FOV" of PSX_TM_MIRRORS (default the cockpit's rear-view
        /// mirror 256x64x30, a door mirror 192x64x30 and one reflection face 512x512x90), the frame waited for.</summary>
        internal static string MirrorCost(Camera rear, System.Func<int, MeshForest> mode, System.Action<MeshForest> forward, Texture2D px, int rounds)
        {
            var sb = new StringBuilder();
            float keepFov = rear.fieldOfView, keepAspect = rear.aspect;
            // (PSX_TM_MIRRORFOL=1, the default: the Foliage layer alone - the forest's draws and the cards - so the
            // editor's ~38 ms of everything else in a camera's render does not bury the difference)
            int keepMask = rear.cullingMask;
            bool fol = Env("PSX_TM_MIRRORFOL", "1") == "1";
            if (fol) rear.cullingMask = 1 << 10;
            foreach (var spec in Env("PSX_TM_MIRRORS", "256x64x30,192x64x30,512x512x90").Split(','))
            {
                var q = spec.Split('x');
                if (q.Length != 3 || !int.TryParse(q[0], out int w) || !int.TryParse(q[1], out int h) || !float.TryParse(q[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float fov)) continue;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                var ms = new[] { new List<double>(), new List<double>() };
                rear.targetTexture = rt; rear.fieldOfView = fov; rear.aspect = w / (float)h;
                try
                {
                    for (int k = 0; k < rounds * 2; k++)
                    {
                        int m = (k + k / 2) & 1;
                        var f = mode(m);
                        for (int i = 0; i < 3; i++) { forward(f); f.DrawOtherNow(rear); rear.Render(); }
                        double t = 0;
                        const int N = 24;
                        for (int i = 0; i < N; i++)
                        {
                            forward(f); f.DrawOtherNow(rear);
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            rear.Render();
                            RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                            t += sw.Elapsed.TotalMilliseconds;
                        }
                        ms[m].Add(t / N);
                    }
                }
                finally { rear.targetTexture = null; rear.fieldOfView = keepFov; rear.aspect = keepAspect; Object.DestroyImmediate(rt); }
                var pd = new List<double>();
                for (int p = 0; p < rounds && p < ms[0].Count && p < ms[1].Count; p++) pd.Add(ms[1][p] - ms[0][p]);
                pd.Sort();
                double pmed = pd.Count == 0 ? 0 : pd.Count % 2 == 1 ? pd[pd.Count / 2] : 0.5 * (pd[pd.Count / 2 - 1] + pd[pd.Count / 2]);
                var a = new List<double>(ms[0]); a.Sort(); var b = new List<double>(ms[1]); b.Sort();
                sb.Append($"    the other camera's render {w} x {h}, {fov:0} deg{(fol ? " [FOLIAGE LAYER ONLY]" : "")} ({rounds} rounds x 24, ABBA): A {a[a.Count / 2]:0.000} ms, B {b[b.Count / 2]:0.000} ms, paired median {(pmed >= 0 ? "+" : "")}{pmed:0.000} ms [{string.Join(" ", pd.ConvertAll(x => x.ToString("0.00", CultureInfo.InvariantCulture)))}]\n");
            }
            rear.cullingMask = keepMask;
            return sb.ToString();
        }

        static Dictionary<int, long[]> ParseTally(string s)
        {
            var o = new Dictionary<int, long[]>();
            if (string.IsNullOrEmpty(s)) return o;
            foreach (var line in s.Split('\n'))
            {
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                var q = line.Substring(c + 1).Split(',');
                if (q.Length < 4 || !int.TryParse(line.Substring(0, c), out int v)) continue;
                var arr = new long[4];
                for (int l = 0; l < 4; l++) long.TryParse(q[l], out arr[l]);
                o[v] = arr;
            }
            return o;
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
            var restoreKnobs = RunKnobs();
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
                // (PSX_TM_CONTROL=1: B is the copy WITHOUT the match - the method's own bias, A against A)
                bool control = Env("PSX_TM_CONTROL", "0") == "1";
                // (PSX_TM_AB = match | full | ring | atlas: AbSet)
                string abMode = Env("PSX_TM_AB", "match");
                bool keepCards = MeshForest.MatchCardsOn;
                float otherKnob = MeshForest.OtherRangeM;
                AbSet(abMode, 0, control, otherKnob); forest.DrawNow(cam, true);
                AbSet(abMode, 1, control, otherKnob); forestB.DrawNow(cam, true);
                MeshForest.TreeMatch = keepMatch; MeshForest.MatchCardsOn = keepCards; MeshForest.OtherRangeM = otherKnob;
                log.Append($"[{abMode}{(control ? ", CONTROL" : "")}] forest A: {forest.MatchCount} match species, cards matched {forest.RingOn}, far range {forest.farRange:0} m; " +
                           $"forest B: {forestB.MatchCount} match species, cards matched {forestB.RingOn}, far range {forestB.farRange:0} m; other cameras' trees to {otherKnob:0} m; dress {Seasons.DressNames[dress]}\n");
                // (PSX_TM_NOFAR=1: neither forest draws its far level - the mesh levels' share of the difference alone)
                if (Env("PSX_TM_NOFAR", "0") == "1")
                {
                    foreach (var ff in new[] { forest, forestB })
                        foreach (var vr in ff.variants) if (vr != null) vr.impostorDress = null;
                    log.Append("  NO FAR LEVEL in either forest (PSX_TM_NOFAR=1)\n");
                }
                MeshForest Mode(int m)
                {
                    copy.SetActive(m == 1);
                    forest.gameObject.SetActive(m == 0);
                    // (the cards the two forests share - the far forest's B chunks stand outside the forest's own
                    // object - as THIS forest's chunk pass wants them: the other one may have left them otherwise)
                    var mf = m == 1 ? forestB : forest;
                    // (and in the material THIS forest gives them: the other may have matched them to the kit's atlas)
                    forest.RestoreBCards(); forestB.RestoreBCards();
                    mf.CardsNow();
                    return mf;
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
                MeshForest.TreeMatch = keepMatch; restoreKnobs();
                MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather; RaceHandoff.CalendarDay = keepDay;
                SkyModePrefs.Dynamic = keepDyn;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_{id}_cost.txt"), log.ToString());
                Debug.Log("[TreeMatchCost]\n" + log);
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }

        /// <summary>
        /// THE MIRRORS (tree-cost round, 2026-11-17): the scene's forest twice in one process, both with the tree
        /// match - A with the cards as they were where the forest does not draw (PSX_TREE_MATCH_CARDS off), B with the
        /// trees' own far cards there (MeshForest.RingOn) - each dress, at the driver's eye at PSX_TM_M:
        ///   a camera looking BACK (a mirror's narrow lens) drawn as an OTHER camera after the forward view's cull - what
        ///   the mirrors show - against the TRUTH, the same camera as the main view (its own cull: the forest as it
        ///   stands when you turn round); the Foliage layer alone measured (FoliageStat), every view shot;
        ///   then the mirror camera's own render time, A against B (ABBA, PSX_TM_ROUNDS), with the other cameras' list.
        ///   -executeMethod PSXRacing.EditorTools.TreeMatchAudit.RunStageRing   PSX_TM_VENUE  PSX_TM_M  PSX_TM_DRESSES  PSX_TM_TAG
        /// Output: Screenshots/treeseason/&lt;tag&gt;/ring_&lt;venue&gt;_&lt;m&gt;_&lt;DRESS&gt;_mirror_A|B|truth.png, treeseason_&lt;tag&gt;_&lt;venue&gt;_ring.txt
        /// </summary>
        public static void RunStageRing()
        {
            string tag = Env("PSX_TM_TAG", "ring");
            string id = Env("PSX_TM_VENUE", "SwissNC226A");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(Path.Combine(root, tag));
            var log = new StringBuilder($"TREE MIRRORS {id} ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}\n");
            int code = 0;
            int keepDay = RaceHandoff.CalendarDay, keepWeather = RaceHandoff.WeatherOverride;
            bool keepDyn = SkyModePrefs.Dynamic, keepMatch = MeshForest.TreeMatch, keepCards = MeshForest.MatchCardsOn;
            var restoreKnobs = RunKnobs();
            GameObject copy = null, rearGo = null;
            RenderTexture fwdRt = null;
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
                const int FoliageL = 10;
                // the copy BEFORE either is set up: both hold the scene's own list of species
                copy = Object.Instantiate(forest.gameObject, forest.transform.parent);
                copy.name = forest.gameObject.name + " (ring copy)";
                var forestB = copy.GetComponent<MeshForest>();
                // (PSX_TM_AB = ring (default here) | atlas | full: AbSet)
                string abMode = Env("PSX_TM_AB", "ring");
                float otherKnob = MeshForest.OtherRangeM;
                AbSet(abMode, 0, false, otherKnob); forest.DrawNow(cam, true);
                AbSet(abMode, 1, false, otherKnob); forestB.DrawNow(cam, true);
                MeshForest.TreeMatch = keepMatch; MeshForest.MatchCardsOn = keepCards; MeshForest.OtherRangeM = otherKnob;
                forest.gameObject.layer = FoliageL; copy.layer = FoliageL;
                log.Append($"[{abMode}] forest A: {forest.MatchCount} match species, cards matched {forest.RingOn}, far range {forest.farRange:0} m; " +
                           $"forest B: {forestB.MatchCount} match species, cards matched {forestB.RingOn} (other cameras' trees to {otherKnob:0} m out of the cone), far range {forestB.farRange:0} m\n");
                MeshForest Mode(int m)
                {
                    copy.SetActive(m == 1);
                    forest.gameObject.SetActive(m == 0);
                    // (the cards the two forests share - the far forest's B chunks stand outside the forest's own
                    // object - as THIS forest's chunk pass wants them: the other one may have left them otherwise)
                    var mf = m == 1 ? forestB : forest;
                    // (and in the material THIS forest gives them: the other may have matched them to the kit's atlas)
                    forest.RestoreBCards(); forestB.RestoreBCards();
                    mf.CardsNow();
                    return mf;
                }
                // the mirror: a plain camera of the main one's settings (its layer distances: the mirrors take the stage's)
                rearGo = new GameObject("TreeCostMirror") { hideFlags = HideFlags.HideAndDontSave };
                var rear = rearGo.AddComponent<Camera>();
                rear.CopyFrom(cam);
                rear.enabled = false;
                rear.fieldOfView = float.Parse(Env("PSX_TM_MIRRORFOV", "40"), CultureInfo.InvariantCulture);
                rear.aspect = 16f / 9f;
                if (StageCulling.LayerDistances != null) rear.layerCullDistances = StageCulling.LayerDistances;
                fwdRt = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
                var keepTarget = cam.targetTexture;
                var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                void Forward(MeshForest f) { f.DrawNow(cam, true); cam.targetTexture = fwdRt; cam.Render(); cam.targetTexture = keepTarget; }
                int rounds = int.Parse(Env("PSX_TM_ROUNDS", "4"), CultureInfo.InvariantCulture);
                foreach (var ds in Env("PSX_TM_DRESSES", "0,3").Split(','))
                {
                    if (!int.TryParse(ds.Trim(), out int dress) || dress < 0 || dress >= Seasons.DressCount) continue;
                    if (dressing != null) dressing.Apply(dress);
                    string dn = Seasons.DressNames[dress];
                    foreach (var ms in Env("PSX_TM_M", "7900").Split(','))
                    {
                        if (!float.TryParse(ms.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mm)) continue;
                        Vector3 p = At(path, mm, out Vector3 fwd);
                        Vector3 eye = p + Vector3.up * 1.15f;
                        var rotF = Quaternion.LookRotation((fwd + Vector3.down * 0.02f).normalized);
                        var rotR = Quaternion.LookRotation((-fwd + Vector3.down * 0.02f).normalized);
                        cam.transform.SetPositionAndRotation(eye, rotF);
                        rear.transform.SetPositionAndRotation(eye, rotR);
                        MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                        var stat = new (float c, float h, float s, float l, string txt)[3];
                        for (int m = 0; m < 2; m++)
                        {
                            var f = Mode(m);
                            Forward(f); f.DrawOtherNow(rear);
                            PSXScreenshotTool.ShotAs(rear, $"treeseason/{tag}/ring_{id}_{ms.Trim()}_{dn}_mirror_{(m == 0 ? "A" : "B")}", eye, rotR);
                            Forward(f); f.DrawOtherNow(rear);
                            stat[m].txt = FoliageStat(rear, FoliageL, out stat[m].c, out stat[m].h, out stat[m].s, out stat[m].l);
                            Forward(f);
                            string fl = f.DebugOther(out long oc);
                            log.Append($"  {id} {ms.Trim()} m {dn} mirror {(m == 0 ? "A (cards)" : "B (far cards)")}: {stat[m].txt}; other cameras' list {oc} far cards; {fl}\n");
                        }
                        // THE TRUTH: forest B culled from the mirror's own eye, as the main view
                        var fb = Mode(1);
                        fb.DrawNow(rear, true);
                        PSXScreenshotTool.ShotAs(rear, $"treeseason/{tag}/ring_{id}_{ms.Trim()}_{dn}_mirror_truth", eye, rotR);
                        fb.DrawNow(rear, true);
                        stat[2].txt = FoliageStat(rear, FoliageL, out stat[2].c, out stat[2].h, out stat[2].s, out stat[2].l);
                        log.Append($"  {id} {ms.Trim()} m {dn} mirror TRUTH (the forest turned round): {stat[2].txt}\n");
                        for (int m = 0; m < 2; m++)
                            log.Append($"    {(m == 0 ? "A" : "B")} against the truth: cover {stat[m].c / Mathf.Max(1e-5f, stat[2].c):0.00} x -> {TwoWays(stat[m].c, stat[m].h, stat[m].s, stat[m].l, stat[2].c, stat[2].h, stat[2].s, stat[2].l)}\n");
                        // THE MIRROR'S COST: its own render after each forward cull, ABBA, at each size and lens asked
                        log.Append(MirrorCost(rear, Mode, Forward, px, rounds));
                    }
                }
                Object.DestroyImmediate(px);
                Mode(0);
                log.Append("TREE MIRRORS OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE MIRRORS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                if (copy != null) Object.DestroyImmediate(copy);
                if (rearGo != null) Object.DestroyImmediate(rearGo);
                if (fwdRt != null) Object.DestroyImmediate(fwdRt);
                MeshForest.TreeMatch = keepMatch; MeshForest.MatchCardsOn = keepCards; restoreKnobs();
                MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather; RaceHandoff.CalendarDay = keepDay;
                SkyModePrefs.Dynamic = keepDyn;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_{id}_ring.txt"), log.ToString());
                Debug.Log("[TreeMirrors]\n" + log);
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
