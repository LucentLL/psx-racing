using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;
using Debug = UnityEngine.Debug;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE RAINLOOK ROUND'S FRAMES (2026-11-27): OLD | NEW of the round's LAB rows from one pose, in a hidden play
    /// check (batch mode, muted), and their cost in alternating blocks inside the same process.
    ///   RL_PLACE = city (Charlotte free roam, West Trade Street) | a stage id (its start)
    ///   RL_HOUR = Night | Noon | Dusk ...   RL_WEATHER = Rain | Clear   RL_DAY = calendar day (0 = the build's)
    ///   RL_VIEWS = Chase,Cockpit   RL_ROWS = softruns,wiperglow,...   RL_TAG   RL_OUT (folder)
    ///   RL_FROM = metres down the Trade Street route (city), RL_COST = reps (0 = no cost blocks)
    ///   Unity.exe -batchmode -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.RainLookShots.Run (no -quit)
    /// Per view: EDITOR_&lt;tag&gt;_&lt;view&gt;_NEW.png, _OLD.png (every listed row OLD), _OLD_&lt;row&gt;.png (that row alone),
    /// _PAIR.png (OLD left | NEW right, half size), and &lt;tag&gt;_report.txt (the cost table, the muted line count).
    /// </summary>
    public static class RainLookShots
    {
        internal static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
        internal static string Place => Env("RL_PLACE", "city");

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            bool city = Place.ToLowerInvariant() == "city";
            int vi = TrackCatalog.IndexOf(city ? "Charlotte" : Place);
            var scenes = EditorBuildSettings.scenes;
            int s = vi >= 0 ? TrackCatalog.SceneIndex(vi) : -1;
            if (vi < 0 || s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Debug.LogError("[RainLookShots] no built scene for " + Place);
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = city;
            RaceHandoff.TrackIndex = vi;
            RaceHandoff.Solo = true;
            RaceHandoff.TrafficLevel = TrafficLevels.None;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.StartFuelPct = 100f;
            string hour = Env("RL_HOUR", "Night");
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Night;
            for (int k = 0; k < TimeOfDay.All.Length; k++)
                if (string.Equals(TimeOfDay.All[k].name, hour, System.StringComparison.OrdinalIgnoreCase)) RaceHandoff.TimeOfDayIndex = k;
            if (System.Enum.TryParse(Env("RL_WEATHER", "Rain"), true, out Weather w)) RaceHandoff.WeatherOverride = (int)w;
            if (int.TryParse(Env("RL_DAY", "0"), out int day) && day > 0) RaceHandoff.CalendarDay = day;
            // (round 2) RL_FIELD=1 on a stage: the arcade's field of rivals, as his race (eighth of eight)
            if (!city && Env("RL_FIELD", "0") == "1")
            {
                var spec = CarCatalog.Get(PSXRacing.LifeSim.CityFrontEnd.DefaultCarId());
                if (spec != null)
                {
                    ArcadeMode.Simulate(true);
                    RaceHandoff.CarSpecId = spec.id;
                    RaceHandoff.Solo = false;
                    ArcadeMode.FillField(spec, ArcadeMode.RivalsAt(TrackCatalog.At(vi)));
                }
            }
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>(round 2) EDIT MODE, before any static batching: the stage road mesh's metres per UV unit, along
        /// (V) and across (U), at the course's start and RL_AT metres on, against what the road texture and the leaf
        /// layer take them to be (RoadVTile along, the road width across; _RoadLeaves.zy). RL_STAGES = ids.
        /// Writes &lt;RL_OUT&gt;/road_uv.txt.</summary>
        public static void MeasureRoadUV()
        {
            var sb = new StringBuilder();
            string outDir = Env("RL_OUT", Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "rainlook"));
            Directory.CreateDirectory(outDir);
            try
            {
                foreach (var id in Env("RL_STAGES", "BlueRidge,LakeLure").Split(','))
                {
                    int vi = TrackCatalog.IndexOf(id.Trim());
                    int s = vi >= 0 ? TrackCatalog.SceneIndex(vi) : -1;
                    var scenes = EditorBuildSettings.scenes;
                    if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { sb.AppendLine(id + ": no scene"); continue; }
                    EditorSceneManager.OpenScene(scenes[s].path);
                    var path = Object.FindFirstObjectByType<TrackPath>();
                    sb.AppendLine($"== {id} ({scenes[s].path}), road width {TrackCatalog.At(vi).roadWidth} m, RoadVTile {CityMeshes.RoadVTile} m");
                    // EVERY mesh drawn with a leaf-carrying road material: per triangle, metres per U and per V unit
                    // (the UV gradient solved), within 80 m of the course's first point and everywhere
                    if (path != null)
                    {
                        var p0w = path.GetPoint(0);
                        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
                        {
                            var mfx = mr.GetComponent<MeshFilter>();
                            if (mfx == null || mfx.sharedMesh == null) continue;
                            var mats = mr.sharedMaterials; var mesh = mfx.sharedMesh;
                            for (int sm = 0; sm < mesh.subMeshCount && sm < mats.Length; sm++)
                            {
                                var mat = mats[sm];
                                if (mat == null || !mat.HasProperty("_RoadLeaves") || mat.GetVector("_RoadLeaves").x < 0.5f) continue;
                                var vv = mesh.vertices; var uu = mesh.uv; var tri = mesh.GetTriangles(sm); var trf = mr.transform;
                                if (uu.Length != vv.Length) continue;
                                var nearV = new System.Collections.Generic.List<float>(); var nearU = new System.Collections.Generic.List<float>();
                                var allV = new System.Collections.Generic.List<float>();
                                for (int t = 0; t + 2 < tri.Length; t += 3)
                                {
                                    Vector3 a = trf.TransformPoint(vv[tri[t]]), b2 = trf.TransformPoint(vv[tri[t + 1]]), c = trf.TransformPoint(vv[tri[t + 2]]);
                                    Vector2 ua = uu[tri[t]], ub = uu[tri[t + 1]], uc = uu[tri[t + 2]];
                                    Vector3 e1 = b2 - a, e2 = c - a; Vector2 d1 = ub - ua, d2 = uc - ua;
                                    float det = d1.x * d2.y - d2.x * d1.y;
                                    if (Mathf.Abs(det) < 1e-9f) continue;
                                    Vector3 dPdu = (e1 * d2.y - e2 * d1.y) / det, dPdv = (e2 * d1.x - e1 * d2.x) / det;
                                    float mv = dPdv.magnitude, mu = dPdu.magnitude;
                                    allV.Add(mv);
                                    if (((a + b2 + c) / 3f - p0w).magnitude < 80f) { nearV.Add(mv); nearU.Add(mu); }
                                }
                                nearV.Sort(); nearU.Sort(); allV.Sort();
                                string Q(System.Collections.Generic.List<float> l) => l.Count == 0 ? "-" : $"min {l[0]:0.00} median {l[l.Count / 2]:0.00} max {l[l.Count - 1]:0.00} ({l.Count} tris)";
                                sb.AppendLine($"  {mr.gameObject.name}/{mesh.name} submesh {sm} ({mat.name}, _RoadLeaves {mat.GetVector("_RoadLeaves")}):");
                                sb.AppendLine($"    within 80 m of the course start: metres per V {Q(nearV)}; metres per U {Q(nearU)}");
                                sb.AppendLine($"    whole: metres per V {Q(allV)}");
                            }
                        }
                    }
                    foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
                    {
                        var m = mf.sharedMesh;
                        if (m == null || !(mf.gameObject.name == "Road" || m.name.StartsWith("RoadMesh"))) continue;
                        var vs = m.vertices; var us = m.uv;
                        var mr = mf.GetComponent<MeshRenderer>();
                        string leaves = "";
                        if (mr != null) foreach (var mat in mr.sharedMaterials) if (mat != null && mat.HasProperty("_RoadLeaves")) leaves += " " + mat.name + " _RoadLeaves " + mat.GetVector("_RoadLeaves") + " _MainTex_ST " + mat.GetVector("_MainTex_ST") + ";";
                        sb.AppendLine($"  mesh {mf.gameObject.name}/{m.name}: {vs.Length} verts, uv {us.Length};{leaves}");
                        if (vs.Length < 9 || us.Length != vs.Length || path == null) continue;
                        var tr = mf.transform;
                        float[] targets = { 0f, float.Parse(Env("RL_AT", "200"), CultureInfo.InvariantCulture), 400f, 800f };
                        var allAlong = new System.Collections.Generic.List<float>();
                        for (int k = 0; k + 5 < vs.Length; k += 3)
                        {
                            float dv = Mathf.Abs(us[k + 4].y - us[k + 1].y);
                            if (dv > 1e-5f) allAlong.Add(Vector3.Distance(tr.TransformPoint(vs[k + 4]), tr.TransformPoint(vs[k + 1])) / dv);
                        }
                        allAlong.Sort();
                        if (allAlong.Count > 0)
                            sb.AppendLine($"    whole road, metres per V unit: min {allAlong[0]:0.00} p5 {allAlong[allAlong.Count / 20]:0.00} median {allAlong[allAlong.Count / 2]:0.00} p95 {allAlong[allAlong.Count * 19 / 20]:0.00} max {allAlong[allAlong.Count - 1]:0.00}");
                        foreach (float at in targets)
                        {
                            int pi = Mathf.Clamp(Mathf.RoundToInt(at / Mathf.Max(0.5f, path.spacing)), 0, path.Count - 1);
                            var p = path.GetPoint(pi);
                            int best = -1; float bd = float.MaxValue;
                            for (int k = 0; k + 2 < vs.Length; k += 3) { float d = (tr.TransformPoint(vs[k + 1]) - p).sqrMagnitude; if (d < bd) { bd = d; best = k; } }
                            if (best < 0 || best + 5 >= vs.Length) continue;
                            float dvA = Mathf.Abs(us[best + 4].y - us[best + 1].y);
                            float along = Vector3.Distance(tr.TransformPoint(vs[best + 4]), tr.TransformPoint(vs[best + 1]));
                            float across = Vector3.Distance(tr.TransformPoint(vs[best]), tr.TransformPoint(vs[best + 2]));
                            float du = Mathf.Abs(us[best + 2].x - us[best].x);
                            sb.AppendLine($"    at {at:0} m (ring {best / 3}, {Mathf.Sqrt(bd):0.0} m off the path): along {along:0.000} m over dV {dvA:0.0000} = {(dvA > 1e-6f ? along / dvA : 0f):0.00} m per V unit; across {across:0.00} m over dU {du:0.000} = {(du > 1e-6f ? across / du : 0f):0.00} m per U unit");
                        }
                    }
                }
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
            File.WriteAllText(Path.Combine(outDir, "road_uv.txt"), sb.ToString());
            Debug.Log("[RainLookShots] road uv:\n" + sb);
            EditorApplication.Exit(0);
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("RainLookShotsRunner").AddComponent<RainLookShotsRunner>();
        }
    }

    public class RainLookShotsRunner : MonoBehaviour
    {
        readonly StringBuilder rep = new StringBuilder();
        float hardStop;
        int muted;
        string outDir, tag;

        void Update()
        {
            if (hardStop > 0f && Time.realtimeSinceStartup > hardStop) { Say("TIMEOUT"); Flush(); EditorApplication.Exit(1); }
        }

        void Say(string s) { rep.AppendLine(s); Debug.Log("[RainLookShots] " + s); }
        void Flush() { try { File.WriteAllText(Path.Combine(outDir, tag + "_report.txt"), rep.ToString()); } catch { } }

        void OnLog(string msg, string stack, LogType type)
        {
            if (msg != null && msg.Contains("[MasterOut] test run: the sound is muted")) muted++;
        }

        void Awake() { Application.logMessageReceived += OnLog; }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            hardStop = Time.realtimeSinceStartup + 900f;
            tag = RainLookShots.Env("RL_TAG", RainLookShots.Place);
            outDir = RainLookShots.Env("RL_OUT", Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "rainlook"));
            Directory.CreateDirectory(outDir);
            CarController car = null;
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 200f;)
            {
                if (CityMode.Instance != null && CityMode.Instance.player != null && CityMode.Instance.Live) car = CityMode.Instance.player;
                else if (RaceManager.Instance != null && RaceManager.Instance.playerCar != null) car = RaceManager.Instance.playerCar;
                if (car != null && Camera.main != null) break;
                yield return null;
            }
            if (car == null || Camera.main == null) { Say("FAIL: no car / camera at " + RainLookShots.Place); Flush(); EditorApplication.Exit(1); yield break; }

            // the city: stood on West Trade Street (the perf tour's "trade" route, RL_FROM metres down it)
            bool standSet = false; Vector3 standPos = Vector3.zero; Quaternion standRot = Quaternion.identity;
            if (CityMode.Instance != null && CityMode.Instance.world != null)
            {
                var map = CityMode.Instance.world.Map;
                var route = PerfTour.RouteFor(map, "trade");
                if (route != null)
                {
                    var pgo = new GameObject("RainLookPath");
                    var path = pgo.AddComponent<TrackPath>();
                    CityMode.BuildPath(map, route, path);
                    float.TryParse(RainLookShots.Env("RL_FROM", "400"), NumberStyles.Float, CultureInfo.InvariantCulture, out float from);
                    int i0 = Mathf.Clamp(Mathf.RoundToInt(from / Mathf.Max(0.5f, path.spacing)), 2, Mathf.Max(2, path.Count - 40));
                    standPos = path.GetPoint(i0) + Vector3.up * 0.45f;
                    var tg = path.GetTangent(i0); tg.y = 0f;
                    standRot = Quaternion.LookRotation(tg.normalized, Vector3.up);
                    CityMode.Instance.world.EnsureRing(standPos, 1);
                    car.ResetTo(standPos, standRot, seated: true);
                    standSet = true;
                }
            }
            else
            {
                // a stage: stood on the road's centre line RL_FROM metres from its start, facing along it
                var path = FindFirstObjectByType<TrackPath>();
                if (path != null && path.Count > 10 && RainLookShots.Env("RL_STAND", "1") == "1")
                {
                    float.TryParse(RainLookShots.Env("RL_FROM", "30"), NumberStyles.Float, CultureInfo.InvariantCulture, out float from);
                    int i0 = Mathf.Clamp(Mathf.RoundToInt(from / Mathf.Max(0.5f, path.spacing)), 1, path.Count - 2);
                    var side = Vector3.Cross(Vector3.up, path.GetTangent(i0)).normalized;
                    standPos = path.GetPoint(i0) + side * 1.6f + Vector3.up * 0.45f;   // the right-hand lane
                    var tg = path.GetTangent(i0); tg.y = 0f;
                    standRot = Quaternion.LookRotation(tg.normalized, Vector3.up);
                    car.ResetTo(standPos, standRot, seated: true);
                    standSet = true;
                }
                // (round 2) RL_TURN=1: where the grid put the car, turned to face back past the start line
                if (RainLookShots.Env("RL_TURN", "0") == "1")
                {
                    standPos = car.transform.position + Vector3.up * 0.3f;
                    standRot = Quaternion.LookRotation(-new Vector3(car.transform.forward.x, 0f, car.transform.forward.z).normalized, Vector3.up);
                    car.ResetTo(standPos, standRot, seated: true);
                    standSet = true;
                }
            }
            var pin = car.GetComponent<PlayerCarInput>(); if (pin != null) { pin.inputEnabled = false; pin.enabled = false; }
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 20f;)
            {
                car.throttleInput = 0f; car.brakeInput = 1f;
                if (standSet) car.ResetTo(standPos, standRot, seated: true);
                yield return null;
            }
            // (round 2) RL_AFTER=s: that many seconds after the green, the player held on the grid (his frame)
            if (float.TryParse(RainLookShots.Env("RL_AFTER", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float after) && RaceManager.Instance != null)
            {
                for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 90f && RaceManager.Instance.State != RaceManager.RaceState.Racing;)
                { car.throttleInput = 0f; car.brakeInput = 1f; yield return null; }
                for (float t0 = Time.time; Time.time - t0 < after;) { car.throttleInput = 0f; car.brakeInput = 1f; yield return null; }
                Say("after the green: " + after + " s, the race " + RaceManager.Instance.State);
            }
            // (round 3) RL_BLURLOG=1: the speed blur's inputs and output, logged in each standing state:
            // held on the brake, wheels spun on the brakes (throttle open), just after a reset to the road
            if (RainLookShots.Env("RL_BLURLOG", "0") == "1")
            {
                if (System.Enum.TryParse(RainLookShots.Env("RL_BLURMODE", ""), true, out SpeedBlurMode bm)) SpeedBlurPrefs.Forced = bm;
                var sb = Camera.main != null ? Camera.main.GetComponent<SpeedBlur>() : null;
                var shownF = typeof(SpeedBlur).GetField("shownMps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                void Log(string state)
                {
                    var bc = sb != null ? sb.car : null;
                    Say($"BLUR {state} t {Time.time:0.00}: player {car.name} v {car.forwardSpeed:0.00} m/s ({car.speedKmh:0.0} km/h), " +
                        $"blur's car {(bc != null ? bc.name : "none")}{(bc == car ? " (= player)" : " (NOT the player)")} v {(bc != null ? bc.forwardSpeed : 0f):0.00}, " +
                        $"shown {(sb != null && shownF != null ? (float)shownF.GetValue(sb) : -1f):0.00} m/s, strength {SpeedBlur.ActiveStrength:0.000}, shutter {SpeedBlur.ActiveShutter:0.000}, mode {SpeedBlurPrefs.Mode}, cam {(SpeedBlur.ActiveCamera != null ? SpeedBlur.ActiveCamera.name : "none")}");
                }
                for (float t0 = Time.time; Time.time - t0 < 4f;) { car.throttleInput = 0f; car.brakeInput = 1f; car.handbrakeInput = false; if (Time.frameCount % 30 == 0) Log("held"); yield return null; }
                for (float t0 = Time.time; Time.time - t0 < 5f;) { car.throttleInput = 1f; car.brakeInput = 1f; car.handbrakeInput = true; if (Time.frameCount % 30 == 0) Log("spin"); yield return null; }
                car.throttleInput = 0f; car.handbrakeInput = false;
                var rp = car.transform.position + Vector3.up * 0.3f; var rr = car.transform.rotation;
                car.ResetTo(rp, rr, seated: true);
                for (float t0 = Time.time; Time.time - t0 < 3f;) { car.throttleInput = 0f; car.brakeInput = 1f; if (Time.frameCount % 15 == 0) Log("reset"); yield return null; }
                if (RainLookShots.Env("RL_BLURSTATE", "held") == "spin")
                    for (float t0 = Time.time; Time.time - t0 < 3f;) { car.throttleInput = 1f; car.brakeInput = 1f; car.handbrakeInput = true; yield return null; }
                Log("shot");
            }
            Say($"RAINLOOK {tag}: place {RainLookShots.Place} hour {TimeOfDay.Current} weather {Seasons.CurrentWeather} dress {SeasonDress.AppliedDress} at {car.transform.position.x:0},{car.transform.position.z:0} {System.DateTime.Now:yyyy-MM-dd HH:mm}");

            // THE ROAD'S OWN STRETCH (a stage): the road mesh's V (x RoadVTile metres) against the metres its
            // centre line really runs, ring to ring (stride 3: left, centre, right)
            if (CityMode.Instance == null)
                foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
                {
                    if (mf.sharedMesh == null || !(mf.gameObject.name == "Road" || mf.sharedMesh.name.StartsWith("RoadMesh"))) continue;
                    Say("road mesh " + mf.gameObject.name + " / " + mf.sharedMesh.name + " readable " + mf.sharedMesh.isReadable + " verts " + mf.sharedMesh.vertexCount);
                    var vs = mf.sharedMesh.vertices; var us = mf.sharedMesh.uv;
                    if (vs.Length < 9 || us.Length != vs.Length) continue;
                    var ratios = new System.Collections.Generic.List<float>();
                    float near = 0f; int nearN = 0;
                    var tr = mf.transform;
                    for (int k = 0; k + 5 < vs.Length; k += 3)
                    {
                        float dv = (us[k + 4].y - us[k + 1].y) * CityMeshes.RoadVTile;
                        float dm = Vector3.Distance(tr.TransformPoint(vs[k + 4]), tr.TransformPoint(vs[k + 1]));
                        if (dv <= 1e-4f || dm <= 1e-4f) continue;
                        float ratio = dm / dv;
                        ratios.Add(ratio);
                        if (Vector3.Distance(tr.TransformPoint(vs[k + 1]), car.transform.position) < 60f) { near += ratio; nearN++; }
                    }
                    ratios.Sort();
                    if (ratios.Count > 0)
                        Say($"ROAD STRETCH {mf.gameObject.scene.name}: real metres per textured metre along the road, {ratios.Count} ring gaps: " +
                            $"min {ratios[0]:0.000} p5 {ratios[ratios.Count / 20]:0.000} median {ratios[ratios.Count / 2]:0.000} p95 {ratios[ratios.Count * 19 / 20]:0.000} max {ratios[ratios.Count - 1]:0.000}; " +
                            $"within 60 m of the car {(nearN > 0 ? (near / nearN).ToString("0.000") : "-")} ({nearN} gaps)");
                }

            var rows = new System.Collections.Generic.List<LabRow>();
            foreach (var id in RainLookShots.Env("RL_ROWS", "softruns,wiperglow,cabinmask,roadleaves,duskleaves").Split(','))
                foreach (var r in LabSwitches.Rows()) if (r.id == id.Trim()) rows.Add(r);
            Say("rows: " + string.Join(", ", rows.ConvertAll(r => r.id + "=" + r.StateWord(r.Chosen) + (r.Present ? "" : " (absent)"))));

            const int W = 1920, H = 1080;
            var full = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1, name = "RainLookFull" };
            full.Create();
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1, name = "RainLookScreen" };
            screen.Create();
            var holder = new GameObject("RainLookView");
            holder.SetActive(false);
            var view = holder.AddComponent<RainLookView>();
            view.cam = Camera.main; view.rt = full; view.screen = screen;
            var outp = Camera.main.GetComponent<PSXCameraOutput>();
            if (outp != null && outp.display != null) { view.blitMat = outp.display.material; view.blit = true; }
            holder.SetActive(true);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            RenderTexture Pic() => view.blit && view.blitMat != null ? screen : full;

            void SetAll(bool v) { foreach (var r in rows) LabSwitches.Set(r, v, false); }
            byte[] Grab()
            {
                var prev = RenderTexture.active;
                RenderTexture.active = Pic();
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = prev;
                return tex.EncodeToPNG();
            }
            IEnumerator Settle() { for (int i = 0; i < 4; i++) yield return null; float t1 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t1 < 0.3f) yield return null; }

            foreach (var vname in RainLookShots.Env("RL_VIEWS", "Chase").Split(','))
            {
                if (!System.Enum.TryParse(vname.Trim(), true, out ChaseCamera.View v)) continue;
                Time.timeScale = 1f; VolumetricClouds.Paused = false;
                ChaseCamera.PreviewView(v);
                for (float t1 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t1 < 4f;) { if (standSet) car.ResetTo(standPos, standRot, seated: true); car.brakeInput = 1f; yield return null; }
                // the wipers mid-stroke (cockpit in rain): wait for the stroke's middle, at most 6 s
                if (v == ChaseCamera.View.Cockpit)
                    for (float t1 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t1 < 6f;)
                    {
                        float ws = CockpitInterior.WiperS;
                        if (ws > 0.30f && ws < 0.55f) break;
                        yield return null;
                    }
                Time.timeScale = 0f; VolumetricClouds.Paused = true;
                yield return Settle();
                string b = Path.Combine(outDir, "EDITOR_" + tag + "_" + v);
                SetAll(true); yield return Settle();
                var pNew = Grab(); File.WriteAllBytes(b + "_NEW.png", pNew);
                // (round 2) THE ID PASS (PSX/Lit _PSXIdMask): r foliage, g paved ground, b any PSX/Lit surface
                Shader.SetGlobalFloat("_PSXIdMask", 1f); yield return Settle();
                { var prev = RenderTexture.active; RenderTexture.active = Pic(); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); tex.Apply(false); RenderTexture.active = prev;
                  File.WriteAllBytes(b + "_ID.png", tex.EncodeToPNG()); }
                if (RainLookShots.Env("RL_DIAG", "0") == "1")
                    foreach (var (mode, suffix) in new[] { (2f, "_LIGHT"), (3f, "_EMIT") })
                        for (int pass = 0; pass < 2; pass++)
                        {
                            // NEW, then the duskleaves row alone OLD
                            foreach (var r in rows) if (r.id == "duskleaves") LabSwitches.Set(r, pass == 0, false);
                            Shader.SetGlobalFloat("_PSXIdMask", mode); yield return Settle();
                            var prev = RenderTexture.active; RenderTexture.active = Pic(); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); tex.Apply(false); RenderTexture.active = prev;
                            File.WriteAllBytes(b + suffix + (pass == 0 ? "_NEW" : "_OLD") + ".png", tex.EncodeToPNG());
                        }
                SetAll(true);
                Shader.SetGlobalFloat("_PSXIdMask", 0f); yield return Settle();
                // (round 2) the cabin mask's two halves apart: NEW with only the reflection half off
                if (RainLookShots.Env("RL_SPLIT", "0") == "1")
                {
                    Shader.SetGlobalFloat("_PSXCabinSplit", 1f); yield return Settle();
                    File.WriteAllBytes(b + "_SSROLD.png", Grab());
                    Shader.SetGlobalFloat("_PSXCabinSplit", 0f); yield return Settle();
                }
                SetAll(false); yield return Settle();
                var pOld = Grab(); File.WriteAllBytes(b + "_OLD.png", pOld);
                Pair(pOld, pNew, b + "_PAIR.png");
                if (rows.Count > 1)
                    foreach (var r in rows)
                    {
                        SetAll(true); LabSwitches.Set(r, false, false); yield return Settle();
                        File.WriteAllBytes(b + "_OLD_" + r.id + ".png", Grab());
                    }
                SetAll(true); yield return Settle();
                var pBack = Grab();
                Say($"{v}: frames written (wiper s {CockpitInterior.WiperS:0.00}, cockpit glass {CockpitInterior.GlassShowing}, eye {Camera.main.transform.position.x:0.0},{Camera.main.transform.position.y:0.0},{Camera.main.transform.position.z:0.0})");

                // THE COST: each row OLD and NEW in alternating blocks, one process, the frame waited for
                int reps = int.TryParse(RainLookShots.Env("RL_COST", "0"), out int rr) ? rr : 0;
                if (reps > 0)
                {
                    Time.timeScale = 1f; VolumetricClouds.Paused = false;
                    var sets = new System.Collections.Generic.List<(string name, System.Action on, System.Action off)>();
                    sets.Add(("null (NEW vs NEW)", () => SetAll(true), () => SetAll(true)));
                    foreach (var r in rows) { var rc = r; sets.Add((r.id, () => LabSwitches.Set(rc, true, false), () => LabSwitches.Set(rc, false, false))); }
                    foreach (var st in sets)
                    {
                        var aMs = new System.Collections.Generic.List<float>(); var bMs = new System.Collections.Generic.List<float>();
                        var aSp = new System.Collections.Generic.List<int>(); var bSp = new System.Collections.Generic.List<int>();
                        SetAll(true);
                        for (int k = 0; k < reps; k++)
                            for (int side = 0; side < 2; side++)
                            {
                                bool newSide = (k + side) % 2 == 0;
                                if (newSide) st.on(); else st.off();
                                for (int i = 0; i < 6; i++) yield return null;
                                for (int i = 0; i < 16; i++)
                                {
                                    view.measure = true;
                                    yield return null;
                                    (newSide ? aMs : bMs).Add(view.lastMs);
                                    (newSide ? aSp : bSp).Add(UnityStats.setPassCalls);
                                }
                                view.measure = false;
                            }
                        SetAll(true);
                        aMs.Sort(); bMs.Sort();
                        float Mean(System.Collections.Generic.List<float> l) { double s = 0; foreach (var x in l) s += x; return l.Count > 0 ? (float)(s / l.Count) : 0f; }
                        float Med(System.Collections.Generic.List<float> l) => l.Count > 0 ? l[l.Count / 2] : 0f;
                        float MeanI(System.Collections.Generic.List<int> l) { double s = 0; foreach (var x in l) s += x; return l.Count > 0 ? (float)(s / l.Count) : 0f; }
                        Say($"COST {v} {st.name}: NEW {Mean(aMs):0.000} ms (median {Med(aMs):0.000}) set-pass {MeanI(aSp):0.0} | OLD {Mean(bMs):0.000} ms (median {Med(bMs):0.000}) set-pass {MeanI(bSp):0.0} | NEW-OLD {Mean(aMs) - Mean(bMs):+0.000;-0.000} ms, median {Med(aMs) - Med(bMs):+0.000;-0.000}, set-pass {MeanI(aSp) - MeanI(bSp):+0.0;-0.0} ({reps} x 2 blocks of 16 frames, render + GPU wait of the main view at {W}x{H})");
                    }
                    Time.timeScale = 0f;
                }
            }
            Say("muted line count: " + muted);
            Flush();
            Time.timeScale = 1f;
            yield return null;
            EditorApplication.Exit(0);
        }

        static void Pair(byte[] a, byte[] b, string path)
        {
            var ta = new Texture2D(2, 2); ta.LoadImage(a);
            var tb = new Texture2D(2, 2); tb.LoadImage(b);
            int w = ta.width / 2, h = ta.height / 2;
            var o = new Texture2D(w * 2 + 8, h, TextureFormat.RGB24, false);
            var pa = ta.GetPixels(); var pb = tb.GetPixels();
            var po = new Color[o.width * h];
            for (int i = 0; i < po.Length; i++) po[i] = Color.white;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    po[y * o.width + x] = pa[(y * 2) * ta.width + x * 2];
                    po[y * o.width + w + 8 + x] = pb[(y * 2) * tb.width + x * 2];
                }
            o.SetPixels(po); o.Apply(false);
            File.WriteAllBytes(path, o.EncodeToPNG());
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); Object.DestroyImmediate(o);
        }
    }

    /// <summary>The main view into a texture every frame with the display blit after it; when measuring, the
    /// render and a one-pixel read-back (the GPU waited for) are timed.</summary>
    [DefaultExecutionOrder(20000)]
    public class RainLookView : MonoBehaviour
    {
        public Camera cam;
        public RenderTexture rt, screen;
        public Material blitMat;
        public bool blit, measure;
        public float lastMs;
        RenderTexture was;
        bool held;
        Texture2D one;
        void OnDisable() { if (cam != null && held) cam.targetTexture = was; }
        void LateUpdate()
        {
            if (cam == null || rt == null) return;
            if (!held) { was = cam.targetTexture; held = true; }
            if (cam.targetTexture != rt) cam.targetTexture = rt;
            var sw = measure ? Stopwatch.StartNew() : null;
            cam.Render();
            if (blit && blitMat != null && screen != null) Graphics.Blit(rt, screen, blitMat);
            if (sw != null)
            {
                if (one == null) one = new Texture2D(1, 1, TextureFormat.RGB24, false);
                var prev = RenderTexture.active;
                RenderTexture.active = blit && screen != null ? screen : rt;
                one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                RenderTexture.active = prev;
                lastMs = (float)sw.Elapsed.TotalMilliseconds;
            }
        }
    }
}
