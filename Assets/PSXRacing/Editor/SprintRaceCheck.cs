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
    /// PLAY every sprint raced on a loop's road (TrackDef.sprintOf), headless.
    ///
    /// Owner, 2026-09-26: "Just leave the track alone and make start/finish at
    /// arbitrary halfway points." A sprint races in its loop's scene with the
    /// whole circuit standing; at load the list is turned round if it runs
    /// backwards, rotated to its start, and given a finish part-way round. The
    /// self-test drives that arithmetic on the bake; this drives the RACE: the
    /// grid is on the sprint's line facing down the road, the finish band is
    /// painted where the finish is, and a car carried up the road from the line
    /// is timed out at the quoted distance - not a lap later, not at the loop's
    /// own line.
    ///
    ///   tools\sprint-check.ps1 -> PSXRacing_sprint_check.txt
    /// </summary>
    public static class SprintRaceCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static List<int> sprints;

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            sprints = new List<int>();
            var scenes = EditorBuildSettings.scenes;
            for (int t = 0; t < TrackCatalog.Count; t++)
            {
                if (!TrackCatalog.At(t).IsSprintVariant) continue;
                int s = TrackCatalog.SceneIndex(t);
                if (s < scenes.Length && System.IO.File.Exists(scenes[s].path)) sprints.Add(t);
                else Check(false, TrackCatalog.At(t).id + ": its loop's scene is built");
            }
            if (sprints.Count == 0) { Finish(); EditorApplication.Exit(1); return; }
            EditorSceneManager.OpenScene(scenes[TrackCatalog.SceneIndex(sprints[0])].path);
            ReverseRaceCheck.Prime(sprints[0]);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("SprintRaceCheckRunner").AddComponent<SprintRaceCheckRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "EVERY SPRINT RUNS ON ITS LOOP, LINE TO LINE." : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "PSXRacing_sprint_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class SprintRaceCheckRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < SprintRaceCheck.sprints.Count; i++)
            {
                int t = SprintRaceCheck.sprints[i];
                if (i > 0)
                {
                    ReverseRaceCheck.Prime(t);
                    SceneManager.LoadScene(TrackCatalog.SceneIndex(t));
                    yield return null;
                }
                yield return null;
                yield return new WaitForFixedUpdate();
                yield return null;
                yield return Judge(TrackCatalog.At(t));
            }
            SprintRaceCheck.Finish();
            EditorApplication.Exit(SprintRaceCheck.failures == 0 ? 0 : 1);
        }

        static IEnumerator Judge(TrackCatalog.TrackDef v)
        {
            SprintRaceCheck.Line(v.id + " (" + v.name + ") on " + v.sprintOf + ":");
            var rm = RaceManager.Instance;
            if (rm == null || rm.path == null || rm.playerCar == null)
            { SprintRaceCheck.Check(false, "the loop's scene has a manager, a path and a player"); yield break; }
            var tp = rm.path;
            var baseDef = TrackCatalog.At(TrackCatalog.IndexOf(v.sprintOf));
            TrackCatalog.EnsureStage(baseDef);
            var bake = baseDef.stagePts;
            int n = tp.Count;

            SprintRaceCheck.Check(!tp.HasEnds && bake != null && n == bake.Length,
                "the whole loop stands - no half of it cut away", n + " of " + (bake != null ? bake.Length : 0));
            int s0 = Mathf.RoundToInt(v.sprintStartM / tp.spacing) % n;
            int s1 = Mathf.RoundToInt(v.sprintFinishM / tp.spacing) % n;
            Vector3 startPt = bake[s0], finishPt = bake[s1];
            SprintRaceCheck.Check(Vector3.Distance(tp.waypoints[0], startPt) < 0.5f,
                "the race starts at the sprint's start", Vector3.Distance(tp.waypoints[0], startPt).ToString("0.00") + " m");
            SprintRaceCheck.Check(rm.Sprint && Mathf.Abs(rm.sprintFinishIndex * tp.spacing - v.RaceMeters) < 12f,
                "and finishes part-way round, at its quoted distance",
                (rm.sprintFinishIndex * tp.spacing).ToString("0") + " m vs " + v.RaceMeters.ToString("0"));
            SprintRaceCheck.Check(Vector3.Distance(tp.GetPoint(rm.sprintFinishIndex), finishPt) < 8f,
                "at the finish the catalog names", Vector3.Distance(tp.GetPoint(rm.sprintFinishIndex), finishPt).ToString("0.0") + " m");

            // The finish band: painted, unless the finish IS the loop's line.
            var band = GameObject.Find("SprintFinish");
            bool onLoopLine = Mathf.Min(s1, n - s1) <= 3;
            SprintRaceCheck.Check(onLoopLine || (band != null && Vector3.Distance(
                    new Vector3(band.transform.position.x, 0f, band.transform.position.z),
                    new Vector3(finishPt.x, 0f, finishPt.z)) < 2f),
                onLoopLine ? "it finishes on the loop's own line" : "a finish band is painted across the road there");

            // The grid: behind the line, together, facing down the road.
            int facing = 0, behind = 0, counted = 0; float spread = 0f;
            foreach (var car in rm.allCars)
            {
                if (car == null || !car.gameObject.activeInHierarchy) continue;
                counted++;
                int idx = tp.NearestIndex(car.transform.position);
                if (Vector3.Dot(car.transform.forward, tp.GetTangent(idx)) > 0.9f) facing++;
                if (idx > n - 40) behind++;
                spread = Mathf.Max(spread, Vector3.Distance(car.transform.position, rm.playerCar.transform.position));
            }
            SprintRaceCheck.Check(counted > 1 && facing == counted, "every car faces down the sprint", facing + "/" + counted);
            SprintRaceCheck.Check(behind == counted, "and stands behind its line", behind + "/" + counted);
            SprintRaceCheck.Check(spread < 80f, "on one grid", spread.ToString("0") + " m");

            // RACE IT: wait for the green, then carry the player up the road a
            // few stations a physics step - the tracker searches round its last
            // index, so a car teleported 6 km in one go would never be found.
            float t0 = Time.realtimeSinceStartup;
            while (rm.State != RaceManager.RaceState.Racing && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            var p = rm.GetProgress(rm.playerCar);
            var body = rm.playerCar.GetComponent<Rigidbody>();
            int from = n - 3, to = n + rm.sprintFinishIndex + 4;
            bool finishedEarly = false;
            for (int k = from; k <= to && p != null && !p.finished; k += 3)
            {
                int i = k % n;
                var pos = tp.GetPoint(i) + Vector3.up * 0.4f;
                rm.playerCar.TeleportTo(pos, tp.GetRotation(i));
                // A physics step AND a frame: the tracker runs in Update, and
                // two steps inside one frame carried the car from the grid
                // straight over the line without the tracker seeing it cross.
                yield return new WaitForFixedUpdate();
                yield return null;
                if (p.finished && k < n + rm.sprintFinishIndex - 6) finishedEarly = true;
            }
            yield return null;
            SprintRaceCheck.Check(p != null && p.finished && !finishedEarly,
                "driven from the line, the player is timed out at the finish",
                p == null ? "no progress" : p.finished ? "ET " + p.finishTime.ToString("0.0") + " s"
                    : "never finished: state " + rm.State + ", crossed " + p.crossedStartOnce + ", at " + p.nearestIdx +
                      " of finish " + rm.sprintFinishIndex + ", lap " + p.lap + ", car at " +
                      tp.NearestIndex(rm.playerCar.transform.position));
        }
    }
}
