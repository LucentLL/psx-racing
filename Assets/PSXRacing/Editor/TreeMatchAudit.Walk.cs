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
            string tag = Env("PSX_TM_TAG", "walk");
            string id = Env("PSX_TM_VENUE", "SwissNC226A");
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
                bool first = true;
                foreach (var ds in Env("PSX_TM_DRESSES", "0,3").Split(','))
                {
                    if (!int.TryParse(ds.Trim(), out int dress) || dress < 0 || dress >= Seasons.DressCount) continue;
                    if (dressing != null) dressing.Apply(dress);
                    string dn = Seasons.DressNames[dress];
                    foreach (var ms in Env("PSX_TM_M", "7900").Split(','))
                    {
                        if (!float.TryParse(ms.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) continue;
                        Vector3 target = At(path, m, out _) + Vector3.up * 3f;
                        Vector3 prevEye = Vector3.zero;
                        for (float d = from + step; d >= to - 0.01f; d -= step)
                        {
                            Vector3 p = At(path, Mathf.Max(2f * TrackCatalog.Spacing, m - d), out Vector3 fwd);
                            Vector3 eye = p + Vector3.up * 1.15f;
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
