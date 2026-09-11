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

            // The player never touches a key; the AI race on. Twelve seconds
            // is a grid, a countdown and eight seconds of driving.
            float t0 = Time.time;
            while (Time.time - t0 < RecordSeconds) yield return null;
            ReplayCheck.Check(rp.FrameCount > (RecordSeconds - 2f) * RaceReplay.SampleHz,
                              "the recorder kept up", rp.FrameCount + " samples");

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
            pauseField.SetValue(rp, false);

            // End: everything back.
            rp.End();
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

            Done();
        }

        static void Done()
        {
            ReplayCheck.Finish();
            EditorApplication.Exit(ReplayCheck.failures == 0 ? 0 : 1);
        }
    }
}
