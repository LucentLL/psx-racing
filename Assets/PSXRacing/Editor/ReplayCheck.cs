using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PLAY a race for a few seconds, then PLAY IT BACK, headless, and check
    /// that the replay is the race.
    ///
    /// A PLAY-MODE check, because everything the replay does that could be
    /// wrong happens on a physics step: kinematic bodies moved with
    /// MovePosition, an interpolation history dropped on a seek, components
    /// switched off and on. The edit-mode self-test covers the arithmetic
    /// (TestReplay); this covers the machine.
    ///
    /// One editor session: the city circuit is loaded from inside play mode
    /// with a LifeSim handoff, the AI drives for RecordSeconds while the
    /// recorder samples the field, the replay is started and stepped, the
    /// cars are compared against the recording, the director's lens is
    /// checked to be somewhere that is not the car, and the replay is ended
    /// and the field checked to have been put back. Menu: PSX Racing/Check
    /// Replay.
    /// </summary>
    public static class ReplayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static int venue;

        [MenuItem("PSX Racing/Check Replay (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            venue = 0;   // the city circuit: short, four cars, no handoff surprises

            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(venue);
            if (s >= scenes.Length || !System.IO.File.Exists(scenes[s].path))
            { failures++; log.AppendLine("  FAIL scene not built"); Finish(); return; }

            EditorSceneManager.OpenScene(scenes[s].path);
            Prime();
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        internal static void Prime()
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = venue;
            var cars = CarCatalog.All;
            if (cars.Count > 4)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id + ";" + cars[3].id;
                RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            }
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("ReplayCheckRunner").AddComponent<ReplayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE REPLAY IS THE RACE." : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                                       "PSXRacing_replay_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class ReplayCheckRunner : MonoBehaviour
    {
        /// <summary>Seconds of race recorded before the replay starts. The
        /// countdown is four of them, so the field is moving for the rest.</summary>
        const float RecordSeconds = 12f;
        /// <summary>How far a replayed car may sit from its recorded pose. The
        /// sample is interpolated between 30 Hz frames; a car at 30 m/s moves
        /// a metre between them, and the physics-step lag is one more.</summary>
        const float PoseToleranceM = 2.5f;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();

            var rm = RaceManager.Instance;
            var rp = RaceReplay.Instance;
            ReplayCheck.Check(rm != null, "the race scene has a manager");
            ReplayCheck.Check(rp != null, "and a replay recorder on it");
            if (rm == null || rp == null) { Done(); yield break; }

            // A DELIVERY'S LOAD, riding in an AI car: the player never drives
            // in this check, and a load on a parked car records nothing worth
            // replaying. The recorder attaches to it on its next sample.
            CarController carrier = null;
            foreach (var c in rm.allCars) if (c != null && c != rm.playerCar) { carrier = c; break; }
            var cargo = carrier != null ? PizzaCargo.Spawn(carrier, new[] { 0, 3, 6 }, 2) : null;
            ReplayCheck.Check(cargo != null && cargo.BoxCount == 3, "a three-box load rides in an AI car",
                              cargo != null ? cargo.BoxCount : 0);
            var trace = cargo != null ? gameObject.AddComponent<CargoTrace>() : null;
            if (trace != null) trace.cargo = cargo;
            // The traffic, traced live the same way (owner, 2026-09-26:
            // "traffic cars are not visible in the replays").
            var ts = TrafficSystem.Instance;
            ReplayCheck.Check(ts != null && ts.PoolCount > 0, "the race has traffic", ts != null ? ts.PoolCount : 0);
            var ttrace = ts != null ? gameObject.AddComponent<TrafficTrace>() : null;
            if (ttrace != null) ttrace.traffic = ts;

            // The player never touches a key; the AI race on. Twelve seconds
            // is a grid, a countdown and eight seconds of driving — and at
            // JoltAt a crash, so the load is somewhere different at the end
            // of the recording from where it was before it.
            float t0 = Time.time;
            bool jolted = false;
            float joltClock = -1f;
            while (Time.time - t0 < RecordSeconds)
            {
                if (!jolted && cargo != null && Time.time - t0 >= JoltAt)
                {
                    jolted = true;
                    joltClock = Time.fixedTime - rp.RecordStartFixedTime;
                    cargo.InjectImpact(-carrier.transform.forward * 6f);
                }
                yield return null;
            }
            ReplayCheck.Check(rp.FrameCount > (RecordSeconds - 2f) * RaceReplay.SampleHz,
                              "the recorder kept up", rp.FrameCount + " samples");
            ReplayCheck.Check(rp.CargoSamples > 0 && rp.CargoFirstSample >= 0 &&
                              rp.CargoFirstSample + rp.CargoSamples == rp.FrameCount,
                              "the load was sampled on every step the cars were",
                              rp.CargoSamples + " from sample " + rp.CargoFirstSample + " of " + rp.FrameCount);
            ReplayCheck.Check(rp.TrafficSamples > 0 && rp.TrafficFirstSample >= 0 &&
                              rp.TrafficFirstSample + rp.TrafficSamples == rp.FrameCount,
                              "the traffic was sampled on every step the cars were",
                              rp.TrafficSamples + " from sample " + rp.TrafficFirstSample + " of " + rp.FrameCount);
            int trafficOnBefore = 0;
            var trafficPosBefore = new List<Vector3>();
            var trafficOnWas = new List<bool>();
            if (ts != null)
                for (int j = 0; j < ts.PoolCount; j++)
                {
                    trafficPosBefore.Add(ts.PoolCar(j).position);
                    trafficOnWas.Add(ts.PoolOn(j));
                    if (ts.PoolOn(j)) trafficOnBefore++;
                }
            ReplayCheck.Check(trafficOnBefore > 0, "traffic is on the road when the replay starts", trafficOnBefore);
            var parts = cargo != null ? cargo.ReplayParts() : new List<Transform>();
            var partPosBefore = new List<Vector3>();
            var partRotBefore = new List<Quaternion>();
            foreach (var p in parts) { partPosBefore.Add(p.position); partRotBefore.Add(p.rotation); }

            // Where everybody is right now, and where they were four seconds ago.
            var before = new Dictionary<CarController, Vector3>();
            foreach (var c in rm.allCars) if (c != null) before[c] = c.transform.position;
            float wanted = Mathf.Max(0f, rp.Duration - 4f);

            // The recorder only offers a replay once the race is over. For the
            // check, the race is over now.
            var field = typeof(RaceReplay).GetField("recording",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(rp, false);
            ReplayCheck.Check(rp.Available, "a recording is offered", rp.Duration.ToString("0.0") + " s");
            rp.Begin();
            ReplayCheck.Check(RaceReplay.Playing, "the replay starts");
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;

            // Every car is kinematic and its driver asleep.
            int kin = 0, asleep = 0, cars = 0;
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                cars++;
                if (c.Body != null && c.Body.isKinematic) kin++;
                if (!c.enabled) asleep++;
            }
            ReplayCheck.Check(kin == cars, "every car is kinematic during the replay", kin + "/" + cars);
            ReplayCheck.Check(asleep == cars, "every controller is asleep", asleep + "/" + cars);

            // The player never touched a key, so everything below follows an
            // AI car: the recorded pose of a car that never moved proves
            // nothing, and "the focus car moves through the replay" read
            // 0.0 m the first time this ran, against the parked player.
            var setFocus = typeof(RaceReplay).GetMethod("SetFocus",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < rp.CarCount; i++)
            {
                setFocus.Invoke(rp, new object[] { i });
                if (rp.Focus != null && rp.Focus != rm.playerCar) break;
            }
            ReplayCheck.Check(rp.Focus != null && rp.Focus != rm.playerCar,
                              "the checks follow an AI car", rp.FocusName);

            // Seek to four seconds before the end and compare poses to the
            // recording, and to where the cars physically were: they should
            // be back in their own past.
            rp.Seek(wanted, hard: true);
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;
            var f = rp.FocusFrame;
            var focus = rp.Focus;
            ReplayCheck.Check(focus != null, "the replay has a focus car", rp.FocusName);
            if (focus != null)
            {
                float err = Vector3.Distance(focus.transform.position, f.pos);
                ReplayCheck.Check(err < PoseToleranceM, "the focus car sits on its recorded pose",
                                  err.ToString("0.00") + " m off");
            }
            // The camera is the director's, and it is not in the car.
            var cam = Camera.main;
            var director = cam != null ? cam.GetComponent<ReplayCamera>() : null;
            ReplayCheck.Check(director != null && director.enabled, "the director has the camera");
            if (director != null)
            {
                ReplayCheck.Check(director.Stations.Count >= 4, "lenses were planted along the road",
                                  director.Stations.Count);
                if (focus != null)
                {
                    float away = Vector3.Distance(cam.transform.position, focus.transform.position);
                    ReplayCheck.Check(away > 4f && away < 400f, "the lens stands off the car", away.ToString("0") + " m");
                    // A tripod pans: the car is in front of the lens.
                    float dot = Vector3.Dot(cam.transform.forward,
                        (focus.transform.position + Vector3.up * 0.7f - cam.transform.position).normalized);
                    ReplayCheck.Check(dot > 0.95f, "and is pointed at it", dot.ToString("0.00"));
                }
            }
            // Let it play a second: the clock moves and the car moves with it.
            float tA = rp.ReplayTime;
            Vector3 pA = focus != null ? focus.transform.position : Vector3.zero;
            for (int i = 0; i < 60; i++) yield return null;
            ReplayCheck.Check(rp.ReplayTime > tA + 0.5f, "the replay clock runs", (rp.ReplayTime - tA).ToString("0.00") + " s");
            if (focus != null)
                ReplayCheck.Check(Vector3.Distance(pA, focus.transform.position) > 0.5f,
                                  "the focus car moves through the replay",
                                  Vector3.Distance(pA, focus.transform.position).ToString("0.0") + " m");

            // Pause holds it.
            var pauseField = typeof(RaceReplay).GetField("paused",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            pauseField.SetValue(rp, true);
            float tP = rp.ReplayTime;
            for (int i = 0; i < 20; i++) yield return null;
            ReplayCheck.Check(Mathf.Abs(rp.ReplayTime - tP) < 1e-4f, "pause holds the clock");

            // THE LOAD REPLAYS ITS OWN PAST. Still paused, so the pose written
            // is the sample's own: once a second before the crash, once near
            // the end, each compared against the trace taken live on the same
            // physics step — and the two against each other, because a replay
            // that shows the load where it ended passes the first comparison
            // at the end and fails it everywhere else.
            if (cargo != null && trace != null && parts.Count > 1)
            {
                int bodies = 0, kinematic = 0;
                foreach (var b in cargo.GetComponentsInChildren<Rigidbody>(true))
                { bodies++; if (b.isKinematic) kinematic++; }
                ReplayCheck.Check(bodies > 3 && kinematic == bodies, "every body on the load is kinematic in the replay",
                                  kinematic + "/" + bodies);

                int kBefore = Mathf.Clamp(Mathf.RoundToInt((joltClock - 1f) * RaceReplay.SampleHz), 0, rp.FrameCount - 1);
                int kAfter = Mathf.Max(0, rp.FrameCount - 8);
                var shown = new Vector3[2][];
                int[] ks = { kBefore, kAfter };
                for (int q = 0; q < 2; q++)
                {
                    float clock = rp.SampleClock(ks[q]);
                    rp.Seek(clock, hard: true);
                    yield return null;
                    shown[q] = new Vector3[parts.Count];
                    for (int j = 0; j < parts.Count; j++) shown[q][j] = parts[j].position;
                    float worst = 0f;
                    bool found = trace.TryAt(rp.RecordStartFixedTime + clock, out var live);
                    if (found)
                        for (int j = 0; j < parts.Count && j < live.Length; j++)
                            worst = Mathf.Max(worst, Vector3.Distance(shown[q][j], live[j]));
                    ReplayCheck.Check(found && worst < 0.01f,
                                      (q == 0 ? "before the crash" : "after it") + ", the load sits where it was on that step",
                                      found ? worst.ToString("0.0000") + " m worst part (t " + clock.ToString("0.00") + " s)" : "no live trace at " + clock.ToString("0.00"));
                }
                float moved = 0f;
                for (int j = 1; j < parts.Count; j++) moved = Mathf.Max(moved, Vector3.Distance(shown[0][j], shown[1][j]));
                ReplayCheck.Check(moved > 0.05f, "and the replay shows it moving between the two, not parked at the flag",
                                  moved.ToString("0.00") + " m");
            }
            // THE TRAFFIC REPLAYS ITS OWN PAST, still paused: at a sample
            // before any was on the road (the countdown) and at one near the
            // end, every pool car shown or hidden as it was live on that step
            // and, where shown, on its live pose.
            if (ts != null && ttrace != null)
            {
                int kinT = 0, onT = 0;
                for (int j = 0; j < ts.PoolCount; j++)
                    if (ts.PoolOn(j)) { onT++; if (ts.PoolCar(j).GetComponent<Rigidbody>().isKinematic) kinT++; }
                ReplayCheck.Check(onT > 0 && kinT == onT, "the traffic is shown and kinematic in the replay", kinT + "/" + onT);
                int[] tks = { Mathf.Clamp(Mathf.RoundToInt(1f * RaceReplay.SampleHz), 0, rp.FrameCount - 1),
                              Mathf.Max(0, rp.FrameCount - 8) };
                foreach (int k in tks)
                {
                    float clock = rp.SampleClock(k);
                    rp.Seek(clock, hard: true);
                    yield return null;
                    bool found = ttrace.TryAt(rp.RecordStartFixedTime + clock, out var livePos, out var liveOn);
                    int shownOn = 0, wasOn = 0, mismatch = 0; float worst = 0f;
                    if (found)
                        for (int j = 0; j < ts.PoolCount && j < liveOn.Length; j++)
                        {
                            bool on = ts.PoolOn(j);
                            if (on) shownOn++;
                            if (liveOn[j]) wasOn++;
                            if (on != liveOn[j]) mismatch++;
                            else if (on) worst = Mathf.Max(worst, Vector3.Distance(ts.PoolCar(j).position, livePos[j]));
                        }
                    ReplayCheck.Check(found && mismatch == 0 && worst < 0.01f,
                                      "at " + clock.ToString("0.0") + " s the replay shows the traffic that was there",
                                      found ? shownOn + " shown, " + wasOn + " live, " + mismatch + " wrong, " +
                                              worst.ToString("0.0000") + " m worst" : "no live trace");
                }
            }
            pauseField.SetValue(rp, false);

            // End: everything back.
            rp.End();
            // The load is read THE MOMENT the replay ends, before physics has
            // stepped it: a put-back is exact or it is wrong.
            float cargoOff = 0f, cargoTurned = 0f;
            for (int j = 0; j < parts.Count; j++)
            {
                cargoOff = Mathf.Max(cargoOff, Vector3.Distance(parts[j].position, partPosBefore[j]));
                cargoTurned = Mathf.Max(cargoTurned, Quaternion.Angle(parts[j].rotation, partRotBefore[j]));
            }
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;
            ReplayCheck.Check(!RaceReplay.Playing, "the replay ends");
            int restored = 0, awake = 0, home = 0;
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                if (c.Body != null && !c.Body.isKinematic) restored++;
                if (c.enabled) awake++;
                if (before.TryGetValue(c, out var was) && Vector3.Distance(was, c.transform.position) < 3f) home++;
            }
            ReplayCheck.Check(restored == cars, "every car is dynamic again", restored + "/" + cars);
            ReplayCheck.Check(awake == cars, "every controller is awake again", awake + "/" + cars);
            ReplayCheck.Check(home == cars, "every car is back where the replay found it", home + "/" + cars);
            var chase = cam != null ? cam.GetComponent<ChaseCamera>() : null;
            ReplayCheck.Check(chase != null && chase.enabled, "the chase camera has the lens back");
            if (ts != null)
            {
                int same = 0, dyn = 0, onNow = 0;
                for (int j = 0; j < ts.PoolCount && j < trafficOnWas.Count; j++)
                {
                    bool on = ts.PoolOn(j);
                    if (on == trafficOnWas[j]) same++;
                    if (on) { onNow++; if (!ts.PoolCar(j).GetComponent<Rigidbody>().isKinematic) dyn++; }
                }
                ReplayCheck.Check(same == ts.PoolCount, "the traffic on the road is the traffic the replay found", same + "/" + ts.PoolCount);
                ReplayCheck.Check(dyn == onNow, "and it is dynamic again", dyn + "/" + onNow);
                // Two steps have passed, so each car is near where it was - not
                // left at its replay pose at the far end of the recording.
                float far = 0f;
                for (int j = 0; j < ts.PoolCount && j < trafficOnWas.Count; j++)
                    if (trafficOnWas[j] && ts.PoolOn(j))
                        far = Mathf.Max(far, Vector3.Distance(ts.PoolCar(j).position, trafficPosBefore[j]));
                ReplayCheck.Check(far < 3f, "each car back where it was, driving on", far.ToString("0.00") + " m worst");
                int wrecks = ts.WreckLog.Count;
                for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
                ReplayCheck.Check(ts.WreckLog.Count == wrecks, "and the put-back is not read as a hit",
                                  (ts.WreckLog.Count - wrecks) + " wrecked");
            }

            if (cargo != null && parts.Count > 1)
            {
                ReplayCheck.Check(cargoOff < 1e-3f && cargoTurned < 0.1f, "the load is back where the replay found it",
                                  cargoOff.ToString("0.0000") + " m, " + cargoTurned.ToString("0.00") + " deg worst part");
                // And a few steps later it has not been fired across the car: a
                // body handed back kinematic flags in the wrong order, or a pose
                // without its velocity, jumps by metres. A box still sliding at
                // a metre or two a second when the replay began legitimately
                // carries on — 8.6 cm on the first run — so the bar is set well
                // above a slide and well below a launch.
                float drift = 0f;
                for (int j = 0; j < parts.Count; j++)
                    drift = Mathf.Max(drift, Vector3.Distance(parts[j].position, partPosBefore[j]));
                ReplayCheck.Check(drift < 0.30f, "and stays there once physics has it again", drift.ToString("0.000") + " m");
                int dynamic = 0, boxes = 0;
                for (int j = 1; j <= cargo.BoxCount && j < parts.Count; j++)
                {
                    var b = parts[j].GetComponent<Rigidbody>();
                    if (b == null) continue;
                    boxes++;
                    if (!b.isKinematic) dynamic++;
                }
                ReplayCheck.Check(boxes == 3 && dynamic == boxes, "every box is dynamic again", dynamic + "/" + boxes);
                ReplayCheck.Check(cargo.enabled, "and the load's driver is awake again");
            }

            Done();
        }

        /// <summary>Seconds into the recording at which the load is thrown.</summary>
        const float JoltAt = 7f;

        static void Done()
        {
            ReplayCheck.Finish();
            EditorApplication.Exit(ReplayCheck.failures == 0 ? 0 : 1);
        }
    }

    /// <summary>The traffic pool, traced LIVE on every physics step like the
    /// load: which cars were on the road and where.</summary>
    public class TrafficTrace : MonoBehaviour
    {
        public TrafficSystem traffic;
        readonly List<float> clock = new List<float>();
        readonly List<Vector3[]> poses = new List<Vector3[]>();
        readonly List<bool[]> ons = new List<bool[]>();

        void FixedUpdate()
        {
            if (traffic == null || RaceReplay.Playing) return;
            int n = traffic.PoolCount;
            var p = new Vector3[n];
            var o = new bool[n];
            for (int j = 0; j < n; j++)
            {
                o[j] = traffic.PoolOn(j);
                p[j] = traffic.PoolCar(j).GetComponent<Rigidbody>().position;
            }
            clock.Add(Time.fixedTime);
            poses.Add(p);
            ons.Add(o);
        }

        public bool TryAt(float fixedTime, out Vector3[] pose, out bool[] on)
        {
            pose = null; on = null;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < clock.Count; i++)
            {
                float d = Mathf.Abs(clock[i] - fixedTime);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0 || bestD > Time.fixedDeltaTime * 0.5f) return false;
            pose = poses[best]; on = ons[best];
            return true;
        }
    }

    /// <summary>
    /// The load's parts, traced LIVE on every physics step, for comparison with
    /// what the replay draws. Taken in FixedUpdate like the recorder's own
    /// sample, so both read the poses the same step produced whatever order the
    /// two FixedUpdates run in: nothing moves a body between the scripts and the
    /// simulation.
    /// </summary>
    public class CargoTrace : MonoBehaviour
    {
        public PizzaCargo cargo;
        readonly List<float> clock = new List<float>();
        readonly List<Vector3[]> poses = new List<Vector3[]>();

        void FixedUpdate()
        {
            if (cargo == null || RaceReplay.Playing) return;
            var parts = cargo.ReplayParts();
            var p = new Vector3[parts.Count];
            for (int j = 0; j < parts.Count; j++) p[j] = parts[j].position;
            clock.Add(Time.fixedTime);
            poses.Add(p);
        }

        /// <summary>The trace at the step whose fixed time is nearest
        /// <paramref name="fixedTime"/>, if one is within half a step.</summary>
        public bool TryAt(float fixedTime, out Vector3[] pose)
        {
            pose = null;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < clock.Count; i++)
            {
                float d = Mathf.Abs(clock[i] - fixedTime);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0 || bestD > Time.fixedDeltaTime * 0.5f) return false;
            pose = poses[best];
            return true;
        }
    }
}
