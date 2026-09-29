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

                yield return Restaurant(mode, roamSpawn, CityProps.Burger);
                yield return Restaurant(mode, roamSpawn, CityProps.Pizzeria);
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
        /// A CITY RESTAURANT (WP-07, plan critic C1), the first lot of each
        /// kind. The streamed city stands up the MERGED variant of the
        /// drive-thru and the pizzeria; what makes them places has to have
        /// come with it, and the room behind the switch must be drawn exactly
        /// when it can be seen. Stop where the car orders (the drive-thru's
        /// lane, the pizzeria's kerb: CityPropBaker.BayStop): ORDER is
        /// offered, every piece still collides, and the room is drawn only if
        /// a door has swung open for the car (the doorway shows it) - the
        /// windows are opaque. Put the camera inside the building: drawn;
        /// back out behind the car: gone. Pull up to a door: it swings open
        /// and the room is drawn through it. 70 m off: door shut, room gone.
        /// </summary>
        IEnumerator Restaurant(CityMode mode, Vector3 home, byte kind)
        {
            string what = CityProps.FoodName(kind);
            CityPlayCheck.Line($"a city restaurant, {what} (the WP-07 variant):");
            var world = mode.world;
            var player = mode.player;
            bool found = false;
            (byte kind, Vector2 pos) lot = default;
            foreach (var l in world.FoodLots) if (l.kind == kind) { lot = l; found = true; break; }
            CityPlayCheck.Check(found, what + ": the city has a lot", world.FoodLots.Count + " restaurant lots");
            if (!found) yield break;

            // stand the lot up, find its bay and its room
            DriveThru bay = null;
            CityPropInterior room = null;
            float best = float.MaxValue;
            void FindLot()
            {
                bay = null; room = null; best = float.MaxValue;
                foreach (var d in Object.FindObjectsByType<DriveThru>(FindObjectsSortMode.None))
                {
                    float dd = Vector2.Distance(new Vector2(d.transform.position.x, d.transform.position.z), lot.pos);
                    if (dd < best) { best = dd; bay = d; }
                }
                if (bay != null) room = bay.GetComponentInParent<CityPropInterior>();
            }
            world.EnsureRing(new Vector3(lot.pos.x, 0f, lot.pos.y), 1);
            yield return null;
            FindLot();
            CityPlayCheck.Check(bay != null && best < 60f, what + ": the lot's order bay stood up with it",
                bay != null ? best.ToString("0") + " m from the lot" : "no DriveThru in the scene");
            if (bay == null) yield break;
            CityPlayCheck.Check(room != null && room.transform.Find("CityMerged") != null,
                what + ": it is the city variant, a merged shell with the room behind a switch");
            if (room == null) yield break;
            int solids = 0;
            foreach (var c in room.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) solids++;
            CityPlayCheck.Check(solids > 20, what + ": every piece of it still collides (walk-in, walls, counters)", solids + " colliders");

            // pull in where it orders and stop
            var stop = CityPropBaker.BayStop(bay, room, out var along);
            float seat = room.transform.position.y + CityProps.Defs[lot.kind].sink;
            player.TeleportTo(new Vector3(stop.x, seat + 0.6f, stop.z), Quaternion.LookRotation(along, Vector3.up));
            float t0 = Time.time;
            while (Time.time - t0 < 2.5f && !DriveThru.AtBay) yield return null;
            CityPlayCheck.Check(DriveThru.AtBay, what + ": ORDER is offered, stopped at the bay", DriveThru.Prompt ?? "no prompt");
            // The tiles built ahead of the teleport were dropped the frame
            // after (the player was elsewhere) and built again round the car:
            // the restaurant standing now is a NEW instance.
            FindLot();
            CityPlayCheck.Check(room != null && room.isActiveAndEnabled, what + ": the lot's room switch is running",
                room == null ? "no switch" : $"world {(CityWorld.Active != null ? "active" : "none")}");
            if (room == null) { player.TeleportTo(home, player.transform.rotation); yield break; }
            var sw = room;
            string Where() =>
                $"car {sw.DistanceTo(player.transform.position):0.0} m, camera " +
                $"{(Camera.main != null ? sw.DistanceTo(Camera.main.transform.position).ToString("0.0") + " m" : "none")} from the room's hull, " +
                $"door {(sw.AnyDoorOpen() ? "OPEN" : "shut")}, room {(sw.Shown ? "DRAWN" : "off")}";

            // 1. At the bay the room is drawn exactly when a door has swung
            // open for the car: the windows are opaque (CityPropBaker's room
            // check renders it both ways from here and counts the pixels).
            // A second is four of the switch's checks.
            t0 = Time.time;
            bool wrong = false;
            while (Time.time - t0 < 1f) { wrong |= sw.Shown != sw.AnyDoorOpen(); yield return null; }
            CityPlayCheck.Check(!wrong && sw.Shown == sw.AnyDoorOpen() && sw.DistanceTo(player.transform.position) > CityPropInterior.InM,
                what + ": at the bay the room is drawn only if a door stands open (the windows hide it)", Where());

            // 2. The camera inside the building: drawn; back out behind the
            // car: gone again (unless a door is open for the car).
            var chase = ChaseCamera.Active;
            var cam = Camera.main;
            if (cam != null)
            {
                if (chase != null) chase.enabled = false;
                cam.transform.position = sw.transform.TransformPoint(sw.hull.center);
                t0 = Time.time;
                while (Time.time - t0 < 1f && !sw.Shown) yield return null;
                CityPlayCheck.Check(sw.Shown, what + ": the room is drawn with the camera inside the building", Where());
                if (chase != null) chase.enabled = true;
                t0 = Time.time;
                while (Time.time - t0 < 2f && sw.Shown != sw.AnyDoorOpen()) yield return null;
                CityPlayCheck.Check(sw.Shown == sw.AnyDoorOpen(), what + ": and goes again with the camera back behind the car", Where());
            }
            else CityPlayCheck.Check(false, what + ": a camera to put inside the building", "no Camera.main");

            // 3. A door swung open (the car pulled up to it): the doorway
            // shows the room, so it is drawn - the frame the leaf moves.
            SwingDoor door = null;
            var hc = sw.transform.TransformPoint(sw.hull.center);
            float doorD = float.MaxValue;
            foreach (var dr in sw.GetComponentsInChildren<SwingDoor>(true))
            {
                float dd = Vector3.Distance(dr.transform.position, hc);
                if (dd < doorD) { doorD = dd; door = dr; }
            }
            if (door != null)
            {
                var n = door.transform.parent != null ? door.transform.parent.TransformDirection(door.throughNormal) : door.throughNormal;
                n.y = 0f; n = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.forward;
                if (Vector3.Dot(door.transform.position - hc, n) < 0f) n = -n;   // the outside
                var p = door.transform.position + n * 2.8f;
                player.TeleportTo(new Vector3(p.x, seat + 0.6f, p.z), Quaternion.LookRotation(Vector3.Cross(Vector3.up, n), Vector3.up));
                t0 = Time.time;
                while (Time.time - t0 < 2f && !(sw.AnyDoorOpen() && sw.Shown)) yield return null;
                CityPlayCheck.Check(sw.AnyDoorOpen() && sw.Shown, what + ": a door swung open for the car and the room is drawn through it", Where());
            }
            else CityPlayCheck.Check(false, what + ": the restaurant has a hinged door", "none");

            // 4. 70 m away, still on the same tiles (the lot must stand, or
            // "switched off" would only mean "destroyed"): door shut, room off.
            var away = sw.transform.position + new Vector3(70f, 3f, 0f);
            player.TeleportTo(away, player.transform.rotation);
            t0 = Time.time;
            while (Time.time - t0 < 4f && sw != null && (sw.Shown || sw.AnyDoorOpen())) yield return null;
            CityPlayCheck.Check(sw != null && !sw.Shown && !sw.AnyDoorOpen(), what + ": 70 m away the door shuts and the room goes, the lot still standing",
                sw != null ? Where() : "the lot was dropped");
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
