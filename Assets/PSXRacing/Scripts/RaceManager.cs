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
        }

        readonly Dictionary<CarController, CarProgress> progressMap = new Dictionary<CarController, CarProgress>();
        public IReadOnlyDictionary<CarController, CarProgress> Progress => progressMap;

        void Awake() => Instance = this;

        void Start()
        {
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

            foreach (var p in progressMap.Values)
            {
                if (p.finished) continue;
                p.raceTime += Time.deltaTime;

                int prev = p.nearestIdx;
                p.nearestIdx = path.NearestIndex(p.car.transform.position, prev);

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
            if (State == RaceState.Finished && kb != null && kb.rKey.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void OnCarFinished(CarProgress p)
        {
            if (p.car == playerCar)
            {
                State = RaceState.Finished;
                SetCarInputEnabled(playerCar, false);
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

        public void RespawnCar(CarController car)
        {
            int idx = path.NearestIndex(car.transform.position,
                progressMap.TryGetValue(car, out var p) ? p.nearestIdx : -1);
            car.ResetTo(path.GetPoint(idx), path.GetRotation(idx));
            if (p != null) p.nearestIdx = idx;
        }
    }
}
