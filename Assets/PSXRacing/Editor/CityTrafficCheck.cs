using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PLAY Charlotte free roam with the city's own traffic (CityTraffic) and
    /// measure it: RUSH HOUR (17:15) and 03:00, each 120 s with the player
    /// parked (a non-colliding ghost hovering over a busy uptown junction) and
    /// 120 s with it driving (the ghost run along the streets at 13 m/s, so the
    /// ring of traffic moves with it and the cars behind it must brake for it).
    /// Counts: cars on the road, stops at reds and departures on greens, queues
    /// at stop lines, STOP-sign stops, lane changes and ramp merges; and the
    /// faults - a car off the pavement, the wrong way, overlapping another, an
    /// exception. Then the frame time with traffic against none (the same
    /// spot), and one real hit (the player driven into a car: it must wreck).
    /// Pictures (a watched run only): a signal queue at rush hour, a freeway, a
    /// STOP-sign stop, light night traffic, to Screenshots\citytraffic_*.png.
    /// Report: PSXRacing_city_traffic_check.txt. tools\city-traffic-check.ps1.
    /// </summary>
    public static class CityTrafficCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static int roamIdx;

        [MenuItem("PSX Racing/Check City Traffic (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            roamIdx = TrackCatalog.IndexOf("Charlotte");
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
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        internal static int exceptions, trafficErrors;
        internal static string firstException;

        static void OnLog(string msg, string stack, LogType type)
        {
            // the game's own (the editor's search indexer threw once at start-up)
            if (type == LogType.Exception && (stack ?? "").Contains("PSXRacing"))
            {
                exceptions++;
                if (firstException == null) firstException = msg + " @ " + (stack ?? "").Split('\n')[0];
            }
            else if (type == LogType.Error && (msg.Contains("CityTraffic") || (stack ?? "").Contains("CityTraffic"))) trafficErrors++;
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CityTrafficCheckRunner").AddComponent<CityTrafficCheckRunner>();
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
            Application.logMessageReceived -= OnLog;
            CityTraffic.ClockOverride = -1f;
            CityTraffic.Suspended = false;
            log.AppendLine(failures == 0 ? "CITY TRAFFIC OK." : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                "PSXRacing_city_traffic_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class CityTrafficCheckRunner : MonoBehaviour
    {
        const float PhaseS = 120f;
        const float HangSeconds = 1500f;
        float bornAt;
        bool finished;
        string stage = "loading";
        CityMode mode;
        CityMap map;
        CarController player;
        readonly List<CityTraffic.CarView> view = new List<CityTraffic.CarView>();
        readonly HashSet<string> shotsTaken = new HashSet<string>();

        // a phase's tallies
        int samples, carSamples, offPave, wrongWay, overlaps, activeMin, activeMax, activeSum;
        float phaseT0;
        string offWhat, wrongWhat, overlapWhat;

        void Awake() { bornAt = Time.realtimeSinceStartup; }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true;
            CityTrafficCheck.Fail("HUNG: no end after " + HangSeconds.ToString("0") + " s, stuck in " + stage);
            CityTrafficCheck.Finish();
            EditorApplication.Exit(1);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 300 && (CityMode.Instance == null || CityTraffic.Instance == null); i++) yield return null;
            mode = CityMode.Instance;
            var traffic = CityTraffic.Instance;
            CityTrafficCheck.Line("Charlotte free roam, city traffic:");
            CityTrafficCheck.Check(mode != null && mode.world != null && mode.world.Map != null, "a free-roam session with a road graph");
            CityTrafficCheck.Check(traffic != null, "CityTraffic attached by CityMode (PSX_CITY_TRAFFIC not 0)");
            if (mode == null || traffic == null || mode.world == null) { End(); yield break; }
            map = mode.world.Map;
            player = mode.player;
            CityTrafficCheck.Line($"  cap {traffic.Cap} ({(Application.isMobilePlatform ? "phone" : "desktop")}; phone cap 24)");

            // ---- the spot: the busiest signalised junction near uptown
            var trims = mode.world.NodeTrims;
            Vector2 spot = map.uptown;
            float bestScore = float.MaxValue;
            foreach (var j in CitySignals.All(map, trims))
            {
                if (!j.signal || j.approaches.Count < 4) continue;
                int big = 0;
                foreach (var a in j.approaches) if (map.edges[a.edge].cls >= 3) big++;
                if (big < 2) continue;
                float d = Vector2.Distance(j.centre, map.uptown) - big * 60f;
                if (d < bestScore) { bestScore = d; spot = j.centre; }
            }
            CityTrafficCheck.Line($"  spot: signalised junction at ({spot.x:0},{spot.y:0}), {Vector2.Distance(spot, map.uptown):0} m from uptown");
            Ghost(true);
            Park(spot);
            for (int i = 0; i < 30; i++) yield return null;

            // ---- RUSH HOUR
            stage = "rush hour, parked";
            CityTraffic.ClockOverride = 17.25f;
            yield return Phase("RUSH HOUR 17:15, parked", spot, false, true);
            stage = "rush hour, driving";
            yield return Phase("RUSH HOUR 17:15, driving", spot, true, true);

            // ---- the frame time, the same spot, with and without
            stage = "frame time A/B";
            Park(spot);
            for (int i = 0; i < 60; i++) yield return null;
            yield return FrameAB(traffic);

            // ---- a STOP sign: the nearest all-way or two-way stop with cars about
            stage = "a stop sign";
            yield return StopSite(traffic, spot);

            // ---- 03:00
            stage = "03:00";
            CityTraffic.ClockOverride = 3f;
            SetHour(TimeOfDay.Night);
            Park(spot);
            yield return Phase("03:00, parked", spot, false, false);
            yield return Phase("03:00, driving", spot, true, false);

            // ---- a real hit
            stage = "the hit";
            CityTraffic.ClockOverride = 17.25f;
            yield return HitTest(traffic, spot);

            // ---- the turns that put a car into a barrier (node 1407 on
            // 2026-10-26, and any this run), laid out lane by lane
            stage = "turn probe";
            var probes = new List<(int n, int a, int b)> { (1407, 1329, 809) };
            foreach (var w in traffic.WreckLog)
            {
                var m = System.Text.RegularExpressions.Regex.Match(w, @"at node (\d+) e(\d+)->e(\d+)");
                if (m.Success && probes.Count < 5)
                {
                    var t = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                    if (!probes.Contains(t)) probes.Add(t);
                }
            }
            foreach (var (n, a, b) in probes)
            {
                if (n < 0 || n >= map.nodes.Length) continue;
                var at = map.nodes[n];
                mode.world.EnsureRing(new Vector3(at.x, 0f, at.y), 1);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                CityTrafficCheck.Line("Turn probe: " + traffic.DescribeTurn(n, a, b).TrimEnd());
            }

            // ---- totals
            var st = traffic.Stats;
            CityTrafficCheck.Line("Totals:");
            CityTrafficCheck.Line($"  spawned {st.spawned}, recycled {st.recycled}, wrecks {st.wrecks}, dead ends {st.deadEnds}, U-turns {st.uTurns}");
            CityTrafficCheck.Check(CityTrafficCheck.exceptions == 0, "no runtime exceptions", CityTrafficCheck.exceptions + (CityTrafficCheck.firstException != null ? " first: " + CityTrafficCheck.firstException : ""));
            CityTrafficCheck.Check(CityTrafficCheck.trafficErrors == 0, "no errors logged from CityTraffic", CityTrafficCheck.trafficErrors);
            int tt = 0;
            foreach (var w in traffic.WreckLog) if (w.Contains("CityTraffic_")) tt++;
            CityTrafficCheck.Check(tt <= 1, "traffic-on-traffic wrecks (at most 1)", tt);
            CityTrafficCheck.Check(st.bumps <= 12, "traffic-on-traffic bumps over the whole run (at most 12)", st.bumps);
            CityTrafficCheck.Line($"  replans {st.replans}, physics steps over 5 ms {st.slowSteps}");
            foreach (var w in traffic.WreckLog) CityTrafficCheck.Line("    wreck: " + w);
            foreach (var w in traffic.BumpLog) CityTrafficCheck.Line("    bump: " + w);
            End();
        }

        void End()
        {
            finished = true;
            CityTrafficCheck.Finish();
            EditorApplication.Exit(CityTrafficCheck.failures == 0 ? 0 : 1);
        }

        // ---- the player as a ghost --------------------------------------------
        readonly List<Collider> ghostCols = new List<Collider>();

        void Ghost(bool on)
        {
            if (player == null) return;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = !on;
            player.enabled = !on;
            // with no graphics device the reflection probes log a failed
            // RenderTexture every frame (gigabytes of log): off
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                foreach (var r in Object.FindObjectsByType<CarReflections>(FindObjectsInactive.Include)) r.enabled = false;
            if (on)
            {
                ghostCols.Clear();
                foreach (var c in player.GetComponentsInChildren<Collider>()) if (c.enabled) { ghostCols.Add(c); c.enabled = false; }
                player.Body.isKinematic = true;
            }
            else
            {
                foreach (var c in ghostCols) if (c != null) c.enabled = true;
                player.Body.isKinematic = false;
            }
        }

        void Park(Vector2 at)
        {
            float y = 0f;
            if (map.NearestRoadPoint(at, 200f, false, out int ei, out float s, out _)) y = map.edges[ei].YAt(s);
            var p = new Vector3(at.x, y + 30f, at.y);
            player.Body.position = p;
            player.transform.position = p;
            mode.world.EnsureRing(p, 1);
        }

        void SetHour(int hour)
        {
            Light sun = RenderSettings.sun;
            if (sun == null) foreach (var l in Object.FindObjectsByType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
            TimeOfDay.Apply(hour, sun);
        }

        // ---- a phase ------------------------------------------------------------
        IEnumerator Phase(string name, Vector2 spot, bool drive, bool rush)
        {
            var traffic = CityTraffic.Instance;
            var before = traffic.Stats;
            samples = carSamples = offPave = wrongWay = overlaps = activeSum = activeMax = 0;
            activeMin = int.MaxValue;
            offWhat = wrongWhat = overlapWhat = null;
            traffic.ResetTiming();
            float t0 = Time.time, nextSample = 0f;
            phaseT0 = t0;
            float frameSum = 0f; int frames = 0;
            // the drive: from the spot along the straightest street on
            int de = -1, ddir = 1; float ds = 0f;
            if (drive && map.NearestRoadPoint(spot, 300f, true, out de, out ds, out _)) ddir = 1;
            while (Time.time - t0 < PhaseS)
            {
                yield return new WaitForFixedUpdate();
                if (drive && de >= 0) DriveGhost(ref de, ref ddir, ref ds, 13f * Time.fixedDeltaTime);
                frameSum += Time.unscaledDeltaTime; frames++;
                if (Time.time < nextSample) continue;
                nextSample = Time.time + 0.5f;
                if (Time.time - t0 > 10f) Sample(traffic);
                yield return Shots(traffic, rush);
            }
            var a = traffic.Stats;
            float mean = samples > 0 ? activeSum / (float)samples : 0f;
            CityTrafficCheck.Line($"{name}: target {traffic.Target} (share {traffic.Share:0.00}, level {TrafficLevels.Name(CityTraffic.LevelAt(CityTraffic.ClockNow))})");
            CityTrafficCheck.Line($"  active cars min {(activeMin == int.MaxValue ? 0 : activeMin)} / mean {mean:0.0} / max {activeMax}; spawned {a.spawned - before.spawned}, recycled {a.recycled - before.recycled}");
            CityTrafficCheck.Line($"  signals: stopped at red {a.redStops - before.redStops}, went on green {a.greenGoes - before.greenGoes}, on yellow {a.yellowGoes - before.yellowGoes}, RAN RED {a.ranRed - before.ranRed}; left turns waiting for oncoming {a.leftYields - before.leftYields}; longest queue so far {a.maxQueue}");
            CityTrafficCheck.Line($"  stop signs: full stops {a.stopSignStops - before.stopSignStops}, rolled {a.stopRolls - before.stopRolls}; yields at minor roads {a.yields - before.yields}");
            CityTrafficCheck.Line($"  lanes: passing changes {a.laneChanges - before.laneChanges}, mandatory (turn lane / lane ends) {a.mandatory - before.mandatory}, keep right {a.keepRight - before.keepRight}; ramp merges {a.merges - before.merges}, exits {a.exits - before.exits}");
            CityTrafficCheck.Line($"  wrecks {a.wrecks - before.wrecks}, traffic bumps (< 4.5 m/s, no wreck) {a.bumps - before.bumps}, replans (missed a turn lane) {a.replans - before.replans}, physics steps over 5 ms {a.slowSteps - before.slowSteps}");
            float stepAvg = traffic.StepCount > 0 ? (float)(traffic.StepMsSum / traffic.StepCount) : 0f;
            CityTrafficCheck.Line($"  CityTraffic physics step: mean {stepAvg:0.000} ms, worst {traffic.StepMsMax:0.00} ms; frame mean {(frames > 0 ? frameSum / frames * 1000f : 0f):0.0} ms");
            if (rush) CityTrafficCheck.Check(mean >= Mathf.Min(20f, traffic.Cap * 0.5f), "rush hour fills the road (mean active >= 20)", mean.ToString("0.0"));
            else CityTrafficCheck.Check(mean <= 8f, "03:00 is very light (mean active <= 8)", mean.ToString("0.0"));
            if (rush && !drive)
            {
                CityTrafficCheck.Check(a.redStops - before.redStops > 0, "cars stop at red lights", a.redStops - before.redStops);
                CityTrafficCheck.Check(a.greenGoes - before.greenGoes > 0, "cars go on green", a.greenGoes - before.greenGoes);
            }
            CityTrafficCheck.Check(a.ranRed - before.ranRed == 0, "nobody runs a red", a.ranRed - before.ranRed);
            CityTrafficCheck.Check(a.stopRolls - before.stopRolls == 0, "nobody rolls a STOP sign", a.stopRolls - before.stopRolls);
            float offShare = carSamples > 0 ? offPave / (float)carSamples : 0f;
            CityTrafficCheck.Check(offShare <= 0.005f, "cars on the pavement (<= 0.5% of samples off it)", $"{offPave}/{carSamples}" + (offWhat != null ? " e.g. " + offWhat : ""));
            CityTrafficCheck.Check(wrongWay == 0, "nobody the wrong way", wrongWay + (wrongWhat != null ? " e.g. " + wrongWhat : ""));
            CityTrafficCheck.Check(overlaps == 0, "no car overlapping another", overlaps + (overlapWhat != null ? " e.g. " + overlapWhat : ""));
            if (rush && drive) CityTrafficCheck.Check(a.laneChanges + a.mandatory + a.keepRight - before.laneChanges - before.mandatory - before.keepRight > 0, "lane changes happen at rush hour");
        }

        void Sample(CityTraffic traffic)
        {
            traffic.Snapshot(view);
            samples++;
            activeSum += view.Count;
            activeMin = Mathf.Min(activeMin, view.Count);
            activeMax = Mathf.Max(activeMax, view.Count);
            for (int i = 0; i < view.Count; i++)
            {
                var v = view[i];
                if (v.wrecked) continue;
                carSamples++;
                if (!v.turning && v.edge >= 0)
                {
                    var e = map.edges[v.edge];
                    LineModel.Extents(e, Mathf.Clamp(v.s, 0f, e.length), out float eM, out float eP);
                    if (v.lat - v.halfW < -eM - 0.3f || v.lat + v.halfW > eP + 0.3f)
                    {
                        offPave++;
                        offWhat ??= $"e{v.edge} '{e.name}' s={v.s:0} lat {v.lat:0.0} pave -{eM:0.0}..+{eP:0.0}";
                    }
                    bool wrong = e.oneway ? v.dir < 0 : false;
                    if (!e.oneway && !v.lc)
                    {
                        float div = LineModel.DividerLat(e);
                        wrong = v.dir > 0 ? v.lat > div + 1.0f : v.lat < div - 1.0f;
                    }
                    if (wrong) { wrongWay++; wrongWhat ??= $"e{v.edge} '{e.name}' dir {v.dir} lat {v.lat:0.0} divider {LineModel.DividerLat(e):0.0}"; }
                }
                for (int j = i + 1; j < view.Count; j++)
                {
                    var w = view[j];
                    if (w.wrecked || (w.pos - v.pos).sqrMagnitude > 64f) continue;
                    if (Physics.ComputePenetration(v.box, v.pos, v.rot, w.box, w.pos, w.rot, out _, out float depth) && depth > 0.05f)
                    {
                        overlaps++;
                        overlapWhat ??= $"cars {v.id} and {w.id} by {depth:0.00} m at ({v.pos.x:0},{v.pos.z:0})";
                    }
                }
            }
        }

        /// <summary>The ghost along the streets: on along its edge's lanes, at a
        /// node the straightest legal way on.</summary>
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
                    float ang = Mathf.Abs(Vector2.SignedAngle(tIn, o.TangentAt(d2 > 0 ? 0f : o.length) * d2)) + (o.cls < 1 ? 40f : 0f);
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
        }

        // ---- frame time ----------------------------------------------------------
        IEnumerator FrameAB(CityTraffic traffic)
        {
            float with = 0f, without = 0f; int nw = 0, nwo = 0;
            int vs = QualitySettings.vSyncCount, fr = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 1000;
            for (int i = 0; i < 30; i++) yield return null;
            float t0 = Time.realtimeSinceStartup;
            traffic.ResetTiming();
            while (Time.realtimeSinceStartup - t0 < 20f) { yield return null; with += Time.unscaledDeltaTime; nw++; }
            float stepAvg = traffic.StepCount > 0 ? (float)(traffic.StepMsSum / traffic.StepCount) : 0f;
            int active = traffic.Active;
            CityTraffic.Suspended = true;
            for (int i = 0; i < 30; i++) yield return null;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 20f) { yield return null; without += Time.unscaledDeltaTime; nwo++; }
            CityTraffic.Suspended = false;
            QualitySettings.vSyncCount = vs; Application.targetFrameRate = fr;
            float mw = nw > 0 ? with / nw * 1000f : 0f, mwo = nwo > 0 ? without / nwo * 1000f : 0f;
            CityTrafficCheck.Line($"Frame time (editor, {(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ? "no graphics" : "rendering")}), the spot at rush hour: " +
                                  $"with {active} cars {mw:0.00} ms/frame, without {mwo:0.00} ms/frame (+{mw - mwo:0.00}); CityTraffic's own step {stepAvg:0.000} ms x {Time.fixedDeltaTime * 1000f:0} ms steps");
            CityTrafficCheck.Check(stepAvg < 1.5f, "CityTraffic's physics step under 1.5 ms (desktop editor)", stepAvg.ToString("0.000"));
            for (int i = 0; i < 120; i++) yield return null;   // refill
        }

        // ---- a STOP sign ------------------------------------------------------------
        IEnumerator StopSite(CityTraffic traffic, Vector2 from)
        {
            var trims = mode.world.NodeTrims;
            Vector2 best = from; float bd = float.MaxValue; bool found = false;
            foreach (var j in CitySignals.All(map, trims))
            {
                if (j.signal || j.approaches.Count < 2) continue;
                bool busy = false;
                foreach (int n in j.nodes) foreach (int ei in map.nodeEdges[n]) if (map.edges[ei].cls >= 1) busy = true;
                float d = Vector2.Distance(j.centre, from) + (busy ? 0f : 800f) + (j.allWay ? 0f : 300f);
                if (d < bd && Vector2.Distance(j.centre, from) > 300f) { bd = d; best = j.centre; found = true; }
            }
            if (!found) { CityTrafficCheck.Line("STOP sign site: none found"); yield break; }
            CityTrafficCheck.Line($"STOP sign site: ({best.x:0},{best.y:0}), {Vector2.Distance(best, from):0} m from the spot");
            Park(best);
            var before = traffic.Stats;
            float t0 = Time.time;
            while (Time.time - t0 < 60f)
            {
                yield return null;
                yield return Shots(traffic, true);
            }
            var a = traffic.Stats;
            CityTrafficCheck.Line($"  60 s: full stops {a.stopSignStops - before.stopSignStops}, rolled {a.stopRolls - before.stopRolls}, yields {a.yields - before.yields}, active {traffic.Active}");
            CityTrafficCheck.Check(a.stopRolls - before.stopRolls == 0, "nobody rolls the STOP signs");
        }

        // ---- the hit ------------------------------------------------------------------
        IEnumerator HitTest(CityTraffic traffic, Vector2 spot)
        {
            Park(spot);
            for (int i = 0; i < 200; i++) yield return null;
            traffic.Snapshot(view);
            CityTraffic.CarView? pick = null;
            foreach (var v in view)
                if (!v.wrecked && !v.turning && v.speed > 5f && v.edge >= 0 && !map.edges[v.edge].bridge &&
                    (pick == null || Vector2.Distance(new Vector2(v.pos.x, v.pos.z), spot) < Vector2.Distance(new Vector2(pick.Value.pos.x, pick.Value.pos.z), spot)))
                    pick = v;
            if (pick == null) { CityTrafficCheck.Line("Hit test: no moving car to hit"); yield break; }
            var car = pick.Value;
            int wrecks0 = traffic.Stats.wrecks;
            Vector3 fwd = car.rot * Vector3.forward; fwd.y = 0f; fwd.Normalize();
            Ghost(false);
            player.TeleportTo(car.pos - fwd * 11f + Vector3.up * 0.3f, Quaternion.LookRotation(fwd, Vector3.up));
            yield return new WaitForFixedUpdate();
            player.Body.linearVelocity = fwd * (car.speed + 9f);
            float t0 = Time.time;
            while (Time.time - t0 < 3f && traffic.Stats.wrecks == wrecks0) yield return new WaitForFixedUpdate();
            bool hit = traffic.Stats.wrecks > wrecks0;
            string by = hit ? traffic.WreckLog[traffic.WreckLog.Count - 1] : "-";
            CityTrafficCheck.Check(hit, "the player driven into a traffic car wrecks it", by);
            player.Body.linearVelocity = Vector3.zero;
            yield return null;
            Ghost(true);
        }

        // ---- pictures -------------------------------------------------------------------
        IEnumerator Shots(CityTraffic traffic, bool rush)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            if (rush && !shotsTaken.Contains("signal_queue") && traffic.QueueNow >= 3 && traffic.QueueAtSignal)
            {
                var f = new Vector3(traffic.QueueFacing.x, 0f, traffic.QueueFacing.y);
                var r = Vector3.Cross(Vector3.up, f);
                var at = traffic.QueueLine;
                yield return Shot("signal_queue", at - f * 38f + r * 7f + Vector3.up * 12f, at + f * 6f);
            }
            if (rush && !shotsTaken.Contains("stop_sign") && traffic.StopSignCar(out var sp, out var sf))
            {
                var f = new Vector3(sf.x, 0f, sf.y);
                var r = Vector3.Cross(Vector3.up, f);
                yield return Shot("stop_sign", sp - f * 13f + r * 4f + Vector3.up * 4.5f, sp + f * 8f);
            }
            if (rush && !shotsTaken.Contains("freeway"))
            {
                traffic.Snapshot(view);
                // the freeway car with the most freeway traffic within 120 m ahead of it
                int near = 0; CityTraffic.CarView? lead = null;
                foreach (var v in view)
                {
                    if (v.wrecked || v.turning || v.edge < 0 || map.edges[v.edge].cls < 5 || map.edges[v.edge].link) continue;
                    var fv = v.rot * Vector3.forward;
                    int k = 0;
                    foreach (var w in view)
                    {
                        if (w.wrecked || w.edge < 0 || map.edges[w.edge].cls < 5) continue;
                        var d = w.pos - v.pos;
                        float al = Vector3.Dot(d, fv);
                        if (al > 0f && al < 120f && Vector3.Cross(fv, d).magnitude < 25f) k++;
                    }
                    if (k > near) { near = k; lead = v; }
                }
                if (near >= 3 && lead != null)
                {
                    var f = lead.Value.rot * Vector3.forward; f.y = 0f; f.Normalize();
                    var r = Vector3.Cross(Vector3.up, f);
                    yield return Shot("freeway", lead.Value.pos - f * 30f + r * 9f + Vector3.up * 9f, lead.Value.pos + f * 25f);
                }
            }
            if (!rush && !shotsTaken.Contains("night") && Time.time - phaseT0 > 20f)
            {
                traffic.Snapshot(view);
                foreach (var v in view)
                {
                    if (v.wrecked || v.turning || v.speed < 4f) continue;
                    var f = v.rot * Vector3.forward; f.y = 0f; f.Normalize();
                    var r = Vector3.Cross(Vector3.up, f);
                    yield return Shot("night", v.pos + f * 22f + r * 5f + Vector3.up * 3.5f, v.pos);
                    break;
                }
            }
        }

        IEnumerator Shot(string name, Vector3 eye, Vector3 look)
        {
            shotsTaken.Add(name);
            yield return null;
            var cam = Camera.main;
            if (cam == null) { CityTrafficCheck.Line("  shot " + name + ": no camera"); yield break; }
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "citytraffic_" + name + ".png");
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
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                CityTrafficCheck.Line("  shot " + path);
            }
            else CityTrafficCheck.Line("  shot " + name + ": the pipeline takes no render request");
            cam.targetTexture = target;
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
