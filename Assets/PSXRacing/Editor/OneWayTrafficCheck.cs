using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ONE-WAY STREETS IN FREE ROAM (tier1 11-23; the race half is LaneUseWatch's ONE-WAY LEGAL line in every race of
    /// race-play-check). Charlotte free roam at rush hour (17:15) with the city's own traffic: the player as a ghost
    /// (no collider, kinematic) drives at 11 m/s along each of three ONE-WAY streets near uptown and one DIVIDED road
    /// (a carriageway of a named road whose twin runs the other way), 50 s each, so the ring of traffic loads round
    /// it; every 0.25 s every traffic car is judged by OneWayLegal (its position on the city map, its heading against
    /// the street's legal direction: nothing of CityTraffic's own bookkeeping is read).
    /// PASS: no car the wrong way; each of the four streets carried at least 20 car samples.
    /// The streets are picked from the map (one-way, class 1-4, not a ramp, longest within 2.5 km of uptown; the
    /// divided road's class 3-4, nearest uptown); PSX_ONEWAY_STREETS="name|name|name|divided name" picks them by name.
    /// Report: PSXRacing_oneway_check.txt. tools\oneway-check.ps1.
    /// </summary>
    public static class OneWayTrafficCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check One-Way Traffic (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            }
            log = new StringBuilder();
            failures = 0;
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int si = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (si < 0 || si >= scenes.Length || !System.IO.File.Exists(scenes[si].path))
            {
                Fail("Charlotte: scene not built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[si].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.TrackIndex = roamIdx;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
            CityTraffic.Enabled = true;
            CityTraffic.Suspended = false;
            CityTraffic.ClockOverride = 17.25f;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("OneWayTrafficCheckRunner").AddComponent<OneWayTrafficCheckRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);
        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }
        internal static void Fail(string what) { failures++; log.AppendLine("  FAIL " + what); }

        internal static void Finish()
        {
            CityTraffic.ClockOverride = -1f;
            log.AppendLine(failures == 0 ? "ONE-WAY TRAFFIC OK." : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                "PSXRacing_oneway_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class OneWayTrafficCheckRunner : MonoBehaviour
    {
        const float StreetS = 50f, DriveMps = 11f, HangSeconds = 900f;
        const int MinSamples = 20;
        float bornAt;
        bool finished;
        string stage = "loading";
        CityMode mode;
        CityMap map;
        CarController player;
        readonly List<CityTraffic.CarView> view = new List<CityTraffic.CarView>();

        void Awake() { bornAt = Time.realtimeSinceStartup; }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true;
            OneWayTrafficCheck.Fail("HUNG: no end after " + HangSeconds.ToString("0") + " s, stuck in " + stage);
            OneWayTrafficCheck.Finish();
            EditorApplication.Exit(1);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 300 && (CityMode.Instance == null || CityTraffic.Instance == null); i++) yield return null;
            mode = CityMode.Instance;
            var traffic = CityTraffic.Instance;
            OneWayTrafficCheck.Line("Charlotte free roam, the city's traffic at 17:15, against the legal direction (OneWayLegal):");
            OneWayTrafficCheck.Check(mode != null && mode.world != null && mode.world.Map != null && traffic != null, "a free-roam session with a road graph and CityTraffic");
            if (mode == null || traffic == null || mode.world == null || mode.world.Map == null) { End(); yield break; }
            map = mode.world.Map;
            player = mode.player;
            Ghost();

            var picks = Pick();
            foreach (var (name, edge, divided) in picks)
            {
                stage = name;
                OneWayLegal.Reset();
                int ei = edge, dir = 1;
                float s = Mathf.Min(15f, map.edges[ei].length * 0.5f);
                DriveGhost(ref ei, ref dir, ref s, 0f);
                mode.world.EnsureRing(player.transform.position, 1);
                float t0 = Time.time, next = 0f;
                while (Time.time - t0 < StreetS)
                {
                    yield return new WaitForFixedUpdate();
                    DriveGhost(ref ei, ref dir, ref s, DriveMps * Time.fixedDeltaTime);
                    if (Time.time < next || Time.time - t0 < 6f) continue;
                    next = Time.time + 0.25f;
                    traffic.Snapshot(view);
                    foreach (var v in view)
                    {
                        if (v.wrecked || v.turning) continue;
                        Vector3 fwd = v.rot * Vector3.forward;
                        OneWayLegal.Sample(map, "car " + v.id, v.pos, fwd * v.speed, fwd, "free roam", Time.time - t0);
                    }
                }
                OneWayLegal.Streets.TryGetValue(name, out var st);
                int own = st != null ? st.samples : 0;
                OneWayTrafficCheck.Line($"{(divided ? "DIVIDED ROAD" : "ONE-WAY STREET")} '{name}' (from e{edge}, {StreetS:0} s at {DriveMps:0} m/s): " + OneWayLegal.Describe(5));
                OneWayTrafficCheck.Check(OneWayLegal.Wrong == 0, $"'{name}' and every one-way street round it: no traffic car against the legal direction", $"{OneWayLegal.Wrong} of {OneWayLegal.OnOneWay}");
                OneWayTrafficCheck.Check(own >= MinSamples && (!divided || (st != null && st.divided)),
                                         $"'{name}' itself carried traffic ({MinSamples}+ car samples{(divided ? ", as a divided road" : "")})", own);
            }
            OneWayTrafficCheck.Check(picks.Count >= 4, "three one-way streets and one divided road found on the map", picks.Count);
            End();
        }

        void End()
        {
            finished = true;
            OneWayTrafficCheck.Finish();
            EditorApplication.Exit(OneWayTrafficCheck.failures == 0 ? 0 : 1);
        }

        /// <summary>Three one-way streets and a divided road: (name, its longest edge near uptown, divided).</summary>
        List<(string name, int edge, bool divided)> Pick()
        {
            var want = (System.Environment.GetEnvironmentVariable("PSX_ONEWAY_STREETS") ?? "").Split('|');
            var lenBy = new Dictionary<string, float>();
            var edgeBy = new Dictionary<string, int>();
            var divBy = new Dictionary<string, bool>();
            for (int i = 0; i < map.edges.Length; i++)
            {
                var e = map.edges[i];
                if (!e.oneway || e.link || e.bridge || e.tunnel || e.roundabout || string.IsNullOrEmpty(e.name) || e.cls < 1 || e.cls > 4 || e.length < 40f) continue;
                if (Vector2.Distance(e.PointAt(e.length * 0.5f), map.uptown) > 6000f) continue;
                bool div = OneWayLegal.Divided(map, i);
                string key = e.name;
                lenBy.TryGetValue(key, out float L);
                lenBy[key] = L + e.length;
                if (!edgeBy.TryGetValue(key, out int be) || map.edges[be].length < e.length) edgeBy[key] = i;
                divBy[key] = (divBy.TryGetValue(key, out bool d0) && d0) || div;
            }
            var picks = new List<(string, int, bool)>();
            if (want.Length == 4)
            {
                for (int k = 0; k < 4; k++)
                    if (edgeBy.TryGetValue(want[k].Trim(), out int e)) picks.Add((want[k].Trim(), e, k == 3));
                    else OneWayTrafficCheck.Fail($"PSX_ONEWAY_STREETS: no one-way street '{want[k]}' near uptown");
                return picks;
            }
            var names = new List<string>(lenBy.Keys);
            // one-way streets (no twin), within 2.5 km of uptown, the longest
            names.Sort((a, b) => lenBy[b].CompareTo(lenBy[a]));
            foreach (var n in names)
            {
                if (picks.Count >= 3) break;
                if (divBy[n]) continue;
                if (Vector2.Distance(map.edges[edgeBy[n]].PointAt(0f), map.uptown) > 2500f) continue;
                picks.Add((n, edgeBy[n], false));
            }
            // the divided road: class 3-4 with its twin, 300 m+ of it, nearest uptown
            string dn = null; float dd = float.MaxValue;
            foreach (var n in names)
            {
                if (!divBy[n] || lenBy[n] < 300f || map.edges[edgeBy[n]].cls < 3) continue;
                float d = Vector2.Distance(map.edges[edgeBy[n]].PointAt(0f), map.uptown);
                if (d < dd) { dd = d; dn = n; }
            }
            if (dn != null) picks.Add((dn, edgeBy[dn], true));
            return picks;
        }

        // ---- the player as a ghost
        void Ghost()
        {
            if (player == null) return;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            player.enabled = false;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                foreach (var r in Object.FindObjectsByType<CarReflections>(FindObjectsInactive.Include)) r.enabled = false;
            foreach (var c in player.GetComponentsInChildren<Collider>()) c.enabled = false;
            player.Body.isKinematic = true;
        }

        /// <summary>On along the edge's legal direction; at a node the straightest legal way on.</summary>
        void DriveGhost(ref int ei, ref int dir, ref float s, float step)
        {
            var e = map.edges[ei];
            s += dir * step;
            if (s < 0f || s > e.length)
            {
                float over = s < 0f ? -s : s - e.length;
                int n = s < 0f ? e.a : e.b;
                Vector2 tIn = e.TangentAt(s < 0f ? 0f : e.length) * dir;
                int best = -1, bdir = 1; float ba = float.MaxValue;
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (oi == ei || o.a == o.b) continue;
                    int d2 = o.a == n ? 1 : -1;
                    if (d2 < 0 && o.oneway) continue;
                    float ang = Mathf.Abs(Vector2.SignedAngle(tIn, o.TangentAt(d2 > 0 ? 0f : o.length) * d2)) + (o.cls < 1 ? 40f : 0f) + (o.name != e.name ? 25f : 0f);
                    if (ang < ba) { ba = ang; best = oi; bdir = d2; }
                }
                if (best < 0) { best = ei; bdir = -dir; }
                ei = best; dir = bdir; e = map.edges[ei];
                s = dir > 0 ? Mathf.Min(over, e.length) : Mathf.Max(0f, e.length - over);
            }
            var q = LineModel.LanePoint(e, s);
            var t = e.TangentAt(s) * dir;
            var right = new Vector2(t.y, -t.x);
            q += right * (e.oneway ? 0f : 1.8f);
            var p = new Vector3(q.x, e.YAt(s) + 0.5f, q.y);
            player.Body.MovePosition(p);
            player.Body.MoveRotation(Quaternion.LookRotation(new Vector3(t.x, 0f, t.y), Vector3.up));
            if (step <= 0f) { player.Body.position = p; player.transform.position = p; }
        }
    }
}
