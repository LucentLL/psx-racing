using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHO IS DRIVING AFTER THE FLAG — in the running game, with a real race
    /// finished by a real car crossing a real finish line.
    ///
    /// The thing being checked is a behaviour, and a behaviour is the one kind
    /// of thing no edit-mode assertion reaches: the flag sets a state, the
    /// state used to feed PlayerCarInput's no-driver branch, and that branch
    /// silently pinned the throttle at zero, held 30% of brake and unwound the
    /// wheel to centre. It compiled perfectly. It looked fine in a screenshot.
    /// It was the game driving the car off the road while the player watched
    /// the results sheet.
    ///
    /// So: finish a race, then ask the car what is being asked of it. With
    /// NOTHING pressed the game must be asking for nothing — that is what
    /// "the player still has it" means from the car's side — and with a pad
    /// pressed the car must answer. A virtual gamepad is fed through the real
    /// InputSystem so the assertion runs down the same path a player's
    /// controller does.
    ///
    /// Venue: a point-to-point strip, because it finishes on a DISTANCE and
    /// one crossing is the whole race. PSX_FINISH_VENUE overrides.
    ///
    ///   tools\finish-play-check.ps1 -> PSXRacing_finish_play_check.txt
    /// </summary>
    public static class FinishPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Finish Line (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;

            string id = System.Environment.GetEnvironmentVariable("PSX_FINISH_VENUE");
            if (string.IsNullOrEmpty(id)) id = "DragEighth";
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) index = i;

            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            var cars = CarCatalog.All;
            if (cars.Count > 2)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id;
                RaceHandoff.OpponentSkills = "1.0;0.95";
            }

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("FinishPlayCheckRunner").AddComponent<FinishPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE FLAG DOES NOT TAKE THE WHEEL."
                                         : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath),
                             "PSXRacing_finish_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class FinishPlayCheckRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();

            var rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            var input = car != null ? car.GetComponent<PlayerCarInput>() : null;
            var path = rm != null ? rm.path : null;
            FinishPlayCheck.Check(rm != null && car != null && input != null && path != null,
                                  "the scene has a race, a player and a path");
            if (rm == null || car == null || input == null || path == null) { Done(); yield break; }
            FinishPlayCheck.Check(path.HasEnds && path.finishIndex > 0,
                                  "this venue finishes on a distance", path.finishIndex);
            if (!path.HasEnds || path.finishIndex <= 0) { Done(); yield break; }

            // Let the countdown run out: the grid holds the same no-driver
            // branch this check is about, and asserting on it there would pass
            // for the wrong reason.
            float until = Time.realtimeSinceStartup + 12f;
            while (rm.State == RaceManager.RaceState.Countdown &&
                   Time.realtimeSinceStartup < until) yield return null;
            FinishPlayCheck.Check(rm.State == RaceManager.RaceState.Racing,
                                  "the race goes live", rm.State);
            FinishPlayCheck.Check(input.inputEnabled, "and hands the player the car");

            // ---- put the car on the finish line and let it cross ----------
            // The progress index is nudged with it: NearestIndex refines from
            // last frame's index inside a window, and a teleport the length of
            // a strip lands outside that window.
            int at = Mathf.Max(2, path.finishIndex - 8);
            var p = rm.GetProgress(car);
            car.TeleportTo(path.GetPoint(at) + Vector3.up * 0.4f, path.GetRotation(at));
            if (p != null) p.nearestIdx = at;
            yield return new WaitForFixedUpdate();
            car.SetRolling(30f);

            until = Time.realtimeSinceStartup + 15f;
            while (rm.State != RaceManager.RaceState.Finished &&
                   Time.realtimeSinceStartup < until) yield return null;
            FinishPlayCheck.Check(rm.State == RaceManager.RaceState.Finished,
                                  "the car crosses the line and the race ends", rm.State);
            if (rm.State != RaceManager.RaceState.Finished) { Done(); yield break; }
            FinishPlayCheck.Check(RaceHandoff.ResultReady, "with a result stamped for the LifeSim");

            // ---- THE POINT OF THIS CHECK ---------------------------------
            // Nothing is pressed. If the game still owns the car, the car is
            // being asked for 30% of brake and the wheel is being unwound; if
            // the player owns it, nothing is being asked of it at all.
            yield return Frames(4);
            FinishPlayCheck.Check(input.inputEnabled,
                                  "past the flag the player still has the car");
            FinishPlayCheck.Check(car.brakeInput < 0.01f,
                                  "the game is not braking it for them",
                                  car.brakeInput.ToString("0.000"));
            FinishPlayCheck.Check(car.throttleInput < 0.01f,
                                  "and is not asking for throttle either",
                                  car.throttleInput.ToString("0.000"));

            // ---- and the field shuts down down the road ------------------
            // The player teleported to the line, so the opponents are still
            // out on the strip: wait for one of them to finish on its own,
            // which is the wiring (RaceManager -> AIDriver.ShutDown) as well
            // as the behaviour.
            AIDriver first = null;
            until = Time.realtimeSinceStartup + 25f;
            while (first == null && Time.realtimeSinceStartup < until)
            {
                foreach (var other in rm.allCars)
                {
                    if (other == car) continue;
                    var a = other.GetComponent<AIDriver>();
                    if (a != null && a.ShuttingDown) { first = a; break; }
                }
                if (first == null) yield return null;
            }
            if (first != null)
                FinishPlayCheck.Check(true,
                                      "an opponent that crosses the line goes into its shutdown");
            else
            {
                // A 7 km stage: the field is still climbing and will be for
                // minutes. Drive the shutdown by hand so the behaviour below
                // is still measured here; the WIRING is what the strip proves.
                foreach (var other in rm.allCars)
                {
                    if (other == car) continue;
                    var a = other.GetComponent<AIDriver>();
                    if (a == null) continue;
                    a.ShutDown();
                    first = a;
                    break;
                }
                FinishPlayCheck.Note("no opponent finished inside the window on this venue — " +
                                     "shutdown entered by hand");
                FinishPlayCheck.Check(first != null, "there is an opponent to shut down");
            }

            if (first != null)
            {
                // Rolling, it carries the shutdown brake rather than the grid's
                // — the grid pose is a car being HELD on a line, and a car doing
                // 100 km/h past the flag braked that hard is one the player,
                // who still has their own throttle, drives into the back of.
                var ac = first.GetComponent<CarController>();
                if (ac != null) ac.SetRolling(20f);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                FinishPlayCheck.Check(ac != null && ac.brakeInput > 0.05f && ac.brakeInput < 0.35f,
                                      "rolling, it eases off rather than standing on the brake",
                                      ac != null ? ac.brakeInput.ToString("0.00") : "no car");
                FinishPlayCheck.Note("shutdown steer on this venue (a straight strip reads ~0): " +
                                     (ac != null ? ac.steerInput.ToString("0.00") : "?"));
            }

            // ---- and a real control reaches it ---------------------------
            Gamepad pad = null;
            try { pad = InputSystem.AddDevice<Gamepad>(); }
            catch (System.Exception e) { FinishPlayCheck.Note("no virtual pad here: " + e.Message); }

            if (pad != null)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState
                {
                    rightTrigger = 1f,
                    leftStick = new Vector2(0.8f, 0f),
                });
                InputSystem.Update();
                yield return Frames(3);
                FinishPlayCheck.Check(car.throttleInput > 0.5f,
                                      "a pad past the flag opens the throttle",
                                      car.throttleInput.ToString("0.00"));
                FinishPlayCheck.Check(car.steerInput > 0.3f,
                                      "and turns the wheel",
                                      car.steerInput.ToString("0.00"));

                // R and pad X belong to the results screen now, so the respawn
                // they also carry has to stand down — one press, one thing.
                //
                // The REPLAY is stood down for this one, because it answers to
                // the same button and teleports every car to the recording's
                // first frame when it starts: with it live there is no way to
                // tell "X was ignored by the respawn" from "X started the
                // replay, which moved the car". Disabling it leaves exactly
                // the path under test.
                var replay = RaceReplay.Instance;
                bool keepReplay = replay != null && replay.enabled;
                if (replay != null) replay.enabled = false;

                int teleports = car.TeleportCount;
                InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 0 });
                InputSystem.Update();
                yield return Frames(2);
                InputSystem.QueueStateEvent(pad, new GamepadState
                {
                    buttons = 1u << (int)GamepadButton.West,
                });
                InputSystem.Update();
                yield return Frames(3);
                FinishPlayCheck.Check(car.TeleportCount == teleports,
                                      "pad X on the results screen does not also respawn the car",
                                      car.TeleportCount - teleports);
                if (replay != null) replay.enabled = keepReplay;
                InputSystem.RemoveDevice(pad);
            }

            Done();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        void Done()
        {
            FinishPlayCheck.Finish();
            EditorApplication.Exit(FinishPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
