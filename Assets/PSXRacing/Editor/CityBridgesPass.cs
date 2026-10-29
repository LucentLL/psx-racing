using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE BRIDGES AND RAILS PASS's one job (2026-10-29, the owner's two
    /// I-77 / Brookshire Freeway photos): the city solved once, then - in this
    /// order, each step on its own so one that throws costs only itself -
    ///   shots   the PSX_PREVIEW_CUSTOM views (CityPreview) at the owner's
    ///           spots, each PNG renamed &lt;name&gt;_&lt;tag&gt;.png in
    ///           Screenshots/City/bridges
    ///   stats   per-tile triangles and draws (every mesh a tile stands up,
    ///           submeshes counted as draws) on the interchange's tiles
    ///   flicker the flicker finder (CityFlickerFinder, its own spots; run
    ///           the job with -force-glcore)
    ///   drive   the drive audit (CityAudit.RunDrive) -> city_drive_&lt;tag&gt;.txt
    ///   launch  the launch audit, city-wide -> city_launch_&lt;tag&gt;.txt
    /// PSX_BR_TAG names the run (before / after), PSX_BR_STEPS picks steps
    /// (comma list, default all), PSX_BR_VIEWS overrides the views.
    /// Headless: -executeMethod PSXRacing.EditorTools.CityBridgesPass.Run
    /// Writes city_bridges_&lt;tag&gt;.txt (what ran, how long, the stats).
    /// </summary>
    public static class CityBridgesPass
    {
        /// <summary>name:eye:x:z:road:heading[:rise] or name:top:x:z:road:size
        /// (CityPreview's PSX_PREVIEW_CUSTOM). own1/own2 stand where the
        /// owner's two photos were taken: I-77 northbound's left lane before
        /// the left exit 11B to NC 16 / the Brookshire Freeway west.</summary>
        public const string DefaultViews =
            "br_own1:eye:-3004.2:6258.6:I-77:45:0:32" +
            ";br_own2:eye:-2930.5:6333.8:I-77:55:0:32" +
            ";br_flyover:eye:-2730:6472:I-77:40:0:50" +
            ";br_ramp:eye:-2722:6590::13:0:50" +
            ";br_viaduct277:eye:-2190:5925:Music Factory:105" +
            ";br_ictop:top:-2700:6520:I-77:330";
        /// <summary>The stats step's pick for the guardrail close-up (a W-beam
        /// piece near the interchange), appended to the views as br_rail.</summary>
        static string railView;

        static readonly Vector2 RailNear = new Vector2(-2722f, 6590f);
        static float railBest = 400f;
        static readonly (int tx, int tz)[] StatTiles =
        {
            (-12, 24), (-11, 24), (-12, 25), (-11, 25), (-11, 26), (-9, 23), (-9, 22),
        };

        public static void Run()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string tag = Environment.GetEnvironmentVariable("PSX_BR_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "run";
            string steps = Environment.GetEnvironmentVariable("PSX_BR_STEPS");
            if (string.IsNullOrWhiteSpace(steps)) steps = "stats,shotsab,flicker,drive,launch";
            var want = new HashSet<string>(steps.Split(','));
            var log = new StringBuilder();
            void Line(string s) { log.AppendLine(s); Debug.Log("[Bridges] " + s); File.WriteAllText(Path.Combine(root, $"city_bridges_{tag}.txt"), log.ToString()); }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Line($"CITY BRIDGES PASS ({tag}) steps {steps}, {DateTime.Now:s}, bridges {(CityMeshes.BridgeDetailOn ? "ON" : "OFF")}");
            var map = CityMap.Get();
            if (map == null) { Line("no city data"); Exit(); return; }
            Line($"solved in {clock.Elapsed.TotalSeconds:0} s");

            if (want.Contains("stats"))
                Step(Line, "stats", () =>
                {
                    var trims = CityMeshes.NodeTrims(map);
                    var buildings = CityBuildings.Precompute(map);
                    foreach (var (tx, tz) in StatTiles)
                    {
                        var t0 = clock.Elapsed.TotalMilliseconds;
                        var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                        double ms = clock.Elapsed.TotalMilliseconds - t0;
                        int draws = 0, tris = 0, colTris = 0;
                        var parts = new StringBuilder();
                        void Count(string what, Mesh m, bool collider)
                        {
                            if (m == null) return;
                            int t = 0;
                            for (int s = 0; s < m.subMeshCount; s++) t += (int)m.GetIndexCount(s) / 3;
                            draws += m.subMeshCount; tris += t;
                            if (collider) colTris += t;
                            parts.Append($" {what} {t}/{m.subMeshCount}");
                        }
                        Count("ground", tm.ground, true);
                        Count("roads", tm.roads, true);
                        Count("barriers", tm.barriers, true);
                        Count("kerbs", tm.kerbs, false);
                        Count("water", tm.water, false);
                        Count("bld", tm.buildings, true);
                        Count("structure", CityMeshes.StructureMeshOf(tm), false);
                        var wb = CityPoles.WBeamMesh(tm);
                        Count("wbeam", wb, false);
                        if (tm.guardrails != null) colTris += (int)tm.guardrails.GetIndexCount(0) / 3;
                        Line($"  tile {tx},{tz}: {tris} tris, {draws} draws (static meshes), collider tris {colTris}, solids {tm.solids.Count}, wbeam pieces {tm.wbeams.Count} posts {tm.wbeamPosts.Count}, masts {tm.mastFeet.Count}, lamps {tm.lamps.Count}, build {ms:0} ms |{parts}");
                        // the guardrail close-up: the full-height W-beam piece nearest the exit ramp
                        foreach (var w in tm.wbeams)
                        {
                            if (w.topA - w.botA < 0.2f || w.topA < w.a.y + 0.6f) continue;
                            var a = w.a + tm.origin; var b = w.b + tm.origin;
                            var along = new Vector2(b.x - a.x, b.z - a.z);
                            if (along.sqrMagnitude < 0.25f) continue;
                            along.Normalize();
                            float d = Vector2.Distance(new Vector2(a.x, a.z), RailNear);
                            if (d >= railBest) continue;
                            railBest = d;
                            var eye = new Vector2(a.x, a.z) - w.outA * 3.2f - along * 16f;
                            float hdg = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg;
                            railView = string.Format(System.Globalization.CultureInfo.InvariantCulture, "br_rail:eye:{0:0.0}:{1:0.0}::{2:0}:0:50", eye.x, eye.y, (hdg + 360f) % 360f);
                        }
                        if (wb != null) UnityEngine.Object.DestroyImmediate(wb);
                    }
                });

            void Shots(string as_)
            {
                string views = Environment.GetEnvironmentVariable("PSX_BR_VIEWS");
                if (string.IsNullOrWhiteSpace(views)) views = DefaultViews;
                if (railView != null) views += ";" + railView;
                Environment.SetEnvironmentVariable("PSX_PREVIEW_CUSTOM", views);
                Environment.SetEnvironmentVariable("PSX_PREVIEW_SPOTS", "custom");
                CityPreview.Run();
                string dir = Path.Combine(root, "Screenshots", "City");
                string outDir = Path.Combine(dir, "bridges");
                Directory.CreateDirectory(outDir);
                foreach (var item in views.Split(';'))
                {
                    string name = item.Split(':')[0].Trim();
                    string src = Path.Combine(dir, name + ".png");
                    if (!File.Exists(src)) { Line("  no shot " + name); continue; }
                    string dst = Path.Combine(outDir, $"{name}_{as_}.png");
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                    Line("  shot " + dst);
                }
            }
            if (want.Contains("shots")) Step(Line, "shots", () => Shots(tag));
            if (want.Contains("shotsab"))
                Step(Line, "shotsab", () =>
                {
                    // the same views with the pass off (main's structures) and on
                    bool was = CityMeshes.BridgeDetailOn;
                    try { CityMeshes.BridgeDetailOn = false; Shots("before"); CityMeshes.BridgeDetailOn = true; Shots("after"); }
                    finally { CityMeshes.BridgeDetailOn = was; }
                });

            if (want.Contains("flicker"))
                Step(Line, "flicker", () =>
                {
                    Environment.SetEnvironmentVariable("PSX_FLICK_TAG", "br_" + tag);
                    if (Environment.GetEnvironmentVariable("PSX_FLICK_PROXIES") == null) Environment.SetEnvironmentVariable("PSX_FLICK_PROXIES", "1");
                    CityFlickerFinder.Run();
                    string md = Path.Combine(root, "Docs", $"CITY_FLICKER_br_{tag}.md");
                    if (File.Exists(md))
                    {
                        int k = 0;
                        foreach (var l in File.ReadAllLines(md)) { if (k++ > 30) break; Line("  | " + l); }
                    }
                    else Line("  no flicker report " + md);
                });

            if (want.Contains("drive"))
                Step(Line, "drive", () =>
                {
                    CityAudit.RunDrive();
                    string src = Path.Combine(root, "city_drive.txt");
                    if (File.Exists(src)) File.Copy(src, Path.Combine(root, $"city_drive_{tag}.txt"), true);
                    foreach (var l in File.ReadAllLines(src))
                        if (l.Contains("FAIL") || l.Contains("WALLS IN LANES") || l.Contains("FALL") || l.Contains("INVISIBLE") || l.StartsWith("CITY AUDIT"))
                            Line("  | " + (l.Length > 260 ? l.Substring(0, 260) : l));
                });

            if (want.Contains("launch"))
                Step(Line, "launch", () =>
                {
                    Environment.SetEnvironmentVariable("PSX_LAUNCH_LABEL", "br_" + tag);
                    CityLaunchAudit.NoExit = true;
                    CityLaunchAudit.Run();
                    string src = Path.Combine(root, "city_launch.txt");
                    if (File.Exists(src))
                    {
                        File.Copy(src, Path.Combine(root, $"city_launch_{tag}.txt"), true);
                        int k = 0;
                        foreach (var l in File.ReadAllLines(src)) { if (k++ > 40) break; Line("  | " + (l.Length > 260 ? l.Substring(0, 260) : l)); }
                    }
                });

            Line($"CITY BRIDGES PASS DONE in {clock.Elapsed.TotalMinutes:0.0} min");
            Exit();
        }

        static void Step(Action<string> line, string name, Action body)
        {
            var c = System.Diagnostics.Stopwatch.StartNew();
            line($"[{name}] start");
            try { body(); line($"[{name}] done in {c.Elapsed.TotalSeconds:0} s"); }
            catch (Exception ex) { line($"[{name}] THREW {ex}"); Debug.LogException(ex); }
        }

        static void Exit()
        {
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
