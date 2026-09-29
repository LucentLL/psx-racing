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
                    ShotSidecar.WritePng(path, tex.EncodeToPNG());
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
