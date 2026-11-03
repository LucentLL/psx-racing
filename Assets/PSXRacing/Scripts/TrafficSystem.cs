using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// HOW MUCH TRAFFIC A RACE HAS - the owner, 2026-09-30: "there needs to be
    /// a toggle to determine traffic amount for each race (none, light,
    /// medium, heavy, rush hour)". Chosen on the pre-race page and the
    /// planner, written into the booking (RaceBooking.trafficPick), carried to
    /// the race in <see cref="RaceHandoff.TrafficLevel"/>.
    ///
    /// Each level is a DENSITY (cars per km, per direction - split across the
    /// lanes when a direction has more than one) and a FLOW (how fast it
    /// cruises against the posted limit, and how much of it runs in platoons,
    /// nose to tail behind a slower car). RUSH HOUR is the dense, slow,
    /// bunched one, both directions. NONE puts nothing on the road.
    ///
    /// A race with no level asked for (a delivery, a test drive, a call-out
    /// from the RIVALS page, an old booking) gets the level nearest to what
    /// its HOUR used to put on the road: the old per-hour table, mapped.
    /// </summary>
    public static class TrafficLevels
    {
        public const int None = 0, Light = 1, Medium = 2, Heavy = 3, RushHour = 4, Count = 5;
        public static readonly string[] Names = { "NONE", "LIGHT", "MEDIUM", "HEAVY", "RUSH HOUR" };
        /// <summary>Cars per km, per LANE (owner, 2026-11-02: "the 'Rush
        /// Hour' traffic [in Charlotte] feels like normal/light traffic"). It
        /// was per direction, split across the lanes: a three-lane arterial at
        /// RUSH HOUR had a car every 230 m in each lane. A two-lane mountain
        /// road (a lane each way) is as it was.</summary>
        public static readonly float[] PerKm = { 0f, 1.5f, 4.5f, 8f, 13f };
        /// <summary>Cruising speed as a share of the posted limit, the slowest
        /// and the fastest driver.</summary>
        public static readonly float[] CruiseMin = { 0.82f, 0.82f, 0.82f, 0.78f, 0.50f };
        public static readonly float[] CruiseMax = { 1.05f, 1.05f, 1.05f, 1.00f, 0.72f };
        /// <summary>Share of cars born close behind the one before (a
        /// platoon), rather than at the level's random spacing.</summary>
        public static readonly float[] PlatoonShare = { 0f, 0f, 0.10f, 0.25f, 0.60f };
        /// <summary>What each HOUR put on the road before the setting existed
        /// (cars per km per lane: Dawn, Morning, Noon, Afternoon, Sunset, Dusk,
        /// Night) - kept only to map an hour to its nearest level.</summary>
        static readonly float[] HourPerKm = { 2.5f, 6f, 4.5f, 6f, 4.5f, 2f, 0.8f };

        public static bool Valid(int level) => level >= 0 && level < Count;
        public static string Name(int level) => Valid(level) ? Names[level] : "BY THE HOUR";

        /// <summary>The level nearest to what this hour used to put on the
        /// road: dawn, dusk and night LIGHT, the day MEDIUM.</summary>
        public static int ForHour(int hour)
        {
            float d = HourPerKm[Mathf.Clamp(hour, 0, HourPerKm.Length - 1)];
            int best = Light;
            for (int i = 0; i < Count; i++)
                if (Mathf.Abs(PerKm[i] - d) < Mathf.Abs(PerKm[best] - d)) best = i;
            return best;
        }

        /// <summary>The level a race runs: the one asked for, or the hour's.</summary>
        public static int Resolve(int pick, int hour) => Valid(pick) ? pick : ForHour(hour);

        /// <summary>The next level on the button: NONE .. RUSH HOUR, round.</summary>
        public static int Step(int level) => Valid(level) ? (level + 1) % Count : Light;

        /// <summary>Whether a venue has traffic to set at all. A drag strip -
        /// and a real road closed for a drag (Bogue Banks) - has none: two
        /// cars and a tree (TrafficSystem.Begin).</summary>
        public static bool VenueHasTraffic(TrackCatalog.TrackDef def) =>
            def != null && !def.IsDragEvent && !def.IsRoam &&
            !def.IsDeckRun;   // traffic runs ON the race path: never up a deck's aisles or its driveway
    }

    /// <summary>
    /// Moving traffic on the race tracks, during races AND deliveries.
    ///
    /// The owner, 2026-09-25: "Cars should drive on the right side of the road.
    /// This is North America." Asked to pin it down: traffic in both modes,
    /// TWO-WAY (same-direction cars in the right lane, oncoming in the left),
    /// density set by the HOUR ("less at night. Random, not every x meters"),
    /// and REAL PHYSICS - a traffic car is a solid rigidbody, a hit damages the
    /// player's car and counts as a HIT for the pizza like a wall does.
    ///
    /// HOW A TRAFFIC CAR DRIVES. Not the racers' tyre model: a traffic car is a
    /// rigidbody whose VELOCITY is servoed along its lane every physics step,
    /// floating on the road surface (gravity off, the box's own ground
    /// clearance keeps it off the tarmac, so no seam can snag it). Because the
    /// velocity is real, the contact solver sees a 1.4-tonne car moving at that
    /// speed and a collision exchanges momentum honestly. The moment anything
    /// hits it hard enough, the servo lets go for good: gravity on, friction
    /// on, and it becomes a WRECK that stays where physics leaves it.
    ///
    /// LANES come from the road the builder painted: one lane each way on
    /// every two-way venue (lane centres half a US lane, 1.83 m, either side of
    /// the centreline), more where the road is wide enough, and all lanes one
    /// way on a one-way road (TrackDef.oneWay). Right of the direction of
    /// travel, always.
    ///
    /// WHERE THEY ARE: a window round the player, 200 m behind to 700 m ahead
    /// along the road. Cars are born at the far edge ahead - the same-direction
    /// ones to be caught, the oncoming ones to come at you - with RANDOM gaps
    /// (exponential, mean set by the hour), and are recycled once they fall
    /// out of the window. A small pool, built up front: the cost stays flat
    /// however long the track is.
    ///
    /// THE REPLAY RECORDS IT (owner, 2026-09-26: "traffic cars are not
    /// visible in the replays" - it used to hide while one played). Every pool
    /// car's pose and wheel roll is sampled on the recorder's step
    /// (<see cref="Capture"/>); for playback the pool is frozen kinematic
    /// (<see cref="BeginReplay"/>), posed from the recording
    /// (<see cref="ShowReplay"/>) and put back exactly on the way out.
    /// </summary>
    public class TrafficSystem : MonoBehaviour
    {
        public static TrafficSystem Instance { get; private set; }

        // ---- the knobs -------------------------------------------------------
        // How MANY and how FAST come from the race's level (TrafficLevels).
        const float Behind = 200f, Ahead = 700f;
        /// <summary>RUSH HOUR's window ahead: the pool it would take to fill
        /// 700 m of both lanes at its density is a pool a phone pays for in
        /// draw calls on every straight.</summary>
        const float AheadRush = 560f;
        /// <summary>No closer than this to any car in the lane, at birth.</summary>
        const float MinGap = 22f;
        /// <summary>...and inside a platoon: nose to tail, which the
        /// following model then opens out to its time gap.</summary>
        const float PlatoonGapMin = 14f, PlatoonGapMean = 8f;
        /// <summary>The pool: enough for the window at the level's density
        /// with a margin, within these bounds.</summary>
        const int PoolMin = 8, PoolMax = 30;
        /// <summary>...on a PC: per-lane density on a city route's six lanes
        /// wants twice the phone's thirty.</summary>
        const int PoolMaxDesktop = 64;
        /// <summary>How far past the tarmac edge a yielding driver puts its
        /// outer side: onto the 0.9 m paved verge strip, 5 cm short of its
        /// edge (a stage's guard wall stands 1.1 m out).</summary>
        public const float VergeUseM = 0.85f;
        /// <summary>A racer sitting this close behind (m), for this long (s),
        /// and even a driver who was not going to move over pulls onto the
        /// verge and lifts - the courtesy of a mountain road.</summary>
        const float TailedNearM = 38f, TailedYieldS = 3.5f;
        const float LaneM = City.RoadProfiles.LaneM;
        /// <summary>Lateral grip a traffic driver is willing to use in a bend.</summary>
        const float CornerAccel = 2.6f;
        /// <summary>Following distance at a stop, and the time gap above it.</summary>
        const float StopGap = 9f, TimeGap = 1.4f;
        /// <summary>A soaked road's share of the cruise and the bend's grip,
        /// and its stretch of the time gap (Seasons.WetT scales them in).</summary>
        const float WetCruise = 0.90f, WetCornerAccel = 0.80f, WetTimeGap = 1.35f;
        /// <summary>A hit that changes the car's speed by this much (m/s) ends
        /// the drive.</summary>
        const float WreckSpeed = 1.5f;
        /// <summary>Gap between the collision box and the road, driving.</summary>
        const float RideClear = 0.22f;
        /// <summary>...and once it is a wreck.</summary>
        const float WreckFloor = 0.01f;

        /// <summary>The owner's traffic cars twice (the taxi and the two
        /// police liveries once), then the pack's everyday shells for variety.</summary>
        internal static readonly string[] Keys =
        {
            "crown_victoria", "sedan_98", "camry_2001", "ford_transit",
            "crown_victoria", "sedan_98", "camry_2001", "ford_transit",
            "fullsize_taxi", "fullsize_police", "fullsize_trooper",
            "volvo_estate", "bmw_e30", "audi_saloon", "euro_hatch",
            "citroen_cx", "jdm_pickup", "landrover", "classic_van",
        };

        // ---- state -------------------------------------------------------------
        RaceManager rm;
        TrackPath path;
        float total;
        /// <summary>Cars per km per direction this race (the level's).</summary>
        float perKm;
        /// <summary>The level this race runs (TrafficLevels), resolved from
        /// the handoff and the hour.</summary>
        public int Level { get; private set; }
        int poolSize;
        /// <summary>Lateral lane centres, metres right of the centreline, per
        /// direction: +1 is the race direction, -1 against it.</summary>
        readonly List<float> lanesFwd = new List<float>(), lanesBack = new List<float>();
        /// <summary>Is there ONCOMING traffic on this road - and so a lane the
        /// racers must keep out of except to pass (AIDriver)?</summary>
        public bool TwoWay => lanesBack.Count > 0;
        /// <summary>The middle of the race direction's lanes, metres right of
        /// the centreline (0 when there are none).</summary>
        public float RaceLanesCentre
        {
            get
            {
                if (lanesFwd.Count == 0) return 0f;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (float l in lanesFwd) { lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); }
                return (lo + hi) * 0.5f;
            }
        }
        readonly List<Car> pool = new List<Car>();
        readonly List<Car> live = new List<Car>();
        int playerHint = -1;
        bool started, replaying;
        int roadMask;
        /// <summary>The window, shrunk on a loop too short for it: at most
        /// 0.45 of the lap ahead and 0.25 behind, or a car born "ahead" is born
        /// behind the player.</summary>
        float ahead, behind;
        /// <summary>The field (player and racers): where each is along the road
        /// this step, measured ONCE per step - NearestIndex without a hint scans
        /// the whole path.</summary>
        readonly List<(Vector3 pos, float s, Vector3 vel)> field = new List<(Vector3, float, Vector3)>();
        int[] fieldHints = new int[0];

        /// <summary>The traffic bodies now on the road, for the racers'
        /// avoidance (AIDriver).</summary>
        public readonly List<Rigidbody> Obstacles = new List<Rigidbody>();
        /// <summary>Half the width of each of <see cref="Obstacles"/>, same
        /// order: a pass is planned round the car that is there, not round a
        /// 2.4 m allowance that fits a Transit and wastes a lane on a Golf.</summary>
        public readonly List<float> ObstacleHalfW = new List<float>();
        /// <summary>...and which stream each is in: +1 with the race, -1 the
        /// oncoming one - a car queued in the other lane is still in it.</summary>
        public readonly List<int> ObstacleDir = new List<int>();
        /// <summary>...and where each is ALONG the road (metres, the path's
        /// own measure: waypoint index x spacing). The racers' oncoming guard
        /// measures up the road with it: in a straight line a car on the leg
        /// below a switchback is "ahead", and a whole field held its lane
        /// behind a slow car for it (AI at the limit, 2026-10-07).</summary>
        public readonly List<float> ObstacleS = new List<float>();

        /// <summary>
        /// HOW A DRIVER TAKES A RACER COMING (owner, 2026-09-26: "traffic
        /// should have variance; sometimes move to side of road, brake, be
        /// oblivious"). Rolled per car at birth (TemperOdds). A racer is
        /// COMING when one closes from behind in this car's direction, or one
        /// is on its way toward it from ahead.
        ///   Normal    - keeps its lane and its speed; minds only what is ahead.
        ///   Yielder   - eases onto the shoulder and lifts to three-quarters.
        ///   Braker    - stays in lane and brakes to under half its speed.
        ///   Oblivious - does nothing about it, and wanders a little in its lane.
        /// </summary>
        public enum Temper { Normal, Yielder, Braker, Oblivious }

        /// <summary>Normal, Yielder, Braker, Oblivious - shares at birth.</summary>
        static readonly float[] TemperOdds = { 0.40f, 0.30f, 0.15f, 0.15f };
        /// <summary>How far behind (m) a racer is noticed, and how far ahead
        /// an oncoming one is.</summary>
        const float NoticeBehindM = 70f, NoticeAheadM = 110f;
        /// <summary>How fast (m/s) a driver moves across its lane.</summary>
        const float LaneShiftMps = 1.8f;
        /// <summary>
        /// STEERED, NOT SLID (owner, 2026-11-02: "traffic cars move sideways to
        /// avoid racers... they just slide sideways"). A move across the road
        /// was the car's position slewed at a constant rate with the body held
        /// along the lane, wheels straight. Now the move has a speed profile -
        /// LaneShiftAccel (m/s^2) up and down, never past the rate it stops in
        /// - the body faces where the car is going, and the front wheels turn
        /// for the yaw it makes, in a bend as well. A car can only go across
        /// as fast as its heading takes it: no more than MaxCrabTan of its
        /// speed (12 degrees), so a car at a standstill does not move over.
        /// </summary>
        const float LaneShiftAccel = 1.5f, MaxCrabTan = 0.21f;
        /// <summary>Going round a stopped racer: noticed this far ahead (m),
        /// passed at this crawl (m/s) with this much air (m); a racer's half
        /// width, about.</summary>
        const float EdgeLookM = 30f, EdgeCreepMps = 2.5f, EdgeAirM = 0.35f, RacerHalfM = 0.95f;
        /// <summary>...held out to a car's length past it (m), at no more than
        /// this (m/s) while beside it.</summary>
        const float EdgePastM = 5f, EdgePassMps = 6f;
        /// <summary>Most the front wheels turn (degrees), and how quickly the
        /// drawn angle follows the one the yaw asks for (1/s).</summary>
        const float MaxSteerDeg = 32f, SteerFollow = 8f;

        class Car
        {
            public GameObject go;
            public Rigidbody rb;
            public Transform[] wheels;
            public float wheelR;
            public bool active, wrecked;
            public int dir;          // +1 with the race, -1 against
            public float lat;        // lane centre, m right of the centreline
            /// <summary>Where it actually is across the road, slewing toward
            /// the lane centre or, for a Yielder with a racer coming, toward
            /// the shoulder. Place puts the car here.</summary>
            public float latNow;
            /// <summary>How fast latNow is moving (m/s, + right).</summary>
            public float latVel;
            /// <summary>Going round a stopped racer: the line it holds (NaN
            /// when it is not), until it is at edgeEndS.</summary>
            public float edgeLat = float.NaN, edgeEndS;
            /// <summary>The body's heading last step (degrees), for its yaw
            /// rate; the front wheels' drawn angle; which wheels are the
            /// fronts, and the wheelbase between the axles.</summary>
            public float yawWas, steer;
            public bool haveYaw;
            public bool[] front;
            public float wheelBase;
            /// <summary>The drawn body and its rest pose: it is the SHELL that
            /// turns toward a move across the road, not the box.</summary>
            public Transform shell;
            public Quaternion shellRest;
            public Temper temper;
            public float wanderPhase;
            public float s;          // metres along the path
            public float speed, cruise;
            /// <summary>Half the collision box's width.</summary>
            public float halfW;
            /// <summary>Seconds a racer has sat close behind this car
            /// (TailedNearM), leaking away once it has gone.</summary>
            public float tailed;
            public float spin;
            public int hint = -1;
            public float wreckedAt;
            /// <summary>The velocity this step handed the body. If it comes
            /// back different, a contact changed it: that is a HIT.</summary>
            public Vector3 commanded;
            public bool hasCommand;
            public string lastContact;
            /// <summary>The box as driven (lifted clear of the road) and as the
            /// bake measured it (down to the ground).</summary>
            public BoxCollider box;
            public Vector3 rideCenter, rideSize, fullCenter, fullSize;
        }

        /// <summary>Called by RaceManager.Start once the path is final (a
        /// reverse twin has been turned round by then).</summary>
        public static void Begin(RaceManager race)
        {
            if (race == null || race.path == null || race.path.Count < 10) return;
            // A drag strip is two cars and a tree; a car wandering across it is
            // not traffic, it is a fault.
            if (race.path.drag) return;
            // A DECK RUN has no traffic and no lanes: the path is already the
            // aisle's right-hand lane, and lanes laid on it would push every
            // racer a lane further right - into the columns.
            if (!TrafficLevels.VenueHasTraffic(TrackCatalog.At(RaceHandoff.TrackIndex)) &&
                TrackCatalog.At(RaceHandoff.TrackIndex).IsDeckRun) return;
            if (Instance != null) return;
            var go = new GameObject("Traffic");
            Instance = go.AddComponent<TrafficSystem>();
            Instance.Init(race);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Init(RaceManager race)
        {
            rm = race;
            path = race.path;
            total = path.TotalLength;
            roadMask = 1 << 8;

            // THE RACE'S LEVEL: the one the player set, or the hour's own.
            Level = TrafficLevels.Resolve(RaceHandoff.TrafficLevel, TimeOfDay.Current);
            perKm = TrafficLevels.PerKm[Level];

            float want = Level == TrafficLevels.RushHour ? AheadRush : Ahead;
            ahead = path.HasEnds ? want : Mathf.Min(want, total * 0.45f);
            behind = path.HasEnds ? Behind : Mathf.Min(Behind, total * 0.25f);

            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            bool oneWay = def != null && def.oneWay;
            // The lanes are laid whatever the level: an EMPTY road is still a
            // two-way road, and the racers keep to their side of it (AIDriver's
            // lane discipline hangs off TwoWay) - NONE means no cars, not a
            // closed circuit.
            BuildLanes(path.roadWidth, oneWay);

            // Enough cars for the window at this density, both ways, with a
            // quarter over for the random gaps; none at all for NONE.
            int lanesAll = Mathf.Max(1, lanesFwd.Count + lanesBack.Count);
            float expected = (ahead + behind) / 1000f * perKm * lanesAll;
            int poolMax = Application.isMobilePlatform ? PoolMax : PoolMaxDesktop;
            poolSize = Level == TrafficLevels.None ? 0
                     : Mathf.Clamp(Mathf.CeilToInt(expected * 1.25f) + 3, PoolMin, poolMax);
            for (int i = 0; i < poolSize; i++) pool.Add(Build(Keys[i % Keys.Length]));
        }

        /// <summary>Lane centres as the painter lays them out: US lanes packed
        /// from the centre line out, as many as fit with a 0.4 m shoulder.</summary>
        void BuildLanes(float width, bool oneWay)
        {
            lanesFwd.Clear(); lanesBack.Clear();
            // A lane is a US lane where there is room for one, and otherwise
            // the road's own share: a mountain road is two 10 ft (3.05 m)
            // lanes, and 3.66 m lanes on it put the cars' centres 0.2 m off
            // where its lanes are.
            if (oneWay)
            {
                int n = Mathf.Max(1, Mathf.FloorToInt((width - 0.8f) / LaneM));
                float lw = Mathf.Min(LaneM, width / n);
                for (int k = 0; k < n; k++) lanesFwd.Add((k - (n - 1) * 0.5f) * lw);
                return;
            }
            int per = Mathf.Max(1, Mathf.FloorToInt((width - 0.8f) / (2f * LaneM)));
            float laneW = Mathf.Min(LaneM, width / (2f * per));
            for (int k = 0; k < per; k++)
            {
                lanesFwd.Add((k + 0.5f) * laneW);      // right of the race direction
                lanesBack.Add(-(k + 0.5f) * laneW);    // right of the oncoming direction
            }
        }

        Car Build(string key)
        {
            var def = CarModelLibrary.Load(key) ?? CarModelLibrary.Load(CarModelLibrary.Default);
            var go = new GameObject("Traffic_" + key);
            go.transform.SetParent(transform, false);
            // Layer 2 like every car: the player's suspension rays skip it, so
            // a car alongside is a car to hit, not a ramp to climb.
            var shell = OnFoot.CarShell.Spawn(go.transform, def,
                Random.Range(0, Mathf.Max(1, def.SkinCount)), out _, solid: false);
            // The box stops RideClear above the ground, whatever the bake says:
            // the car floats on its lane point, and a box that reaches the
            // tarmac grazes it on every crest and dip (the first play check
            // wrecked three cars in thirty seconds "hit by Road at 19 m/s").
            var box = go.AddComponent<BoxCollider>();
            // As wide as the shell CarShell just scaled to its reference car.
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
            // The shell's own measured lamps, slid as CarShell slid the body -
            // the lifted, shrunk box buried them inside the car.
            var lights = go.AddComponent<CarLights>();
            lights.box = box;
            lights.shellDef = def;
            lights.shellZ = -def.colliderCenter.z;
            go.AddComponent<TrafficContact>();

            var wheels = new List<Transform>();
            if (shell != null)
                for (int i = 0; i < 4; i++)
                {
                    var w = shell.Find("Wheel" + i);
                    if (w != null) wheels.Add(w);
                }
            // The fronts: the wheels ahead of the middle of the four (the shell
            // faces +Z), and the wheelbase between the two axles.
            var front = new bool[wheels.Count];
            float wheelBase = 2.6f;
            if (wheels.Count >= 2)
            {
                float mid = 0f;
                foreach (var w in wheels) mid += w.localPosition.z;
                mid /= wheels.Count;
                float zf = 0f, zr = 0f; int nf = 0, nr = 0;
                for (int i = 0; i < wheels.Count; i++)
                {
                    float z = wheels[i].localPosition.z;
                    front[i] = z > mid;
                    if (front[i]) { zf += z; nf++; } else { zr += z; nr++; }
                }
                if (nf > 0 && nr > 0) wheelBase = Mathf.Max(1.5f, zf / nf - zr / nr);
            }
            go.SetActive(false);
            return new Car
            {
                go = go, rb = rb, wheels = wheels.ToArray(), wheelR = Mathf.Max(0.2f, def.wheelRadius),
                front = front, wheelBase = wheelBase,
                shell = shell, shellRest = shell != null ? shell.localRotation : Quaternion.identity,
                halfW = boxW * 0.5f,
                box = box, rideCenter = box.center, rideSize = box.size,
                // A WRECK has no suspension to stand on, so its box reaches
                // down to the tyres' contact. The baked box stops at the sills,
                // like a driven car's: a wreck resting on that sat 35 cm into
                // the road.
                fullCenter = new Vector3(0f, (top + WreckFloor) * 0.5f, 0f),
                fullSize = new Vector3(boxW, top - WreckFloor, def.colliderSize.z),
            };
        }

        // ---- the road ---------------------------------------------------------
        float Wrap(float s) => path.HasEnds ? s : Mathf.Repeat(s, total);

        /// <summary>Signed along-road distance from a to b, the short way round
        /// on a loop.</summary>
        float Delta(float a, float b)
        {
            float d = b - a;
            if (!path.HasEnds)
            {
                if (d > total * 0.5f) d -= total;
                else if (d < -total * 0.5f) d += total;
            }
            return d;
        }

        void Sample(float s, out Vector3 pos, out Vector3 tan)
        {
            float f = s / path.spacing;
            int i = Mathf.FloorToInt(f);
            float t = f - i;
            pos = Vector3.Lerp(path.GetPoint(i), path.GetPoint(i + 1), t);
            tan = Vector3.Slerp(path.GetTangent(i), path.GetTangent(i + 1), t).normalized;
        }

        float SOf(Vector3 p, ref int hint)
        {
            hint = path.NearestIndex(p, hint);
            return hint * path.spacing;
        }

        /// <summary>1/radius of the road around s, looking a few points either
        /// way - enough for a driver to slow before a bend, not only in it.</summary>
        float Curvature(float s, int dir)
        {
            int i = Mathf.FloorToInt(s / path.spacing);
            float k = 0f;
            for (int j = 0; j <= 10; j++)
            {
                int w = i + j * dir;
                if (path.HasEnds && (w < 0 || w >= path.Count)) break;
                if (path.curvatures != null && path.curvatures.Length == path.Count)
                    k = Mathf.Max(k, Mathf.Abs(path.curvatures[path.Wrap(w)]));
                else
                {
                    Vector3 a = path.GetTangent(w), b = path.GetTangent(w + dir);
                    k = Mathf.Max(k, Vector3.Angle(a, b) * Mathf.Deg2Rad / path.spacing);
                }
            }
            return k;
        }

        // ---- the loop ---------------------------------------------------------
        void FixedUpdate()
        {
            if (rm == null || rm.playerCar == null) return;

            // A replay is posing the pool from its recording: nothing drives.
            if (replaying || RaceReplay.Playing) { Obstacles.Clear(); ObstacleHalfW.Clear(); ObstacleDir.Clear(); ObstacleS.Clear(); return; }

            // Nothing moves on the road until the race is on: a car arriving
            // at a grid of four stationary racers would be a pile-up the
            // player did not cause.
            if (!started)
            {
                if (rm.State != RaceManager.RaceState.Racing) return;
                started = true;
                Populate();
            }

            float ps = SOf(rm.playerCar.transform.position, ref playerHint);
            MeasureField();
            Recycle(ps);
            SpawnAtEdge(ps);
            float dt = Time.fixedDeltaTime;
            foreach (var c in live) Drive(c, dt);
            Obstacles.Clear();
            ObstacleHalfW.Clear();
            ObstacleDir.Clear();
            ObstacleS.Clear();
            foreach (var c in live) { Obstacles.Add(c.rb); ObstacleHalfW.Add(c.halfW); ObstacleDir.Add(c.dir); ObstacleS.Add(c.s); }
        }

        void MeasureField()
        {
            field.Clear();
            var cars = rm.allCars;
            if (fieldHints.Length != cars.Count)
            {
                fieldHints = new int[cars.Count];
                for (int i = 0; i < fieldHints.Length; i++) fieldHints[i] = -1;
            }
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (car == null || !car.gameObject.activeInHierarchy) continue;
                var body = car.GetComponent<Rigidbody>();
                Vector3 pos = car.transform.position;
                float so = SOf(pos, ref fieldHints[i]);
                field.Add((pos, so, body != null ? body.linearVelocity : Vector3.zero));
            }
        }

        void Populate()
        {
            float ps = SOf(rm.playerCar.transform.position, ref playerHint);
            foreach (int dir in Dirs())
                foreach (float lat in LanesFor(dir))
                {
                    // Random gaps from just ahead of the grid to the far edge -
                    // ONCOMING cars from further off: the grid stands across both
                    // lanes, and at 90 m one met a rival still merging into its
                    // own lane 7.5 s in (NC 226A). At 400 m the field is in single
                    // file before the first one arrives.
                    float d = (dir < 0 ? OncomingStartM : 90f) + Gap(dir);
                    while (d < ahead && live.Count < poolSize)
                    {
                        TrySpawn(Wrap(ps + d), dir, lat);
                        d += Gap(dir);
                    }
                }
        }

        /// <summary>Nearest an ONCOMING car is placed ahead of the grid at the
        /// green (Populate).</summary>
        const float OncomingStartM = 400f;

        IEnumerable<int> Dirs()
        {
            yield return 1;
            if (lanesBack.Count > 0) yield return -1;
        }

        List<float> LanesFor(int dir) => dir > 0 ? lanesFwd : lanesBack;

        /// <summary>
        /// The gap to the next car born in a lane: RANDOM (exponential) with
        /// the level's mean spacing in this lane. At a level with platoons, that share of cars is
        /// born nose to tail behind the one before instead, and the rest are
        /// spaced wider so the density comes out the same: bunches with road
        /// between them, the way a slow car gathers a queue.
        /// </summary>
        float Gap(int dir)
        {
            // Per lane (TrafficLevels.PerKm): the lane's own mean spacing.
            float mean = 1000f / Mathf.Max(0.05f, perKm);
            float p = TrafficLevels.PlatoonShare[Level];
            if (p > 0f && Random.value < p)
                return PlatoonGapMin + -Mathf.Log(1f - Random.value * 0.999f) * PlatoonGapMean;
            // The loose share: what is left of the mean once the platoon gaps
            // have taken theirs.
            float loose = p > 0f ? (mean - p * (PlatoonGapMin + PlatoonGapMean)) / (1f - p) : mean;
            loose = Mathf.Max(loose - MinGap, 10f);
            return MinGap + -Mathf.Log(1f - Random.value * 0.999f) * loose;
        }

        /// <summary>No car is born nearer than this to another in its lane.</summary>
        float BirthGap => TrafficLevels.PlatoonShare[Level] > 0f ? PlatoonGapMin : MinGap;

        float nextEdgeCheck;
        readonly Dictionary<float, float> laneNextGap = new Dictionary<float, float>();

        void SpawnAtEdge(float ps)
        {
            if (Time.time < nextEdgeCheck) return;
            nextEdgeCheck = Time.time + 0.25f;
            foreach (int dir in Dirs())
                foreach (float lat in LanesFor(dir))
                {
                    // Distance from the far edge back to the nearest car in
                    // this lane; a new car is born when that stretch is longer
                    // than this lane's next random gap.
                    float edge = Wrap(ps + ahead);
                    float nearest = ahead;
                    foreach (var c in live)
                    {
                        if (c.dir != dir || Mathf.Abs(c.lat - lat) > 0.1f) continue;
                        float d = -Delta(edge, c.s);           // how far behind the edge
                        if (d >= 0f && d < nearest) nearest = d;
                    }
                    float key = dir * 100f + lat;
                    if (!laneNextGap.TryGetValue(key, out float want)) want = laneNextGap[key] = Gap(dir);
                    if (nearest >= want && TrySpawn(edge, dir, lat)) laneNextGap[key] = Gap(dir);
                }
        }

        bool TrySpawn(float s, int dir, float lat)
        {
            if (path.HasEnds && (s < 8f || s > total - 8f)) return false;
            // Never on top of anything: another traffic car in the lane, or a
            // racer (they use the whole road).
            foreach (var c in live)
                if (Mathf.Abs(Delta(s, c.s)) < BirthGap && Mathf.Abs(c.lat - lat) < 1.5f) return false;
            Sample(s, out Vector3 p0, out Vector3 t0);
            Vector3 at = p0 + Vector3.Cross(Vector3.up, t0).normalized * lat;
            foreach (var f in field)
                if ((f.pos - at).sqrMagnitude < MinGap * MinGap) return false;

            var free = pool.Find(x => !x.active);
            if (free == null) return false;
            free.active = true; free.wrecked = false; free.hasCommand = false; free.lastContact = null;
            free.box.center = free.rideCenter; free.box.size = free.rideSize;
            free.dir = dir; free.lat = lat; free.latNow = lat; free.latVel = 0f; free.s = s; free.hint = -1;
            free.haveYaw = false; free.steer = 0f; free.edgeLat = float.NaN;
            free.temper = RollTemper();
            TempersSeen[(int)free.temper]++;
            free.wanderPhase = Random.Range(0f, 6.2832f);
            free.tailed = 0f;
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            float limit = (def != null ? def.speedLimitKmh : 80f) / 3.6f;
            // The level's flow: RUSH HOUR crawls at half to three-quarters of
            // the limit, the rest drive it.
            free.cruise = limit * Random.Range(TrafficLevels.CruiseMin[Level], TrafficLevels.CruiseMax[Level]);
            free.speed = free.cruise;
            free.rb.useGravity = false;
            Place(free, true);
            free.go.SetActive(true);
            free.rb.linearVelocity = free.go.transform.forward * free.speed;
            free.rb.angularVelocity = Vector3.zero;
            live.Add(free);
            return true;
        }

        /// <summary>Seconds a wreck stays at least; past it, it goes the
        /// moment no camera can see it (Recycle).</summary>
        const float WreckClearS = 20f;
        static readonly Plane[] frustum = new Plane[6];

        /// <summary>Is a point inside the main camera's view (within 250 m)?
        /// No camera: nothing is in view.</summary>
        static bool InView(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null || (p - cam.transform.position).sqrMagnitude > 250f * 250f) return false;
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            return GeometryUtility.TestPlanesAABB(frustum, new Bounds(p, new Vector3(5f, 3f, 5f)));
        }

        void Recycle(float ps)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i];
                float d = Delta(ps, c.wrecked ? SOf(c.go.transform.position, ref c.hint) : c.s);
                bool outOfWindow = d < -behind || d > ahead + 60f
                                   || (!c.wrecked && path.HasEnds && (c.s < 2f || c.s > total - 2f))
                                   || c.go.transform.position.y < -500f;
                // A wreck stays where physics left it: until it is out of the
                // window, or a minute old and behind the player (out of sight).
                // Or twenty seconds old and out of the camera's view, wherever it
                // is: on a 6 m mountain road a wreck AHEAD of the player stood in
                // its lane for the rest of the race, the traffic behind it queued
                // for good, and the rivals racing past it gridlocked (NC 226A,
                // Mount Mitchell and Gillespie Gap, all in one afternoon).
                bool keep = c.wrecked
                    ? !outOfWindow && !(Time.time - c.wreckedAt > 60f && d < -40f)
                      && !(Time.time - c.wreckedAt > WreckClearS && !InView(c.go.transform.position))
                    : !outOfWindow;
                if (keep) continue;
                c.active = false;
                c.go.SetActive(false);
                live.RemoveAt(i);
            }
        }

        /// <summary>The open road's grip a second and a half ahead of a
        /// traffic car (1 on a dry day).</summary>
        static float GripAhead(Car c)
        {
            var w = Seasons.Now;
            if (w.road.water <= 0f && w.road.slush <= 0f && w.road.snow <= 0f && !w.road.frost) return 1f;
            Vector3 p = c.go.transform.position + c.rb.linearVelocity * 1.5f;
            return RoadSurface.OfDeck(w.road, RoadSurface.Patch(p.x, p.z)).grip;
        }

        void Drive(Car c, float dt)
        {
            if (c.wrecked) { SpinWheels(c, Vector3.Dot(c.rb.linearVelocity, c.go.transform.forward), dt); return; }

            // WAS IT HIT? Nothing but a contact changes this body's velocity
            // between steps (no gravity, no ground contact, no drag), so the
            // difference from what it was given IS the knock. Measured here
            // rather than from OnCollisionEnter's impulse, which with
            // speculative contacts arrives spread over several steps and read
            // a 12 m/s rear-end as nothing.
            if (c.hasCommand && (c.rb.linearVelocity - c.commanded).magnitude > WreckSpeed)
            {
                Wreck(c.go, (c.lastContact ?? "?") + ", knocked " +
                            (c.rb.linearVelocity - c.commanded).magnitude.ToString("0.0") + " m/s");
                return;
            }

            // Target speed: cruise, slowed for the bend, then for whatever is
            // ahead in the lane - traffic, a wreck, a racer, the player.
            // WET TARMAC (2026-10-06): in the rain a driver goes a little
            // slower, takes the bends gentler, leaves a longer gap and brakes
            // on the road's grip - all off the same number the tyres get.
            // The emergency 9 stays: the pile-up it stops is worse than a
            // servo braking harder than wet rubber would.
            // The road ahead of THIS car (weather layers: RoadSurface's patches).
            float roadGrip = GripAhead(c);
            float wet = Seasons.WetT(roadGrip);
            float v = c.cruise * Mathf.Lerp(1f, WetCruise, wet);
            float k = Curvature(c.s, c.dir);
            if (k > 1e-4f) v = Mathf.Min(v, Mathf.Sqrt(CornerAccel * Mathf.Lerp(1f, WetCornerAccel, wet) / k));
            float gap = LeaderGap(c, out float leaderV, out float leaderRacerLat);
            float timeGap = TimeGap * Mathf.Lerp(1f, WetTimeGap, wet);
            if (gap < 120f)
                v = Mathf.Min(v, Mathf.Max(0f, leaderV + (gap - StopGap - leaderV * timeGap) * 0.5f));
            // A driver, not a servo: 2.5 m/s^2 up, 6 down - and a full 9 when
            // what is ahead is closer than a 6 m/s^2 stop needs (a car ahead
            // just wrecked by a racer: at 6 the ones behind piled into it).
            float firm = 6f * roadGrip;
            float decel = gap < c.speed * c.speed / (2f * firm) + StopGap ? 9f : firm;

            // A RACER COMING, and what this driver does about it (Temper).
            float latTarget = c.lat;
            bool coming = RacerComing(c, dt, out float racerLat, out bool sat);
            // PULLING OVER (2026-09-30, "AI really struggles to pass when there
            // is heavy traffic"): a Yielder with a racer coming, and ANY
            // ordinary driver with one sat on its bumper for a few seconds,
            // moves onto the paved verge and lifts - on a 6.4 m mountain road
            // that is the room a racer needs to get by without crossing the
            // centreline into the oncoming queue.
            bool letBy = (coming || sat) && c.temper == Temper.Yielder
                      || c.tailed > TailedYieldS && c.temper == Temper.Normal;
            // Only where the road is NARROW: with a racer's width of tarmac
            // beside it on the right already (a 12 m street), a driver moving
            // over only closes the side the racer was going to use (Sunset
            // City, rush hour: yielders and racers both went right, six hits).
            // There it holds its lane and eases off. And only where pulling
            // over makes the room (PullOverMakesRoom).
            bool narrow = path.roadWidth * 0.5f - (Mathf.Abs(c.lat) + c.halfW) < RoomToPassM;
            bool pullOver = letBy && narrow && PullOverMakesRoom(c);
            if (letBy && !narrow) v = Mathf.Min(v, c.cruise * 0.8f);
            switch (pullOver ? Temper.Yielder : c.temper)
            {
                case Temper.Yielder:
                    if (pullOver)
                    {
                        latTarget = PullOverLat(c, racerLat);
                        // Lifting: to three-quarters for one coming, to
                        // six-tenths for one already sat behind - it goes by
                        // sooner, and at less closing speed.
                        v = Mathf.Min(v, c.cruise * (sat ? 0.6f : 0.75f));
                    }
                    break;
                case Temper.Braker:
                    // A firm brake, not an emergency stop: at 7 m/s^2 it was a
                    // brake-check a racer closing at 22 m/s ran straight into.
                    if (coming) { v = Mathf.Min(v, c.cruise * 0.4f); decel = Mathf.Max(decel, 5f); }
                    break;
                case Temper.Oblivious:
                    latTarget = c.lat + Mathf.Sin(Time.time * 0.35f + c.wanderPhase) * 0.35f;
                    break;
            }
            // ROUND A RACER STOPPED IN ITS WAY (owner, 2026-11-02: cars "just
            // stop... park and wait"): a rival or the player standing with its
            // nose in this lane - stuck behind a wreck, pulled half out to
            // pass - held this car still, and this car held IT (a racer
            // will not pull out round a stopped car with one stopped in the
            // other lane). Where the rest of the lane and the verge have the
            // room, it eases over to the outside at a crawl and goes by.
            if (!float.IsNaN(leaderRacerLat) && leaderV < 1.5f && gap < EdgeLookM)
            {
                float side = c.lat >= 0f ? 1f : -1f;
                float edge = leaderRacerLat + side * (RacerHalfM + c.halfW + EdgeAirM);
                float outer = path.roadWidth * 0.5f + VergeUseM - c.halfW;
                float across = (edge - c.latNow) * side;
                // Only outward, only as far as the verge, and only with the
                // road to make the move in before the racer is reached.
                if (side * leaderRacerLat < side * c.latNow + 0.5f && side * edge <= outer &&
                    gap > Mathf.Max(across, 0f) / MaxCrabTan + 1f)
                {
                    // Held out there until it is past the racer: once it is
                    // over far enough the racer is not "ahead" any more, and
                    // the lane centre would bring it straight back across.
                    c.edgeLat = edge;
                    c.edgeEndS = Wrap(c.s + c.dir * (gap + 4.5f + EdgePastM));
                    v = Mathf.Max(v, EdgeCreepMps);
                }
            }
            if (!float.IsNaN(c.edgeLat))
            {
                if (Delta(c.s, c.edgeEndS) * c.dir > 0f)
                {
                    float side = c.lat >= 0f ? 1f : -1f;
                    latTarget = side * Mathf.Max(side * latTarget, side * c.edgeLat);
                    v = Mathf.Min(v, EdgePassMps);
                }
                else c.edgeLat = float.NaN;
            }
            MoveAcross(c, latTarget, dt);
            c.speed = Mathf.MoveTowards(c.speed, v, (v > c.speed ? 2.5f : decel) * dt);

            c.s = Wrap(c.s + c.speed * dt * c.dir);
            Place(c, false);
            SpinWheels(c, c.speed, dt);
        }

        /// <summary>Move the car across the road toward <paramref name="latTarget"/>
        /// the way a driver steers there: its sideways speed ramps up at
        /// LaneShiftAccel and comes down in time to stop on the mark, capped at
        /// LaneShiftMps and at what its heading allows at its speed.</summary>
        static void MoveAcross(Car c, float latTarget, float dt)
        {
            float d = latTarget - c.latNow;
            float stopIn = Mathf.Sqrt(2f * LaneShiftAccel * 0.8f * Mathf.Abs(d));
            float want = Mathf.Sign(d) * Mathf.Min(LaneShiftMps, stopIn, Mathf.Max(c.speed, 0f) * MaxCrabTan);
            c.latVel = Mathf.MoveTowards(c.latVel, want, LaneShiftAccel * dt);
            if (Mathf.Abs(d) < 0.005f && Mathf.Abs(c.latVel) < LaneShiftAccel * dt) { c.latVel = 0f; c.latNow = latTarget; return; }
            c.latNow += c.latVel * dt;
        }

        /// <summary>Put the car at its lane point: the velocity that reaches it
        /// this step (so the solver sees a real moving body), facing where it
        /// is going - along the lane, turned by any move across it - sitting
        /// on the road surface.</summary>
        void Place(Car c, bool teleport)
        {
            Sample(c.s, out Vector3 p, out Vector3 tan);
            Vector3 fwd = tan * c.dir;
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            Vector3 target = p + right * c.latNow;
            // The tarmac, where there is a Road collider; the datum plus the
            // builder's road lift where there is not.
            float y = p.y + 0.12f;
            // Pulled onto the verge, the strip it is on: a stage's gravel
            // shoulder is not on the Road layer (it does not grip like tarmac).
            int mask = Mathf.Abs(c.latNow) > path.roadWidth * 0.5f ? roadMask | 1 : roadMask;
            if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, mask,
                                QueryTriggerInteraction.Ignore))
                y = hit.point.y;
            target.y = y;
            // Its heading: along the lane, turned toward the side it is moving
            // to by as much as the move takes at this speed. The DRAWN body
            // turns; the box stays square to the lane. A turned box swings a
            // corner toward the centreline (0.3 m at 7 degrees on a 4.8 m car),
            // and a racer passing an oncoming car at its planned air met that
            // corner head-on (the first runs with it: head-ons 0.25 -> 0.9 a
            // race, every one on a straight, 2.0-2.2 m between centres).
            Vector3 head = fwd * Mathf.Max(c.speed, 0.5f) + right * c.latVel;
            var rot = Quaternion.LookRotation(head, Vector3.up);
            var laneRot = Quaternion.LookRotation(fwd, Vector3.up);
            if (c.shell != null)
                c.shell.localRotation = Quaternion.Euler(0f, Mathf.DeltaAngle(laneRot.eulerAngles.y, rot.eulerAngles.y), 0f) * c.shellRest;
            // The front wheels: the angle that makes this yaw rate at this
            // speed (tan = wheelbase x yaw rate / speed), eased.
            float yaw = rot.eulerAngles.y;
            if (c.haveYaw && !teleport && c.speed > 0.5f)
            {
                float rate = Mathf.DeltaAngle(c.yawWas, yaw) * Mathf.Deg2Rad / Time.fixedDeltaTime;
                float want = Mathf.Clamp(Mathf.Atan(c.wheelBase * rate / c.speed) * Mathf.Rad2Deg, -MaxSteerDeg, MaxSteerDeg);
                c.steer = Mathf.Lerp(c.steer, want, 1f - Mathf.Exp(-SteerFollow * Time.fixedDeltaTime));
            }
            c.yawWas = yaw; c.haveYaw = true;
            if (teleport)
            {
                c.rb.position = target; c.rb.rotation = laneRot;
                c.go.transform.SetPositionAndRotation(target, laneRot);
                return;
            }
            c.rb.linearVelocity = c.commanded = (target - c.rb.position) / Time.fixedDeltaTime;
            c.hasCommand = true;
            c.rb.MoveRotation(laneRot);
        }

        /// <summary>Distance to the nearest thing ahead in this car's lane, and
        /// its speed along the lane.</summary>
        float LeaderGap(Car c, out float leaderV, out float racerLat)
        {
            float best = float.MaxValue; leaderV = 0f; racerLat = float.NaN;
            foreach (var o in live)
            {
                if (o == c) continue;
                if (o.wrecked)
                {
                    float sw = SOf(o.go.transform.position, ref o.hint);
                    float dw = Delta(c.s, sw) * c.dir;
                    if (dw > 0f && dw < best && LateralNear(o.go.transform.position, sw, c.latNow)) { best = dw; leaderV = 0f; }
                    continue;
                }
                if (o.dir != c.dir || Mathf.Abs(o.lat - c.lat) > 1.5f) continue;
                float d = Delta(c.s, o.s) * c.dir;
                if (d > 0f && d < best) { best = d; leaderV = o.speed; }
            }
            foreach (var f in field)
            {
                float d = Delta(c.s, f.s) * c.dir;
                if (d <= 0f || d >= best || d > 120f) continue;
                // Where this car IS across the road: pulled onto the verge, a
                // rival parked there is ahead of it and one going by in the lane
                // is not.
                if (!LateralNear(f.pos, f.s, c.latNow)) continue;
                best = d;
                Sample(f.s, out Vector3 fp, out Vector3 ft);
                racerLat = Vector3.Dot(f.pos - fp, Vector3.Cross(Vector3.up, ft).normalized);
                Sample(f.s, out _, out Vector3 tan);
                leaderV = Mathf.Max(0f, Vector3.Dot(f.vel, tan * c.dir));
            }
            // Bumper to bumper, roughly.
            return best - 4.5f;
        }

        static Temper RollTemper()
        {
            float r = Random.value;
            for (int i = 0; i < TemperOdds.Length; i++)
            {
                if (r < TemperOdds[i]) return (Temper)i;
                r -= TemperOdds[i];
            }
            return Temper.Normal;
        }

        /// <summary>Is a racer (or the player) coming at this car: closing
        /// from behind in its direction, or on its way toward it from ahead?
        /// <paramref name="sat"/>: one is SITTING behind it in its lane (it
        /// closed, and is held up - it has not stopped wanting by). Also runs
        /// the car's <c>tailed</c> clock, and says where across the road the
        /// nearest racer behind it is.</summary>
        bool RacerComing(Car c, float dt, out float racerLat, out bool sat)
        {
            racerLat = float.NaN;
            sat = false;
            Sample(c.s, out Vector3 p0, out Vector3 tan);
            Vector3 fwd = tan * c.dir;
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            bool coming = false;
            float nearest = float.MaxValue;
            foreach (var f in field)
            {
                float d = Delta(c.s, f.s) * c.dir;
                float vAlong = Vector3.Dot(f.vel, fwd);
                if (d < -2f && d > -NoticeBehindM)
                {
                    bool closing = vAlong > c.speed + 4f;
                    // Behind in this car's own lane at its speed: a racer that
                    // has caught it and cannot get by.
                    bool behindClose = d > -TailedNearM && vAlong > 3f &&
                                       Mathf.Abs(Vector3.Dot(f.pos - p0, right) - c.latNow) < 2.2f;
                    if (closing || behindClose)
                    {
                        if (closing) coming = true;
                        if (behindClose) sat = true;
                        if (-d < nearest) { nearest = -d; racerLat = Vector3.Dot(f.pos - p0, right); }
                    }
                }
                if (d > 2f && d < NoticeAheadM && vAlong < -3f) coming = true;
            }
            c.tailed = sat ? c.tailed + dt : Mathf.Max(0f, c.tailed - dt * 0.5f);
            return coming;
        }

        /// <summary>
        /// Where a driver letting a racer by puts itself across the road: onto
        /// its own verge - outer side <see cref="VergeUseM"/> past the tarmac
        /// edge - UNLESS the racer behind is already out on that side, going
        /// round it on the shoulder; then it holds its lane and eases a little
        /// toward the centreline instead of shutting the gap on it.
        /// </summary>
        float PullOverLat(Car c, float racerLat)
        {
            float side = c.lat >= 0f ? 1f : -1f;
            float outer = path.roadWidth * 0.5f + VergeUseM - c.halfW;
            if (!float.IsNaN(racerLat) && (racerLat - c.latNow) * side > 0.6f)
            {
                float inner = c.halfW + 0.25f;
                return side * Mathf.Max(inner, Mathf.Abs(c.lat) - 0.4f);
            }
            // A lane's width over at most: on a 12 m street the verge is the
            // kerb four metres away, and a car does not cross a whole lane to
            // let someone by.
            return side * Mathf.Max(Mathf.Abs(c.lat), Mathf.Min(outer, Mathf.Abs(c.lat) + PullOverMaxM));
        }
        /// <summary>The most a driver moves across to let a racer by.</summary>
        const float PullOverMaxM = 1.6f;

        /// <summary>Room a racer needs between the centreline and a car pulled
        /// over, to go by inside its own lane: the racers' own-lane rule
        /// (centre 1.0 m right of the line), a half-width, and air.</summary>
        const float RoomToPassM = 1.0f + 0.9f + 0.35f;

        /// <summary>
        /// Does pulling over leave a racer room to get by inside its lane? A
        /// Crown Vic on the verge of a 6.4 m road still leaves 2.05 m, not
        /// the 2.25 a racer needs - and a car that had pulled over AND slowed
        /// to six-tenths without making the room was only a slower car to be
        /// stuck behind (and one a racer tried to squeeze past anyway, at 20
        /// m/s: Blue Ridge, rush hour). Such a driver keeps its lane and its
        /// speed; the racer passes it in the other lane when that is clear.
        /// </summary>
        bool PullOverMakesRoom(Car c)
        {
            float outer = Mathf.Min(path.roadWidth * 0.5f + VergeUseM - c.halfW, Mathf.Abs(c.lat) + PullOverMaxM);
            return outer - c.halfW >= RoomToPassM;
        }

        bool LateralNear(Vector3 pos, float s, float lat)
        {
            Sample(s, out Vector3 p, out Vector3 tan);
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            float l = Vector3.Dot(pos - p, right);
            return Mathf.Abs(l - lat) < 1.9f;
        }

        void SpinWheels(Car c, float v, float dt)
        {
            if (c.wheels.Length == 0) return;
            c.spin = Mathf.Repeat(c.spin + v / c.wheelR * Mathf.Rad2Deg * dt, 360f);
            PoseWheels(c);
        }

        static void PoseWheels(Car c)
        {
            for (int i = 0; i < c.wheels.Length; i++)
            {
                bool left = c.wheels[i].name.EndsWith("0") || c.wheels[i].name.EndsWith("2");
                float steer = c.front != null && i < c.front.Length && c.front[i] ? c.steer : 0f;
                c.wheels[i].localRotation = Quaternion.Euler(0f, steer + (left ? 180f : 0f), 0f)
                                          * Quaternion.Euler(left ? -c.spin : c.spin, 0f, 0f);
            }
        }

        /// <summary>A traffic car was hit (TrafficContact). It stops driving for
        /// good and becomes a physics object: whatever the hit did, it keeps.</summary>
        /// <summary>What each wreck hit, for the play check.</summary>
        public readonly List<string> WreckLog = new List<string>();
        /// <summary>How many cars of each Temper have been born this race
        /// (for TrafficPlayCheck).</summary>
        public readonly int[] TempersSeen = new int[4];

        internal void Wreck(GameObject go, string by = null)
        {
            var c = live.Find(x => x.go == go);
            if (c == null || c.wrecked) return;
            c.wrecked = true;
            c.wreckedAt = Time.time;
            // THE BOX DOWN TO THE TYRES. Driving, it stops 22 cm above the
            // road so no crest can graze it; a wreck landed on that and settled
            // INTO the road - wheels buried to the hubs (owner, 2026-09-25:
            // "traffic gets stuck in the road (literally)"). The body is at
            // ride height when it is hit, so this box starts just above the
            // tarmac, not in it.
            c.box.center = c.fullCenter; c.box.size = c.fullSize;
            WreckLog.Add((c.dir > 0 ? "with" : "oncoming") + " hit by " + (by ?? "?"));
            c.rb.useGravity = true;
        }

        internal void NoteContact(GameObject go, string what)
        {
            var c = live.Find(x => x.go == go);
            if (c != null) c.lastContact = what;
        }

        // ---- the replay ---------------------------------------------------------
        /// <summary>One pool car on one recorder step.</summary>
        public struct Pose
        {
            public Vector3 pos;
            public Quaternion rot;
            public float spin;
            public bool on;
        }

        /// <summary>The pool is built up front and never grows, so pool index
        /// j is the same car for the whole race.</summary>
        public int PoolCount => pool.Count;
        public Transform PoolCar(int j) => pool[j].go.transform;
        public bool PoolOn(int j) => pool[j].go.activeSelf;

        /// <summary>Every pool car's pose, in pool order, off the physics step
        /// the recorder is sampling (the body's pose, not the interpolated
        /// transform).</summary>
        public void Capture(List<Pose> into)
        {
            foreach (var c in pool)
                into.Add(c.active
                    ? new Pose { pos = c.rb.position, rot = c.rb.rotation, spin = c.spin, on = true }
                    : new Pose { rot = Quaternion.identity });
        }

        struct Saved
        {
            public bool on, kinematic;
            public Vector3 pos, vel, angVel;
            public Quaternion rot;
            public float spin;
        }
        Saved[] saved;

        /// <summary>Freeze the pool for a replay: every body kinematic (a
        /// dynamic body the replay writes a pose into is one the solver takes
        /// back, and a wreck's gravity would pull it through its recording),
        /// its state kept to be put back.</summary>
        public void BeginReplay()
        {
            if (replaying) return;
            replaying = true;
            Obstacles.Clear();
            saved = new Saved[pool.Count];
            for (int j = 0; j < pool.Count; j++)
            {
                var c = pool[j];
                saved[j] = new Saved
                {
                    on = c.go.activeSelf, kinematic = c.rb.isKinematic,
                    pos = c.rb.position, rot = c.rb.rotation, spin = c.spin,
                    vel = c.rb.isKinematic ? Vector3.zero : c.rb.linearVelocity,
                    angVel = c.rb.isKinematic ? Vector3.zero : c.rb.angularVelocity,
                };
                if (!c.rb.isKinematic) { c.rb.linearVelocity = Vector3.zero; c.rb.angularVelocity = Vector3.zero; }
                c.rb.isKinematic = true;
            }
        }

        /// <summary>Pose pool car j as the recording has it. A car appearing,
        /// or one the replay jumps (a seek, a recycled car reborn elsewhere),
        /// is placed outright; otherwise it is moved on the step so the
        /// interpolation the racers are drawn with draws it too.</summary>
        public void ShowReplay(int j, bool on, Vector3 pos, Quaternion rot, float spin, bool teleport)
        {
            if (!replaying || j < 0 || j >= pool.Count) return;
            var c = pool[j];
            if (!on) { if (c.go.activeSelf) c.go.SetActive(false); return; }
            if (!c.go.activeSelf) { c.go.SetActive(true); teleport = true; }
            if (teleport) Put(c, pos, rot);
            else { c.rb.MovePosition(pos); c.rb.MoveRotation(rot); }
            c.spin = spin;
            PoseWheels(c);
        }

        /// <summary>Put the pool back exactly as the replay found it.</summary>
        public void EndReplay()
        {
            if (!replaying) return;
            for (int j = 0; j < pool.Count && saved != null; j++)
            {
                var c = pool[j];
                var s = saved[j];
                c.go.SetActive(s.on);
                Put(c, s.pos, s.rot);
                c.rb.isKinematic = s.kinematic;
                if (!s.kinematic) { c.rb.linearVelocity = s.vel; c.rb.angularVelocity = s.angVel; }
                c.spin = s.spin;
                PoseWheels(c);
                // The first step back must not read the put-back as a HIT.
                c.hasCommand = false;
            }
            saved = null;
            replaying = false;
        }

        /// <summary>Place a body outright, dropping its interpolation history
        /// (a bare transform write is painted over by an interpolated body;
        /// see CarController.TeleportTo).</summary>
        static void Put(Car c, Vector3 pos, Quaternion rot)
        {
            var interp = c.rb.interpolation;
            c.rb.interpolation = RigidbodyInterpolation.None;
            c.rb.position = pos;
            c.rb.rotation = rot;
            c.go.transform.SetPositionAndRotation(pos, rot);
            c.rb.interpolation = interp;
        }
    }

    /// <summary>On each traffic car: a real hit ends its drive. Scrapes along
    /// the side of the player's car below WreckSpeed do not.</summary>
    public class TrafficContact : MonoBehaviour
    {
        void OnCollisionEnter(Collision col)
        {
            // Only NAMES the contact for the wreck log; whether it was a hit
            // is decided by the velocity it left behind (TrafficSystem.Drive).
            if (col.gameObject.layer == 8) return;
            TrafficSystem.Instance?.NoteContact(gameObject,
                col.gameObject.name + " (" + LayerMask.LayerToName(col.gameObject.layer) + ")");
        }
    }
}
