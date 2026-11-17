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
            if (Application.isBatchMode) EditorApplication.Exit(code);
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
                        string a = FoliageStat(cam, forest.gameObject.layer, out float cA, out float hA, out float sA, out float lA);
                        forest.CardsNow();
                        PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/cards_{id}_{ms.Trim()}_{dn}_cards", eye, rot);
                        string b = FoliageStat(cam, forest.gameObject.layer, out float cB, out float hB, out float sB, out float lB);
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
            if (Application.isBatchMode) EditorApplication.Exit(code);
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
