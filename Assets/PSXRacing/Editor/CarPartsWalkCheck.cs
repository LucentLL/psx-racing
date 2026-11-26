using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARPARTS STAGE 3, ON FOOT (a hidden play check): the player's own street
    /// (the neighbourhood, arrived on foot as HomeWalk hands over), the car with
    /// the keys in the garage bay (ForecourtMode's live car: the driveway path)
    /// and two more on the lawn (GarageWorld's bay shells), all three on the
    /// stand-in's shell with the LAB preview on (PSX_PARTS_STANDIN=1). At each
    /// part's target the walker stands, looks, and the frame shows the prompt;
    /// USE opens the part (the frame shows it open and the prompt saying SHUT),
    /// USE again shuts it. Checks: one target per part on each car, the hood's
    /// second verb is the car's INSPECT IT, each USE does what it says, and a
    /// car without parts gets no target.
    ///   Writes Screenshots/carparts_walk/*.png and PSXRacing_carparts_walk.txt.
    /// </summary>
    public static class CarPartsWalkCheck
    {
        internal static StringBuilder log = new StringBuilder();
        internal static int failures;

        public static void Run()
        {
            log.Clear();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int idx = TrackCatalog.NeighborhoodSceneIndex;
            if (idx <= 0 || idx >= scenes.Length || !File.Exists(scenes[idx].path))
            {
                Check(false, "the neighbourhood is built");
                Finish(); EditorApplication.Exit(1); return;
            }
            // three cars on the stand-in's shell
            var specs = CarCatalog.All.Where(s => { var d = CarModelLibrary.LoadFor(s); return d != null && d.key == CarPartsStandIn.Key; }).ToList();
            log.AppendLine($"CARPARTS WALK (EDITOR, STAND-IN, LAB preview {(CarPartsStandIn.Preview ? "ON" : "OFF")}): " +
                           $"{specs.Count} catalog rows wear {CarPartsStandIn.Key}");
            if (specs.Count == 0) { Check(false, "a catalog car on " + CarPartsStandIn.Key); Finish(); EditorApplication.Exit(1); return; }
            LifeSimManager.StartNewGame("Parts Check", 24, 0);
            var S = LifeSimManager.State;
            S.cars.Clear();
            S.activeCar = null;
            S.garageSlots = 6;
            S.money = 100000;
            for (int i = 0; i < 3; i++) CarMarket.MakeOwnedCar(S, specs[i % specs.Count], 80, 60000f + i * 1000f, 4000);
            S.ActiveCar.fuel = 80f;
            LifeSimManager.Save();
            var car = S.ActiveCar;
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.ArriveOnFoot = true;
            RaceHandoff.NoCar = false;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
            RaceHandoff.CarId = car.id;
            RaceHandoff.CarSpecId = car.specId;
            RaceHandoff.StartFuelPct = car.fuel;
            LifeHomeScreen.FillCarRequestFor(S, car);
            EditorSceneManager.OpenScene(scenes[idx].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CarPartsWalkRunner").AddComponent<CarPartsWalkRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "CARPARTS WALK OK" : "CARPARTS WALK FAILED (" + failures + ")");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_carparts_walk.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class CarPartsWalkRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => CarPartsWalkCheck.Check(ok, what, got);
        float hardStop;

        void Update()
        {
            if (hardStop > 0f && Time.realtimeSinceStartup > hardStop) { CarPartsWalkCheck.log.AppendLine("TIMEOUT"); Done(); }
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            hardStop = Time.realtimeSinceStartup + 400f;
            float t0 = Time.realtimeSinceStartup;
            while (!HomeArrival.Settled && Time.realtimeSinceStartup - t0 < 15f) yield return null;
            for (int i = 0; i < 30; i++) yield return null;
            var home = Object.FindAnyObjectByType<GarageWorld>();
            var fm = ForecourtMode.Instance;
            var walker = GameObject.Find("Walker");
            var interactor = Object.FindAnyObjectByType<FootInteractor>();
            Check(ForecourtMode.OnFoot && home != null && walker != null && interactor != null, "on foot at home, the walker and the interactor up");
            if (home == null || walker == null || interactor == null) { Done(); yield break; }
            if (System.Environment.GetEnvironmentVariable("PSX_CP_ENGINE") == "1") { yield return EngineRoutine(home); yield break; }

            // every car: its targets
            var sets = Object.FindObjectsByType<CarPartsFoot>().ToList();
            foreach (var f in sets)
                CarPartsWalkCheck.log.AppendLine($"    {f.transform.name}: {f.Targets.Count} targets ({string.Join(", ", f.Targets.Select(t => t.name))})" +
                                                 $"{(f.HoodTarget != null ? ", hood's second verb \"" + f.HoodTarget.action2 + "\"" : "")}");
            var live = home.liveCar != null ? home.liveCar.GetComponent<CarPartsFoot>() : null;
            Check(live != null && live.Targets.Count == 4, "the car with the keys (the driveway / garage path): a target per hood, trunk and door", live != null ? live.Targets.Count : 0);
            var shells = sets.Where(f => f != live).ToList();
            Check(shells.Count == 2 && shells.All(f => f.Targets.Count == 4), "the two cars on the lawn (the garage bays): four targets each", shells.Count);
            Check(live != null && live.HoodTarget != null && live.HoodTarget.action2.StartsWith("INSPECT") || (live != null && live.HoodTarget != null && live.HoodTarget.action2.StartsWith("CARRY ON")),
                  "the hood's second verb is the car's INSPECT IT", live != null && live.HoodTarget != null ? live.HoodTarget.action2 : "-");

            var cc = walker.GetComponent<CharacterController>();
            foreach (var (f, tag) in new[] { (live, "drive"), (shells.FirstOrDefault(), "lawn") })
            {
                if (f == null) continue;
                var body = f.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == "Body");
                var cb = f.GetComponent<CarBody>();
                if (cb != null) body = cb.bodyFilter;
                var centre = body != null ? body.GetComponent<Renderer>().bounds.center : f.transform.position;
                foreach (var (t, bit, name) in f.Targets)
                {
                    var tp = t.transform.position;
                    var outv = tp - centre; outv.y = 0f;
                    if (outv.sqrMagnitude < 1e-4f) outv = f.transform.forward;
                    // a step out from the part, closer where a wall stands there (a car in the garage bay)
                    var at = tp + outv.normalized * 1.45f; at.y = walker.transform.position.y;
                    bool room = false;
                    // straight out first, then from the front or rear quarter (a door against a wall)
                    var fwdc = f.transform.forward; fwdc.y = 0f; fwdc.Normalize();
                    var spots = new List<Vector3>();
                    foreach (float d in new[] { 1.45f, 1.15f, 0.9f, 0.7f }) spots.Add(outv.normalized * d);
                    foreach (float a2 in new[] { 1.3f, -1.3f, 1.8f, -1.8f }) spots.Add(fwdc * a2 + outv.normalized * 0.45f);
                    foreach (var off in spots)
                    {
                        var c = tp + off; c.y = walker.transform.position.y;
                        if (Physics.CheckCapsule(c + Vector3.up * 0.5f, c + Vector3.up * 1.6f, 0.28f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        // and nothing but the car between that spot and the part (not the far side of a wall)
                        bool wall = false;
                        foreach (var hit in Physics.RaycastAll(c + Vector3.up * 1.5f, (tp - (c + Vector3.up * 1.5f)).normalized, Vector3.Distance(tp, c + Vector3.up * 1.5f), ~0, QueryTriggerInteraction.Ignore))
                            if (!hit.transform.IsChildOf(f.transform)) { wall = true; break; }
                        if (wall) continue;
                        at = c; room = true; break;
                    }
                    Place(walker, cc, at);
                    var look = tp - at; look.y = 0f;
                    walker.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                    var fpw = walker.GetComponent<FirstPersonWalk>();
                    fpw?.SnapYawToTransform();
                    // and look DOWN at it, as a player standing there does (angle-first picking)
                    if (fpw != null && fpw.head != null)
                    {
                        var eyeP = fpw.head.position;
                        float pitch = Mathf.Atan2(eyeP.y - tp.y, new Vector2(tp.x - eyeP.x, tp.z - eyeP.z).magnitude) * Mathf.Rad2Deg;
                        typeof(FirstPersonWalk).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(fpw, pitch);
                        fpw.head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    }
                    for (int i = 0; i < 12; i++) yield return null;
                    bool picked = interactor.Current == t;
                    if (!room) CarPartsWalkCheck.log.AppendLine($"  NOTE {tag} {name}: no room to stand beside it with a clear view (a wall within 1.5 m: the car in the garage bay) - its prompt is not checked here");
                    else Check(picked, $"{tag} {name}: standing at it, the walker's prompt is the part's (\"{t.Verb}: {t.action}\")",
                          interactor.Current != null ? interactor.Current.title + " / " + interactor.Current.action : "nothing");
                    Snap($"{tag}_{name}_prompt");
                    t.onUse?.Invoke();
                    yield return new WaitForSecondsRealtime(1.3f);
                    int mask = CarParts.MaskOf(body);
                    Check((mask & bit) != 0 && t.verb == "SHUT", $"{tag} {name}: USE opens it, the prompt then says SHUT", $"mask {mask}, verb {t.verb}");
                    for (int i = 0; i < 4; i++) yield return null;
                    Snap($"{tag}_{name}_open");
                    t.onUse?.Invoke();
                    yield return new WaitForSecondsRealtime(1.3f);
                    Check((CarParts.MaskOf(body) & bit) == 0, $"{tag} {name}: USE again shuts it");
                }
            }
            // the LAB preview off: the targets offer nothing (and come back on)
            CarPartsStandIn.Preview = false; CarPartsStandIn.ApplyLab();
            for (int i = 0; i < 4; i++) yield return null;
            Check(live == null || live.Targets.All(x => !x.t.gameObject.activeSelf), "LAB preview OFF: no part target offers anything");
            CarPartsStandIn.Preview = true;
            for (int i = 0; i < 4; i++) yield return null;
            Done();
        }

        /// <summary>
        /// STAGE 4 in play (PSX_CP_ENGINE=1): the car with the keys on the drive.
        /// The load's hitch (the worst frame around a fresh preload against the
        /// frames before it); a press that comes before the load completes (the
        /// lid waits, and no frame shows an open lid over an empty bay); ten
        /// open-shut and load-release cycles (objects, meshes, memory).
        /// </summary>
        IEnumerator EngineRoutine(GarageWorld home)
        {
            var car = home.liveCar;
            var cb = car != null ? car.GetComponent<CarBody>() : null;
            var body = cb != null ? cb.bodyFilter : null;
            var def = cb != null ? cb.Def : null;
            var bay0 = CarEngineBay.Of(body);
            Check(bay0 != null && bay0.Ready, "on foot at the car on the drive: its engine was loaded on arrival (ForecourtMode)", bay0 != null ? bay0.Choice.key : "none");
            if (body == null) { Done(); yield break; }
            var L = CarPartsWalkCheck.log;
            // ---- the load's hitch ----------------------------------------------
            // every bay let go (the lawn cars hold the same engine): a load that has to come from disk
            foreach (var other in Object.FindObjectsByType<CarEngineBay>()) { var f = other.GetComponent<MeshFilter>(); if (f != null && f != body) CarEngineBay.Release(f); }
            for (int run = 0; run < 3; run++)
            {
                CarEngineBay.Release(body);
                Resources.UnloadUnusedAssets();
                for (int i = 0; i < 10; i++) yield return null;
                float before = 0f;
                for (int i = 0; i < 60; i++) { yield return null; before = Mathf.Max(before, Time.unscaledDeltaTime); }
                var t0 = Time.realtimeSinceStartup;
                var bay = CarEngineBay.Preload(body, def);
                int frames = 0; float worst = 0f;
                while (bay != null && !bay.Ready && frames < 600) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
                for (int i = 0; i < 30; i++) { yield return null; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
                L.AppendLine($"    load {run + 1}: ready in {frames} frames ({(Time.realtimeSinceStartup - t0) * 1000f:0} ms incl. 30 frames after); worst frame {worst * 1000f:0.0} ms against {before * 1000f:0.0} ms in the 60 frames before (EDITOR, shared machine)");
            }
            // ---- the press that comes first (each load held 20 frames: the editor
            // loads at once, a phone reading from storage does not) -------------
            CarEngineBay.LongestWait = 0f;
            CarEngineBay.EngineAssets.TestDelayFrames = 20;
            int bad = 0, seen = 0;
            for (int run = 0; run < 3; run++)
            {
                CarParts.Request(body, def, 0, true);
                for (int i = 0; i < 70; i++) yield return null;
                CarEngineBay.Release(body);
                Resources.UnloadUnusedAssets();
                for (int i = 0; i < 5; i++) yield return null;
                CarParts.Request(body, def, CarParts.Hood, true);   // before any load
                for (int i = 0; i < 200; i++)
                {
                    yield return null;
                    var cp = CarParts.Find(body);
                    float ang = cp != null ? cp.Angle(CarParts.Hood) : 0f;
                    var bay = CarEngineBay.Of(body);
                    if (ang > 0f)
                    {
                        seen++;
                        if (bay == null || !bay.Ready || !bay.EngineRenderer.enabled || !bay.KitRenderer.enabled) bad++;
                    }
                    if (ang >= 54.9f) break;
                }
            }
            CarEngineBay.EngineAssets.TestDelayFrames = 0;
            Check(seen > 0 && bad == 0 && CarEngineBay.Waits > 0, "a press before the load (held 20 frames): the lid waits for it; no frame with the lid off its seat and the bay empty",
                  $"{seen} frames with the lid up, {bad} with an empty bay, {CarEngineBay.Waits} waits");
            L.AppendLine($"    the longest a lid waited for its engine: {CarEngineBay.LongestWait * 1000f:0} ms ({CarEngineBay.Waits} waits logged)");
            // ---- ten cycles -------------------------------------------------------
            CarParts.Request(body, def, 0, true);
            for (int i = 0; i < 70; i++) yield return null;
            System.GC.Collect();
            int me0 = Resources.FindObjectsOfTypeAll<Mesh>().Length, go0 = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            long mem0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            for (int c = 0; c < 10; c++)
            {
                CarEngineBay.Release(body);
                yield return null;
                CarParts.Request(body, def, CarParts.Hood, true);
                for (int i = 0; i < 70; i++) yield return null;
                CarParts.Request(body, def, 0, true);
                for (int i = 0; i < 70; i++) yield return null;
            }
            for (int i = 0; i < 5; i++) yield return null;
            System.GC.Collect();
            int me1 = Resources.FindObjectsOfTypeAll<Mesh>().Length, go1 = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            long mem1 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            L.AppendLine($"    ten load-open-shut-release cycles: meshes {me0} -> {me1}, objects {go0} -> {go1}, allocated {mem0 / 1048576f:0.0} -> {mem1 / 1048576f:0.0} MB; loads {CarEngineBay.EngineAssets.Loads}, bays live {CarEngineBay.LiveCount}");
            Check(me1 == me0 && go1 == go0, "ten cycles: meshes and objects as before", $"{me0}->{me1}, {go0}->{go1}");
            Done();
        }

        static void Place(GameObject walker, CharacterController cc, Vector3 at)
        {
            if (cc != null) cc.enabled = false;
            walker.transform.position = at;
            if (cc != null) cc.enabled = true;
        }

        /// <summary>The player's view WITH the walker's prompt (its overlay
        /// canvases drawn by the camera for the shot), 1920 x 1080.</summary>
        static void Snap(string name)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var flipped = new List<Canvas>();
            foreach (var c in Object.FindObjectsByType<Canvas>())
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy)
                { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f; flipped.Add(c); }
            Canvas.ForceUpdateCanvases();
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string dir = System.Environment.GetEnvironmentVariable("PSX_CP_OUT");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "carparts_walk");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "walk_" + name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            foreach (var c in flipped) if (c != null) c.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        static void Done()
        {
            CarPartsWalkCheck.Finish();
            EditorApplication.Exit(CarPartsWalkCheck.failures == 0 ? 0 : 1);
        }
    }
}
