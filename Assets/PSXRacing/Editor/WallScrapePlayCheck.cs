using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// LEAN THE REAL CAR ON THE REAL WALLS, IN THE RUNNING GAME.
    ///
    /// <see cref="WallScrapeAudit"/> flies a box: the player's size, mass and
    /// sweep, and nothing else. The owner's report — "the walls seem smooth
    /// (even bridges for drag race), but the car goes from full speed to
    /// instantly stopped" — is about the CAR, and the car is more than a box.
    /// It has tyres steering it into the barrier, a lateral stabilizer
    /// fighting that, a CollisionResponder that scrubs speed on a hard hit and
    /// counts it on the driving record, and a ghost filter registered by
    /// CarController itself. Any of those can turn a clean wall into a stop,
    /// or a snag into something that looks clean in a proxy. So this loads
    /// the race scenes in PLAY mode — one editor session for the whole list,
    /// the ReverseRaceCheck pattern — takes the player's own car as it ships,
    /// and drives it along the walls the way the report describes: full
    /// throttle from 137 km/h, nose three degrees into the barrier, steered
    /// to aim just inside the wall and shoved at half a g so it cannot drift
    /// off it and pass by accident. Both directions, straights and bends.
    ///
    /// A SNAG is what the report describes and nothing a scrape can explain:
    /// more than 4 m/s of speed along the road gone in ONE physics step, or
    /// more than 8 m/s within six. A scrape must also add ZERO hard hits.
    /// CollisionResponder counts an incident on a square contact above
    /// 22 km/h, and a seam's end face IS a square contact — which is how a
    /// rub along a bridge rail used to land on the insurance record as a
    /// crash.
    ///
    /// TWO CONTROLS, so "smooth" cannot mean "not there". Head-on: square
    /// into a wall at 72 km/h, which must stop the car at the face AND count
    /// a hard hit — a filter that deleted real contacts would fail it. And
    /// the lean: a drive that spent most of its time off the wall is printed
    /// as proving nothing, and a venue where that is most drives FAILS.
    ///
    /// Venues: PSX_SCRAPE_VENUES (comma list, default
    /// <see cref="DefaultVenues"/>). The ghost filter runs as shipped;
    /// PSX_SCRAPE_FILTER=0 forces it off for an A/B. Menu: PSX Racing/Check
    /// Wall Scrape (play mode). Report: PSXRacing_wall_scrape_play.txt.
    /// </summary>
    public static class WallScrapePlayCheck
    {
        /// <summary>A strip, a drag bridge, a circuit, two mountain roads
        /// (guard walls, cut banks, tunnels) and the city's Jerseys: one of
        /// every kind of wall the game builds.</summary>
        internal const string DefaultVenues =
            "DragQuarter,LangstonBridge,HarborPoint,BeechGap,LittleSwitzerland,UptownLoop";

        internal static StringBuilder log;
        internal static int failures;
        internal static List<int> venues;
        internal static bool filterForcedOff;

        // What this run changes and has to put back.
        static bool saved;
        static bool savedFilter;
        static bool savedOptionsEnabled;
        static EnterPlayModeOptions savedOptions;
        static int savedFrameRate;

        /// <summary>Set by the runner's Start. Until it is, the edit-mode
        /// watchdog owns the run: a play mode that never starts (or a runner
        /// that is never added) would otherwise leave a batch editor with no
        /// -quit sitting there until the script's own timeout, reportless.</summary>
        internal static bool runnerStarted;
        static double playDeadline;
        const double EnterPlaySeconds = 300.0;

        [MenuItem("PSX Racing/Check Wall Scrape (play mode)")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            log = new StringBuilder();
            failures = 0;
            venues = new List<int>();
            saved = false;
            runnerStarted = false;
            // Everything from here to EnterPlaymode can throw (a scene that
            // will not open, a catalogue that will not load), and a throw in
            // an -executeMethod with no -quit is an editor that never exits
            // and a report that is never written. So a throw is a FAIL report.
            try { Begin(); }
            catch (System.Exception e)
            {
                EditorApplication.playModeStateChanged -= OnState;
                EditorApplication.update -= Watchdog;
                Check(false, "the harness started", e.GetType().Name + ": " + e.Message);
                Finish();
                Quit();
            }
        }

        static void Begin()
        {
            string want = System.Environment.GetEnvironmentVariable("PSX_SCRAPE_VENUES");
            if (string.IsNullOrEmpty(want)) want = DefaultVenues;
            filterForcedOff = System.Environment.GetEnvironmentVariable("PSX_SCRAPE_FILTER") == "0";

            Line("=== THE REAL CAR LEANING ON THE REAL WALLS: play mode, both ways ===");
            // Not GhostContactFilter.Enabled as it stands NOW: that is the
            // edit-mode value, and entering play mode resets it to on
            // (GhostContactFilter.ResetForPlay). The runner checks the value
            // the cars actually drive with.
            Line("  ghost filter: " + (filterForcedOff
                ? "FORCED OFF for this run (PSX_SCRAPE_FILTER=0)"
                : "as shipped (on)"));
            Line("  drive: full throttle from " + (WallScrapePlayCheckRunner.Speed * 3.6f).ToString("0") + " km/h, " +
                 WallScrapePlayCheckRunner.YawInDeg + " deg into the wall, steered at it and pressed at " +
                 WallScrapePlayCheckRunner.PressG + " g, ~" + WallScrapePlayCheckRunner.DriveMetres.ToString("0") +
                 " m a stretch; snag = more than " + WallScrapePlayCheckRunner.SnagStepMps + " m/s lost in one step or " +
                 WallScrapePlayCheckRunner.SnagWindowMps + " m/s within " + WallScrapePlayCheckRunner.SnagWindowSteps +
                 "; a scrape must add no hard hit");
            Line("  control: square into the wall at " + (WallScrapePlayCheckRunner.HeadOnMps * 3.6f).ToString("0") +
                 " km/h must stop the car at the face and count a hard hit");

            var scenes = EditorBuildSettings.scenes;
            foreach (var raw in want.Split(','))
            {
                string id = raw.Trim();
                if (id.Length == 0) continue;
                // By exact id: TrackCatalog.IndexOf answers 0 for a name it
                // does not know, which would quietly test the wrong venue.
                int t = -1;
                for (int i = 0; i < TrackCatalog.Count; i++)
                    if (TrackCatalog.At(i).id == id) { t = i; break; }
                int si = t >= 0 ? TrackCatalog.SceneIndex(t) : -1;
                if (t < 0 || si < 0 || si >= scenes.Length || !System.IO.File.Exists(scenes[si].path))
                {
                    Check(false, id + ": a built race scene to play", t < 0 ? "no such venue" : "scene not built");
                    continue;
                }
                if (TrackCatalog.At(t).IsRoam)
                {
                    Check(false, id + ": a RACE venue", "free roam has no route to lean along");
                    continue;
                }
                venues.Add(t);
            }
            if (venues.Count == 0) { Finish(); Quit(); return; }

            saved = true;
            savedFilter = GhostContactFilter.Enabled;
            savedOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            savedOptions = EditorSettings.enterPlayModeOptions;
            savedFrameRate = Application.targetFrameRate;
            if (filterForcedOff) GhostContactFilter.Enabled = false;

            EditorSceneManager.OpenScene(scenes[TrackCatalog.SceneIndex(venues[0])].path);
            Prime(venues[0]);

            // The handoff is a pile of statics; a domain reload on the way
            // into play mode would clear them and boot a standalone race.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            playDeadline = EditorApplication.timeSinceStartup + EnterPlaySeconds;
            EditorApplication.update += Watchdog;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Edit-mode side of the run, until the runner takes over:
        /// if play mode has not produced a runner in five minutes, write
        /// what there is as a FAIL and exit, rather than hang.</summary>
        static void Watchdog()
        {
            if (runnerStarted) { EditorApplication.update -= Watchdog; return; }
            if (EditorApplication.timeSinceStartup < playDeadline) return;
            EditorApplication.update -= Watchdog;
            EditorApplication.playModeStateChanged -= OnState;
            Check(false, "play mode started and the runner took over",
                  "nothing after " + EnterPlaySeconds.ToString("0") + " s");
            Finish();
            Quit();
        }

        /// <summary>
        /// A race entered from the LifeSim, alone. SOLO, because an AI car
        /// arriving in the lane the harness is leaning on is a crash this
        /// report would then blame on the wall. ROLLING, because a rolling
        /// start goes green on frame one instead of after a countdown (a strip
        /// ignores it and counts down, which the runner waits out).
        /// </summary>
        internal static void Prime(int t)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = t;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.Solo = true;
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.RollingStartKmh = 60f;
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("WallScrapePlayCheckRunner").AddComponent<WallScrapePlayCheckRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "WALLS SMOOTH." : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                                       "PSXRacing_wall_scrape_play.txt"), log.ToString());
            Debug.Log(log.ToString());
            Restore();
        }

        /// <summary>Everything this run changed, put back: the filter
        /// switch, the play-mode options, the clock, and the handoff — a
        /// primed Solo rolling race left in the statics is what the next
        /// press of Play in this editor would get.</summary>
        static void Restore()
        {
            Time.captureDeltaTime = 0f;
            RaceHandoff.ClearAll();
            if (!saved) return;
            saved = false;
            GhostContactFilter.Enabled = savedFilter;
            Application.targetFrameRate = savedFrameRate;
            EditorSettings.enterPlayModeOptionsEnabled = savedOptionsEnabled;
            EditorSettings.enterPlayModeOptions = savedOptions;
        }

        /// <summary>Batch: exit with the verdict. From the menu: leave play
        /// mode and keep the editor — the other play checks close it, which
        /// is a rude way to hand back a report.</summary>
        internal static void Quit()
        {
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }

    /// <summary>Drives the venue list from inside play mode; see WallScrapePlayCheck.</summary>
    public class WallScrapePlayCheckRunner : MonoBehaviour
    {
        // ---- the drive (the owner's report, as numbers) ----
        internal const float Speed = 38f;              // 137 km/h
        internal const float YawInDeg = 3f;
        /// <summary>A steady shove toward the wall on top of the steering.
        /// The stabilizer damps lateral velocity at up to 0.7 g, so this
        /// alone settles at a metre a second into the wall — enough to
        /// guarantee contact, nowhere near a crash.</summary>
        internal const float PressG = 0.5f;
        /// <summary>The steering aims this far INSIDE the wall face, so the
        /// car keeps leaning rather than settling a hair off it.</summary>
        const float LeanInsideM = 0.3f;
        const float SteerGain = 2.5f;
        internal const float DriveMetres = 120f;
        const float DriveMaxSeconds = 8f;
        const float MinCountedMetres = 40f;
        internal const float SnagStepMps = 4f;          // 24 g
        internal const float SnagWindowMps = 8f;
        internal const int SnagWindowSteps = 6;
        /// <summary>Steps after a placement before speed is judged: the
        /// suspension finds the road in the first couple.</summary>
        const int SettleSteps = 2;
        const int MaxSnagsPerDrive = 4;
        /// <summary>The body box this far past the wall's face is through
        /// it, whatever the speed trace says.</summary>
        const float ThroughM = 0.5f;
        const float LeaningShare = 0.5f;

        // ---- the head-on control ----
        internal const float HeadOnMps = 20f;          // 72 km/h
        const int HeadOnSteps = 90;
        /// <summary>One step of travel at 72 km/h is 0.33 m, under PhysX's
        /// sweep threshold for a car-sized box: the discrete pass meets the
        /// wall wherever the step lands, so up to that much in and pushed out
        /// is a wall stopping a car (WallScrapeAudit measured 0.32 m on old
        /// boxes and new solids alike). A car the wall does not stop goes
        /// metres through.</summary>
        const float ControlSinkM = 0.4f;
        const float ControlStopMps = 3f;
        const float ControlReachM = 0.3f;

        // ---- finding the walls ----
        /// <summary>A city route's line is a lane, not a road centre, and its
        /// barriers sit anywhere from the next lane to the far shoulder.</summary>
        const float CityReachM = 12f;
        const float CityNearestM = 1.5f;
        const int RunEndMargin = 3;
        const int MinRunStations = 8;
        const int WindowStations = 32;                 // 128 m
        const int MinWindowStations = 16;              // 64 m
        const float BendDeg = 15f;
        const int PerSide = 8;

        const int SolidLayer = 9;
        const int SolidMask = 1 << SolidLayer;
        /// <summary>What a car stands on: not itself (Ignore Raycast), not a
        /// wall.</summary>
        const int GroundMask = ~((1 << 2) | (1 << SolidLayer));
        /// <summary>Origin over the tarmac for a placement: ResetTo's lift
        /// measured from the surface rather than the datum.</summary>
        const float SeatLift = 0.3f;
        /// <summary>Inside wall-scrape-play.ps1's 60 minutes with room for
        /// the editor to open the project and enter play mode first: the
        /// harness must give up and WRITE its report before the script's
        /// timeout finds nothing to read.</summary>
        const float BudgetMinutes = 50f;
        /// <summary>How far outside the body box, in plan, a barrier may be
        /// and still count as the car leaning on it: the solver's contact
        /// offset and a couple of centimetres of daylight.</summary>
        const float ContactSkinM = 0.05f;

        class Ctx
        {
            public TrackCatalog.TrackDef def;
            public TrackPath path;
            public CarController car;
            public Rigidbody body;
            public CollisionResponder responder;
            public BoxCollider box;
            public CityWorld world;
            public float[][] dist;
            public Vector3 half, centre;
        }

        class Stretch
        {
            public int s, a, b, runLen;
            public float turnDeg;
            public bool Bend => turnDeg >= BendDeg;
        }

        class Drive
        {
            public bool placed, spun;
            public float metres, entry = -1f, exit, worstStep, worstWindow, maxPen = float.MinValue, damage;
            public int steps, contactSteps, hardHits, ghosts, snags;
            public readonly List<string> snagLines = new List<string>();
            public float Share => steps > 0 ? (float)contactSteps / steps : 0f;
        }

        class HeadOnResult
        {
            public bool placed;
            public float gap, minGap = float.MaxValue, arrive, intoEnd;
            public int hardHits, ghosts;
        }

        RaceManager lastRm;
        float deadline;
        bool done;
        string abortWhy;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        /// <summary>An exception in a nested coroutine ends it and leaves the
        /// one waiting on it suspended for ever: the job would sit there
        /// until the script's timeout killed it, with no report. So a throw
        /// from THIS harness ends the run with what it has.</summary>
        void OnLog(string msg, string stack, LogType type)
        {
            if (done || type != LogType.Exception || abortWhy != null) return;
            if (string.IsNullOrEmpty(stack) || !stack.Contains("WallScrapePlayCheck")) return;
            abortWhy = msg;
        }

        void Update()
        {
            if (done) return;
            if (abortWhy == null && Time.realtimeSinceStartup < deadline) return;
            StopAllCoroutines();
            WallScrapePlayCheck.Check(false,
                abortWhy != null ? "the harness ran without throwing" : "the run finished inside " + BudgetMinutes + " minutes",
                abortWhy ?? "gave up");
            End();
        }

        void End()
        {
            if (done) return;
            done = true;
            SceneManager.sceneLoaded -= OnLoaded;
            WallScrapePlayCheck.Finish();
            WallScrapePlayCheck.Quit();
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnLoaded;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            WallScrapePlayCheck.runnerStarted = true;
            deadline = Time.realtimeSinceStartup + BudgetMinutes * 60f;
            SceneManager.sceneLoaded += OnLoaded;
            Pace();
            Quiet();
            // The switch as the cars will actually drive with it — play mode
            // resets it on the way in, and an A/B whose B silently ran as A
            // would report the filter's absence as its presence.
            bool want = !WallScrapePlayCheck.filterForcedOff;
            WallScrapePlayCheck.Check(GhostContactFilter.Enabled == want,
                "the ghost filter is " + (want ? "ON, as shipped" : "OFF, as forced"),
                "GhostContactFilter.Enabled = " + GhostContactFilter.Enabled);

            var list = WallScrapePlayCheck.venues;
            for (int i = 0; i < list.Count; i++)
            {
                int t = list[i];
                if (i > 0)
                {
                    WallScrapePlayCheck.Prime(t);
                    SceneManager.LoadScene(TrackCatalog.SceneIndex(t));
                    yield return null;
                }
                yield return null;
                yield return new WaitForFixedUpdate();
                yield return null;
                yield return StartCoroutine(Venue(t));
            }
            End();
        }

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            Pace();
            Quiet();
        }

        /// <summary>
        /// One physics step per frame, frames as fast as they come. The
        /// bootstrap caps every scene at 60 fps, which in a headless editor
        /// makes game time real time: forty minutes of driving would take
        /// forty minutes. A captured delta of exactly one fixed step changes
        /// nothing the physics sees — the step is 1/60 s either way — and
        /// only how long the wall clock waits for it.
        /// </summary>
        static void Pace()
        {
            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 1f / 60f;
            Time.captureDeltaTime = dt;
            Application.targetFrameRate = -1;
            // Again after every load, in case anything re-arms the filter on
            // a scene's way in.
            if (WallScrapePlayCheck.filterForcedOff) GhostContactFilter.Enabled = false;
        }

        /// <summary>Every camera off: under -nographics URP logs an error per
        /// camera per frame, and nothing here needs a picture.</summary>
        static void Quiet()
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) cam.enabled = false;
        }

        RaceManager Live()
        {
            var rm = RaceManager.Instance;
            // == null is also true of the LAST scene's manager, destroyed
            // but still in the static until the new one's Awake runs.
            if (rm == null || ReferenceEquals(rm, lastRm)) return null;
            return rm.State == RaceManager.RaceState.Racing ? rm : null;
        }

        IEnumerator Venue(int t)
        {
            var def = TrackCatalog.At(t);
            WallScrapePlayCheck.Line("");
            WallScrapePlayCheck.Line(def.id + " (" + def.name + ")" + (def.IsCityRace ? ", a city route" : "") + ":");

            float until = Time.realtimeSinceStartup + 120f;
            RaceManager rm;
            while ((rm = Live()) == null && Time.realtimeSinceStartup < until) yield return null;
            bool live = rm != null && rm.playerCar != null && rm.path != null && rm.path.Count > 8;
            WallScrapePlayCheck.Check(live, "the race is live with a player car",
                                      live ? rm.path.Count + " stations" : "never went green");
            if (!live) yield break;
            lastRm = rm;

            // Nothing may fight the harness for the car: not the keyboard,
            // not the stuck watchdog (a car leaning on a wall is exactly what
            // it recovers), not the replay recorder.
            var car = rm.playerCar;
            foreach (var mb in car.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            foreach (var rp in FindObjectsByType<RaceReplay>(FindObjectsSortMode.None)) rp.enabled = false;
            Quiet();

            var cx = new Ctx
            {
                def = def,
                path = rm.path,
                car = car,
                body = car.Body,
                responder = car.GetComponent<CollisionResponder>(),
                box = car.GetComponent<BoxCollider>(),
                world = def.IsCityRace ? FindFirstObjectByType<CityWorld>() : null,
            };
            bool kit = cx.body != null && cx.responder != null && cx.box != null;
            WallScrapePlayCheck.Check(kit, "the player car has its rigidbody, its body box and its CollisionResponder");
            if (!kit) yield break;
            if (def.IsCityRace)
                WallScrapePlayCheck.Check(cx.world != null, "the city route streams a CityWorld");
            Vector3 scale = car.transform.lossyScale;
            cx.half = Vector3.Scale(cx.box.size, scale) * 0.5f;
            cx.centre = Vector3.Scale(cx.box.center, scale);
            GhostContactFilter.ResetStats();

            string census = Scan(cx, out int walled);
            WallScrapePlayCheck.Line("  " + walled + " station-sides have a barrier facing the road" +
                                     (walled > 0 ? ": " + census : ""));
            var runs = Runs(cx);
            var picked = Pick(cx, runs);
            int bends = 0;
            foreach (var st in picked) if (st.Bend) bends++;
            WallScrapePlayCheck.Check(picked.Count > 0, "wall-lined stretches found to lean on",
                                      runs.Count + " runs; driving " + picked.Count + " stretches (" + bends +
                                      " bends, " + (picked.Count - bends) + " straights), both ways");
            if (picked.Count == 0) yield break;

            // ---- THE CONTROL FIRST: the wall must stop a car driven at it.
            // Longest runs first, until one has room in front of it.
            var byLength = new List<(int s, int a, int b)>(runs);
            byLength.Sort((x, y) => (y.b - y.a).CompareTo(x.b - x.a));
            var ho = new HeadOnResult();
            int hoS = 0, hoK = 0;
            for (int i = 0; i < byLength.Count && i < 6 && !ho.placed; i++)
            {
                hoS = byLength[i].s;
                hoK = (byLength[i].a + byLength[i].b) / 2;
                ho = new HeadOnResult();
                yield return StartCoroutine(HeadOn(cx, hoS, hoK, ho));
            }
            string hoName = "square into the " + (hoS == 0 ? "L" : "R") + " wall at wp " + hoK + " at " +
                            (HeadOnMps * 3.6f).ToString("0") + " km/h";
            if (!ho.placed)
                WallScrapePlayCheck.Check(false, "head-on control: a clear spot in front of a wall to start from",
                                          "none in the six longest runs");
            else
            {
                string why = ho.minGap < -ControlSinkM ? "DROVE THROUGH THE WALL"
                           : ho.intoEnd >= ControlStopMps ? "was still moving into the wall at the end"
                           : ho.minGap >= ControlReachM ? "never reached the wall (harness)"
                           : ho.hardHits < 1 ? "stopped, but no hard hit was counted"
                           : "";
                WallScrapePlayCheck.Check(why.Length == 0,
                    hoName + (why.Length == 0 ? ": stopped at the face, hard hit counted" : ": " + why),
                    "started " + ho.gap.ToString("0.0") + " m off, arrived at " + Kmh(ho.arrive) + " km/h, deepest " +
                    (ho.minGap == float.MaxValue ? "-" : (-ho.minGap).ToString("+0.00;-0.00") + " m into the face") +
                    ", " + ho.intoEnd.ToString("0.0") + " m/s into it at the end, hard hits +" + ho.hardHits +
                    ", ghosts dropped +" + ho.ghosts);
            }

            // ---- THE SCRAPES ----
            int drives = 0, leaned = 0, snags = 0, hits = 0;
            foreach (var st in picked)
            {
                foreach (int dirSign in new[] { 1, -1 })
                {
                    var d = new Drive();
                    yield return StartCoroutine(DriveAlong(cx, st, dirSign, d));
                    string name = (st.s == 0 ? "L" : "R") + " wp " + st.a + "-" + st.b + " (" +
                                  (st.Bend ? "bend " + st.turnDeg.ToString("0") + " deg" : "straight") + "), " +
                                  (dirSign > 0 ? "forward" : "backward");
                    if (!d.placed)
                    {
                        WallScrapePlayCheck.Line("  --   " + name + ": no clear spot beside the wall to put the car down");
                        continue;
                    }
                    string got = Kmh(d.entry) + " -> " + Kmh(d.exit) + " km/h over " + d.metres.ToString("0") +
                                 " m, worst step -" + d.worstStep.ToString("0.0") + " m/s, worst " + SnagWindowSteps +
                                 " steps -" + d.worstWindow.ToString("0.0") + " m/s, on the wall " +
                                 (d.Share * 100f).ToString("0") + "%, hard hits +" + d.hardHits + ", damage +" +
                                 d.damage.ToString("0.0") + ", deepest " +
                                 (d.maxPen == float.MinValue ? "-" : d.maxPen.ToString("+0.00;-0.00") + " m") +
                                 ", ghosts dropped +" + d.ghosts + (d.spun ? ", SPUN" : "");
                    bool clean = d.snags == 0 && d.hardHits == 0 && d.maxPen < ThroughM;
                    snags += d.snags;
                    hits += d.hardHits;
                    drives++;
                    // Only a drive that PROVES something counts toward the
                    // lean control. A drive that spun after twenty steps on
                    // the wall, or stopped short, has a contact share of 100%
                    // and is printed below as proving nothing — counting it
                    // as "leaning" let a venue where every drive spun pass
                    // the control with not one stretch actually driven.
                    bool proves = !d.spun && d.metres >= MinCountedMetres && d.Share >= LeaningShare;
                    if (proves) leaned++;
                    if (clean && !proves)
                    {
                        // A pass that proves nothing: the car was mostly off
                        // the wall, or lost itself, or never got going.
                        WallScrapePlayCheck.Line("  --   " + name + ": " +
                            (d.spun ? "the car spun - the harness lost it" :
                             d.metres < MinCountedMetres ? "too short to count" :
                             "off the wall for most of it - proves nothing") + "  [" + got + "]");
                        continue;
                    }
                    var faults = new List<string>();
                    if (d.snags > 0) faults.Add(d.snags + " SNAG(S)");
                    if (d.hardHits > 0) faults.Add("a scrape counted " + d.hardHits + " hard hit(s)");
                    if (d.maxPen >= ThroughM) faults.Add("went " + d.maxPen.ToString("0.00") + " m through the wall");
                    WallScrapePlayCheck.Check(clean, name + ": " + (clean ? "slid the whole way" : string.Join(", ", faults)), got);
                    foreach (var sl in d.snagLines) WallScrapePlayCheck.Line("         " + sl);
                }
            }

            WallScrapePlayCheck.Line("  " + drives + " drives, " + leaned + " leaning on the wall, " + snags +
                                     " snag(s), hard hits +" + hits + " along the walls, ghost contacts dropped by the filter: " +
                                     GhostContactFilter.IgnoredCount);
            // THE LEAN CONTROL. A car that bounced off the first wall and
            // drove down the middle of the road would pass every stretch.
            WallScrapePlayCheck.Check(drives > 0 && leaned * 2 >= drives,
                "the car was actually leaning on the wall for most of the drive in at least half the drives (the lean control; " +
                "a spun or short drive counts against it)",
                leaned + " of " + drives);
        }

        // ------------------------------------------------------------------
        // Finding the walls
        // ------------------------------------------------------------------

        /// <summary>
        /// Every station, both sides: a horizontal ray from the centreline at
        /// 0.6 m, and the first thing it meets on the Solid layer — if that
        /// is a BARRIER facing the road. First thing, not first barrier: a
        /// tree or a pier standing in front of the wall is what the car
        /// would meet, and that station is not a wall to lean on.
        ///
        /// A city route's world does not exist until it is streamed, so the
        /// tiles along the whole route are built as the scan walks it. The
        /// world drops the far ones again on its next Update.
        /// </summary>
        string Scan(Ctx cx, out int walled)
        {
            var path = cx.path;
            int n = path.Count;
            bool city = cx.def.IsCityRace;
            float reach = city ? CityReachM : PSXRacingBuilder.WallOffsetFor(cx.def) + 4f;
            float nearest = city ? CityNearestM : path.roadWidth * 0.5f - 0.5f;
            cx.dist = new[] { new float[n], new float[n] };
            var kinds = new SortedDictionary<string, int>();
            long lastTile = long.MinValue;
            walled = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = path.GetPoint(i);
                if (cx.world != null)
                {
                    long tile = TileKey(c);
                    if (tile != lastTile)
                    {
                        cx.world.EnsureRing(c, 1);
                        Physics.SyncTransforms();
                        lastTile = tile;
                    }
                }
                for (int s = 0; s < 2; s++)
                {
                    cx.dist[s][i] = -1f;
                    Vector3 r = Right(path, i, s);
                    if (!Physics.Raycast(c + Vector3.up * 0.6f, r, out var hit, reach, SolidMask,
                                         QueryTriggerInteraction.Ignore)) continue;
                    if (hit.distance < nearest) continue;                    // something ON the road
                    if (Vector3.Dot(hit.normal, -r) < 0.7f) continue;          // not facing the road
                    if (!IsBarrier(hit.collider, city)) continue;
                    cx.dist[s][i] = hit.distance;
                    walled++;
                    string k = Kind(hit.collider);
                    kinds.TryGetValue(k, out int m);
                    kinds[k] = m + 1;
                }
            }
            var sb = new StringBuilder();
            foreach (var kv in kinds)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key).Append(" x").Append(kv.Value);
            }
            return sb.ToString();
        }

        /// <summary>The barriers by the names the builder and the city give
        /// them. Box chains (a scene built before the wall solids) and
        /// concave solids both count: the report says which it met.</summary>
        static bool IsBarrier(Collider c, bool city)
        {
            if (c == null || c.gameObject.layer != SolidLayer) return false;
            if (city) return c.name == "Barriers";
            switch (c.name)
            {
                case "Wall":
                case "WallColl":
                case "BankColl":
                case "WallTunnel":
                    return true;
                default:
                    return false;
            }
        }

        static string Kind(Collider c)
        {
            string type = c is MeshCollider mc ? (mc.convex ? "convex mesh" : "concave mesh")
                        : c is BoxCollider ? "box" : c.GetType().Name;
            return c.name + " (" + type + ")";
        }

        /// <summary>Consecutive stations with a wall at a near-constant
        /// distance, as the edit-mode audit cuts them: a jump of more than
        /// half a metre is a different surface and starts a new run.</summary>
        static List<(int s, int a, int b)> Runs(Ctx cx)
        {
            int n = cx.path.Count;
            var runs = new List<(int s, int a, int b)>();
            for (int s = 0; s < 2; s++)
            {
                var dist = cx.dist[s];
                int start = -1;
                for (int i = 0; i <= n; i++)
                {
                    bool on = i < n && dist[i] > 0f &&
                              (i == 0 || start < 0 || Mathf.Abs(dist[i] - dist[i - 1]) < 0.5f);
                    if (on && start < 0) start = i;
                    else if (!on && start >= 0)
                    {
                        if (i - start >= MinRunStations) runs.Add((s, start, i - 1));
                        start = i < n && dist[i] > 0f ? i : -1;
                    }
                }
            }
            return runs;
        }

        /// <summary>
        /// Up to <see cref="PerSide"/> stretches a side: 128 m windows cut
        /// from the runs (clear of each run's ends, where the stone flares),
        /// longest runs first, alternating bends and straights so a venue
        /// with one long straight still gets its corners driven — leaning
        /// through a corner is where the report comes from.
        /// </summary>
        static List<Stretch> Pick(Ctx cx, List<(int s, int a, int b)> runs)
        {
            var picked = new List<Stretch>();
            for (int s = 0; s < 2; s++)
            {
                var cands = new List<Stretch>();
                foreach (var run in runs)
                {
                    if (run.s != s) continue;
                    int a2 = run.a + RunEndMargin, b2 = run.b - RunEndMargin;
                    for (int wa = a2; wa + MinWindowStations - 1 <= b2; wa += WindowStations)
                    {
                        int wb = Mathf.Min(wa + WindowStations - 1, b2);
                        cands.Add(new Stretch
                        {
                            s = s, a = wa, b = wb, runLen = run.b - run.a + 1,
                            turnDeg = Turn(cx.path, wa, wb),
                        });
                    }
                }
                cands.Sort((x, y) => x.runLen != y.runLen ? y.runLen.CompareTo(x.runLen) : x.a.CompareTo(y.a));
                var bends = cands.FindAll(c => c.Bend);
                var straights = cands.FindAll(c => !c.Bend);
                int bi = 0, si = 0, got = 0;
                bool bendNext = bends.Count > 0;
                while (got < PerSide && (bi < bends.Count || si < straights.Count))
                {
                    if (bendNext && bi < bends.Count) picked.Add(bends[bi++]);
                    else if (si < straights.Count) picked.Add(straights[si++]);
                    else picked.Add(bends[bi++]);
                    got++;
                    bendNext = !bendNext;
                }
            }
            return picked;
        }

        static float Turn(TrackPath path, int a, int b)
        {
            float sum = 0f;
            for (int i = a; i < b; i++)
                sum += Mathf.Abs(Vector3.SignedAngle(path.GetTangent(i), path.GetTangent(i + 1), Vector3.up));
            return sum;
        }

        // ------------------------------------------------------------------
        // Driving
        // ------------------------------------------------------------------

        /// <summary>
        /// One stretch, one direction. Placed beside the wall, clear of it by
        /// the yawed body's corner, three degrees in, already at speed; then
        /// driven until ~120 m have gone by or the run ends. After a snag the
        /// car is put back three stations on at speed and carries on, so one
        /// drive reports every seam rather than the first.
        /// </summary>
        IEnumerator DriveAlong(Ctx cx, Stretch st, int dirSign, Drive d)
        {
            var path = cx.path;
            var car = cx.car;
            var body = cx.body;
            int s = st.s;
            int from = dirSign > 0 ? st.a : st.b;
            int to = dirSign > 0 ? st.b : st.a;
            Stream(cx, from, to);

            int k = from;
            for (int tries = 0; tries < 3; tries++)
            {
                if (Place(cx, s, k, dirSign, Speed)) { d.placed = true; break; }
                k += dirSign;
                if ((to - k) * dirSign <= 0) break;
            }
            if (!d.placed) yield break;

            int hits0 = cx.responder.HardHits;
            float damage0 = cx.responder.DamageScore;
            int ghosts0 = GhostContactFilter.IgnoredCount;
            var hist = new float[SnagWindowSteps];
            int histCount = 0, histAt = 0, sincePlaced = 0;
            float prev = 0f;
            bool hasPrev = false;
            Vector3 last = body.position;
            int maxSteps = Mathf.RoundToInt(DriveMaxSeconds / Time.fixedDeltaTime);
            for (int step = 0; step < maxSteps; step++)
            {
                Steer(cx, s, k, dirSign, to);
                yield return new WaitForFixedUpdate();
                if (car == null || body == null) yield break;

                k = path.NearestIndex(body.position, k, 6);
                Vector3 moved = body.position - last; moved.y = 0f;
                d.metres += moved.magnitude;
                last = body.position;
                Vector3 t = path.GetTangent(k) * dirSign;
                float along = Vector3.Dot(body.linearVelocity, t);
                d.steps++;
                // Measured, this step, against a BARRIER — not
                // CollisionResponder.InWallContact, which stays true for a
                // quarter of a second after any non-ground contact at all: a
                // car that brushed the wall once every fifteen steps and
                // spent the rest in the lane read as 100% on the wall.
                if (OnBarrier(cx)) d.contactSteps++;

                // Through the wall, whatever the speed trace says.
                if (cx.dist[s][k] > 0f)
                {
                    Vector3 r = Right(path, k, s);
                    Vector3 centre = body.position + body.rotation * cx.centre;
                    float pen = Vector3.Dot(centre - path.GetPoint(k), r) + Support(body.rotation, cx.half, r) - cx.dist[s][k];
                    if (pen > d.maxPen) d.maxPen = pen;
                }

                Vector3 fwd = car.transform.forward; fwd.y = 0f;
                if (Vector3.Angle(fwd, t) > 60f) { d.spun = true; break; }

                sincePlaced++;
                if (sincePlaced > SettleSteps)
                {
                    if (d.entry < 0f) d.entry = along;
                    float peak = along;
                    for (int h = 0; h < histCount; h++) peak = Mathf.Max(peak, hist[h]);
                    float dropStep = hasPrev ? prev - along : 0f;
                    float dropWindow = peak - along;
                    d.worstStep = Mathf.Max(d.worstStep, dropStep);
                    d.worstWindow = Mathf.Max(d.worstWindow, dropWindow);
                    if (dropStep > SnagStepMps || dropWindow > SnagWindowMps)
                    {
                        d.snags++;
                        bool oneStep = dropStep > SnagStepMps;
                        d.snagLines.Add("SNAG wp " + k + ": " + Kmh(oneStep ? prev : peak) + " -> " + Kmh(along) +
                                        " km/h " + (oneStep ? "in one step" : "within " + SnagWindowSteps + " steps") +
                                        "  [touching " + Touching(cx) + "]");
                        if (d.snags >= MaxSnagsPerDrive) break;
                        int k2 = k + 3 * dirSign;
                        if ((to - k2) * dirSign <= 0 || cx.dist[s][k2] <= 0f || !Place(cx, s, k2, dirSign, Speed)) break;
                        k = k2;
                        last = body.position;
                        hasPrev = false;
                        histCount = histAt = sincePlaced = 0;
                        continue;
                    }
                }
                hist[histAt] = along;
                histAt = (histAt + 1) % SnagWindowSteps;
                histCount = Mathf.Min(histCount + 1, SnagWindowSteps);
                prev = along;
                hasPrev = true;
                d.exit = along;

                if (d.metres >= DriveMetres || (to - k) * dirSign <= 0 || cx.dist[s][k] <= 0f) break;
            }
            d.hardHits = cx.responder.HardHits - hits0;
            d.damage = cx.responder.DamageScore - damage0;
            d.ghosts = GhostContactFilter.IgnoredCount - ghosts0;
            if (d.entry < 0f) d.entry = Speed;
            car.throttleInput = 0f;
            car.steerInput = 0f;
            car.brakeInput = 1f;
        }

        /// <summary>
        /// The driver: pure pursuit on a point just INSIDE the wall face a
        /// speed-scaled distance up the road (the AI's own look-ahead), full
        /// throttle, and half a g of shove toward the wall. Pursuit rather
        /// than a lateral PD because it follows a bend the way a driver does,
        /// by looking where the wall goes.
        /// </summary>
        static void Steer(Ctx cx, int s, int k, int dirSign, int to)
        {
            var path = cx.path;
            var car = cx.car;
            float speed = cx.body.linearVelocity.magnitude;
            int look = Mathf.Max(2, Mathf.RoundToInt((7f + speed * 0.45f) / path.spacing));
            int kl = dirSign > 0 ? Mathf.Min(k + look, to) : Mathf.Max(k - look, to);
            float wall = cx.dist[s][kl] > 0f ? cx.dist[s][kl] : cx.dist[s][k];
            Vector3 aim = path.GetPoint(kl) + Right(path, kl, s) * (wall - cx.half.x + LeanInsideM);
            Vector3 local = car.transform.InverseTransformPoint(aim);
            car.steerInput = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f)) * SteerGain, -1f, 1f);
            // A DRIVER LIFTS FOR A BEND. Flat out at 137 km/h into a stage
            // hairpin the car cannot follow an inside wall at all; it spins
            // or crosses the road, and the drive proves nothing about the
            // wall. Held to what the bend ahead allows on about a g.
            float target = BendSpeed(path, k, dirSign);
            car.throttleInput = speed < target ? 1f : 0f;
            car.brakeInput = speed > target + 3f ? 0.6f : 0f;
            car.handbrakeInput = false;
            cx.body.AddForce(Right(path, k, s) * (PressG * 9.81f), ForceMode.Acceleration);
        }

        /// <summary>
        /// Beside the wall at station <paramref name="k"/>, nose in by
        /// <see cref="YawInDeg"/>, the body's nearest corner a few
        /// centimetres off the face, seated on whatever road is under it,
        /// already doing <paramref name="speed"/>. Through TeleportTo, never
        /// the transform: the player's body is interpolated.
        /// </summary>
        /// <summary>Speed a car holds through the bend at station k on
        /// <see cref="BendGripMps2"/> of grip: sqrt(grip * R), capped at
        /// <see cref="Speed"/>.</summary>
        static float BendSpeed(TrackPath path, int k, int dirSign)
        {
            // Every bend in the next 100 m, each allowed sqrt(grip * R) plus
            // what 7 m/s^2 of braking takes off on the way to it: a bend read
            // only where the car already is arrives too late to brake for.
            float best = Speed;
            if (path.curvatures == null) return best;
            for (int o = 0; o <= 25; o++)
            {
                int j = path.Wrap(k + o * dirSign);
                if (j < 0 || j >= path.curvatures.Length) break;
                float kappa = path.curvatures[j];
                if (kappa <= 1e-4f) continue;
                float v = Mathf.Sqrt(BendGripMps2 / kappa + 2f * 7f * o * path.spacing);
                if (v < best) best = v;
            }
            return best;
        }
        const float BendGripMps2 = 10f;

        static bool Place(Ctx cx, int s, int k, int dirSign, float speed)
        {
            speed = Mathf.Min(speed, BendSpeed(cx.path, k, dirSign));
            var path = cx.path;
            if (cx.dist[s][k] <= 0f) return false;
            float side = s == 0 ? -1f : 1f;
            Vector3 c = path.GetPoint(k);
            Vector3 tan = path.GetTangent(k);
            Vector3 r = Right(path, k, s);
            Quaternion rot = Quaternion.LookRotation(
                Quaternion.AngleAxis(YawInDeg * side * dirSign, Vector3.up) * (tan * dirSign), Vector3.up);
            Vector3 off = rot * cx.centre;
            foreach (float clearance in new[] { 0.05f, 0.2f })
            {
                float lat = cx.dist[s][k] - Support(rot, cx.half, r) - clearance;
                Vector3 origin = c + r * lat - new Vector3(off.x, 0f, off.z);
                origin.y = Seat(origin.x, origin.z, c.y, out float y) ? y : c.y + CarController.ResetLift;
                if (Physics.CheckBox(origin + off, cx.half, rot, SolidMask, QueryTriggerInteraction.Ignore)) continue;
                cx.car.TeleportTo(origin, rot);
                cx.car.SetRolling(speed);
                return true;
            }
            return false;
        }

        /// <summary>
        /// THE CONTROL: the car square at the wall face at 72 km/h, coasting,
        /// from as far out as the road allows up to 6 m. The face is taken
        /// from its own ray, not from the road's right vector, so "square"
        /// means square to the wall the car actually meets.
        /// </summary>
        IEnumerator HeadOn(Ctx cx, int s, int k, HeadOnResult h)
        {
            var path = cx.path;
            var car = cx.car;
            var body = cx.body;
            Stream(cx, k, k);
            Vector3 c = path.GetPoint(k);
            Vector3 r = Right(path, k, s);
            if (!Physics.Raycast(c + Vector3.up * 0.6f, r, out var hit, cx.dist[s][k] + 1f, SolidMask,
                                 QueryTriggerInteraction.Ignore)) yield break;
            Vector3 n = hit.normal; n.y = 0f;
            n = n.sqrMagnitude > 1e-4f ? n.normalized : -r;
            Vector3 face = hit.point;
            Quaternion rot = Quaternion.LookRotation(-n, Vector3.up);
            Vector3 off = rot * cx.centre;
            foreach (float g in new[] { 6f, 4.5f, 3f, 2f })
            {
                Vector3 origin = face + n * (g + Support(rot, cx.half, n)) - new Vector3(off.x, 0f, off.z);
                if (!Seat(origin.x, origin.z, c.y, out float y)) continue;
                if (Mathf.Abs(y - SeatLift - c.y) > 1.5f) continue;     // another road, not this one
                origin.y = y;
                if (Physics.CheckBox(origin + off, cx.half, rot, SolidMask, QueryTriggerInteraction.Ignore)) continue;
                car.TeleportTo(origin, rot);
                car.SetRolling(HeadOnMps);
                h.placed = true;
                h.gap = g;
                break;
            }
            if (!h.placed) yield break;

            int hits0 = cx.responder.HardHits;
            int ghosts0 = GhostContactFilter.IgnoredCount;
            for (int i = 0; i < HeadOnSteps; i++)
            {
                car.throttleInput = 0f;
                car.brakeInput = 0f;
                car.steerInput = 0f;
                car.handbrakeInput = false;
                yield return new WaitForFixedUpdate();
                if (car == null || body == null) yield break;
                Vector3 centre = body.position + body.rotation * cx.centre;
                float gap = Vector3.Dot(centre - face, n) - Support(body.rotation, cx.half, n);
                if (gap < h.minGap) h.minGap = gap;
                h.arrive = Mathf.Max(h.arrive, Vector3.Dot(body.linearVelocity, -n));
            }
            h.intoEnd = Vector3.Dot(body.linearVelocity, -n);
            h.hardHits = cx.responder.HardHits - hits0;
            h.ghosts = GhostContactFilter.IgnoredCount - ghosts0;
            car.brakeInput = 1f;
        }

        // ------------------------------------------------------------------
        // Small things
        // ------------------------------------------------------------------

        /// <summary>A city route's colliders exist only near a car. Both ends
        /// of what is about to be driven, and everything between them, are
        /// within one tile of one end or the other on a 128 m stretch.</summary>
        static void Stream(Ctx cx, int from, int to)
        {
            if (cx.world == null) return;
            cx.world.EnsureRing(cx.path.GetPoint(from), 1);
            cx.world.EnsureRing(cx.path.GetPoint(to), 1);
            Physics.SyncTransforms();
        }

        static long TileKey(Vector3 p)
        {
            long tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize);
            long tz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);
            return (tx << 32) ^ (tz & 0xFFFFFFFFL);
        }

        /// <summary>Unit vector from the centreline toward side
        /// <paramref name="s"/> (0 = left, 1 = right).</summary>
        static Vector3 Right(TrackPath path, int k, int s) =>
            Vector3.Cross(Vector3.up, path.GetTangent(k)).normalized * (s == 0 ? -1f : 1f);

        /// <summary>The surface under a point, from 2.5 m over the road's own
        /// height — never from the sky, which finds the deck of whatever
        /// crosses overhead, or the hillside over a tunnel.</summary>
        static bool Seat(float x, float z, float roadY, out float y)
        {
            y = 0f;
            if (!Physics.Raycast(new Vector3(x, roadY + 2.5f, z), Vector3.down, out var g, 5f, GroundMask,
                                 QueryTriggerInteraction.Ignore)) return false;
            y = g.point.y + SeatLift;
            return true;
        }

        /// <summary>How far a box of half-size <paramref name="half"/> in pose
        /// <paramref name="rot"/> reaches along <paramref name="dir"/>.</summary>
        static float Support(Quaternion rot, Vector3 half, Vector3 dir) =>
            half.x * Mathf.Abs(Vector3.Dot(rot * Vector3.right, dir)) +
            half.y * Mathf.Abs(Vector3.Dot(rot * Vector3.up, dir)) +
            half.z * Mathf.Abs(Vector3.Dot(rot * Vector3.forward, dir));

        /// <summary>Is the body box, grown by <see cref="ContactSkinM"/> in
        /// plan, touching a barrier right now? The geometric question, asked
        /// of the barriers alone: a tree, a pier or a building on the Solid
        /// layer is not the wall this drive is leaning on.</summary>
        static bool OnBarrier(Ctx cx)
        {
            var body = cx.body;
            var hits = Physics.OverlapBox(body.position + body.rotation * cx.centre,
                                          cx.half + new Vector3(ContactSkinM, 0f, ContactSkinM),
                                          body.rotation, SolidMask, QueryTriggerInteraction.Ignore);
            bool city = cx.def.IsCityRace;
            foreach (var h in hits)
                if (IsBarrier(h, city)) return true;
            return false;
        }

        /// <summary>What the body is up against right now, by name and
        /// collider type — a box chain or one surface is the question.</summary>
        static string Touching(Ctx cx)
        {
            var body = cx.body;
            var hits = Physics.OverlapBox(body.position + body.rotation * cx.centre, cx.half + new Vector3(0.04f, 0f, 0.04f),
                                          body.rotation, SolidMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return "nothing (the step already resolved it)";
            var names = new SortedSet<string>();
            foreach (var hc in hits)
                names.Add((hc.transform.parent != null ? hc.transform.parent.name + "/" : "") + Kind(hc));
            return string.Join(", ", names);
        }

        static string Kmh(float mps) => (mps * 3.6f).ToString("0");
    }
}
