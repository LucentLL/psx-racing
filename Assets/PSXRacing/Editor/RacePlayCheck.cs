using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A WHOLE FIELD RACING (tools\race-play-check.ps1): a venue with three AI
    /// rivals and the player on autopilot, raced for PSX_RACE_SECONDS, logging
    /// every hard hit (who, how fast, into what, where) and every retirement.
    /// Written for "all three opponents knocked out in the first half mile"
    /// (2026-09-27): the owner wants a car to retire in about half of races,
    /// more often late than early - never the whole field in a minute.
    /// </summary>
    public static class RacePlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        /// <summary>One race of a run: where, at what traffic, which seed.</summary>
        internal struct Job { public string venue; public string traffic; public int seed; public bool trees, signs, poles; }
        internal static int SeedNow => current.seed;
        internal static string VenueNow => current.venue;
        static readonly Queue<Job> queue = new Queue<Job>();
        internal static Job current;
        /// <summary>More than one race in this Unity session
        /// (PSX_RACE_MATRIX): each race's report is APPENDED to the log file
        /// and one SUMMARY line per race goes to PSXRacing_race_matrix.txt.
        /// </summary>
        internal static bool matrix;
        static int totalFailures;

        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();   // a killed edition build's park, back first
            queue.Clear();
            totalFailures = 0;
            // PSX_RACE_MATRIX = "Venue:TRAFFIC:seed;Venue:TRAFFIC:seed;..." -
            // every race in ONE editor session (a Unity start per race was
            // most of the time a sweep took). Otherwise the one race the
            // PSX_RACE_VENUE / _TRAFFIC / _SEED variables name.
            string m = System.Environment.GetEnvironmentVariable("PSX_RACE_MATRIX");
            if (!string.IsNullOrEmpty(m))
            {
                foreach (var part in m.Split(';'))
                {
                    var f = part.Trim().Split(':');
                    if (f.Length == 0 || f[0].Length == 0) continue;
                    int sd = 0;
                    if (f.Length > 2) int.TryParse(f[2], out sd);
                    queue.Enqueue(new Job { venue = f[0], traffic = f.Length > 1 ? f[1] : "", seed = sd, trees = true, signs = true, poles = true });
                }
                matrix = true;
                File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_race_play_check.txt"), "");
            }
            else
            {
                // SEVERAL RACES IN ONE LAUNCH, the city's way (WP-08 review):
                // PSX_RACE_VENUES a comma list of venue ids, PSX_RACE_SEEDS a
                // comma list of seeds, and PSX_CITY_TREES / _SIGNS / _POLES
                // "1" (on, the default), "0" (off) or "ab" (every race twice,
                // on then off). One venue and one seed is the single race.
                string venues = System.Environment.GetEnvironmentVariable("PSX_RACE_VENUES");
                if (string.IsNullOrEmpty(venues)) venues = System.Environment.GetEnvironmentVariable("PSX_RACE_VENUE");
                if (string.IsNullOrEmpty(venues)) venues = "GillespieGap";
                string seeds = System.Environment.GetEnvironmentVariable("PSX_RACE_SEEDS");
                if (string.IsNullOrEmpty(seeds)) seeds = System.Environment.GetEnvironmentVariable("PSX_RACE_SEED");
                if (string.IsNullOrEmpty(seeds)) seeds = "0";
                string traffic = System.Environment.GetEnvironmentVariable("PSX_RACE_TRAFFIC") ?? "";
                string Mode(string key) { string v = System.Environment.GetEnvironmentVariable(key); return string.IsNullOrEmpty(v) ? "1" : v; }
                bool[] Ways(string mode) => mode == "ab" ? new[] { true, false } : new[] { mode != "0" };
                string treesMode = Mode("PSX_CITY_TREES"), signsMode = Mode("PSX_CITY_SIGNS"), polesMode = Mode("PSX_CITY_POLES");
                foreach (var v in venues.Split(','))
                    foreach (var sd in seeds.Split(','))
                    {
                        if (string.IsNullOrWhiteSpace(v) || !int.TryParse(sd.Trim(), out int seed)) continue;
                        foreach (bool tr in Ways(treesMode))
                            foreach (bool sg in Ways(signsMode))
                                foreach (bool pl in Ways(polesMode))
                                    queue.Enqueue(new Job { venue = v.Trim(), traffic = traffic, seed = seed, trees = tr, signs = sg, poles = pl });
                    }
                matrix = queue.Count > 1;
                if (matrix) File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_race_play_check.txt"), "");
            }
            StartNext();
        }

        /// <summary>The traffic level a name asks for (NONE, LIGHT, MEDIUM,
        /// HEAVY, RUSH / RUSHHOUR, or 0-4), or -1 for the hour's own.</summary>
        static int TrafficIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            string n = name.Trim().ToUpperInvariant().Replace(" ", "").Replace("_", "");
            string[] names = { "NONE", "LIGHT", "MEDIUM", "HEAVY", "RUSHHOUR" };
            for (int i = 0; i < names.Length; i++) if (n == names[i]) return i;
            if (n == "RUSH") return 4;
            if (int.TryParse(n, out int k) && k >= 0 && k < names.Length) return k;
            return -1;
        }

        static void StartNext()
        {
            if (queue.Count == 0)
            {
                EditorApplication.Exit(totalFailures == 0 ? 0 : 1);
                return;
            }
            current = queue.Dequeue();
            log = new StringBuilder();
            failures = 0;
            string id = current.venue;
            PSXRacing.City.CityTrees.Enabled = current.trees;
            PSXRacing.City.CitySigns.Enabled = current.signs;
            PSXRacing.City.CityPoles.Enabled = current.poles;
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) index = i;
            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                log.AppendLine("race on " + id + ":");
                Check(false, "the venue " + id + " is built");
                Finish();
                StartNext();
                return;
            }
            log.AppendLine("race on " + id + ":");
            // The edition it plays AS (PSX_EDITION / -psxEdition; ALL by
            // default): a MAIN run races under MAIN's runtime rules.
            log.AppendLine("  edition " + Edition.Name(Edition.Current));
            if (matrix) log.AppendLine($"  seed {current.seed}, city trees {(current.trees ? "on" : "off")}, signs {(current.signs ? "on" : "off")}, poles {(current.poles ? "on" : "off")}");
            Check(Edition.Ships(TrackCatalog.At(index)), id + " is a venue this edition ships");
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            string hour = System.Environment.GetEnvironmentVariable("PSX_RACE_HOUR");
            // Any hour by its name (night, noon, dusk ...); morning by default.
            int hourIdx = TimeOfDay.Morning;
            for (int h = 0; h < TimeOfDay.All.Length; h++)
                if (!string.IsNullOrEmpty(hour) && string.Equals(TimeOfDay.All[h].name, hour, System.StringComparison.OrdinalIgnoreCase))
                    hourIdx = h;
            RaceHandoff.TimeOfDayIndex = hourIdx;
            log.AppendLine("  hour " + TimeOfDay.All[hourIdx].name);
            // THE TRAFFIC LEVEL (the owner's per-race toggle), by reflection so
            // this harness also runs against a build from before it existed -
            // the BASELINE a change is measured against. Absent there, the
            // hour's own traffic runs, and the log says so.
            int lvl = TrafficIndex(current.traffic);
            var fld = typeof(RaceHandoff).GetField("TrafficLevel",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (fld != null) fld.SetValue(null, lvl);
            log.AppendLine("  traffic " + (lvl < 0 ? "by the hour" : current.traffic.ToUpperInvariant()) +
                           (fld == null ? " (this build has no traffic setting: the hour's own)" : ""));
            var cars = CarCatalog.All;
            // Four cars of a price, like a booked race: the player's and the
            // next three in the catalog.
            int seed = current.seed;
            log.AppendLine("  seed " + seed);
            int b = Mathf.Clamp(20 + seed * 7, 0, cars.Count - 5);
            RaceHandoff.CarSpecId = cars[b].id;
            RaceHandoff.OpponentSpecIds = cars[b + 1].id + ";" + cars[b + 2].id + ";" + cars[b + 3].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("RacePlayCheckRunner").AddComponent<RacePlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "RACE CHECK OK." : failures + " FAILURE(S).");
            totalFailures += failures;
            string path = Path.Combine(ProjectDir, "PSXRacing_race_play_check.txt");
            if (matrix) File.AppendAllText(path, log.ToString() + "\n");
            else File.WriteAllText(path, log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>One machine-readable line per race (the sweep's table).</summary>
        internal static void Summary(string line)
        {
            log.AppendLine("SUMMARY " + line);
            File.AppendAllText(Path.Combine(ProjectDir, "PSXRacing_race_matrix.txt"), line + "\n");
        }

        /// <summary>This race is over: the next one, or the exit.</summary>
        internal static void Done()
        {
            Finish();
            if (!matrix || queue.Count == 0)
            {
                EditorApplication.Exit(totalFailures == 0 ? 0 : 1);
                return;
            }
            EditorApplication.playModeStateChanged += OnExited;
            EditorApplication.ExitPlaymode();
        }

        static void OnExited(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnExited;
            // A frame for the editor to settle before the next scene opens.
            EditorApplication.delayCall += StartNext;
        }
    }

    public class RacePlayCheckRunner : MonoBehaviour
    {
        RaceManager rm;
        float t0;
        bool headless;

        /// <summary>Every camera off, twice a second (the replay's and the
        /// HUD overlay's come and go).</summary>
        IEnumerator CamerasOff()
        {
            while (true)
            {
                foreach (var c in Camera.allCameras) if (c != null) c.enabled = false;
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            RacePlayCheck.Check(rm != null && car != null && rm.path != null, "the scene has a race, a player and a path");
            if (rm == null || car == null) { Done(); yield break; }

            CollisionResponder.HitReported += OnHit;
            CollisionResponder.HitReportedOn += OnHitOn;
            RaceManager.Respawned += OnRespawn;
            // Repeatable per seed: the traffic laid at the green and the race's
            // incident roll both draw from here.
            int seedN = RacePlayCheck.current.seed;
            Random.InitState(9173 + seedN * 101);
            // PSX_RACE_TIMESCALE=3: the race in a third of the wall-clock time.
            // Physics keeps its fixed step (more steps per frame), so the
            // driving is the same; only the waiting goes.
            float scale = 1f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_RACE_TIMESCALE") ?? "1", out scale);
            Time.timeScale = Mathf.Clamp(scale, 0.25f, 8f);
            if (Time.timeScale > 1f) Time.maximumDeltaTime = 0.1f * Time.timeScale;
            RacePlayCheck.Note($"  time scale x{Time.timeScale:0.#}");
            // NO GRAPHICS DEVICE (-nographics): nothing can be drawn, and URP
            // logged "RenderTexture.Create failed" with a stack every frame -
            // 1.9 GB of raceplay.log in four races at an unpaced frame rate.
            // Cameras off (the race does not need them) and the frame rate
            // paced to 60 game-frames a second, so the per-frame half of the
            // game (lap counting, the HUD's clocks) steps as it does on a
            // screen.
            headless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (headless)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = Mathf.RoundToInt(60f * Time.timeScale);
                StartCoroutine(CamerasOff());
            }
            float until = Time.realtimeSinceStartup + 15f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < until) yield return null;
            RacePlayCheck.Check(rm.State == RaceManager.RaceState.Racing, "the race goes live", rm.State);
            RacePlayCheck.Note($"  road grip x{Seasons.RoadGripMult:0.00}, road {rm.path.roadWidth:0.0} m, two-way {(TrafficSystem.Instance != null && TrafficSystem.Instance.TwoWay)}");

            // The player's own input stack OFF, not just told to stand down: it
            // still wrote the parking brake over the autopilot every tick, the
            // player sat on the grid, and the traffic - spawned and recycled
            // round the PLAYER - never went where the rivals raced.
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            var auto = car.gameObject.AddComponent<AIDriver>();
            auto.path = rm.path;
            auto.skill = 0.9f;
            // Driven like the rivals, it must shift like them: the player's car
            // selects reverse on a held brake at a standstill, and the
            // autopilot, holding the brake behind a queue on a grade, drove it
            // backwards down Chimney Rock at 15 km/h ("WRONG WAY RX-7 Player").
            // The builder sets this false on every AI car for the same reason.
            car.allowReverse = false;

            // PSX_RACE_MISTAKE=0.3: hand every rival a driver error at that
            // fraction of the race (AIDriver.PlanMistake), to see what one does.
            var forced = System.Environment.GetEnvironmentVariable("PSX_RACE_MISTAKE");
            if (!string.IsNullOrEmpty(forced) && float.TryParse(forced, out float mf))
                foreach (var c in rm.allCars)
                {
                    var ai = c != null && c != car ? c.GetComponent<AIDriver>() : null;
                    if (ai != null) ai.PlanMistake(mf);
                }
            var wasMaking = new Dictionary<AIDriver, bool>();

            float seconds = 150f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_RACE_SECONDS") ?? "150", out seconds);
            t0 = Time.time;
            var retiredAt = new Dictionary<CarController, float>();
            // PSX_RACE_FINISH=1 (race-play-check.ps1 -Finish): the run is the
            // whole race - it goes on past the player's flag until every rival
            // has finished or retired (or the time is up), and every rival has
            // to get there, neither stalled nor driving the wrong way.
            bool toFinish = System.Environment.GetEnvironmentVariable("PSX_RACE_FINISH") == "1";
            var finishedAt = new Dictionary<CarController, float>();
            var wrongWay = new Dictionary<CarController, float>();
            bool RivalsRunning()
            {
                foreach (var c in rm.allCars)
                {
                    if (c == null || c == car) continue;
                    var p = rm.GetProgress(c);
                    if (p != null && !p.finished && !p.retired) return true;
                }
                return false;
            }
            while (Time.time - t0 < seconds &&
                   (rm.State == RaceManager.RaceState.Racing || (toFinish && RivalsRunning())))
            {
                yield return new WaitForSeconds(0.5f);
                foreach (var c in rm.allCars)
                {
                    var p = c != null ? rm.GetProgress(c) : null;
                    if (p == null) continue;
                    if (p.finished && !finishedAt.ContainsKey(c))
                    {
                        finishedAt[c] = Time.time - t0;
                        RacePlayCheck.Note($"FINISHED {c.name}{(c == car ? " (player, autopilot)" : "")} at {Time.time - t0:0}s");
                    }
                    if (p.finished || p.retired) continue;
                    // Wrong way: moving, nose against the road's direction.
                    Vector3 vel = c.Body != null ? Vector3.ProjectOnPlane(c.Body.linearVelocity, Vector3.up) : Vector3.zero;
                    if (vel.sqrMagnitude < 9f) continue;
                    // The car's own station (the race's progress hint): on a
                    // switchback the NEAREST station can be on the leg the other
                    // side of the hairpin, 13 m away and running the other way.
                    int wi = rm.path.NearestIndex(c.transform.position, p.nearestIdx);
                    Vector3 tan = Vector3.ProjectOnPlane(rm.path.GetTangent(wi), Vector3.up);
                    if (Vector3.Angle(vel, tan) > 110f)
                    {
                        wrongWay.TryGetValue(c, out float had);
                        wrongWay[c] = had + 0.5f;
                        if (had < 0.25f)
                            RacePlayCheck.Note($"WRONG WAY {c.name} at {Time.time - t0:0}s, wp {wi}, {vel.magnitude * 3.6f:0} km/h");
                    }
                }
                foreach (var c in rm.allCars)
                {
                    var ai = c != null ? c.GetComponent<AIDriver>() : null;
                    if (ai != null && c != car)
                    {
                        wasMaking.TryGetValue(ai, out bool was);
                        if (ai.MakingMistake && !was)
                            RacePlayCheck.Note($"MISTAKE {c.name} at {Time.time - t0:0}s, wp {rm.path.NearestIndex(c.transform.position)}, " +
                                               $"{Mathf.Abs(c.forwardSpeed) * 3.6f:0} km/h");
                        wasMaking[ai] = ai.MakingMistake;
                    }
                }
                foreach (var c in rm.allCars)
                {
                    var p = c != null ? rm.GetProgress(c) : null;
                    if (p == null || !p.retired || retiredAt.ContainsKey(c)) continue;
                    retiredAt[c] = Time.time - t0;
                    var r = c.GetComponent<CollisionResponder>();
                    RacePlayCheck.Note($"RETIRED {c.name} at {Time.time - t0:0}s, wp {rm.path.NearestIndex(c.transform.position)} " +
                                       $"({rm.path.NearestIndex(c.transform.position) * rm.path.spacing:0} m): worst hit " +
                                       $"{(r != null ? r.WorstHit.ToString("0.0") : "?")} m/s into {(r != null ? r.WorstHitWhat : "?")}, " +
                                       $"damage {(r != null ? r.DamageScore.ToString("0") : "?")}");
                }
            }
            CollisionResponder.HitReported -= OnHit;
            CollisionResponder.HitReportedOn -= OnHitOn;
            RaceManager.Respawned -= OnRespawn;
            // The eye's adaptation is a picture's: with no device and the
            // cameras off there is nothing to adapt to.
            if (headless) RacePlayCheck.Note("THE EYE (C10): not traced (no graphics device; cameras off)");
            else ReportEye();
            float raced = Time.time - t0;
            int rivals = 0;
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                var r = c.GetComponent<CollisionResponder>();
                var p = rm.GetProgress(c);
                if (c != car) rivals++;
                RacePlayCheck.Note($"{c.name}{(c == car ? " (player, autopilot)" : "")}: " +
                                   $"{(p != null ? rm.path.NearestIndex(c.transform.position) * rm.path.spacing : 0f):0} m, " +
                                   $"damage {(r != null ? r.DamageScore.ToString("0") : "?")}, hard hits {(r != null ? r.HardHits : 0)}, " +
                                   $"worst {(r != null ? r.WorstHit.ToString("0.0") : "?")} into {(r != null ? r.WorstHitWhat : "?")}" +
                                   (p != null && p.retired ? ", RETIRED" : ""));
            }
            var ts = TrafficSystem.Instance;
            if (ts != null) RacePlayCheck.Note(ts.WreckLog.Count + " traffic wrecks: " + string.Join("; ", ts.WreckLog));
            // A rival that is barely moving at the end: where it is, what it
            // is doing, and what traffic is round it.
            foreach (var c in rm.allCars)
            {
                // The autopilot player too: it races the same AIDriver.
                var ai = c != null ? c.GetComponent<AIDriver>() : null;
                var p = c != null ? rm.GetProgress(c) : null;
                if (ai == null || p == null || p.retired || p.finished || Mathf.Abs(c.forwardSpeed) > 3f) continue;
                RacePlayCheck.Note($"STALLED {c.name}: {Mathf.Abs(c.forwardSpeed) * 3.6f:0} km/h, trail: " +
                                   (trail.TryGetValue(c.name, out var tq) ? string.Join(" | ", tq) : "?"));
                // What is physically in front of it, at bumper height.
                foreach (var h in Physics.RaycastAll(c.transform.position + Vector3.up * 0.5f, c.transform.forward, 12f,
                                                     ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider != null && !h.collider.transform.IsChildOf(c.transform))
                        RacePlayCheck.Note($"    in front: {h.collider.name} (layer {h.collider.gameObject.layer}) at {h.distance:0.0} m");
                if (ts != null)
                    foreach (var rb in ts.Obstacles)
                    {
                        if (rb == null) continue;
                        Vector3 lo = c.transform.InverseTransformPoint(rb.position);
                        if (lo.z < -10f || lo.z > 150f) continue;
                        int i = rm.path.NearestIndex(rb.position);
                        Vector3 r = Vector3.Cross(Vector3.up, rm.path.GetTangent(i)).normalized;
                        float lat = Vector3.Dot(rb.position - rm.path.GetPoint(i), r);
                        RacePlayCheck.Note($"    traffic {rb.name} {lo.z:0} m ahead, lat {lat:+0.0;-0.0}, " +
                                           $"{rb.linearVelocity.magnitude * 3.6f:0} km/h{(rb.useGravity ? ", WRECK" : "")}");
                    }
                foreach (var o in rm.allCars)
                {
                    if (o == null || o == c) continue;
                    Vector3 lo = c.transform.InverseTransformPoint(o.transform.position);
                    if (lo.z < -10f || lo.z > 60f) continue;
                    RacePlayCheck.Note($"    racer {o.name} {lo.z:0} m ahead, {lo.x:+0.0;-0.0} across, {Mathf.Abs(o.forwardSpeed) * 3.6f:0} km/h" +
                                       (rm.GetProgress(o) != null && rm.GetProgress(o).retired ? ", RETIRED" : ""));
                }
            }
            RacePlayCheck.Note($"hits by kind: {string.Join(", ", kinds)}");
            RacePlayCheck.Note($"raced {raced:0} s; {retiredAt.Count} of {rivals} rivals retired");
            RacePlayCheck.Note($"city tree trunks hit: {trunkHits} (hard {trunkHard}); posts hit (billboards, gantries, utility poles: CityPost): {postHits} (hard {postHard})");
            RacePlayCheck.Check(retiredAt.Count <= 1, "at most one rival retires in the run", retiredAt.Count);
            if (toFinish)
            {
                int rivalsHome = 0, stalled = 0;
                float worstWrong = 0f;
                string wrongWho = "";
                foreach (var c in rm.allCars)
                {
                    if (c == null) continue;
                    var p = rm.GetProgress(c);
                    if (c != car && p != null && (p.finished || p.retired)) rivalsHome++;
                    if (c != car && p != null && !p.finished && !p.retired) stalled++;
                    if (wrongWay.TryGetValue(c, out float w) && w > worstWrong) { worstWrong = w; wrongWho = c.name; }
                }
                RacePlayCheck.Check(rivalsHome == rivals && stalled == 0,
                                    "every rival reaches the finish (or retires) within the run",
                                    rivalsHome + " of " + rivals + (stalled > 0 ? ", " + stalled + " still out there" : ""));
                RacePlayCheck.Check(finishedAt.Count >= rivals - retiredAt.Count, "and the finishers are counted",
                                    finishedAt.Count + " finished");
                // A spin can point a car backwards for a moment; a car that
                // DRIVES the wrong way does it for seconds.
                RacePlayCheck.Check(worstWrong <= 2f, "no car drives the wrong way for more than 2 s",
                                    worstWrong > 0f ? wrongWho + " " + worstWrong.ToString("0.0") + " s" : "none");
            }
            RacePlayCheck.Note($"FRAMES (WP-09; city streaming {(PSXRacing.City.CityWorld.SliceBuilds ? "SLICED" : "ONE TILE A FRAME")}): {framesN} frames, " +
                               $"over 33 ms {frames33}, over 50 ms {frames50}, over 100 ms {frames100}, worst {worstFrameMs:0} ms");
            ReportTraffic(retiredAt, finishedAt, raced);
            Done();
        }

        // ------------------------------------------------------------------
        //  RACERS IN TRAFFIC (2026-09-30): "AI really struggles to pass when
        //  there is heavy traffic". Measured from the outside - positions and
        //  velocities only - so the same numbers come off a build from before
        //  any change (the baseline) and after it.
        // ------------------------------------------------------------------

        /// <summary>Per racer and traffic car: the signed along-road gap last
        /// sample (+ = the traffic car is ahead).</summary>
        readonly Dictionary<(Object, Object), float> passGap = new Dictionary<(Object, Object), float>();
        readonly Dictionary<Object, int> hintOf = new Dictionary<Object, int>();
        /// <summary>Per car name: passes on the left over the centreline, on the
        /// left inside its own lane, on the right; seconds held up behind
        /// traffic (following in its lane at its speed or less).</summary>
        readonly Dictionary<string, int> passL = new Dictionary<string, int>(), passLane = new Dictionary<string, int>(),
                                         passR = new Dictionary<string, int>();
        readonly Dictionary<string, float> stuckS = new Dictionary<string, float>();
        int maxTraffic, trafficHitsRivals, trafficHitsPlayer, headOns, hardTrafficHitsRivals;
        int trunkHits, trunkHard, postHits, postHard;
        float lastPassSample;
        readonly List<string> headOnLines = new List<string>();

        int HintFor(Object o, Vector3 p)
        {
            if (!hintOf.TryGetValue(o, out int h)) h = -1;
            h = rm.path.NearestIndex(p, h);
            hintOf[o] = h;
            return h;
        }

        float LatAt(Vector3 p, int i)
        {
            Vector3 r = Vector3.Cross(Vector3.up, rm.path.GetTangent(i)).normalized;
            return Vector3.Dot(p - rm.path.GetPoint(i), r);
        }

        /// <summary>Ten times a (game) second: who passed whom and on which
        /// side, and who is sitting behind traffic.</summary>
        void SamplePasses()
        {
            var ts = TrafficSystem.Instance;
            if (ts == null || rm.State != RaceManager.RaceState.Racing) return;
            maxTraffic = Mathf.Max(maxTraffic, ts.Obstacles.Count);
            float total = rm.path.TotalLength;
            bool loop = !rm.path.HasEnds;
            float dtS = Time.fixedTime - lastPassSample;
            foreach (var c in rm.allCars)
            {
                if (c == null || c.Body == null) continue;
                var p = rm.GetProgress(c);
                if (p == null || p.finished || p.retired) continue;
                int ci = HintFor(c, c.transform.position);
                float cs = ci * rm.path.spacing;
                float cLat = LatAt(c.transform.position, ci);
                float cv = c.forwardSpeed;
                bool held = false;
                foreach (var rb in ts.Obstacles)
                {
                    if (rb == null || !rb.gameObject.activeInHierarchy) continue;
                    int ti = HintFor(rb, rb.position);
                    Vector3 tan = rm.path.GetTangent(ti);
                    float along = Vector3.Dot(rb.linearVelocity, tan);
                    var key = ((Object)c, (Object)rb);
                    // Same-direction, driving traffic only: an oncoming car is
                    // met, not passed; a wreck is scenery.
                    if (rb.useGravity || along < 1f) { passGap.Remove(key); continue; }
                    float d = ti * rm.path.spacing - cs;
                    if (loop) { if (d > total * 0.5f) d -= total; else if (d < -total * 0.5f) d += total; }
                    float tLat = LatAt(rb.position, ti);
                    if (passGap.TryGetValue(key, out float was) && Mathf.Abs(d - was) < 20f && was >= 0f && d < 0f && cv > along)
                    {
                        // Drawn level and gone by: the side is where the racer
                        // was at the moment it went past.
                        string n = c.name;
                        if (cLat > tLat) passR[n] = (passR.TryGetValue(n, out int k) ? k : 0) + 1;
                        else if (cLat >= 0.5f) passLane[n] = (passLane.TryGetValue(n, out int k2) ? k2 : 0) + 1;
                        else passL[n] = (passL.TryGetValue(n, out int k3) ? k3 : 0) + 1;
                    }
                    passGap[key] = d;
                    // Held up: in its lane, 4-40 m behind it, no faster than it.
                    if (d > 4f && d < 40f && Mathf.Abs(cLat - tLat) < 1.6f && cv <= along + 1f) held = true;
                }
                if (held) stuckS[c.name] = (stuckS.TryGetValue(c.name, out float s) ? s : 0f) + dtS;
            }
            lastPassSample = Time.fixedTime;
        }

        void ReportTraffic(Dictionary<CarController, float> retiredAt, Dictionary<CarController, float> finishedAt, float raced)
        {
            var car = rm.playerCar;
            int pl = 0, pln = 0, pr = 0, rivals = 0, retiredTraffic = 0;
            float stuckSum = 0f, stuckMax = 0f;
            var parts = new List<string>();
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                string n = c.name;
                passL.TryGetValue(n, out int l); passLane.TryGetValue(n, out int ln); passR.TryGetValue(n, out int r);
                stuckS.TryGetValue(n, out float st);
                parts.Add($"{n}{(c == car ? "*" : "")} L{l}/lane{ln}/R{r} held {st:0}s");
                if (c == car) continue;
                rivals++;
                pl += l; pln += ln; pr += r;
                stuckSum += st; stuckMax = Mathf.Max(stuckMax, st);
                if (retiredAt.ContainsKey(c))
                {
                    var resp = c.GetComponent<CollisionResponder>();
                    if (resp != null && resp.WorstHitWhat != null && resp.WorstHitWhat.StartsWith("Traffic")) retiredTraffic++;
                }
            }
            RacePlayCheck.Note("PASSES (L over the line / L inside its own lane / R on the verge) and seconds held up: " +
                               string.Join("; ", parts));
            foreach (var h in headOnLines) RacePlayCheck.Note(h);
            // The RIVALS' spread: the autopilot player drives in the middle of
            // the traffic window (traffic is born round the player), and its
            // finish says more about that than about the field.
            float spread = -1f, first = float.MaxValue, last = float.MinValue;
            int rivalsHome = 0;
            foreach (var kv in finishedAt)
            {
                if (kv.Key == car) continue;
                rivalsHome++;
                first = Mathf.Min(first, kv.Value); last = Mathf.Max(last, kv.Value);
            }
            if (rivalsHome >= 2) spread = last - first;
            passL.TryGetValue(car.name, out int ppl); passLane.TryGetValue(car.name, out int ppn);
            passR.TryGetValue(car.name, out int ppr); stuckS.TryGetValue(car.name, out float pst);
            var ts = TrafficSystem.Instance;
            int wrecksByRivals = 0;
            if (ts != null)
                foreach (var w in ts.WreckLog)
                    foreach (var c in rm.allCars)
                        if (c != null && c != car && w.Contains(c.name)) { wrecksByRivals++; break; }
            string venue = RacePlayCheck.current.venue;
            string lvl = string.IsNullOrEmpty(RacePlayCheck.current.traffic) ? "HOUR" : RacePlayCheck.current.traffic.ToUpperInvariant();
            RacePlayCheck.Summary($"{venue,-18} {lvl,-9} seed {RacePlayCheck.current.seed} | raced {raced:0}s | " +
                                  $"passes L {pl} lane {pln} R {pr} | held avg {(rivals > 0 ? stuckSum / rivals : 0f):0}s max {stuckMax:0}s | " +
                                  $"traffic hits rivals {trafficHitsRivals} (hard {hardTrafficHitsRivals}) player {trafficHitsPlayer} | " +
                                  $"head-ons {headOns} | retired {retiredAt.Count} (traffic {retiredTraffic}) | " +
                                  $"wrecks by rivals {wrecksByRivals} | rivals home {rivalsHome}/{rivals} spread {(spread >= 0f ? spread.ToString("0") + "s" : "-")} | " +
                                  $"player L{ppl}/lane{ppn}/R{ppr} held {pst:0}s{(finishedAt.ContainsKey(car) ? "" : " DNF")} | " +
                                  $"max traffic {maxTraffic} | fails {RacePlayCheck.failures}");
            // NONE means none: nothing on the road, the whole race.
            if (lvl == "NONE") RacePlayCheck.Check(maxTraffic == 0, "TRAFFIC NONE: not one traffic car on the road", maxTraffic);
        }

        /// <summary>Per car, the last 3 s: time, lateral (m right of the
        /// centreline), km/h, and the AI's line + give-way and limit.</summary>
        readonly Dictionary<string, Queue<string>> trail = new Dictionary<string, Queue<string>>();

        void FixedUpdate()
        {
            if (rm == null || rm.path == null || t0 <= 0f) return;
            if (Time.fixedTime - lastPassSample >= 0.1f) SamplePasses();
            if (Time.fixedTime - lastTrail < 0.25f) return;
            lastTrail = Time.fixedTime;
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                var ai = c.GetComponent<AIDriver>();
                int i = rm.path.NearestIndex(c.transform.position);
                Vector3 r = Vector3.Cross(Vector3.up, rm.path.GetTangent(i)).normalized;
                float lat = Vector3.Dot(c.transform.position - rm.path.GetPoint(i), r);
                // Steer input and slip (velocity off the nose): a weave with the
                // wheel swinging is the controller; with slip, the car let go.
                // Everything in PLAN: a grade's pitch read as 7 deg of slip.
                var body = c.Body;
                Vector3 nose = Vector3.ProjectOnPlane(c.transform.forward, Vector3.up);
                Vector3 vel = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up) : Vector3.zero;
                float slip = vel.sqrMagnitude > 4f ? Vector3.SignedAngle(nose, vel, Vector3.up) : 0f;
                float head = Vector3.SignedAngle(Vector3.ProjectOnPlane(rm.path.GetTangent(i), Vector3.up), nose, Vector3.up);
                float vAcross = Vector3.Dot(vel, r);
                string s = $"{Time.time - t0:0.00}s lat {lat:+0.0;-0.0} {Mathf.Abs(c.forwardSpeed) * 3.6f:0}kmh" +
                           $" st {c.steerInput:+0.00;-0.00} sl {slip:+0;-0} hd {head:+0;-0} vx {vAcross:+0.0;-0.0}" +
                           (ai != null ? $" line {ai.DebugLine:+0.0;-0.0} bias {ai.DebugBias:+0.0;-0.0}" +
                                         (float.IsNegativeInfinity(ai.DebugLeftLimit) ? "" : $" LIM {ai.DebugLeftLimit:+0.0;-0.0}") +
                                         PassNote(ai) +
                                         // pedals, gear, and what the give-way asked for
                                         $" th {c.throttleInput:0.00} br {c.brakeInput:0.00} g {c.currentGear}" +
                                         (ai.DebugLift > 0f || ai.DebugTrafficBrake > 0f
                                             ? $" give {ai.DebugLift:0.00}/{ai.DebugTrafficBrake:0.00}" : "") : "");
                if (!trail.TryGetValue(c.name, out var q)) trail[c.name] = q = new Queue<string>();
                q.Enqueue(s);
                while (q.Count > 12) q.Dequeue();
                lastPose[c.name] = (c.transform.position, c.transform.rotation);
            }
        }
        float lastTrail;

        /// <summary>The pass this tick for the trail (" pass R VERGE"), read by
        /// reflection so the harness still builds against a baseline from
        /// before AIDriver.DebugPassSide existed.</summary>
        static readonly System.Reflection.PropertyInfo passSideProp = typeof(AIDriver).GetProperty("DebugPassSide");
        static readonly System.Reflection.PropertyInfo onVergeProp = typeof(AIDriver).GetProperty("DebugOnVerge");
        static string PassNote(AIDriver ai)
        {
            if (passSideProp == null || ai == null) return "";
            int side = (int)passSideProp.GetValue(ai);
            if (side == 0) return "";
            bool verge = onVergeProp != null && (bool)onVergeProp.GetValue(ai);
            return " pass " + (side < 0 ? "L" : "R") + (verge ? " VERGE" : "");
        }
        /// <summary>Each car's pose at its last trail sample (the recovery
        /// note looks round where the car WAS, not where it was put).</summary>
        readonly Dictionary<string, (Vector3 pos, Quaternion rot)> lastPose = new Dictionary<string, (Vector3, Quaternion)>();

        readonly List<string> kinds = new List<string>();
        readonly Dictionary<string, int> kindCount = new Dictionary<string, int>();

        void OnRespawn(CarController c, int step, float lat)
        {
            if (rm == null || c == null) return;
            var ai = c.GetComponent<AIDriver>();
            RacePlayCheck.Note($"  recover {c.name} at {Time.time - t0:0}s ({(ai != null ? ai.LastRecoveryWhy : "?")}), " +
                               $"wp {rm.path.NearestIndex(c.transform.position)}: " +
                               (step < 0 ? $"NO clear station, seated on the centreline (asked lat {lat:+0.0;-0.0})"
                                         : $"{step} stations on, lat {lat:+0.0;-0.0}"));
            // PSX_RACE_WHY=1: what the car was doing before it was put back,
            // and what stood round it (the trail is from before the respawn:
            // FixedUpdate has not sampled the new pose yet).
            if (System.Environment.GetEnvironmentVariable("PSX_RACE_WHY") == "1")
            {
                if (trail.TryGetValue(c.name, out var tq))
                    RacePlayCheck.Note("      trail: " + string.Join(" | ", tq));
                var ts = TrafficSystem.Instance;
                if (ts != null && lastPose.TryGetValue(c.name, out var pose))
                    foreach (var rb in ts.Obstacles)
                    {
                        if (rb == null) continue;
                        Vector3 lo = Quaternion.Inverse(pose.rot) * (rb.position - pose.pos);
                        if (lo.z < -12f || lo.z > 40f || Mathf.Abs(lo.x) > 12f) continue;
                        RacePlayCheck.Note($"      traffic {rb.name} {lo.z:+0;-0} m ahead, {lo.x:+0.0;-0.0} across, " +
                                           $"{rb.linearVelocity.magnitude * 3.6f:0} km/h{(rb.useGravity ? ", WRECK" : "")}");
                    }
            }
        }

        void OnHit(CollisionResponder who, float speed, bool hard, string what)
        {
            if (speed < 6f || rm == null) return;
            if (what == PSXRacing.City.CityTrees.TrunkName) { trunkHits++; if (hard) trunkHard++; }
            if (what == PSXRacing.City.CitySigns.PostName) { postHits++; if (hard) postHard++; }
            string kind = what.StartsWith("WallColl") ? "wall" : what.StartsWith("Bank") ? "rock" :
                          what.StartsWith("Traffic") || what.Contains("traffic") ? "traffic" :
                          who.GetComponentInParent<CarController>() != null && what.Length > 0 && IsCarName(what) ? "car" : what;
            kindCount[kind] = kindCount.TryGetValue(kind, out int k) ? k + 1 : 1;
            kinds.Clear();
            foreach (var kv in kindCount) kinds.Add(kv.Key + " " + kv.Value);
            if (kind == "traffic")
            {
                bool isPlayer = who.GetComponentInParent<CarController>() == rm.playerCar;
                if (isPlayer) trafficHitsPlayer++;
                else { trafficHitsRivals++; if (speed >= 10f) hardTrafficHitsRivals++; }
                // HEAD-ON: the traffic car was coming at us.
                var orb = lastHitOther != null ? lastHitOther.GetComponentInParent<Rigidbody>() : null;
                if (orb != null && Vector3.Dot(orb.linearVelocity, who.transform.forward) < -3f)
                {
                    headOns++;
                    headOnLines.Add($"HEAD-ON {who.name} at {Time.time - t0:0}s, {speed:0.0} m/s into {what}");
                }
            }
            if (speed < 10f) return;
            // The three seconds before a hard hit, for the car that took it.
            if (speed >= 15f && trail.TryGetValue(who.name, out var tr))
                RacePlayCheck.Note($"    before {who.name}'s hit: " + string.Join(" | ", tr));
            int wp = rm.path.NearestIndex(who.transform.position);
            // Where across the road: metres right of the centreline, for the
            // car and (found by name) what it hit; and which way the road bends.
            float Lat(Vector3 p)
            {
                int i = rm.path.NearestIndex(p);
                Vector3 r = Vector3.Cross(Vector3.up, rm.path.GetTangent(i)).normalized;
                return Vector3.Dot(p - rm.path.GetPoint(i), r);
            }
            // The collider that was hit (OnHitOn keeps it), not a Find by
            // name: traffic cars share names, and the old lookup reported
            // wherever ANOTHER Crown Vic happened to be.
            var other = lastHitOther != null && lastHitOther.name == what ? lastHitOther.gameObject : null;
            Vector3 t0v = rm.path.GetTangent(wp), t1v = rm.path.GetTangent(Mathf.Min(wp + 5, rm.path.Count - 1));
            float turn = Vector3.SignedAngle(t0v, t1v, Vector3.up);
            RacePlayCheck.Note($"  hit {Time.time - t0,5:0.0}s {who.name}: {speed:0.0} m/s {(hard ? "HARD" : "glancing")} into {what} at wp {wp}" +
                               $"  car lat {Lat(who.transform.position):+0.0;-0.0}" +
                               (other != null ? $", its lat {Lat(other.transform.position):+0.0;-0.0}" : "") +
                               $", road {(turn < -3f ? "bends LEFT" : turn > 3f ? "bends right" : "straight")} ({turn:0} deg over 20 m)" +
                               (other != null ? OtherInCarFrame(who.transform, other) : ""));
        }

        Collider lastHitOther;

        // HitReportedOn is raised just before HitReported for the same contact.
        void OnHitOn(CollisionResponder who, float speed, bool hard, Collider c) { lastHitOther = c; }

        static string OtherInCarFrame(Transform car, GameObject other)
        {
            Vector3 lo = car.InverseTransformPoint(other.transform.position);
            var rb = other.GetComponentInParent<Rigidbody>();
            float along = rb != null ? Vector3.Dot(rb.linearVelocity, car.forward) : 0f;
            return $" | it was {lo.z:+0.0;-0.0} m ahead, {lo.x:+0.0;-0.0} right, moving {along * 3.6f:+0;-0} km/h along our heading";
        }

        bool IsCarName(string what)
        {
            foreach (var c in rm.allCars) if (c != null && c.name == what) return true;
            return false;
        }

        // ------------------------------------------------------------------
        //  THE EYE'S ADAPTATION, traced (the colour pass, C10)
        // ------------------------------------------------------------------

        struct EyeSample { public float t, m, a, target, open; public bool tunnel; }
        readonly List<EyeSample> eye = new List<EyeSample>();
        float lastEye;
        int eyeHint = -1;

        /// <summary>Ten times a second: where the player is and what the eye
        /// is doing (ExposureAdapt, stepped by PSXGlobals this frame).</summary>
        // WP-09: how the race's frames went (the city's tile builds are the
        // spikes): frames over 33 / 50 / 100 ms and the worst, real time.
        int framesN, frames33, frames50, frames100;
        float worstFrameMs;

        // THE CITY BENCH (2026-10-02), PSX_RACE_BENCHCHECK=1 in a Charlotte
        // race: the date to winter (the planted trees must wear winter), then
        // three debug teleports - the I-277 loop, rural Beatties Ford 30 km
        // out, Uptown - each judged two seconds later: on a road, at its
        // height, not falling.
        int benchStep;
        float benchNext = 8f;
        void BenchCheck()
        {
            if (System.Environment.GetEnvironmentVariable("PSX_RACE_BENCHCHECK") != "1") return;
            var mode = FindAnyObjectByType<PSXRacing.City.CityMode>();
            var world = PSXRacing.City.CityWorld.Active;
            if (mode == null || world == null || world.Map == null) return;
            float t = Time.time - t0;
            if (t < benchNext || benchStep > 6) return;
            var car = rm.playerCar;
            switch (benchStep)
            {
                case 0:
                {
                    PSXRacing.LifeSim.DebugWorldOps.SetDay(PSXRacing.LifeSim.DebugWorldOps.DayIn(Season.Winter));
                    var want = PSXRacing.City.CityTrees.MaterialFor(PSXRacing.City.CityTrees.DressNow());
                    int trees = 0, dressed = 0;
                    foreach (var mr in world.GetComponentsInChildren<MeshRenderer>(false))
                    {
                        if (mr.gameObject.name != "Trees") continue;
                        trees++;
                        if (mr.sharedMaterial == want) dressed++;
                    }
                    RacePlayCheck.Note($"BENCH date -> {PSXRacing.LifeSim.DebugWorldOps.DateLine()}, dress {Seasons.DressNames[Seasons.CurrentDress]}: {dressed}/{trees} tiles' trees re-dressed");
                    RacePlayCheck.Check(trees > 0 && dressed == trees && Seasons.Current == Season.Winter,
                                        "the bench's date puts the planted trees in the day's dress", dressed + "/" + trees);
                    break;
                }
                case 1: case 3: case 5:
                {
                    Vector2[] spots = { LLPlan(35.2195, -80.8500), new Vector2(-8938f, 26444f), new Vector2(-2314f, 4726f) };
                    string[] names = { "I-277", "Beatties Ford", "Uptown" };
                    int k = benchStep / 2;
                    bool ok = mode.DebugTeleport(spots[k], out string where);
                    RacePlayCheck.Note($"BENCH teleport {names[k]} -> {(ok ? where : "REFUSED")}");
                    RacePlayCheck.Check(ok, "the bench can teleport to " + names[k]);
                    benchNext = t + 2f;
                    benchStep++;
                    return;
                }
                case 2: case 4: case 6:
                {
                    var p = car.transform.position;
                    bool onRoad = world.Map.NearestRoadPoint(new Vector2(p.x, p.z), 12f, false, out int ei, out float at, out float d);
                    float dy = onRoad ? p.y - world.Map.edges[ei].YAt(at) : float.NaN;
                    float vy = car.GetComponent<Rigidbody>().linearVelocity.y;
                    RacePlayCheck.Note($"  2 s later: {d:0.0} m off the road's line, {dy:+0.00;-0.00} m over its height, falling {-vy:0.0} m/s");
                    RacePlayCheck.Check(onRoad && Mathf.Abs(dy) < 1.5f && vy > -3f, "the car landed on the road and stayed there");
                    break;
                }
            }
            benchStep++;
            benchNext = t + 1f;
        }

        static Vector2 LLPlan(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * PSXRacing.City.CityMap.LayoutScale;
        }

        void Update()
        {
            if (rm == null || rm.path == null || t0 <= 0f || rm.playerCar == null) return;
            float fms = Time.unscaledDeltaTime * 1000f;
            framesN++;
            if (fms > 33f) frames33++;
            if (fms > 50f) frames50++;
            if (fms > 100f) frames100++;
            if (fms > worstFrameMs) worstFrameMs = fms;
            BenchCheck();
            if (Time.time - lastEye < 0.1f) return;
            lastEye = Time.time;
            eyeHint = rm.path.NearestIndex(rm.playerCar.transform.position, eyeHint, 40);
            eye.Add(new EyeSample
            {
                t = Time.time - t0, m = eyeHint * rm.path.spacing, a = ExposureAdapt.Current,
                target = ExposureAdapt.Target, open = ExposureAdapt.Openness, tunnel = ExposureAdapt.Tunnel,
            });
        }

        /// <summary>
        /// The eye's trace, judged: through every tunnel, the gain it reached
        /// 1 and 2 s after the portal (readable in about 2 s: the settled
        /// tunnel gain is 2.4, and 1 + 1.4 x (1 - e^-2/1.2) = 2.13 at 2 s),
        /// the time it took to settle after the exit; and on the open road -
        /// open sky over the car, more than 3 s from any tunnel - the most it
        /// ever rose (no pumping under trees: at most 1.10).
        /// </summary>
        void ReportEye()
        {
            if (eye.Count == 0) { RacePlayCheck.Note("THE EYE (C10): no samples"); return; }
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var s in eye) { lo = Mathf.Min(lo, s.a); hi = Mathf.Max(hi, s.a); }
            RacePlayCheck.Note($"THE EYE (C10): {eye.Count} samples at 10 Hz, gain {lo:0.00}-{hi:0.00}");
            int tunnels = 0;
            for (int i = 0; i < eye.Count; i++)
            {
                if (!eye[i].tunnel || (i > 0 && eye[i - 1].tunnel)) continue;
                int j = i;
                while (j + 1 < eye.Count && eye[j + 1].tunnel) j++;
                tunnels++;
                float te = eye[i].t, tx = eye[j].t;
                float At(float t) { foreach (var s in eye) if (s.t >= t) return s.a; return eye[eye.Count - 1].a; }
                float a1 = At(te + 1f), a2 = At(te + 2f);
                // Settled after the exit: back within 5% of its target.
                float settle = -1f;
                for (int k = j + 1; k < eye.Count; k++)
                    if (Mathf.Abs(eye[k].a - eye[k].target) <= 0.05f * eye[k].target) { settle = eye[k].t - tx; break; }
                var trace = new System.Text.StringBuilder();
                for (float t = te - 1f; t <= tx + 3f + 1e-3f; t += 0.5f) trace.Append($" {At(t):0.00}");
                RacePlayCheck.Note($"  tunnel {tunnels}: in at {te:0.0}s ({eye[i].m:0} m), out at {tx:0.0}s ({eye[j].m:0} m); " +
                                   $"gain +1 s {a1:0.00}, +2 s {a2:0.00}; after the exit settled in {(settle >= 0f ? settle.ToString("0.0") + " s" : "never")}");
                RacePlayCheck.Note("    gain every 0.5 s from 1 s before the portal to 3 s after the exit:" + trace);
                if (tx - te >= 2f)
                    RacePlayCheck.Check(a2 >= 1.9f, $"tunnel {tunnels}: the eye has opened up 2 s in (gain >= 1.9)", a2.ToString("0.00"));
                RacePlayCheck.Check(settle >= 0f && settle <= 2.5f, $"tunnel {tunnels}: the exit's bloom settles within 2.5 s",
                                    settle >= 0f ? settle.ToString("0.0") + " s" : "never");
            }
            // The open road, away from the tunnels.
            float openMax = 1f; int openN = 0, crossings = 0; bool above = false;
            float shadeMax = 1f; int shadeN = 0;
            for (int i = 0; i < eye.Count; i++)
            {
                bool nearTunnel = false;
                for (int k = Mathf.Max(0, i - 30); k <= i && !nearTunnel; k++) nearTunnel |= eye[k].tunnel;
                if (nearTunnel || eye[i].tunnel) { above = false; continue; }
                if (eye[i].open >= 0.99f)
                {
                    openN++;
                    openMax = Mathf.Max(openMax, eye[i].a);
                    bool now = eye[i].a > 1.10f;
                    if (now && !above) crossings++;
                    above = now;
                }
                else { shadeN++; shadeMax = Mathf.Max(shadeMax, eye[i].a); }
            }
            RacePlayCheck.Note($"  open road: {openN} samples, gain at most {openMax:0.00}, {crossings} rise(s) over 1.10; " +
                               $"under something (a bridge, a building's shadow): {shadeN} samples, gain at most {shadeMax:0.00}");
            RacePlayCheck.Check(openMax <= 1.10f, "no pumping on the open road (gain <= 1.10 under an open sky)", openMax.ToString("0.00"));
        }

        void Done()
        {
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 1f / 3f;
            Application.targetFrameRate = -1;
            StopAllCoroutines();
            CollisionResponder.HitReported -= OnHit;
            CollisionResponder.HitReportedOn -= OnHitOn;
            RaceManager.Respawned -= OnRespawn;
            RacePlayCheck.Done();
        }
    }
}
