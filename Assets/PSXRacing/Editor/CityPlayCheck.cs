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
            EditionParking.RecoverIfNeeded();   // a killed edition build's park, back first
            log = new StringBuilder();
            failures = 0;
            roamIdx = TrackCatalog.IndexOf("Charlotte");
            raceIdx = TrackCatalog.IndexOf("UptownLoop");
            // Which edition this plays AS (PSX_EDITION / -psxEdition; ALL by
            // default). Under CITY the two drives are launched through the
            // CITY front end's own request (CityFrontEnd.FillFreeRoam /
            // FillRace), so the check plays what the test page hands over.
            log.AppendLine("edition " + Edition.Name(Edition.Current));
            if (!Edition.HasCharlotte) Fail("this edition has no Charlotte - run it as CITY or ALL");

            var scenes = EditorBuildSettings.scenes;
            foreach (var (name, idx) in new[] { ("Charlotte", roamIdx), ("UptownLoop", raceIdx) })
            {
                int s = idx >= 0 ? TrackCatalog.SceneIndex(idx) : -1;
                if (idx < 0 || s < 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path))
                    Fail(name + ": scene not built");
            }
            // Exit too: the job runs without -quit (it leaves for play mode), so
            // a return here would leave a batch editor open with nothing to do.
            if (failures > 0) { Finish(); EditorApplication.Exit(1); return; }

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
            if (Edition.Current == EditionKind.City)
            {
                Check(PSXRacing.LifeSim.CityFrontEnd.FillFreeRoam(PSXRacing.LifeSim.CityFrontEnd.DefaultCarId(),
                          TimeOfDay.Noon, 0, out int scene) && scene == TrackCatalog.SceneIndex(roamIdx),
                      "CITY front end: FREE ROAM fills the handoff for the Charlotte scene",
                      RaceHandoff.CarSpecId);
                return;
            }
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
            if (Edition.Current == EditionKind.City)
            {
                Check(PSXRacing.LifeSim.CityFrontEnd.FillRace(raceIdx, PSXRacing.LifeSim.CityFrontEnd.DefaultCarId(),
                          TimeOfDay.Noon, 0, out int scene) && scene == TrackCatalog.SceneIndex(raceIdx),
                      "CITY front end: UPTOWN LOOP fills the handoff with a field",
                      RaceHandoff.OpponentSpecIds);
                return;
            }
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

        /// <summary>Real seconds the whole drive may take in play mode before
        /// the check calls itself HUNG and exits. A hidden job is never killed
        /// by tools\unity-wait.ps1 (the rule is for imports), so a wait that
        /// never comes - WaitForEndOfFrame in batch mode did - kept a play
        /// mode editor spinning past the tool's budget with its log growing
        /// without end. The drive itself takes well under a minute.</summary>
        const float HangSeconds = 480f;
        float bornAt;
        bool finished;
        string stage = "free roam";

        void Awake() { bornAt = Time.realtimeSinceStartup; }

        /// <summary>Unscaled, and in Update: runs whatever the coroutine is
        /// stuck on (a paused game's timeScale 0 included).</summary>
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true;
            CityPlayCheck.Fail("HUNG: no end after " + HangSeconds.ToString("0") + " s of play, stuck in " + stage +
                               " (a wait that never came)");
            CityPlayCheck.Finish();
            EditorApplication.Exit(1);
        }

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

                stage = "the drive-thru's order bay";
                yield return StartCoroutine(OrderBay(mode));
                stage = "a city restaurant (the drive-thru)";
                yield return Restaurant(mode, roamSpawn, CityProps.Burger);
                stage = "a city restaurant (the pizzeria)";
                yield return Restaurant(mode, roamSpawn, CityProps.Pizzeria);
                stage = "the city's trees";
                yield return Trunks(mode, roamSpawn);
                stage = "driving off the road (WP-14)";
                yield return DriveOff(mode, roamSpawn);
                stage = "the pause menu";
                yield return StartCoroutine(PauseCheck("roam"));
            }

            // ---- THE 277 RACE ---------------------------------------------
            stage = "the 277 race";
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

            if (finished) yield break;   // the hang watchdog already reported
            finished = true;
            CityPlayCheck.Finish();
            EditorApplication.Exit(CityPlayCheck.failures == 0 ? 0 : 1);
        }

        /// <summary>
        /// STOPPED IN A DRIVE-THRU'S ORDER BAY, with the controls live - the
        /// exact conditions under which DriveThru prompts "ORDER AT ...". In
        /// the CITY edition nothing may offer an order (DriveThru.Serves: an
        /// order is paid from, and saved to, a career the test page does not
        /// have): no centre prompt, no ORDER button, no food signpost in the
        /// lap slot, no store open. In ALL and MAIN the same stop must prompt,
        /// which is what proves the car really was in the bay. The frame is
        /// saved (watched runs) as Screenshots\city_play_orderbay.png.
        /// </summary>
        IEnumerator OrderBay(CityMode mode)
        {
            CityPlayCheck.Line("a drive-thru's order bay (" + Edition.Name(Edition.Current) + "):");
            var player = mode.player;
            var world = mode.world;
            if (!world.NearestFood(player.transform.position, out string label, out Vector2 at, out float far))
            {
                CityPlayCheck.Fail("the city has no restaurant to stop at");
                yield break;
            }
            // Drive there first (a teleport onto the nearest street): the city
            // streams around the PLAYER, and a tile stood up far from it is
            // dropped again on the next frame.
            var map = world.Map;
            if (!map.NearestRoadPoint(at, 250f, false, out int ei, out float ea, out float ed))
            {
                CityPlayCheck.Fail("no street within 250 m of " + label + " (" + at + ")");
                yield break;
            }
            Vector2 rp = map.edges[ei].PointAt(ea), rt = map.edges[ei].TangentAt(ea);
            player.TeleportTo(new Vector3(rp.x, map.edges[ei].YAt(ea) + 0.45f, rp.y),
                              Quaternion.LookRotation(new Vector3(rt.x, 0f, rt.y), Vector3.up));
            world.EnsureRing(new Vector3(rp.x, player.transform.position.y, rp.y), 1);
            float tw = Time.time;
            while (Time.time - tw < 1.5f) yield return null;
            DriveThru bay = null;
            float best = float.MaxValue;
            foreach (var d in Object.FindObjectsByType<DriveThru>(FindObjectsSortMode.None))
            {
                float dd = (new Vector2(d.transform.position.x, d.transform.position.z) - at).sqrMagnitude;
                if (dd < best) { best = dd; bay = d; }
            }
            var box = bay != null ? bay.GetComponent<BoxCollider>() : null;
            int bays = Object.FindObjectsByType<DriveThru>(FindObjectsSortMode.None).Length;
            if (box == null || Mathf.Sqrt(best) > 60f)
            {
                CityPlayCheck.Fail("no order bay streamed in at " + label + " (" + at + "): " + bays +
                                   " in the scene" + (bay != null ? ", nearest " + Mathf.Sqrt(best).ToString("0") + " m away" : ""));
                yield break;
            }
            if (!BaySpot(box, player.Body, out Vector3 spot, out Quaternion facing, out string whyNot))
            {
                CityPlayCheck.Fail("no clear, flat spot for a car inside " + bay.Title + "'s order bay (" + whyNot + ")");
                yield break;
            }
            player.TeleportTo(spot, facing);
            // Past the attribution's seven seconds, so the lap slot is the
            // signpost's, and long enough for the bay to claim a stopped car.
            // Held where it was put (an apron has a fall to it, and a car with
            // nobody on the brake creeps): stopped is the condition under test.
            float t0 = Time.time;
            while (Time.time - t0 < 2.5f || mode.SessionSeconds < 8f)
            {
                if (player.Body != null && Time.time - t0 > 0.3f)
                {
                    player.Body.linearVelocity = new Vector3(0f, Mathf.Min(0f, player.Body.linearVelocity.y), 0f);
                    player.Body.angularVelocity = Vector3.zero;
                }
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            // THE EDITOR'S OWN FAULT, not the game's: in this harness the
            // free-roam street map's "Streets" graphic turns up with no
            // CanvasRenderer and every canvas update throws after it, which
            // would also stop the HUD text below from updating (the WebGL
            // player draws the same map - see the browser captures). Give it
            // one before reading the HUD.
            int fixedUi = 0;
            foreach (var g in Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None))
                if (g.GetComponent<CanvasRenderer>() == null) { g.gameObject.AddComponent<CanvasRenderer>(); fixedUi++; }
            if (fixedUi > 0)
                CityPlayCheck.Line("  note " + fixedUi + " UI graphic(s) had no CanvasRenderer in this editor (the street map's " +
                                   "\"Streets\"); added one before reading the HUD - an editor-side fault, reported separately");

            bool inside = InBay(box, player.transform.position, 0f);
            float kmh = Mathf.Abs(player.speedKmh);
            CityPlayCheck.Check(inside && kmh <= 4.5f, "the player is stopped inside " + bay.Title + "'s order bay",
                (inside ? "inside" : "OUTSIDE") + ", " + kmh.ToString("0.0") + " km/h, " +
                Vector3.Distance(player.transform.position, box.bounds.center).ToString("0.0") + " m from its centre");

            var hud = Object.FindFirstObjectByType<RaceHUD>();
            string center = hud != null && hud.centerText != null ? hud.centerText.text : "";
            string lap = hud != null && hud.lastLapText != null ? hud.lastLapText.text : "";
            var touch = TouchControls.Instance;
            string action = "";
            if (touch != null)
                foreach (var b in touch.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                {
                    var tx = b.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (tx != null && tx.text == "ORDER") action = "ORDER";
                }
            bool storeOpen = false;
            foreach (var s in Object.FindObjectsByType<PSXRacing.OnFoot.StoreScreen>(FindObjectsSortMode.None))
                if (s.IsOpen) storeOpen = true;

            if (DriveThru.Serves)
            {
                CityPlayCheck.Check(DriveThru.AtBay && center.Contains("ORDER AT"),
                    "the window offers an order (" + Edition.Name(Edition.Current) + " has a career)", center);
            }
            else
            {
                CityPlayCheck.Check(!DriveThru.AtBay && DriveThru.Prompt == null && !center.Contains("ORDER"),
                    "CITY: no ORDER prompt at the window", "centre: '" + center + "'");
                CityPlayCheck.Check(action == "", "CITY: no ORDER button");
                CityPlayCheck.Check(!lap.Contains(label) && !lap.Contains(" km") && !lap.EndsWith(" m"),
                    "CITY: no food signpost in the lap slot", "lap slot: '" + lap + "'");
                CityPlayCheck.Check(!storeOpen, "CITY: no store is open");
            }
            stage = "the order bay's picture";
            yield return StartCoroutine(Shot("orderbay"));
        }

        /// <summary>The pause menu over the drive: EXIT TO MENU is the CITY
        /// page's way home. Opened through PauseMenu's own SetOpen and closed
        /// again. (Its picture is the browser capture of the real build: an
        /// overlay canvas is not in a camera render.)</summary>
        IEnumerator PauseCheck(string what)
        {
            var pm = Object.FindFirstObjectByType<PauseMenu>();
            var set = typeof(PauseMenu).GetMethod("SetOpen",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (pm == null || set == null) { CityPlayCheck.Fail("no pause menu to open"); yield break; }
            set.Invoke(pm, new object[] { true });
            yield return null;
            bool exit = false;
            foreach (var tx in pm.GetComponentsInChildren<UnityEngine.UI.Text>(false))
                if (tx.text == "EXIT TO MENU") exit = true;
            CityPlayCheck.Check(PauseMenu.IsOpen && exit, "the pause menu opens over the drive with EXIT TO MENU");
            set.Invoke(pm, new object[] { false });
            yield return null;
        }

        /// <summary>
        /// A CITY RESTAURANT (WP-07, plan critic C1), the first lot of each
        /// kind. The streamed city stands up the MERGED variant of the
        /// drive-thru and the pizzeria; what makes them places has to have
        /// come with it, and the room behind the switch must be drawn exactly
        /// when it can be seen. Stop where the car orders (the drive-thru's
        /// lane, the pizzeria's kerb: CityPropBaker.BayStop): ORDER is
        /// offered (and none in CITY: DriveThru.Serves), every piece still
        /// collides, and the room is drawn only if
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
            // ORDER only where the edition has a career to pay from
            // (DriveThru.Serves); in CITY the same stop offers nothing, the
            // rule OrderBay checks with the HUD as well.
            if (DriveThru.Serves)
                CityPlayCheck.Check(DriveThru.AtBay, what + ": ORDER is offered, stopped at the bay", DriveThru.Prompt ?? "no prompt");
            else
                CityPlayCheck.Check(!DriveThru.AtBay && DriveThru.Prompt == null,
                    what + ": CITY: no ORDER offered, stopped at the bay", DriveThru.Prompt ?? "no prompt");
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
        /// What the game camera sees now, through the game's own dither, to
        /// Screenshots\city_play_&lt;name&gt;.png - rendered on request from
        /// the PSX camera (CamFramePlayCheck's way), in a hidden run as well as
        /// a watched one. Not ScreenCapture: in the watched editor the Game
        /// view came back BLACK (2026-09-29, pause menu included - the view
        /// was not being presented), so a capture of it proves nothing.
        /// </summary>
        IEnumerator Shot(string name)
        {
            // A frame, never WaitForEndOfFrame: batch mode has no game view, so
            // it never comes. A hidden run (-NoWatch) hung right here, in play
            // mode, for its whole budget and past it, writing ~150 MB of
            // per-frame render errors a minute (2026-09-29, 5 GB). The render
            // request below is synchronous and needs no point in the frame.
            yield return null;
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "city_play_" + name + ".png");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            var cam = Camera.main;
            string why = null;
            // -nographics: a Null device renders nothing (RenderTexture.Create
            // fails), so there is no picture to take - said, not attempted.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                why = "no graphics device - a -nographics run";
            else if (cam == null) why = "no main camera";
            else
            {
                var target = cam.targetTexture;
                int w = target != null ? target.width : 640, h = target != null ? target.height : 360;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                rt.Create();
                StreetLights.Push(cam.transform.position, cam.transform.forward);
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                    var shown = PSXScreenshotTool.Dithered(rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = shown;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev == rt || prev == shown ? null : prev;
                    if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                else why = "the pipeline takes no render request";
                cam.targetTexture = target;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            CityPlayCheck.Line("  shot " + (System.IO.File.Exists(path) ? path + " (the game camera; the HUD text is checked above)"
                                                                          : "not written (" + name + ": " + why + ")"));
        }

        /// <summary>A place inside the bay's box where a car fits: flat ground
        /// under it, nothing solid in a car-sized box above that, and the whole
        /// car inside the bay. Nearest the bay's centre first (the window). The
        /// burger bay is centred on the menu board and the pizzeria's on the
        /// building, so the centre itself is usually taken.</summary>
        static bool BaySpot(BoxCollider box, Rigidbody self, out Vector3 spot, out Quaternion facing, out string why)
        {
            var t = box.transform;
            var b = box.bounds;
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            var facings = new[] { Quaternion.LookRotation(fwd, Vector3.up),
                                  Quaternion.LookRotation(Vector3.Cross(Vector3.up, fwd), Vector3.up) };
            var half = new Vector3(1.05f, 0.5f, 2.3f);
            var cands = new System.Collections.Generic.List<Vector3>();
            for (int iz = -8; iz <= 8; iz++)
                for (int ix = -8; ix <= 8; ix++)
                    cands.Add(t.TransformPoint(box.center + new Vector3(ix / 8f * 0.9f * box.size.x * 0.5f, 0f,
                                                                         iz / 8f * 0.9f * box.size.z * 0.5f)));
            Vector3 mid = b.center;
            cands.Sort((p, q) => (new Vector2(p.x - mid.x, p.z - mid.z)).sqrMagnitude
                                 .CompareTo((new Vector2(q.x - mid.x, q.z - mid.z)).sqrMagnitude));
            int noGround = 0, steep = 0, blocked = 0, outside = 0;
            foreach (var w in cands)
            {
                // The GROUND inside the box's height, not a canopy or a roof over it.
                RaycastHit ground = default;
                bool found = false;
                foreach (var h in Physics.RaycastAll(new Vector3(w.x, b.max.y + 0.5f, w.z), Vector3.down,
                             b.size.y + 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (h.point.y >= b.min.y - 1.5f && (!found || h.point.y < ground.point.y)) { ground = h; found = true; }
                if (!found) { noGround++; continue; }
                if (ground.normal.y < 0.94f) { steep++; continue; }
                Vector3 s = ground.point + Vector3.up * 0.45f;
                bool placed = false;
                foreach (var f in facings)
                {
                    // the whole footprint in the bay, not just the origin
                    bool inBay = true;
                    foreach (var c in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                        if (!InBay(box, s + f * Vector3.Scale(c, new Vector3(half.x, 0f, half.z)), 0f)) inBay = false;
                    if (!inBay) { outside++; continue; }
                    bool hit = false;
                    foreach (var c in Physics.OverlapBox(ground.point + Vector3.up * 1.05f, half, f,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (self == null || c.attachedRigidbody != self) { hit = true; break; }
                    if (hit) { blocked++; continue; }
                    spot = s; facing = f; why = null;
                    placed = true;
                    return true;
                }
                if (placed) break;
            }
            spot = Vector3.zero; facing = Quaternion.identity;
            why = cands.Count + " spots: " + noGround + " no ground, " + steep + " not flat, " + outside +
                  " car not wholly in the box, " + blocked + " blocked by something solid";
            return false;
        }

        /// <summary>Is a car standing at <paramref name="p"/> (its origin; the
        /// body reaches ~1.4 m up from the ground under it) in the bay's
        /// trigger: inside its footprint, in the box's own axes, and its height
        /// span overlapping the box's - the trigger fires on any overlap of the
        /// car's colliders, and the bay box starts a little above an apron
        /// that sits below the shell.</summary>
        static bool InBay(BoxCollider box, Vector3 p, float margin)
        {
            Vector3 l = box.transform.InverseTransformPoint(p) - box.center;
            Vector3 h = box.size * 0.5f;
            if (Mathf.Abs(l.x) > h.x + margin || Mathf.Abs(l.z) > h.z + margin) return false;
            var b = box.bounds;
            return p.y + 1.4f >= b.min.y && p.y - 0.6f <= b.max.y;
        }

        /// <summary>
        /// THE CITY'S TREES STOP A CAR (WP-08; the stage trees' tree-play-check
        /// in Charlotte, critic C42). Stand the car on Queens Road West in Myers
        /// Park, under the densest canopy on the plan's list, take the solid
        /// trunks from the city's trunk table (CityWorld.Trunks, which stands
        /// their capsules, named CityTrees.TrunkName, only round the cars -
        /// each run checks its trunk was stood once the car was beside it),
        /// and drive the real
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

            // the solid trunks are the city's trunk table's (WP-08 review: a
            // tile stands no collider; the table stands them round the cars)
            var table = world.Trunks;
            var trunks = new List<Vector4>();
            if (table != null) table.TableTrunksNear(player.transform.position, 150f, trunks);
            for (int k = 0; k < 15; k++) yield return new WaitForFixedUpdate();
            int trees = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.gameObject.name == "Trees") trees += r.GetComponent<MeshFilter>().sharedMesh.vertexCount / 8;
            int standing = table != null ? table.LiveColliders : 0;
            CityPlayCheck.Check(trees > 100 && trunks.Count > 20 && standing > 0, "Myers Park stands trees, the solid ones near the road in the trunk table, stood round the car",
                $"{trees} trees on the live tiles, {trunks.Count} solid trunks in the table within 150 m, {standing} capsules standing round the cars; " +
                $"slowest cell stood {(table != null ? table.WorstStandMs : 0f):0.00} ms ({(table != null ? table.WorstStandCapsules : 0)} capsules)");
            if (trunks.Count == 0) yield break;

            // nearest the car's road first
            var p0 = new Vector2(player.transform.position.x, player.transform.position.z);
            trunks.Sort((a, b) => Vector2.Distance(new Vector2(a.x, a.z), p0).CompareTo(Vector2.Distance(new Vector2(b.x, b.z), p0)));
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
            float capH = Mathf.Max(CityTrees.TrunkHeightM, 0.5f);
            foreach (var tr in trunks)
            {
                if (runs >= 4 || tried >= 40) break;
                // the capsule the table stands: base + half its height, its radius
                var trunk = new Vector3(tr.x, tr.y + capH * 0.5f, tr.z);
                var cap = new { radius = tr.w, height = Mathf.Max(capH, tr.w * 2f + 0.01f) };
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
                    // the table stood this trunk's cell when the car arrived beside it
                    bool stood = false;
                    foreach (var c in Physics.OverlapSphere(trunk, 0.05f, solidMask, QueryTriggerInteraction.Ignore))
                        if (c.gameObject.name == CityTrees.TrunkName) stood = true;
                    if (!stood) { CityPlayCheck.Check(false, $"the trunk table stood the trunk at ({trunk.x:0},{trunk.z:0}) once the car was 16 m from it"); break; }
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

        /// <summary>
        /// WP-14's play check: a car that leaves a race route at 25 m/s and
        /// 15 degrees onto the graded roadside - down a fill's bank, up a
        /// cut's back slope, onto a level verge - comes through it. Spots are
        /// found on the three routes from the solved ground (the land 12 m
        /// past the edge 1.5 m or more under the road, 1.5 m or more over it,
        /// or level), grounded, 40 m from a node, with no other road near
        /// and nothing solid in the run (a lamp post is not the grading's).
        /// Judged while the car is inside the race run-off (RaceRunOff, kept
        /// clear of trees) and while it brakes to a stop after: no hard stop
        /// (a wall or a face), upright, and never more than a car's height
        /// over the ground under it (no drop it fell off).
        /// </summary>
        IEnumerator DriveOff(CityMode mode, Vector3 home)
        {
            CityPlayCheck.Line("driving off the road onto the graded roadside (WP-14: 25 m/s, 15 degrees):");
            var world = mode.world; var player = mode.player; var map = world.Map;
            int solidMask = 1 << CityWorld.SolidLayer, groundMask = ~((1 << 2) | solidMask);
            const float Speed = 25f, Angle = 15f, Probe = 12f, Need = 1.5f;
            var picks = new List<(int e, float s, int side, string kind, float dy)>();
            int want = 2, fills = 0, cuts = 0, level = 0;
            if (map.routes != null)
                foreach (var rt in map.routes)
                    foreach (int ei in rt.edges)
                    {
                        var e = map.edges[ei];
                        if (e.link || e.length < 90f) continue;
                        for (float s = 40f; s <= e.length - 40f; s += 25f)
                        {
                            if (e.ElevatedAt(s)) continue;
                            var c = e.PointAt(s); var t = e.TangentAt(s); var n = new Vector2(-t.y, t.x);
                            float hw = e.width * 0.5f, y = e.YAt(s);
                            bool took = false;
                            for (int side = -1; side <= 1 && !took; side += 2)
                            {
                                var q = c + n * side * (hw + Probe);
                                // no other road beside it: the run is this road's roadside
                                if (map.NearestRoadPoint(q, hw + Probe + 6f, false, out int oe, out _, out float od) && oe != ei &&
                                    od < map.edges[oe].width * 0.5f + 14f) continue;
                                float dy = CityElevation.GroundY(map, q.x, q.y) - y;
                                string kind = dy <= -Need ? "fill" : dy >= Need ? "cut" : Mathf.Abs(dy) < 0.3f ? "level" : null;
                                if (kind == null) continue;
                                if (kind == "fill" && fills >= want || kind == "cut" && cuts >= want || kind == "level" && level >= want) continue;
                                picks.Add((ei, s, side, kind, dy));
                                if (kind == "fill") fills++; else if (kind == "cut") cuts++; else level++;
                                took = true;
                            }
                            if (took) s += 400f;   // spread them out
                        }
                    }
            CityPlayCheck.Line($"  spots on the routes: {fills} fill, {cuts} cut, {level} level");
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            int driven = 0, drivenFill = 0, drivenCut = 0;
            foreach (var pk in picks)
            {
                var e = map.edges[pk.e];
                var c = e.PointAt(pk.s); var t = e.TangentAt(pk.s); var n = new Vector2(-t.y, t.x) * pk.side;
                float hw = e.width * 0.5f, y = e.YAt(pk.s);
                // 2 m inside the edge, heading 15 degrees off the road toward it
                var start2 = c + n * (hw - 2f) - t * 6f;
                var dir2 = (t * Mathf.Cos(Angle * Mathf.Deg2Rad) + n * Mathf.Sin(Angle * Mathf.Deg2Rad)).normalized;
                var start = new Vector3(start2.x, e.YAt(Mathf.Max(0f, pk.s - 6f)), start2.y);
                var dir = new Vector3(dir2.x, 0f, dir2.y);
                world.EnsureRing(start, 1);
                for (int k = 0; k < 10; k++) yield return null;
                // the run to RaceRunOff's reach past the edge, clear of solids
                float runM = (2f + RaceRunOff.RunOffM) / Mathf.Sin(Angle * Mathf.Deg2Rad);
                string what = $"{pk.kind} ({pk.dy:+0.0;-0.0} m 12 m out) beside e{pk.e} '{e.name}' s={pk.s:0} side {(pk.side < 0 ? "L" : "R")} ({c.x:0},{c.y:0})";
                if (Physics.SphereCast(start + Vector3.up * 1.2f, 0.9f, dir, out var blk, runM, solidMask, QueryTriggerInteraction.Ignore))
                { CityPlayCheck.Line($"  --   {what}: {blk.collider.name} in the run at {blk.distance:0.0} m, skipped"); continue; }
                player.TeleportTo(start + Vector3.up * 0.6f, Quaternion.LookRotation(dir, Vector3.up));
                player.throttleInput = 0f; player.brakeInput = 1f; player.steerInput = 0f;
                for (int k = 0; k < 30; k++) yield return new WaitForFixedUpdate();
                player.brakeInput = 0f;
                player.Body.linearVelocity = player.transform.forward * Speed;
                float lastV = Speed, worstLoss = 0f, minUp = 1f, worstAir = 0f, past = 0f;
                bool left = false;
                var edgeLine = c + n * hw;
                for (int k = 0; k < Mathf.RoundToInt(7f / Time.fixedDeltaTime); k++)
                {
                    var pos = player.Body.position;
                    past = Vector2.Dot(new Vector2(pos.x, pos.z) - edgeLine, n);
                    bool inRunOff = past <= RaceRunOff.RunOffM;
                    if (past > 0.5f) left = true;
                    // coast across the run-off, then brake as a driver would on the grass
                    player.throttleInput = 0f; player.steerInput = 0f;
                    player.brakeInput = left && !inRunOff ? 1f : 0f;
                    yield return new WaitForFixedUpdate();
                    float v = player.Body.linearVelocity.magnitude;
                    // the braking run past the run-off is judged until something
                    // other than the land (a tree, a post) could be what stops it
                    if (inRunOff)
                    {
                        worstLoss = Mathf.Max(worstLoss, lastV - v);
                        minUp = Mathf.Min(minUp, player.transform.up.y);
                        // the ground under the car, not the car: the highest hit that is not its own
                        float gy = float.NegativeInfinity;
                        foreach (var h in Physics.RaycastAll(player.Body.position + Vector3.up * 2f, Vector3.down, 40f, groundMask, QueryTriggerInteraction.Ignore))
                            if (h.collider.attachedRigidbody != player.Body && h.point.y > gy) gy = h.point.y;
                        if (!float.IsNegativeInfinity(gy)) worstAir = Mathf.Max(worstAir, player.Body.position.y - gy);
                    }
                    else minUp = Mathf.Min(minUp, player.transform.up.y);
                    lastV = v;
                    if (!inRunOff && v < 1f) break;
                }
                string got = $"left the pavement: {(left ? "yes" : "no")}; {past:0.0} m past the edge at the end; worst speed lost in one step across the run-off {worstLoss:0.0} m/s; lowest up {minUp:0.00}; most air under the body {worstAir:0.00} m; end {player.Body.linearVelocity.magnitude * 3.6f:0} km/h";
                CityPlayCheck.Check(left && worstLoss < 4f && minUp > 0.7f && worstAir < 1.3f,
                    $"a car driven off at 25 m/s and 15 degrees comes through the {what}", got);
                driven++;
                if (pk.kind == "fill") drivenFill++; else if (pk.kind == "cut") drivenCut++;
            }
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = true;
            CityPlayCheck.Check(driven >= 4 && drivenFill > 0 && drivenCut > 0, "the drive-off ran on fills and cuts (at least four spots)",
                $"{driven} driven ({drivenFill} fill, {drivenCut} cut)");
            player.TeleportTo(home, Quaternion.identity);
            for (int k = 0; k < 10; k++) yield return null;
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
