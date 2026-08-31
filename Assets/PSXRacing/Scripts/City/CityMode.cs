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
    /// </summary>
    public class CityMode : MonoBehaviour
    {
        public static CityMode Instance { get; private set; }

        public CarController player;
        /// <summary>The streamed city, when there is one. NULL in the town,
        /// which is a small baked map with no road graph — see
        /// <see cref="respawnPoints"/>.</summary>
        public CityWorld world;

        /// <summary>
        /// Where to put a stuck car back, on a map with no road graph.
        ///
        /// This exists because <see cref="DriveSession"/> resolves "the
        /// session" to RaceManager OR CityMode and nothing else, so a small
        /// free-roam scene has to BE a CityMode to have a live session at all
        /// — and a CityMode with no world used to have a live session and a
        /// dead respawn, which is worse than no session because
        /// <see cref="StuckRecovery"/> then fires forever and nothing happens.
        /// Empty is fine: the spawn point is always the last resort.
        /// </summary>
        public Transform[] respawnPoints = new Transform[0];

        /// <summary>What the HUD calls this place during the rolling start.
        /// Was the literal "CHARLOTTE" in RaceHUD, which is the wrong name for
        /// every free-roam map that is not Charlotte.</summary>
        public string venueName = "CHARLOTTE";
        public string VenueName => string.IsNullOrEmpty(venueName) ? "CHARLOTTE" : venueName;

        /// <summary>Session control is live (car responds). Read by
        /// StuckRecovery through DriveSession.</summary>
        public bool Live { get; private set; }

        public float SessionSeconds { get; private set; }
        public float MetersDriven { get; private set; }
        public float DriftSeconds { get; private set; }
        public string CurrentStreet { get; private set; } = "";

        const float StartDelay = 1.6f;
        const float DriftWearMinSpeed = 4f;

        float startTimer;
        float streetPoll;
        Vector3 spawnPos;
        Quaternion spawnRot;
        bool stamped;

        void Awake()
        {
            Instance = this;
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
                if (engine != null) engine.PlayStartup(0.3f);
            }
        }

        void Update()
        {
            if (player == null) return;

            if (!Live)
            {
                startTimer += Time.deltaTime;
                if (startTimer >= StartDelay)
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
            // which way along the edge's own parameterisation "forward" is.
            bool along = e.oneway || Vector3.Dot(car.transform.forward, tan) >= 0f;
            if (!along) tan = -tan;
            var rot = Quaternion.LookRotation(tan, Vector3.up);

            // Walk ALONG the street until there is room, the same way the
            // circuits walk the racing line: the nearest point on the nearest
            // road is also the nearest point to whatever the car wedged itself
            // against, and putting it back there is putting it back stuck.
            // Forward for the CAR, so a recovery never faces the player the
            // wrong way down the street to find space.
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

        /// <summary>
        /// Put a car back with no road graph to put it back ONTO.
        ///
        /// The nearest authored respawn point that the car will actually FIT
        /// in, tried in order of distance, falling back to the spawn. Keeps
        /// the car's own heading where it can — a recovery that spins you
        /// round is a recovery that then drives you the wrong way up the
        /// street you were on.
        /// </summary>
        void RespawnOffGraph(CarController car)
        {
            var from = car.transform.position;
            var best = new System.Collections.Generic.List<Transform>();
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
        /// one moment the LifeSim hears what the drive cost: real metres,
        /// real fuel, real damage — and no purse, no slot-free ride home.
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
