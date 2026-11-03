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
        /// <summary>The spawn was seated from the graph (<see cref="SeatOnStreet"/>
        /// found a street), so its height is the road's own plus
        /// <see cref="SpawnLiftM"/>, not a baked guess.</summary>
        bool spawnSeated;
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
                spawnSeated = SeatOnStreet();
                // DRIVE TO THE START (the CITY race card): down on the race's
                // own road just short of its line, the gate ahead.
                if (RaceHandoff.RoamAtVenue >= 0 && CityRaceStarts.Enabled &&
                    CityRaceStarts.PlaceShort(world, player, RaceHandoff.RoamAtVenue))
                    spawnSeated = false;
                spawnPos = player.transform.position;
                spawnRot = player.transform.rotation;
                var input = player.GetComponent<PlayerCarInput>();
                if (input != null) input.inputEnabled = false;
                var engine = player.GetComponent<EngineAudio>();
                if (engine != null && !arrivedRolling) engine.PlayStartup(0.3f);
            }
            // Every city race's start line, standing in the street.
            CityRaceStarts.Attach(this);
            // The city's own traffic (free roam only: a race keeps TrafficSystem).
            CityTraffic.Attach(this);
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

        /// <summary>Metres above the solved road surface a car is set down
        /// at: the builder's own spawn lift, so the wheels settle rather
        /// than start inside the tarmac.</summary>
        const float SpawnLiftM = 0.45f;

        /// <summary>
        /// Put the player ON the street at load, whatever the scene says.
        ///
        /// The scene bakes the car at the spawn's solved height — solved from
        /// the DEM and the graph AS THEY WERE WHEN THE SCENE WAS BUILT. The
        /// city is solved again at every load from the data that ships, so a
        /// data change (the 2026-09-12 DEM alone moved the land by metres)
        /// leaves the baked pose in the air, or under the tarmac, until the
        /// next rebake — and "each time I free roam Charlotte my car is
        /// dropped from the sky" is what a stale bake reads as. The graph is
        /// the authority here as it is for a race grid: nearest street to the
        /// baked spot, the road's own solved height there, the tiles under it
        /// built first, and the pose written through TeleportTo so the
        /// interpolated body takes it.
        /// </summary>
        /// <returns>True if the car now stands on a street's solved height.</returns>
        /// <summary>
        /// The debug bench's MAP (owner, 2026-10-02: "select a part of the city
        /// from the map and teleport there"): the player onto the nearest
        /// through street to a plan point (metres) - any road at all within
        /// 2 km when no through street is within 400 m - on its lanes, at its
        /// solved height, pointing along it, the 3x3 of tiles round it built
        /// first so it lands on ground. In a race the lap count does not
        /// follow the car; it is a debug jump, not a shortcut.
        /// </summary>
        /// <param name="where">The road's name, for the page's note.</param>
        public bool DebugTeleport(Vector2 at, out string where)
        {
            where = null;
            if (world == null || world.Map == null || player == null) return false;
            var map = world.Map;
            if (!map.NearestRoadPoint(at, 400f, skipLinks: true, out int ei, out float s, out _) &&
                !map.NearestRoadPoint(at, 2000f, skipLinks: false, out ei, out s, out _))
                return false;
            var e = map.edges[ei];
            var q = LineModel.LanePoint(e, s);
            var t2 = e.TangentAt(s);
            var rot = Quaternion.LookRotation(new Vector3(t2.x, 0f, t2.y), Vector3.up);
            var pos = new Vector3(q.x, e.YAt(s), q.y);
            world.EnsureRing(pos, 1);
            player.ResetTo(pos, rot, seated: true);
            where = string.IsNullOrEmpty(e.name) ? "an unnamed road" : e.name;
            return true;
        }

        /// <summary>Where the player is, in plan metres (the MAP's car mark).</summary>
        public Vector2 PlayerPlan => player != null ? new Vector2(player.transform.position.x, player.transform.position.z) : Vector2.zero;

        bool SeatOnStreet()
        {
            if (world == null || world.Map == null) return false;   // the town: a baked map, its own spawn
            var map = world.Map;
            var p = player.transform.position;
            if (!map.NearestRoadPoint(new Vector2(p.x, p.z), 120f, skipLinks: true,
                    out int ei, out float at, out _) &&
                !map.NearestRoadPoint(new Vector2(p.x, p.z), 600f, skipLinks: false,
                    out ei, out at, out _))
                return false;
            var e = map.edges[ei];
            // on the lanes (the line model), not the OSM line under them
            var q = LineModel.LanePoint(e, at);
            var t2 = e.TangentAt(at);
            var fwd = new Vector3(t2.x, 0f, t2.y);
            // the way the scene pointed it, unless the street only goes one way
            if (!e.oneway && Vector3.Dot(player.transform.forward, fwd) < 0f) fwd = -fwd;
            var pos = new Vector3(q.x, e.YAt(at) + SpawnLiftM, q.y);
            world.EnsureRing(pos, 1);
            player.TeleportTo(pos, Quaternion.LookRotation(fwd, Vector3.up));
            return true;
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
            var venue = TrackCatalog.At(RaceHandoff.TrackIndex);
            if (venue != null && venue.IsDeckRun) { SetupDeckRun(venue); return; }
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
            int lineIdx = route.loop ? 0 : TrackCatalog.RouteIndex(route.startM);
            for (int slot = 0; slot < field.Count; slot++)
            {
                var car = field[slot];
                if (car == null) continue;
                // Two abreast (RaceField), the player's slot the last.
                float back = GridFrontM + RaceField.Row(slot) * GridRowM;
                float fIdx = lineIdx - back / path.spacing;
                if (path.HasEnds) fIdx = Mathf.Clamp(fIdx, 0f, path.Count - 1.001f);
                int i0 = Mathf.FloorToInt(fIdx);
                float frac = fIdx - i0;
                int a = path.Wrap(i0), b = path.Wrap(i0 + 1);
                var centre = Vector3.Lerp(path.GetPoint(a), path.GetPoint(b), frac);
                var fwd = path.GetTangent(a); fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                float lateral = RaceField.Side(slot) * Mathf.Min(GridLateralM, path.roadWidth * 0.25f);
                car.TeleportTo(centre + right * lateral + Vector3.up * GridLiftM,
                               Quaternion.LookRotation(fwd, Vector3.up));
                car.GetComponent<AIDriver>()?.ReseedPath();
            }

            // The ground under the whole grid, now, before physics steps.
            if (world != null && player != null) world.EnsureRing(player.transform.position, 1);
            // ...and the whole course while the countdown waits (CityWorld.CourseOn)
            if (world != null && CityWorld.CourseOn) world.PreloadCourse(path, CityWorld.CourseAround);
        }

        /// <summary>
        /// A DECK RUN in this scene: the route is set aside and the path is the
        /// deck's (DeckRun.BuildPath). The grid stands SINGLE FILE in the
        /// street's right-hand lane behind the line, and every rival drives its
        /// lane's centre (no spread: an aisle lane is a car wide).
        /// </summary>
        void SetupDeckRun(TrackCatalog.TrackDef venue)
        {
            var map = CityMap.Get();
            var path = Object.FindFirstObjectByType<TrackPath>(FindObjectsInactive.Include);
            var deck = DeckRun.Find(venue.deckWay);
            if (Application.isEditor) LogDeckRunLengths(map);
            if (!DeckRun.BuildPath(map, deck, path, out int lineIdx, venue.deckLeg))
            {
                Debug.LogError("[City] deck run " + venue.id + ": deck " + venue.deckWay + " is not drivable in this build" +
                               (deck != null ? " (" + deck.why + ")" : ""));
                return;
            }
            if (!string.IsNullOrEmpty(venue.dragLabel)) path.dragLabel = venue.dragLabel;   // the HUD's name for the run
            var world = this.world != null ? this.world : Object.FindFirstObjectByType<CityWorld>();
            // A DECK races four (RaceField): its roof grid has four places and
            // its aisles one lane. The rivals past the third go home now,
            // before physics steps - stood on the roof they would share the
            // last place.
            var rmDeck = Object.FindFirstObjectByType<RaceManager>();
            for (int i = aiCars.Count - 1; i >= RaceField.DragAndDeckCars - 1; i--)
            {
                var spare = aiCars[i];
                aiCars.RemoveAt(i);
                if (spare == null) continue;
                rmDeck?.allCars.Remove(spare);
                spare.gameObject.SetActive(false);
            }
            var field = new List<CarController>(aiCars);
            if (player != null) field.Add(player);
            for (int row = 0; row < field.Count; row++)
            {
                var car = field[row];
                if (car == null) continue;
                // alone against the clock, the player has the front row
                // (the field, retired at Start, stands behind it meanwhile)
                int slot = !RaceHandoff.Solo ? row : car == player ? 0 : row + 1;
                if (venue.deckLeg == DeckRun.Down)
                {
                    // DOWN: a standing start on the roof, 2x2 in the turning
                    // bay's aisle, every car facing the way down
                    DeckRun.RoofGrid(deck, Mathf.Min(slot, 3), out var rp, out var rr, out float gap);
                    car.TeleportTo(rp + Vector3.up * GridLiftM, rr);
                    if (slot >= 2) Debug.Log("[DeckRun] roof grid slot " + slot + " " + car.name + " at " + rp.ToString("0.0") +
                                             ", back wall " + gap.ToString("0.0") + " m behind its tail");
                }
                else
                {
                    float fIdx = Mathf.Clamp(lineIdx - (GridFrontM + slot * GridRowM) / path.spacing, 0f, path.Count - 1.001f);
                    int i0 = Mathf.FloorToInt(fIdx);
                    var centre = Vector3.Lerp(path.GetPoint(i0), path.GetPoint(i0 + 1), fIdx - i0);
                    var fwd = path.GetTangent(i0); fwd.y = 0f;
                    fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                    car.TeleportTo(centre + Vector3.up * GridLiftM, Quaternion.LookRotation(fwd, Vector3.up));
                }
                var ai = car.GetComponent<AIDriver>();
                if (ai != null) { ai.lateralOffset = 0f; ai.ReseedPath(); }
                if (world != null && car != player) world.anchors.Add(car.transform);
            }
            if (world != null && player != null) world.EnsureRing(player.transform.position, 1);
        }

        /// <summary>Editor only: every deck run's line-to-flag length, per run
        /// (TrackDef.deckRunM is this figure, kept as data for the menu).</summary>
        static void LogDeckRunLengths(CityMap map)
        {
            var sb = new System.Text.StringBuilder("[DeckRun] lengths (line to flag, m):");
            foreach (var t in TrackCatalog.All)
            {
                if (!t.IsDeckRun) continue;
                bool ok = DeckRun.BuildPath(map, DeckRun.Find(t.deckWay), null, out _, t.deckLeg);
                sb.Append(" " + t.id + "=" + (ok ? DeckRun.LastRunM.ToString("0") : "n/a") + " (menu " + t.deckRunM.ToString("0") + ")");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>Resample the chain and fill the TrackPath. Public and
        /// static so the self-test can build a route's path without a scene.</summary>
        /// <summary>The last path's length along its lanes over its length
        /// along the OSM line (route arcs are OSM arcs: the grid scales its
        /// start line by it).</summary>
        public static float LastArcScale = 1f;

        /// <summary>
        /// A TURN AT A JUNCTION IS DRIVEN ON A RADIUS (Five Points round 4,
        /// 2026-11-03). The lanes' centre of a route turns at a junction node
        /// as a corner: the in-lane's line runs to the node and the out-lane's
        /// leaves it. On a 110-125 degree turn (Five Points' right turns from
        /// Trade NB and Beatties Ford SB, the left from W 5th WB) an AIDriver
        /// following that corner at 20-28 km/h starts to turn only at the node
        /// and its turning circle carries it 6-8 m wide, onto the far corner's
        /// kerb. Where consecutive edges meet at a deflection of
        /// <see cref="TurnFilletMinDeg"/> or more, the corner is replaced by a
        /// circular arc of <see cref="TurnFilletR"/> tangent to both lanes
        /// (cubic Bezier), its tangent length capped at 45% of the leg on each
        /// side: the path a driver takes. The AI's curvature (and speed) is
        /// read off the arc. PSX_CITY_TURN_FILLET=0: the corner as before.
        /// </summary>
        public static bool TurnFilletOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TURN_FILLET") != "0";
        public const float TurnFilletR = 8f, TurnFilletMinDeg = 45f;
        /// <summary>Corners the last call replaced (for the logs).</summary>
        public static int LastTurnFillets;

        /// <summary>Fillets the corners of <paramref name="pts"/> at the given
        /// vertex indices (where a route passes from one edge to the next), in
        /// place; <paramref name="ys"/> (optional) is interpolated along each
        /// arc. Returns the number of corners filleted.</summary>
        public static int FilletTurns(List<Vector2> pts, List<float> ys, List<int> joins, float radius = TurnFilletR, float minDeg = TurnFilletMinDeg)
        {
            LastTurnFillets = 0;
            if (!TurnFilletOn || pts == null || pts.Count < 3 || joins == null || joins.Count == 0) return 0;
            int n = pts.Count;
            var arc = new float[n];
            for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            Vector2 At(float a, out Vector2 tan, out float y)
            {
                a = Mathf.Clamp(a, 0f, arc[n - 1]);
                int i = 0;
                while (i < n - 2 && arc[i + 1] < a) i++;
                while (i < n - 2 && arc[i + 1] - arc[i] < 1e-5f) i++;
                float L = arc[i + 1] - arc[i], t = L > 1e-5f ? Mathf.Clamp01((a - arc[i]) / L) : 0f;
                var d = pts[i + 1] - pts[i];
                tan = d.sqrMagnitude > 1e-10f ? d.normalized : Vector2.zero;
                y = ys != null ? Mathf.Lerp(ys[i], ys[i + 1], t) : 0f;
                return Vector2.Lerp(pts[i], pts[i + 1], t);
            }
            var js = new List<int>(joins);
            js.Sort();
            var cuts = new List<(float a0, float a1, Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1, float y0, float y1)>();
            float lastA1 = 0f;
            for (int k = 0; k < js.Count; k++)
            {
                int j = js[k];
                if (j <= 0 || j >= n - 1 || (k > 0 && js[k - 1] == j)) continue;
                float aJ = arc[j];
                float prev = k > 0 ? Mathf.Max(lastA1, arc[Mathf.Clamp(js[k - 1], 0, n - 1)]) : 0f;
                float next = k + 1 < js.Count ? arc[Mathf.Clamp(js[k + 1], 0, n - 1)] : arc[n - 1];
                float probe = Mathf.Min(4f, 0.45f * (aJ - prev), 0.45f * (next - aJ));
                if (probe < 0.5f) continue;
                var tIn = pts[j] - At(aJ - probe, out _, out _); var tOut = At(aJ + probe, out _, out _) - pts[j];
                if (tIn.sqrMagnitude < 1e-6f || tOut.sqrMagnitude < 1e-6f) continue;
                tIn.Normalize(); tOut.Normalize();
                float defl = Vector2.Angle(tIn, tOut);
                if (defl < minDeg || defl > 175f) continue;
                float half = 0.5f * defl * Mathf.Deg2Rad;
                float T = Mathf.Min(radius * Mathf.Tan(half), 0.45f * (aJ - prev), 0.45f * (next - aJ));
                if (T < 1f) continue;
                float r = T / Mathf.Tan(half);
                var p0 = At(aJ - T, out var t0, out float y0);
                var p1 = At(aJ + T, out var t1, out float y1);
                if (t0 == Vector2.zero) t0 = tIn;
                if (t1 == Vector2.zero) t1 = tOut;
                float h = 4f / 3f * Mathf.Tan(0.25f * defl * Mathf.Deg2Rad) * r;
                cuts.Add((aJ - T, aJ + T, p0, p0 + t0 * h, p1 - t1 * h, p1, y0, y1));
                lastA1 = aJ + T;
            }
            if (cuts.Count == 0) return 0;
            var np = new List<Vector2>(n + cuts.Count * 24);
            var ny = ys != null ? new List<float>(n + cuts.Count * 24) : null;
            int ci = 0;
            for (int i = 0; i < n; i++)
            {
                while (ci < cuts.Count && i < n && arc[i] >= cuts[ci].a0)
                {
                    var c = cuts[ci];
                    int m = Mathf.Max(6, Mathf.CeilToInt(c.a1 - c.a0));
                    for (int q = 0; q <= m; q++)
                    {
                        float u = q / (float)m, v = 1f - u;
                        np.Add(v * v * v * c.p0 + 3f * v * v * u * c.c0 + 3f * v * u * u * c.c1 + u * u * u * c.p1);
                        ny?.Add(Mathf.Lerp(c.y0, c.y1, u));
                    }
                    while (i < n && arc[i] <= c.a1) i++;
                    ci++;
                }
                if (i >= n) break;
                if (ci > 0 && arc[i] <= cuts[ci - 1].a1) continue;
                np.Add(pts[i]); ny?.Add(ys[i]);
            }
            pts.Clear(); pts.AddRange(np);
            if (ys != null) { ys.Clear(); ys.AddRange(ny); }
            LastTurnFillets = cuts.Count;
            return cuts.Count;
        }

        public static void BuildPath(CityMap map, CityMap.Route route, TrackPath path)
        {
            float spacing = TrackCatalog.Spacing;
            // the chain as one polyline with cumulative arc positions
            var pts = new List<Vector2>(4096);
            var ys = new List<float>(4096);
            var arcs = new List<float>(4096);
            float acc = 0f;
            var marks = new List<float>(64);
            var joins = new List<int>(route.edges.Length);
            for (int k = 0; k < route.edges.Length; k++)
            {
                var e = map.edges[route.edges[k]];
                bool fwd = route.dirs[k] >= 0;
                if (k > 0 && pts.Count > 0) joins.Add(pts.Count - 1);   // the node this edge leaves from
                // Every polyline vertex AND every elevation station, in
                // order along the edge. The height is solved at the 10 m
                // stations; sampled at the vertices alone, the path ran
                // straight between two OSM vertices 200 m apart while the
                // road under it humped and dipped, and the grid stood a
                // metre and a half in the air over the 277 (the whole field
                // was dropped onto the deck at the green).
                marks.Clear();
                for (int i = 0; i < e.s.Length; i++) marks.Add(e.s[i]);
                if (e.stS != null) for (int i = 0; i < e.stS.Length; i++) marks.Add(e.stS[i]);
                // and the drawn profile's knots, where its vertical curves bend
                e.ProfileKnots(marks, 0f, e.length, 0f);
                // and through its tapers, where the lanes' centre eases over
                LineModel.TaperSamples(e, 0f, e.length, marks);
                // and through a ramp's merge zone, where it drives the host's aux lane (L5)
                CityMeshes.ZoneSamples(e, 0f, e.length, marks);
                marks.Sort();
                int m = 0;
                for (int i = 1; i < marks.Count; i++)
                    if (marks[i] - marks[m] > 0.05f) marks[++m] = marks[i];
                marks.RemoveRange(m + 1, marks.Count - m - 1);
                int n = marks.Count;
                for (int i = 0; i < n; i++)
                {
                    float sAt = fwd ? marks[i] : marks[n - 1 - i];
                    if (k > 0 && i == 0) continue;   // shared node with the previous edge
                    // THE LANES' CENTRE (WP-11b line model): the race line,
                    // the grid, traffic's lanes and the AI's all measure from
                    // the path, so they drive the lanes the paint shows, off
                    // the OSM line where a lane was added on one side
                    var p = LineModel.LanePoint(e, sAt);
                    if (pts.Count > 0) acc += Vector2.Distance(pts[pts.Count - 1], p);
                    pts.Add(p);
                    ys.Add(e.YAt(sAt));
                    arcs.Add(acc);
                }
            }
            // Five Points round 4: a turn at a junction driven on a radius
            if (FilletTurns(pts, ys, joins) > 0)
            {
                arcs.Clear(); acc = 0f;
                for (int i = 0; i < pts.Count; i++) { if (i > 0) acc += Vector2.Distance(pts[i - 1], pts[i]); arcs.Add(acc); }
            }
            float total = acc;
            // the route's start and finish are arcs along its OSM line; the
            // lanes' centre runs a little longer or shorter (off the line on
            // bends, across a one-sided lane change): scale them onto it
            float osmTotal = 0f;
            for (int k = 0; k < route.edges.Length; k++) osmTotal += map.edges[route.edges[k]].length;
            float arcScale = osmTotal > 1f ? total / osmTotal : 1f;
            LastArcScale = arcScale;
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
            // the path carries the pitch it actually has.
            // A route with ends is indexed by its OSM arc, as the catalog's
            // bake is: waypoint i stands at OSM metre i * Spacing, mapped onto
            // the lanes' centre by the route's one scale, so the pitch is
            // Spacing * arcScale and a start or finish in OSM metres is the
            // same index here and in TrackCatalog (TrackCatalog.RouteIndex,
            // one definition). It was Spacing exactly with the finish at
            // round(finishM * arcScale / Spacing): when the drawn profile's
            // knots moved the lanes' length by a few metres the two rounded
            // to different waypoints (IndependenceSprint's self-test).
            int count = route.loop ? Mathf.Max(2, Mathf.RoundToInt(total / spacing))
                                   : Mathf.Max(2, Mathf.FloorToInt(total / (spacing * arcScale) + 1e-4f) + 1);
            float step = route.loop ? total / count : spacing * arcScale;
            var wps = new Vector3[count];
            int seg = 0;
            for (int i = 0; i < count; i++)
            {
                float s = route.loop ? Mathf.Repeat(route.startM * arcScale + i * step, total) : Mathf.Min(i * step, total);
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
            path.finishIndex = route.loop ? -1 : TrackCatalog.RouteIndex(route.finishM);
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
                ResetToSpawn(car);
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
                var q = LineModel.LanePoint(e, s);
                if (DriveSession.TryPlace(car, new Vector3(q.x, e.YAt(s), q.y), rot)) return;
                if (s <= 0f || s >= e.length) break;
            }

            // Nowhere with room: the nearest point anyway, at THIS street's
            // solved height. Seated, because the graph already says where the
            // tarmac is, and ResetTo's own probe from six metres up would find
            // the deck of any road crossing over this one first (a Charlotte
            // deck top is 5.55 m over the road beneath).
            var pt = LineModel.LanePoint(e, at);
            car.ResetTo(new Vector3(pt.x, e.YAt(at), pt.y), rot, seated: true);
        }

        /// <summary>Back to where the session started. Seated from the graph
        /// when <see cref="SeatOnStreet"/> put it there — the same overpass
        /// argument as the last resort in <see cref="Respawn"/> — and probed
        /// otherwise, because the town's spawn is only the pose it was baked
        /// at.</summary>
        void ResetToSpawn(CarController car)
        {
            if (spawnSeated) car.ResetTo(spawnPos - Vector3.up * SpawnLiftM, spawnRot, seated: true);
            else car.ResetTo(spawnPos, spawnRot);
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
            ResetToSpawn(car);
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
            // What the coolant gauge did. Stamped here as well as at the finish
            // line: most of the driving in this game is not a race, and an
            // engine cooked on the way into town has to cost the same as one
            // cooked on the last lap.
            EngineTemp.StampResult(player);
            var responder = player.GetComponent<CollisionResponder>();
            RaceHandoff.DamageScore = responder != null ? responder.DamageScore : 0f;
            RaceHandoff.HardHits = responder != null ? responder.HardHits : 0;
            RaceHandoff.BottomScore = responder != null ? responder.BottomScore : 0f;
            RaceHandoff.BottomWorst = responder != null ? responder.WorstBottom : 0f;
        }
    }
}
