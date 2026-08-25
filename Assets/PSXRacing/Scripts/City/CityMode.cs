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
        public CityWorld world;

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
            if (car == null || world == null || world.Map == null) return;
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
            var pt = e.PointAt(at);
            var tan2 = e.TangentAt(at);
            var tan = new Vector3(tan2.x, 0f, tan2.y);
            if (Vector3.Dot(car.transform.forward, tan) < 0f && !e.oneway) tan = -tan;
            float y = e.YAt(at);
            car.ResetTo(new Vector3(pt.x, y + 0.45f, pt.y), Quaternion.LookRotation(tan, Vector3.up));
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
