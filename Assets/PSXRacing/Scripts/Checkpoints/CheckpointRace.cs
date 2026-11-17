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
        CityRouter.Field[] met, cost;
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
        }
        readonly Dictionary<CarController, CarCP> cars = new Dictionary<CarController, CarCP>();
        public CarCP Of(CarController c) => c != null && cars.TryGetValue(c, out var v) ? v : null;
        public IEnumerable<CarCP> All => cars.Values;

        /// <summary>For the harness: every gate pass, (car, gate index, passedTotal, time).</summary>
        public static event System.Action<CarController, int, int, float> GatePassed;
        public static event System.Action<CarController, int> Missed;

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
            Debug.Log($"[Checkpoints] {venue.id}: CHECKPOINT RACE on {venue.cityRoute} ({cp.GateCount} gates, {(cp.Loop ? rm.totalLaps + " lap(s)" : "sprint")}, {d.cost} cost)");
            return cp;
        }

        void Setup()
        {
            R = CityRouter.For(map);
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
                        l0 = -Mathf.Abs(leftExt) - 3f, l1 = Mathf.Abs(rightExt) + 3f,
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
        static CityRouter.Field[] cacheMet, cacheCost;

        IEnumerator BuildFields()
        {
            string key = data.route + "|" + chain.dir + "|" + map.graphHash.ToString("x8") + "|" + GateCount;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (cacheKey == key && cacheMet != null && cacheCost != null)
            {
                met = cacheMet; cost = cacheCost;
            }
            else
            {
                met = new CityRouter.Field[GateCount]; cost = new CityRouter.Field[GateCount];
                var seeds = new List<CityRouter.Seed>(8);
                float worst = 0f;
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
                    met[k] = R.Build(seeds, false, fwy);
                    cost[k] = R.Build(seeds, true, fwy);
                    worst = Mathf.Max(worst, (float)t0.Elapsed.TotalMilliseconds);
                    yield return null;
                }
                cacheKey = key; cacheMet = met; cacheCost = cost;
                Debug.Log($"[Checkpoints] fields: {GateCount} gates x 2, worst gate {worst:0.0} ms");
            }
            // one race-wide ruler: each leg's metres from the previous gate's anchor, by the leg's own field
            legM = new float[GateCount];
            for (int k = 0; k < GateCount; k++)
            {
                int from = k == 0 ? GateCount - 1 : k - 1;
                if (!Loop && k == 0) { legM[0] = 0f; continue; }
                legM[k] = R.CostFrom(met[k], gates[from].anchorSt, gates[from].anchorT);
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
            if (Ready && rm != null) UpdateGps();
        }

        void OnDestroy() { if (Active == this) Active = null; }

        // ------------------------------------------------------------------ fields, for the planners and the self-test
        public CityRouter Router => R;
        public CityRouter.Field Metric(int k) => met != null ? met[k] : null;
        public CityRouter.Field Cost(int k) => cost != null ? cost[k] : null;
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

            // a teleport (a respawn, TeleportTo, a reset) passes nothing
            if ((pos - c.sweptFrom).sqrMagnitude > 30f * 30f) c.sweptFrom = pos;

            // ---- the gates crossed this frame
            Vector3 a = c.sweptFrom, b = pos;
            if ((b - a).sqrMagnitude > 1e-6f)
            {
                // the lap line at the start (circuits): the first forward crossing starts the clock
                if (Loop && !c.started && c.passedTotal == 0 && c.next == 1)
                {
                    if (Crosses(gates[0], a, b, out bool fwd0) && fwd0)
                    { c.started = true; p.crossedStartOnce = true; p.lapStartTime = Time.time; }
                }
                else if (Loop && c.started && c.passedTotal == 0 && c.next == 1 && Crosses(gates[0], a, b, out bool fwdB) && !fwdB)
                { c.started = false; p.crossedStartOnce = false; p.lapStartTime = Time.time; }   // turned round over the line on lap 1
                else if (Crosses(gates[c.next], a, b, out _))
                    Pass(p, c);
                else
                {
                    // a later gate crossed while this one is pending: MISSED
                    for (int g = 0; g < GateCount; g++)
                    {
                        if (g == c.next || (Loop && g == 0 && c.passedTotal == 0)) continue;
                        int last = c.next == 0 ? GateCount - 1 : c.next - 1;
                        if (g == last) continue;
                        if (Crosses(gates[g], a, b, out bool fw) && fw) { RaiseMissed(c); break; }
                    }
                }
            }
            c.sweptFrom = pos;
            if (p.finished) return;

            // ---- the ruler
            float rg = RemainingToGate(c, pos);
            c.remainingGate = rg;
            c.remaining = rg + LegsAfter(c);
            p.progress = TotalM - c.remaining;
            if (c.remaining < 1e8f && rg < c.lastRemGateSample - 25f) { c.lastRemGateSample = rg; c.lastAdvanceTime = Time.time; }
            // THE LOST-AI WATCHDOG (DESIGN_v2 2.6): a rival whose remaining distance has not fallen 50 m in 20 s
            // (its own stuck detection has not fired) goes back on its road
            if (!c.isPlayer && Time.time - c.lastAdvanceTime > 20f && CheckpointFile.On("PSX_CP_RESPAWN"))
            {
                c.lastAdvanceTime = Time.time; c.lastRemGateSample = float.PositiveInfinity;
                Debug.Log($"[Checkpoints] {car.name}: no progress to gate {c.next} in 20 s - back on its road");
                rm.RespawnCar(car);
                return;
            }

            // ---- MISSED by distance: the closest approach came within 60 m, and the gate is 150 m further now
            float d3 = Vector3.Distance(pos, gates[c.next].centre);
            if (rg < c.closestRem) { c.closestRem = rg; c.closestD3 = Mathf.Min(c.closestD3, d3); }
            if (d3 < c.closestD3) c.closestD3 = d3;
            if (!c.missed && c.closestD3 <= 60f && rg - c.closestRem >= 150f) RaiseMissed(c);
            if (c.missed && Time.time - c.missedSince > 2f && rg <= c.closestRem + 50f && c.missedGate == c.next) c.missed = false;

            // ---- WRONG WAY (the player): remaining rising against the U-turn-aware reference, no node crossed
            if (c.isPlayer) UpdateWrongWay(c, car, pos);

            // ---- the compatibility index on the suggested route
            UpdateNearestIdx(p, pos);
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
        float RemainingToGate(CarCP c, Vector3 pos)
        {
            var F = met[c.next];
            var car = c.car;
            Vector3 vel = car.Body != null ? car.Body.linearVelocity : Vector3.zero;
            // an AI on its own routed road: exact and free, off its own waypoints
            var pl = c.planner;
            if (pl != null && pl.Path != null && pl.Path.Routed)
            {
                var path = pl.Path;
                c.pathHint = path.NearestIndex(pos, c.pathHint < 0 ? -1 : Mathf.Min(c.pathHint, path.Count - 1), 25);
                int i = c.pathHint;
                Vector3 wp = path.GetPoint(i);
                if (new Vector2(pos.x - wp.x, pos.z - wp.z).sqrMagnitude < 15f * 15f)
                {
                    int e = path.edgeAt[i];
                    var ed = map.edges[e];
                    Vector3 tan = path.GetTangent(i);
                    var osmT = ed.TangentAt(path.sAt[i]);
                    int dir = Vector2.Dot(osmT, new Vector2(tan.x, tan.z)) >= 0f ? 1 : -1;
                    float s = Mathf.Clamp(path.sAt[i] + dir * Vector3.Dot(pos - wp, tan), 0f, ed.length);
                    int st = e * 2 + (dir == 1 ? 0 : 1);
                    c.loc.valid = true; c.loc.offGraph = false; c.loc.edge = e; c.loc.s = s; c.loc.dir = dir;
                    float v = R.CostFrom(F, st, dir == 1 ? s : ed.length - s);
                    if (!ed.oneway) v = Mathf.Min(v, R.CostFrom(F, st ^ 1, dir == 1 ? ed.length - s : s));
                    c.bestDir = dir;
                    return v;
                }
            }
            // anyone else: a full locate at 10 Hz (staggered), a slide along the located edge in between
            if (Time.time >= c.nextLocate || !c.loc.valid)
            {
                R.Locate(pos, vel, car.transform.forward, ref c.loc);
                c.nextLocate = Time.time + 0.1f + Random.Range(0f, 0.02f);
            }
            else R.Slide(pos, vel, car.transform.forward, ref c.loc);
            float r = R.Remaining(F, pos, ref c.loc, out int bd);
            c.bestDir = bd;
            return r;
        }

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
            int pos = rm.GetPosition(c.car);
            string label = "CHECKPOINT " + DisplayIndex(c, k);
            if (pos <= 1) return label + "   LEAD";
            // the car directly ahead now, at this same gate
            CarCP ahead = null;
            foreach (var o in cars.Values)
                if (o != c && rm.GetPosition(o.car) == pos - 1) { ahead = o; break; }
            if (ahead != null && ahead.passTimes.Count >= n)
                return label + "   +" + (c.passTimes[n - 1] - ahead.passTimes[n - 1]).ToString("0.0");
            return label;
        }

        void RaiseMissed(CarCP c)
        {
            if (c.missed && c.missedGate == c.next) return;
            c.missed = true; c.missedSince = Time.time; c.missedGate = c.next; c.missedCount++;
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
            if (c.loc.valid && !c.loc.offGraph && v.magnitude > 6f)
            {
                var ed = map.edges[c.loc.edge];
                var t = ed.TangentAt(c.loc.s) * (ed.oneway ? 1f : c.bestDir);
                against = Vector2.Dot(new Vector2(v.x, v.z).normalized, t) < -0.3f;
            }
            if (against && c.wrongRise >= 25f) c.wrongTimer += dt; else c.wrongTimer = 0f;
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
            if (Time.unscaledTime < gpsNext && !offChain) return;
            gpsNext = Time.unscaledTime + 0.5f;
            gpsEdge = c.loc.edge;
            BuildGps(c);
        }

        void BuildGps(CarCP c)
        {
            GpsPts.Clear(); gpsStates.Clear();
            gpsStampNow++;
            var ed = map.edges[c.loc.edge];
            int k = c.next;
            // from the car, the better way on a two-way street (it may be behind him)
            R.ValueAt(cost[k], c.loc.edge, c.loc.s, ed.oneway && c.loc.dir < 0, out int dir);
            int st = c.loc.edge * 2 + (dir == 1 ? 0 : 1);
            if (!R.legal[st]) st ^= 1;
            float t = (st & 1) == 0 ? c.loc.s : ed.length - c.loc.s;
            var car = c.car.transform.position;
            GpsPts.Add(new Vector2(car.x, car.z));
            for (int leg = 0; leg < 2 && k >= 0; leg++)
            {
                if (!R.Descend(cost[k], st, t, legStates, out float lastT)) break;
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
                R.ValueAt(met[c.next], e, s, false, out int dir);
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
