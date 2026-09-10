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
    /// PLAY every reverse twin, headless, and check the field it lines up.
    ///
    /// A PLAY-MODE check, and it has to be one: the reversal itself is
    /// arithmetic on a list, and the edit-mode self-test has always agreed that
    /// it is right. But the grid staging writes CAR POSES, and a pose written
    /// outside a physics step is not a pose the physics engine has agreed to.
    /// The bug this was written for — the player's interpolated body repainting
    /// its baked pose over the one the staging gave it, so that on Beech Gap II
    /// the car started 6.3 km from its own opponents, facing back down the
    /// mountain — is INVISIBLE in edit mode, where nothing steps. See
    /// CarController.TeleportTo.
    ///
    /// One editor session for the whole list: the scenes are loaded from inside
    /// play mode exactly as the game loads them, which is also the only way to
    /// exercise the real handoff. Menu: PSX Racing/Check Reverse Races.
    /// </summary>
    public static class ReverseRaceCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static List<int> twins;

        [MenuItem("PSX Racing/Check Reverse Races (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            twins = new List<int>();

            var scenes = EditorBuildSettings.scenes;
            for (int t = 0; t < TrackCatalog.Count; t++)
            {
                if (!TrackCatalog.At(t).Reversed) continue;
                int s = TrackCatalog.SceneIndex(t);
                if (s < scenes.Length && System.IO.File.Exists(scenes[s].path)) twins.Add(t);
                else Fail(TrackCatalog.At(t).id + ": scene not built");
            }

            if (twins.Count == 0) { Finish(); return; }

            EditorSceneManager.OpenScene(scenes[TrackCatalog.SceneIndex(twins[0])].path);
            Prime(twins[0]);

            // The handoff is a pile of statics, and a domain reload on the way
            // into play mode would clear every one of them: the race would boot
            // as a standalone editor race and run FORWARDS, which this tool
            // would then certify.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>The handoff a race entered from the LifeSim carries.</summary>
        internal static void Prime(int t)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = t;
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
            new GameObject("ReverseRaceCheckRunner").AddComponent<ReverseRaceCheckRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what +
                           (got != null ? "  [" + got + "]" : ""));
        }

        static void Fail(string what) { failures++; log.AppendLine("  FAIL " + what); }

        internal static void Finish()
        {
            log.AppendLine(failures == 0
                ? "ALL REVERSE RACES LINE UP."
                : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.dataPath),
                    "PSXRacing_reverse_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    /// <summary>Drives the list from inside play mode; see ReverseRaceCheck.</summary>
    public class ReverseRaceCheckRunner : MonoBehaviour
    {
        /// <summary>How far apart a four-car grid may be, front row to back.
        /// Four rows 6.5 m apart is under 30 m; 80 leaves the staging room to
        /// change without this needing to, and is still three orders short of
        /// the 6.3 km the bug produced.</summary>
        const float FieldSpreadM = 80f;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);

            for (int i = 0; i < ReverseRaceCheck.twins.Count; i++)
            {
                int t = ReverseRaceCheck.twins[i];
                if (i > 0)
                {
                    ReverseRaceCheck.Prime(t);
                    SceneManager.LoadScene(TrackCatalog.SceneIndex(t));
                    yield return null;
                }
                // Past the first PHYSICS step, not just the first frame: the
                // staging runs in Start and the old bug undid it on the step
                // after, so a check that looked before it would have passed.
                yield return null;
                yield return new WaitForFixedUpdate();
                yield return null;

                Judge(TrackCatalog.At(t));
            }

            ReverseRaceCheck.Finish();
            EditorApplication.Exit(ReverseRaceCheck.failures == 0 ? 0 : 1);
        }

        static void Judge(TrackCatalog.TrackDef twin)
        {
            ReverseRaceCheck.Line(twin.id + " (" + twin.name + "):");

            var rm = RaceManager.Instance;
            if (rm == null || rm.path == null)
            { ReverseRaceCheck.Check(false, "the race scene has a manager and a path"); return; }

            var tp = rm.path;
            ReverseRaceCheck.Check(tp.reversed, "the path is turned round");

            var player = rm.playerCar;
            if (player == null)
            { ReverseRaceCheck.Check(false, "the scene has a player car"); return; }

            int facing = 0, counted = 0, pastFinish = 0;
            float worstDot = 1f, spread = 0f;
            foreach (var car in rm.allCars)
            {
                if (car == null || !car.gameObject.activeInHierarchy) continue;
                counted++;
                int idx = tp.NearestIndex(car.transform.position);
                float dot = Vector3.Dot(car.transform.forward, tp.GetTangent(idx));
                if (dot > 0.9f) facing++;
                worstDot = Mathf.Min(worstDot, dot);
                spread = Mathf.Max(spread,
                    Vector3.Distance(car.transform.position, player.transform.position));
                if (tp.finishIndex > 0 && idx >= tp.finishIndex) pastFinish++;
            }

            // THE THREE WAYS A REVERSED GRID HAS ACTUALLY BEEN WRONG.
            //
            // FACING: a car left on the baked grid points back down the road,
            // and the wrong-way banner tells the driver so within four seconds.
            ReverseRaceCheck.Check(facing == counted && counted > 1,
                  "every car faces the reversed path", facing + "/" + counted +
                  ", worst dot " + worstDot.ToString("0.00"));
            // TOGETHER: one car left behind is a race against nobody, and on a
            // point-to-point stage the two grids are the two ends of a mountain.
            ReverseRaceCheck.Check(spread <= FieldSpreadM,
                  "the whole field is on one grid", spread.ToString("0") + " m apart");
            // PAST THE LINE: a car standing beyond the remapped finish is a car
            // the timing sheet retires on frame one, before it has moved.
            ReverseRaceCheck.Check(pastFinish == 0,
                  "nobody starts past the finish", pastFinish + " past it");
        }
    }
}
