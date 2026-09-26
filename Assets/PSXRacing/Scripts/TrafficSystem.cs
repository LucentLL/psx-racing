using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
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
        /// <summary>Cars per km, per direction, by TimeOfDay hour index
        /// (Dawn, Morning, Noon, Afternoon, Sunset, Dusk, Night). The two rush
        /// hours are busiest; late night is nearly empty.</summary>
        static readonly float[] PerKmByHour = { 2.5f, 6f, 4.5f, 6f, 4.5f, 2f, 0.8f };
        const float Behind = 200f, Ahead = 700f;
        /// <summary>No closer than this to any car in the lane, at birth.</summary>
        const float MinGap = 22f;
        const int PoolSize = 16;
        const float LaneM = City.RoadProfiles.LaneM;
        /// <summary>Lateral grip a traffic driver is willing to use in a bend.</summary>
        const float CornerAccel = 2.6f;
        /// <summary>Following distance at a stop, and the time gap above it.</summary>
        const float StopGap = 9f, TimeGap = 1.4f;
        /// <summary>A hit that changes the car's speed by this much (m/s) ends
        /// the drive.</summary>
        const float WreckSpeed = 1.5f;
        /// <summary>Gap between the collision box and the road, driving.</summary>
        const float RideClear = 0.22f;
        /// <summary>...and once it is a wreck.</summary>
        const float WreckFloor = 0.01f;

        /// <summary>The owner's traffic cars twice, then the pack's everyday
        /// shells for variety.</summary>
        static readonly string[] Keys =
        {
            "crown_victoria", "camry_2001", "ford_transit",
            "crown_victoria", "camry_2001", "ford_transit",
            "volvo_estate", "bmw_e30", "audi_saloon", "euro_hatch",
            "citroen_cx", "jdm_pickup", "landrover", "classic_van",
        };

        // ---- state -------------------------------------------------------------
        RaceManager rm;
        TrackPath path;
        float total;
        float perKm;
        /// <summary>Lateral lane centres, metres right of the centreline, per
        /// direction: +1 is the race direction, -1 against it.</summary>
        readonly List<float> lanesFwd = new List<float>(), lanesBack = new List<float>();
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

        class Car
        {
            public GameObject go;
            public Rigidbody rb;
            public Transform[] wheels;
            public float wheelR;
            public bool active, wrecked;
            public int dir;          // +1 with the race, -1 against
            public float lat;        // lane centre, m right of the centreline
            public float s;          // metres along the path
            public float speed, cruise;
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
            ahead = path.HasEnds ? Ahead : Mathf.Min(Ahead, total * 0.45f);
            behind = path.HasEnds ? Behind : Mathf.Min(Behind, total * 0.25f);

            int hour = Mathf.Clamp(TimeOfDay.Current, 0, PerKmByHour.Length - 1);
            perKm = PerKmByHour[hour];

            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            bool oneWay = def != null && def.oneWay;
            BuildLanes(path.roadWidth, oneWay);

            for (int i = 0; i < PoolSize; i++) pool.Add(Build(Keys[i % Keys.Length]));
        }

        /// <summary>Lane centres as the painter lays them out: US lanes packed
        /// from the centre line out, as many as fit with a 0.4 m shoulder.</summary>
        void BuildLanes(float width, bool oneWay)
        {
            lanesFwd.Clear(); lanesBack.Clear();
            if (oneWay)
            {
                int n = Mathf.Max(1, Mathf.FloorToInt((width - 0.8f) / LaneM));
                for (int k = 0; k < n; k++) lanesFwd.Add((k - (n - 1) * 0.5f) * LaneM);
                return;
            }
            int per = Mathf.Max(1, Mathf.FloorToInt((width - 0.8f) / (2f * LaneM)));
            for (int k = 0; k < per; k++)
            {
                lanesFwd.Add((k + 0.5f) * LaneM);      // right of the race direction
                lanesBack.Add(-(k + 0.5f) * LaneM);    // right of the oncoming direction
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
            float top = def.colliderCenter.y + def.colliderSize.y * 0.5f;
            float bottom = Mathf.Max(def.colliderCenter.y - def.colliderSize.y * 0.5f, RideClear);
            box.center = new Vector3(0f, (top + bottom) * 0.5f, 0f);
            box.size = new Vector3(def.colliderSize.x, Mathf.Max(0.3f, top - bottom), def.colliderSize.z);
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
            go.SetActive(false);
            return new Car
            {
                go = go, rb = rb, wheels = wheels.ToArray(), wheelR = Mathf.Max(0.2f, def.wheelRadius),
                box = box, rideCenter = box.center, rideSize = box.size,
                // A WRECK has no suspension to stand on, so its box reaches
                // down to the tyres' contact. The baked box stops at the sills,
                // like a driven car's: a wreck resting on that sat 35 cm into
                // the road.
                fullCenter = new Vector3(0f, (top + WreckFloor) * 0.5f, 0f),
                fullSize = new Vector3(def.colliderSize.x, top - WreckFloor, def.colliderSize.z),
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
            if (replaying || RaceReplay.Playing) { Obstacles.Clear(); return; }

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
            foreach (var c in live) Obstacles.Add(c.rb);
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
                    // Random gaps from just ahead of the grid to the far edge.
                    float d = 90f + Gap();
                    while (d < ahead && live.Count < PoolSize)
                    {
                        TrySpawn(Wrap(ps + d), dir, lat);
                        d += Gap();
                    }
                }
        }

        IEnumerable<int> Dirs()
        {
            yield return 1;
            if (lanesBack.Count > 0) yield return -1;
        }

        List<float> LanesFor(int dir) => dir > 0 ? lanesFwd : lanesBack;

        /// <summary>Exponential gap: RANDOM spacing with the hour's mean, never
        /// tighter than MinGap. Per LANE, so a wide road is not busier per lane.</summary>
        float Gap()
        {
            float mean = 1000f / Mathf.Max(0.05f, perKm);
            return MinGap + -Mathf.Log(1f - Random.value * 0.999f) * mean;
        }

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
                    if (!laneNextGap.TryGetValue(key, out float want)) want = laneNextGap[key] = Gap();
                    if (nearest >= want && TrySpawn(edge, dir, lat)) laneNextGap[key] = Gap();
                }
        }

        bool TrySpawn(float s, int dir, float lat)
        {
            if (path.HasEnds && (s < 8f || s > total - 8f)) return false;
            // Never on top of anything: another traffic car in the lane, or a
            // racer (they use the whole road).
            foreach (var c in live)
                if (Mathf.Abs(Delta(s, c.s)) < MinGap && Mathf.Abs(c.lat - lat) < 1.5f) return false;
            Sample(s, out Vector3 p0, out Vector3 t0);
            Vector3 at = p0 + Vector3.Cross(Vector3.up, t0).normalized * lat;
            foreach (var f in field)
                if ((f.pos - at).sqrMagnitude < MinGap * MinGap) return false;

            var free = pool.Find(x => !x.active);
            if (free == null) return false;
            free.active = true; free.wrecked = false; free.hasCommand = false; free.lastContact = null;
            free.box.center = free.rideCenter; free.box.size = free.rideSize;
            free.dir = dir; free.lat = lat; free.s = s; free.hint = -1;
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            float limit = (def != null ? def.speedLimitKmh : 80f) / 3.6f;
            free.cruise = limit * Random.Range(0.82f, 1.05f);
            free.speed = free.cruise;
            free.rb.useGravity = false;
            Place(free, true);
            free.go.SetActive(true);
            free.rb.linearVelocity = free.go.transform.forward * free.speed;
            free.rb.angularVelocity = Vector3.zero;
            live.Add(free);
            return true;
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
                bool keep = c.wrecked
                    ? !outOfWindow && !(Time.time - c.wreckedAt > 60f && d < -40f)
                    : !outOfWindow;
                if (keep) continue;
                c.active = false;
                c.go.SetActive(false);
                live.RemoveAt(i);
            }
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
            float v = c.cruise;
            float k = Curvature(c.s, c.dir);
            if (k > 1e-4f) v = Mathf.Min(v, Mathf.Sqrt(CornerAccel / k));
            float gap = LeaderGap(c, out float leaderV);
            if (gap < 120f)
                v = Mathf.Min(v, Mathf.Max(0f, leaderV + (gap - StopGap - leaderV * TimeGap) * 0.5f));
            // A driver, not a servo: 2.5 m/s^2 up, 6 down - and a full 9 when
            // what is ahead is closer than a 6 m/s^2 stop needs (a car ahead
            // just wrecked by a racer: at 6 the ones behind piled into it).
            float decel = gap < c.speed * c.speed / 12f + StopGap ? 9f : 6f;
            c.speed = Mathf.MoveTowards(c.speed, v, (v > c.speed ? 2.5f : decel) * dt);

            c.s = Wrap(c.s + c.speed * dt * c.dir);
            Place(c, false);
            SpinWheels(c, c.speed, dt);
        }

        /// <summary>Put the car at its lane point: the velocity that reaches it
        /// this step (so the solver sees a real moving body), facing along the
        /// lane, sitting on the road surface.</summary>
        void Place(Car c, bool teleport)
        {
            Sample(c.s, out Vector3 p, out Vector3 tan);
            Vector3 fwd = tan * c.dir;
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            Vector3 target = p + right * c.lat;
            // The tarmac, where there is a Road collider; the datum plus the
            // builder's road lift where there is not.
            float y = p.y + 0.12f;
            if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, roadMask,
                                QueryTriggerInteraction.Ignore))
                y = hit.point.y;
            target.y = y;
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            if (teleport)
            {
                c.rb.position = target; c.rb.rotation = rot;
                c.go.transform.SetPositionAndRotation(target, rot);
                return;
            }
            c.rb.linearVelocity = c.commanded = (target - c.rb.position) / Time.fixedDeltaTime;
            c.hasCommand = true;
            c.rb.MoveRotation(rot);
        }

        /// <summary>Distance to the nearest thing ahead in this car's lane, and
        /// its speed along the lane.</summary>
        float LeaderGap(Car c, out float leaderV)
        {
            float best = float.MaxValue; leaderV = 0f;
            foreach (var o in live)
            {
                if (o == c) continue;
                if (o.wrecked)
                {
                    float sw = SOf(o.go.transform.position, ref o.hint);
                    float dw = Delta(c.s, sw) * c.dir;
                    if (dw > 0f && dw < best && LateralNear(o.go.transform.position, sw, c.lat)) { best = dw; leaderV = 0f; }
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
                if (!LateralNear(f.pos, f.s, c.lat)) continue;
                best = d;
                Sample(f.s, out _, out Vector3 tan);
                leaderV = Mathf.Max(0f, Vector3.Dot(f.vel, tan * c.dir));
            }
            // Bumper to bumper, roughly.
            return best - 4.5f;
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
                c.wheels[i].localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f)
                                          * Quaternion.Euler(left ? -c.spin : c.spin, 0f, 0f);
            }
        }

        /// <summary>A traffic car was hit (TrafficContact). It stops driving for
        /// good and becomes a physics object: whatever the hit did, it keeps.</summary>
        /// <summary>What each wreck hit, for the play check.</summary>
        public readonly List<string> WreckLog = new List<string>();

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
