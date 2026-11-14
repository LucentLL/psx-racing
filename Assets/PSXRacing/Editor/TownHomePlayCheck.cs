using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;
using PSXRacing.Town;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE MERGED TOWN, PLAYED: start at home in the town's west end, drive to
    /// the pizzeria, take a run, drive to the marked house, hand it over at the
    /// door, and read the tip off the panel.
    ///
    ///   Unity -batchmode -projectPath &lt;sandbox&gt; -executeMethod
    ///         PSXRacing.EditorTools.TownHomePlayCheck.Run   (NOT -nographics: it photographs)
    ///
    /// Through the real doors wherever there is one: the house's DRIVE hop
    /// ("drivehome", which loads TownLayout.HomeSceneIndex), the shop's PICK UP
    /// and the drop's DELIVER foot targets, ForecourtMode's own get-in. The car
    /// is DRIVEN — throttle, brake and steering written to the CarController
    /// with the player's input switched off, along the one straight line every
    /// road on this map shares (z 0). Two things are placed rather than walked
    /// or driven, and the log says so: the walker is stood at a door, and the
    /// car is turned round at the shop's kerb (the anchor a walk out of the
    /// shop puts it on) instead of being made to three-point turn.
    ///
    /// Writes PSXRacing_townhome_play_check.txt in the project root and four
    /// PNGs in Screenshots/TownHome.
    /// </summary>
    public static class TownHomePlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        internal static string Root => Path.GetDirectoryName(Application.dataPath);
        internal static string ShotDir => Path.Combine(Root, "Screenshots", "TownHome");

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || !File.Exists(scenes[0].path) || TrackCatalog.TownSceneIndex <= 0)
            {
                Check(false, "the LifeHome and Town scenes are built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            Directory.CreateDirectory(ShotDir);
            foreach (var f in Directory.GetFiles(ShotDir, "*.png")) File.Delete(f);

            LifeSimManager.StartNewGame("Town Check", 24, LifeRules.DefaultJobIndex);
            var S = LifeSimManager.State;
            S.money = 2000;
            S.health = 100f;
            var spec = CarCatalog.All.FirstOrDefault(c => !c.IsRaceCar && !c.IsRallyCar &&
                                                          CarShell.DefFor(c) != null && c.price < 20000);
            Check(spec != null, "a road car to drive", spec != null ? spec.name : "none");
            if (spec != null)
            {
                var car = CarMarket.MakeOwnedCar(S, spec, 92, 3000f, spec.price);
                car.fuel = 95f;
                S.activeCar = car.id;
            }
            // A block Tony's hands a run in, found by asking the schedule
            // rather than by knowing it.
            bool found = false;
            int day0 = S.day;
            for (int d = day0; d < day0 + 14 && !found; d++)
                for (int sl = 0; sl < LifeRules.SlotCount && !found; sl++)
                {
                    S.day = d;
                    S.slotIndex = sl;
                    if (WorkSchedule.CanClockOn(S)) found = true;
                }
            Check(found, "a block with a shift to take", LifeRules.DateLabel(S.day) + " " +
                  LifeRules.SlotNames[Mathf.Clamp(S.slotIndex, 0, LifeRules.SlotCount - 1)]);
            LifeSimManager.Save();

            RaceHandoff.ClearAll();
            EditorSceneManager.OpenScene(scenes[0].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            // Run WITHOUT -quit: the editor has to stay up to play. A coroutine
            // that throws never reaches Done(), so the check ends itself here
            // instead of waiting for somebody to stop the editor.
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Watchdog;
            EditorApplication.EnterPlaymode();
        }

        static double startedAt;
        const double TimeoutS = 1800;

        static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup - startedAt < TimeoutS) return;
            EditorApplication.update -= Watchdog;
            Check(false, "the check finished inside " + TimeoutS + " s (it stopped part-way: see the editor log)");
            Finish();
            EditorApplication.Exit(1);
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TownHomeCheckRunner").AddComponent<TownHomeCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Line(string s) => log.AppendLine(s);

        internal static void Finish()
        {
            log.AppendLine(failures == 0
                ? "TOWN HOME END: HOME -> PIZZERIA -> MARKED HOUSE -> DELIVERED, TIP PAID - ALL PLAYED."
                : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Root, "PSXRacing_townhome_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class TownHomeCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => TownHomePlayCheck.Check(ok, what, got);
        static void Line(string s) => TownHomePlayCheck.Line(s);
        static LifeState S => LifeSimManager.State;
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        CarController car;
        bool driving;
        float goalX, laneZ, cruiseKmh;
        float drivenM;
        Vector3 lastPos;

        void Awake() { DontDestroyOnLoad(gameObject); }

        IEnumerator WaitScene(int idx, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().buildIndex != idx && Time.realtimeSinceStartup - t0 < seconds)
                yield return null;
            for (int i = 0; i < 20; i++) yield return null;
        }

        IEnumerator Seconds(float s)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < s) yield return null;
        }

        // ------------------------------------------------------------------
        //  the driver: holds z = laneZ, runs to x = goalX and stops there
        // ------------------------------------------------------------------
        void FixedUpdate()
        {
            if (!driving || car == null) return;
            var p = car.transform.position;
            drivenM += Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(lastPos.x, 0f, lastPos.z));
            lastPos = p;
            var f = car.transform.forward; f.y = 0f; f.Normalize();
            float dir = Mathf.Sign(goalX - p.x);
            float rem = Mathf.Abs(goalX - p.x);
            var aim = new Vector3(p.x + dir * Mathf.Clamp(rem, 7f, 16f), p.y, laneZ);
            var to = aim - p; to.y = 0f;
            float ang = Vector3.SignedAngle(f, to.normalized, Vector3.up);
            car.steerInput = Mathf.Clamp(ang / 28f, -1f, 1f);
            float v = car.forwardSpeed * 3.6f;
            float want = Mathf.Min(cruiseKmh, 3.6f * Mathf.Sqrt(2f * 3.0f * Mathf.Max(0f, rem - 1.5f)));
            car.handbrakeInput = false;
            car.manualMode = false;
            if (rem < 2.5f && Mathf.Abs(v) < 4f)
            {
                car.throttleInput = 0f; car.brakeInput = 1f; car.handbrakeInput = true;
                driving = false;
                return;
            }
            if (v < want - 2f) { car.throttleInput = Mathf.Clamp01((want - v) / 14f + 0.3f); car.brakeInput = 0f; }
            else if (v > want + 3f) { car.throttleInput = 0f; car.brakeInput = Mathf.Clamp01((v - want) / 18f + 0.25f); }
            else { car.throttleInput = 0.18f; car.brakeInput = 0f; }
        }

        IEnumerator DriveTo(float x, float z, float kmh, float timeout, string what)
        {
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            goalX = x; laneZ = z; cruiseKmh = kmh;
            lastPos = car.transform.position;
            drivenM = 0f;
            driving = true;
            float t0 = Time.time;
            while (driving && Time.time - t0 < timeout) yield return new WaitForFixedUpdate();
            bool made = !driving;
            driving = false;
            car.throttleInput = 0f; car.brakeInput = 1f; car.handbrakeInput = true;
            yield return new WaitForSeconds(1.0f);
            Check(made && Mathf.Abs(car.transform.position.x - x) < 6f,
                  what, "drove " + drivenM.ToString("0") + " m in " + (Time.time - t0).ToString("0.0") +
                        " s, stopped at " + car.transform.position.ToString("0.0"));
        }

        static FootTarget TargetUnder(Transform t) => t != null ? t.GetComponentInChildren<FootTarget>(true) : null;

        TownWorld world;
        float shopX;

        static int Abs(LifeState s) => s.day * LifeRules.SlotCount + s.slotIndex;
        static string When(LifeState s) =>
            LifeRules.DateLabel(s.day) + " " + LifeRules.SlotNames[Mathf.Clamp(s.slotIndex, 0, LifeRules.SlotCount - 1)];

        static Button FindButton(Component under, string label)
        {
            if (under == null) return null;
            foreach (var bt in under.GetComponentsInChildren<Button>())
            {
                var t = bt.GetComponentInChildren<Text>();
                if (t != null && t.text == label) return bt;
            }
            return null;
        }

        IEnumerator GetIn()
        {
            var fm = ForecourtMode.Instance;
            var gi = typeof(ForecourtMode).GetMethod("GetIn", Any);
            if (fm != null && gi != null && ForecourtMode.OnFoot) fm.StartCoroutine((IEnumerator)gi.Invoke(fm, null));
            yield return Seconds(2.2f);
        }

        /// <summary>DRIVE from the house: the front end's own "drivehome"
        /// hop, which loads TownLayout.HomeSceneIndex.</summary>
        IEnumerator HomeToTown(bool first)
        {
            LifeHomeScreen.PendingTab = "drivehome";
            SceneManager.LoadScene(0);
            yield return WaitScene(TrackCatalog.TownSceneIndex, 150f);
            yield return Seconds(3f);
            world = Object.FindFirstObjectByType<TownWorld>();
            car = world != null ? world.player : null;
            Check(SceneManager.GetActiveScene().buildIndex == TrackCatalog.TownSceneIndex && world != null && car != null,
                  "DRIVE from the house loads the town", SceneManager.GetActiveScene().name + ", " + When(S));
            if (world == null || car == null) { car = null; yield break; }
            var spawn = GameObject.Find("HomeSpawn");
            float fromSpawn = spawn != null ? Vector3.Distance(car.transform.position, spawn.transform.position) : -1f;
            Check(spawn != null && fromSpawn < 2.5f, "the car starts on your own drive, at the town's west end",
                  "car " + car.transform.position.ToString("0.0") + ", " + fromSpawn.ToString("0.0") + " m from HomeSpawn");
            shopX = world.pizzaKerb != null ? world.pizzaKerb.position.x : -14f;
            if (!first) yield break;
            Check(world.dropDoors != null && world.dropDoors.Length >= 8 && world.dropDoors.All(d => d != null),
                  "the neighbours' doors are wired for deliveries", world.dropDoors != null ? world.dropDoors.Length : 0);
            var road = GameObject.Find("HomeRoadSlab");
            float roadX = road != null ? road.transform.position.x : -213f;
            // Picture 1: the neighbourhood end, from over the junction, up the hill to your house.
            Shot("1_home_end", new Vector3(roadX - 70f, 42f, -62f), new Vector3(roadX - 230f, 6f, 0f));
            // Picture 2: the road in, from the end of the main street.
            Shot("2_road_in", new Vector3(-115f, 6.5f, -13f), new Vector3(roadX - 70f, 1.5f, 0f));
        }

        /// <summary>Drive to Tony's from wherever the car is (pointed at it),
        /// get out at the door, and take the run.</summary>
        IEnumerator ToShopAndPick(string what, bool expectNext)
        {
            yield return DriveTo(shopX, 0f, 55f, 150f, what);
            var fm = ForecourtMode.Instance;
            Transform hook = world.pizzaHooks != null && world.pizzaHooks.Length > 0 ? world.pizzaHooks[0] : null;
            if (fm == null || hook == null) { Check(false, "a forecourt and a shop door to walk to"); yield break; }
            fm.BeginAfoot(hook.position + hook.forward * -1.2f, Quaternion.LookRotation(hook.forward, Vector3.up));
            Line("  (placed: the walker is stood at the shop's door)");
            yield return Seconds(0.8f);
            var pick = TargetUnder(hook);
            bool nextOk = !expectNext || (pick != null && pick.action.StartsWith("NEXT RUN"));
            Check(pick != null && pick.onUse != null && pick.verb == "PICK UP" && nextOk,
                  expectNext ? "on shift, the counter hands over the next run without clocking on again"
                             : "the shop offers a run on PICK UP",
                  pick != null ? pick.action + " / " + pick.verb : "no target");
            if (pick == null || pick.onUse == null) yield break;
            pick.onUse();
            yield return Seconds(0.6f);
            Check(PizzaRun.Carrying && PizzaRun.LocalDrop && PizzaRun.ShiftOn,
                  "the order is on, to a house, not a race track, and the shift is on",
                  "house " + PizzaRun.DropHouse + " " + PizzaRun.DropName + ", track " + PizzaRun.TrackIndex +
                  ", par " + LifeRules.DeliveryClock(PizzaRun.ParSeconds) + ", quote $" + PizzaRun.Pay);
        }

        /// <summary>From the shop's door: the car turned round at the kerb,
        /// back in, driven to the order's house, stopped, out, DELIVER. Leaves
        /// the done panel up.</summary>
        IEnumerator DriveAndDeliver(bool firstRun)
        {
            var door = world.dropDoors[PizzaRun.DropHouse];
            var marker = world.dropMarkers != null && PizzaRun.DropHouse < world.dropMarkers.Length
                ? world.dropMarkers[PizzaRun.DropHouse] : null;
            int lit = world.dropMarkers != null ? world.dropMarkers.Count(m => m != null && m.activeInHierarchy) : 0;
            Check(marker != null && marker.activeInHierarchy && lit == 1,
                  "the house is marked, and only that house", lit + " marker(s) lit");
            Check(TownWorld.MapTarget(out Vector2 mapAt) &&
                  Vector2.Distance(mapAt, new Vector2(door.position.x, door.position.z)) < 0.5f,
                  "and it is the map's destination", mapAt.ToString("0.0"));
            if (world.pizzaKerb != null)
            {
                car.ResetTo(world.pizzaKerb.position + Vector3.up * 0.3f, world.pizzaKerb.rotation);
                Line("  (placed: the car is turned round at the shop's kerb, facing west)");
            }
            yield return Seconds(1.0f);
            yield return GetIn();
            Check(!ForecourtMode.OnFoot, "back in the car");
            yield return Seconds(1.0f);
            if (firstRun)
            {
                // The HUD's map is a driving instrument (it hides on foot).
                var hud = Object.FindFirstObjectByType<RaceHUD>();
                var mm = hud != null ? hud.Minimap : null;
                bool mmDest = false;
                if (mm != null)
                {
                    var dm = typeof(HudMinimap).GetMethod("Destination", Any);
                    var args = new object[] { Vector2.zero };
                    mmDest = dm != null && (bool)dm.Invoke(mm, args) &&
                             Vector2.Distance((Vector2)args[0], mapAt) < 0.5f;
                }
                Check(mm != null && mm.Root != null && mm.Root.activeInHierarchy && mmDest,
                      "the HUD has a map in town, and the drop is marked on it",
                      mm == null ? "no minimap" : "root " + (mm.Root != null && mm.Root.activeInHierarchy) + ", dest " + mmDest);
                float back = Mathf.Sign(shopX - door.position.x);
                // Picture 3: the marked house, from the street as you come up it.
                Shot("3_drop_marker", new Vector3(door.position.x + back * 46f, door.position.y + 3.5f, 0f),
                     new Vector3(door.position.x, door.position.y + 4.5f, door.position.z));
            }
            Check(PizzaCargo.Instance != null, "the boxes are on the passenger seat");
            float clockAtGo = PizzaRun.LocalSeconds;
            yield return DriveTo(door.position.x, 0f, 55f, 150f, "drove the order from Tony's to " + PizzaRun.DropName);
            yield return Seconds(1.5f);
            Check(PizzaRun.LocalArrived, "stopping at the house is arriving",
                  "clock " + LifeRules.DeliveryClock(PizzaRun.LocalSeconds) + " (" +
                  PizzaRun.LocalSeconds.ToString("0.0") + " s; " + clockAtGo.ToString("0.0") + " s at the kerb)");
            float clockStop = PizzaRun.LocalSeconds;
            yield return Seconds(1.0f);
            Check(Mathf.Abs(PizzaRun.LocalSeconds - clockStop) < 0.05f, "and the clock stops there");
            var drop = TargetUnder(door);
            var fm = ForecourtMode.Instance;
            fm.BeginAfoot(door.position + door.forward * 1.6f - Vector3.up * 0.9f,
                          Quaternion.LookRotation(-door.forward, Vector3.up));
            Line("  (placed: the walker is stood at the door)");
            yield return Seconds(0.8f);
            Check(drop != null && drop.onUse != null && drop.verb == "DELIVER", "the door takes the order on DELIVER",
                  drop != null ? drop.title + " / " + drop.action + " / " + drop.verb : "no target");
            if (drop == null || drop.onUse == null) yield break;
            int before = S.money;
            drop.onUse();
            yield return Seconds(0.8f);
            var done = DeliveryDoneScreen.Open;
            Check(!PizzaRun.Carrying && PizzaCargo.Instance == null && done != null,
                  "the order is gone from the car and the delivery-done panel is up");
            if (done != null)
                Check(done.outcome.tip > 0 && S.money - before == done.outcome.tip,
                      "and the tip is in the wallet", "tip $" + done.outcome.tip + ", wallet +" + (S.money - before) +
                      ", time " + LifeRules.DeliveryClock(done.outcome.seconds) + " par " +
                      LifeRules.DeliveryClock(done.outcome.parSeconds) + ", " + done.outcome.condition.ToString("0.00"));
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 30; i++) yield return null;
            Check(TownLayout.HomeInTown && TownLayout.HomeSceneIndex == TrackCatalog.TownSceneIndex,
                  "home is the town's west end in this build", "home scene #" + TownLayout.HomeSceneIndex +
                  ", town #" + TrackCatalog.TownSceneIndex + ", street map #" + TrackCatalog.NeighborhoodSceneIndex);

            // ==== A. a shift in town: two deliveries, then END SHIFT ====
            Line("--- A. a shift: two deliveries, then END SHIFT");
            yield return HomeToTown(true);
            if (car == null) { Done(); yield break; }
            int abs0 = Abs(S), blocks = WorkSchedule.BlocksToSpend(S);
            int money0 = S.money;
            string start = When(S);
            yield return ToShopAndPick("drove from home down your street and the road in to Tony's", false);
            if (!PizzaRun.LocalDrop) { Done(); yield break; }
            Check(Abs(S) == abs0, "clocking on spends no block yet: the shift is spent when it ends", When(S));
            yield return DriveAndDeliver(true);
            var done = DeliveryDoneScreen.Open;
            if (done == null) { Done(); yield break; }
            int tips = done.outcome.tip;
            var next = FindButton(done, "NEXT ORDER");
            var end = FindButton(done, "END SHIFT");
            Check(next != null && end != null, "on a shift the panel offers NEXT ORDER and END SHIFT");
            var canvas = done.GetComponentInChildren<Canvas>();
            var cam = Camera.main;
            Shot("4_delivery_done", cam != null ? cam.transform.position : Vector3.up * 20f,
                 cam != null ? cam.transform.position + cam.transform.forward * 10f : Vector3.zero, canvas,
                 (c, w, h) => CheckPanel(c, done.transform, w, h));
            if (next == null) { Done(); yield break; }
            next.onClick.Invoke();
            yield return Seconds(1.0f);
            Check(DeliveryDoneScreen.Open == null && PizzaRun.ShiftOn && !PizzaRun.Carrying &&
                  TownWorld.Cue != null && TownWorld.Cue.Contains("TONY"),
                  "NEXT ORDER: the panel goes, the shift stays on, the cue points back to Tony's", TownWorld.Cue);
            car.ResetTo(car.transform.position + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.right, Vector3.up));
            Line("  (placed: the car is turned round where it stopped, facing the shop)");
            yield return Seconds(0.8f);
            yield return GetIn();
            yield return ToShopAndPick("drove back from the drop to Tony's", true);
            if (!PizzaRun.LocalDrop) { Done(); yield break; }
            Check(Abs(S) == abs0, "the second run spends no block either", When(S));
            yield return DriveAndDeliver(false);
            done = DeliveryDoneScreen.Open;
            if (done == null) { Done(); yield break; }
            tips += done.outcome.tip;
            end = FindButton(done, "END SHIFT");
            Check(end != null, "and after the second drop, END SHIFT");
            if (end == null) { Done(); yield break; }
            end.onClick.Invoke();
            yield return WaitScene(0, 90f);
            yield return Seconds(2f);
            int moved = Abs(S) - abs0;
            Check(SceneManager.GetActiveScene().buildIndex == 0 && !PizzaRun.ShiftOn && moved == blocks,
                  "END SHIFT clocks off and goes home, the shift's blocks spent as the schedule has them",
                  start + " -> " + When(S) + ": " + moved + " block(s), the shift's " + blocks);
            Check(tips > 0 && S.money - money0 >= tips, "both drops' tips are in the wallet",
                  "+" + (S.money - money0) + " (tips " + tips + ")");

            // ==== B. END DRIVE part-way through a shift ====
            Line("--- B. END DRIVE after a drop, on shift: one block, the same day");
            bool found = false;
            int fromDay = S.day;
            for (int d = fromDay + 1; d < fromDay + 15 && !found; d++)
                for (int sl = 0; sl < LifeRules.SlotCount - 1 && !found; sl++)
                {
                    S.day = d; S.slotIndex = sl; S.workedToday = false;
                    if (WorkSchedule.CanClockOn(S)) found = true;
                }
            Check(found, "a later shift block, not the day's last (the check moves the clock to it)", When(S));
            if (!found) { Done(); yield break; }
            LifeSimManager.Save();
            yield return HomeToTown(false);
            if (car == null) { Done(); yield break; }
            int dayB = S.day, absB = Abs(S);
            string startB = When(S);
            yield return ToShopAndPick("drove from home to Tony's", false);
            if (!PizzaRun.LocalDrop) { Done(); yield break; }
            yield return DriveAndDeliver(false);
            done = DeliveryDoneScreen.Open;
            next = done != null ? FindButton(done, "NEXT ORDER") : null;
            if (next != null) next.onClick.Invoke();
            yield return Seconds(1.0f);
            yield return GetIn();
            var menu = Object.FindFirstObjectByType<PauseMenu>();
            var exit = typeof(PauseMenu).GetMethod("ExitToMenu", Any);
            Check(menu != null && exit != null && PizzaRun.ShiftOn, "on shift, back in the car, the pause menu's END DRIVE");
            if (menu == null || exit == null) { Done(); yield break; }
            exit.Invoke(menu, null);
            yield return WaitScene(0, 90f);
            yield return Seconds(2f);
            Check(SceneManager.GetActiveScene().buildIndex == 0 && S.day == dayB && Abs(S) - absB == 1 && !PizzaRun.ShiftOn,
                  "END DRIVE: home, the same day, one block on", startB + " -> " + When(S));
            LifeSimManager.Save();
            Done();
        }

        // ------------------------------------------------------------------
        //  pictures, and the panel's text
        // ------------------------------------------------------------------
        const int ShotW = 1280, ShotH = 720;

        static void Shot(string name, Vector3 from, Vector3 at, Canvas ui = null,
                         System.Action<Camera, int, int> measure = null)
        {
            var go = new GameObject("CheckCam");
            var cam = go.AddComponent<Camera>();
            cam.transform.position = from;
            cam.transform.LookAt(at);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2500f;
            var main = Camera.main;
            if (main != null) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }
            var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            RenderMode mode = RenderMode.ScreenSpaceOverlay;
            Camera oldCam = null;
            CanvasScaler scaler = null;
            CanvasScaler.ScaleMode oldScale = CanvasScaler.ScaleMode.ConstantPixelSize;
            float oldFactor = 1f;
            if (ui != null)
            {
                mode = ui.renderMode; oldCam = ui.worldCamera;
                ui.renderMode = RenderMode.ScreenSpaceCamera;
                ui.worldCamera = cam;
                ui.planeDistance = 1f;
                scaler = ui.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    oldScale = scaler.uiScaleMode; oldFactor = scaler.scaleFactor;
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = ShotH / MenuKit.DesignHeight;
                }
                Canvas.ForceUpdateCanvases();
            }
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
            {
                req.destination = rt;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            }
            else cam.Render();
            measure?.Invoke(cam, ShotW, ShotH);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(TownHomePlayCheck.ShotDir, "townhome_" + name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Line("  picture: " + path);
            if (ui != null)
            {
                ui.renderMode = mode; ui.worldCamera = oldCam;
                if (scaler != null) { scaler.uiScaleMode = oldScale; scaler.scaleFactor = oldFactor; }
            }
            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(tex);
            Object.Destroy(go);
        }

        /// <summary>No line on the panel clipped by its own box or the
        /// screen, and no two lines (or a line and the button) on top of each
        /// other — measured on the glyphs as drawn, at the picture's size.</summary>
        static void CheckPanel(Camera cam, Transform root, int w, int h)
        {
            const float Slack = 2f;
            var boxes = new List<(Rect r, string s)>();
            var issues = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Text>())
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
                var gen = t.cachedTextGenerator;
                if (gen == null || gen.vertexCount == 0) { issues.Add("'" + t.text + "' drew nothing"); continue; }
                if (gen.characterCountVisible < t.text.Replace(" ", "").Length && gen.characterCountVisible < t.text.Length)
                    issues.Add("'" + t.text + "' truncated to " + gen.characterCountVisible + " chars");
                float upp = 1f / Mathf.Max(0.0001f, t.pixelsPerUnit);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var v in gen.verts)
                {
                    Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, t.rectTransform.TransformPoint(v.position * upp));
                    x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x); y0 = Mathf.Min(y0, sp.y); y1 = Mathf.Max(y1, sp.y);
                }
                var c = new Vector3[4];
                t.rectTransform.GetWorldCorners(c);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                if (x0 < a.x - Slack || x1 > b.x + Slack || y0 < a.y - Slack || y1 > b.y + Slack)
                    issues.Add("'" + t.text + "' spills out of its own box");
                if (x0 < -Slack || x1 > w + Slack || y0 < -Slack || y1 > h + Slack)
                    issues.Add("'" + t.text + "' is off the screen");
                boxes.Add((Rect.MinMaxRect(x0, y0, x1, y1), t.text));
            }
            bool Over(Rect r1, Rect r2) =>
                r1.xMin < r2.xMax - 1f && r2.xMin < r1.xMax - 1f && r1.yMin < r2.yMax - 1f && r2.yMin < r1.yMax - 1f;
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                    if (Over(boxes[i].r, boxes[j].r))
                        issues.Add("'" + boxes[i].s + "' overlaps '" + boxes[j].s + "'");
            // And no line rides over a BUTTON it is not the label of.
            foreach (var btn in root.GetComponentsInChildren<Button>())
            {
                var c = new Vector3[4];
                ((RectTransform)btn.transform).GetWorldCorners(c);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                var br = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                var own = btn.GetComponentInChildren<Text>();
                foreach (var bx in boxes)
                    if ((own == null || bx.s != own.text) && Over(bx.r, br))
                        issues.Add("'" + bx.s + "' runs over the " + (own != null ? own.text : btn.name) + " button");
            }
            Check(issues.Count == 0, "the delivery-done panel: " + boxes.Count + " lines, none clipped, none overlapping",
                  issues.Count == 0 ? string.Join(" | ", boxes.Select(x => x.s)) : string.Join(" | ", issues));
        }

        static void Done()
        {
            TownHomePlayCheck.Finish();
            EditorApplication.Exit(TownHomePlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
