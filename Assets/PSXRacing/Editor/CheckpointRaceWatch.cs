using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// RacePlayCheck's CHECKPOINT MODE (DESIGN_v2 9.3, stage 1). Started by the play check when the race is a
    /// checkpoint race: the player's autopilot gets the same AIRoutePlanner the rivals have, and this watches every
    /// frame:
    /// - every gate pass, per car, in order (a gate log with times); MISSED raised; the player's WRONG WAY;
    /// - positions against remaining distance (outside the 4 m tie band) and position FLICKER (a swap that reverses
    ///   within 0.5 s);
    /// - each car's progress: a fall of 30 m or more with no cause (respawn, MISSED) is logged and fails;
    /// - AI wrong way against its OWN road; the finishers' run-out past the flag;
    /// then asserts at the end. PSX_CP_TEST=reset: a forced reset of the player mid-leg (no gain allowed).
    /// PSX_RACE_LAPS=n: the circuit's laps (Editor only).
    /// </summary>
    public static class CheckpointRaceWatch
    {
        static RaceManager rm;
        static CheckpointRace cp;
        static CarController player;
        static float t0;
        static readonly Dictionary<CarController, List<string>> gateLog = new Dictionary<CarController, List<string>>();
        static readonly Dictionary<CarController, List<int>> gateSeq = new Dictionary<CarController, List<int>>();
        static readonly Dictionary<CarController, int> misses = new Dictionary<CarController, int>();
        static readonly Dictionary<CarController, float> lastProgress = new Dictionary<CarController, float>();
        static readonly Dictionary<CarController, float> lastCause = new Dictionary<CarController, float>();
        static readonly Dictionary<CarController, float> aiWrong = new Dictionary<CarController, float>();
        static readonly Dictionary<CarController, int> lastPos = new Dictionary<CarController, int>();
        static readonly Dictionary<(CarController, CarController), float> swapAt = new Dictionary<(CarController, CarController), float>();
        static int flicker, disagree, unexplainedFalls, samples;
        static float playerWrongS, worstAiWrong; static string worstAiWrongWho = "";
        static readonly StringBuilder notes = new StringBuilder();
        static bool resetDone; static string resetResult = "";
        static float sampleNext;
        public static bool Active => cp != null;

        public static void Begin(RaceManager race, CarController autopilotCar)
        {
            rm = race; cp = race != null ? race.Checkpoints : null; player = autopilotCar;
            gateLog.Clear(); gateSeq.Clear(); misses.Clear(); lastProgress.Clear(); lastCause.Clear(); aiWrong.Clear();
            lastPos.Clear(); swapAt.Clear();
            flicker = disagree = unexplainedFalls = samples = 0; playerWrongS = 0f; worstAiWrong = 0f; worstAiWrongWho = "";
            notes.Clear(); resetDone = false; resetResult = ""; sampleNext = 0f;
            wwPhase = 0; wwShownAt = -1f; wwClearedAt = -1f;
            if (cp == null) return;
            t0 = Time.time;
            // a watched race keeps running when another window has the focus (the 2026-11-16 23:24 run froze at a
            // frame for minutes while a game window was in front): this play session only, never saved
            Application.runInBackground = true;
            cp.EnsurePlanner(autopilotCar);
            CheckpointRace.GatePassed += OnGate;
            CheckpointRace.Missed += OnMissed;
            RaceManager.Respawned += OnRespawn;
            var go = new GameObject("CheckpointRaceWatch");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<CheckpointRaceWatchTick>();
            RacePlayCheck.Note($"CHECKPOINT RACE: {cp.GateCount} gates, {(cp.Loop ? rm.totalLaps + " lap(s), lap " + cp.LapM.ToString("0") + " m" : "sprint")}, " +
                               $"ruler {cp.TotalM:0} m, fields {cp.FieldBuildMs:0} ms; the autopilot routes like the rivals");
        }

        static void OnGate(CarController c, int gate, int n, float t)
        {
            if (!gateLog.TryGetValue(c, out var l)) gateLog[c] = l = new List<string>();
            if (!gateSeq.TryGetValue(c, out var q)) gateSeq[c] = q = new List<int>();
            l.Add(gate + "@" + t.ToString("0.0"));
            q.Add(gate);
        }
        static void OnMissed(CarController c, int gate)
        {
            misses.TryGetValue(c, out int m); misses[c] = m + 1;
            lastCause[c] = Time.time;
            RacePlayCheck.Note($"MISSED CHECKPOINT {c.name}{(c == player ? " (player)" : "")} gate {gate} at {Time.time - t0:0}s");
        }
        static void OnRespawn(CarController c, int step, float lat) { lastCause[c] = Time.time; }

        /// <summary>Every frame (CheckpointRaceWatchTick).</summary>
        public static void Frame()
        {
            if (cp == null || rm == null || RaceReplay.Playing) return;
            if (rm.State == RaceManager.RaceState.Countdown) return;
            float now = Time.time;
            // ---- flicker: two cars that swap places and swap back within 0.5 s
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                var p = rm.GetProgress(c);
                if (p == null || p.finished || p.retired) continue;
                int pos = rm.GetPosition(c);
                foreach (var o in rm.allCars)
                {
                    if (o == null || o == c) continue;
                    var po = rm.GetProgress(o);
                    if (po == null || po.finished || po.retired) continue;
                    if (!lastPos.TryGetValue(c, out int lc) || !lastPos.TryGetValue(o, out int lo)) continue;
                    int opos = rm.GetPosition(o);
                    // c just went ahead of o
                    if (lc > lo && pos < opos)
                    {
                        if (swapAt.TryGetValue((o, c), out float back) && now - back < 0.5f)
                        { flicker++; if (flicker <= 5) RacePlayCheck.Note($"FLICKER {c.name} / {o.name} at {now - t0:0.0}s"); }
                        swapAt[(c, o)] = now;
                    }
                }
            }
            foreach (var c in rm.allCars) if (c != null) lastPos[c] = rm.GetPosition(c);
            // ---- progress falls
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                var p = rm.GetProgress(c);
                if (p == null || p.finished || p.retired) continue;
                if (lastProgress.TryGetValue(c, out float lp) && lp - p.progress >= 30f)
                {
                    bool cause = lastCause.TryGetValue(c, out float tc) && now - tc < 1.5f;
                    if (!cause)
                    {
                        unexplainedFalls++;
                        RacePlayCheck.Note($"PROGRESS FELL {c.name} by {lp - p.progress:0} m at {now - t0:0.0}s (no respawn or miss)");
                    }
                }
                lastProgress[c] = p.progress;
            }
            if (cp.PlayerWrongWay && !WrongWayTestActive) playerWrongS += Time.deltaTime;
            // ---- 1 Hz: positions vs remaining, AI wrong way on its own road, the reset test
            if (now < sampleNext) return;
            sampleNext = now + 1f;
            samples++;
            var running = new List<CarController>();
            foreach (var c in rm.allCars)
            {
                var p = c != null ? rm.GetProgress(c) : null;
                if (p != null && !p.finished && !p.retired) running.Add(c);
            }
            foreach (var a in running)
                foreach (var b in running)
                {
                    if (a == b) continue;
                    float ra = cp.RemainingToFinish(a), rb2 = cp.RemainingToFinish(b);
                    if (rm.GetPosition(a) < rm.GetPosition(b) && ra > rb2 + 4f)
                    {
                        disagree++;
                        if (disagree <= 5) RacePlayCheck.Note($"POSITION vs REMAINING: {a.name} P{rm.GetPosition(a)} {ra:0} m, {b.name} P{rm.GetPosition(b)} {rb2:0} m");
                    }
                }
            foreach (var c in running)
            {
                if (c == player) continue;
                var ai = c.GetComponent<AIDriver>();
                if (ai == null || ai.path == null) continue;
                Vector3 vel = c.Body != null ? Vector3.ProjectOnPlane(c.Body.linearVelocity, Vector3.up) : Vector3.zero;
                if (vel.sqrMagnitude < 9f) { aiWrong[c] = 0f; continue; }
                int i = ai.path.NearestIndex(c.transform.position);
                if (Vector3.Angle(vel, Vector3.ProjectOnPlane(ai.path.GetTangent(i), Vector3.up)) > 110f)
                {
                    aiWrong.TryGetValue(c, out float w); aiWrong[c] = w + 1f;
                    if (w + 1f > worstAiWrong) { worstAiWrong = w + 1f; worstAiWrongWho = c.name; }
                }
                else aiWrong[c] = 0f;
            }
            string test = System.Environment.GetEnvironmentVariable("PSX_CP_TEST") ?? "";
            WrongWayTest(test, now);
            if (!resetDone && test.Contains("reset") && cp.RaceFraction(player) > 0.35f)
            {
                resetDone = true;
                float before = cp.MeasureNow(player);
                rm.RespawnCar(player);
                Physics.SyncTransforms();
                float after = cp.MeasureNow(player);
                resetResult = $"{before:0} m before, {after:0} m after";
                RacePlayCheck.Note("CP RESET TEST: the player reset mid-leg: " + resetResult);
                RacePlayCheck.Check(after >= before - 5f, "a reset never gains (remaining after >= before - 5 m)", resetResult);
            }
        }


        // ---- PSX_CP_TEST=wrongway: at 55 % the autopilot is turned round and driven back down the suggested route
        // (against the one-way carriageway) for 12 s: WRONG WAY must show within 5 s of moving away; turned back to
        // its route, it must clear within 6 s.
        static int wwPhase; static float wwAt, wwShownAt = -1f, wwClearedAt = -1f;
        static void WrongWayTest(string test, float now)
        {
            if (!test.Contains("wrongway") || player == null) return;
            var ai = player.GetComponent<AIDriver>();
            if (ai == null) return;
            if (wwPhase == 0 && cp.RaceFraction(player) > 0.55f)
            {
                var src = rm.path;
                var go = new GameObject("CPTest_reversed"); go.SetActive(false);
                var rev = go.AddComponent<TrackPath>();
                int n = src.Count;
                rev.waypoints = new Vector3[n]; rev.curvatures = new float[n];
                for (int i = 0; i < n; i++) { rev.waypoints[i] = src.waypoints[n - 1 - i]; rev.curvatures[i] = src.curvatures[n - 1 - i]; }
                rev.spacing = src.spacing; rev.roadWidth = src.roadWidth; rev.pointToPoint = src.pointToPoint; rev.finishIndex = -1;
                var t = player.transform;
                player.TeleportTo(t.position, Quaternion.LookRotation(-t.forward, Vector3.up));
                ai.SetPath(rev);
                wwPhase = 1; wwAt = now;
                RacePlayCheck.Note($"CP WRONG-WAY TEST: the autopilot turned round at {now - t0:0}s and driven back down the route");
            }
            else if (wwPhase == 1)
            {
                if (cp.PlayerWrongWay && wwShownAt < 0f) wwShownAt = now;
                if (now - wwAt > 12f)
                {
                    var t = player.transform;
                    player.TeleportTo(t.position, Quaternion.LookRotation(-t.forward, Vector3.up));
                    var pl = cp.Of(player) != null ? cp.Of(player).planner : null;
                    if (pl != null) { pl.OnRespawned(); }
                    wwPhase = 2; wwAt = now;
                }
            }
            else if (wwPhase == 2)
            {
                if (!cp.PlayerWrongWay && wwClearedAt < 0f) wwClearedAt = now;
                if (now - wwAt > 8f)
                {
                    wwPhase = 3;
                    RacePlayCheck.Check(wwShownAt > 0f && wwShownAt - (wwAt - 12f) <= 7f, "WRONG WAY shows while driving away from the next gate (within 5 s of speed)",
                                        wwShownAt > 0f ? $"after {wwShownAt - (wwAt - 12f):0.0} s" : "never");
                    RacePlayCheck.Check(wwClearedAt > 0f && wwClearedAt - wwAt <= 6f, "and it clears once turned back",
                                        wwClearedAt > 0f ? $"after {wwClearedAt - wwAt:0.0} s" : "never");
                }
            }
        }
        public static bool WrongWayTestActive => wwPhase == 1 || wwPhase == 2;

        /// <summary>The asserts, at the end of the run.</summary>
        public static void End(bool toFinish)
        {
            if (cp == null) return;
            CheckpointRace.GatePassed -= OnGate;
            CheckpointRace.Missed -= OnMissed;
            RaceManager.Respawned -= OnRespawn;
            int N = cp.GateCount;
            int expect = cp.Loop ? N * rm.totalLaps : N - 1;
            var finishOrder = new List<RaceManager.CarProgress>();
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                var p = rm.GetProgress(c);
                gateSeq.TryGetValue(c, out var q);
                misses.TryGetValue(c, out int m);
                var pl = cp.Of(c) != null ? cp.Of(c).planner : null;
                RacePlayCheck.Note($"CP {c.name}{(c == player ? " (player, autopilot)" : "")}: {(q != null ? q.Count : 0)} of {expect} gates" +
                                   $"{(p != null && p.finished ? ", FINISHED " + p.finishTime.ToString("0.0") + " s" : p != null && p.retired ? ", RETIRED" : "")}, " +
                                   $"missed {m}, odometer {cp.Odometer(c):0} m" +
                                   (pl != null ? $", {pl.Rebuilds} plans (worst {pl.WorstBuildMs:0.0} ms), first leg edges {pl.LastSignature}" : "") +
                                   $"; gates {(gateLog.TryGetValue(c, out var gl) ? string.Join(" ", gl) : "-")}");
                if (p != null && p.finished) finishOrder.Add(p);
                if (p != null && p.finished)
                {
                    bool inOrder = q != null && q.Count == expect;
                    if (inOrder)
                        for (int i = 0; i < q.Count; i++)
                        {
                            int want = cp.Loop ? (i + 1) % N : i + 1;
                            if (q[i] != want) { inOrder = false; break; }
                        }
                    RacePlayCheck.Check(inOrder, $"{c.name} passed every gate in order", q != null ? string.Join(",", q) : "none");
                }
            }
            misses.TryGetValue(player, out int pm);
            RacePlayCheck.Check(pm == 0, "the autopilot player gets no MISSED CHECKPOINT", pm);
            RacePlayCheck.Check(playerWrongS <= 0.01f, "the autopilot player gets no WRONG WAY", playerWrongS.ToString("0.0") + " s");
            RacePlayCheck.Check(worstAiWrong <= 2f, "no rival drives against its own road for more than 2 s",
                                worstAiWrong > 0f ? worstAiWrongWho + " " + worstAiWrong.ToString("0") + " s" : "none");
            RacePlayCheck.Check(flicker == 0, "no position flicker (a swap reversed within 0.5 s)", flicker);
            RacePlayCheck.Check(disagree == 0, "positions agree with remaining distance (outside the 4 m band, 1 Hz)", disagree + " in " + samples + " samples");
            RacePlayCheck.Check(unexplainedFalls == 0, "no progress fall of 30 m or more without a respawn or a miss", unexplainedFalls);
            // finish order = finish time order
            bool ordered = true;
            for (int i = 0; i < finishOrder.Count; i++)
                for (int j = 0; j < finishOrder.Count; j++)
                    if (finishOrder[i].finishTime < finishOrder[j].finishTime && rm.GetPosition(finishOrder[i].car) > rm.GetPosition(finishOrder[j].car))
                        ordered = false;
            RacePlayCheck.Check(ordered, "the finish order is the finish-time order", finishOrder.Count + " finishers");
            var pp = rm.GetProgress(player);
            if (toFinish && pp != null && pp.finished)
            {
                float metres = RaceHandoff.MetersDriven, route = cp.TotalM;
                RacePlayCheck.Check(metres >= 0.9f * route && metres <= 1.4f * route,
                                    "MetersDriven is the honest odometer (0.9-1.4 x the route)", $"{metres:0} m for a {route:0} m race");
            }
            // no closure, no barricade, no cone anywhere in the race
            int props = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                string n = t.name;
                if (n.IndexOf("Barricade", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Closure", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("TrafficCone", System.StringComparison.OrdinalIgnoreCase) >= 0) props++;
            }
            RacePlayCheck.Check(props == 0 && string.IsNullOrEmpty(CityRaceClosures.LastRoute), "no barricade, closure or cone in the race", props);
            string tst = System.Environment.GetEnvironmentVariable("PSX_CP_TEST") ?? "";
            if (tst.Contains("reset")) RacePlayCheck.Check(resetDone, "the reset test ran", resetResult);
            if (tst.Contains("wrongway")) RacePlayCheck.Check(wwPhase == 3, "the wrong-way test ran", wwPhase);
            cp = null;
        }
    }

    /// <summary>Drives <see cref="CheckpointRaceWatch.Frame"/> every frame (not the file's first class), and with
    /// PSX_RACE_SHOTS=dir takes the race's frames from the Game view: the start, the first gate pass with the HUD,
    /// the next gate's beam at about 700 m and 300 m, the turnaround gates, the minimap past half distance and the
    /// finish sheet.</summary>
    public class CheckpointRaceWatchTick : MonoBehaviour
    {
        string dir, tag;
        readonly HashSet<string> taken = new HashSet<string>();
        float finishedAt = -1f;
        int turnShots;
        void Start()
        {
            dir = System.Environment.GetEnvironmentVariable("PSX_RACE_SHOTS");
            tag = (System.Environment.GetEnvironmentVariable("PSX_RACE_SHOTTAG") ?? "cp");
            if (string.IsNullOrEmpty(dir) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) dir = null;
            else System.IO.Directory.CreateDirectory(dir);
        }
        void Update()
        {
            CheckpointRaceWatch.Frame();
            if (dir != null) Shots();
            if (!CheckpointRaceWatch.Active && pending == 0) Destroy(gameObject);
        }
        int pending;
        void Take(string name)
        {
            if (taken.Contains(name)) return;
            taken.Add(name);
            pending++;
            StartCoroutine(ShotNow(name));
        }
        System.Collections.IEnumerator ShotNow(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string file = System.IO.Path.Combine(dir, tag + "_" + name + ".jpg");
            System.IO.File.WriteAllBytes(file, tex.EncodeToJPG(90));
            RacePlayCheck.Note($"shot {file} ({tex.width}x{tex.height})");
            Destroy(tex);
            pending--;
        }
        void Shots()
        {
            var rm = RaceManager.Instance;
            var cp = rm != null ? rm.Checkpoints : null;
            if (cp == null || rm.playerCar == null) return;
            var c = cp.Of(rm.playerCar);
            var p = rm.GetProgress(rm.playerCar);
            if (c == null || p == null) return;
            if (rm.State == RaceManager.RaceState.Racing && !taken.Contains("1_start")) Take("1_start");
            if (c.passedTotal >= 1 && Time.unscaledTime < c.flashUntil - 1.1f) Take("2_gate_pass");
            var g = cp.gates[c.next];
            Vector3 to = g.centre - rm.playerCar.transform.position; to.y = 0f;
            bool ahead = to.sqrMagnitude > 1f && Vector3.Dot(rm.playerCar.transform.forward, to.normalized) > 0.85f;
            if (!p.finished && c.passedTotal >= 1 && ahead && c.remainingGate > 560f && c.remainingGate < 640f && Time.unscaledTime > c.flashUntil) Take("3_beam_600m");
            if (!p.finished && c.passedTotal >= 1 && ahead && c.remainingGate > 260f && c.remainingGate < 330f) Take("4_beam_300m");
            if (!p.finished && g.kind == "turn" && c.remainingGate < 140f && c.remainingGate > 60f && turnShots < 2)
            { int before = taken.Count; Take("5_turn_" + (g.name ?? "").Replace(' ', '_')); if (taken.Count > before) turnShots++; }
            if (!p.finished && rm.State == RaceManager.RaceState.Racing && cp.RaceFraction(rm.playerCar) > 0.5f) Take("6_minimap_half");
            if (p.finished && finishedAt < 0f) finishedAt = Time.time;
            if (finishedAt > 0f && Time.time - finishedAt > 2.5f) Take("7_finish");
        }
    }
}
