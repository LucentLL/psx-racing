using System.Collections;
using System.Collections.Generic;
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
                yield return Trunks(mode, roamSpawn);
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

        /// <summary>
        /// THE CITY'S TREES STOP A CAR (WP-08; the stage trees' tree-play-check
        /// in Charlotte, critic C42). Stand the car on Queens Road West in Myers
        /// Park, under the densest canopy on the plan's list, find the trunk
        /// colliders the tiles stood (CityTrees.TrunkName), and drive the real
        /// car at the nearest ones from the road at 50 km/h, dead on and a
        /// car's half-width to the side: it must never get the trunk inside
        /// its body, and must be all but stopped. A run whose way to the tree
        /// is blocked (a wall, another trunk) is not a run.
        /// </summary>
        IEnumerator Trunks(CityMode mode, Vector3 home)
        {
            CityPlayCheck.Line("the city's trees (WP-08):");
            var world = mode.world; var player = mode.player; var map = world.Map;
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            var at = new Vector2((float)((-80.83678 - Lon0) * mLon), (float)((35.19280 - Lat0) * 111132.0));
            if (!map.NearestRoadPoint(at, 200f, true, out int ei, out float es, out _)) { CityPlayCheck.Fail("no road at Myers Park"); yield break; }
            var road = map.edges[ei].PointAt(es);
            player.TeleportTo(new Vector3(road.x, map.edges[ei].YAt(es) + 0.6f, road.y), player.transform.rotation);
            for (int k = 0; k < 10; k++) yield return null;
            world.EnsureRing(player.transform.position, 1);
            yield return null;

            var trunks = new List<CapsuleCollider>();
            foreach (var c in Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None))
                if (c.gameObject.name == CityTrees.TrunkName) trunks.Add(c);
            int trees = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.gameObject.name == "Trees") trees += r.GetComponent<MeshFilter>().sharedMesh.vertexCount / 8;
            CityPlayCheck.Check(trees > 100 && trunks.Count > 20, "Myers Park stands trees, the ones near the road with trunks",
                trees + " trees, " + trunks.Count + " trunk capsules on the live tiles");
            if (trunks.Count == 0) yield break;

            // nearest the car's road first
            var p0 = new Vector2(player.transform.position.x, player.transform.position.z);
            trunks.Sort((a, b) => Vector2.Distance(Plan(a), p0).CompareTo(Vector2.Distance(Plan(b), p0)));
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            int solidMask = 1 << CityWorld.SolidLayer, groundMask = ~((1 << 2) | solidMask);
            int runs = 0, tried = 0;
            // THE CAR'S OWN SOLID, in its frame: "inside the body" is judged
            // against the box the physics has (the stage harness's generic
            // 4.1 m box is 0.4 m longer at the nose than this car's collider,
            // so a trunk the collider stopped cleanly read as inside it)
            var body = player.GetComponentInChildren<BoxCollider>();
            Vector3 bc = new Vector3(0f, 0.72f, 0.05f), bh = new Vector3(0.86f, 0.5f, 2.05f);
            if (body != null && !body.isTrigger)
            {
                bc = player.transform.InverseTransformPoint(body.transform.TransformPoint(body.center));
                bh = Vector3.Scale(body.size * 0.5f, body.transform.lossyScale);
            }
            CityPlayCheck.Line($"  the car's solid: a box {bh.x * 2f:0.00} x {bh.y * 2f:0.00} x {bh.z * 2f:0.00} m centred ({bc.x:0.00},{bc.y:0.00},{bc.z:0.00}) in its frame");
            foreach (var cap in trunks)
            {
                if (runs >= 4 || tried >= 40) break;
                if (cap == null) continue;
                var trunk = cap.transform.TransformPoint(cap.center);
                var tp = new Vector2(trunk.x, trunk.z);
                if (!map.NearestRoadPoint(tp, 30f, false, out int re, out float rs, out _)) continue;
                var rp = map.edges[re].PointAt(rs);
                var dir2 = tp - rp;
                if (dir2.sqrMagnitude < 1f) continue;
                tried++;
                // the way in from the road, or up to 80 degrees either side of
                // it: the first whose 16 m run-up is open at a car's width
                // (a leafy street lines both kerbs with trunks and posts)
                var fromRoad = new Vector3(dir2.x, 0f, dir2.y).normalized;
                var dir = Vector3.zero;
                string why = "";
                foreach (float turn in new[] { 0f, 40f, -40f, 80f, -80f })
                {
                    var d = Quaternion.Euler(0f, turn, 0f) * fromRoad;
                    var st = new Vector3(trunk.x, trunk.y, trunk.z) - d * 16f;
                    if (!Physics.Raycast(st + Vector3.up * 60f, Vector3.down, out var g0, 150f, groundMask)) { why = "no ground at the run-up"; continue; }
                    var e0 = g0.point + Vector3.up * 1.0f;
                    var hitsIn = Physics.SphereCastAll(e0, 1.0f, d, 16f - 2f, solidMask, QueryTriggerInteraction.Ignore);
                    if (hitsIn.Length > 0) { why = "blocked by " + hitsIn[0].collider.name; continue; }
                    // and the run-up itself is drivable: not a slope a car cannot hold
                    if (Mathf.Abs(g0.point.y - trunk.y + cap.height * 0.5f) > 2.5f) { why = "run-up " + (g0.point.y - trunk.y + cap.height * 0.5f).ToString("0.0") + " m off the trunk's ground"; continue; }
                    dir = d; break;
                }
                if (dir == Vector3.zero) { if (tried <= 5) CityPlayCheck.Line("  --   trunk at (" + trunk.x.ToString("0") + "," + trunk.z.ToString("0") + "): " + why); continue; }
                foreach (float off in new[] { 0f, 0.9f })
                {
                    var side = Vector3.Cross(Vector3.up, dir);
                    var start = new Vector3(trunk.x, trunk.y, trunk.z) - dir * 16f + side * off;
                    if (!Physics.Raycast(start + Vector3.up * 60f, Vector3.down, out var g, 150f, groundMask)) continue;
                    player.TeleportTo(g.point + Vector3.up * 0.6f, Quaternion.LookRotation(dir, Vector3.up));
                    player.throttleInput = 0f; player.brakeInput = 1f; player.steerInput = 0f;
                    for (int k = 0; k < 30; k++) yield return new WaitForFixedUpdate();
                    if (cap == null) break;
                    player.brakeInput = 0f;
                    player.Body.linearVelocity = player.transform.forward * (50f / 3.6f);
                    bool inside = false, reached = false; float arrive = 0f, closest = float.MaxValue;
                    string insideAt = "";
                    for (int k = 0; k < Mathf.RoundToInt(2.5f / Time.fixedDeltaTime); k++)
                    {
                        player.throttleInput = 0.6f; player.steerInput = 0f; player.brakeInput = 0f;
                        yield return new WaitForFixedUpdate();
                        float slack = cap.radius * 0.5f, dist = float.MaxValue;
                        for (float up = 0f; up <= 3.5f; up += 0.25f)
                        {
                            var local = player.transform.InverseTransformPoint(new Vector3(trunk.x, trunk.y - cap.height * 0.5f + up, trunk.z)) - bc;
                            var over = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - bh.x), Mathf.Max(0f, Mathf.Abs(local.y) - bh.y), Mathf.Max(0f, Mathf.Abs(local.z) - bh.z));
                            dist = Mathf.Min(dist, over.magnitude);
                            if (Mathf.Abs(local.x) < bh.x - slack && Mathf.Abs(local.y) < bh.y - slack && Mathf.Abs(local.z) < bh.z - slack)
                            {
                                if (!inside)
                                {
                                    var ea = player.transform.eulerAngles;
                                    insideAt = $"; inside at step {k}, {up:0.00} m up the trunk, car-frame ({local.x:0.00},{local.y:0.00},{local.z:0.00}), pitch {Mathf.DeltaAngle(0f, ea.x):0} roll {Mathf.DeltaAngle(0f, ea.z):0}, {player.Body.linearVelocity.magnitude * 3.6f:0} km/h";
                                }
                                inside = true;
                            }
                        }
                        closest = Mathf.Min(closest, dist);
                        if (!reached && dist < 2.5f) { reached = true; arrive = player.Body.linearVelocity.magnitude; }
                    }
                    float end = player.Body.linearVelocity.magnitude;
                    string what = $"trunk at ({trunk.x:0},{trunk.z:0}), r {cap.radius:0.00}, {(off == 0f ? "dead on" : "offset " + off.ToString("0.0") + " m")}";
                    string got = $"closest {closest:0.00} m, arrived at {arrive * 3.6f:0} km/h, left at {end * 3.6f:0} km/h{insideAt}";
                    if (!reached) { CityPlayCheck.Line("  --   " + what + ": never got there  [" + got + "]"); continue; }
                    CityPlayCheck.Check(!inside && end < 3f, what + " stops the car", got);
                    if (off == 0f) runs++;
                }
            }
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = true;
            CityPlayCheck.Check(runs >= 3, "at least three city trunks were driven at", runs + " of " + tried + " tried");
            player.TeleportTo(home, Quaternion.identity);
            for (int k = 0; k < 10; k++) yield return null;
        }

        static Vector2 Plan(Collider c) { var p = c.bounds.center; return new Vector2(p.x, p.z); }


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
