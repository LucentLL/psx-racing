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
        const double TimeoutS = 900;

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

        IEnumerator Start()
        {
            for (int i = 0; i < 30; i++) yield return null;

            // ---- 1. home, through the house's own DRIVE door ----
            Check(TownLayout.HomeInTown && TownLayout.HomeSceneIndex == TrackCatalog.TownSceneIndex,
                  "home is the town's west end in this build", "home scene #" + TownLayout.HomeSceneIndex +
                  ", town #" + TrackCatalog.TownSceneIndex + ", street map #" + TrackCatalog.NeighborhoodSceneIndex);
            LifeHomeScreen.PendingTab = "drivehome";
            SceneManager.LoadScene(0);
            yield return WaitScene(TrackCatalog.TownSceneIndex, 150f);
            yield return Seconds(3f);
            var world = Object.FindFirstObjectByType<TownWorld>();
            car = world != null ? world.player : null;
            Check(SceneManager.GetActiveScene().buildIndex == TrackCatalog.TownSceneIndex && world != null && car != null,
                  "DRIVE from the house loads the town", SceneManager.GetActiveScene().name);
            if (world == null || car == null) { Done(); yield break; }
            var spawn = GameObject.Find("HomeSpawn");
            float fromSpawn = spawn != null ? Vector3.Distance(car.transform.position, spawn.transform.position) : -1f;
            Check(spawn != null && fromSpawn < 2.5f,
                  "the car starts on your own drive, at the town's west end",
                  "car " + car.transform.position.ToString("0.0") + ", " + fromSpawn.ToString("0.0") + " m from HomeSpawn");
            Check(world.dropDoors != null && world.dropDoors.Length >= 8 && world.dropDoors.All(d => d != null),
                  "the neighbours' doors are wired for deliveries", world.dropDoors != null ? world.dropDoors.Length : 0);

            var road = GameObject.Find("HomeRoadSlab");
            float roadX = road != null ? road.transform.position.x : -213f;
            // Picture 1: the neighbourhood end, from over the junction, up the hill to your house.
            Shot("1_home_end", new Vector3(roadX - 70f, 42f, -62f), new Vector3(roadX - 230f, 6f, 0f));
            // Picture 2: the road in, from the end of the main street.
            Shot("2_road_in", new Vector3(-115f, 6.5f, -13f), new Vector3(roadX - 70f, 1.5f, 0f));

            // ---- 2. drive to the pizzeria ----
            float shopX = world.pizzaKerb != null ? world.pizzaKerb.position.x : -14f;
            yield return DriveTo(shopX, 0f, 55f, 150f, "drove from home down your street and the road in to Tony's");
            Check(drivenM > 300f, "and it was a drive, not a placement", drivenM.ToString("0") + " m");

            // ---- 3. take a run ----
            var fm = ForecourtMode.Instance;
            Transform hook = world.pizzaHooks != null && world.pizzaHooks.Length > 0 ? world.pizzaHooks[0] : null;
            Check(fm != null && hook != null, "there is a forecourt and a shop door to walk to");
            if (fm == null || hook == null) { Done(); yield break; }
            fm.BeginAfoot(hook.position + hook.forward * -1.2f, Quaternion.LookRotation(hook.forward, Vector3.up));
            Line("  (placed: the walker is stood at the shop's door)");
            yield return Seconds(0.8f);
            var pick = TargetUnder(hook);
            Check(pick != null && pick.onUse != null && pick.verb == "PICK UP",
                  "the shop offers a run on PICK UP", pick != null ? pick.action + " / " + pick.verb : "no target");
            if (pick == null || pick.onUse == null) { Done(); yield break; }
            pick.onUse();
            yield return Seconds(0.6f);
            Check(PizzaRun.Carrying && PizzaRun.LocalDrop, "the order is on, to a house, not a race track",
                  "carrying " + PizzaRun.Carrying + ", house " + PizzaRun.DropHouse + " " + PizzaRun.DropName +
                  ", track " + PizzaRun.TrackIndex + ", par " + LifeRules.DeliveryClock(PizzaRun.ParSeconds) +
                  ", quote $" + PizzaRun.Pay);
            if (!PizzaRun.LocalDrop) { Done(); yield break; }
            var door = world.dropDoors[PizzaRun.DropHouse];
            var marker = world.dropMarkers != null && PizzaRun.DropHouse < world.dropMarkers.Length
                ? world.dropMarkers[PizzaRun.DropHouse] : null;
            int lit = world.dropMarkers != null ? world.dropMarkers.Count(m => m != null && m.activeInHierarchy) : 0;
            Check(marker != null && marker.activeInHierarchy && lit == 1,
                  "the house is marked, and only that house", lit + " marker(s) lit");
            Check(TownWorld.MapTarget(out Vector2 mapAt) && Vector2.Distance(mapAt, new Vector2(door.position.x, door.position.z)) < 0.5f,
                  "and it is the map's destination", mapAt.ToString("0.0"));
            var hud = Object.FindFirstObjectByType<RaceHUD>();
            var mm = hud != null ? hud.Minimap : null;
            bool mmDest = false;
            if (mm != null)
            {
                var m = typeof(HudMinimap).GetMethod("Destination", Any);
                var args = new object[] { Vector2.zero };
                mmDest = m != null && (bool)m.Invoke(mm, args) &&
                         Vector2.Distance((Vector2)args[0], mapAt) < 0.5f;
            }
            Check(mm != null && mm.Root != null && mm.Root.activeInHierarchy && mmDest,
                  "the HUD has a map in town, and the drop is marked on it",
                  mm == null ? "no minimap" : "root " + (mm.Root != null && mm.Root.activeInHierarchy) + ", dest " + mmDest);
            float paidBefore = S.money;

            // ---- 4. turned round at the kerb, back in, and out to the house ----
            // Turned while nobody is in it, so the boxes are not on the seat
            // for the move (they land when the driver does).
            if (world.pizzaKerb != null)
            {
                car.ResetTo(world.pizzaKerb.position + Vector3.up * 0.3f, world.pizzaKerb.rotation);
                Line("  (placed: the car is turned round at the shop's kerb, facing west)");
            }
            yield return Seconds(1.0f);
            var getIn = typeof(ForecourtMode).GetMethod("GetIn", Any);
            if (getIn != null) fm.StartCoroutine((IEnumerator)getIn.Invoke(fm, null));
            yield return Seconds(2.2f);
            Check(!ForecourtMode.OnFoot, "back in the car");
            yield return Seconds(1.0f);
            Check(PizzaCargo.Instance != null, "the boxes are on the passenger seat");

            float doorX = door.position.x;
            // Picture 3: the marked house, from the street as you come up it.
            float back = Mathf.Sign(shopX - doorX);
            Shot("3_drop_marker", new Vector3(doorX + back * 46f, door.position.y + 3.5f, 0f),
                 new Vector3(doorX, door.position.y + 4.5f, door.position.z));
            float clockAtGo = PizzaRun.LocalSeconds;
            yield return DriveTo(doorX, 0f, 55f, 150f, "drove the order from Tony's to " + PizzaRun.DropName);
            yield return Seconds(1.5f);
            Check(PizzaRun.LocalArrived, "stopping at the house is arriving",
                  "clock " + LifeRules.DeliveryClock(PizzaRun.LocalSeconds) + " (" +
                  PizzaRun.LocalSeconds.ToString("0.0") + " s; " + clockAtGo.ToString("0.0") + " s at the kerb)");
            float clockStop = PizzaRun.LocalSeconds;
            yield return Seconds(1.0f);
            Check(Mathf.Abs(PizzaRun.LocalSeconds - clockStop) < 0.05f, "and the clock stops there");

            // ---- 5. hand it over at the door ----
            var drop = TargetUnder(door);
            fm.BeginAfoot(door.position + door.forward * 1.6f - Vector3.up * 0.9f,
                          Quaternion.LookRotation(-door.forward, Vector3.up));
            Line("  (placed: the walker is stood at the door)");
            yield return Seconds(0.8f);
            Check(drop != null && drop.onUse != null && drop.verb == "DELIVER",
                  "the door takes the order on DELIVER", drop != null ? drop.title + " / " + drop.action + " / " + drop.verb : "no target");
            if (drop == null || drop.onUse == null) { Done(); yield break; }
            drop.onUse();
            yield return Seconds(0.8f);
            var done = DeliveryDoneScreen.Open;
            int tip = done != null ? done.outcome.tip : -1;
            Check(!PizzaRun.Carrying && PizzaCargo.Instance == null, "the order is gone from the car");
            Check(done != null, "the delivery-done panel is up");
            Check(done != null && tip > 0 && Mathf.Abs(S.money - paidBefore - tip) < 0.5f,
                  "and the tip is in the wallet", "tip $" + tip + ", wallet +" + (S.money - paidBefore) +
                  ", time " + (done != null ? LifeRules.DeliveryClock(done.outcome.seconds) + " par " +
                  LifeRules.DeliveryClock(done.outcome.parSeconds) + ", " + done.outcome.condition.ToString("0.00") : ""));
            if (done != null)
            {
                var canvas = done.GetComponentInChildren<Canvas>();
                var cam = Camera.main;
                Shot("4_delivery_done", cam != null ? cam.transform.position : door.position + door.forward * 6f,
                     cam != null ? cam.transform.position + cam.transform.forward * 10f : door.position, canvas,
                     (c, w, h) => CheckPanel(c, done.transform, w, h));
            }
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
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var r1 = boxes[i].r; var r2 = boxes[j].r;
                    if (r1.xMin < r2.xMax - 1f && r2.xMin < r1.xMax - 1f && r1.yMin < r2.yMax - 1f && r2.yMin < r1.yMax - 1f)
                        issues.Add("'" + boxes[i].s + "' overlaps '" + boxes[j].s + "'");
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
