using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A WHOLE FIELD RACING (tools\race-play-check.ps1): a venue with three AI
    /// rivals and the player on autopilot, raced for PSX_RACE_SECONDS, logging
    /// every hard hit (who, how fast, into what, where) and every retirement.
    /// Written for "all three opponents knocked out in the first half mile"
    /// (2026-09-27): the owner wants a car to retire in about half of races,
    /// more often late than early - never the whole field in a minute.
    /// </summary>
    public static class RacePlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            string id = System.Environment.GetEnvironmentVariable("PSX_RACE_VENUE");
            if (string.IsNullOrEmpty(id)) id = "GillespieGap";
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
            log.AppendLine("race on " + id + ":");
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            string hour = System.Environment.GetEnvironmentVariable("PSX_RACE_HOUR");
            RaceHandoff.TimeOfDayIndex = hour == "night" ? TimeOfDay.Night : TimeOfDay.Morning;
            var cars = CarCatalog.All;
            // Four cars of a price, like a booked race: the player's and the
            // next three in the catalog.
            int seed = 0;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_RACE_SEED") ?? "0", out seed);
            int b = Mathf.Clamp(20 + seed * 7, 0, cars.Count - 5);
            RaceHandoff.CarSpecId = cars[b].id;
            RaceHandoff.OpponentSpecIds = cars[b + 1].id + ";" + cars[b + 2].id + ";" + cars[b + 3].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("RacePlayCheckRunner").AddComponent<RacePlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "RACE CHECK OK." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_race_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class RacePlayCheckRunner : MonoBehaviour
    {
        RaceManager rm;
        float t0;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            RacePlayCheck.Check(rm != null && car != null && rm.path != null, "the scene has a race, a player and a path");
            if (rm == null || car == null) { Done(); yield break; }

            CollisionResponder.HitReported += OnHit;
            float until = Time.realtimeSinceStartup + 15f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < until) yield return null;
            RacePlayCheck.Check(rm.State == RaceManager.RaceState.Racing, "the race goes live", rm.State);

            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.inputEnabled = false;
            var auto = car.gameObject.AddComponent<AIDriver>();
            auto.path = rm.path;
            auto.skill = 0.9f;

            // PSX_RACE_MISTAKE=0.3: hand every rival a driver error at that
            // fraction of the race (AIDriver.PlanMistake), to see what one does.
            var forced = System.Environment.GetEnvironmentVariable("PSX_RACE_MISTAKE");
            if (!string.IsNullOrEmpty(forced) && float.TryParse(forced, out float mf))
                foreach (var c in rm.allCars)
                {
                    var ai = c != null && c != car ? c.GetComponent<AIDriver>() : null;
                    if (ai != null) ai.PlanMistake(mf);
                }
            var wasMaking = new Dictionary<AIDriver, bool>();

            float seconds = 150f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_RACE_SECONDS") ?? "150", out seconds);
            t0 = Time.time;
            var retiredAt = new Dictionary<CarController, float>();
            while (Time.time - t0 < seconds && rm.State == RaceManager.RaceState.Racing)
            {
                yield return new WaitForSeconds(0.5f);
                foreach (var c in rm.allCars)
                {
                    var ai = c != null ? c.GetComponent<AIDriver>() : null;
                    if (ai != null && c != car)
                    {
                        wasMaking.TryGetValue(ai, out bool was);
                        if (ai.MakingMistake && !was)
                            RacePlayCheck.Note($"MISTAKE {c.name} at {Time.time - t0:0}s, wp {rm.path.NearestIndex(c.transform.position)}, " +
                                               $"{Mathf.Abs(c.forwardSpeed) * 3.6f:0} km/h");
                        wasMaking[ai] = ai.MakingMistake;
                    }
                }
                foreach (var c in rm.allCars)
                {
                    var p = c != null ? rm.GetProgress(c) : null;
                    if (p == null || !p.retired || retiredAt.ContainsKey(c)) continue;
                    retiredAt[c] = Time.time - t0;
                    var r = c.GetComponent<CollisionResponder>();
                    RacePlayCheck.Note($"RETIRED {c.name} at {Time.time - t0:0}s, wp {rm.path.NearestIndex(c.transform.position)} " +
                                       $"({rm.path.NearestIndex(c.transform.position) * rm.path.spacing:0} m): worst hit " +
                                       $"{(r != null ? r.WorstHit.ToString("0.0") : "?")} m/s into {(r != null ? r.WorstHitWhat : "?")}, " +
                                       $"damage {(r != null ? r.DamageScore.ToString("0") : "?")}");
                }
            }
            CollisionResponder.HitReported -= OnHit;
            float raced = Time.time - t0;
            int rivals = 0;
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                var r = c.GetComponent<CollisionResponder>();
                var p = rm.GetProgress(c);
                if (c != car) rivals++;
                RacePlayCheck.Note($"{c.name}{(c == car ? " (player, autopilot)" : "")}: " +
                                   $"{(p != null ? rm.path.NearestIndex(c.transform.position) * rm.path.spacing : 0f):0} m, " +
                                   $"damage {(r != null ? r.DamageScore.ToString("0") : "?")}, hard hits {(r != null ? r.HardHits : 0)}, " +
                                   $"worst {(r != null ? r.WorstHit.ToString("0.0") : "?")} into {(r != null ? r.WorstHitWhat : "?")}" +
                                   (p != null && p.retired ? ", RETIRED" : ""));
            }
            var ts = TrafficSystem.Instance;
            if (ts != null) RacePlayCheck.Note(ts.WreckLog.Count + " traffic wrecks: " + string.Join("; ", ts.WreckLog));
            RacePlayCheck.Note($"hits by kind: {string.Join(", ", kinds)}");
            RacePlayCheck.Note($"raced {raced:0} s; {retiredAt.Count} of {rivals} rivals retired");
            RacePlayCheck.Check(retiredAt.Count <= 1, "at most one rival retires in the run", retiredAt.Count);
            Done();
        }

        /// <summary>Per car, the last 3 s: time, lateral (m right of the
        /// centreline), km/h, and the AI's line + give-way and limit.</summary>
        readonly Dictionary<string, Queue<string>> trail = new Dictionary<string, Queue<string>>();

        void FixedUpdate()
        {
            if (rm == null || rm.path == null || t0 <= 0f) return;
            if (Time.fixedTime - lastTrail < 0.25f) return;
            lastTrail = Time.fixedTime;
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                var ai = c.GetComponent<AIDriver>();
                int i = rm.path.NearestIndex(c.transform.position);
                Vector3 r = Vector3.Cross(Vector3.up, rm.path.GetTangent(i)).normalized;
                float lat = Vector3.Dot(c.transform.position - rm.path.GetPoint(i), r);
                string s = $"{Time.time - t0:0.00}s lat {lat:+0.0;-0.0} {Mathf.Abs(c.forwardSpeed) * 3.6f:0}kmh" +
                           (ai != null ? $" line {ai.DebugLine:+0.0;-0.0} bias {ai.DebugBias:+0.0;-0.0}" +
                                         (float.IsNegativeInfinity(ai.DebugLeftLimit) ? "" : $" LIM {ai.DebugLeftLimit:+0.0;-0.0}") : "");
                if (!trail.TryGetValue(c.name, out var q)) trail[c.name] = q = new Queue<string>();
                q.Enqueue(s);
                while (q.Count > 12) q.Dequeue();
            }
        }
        float lastTrail;

        readonly List<string> kinds = new List<string>();
        readonly Dictionary<string, int> kindCount = new Dictionary<string, int>();

        void OnHit(CollisionResponder who, float speed, bool hard, string what)
        {
            if (speed < 6f || rm == null) return;
            string kind = what.StartsWith("WallColl") ? "wall" : what.StartsWith("Bank") ? "rock" :
                          what.StartsWith("Traffic") || what.Contains("traffic") ? "traffic" :
                          who.GetComponentInParent<CarController>() != null && what.Length > 0 && IsCarName(what) ? "car" : what;
            kindCount[kind] = kindCount.TryGetValue(kind, out int k) ? k + 1 : 1;
            kinds.Clear();
            foreach (var kv in kindCount) kinds.Add(kv.Key + " " + kv.Value);
            if (speed < 10f) return;
            // The three seconds before a hard hit, for the car that took it.
            if (speed >= 15f && trail.TryGetValue(who.name, out var tr))
                RacePlayCheck.Note($"    before {who.name}'s hit: " + string.Join(" | ", tr));
            int wp = rm.path.NearestIndex(who.transform.position);
            // Where across the road: metres right of the centreline, for the
            // car and (found by name) what it hit; and which way the road bends.
            float Lat(Vector3 p)
            {
                int i = rm.path.NearestIndex(p);
                Vector3 r = Vector3.Cross(Vector3.up, rm.path.GetTangent(i)).normalized;
                return Vector3.Dot(p - rm.path.GetPoint(i), r);
            }
            var other = GameObject.Find(what);
            Vector3 t0v = rm.path.GetTangent(wp), t1v = rm.path.GetTangent(Mathf.Min(wp + 5, rm.path.Count - 1));
            float turn = Vector3.SignedAngle(t0v, t1v, Vector3.up);
            RacePlayCheck.Note($"  hit {Time.time - t0,5:0.0}s {who.name}: {speed:0.0} m/s {(hard ? "HARD" : "glancing")} into {what} at wp {wp}" +
                               $"  car lat {Lat(who.transform.position):+0.0;-0.0}" +
                               (other != null ? $", its lat {Lat(other.transform.position):+0.0;-0.0}" : "") +
                               $", road {(turn < -3f ? "bends LEFT" : turn > 3f ? "bends right" : "straight")} ({turn:0} deg over 20 m)");
        }

        bool IsCarName(string what)
        {
            foreach (var c in rm.allCars) if (c != null && c.name == what) return true;
            return false;
        }

        void Done()
        {
            RacePlayCheck.Finish();
            EditorApplication.Exit(RacePlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
