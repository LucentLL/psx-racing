using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Race flow: countdown, per-car lap/progress tracking, positions, results.
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        public static RaceManager Instance { get; private set; }

        public TrackPath path;
        public int totalLaps = 3;
        public CarController playerCar;
        public List<CarController> allCars = new List<CarController>();

        public enum RaceState { Countdown, Racing, Finished }
        public RaceState State { get; private set; } = RaceState.Countdown;
        public float CountdownRemaining { get; private set; } = 4f;

        public class CarProgress
        {
            public CarController car;
            public int nearestIdx;
            public int lap = 1;
            public bool crossedStartOnce;  // grid sits behind the line; first crossing starts lap 1
            public float progress;         // lap * length + distance along
            public float raceTime;
            public float lastLapTime;
            public float bestLapTime;
            public float lapStartTime;
            public bool finished;
            public float finishTime;
            /// <summary>Speed through the traps, km/h. Drag only.</summary>
            public float trapSpeedKmh;
        }

        readonly Dictionary<CarController, CarProgress> progressMap = new Dictionary<CarController, CarProgress>();
        public IReadOnlyDictionary<CarController, CarProgress> Progress => progressMap;

        /// <summary>Seconds the player spent actually sliding. RG2 charges tire,
        /// chassis and paint wear per second of drift; the LifeSim's wear math has
        /// always read this, but until now nothing wrote it, so drift wear was
        /// silently zero on every race.</summary>
        float playerDriftSeconds;
        /// <summary>Drift wear should not accrue from the state flag alone — the
        /// machine can read Drifting while the car is nearly stopped.</summary>
        const float DriftWearMinSpeed = 4f;

        void Awake() => Instance = this;

        void Start()
        {
            // The handoff can RETIRE cars (a blacklist challenge is 1v1 on a grid
            // built for four), so the field has to settle before the progress
            // table is built from it.
            GetComponent<RaceHandoffApplier>()?.Apply(allCars);

            // Stagger the starters across the countdown so the grid does not fire
            // as one voice, and so the tach and the audio agree at lights-out.
            float startDelay = 0.25f;
            foreach (var car in allCars)
            {
                progressMap[car] = new CarProgress
                {
                    car = car,
                    nearestIdx = path.NearestIndex(car.transform.position)
                };
                SetCarInputEnabled(car, false);

                var engine = car.GetComponent<EngineAudio>();
                if (engine != null) engine.PlayStartup(startDelay);
                startDelay += 0.3f;
            }
        }

        void Update()
        {
            CountdownRemaining -= Time.deltaTime;
            if (State == RaceState.Countdown)
            {
                if (CountdownRemaining <= 1f) // race goes live; "GO!" shows for ~1 more second
                {
                    State = RaceState.Racing;
                    foreach (var car in allCars) SetCarInputEnabled(car, true);
                    foreach (var p in progressMap.Values) p.lapStartTime = Time.time;
                }
                return;
            }

            if (State == RaceState.Racing && playerCar != null && playerCar.Drifting &&
                Mathf.Abs(playerCar.forwardSpeed) > DriftWearMinSpeed)
                playerDriftSeconds += Time.deltaTime;

            foreach (var p in progressMap.Values)
            {
                if (p.finished) continue;
                p.raceTime += Time.deltaTime;

                int prev = p.nearestIdx;
                p.nearestIdx = path.NearestIndex(p.car.transform.position, prev);

                // A strip — or a point-to-point stage — is decided by a
                // DISTANCE, not a lap count. There is no line to cross twice
                // and no rolling start to detect: the clock runs from the
                // green and stops at the traps (or the stage finish).
                if (path.HasEnds)
                {
                    p.progress = p.nearestIdx * path.spacing;
                    if (path.finishIndex > 0 && p.nearestIdx >= path.finishIndex)
                    {
                        p.finished = true;
                        p.finishTime = p.raceTime;
                        p.trapSpeedKmh = Mathf.Abs(p.car.speedKmh);
                        OnCarFinished(p);
                    }
                    continue;
                }

                // Lap line crossing: jump from the last few waypoints to the first few
                int n = path.Count;
                if (prev > n - 6 && p.nearestIdx < 5)
                {
                    if (!p.crossedStartOnce)
                    {
                        p.crossedStartOnce = true;      // rolling start from the grid
                        p.lapStartTime = Time.time;
                    }
                    else
                    {
                        p.lastLapTime = Time.time - p.lapStartTime;
                        if (p.bestLapTime <= 0f || p.lastLapTime < p.bestLapTime) p.bestLapTime = p.lastLapTime;
                        p.lapStartTime = Time.time;
                        p.lap++;
                        if (p.lap > totalLaps)
                        {
                            p.finished = true;
                            p.finishTime = p.raceTime;
                            OnCarFinished(p);
                        }
                    }
                }
                else if (p.nearestIdx > n - 6 && prev < 5 && p.crossedStartOnce)
                {
                    p.lap = Mathf.Max(1, p.lap - 1); // crossed the line backwards
                }

                p.progress = (p.lap - (p.crossedStartOnce ? 0 : 1)) * path.TotalLength
                             + p.nearestIdx * path.spacing;
            }

            RecomputePositions();

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool touchContinue = TouchControls.Instance != null &&
                                 TouchControls.Instance.ContinuePressed;

            // A pad had no way off the results screen at all: R and the touch
            // RESET button were the only two continues, so a controller player
            // who finished a race was simply stranded there.
            // Not Start: that is the pause toggle, and having it also advance
            // the results screen would mean one press did two things.
            bool padContinue = pad != null && pad.buttonSouth.wasPressedThisFrame &&
                               !PauseMenu.IsOpen;
            if (State == RaceState.Finished &&
                ((kb != null && kb.rKey.wasPressedThisFrame) || touchContinue || padContinue))
            {
                // From the LifeSim, R returns home with the result in the
                // mailbox; standalone it just restarts the race as before.
                if (RaceHandoff.FromLifeSim)
                    SceneManager.LoadScene(0);
                else
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        void OnCarFinished(CarProgress p)
        {
            if (p.car == playerCar)
            {
                State = RaceState.Finished;
                SetCarInputEnabled(playerCar, false);

                // Stamp the result for the LifeSim. Harmless standalone.
                RaceHandoff.ResultReady = true;
                RaceHandoff.FinishPos = GetPosition(playerCar);
                RaceHandoff.FieldSize = allCars.Count;
                RaceHandoff.RaceTimeSeconds = p.finishTime;
                // On a strip the ET IS the lap: there is one run and its time is
                // the whole result, so it goes in the field the LifeSim already
                // reports as the headline number rather than staying blank.
                RaceHandoff.BestLapSeconds = path.HasEnds ? p.finishTime : p.bestLapTime;
                RaceHandoff.TrapSpeedKmh = p.trapSpeedKmh;
                RaceHandoff.MetersDriven = path.HasEnds
                    ? path.finishIndex * path.spacing
                    : totalLaps * path.TotalLength;
                RaceHandoff.DriftSeconds = playerDriftSeconds;
                var responder = playerCar.GetComponent<CollisionResponder>();
                RaceHandoff.DamageScore = responder != null ? responder.DamageScore : 0f;
                RaceHandoff.HardHits = responder != null ? responder.HardHits : 0;

                // What the customer is about to open. Stamped like the tank is
                // stamped, and for the same reason: the cargo is the only thing
                // that actually knows, and re-deriving it from the impact tally
                // back in the menu would be answering a question the simulation
                // already answered.
                // BoxCount, not just the instance. A cargo whose prefabs
                // failed to load is a component with no boxes in it, and its
                // Condition is a truthful 1.0 about nothing — stamping that
                // would override the damage model with "perfect" and make every
                // delivery in a broken build pay full whack.
                if (PizzaCargo.Instance != null && PizzaCargo.Instance.BoxCount > 0)
                {
                    RaceHandoff.CargoCondition = PizzaCargo.Instance.Condition;
                    RaceHandoff.CargoReported = true;
                }

                // The tank is MEASURED, not re-derived. A car that pulled into
                // the forecourt on lap two covered the same distance as one that
                // did not and is carrying a completely different amount of fuel,
                // so the odometer is no longer able to answer this question.
                var tank = playerCar.GetComponent<FuelTank>();
                if (tank != null)
                {
                    RaceHandoff.EndFuelPct = tank.percent;
                    RaceHandoff.FuelReported = true;
                }
                // FuelSpent is accumulated by the pumps as the money is taken,
                // not stamped here. Reading a static counter at the flag meant a
                // strip — which has no pumps, so no pump ever ran to clear
                // it — reported the previous circuit's fuel bill as its own.
            }
            else
            {
                var ai = p.car.GetComponent<AIDriver>();
                if (ai != null) ai.driving = false;
            }
        }

        void SetCarInputEnabled(CarController car, bool enabled)
        {
            var player = car.GetComponent<PlayerCarInput>();
            if (player != null) player.inputEnabled = enabled;
            var ai = car.GetComponent<AIDriver>();
            if (ai != null) ai.driving = enabled;
        }

        // Positions are recomputed once per frame into a cache. The HUD asks for
        // them every frame, and a LINQ OrderBy per call allocated an enumerator,
        // a sorted buffer and a list each time.
        readonly List<CarProgress> sortBuffer = new List<CarProgress>();
        readonly Dictionary<CarController, int> positionCache = new Dictionary<CarController, int>();
        // Compared in three stages rather than with a single blended key. The
        // obvious "float.MaxValue - finishTime" trick silently collapses: at that
        // magnitude a float cannot represent the subtraction, so every finisher
        // compares exactly equal and the final standings come out arbitrary.
        static readonly Comparison<CarProgress> ByProgress = (a, b) =>
        {
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            if (a.finished) return a.finishTime.CompareTo(b.finishTime);
            return b.progress.CompareTo(a.progress);
        };

        void RecomputePositions()
        {
            sortBuffer.Clear();
            foreach (var p in progressMap.Values) sortBuffer.Add(p);
            sortBuffer.Sort(ByProgress);
            for (int i = 0; i < sortBuffer.Count; i++) positionCache[sortBuffer[i].car] = i + 1;
        }

        /// <summary>1-based race position of a car, from this frame's cache.</summary>
        public int GetPosition(CarController car) =>
            positionCache.TryGetValue(car, out int pos) ? pos : 1;

        public CarProgress GetProgress(CarController car) =>
            progressMap.TryGetValue(car, out var p) ? p : null;

        /// <summary>
        /// Put a car back in the middle of the road, facing the way the road
        /// goes — and somewhere it can actually drive away from.
        ///
        /// The nearest waypoint IS the centre of the road, so that part was
        /// always right. What was missing is that the nearest waypoint is also
        /// the nearest waypoint to whatever the car got stuck ON: a pier, a
        /// barrier end, another car, a block of concrete beside the line. The
        /// old version dropped the car there regardless, so recovering from
        /// those spots handed the car straight back into them, and the player
        /// experienced the unstick as doing nothing. It now walks FORWARD along
        /// the line until it finds a clear station, which is also the direction
        /// the player wants to be pointed in.
        /// </summary>
        public void RespawnCar(CarController car)
        {
            if (car == null || path == null || path.Count == 0) return;
            int start = path.NearestIndex(car.transform.position,
                progressMap.TryGetValue(car, out var p) ? p.nearestIdx : -1);

            // Roughly 60 m of line at 4 m stations, then give up and take the
            // nearest one anyway: a car left where it is would be worse than a
            // car put back somewhere imperfect.
            const int Search = 15;
            for (int step = 0; step <= Search; step++)
            {
                int idx = path.Wrap(start + step);
                if (!DriveSession.TryPlace(car, path.GetPoint(idx), path.GetRotation(idx))) continue;
                if (p != null) p.nearestIdx = idx;
                return;
            }

            car.ResetTo(path.GetPoint(start), path.GetRotation(start));
            if (p != null) p.nearestIdx = start;
        }
    }
}
