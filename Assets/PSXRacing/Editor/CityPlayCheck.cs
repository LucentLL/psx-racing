using System.Collections;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PLAY Charlotte headless — free roam, then the 277 race — and check
    /// where the car is.
    ///
    /// Two owner reports, one afternoon: "each time I free roam Charlotte my
    /// car is dropped from the sky", and "when I race on 277 it drops me in
    /// the center of Charlotte, just like free roam". Both are POSES, and a
    /// pose is only true once physics has had its say: the scene bakes the
    /// spawn at a height solved from data that has since moved, and the race
    /// grid is written in Awake through a teleport that used to skip the
    /// rigidbody before the car's own Awake had run. The edit-mode self-test
    /// cannot see either — nothing steps — so this enters play mode, loads
    /// the two scenes exactly as the game does (through the handoff), waits
    /// past the first physics step, and asks: is the car on a street, at
    /// street height, staying there; and is the race field on its grid at
    /// the line, nowhere near the free-roam spawn. Menu: PSX Racing/Check
    /// City Spawns.
    /// </summary>
    public static class CityPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static int roamIdx, raceIdx;

        [MenuItem("PSX Racing/Check City Spawns (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            roamIdx = TrackCatalog.IndexOf("Charlotte");
            raceIdx = TrackCatalog.IndexOf("UptownLoop");

            var scenes = EditorBuildSettings.scenes;
            foreach (var (name, idx) in new[] { ("Charlotte", roamIdx), ("UptownLoop", raceIdx) })
            {
                int s = idx >= 0 ? TrackCatalog.SceneIndex(idx) : -1;
                if (idx < 0 || s < 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path))
                    Fail(name + ": scene not built");
            }
            if (failures > 0) { Finish(); return; }

            EditorSceneManager.OpenScene(scenes[TrackCatalog.SceneIndex(roamIdx)].path);
            PrimeRoam();

            // The handoff is a pile of statics; a domain reload on the way
            // into play mode would clear them and boot a standalone scene.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>What LifeHomeScreen.StartFreeRoam hands over.</summary>
        internal static void PrimeRoam()
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.TrackIndex = roamIdx;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.StartFuelPct = 100f;
        }

        /// <summary>What a race entered from the LifeSim carries.</summary>
        internal static void PrimeRace()
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = raceIdx;
            var cars = CarCatalog.All;
            if (cars.Count > 4)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id + ";" + cars[3].id;
                RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            }
            RaceHandoff.StartFuelPct = 100f;
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CityPlayCheckRunner").AddComponent<CityPlayCheckRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what +
                           (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Fail(string what) { failures++; log.AppendLine("  FAIL " + what); }

        internal static void Finish()
        {
            log.AppendLine(failures == 0
                ? "CITY SPAWNS OK."
                : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.dataPath),
                    "PSXRacing_city_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    /// <summary>Drives the two scenes from inside play mode; see CityPlayCheck.</summary>
    public class CityPlayCheckRunner : MonoBehaviour
    {
        /// <summary>Metres a car's origin may sit above the solved tarmac and
        /// still be "on it": the spawn lift is 0.45, the reset lift 0.4, and
        /// a car in the sky is metres up.</summary>
        const float AboveRoadMax = 1.3f, BelowRoadMax = 0.3f;
        const float FieldSpreadM = 80f;
        const float LineReachM = 60f;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);

            // ---- FREE ROAM ------------------------------------------------
            // Past the first physics step, not just the first frame: the
            // seat happens in Start and a repainted pose shows on the step
            // after, so a check that looked before it would have passed.
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;

            CityPlayCheck.Line("Charlotte (free roam):");
            var mode = CityMode.Instance;
            CityPlayCheck.Check(mode != null && mode.player != null && mode.world != null && mode.world.Map != null,
                "the scene has a free-roam session with a player and a road graph");
            Vector3 roamSpawn = Vector3.zero;
            if (mode != null && mode.player != null && mode.world != null && mode.world.Map != null)
            {
                var player = mode.player;
                var map = mode.world.Map;
                float y0 = JudgeOnStreet(map, player, "the player");
                roamSpawn = player.transform.position;

                // and then it STAYS there: two seconds of physics with no
                // input, tracking the origin's height
                float minY = player.transform.position.y, maxY = minY;
                float t0 = Time.time;
                while (Time.time - t0 < 2f)
                {
                    yield return null;
                    float y = player.transform.position.y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                float vy = player.Body != null ? player.Body.linearVelocity.y : 0f;
                CityPlayCheck.Check(maxY - minY < 0.8f,
                    "the player settled where it was seated, not dropped from above",
                    "fell " + (maxY - player.transform.position.y).ToString("0.00") + " m, rose " +
                    (player.transform.position.y - minY).ToString("0.00") + " m");
                CityPlayCheck.Check(Mathf.Abs(vy) < 0.5f, "the player is at rest vertically after two seconds",
                    vy.ToString("0.00") + " m/s");
                CityPlayCheck.Check(Mathf.Abs(player.transform.position.y - y0) < 0.6f,
                    "the player is still at street height after two seconds",
                    (player.transform.position.y - y0).ToString("+0.00;-0.00") + " m from where it was seated");

                yield return Restaurant(mode, roamSpawn);
            }

            // ---- THE 277 RACE ---------------------------------------------
            CityPlayCheck.PrimeRace();
            SceneManager.LoadScene(TrackCatalog.SceneIndex(CityPlayCheck.raceIdx));
            yield return null;
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;

            CityPlayCheck.Line("UptownLoop (the 277 race):");
            var rm = RaceManager.Instance;
            CityPlayCheck.Check(rm != null && rm.path != null && rm.path.Count > 500,
                "the race scene has a manager and a filled route path",
                rm != null && rm.path != null ? rm.path.Count + " waypoints" : "none");
            CityPlayCheck.Check(CityMode.Instance == null, "no free-roam session runs under a race");
            if (rm != null && rm.path != null && rm.path.Count > 500 && rm.playerCar != null)
            {
                var tp = rm.path;
                var player = rm.playerCar;
                var world = Object.FindFirstObjectByType<CityWorld>();
                var map = world != null ? world.Map : CityMap.Get();

                float toLine = Vector3.Distance(player.transform.position, tp.GetPoint(0));
                CityPlayCheck.Check(toLine < LineReachM, "the player starts at the start line",
                    toLine.ToString("0") + " m from waypoint 0");
                float toSpawn = Vector3.Distance(player.transform.position, roamSpawn);
                CityPlayCheck.Check(roamSpawn == Vector3.zero || toSpawn > 300f,
                    "the player is nowhere near the free-roam spawn",
                    toSpawn.ToString("0") + " m from it");
                if (player.Body != null)
                    CityPlayCheck.Check(Vector3.Distance(player.Body.position, player.transform.position) < 0.5f,
                        "the player's rigidbody agrees with its transform",
                        Vector3.Distance(player.Body.position, player.transform.position).ToString("0.00") + " m apart");

                int facing = 0, counted = 0;
                float worstDot = 1f, spread = 0f;
                foreach (var car in rm.allCars)
                {
                    if (car == null || !car.gameObject.activeInHierarchy) continue;
                    counted++;
                    int idx = tp.NearestIndex(car.transform.position);
                    float dot = Vector3.Dot(car.transform.forward, tp.GetTangent(idx));
                    if (dot > 0.9f) facing++;
                    worstDot = Mathf.Min(worstDot, dot);
                    spread = Mathf.Max(spread, Vector3.Distance(car.transform.position, player.transform.position));
                    if (map != null) JudgeOnStreet(map, car, car == player ? "the player" : car.name);
                }
                CityPlayCheck.Check(facing == counted && counted > 1, "every car faces the route",
                    facing + "/" + counted + ", worst dot " + worstDot.ToString("0.00"));
                CityPlayCheck.Check(spread <= FieldSpreadM, "the whole field is on one grid",
                    spread.ToString("0") + " m apart");
            }

            CityPlayCheck.Finish();
            EditorApplication.Exit(CityPlayCheck.failures == 0 ? 0 : 1);
        }

        /// <summary>
        /// A CITY RESTAURANT (WP-07, plan critic C1). The streamed city now
        /// stands up the MERGED variant of the drive-thru and the pizzeria;
        /// what makes them places has to have come with it. Pull into the
        /// first lot's order bay and stop: ORDER is offered, the room is drawn
        /// (the viewer is within 40 m), every piece still collides; drive
        /// away and the room goes again.
        /// </summary>
        IEnumerator Restaurant(CityMode mode, Vector3 home)
        {
            CityPlayCheck.Line("a city restaurant (the WP-07 variant):");
            var world = mode.world;
            var player = mode.player;
            bool found = false;
            (byte kind, Vector2 pos) lot = default;
            foreach (var l in world.FoodLots) { lot = l; found = true; break; }
            CityPlayCheck.Check(found, "the city has restaurant lots", world.FoodLots.Count);
            if (!found) yield break;
            world.EnsureRing(new Vector3(lot.pos.x, 0f, lot.pos.y), 1);
            yield return null;

            DriveThru bay = null;
            float best = float.MaxValue;
            foreach (var d in Object.FindObjectsByType<DriveThru>(FindObjectsSortMode.None))
            {
                float dd = Vector2.Distance(new Vector2(d.transform.position.x, d.transform.position.z), lot.pos);
                if (dd < best) { best = dd; bay = d; }
            }
            CityPlayCheck.Check(bay != null && best < 60f, "the lot's order bay stood up with it",
                bay != null ? CityProps.FoodName(lot.kind) + ", " + best.ToString("0") + " m from the lot" : "no DriveThru in the scene");
            if (bay == null) yield break;
            var room = bay.GetComponentInParent<CityPropInterior>();
            CityPlayCheck.Check(room != null && room.transform.Find("CityMerged") != null,
                "it is the city variant: a merged shell, the room behind a switch");
            if (room != null)
            {
                int solids = 0;
                foreach (var c in room.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) solids++;
                CityPlayCheck.Check(solids > 20, "every piece of it still collides (walk-in, walls, counters)", solids + " colliders");
            }

            // pull in and stop
            var box = bay.GetComponent<BoxCollider>();
            var at = box != null ? box.bounds.center : bay.transform.position;
            float seat = room != null ? room.transform.position.y + CityProps.Defs[lot.kind].sink : at.y;
            player.TeleportTo(new Vector3(at.x, seat + 0.6f, at.z), Quaternion.Euler(0f, bay.transform.eulerAngles.y, 0f));
            float t0 = Time.time;
            while (Time.time - t0 < 2.5f && !DriveThru.AtBay) yield return null;
            CityPlayCheck.Check(DriveThru.AtBay, "ORDER is offered, stopped in the bay", DriveThru.Prompt ?? "no prompt");
            // The tiles built ahead of the teleport were dropped the frame
            // after (the player was still uptown) and built again round the
            // car: the restaurant standing now is a NEW instance.
            room = null;
            best = float.MaxValue;
            foreach (var r in Object.FindObjectsByType<CityPropInterior>(FindObjectsSortMode.None))
            {
                float dd = Vector2.Distance(new Vector2(r.transform.position.x, r.transform.position.z), lot.pos);
                if (dd < best) { best = dd; room = r; }
            }
            t0 = Time.time;
            while (Time.time - t0 < 2f && room != null && !room.Shown) yield return null;
            CityPlayCheck.Check(room != null && room.Shown, "the room is drawn with the player at the lot",
                room == null ? "no switch" :
                $"{(room.isActiveAndEnabled ? "running" : "NOT running")}, car {Vector3.Distance(player.transform.position, room.transform.position):0} m, " +
                $"camera {(Camera.main != null ? Vector3.Distance(Camera.main.transform.position, room.transform.position).ToString("0") + " m" : "none")}, " +
                $"world {(CityWorld.Active != null ? "active" : "none")}");

            // and 70 m away, still on the same tiles (the lot must stand, or
            // "switched off" would only mean "destroyed")
            if (room != null)
            {
                var away = room.transform.position + new Vector3(70f, 3f, 0f);
                player.TeleportTo(away, player.transform.rotation);
                t0 = Time.time;
                while (Time.time - t0 < 4f && room != null && room.Shown) yield return null;
                CityPlayCheck.Check(room != null && !room.Shown, "and switched off again 70 m away, the lot still standing",
                    room != null ? Vector3.Distance(player.transform.position, room.transform.position).ToString("0") + " m" : "the lot was dropped");
            }
            player.TeleportTo(home, player.transform.rotation);
            yield return null;
        }

        /// <summary>On a street, at the street's own height, with a collider
        /// under it. Returns the origin's height for the settle check.</summary>
        static float JudgeOnStreet(CityMap map, CarController car, string who)
        {
            var p = car.transform.position;
            bool near = map.NearestRoadPoint(new Vector2(p.x, p.z), 60f, skipLinks: false,
                out int ei, out float at, out float dist);
            CityPlayCheck.Check(near && dist < 6f, who + " is on a street",
                near ? map.edges[ei].name + " " + dist.ToString("0.0") + " m off the centreline" : "no street within 60 m");
            if (!near) return p.y;
            float roadY = map.edges[ei].YAt(at);
            float dy = p.y - roadY;
            CityPlayCheck.Check(dy > -BelowRoadMax && dy < AboveRoadMax, who + " is at street height",
                dy.ToString("+0.00;-0.00") + " m from the solved tarmac");
            bool under = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 3f + AboveRoadMax + 0.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            CityPlayCheck.Check(under, who + " has ground under it",
                under ? hit.collider.name + " " + (p.y - hit.point.y).ToString("0.00") + " m below the origin" : "nothing within 4 m");
            return p.y;
        }
    }
}
