using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// A CITY CHECKPOINT RACE (owner 2026-11-16 ~21:00: "I'm not a fan of the road construction barriers for races.
    /// Drivers can just go around them. I guess the Midnight Club approach is the way with checkpoints and allow
    /// players to pathfind"). The race FORMAT only is the reference: an ordered chain of gates, ANY road between them.
    /// - Gates come from Resources/CityCheckpoints/&lt;route&gt;.json (<see cref="CheckpointFile"/>), audited offline
    ///   so no leg can be cut on the graph or across open ground (tools/city/tracks/checkpoints.mjs).
    /// - Positions come from the REMAINING ROUTE DISTANCE on one race-wide ruler (the metric fields of
    ///   <see cref="CityRouter"/>): progress = totalM - remaining, so RaceManager's standings sort is unchanged.
    /// - Circuits repeat the chain each lap (gate 0 is the lap line and the last gate of every lap; the grid stands
    ///   behind it, so its first forward crossing is the START, never a miss); sprints chain to the finish gate.
    /// - The player gets CHECKPOINT k/N, an arrow with the road distance, a live GPS line on the minimap, a light over
    ///   the next gate (<see cref="CheckpointMarkers"/>), a chime per gate and MISSED CHECKPOINT - TURN BACK.
    /// - Every AI drives its own routed road (<see cref="AIRoutePlanner"/>, <see cref="RoutedPath"/>).
    /// Added by <see cref="TryBegin"/> from RaceManager.Start, after the handoff (a reverse twin is already turned
    /// round) and before the traffic: no scene pass, nothing under Scripts/City touched.
    /// Switches: PSX_CP_RACES (master), PSX_CP_VENUES, PSX_CP_AI_ROUTE, PSX_CP_GPS, PSX_CP_RESPAWN, PSX_CP_ODOMETER,
    /// PSX_CP_MARKERS (each "=0" = the old behaviour for that part).
    /// </summary>
    public class CheckpointRace : MonoBehaviour
    {
        /// <summary>The live checkpoint race, or null (every fixed-line race).</summary>
        public static CheckpointRace Active { get; private set; }

        RaceManager rm;
        CityMap map;
        CityRouter R;
        CheckpointFile.Data data;
        CheckpointFile.Chain chain;
        string venueId;
        bool fwy;
        public bool Loop { get; private set; }
        public int GateCount { get; private set; }
        public bool Ready { get; private set; }

        public struct SpanRT { public Vector2 p, h, r; public float y, l0, l1; public int st; public float g; }
        public class GateRT
        {
            public string kind, name;
            public Vector3 centre;
            public Vector2 h, right;
            public SpanRT[] spans;
            public int anchorSt; public float anchorT;
            public float halfW;     // the anchor carriageway's half width + 3 m (markers)
        }
        public GateRT[] gates;
        /// <summary>Metres past each kerb a span still captures a car (design: 3; a car running wide on a loop ramp
        /// went past a 3 m span in the first watched race).</summary>
        const float CaptureM = 5f;
        /// <summary>Per gate, ONE field (cpfix 11-17): the cost model for the route (the GPS line, every AI's road)
        /// and, summed along that same route, the ruler's metres (<see cref="CityRouter.Field.RM"/>). The plain-metre
        /// field of stage 1 is gone: it disagreed with the route off the suggested line (the check's blocker 3) and
        /// took half the build time.</summary>
        CityRouter.Field[] fields;
        float[] legM;
        public float LapM { get; private set; }
        public float TotalM { get; private set; }
        public float FieldBuildMs { get; private set; }

        /// <summary>Per car.</summary>
        public class CarCP
        {
            public CarController car;
            public bool isPlayer;
            public int next = 1;
            public int passedTotal;
            public bool started;
            public float remaining, remainingGate;
            public CityRouter.Loc loc;
            public int bestDir = 1;
            public Vector3 sweptFrom; public bool sweptInit;
            public float odometer;
            public readonly List<float> passTimes = new List<float>(64);
            public float closestRem = float.PositiveInfinity, closestD3 = float.PositiveInfinity;
            public bool missed; public float missedSince; public int missedGate = -1;
            public float flashUntil; public string flash;
            public float nextLocate;
            public int pathHint = -1;
            public AIRoutePlanner planner;
            // wrong way: remaining rising against the reference direction
            public float wrongRise, wrongTimer, wrongHold, lastRemGate = -1f; public int lastEdge = -1;
            public bool wrongWay;
            public float lastRemGateSample; public float lastAdvanceTime;
            public int missedCount, slot;
            public int prevSt = -1; public float prevT;
            public bool onPath;
            public float lastRg = -1f; public int jumps;
            /// <summary>The ruler's held direction on a two-way edge (+1 a->b, -1 b->a, 0 none) and the edge it is
            /// for: the GPS line starts the same way.</summary>
            public int rDir; public int rEdge = -1;
            /// <summary>Metres the car has closed on its next gate lately (decays over 1.5 s): a car crossing a later
            /// gate's line while still closing on its own (a legal re-route) has not missed it.</summary>
            public float towardAcc;
            public float missedRg;
            public float flipT, restT;
            public AIDriver ai;
            /// <summary>A two-way street where turning round is clearly the better way (WRONG WAY's two-way rule).</summary>
            public bool turnBack; public float turnBackT;
            // the ruler's hold (RulerHoldOn)
            public RulerHold hold = RulerHold.Fresh;
            /// <summary>The road the GPS route takes next, preferred by the locate where two overlap (a ramp's gore);
            /// held for 80 m after the car was located elsewhere (it is usually still on the ramp: the H2 race of
            /// 02:15 read the main line for the I-485 exit ramp, +19 km, until the ramp was 10 m off the main line).</summary>
            public CityRouter.PreferState prefer = CityRouter.PreferState.None;
        }
        readonly Dictionary<CarController, CarCP> cars = new Dictionary<CarController, CarCP>();
        public CarCP Of(CarController c) => c != null && cars.TryGetValue(c, out var v) ? v : null;
        public IEnumerable<CarCP> All => cars.Values;

        /// <summary>For the harness: every gate pass, (car, gate index, passedTotal, time).</summary>
        public static event System.Action<CarController, int, int, float> GatePassed;
        public static event System.Action<CarController, int> Missed;
        /// <summary>For the harness: a car went over a node onto a road its gate's route does not take (a route
        /// choice: the ruler steps there by the choice's extra metres, DESIGN_v2 2.3).</summary>
        public static event System.Action<CarController> RouteChoice;
        /// <summary>A rival's re-plan mid-leg (a re-route): its remaining distance is now its new road's.</summary>
        public static void NoteReplan(CarController car) => RouteChoice?.Invoke(car);

        // ------------------------------------------------------------------ bootstrap
        /// <summary>
        /// RaceManager.Start's one line. Null (the race is the old fixed-line race) unless: PSX_CP_RACES is on, the
        /// venue is a city race that is not a deck run and not a delivery, no tape is priming, PSX_CP_VENUES allows
        /// it, and its route has a checkpoint file on this graph with a chain for the raced direction.
        /// </summary>
        public static CheckpointRace TryBegin(RaceManager rm)
        {
            Active = null;
            if (rm == null || rm.path == null || !CheckpointFile.RacesOn) return null;
            var venue = TrackCatalog.At(RaceHandoff.TrackIndex);
            if (venue == null || !venue.IsCityRace || venue.IsDeckRun || rm.path.drag) return null;
            if (RaceHandoff.Delivery || TapePlayback.Active || !string.IsNullOrEmpty(TapePlayback.PendingPath)) return null;
            if (!CheckpointFile.VenueAllowed(venue.id)) return null;
            var d = CheckpointFile.Load(venue.cityRoute);
            if (d == null) return null;
            if (CheckpointFile.IsHeld(d))
            {
                Debug.Log("[Checkpoints] " + venue.cityRoute + ".json is held back: " + venue.id + " races its fixed line (PSX_CP_HELD=1 races it)");
                return null;
            }
            var map = CityMap.Get();
            if (!CheckpointFile.Matches(d, map, out string why))
            {
                Debug.LogWarning("[Checkpoints] " + venue.cityRoute + ".json " + why + ": " + venue.id + " races its fixed line");
                return null;
            }
            var ch = CheckpointFile.ChainFor(d, venue.Reversed);
            if (ch == null)
            {
                Debug.LogWarning("[Checkpoints] " + venue.cityRoute + ".json has no " + (venue.Reversed ? "rev" : "fwd") + " chain: " + venue.id + " races its fixed line");
                return null;
            }
            var cp = rm.gameObject.AddComponent<CheckpointRace>();
            cp.rm = rm; cp.map = map; cp.data = d; cp.chain = ch; cp.venueId = venue.id;
            cp.fwy = d.cost == "fwy";
            cp.Loop = ch.loop != 0;
            cp.Setup();
            Active = cp;
            // THE CAMCORDER (owner 11-16 ~21:50: "at least one camera ... at each checkpoint ... it proves the cars
            // started at the starting line, reached each checkpoint, then reached the finish line"): the tape's
            // bystanders stand at THESE gates, not at the fallback's evenly spaced points. Cleared when the race goes
            // (OnDestroy), not at the flag: the replay and SAVE TO TAPE come after it.
            cp.provider = r => r != null && r == cp.rm && Active == cp ? cp.Course() : null;
            RacePoints.Provider = cp.provider;
            Debug.Log($"[Checkpoints] {venue.id}: CHECKPOINT RACE on {venue.cityRoute} ({cp.GateCount} gates, {(cp.Loop ? rm.totalLaps + " lap(s)" : "sprint")}, {d.cost} cost)");
            return cp;
        }

        System.Func<RaceManager, RaceCourse> provider;
        RaceCourse course;

        /// <summary>The camcorder's course: one <see cref="RacePoints.Gate"/> per gate (its centre, heading and the
        /// lateral range its spans cover, less the capture margin). A circuit's gate 0 is its lap line; a sprint's
        /// gate 0 is its start line, so the camcorder's START stands there and its checkpoints are gates 1..N-2.</summary>
        public RaceCourse Course()
        {
            if (course != null) return course;
            if (gates == null || gates.Length < 2) return null;
            var list = new List<RacePoints.Gate>(gates.Length);
            for (int i = Loop ? 0 : 1; i < gates.Length; i++)
            {
                var g = gates[i];
                float l0 = float.MaxValue, l1 = float.MinValue;
                var c2 = new Vector2(g.centre.x, g.centre.z);
                foreach (var sp in g.spans)
                {
                    float off = Vector2.Dot(sp.p - c2, g.right);
                    l0 = Mathf.Min(l0, off + sp.l0 + CaptureM);
                    l1 = Mathf.Max(l1, off + sp.l1 - CaptureM);
                }
                if (l0 > l1) { l0 = -6f; l1 = 6f; }
                list.Add(new RacePoints.Gate
                {
                    kind = Loop && i == 0 ? "line" : (!Loop && i == gates.Length - 1 ? "finish" : g.kind),
                    x = g.centre.x, y = g.centre.y, z = g.centre.z, hx = g.h.x, hz = g.h.y, l0 = l0, l1 = l1,
                });
            }
            var g0 = gates[0];
            course = RacePoints.FromGates(list, Loop, Mathf.Max(1, rm.totalLaps), g0.centre, new Vector3(g0.h.x, 0f, g0.h.y));
            if (course != null) course.source = "checkpoints " + data.route + " " + chain.dir;
            return course;
        }

        void Setup()
        {
            R = CityRouter.For(map);
            // the suggested route's roads: a fresh locate or a first plan takes them where roads overlap
            var route = map.RouteById(data.route);
            var set = new HashSet<int>();
            if (route != null && route.edges != null) foreach (int e in route.edges) set.Add(e);
            R.RouteSet = set.Count > 0 ? set : null;
            var cps = chain.cps;
            GateCount = cps.Length;
            gates = new GateRT[GateCount];
            for (int i = 0; i < GateCount; i++)
            {
                var g = cps[i];
                var e = map.edges[g.e];
                var gr = new GateRT { kind = g.kind, name = g.name };
                gr.anchorSt = g.e * 2 + (g.d == 1 ? 0 : 1);
                gr.anchorT = g.d == 1 ? g.s : e.length - g.s;
                var spans = new List<SpanRT>();
                var src = g.spans != null && g.spans.Length > 0 ? g.spans : new[] { new CheckpointFile.Span { e = g.e, d = g.d, s = g.s } };
                float anchorY = e.YAt(g.s);
                foreach (var sp in src)
                {
                    var se = map.edges[sp.e];
                    var t = se.TangentAt(sp.s) * (sp.d == 1 ? 1f : -1f);
                    float y = se.YAt(sp.s);
                    if (Mathf.Abs(y - anchorY) > 6f) continue;   // a deck over or under the gate line is not on it
                    float rightExt = se.PaveEdgeM(sp.s, sp.d == 1 ? 1 : -1), leftExt = se.PaveEdgeM(sp.s, sp.d == 1 ? -1 : 1);
                    spans.Add(new SpanRT
                    {
                        p = se.PointAt(sp.s), h = t, r = new Vector2(t.y, -t.x), y = y,
                        l0 = -Mathf.Abs(leftExt) - CaptureM, l1 = Mathf.Abs(rightExt) + CaptureM,
                        st = sp.e * 2 + (sp.d == 1 ? 0 : 1), g = sp.d == 1 ? sp.s : se.length - sp.s,
                    });
                }
                gr.spans = spans.ToArray();
                var a = gr.spans[0];
                var lp = LineModel.LanePoint(e, g.s);
                gr.centre = new Vector3(lp.x, anchorY, lp.y);
                gr.h = a.h; gr.right = a.r;
                gr.halfW = Mathf.Max(Mathf.Abs(a.l0), Mathf.Abs(a.l1));
                gates[i] = gr;
            }
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                cars[c] = new CarCP { car = c, isPlayer = c == rm.playerCar, next = 1, started = !Loop, slot = cars.Count };
            }
            // the countdown waits for the fields, chained onto whatever already holds it (the course preload)
            var prev = RaceManager.HoldCountdown;
            RaceManager.HoldCountdown = () => (prev != null && prev()) || !Ready;
            gameObject.AddComponent<CheckpointMarkers>();
            MakeSounds();
            StartCoroutine(BuildFields());
        }

        // the static restart cache: RESTART (a scene reload) starts at once
        static string cacheKey;
        static CityRouter.Field[] cacheFields;
        /// <summary>The fields' own build time (the sum of the per-gate builds, no frame yields) and the worst gate.</summary>
        public float FieldComputeMs { get; private set; }
        public float FieldWorstMs { get; private set; }

        IEnumerator BuildFields()
        {
            string key = data.route + "|" + chain.dir + "|" + map.graphHash.ToString("x8") + "|" + GateCount;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (cacheKey == key && cacheFields != null)
            {
                fields = cacheFields;
            }
            else
            {
                fields = new CityRouter.Field[GateCount];
                var seeds = new List<CityRouter.Seed>(8);
                float worst = 0f, sum = 0f;
                var frame = System.Diagnostics.Stopwatch.StartNew();
                for (int k = 0; k < GateCount; k++)
                {
                    seeds.Clear();
                    foreach (var sp in gates[k].spans)
                    {
                        seeds.Add(new CityRouter.Seed { st = sp.st, g = sp.g });
                        // a two-way span is reached from either side of the gate line
                        if (!map.edges[sp.st >> 1].oneway) seeds.Add(new CityRouter.Seed { st = sp.st ^ 1, g = R.len[sp.st] - sp.g });
                    }
                    var t0 = System.Diagnostics.Stopwatch.StartNew();
                    fields[k] = R.Build(seeds, true, fwy);
                    float ms = (float)t0.Elapsed.TotalMilliseconds;
                    worst = Mathf.Max(worst, ms); sum += ms;
                    // several gates a frame inside a 40 ms budget (the countdown is held: loading, not racing); one
                    // a frame stretched the build to 29 s under a slow loading frame rate
                    if (frame.Elapsed.TotalMilliseconds > 40.0) { yield return null; frame.Restart(); }
                }
                cacheKey = key; cacheFields = fields;
                FieldComputeMs = sum; FieldWorstMs = worst;
                Debug.Log($"[Checkpoints] fields: {GateCount} gates, worst gate {worst:0.0} ms, all {sum:0} ms");
            }
            // one race-wide ruler: each leg's metres from the previous gate's anchor ALONG ITS ROUTE (the field's RM)
            legM = new float[GateCount];
            for (int k = 0; k < GateCount; k++)
            {
                int from = k == 0 ? GateCount - 1 : k - 1;
                if (!Loop && k == 0) { legM[0] = 0f; continue; }
                legM[k] = R.RouteFrom(fields[k], gates[from].anchorSt, gates[from].anchorT);
                if (float.IsInfinity(legM[k])) Debug.LogError($"[Checkpoints] gate {k} cannot be reached from gate {from}");
            }
            float lap = 0f;
            for (int k = Loop ? 0 : 1; k < GateCount; k++) lap += legM[k];
            LapM = lap;
            TotalM = Loop ? lap * Mathf.Max(1, rm.totalLaps) : lap;
            FieldBuildMs = (float)sw.Elapsed.TotalMilliseconds;
            // every AI on its own road
            if (CheckpointFile.On("PSX_CP_AI_ROUTE"))
                foreach (var c in rm.allCars)
                {
                    if (c == null || c == rm.playerCar) continue;
                    if (c.GetComponent<AIDriver>() != null) EnsurePlanner(c);
                }
            // the first plans, one per frame, before the green
            for (int guard = 0; guard < 64 && queue.Count > 0; guard++) { PumpQueue(); yield return null; }
            Ready = true;
            Debug.Log($"[Checkpoints] ready: lap {LapM:0} m, race {TotalM:0} m, fields {FieldBuildMs:0} ms");
        }

        /// <summary>A routed road for this car (the rivals; the harness's autopilot too).</summary>
        public AIRoutePlanner EnsurePlanner(CarController c)
        {
            var cp = Of(c);
            if (cp == null) return null;
            if (cp.planner != null) return cp.planner;
            var ai = c.GetComponent<AIDriver>();
            if (ai == null) return null;
            cp.planner = AIRoutePlanner.Create(this, cp, ai, transform);
            Request(cp.planner);
            return cp.planner;
        }

        // ------------------------------------------------------------------ the rebuild queue: one per frame race-wide
        readonly Queue<AIRoutePlanner> queue = new Queue<AIRoutePlanner>();
        public void Request(AIRoutePlanner p) { if (p != null && !p.queued) { p.queued = true; queue.Enqueue(p); } }
        void PumpQueue()
        {
            if (queue.Count == 0) return;
            var p = queue.Dequeue();
            p.queued = false;
            if (p != null) p.Rebuild();
        }

        void Update()
        {
            if (RaceReplay.Playing) return;
            PumpQueue();
            if (Ready && rm != null) { UpdateGps(); TowWrecks(); }
        }

        /// <summary>
        /// A RETIRED RIVAL IS TOWED (PSX_CP_TOW_WRECKS, default ON; =0 it stays where it stopped, as before). A rival's
        /// routed road is the real pavement, so a wreck stopped on a one-lane ramp leaves no line round it: on CltH6 a
        /// retired Civic held five cars on the US 74 ramp for the rest of the race (stuck recoveries found 0 clear
        /// stations). After 6 s at rest the wreck leaves the race (the race's DNF stands; the replay shows it until then).
        /// </summary>
        void TowWrecks()
        {
            if (!CheckpointFile.On("PSX_CP_TOW_WRECKS") || rm.State != RaceManager.RaceState.Racing) return;
            foreach (var c in cars.Values)
            {
                if (c.isPlayer || c.car == null || !c.car.gameObject.activeSelf) continue;
                var p = rm.GetProgress(c.car);
                if (p == null || !p.retired) continue;
                if (Mathf.Abs(c.car.forwardSpeed) > 1f) { c.restT = 0f; continue; }
                c.restT += Time.deltaTime;
                if (c.restT < 6f) continue;
                Debug.Log($"[Checkpoints] {c.car.name}: retired and stopped 6 s - towed off the course");
                c.car.gameObject.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            // the camcorder's feed goes with the race (a later race in another scene must not film these gates)
            if (provider != null && RacePoints.Provider == provider) RacePoints.Provider = null;
        }

        // ------------------------------------------------------------------ fields, for the planners and the self-test
        public CityRouter Router => R;
        /// <summary>Gate k's field: its cost (the route) and its ruler metres (RM).</summary>
        public CityRouter.Field Cost(int k) => fields != null ? fields[k] : null;
        public int NextAfter(int k)
        {
            if (Loop) return (k + 1) % GateCount;
            return k + 1 < GateCount ? k + 1 : -1;
        }
        public bool IsFinishGate(CarCP c, int k)
        {
            if (!Loop) return k == GateCount - 1;
            return k == 0 && LapOf(c) >= rm.totalLaps;
        }
        int LapOf(CarCP c) { var p = rm.GetProgress(c.car); return p != null ? p.lap : 1; }

        // ------------------------------------------------------------------ per car, per frame (RaceManager.Update)
        /// <summary>The per-car loop's checkpoint branch: locate, the swept gate test, the ruler, MISSED, wrong way.</summary>
        public void Step(RaceManager.CarProgress p)
        {
            var c = Of(p.car);
            if (c == null || !Ready) return;
            var car = p.car;
            Vector3 pos = car.transform.position;
            if (!c.sweptInit)
            {
                c.sweptInit = true; c.sweptFrom = pos;
                // a car seated on or past the lap line at the green has started
                if (Loop && !c.started && AlongGate(gates[0], pos, out float along) && along > 0f && along < 60f)
                { c.started = true; p.crossedStartOnce = true; }
                c.lastAdvanceTime = Time.time;
                c.lastRemGateSample = float.PositiveInfinity;
            }
            c.odometer += Mathf.Abs(car.forwardSpeed) * Time.deltaTime;

            // a teleport (a respawn, TeleportTo, a reset) passes nothing, and the car's approach to its gate starts
            // again from where it now stands (the check race's forced detour: a car put down within 60 m of gate 5 on
            // a road with no way to it but an 8.3 km loop showed MISSED for 15 s)
            if ((pos - c.sweptFrom).sqrMagnitude > 30f * 30f)
            {
                c.sweptFrom = pos; c.prevSt = -1;
                c.closestRem = float.PositiveInfinity; c.closestD3 = float.PositiveInfinity;
                c.lastRg = -1f; c.towardAcc = 0f; c.lastRemGateSample = float.PositiveInfinity;
                c.hold = RulerHold.Fresh;
            }

            // ---- where the car is on the graph (its state and arc along it), for the ruler and the arc pass
            float rg = RemainingToGate(c, pos);
            int curSt = c.loc.valid && !c.loc.offGraph ? c.loc.edge * 2 + (c.loc.dir == 1 ? 0 : 1) : -1;
            float curT = curSt >= 0 ? R.TOn(curSt, c.loc.s) : 0f;
            // THE ARC PASS: the car's own road carried it over the gate's arc on a span's state this frame (the
            // swept line test can miss a car running wide on a loop ramp; the watched race of 23:07, the Charger
            // at the Wilkinson apex). Any direction on a two-way span, as the line test.
            bool ArcPassed(int k, bool anchorOnly)
            {
                if (curSt < 0 || c.prevSt < 0) return false;
                float pT = c.prevT;
                // just over a node OFF the gate's state, short of the gate a frame ago: it ran the rest of that state,
                // the gate's arc included (Uptown's lap line 2 m from its edge's end: two rivals went over the node
                // unseen, then had 3.7 km "to go" round the loop)
                if (curSt != c.prevSt && R.eNode[c.prevSt] == R.sNode[curSt])
                {
                    float gp = fields[k].seedG[c.prevSt];
                    if (gp >= 0f && pT < gp - 0.01f && R.len[c.prevSt] - pT <= 30f && (!anchorOnly || c.prevSt == gates[k].anchorSt))
                        return true;
                }
                // just over a node onto the gate's state: it came in at the state's start
                if (curSt != c.prevSt) { if (R.eNode[c.prevSt] != R.sNode[curSt]) return false; pT = 0f; }
                if (curT - pT > 30f) return false;
                float g = fields[k].seedG[curSt];
                if (g < 0f) return false;
                if (anchorOnly && curSt != gates[k].anchorSt) return false;
                return pT < g - 0.01f && curT >= g;
            }

            // a route choice: over a node onto a road the gate's route does not take
            if (curSt >= 0 && c.prevSt >= 0 && curSt != c.prevSt && R.eNode[c.prevSt] == R.sNode[curSt] &&
                fields[c.next].nxt[c.prevSt] != curSt)
                RouteChoice?.Invoke(car);

            // ---- the gates crossed this frame
            Vector3 a = c.sweptFrom, b = pos;
            bool moved = (b - a).sqrMagnitude > 1e-6f;
            // the lap line at the start (circuits): the first forward crossing starts the clock
            if (Loop && !c.started && c.passedTotal == 0 && c.next == 1)
            {
                if ((moved && Crosses(gates[0], a, b, out bool fwd0) && fwd0) || ArcPassed(0, true))
                { c.started = true; p.crossedStartOnce = true; p.lapStartTime = Time.time; }
            }
            else if (moved && Loop && c.started && c.passedTotal == 0 && c.next == 1 && Crosses(gates[0], a, b, out bool fwdB) && !fwdB)
            { c.started = false; p.crossedStartOnce = false; p.lapStartTime = Time.time; }   // turned round over the line on lap 1
            else if ((moved && Crosses(gates[c.next], a, b, out _)) || ArcPassed(c.next, false))
            {
                Pass(p, c);
                if (!p.finished) rg = RemainingToGate(c, pos);
            }
            else if (moved && c.towardAcc <= 2f)
            {
                // A LATER gate of this lap crossed forward while this one is pending, and the car is not closing on
                // its own gate: MISSED. Only gates AFTER the pending one in this lap's order count (cpfix 11-17: the
                // check race's player re-crossed gate 3 on I-485 SB on its legal way back to gate 5 and got a false
                // MISSED); a car still closing on its gate is on a legal route that runs through that line.
                for (int g = LaterInLap(c.next); g >= 0; g = LaterInLap(g))
                    if (Crosses(gates[g], a, b, out bool fw) && fw) { RaiseMissed(c, rg); break; }
            }
            c.sweptFrom = pos;
            c.prevSt = curSt; c.prevT = curT;
            if (p.finished) return;

            // ---- the ruler (a frame with no finite answer - a car between graph states - keeps the last one)
            if (float.IsInfinity(rg) || float.IsNaN(rg))
            {
                UpdateNearestIdx(p, pos);
                return;
            }
            // a diagnostic for the harness: a jump of 30 m or more in one frame with no gate passed
            if (c.lastRg >= 0f && Mathf.Abs(rg - c.lastRg) > 30f && c.jumps++ < 6)
                Debug.Log($"[Checkpoints] {car.name}: remaining to gate {c.next} {c.lastRg:0} -> {rg:0} m ({(c.onPath ? "own road" : "graph locate")}, edge {c.loc.edge} s {c.loc.s:0} dir {c.loc.dir} rdir {c.rDir} off {c.loc.dist:0} m{(c.loc.offGraph ? ", OFF GRAPH" : "")}{(c.prevSt != curSt ? ", over a node" : "")})");
            // closing on the gate lately (a later gate's line on a legal re-route is not a miss)
            if (c.lastRg >= 0f && Mathf.Abs(rg - c.lastRg) < 30f) c.towardAcc += c.lastRg - rg;
            c.towardAcc *= Mathf.Exp(-Time.deltaTime / 1.5f);
            c.lastRg = rg;
            c.remainingGate = rg;
            c.remaining = rg + LegsAfter(c);
            p.progress = TotalM - c.remaining;
            if (c.remaining < 1e8f && rg < c.lastRemGateSample - 25f) { c.lastRemGateSample = rg; c.lastAdvanceTime = Time.time; }
            // a step UP (a route choice at a node, a re-plan) is not progress, but progress counts from there on
            else if (c.remaining < 1e8f && rg > c.lastRemGateSample + 25f) c.lastRemGateSample = rg;
            // a rival GIVING WAY (held behind traffic, following, backing out of a jam) is not lost: its clock waits
            // (the Tryon street race at MEDIUM: 18 re-routes and two cars put back on their road while queued)
            if (!c.isPlayer)
            {
                if (c.ai == null) c.ai = car.GetComponent<AIDriver>();
                var aiw = c.ai;
                if (aiw != null && (aiw.DebugTrafficBrake > 0f || aiw.DebugLift > 0.05f || aiw.DebugBackingUp)) c.lastAdvanceTime = Time.time;
            }
            // THE LOST-AI WATCHDOG (DESIGN_v2 2.6): a rival whose remaining distance has not fallen 50 m in 20 s
            // (its own stuck detection has not fired) goes back on its road
            if (!c.isPlayer && Time.time - c.lastAdvanceTime > 20f && CheckpointFile.On("PSX_CP_RESPAWN"))
            {
                c.lastAdvanceTime = Time.time; c.lastRemGateSample = float.PositiveInfinity;
                Debug.Log($"[Checkpoints] {car.name}: no progress to gate {c.next} in 20 s - back on its road");
                rm.RespawnCar(car);
                return;
            }

            // ---- MISSED by distance: the car came within 120 m of the gate BY THE ROAD (and 60 m as the crow flies),
            // and the road to it is 150 m longer now: it went past without crossing. Near by the road, not just in
            // the air: a street under a gate whose only way up is a long loop is not a miss.
            float d3 = Vector3.Distance(pos, gates[c.next].centre);
            if (rg < c.closestRem) c.closestRem = rg;
            if (d3 < c.closestD3) c.closestD3 = d3;
            if (!c.missed && c.closestRem <= 120f && c.closestD3 <= 60f && rg - c.closestRem >= 150f) RaiseMissed(c, rg);
            // it clears once the car is back near its closest approach, or has made 60 m back toward the gate
            if (c.missed && c.missedGate == c.next && Time.time - c.missedSince > 2f && (rg <= c.closestRem + 50f || rg <= c.missedRg - 60f))
            { c.missed = false; c.closestRem = rg; c.closestD3 = d3; }   // a new approach starts here

            // ---- WRONG WAY (the player): remaining rising against the U-turn-aware reference, no node crossed
            if (c.isPlayer) UpdateWrongWay(c, car, pos);

            // ---- the compatibility index on the suggested route
            UpdateNearestIdx(p, pos);
        }

        /// <summary>The edge the locate should favour: the route's next one from the last road the car was on the
        /// route, held 80 m past a node where the car was read on another road.</summary>
        int PreferEdge(CarCP c, CityRouter.Field F, Vector3 pos) => R.Prefer(ref c.prefer, F, c.next, c.loc, c.rDir, pos);

        /// <summary>Which way along its located edge the car's nose points (+1 a->b).</summary>
        int NoseDir(CarCP c)
        {
            var f = c.car.transform.forward;
            return Vector2.Dot(new Vector2(f.x, f.z), map.edges[c.loc.edge].TangentAt(c.loc.s)) >= 0f ? 1 : -1;
        }

        float LegsAfter(CarCP c)
        {
            float sum = 0f;
            if (Loop)
            {
                if (c.next != 0) { for (int k = c.next + 1; k < GateCount; k++) sum += legM[k]; sum += legM[0]; }
                int lap = LapOf(c);
                sum += Mathf.Max(0, rm.totalLaps - lap) * LapM;
            }
            else for (int k = c.next + 1; k < GateCount; k++) sum += legM[k];
            return sum;
        }

        /// <summary>Metres from the car to its next gate on the ruler.</summary>
        /// <summary>Metres a car may stand off its road (its waypoint line or a located edge's pavement) before the
        /// ruler counts the way back.</summary>
        const float OffRoadFreeM = 10f;

        float RemainingToGate(CarCP c, Vector3 pos)
        {
            var F = fields[c.next];
            var car = c.car;
            Vector3 vel = car.Body != null ? car.Body.linearVelocity : Vector3.zero;
            // A RIVAL on its own routed road: exact and free, off its own waypoints. It is HELD on that road while it
            // stays within 80 m of it on its level (cpfix 11-17: the check race's Supra, knocked 23.5 m off the
            // Wilkinson loop ramp into a tree, was handed to the graph locate at 25 m and measured 190 m behind its
            // own station). Off it, the ruler adds the metres beyond 10 m: it falls back as the car strays, never jumps.
            // The PLAYER never reads a road of his own, even on the play check's autopilot: he is measured as the
            // real player is, by the graph locate (the check: the autopilot never exercised the player's code path).
            var pl = c.planner;
            if (!c.isPlayer && pl != null && pl.Path != null && pl.Path.Routed)
            {
                var path = pl.Path;
                c.pathHint = path.NearestIndex(pos, c.pathHint < 0 ? -1 : Mathf.Min(c.pathHint, path.Count - 1), 25);
                int i = c.pathHint;
                Vector3 wp = path.GetPoint(i);
                float off = new Vector2(pos.x - wp.x, pos.z - wp.z).magnitude;
                c.onPath = off < (c.onPath ? CityRouter.KeepMaxM : 25f) && Mathf.Abs(pos.y - wp.y) < (c.onPath ? 8f : 5f);
                if (c.onPath)
                {
                    int e = path.edgeAt[i];
                    var ed = map.edges[e];
                    Vector3 tan = path.GetTangent(i);
                    // the direction the plan drives this edge (RoutedPath's own; a corner waypoint's tangent can
                    // point anywhere, and a one-way read backwards was an unreachable gate: the Infinity frames)
                    int dir = path.dirAt != null ? path.dirAt[i] : 1;
                    float sRaw = path.sAt[i] + dir * Vector3.Dot(pos - wp, tan);
                    float s = Mathf.Clamp(sRaw, 0f, ed.length);
                    int st = e * 2 + (dir == 1 ? 0 : 1);
                    c.loc.valid = true; c.loc.offGraph = false; c.loc.edge = e; c.loc.s = s; c.loc.dir = dir; c.loc.dist = off;
                    c.rDir = dir; c.rEdge = e;
                    c.bestDir = dir;
                    // ITS OWN ROAD'S METRES to the gate (a varied leg is ranked by what it drives); off its plan, the
                    // field's route from where it is
                    float tt = dir == 1 ? s : ed.length - s;
                    // the metres past either end of the edge its waypoint maps to (a join's last raw segment belongs to the
                    // NEXT edge, so a car a few metres short of the node already reads that edge's start: a 4 m step at
                    // every join, beside a rival in the 4 m tie band - the AI flickers of Tryon)
                    float tRaw = dir == 1 ? sRaw : ed.length - sRaw;
                    float beyond = Mathf.Clamp(tRaw < 0f ? -tRaw : tRaw > ed.length ? -(tRaw - ed.length) : 0f, -8f, 8f);
                    float own = pl.RemainingOnPlan(st, tt, c.next);
                    if (float.IsInfinity(own)) own = R.RouteFrom(F, st, tt);
                    return Mathf.Max(0f, own + beyond) + Mathf.Max(0f, off - OffRoadFreeM);
                }
            }
            // the player (and a rival far off its road): the sticky locate at 10 Hz (staggered), a slide in between
            if (Time.time >= c.nextLocate || !c.loc.valid)
            {
                // where two roads overlap (a ramp split), the one the route takes next
                R.Locate(pos, vel, car.transform.forward, ref c.loc, Time.time, PreferEdge(c, F, pos));
                c.nextLocate = Time.time + 0.1f + Random.Range(0f, 0.02f);
            }
            else
            {
                R.Slide(pos, vel, car.transform.forward, ref c.loc);
                // at the end of its edge the slide holds s while the car runs on: the full locate NOW, not up to 0.1 s
                // later (a 5 m freeze then a drop at every node, beside a rival in the 4 m tie band: the flickers of
                // C7 and Tryon)
                if (c.loc.valid && !c.loc.offGraph && (c.loc.s <= 0.3f || c.loc.s >= map.edges[c.loc.edge].length - 0.3f))
                {
                    R.Locate(pos, vel, car.transform.forward, ref c.loc, Time.time, PreferEdge(c, F, pos));
                    c.nextLocate = Time.time + 0.1f;
                }
            }
            // the held direction belongs to an edge: a new edge chooses afresh (by the cost, as the GPS does)
            // THE RULER'S DIRECTION ON A TWO-WAY STREET is the way the car is going (cpfix 11-17, after the detour
            // race of 02:27: chosen by the cost it flipped back and forth on Todd Rd, where the freeway profile prices a
            // local street at x4, and the remaining jumped 1,245 m and back). A new edge starts the way the car travels;
            // a flip needs 1 s of travel the other way at 4 m/s or more (a spinning car's nose means nothing). Turning
            // round when that is clearly better is WRONG WAY's business (UpdateWrongWay), and the GPS line then points
            // back; following the line, the remaining falls metre for metre.
            if (c.loc.valid && !c.loc.offGraph)
            {
                if (c.loc.edge != c.rEdge) { c.rEdge = c.loc.edge; c.rDir = c.loc.dir == -1 ? -1 : 1; c.flipT = 0f; }
                // ...driving FORWARDS the other way: a car backing out of a jam still faces its way (the Tryon street
                // race of 02:45: the autopilot reversing in traffic flipped the ruler, +539 m)
                else if (!map.edges[c.loc.edge].oneway && NoseDir(c) != c.rDir && car.forwardSpeed > 4f)
                {
                    c.flipT += Time.deltaTime;
                    if (c.flipT >= 1f) { c.rDir = c.loc.dir; c.flipT = 0f; }
                }
                else c.flipT = 0f;
            }
            float r = R.Remaining(F, pos, ref c.loc, ref c.rDir);
            if (RulerHoldOn)
            {
                bool was = c.hold.on;
                r = RulerHold.Step(R, map, ref c.hold, F, pos, car.Body != null ? car.Body.linearVelocity : Vector3.zero, car.transform.forward, c.loc, c.rDir, r);
                if (c.hold.on && !was && RulerHolds++ < 40) { Debug.Log($"[Checkpoints] {car.name}: ruler reads edge {c.hold.loc.edge} under the car ({r:0} m), not the located edge {c.loc.edge}"); }
            }
            c.bestDir = c.rDir == -1 ? -1 : 1;
            return r;
        }

        /// <summary>The ruler holds across a lane's width (<see cref="RulerHold"/>, cityai 11-25). PSX_CP_RULER_HOLD=0:
        /// as before (LAB row AI RACING TURNS does not hold it: its own env switch only).</summary>
        public static bool RulerHoldOn = CheckpointFile.On("PSX_CP_RULER_HOLD");
        /// <summary>Holds begun, all cars (the harness).</summary>
        public static int RulerHolds;

        void UpdateNearestIdx(RaceManager.CarProgress p, Vector3 pos)
        {
            var path = rm.path;
            int h = path.NearestIndex(pos, p.nearestIdx);
            var w = path.GetPoint(h);
            var cc = Of(p.car);
            int slot = cc != null ? cc.slot : 0;
            if (new Vector2(pos.x - w.x, pos.z - w.z).sqrMagnitude > 30f * 30f && Time.frameCount % 30 == slot % 30)
                h = path.NearestIndex(pos);
            p.nearestIdx = h;
        }

        // ------------------------------------------------------------------ the gate line
        /// <summary>Signed metres past the gate line along its heading, if the point is inside a span's width.</summary>
        bool AlongGate(GateRT g, Vector3 pos, out float along)
        {
            along = 0f;
            var q = new Vector2(pos.x, pos.z);
            foreach (var sp in g.spans)
            {
                float lat = Vector2.Dot(q - sp.p, sp.r);
                if (lat < sp.l0 || lat > sp.l1 || Mathf.Abs(pos.y - sp.y) > 5f) continue;
                along = Vector2.Dot(q - sp.p, sp.h);
                return true;
            }
            return false;
        }

        /// <summary>The swept test: the segment a..b crosses the gate line inside a span, on its level.</summary>
        public bool Crosses(GateRT g, Vector3 a, Vector3 b, out bool forward)
        {
            forward = false;
            var pa = new Vector2(a.x, a.z); var pb = new Vector2(b.x, b.z);
            foreach (var sp in g.spans)
            {
                float da = Vector2.Dot(pa - sp.p, sp.h), db = Vector2.Dot(pb - sp.p, sp.h);
                if ((da < 0f) == (db < 0f)) continue;
                float u = da / (da - db);
                var x = Vector2.LerpUnclamped(pa, pb, u);
                float lat = Vector2.Dot(x - sp.p, sp.r);
                if (lat < sp.l0 || lat > sp.l1) continue;
                float y = Mathf.LerpUnclamped(a.y, b.y, u);
                if (Mathf.Abs(y - sp.y) > 5f) continue;
                forward = db > da;
                return true;
            }
            return false;
        }

        void Pass(RaceManager.CarProgress p, CarCP c)
        {
            int k = c.next;
            c.passedTotal++;
            c.passTimes.Add(p.raceTime);
            c.missed = false; c.missedGate = -1;
            GatePassed?.Invoke(c.car, k, c.passedTotal, p.raceTime);
            bool finish = false;
            if (Loop)
            {
                if (k == 0)
                {
                    p.lastLapTime = Time.time - p.lapStartTime;
                    if (p.bestLapTime <= 0f || p.lastLapTime < p.bestLapTime) p.bestLapTime = p.lastLapTime;
                    p.lapStartTime = Time.time;
                    p.lap++;
                    if (p.lap > rm.totalLaps) finish = true;
                    c.next = 1;
                }
                else c.next = (k + 1) % GateCount;
            }
            else
            {
                if (k >= GateCount - 1) finish = true;
                else c.next = k + 1;
            }
            c.closestRem = float.PositiveInfinity; c.closestD3 = float.PositiveInfinity;
            c.lastRemGateSample = float.PositiveInfinity;
            c.lastRg = -1f; c.towardAcc = 0f; c.rDir = 0;
            c.hold = RulerHold.Fresh;   // a new gate: a new ruler
            if (c.isPlayer && !finish)
            {
                c.flash = FlashText(c, k);
                c.flashUntil = Time.unscaledTime + 1.5f;
                Play(chime, 0.55f);
            }
            if (finish)
            {
                p.progress = TotalM;
                c.remaining = 0f; c.remainingGate = 0f;
                p.finished = true;
                p.finishTime = p.raceTime;
                p.trapSpeedKmh = Mathf.Abs(p.car.speedKmh);
                rm.FinishCar(p);
            }
            if (c.planner != null) c.planner.OnGatePassed(finish);
        }

        string FlashText(CarCP c, int k)
        {
            int n = c.passedTotal;
            string label = "CHECKPOINT " + DisplayIndex(c, k);
            // the gap to the car that went through this same gate just before (none: first through it, LEAD).
            // Not "the car one place up": the order is the ruler's, and at the gate it can still be last frame's.
            float mine = c.passTimes[n - 1], best = float.NegativeInfinity;
            foreach (var o in cars.Values)
                if (o != c && o.passTimes.Count >= n && o.passTimes[n - 1] <= mine && o.passTimes[n - 1] > best) best = o.passTimes[n - 1];
            if (float.IsNegativeInfinity(best)) return label + "   LEAD";
            return label + "   +" + (mine - best).ToString("0.0");
        }

        /// <summary>The gate after <paramref name="k"/> in this lap's order, or -1 at the lap's end (a circuit's lap
        /// line, gate 0, is the last gate of every lap; a sprint ends at its last gate).</summary>
        int LaterInLap(int k)
        {
            if (Loop) return k == 0 ? -1 : (k + 1) % GateCount;
            return k + 1 < GateCount ? k + 1 : -1;
        }

        void RaiseMissed(CarCP c, float rg)
        {
            if (c.missed && c.missedGate == c.next) return;
            c.missed = true; c.missedSince = Time.time; c.missedGate = c.next; c.missedCount++; c.missedRg = rg;
            if (c.isPlayer) Play(buzz, 0.6f);
            Missed?.Invoke(c.car, c.next);
            if (c.planner != null) c.planner.OnMissed();
        }

        void UpdateWrongWay(CarCP c, CarController car, Vector3 pos)
        {
            float dt = Time.deltaTime;
            float rem = c.remainingGate;
            bool nodeCrossed = c.loc.edge != c.lastEdge;
            // the rise in remaining over about the last 3 s (older rise decays), counted only on frames that cross
            // no node: a route choice's one-off step at a node (DESIGN_v2 2.3) never counts
            if (c.lastRemGate >= 0f && !nodeCrossed && rem < 1e8f && c.lastRemGate < 1e8f)
                c.wrongRise = Mathf.Max(0f, c.wrongRise + (rem - c.lastRemGate));
            c.wrongRise *= Mathf.Exp(-dt / 3f);
            c.lastRemGate = rem; c.lastEdge = c.loc.edge;
            bool against = false;
            Vector3 v = car.Body != null ? car.Body.linearVelocity : Vector3.zero; v.y = 0f;
            bool twoWayBack = false;
            if (c.loc.valid && !c.loc.offGraph && v.magnitude > 6f)
            {
                var ed = map.edges[c.loc.edge];
                if (ed.oneway)
                {
                    // a one-way driven against its flow: the remaining rises (no backwards credit)
                    against = Vector2.Dot(new Vector2(v.x, v.z).normalized, ed.TangentAt(c.loc.s)) < -0.3f;
                }
                else
                {
                    // a two-way street: TURNING ROUND is clearly better than going on (by a U-turn's 240 cost-m and
                    // 15 %; out again under 120 and 8 %)
                    var F = fields[c.next];
                    float L = ed.length;
                    float go = c.rDir == -1 ? R.CostFrom(F, c.loc.edge * 2 + 1, L - c.loc.s) : R.CostFrom(F, c.loc.edge * 2, c.loc.s);
                    float back = c.rDir == -1 ? R.CostFrom(F, c.loc.edge * 2, c.loc.s) : R.CostFrom(F, c.loc.edge * 2 + 1, L - c.loc.s);
                    bool clearly = !float.IsInfinity(back) && (float.IsInfinity(go) || (back + 240f < go && back < 0.85f * go));
                    bool still = !float.IsInfinity(back) && (float.IsInfinity(go) || (back + 120f < go && back < 0.92f * go));
                    c.turnBack = c.turnBack ? still : clearly;
                    twoWayBack = c.turnBack;
                }
            }
            else if (!c.loc.valid || c.loc.offGraph) c.turnBack = false;
            if ((against && c.wrongRise >= 25f) || twoWayBack) c.wrongTimer += dt; else c.wrongTimer = 0f;
            c.wrongWay = c.wrongTimer >= 2f;
        }

        // ------------------------------------------------------------------ what the race reads
        public float RemainingToFinish(CarController car) { var c = Of(car); return c != null ? Mathf.Max(0f, c.remaining) : -1f; }
        public float RaceFraction(CarController car)
        {
            var c = Of(car);
            if (c == null || TotalM < 1f) return 0f;
            var p = rm.GetProgress(car);
            if (p != null && p.finished) return 1f;
            if (!c.sweptInit) return 0f;   // before its first racing frame there is no measure yet
            return Mathf.Clamp01(1f - c.remaining / TotalM);
        }
        public float Odometer(CarController car) { var c = Of(car); return c != null ? c.odometer : 0f; }
        /// <summary>The play check's -Laps (Editor only): the circuit's laps, and the ruler with them.</summary>
        public void SetLaps(int laps) { rm.totalLaps = Mathf.Max(1, laps); TotalM = Loop ? LapM * rm.totalLaps : LapM; }
        /// <summary>The remaining distance measured NOW (a fresh locate): the harness's reset test.</summary>
        public float MeasureNow(CarController car)
        {
            var c = Of(car); if (c == null) return -1f;
            c.loc.valid = false; c.nextLocate = 0f; c.pathHint = -1;
            float rg = RemainingToGate(c, car.transform.position);
            return rg + LegsAfter(c);
        }
        public bool PlayerWrongWay { get { var c = Of(rm != null ? rm.playerCar : null); return c != null && c.wrongWay; } }

        /// <summary>The gate number the HUD shows for gate index k (circuits: 1..N with the lap line last).</summary>
        public int DisplayIndex(CarCP c, int k) => Loop ? (k == 0 ? GateCount : k) : k;
        public int DisplayCount => Loop ? GateCount : GateCount - 1;

        public string LapLine(CarController car)
        {
            var c = Of(car); if (c == null) return "";
            var p = rm.GetProgress(car);
            if (p != null && p.finished) return "FINISHED";
            bool fin = IsFinishGate(c, c.next);
            string cp = fin ? "FINISH" : "CHECKPOINT " + DisplayIndex(c, c.next) + "/" + DisplayCount;
            if (Loop && rm.totalLaps > 1)
                return "LAP " + Mathf.Min(p != null ? p.lap : 1, rm.totalLaps) + "/" + rm.totalLaps + "  " +
                       (fin ? "FINISH" : "CP " + DisplayIndex(c, c.next) + "/" + DisplayCount);
            return cp;
        }

        static readonly string[] Arrows = { "^", "/^", ">", "\\v", "v", "v/", "<", "^\\" };
        float cueNext; string cueLine = "";
        public string Cue(CarController car)
        {
            if (Time.unscaledTime < cueNext) return cueLine;
            cueNext = Time.unscaledTime + 0.25f;
            var c = Of(car); if (c == null) return "";
            var g = gates[c.next];
            Vector3 to = g.centre - car.transform.position; to.y = 0f;
            float rel = Vector3.SignedAngle(new Vector3(car.transform.forward.x, 0f, car.transform.forward.z), to, Vector3.up);
            int oct = Mathf.RoundToInt(Mathf.Repeat(rel, 360f) / 45f) % 8;
            float d = c.remainingGate < 1e8f ? c.remainingGate : to.magnitude;
            string range = d >= 1000f ? (d / 1000f).ToString("0.0") + " KM" : Mathf.RoundToInt(d / 10f) * 10 + " M";
            string what = IsFinishGate(c, c.next) ? "FINISH" : "CHECKPOINT " + DisplayIndex(c, c.next);
            cueLine = Arrows[oct] + "  " + what + "  " + range;
            return cueLine;
        }

        /// <summary>The centre banner's checkpoint lines (lowest priority): MISSED, then the pass flash.</summary>
        public string Banner(CarController car)
        {
            var c = Of(car); if (c == null) return null;
            var p = rm.GetProgress(car);
            if (p != null && p.finished) return null;
            if (c.missed) return "MISSED CHECKPOINT " + DisplayIndex(c, c.missedGate >= 0 ? c.missedGate : c.next) + " - TURN BACK";
            if (Time.unscaledTime < c.flashUntil) return c.flash;
            return null;
        }

        // ------------------------------------------------------------------ the GPS line (minimap)
        public readonly List<Vector2> GpsPts = new List<Vector2>(4096);
        public int GpsVersion { get; private set; }
        float gpsNext; int gpsEdge = -1;
        readonly List<int> gpsStates = new List<int>(1024), legStates = new List<int>(512);
        int[] gpsStamp; int gpsStampNow = 1;

        void UpdateGps()
        {
            if (!CheckpointFile.On("PSX_CP_GPS")) return;
            var c = Of(rm.playerCar);
            if (c == null) return;
            if (!c.loc.valid)
            {
                // on the grid (no Step yet): locate him so the line is there before the green
                var car = rm.playerCar;
                R.Locate(car.transform.position, Vector3.zero, car.transform.forward, ref c.loc);
                if (!c.loc.valid) return;
            }
            var pp = rm.GetProgress(rm.playerCar);
            if (pp != null && pp.finished) { if (GpsPts.Count > 0) { GpsPts.Clear(); GpsVersion++; } return; }
            if (gpsStamp == null) gpsStamp = new int[map.edges.Length];
            bool offChain = c.loc.edge != gpsEdge && gpsStamp[c.loc.edge] != gpsStampNow;
            // the ruler turned round on this street (the cost now prefers the other way): the line turns with it
            bool turned = c.loc.edge == c.rEdge && c.rDir != 0 && (c.turnBack ? -c.rDir : c.rDir) != gpsDir;
            if (Time.unscaledTime < gpsNext && !offChain && !turned) return;
            gpsNext = Time.unscaledTime + 0.5f;
            gpsEdge = c.loc.edge;
            BuildGps(c);
        }
        int gpsDir;

        void BuildGps(CarCP c)
        {
            GpsPts.Clear(); gpsStates.Clear();
            gpsStampNow++;
            var ed = map.edges[c.loc.edge];
            int k = c.next;
            // from the car, the way the RULER measures (cpfix 11-17: one route for the line and the remaining
            // distance): a two-way street's held direction (it may be behind him), a one-way's legal direction
            int dir = 1;
            if (!ed.oneway)
            {
                if (c.rEdge == c.loc.edge && c.rDir != 0) dir = c.turnBack ? -c.rDir : c.rDir;
                else R.ValueAt(fields[k], c.loc.edge, c.loc.s, false, out dir);
            }
            gpsDir = dir;
            int st = c.loc.edge * 2 + (dir == 1 ? 0 : 1);
            if (!R.legal[st]) st ^= 1;
            float t = (st & 1) == 0 ? c.loc.s : ed.length - c.loc.s;
            var car = c.car.transform.position;
            GpsPts.Add(new Vector2(car.x, car.z));
            for (int leg = 0; leg < 2 && k >= 0; leg++)
            {
                if (!R.Descend(fields[k], st, t, legStates, out float lastT)) break;
                for (int i = 0; i < legStates.Count; i++)
                {
                    int s = legStates[i];
                    float a = i == 0 ? t : 0f, b = i == legStates.Count - 1 ? lastT : R.len[s];
                    AddPts(s, a, b);
                    gpsStamp[s >> 1] = gpsStampNow;
                    if (GpsPts.Count > 4000) break;
                }
                st = legStates[legStates.Count - 1]; t = lastT;
                k = NextAfter(k);
            }
            GpsVersion++;
        }

        void AddPts(int st, float t0, float t1)
        {
            var e = map.edges[st >> 1];
            bool fwd = (st & 1) == 0;
            float s0 = fwd ? t0 : e.length - t0, s1 = fwd ? t1 : e.length - t1;
            GpsPts.Add(e.PointAt(s0));
            if (fwd) { for (int i = 1; i < e.pts.Length - 1; i++) if (e.s[i] > s0 && e.s[i] < s1) GpsPts.Add(e.pts[i]); }
            else { for (int i = e.pts.Length - 2; i >= 1; i--) if (e.s[i] < s0 && e.s[i] > s1) GpsPts.Add(e.pts[i]); }
            GpsPts.Add(e.PointAt(s1));
        }

        /// <summary>The gate a car is going for next.</summary>
        public int NextGateOf(CarController car) { var c = Of(car); return c != null ? c.next : -1; }
        public Vector2 GatePlan(int k) => new Vector2(gates[k].centre.x, gates[k].centre.z);

        // ------------------------------------------------------------------ respawn (DESIGN_v2 2.7)
        public bool HandlesRespawn => CheckpointFile.On("PSX_CP_RESPAWN") && Ready;

        public void Respawn(CarController car)
        {
            var c = Of(car);
            if (c == null) return;
            // the player first (even on the harness's autopilot): the graph under him, never forward
            if (c.isPlayer && RespawnPlayer(c))
            {
                c.sweptFrom = car.transform.position; c.loc.valid = false; c.pathHint = -1;
                if (c.planner != null) c.planner.OnRespawned();
                return;
            }
            if (c.planner != null && c.planner.Path != null && c.planner.Path.Routed)
            {
                // an AI: back on its own road, then a fresh plan from there
                var path = c.planner.Path;
                int start = path.NearestIndex(car.transform.position, c.pathHint);
                rm.RespawnOn(car, path, start);
                c.sweptFrom = car.transform.position; c.loc.valid = false; c.pathHint = -1;
                c.planner.OnRespawned();
                return;
            }
            int idx = rm.path.NearestIndex(car.transform.position, rm.GetProgress(car)?.nearestIdx ?? -1);
            rm.RespawnOn(car, rm.path, idx);
            c.sweptFrom = car.transform.position; c.loc.valid = false;
        }

        /// <summary>The player: the road under him (120 m, height-aware), seated on his route's better direction at
        /// the nearest point, walking BACK up to 30 m for room, never forward (a reset never gains). No road: 10 m past
        /// the last gate he passed.</summary>
        bool RespawnPlayer(CarCP c)
        {
            var car = c.car;
            Vector3 pos = car.transform.position;
            if (R.Nearest(pos, 120f, 8f, -1, 0f, out int e, out float s, out _))
            {
                var ed = map.edges[e];
                R.ValueAt(fields[c.next], e, s, false, out int dir);
                int st = e * 2 + (dir == 1 ? 0 : 1);
                if (!R.legal[st]) { st ^= 1; dir = -dir; }
                float t = (st & 1) == 0 ? s : ed.length - s;
                for (float back = 0f; back <= 30f; back += 3f)
                {
                    float tt = t - back;
                    if (tt < 0f) break;
                    float sAt = R.SOn(st, tt);
                    var lp = LineModel.LanePoint(ed, sAt);
                    var tan = ed.TangentAt(sAt) * dir;
                    var at = new Vector3(lp.x, ed.YAt(sAt), lp.y);
                    var rot = Quaternion.LookRotation(new Vector3(tan.x, 0f, tan.y), Vector3.up);
                    if (!DriveSession.TryPlace(car, at, rot)) continue;
                    rm.RaiseRespawned(car, (int)back, 0f);
                    return true;
                }
            }
            // the last gate passed (the grid's line when none yet), 10 m past it on its carriageway
            int last = c.passedTotal == 0 ? 0 : (c.next == 0 ? GateCount - 1 : (c.next == 1 && Loop ? 0 : c.next - 1));
            var g = gates[last];
            {
                var ed = map.edges[g.anchorSt >> 1];
                float tt = Mathf.Min(g.anchorT + 10f, R.len[g.anchorSt]);
                float sAt = R.SOn(g.anchorSt, tt);
                var lp = LineModel.LanePoint(ed, sAt);
                var tan = ed.TangentAt(sAt) * ((g.anchorSt & 1) == 0 ? 1f : -1f);
                var at = new Vector3(lp.x, ed.YAt(sAt), lp.y);
                var rot = Quaternion.LookRotation(new Vector3(tan.x, 0f, tan.y), Vector3.up);
                if (DriveSession.TryPlace(car, at, rot)) { rm.RaiseRespawned(car, -2, 0f); return true; }
                car.ResetTo(at, rot, seated: true);
                rm.RaiseRespawned(car, -1, 0f);
                return true;
            }
        }

        // ------------------------------------------------------------------ the positions' tie band (4 m / 0.4 s)
        readonly Dictionary<(CarController, CarController), float> swapSince = new Dictionary<(CarController, CarController), float>();
        readonly Dictionary<CarController, int> lastOrder = new Dictionary<CarController, int>();
        public void StabilizeOrder(List<RaceManager.CarProgress> sorted)
        {
            for (int i = 0; i + 1 < sorted.Count; i++)
            {
                var a = sorted[i]; var b = sorted[i + 1];
                if (a.finished || b.finished || a.retired || b.retired) continue;
                if (!lastOrder.TryGetValue(a.car, out int pa) || !lastOrder.TryGetValue(b.car, out int pb)) continue;
                var key = (a.car, b.car);
                if (pa > pb && a.progress - b.progress < 4f)
                {
                    if (!swapSince.TryGetValue(key, out float t0)) { swapSince[key] = Time.time; t0 = Time.time; }
                    if (Time.time - t0 < 0.4f) { sorted[i] = b; sorted[i + 1] = a; }
                }
                else swapSince.Remove(key);
            }
            for (int i = 0; i < sorted.Count; i++) lastOrder[sorted[i].car] = i;
        }

        // ------------------------------------------------------------------ sounds (generated: no asset, no copyright)
        AudioSource src; AudioClip chime, buzz;
        void MakeSounds()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.loop = false;
            const int rate = 22050;
            // a gate: a short two-tone chime, pager-like (1999 arcade)
            int n = (int)(rate * 0.34f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = t < 0.12f ? 1046.5f : 1568f;
                float tt = t < 0.12f ? t : t - 0.12f;
                float env = Mathf.Exp(-tt * 14f) * Mathf.Clamp01(tt * 400f);
                float w = Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * f * t);
                d[i] = 0.45f * env * w;
            }
            chime = AudioClip.Create("psx_cp_chime", n, 1, rate, false); chime.SetData(d, 0);
            // a missed gate: a low double buzz
            n = (int)(rate * 0.5f);
            d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                bool on = t < 0.18f || (t > 0.26f && t < 0.44f);
                float w = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 110f * t)) * 0.6f + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.3f;
                d[i] = on ? 0.32f * w : 0f;
            }
            buzz = AudioClip.Create("psx_cp_buzz", n, 1, rate, false); buzz.SetData(d, 0);
        }
        void Play(AudioClip clip, float vol)
        {
            if (src == null || clip == null) return;
            src.PlayOneShot(clip, vol * AudioPrefs.Effects);
        }
    }
}
