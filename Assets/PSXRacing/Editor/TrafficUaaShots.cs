using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE OWNER'S TRAFFIC IN CHARLOTTE, PHOTOGRAPHED (2026-11-16): the
    /// before/after frames of the UAA vehicles swap (TrafficModels). Free
    /// roam in Charlotte with the player a hidden ghost; at each of the city
    /// reference spots (CityRefSpots' street-view places: Uptown, I-277,
    /// I-77, South Blvd, Independence, Wilkinson, Central, Queens) the
    /// traffic fills round it the census way (CityTraffic.CensusMode, the
    /// game's own spawn rules and TrafficModels.Fits), then three FIXED poses
    /// are shot - the same with the switch off and on - plus a close view of
    /// the nearest vehicle of the kind the spot is for (a rig on a freeway,
    /// a bus on an arterial, a cab in Uptown), which only the "after" run has.
    ///
    ///   -executeMethod PSXRacing.EditorTools.TrafficUaaShots.Run
    ///   PSX_UAA_SHOT_TAG   file prefix (base / after), PSX_UAA_SHOT_NIGHT=1 (22:00,
    ///   the night light and lamps), PSX_TRAFFIC_UAA=0 for the base, PSX_UAA_SHOT_ONLY=a,b
    ///   (spot labels). Out: Screenshots/uaa_city/*.png and PSXRacing_uaa_shots.txt
    ///   (per spot the vehicles within 200 m by kind; per car the drawn
    ///   triangles and renderers).
    /// </summary>
    public static class TrafficUaaShots
    {
        internal static readonly StringBuilder report = new StringBuilder();
        internal static int failures;
        internal static string ProjectDir => Directory.GetParent(Application.dataPath).FullName;
        internal static string Dir => Path.Combine(ProjectDir, "Screenshots", "uaa_city");
        internal static string Tag => System.Environment.GetEnvironmentVariable("PSX_UAA_SHOT_TAG") is string t && t.Length > 0 ? t : "shot";
        internal static bool Night => System.Environment.GetEnvironmentVariable("PSX_UAA_SHOT_NIGHT") == "1";

        /// <summary>label, lat, lon, heading (deg, the street-view frame's),
        /// the kind the close view looks for (TrafficModels.Car .. Rig).</summary>
        internal static readonly (string label, double lat, double lon, float heading, int want)[] Spots =
        {
            ("uptown_trade", 35.22662, -80.84248, 305f, TrafficModels.Taxi),
            ("uptown_tryon", 35.22880, -80.84078, 229f, TrafficModels.Police),
            ("i277", 35.23363, -80.83582, 131f, TrafficModels.Rig),
            ("i77", 35.22700, -80.86208, 46f, TrafficModels.Rig),
            ("south_blvd", 35.15993, -80.87621, 181f, TrafficModels.Bus),
            ("independence", 35.16804, -80.74309, 334f, TrafficModels.Bus),
            ("wilkinson", 35.22463, -80.90005, 95f, TrafficModels.Truck),
            ("central", 35.22019, -80.80899, 94f, TrafficModels.Van),
            ("queens", 35.19280, -80.83678, 13f, TrafficModels.Car),
        };

        /// <summary>The city's metres for a lat / lon (the city's own origin).</summary>
        internal static Vector2 CityXZ(double lat, double lon) =>
            new Vector2((float)((lon + 80.8177) * 111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)),
                        (float)((lat - 35.18456) * 111132.0));

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            report.Clear(); failures = 0;
            Directory.CreateDirectory(Dir);
            foreach (var f in Directory.GetFiles(Dir, Tag + "_*.png")) File.Delete(f);
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int si = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (si < 0 || si >= scenes.Length || !File.Exists(scenes[si].path))
            {
                Fail("Charlotte: scene not built");
                Finish();
                return;
            }
            Line($"TRAFFIC UAA SHOTS tag={Tag} night={Night} PSX_TRAFFIC_UAA={System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_UAA") ?? "(unset)"}");
            EditorSceneManager.OpenScene(scenes[si].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.TrackIndex = roamIdx;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.TimeOfDayIndex = Night ? TimeOfDay.Night : TimeOfDay.Afternoon;
            CityTraffic.Enabled = true;
            CityTraffic.Suspended = false;
            CityTraffic.ClockOverride = Night ? 21.5f : 17.25f;
            CityTraffic.CapOverride = 48;
            CityTraffic.CensusMode = true;
            // the pool born within 150 m of the spot: a street full, the way a
            // busy hour fills the roads round the player
            CityTraffic.CensusRadiusM = 150f;
            CityTraffic.CensusDespawnM = 230f;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static int exceptions;
        static string firstException;

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Exception || !(stack ?? "").Contains("PSXRacing")) return;
            exceptions++;
            if (firstException == null)
            {
                string where = "";
                foreach (var line in (stack ?? "").Split('\n'))
                    if (line.Contains("PSXRacing")) { where = line.Trim(); break; }
                firstException = msg + " @ " + where;
            }
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TrafficUaaShotRunner").AddComponent<TrafficUaaShotRunner>();
        }

        internal static void Line(string s) { report.AppendLine(s); Debug.Log("[UAASHOTS] " + s); }
        internal static void Fail(string s) { failures++; Line("  FAIL " + s); }

        internal static void Finish()
        {
            Line($"exceptions in PSXRacing code: {exceptions}" + (firstException != null ? " first: " + firstException : ""));
            Line(failures == 0 ? "UAA SHOTS OK." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_uaa_shots_" + Tag + ".txt"), report.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    /// <summary>The play-mode half of <see cref="TrafficUaaShots"/>.</summary>
    public class TrafficUaaShotRunner : MonoBehaviour
    {
        CityMode mode;
        CityMap map;
        CarController player;
        CityTraffic traffic;
        float bornAt;
        bool finished;
        string stage = "loading";
        readonly List<CityTraffic.CarView> view = new List<CityTraffic.CarView>();

        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 1500f) return;
            finished = true;
            TrafficUaaShots.Fail("HUNG in " + stage);
            TrafficUaaShots.Finish();
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 900 && (CityMode.Instance == null || CityTraffic.Instance == null); i++) yield return null;
            mode = CityMode.Instance;
            traffic = CityTraffic.Instance;
            if (mode == null || traffic == null || mode.world == null || mode.world.Map == null)
            {
                TrafficUaaShots.Fail("no free-roam session with CityTraffic");
                End(); yield break;
            }
            map = mode.world.Map;
            player = mode.player;
            Ghost();
            TrafficUaaShots.Line($"kit: {(TrafficModels.On ? TrafficModels.KitCount + " vehicles" : "none")}, switch {(TrafficModels.SwitchOn ? "on" : "off")}, city mix {TrafficModels.CityMixLength} slots");

            string only = System.Environment.GetEnvironmentVariable("PSX_UAA_SHOT_ONLY") ?? "";
            bool costDone = false;
            int idx = 0;
            foreach (var sp in TrafficUaaShots.Spots)
            {
                idx++;
                if (only.Length > 0 && !("," + only + ",").Contains("," + sp.label + ",")) continue;
                stage = sp.label;
                var p = TrafficUaaShots.CityXZ(sp.lat, sp.lon);
                if (!map.NearestRoadPoint(p, 80f, false, out int ei, out float s, out float dist))
                { TrafficUaaShots.Fail(sp.label + ": no road within 80 m"); continue; }
                var e = map.edges[ei];
                Vector2 q = e.PointAt(s), t = e.TangentAt(s);
                var h = new Vector2(Mathf.Sin(sp.heading * Mathf.Deg2Rad), Mathf.Cos(sp.heading * Mathf.Deg2Rad));
                if (Vector2.Dot(t, h) < 0f) t = -t;
                t.Normalize();
                float y = e.YAt(s);
                var at = new Vector3(q.x, y, q.y);
                var f = new Vector3(t.x, 0f, t.y);
                var r = Vector3.Cross(Vector3.up, f);

                Random.InitState(7000 + idx);
                Park(new Vector2(at.x, at.z));
                traffic.CensusArrive();
                float g0 = Time.time;
                while (Time.time - g0 < 24f) { Time.timeScale = 4f; yield return null; }
                Time.timeScale = 1f;
                yield return null;

                // what stands round the spot, by kind
                traffic.Snapshot(view);
                var byKind = new int[TrafficModels.KindNames.Length];
                int near = 0, uaa = 0;
                foreach (var v in view)
                {
                    if ((v.pos - at).sqrMagnitude > 200f * 200f || v.box == null) continue;
                    string key = KeyOf(v.box.gameObject);
                    byKind[TrafficModels.KindOf(key)]++;
                    near++;
                    if (TrafficModels.IsKitKey(key)) uaa++;
                }
                var kinds = new StringBuilder();
                for (int k = 0; k < byKind.Length; k++) if (byKind[k] > 0) kinds.Append($" {TrafficModels.KindNames[k]}={byKind[k]}");
                TrafficUaaShots.Line($"{sp.label}: edge {ei} '{e.name}' cls {e.cls}{(e.link ? " link" : "")} lanes {e.lanes} ({dist:0} m off the frame's point); {near} vehicles within 200 m ({uaa} of the pack):{kinds}");
                if (!costDone && near >= 12) { Cost(); costDone = true; }

                Time.timeScale = 0f;
                string pre = Path.Combine(TrafficUaaShots.Dir, $"{TrafficUaaShots.Tag}_{idx:00}_{sp.label}");
                // 1 the driver's eye, in the right-hand lane's side
                yield return Shot(pre + "_street.png", at - f * 10f + r * Mathf.Min(3.2f, e.width * 0.25f) + Vector3.up * 1.3f, at + f * 40f + Vector3.up * 1.0f);
                // 2 a traffic camera's eye: the mix over a few hundred metres
                yield return Shot(pre + "_high.png", at - f * 55f + r * 14f + Vector3.up * 22f, at + f * 25f);
                // 3-5 the nearest vehicle of the spot's kind (else the nearest), then
                // the two nearest other models, close
                closeKeys.Clear();
                // PSX_UAA_SHOT_WANT=rig|bus|truck|taxi|police|van|car: that kind at every spot
                int want = System.Array.IndexOf(TrafficModels.KindNames, System.Environment.GetEnvironmentVariable("PSX_UAA_SHOT_WANT") ?? "");
                yield return Close(pre, at, want >= 0 ? want : sp.want, 1);
                yield return Close(pre, at, -1, 2);
                yield return Close(pre, at, -1, 3);
                Time.timeScale = 1f;
            }
            End();
        }

        static string KeyOf(GameObject go)
        {
            string n = go != null ? go.name : "";
            return n.StartsWith("CityTraffic_") ? n.Substring("CityTraffic_".Length) : n;
        }

        readonly List<string> closeKeys = new List<string>();

        /// <summary>A close view of the nearest live vehicle of kind
        /// <paramref name="want"/> (-1: any), a model not shown at this spot
        /// yet where there is one.</summary>
        IEnumerator Close(string pre, Vector3 at, int want, int n)
        {
            traffic.Snapshot(view);
            int best = -1; float bestD = float.MaxValue; bool bestKind = false;
            for (int i = 0; i < view.Count; i++)
            {
                var v = view[i];
                if (v.box == null || v.wrecked) continue;
                float d = (v.pos - at).sqrMagnitude;
                if (d > 240f * 240f) continue;
                string k = KeyOf(v.box.gameObject);
                if (closeKeys.Contains(k)) d += 1e7f;          // a model already shown: only if nothing else
                bool kind = want < 0 || TrafficModels.KindOf(k) == want;
                if ((kind && !bestKind) || (kind == bestKind && d < bestD)) { best = i; bestD = d; bestKind = kind; }
            }
            if (best < 0) { TrafficUaaShots.Line("  close: no vehicle within 240 m"); yield break; }
            closeKeys.Add(KeyOf(view[best].box.gameObject));
            if (bestD >= 1e7f) bestD -= 1e7f;
            var c = view[best];
            var fwd = c.rot * Vector3.forward; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            float halfL = c.box.size.z * 0.5f, hgt = c.box.size.y;
            float back = Mathf.Max(5.5f, halfL * 1.25f + 3f);
            // at night the second close view is from behind: the tail lamps
            float s = TrafficUaaShots.Night && n == 2 ? -1f : 1f;
            var eye = c.pos + fwd * (back * s) + right * (back * 0.62f) + Vector3.up * Mathf.Max(1.6f, hgt * 0.75f);
            var look = c.pos + fwd * (halfL * 0.15f * s) + Vector3.up * (hgt * 0.35f);
            string key = KeyOf(c.box.gameObject);
            TrafficUaaShots.Line($"  close {n}: {key} ({TrafficModels.KindNames[TrafficModels.KindOf(key)]}{(bestKind || want < 0 ? "" : ", not the wanted " + TrafficModels.KindNames[want])}) {Mathf.Sqrt(bestD):0} m from the spot");
            yield return Shot(pre + "_close" + n + "_" + key + ".png", eye, look);
        }

        /// <summary>What the traffic costs to draw: per live car, the
        /// near level's triangles and renderers, and the far level's.</summary>
        void Cost()
        {
            var per = new SortedDictionary<string, (int tris, int rends, int farTris, int n)>();
            long tris = 0, rends = 0;
            traffic.Snapshot(view);
            foreach (var v in view)
            {
                if (v.box == null) continue;
                var go = v.box.gameObject;
                string key = KeyOf(go);
                int ct = 0, cr = 0, ft = 0;
                var lod = go.GetComponentInChildren<LODGroup>();
                var farSet = new HashSet<Renderer>();
                if (lod != null)
                {
                    var lods = lod.GetLODs();
                    for (int l = 1; l < lods.Length; l++) foreach (var rr in lods[l].renderers) if (rr != null) farSet.Add(rr);
                }
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!mr.enabled) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    int n = mf != null && mf.sharedMesh != null ? Tris(mf.sharedMesh) : 0;
                    if (farSet.Contains(mr)) { ft += n; continue; }
                    ct += n; cr += Mathf.Max(1, mr.sharedMaterials.Length);
                }
                tris += ct; rends += cr;
                per.TryGetValue(key, out var acc);
                per[key] = (ct, cr, ft, acc.n + 1);
            }
            TrafficUaaShots.Line($"COST {view.Count} live cars: {tris} near-level triangles, {rends} renderer-materials (draw calls before batching); per car {tris / Mathf.Max(1, view.Count)} tris, {(float)rends / Mathf.Max(1, view.Count):0.0} draws");
            foreach (var kv in per)
                TrafficUaaShots.Line($"  cost {kv.Key,-18} x{kv.Value.n}: {kv.Value.tris} tris, {kv.Value.rends} draws near; far level {kv.Value.farTris} tris");
        }

        static int Tris(Mesh m)
        {
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i);
            return (int)(n / 3);
        }

        void Ghost()
        {
            if (player == null) return;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            player.enabled = false;
            foreach (var c in player.GetComponentsInChildren<Collider>()) c.enabled = false;
            player.Body.isKinematic = true;
            foreach (var rr in player.GetComponentsInChildren<Renderer>(true)) rr.enabled = false;
            foreach (var l in player.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var cl in player.GetComponentsInChildren<CarLights>(true)) cl.enabled = false;
        }

        void Park(Vector2 at)
        {
            float y = 0f;
            if (map.NearestRoadPoint(at, 300f, false, out int ei, out float s, out _)) y = map.edges[ei].YAt(s);
            var p = new Vector3(at.x, y + 30f, at.y);
            player.Body.position = p;
            player.transform.position = p;
            mode.world.EnsureRing(p, 1);
        }

        IEnumerator Shot(string path, Vector3 eye, Vector3 look)
        {
            yield return null;
            var cam = Camera.main;
            if (cam == null) { TrafficUaaShots.Line("  no camera for " + path); yield break; }
            var oldPos = cam.transform.position; var oldRot = cam.transform.rotation;
            var target = cam.targetTexture;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            mode.world.EnsureRing(eye, 1);
            mode.world.RefreshSkyline(cam);
            int w = 960, h = 540;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt || prev == shown ? null : prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            else TrafficUaaShots.Line("  the pipeline takes no render request");
            cam.targetTexture = target;
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        void End()
        {
            if (finished) return;
            finished = true;
            Time.timeScale = 1f;
            TrafficUaaShots.Finish();
        }
    }
}
