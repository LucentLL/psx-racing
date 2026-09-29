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

        // SEVERAL RACES IN ONE LAUNCH (WP-08 review: G-play wants the three
        // city routes with fixed seeds, ten runs a route, trees on and off -
        // sixty launches of the editor): PSX_RACE_VENUES is a comma list of
        // venue ids, PSX_RACE_SEEDS a comma list of seeds, and PSX_CITY_TREES
        // "0" (off), "1" (on, the default) or "ab" (every race twice, trees on
        // then off). Each race enters play mode, races, leaves, and the next
        // opens its scene; one report at the end with a line per race.
        struct Job { public string venue; public int seed; public bool trees; }
        static readonly List<Job> jobs = new List<Job>();
        static int jobAt;
        internal static int SeedNow => jobAt < jobs.Count ? jobs[jobAt].seed : 0;
        internal static string VenueNow => jobAt < jobs.Count ? jobs[jobAt].venue : "?";
        internal static readonly List<string> summaries = new List<string>();

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();   // a killed edition build's park, back first
            log = new StringBuilder();
            failures = 0;
            jobs.Clear(); summaries.Clear(); jobAt = 0;
            string venues = System.Environment.GetEnvironmentVariable("PSX_RACE_VENUES");
            if (string.IsNullOrEmpty(venues)) venues = System.Environment.GetEnvironmentVariable("PSX_RACE_VENUE");
            if (string.IsNullOrEmpty(venues)) venues = "GillespieGap";
            string seeds = System.Environment.GetEnvironmentVariable("PSX_RACE_SEEDS");
            if (string.IsNullOrEmpty(seeds)) seeds = System.Environment.GetEnvironmentVariable("PSX_RACE_SEED") ?? "0";
            string treesMode = System.Environment.GetEnvironmentVariable("PSX_CITY_TREES") ?? "1";
            foreach (var v in venues.Split(','))
                foreach (var sd in seeds.Split(','))
                {
                    if (string.IsNullOrWhiteSpace(v) || !int.TryParse(sd.Trim(), out int seed)) continue;
                    if (treesMode == "ab")
                    {
                        jobs.Add(new Job { venue = v.Trim(), seed = seed, trees = true });
                        jobs.Add(new Job { venue = v.Trim(), seed = seed, trees = false });
                    }
                    else jobs.Add(new Job { venue = v.Trim(), seed = seed, trees = treesMode != "0" });
                }
            StartJob();
        }

        /// <summary>The race the current job asks for, or the report and the
        /// exit when there are no more.</summary>
        static void StartJob()
        {
            if (jobAt >= jobs.Count)
            {
                if (jobs.Count > 1)
                {
                    log.AppendLine();
                    log.AppendLine("ALL RACES (venue, seed, trees; rivals retired; what the retired hit; city tree trunks hit):");
                    foreach (var line in summaries) log.AppendLine("  " + line);
                }
                Finish();
                EditorApplication.Exit(failures == 0 ? 0 : 1);
                return;
            }
            var job = jobs[jobAt];
            PSXRacing.City.CityTrees.Enabled = job.trees;
            string id = job.venue;
            System.Environment.SetEnvironmentVariable("PSX_RACE_SEED", job.seed.ToString());
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) index = i;
            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + id + " is built");
                jobAt++;
                StartJob();
                return;
            }
            log.AppendLine("race on " + id + (jobs.Count > 1 ? $" (seed {job.seed}, city trees {(job.trees ? "on" : "off")})" : "") + ":");
            // The edition it plays AS (PSX_EDITION / -psxEdition; ALL by
            // default): a MAIN run races under MAIN's runtime rules.
            log.AppendLine("  edition " + Edition.Name(Edition.Current));
            Check(Edition.Ships(TrackCatalog.At(index)), id + " is a venue this edition ships");
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            string hour = System.Environment.GetEnvironmentVariable("PSX_RACE_HOUR");
            RaceHandoff.TimeOfDayIndex = hour == "night" ? TimeOfDay.Night : TimeOfDay.Morning;
            var cars = CarCatalog.All;
            // Four cars of a price, like a booked race: the player's and the
            // next three in the catalog.
            int seed = 0;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_RACE_SEED") ?? "0", out seed);
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

        /// <summary>A race is done: its summary line kept, then out of play
        /// mode and on to the next job (or the report).</summary>
        internal static void RaceDone(string summary)
        {
            summaries.Add(summary);
            jobAt++;
            // the report so far, after every race: a long batch cut short
            // still says what it raced
            if (jobs.Count > 1)
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_race_play_check.txt"),
                    log + "\n(" + summaries.Count + " of " + jobs.Count + " races so far)\n  " + string.Join("\n  ", summaries) + "\n");
            if (jobAt >= jobs.Count && jobs.Count <= 1) { Finish(); EditorApplication.Exit(failures == 0 ? 0 : 1); return; }
            EditorApplication.playModeStateChanged += OnLeft;
            EditorApplication.ExitPlaymode();
        }

        static void OnLeft(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnLeft;
            EditorApplication.delayCall += StartJob;
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
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_race_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class RacePlayCheckRunner : MonoBehaviour
    {
        RaceManager rm;
        float t0;

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
            int seedN = 0;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_RACE_SEED") ?? "0", out seedN);
            Random.InitState(9173 + seedN * 101);
            NoRendering();
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
                NoRendering();
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
            // did it FINISH (a run long enough for the route), and who crossed the line
            var done = new List<string>();
            foreach (var c in rm.allCars)
            {
                var p = c != null ? rm.GetProgress(c) : null;
                if (p != null && p.finished) done.Add(c.name);
            }
            RacePlayCheck.Note($"race state at the end: {rm.State}; finished: {(done.Count > 0 ? string.Join(", ", done) : "none")}");
            RacePlayCheck.Note($"city tree trunks hit: {trunkHits} (hard {trunkHard})");
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
            var why = new List<string>();
            foreach (var kv in retiredAt)
            {
                var r = kv.Key != null ? kv.Key.GetComponent<CollisionResponder>() : null;
                why.Add($"{(kv.Key != null ? kv.Key.name : "?")} at {kv.Value:0}s into {(r != null ? r.WorstHitWhat : "?")}");
            }
            string venue = RacePlayCheck.VenueNow;
            summary = $"{venue,-18} seed {RacePlayCheck.SeedNow,2} trees {(PSXRacing.City.CityTrees.Enabled ? "on " : "off")}: {retiredAt.Count} of {rivals} retired" +
                      (why.Count > 0 ? " (" + string.Join("; ", why) + ")" : "") +
                      $"; trunk hits {trunkHits} ({trunkHard} hard); raced {raced:0} s, {rm.State}, finished {done.Count}";
            Done();
        }

        string summary = "";
        int trunkHits, trunkHard;

        /// <summary>A -nographics editor has no GPU, and every camera still
        /// asked for a frame: each failed with a logged error and a stack
        /// trace, 1.4 GB of log for two races (WP-08 review), and the frames
        /// slow enough to race slower than real time. Nothing the race does
        /// reads a rendered frame, so the cameras are switched off (new ones,
        /// the mirror and the pizza cam, are caught on the next pass).</summary>
        static void NoRendering()
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam.enabled) cam.enabled = false;
        }

        /// <summary>Per car, the last 3 s: time, lateral (m right of the
        /// centreline), km/h, and the AI's line + give-way and limit.</summary>
        readonly Dictionary<string, Queue<string>> trail = new Dictionary<string, Queue<string>>();

        void FixedUpdate()
        {
            if (rm == null || rm.path == null || t0 <= 0f) return;
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
            string kind = what.StartsWith("WallColl") ? "wall" : what.StartsWith("Bank") ? "rock" :
                          what.StartsWith("Traffic") || what.Contains("traffic") ? "traffic" :
                          who.GetComponentInParent<CarController>() != null && what.Length > 0 && IsCarName(what) ? "car" : what;
            kindCount[kind] = kindCount.TryGetValue(kind, out int k) ? k + 1 : 1;
            kinds.Clear();
            foreach (var kv in kindCount) kinds.Add(kv.Key + " " + kv.Value);
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

        void Done()
        {
            CollisionResponder.HitReported -= OnHit;
            CollisionResponder.HitReportedOn -= OnHitOn;
            RaceManager.Respawned -= OnRespawn;
            RacePlayCheck.RaceDone(summary.Length > 0 ? summary : "(no race)");
        }
    }
}
