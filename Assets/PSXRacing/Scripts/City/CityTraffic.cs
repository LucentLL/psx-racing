using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// AMBIENT TRAFFIC IN CHARLOTTE FREE ROAM (the skeleton). The owner,
    /// 2026-10-26: "add traffic that drives around Charlotte (from very light
    /// at late hours) to rush hour ... (Use traffic lights, stop signs, change
    /// lanes, merge, drive speed limits, etc)". Earlier: traffic reads the SAME
    /// line model as the paint, this is North America (right-hand traffic),
    /// density by the time of day, random not regular, real physics (a hit
    /// wrecks it).
    ///
    /// WHAT A CAR IS: TrafficSystem's car (a CarShell on a lifted box, layer 2,
    /// CarLights) driven the same way - a rigidbody whose VELOCITY is servoed
    /// onto its lane point every physics step, gravity off, the height the
    /// road's own drawn profile (<see cref="CityMap.Edge.YAt"/>); a hit that
    /// changes that velocity by more than 1.5 m/s WRECKS it (gravity on, servo
    /// off for good). Races keep TrafficSystem; this runs only in free roam.
    ///
    /// WHERE IT DRIVES: a PLAN of legs - RUNS along an edge in one lane (the
    /// lane's centre from the line model's pavement band,
    /// <see cref="LineModel.ExtentsBefore"/>, laid out as the painter lays the
    /// edge's line set: the b->a lanes from the left edge, then the TWLTL, then
    /// the a->b lanes), and TURNS across each node (a cubic Bezier from the
    /// lane at one mouth to the lane at the next; freeway ramps take a long
    /// one through the merge or diverge). A route is chosen node by node: a
    /// random legal turn weighted by class (one-ways respected, U-turns only at
    /// a dead end).
    ///
    /// HOW IT DRIVES: IDM car-following on whatever is ahead on its own plan -
    /// cars on the same leg in a lane it uses, cars converging onto the leg it
    /// is heading for (a ramp yields to the mainline unless it is clearly
    /// ahead), the player, wrecks - and the junction it is coming to:
    ///   SIGNALS   <see cref="CitySignals.AspectOf"/>: stop on red, on yellow
    ///             unless too close to stop; a left turn yields to oncoming.
    ///   STOP      a full stop at the bar, then first come first served (an
    ///             all-way), or a gap in the major road (a two-way stop).
    ///   UNSIGNED  the minor road yields to the major one.
    /// Lane changes (3-5 s) to reach a turn lane, where a lane ends, to pass a
    /// slower car, and back to the right on a freeway - with gap acceptance.
    ///
    /// HOW MANY: a share of the pool cap by the clock hour (very light at
    /// 03:00, the cap at 08:00 and 17:00), born 140-380 m from the player out
    /// of the camera's view, on roads weighted by class and lanes (freeways and
    /// arterials busier than locals), recycled past 480 m. Cap 48 desktop, 24
    /// phone. ON by default on a desktop, OFF on a phone until its frame cost
    /// is measured (<see cref="DefaultOn"/>, <see cref="DefaultOnPhone"/>); the
    /// page's ?traffic=1 / ?traffic=0, PSX_CITY_TRAFFIC=1/0 or <see cref="Enabled"/> override.
    /// </summary>
    public partial class CityTraffic : MonoBehaviour
    {
        public static CityTraffic Instance { get; private set; }

        /// <summary>THE DEFAULT (2026-10-29, the owner: "I still don't see
        /// traffic in city free roam"): ON in desktop free roam. A phone stays
        /// OFF (<see cref="DefaultOnPhone"/>) until its frame cost is measured.</summary>
        public const bool DefaultOn = true;
        public const bool DefaultOnPhone = false;
        /// <summary>The tools' and the environment's switch: PSX_CITY_TRAFFIC=1
        /// / =0 overrides the default; a tool sets <see cref="Enabled"/> before
        /// the scene starts (CityTrafficCheck does). Null: the platform default.</summary>
        public static bool? Forced = System.Environment.GetEnvironmentVariable("PSX_CITY_TRAFFIC") is string v && v.Length > 0
            ? v != "0" : (bool?)null;
        public static bool Enabled
        {
            get => Forced ?? (Application.isMobilePlatform ? DefaultOnPhone : DefaultOn);
            set => Forced = value;
        }
        /// <summary>Tools: the clock hour the density reads (negative: the
        /// hour the scene runs at).</summary>
        public static float ClockOverride = -1f;
        /// <summary>Tools: the pool cap (negative: 48 desktop, 24 phone).</summary>
        public static int CapOverride = -1;
        /// <summary>Tools: no cars on the road at all (the frame-time A/B).</summary>
        public static bool Suspended;

        // ---- the knobs -------------------------------------------------------
        const float LaneM = RoadProfiles.LaneM;
        const float SpawnMinM = 140f, SpawnMaxM = 380f, SpawnFirstMinM = 60f;
        const float DespawnM = 480f, DespawnHardM = 700f;
        const int CapDesktop = 48, CapPhone = 24;
        const int MaxLegs = 8;
        const float RideClear = 0.22f, WreckFloor = 0.01f, WreckSpeed = 1.5f;
        /// <summary>Two traffic cars touching at less than this (m/s of knock)
        /// is a BUMP - counted, both drive on - not a wreck: a fender-bender
        /// between two servoed bodies is the solver separating them.</summary>
        const float BumpMps = 4.5f;
        /// <summary>The lateral acceleration a city driver takes a bend at.</summary>
        const float ALat = 2.8f;
        /// <summary>The gap left at a stop, bumper to bumper.</summary>
        const float StopS0 = 2.2f;
        const float EmergencyDecel = 9f;
        /// <summary>A yellow is run when stopping would need more than this.</summary>
        const float YellowDecel = 4f;
        /// <summary>A stop at a STOP bar lasts at least this long.</summary>
        const float StopHoldS = 1.0f;
        /// <summary>A car on a major road this many seconds out (or closer
        /// than <see cref="MajorNearM"/>) is not a gap to pull out into.</summary>
        const float MajorGapS = 4.5f, MajorNearM = 9f;
        /// <summary>Time to the line an oncoming car must be past for a left
        /// turn across it.</summary>
        const float OncomingGapS = 5f;

        /// <summary>Share of the cap on the road, by clock hour (00..23):
        /// light all night, the morning and evening peaks.</summary>
        static readonly float[] HourShare =
        {
            0.10f, 0.07f, 0.05f, 0.04f, 0.05f, 0.12f, 0.35f, 0.85f, 1.00f, 0.75f, 0.55f, 0.60f,
            0.68f, 0.65f, 0.62f, 0.72f, 0.90f, 1.00f, 0.85f, 0.60f, 0.45f, 0.35f, 0.25f, 0.16f,
        };
        /// <summary>Spawn weight per lane by class (local .. motorway).</summary>
        static readonly float[] ClassSpawnW = { 0.10f, 0.35f, 0.55f, 0.75f, 0.90f, 1.00f };
        /// <summary>Route weight by the class of the road turned into.</summary>
        static readonly float[] ClassRouteW = { 0.35f, 0.70f, 0.90f, 1.00f, 1.00f, 1.00f };

        public static float ShareAt(float clock)
        {
            clock = Mathf.Repeat(clock, 24f);
            int h = Mathf.FloorToInt(clock) % 24;
            return Mathf.Lerp(HourShare[h], HourShare[(h + 1) % 24], clock - Mathf.Floor(clock));
        }
        public static float ClockNow => ClockOverride >= 0f ? ClockOverride : TimeOfDay.ClockHours(TimeOfDay.Current);
        /// <summary>The TrafficLevels level nearest this clock's density.</summary>
        public static int LevelAt(float clock)
        {
            float k = ShareAt(clock) * TrafficLevels.PerKm[TrafficLevels.RushHour];
            int best = 0;
            for (int i = 1; i < TrafficLevels.Count; i++)
                if (Mathf.Abs(TrafficLevels.PerKm[i] - k) < Mathf.Abs(TrafficLevels.PerKm[best] - k)) best = i;
            return best;
        }

        // ---- the plan --------------------------------------------------------
        const byte CtlNone = 0, CtlSignal = 1, CtlStop = 2, CtlYield = 3;

        struct Leg
        {
            /// <summary>False: a RUN along an edge; true: a TURN across a node.</summary>
            public bool turn;
            public int edge, edge2;
            public sbyte dir, dir2;
            /// <summary>Run: arc positions at its start and end.</summary>
            public float s0, s1;
            /// <summary>Run: the lane it arrives in and the lane wanted at its
            /// end (kerb-counted: 0 the rightmost). Turn: the lane left, the
            /// lane joined.</summary>
            public int laneIn, laneOut;
            /// <summary>The node at its end (run) or across which it turns.</summary>
            public int node;
            /// <summary>Run: the control at its end, and the approach (apps).</summary>
            public byte ctl;
            public int app;
            /// <summary>Turn: -1 left, 0 straight, 1 right, 2 U.</summary>
            public sbyte kind;
            public bool merge, exit;
            public Vector2 p0, p1, p2, p3;
            public float y0, y1;
            public float len;
            /// <summary>Turn: the speed it is taken at.</summary>
            public float vMax;
            /// <summary>Run: the key (edge * 2 + arrives at b) of its approach.</summary>
            public int key;
        }

        sealed class Car
        {
            public int id;
            public GameObject go;
            public Rigidbody rb;
            public BoxCollider box;
            public CarLights lights;
            public Transform[] wheels;
            public float wheelR, halfW, halfL;
            public Vector3 rideCenter, rideSize, fullCenter, fullSize;
            public bool active, wrecked;
            public float wreckedAt, bornAt;
            public Vector3 commanded;
            public bool hasCommand;
            public string lastContact;

            public readonly Leg[] legs = new Leg[MaxLegs];
            public int legCount;
            /// <summary>Metres into legs[0].</summary>
            public float u;
            public float speed, accel;
            /// <summary>The driver: share of the limit, time gap, comfortable
            /// acceleration and braking.</summary>
            public float factor, timeGap, aMax, bDec;
            public int lane, laneFrom, laneTo;
            public bool lc;
            public float lcT, lcDur, nextLc;
            /// <summary>The pose placed this step: plan point, heading, the
            /// travel tangent, height, lateral off the OSM line (runs).</summary>
            public Vector2 pos, fwd, tan;
            public float y, lat, spin;
            public bool deadEnd;
            /// <summary>Its plan ends where the DATA ends and the road goes on
            /// (CityMap.nodeFrontier): it drives on off the map there, no stop.</summary>
            public bool offMap;

            // the junction facts this step (Facts)
            public int appJ = -1, appKey = -1, appIdx = -1, boxJ = -1, inKey = -1;
            public byte appCtl;
            public float appDist;
            public Vector2 appU;
            public sbyte turnNext, boxKind;
            public Vector2 boxU;
            // per-approach state
            public int committed = -1, stopKey = -1, goKey = -1, redStopKey = -1, lineKey = -1, waitKey = -1;
            public float stopAt, lineD, waitAt;
            /// <summary>Junction logic (CityTraffic.Junctions): held short of a
            /// full box this step; the approach it turns right on red from, the
            /// approach it stopped at on the red, when; the last box it held for.</summary>
            public bool heldClear;
            public int rtorKey = -1, rtorStopKey = -1, clearKey = -1;
            public float rtorAt;
            /// <summary>Since when it has waited for a gap to reach the lane its
            /// turn needs (-1: not waiting).</summary>
            public float mandAt = -1f;
            /// <summary>The last traffic car it touched, and when (a bump, not
            /// a wreck, below <see cref="BumpMps"/>).</summary>
            public Car lastTraffic;
            public float lastTrafficAt = -9f, lastBumpAt = -9f;
            public int vTick;
            public float vDesCached;
            /// <summary>The next Extend keeps the lane it is in (a replan).</summary>
            public bool keepLane;
            public float gap;
            public bool leaderInLane;
            public float leaderV;
            /// <summary>What held it this step (the road log's 'why', Census
            /// WhyName), the kind of thing ahead, and the node of the control
            /// that held it.</summary>
            public byte why, leadKind, ctlWhy;
            public int ctlNode = -1;
        }

        // ---- state -----------------------------------------------------------
        CityMap map;
        CityMeshes.Trims trims;
        CarController player;
        CityWorld world;
        int roadMask;
        int cap;
        byte[] lanesF, lanesB, slots, offF;
        float[] limitMps, spawnW;
        bool[] routable;
        int[] degree, nodeJunction, majorA, majorB;
        /// <summary>Per directed edge ((edge << 1) | (driven a->b ? 1 : 0)):
        /// the node it arrives at has no legal way on but back the way it
        /// came - a cul-de-sac, or a street that turns one-way against it
        /// (2026-10-29 census: East 16th, Barry, Buchanan, Atherton Heights,
        /// Berkeley, Morehead Medical - "only a U-turn"). Extend keeps out of
        /// those unless the data ends there (a frontier: it drives on off the map).</summary>
        bool[] noWayOn;
        float maxSpawnW;

        struct App { public int junction; public bool signal, allWay, yield, warrant, noRtor; public float sStop; public byte group; public int channel; }
        readonly List<App> apps = new List<App>();
        readonly Dictionary<int, int> approachOf = new Dictionary<int, int>();

        readonly List<Car> pool = new List<Car>();
        readonly List<Car> live = new List<Car>();
        float firstFillUntil, nextSpawn, nextQueueCount, driftAt, drift = 1f;
        Vector2 playerPos, playerVel;
        Vector3 playerPos3;
        bool havePlayer;
        /// <summary>Something more than this above or below a car is on
        /// another level (a deck over it, a road under it): not in its way.</summary>
        const float LevelM = 3.5f;

        // ---- the tools' numbers ----------------------------------------------
        public struct Counters
        {
            public int spawned, recycled, wrecks, laneChanges, mandatory, keepRight, merges, exits,
                redStops, greenGoes, yellowGoes, ranRed, stopSignStops, stopRolls, yields, uTurns, deadEnds,
                maxQueue, leftYields, bumps, replans, slowSteps,
                rightOnRed, sneakLefts, keepClearHolds;
        }
        public Counters Stats;
        public readonly List<string> WreckLog = new List<string>();
        /// <summary>Traffic-on-traffic touches below BumpMps (the first 40).</summary>
        public readonly List<string> BumpLog = new List<string>();

        /// <summary>One line on what a car was doing (the logs).</summary>
        string Describe(Car c)
        {
            if (c == null) return "?";
            if (c.legCount == 0) return "car " + c.id + " no plan";
            ref Leg L = ref c.legs[0];
            string ctl = L.ctl == CtlSignal ? "signal" : L.ctl == CtlStop ? "stop" : L.ctl == CtlYield ? "yield" : "none";
            var e = map.edges[L.edge];
            string where = L.turn
                ? $"turn {(L.kind == -1 ? "L" : L.kind == 1 ? "R" : L.kind == 2 ? "U" : "S")}{(L.merge ? " merge" : L.exit ? " exit" : "")} at node {L.node} e{L.edge}->e{L.edge2} lanes {L.laneIn}->{L.laneOut} {c.u:0.0}/{L.len:0.0} m"
                : $"run e{L.edge} cls{e.cls}{(e.link ? "L" : "")} dir {L.dir} lane {c.lane}{(c.lc ? "->" + c.laneTo : "")} of {Lanes(L.edge, L.dir)} {c.u:0.0}/{L.len:0.0} m to {ctl}";
            return $"car {c.id} {c.speed:0.0} m/s {where} at ({c.pos.x:0},{c.pos.y:0})";
        }
        /// <summary>A movement across a node as Extend would lay it, lane by
        /// lane (the tools: what a turn path meets, why a lane lands where it
        /// does) - each in-lane's mapping, its ends against the pavement, and
        /// any collider (not road, not traffic) on the path or the first 24 m
        /// of the run after it. Its tiles must be built (EnsureRing).</summary>
        public string DescribeTurn(int node, int eIn, int eOut)
        {
            var sb = new System.Text.StringBuilder();
            if (map == null || eIn < 0 || eOut < 0 || eIn >= map.edges.Length || eOut >= map.edges.Length) return "no such edges";
            var e = map.edges[eIn]; var e2 = map.edges[eOut];
            if ((e.a != node && e.b != node) || (e2.a != node && e2.b != node)) return $"e{eIn} / e{eOut} do not meet at node {node}";
            int dir = e.b == node ? 1 : -1, dir2 = e2.a == node ? 1 : -1;
            float sA = EndS(e, dir), sB = dir2 > 0 ? Back(e2, node) : e2.length - Back(e2, node);
            float ang = Vector2.SignedAngle(TravelTan(e, dir, dir > 0 ? e.length : 0f), TravelTan(e2, dir2, dir2 > 0 ? 0f : e2.length));
            string Facts(CityMap.Edge x, int d) =>
                $"e{x.index} '{x.name}' cls{x.cls}{(x.link ? " link" : "")}{(x.oneway ? " oneway" : "")} len {x.length:0} lanes {Lanes(x.index, d)} (F{lanesF[x.index]} B{lanesB[x.index]} slots {slots[x.index]} offF {offF[x.index]}" +
                $"{(x.hasLset ? $" lset {x.lsNF}/{x.lsNB} c{x.lsCentre} sub{x.lsSub}" : " no lset")}) width {x.width:0.0} lm +{x.lmPlus:0.0}/-{x.lmMinus:0.0} sh {x.shl:0.0}/{x.shr:0.0}";
            var pn = map.nodes[node];
            sb.AppendLine($"node {node} ({pn.x:0},{pn.y:0}) degree {degree[node]} junction {nodeJunction[node]}, turn {ang:0} deg");
            sb.AppendLine("  in  " + Facts(e, dir) + $" dir {dir}, ends at s {sA:0.0}");
            sb.AppendLine("  out " + Facts(e2, dir2) + $" dir {dir2}, starts at s {sB:0.0}");
            LineModel.Extents(e2, sB, out float eM, out float eP);
            int mask = ~((1 << 2) | roadMask);
            int nIn = Lanes(eIn, dir), nOut = Lanes(eOut, dir2);
            for (int lane = 0; lane < nIn; lane++)
            {
                int req, outLane;
                float aAbs = Mathf.Abs(ang);
                if (aAbs >= 35f && aAbs <= 150f) { req = ang > 0f ? nIn - 1 : 0; outLane = ang > 0f ? nOut - 1 : 0; }
                else MapStraight(e, dir, sA, e2, dir2, sB, lane, out req, out outLane);
                var T = new Leg();
                Shape(ref T, LanePos(e, dir, req, sA), TravelTan(e, dir, sA), LanePos(e2, dir2, outLane, sB), TravelTan(e2, dir2, sB));
                var hits = new System.Text.StringBuilder();
                var seen = new HashSet<Collider>();
                for (int i = 0; i <= 28; i++)
                {
                    Vector2 p; float y;
                    if (i <= 16) { p = Bez(T, i / 16f, out _); y = Mathf.Lerp(e.YAt(sA), e2.YAt(sB), i / 16f); }
                    else
                    {
                        float s = Mathf.Clamp(sB + dir2 * (i - 16) * 2f, 0f, e2.length);
                        p = LanePos(e2, dir2, outLane, s); y = e2.YAt(s);
                    }
                    foreach (var col in Physics.OverlapBox(new Vector3(p.x, y + 0.9f, p.y), new Vector3(0.9f, 0.6f, 0.9f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore))
                        if (seen.Add(col)) hits.Append($" | {col.name} ({LayerMask.LayerToName(col.gameObject.layer)}) {(i <= 16 ? "turn " + (i * 100 / 16) + "%" : "run +" + ((i - 16) * 2) + " m")} at ({p.x:0.0},{p.y:0.0})");
                }
                sb.AppendLine($"  lane {lane}: from lane {req} to lane {outLane} of {nOut}; ends lat {LaneLat(e, dir, req, sA):0.00} -> {LaneLat(e2, dir2, outLane, sB):0.00} " +
                              $"(out pavement -{eM:0.0}..+{eP:0.0}, divider {LineModel.DividerLat(e2):0.0}), path {T.len:0.0} m{(hits.Length > 0 ? hits.ToString() : " | meets nothing")}");
            }
            return sb.ToString();
        }

        public int Active => live.Count;
        public int Target { get; private set; }
        public int Cap => cap;
        public int PoolBuilt => pool.Count;
        public float Share { get; private set; }
        public double StepMsSum { get; private set; }
        public int StepCount { get; private set; }
        public float StepMsMax { get; private set; }
        public void ResetTiming() { StepMsSum = 0; StepCount = 0; StepMsMax = 0f; }
        /// <summary>The longest stop-line queue standing now: its stop line
        /// (plan), the direction it faces, and its length in cars.</summary>
        public Vector3 QueueLine { get; private set; }
        public Vector2 QueueFacing { get; private set; }
        public int QueueNow { get; private set; }
        public bool QueueAtSignal { get; private set; }
        /// <summary>A car standing at a STOP bar now (or none), for a picture.</summary>
        public bool StopSignCar(out Vector3 at, out Vector2 facing)
        {
            foreach (var c in live)
                if (!c.wrecked && c.stopKey >= 0 && c.stopKey == c.appKey && c.goKey != c.stopKey && c.speed < 0.3f)
                { at = new Vector3(c.pos.x, c.y, c.pos.y); facing = c.fwd; return true; }
            at = default; facing = default; return false;
        }

        public struct CarView
        {
            public int id;
            public Vector3 pos;
            public Quaternion rot;
            public BoxCollider box;
            public bool wrecked, turning, lc;
            public int edge, dir;
            public float s, lat, speed, halfW;
        }
        public void Snapshot(List<CarView> into)
        {
            into.Clear();
            foreach (var c in live)
            {
                ref Leg L = ref c.legs[0];
                into.Add(new CarView
                {
                    id = c.id, pos = c.rb.position, rot = c.rb.rotation, box = c.box, wrecked = c.wrecked,
                    turning = c.legCount > 0 && L.turn, lc = c.lc,
                    edge = c.legCount > 0 ? L.edge : -1, dir = c.legCount > 0 ? L.dir : 0,
                    s = c.legCount > 0 && !L.turn ? L.s0 + L.dir * c.u : 0f, lat = c.lat, speed = c.speed, halfW = c.halfW,
                });
            }
        }

        // ======================================================================
        //  Start
        // ======================================================================
        /// <summary>The live page's switch: opened with ?traffic=1 it is on
        /// (a phone too), with ?traffic=0 off; 1 / 0, or -1 when the page does
        /// not say (and always in the editor).</summary>
        static int UrlSwitch()
        {
            if (Application.isEditor) return -1;
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            for (int i = url.IndexOf("traffic=", q < 0 ? 0 : q, System.StringComparison.OrdinalIgnoreCase); q >= 0 && i > q;
                 i = url.IndexOf("traffic=", i + 8, System.StringComparison.OrdinalIgnoreCase))
            {
                char before = url[i - 1];
                if ((before != '?' && before != '&') || i + 8 >= url.Length) continue;
                if (url[i + 8] == '1') return 1;
                if (url[i + 8] == '0') return 0;
            }
            return -1;
        }

        /// <summary>Is traffic on for this session: the page's switch, else
        /// the tools' / environment's, else the platform default.</summary>
        public static bool WantedOn()
        {
            int url = UrlSwitch();
            return url >= 0 ? url == 1 : Enabled;
        }

        /// <summary>Called by CityMode.Start in free roam (never in a race).</summary>
        public static void Attach(CityMode mode)
        {
            if (!WantedOn() || Instance != null || mode == null || mode.IsRace || mode.player == null ||
                mode.world == null || mode.world.Map == null) return;
            var go = new GameObject("CityTraffic");
            Instance = go.AddComponent<CityTraffic>();
            Instance.Init(mode);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Init(CityMode mode)
        {
            player = mode.player;
            world = mode.world;
            map = world.Map;
            trims = world.NodeTrims ?? CityWorld.SharedTrims;
            roadMask = 1 << CityWorld.RoadLayer;
            cap = CapOverride > 0 ? CapOverride : Application.isMobilePlatform ? CapPhone : CapDesktop;
            Prepare();
            firstFillUntil = Time.time + 3f;
            driftAt = Time.time;
        }

        /// <summary>The per-edge tables (lanes, limits, weights) and the
        /// junction controls, once.</summary>
        void Prepare()
        {
            int ne = map.edges.Length, nn = map.nodes.Length;
            lanesF = new byte[ne]; lanesB = new byte[ne]; slots = new byte[ne]; offF = new byte[ne];
            limitMps = new float[ne]; spawnW = new float[ne]; routable = new bool[ne];
            degree = new int[nn];
            for (int i = 0; i < nn; i++)
                foreach (int ei in map.nodeEdges[i]) if (map.edges[ei].a != map.edges[ei].b) degree[i]++;
            maxSpawnW = 0.01f;
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                int nF, nB, centre;
                if (e.hasLset) { nF = e.lsNF; nB = e.lsNB; centre = e.lsCentre; }
                else RoadProfiles.DefaultSplit(RoadProfiles.All[Mathf.Clamp(e.profile, 0, RoadProfiles.Count - 1)], out nF, out nB, out centre);
                bool twl = e.hasLset ? e.lsCentre == 2 : e.turnLane;
                if (e.oneway) { nF = Mathf.Clamp(nF > 0 ? nF : e.lanes, 1, 6); nB = 0; twl = false; }
                else { nF = Mathf.Clamp(nF, 1, 4); nB = Mathf.Clamp(nB, 1, 4); }
                int used = nF + nB + (twl ? 1 : 0);
                float band = ((e.lmPlus != 0f || e.lmMinus != 0f) ? e.lmPlus + e.lmMinus : e.width) - e.shl - e.shr;
                lanesF[i] = (byte)nF; lanesB[i] = (byte)nB;
                slots[i] = (byte)Mathf.Clamp(Mathf.Max(used, Mathf.RoundToInt(band / LaneM)), 1, 12);
                offF[i] = (byte)(e.oneway ? 0 : nB + (twl ? 1 : 0));
                float lim = e.speedKmh > 0 ? e.speedKmh / 3.6f : DefaultLimit(e.cls);
                if (e.link) lim = Mathf.Min(lim, e.cls >= 4 ? 20f : lim);
                if (e.hasLset && e.lsSub == 4) lim = Mathf.Min(lim, 7f);
                limitMps[i] = Mathf.Max(5f, lim);
                byte sub = e.hasLset ? e.lsSub : (byte)0;
                bool unpaved = e.hasLset && (e.lsFlags & 32) != 0;
                routable[i] = e.a != e.b && sub < 5 && !unpaved && e.length > 0.5f;
                bool spawnable = routable[i] && !trims.Internal(i) && e.length > 50f && !e.tunnel;
                float w = spawnable ? ClassSpawnW[Mathf.Clamp(e.cls, 0, 5)] * (e.link ? 0.5f : 1f) * (sub == 4 ? 0.2f : 1f) * (nF + nB) : 0f;
                spawnW[i] = w;
                maxSpawnW = Mathf.Max(maxSpawnW, w);
            }
            // the dead ends ahead of every directed edge (Extend's look-ahead)
            noWayOn = new bool[ne * 2];
            for (int i = 0; i < ne; i++)
            {
                if (!routable[i]) continue;
                var e = map.edges[i];
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    if (dir < 0 && e.oneway) continue;
                    int n = dir > 0 ? e.b : e.a;
                    bool on = false;
                    foreach (int oi in map.nodeEdges[n])
                    {
                        if (oi == i) continue;
                        var o = map.edges[oi];
                        if (o.a == o.b || !routable[oi]) continue;
                        if (o.oneway && o.a != n) continue;
                        on = true; break;
                    }
                    noWayOn[(i << 1) | (dir > 0 ? 1 : 0)] = !on;
                }
            }
            // the major road through every unsigned junction (CtlAt)
            majorA = new int[nn]; majorB = new int[nn];
            for (int i = 0; i < nn; i++)
            {
                majorA[i] = majorB[i] = -1;
                if (degree[i] >= 3) MajorAt(i, out majorA[i], out majorB[i]);
            }
            // the controls: every approach CitySignals stands a signal head or a STOP sign on
            nodeJunction = new int[nn];
            for (int i = 0; i < nn; i++) nodeJunction[i] = -1;
            var js = CitySignals.All(map, trims);
            if (js != null)
                foreach (var j in js)
                {
                    foreach (int n in j.nodes) nodeJunction[n] = j.id;
                    foreach (var a in j.approaches)
                    {
                        var e = map.edges[a.edge];
                        int key = (a.edge << 1) | (e.b == a.node ? 1 : 0);
                        approachOf[key] = apps.Count;
                        apps.Add(new App { junction = j.id, signal = j.signal, allWay = j.allWay, sStop = a.sStop, group = a.group, channel = a.channel,
                                           yield = !j.signal && a.ctl == 1, warrant = j.warrant, noRtor = j.NoTurnOnRed });
                    }
                }
            PrepareJunctions();
        }

        static float DefaultLimit(int cls)
        {
            switch (cls)
            {
                case 0: return 11.2f;   // 25 mph
                case 1: return 15.6f;   // 35
                case 2: return 17.9f;   // 40
                case 3: return 20.1f;   // 45
                case 4: return 24.6f;   // 55
                default: return 29.1f;  // 65
            }
        }

        int JKey(int node) => nodeJunction[node] >= 0 ? nodeJunction[node] : 65536 + node;

        // ======================================================================
        //  Lanes and poses
        // ======================================================================
        int Lanes(int edge, int dir) => dir > 0 ? lanesF[edge] : lanesB[edge];

        /// <summary>A lane's centre off the OSM line at s (+ left of a->b): the
        /// lanes' band of the line model (tapers in, aux lanes out), its slots
        /// laid out as the painter lays the line set - the b->a lanes from the
        /// left edge, the TWLTL, the a->b lanes.</summary>
        float LaneLat(CityMap.Edge e, int dir, int lane, float s)
        {
            LineModel.ExtentsBefore(e, s, out float eM, out float eP);
            float left = eP - e.shl, right = -(eM - e.shr);
            int i = e.index;
            float w = Mathf.Max(1.5f, (left - right) / slots[i]);
            int n = Lanes(i, dir);
            lane = Mathf.Clamp(lane, 0, n - 1);
            int j = dir > 0 ? offF[i] + (n - 1 - lane) : lane;
            return left - (j + 0.5f) * w;
        }

        static Vector2 Bisector(CityMap.Edge e, float s)
        {
            var t = e.TangentAt(Mathf.Max(0f, s - 0.01f)) + e.TangentAt(Mathf.Min(e.length, s + 0.01f));
            return t.sqrMagnitude > 1e-8f ? t.normalized : e.TangentAt(s);
        }

        /// <summary>The plan point at arc s, lat off the OSM line, and the
        /// edge's tangent there (a->b).</summary>
        static Vector2 PlanPoint(CityMap.Edge e, float s, float lat, out Vector2 tan)
        {
            tan = Bisector(e, s);
            return e.PointAt(s) + new Vector2(-tan.y, tan.x) * lat;
        }

        Vector2 LanePos(CityMap.Edge e, int dir, int lane, float s) =>
            PlanPoint(e, s, LaneLat(e, dir, lane, s), out _);

        static Vector2 TravelTan(CityMap.Edge e, int dir, float s) => Bisector(e, Mathf.Clamp(s, 0f, e.length)) * dir;

        /// <summary>Where a run ends short of its node: the junction's mouth
        /// (the ribbon trim), at least 2 m, at most 45% of the edge.</summary>
        float Back(CityMap.Edge e, int node) =>
            Mathf.Min(Mathf.Max(trims.TrimAt(e, node), 2f), e.length * 0.45f);

        float EndS(CityMap.Edge e, int dir) => dir > 0 ? e.length - Back(e, e.b) : Back(e, e.a);

        static Vector2 Bez(in Leg L, float t, out Vector2 d)
        {
            float m = 1f - t;
            d = 3f * m * m * (L.p1 - L.p0) + 6f * m * t * (L.p2 - L.p1) + 3f * t * t * (L.p3 - L.p2);
            return m * m * m * L.p0 + 3f * m * m * t * L.p1 + 3f * m * t * t * L.p2 + t * t * t * L.p3;
        }

        static float BezLen(in Leg L)
        {
            float len = 0f;
            Vector2 prev = L.p0;
            for (int i = 1; i <= 8; i++)
            {
                var p = Bez(L, i / 8f, out _);
                len += Vector2.Distance(prev, p);
                prev = p;
            }
            return Mathf.Max(0.1f, len);
        }

        /// <summary>A circular-arc cubic between two lane points and their
        /// travel tangents (control length 2/3 tan(a/4)/sin(a/2) of the chord;
        /// a third of it on a straight).</summary>
        static void Shape(ref Leg L, Vector2 p0, Vector2 t0, Vector2 p3, Vector2 t3)
        {
            float c = Vector2.Distance(p0, p3);
            float a = Mathf.Clamp(Mathf.Abs(Vector2.SignedAngle(t0, t3)), 1f, 179f) * Mathf.Deg2Rad;
            float k = c * (2f / 3f) * Mathf.Tan(a * 0.25f) / Mathf.Sin(a * 0.5f);
            k = Mathf.Clamp(k, 0.5f, c + 4f);
            L.p0 = p0; L.p1 = p0 + t0 * k; L.p2 = p3 - t3 * k; L.p3 = p3;
            L.len = BezLen(L);
        }

        // ======================================================================
        //  The plan
        // ======================================================================
        Leg MakeRun(int ei, int dir, float s0, int lane)
        {
            var e = map.edges[ei];
            var L = new Leg { turn = false, edge = ei, dir = (sbyte)dir, s0 = s0, s1 = EndS(e, dir), laneIn = lane, laneOut = lane };
            if ((L.s1 - L.s0) * dir < 0f) L.s1 = L.s0;
            L.len = Mathf.Abs(L.s1 - L.s0);
            L.node = dir > 0 ? e.b : e.a;
            L.key = (ei << 1) | (dir > 0 ? 1 : 0);
            L.app = -1;
            L.ctl = CtlAt(e, dir, out L.app);
            return L;
        }

        /// <summary>What controls the end of a run: the CitySignals approach
        /// (a signal or a STOP), else a minor road at a real junction yields.</summary>
        byte CtlAt(CityMap.Edge e, int dir, out int app)
        {
            int key = (e.index << 1) | (dir > 0 ? 1 : 0);
            if (approachOf.TryGetValue(key, out app)) return apps[app].signal ? CtlSignal : apps[app].yield ? CtlYield : CtlStop;
            app = -1;
            int n = dir > 0 ? e.b : e.a;
            if (degree[n] < 3 || nodeJunction[n] >= 0 || trims.mitre[n] || trims.Internal(e.index)) return CtlNone;
            int topCls = -1;
            foreach (int ei in map.nodeEdges[n])
            {
                if (ei == e.index) continue;
                var o = map.edges[ei];
                if (o.a == o.b) continue;
                topCls = Mathf.Max(topCls, o.link ? 0 : o.cls);
            }
            // a ramp at the freeway: the merge's own rules
            if (e.link && topCls >= 4) return CtlNone;
            // THE MAJOR ROAD has the way (MajorAt). 2026-10-29: before, an arm
            // yielded to ANY other arm of its own rank - so a through street
            // yielded to its own continuation, and an arterial gave way to a
            // side street on its right at every unsigned T. Everyone else
            // yields; where no one road goes through (two equal roads
            // crossing) all yield, the car on the right first (CanGo).
            return majorA[n] == e.index || majorB[n] == e.index ? CtlNone : CtlYield;
        }

        /// <summary>The major road through an unsigned junction: the two arms
        /// that carry on from each other (within 40 deg, or within 80 under
        /// one name - a through road that bends at a T) with the highest rank
        /// between them, one of them of the node's top rank; else the single
        /// arm of the top rank. None (-1) where that is ambiguous: two through
        /// pairs of one rank (equal roads crossing), or several top-rank arms
        /// and no straight pair.</summary>
        void MajorAt(int n, out int m1, out int m2)
        {
            m1 = m2 = -1;
            // a CARRIAGEWAY SPLIT is not a junction (CitySignals.SplitArms): its
            // two-way arm and arriving carriageway both have the way, a side road yields
            if (CitySignals.SplitArms(map, n, out int sTwo, out int sIn, out _)) { m1 = sTwo; m2 = sIn; return; }
            var arms = map.nodeEdges[n];
            int top = -1, topN = 0, topArm = -1;
            foreach (int ei in arms)
            {
                var e = map.edges[ei];
                if (e.a == e.b || !routable[ei]) continue;
                int r = Rank(e);
                if (r > top) { top = r; topN = 1; topArm = ei; }
                else if (r == top) topN++;
            }
            if (top < 0) return;
            int bestSum = -1, pairs = 0, namedPairs = 0;
            float bestAng = 999f;
            bool bestNamed = false;
            for (int i = 0; i < arms.Count; i++)
            {
                var a = map.edges[arms[i]];
                if (a.a == a.b || !routable[arms[i]]) continue;
                for (int j = i + 1; j < arms.Count; j++)
                {
                    var b = map.edges[arms[j]];
                    if (arms[j] == arms[i] || b.a == b.b || !routable[arms[j]]) continue;
                    if (Rank(a) != top && Rank(b) != top) continue;
                    // a road carries on straight (40 deg) - or, by its name, through a bend (80)
                    bool named = !string.IsNullOrEmpty(a.name) && a.name == b.name;
                    float ang = Vector2.Angle(-ArmOut(a, n), ArmOut(b, n));
                    if (ang > (named ? 80f : 40f)) continue;
                    int sum = Rank(a) + Rank(b);
                    if (sum > bestSum) { bestSum = sum; pairs = 1; namedPairs = named ? 1 : 0; bestAng = ang; bestNamed = named; m1 = arms[i]; m2 = arms[j]; }
                    else if (sum == bestSum)
                    {
                        pairs++;
                        if (named) namedPairs++;
                        if ((named && !bestNamed) || (named == bestNamed && ang < bestAng)) { bestAng = ang; bestNamed = named; m1 = arms[i]; m2 = arms[j]; }
                    }
                }
            }
            if (pairs == 1 || (bestNamed && namedPairs == 1)) return;
            m1 = m2 = -1;
            if (pairs == 0 && topN == 1) m1 = topArm;
        }

        static int Rank(CityMap.Edge e) => e.link ? 2 : e.cls * 2 + 1;

        /// <summary>The way out of node n along edge e.</summary>
        static Vector2 ArmOut(CityMap.Edge e, int n) => e.a == n ? e.TangentAt(0f) : -e.TangentAt(e.length);

        float Remaining(Car c)
        {
            float r = -c.u;
            for (int k = 0; k < c.legCount; k++) r += c.legs[k].len;
            return r;
        }

        void EnsurePlan(Car c)
        {
            float look = Mathf.Max(150f, c.speed * 7f);
            int guard = 0;
            while (!c.deadEnd && c.legCount <= MaxLegs - 2 && Remaining(c) < look && guard++ < 4)
                if (!Extend(c))
                {
                    c.deadEnd = true; Stats.deadEnds++;
                    if (Log != null)
                    {
                        int dn = c.legs[c.legCount - 1].node;
                        bool cutEnd = c.offMap || AtMapEdge(dn, map.edges[c.legs[c.legCount - 1].edge].cls);
                        Note(c, "route", cutEnd ? "dead-end-map-edge" : "dead-end", 1f, (cutEnd ? "the road is cut where the data stops (it drives on off the map): " : "no legal way on at the end of its plan (a one-way into nothing): ") + Describe(c), node: dn);
                    }
                }
        }

        static readonly List<(int e, int dir, float w, float ang)> options = new List<(int, int, float, float)>(8);

        /// <summary>The next turn and run after the plan's last run: a random
        /// legal way on, weighted by class and by going straight.</summary>
        bool Extend(Car c)
        {
            int li = c.legCount - 1;
            if (li < 0 || c.legs[li].turn) return false;
            var e = map.edges[c.legs[li].edge];
            int dir = c.legs[li].dir;
            int n = dir > 0 ? e.b : e.a;
            Vector2 tIn = TravelTan(e, dir, dir > 0 ? e.length : 0f);
            options.Clear();
            float total = 0f;
            bool anyMainline = false;
            foreach (int ei in map.nodeEdges[n])
            {
                var o = map.edges[ei];
                if (o.a == o.b || !routable[ei]) continue;
                int d2 = o.a == n ? 1 : -1;
                if (d2 < 0 && o.oneway) continue;
                bool reverse = ei == e.index;
                Vector2 tOut = TravelTan(o, d2, d2 > 0 ? 0f : o.length);
                float ang = Vector2.SignedAngle(tIn, tOut);
                float aa = Mathf.Abs(ang);
                float w = reverse || aa > 150f ? 0.001f : aa < 35f ? 4f : 1.2f;
                w *= ClassRouteW[Mathf.Clamp(o.cls, 0, 5)];
                if (o.cls == e.cls && o.link == e.link) w *= 1.5f;
                if (!e.link && o.link && e.cls >= 4) w *= 0.3f;
                if (o.hasLset && o.lsSub == 4) w *= 0.2f;
                int far = d2 > 0 ? o.b : o.a;
                // a dead end ahead (nothing on but back), or the data's edge
                if ((degree[far] <= 1 || noWayOn[(ei << 1) | (d2 > 0 ? 1 : 0)]) && o.length < 300f) w *= 0.05f;
                if (!reverse && !o.link && aa < 35f) anyMainline = true;
                if (CensusMode) w *= CensusRouteBonus(ei, o);
                options.Add((ei, d2, w, ang));
                total += w;
            }
            // THE DATA'S EDGE (CityMap.nodeFrontier): where the map stops and
            // the road goes on, nothing on but back is the road leaving the
            // map - the car drives on off it (Cull takes it at the end)
            // rather than stopping there or turning round in the road.
            if (map.Frontier(n))
            {
                bool forward = false;
                foreach (var op in options) if (op.e != e.index && Mathf.Abs(op.ang) <= 150f) { forward = true; break; }
                if (!forward) { c.offMap = true; return false; }
            }
            if (options.Count == 0) return false;
            ref Leg last = ref c.legs[li];
            int refLane = li == 0 ? (c.lc ? c.laneTo : c.lane) : last.laneIn;
            int e2i, dir2, req, outLane;
            float angle, sA, sB;
            sbyte kind;
            bool exit, merge;
            CityMap.Edge e2;
            for (int attempt = 0; ; attempt++)
            {
                float r = Random.value * total;
                int pick = -1;
                for (int i = 0; i < options.Count; i++) { if (options[i].w <= 0f) continue; pick = i; r -= options[i].w; if (r <= 0f) break; }
                if (pick < 0) pick = 0;
                (e2i, dir2, _, angle) = options[pick];
                e2 = map.edges[e2i];

                float aAbs = Mathf.Abs(angle);
                kind = e2i == e.index || aAbs > 150f ? (sbyte)2 : aAbs < 35f ? (sbyte)0 : angle > 0f ? (sbyte)-1 : (sbyte)1;
                exit = kind == 0 && !e.link && e2.link && anyMainline && e.cls >= 4;
                merge = kind == 0 && e.link && !e2.link && e2.cls >= 4;

                // where the turn starts and ends: the junction's mouths, or a
                // long run through a freeway merge or diverge
                sA = last.s1;
                if (merge) sA = dir > 0 ? e.length - ZoneBack(e, dir) : ZoneBack(e, dir);
                if (exit) sA = dir > 0 ? Mathf.Max(e.length - 25f, e.length * 0.4f) : Mathf.Min(25f, e.length * 0.6f);
                if (merge) sB = dir2 > 0 ? Mathf.Min(25f, e2.length * 0.6f) : Mathf.Max(e2.length - 25f, e2.length * 0.4f);
                else if (exit) sB = dir2 > 0 ? ZoneBack(e2, -dir2) : e2.length - ZoneBack(e2, -dir2);
                else sB = dir2 > 0 ? Back(e2, n) : e2.length - Back(e2, n);
                // never behind where the run started
                if ((sA - last.s0) * dir < 0f) sA = last.s0;

                // the lanes: the kerb lane to turn right, the inside lane to
                // turn left, the lane that runs on going straight (one that
                // ends merges)
                int nIn = Lanes(e.index, dir), nOut = Lanes(e2i, dir2);
                bool rampRight = exit ? RampOnRight(e2, dir2, n, TravelTan(e, dir, dir > 0 ? e.length : 0f), angle <= 0f) : angle <= 0f;
                if (kind == 1) { req = 0; outLane = 0; }
                else if (kind == -1 || kind == 2) { req = nIn - 1; outLane = nOut - 1; }
                else if (exit) { req = rampRight ? 0 : nIn - 1; outLane = rampRight ? 0 : nOut - 1; }
                else if (merge)
                {
                    bool fromRight = RampOnRight(e, dir, n, TravelTan(e2, dir2, dir2 > 0 ? 0f : e2.length), angle >= 0f);
                    req = fromRight ? 0 : nIn - 1; outLane = fromRight ? 0 : nOut - 1;
                }
                else MapStraight(e, dir, sA, e2, dir2, sB, refLane, out req, out outLane);
                // A REPLAN (the lane change its turn needed never found a gap):
                // only a way on from the lane it is in
                if (!c.keepLane || li != 0 || req == refLane || attempt >= options.Count) break;
                total -= options[pick].w;
                var op = options[pick]; op.w = 0f; options[pick] = op;
                if (total <= 1e-5f) break;
            }
            c.keepLane = false;
            last.s1 = sA;
            last.len = Mathf.Abs(last.s1 - last.s0);
            last.laneOut = req;

            var T = new Leg
            {
                turn = true, edge = e.index, dir = (sbyte)dir, edge2 = e2i, dir2 = (sbyte)dir2, node = n,
                laneIn = req, laneOut = outLane, kind = kind, merge = merge, exit = exit, app = -1, key = -1,
            };
            Vector2 p0 = LanePos(e, dir, req, sA), t0 = TravelTan(e, dir, sA);
            Vector2 p3 = LanePos(e2, dir2, outLane, sB), t3 = TravelTan(e2, dir2, sB);
            Shape(ref T, p0, t0, p3, t3);
            T.y0 = e.YAt(sA); T.y1 = e2.YAt(sB);
            float turnA = Mathf.Abs(Vector2.SignedAngle(t0, t3)) * Mathf.Deg2Rad;
            float vLim = Mathf.Min(limitMps[e.index], limitMps[e2i]);
            if (kind == 2) T.vMax = 3f;
            else if (turnA < 0.17f) T.vMax = vLim;
            else
            {
                float chord = Vector2.Distance(p0, p3);
                float R = chord / Mathf.Max(0.05f, 2f * Mathf.Sin(turnA * 0.5f));
                T.vMax = Mathf.Clamp(Mathf.Sqrt(ALat * R), 4f, vLim);
            }
            c.legs[c.legCount++] = T;
            c.legs[c.legCount++] = MakeRun(e2i, dir2, sB, outLane);
            return true;
        }

        /// <summary>Which side of the freeway a ramp lies on: the ramp's own
        /// point 40 m from the node (or half its length) against the freeway's
        /// line through the node. 2026-10-29: the old test, the sign of the
        /// angle between the end tangents, is a coin toss on a near-parallel
        /// merge or diverge - the census saw ramps sent to the FAR lane, across
        /// the freeway, and the mainline hit (node 690). The angle decides
        /// only when the ramp lies on the line (under half a metre off).</summary>
        bool RampOnRight(CityMap.Edge ramp, int rampDir, int n, Vector2 fwyTan, bool fallback)
        {
            float z = Mathf.Min(40f, ramp.length * 0.5f);
            bool arrives = (rampDir > 0 ? ramp.b : ramp.a) == n;
            float s = arrives ? (rampDir > 0 ? ramp.length - z : z) : (rampDir > 0 ? z : ramp.length - z);
            Vector2 d = ramp.PointAt(s) - map.nodes[n];
            float cross = fwyTan.x * d.y - fwyTan.y * d.x;
            if (Mathf.Abs(cross) < 0.5f) return fallback;
            return cross < 0f;
        }

        /// <summary>How far back from its end (dir: the way it is driven) a
        /// ramp still runs within 12 degrees of its end heading: the stretch a
        /// merge (or, from the start, a diverge) is driven over. 20-110 m.</summary>
        static float ZoneBack(CityMap.Edge e, int dir)
        {
            float cap = Mathf.Min(110f, e.length * 0.8f);
            if (cap <= 20f) return Mathf.Max(2f, cap);
            Vector2 tEnd = dir > 0 ? e.TangentAt(e.length) : e.TangentAt(0f);
            float d = 0f;
            for (float x = 5f; x <= cap; x += 5f)
            {
                float s = dir > 0 ? e.length - x : x;
                if (Vector2.Angle(e.TangentAt(s), tEnd) > 12f) break;
                d = x;
            }
            return Mathf.Clamp(d, 20f, cap);
        }

        /// <summary>Going straight on: the lane on the next edge nearest
        /// (across the road) to the lane the car is in; where that lane ENDS
        /// (nothing within 2 m), the nearest lane that runs on instead.</summary>
        void MapStraight(CityMap.Edge e, int dir, float sA, CityMap.Edge e2, int dir2, float sB, int refLane, out int req, out int outLane)
        {
            int nIn = Lanes(e.index, dir), nOut = Lanes(e2.index, dir2);
            refLane = Mathf.Clamp(refLane, 0, nIn - 1);
            Vector2 t0 = TravelTan(e, dir, sA);
            Vector2 nrm = new Vector2(-t0.y, t0.x);
            req = refLane; outLane = Mathf.Clamp(refLane, 0, nOut - 1);
            float bestScore = float.MaxValue;
            for (int i = 0; i < nIn; i++)
            {
                Vector2 p = LanePos(e, dir, i, sA);
                int bj = 0; float bd = float.MaxValue;
                for (int j = 0; j < nOut; j++)
                {
                    float d = Mathf.Abs(Vector2.Dot(LanePos(e2, dir2, j, sB) - p, nrm));
                    if (d < bd) { bd = d; bj = j; }
                }
                if (bd > 2.0f) continue;
                float score = Mathf.Abs(i - refLane) * 10f + bd;
                if (score < bestScore) { bestScore = score; req = i; outLane = bj; }
            }
        }

        // ======================================================================
        //  The loop
        // ======================================================================
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        void FixedUpdate()
        {
            if (player == null || map == null) return;
            clock.Restart();
            float dt = Time.fixedDeltaTime;
            var body = player.Body;
            var pp = body != null ? body.position : player.transform.position;
            // the player's velocity from where it was a step ago: a body moved
            // by a tool (or a teleport) reads true too
            var pv = havePlayer && dt > 0f ? (pp - playerPos3) / dt : Vector3.zero;
            if (pv.sqrMagnitude > 90f * 90f) pv = Vector3.zero;
            havePlayer = true;
            playerPos3 = pp;
            playerPos = new Vector2(pp.x, pp.z);
            playerVel = new Vector2(pv.x, pv.z);
            double now = Time.timeAsDouble;

            for (int i = 0; i < live.Count; i++) if (!live[i].wrecked) Facts(live[i]);
            for (int i = 0; i < live.Count; i++)
            {
                var c = live[i];
                if (c.wrecked)
                {
                    // where physics has it now: an obstacle for the rest
                    var wp = c.rb.position;
                    c.pos = new Vector2(wp.x, wp.z);
                    SpinWheels(c, Vector3.Dot(c.rb.linearVelocity, c.go.transform.forward), dt);
                    continue;
                }
                Drive(c, dt, now);
            }
            // OFF THE MAP: a car whose plan ends at the data's frontier leaves
            // in the step it reaches the end, at speed - left to Cull (once a
            // frame, many steps at a census's time scale) it stood frozen at
            // the end and the next car ran into it (the first spot census:
            // 56 same-road wrecks at I-77's, Wilkinson's, I-85's ends)
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i];
                if (c.offMap && !c.wrecked && Remaining(c) < 0.5f + c.speed * dt) Recycle(i);
            }
            if (Log != null) CensusStep(dt);
            clock.Stop();
            float ms = (float)clock.Elapsed.TotalMilliseconds;
            StepMsSum += ms; StepCount++;
            if (ms > StepMsMax) StepMsMax = ms;
            if (ms > 5f) Stats.slowSteps++;
        }

        /// <summary>What each car is coming to, this step: the junction at the
        /// end of its run, how far, from which way, and what it turns there; or
        /// the junction it is in.</summary>
        void Facts(Car c)
        {
            c.appJ = -1; c.appKey = -1; c.appIdx = -1; c.boxJ = -1; c.turnNext = 0; c.appCtl = CtlNone;
            if (c.legCount == 0) return;
            ref Leg L = ref c.legs[0];
            if (L.turn)
            {
                c.boxJ = JKey(L.node);
                c.boxKind = L.kind;
                c.boxU = (L.p1 - L.p0).sqrMagnitude > 1e-6f ? (L.p1 - L.p0).normalized : c.tan;
                c.inKey = (L.edge << 1) | (L.dir > 0 ? 1 : 0);
                if (c.legCount > 1)
                {
                    ref Leg R = ref c.legs[1];
                    c.appJ = JKey(R.node); c.appKey = R.key; c.appIdx = R.app; c.appCtl = R.ctl;
                    c.appDist = L.len - c.u + LineAt(R) - c.halfL;
                    c.appU = TravelTan(map.edges[R.edge], R.dir, R.s1);
                    c.turnNext = c.legCount > 2 ? c.legs[2].kind : (sbyte)0;
                }
                return;
            }
            c.inKey = L.key;
            c.appJ = JKey(L.node); c.appKey = L.key; c.appIdx = L.app; c.appCtl = L.ctl;
            c.appDist = LineAt(L) - c.u - c.halfL;
            c.appU = TravelTan(map.edges[L.edge], L.dir, L.s1);
            c.turnNext = c.legCount > 1 ? c.legs[1].kind : (sbyte)0;
            if (trims.Internal(L.edge)) { c.boxJ = c.appJ; c.boxKind = c.turnNext; c.boxU = c.tan; }
        }

        /// <summary>Metres from a run's start to its stop line (its end when
        /// the approach has no bar).</summary>
        float LineAt(in Leg L) => L.app >= 0 ? Mathf.Min(Mathf.Abs(apps[L.app].sStop - L.s0), L.len) : L.len;

        /// <summary>A run that ends merging (a one-way slip or ramp joining
        /// at a shallow angle: its yield wants a gap in one lane, not a crossing).</summary>
        bool MergeArm(in Leg L)
        {
            var e = map.edges[L.edge];
            return e.oneway && (e.link || string.IsNullOrEmpty(e.name));
        }

        void Drive(Car c, float dt, double now)
        {
            // WAS IT HIT? (TrafficSystem.Drive's test: only a contact changes
            // this body's velocity between steps.)
            float knock = c.hasCommand ? (c.rb.linearVelocity - c.commanded).magnitude : 0f;
            if (knock > WreckSpeed)
            {
                bool bump = knock < BumpMps && c.lastTraffic != null && Time.time - c.lastTrafficAt < 0.3f;
                if (!bump)
                {
                    Wreck(c, (c.lastContact ?? "?") + ", knocked " + knock.ToString("0.0") + " m/s; " + Describe(c) +
                             (c.lastTraffic != null && Time.time - c.lastTrafficAt < 0.3f ? " | other: " + Describe(c.lastTraffic) : ""));
                    return;
                }
                if (Time.time - c.lastBumpAt > 2f)
                {
                    Stats.bumps++;
                    if (Log != null) Note(c, "contact", "car-bump", knock, "a bump below the wreck line: " + Describe(c) + " | other: " + Describe(c.lastTraffic));
                    if (BumpLog.Count < 40) BumpLog.Add(knock.ToString("0.0") + " m/s: " + Describe(c) + " | other: " + Describe(c.lastTraffic));
                }
                c.lastBumpAt = Time.time;
            }
            if (c.legCount == 0) { Place(c, dt, false); return; }

            // the wanted speed (bends, limits ahead) every third step
            if (c.vTick-- <= 0) { c.vTick = 2; c.vDesCached = DesiredSpeed(c); }
            float vDes = c.vDesCached;
            float gap = LeaderGap(c, out float vL);
            byte why = gap < float.MaxValue ? c.leadKind : (byte)0;
            float g2 = ControlGap(c, now);
            if (g2 < gap) { gap = g2; vL = 0f; why = (byte)(10 + c.ctlWhy); }
            if (c.deadEnd && !c.offMap)
            {
                float end = Remaining(c) - c.halfL - 1f;
                if (end < gap) { gap = end; vL = 0f; why = 20; }
            }
            // the lane its turn needs not reached yet: wait short of the
            // junction for a gap rather than cut across a lane in the turn
            ref Leg L0 = ref c.legs[0];
            if (!L0.turn && !c.lc && c.legCount > 1 && Mathf.Clamp(L0.laneOut, 0, Lanes(L0.edge, L0.dir) - 1) != c.lane)
            {
                float rem = L0.len - c.u;
                if (rem < 40f)
                {
                    float hold = rem - c.halfL - 6f + StopS0;
                    if (hold < gap) { gap = Mathf.Max(0.05f, hold); vL = 0f; why = 21; }
                    if (c.mandAt < 0f && c.speed < 0.5f) c.mandAt = Time.time;
                }
            }
            else c.mandAt = -1f;
            c.gap = gap;
            c.why = why;

            // IDM
            float v = c.speed;
            float free = 1f - Mathf.Pow(Mathf.Clamp(v / Mathf.Max(0.5f, vDes), 0f, 3f), 4f);
            float acc = c.aMax * Mathf.Max(-2f, free);
            if (gap < 250f)
            {
                float sStar = StopS0 + Mathf.Max(0f, v * c.timeGap + v * (v - vL) / (2f * Mathf.Sqrt(c.aMax * c.bDec)));
                float g = Mathf.Max(gap, 0.05f);
                acc -= c.aMax * (sStar / g) * (sStar / g);
            }
            acc = Mathf.Clamp(acc, -EmergencyDecel, c.aMax);
            if (gap <= 0.05f) acc = -EmergencyDecel;
            c.accel = acc;
            c.speed = Mathf.Max(0f, v + acc * dt);

            c.u += c.speed * dt;
            int guard = 0;
            while (c.legCount > 0 && c.u >= c.legs[0].len && guard++ < 4)
            {
                if (c.legCount == 1) { c.u = c.legs[0].len; break; }
                c.u -= c.legs[0].len;
                PopLeg(c);
            }
            EnsurePlan(c);
            LaneChange(c, dt);
            Place(c, dt, false);
            if (c.lights != null) c.lights.externalBrake = acc < -1.2f || (c.speed < 0.3f && gap < 12f);
        }

        void PopLeg(Car c)
        {
            ref Leg old = ref c.legs[0];
            if (old.turn)
            {
                if (old.merge) Stats.merges++;
                if (old.exit) Stats.exits++;
                if (old.kind == 2) Stats.uTurns++;
                if (Log != null) CensusTurnDone(c, ref old);
            }
            for (int k = 1; k < c.legCount; k++) c.legs[k - 1] = c.legs[k];
            c.legCount--;
            ref Leg N = ref c.legs[0];
            if (N.turn)
            {
                if (Log != null && (c.lc || c.lane != N.laneIn)) CensusWrongLaneTurn(c, ref N);
                // the turn starts where the car IS (a lane change still under
                // way, a lane that ended): the same arc to the same lane
                Shape(ref N, c.pos, c.tan, N.p3, (N.p3 - N.p2).sqrMagnitude > 1e-6f ? (N.p3 - N.p2).normalized : c.tan);
                if (c.u > N.len) c.u = N.len;
            }
            else
            {
                c.lane = N.laneIn; c.lc = false;
            }
            c.committed = -1; c.stopKey = -1; c.goKey = -1; c.waitKey = -1;
        }

        // ---- speed -----------------------------------------------------------
        /// <summary>The speed this driver wants now: its share of each leg's
        /// limit, bends (a turn's arc; a run's polyline sampled ahead), with the
        /// braking to reach each in time.</summary>
        float DesiredSpeed(Car c)
        {
            float best = float.MaxValue;
            float dist = -c.u;
            for (int k = 0; k < c.legCount && dist < 180f; k++)
            {
                ref Leg L = ref c.legs[k];
                float lim = L.turn ? Mathf.Min(L.vMax, limitMps[L.edge2] * c.factor) : limitMps[L.edge] * c.factor;
                float d0 = Mathf.Max(0f, dist);
                best = Mathf.Min(best, Mathf.Sqrt(lim * lim + 2f * c.bDec * d0));
                if (!L.turn && L.len > 8f && (k == 0 || dist < 80f))
                {
                    var e = map.edges[L.edge];
                    float from = k == 0 ? c.u : 0f;
                    for (float x = from; x < L.len - 4f && x < from + 60f; x += 15f)
                    {
                        float s = L.s0 + L.dir * x;
                        float s2 = Mathf.Clamp(s + L.dir * 8f, 0f, e.length);
                        float kap = Vector2.Angle(e.TangentAt(s), e.TangentAt(s2)) * Mathf.Deg2Rad / 8f;
                        if (kap < 1e-3f) continue;
                        float vc = Mathf.Sqrt(ALat / kap);
                        best = Mathf.Min(best, Mathf.Sqrt(vc * vc + 2f * c.bDec * Mathf.Max(0f, dist + x)));
                    }
                }
                dist += L.len;
            }
            return Mathf.Max(0.5f, best);
        }

        // ---- what is ahead -----------------------------------------------------
        static readonly float[] cumC = new float[MaxLegs + 1];

        static bool SameLeg(in Leg a, in Leg b)
        {
            if (a.turn != b.turn || a.edge != b.edge || a.dir != b.dir) return false;
            if (!a.turn) return true;
            return a.edge2 == b.edge2 && a.dir2 == b.dir2 && (a.laneOut == b.laneOut || a.laneIn == b.laneIn);
        }

        /// <summary>Two turns out of ONE lane (one goes on, one turns left):
        /// one path until they part, so the follower keeps behind the leader
        /// while the two are side by side within a car's width (2026-10-29:
        /// the check saw a straight-on and a left out of one lane overlap in
        /// the box at node 2301 - SameLeg counts a turn as one leg only to the
        /// same road out).</summary>
        static bool OneLaneOut(Car c, int k, Car o, int j)
        {
            ref Leg a = ref c.legs[k];
            ref Leg b = ref o.legs[j];
            if (!a.turn || !b.turn || a.edge != b.edge || a.dir != b.dir || a.node != b.node || a.laneIn != b.laneIn) return false;
            Vector2 d = o.pos - c.pos;
            return Mathf.Abs(c.fwd.x * d.y - c.fwd.y * d.x) < c.halfW + o.halfW + 0.3f;
        }

        /// <summary>Do the two cars use a lane in common on these legs (runs:
        /// the lane it is in, both while changing; a later run the lane it
        /// arrives in)?</summary>
        static bool LanesMeet(Car c, int k, Car o, int j)
        {
            if (c.legs[k].turn) return true;
            int a0, a1, b0, b1;
            if (k == 0) { a0 = c.lc ? c.laneFrom : c.lane; a1 = c.lc ? c.laneTo : c.lane; } else { a0 = a1 = c.legs[k].laneIn; }
            if (j == 0) { b0 = o.lc ? o.laneFrom : o.lane; b1 = o.lc ? o.laneTo : o.lane; } else { b0 = b1 = o.legs[j].laneIn; }
            return a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1;
        }

        /// <summary>How the stream entering a leg ranks at a merge: a ramp
        /// joining a freeway 0, a turn 1, straight on 2.</summary>
        static int StreamRank(in Leg pred) => pred.turn ? (pred.merge ? 0 : pred.kind == 0 ? 2 : 1) : 2;

        /// <summary>The bumper-to-bumper gap to what is ahead on this car's
        /// plan, and its speed along it.</summary>
        float LeaderGap(Car c, out float vL)
        {
            float best = float.MaxValue; vL = 0f;
            c.leaderInLane = false;
            c.leadKind = 0;
            int kc = Mathf.Min(c.legCount, 5);
            cumC[0] = -c.u;
            for (int k = 0; k < kc; k++) cumC[k + 1] = cumC[k] + c.legs[k].len;
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c) continue;
                Vector2 d = o.pos - c.pos;
                float d2 = d.sqrMagnitude;
                if (d2 > 170f * 170f) continue;
                if (o.wrecked)
                {
                    float b0 = best;
                    if (Mathf.Abs(o.rb.position.y - c.y) < LevelM) Obstacle(c, o.pos, 2.4f, Vector2.zero, ref best, ref vL);
                    if (best < b0) c.leadKind = 2;
                    continue;
                }
                bool known = false;
                float oc = -o.u;
                int jo = Mathf.Min(o.legCount, 3);
                for (int j = 0; j < jo && !known; j++)
                {
                    for (int k = 0; k < kc; k++)
                    {
                        if (!SameLeg(c.legs[k], o.legs[j]) && !OneLaneOut(c, k, o, j)) continue;
                        known = true;
                        if (!LanesMeet(c, k, o, j)) break;
                        // o's place on the leg less c's: on a RUN by arc position
                        // (two cars' runs on one edge start where each joined it
                        // - a car born mid-edge starts there), on a turn by the
                        // metres into it
                        float gap;
                        if (!c.legs[k].turn)
                        {
                            ref Leg lc = ref c.legs[k];
                            ref Leg lo = ref o.legs[j];
                            float sc = k == 0 ? lc.s0 + lc.dir * c.u : lc.s0 - lc.dir * cumC[k];
                            float so = j == 0 ? lo.s0 + lo.dir * o.u : lo.s0 - lo.dir * oc;
                            gap = (so - sc) * lc.dir;
                        }
                        else gap = cumC[k] - oc;
                        float lengths = c.halfL + o.halfL;
                        if (k >= 1 && j >= 1 && !SameLeg(c.legs[k - 1], o.legs[j - 1]))
                        {
                            // CONVERGING on this leg from different ways (a ramp,
                            // a turn). Both cars decide who goes first by the same
                            // rule: the higher stream (straight on > a turn > a ramp
                            // joining a freeway) unless the lower one is clearly
                            // ahead; equal streams, whoever gets there first. The
                            // second yields short of the leg; the first ignores it.
                            float dC = cumC[k], dO = oc;
                            if (dC > 120f && dO > 120f) break;
                            int rc = StreamRank(c.legs[k - 1]), ro = StreamRank(o.legs[j - 1]);
                            float tC = dC / Mathf.Max(c.speed, 2f), tO = dO / Mathf.Max(o.speed, 2f);
                            bool cFirst;
                            if (rc == ro) cFirst = tC < tO || (Mathf.Approximately(tC, tO) && c.id < o.id);
                            else if (rc > ro) cFirst = !(gap - lengths > 6f + c.speed * 0.8f || tC - tO > 2.5f);
                            else cFirst = -gap - lengths > 6f + o.speed * 0.8f || tO - tC > 2.5f;
                            if (cFirst) break;
                            gap = Mathf.Max(gap, lengths + 1.5f);
                        }
                        else if (gap <= 0f) break;
                        float g = gap - lengths;
                        if (g < best)
                        {
                            best = g; vL = o.speed;
                            c.leaderInLane = k == 0 && j == 0;
                            c.leaderV = o.speed;
                            c.leadKind = 1;
                        }
                        break;
                    }
                    oc += o.legs[j].len;
                }
                if (known || d2 > 90f * 90f || Mathf.Abs(o.y - c.y) > LevelM) continue;
                // not on this car's plan: its neighbour lanes and the oncoming
                // lanes of its own run are kept apart by the lanes
                if (o.legCount > 0 && !o.legs[0].turn && c.legCount > 0 && !c.legs[0].turn && o.legs[0].edge == c.legs[0].edge) continue;
                float dot = Vector2.Dot(o.fwd, c.fwd);
                bool crossing = c.legs[0].turn || o.legCount > 0 && o.legs[0].turn || Mathf.Abs(dot) < 0.85f;
                if (crossing)
                {
                    // something ACROSS its path - a car in the junction, one
                    // crossing it; of two that would each stop for the other,
                    // the one in the box (then the moving one, then the lower
                    // id) goes, so they never wait for each other
                    if (d2 > 40f * 40f) continue;
                    float g = CrossGap(c, o, d);
                    if (g == float.MaxValue) continue;
                    // c goes first and o, still moving, waits for it; a car standing
                    // with its nose in the lane is braked for whoever it is (the
                    // census: a ramp car stopped at a stop line inside Sunset
                    // Road, run into by the car that "went first")
                    if (o.speed > 1.5f && CrossGap(o, c, -d) < float.MaxValue && Precedes(c, o)) continue;
                    if (g < best) { best = g; vL = Mathf.Max(0f, dot * o.speed); c.leadKind = 4; }
                }
                else if (dot > 0f)
                {
                    // A CAR ON ANOTHER ROAD IN THIS CAR'S LANE AHEAD, going its
                    // way (2026-10-29 census, road-logic topology): where two
                    // roads' lanes run together short of or past the node they
                    // share - a ramp laid beside lane 0 before the merge node, an
                    // exit just past the diverge, the last metres of two
                    // approaches meeting at a sharp angle, the first metres of a
                    // run past a node. All 98 car-on-car wrecks the census filed
                    // as "overlapping roads" or "roads crossing with no node"
                    // were these, on edges sharing a node (none on roads laid
                    // over each other or crossing unseen). Measured onto its lane
                    // like the player; only the car behind brakes (the other has
                    // this one behind it).
                    float b0 = best;
                    Obstacle(c, o.pos, o.halfL, o.fwd * o.speed, ref best, ref vL, o.halfW + 0.3f, true);
                    if (best < b0) c.leadKind = 5;
                }
            }
            // the player
            float bp = best;
            if (Mathf.Abs(playerPos3.y - c.y) < LevelM) Obstacle(c, playerPos, 2.3f, playerVel, ref best, ref vL);
            if (best < bp) c.leadKind = 3;
            return best;
        }

        /// <summary>The gap from c to o (d = o - c) when o is across c's path
        /// ahead within its reach: o's half extent across c's heading counts
        /// its angle (a car standing at 60 degrees reaches 2 m further across
        /// than its half width - the census's wrecks at the junction mouths of
        /// Sunset Road and Brookshire Boulevard, a nose in the lane); else
        /// MaxValue.</summary>
        static float CrossGap(Car c, Car o, Vector2 d)
        {
            float cosA = Mathf.Abs(Vector2.Dot(o.fwd, c.fwd));
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            float across = o.halfW * cosA + o.halfL * sinA, alongExt = o.halfL * cosA + o.halfW * sinA;
            float along = Vector2.Dot(d, c.fwd);
            float lat = c.fwd.x * d.y - c.fwd.y * d.x;
            float reach = 3f + c.speed * 1.0f + c.halfL + alongExt;
            if (along > 0f && along < reach && Mathf.Abs(lat) < c.halfW + across + 0.3f) return along - c.halfL - alongExt;
            return float.MaxValue;
        }

        /// <summary>Of two cars each across the other's path, does a go first?
        /// The one already turning in the box, then the moving one, then the
        /// lower id.</summary>
        static bool Precedes(Car a, Car b)
        {
            bool ta = a.legCount > 0 && a.legs[0].turn, tb = b.legCount > 0 && b.legs[0].turn;
            if (ta != tb) return ta;
            bool ma = a.speed > 1f, mb = b.speed > 1f;
            if (ma != mb) return ma;
            return a.id < b.id;
        }

        /// <summary>Something that is not traffic (the player, a wreck) in this
        /// car's lane ahead: on its run, measured onto the lane; past it, in a
        /// short corridor straight ahead. halfW: the obstacle's half width
        /// (the player's and a wreck's 1.15 m; a car its own + 0.3 m);
        /// runOnly: only on its run, no corridor past it.</summary>
        void Obstacle(Car c, Vector2 p, float halfL, Vector2 vel, ref float best, ref float vL, float halfW = 1.15f, bool runOnly = false)
        {
            Vector2 d = p - c.pos;
            float along = Vector2.Dot(d, c.fwd);
            if (along <= 0f || along > 80f) return;
            float latErr;
            if (c.legCount > 0 && !c.legs[0].turn && along < c.legs[0].len - c.u + 4f)
            {
                ref Leg L = ref c.legs[0];
                var e = map.edges[L.edge];
                float s = Mathf.Clamp(L.s0 + L.dir * (c.u + along), 0f, e.length);
                var q = PlanPoint(e, s, c.lat, out var t);
                latErr = Mathf.Abs(t.x * (p.y - q.y) - t.y * (p.x - q.x));
            }
            else
            {
                // runOnly (a car on another road): past this car's run its
                // plan turns where it will, and the plan's own legs see what
                // stands on them - a straight corridor would stop a right turn
                // behind a car that went straight on
                if (runOnly || along > 30f) return;
                latErr = Mathf.Abs(c.fwd.x * d.y - c.fwd.y * d.x);
            }
            if (latErr > c.halfW + halfW) return;
            float g = along - c.halfL - halfL;
            if (g < best) { best = g; vL = Mathf.Max(0f, Vector2.Dot(vel, c.fwd)); }
        }

        // ---- the junction ------------------------------------------------------
        /// <summary>The distance to a stop line this car must stop at (a red, a
        /// yellow it can stop for, a STOP bar not yet stopped at, a minor road
        /// with no gap, a left turn with something coming), or MaxValue.</summary>
        float ControlGap(Car c, double now)
        {
            c.ctlWhy = 0;
            c.heldClear = false;
            bool boxSeen = false;
            float dist = -c.u;
            for (int k = 0; k < c.legCount && dist < 160f; k++)
            {
                ref Leg L = ref c.legs[k];
                if (L.turn) { dist += L.len; continue; }
                float dLine = dist + LineAt(L) - c.halfL;       // front bumper to the line
                // DON'T BLOCK THE BOX (CityTraffic.Junctions): the first junction
                // ahead, whatever controls it, not entered while its way out is full
                if (!boxSeen && k + 1 < c.legCount && c.legs[k + 1].turn && degree[L.node] >= 3)
                {
                    boxSeen = true;
                    if (KeepClearHold(c, k, dLine))
                    {
                        if (k == 0 && c.clearKey != L.key && c.speed < 0.5f) { c.clearKey = L.key; Stats.keepClearHolds++; }
                        // a STOP's full stop counts while it waits (the all-way's order)
                        if (L.ctl == CtlStop && k == 0 && c.stopKey != L.key && c.speed < 0.3f && dLine < 2.0f) { c.stopKey = L.key; c.stopAt = Time.time; }
                        c.heldClear = true; c.ctlWhy = WhyKeepClear; c.ctlNode = L.node;
                        return Mathf.Max(0f, dLine - 0.6f) + StopS0 - 0.4f;
                    }
                }
                if (L.ctl == CtlNone)
                {
                    // an uncontrolled junction: a LEFT turn across the other
                    // direction still waits for a gap in it
                    if (k + 1 < c.legCount && c.legs[k + 1].kind == -1 && degree[L.node] >= 3 && dLine > -1f && dLine < 40f &&
                        OncomingConflict(c, JKey(L.node), L.key, TravelTan(map.edges[L.edge], L.dir, L.s1)))
                    {
                        if (k == 0 && c.waitKey != L.key && c.speed < 0.5f) { c.waitKey = L.key; c.waitAt = Time.time; Stats.leftYields++; }
                        c.ctlWhy = 6; c.ctlNode = L.node;
                        return Mathf.Max(0f, dLine - 0.6f) + StopS0 - 0.4f;
                    }
                    dist += L.len;
                    continue;
                }
                Vector2 u = TravelTan(map.edges[L.edge], L.dir, L.s1);
                int key = L.key;
                // crossing the line, for the tools
                if (k == 0)
                {
                    if (c.lineKey == key && c.lineD > 0f && dLine <= 0f) LineCrossed(c, L, now);
                    c.lineKey = key; c.lineD = dLine;
                }
                if (dLine < -1.0f) { dist += L.len; continue; }  // past it
                float gStop = Mathf.Max(0f, dLine - 0.6f) + StopS0 - 0.4f;
                sbyte next = k + 1 < c.legCount ? c.legs[k + 1].kind : (sbyte)0;
                int jk = JKey(L.node);
                switch (L.ctl)
                {
                    case CtlSignal:
                    {
                        var asp = CitySignals.AspectOf(apps[L.app].channel, now);
                        if (asp == CitySignals.Aspect.Green)
                        {
                            // moving off the line: the junction clear of anyone
                            // still crossing it from the other phase first; coming
                            // up to it, one still clearing it from the last phase
                            if (c.speed < 3f && dLine < 6f && CrossInBox(c, jk, key, u)) { c.ctlWhy = 2; c.ctlNode = L.node; return gStop; }
                            if (dLine < 40f && dLine > c.speed * c.speed / (2f * 4f) + 1f && CitySignals.GreenAge(apps[L.app].channel, now) < 8f &&
                                ClearingInBox(c, jk, key, u, now)) { c.ctlWhy = 2; c.ctlNode = L.node; return gStop; }
                            if (next == -1 && OncomingConflict(c, jk, key, u))
                            {
                                if (k == 0 && c.waitKey != key && c.speed < 0.5f && dLine < 4f) { c.waitKey = key; c.waitAt = Time.time; Stats.leftYields++; }
                                c.ctlWhy = 3; c.ctlNode = L.node;
                                return gStop;
                            }
                            return float.MaxValue;
                        }
                        if (c.committed == key || c.rtorKey == key) return float.MaxValue;
                        if (asp == CitySignals.Aspect.Yellow && dLine < c.speed * c.speed / (2f * YellowDecel) + 0.5f)
                        { c.committed = key; return float.MaxValue; }
                        if (dLine < 0f) { c.committed = key; return float.MaxValue; }   // already over it
                        // the permissive left that waited through its green goes
                        // on the yellow once the oncoming stop for it
                        if (asp == CitySignals.Aspect.Yellow && next == -1 && k == 0 && c.waitKey == key && dLine < 6f)
                        {
                            if (!OncomingConflict(c, jk, key, u)) { c.committed = key; Stats.sneakLefts++; return float.MaxValue; }
                            c.ctlWhy = 3; c.ctlNode = L.node;
                            return gStop;
                        }
                        if (k == 0 && c.speed < 0.3f && dLine < 3f && c.redStopKey != key) { c.redStopKey = key; Stats.redStops++; }
                        // RIGHT ON RED after the full stop, clear of the cross street
                        if (asp == CitySignals.Aspect.Red && next == 1 && k == 0 && !apps[L.app].noRtor)
                        {
                            if (c.speed < 0.3f && dLine < 3f && c.rtorStopKey != key) { c.rtorStopKey = key; c.rtorAt = Time.time; }
                            if (c.rtorStopKey == key && Time.time - c.rtorAt >= StopHoldS && RtorClear(c, jk, key, u, now))
                            { c.rtorKey = key; return float.MaxValue; }
                        }
                        c.ctlWhy = 1; c.ctlNode = L.node;
                        return gStop;
                    }
                    case CtlStop:
                    {
                        if (c.goKey == key) return float.MaxValue;
                        if (k == 0 && c.stopKey != key && c.speed < 0.3f && dLine < 2.0f) { c.stopKey = key; c.stopAt = Time.time; }
                        c.ctlWhy = 4; c.ctlNode = L.node;
                        if (c.stopKey != key || Time.time - c.stopAt < StopHoldS) return gStop;
                        bool allWay = apps[L.app].allWay;
                        if (CanGo(c, jk, key, L.node, u, allWay ? 1 : 2, Time.time - c.stopAt > 8f, next, MergeArm(L))) { c.goKey = key; c.ctlWhy = 0; return float.MaxValue; }
                        return gStop;
                    }
                    case CtlYield:
                    {
                        if (c.goKey == key) return float.MaxValue;
                        if (dLine > 30f) return float.MaxValue;
                        bool waiting = c.waitKey == key && c.speed < 0.5f;
                        if (CanGo(c, jk, key, L.node, u, 2, waiting && Time.time - c.waitAt > 6f, next, MergeArm(L)))
                        {
                            if (dLine < 8f) c.goKey = key;
                            return float.MaxValue;
                        }
                        if (k == 0 && c.waitKey != key && c.speed < 0.5f) { c.waitKey = key; c.waitAt = Time.time; Stats.yields++; }
                        c.ctlWhy = 5; c.ctlNode = L.node;
                        return gStop;
                    }
                }
                dist += L.len;
            }
            return float.MaxValue;
        }

        void LineCrossed(Car c, in Leg L, double now)
        {
            if (L.ctl == CtlSignal)
            {
                var asp = CitySignals.AspectOf(apps[L.app].channel, now);
                if (asp == CitySignals.Aspect.Green) Stats.greenGoes++;
                else if (asp == CitySignals.Aspect.Yellow || c.committed == L.key) Stats.yellowGoes++;
                else if (c.rtorKey == L.key) Stats.rightOnRed++;
                else Stats.ranRed++;
            }
            else if (L.ctl == CtlStop)
            {
                if (c.stopKey == L.key) Stats.stopSignStops++; else Stats.stopRolls++;
            }
        }

        /// <summary>May a car at a STOP bar or a yield line go? Nothing in the
        /// junction from another way (nor the player in it); at an all-way stop
        /// nobody who stopped before it still waiting; at a minor road nothing
        /// coming on the major one.</summary>
        bool CanGo(Car c, int jk, int key, int node, Vector2 u, int mode, bool impatient, sbyte turn = 0, bool merge = false)
        {
            Vector2 at = map.nodes[node];
            if ((playerPos - at).sqrMagnitude < 16f * 16f && playerVel.sqrMagnitude > 0.25f && Mathf.Abs(playerPos3.y - c.y) < LevelM) return false;
            // a driver who has waited a while takes a shorter gap (HCM: the critical gap falls with the wait)
            float need = GapFor(turn, merge) * (impatient ? 0.8f : 1f);
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked) continue;
                // in the box from another way - unless the two do not cross
                if (o.boxJ == jk && o.inKey != key && !(mode == 1 && Compatible(u, turn, o.boxU, o.boxKind))) return false;
                if (o.appJ != jk || o.appKey == key) continue;
                // a car given the way and about to enter the box holds it (its claim)
                if (o.goKey == o.appKey && o.appDist < 4f && o.appDist > -3f && !(mode == 1 && Compatible(u, turn, o.appU, o.turnNext))) return false;
                if (mode == 1 && !impatient && o.stopKey == o.appKey && o.goKey != o.appKey && o.stopAt < c.stopAt &&
                    !o.heldClear && !Compatible(u, turn, o.appU, o.turnNext)) return false;
                if (mode != 2) continue;
                float t = o.appDist / Mathf.Max(o.speed, 0.5f);
                if (o.appCtl == CtlNone)
                {
                    // the major road: a gap in it, by this car's movement; a car
                    // standing in its queue leaves the way
                    if (StandingInQueue(o)) continue;
                    if ((o.speed > 1f && t < need) || (o.appDist < MajorNearM && o.appDist > -2f)) return false;
                }
                else if (o.appCtl == CtlYield && !impatient && u.x * o.appU.y - u.y * o.appU.x > 0.3f)
                {
                    // two yields of one rank: the car from the right goes first
                    if ((o.speed > 1f && t < 3f) || (o.appDist < 5f && o.appDist > -2f && o.goKey != o.appKey)) return false;
                }
            }
            return true;
        }

        /// <summary>Anyone in the junction now who came in from a crossing
        /// approach (a car still clearing it on the last of its phase).</summary>
        bool CrossInBox(Car c, int jk, int key, Vector2 u)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked || o.boxJ != jk || o.inKey == key) continue;
                if (Mathf.Abs(Vector2.Dot(o.boxU, u)) < 0.6f) return true;
            }
            return false;
        }

        /// <summary>A left turn on a green: anything coming the other way (not
        /// itself turning left) within a few seconds of the line, or crossing
        /// the junction from there now.</summary>
        bool OncomingConflict(Car c, int jk, int key, Vector2 u)
        {
            // a left turn that has waited 15 s takes a shorter gap (HCM: 4.1 s for a
            // major-street left; the critical gap falls with the wait)
            float gapS = c.waitKey == key && Time.time - c.waitAt > 15f ? OncomingGapS * 0.75f : OncomingGapS;
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked) continue;
                if (o.boxJ == jk && o.boxKind != -1 && Vector2.Dot(o.boxU, u) < -0.5f && o.inKey != key) return true;
                if (o.appJ != jk || o.appKey == key || o.turnNext == -1) continue;
                if (Vector2.Dot(o.appU, u) > -0.5f) continue;
                if (o.appCtl == CtlSignal && o.appIdx >= 0)
                {
                    var oa = CitySignals.AspectOf(apps[o.appIdx].channel, Time.timeAsDouble);
                    if (oa == CitySignals.Aspect.Red) continue;
                    // on its yellow and not committed to it: it stops
                    if (oa == CitySignals.Aspect.Yellow && o.committed != o.appKey && o.appDist > 0.5f) continue;
                }
                float t = o.appDist / Mathf.Max(o.speed, 0.5f);
                if ((o.speed > 1f && t < gapS) || (o.appDist < 6f && o.appDist > -2f)) return true;
            }
            return false;
        }

        // ---- lanes -------------------------------------------------------------
        void LaneChange(Car c, float dt)
        {
            if (c.lc)
            {
                c.lcT += dt / c.lcDur;
                if (c.lcT >= 1f) { c.lc = false; c.lane = c.laneTo; c.lcT = 0f; }
                return;
            }
            if (c.legCount == 0 || c.legs[0].turn || Time.time < c.nextLc) return;
            c.nextLc = Time.time + 0.6f + Random.value * 0.6f;
            ref Leg L = ref c.legs[0];
            int n = Lanes(L.edge, L.dir);
            if (n < 2) return;
            float rem = L.len - c.u;
            var e = map.edges[L.edge];
            bool fwy = e.cls >= 4 && !e.link;
            int req = Mathf.Clamp(L.laneOut, 0, n - 1);
            if (req != c.lane)
            {
                // stuck for good waiting for the gap: take another way on from
                // the lane it is in (a missed turn, not a cut across a lane)
                if (c.mandAt >= 0f && Time.time - c.mandAt > 14f) { Replan(c); return; }
                // MANDATORY: to the lane its turn (or the lane that runs on) needs
                float zone = Mathf.Abs(req - c.lane) * (fwy ? 250f : 110f) + 50f;
                if (rem < zone && rem > 6f)
                {
                    int to = c.lane + (req > c.lane ? 1 : -1);
                    if (GapOk(c, to)) { StartChange(c, to, rem); Stats.mandatory++; }
                }
                return;
            }
            if (rem < 70f) return;
            // DISCRETIONARY: round a slower car (left first), and keep right
            if (c.leaderInLane && c.gap < 30f + c.speed * 2f && c.leaderV < limitMps[L.edge] * c.factor - 2.5f)
            {
                for (int side = 0; side < 2; side++)
                {
                    int to = side == 0 ? c.lane + 1 : c.lane - 1;
                    if (to < 0 || to >= n) continue;
                    if (LaneAhead(c, to) < c.gap + 15f) continue;
                    if (!GapOk(c, to)) continue;
                    StartChange(c, to, rem); Stats.laneChanges++;
                    return;
                }
            }
            else if (fwy && c.lane > 0 && Random.value < 0.15f && LaneAhead(c, c.lane - 1) > 110f && GapOk(c, c.lane - 1))
            {
                StartChange(c, c.lane - 1, rem); Stats.keepRight++;
            }
        }

        void Replan(Car c)
        {
            ref Leg L = ref c.legs[0];
            var e = map.edges[L.edge];
            if (Log != null)
            {
                string turn = c.legCount > 1 ? (c.legs[1].merge ? "merge" : c.legs[1].exit ? "exit" : c.legs[1].kind == -1 ? "left" : c.legs[1].kind == 1 ? "right" : "straight on") : "?";
                Note(c, "lane", "missed-turn-lane", Mathf.Abs(Mathf.Clamp(L.laneOut, 0, Lanes(L.edge, L.dir) - 1) - c.lane),
                     $"waited 14 s for a gap into lane {L.laneOut} of {Lanes(L.edge, L.dir)} from lane {c.lane} for its {turn} at node {L.node}: replanned; " + Describe(c));
            }
            float s1 = EndS(e, L.dir);
            if ((s1 - L.s0) * L.dir < c.u) s1 = L.s0 + L.dir * c.u;
            L.s1 = s1; L.len = Mathf.Abs(L.s1 - L.s0); L.laneOut = c.lane;
            c.legCount = 1;
            c.deadEnd = false; c.offMap = false;
            c.mandAt = -1f;
            c.keepLane = true;
            Stats.replans++;
            EnsurePlan(c);
        }

        void StartChange(Car c, int to, float rem)
        {
            c.lc = true; c.laneFrom = c.lane; c.laneTo = to; c.lcT = 0f;
            float dur = Random.Range(3f, 5f);
            // never longer than the road left at its speed
            if (c.speed > 1f) dur = Mathf.Min(dur, Mathf.Max(1.5f, (rem - 4f) / c.speed));
            c.lcDur = dur;
            if (Log != null)
            {
                bool pushy = c.mandAt >= 0f && Time.time - c.mandAt > 5f;
                if (pushy || dur < 2.2f)
                    Note(c, "lane", pushy ? "forced-into-tight-gap" : "forced-short-change", pushy ? Time.time - c.mandAt : dur,
                         $"lane {c.lane}->{to} {(pushy ? $"after waiting {Time.time - c.mandAt:0} s, into a tight gap" : $"in {dur:0.0} s with {rem:0} m left at {c.speed:0.0} m/s")}; " + Describe(c));
            }
        }

        /// <summary>The free road ahead in a lane of this car's run.</summary>
        /// <summary>A car's arc position on the edge of its current run.</summary>
        static float ArcNow(Car c) => c.legs[0].s0 + c.legs[0].dir * c.u;

        float LaneAhead(Car c, int lane)
        {
            ref Leg L = ref c.legs[0];
            float best = L.len - c.u + 60f, sc = ArcNow(c);
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.legCount == 0 || o.legs[0].turn || o.legs[0].edge != L.edge || o.legs[0].dir != L.dir) continue;
                if (o.lane != lane && !(o.lc && (o.laneFrom == lane || o.laneTo == lane)) && !o.wrecked) continue;
                float d = (ArcNow(o) - sc) * L.dir - c.halfL - o.halfL;
                if (d > 0f && d < best) best = d;
            }
            return best;
        }

        /// <summary>Gap acceptance: room ahead and behind in the target lane -
        /// cars on this run in it, and anything else (a car still in its turn
        /// onto this road, one just behind on the road before) near that
        /// lane's centre - and the player not there. Waiting long for the
        /// lane its turn needs, a driver takes a tighter gap (the car behind
        /// brakes for it).</summary>
        bool GapOk(Car c, int lane)
        {
            ref Leg L = ref c.legs[0];
            var e = map.edges[L.edge];
            bool pushy = c.mandAt >= 0f && Time.time - c.mandAt > 5f;
            float sc = ArcNow(c);
            float s = Mathf.Clamp(sc, 0f, e.length);
            var q = PlanPoint(e, s, LaneLat(e, L.dir, lane, s), out var t);
            var tr = t * L.dir;
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.legCount == 0) continue;
                float d, lengths = c.halfL + o.halfL;
                bool sameRun = !o.legs[0].turn && o.legs[0].edge == L.edge && o.legs[0].dir == L.dir;
                if (sameRun)
                {
                    if (o.lane != lane && !(o.lc && (o.laneFrom == lane || o.laneTo == lane)) && !o.wrecked) continue;
                    d = (ArcNow(o) - sc) * L.dir;
                }
                else
                {
                    Vector2 dp = o.pos - q;
                    if (dp.sqrMagnitude > 45f * 45f || Mathf.Abs(o.y - c.y) > LevelM) continue;
                    if (Mathf.Abs(tr.x * dp.y - tr.y * dp.x) > 2.2f) continue;
                    d = Vector2.Dot(dp, tr);
                }
                float ov = o.wrecked ? 0f : o.speed;
                if (d >= 0f) { if (d - lengths < 4f + c.speed * 0.5f + Mathf.Max(0f, c.speed - ov) * 1.5f) return false; }
                else if (-d - lengths < (pushy ? 3f + ov * 0.3f : 4f + ov * 0.4f + Mathf.Max(0f, ov - c.speed) * 2.5f)) return false;
            }
            // the player, near that lane
            Vector2 dpp = playerPos - q;
            float along = Vector2.Dot(dpp, tr);
            if (Mathf.Abs(along) < 25f && Mathf.Abs(t.x * dpp.y - t.y * dpp.x) < 2.6f && Mathf.Abs(playerPos3.y - c.y) < LevelM) return false;
            return true;
        }

        // ---- the pose ----------------------------------------------------------
        void Place(Car c, float dt, bool teleport)
        {
            if (c.legCount == 0) return;
            ref Leg L = ref c.legs[0];
            Vector2 p, head, travel;
            float y, slope;
            if (!L.turn)
            {
                var e = map.edges[L.edge];
                float s = Mathf.Clamp(L.s0 + L.dir * c.u, 0f, e.length);
                float lat, latRate = 0f;
                if (c.lc)
                {
                    float a = LaneLat(e, L.dir, c.laneFrom, s), b = LaneLat(e, L.dir, c.laneTo, s);
                    float t = Mathf.Clamp01(c.lcT);
                    lat = Mathf.Lerp(a, b, LineModel.Smooth(t));
                    latRate = (b - a) * 6f * t * (1f - t) / Mathf.Max(0.5f, c.lcDur);
                }
                else lat = LaneLat(e, L.dir, c.lane, s);
                c.lat = lat;
                p = PlanPoint(e, s, lat, out var tan);
                travel = tan * L.dir;
                // + lat is left of a->b: a lateral rate seen along the travel
                Vector2 nrm = new Vector2(-tan.y, tan.x);
                head = c.speed > 0.5f ? (travel * c.speed + nrm * latRate).normalized : travel;
                y = e.YAt(s);
                float y2 = e.YAt(Mathf.Clamp(s + L.dir * 1.5f, 0f, e.length)), y1 = e.YAt(Mathf.Clamp(s - L.dir * 1.5f, 0f, e.length));
                slope = (y2 - y1) / 3f;
            }
            else
            {
                float t = Mathf.Clamp01(c.u / L.len);
                p = Bez(L, t, out var d);
                travel = d.sqrMagnitude > 1e-6f ? d.normalized : c.tan;
                head = travel;
                y = Mathf.Lerp(L.y0, L.y1, t);
                // the junction's own surface where its tile is built
                if (Physics.Raycast(new Vector3(p.x, y + 1.2f, p.y), Vector3.down, out var hit, 2.6f, roadMask, QueryTriggerInteraction.Ignore))
                    y = hit.point.y;
                slope = L.len > 0.5f ? (L.y1 - L.y0) / L.len : 0f;
            }
            c.pos = p; c.fwd = head; c.tan = travel; c.y = y;
            var target = new Vector3(p.x, y, p.y);
            var rot = Quaternion.LookRotation(new Vector3(head.x, slope, head.y), Vector3.up);
            if (teleport)
            {
                Put(c, target, rot);
                c.hasCommand = false;
                return;
            }
            c.rb.linearVelocity = c.commanded = (target - c.rb.position) / Mathf.Max(1e-4f, dt);
            c.hasCommand = true;
            c.rb.MoveRotation(rot);
            SpinWheels(c, c.speed, dt);
        }

        static void Put(Car c, Vector3 pos, Quaternion rot)
        {
            var interp = c.rb.interpolation;
            c.rb.interpolation = RigidbodyInterpolation.None;
            c.rb.position = pos;
            c.rb.rotation = rot;
            c.go.transform.SetPositionAndRotation(pos, rot);
            c.rb.interpolation = interp;
        }

        static void SpinWheels(Car c, float v, float dt)
        {
            if (c.wheels.Length == 0) return;
            c.spin = Mathf.Repeat(c.spin + v / c.wheelR * Mathf.Rad2Deg * dt, 360f);
            for (int i = 0; i < c.wheels.Length; i++)
            {
                bool left = c.wheels[i].name.EndsWith("0") || c.wheels[i].name.EndsWith("2");
                c.wheels[i].localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f) * Quaternion.Euler(left ? -c.spin : c.spin, 0f, 0f);
            }
        }

        // ======================================================================
        //  Birth, death, the pool
        // ======================================================================
        void Update()
        {
            if (player == null || map == null) return;
            if (Suspended)
            {
                for (int i = live.Count - 1; i >= 0; i--) Recycle(i);
                return;
            }
            // the density: the hour's share of the cap, drifting a little
            if (Time.time >= driftAt) { drift = Random.Range(0.85f, 1.15f); driftAt = Time.time + Random.Range(15f, 30f); }
            Share = ShareAt(ClockNow);
            Target = Share <= 0f ? 0 : Mathf.Clamp(Mathf.RoundToInt(cap * Share * drift), 1, cap);
            // the pool, one car a frame while it is short
            for (int k = 0; k < (CensusMode ? 8 : 1); k++)
                if (pool.Count < cap && (CensusMode || live.Count >= pool.Count - 1) && live.Count < Target) pool.Add(Build(pool.Count));
            if (Time.time >= nextSpawn)
            {
                nextSpawn = Time.time + (Time.time < firstFillUntil ? 0.02f : 0.25f);
                Cull();
                int tries = CensusMode ? 120 : Time.time < firstFillUntil ? 12 : 6;
                for (int t = 0; t < tries && live.Count < Target; t++) TrySpawn();
            }
            if (Time.time >= nextQueueCount) { nextQueueCount = Time.time + 0.5f; CountQueues(); }
        }

        readonly Dictionary<int, int> queue = new Dictionary<int, int>();

        void CountQueues()
        {
            queue.Clear();
            foreach (var c in live)
            {
                if (c.wrecked || c.legCount == 0 || c.legs[0].turn || c.legs[0].ctl == CtlNone || c.speed > 0.5f || c.appDist > 100f) continue;
                queue.TryGetValue(c.legs[0].key, out int q);
                queue[c.legs[0].key] = q + 1;
            }
            int best = 0, bestKey = -1;
            foreach (var kv in queue) if (kv.Value > best) { best = kv.Value; bestKey = kv.Key; }
            QueueNow = best;
            if (best > Stats.maxQueue) Stats.maxQueue = best;
            if (bestKey >= 0)
            {
                var e = map.edges[bestKey >> 1];
                int dir = (bestKey & 1) == 1 ? 1 : -1;
                bool has = approachOf.TryGetValue(bestKey, out int ai);
                QueueAtSignal = has && apps[ai].signal;
                float s = has ? apps[ai].sStop : EndS(e, dir);
                var p = e.PointAt(s);
                QueueLine = new Vector3(p.x, e.YAt(s), p.y);
                QueueFacing = TravelTan(e, dir, s);
            }
        }

        Plane[] frustum = new Plane[6];
        int frustumFrame = -1;
        bool haveCam;

        bool InView(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            if (frustumFrame != Time.frameCount)
            {
                frustumFrame = Time.frameCount;
                GeometryUtility.CalculateFrustumPlanes(cam, frustum);
                haveCam = true;
            }
            if (!haveCam || (p - cam.transform.position).sqrMagnitude > 600f * 600f) return false;
            return GeometryUtility.TestPlanesAABB(frustum, new Bounds(p + Vector3.up, new Vector3(5f, 3f, 5f)));
        }

        void Cull()
        {
            int over = live.Count - Target;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i];
                var p = c.rb.position;
                float d = Vector2.Distance(new Vector2(p.x, p.z), playerPos);
                bool seen = InView(p);
                bool go;
                if (CensusMode)
                    go = d > CensusDespawnM || p.y < -500f ||
                         (c.wrecked ? Time.time - c.wreckedAt > 10f : (c.deadEnd && Remaining(c) < 5f) || (over > 4 && d > CensusRadiusM));
                else if (c.offMap && !c.wrecked && Remaining(c) < 3f + c.speed * 0.25f) go = true;   // drove off the map's data
                else if (c.wrecked) go = d > DespawnHardM || (Time.time - c.wreckedAt > 20f && !seen) || (Time.time - c.wreckedAt > 60f && d > 60f) || p.y < -500f;
                else go = d > DespawnHardM || (d > DespawnM && !seen) || (c.deadEnd && !seen && Remaining(c) < 5f) || p.y < -500f
                          || (over > 4 && d > SpawnMinM && !seen);
                if (!go) continue;
                if (!c.wrecked && over > 4 && d <= DespawnM) over--;
                Recycle(i);
            }
        }

        void Recycle(int i)
        {
            var c = live[i];
            c.active = false;
            c.go.SetActive(false);
            live.RemoveAt(i);
            Stats.recycled++;
        }

        void TrySpawn()
        {
            Car free = null;
            foreach (var x in pool) if (!x.active) { free = x; break; }
            if (Log != null) Log.spTries++;
            if (free == null) { if (Log != null) Log.spNoFree++; return; }
            bool first = Time.time < firstFillUntil;
            int ei; float s;
            if (CensusMode) { if (!CensusSpawnPoint(out ei, out s)) return; }
            else
            {
                float r = Random.Range(first ? SpawnFirstMinM : SpawnMinM, SpawnMaxM);
                float a = Random.value * Mathf.PI * 2f;
                var p = playerPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (!map.NearestRoadPoint(p, 45f, false, out ei, out s, out _)) return;
                if (spawnW[ei] <= 0f || Random.value * maxSpawnW > spawnW[ei]) return;
            }
            var e = map.edges[ei];
            int dir = e.oneway ? 1 : Random.value < 0.5f ? 1 : -1;
            float start = dir > 0 ? Back(e, e.a) + 4f : e.length - Back(e, e.b) - 4f;
            float end = EndS(e, dir);
            if ((s - start) * dir < 0f || (end - s) * dir < 20f) { if (Log != null) Log.spBounds++; return; }
            int n = Lanes(ei, dir);
            int lane = Random.Range(0, n);
            var q = LanePos(e, dir, lane, s);
            var at = new Vector3(q.x, e.YAt(s), q.y);
            if (!CensusMode)
            {
                if ((q - playerPos).sqrMagnitude < (first ? 40f * 40f : SpawnMinM * SpawnMinM * 0.6f)) return;
                if (!first && InView(at)) return;
            }
            foreach (var o in live) if ((o.pos - q).sqrMagnitude < 20f * 20f) { if (Log != null) Log.spCrowded++; return; }

            var c = free;
            c.active = true; c.wrecked = false; c.hasCommand = false; c.lastContact = null; c.deadEnd = false; c.offMap = false;
            c.box.center = c.rideCenter; c.box.size = c.rideSize;
            c.rb.useGravity = false;
            c.legCount = 0; c.u = 0f;
            c.legs[c.legCount++] = MakeRun(ei, dir, s, lane);
            c.lane = lane; c.lc = false; c.nextLc = Time.time + Random.Range(1f, 3f);
            c.factor = Random.Range(0.90f, 1.08f) * (1f - 0.10f * Share);
            c.timeGap = Random.Range(1.1f, 1.7f);
            c.aMax = Random.Range(1.4f, 2.2f);
            c.bDec = Random.Range(2.0f, 3.0f);
            c.committed = c.stopKey = c.goKey = c.redStopKey = c.lineKey = c.waitKey = -1;
            c.mandAt = -1f; c.lastTraffic = null; c.lastTrafficAt = -9f; c.keepLane = false; c.vTick = 0;
            c.bornAt = Time.time;
            if (Log != null) CensusSpawned(c);
            c.tan = TravelTan(e, dir, s);
            c.pos = q; c.fwd = c.tan; c.y = at.y;
            EnsurePlan(c);
            c.speed = Mathf.Min(DesiredSpeed(c), limitMps[ei] * c.factor) * 0.85f;
            c.go.SetActive(true);
            Place(c, Time.fixedDeltaTime, true);
            c.rb.linearVelocity = new Vector3(c.fwd.x, 0f, c.fwd.y) * c.speed;
            c.rb.angularVelocity = Vector3.zero;
            live.Add(c);
            Stats.spawned++;
        }

        Car Build(int index)
        {
            string key = TrafficSystem.Keys[index % TrafficSystem.Keys.Length];
            var def = CarModelLibrary.Load(key) ?? CarModelLibrary.Load(CarModelLibrary.Default);
            var go = new GameObject("CityTraffic_" + key);
            go.transform.SetParent(transform, false);
            // TrafficSystem.Build's car: layer 2, the box lifted RideClear off the
            // road (the car floats on its lane point), the shell's own lamps
            var shell = OnFoot.CarShell.Spawn(go.transform, def, Random.Range(0, Mathf.Max(1, def.SkinCount)), out _, solid: false);
            var box = go.AddComponent<BoxCollider>();
            float boxW = def.colliderSize.x * CarModelLibrary.WidthScale(def, 0);
            float top = def.colliderCenter.y + def.colliderSize.y * 0.5f;
            float bottom = Mathf.Max(def.colliderCenter.y - def.colliderSize.y * 0.5f, RideClear);
            box.center = new Vector3(0f, (top + bottom) * 0.5f, 0f);
            box.size = new Vector3(boxW, Mathf.Max(0.3f, top - bottom), def.colliderSize.z);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1400f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.useGravity = false;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
            var lights = go.AddComponent<CarLights>();
            lights.box = box;
            lights.shellDef = def;
            lights.shellZ = -def.colliderCenter.z;
            go.AddComponent<CityTrafficContact>();
            var wheels = new List<Transform>();
            if (shell != null)
                for (int i = 0; i < 4; i++)
                {
                    var w = shell.Find("Wheel" + i);
                    if (w != null) wheels.Add(w);
                }
            go.SetActive(false);
            return new Car
            {
                id = index, go = go, rb = rb, box = box, lights = lights, wheels = wheels.ToArray(),
                wheelR = Mathf.Max(0.2f, def.wheelRadius), halfW = boxW * 0.5f, halfL = def.colliderSize.z * 0.5f,
                rideCenter = box.center, rideSize = box.size,
                fullCenter = new Vector3(0f, (top + WreckFloor) * 0.5f, 0f),
                fullSize = new Vector3(boxW, top - WreckFloor, def.colliderSize.z),
            };
        }

        void Wreck(Car c, string by)
        {
            if (c.wrecked) return;
            c.wrecked = true;
            c.wreckedAt = Time.time;
            c.box.center = c.fullCenter; c.box.size = c.fullSize;
            c.rb.useGravity = true;
            if (c.lights != null) c.lights.externalBrake = false;
            Stats.wrecks++;
            WreckLog.Add("hit by " + (by ?? "?"));
            if (Log != null) CensusWreck(c, by);
        }

        internal void NoteContact(GameObject go, GameObject other, string what, Vector3 point = default)
        {
            Car c = null, o = null;
            foreach (var x in live) { if (x.go == go) c = x; else if (x.go == other) o = x; }
            if (c == null) return;
            c.lastContact = what;
            if (o != null) { c.lastTraffic = o; c.lastTrafficAt = Time.time; }
            else if (Log != null && !c.wrecked) CensusContact(c, what, point);
        }
    }

    /// <summary>On each city traffic car: names a contact for the wreck log
    /// (whether it WAS a hit is the velocity it left behind: CityTraffic.Drive).</summary>
    public class CityTrafficContact : MonoBehaviour
    {
        void OnCollisionEnter(Collision col) => Note(col);
        void OnCollisionStay(Collision col) { if (col.rigidbody != null && col.gameObject.GetComponent<CityTrafficContact>() != null) Note(col); }

        void Note(Collision col)
        {
            CityTraffic.Instance?.NoteContact(gameObject, col.gameObject, col.gameObject.name + " (" + LayerMask.LayerToName(col.gameObject.layer) + ")",
                                              col.contactCount > 0 ? col.GetContact(0).point : transform.position);
        }
    }
}
