using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FALL COLOUR SHOTS (the fallcolour round, 2026-11-23: the owner, "the
    /// yellows are wrong, the oranges are wrong, the reds are wrong, the browns
    /// are wrong"). PLAY mode, the real stage scene, the real player car stood
    /// still at named spots along the route (road metres from the path's first
    /// point, a lateral offset, a yaw), the chase camera settled behind it, and
    /// one frame of the game's own view per spot - for the swatch measurement
    /// (scratchpad leafmeasure.py) and the sheet beside his pictures.
    ///
    ///   -executeMethod PSXRacing.EditorTools.FallShots.Run     (no -quit: it plays)
    ///   PSX_FS_VENUE=LittleSwitzerland  PSX_FS_DAY=287  PSX_FS_HOUR=noon  PSX_FS_SKY=overcast|clear|partly
    ///   PSX_FS_SPOTS="name@metres@lateral@yaw;..."  PSX_FS_TAG=name  PSX_FS_VIEW=chase|cockpit|bumper
    /// Frames: Screenshots/fall/&lt;tag&gt;/&lt;venue&gt;_&lt;spot&gt;.png. Report: PSXRacing_fallshots_&lt;tag&gt;.txt.
    /// Hidden (batch mode with graphics) the clock is stepped 30 frames a game second.
    /// No WaitForEndOfFrame; a hard time limit ends it whatever happens.
    /// </summary>
    public static class FallShots
    {
        internal static StringBuilder log = new StringBuilder();
        internal static string tag, venue, view, spots;
        internal static double hardUntil;
        internal static bool done;

        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        public static void Run()
        {
            tag = Env("PSX_FS_TAG", "fall");
            venue = Env("PSX_FS_VENUE", "LittleSwitzerland");
            view = Env("PSX_FS_VIEW", "chase").ToLowerInvariant();
            spots = Env("PSX_FS_SPOTS", "s4240@4240@1.8@0");
            int day = int.Parse(Env("PSX_FS_DAY", "287"), CultureInfo.InvariantCulture);
            log = new StringBuilder($"FALL SHOTS {venue} ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; day {day} = {LifeSim.LifeRules.DateLabel(day)}; PSX_FALL2={Env("PSX_FALL2", "(default)")}; " +
                                    $"{(Application.isBatchMode ? "hidden (batch mode)" : "watched")}\n");
            done = false;
            hardUntil = EditorApplication.timeSinceStartup + 300.0 + spots.Split(';').Length * 40.0;
            EditorApplication.update += Watchdog;
            try
            {
                int index = TrackCatalog.IndexOf(venue);
                if (index < 0) throw new System.Exception("no venue " + venue);
                var def = TrackCatalog.At(index);
                string path = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
                if (!File.Exists(path)) throw new System.Exception("no scene " + path);
                Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "fall", tag));
                EditorSceneManager.OpenScene(path);
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.TrackIndex = index;
                RaceHandoff.CalendarDay = day;
                int hourIdx = TimeOfDay.Noon;
                string hour = Env("PSX_FS_HOUR", "noon");
                for (int h = 0; h < TimeOfDay.All.Length; h++)
                    if (string.Equals(TimeOfDay.All[h].name, hour, System.StringComparison.OrdinalIgnoreCase)) hourIdx = h;
                RaceHandoff.TimeOfDayIndex = hourIdx;
                RaceHandoff.WeatherOverride = (int)Weather.Clear;
                string sky = Env("PSX_FS_SKY", "overcast").ToLowerInvariant();
                RaceHandoff.SkyOverride = sky == "clear" ? (int)SkyKind.Clear : sky == "partly" ? (int)SkyKind.PartlyCloudy : (int)SkyKind.Overcast;
                RaceHandoff.Solo = true;
                RaceHandoff.TrafficLevel = TrafficLevels.None;
                var cars = CarCatalog.All;
                RaceHandoff.CarSpecId = cars[Mathf.Clamp(20, 0, cars.Count - 1)].id;
                log.AppendLine($"  hour {TimeOfDay.All[hourIdx].name}, sky {sky}, solo, no traffic, car {RaceHandoff.CarSpecId}, season {Seasons.Of(day)}, grade {(Env("PSX_GRADE", "1") == "0" ? "OFF" : "on")}");
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                EditorApplication.playModeStateChanged += OnState;
                EditorApplication.EnterPlaymode();
            }
            catch (System.Exception e)
            {
                log.AppendLine("FAILED before play: " + e);
                Finish(1);
            }
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
                new GameObject("FallShotsRunner").AddComponent<FallShotsRunner>();
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnState;
                Finish(done ? 0 : 2);
            }
        }

        static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < hardUntil) return;
            log.AppendLine("HARD TIME LIMIT: ended by the watchdog");
            Finish(3);
        }

        /// <summary>ITERATION ONLY (no scene build): the built FALL 2 leaf materials' brightness set to
        /// PSX_FALL2_GAIN (their far cards keep the sheets they were baked with).
        ///   -executeMethod PSXRacing.EditorTools.FallShots.Tune</summary>
        public static void Tune()
        {
            float g = float.Parse(Env("PSX_FALL2_GAIN", "1.3"), CultureInfo.InvariantCulture);
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material _fall2", new[] { "Assets/PSXRacing/Materials" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.name.StartsWith("TM_") || !m.HasProperty("_LeafFall") || m.GetVector("_LeafFall").x < 0.5f) continue;
                var v = m.GetVector("_LeafFall"); var tg = m.GetColor("_LeafTarget"); v.y = g * (tg.g > 0.45f && tg.b < 0.1f ? 1.15f : 1f); m.SetVector("_LeafFall", v); EditorUtility.SetDirty(m); n++;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/PSXRacing/Materials" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.HasProperty("_BlendParams")) continue;
                var bp = m.GetVector("_BlendParams");
                if (Mathf.Abs(bp.z - 0.58f) < 0.001f && bp.w < 0.5f) { bp.w = 1f; m.SetVector("_BlendParams", bp); EditorUtility.SetDirty(m); Debug.Log("[FallTune] verge one-octave: " + m.name); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FallTune] {n} fall leaf materials at brightness x{g}");
        }

        internal static void Finish(int code)
        {
            EditorApplication.update -= Watchdog;
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_fallshots_" + tag + ".txt"), log.ToString());
            Debug.Log("[FallShots]\n" + log);
            EditorApplication.Exit(code);
        }
    }

    // (after every camera script of the frame, and after the forests queued their draws)
    [DefaultExecutionOrder(20000)]
    public class FallShotsRunner : MonoBehaviour
    {
        RaceManager rm;
        CarController car;
        bool shoot;
        string shotName;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            if (rm == null || car == null || rm.path == null) { FallShots.log.AppendLine("  NO race, player or path in the scene"); End(); yield break; }
            if (Application.isBatchMode) { QualitySettings.vSyncCount = 0; Time.captureFramerate = 30; }
            float until = Time.realtimeSinceStartup + 30f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < until) yield return null;
            FallShots.log.AppendLine($"  dress {Seasons.DressNames[Mathf.Clamp(SeasonDress.AppliedDress, 0, Seasons.DressCount - 1)]}; path {rm.path.waypoints.Length} points x {rm.path.spacing:0.0} m");
            if (FallShots.view == "cockpit") ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            else ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var body = car.GetComponent<Rigidbody>();
            int n = 0;
            // PSX_FS_FINDVISTA=1: where the land falls away beside the road (an overlook): the drop from the
            // road to the lowest ground 60-240 m to each side, every 40 m, the best ten logged
            if (System.Environment.GetEnvironmentVariable("PSX_FS_FINDVISTA") == "1")
            {
                var best = new System.Collections.Generic.List<(float drop, int idx, int side)>();
                for (int i = 0; i < rm.path.waypoints.Length; i += 10)
                {
                    var p0 = rm.path.GetPoint(i);
                    var rgt = rm.path.GetRotation(i) * Vector3.right;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float low = float.MaxValue;
                        foreach (float d in new[] { 60f, 120f, 180f, 240f })
                        {
                            var q = p0 + rgt * (side * d);
                            if (Physics.Raycast(q + Vector3.up * 600f, Vector3.down, out var h2, 1500f, ~0, QueryTriggerInteraction.Ignore)) low = Mathf.Min(low, h2.point.y);
                        }
                        if (low < float.MaxValue) best.Add((p0.y - low, i, side));
                    }
                }
                best.Sort((a, b) => b.drop.CompareTo(a.drop));
                var sbv = new StringBuilder("  VISTAS (road m, side, drop m):");
                var used = new System.Collections.Generic.List<int>();
                foreach (var b in best)
                {
                    bool near = false; foreach (var u in used) near |= Mathf.Abs(u - b.idx) < 60;
                    if (near) continue;
                    used.Add(b.idx);
                    sbv.Append($" {b.idx * rm.path.spacing:0}{(b.side > 0 ? "R" : "L")}:{b.drop:0}");
                    if (used.Count >= 12) break;
                }
                FallShots.log.AppendLine(sbv.ToString());
            }
            foreach (var raw in FallShots.spots.Split(';'))
            {
                var f = raw.Trim().Split('@');
                if (f.Length < 2) continue;
                var ci = CultureInfo.InvariantCulture;
                float metres = float.Parse(f[1], ci);
                float lateral = f.Length > 2 ? float.Parse(f[2], ci) : 1.8f;
                float yaw = f.Length > 3 ? float.Parse(f[3], ci) : 0f;
                int idx = rm.path.Wrap(Mathf.RoundToInt(metres / rm.path.spacing));
                var p = rm.path.GetPoint(idx);
                var rot = rm.path.GetRotation(idx) * Quaternion.Euler(0f, yaw, 0f);
                var right = rm.path.GetRotation(idx) * Vector3.right;
                var pos = p + right * lateral;
                if (Physics.Raycast(pos + Vector3.up * 6f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                pos += Vector3.up * 0.6f;
                // stand it there, let it settle and the chase camera come round behind it (2.5 game s)
                for (int k = 0; k < 75; k++)
                {
                    if (k < 20 || (body != null && body.linearVelocity.magnitude > 0.5f && k < 40)) car.TeleportTo(pos, rot);
                    yield return null;
                }
                shotName = $"fall/{FallShots.tag}/{FallShots.venue}_{f[0]}";
                shoot = true;
                yield return null;
                yield return null;
                FallShots.log.AppendLine($"  {f[0]}: road {metres:0} m (point {idx}) lateral {lateral:0.0} yaw {yaw:0}; at ({pos.x:0}, {pos.y:0}, {pos.z:0})");
                n++;
            }
            if (System.Environment.GetEnvironmentVariable("PSX_FS_COST") == "1") yield return CostAB();
            FallShots.done = true;
            FallShots.log.AppendLine($"{n} frames\nFALL SHOTS OK");
            End();
        }

        // ---- THE COST (PSX_FS_COST=1): at the last spot, the main camera drawn at the owner's 3832x2054
        // through the pipeline, in blocks of renders timed to a read-back, ABBA: A = FALL 2 as built and
        // live; B = its run-time pieces off (the phase / glow / road leaves global, the leaf recolour's
        // strength, the verge's leaf patches); and a null A/A to show the noise. Editor numbers.
        System.Collections.Generic.List<(Material m, float w)> leafMats;
        System.Collections.Generic.List<Material> blendMats, roadMats;
        string piece;
        Vector4 phaseOn;

        void Collect()
        {
            leafMats = new System.Collections.Generic.List<(Material, float)>();
            blendMats = new System.Collections.Generic.List<Material>();
            roadMats = new System.Collections.Generic.List<Material>();
            piece = System.Environment.GetEnvironmentVariable("PSX_FS_COSTPIECE") ?? "all";
            var seen = new System.Collections.Generic.HashSet<Material>();
            void Add(Material m)
            {
                if (m == null || !seen.Add(m)) return;
                if (m.HasProperty("_LeafFall") && m.GetVector("_LeafFall").x > 0.5f) leafMats.Add((m, m.GetVector("_LeafVivid").w));
                if (m.HasProperty("_BlendParams") && m.IsKeywordEnabled("PSX_GROUNDBLEND") && Mathf.Abs(m.GetVector("_BlendParams").z - 0.58f) < 0.001f) blendMats.Add(m);   // the verge's new patches only (the floor had its own before)
                if (m.HasProperty("_RoadLeaves") && m.GetVector("_RoadLeaves").x > 0.5f) roadMats.Add(m);
            }
            foreach (var f in FindObjectsByType<MeshForest>(FindObjectsInactive.Exclude))
                if (f.variants != null)
                    foreach (var v in f.variants)
                    {
                        if (v == null) continue;
                        if (v.leafDress != null) foreach (var m in v.leafDress) Add(m);
                        if (v.impostorDress != null) foreach (var m in v.impostorDress) Add(m);
                        if (v.matchDress != null) foreach (var m in v.matchDress) Add(m);
                    }
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                foreach (var m in r.sharedMaterials) Add(m);
            phaseOn = Shader.GetGlobalVector("_PSXFallPhase");
        }

        void SetSide(bool on)
        {
            bool all = piece == "all";
            if (all || piece == "glow") Shader.SetGlobalVector("_PSXFallPhase", on ? phaseOn : new Vector4(0f, phaseOn.y, 0f, 0f));
            if (all || piece == "recolour") foreach (var (m, w) in leafMats) { var lv = m.GetVector("_LeafVivid"); lv.w = on ? w : 0f; m.SetVector("_LeafVivid", lv); }
            if (all || piece == "verge") foreach (var m in blendMats) { if (on) m.EnableKeyword("PSX_GROUNDBLEND"); else m.DisableKeyword("PSX_GROUNDBLEND"); }
            if (piece == "road") foreach (var m in roadMats) { var r = m.GetVector("_RoadLeaves"); r.x = on ? 1f : 0f; m.SetVector("_RoadLeaves", r); }
        }

        IEnumerator CostAB()
        {
            Collect();
            var cam = Camera.main;
            if (cam == null) yield break;
            int rounds = 16, per = 12;
            var rt = new RenderTexture(3832, 2054, 32, RenderTextureFormat.ARGB32);
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            double Block()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int k = 0; k < per; k++)
                {
                    var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                }
                var prev = RenderTexture.active; RenderTexture.active = rt;
                one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); one.Apply();
                RenderTexture.active = prev;
                return sw.Elapsed.TotalMilliseconds / per;
            }
            // warm both sides
            SetSide(false); Block(); SetSide(true); Block(); Block();
            var dAB = new System.Collections.Generic.List<double>(); var dAA = new System.Collections.Generic.List<double>();
            double sumA = 0;
            for (int r = 0; r < rounds; r++)
            {
                SetSide(true); double a1 = Block();
                SetSide(false); double b1 = Block(); double b2 = Block();
                SetSide(true); double a2 = Block();
                double a3 = Block();
                dAB.Add((a1 + a2) * 0.5 - (b1 + b2) * 0.5);
                dAA.Add(a3 - a2);
                sumA += (a1 + a2) * 0.5;
                yield return null;
            }
            SetSide(true);
            double Mean(System.Collections.Generic.List<double> l) { double s = 0; foreach (var x in l) s += x; return s / l.Count; }
            double Med(System.Collections.Generic.List<double> l) { var c = new System.Collections.Generic.List<double>(l); c.Sort(); return c[c.Count / 2]; }
            double Sd(System.Collections.Generic.List<double> l) { double m = Mean(l), s = 0; foreach (var x in l) s += (x - m) * (x - m); return System.Math.Sqrt(s / System.Math.Max(1, l.Count - 1)); }
            FallShots.log.AppendLine($"  COST (editor, 3832x2054, {rounds} ABBA rounds x {per} renders): frame A {sumA / rounds:0.00} ms; " +
                $"FALL2 run-time pieces A-B {Mean(dAB):+0.00;-0.00} +- {Sd(dAB) / System.Math.Sqrt(rounds):0.00} ms (sd {Sd(dAB):0.00}, median {Med(dAB):+0.00;-0.00}); null A-A {Mean(dAA):+0.00;-0.00} +- {Sd(dAA) / System.Math.Sqrt(rounds):0.00} ms (median {Med(dAA):+0.00;-0.00}); " +
                $"{leafMats.Count} fall leaf materials, {blendMats.Count} verge leaf-patch materials, {roadMats.Count} road materials; piece '{piece}'");
            rt.Release(); Destroy(rt); Destroy(one);
        }

        void LateUpdate()
        {
            if (!shoot) return;
            shoot = false;
            var cam = Camera.main;
            if (cam == null) return;
            PSXScreenshotTool.ShotAs(cam, shotName, cam.transform.position, cam.transform.rotation);
        }

        void End()
        {
            Time.captureFramerate = 0;
            EditorApplication.ExitPlaymode();
        }
    }
}
