using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// The free-roam session: what RaceManager is to a circuit, this is to
    /// Charlotte — minus everything lap-shaped. It owns the rolling start,
    /// respawn onto the road graph, the street name the HUD shows, and the
    /// session ledger (metres, drift, fuel) that gets stamped into
    /// RaceHandoff when the player exits, so the LifeSim banks a free-roam
    /// drive with the same odometer/fuel/wear honesty as a race.
    ///
    /// AND THE CITY RACE. A venue with <c>routeId</c> set is one of the
    /// Charlotte street races (the 277 belt, Tryon, Independence) run on the
    /// streamed city itself rather than on a baked stage of its own: this
    /// component turns the route — an edge chain through the same graph
    /// FREE ROAM drives — into the TrackPath RaceManager races, stands the
    /// grid on it, and then steps aside. In that mode there is NO CityMode
    /// session: <see cref="Instance"/> stays null so DriveSession, the HUD
    /// and the pause menu all see a RaceManager race, exactly as on a circuit.
    /// </summary>
    public class CityMode : MonoBehaviour
    {
        public static CityMode Instance { get; private set; }

        public CarController player;
        /// <summary>The streamed city, when there is one. NULL in the town,
        /// which is a small baked map with no road graph — see
        /// <see cref="respawnPoints"/>.</summary>
        public CityWorld world;

        /// <summary>Where to put a stuck car back, on a map with no road graph.
        /// Empty is fine: the spawn point is always the last resort.</summary>
        public Transform[] respawnPoints = new Transform[0];

        /// <summary>What the HUD calls this place during the rolling start.</summary>
        public string venueName = "CHARLOTTE";
        public string VenueName => string.IsNullOrEmpty(venueName) ? "CHARLOTTE" : venueName;

        /// <summary>The city route this scene races, or empty for free roam.
        /// Baked by the scene builder from TrackDef.cityRoute.</summary>
        public string routeId = "";
        /// <summary>What the HUD calls the run (TrackDef.dragLabel).</summary>
        public string routeLabel = "";
        /// <summary>The AI cars the builder baked for a race, in grid order.</summary>
        public List<CarController> aiCars = new List<CarController>();

        public bool IsRace => !string.IsNullOrEmpty(routeId);

        /// <summary>Session control is live (car responds). Read by
        /// StuckRecovery through DriveSession.</summary>
        public bool Live { get; private set; }

        public float SessionSeconds { get; private set; }
        public float MetersDriven { get; private set; }
        public float DriftSeconds { get; private set; }
        public string CurrentStreet { get; private set; } = "";

        const float StartDelay = 1.6f;
        const float DriftWearMinSpeed = 4f;
        const float GridFrontM = 9f, GridRowM = 6.5f, GridLateralM = 2.6f, GridLiftM = 0.35f;

        float startTimer;
        float streetPoll;
        Vector3 spawnPos;
        Quaternion spawnRot;
        bool stamped;

        /// <summary>The car came in THROUGH THE ZONE LINE, already rolling.</summary>
        bool arrivedRolling;
        const float RollingStartDelay = 0.3f;

        void Awake()
        {
            if (IsRace)
            {
                // A race, not a session. Build the path and the grid before
                // any Start runs (RaceManager.Start reads both), then hand
                // the scene to RaceManager by never registering as the
                // session — a CityMode.Instance here would hijack respawn
                // and the HUD.
                SetupRace();
                enabled = false;
                return;
            }
            Instance = this;
            arrivedRolling = Town.TownEdge.ArrivePending;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (player != null)
            {
                spawnPos = player.transform.position;
                spawnRot = player.transform.rotation;
                var input = player.GetComponent<PlayerCarInput>();
                if (input != null) input.inputEnabled = false;
                var engine = player.GetComponent<EngineAudio>();
                if (engine != null && !arrivedRolling) engine.PlayStartup(0.3f);
            }
        }

        void Update()
        {
            if (player == null) return;

            if (!Live)
            {
                startTimer += Time.deltaTime;
                if (startTimer >= (arrivedRolling ? RollingStartDelay : StartDelay))
                {
                    Live = true;
                    var input = player.GetComponent<PlayerCarInput>();
                    if (input != null) input.inputEnabled = true;
                }
                return;
            }

            SessionSeconds += Time.deltaTime;
            MetersDriven += Mathf.Abs(player.forwardSpeed) * Time.deltaTime;
            if (player.Drifting && Mathf.Abs(player.forwardSpeed) > DriftWearMinSpeed)
                DriftSeconds += Time.deltaTime;

            streetPoll -= Time.deltaTime;
            if (streetPoll <= 0f && world != null)
            {
                streetPoll = 0.5f;
                var name = world.StreetNameAt(player.transform.position);
                if (!string.IsNullOrEmpty(name)) CurrentStreet = name;
            }
        }

        // ==================================================================
        //  The city race
        // ==================================================================
        /// <summary>
        /// Turn the route into a TrackPath and stand the field on it.
        ///
        /// The waypoints are an arc-length resample of the route's edge chain
        /// at TrackCatalog.Spacing with the CITY's solved heights — the same
        /// stations the tiles are built from, so the race line sits on the
        /// tarmac it will be driven over. A loop rotates so waypoint 0 is the
        /// start line; a route with ends keeps its lead-in before the line
        /// and its shutdown past the finish, exactly as a stage bake does, so
        /// RaceManager, the reverse twin and the HUD's map need no new rules.
        /// </summary>
        void SetupRace()
        {
            var map = CityMap.Get();
            var route = map?.RouteById(routeId);
            var path = Object.FindFirstObjectByType<TrackPath>(FindObjectsInactive.Include);
            if (route == null || path == null)
            {
                Debug.LogError("[City] race route '" + routeId + "' not found in the city data — " +
                               (path == null ? "and no TrackPath in the scene" : "the scene has a TrackPath"));
                return;
            }

            BuildPath(map, route, path);

            var world = this.world != null ? this.world : Object.FindFirstObjectByType<CityWorld>();
            if (world != null)
                foreach (var ai in aiCars) if (ai != null) world.anchors.Add(ai.transform);

            // The grid: AI rows first in list order (the contract with
            // OpponentSpecIds), the player at the back — the builder's own
            // layout, measured back from the start line.
            var field = new List<CarController>(aiCars);
            if (player != null) field.Add(player);
            int lineIdx = route.loop ? 0 : Mathf.RoundToInt(route.startM / TrackCatalog.Spacing);
            for (int row = 0; row < field.Count; row++)
            {
                var car = field[row];
                if (car == null) continue;
                float back = GridFrontM + row * GridRowM;
                float fIdx = lineIdx - back / path.spacing;
                if (path.HasEnds) fIdx = Mathf.Clamp(fIdx, 0f, path.Count - 1.001f);
                int i0 = Mathf.FloorToInt(fIdx);
                float frac = fIdx - i0;
                int a = path.Wrap(i0), b = path.Wrap(i0 + 1);
                var centre = Vector3.Lerp(path.GetPoint(a), path.GetPoint(b), frac);
                var fwd = path.GetTangent(a); fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                float lateral = (row % 2 == 0) ? -GridLateralM : GridLateralM;
                car.TeleportTo(centre + right * lateral + Vector3.up * GridLiftM,
                               Quaternion.LookRotation(fwd, Vector3.up));
                car.GetComponent<AIDriver>()?.ReseedPath();
            }

            // The ground under the whole grid, now, before physics steps.
            if (world != null && player != null) world.EnsureRing(player.transform.position, 1);
        }

        /// <summary>Resample the chain and fill the TrackPath. Public and
        /// static so the self-test can build a route's path without a scene.</summary>
        public static void BuildPath(CityMap map, CityMap.Route route, TrackPath path)
        {
            float spacing = TrackCatalog.Spacing;
            // the chain as one polyline with cumulative arc positions
            var pts = new List<Vector2>(4096);
            var ys = new List<float>(4096);
            var arcs = new List<float>(4096);
            float acc = 0f;
            for (int k = 0; k < route.edges.Length; k++)
            {
                var e = map.edges[route.edges[k]];
                bool fwd = route.dirs[k] >= 0;
                int n = e.pts.Length;
                for (int i = 0; i < n; i++)
                {
                    int pi = fwd ? i : n - 1 - i;
                    if (k > 0 && i == 0) continue;   // shared node with the previous edge
                    var p = e.pts[pi];
                    if (pts.Count > 0) acc += Vector2.Distance(pts[pts.Count - 1], p);
                    pts.Add(p);
                    ys.Add(e.YAt(e.s[pi]));
                    arcs.Add(acc);
                }
            }
            float total = acc;
            if (route.loop && pts.Count > 1)
            {
                // close the ring so a sample past the last point wraps to the first
                acc += Vector2.Distance(pts[pts.Count - 1], pts[0]);
                pts.Add(pts[0]); ys.Add(ys[0]); arcs.Add(acc);
                total = acc;
            }

            // A loop divides its length into a whole number of stations so
            // the seam is one station like every other (floor left a 7 m gap
            // at the line on a 9195 m ring; the self-test holds it to 1.5
            // stations). The pitch is then 4 m to a tenth of a percent, and
            // the path carries the pitch it actually has. A route with ends
            // keeps the exact 4 m: its finish is an index times Spacing.
            int count = route.loop ? Mathf.Max(2, Mathf.RoundToInt(total / spacing))
                                   : Mathf.Max(2, Mathf.FloorToInt(total / spacing) + 1);
            float step = route.loop ? total / count : spacing;
            var wps = new Vector3[count];
            int seg = 0;
            for (int i = 0; i < count; i++)
            {
                float s = route.loop ? Mathf.Repeat(route.startM + i * step, total) : Mathf.Min(i * spacing, total);
                if (route.loop && i > 0 && s < arcs[seg]) seg = 0;   // wrapped past the seam
                while (seg < arcs.Count - 2 && arcs[seg + 1] < s) seg++;
                float segL = arcs[seg + 1] - arcs[seg];
                float t = segL > 1e-5f ? Mathf.Clamp01((s - arcs[seg]) / segL) : 0f;
                var p = Vector2.LerpUnclamped(pts[seg], pts[seg + 1], t);
                float y = Mathf.LerpUnclamped(ys[seg], ys[seg + 1], t);
                wps[i] = new Vector3(p.x, y, p.y);
            }

            // curvature, flattened and lightly smoothed — the builder's own
            // recipe, so the AI brakes for corners and not for hills
            int Idx(int i) => route.loop ? (i % count + count) % count : Mathf.Clamp(i, 0, count - 1);
            var curv = new float[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 a = wps[Idx(i - 1)]; a.y = 0f;
                Vector3 b = wps[i]; b.y = 0f;
                Vector3 c = wps[Idx(i + 1)]; c.y = 0f;
                float angle = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad;
                curv[i] = angle / step;
            }
            var smooth = new float[count];
            for (int i = 0; i < count; i++)
            {
                float sum = 0f;
                for (int o = -2; o <= 2; o++) sum += curv[Idx(i + o)];
                smooth[i] = sum / 5f;
            }

            path.waypoints = wps;
            path.curvatures = smooth;
            path.spacing = step;
            path.roadWidth = route.roadWidth;
            path.drag = false;
            path.pointToPoint = !route.loop;
            path.finishIndex = route.loop ? -1 : Mathf.RoundToInt(route.finishM / spacing);
            path.reversed = false;
        }

        /// <summary>
        /// Put a car back on the nearest street. The graph replaces the
        /// circuit's waypoint list: nearest non-ramp edge, facing whichever
        /// direction the car was already pointing along it.
        /// </summary>
        public void Respawn(CarController car)
        {
            if (car == null) return;
            if (world == null || world.Map == null) { RespawnOffGraph(car); return; }
            var map = world.Map;
            var p = car.transform.position;

            if (!map.NearestRoadPoint(new Vector2(p.x, p.z), 260f, skipLinks: true,
                    out int ei, out float at, out _) &&
                !map.NearestRoadPoint(new Vector2(p.x, p.z), 800f, skipLinks: false,
                    out ei, out at, out _))
            {
                car.ResetTo(spawnPos, spawnRot);
                return;
            }

            var e = map.edges[ei];
            var tan2 = e.TangentAt(at);
            var tan = new Vector3(tan2.x, 0f, tan2.y);
            // Which way round the street the car ends up pointing, and therefore
            // which way along the edge's own parameterisation "forward" is. A
            // one-way carriageway only has the one answer.
            bool along = e.oneway || Vector3.Dot(car.transform.forward, tan) >= 0f;
            if (!along) tan = -tan;
            var rot = Quaternion.LookRotation(tan, Vector3.up);

            // Walk ALONG the street until there is room: the nearest point on
            // the nearest road is also the nearest point to whatever the car
            // wedged itself against. Forward for the CAR.
            float dir = along ? 1f : -1f;
            for (int step = 0; step <= 12; step++)
            {
                float s = Mathf.Clamp(at + dir * step * 6f, 0f, e.length);
                var q = e.PointAt(s);
                if (DriveSession.TryPlace(car, new Vector3(q.x, e.YAt(s), q.y), rot)) return;
                if (s <= 0f || s >= e.length) break;
            }

            var pt = e.PointAt(at);
            car.ResetTo(new Vector3(pt.x, e.YAt(at) + 0.05f, pt.y), rot);
        }

        /// <summary>Put a car back with no road graph to put it back ONTO: the
        /// nearest authored respawn point that the car will actually FIT in,
        /// tried in order of distance, falling back to the spawn.</summary>
        void RespawnOffGraph(CarController car)
        {
            var from = car.transform.position;
            var best = new List<Transform>();
            foreach (var t in respawnPoints) if (t != null) best.Add(t);
            best.Sort((a, b) => (a.position - from).sqrMagnitude
                                .CompareTo((b.position - from).sqrMagnitude));
            foreach (var t in best)
                if (DriveSession.TryPlace(car, t.position + Vector3.up * 0.2f, t.rotation))
                    return;
            car.ResetTo(spawnPos, spawnRot);
        }

        /// <summary>
        /// Bank the session into the handoff on the way out (called by the
        /// pause menu's EXIT). Free roam has no finish line, so this is the
        /// one moment the LifeSim hears what the drive cost.
        /// </summary>
        public void StampExitResult()
        {
            if (stamped || player == null) return;
            stamped = true;

            RaceHandoff.FreeRoam = true;
            RaceHandoff.FreeRoamPlace = VenueName;
            RaceHandoff.ResultReady = true;
            RaceHandoff.FinishPos = 0;
            RaceHandoff.FieldSize = 0;
            RaceHandoff.RaceTimeSeconds = SessionSeconds;
            RaceHandoff.BestLapSeconds = 0f;
            RaceHandoff.MetersDriven = MetersDriven;
            RaceHandoff.DriftSeconds = DriftSeconds;

            var tank = player.GetComponent<FuelTank>();
            if (tank != null)
            {
                RaceHandoff.EndFuelPct = tank.percent;
                RaceHandoff.FuelReported = true;
            }
            var responder = player.GetComponent<CollisionResponder>();
            RaceHandoff.DamageScore = responder != null ? responder.DamageScore : 0f;
            RaceHandoff.HardHits = responder != null ? responder.HardHits : 0;
        }
    }
}
