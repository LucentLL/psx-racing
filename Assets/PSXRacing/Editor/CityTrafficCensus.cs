using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD-LOGIC CENSUS BY TRAFFIC (2026-10-29, the owner: "I want to
    /// improve road logic (a lot of strange implementation currently) and I
    /// think logging traffic traveling the city can help improve road
    /// logic"). Charlotte free roam at rush hour (17:15) with the player a
    /// non-colliding ghost that TOURS the city: a greedy cover of every
    /// drivable road by discs of 380 m, visited nearest-first, each run for a
    /// share of the time budget at an accelerated time scale (the physics step
    /// stays 1/60 s, only more of them a frame). At each spot CityTraffic
    /// (CensusMode) fills the disc and every car is a probe: CityTraffic.Census
    /// logs what it meets into a CityRoadLog. Plus the static audit (signals,
    /// control, one-way traps and islands, limits, lane alignment, merge room).
    ///
    /// Settings (environment, tools\city-traffic-census.ps1 sets them):
    /// PSX_CENSUS_MINUTES (60) the whole run's budget, PSX_CENSUS_MAXSPOTS (0:
    /// all), PSX_CENSUS_CAP (64 cars), PSX_CENSUS_SPEED (30, the time scale
    /// ceiling), PSX_CENSUS_THENCHECK=1 runs CityTrafficCheck after it in the
    /// same editor. Out: census\events.csv, edges.txt, spots.csv, summary.json
    /// and PSXRacing_city_traffic_census.txt in the project folder.
    /// <see cref="Shots"/>: the close-ups of census\top6.txt.
    /// </summary>
    public static class CityTrafficCensus
    {
        internal static StringBuilder report;
        internal static int failures;
        internal static int exceptions;
        internal static string firstException;
        internal static bool shotsMode;
        internal static readonly List<(string label, Vector3 at, float heading)> shotSpots = new List<(string, Vector3, float)>();

        static float Env(string k, float d) =>
            float.TryParse(System.Environment.GetEnvironmentVariable(k) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : d;
        internal static float Minutes => Env("PSX_CENSUS_MINUTES", 60f);
        internal static int MaxSpots => (int)Env("PSX_CENSUS_MAXSPOTS", 0f);
        internal static int Cap => (int)Env("PSX_CENSUS_CAP", 64f);
        internal static float Speed => Env("PSX_CENSUS_SPEED", 30f);
        internal static bool ThenCheck => System.Environment.GetEnvironmentVariable("PSX_CENSUS_THENCHECK") == "1";
        internal static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        internal static string OutDir => Path.Combine(ProjectDir, "census");

        [MenuItem("PSX Racing/Road-logic census by traffic (play mode)")]
        public static void Run() => Begin(false);

        /// <summary>Close-ups of the spots in census\top6.txt (label|x|z|y|heading).</summary>
        public static void Shots() => Begin(true);

        static void Begin(bool shots)
        {
            EditionParking.RecoverIfNeeded();
            report = new StringBuilder();
            failures = 0; exceptions = 0; firstException = null;
            shotsMode = shots;
            shotSpots.Clear();
            if (shots)
            {
                string f = Path.Combine(OutDir, "top6.txt");
                if (File.Exists(f))
                    foreach (var line in File.ReadAllLines(f))
                    {
                        var p = line.Split('|');
                        if (p.Length < 5) continue;
                        float F(string x) => float.Parse(x, CultureInfo.InvariantCulture);
                        shotSpots.Add((p[0], new Vector3(F(p[1]), F(p[3]), F(p[2])), F(p[4])));
                    }
                if (shotSpots.Count == 0) { Fail("no census\\top6.txt to photograph"); Finish(); EditorApplication.Exit(1); return; }
            }
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int si = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (si < 0 || si >= scenes.Length || !File.Exists(scenes[si].path))
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
            CityTraffic.CapOverride = shots ? 48 : Cap;
            CityTraffic.CensusMode = true;
            CityTraffic.CensusRadiusM = shots ? 220f : 430f;
            CityTraffic.CensusDespawnM = shots ? 320f : 520f;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception && (stack ?? "").Contains("PSXRacing"))
            {
                exceptions++;
                if (firstException == null) firstException = msg + " @ " + (stack ?? "").Split('\n')[0];
            }
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            var go = new GameObject("CityTrafficCensusRunner");
            if (shotsMode) go.AddComponent<CityTrafficCensusShots>();
            else go.AddComponent<CityTrafficCensusRunner>();
        }

        internal static void Line(string s) { report.AppendLine(s); Debug.Log("[CENSUS] " + s); }
        internal static void Fail(string what) { failures++; report.AppendLine("  FAIL " + what); }

        internal static void Finish()
        {
            Application.logMessageReceived -= OnLog;
            CityTraffic.Log = null;
            CityTraffic.CensusMode = false;
            CityTraffic.CensusRadiusM = 430f; CityTraffic.CensusDespawnM = 520f;
            CityTraffic.ClockOverride = -1f;
            CityTraffic.CapOverride = -1;
            CityTraffic.Suspended = false;
            Time.timeScale = 1f;
            if (exceptions > 0) Fail($"runtime exceptions {exceptions}, first: {firstException}");
            report.AppendLine(failures == 0 ? (shotsMode ? "CENSUS SHOTS OK." : "CENSUS OK.") : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(ProjectDir, shotsMode ? "PSXRacing_city_traffic_census_shots.txt" : "PSXRacing_city_traffic_census.txt"), report.ToString());
        }

        /// <summary>Leave play mode and run the traffic check in this editor
        /// (it exits the editor when done).</summary>
        internal static void ChainCheck()
        {
            EditorApplication.playModeStateChanged += AfterExit;
            EditorApplication.ExitPlaymode();
        }

        static void AfterExit(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= AfterExit;
            EditorApplication.delayCall += CityTrafficCheck.Run;
        }
    }

    /// <summary>What the tour and the close-ups share: the session, the
    /// ghost, a park with its tiles built, a hang guard.</summary>
    public abstract class CityTrafficCensusBase : MonoBehaviour
    {
        protected CityMode mode;
        protected CityMap map;
        protected CarController player;
        protected CityTraffic traffic;
        float bornAt;
        protected bool finished;
        protected string stage = "loading";
        protected abstract float HangSeconds { get; }

        protected void Awake() { bornAt = Time.realtimeSinceStartup; }

        protected void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true;
            CityTrafficCensus.Fail("HUNG: no end after " + HangSeconds.ToString("0") + " s, stuck in " + stage);
            CityTrafficCensus.Finish();
            EditorApplication.Exit(1);
        }

        protected IEnumerator Arrive()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 300 && (CityMode.Instance == null || CityTraffic.Instance == null); i++) yield return null;
            mode = CityMode.Instance;
            traffic = CityTraffic.Instance;
            if (mode == null || traffic == null || mode.world == null || mode.world.Map == null)
            {
                CityTrafficCensus.Fail("no free-roam session with CityTraffic attached");
                yield break;
            }
            map = mode.world.Map;
            player = mode.player;
            Ghost();
        }

        // ---- the player as a ghost --------------------------------------------
        protected void Ghost()
        {
            if (player == null) return;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            player.enabled = false;
            foreach (var c in player.GetComponentsInChildren<Collider>()) c.enabled = false;
            player.Body.isKinematic = true;
            // the ghost's reflection probes: nothing to see, and with no
            // graphics device every frame logs a failed RenderTexture (1.8 GB
            // of log in ten minutes, 2026-10-29)
            foreach (var r in Object.FindObjectsByType<CarReflections>(FindObjectsInactive.Include)) r.enabled = false;
        }

        protected void Park(Vector2 at, int ring)
        {
            float y = 0f;
            if (map.NearestRoadPoint(at, 300f, false, out int ei, out float s, out _)) y = map.edges[ei].YAt(s);
            var p = new Vector3(at.x, y + 30f, at.y);
            player.Body.position = p;
            player.transform.position = p;
            mode.world.EnsureRing(p, ring);
        }
    }

    /// <summary>The census's tour (see <see cref="CityTrafficCensus"/>).</summary>
    public class CityTrafficCensusRunner : CityTrafficCensusBase
    {
        const float PlanRadiusM = 380f, MinGameS = 30f, MaxGameS = 90f;
        readonly List<CityTraffic.CarView> view = new List<CityTraffic.CarView>();
        protected override float HangSeconds => CityTrafficCensus.Minutes * 60f + 900f;

        IEnumerator Start()
        {
            yield return Arrive();
            if (traffic == null) { End(); yield break; }
            var log = new CityRoadLog(map.edges.Length);
            CityTraffic.Log = log;
            float tStart = Time.realtimeSinceStartup;
            float budgetS = CityTrafficCensus.Minutes * 60f - 100f;
            CityTrafficCensus.Line($"Charlotte road-logic census by traffic: budget {CityTrafficCensus.Minutes:0} min, cap {traffic.Cap} cars, time scale up to {CityTrafficCensus.Speed:0}, rush hour 17:15");

            // ---- the audit that needs no driving
            stage = "static audit";
            var rep = new StringBuilder();
            float a0 = Time.realtimeSinceStartup;
            traffic.CensusStaticAudit(log, rep);
            CityTrafficCensus.Line($"Static audit ({Time.realtimeSinceStartup - a0:0.0} s):");
            CityTrafficCensus.report.Append(rep);
            int staticEvents = log.events.Count;

            // ---- the tour
            stage = "planning";
            var spots = Plan(out float totalW, out float plannedW);
            CityTrafficCensus.Line($"Tour: {spots.Count} spots cover {plannedW / Mathf.Max(1f, totalW) * 100f:0}% of the weighted road ({totalW / 1000f:0} km weighted) within {PlanRadiusM:0} m");
            var spotRows = new StringBuilder("i,x,z,gain_km,game_s,real_s,stream_s,cars_mean,events\n");
            float rate = 6f, streamEst = 4f;
            var gains = new List<float>();
            foreach (var s in spots) gains.Add(s.gain);
            var visited = new List<Vector2>();
            int idx = 0;
            float timeScale = CityTrafficCensus.Speed;
            Time.maximumDeltaTime = 0.5f;
            while (idx < spots.Count)
            {
                float left = tStart + budgetS - Time.realtimeSinceStartup;
                if (left < 15f) { CityTrafficCensus.Line($"  budget spent: {spots.Count - idx} spots left unvisited"); break; }
                // drop the least useful spots ahead until each left gets its minimum
                float need = streamEst + MinGameS / Mathf.Max(0.5f, rate);
                while (spots.Count - idx > 1 && (spots.Count - idx) * need > left)
                {
                    int worst = -1;
                    for (int k = idx + 1; k < spots.Count; k++) if (worst < 0 || spots[k].gain < spots[worst].gain) worst = k;
                    if (worst < 0) break;
                    spots.RemoveAt(worst);
                }
                float sumG = 0f;
                for (int k = idx; k < spots.Count; k++) sumG += spots[k].gain;
                float driveReal = Mathf.Max(1f, left - streamEst * (spots.Count - idx)) * spots[idx].gain / Mathf.Max(1f, sumG);
                float allocGame = Mathf.Clamp(driveReal * rate, MinGameS, MaxGameS);

                stage = "spot " + idx;
                var sp = spots[idx].p;
                float r0 = Time.realtimeSinceStartup;
                Park(sp, 2);
                float streamS = Time.realtimeSinceStartup - r0;
                traffic.CensusArrive();
                float g0 = Time.time, r1 = Time.realtimeSinceStartup;
                int ev0 = log.events.Count;
                double carsSum = 0; int frames = 0;
                float nextOverlap = 0f;
                while (Time.time - g0 < allocGame)
                {
                    Time.timeScale = timeScale;
                    yield return null;
                    carsSum += traffic.Active; frames++;
                    if (Time.time >= nextOverlap) { nextOverlap = Time.time + 1f; Overlaps(log); }
                    if (Time.realtimeSinceStartup > tStart + budgetS && Time.time - g0 > 10f) break;
                }
                Time.timeScale = 1f;
                float game = Time.time - g0, real = Mathf.Max(0.01f, Time.realtimeSinceStartup - r1);
                rate = idx == 0 ? game / real : Mathf.Lerp(rate, game / real, 0.3f);
                streamEst = idx == 0 ? streamS : Mathf.Lerp(streamEst, streamS, 0.3f);
                visited.Add(sp);
                var ic = CultureInfo.InvariantCulture;
                spotRows.Append(string.Format(ic, "{0},{1:0},{2:0},{3:0.00},{4:0.0},{5:0.0},{6:0.0},{7:0.0},{8}\n", idx, sp.x, sp.y, spots[idx].gain / 1000f, game, real, streamS, frames > 0 ? carsSum / frames : 0, log.events.Count - ev0));
                Debug.Log(string.Format(ic, "[CENSUS] spot {0}/{1} ({2:0},{3:0}): {4:0} game s in {5:0.0} s (+{6:0.0} s tiles), {7:0.0} cars, {8} events; {9:0} min left",
                    idx + 1, spots.Count, sp.x, sp.y, game, real, streamS, frames > 0 ? carsSum / frames : 0, log.events.Count - ev0, (tStart + budgetS - Time.realtimeSinceStartup) / 60f));
                idx++;
            }
            Time.timeScale = 1f;

            // ---- what was driven
            stage = "writing";
            Write(log, visited, spotRows.ToString(), staticEvents, Time.realtimeSinceStartup - tStart);
            End();
        }

        void End()
        {
            finished = true;
            CityTrafficCensus.Finish();
            if (CityTrafficCensus.ThenCheck && CityTrafficCensus.failures == 0) CityTrafficCensus.ChainCheck();
            else EditorApplication.Exit(CityTrafficCensus.failures == 0 ? 0 : 1);
        }

        // ---- the plan ---------------------------------------------------------------
        struct Spot { public Vector2 p; public float gain; }

        /// <summary>A greedy cover of the drivable road (60 m pieces, locals
        /// weighted 0.7) by discs of <see cref="PlanRadiusM"/>, then a
        /// nearest-first tour from uptown.</summary>
        List<Spot> Plan(out float totalW, out float coveredW)
        {
            var pts = new List<Vector2>(); var ws = new List<float>();
            totalW = 0f;
            for (int i = 0; i < map.edges.Length; i++)
            {
                if (!traffic.Routable(i)) continue;
                var e = map.edges[i];
                float cw = e.link ? 1f : e.cls <= 0 ? 0.7f : e.cls == 1 ? 0.85f : 1f;
                int n = Mathf.Max(1, Mathf.CeilToInt(e.length / 60f));
                for (int k = 0; k < n; k++)
                {
                    pts.Add(e.PointAt((k + 0.5f) * e.length / n));
                    ws.Add(e.length / n * cw);
                    totalW += e.length / n * cw;
                }
            }
            const float C = 100f;
            var grid = new Dictionary<long, List<int>>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            for (int i = 0; i < pts.Count; i++)
            {
                long k = Key(Mathf.FloorToInt(pts[i].x / C), Mathf.FloorToInt(pts[i].y / C));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                l.Add(i);
            }
            // candidates: the centroid of every 200 m cell with road in it
            var cells = new Dictionary<long, (Vector2 sum, int n)>();
            foreach (var p in pts)
            {
                long k = Key(Mathf.FloorToInt(p.x / 200f), Mathf.FloorToInt(p.y / 200f));
                cells.TryGetValue(k, out var v);
                cells[k] = (v.sum + p, v.n + 1);
            }
            var cand = new List<Vector2>();
            foreach (var kv in cells) cand.Add(kv.Value.sum / kv.Value.n);
            var covered = new bool[pts.Count];
            float r2 = PlanRadiusM * PlanRadiusM;
            int rc = Mathf.CeilToInt(PlanRadiusM / C);
            float Gain(Vector2 c, bool take)
            {
                float g = 0f;
                int cx = Mathf.FloorToInt(c.x / C), cz = Mathf.FloorToInt(c.y / C);
                for (int dz = -rc; dz <= rc; dz++)
                    for (int dx = -rc; dx <= rc; dx++)
                    {
                        if (!grid.TryGetValue(Key(cx + dx, cz + dz), out var l)) continue;
                        foreach (int i in l)
                        {
                            if (covered[i] || (pts[i] - c).sqrMagnitude > r2) continue;
                            g += ws[i];
                            if (take) covered[i] = true;
                        }
                    }
                return g;
            }
            // lazy greedy (a gain only ever falls)
            var heap = new List<(float g, int i)>();
            for (int i = 0; i < cand.Count; i++) heap.Add((Gain(cand[i], false), i));
            heap.Sort((a, b) => b.g.CompareTo(a.g));
            var chosen = new List<Spot>();
            coveredW = 0f;
            int max = CityTrafficCensus.MaxSpots > 0 ? CityTrafficCensus.MaxSpots : 100000;
            while (heap.Count > 0 && chosen.Count < max && coveredW < totalW * 0.985f)
            {
                var top = heap[0];
                heap.RemoveAt(0);
                float g = Gain(cand[top.i], false);
                if (heap.Count > 0 && g < heap[0].g)
                {
                    // re-insert where it now belongs
                    int lo = 0, hi = heap.Count;
                    while (lo < hi) { int mid = (lo + hi) / 2; if (heap[mid].g > g) lo = mid + 1; else hi = mid; }
                    heap.Insert(lo, (g, top.i));
                    continue;
                }
                if (g < 500f) break;   // less than half a kilometre of new road: not worth the trip
                Gain(cand[top.i], true);
                coveredW += g;
                chosen.Add(new Spot { p = cand[top.i], gain = g });
            }
            // the smoke run (a few spots) keeps the best; the tour: nearest first from uptown
            var tour = new List<Spot>();
            var left = new List<Spot>(chosen);
            Vector2 at = map.uptown;
            while (left.Count > 0)
            {
                int bi = 0; float bd = float.MaxValue;
                for (int i = 0; i < left.Count; i++) { float d = (left[i].p - at).sqrMagnitude; if (d < bd) { bd = d; bi = i; } }
                tour.Add(left[bi]); at = left[bi].p; left.RemoveAt(bi);
            }
            return tour;
        }

        // ---- overlaps (the check's test, a traffic fault) --------------------------------
        void Overlaps(CityRoadLog log)
        {
            traffic.Snapshot(view);
            for (int i = 0; i < view.Count; i++)
            {
                var v = view[i];
                if (v.wrecked) continue;
                for (int j = i + 1; j < view.Count; j++)
                {
                    var w = view[j];
                    if (w.wrecked || (w.pos - v.pos).sqrMagnitude > 64f) continue;
                    if (Physics.ComputePenetration(v.box, v.pos, v.rot, w.box, w.pos, w.rot, out _, out float depth) && depth > 0.05f)
                        log.Add("traffic", "overlap", -1, v.edge, w.edge, v.pos, v.id, v.speed, depth, v.turning ? "turn" : "run",
                                $"cars {v.id} and {w.id} overlap {depth:0.00} m (a traffic fault, not the road's)");
                }
            }
        }

        // ---- the outputs ----------------------------------------------------------------
        void Write(CityRoadLog log, List<Vector2> visited, string spotRows, int staticEvents, float realS)
        {
            var ic = CultureInfo.InvariantCulture;
            Directory.CreateDirectory(CityTrafficCensus.OutDir);
            string Out(string f) => Path.Combine(CityTrafficCensus.OutDir, f);
            int ne = map.edges.Length;
            // driven: a good share of its length on runs, or a turn into / out of it
            var touched = new bool[ne];
            foreach (var kv in log.moves)
            {
                int eIn = (int)((kv.Key >> 32) >> 1), eOut = (int)(kv.Key & 0xffffffff);
                if (eIn >= 0 && eIn < ne) touched[eIn] = true;
                if (eOut >= 0 && eOut < ne) touched[eOut] = true;
            }
            float r2 = 430f * 430f;
            var sb = new StringBuilder(ne * 80);
            string[] cn = { "local", "minor", "collector", "arterial", "trunk", "freeway", "ramp" };
            var kmAll = new float[7]; var kmIn = new float[7]; var kmDrv = new float[7]; var nAll = new int[7]; var nIn = new int[7]; var nDrv = new int[7];
            double drivenKm = 0;
            int routable = 0;
            for (int i = 0; i < ne; i++)
            {
                if (!traffic.Routable(i)) continue;
                routable++;
                var e = map.edges[i];
                int k = e.link ? 6 : Mathf.Clamp(e.cls, 0, 5);
                bool drv = log.driven[i] >= 0.4f * Mathf.Min(e.length, 100f) || touched[i];
                var mid = e.PointAt(e.length * 0.5f);
                bool inDisc = false;
                foreach (var v in visited) if ((v - mid).sqrMagnitude < r2) { inDisc = true; break; }
                kmAll[k] += e.length / 1000f; nAll[k]++;
                if (inDisc) { kmIn[k] += e.length / 1000f; nIn[k]++; }
                if (drv) { kmDrv[k] += e.length / 1000f; nDrv[k]++; }
                drivenKm += log.driven[i] / 1000.0;
                // id;cls;link;oneway;limit;driven flag;metres driven;in a visited disc;name;x,z x,z ...
                sb.Append(i).Append(';').Append(e.cls).Append(';').Append(e.link ? 1 : 0).Append(';').Append(e.oneway ? 1 : 0).Append(';')
                  .Append(traffic.LimitKmh(i).ToString("0", ic)).Append(';').Append(drv ? 1 : 0).Append(';').Append(log.driven[i].ToString("0", ic)).Append(';')
                  .Append(inDisc ? 1 : 0).Append(';').Append((e.name ?? "").Replace(';', ',')).Append(';');
                Vector2 last = new Vector2(float.MaxValue, 0f);
                for (int p = 0; p < e.pts.Length; p++)
                {
                    var q = e.pts[p];
                    if (p > 0 && p < e.pts.Length - 1 && (q - last).sqrMagnitude < 64f) continue;
                    sb.Append(q.x.ToString("0", ic)).Append(',').Append(q.y.ToString("0", ic)).Append(' ');
                    last = q;
                }
                sb.Append('\n');
            }
            File.WriteAllText(Out("edges.txt"), sb.ToString());
            File.WriteAllText(Out("events.csv"), log.ToCsv(map));
            File.WriteAllText(Out("spots.csv"), spotRows);
            var mv = new StringBuilder("edge_in,dir_in,edge_out,count\n");
            foreach (var kv in log.moves)
            {
                int inKey = (int)(kv.Key >> 32);
                mv.Append(inKey >> 1).Append(',').Append((inKey & 1) == 1 ? 1 : -1).Append(',').Append((int)(kv.Key & 0xffffffff)).Append(',').Append(kv.Value).Append('\n');
            }
            File.WriteAllText(Out("moves.csv"), mv.ToString());

            // the summary
            float carH = (float)(log.carSeconds / 3600.0);
            CityTrafficCensus.Line($"Driven: {visited.Count} spots, {log.simSeconds / 60.0:0} game minutes in {realS / 60f:0} real, {carH:0.0} car-hours, {drivenKm:0} km driven, {log.spawned} cars born, {log.moves.Count} distinct movements taken");
            CityTrafficCensus.Line("Coverage by class (edges driven / in visited discs / all routable; km):");
            for (int k = 0; k < 7; k++)
                CityTrafficCensus.Line($"  {cn[k],-9} driven {nDrv[k]} ({kmDrv[k]:0} km) / in discs {nIn[k]} ({kmIn[k]:0} km) / all {nAll[k]} ({kmAll[k]:0} km)");
            CityTrafficCensus.Line($"Spawns: {log.spTries} tries - no free car {log.spNoFree}, no road within 45 m {log.spNoRoad}, not a spawn road {log.spNoSpawn}, weight said no {log.spReject}, too near an end {log.spBounds}, too near a car {log.spCrowded}; born {log.spawned}");
            CityTrafficCensus.Line($"Events: {log.events.Count} ({staticEvents} static){(log.dropped > 0 ? $", {log.dropped} past the cap not kept" : "")}");
            var keys = new List<string>(log.counts.Keys);
            keys.Sort();
            foreach (var k in keys) CityTrafficCensus.Line($"  {k} {log.counts[k]}");
            if (log.events.Count == staticEvents) CityTrafficCensus.Fail("the tour logged nothing (did any car drive?)");
            if (carH < 0.05f) CityTrafficCensus.Fail("under 3 car-minutes simulated");

            var js = new StringBuilder();
            js.Append("{\n");
            js.AppendFormat(ic, "  \"date\": \"{0:yyyy-MM-dd HH:mm}\",\n  \"budget_min\": {1:0},\n  \"real_min\": {2:0.0},\n  \"cap\": {3},\n  \"spots\": {4},\n", System.DateTime.Now, CityTrafficCensus.Minutes, realS / 60f, traffic.Cap, visited.Count);
            js.AppendFormat(ic, "  \"uptown\": [{0:0}, {1:0}],\n", map.uptown.x, map.uptown.y);
            js.AppendFormat(ic, "  \"game_min\": {0:0.0},\n  \"car_hours\": {1:0.00},\n  \"km_driven\": {2:0.0},\n  \"cars_born\": {3},\n  \"movements\": {4},\n  \"routable_edges\": {5},\n  \"events\": {6},\n  \"static_events\": {7},\n  \"dropped\": {8},\n",
                log.simSeconds / 60.0, carH, drivenKm, log.spawned, log.moves.Count, routable, log.events.Count, staticEvents, log.dropped);
            js.Append("  \"coverage\": {");
            for (int k = 0; k < 7; k++)
                js.AppendFormat(ic, "{0}\"{1}\": {{\"edges\": {2}, \"km\": {3:0.0}, \"edges_in_discs\": {4}, \"km_in_discs\": {5:0.0}, \"edges_driven\": {6}, \"km_driven\": {7:0.0}}}",
                    k == 0 ? "" : ", ", cn[k], nAll[k], kmAll[k], nIn[k], kmIn[k], nDrv[k], kmDrv[k]);
            js.Append("},\n  \"counts\": {");
            for (int i = 0; i < keys.Count; i++) js.AppendFormat(ic, "{0}\"{1}\": {2}", i == 0 ? "" : ", ", keys[i], log.counts[keys[i]]);
            js.Append("},\n  \"report\": [");
            var lines = CityTrafficCensus.report.ToString().Split('\n');
            bool first = true;
            foreach (var l in lines)
            {
                string t = l.TrimEnd('\r');
                if (t.Length == 0) continue;
                js.Append(first ? "\n    \"" : ",\n    \"").Append(t.Replace("\\", "\\\\").Replace("\"", "'")).Append('"');
                first = false;
            }
            js.Append("\n  ]\n}\n");
            File.WriteAllText(Out("summary.json"), js.ToString());
        }
    }

    /// <summary>The census's close-ups: each spot of census\top6.txt with rush
    /// hour traffic round it, a 3/4 view along the move it records.</summary>
    public class CityTrafficCensusShots : CityTrafficCensusBase
    {
        protected override float HangSeconds => 1200f;

        IEnumerator Start()
        {
            yield return Arrive();
            if (traffic == null) { Done(); yield break; }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CityTrafficCensus.Fail("no graphics device (run it watched, not -nographics)");
                Done(); yield break;
            }
            string dir = Path.Combine(CityTrafficCensus.ProjectDir, "Screenshots");
            Directory.CreateDirectory(dir);
            int i = 0;
            foreach (var (label, at, heading) in CityTrafficCensus.shotSpots)
            {
                i++;
                stage = "shot " + i;
                Park(new Vector2(at.x, at.z), 1);
                traffic.CensusArrive();
                float g0 = Time.time;
                while (Time.time - g0 < 14f) { Time.timeScale = 3f; yield return null; }
                Time.timeScale = 1f;
                yield return null;
                var f = new Vector3(Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad));
                var r = Vector3.Cross(Vector3.up, f);
                var eye = at - f * 24f + r * 8f + Vector3.up * 13f;
                yield return Shot(Path.Combine(dir, $"census_top{i}.png"), eye, at + f * 3f);
                CityTrafficCensus.Line($"shot {i}: {label}");
            }
            Done();
        }

        void Done()
        {
            finished = true;
            CityTrafficCensus.Finish();
            EditorApplication.Exit(CityTrafficCensus.failures == 0 ? 0 : 1);
        }

        IEnumerator Shot(string path, Vector3 eye, Vector3 look)
        {
            yield return null;
            var cam = Camera.main;
            if (cam == null) { CityTrafficCensus.Line("  no camera for " + path); yield break; }
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
            else CityTrafficCensus.Line("  the pipeline takes no render request");
            cam.targetTexture = target;
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
