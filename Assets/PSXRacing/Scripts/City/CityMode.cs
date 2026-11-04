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
        public const float TurnFilletR = 6f, TurnFilletMinDeg = 45f, TurnFilletPastTrimM = 1f;
        /// <summary>Corners the last call replaced (for the logs).</summary>
        public static int LastTurnFillets;

        /// <summary>
        /// THE TURN PATH KEEPS OFF THE JUNCTION'S KERBS (round 4, second pass).
        /// Two faults in the first pass: the corner was read at the join's
        /// vertex, where a route moving from one road's lane to another's jumps
        /// sideways (Five Points' 124-126 degree turns measured 159-163, and the
        /// "6 m" arcs came out 1.6 m round, hugging the corner), and a fixed
        /// radius between two lane lines that meet only 4.3 m from a triangle
        /// island's point runs over it (R1b: 7-8 cm up onto the island; R2: 49
        /// cm down off the fan's chord). Now the corner is where the in-lane's
        /// and the out-lane's lines MEET, and the arc is the largest (up to the
        /// cap, no tighter than <see cref="TurnFilletMinR"/>; its tangent no
        /// longer than <see cref="TurnTangentMaxM"/>: a car turns as it enters
        /// the junction - begun 8-9 m out, the AI's own lookahead turned it in
        /// 15 m out, over the inside kerb, while 5-7 m drove both turns clean)
        /// that stays <see cref="TurnKerbClearM"/> off every kerb stretch of the
        /// fan's perimeter (<see cref="CityMeshes.FanKerbs"/>), or as far as the
        /// lanes' own corner does where that is less. A sharp right turn (from
        /// <see cref="TurnSwingSharpDeg"/>), or one where no such arc fits
        /// between the lanes' lines, swings out across its own lane first, as a
        /// driver sets up a tight turn - in steps of
        /// <see cref="TurnSwingStepM"/> up to the lane's room (passed per turn),
        /// eased over <see cref="TurnSwingEaseM"/> either side - and the arc is
        /// laid between the swung lines. No arc fits: the corner as before.
        /// PSX_CITY_TURN_KERBS=0: the first pass's rule (as 82e7fbd2).
        /// </summary>
        public static bool TurnKerbsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TURN_KERBS") != "0";
        public static float TurnKerbClearM = 1.4f, TurnFilletMinR = 2.5f, TurnSwingStepM = 0.3f, TurnSwingEaseM = 8f, TurnTangentMaxM = 6f, TurnSwingSharpDeg = 110f;
        /// <summary>A car's half width and a margin: what a turn's swing leaves
        /// between the car and its lane's outer edge.</summary>
        public const float TurnSwingKeepM = 1.15f;
        /// <summary>Round 5: the longest tangent a LEFT turn's arc may take (a left
        /// turn crosses the junction's width; at <see cref="TurnTangentMaxM"/> a
        /// 123 degree left was a 3.2 m arc, tighter than a car turns, and L3 at
        /// 28 km/h ran out of it onto Trade SB's verge, 2.9 cm; at 8 m it is a 4.3 m arc
        /// and L3 stays on the pavement, 1.5 cm). PSX_CITY_TURN_TLEFT.</summary>
        public static float TurnTangentLeftMaxM = float.TryParse(System.Environment.GetEnvironmentVariable("PSX_CITY_TURN_TLEFT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tl) ? tl : 8f;
        /// <summary>Test hook (RefugeLapCheck): a fixed tangent cap in metres
        /// over every join's own (0 = none).</summary>
        public static float TurnFilletCapM = 0f;
        /// <summary>What the last call decided at each turn (for the logs).</summary>
        public static readonly List<string> LastFilletNotes = new List<string>();

        static float KerbDist(Vector2 q, List<Vector2> segs)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < segs.Count; i += 2)
            {
                var a = segs[i]; var ab = segs[i + 1] - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
            }
            return best;
        }

        /// <summary>How far a turn from lane <paramref name="a"/> into lane
        /// <paramref name="b"/> may swing out across them: the narrower lane's
        /// half width less <see cref="TurnSwingKeepM"/>.</summary>
        public static float JoinSwingRoom(CityMap.Edge a, CityMap.Edge b)
        {
            if (a == null || b == null) return 0f;
            float la = a.width / Mathf.Max(1, a.lanes), lb = b.width / Mathf.Max(1, b.lanes);
            return Mathf.Max(0f, 0.5f * Mathf.Min(la, lb) - TurnSwingKeepM);
        }

        /// <summary>The kerb stretches of the fan at each join's node, for
        /// <see cref="FilletTurns"/>; null entries where there are none.</summary>
        public static List<Vector2> JoinKerbs(CityMap map, CityMeshes.Trims t, int node)
        {
            if (!TurnKerbsOn || t == null) return null;
            var segs = new List<Vector2>();
            CityMeshes.FanKerbs(map, t, node, segs);
            return segs.Count >= 2 ? segs : null;
        }

        /// <summary>Fillets the corners of <paramref name="pts"/> at the given
        /// vertex indices (where a route passes from one edge to the next), in
        /// place; <paramref name="ys"/> (optional) is interpolated along each
        /// arc; capT, kerbs and swing (optional) are per join: the tangent cap, the fan's kerb stretches and the lane's room to swing out. Returns the number of corners filleted.</summary>
        public static int FilletTurns(List<Vector2> pts, List<float> ys, List<int> joins, float radius = TurnFilletR, float minDeg = TurnFilletMinDeg, List<float> capT = null, List<List<Vector2>> kerbs = null, List<float> swing = null)
        {
            LastTurnFillets = 0;
            LastFilletNotes.Clear();
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
            // each cut: the arc range it replaces and the points (and heights) laid there
            var cuts = new List<(float a0, float a1, List<Vector2> p, List<float> y)>();
            float lastA1 = 0f;
            Vector2 Bez(Vector2 b0, Vector2 b1, Vector2 b2, Vector2 b3, float u) { float v = 1f - u; return v * v * v * b0 + 3f * v * v * u * b1 + 3f * v * u * u * b2 + u * u * u * b3; }
            float Cr(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
            for (int k = 0; k < js.Count; k++)
            {
                int j = js[k];
                if (j <= 0 || j >= n - 1 || (k > 0 && js[k - 1] == j)) continue;
                float aJ = arc[j];
                // the room either side: the last arc's end and the route's (a join
                // where the route goes straight on bounds nothing - L3's turn off the
                // 4.6 m median link was held to a 2 m tangent by the link's own start)
                float prev = TurnKerbsOn ? lastA1 : k > 0 ? Mathf.Max(lastA1, arc[Mathf.Clamp(js[k - 1], 0, n - 1)]) : 0f;
                float next = TurnKerbsOn ? arc[n - 1] : k + 1 < js.Count ? arc[Mathf.Clamp(js[k + 1], 0, n - 1)] : arc[n - 1];
                float probe = Mathf.Min(4f, 0.45f * (aJ - prev), 0.45f * (next - aJ));
                if (probe < 0.5f) continue;
                var ks = kerbs != null && k < kerbs.Count ? kerbs[k] : null;
                if (!TurnKerbsOn)
                {
                    // as 82e7fbd2: the corner at the join's own vertex
                    var tIn0 = pts[j] - At(aJ - probe, out _, out _); var tOut0 = At(aJ + probe, out _, out _) - pts[j];
                    if (tIn0.sqrMagnitude < 1e-6f || tOut0.sqrMagnitude < 1e-6f) continue;
                    tIn0.Normalize(); tOut0.Normalize();
                    float defl0 = Vector2.Angle(tIn0, tOut0);
                    if (defl0 < minDeg || defl0 > 175f) continue;
                    float half0 = 0.5f * defl0 * Mathf.Deg2Rad;
                    float T0 = Mathf.Min(radius * Mathf.Tan(half0), 0.45f * (aJ - prev), 0.45f * (next - aJ));
                    if (capT != null && k < capT.Count && capT[k] > 0f) T0 = Mathf.Min(T0, capT[k]);
                    if (TurnFilletCapM > 0f) T0 = Mathf.Min(T0, TurnFilletCapM);
                    if (T0 < 1f) continue;
                    float r0 = T0 / Mathf.Tan(half0);
                    var q0 = At(aJ - T0, out var u0, out float y0); var q1 = At(aJ + T0, out var u1, out float y1);
                    if (u0 == Vector2.zero) u0 = tIn0;
                    if (u1 == Vector2.zero) u1 = tOut0;
                    float h0 = 4f / 3f * Mathf.Tan(0.25f * defl0 * Mathf.Deg2Rad) * r0;
                    int m0 = Mathf.Max(6, Mathf.CeilToInt(2f * T0));
                    var lp0 = new List<Vector2>(m0 + 1); var ly0 = ys != null ? new List<float>(m0 + 1) : null;
                    for (int q = 0; q <= m0; q++) { lp0.Add(Bez(q0, q0 + u0 * h0, q1 - u1 * h0, q1, q / (float)m0)); ly0?.Add(Mathf.Lerp(y0, y1, q / (float)m0)); }
                    cuts.Add((aJ - T0, aJ + T0, lp0, ly0));
                    LastFilletNotes.Add($"turn {defl0:0} deg at {pts[j].x:0.0},{pts[j].y:0.0} (old rule) -> arc r {r0:0.0} m");
                    lastA1 = aJ + T0;
                    continue;
                }
                // THE TWO LANES' LINES: the in-lane's up to the join, the out-lane's
                // from its first point on. A route moving between the lanes of two
                // roads jumps sideways at the join, so the corner is where the two
                // lines MEET, not the join's vertex: read at the vertex, Five
                // Points' 125 degree turns measured 159-163 and their arcs came out
                // 1.6 m round.
                int j1 = j + 1; float aO = arc[j1];
                var tIn = pts[j] - At(aJ - probe, out _, out _);
                var tOut = At(Mathf.Min(aO + probe, next), out _, out _) - pts[j1];
                if (tIn.sqrMagnitude < 1e-6f || tOut.sqrMagnitude < 1e-6f) continue;
                tIn.Normalize(); tOut.Normalize();
                float defl = Vector2.Angle(tIn, tOut);
                if (defl < minDeg || defl > 175f) continue;
                float den = Cr(tIn, tOut);
                if (Mathf.Abs(den) < 1e-3f) continue;
                float tanH = Mathf.Tan(0.5f * defl * Mathf.Deg2Rad), hK = 4f / 3f * Mathf.Tan(0.25f * defl * Mathf.Deg2Rad);
                // outward: away from the turn's inside
                var oIn = -Mathf.Sign(den) * new Vector2(-tIn.y, tIn.x);
                var oOut = -Mathf.Sign(den) * new Vector2(-tOut.y, tOut.x);
                float Tcap = Mathf.Min(radius * tanH, den > 0f ? TurnTangentLeftMaxM : TurnTangentMaxM);
                if (capT != null && k < capT.Count && capT[k] > 0f) Tcap = Mathf.Min(Tcap, capT[k]);
                if (TurnFilletCapM > 0f) Tcap = Mathf.Min(Tcap, TurnFilletCapM);
                float room = swing != null && k < swing.Count ? Mathf.Max(0f, swing[k]) : 0f;
                string note = $"turn {defl:0} deg at {pts[j].x:0.0},{pts[j].y:0.0}: cap {Tcap:0.0} m, swing room {room:0.00} m";
                List<Vector2> bestP = null; List<float> bestY = null;
                float bestA0 = 0f, bestA1 = 0f, bestR = 0f, bestD = 0f, bestClr = 0f, lineClr = float.MaxValue;
                // the lanes' own corner's clearance over the span a turn may use
                if (ks != null)
                    for (float a = Mathf.Max(prev, aJ - Tcap - TurnSwingEaseM - 8f); a <= Mathf.Min(next, aO + Tcap + TurnSwingEaseM + 8f); a += 0.25f)
                        lineClr = Mathf.Min(lineClr, KerbDist(At(a, out _, out _), ks));
                // a sharp RIGHT turn swings out as far as its lane allows first (a
                // driver sets up wide round the kerb corner, and the car's own line
                // falls inside the path: R1b clipped the island's point with its
                // arc 2.1 m clear of it); a left turn has the junction's width
                // and swings only if nothing else fits (swung, L3 at 28 km/h ran
                // wide onto Trade SB's verge)
                int sMax = Mathf.Min(3, Mathf.FloorToInt(room / Mathf.Max(TurnSwingStepM, 0.05f) + 1e-3f));
                bool sharp = defl >= TurnSwingSharpDeg && den < 0f;   // a right turn (x east, z north)
                for (int st = 0; st <= sMax && bestP == null; st++)
                {
                    float d = (sharp ? sMax - st : st) * TurnSwingStepM;
                    // the lines offset outward by d meet at Pd
                    var bIn = pts[j] + oIn * d; var bOut = pts[j1] + oOut * d;
                    float uP = Cr(bOut - bIn, tOut) / den;          // Pd = bIn + tIn * uP
                    var Pd = bIn + tIn * uP;
                    float vP = Vector2.Dot(Pd - bOut, tOut);          // along the out-lane from its first point
                    if (Mathf.Abs(uP) > 8f || Mathf.Abs(vP) > 8f) continue;   // the lanes do not meet near the join
                    float ease = d > 0f ? TurnSwingEaseM : 0f;
                    for (float T = Tcap; T >= 1f - 1e-3f; T -= 0.25f)
                    {
                        float r = T / tanH;
                        if (r < TurnFilletMinR) break;
                        float a0 = aJ + uP - T, a1 = aO + vP + T;
                        if (a0 > aJ + 1e-3f || a1 < aO + 0.25f) continue;      // each tangent point on its own lane
                        float e0 = Mathf.Min(ease, a0 - prev - 0.5f), e1 = Mathf.Min(ease, next - 0.5f - a1);
                        if (a0 < prev + 0.5f || a1 > next - 0.5f || (d > 0f && (e0 < 3f || e1 < 3f))) continue;
                        var lp = new List<Vector2>(64); var ly = ys != null ? new List<float>(64) : null;
                        // ease out to the swing, the arc, ease back
                        if (d > 0f)
                            for (float a = a0 - e0; a < a0 - 1e-3f; a += 0.5f)
                            {
                                float f = Mathf.SmoothStep(0f, 1f, (a - (a0 - e0)) / e0);
                                lp.Add(At(a, out _, out float ya) + oIn * d * f); ly?.Add(ya);
                            }
                        var p0 = Pd - tIn * T; var p1 = Pd + tOut * T;
                        At(a0, out _, out float y0); At(a1, out _, out float y1);
                        float h = hK * r;
                        int m = Mathf.Max(8, Mathf.CeilToInt(2f * T));
                        for (int q = 0; q <= m; q++) { lp.Add(Bez(p0, p0 + tIn * h, p1 - tOut * h, p1, q / (float)m)); ly?.Add(Mathf.Lerp(y0, y1, q / (float)m)); }
                        if (d > 0f)
                            for (float a = a1 + 0.5f; a <= a1 + e1 + 1e-3f; a += 0.5f)
                            {
                                float f = Mathf.SmoothStep(0f, 1f, 1f - (a - a1) / e1);
                                lp.Add(At(a, out _, out float ya) + oOut * d * f); ly?.Add(ya);
                            }
                        float clr = float.MaxValue;
                        if (ks != null) foreach (var q in lp) clr = Mathf.Min(clr, KerbDist(q, ks));
                        if (ks == null || clr >= Mathf.Min(TurnKerbClearM, lineClr) - 0.02f)
                        {
                            bestP = lp; bestY = ly; bestA0 = d > 0f ? a0 - e0 : a0; bestA1 = d > 0f ? a1 + e1 : a1; bestR = r; bestD = d; bestClr = clr;
                            break;
                        }
                    }
                }
                string kn = ks != null ? $", kerbs {ks.Count / 2} (the lanes' corner {lineClr:0.00} m off them)" : "";
                if (bestP == null) { LastFilletNotes.Add(note + kn + $" -> no arc of {TurnFilletMinR:0.#} m or more fits"); continue; }
                LastFilletNotes.Add(note + kn + $" -> arc r {bestR:0.0} m, swung {bestD:0.00} m out, {(ks != null ? bestClr.ToString("0.00") : "-")} m off the kerbs");
                cuts.Add((bestA0, bestA1, bestP, bestY));
                lastA1 = bestA1;
            }
            if (cuts.Count == 0) return 0;
            var np = new List<Vector2>(n + cuts.Count * 48);
            var ny = ys != null ? new List<float>(n + cuts.Count * 48) : null;
            int ci = 0;
            for (int i = 0; i < n; i++)
            {
                while (ci < cuts.Count && i < n && arc[i] >= cuts[ci].a0)
                {
                    var c = cuts[ci];
                    np.AddRange(c.p);
                    if (ny != null) ny.AddRange(c.y);
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

        /// <summary>The longest tangent a turn at <paramref name="node"/> from
        /// <paramref name="a"/> into <paramref name="b"/> may have: the nearer
        /// arm's trim (where the junction's pavement starts) plus
        /// <see cref="TurnFilletPastTrimM"/>; 0 = no cap (no trims).</summary>
        /// <summary>
        /// Round 5: whether a route's turn at <paramref name="node"/> is laid on
        /// <see cref="FilletTurns"/>' arc - the junctions inside the Five Points
        /// circle (<see cref="CityMeshes.InR23Scope(Vector2)"/>, 90 m round
        /// (-3725, 6342)) only, so no other city race's path moves until the
        /// rule is proven there. PSX_CITY_R23_SCOPE=all: everywhere (as round 4).
        /// </summary>
        public static bool FilletAt(CityMap map, int node)
            => TurnFilletOn && node >= 0 && node < map.nodes.Length && CityMeshes.InR23Scope(map.nodes[node]);

        public static float JoinCap(CityMap map, CityMeshes.Trims t, CityMap.Edge a, CityMap.Edge b, int node)
        {
            if (t == null || a == null || b == null || t.atA == null) return 0f;
            float ta = a.a == node ? t.atA[a.index] : a.b == node ? t.atB[a.index] : -1f;
            float tb = b.a == node ? t.atA[b.index] : b.b == node ? t.atB[b.index] : -1f;
            if (ta < 0f || tb < 0f) return 0f;
            // a short link inside the junction complex (a median crossing) is the
            // junction's own pavement: the other arm's trim bounds the turn
            if (TurnKerbsOn)
            {
                bool ja = CityMeshes.IsJcl(map, a.index), jb = CityMeshes.IsJcl(map, b.index);
                if (ja && !jb) return Mathf.Max(1f, tb + TurnFilletPastTrimM);
                if (jb && !ja) return Mathf.Max(1f, ta + TurnFilletPastTrimM);
            }
            return Mathf.Max(1f, Mathf.Min(ta, tb) + TurnFilletPastTrimM);
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
            var caps = new List<float>(route.edges.Length);
            var kerbs = new List<List<Vector2>>(route.edges.Length);
            var swing = new List<float>(route.edges.Length);
            CityMeshes.Trims trimsNow = null;
            for (int k = 0; k < route.edges.Length; k++)
            {
                var e = map.edges[route.edges[k]];
                bool fwd = route.dirs[k] >= 0;
                // round 5: only Five Points' junctions are driven on the
                // fillet; every other route's path is the corner as before
                if (k > 0 && pts.Count > 0 && FilletAt(map, fwd ? e.a : e.b))
                {
                    int jn = fwd ? e.a : e.b;   // the node this edge leaves from
                    if (trimsNow == null) trimsNow = CityWorld.TrimsFor(map);
                    joins.Add(pts.Count - 1);
                    caps.Add(JoinCap(map, trimsNow, map.edges[route.edges[k - 1]], e, jn));
                    kerbs.Add(JoinKerbs(map, trimsNow, jn));
                    swing.Add(JoinSwingRoom(map.edges[route.edges[k - 1]], e));
                }
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
            if (FilletTurns(pts, ys, joins, TurnFilletR, TurnFilletMinDeg, caps, kerbs, swing) > 0)
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
