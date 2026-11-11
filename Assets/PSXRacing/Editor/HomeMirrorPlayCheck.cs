using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE REAR-VIEW AT HOME (owner, 2026-11-11: in the yellow CTR at HOME, the
    /// rear-view mirror full of dark mechanical parts and a wheel cluster). In
    /// play, a visible editor: a new game with PSX_HOME_CAR (default the
    /// Yellowbird) as the active car, free roam in the Town from home, into
    /// the car, cockpit view; then the driver's frame, the rear-view's own
    /// picture, and every renderer the rear-view camera can see within 12 m -
    /// as it renders (the cockpit's cut for it applied), whose it is, and why
    /// it is not culled. Writes PSXRacing_home_mirror.txt and
    /// Screenshots/HomeMirror/*.png.
    /// </summary>
    public static class HomeMirrorPlayCheck
    {
        internal static StringBuilder log = new StringBuilder();

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            var scenes = EditorBuildSettings.scenes;
            int ti = TrackCatalog.TownSceneIndex;
            string key = System.Environment.GetEnvironmentVariable("PSX_HOME_CAR");
            if (string.IsNullOrEmpty(key)) key = "flatsix_coupe_87";
            LifeSimManager.StartNewGame("Home Mirror", 24, LifeRules.DefaultJobIndex);
            var S = LifeSimManager.State;
            S.money = 50000; S.health = 100f; S.debugMode = true;
            S.cars.Clear(); S.activeCar = null;
            var spec = ParkedPlayCheck.SpecFor(key) ?? CarCatalog.All[0];
            var owned = CarMarket.MakeOwnedCar(S, spec, 90, 3000f, spec.price);
            owned.fuel = 90f;
            S.activeCar = owned.id;
            LifeSimManager.Save();
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = S.ActiveCar.specId;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.ForSlot(S.slotIndex, S.day);
            RaceHandoff.StartFuelPct = S.ActiveCar.fuel;
            LifeHomeScreen.FillCarRequestFor(S);
            LifeSimManager.Save();
            log.AppendLine($"[HomeMirror] car {key} spec {spec.id}");
            EditorSceneManager.OpenScene(scenes[ti].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("HomeMirrorRunner").AddComponent<HomeMirrorRunner>();
        }

        internal static void Finish()
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_home_mirror.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class HomeMirrorRunner : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static StringBuilder Log => HomeMirrorPlayCheck.log;
        static string OutDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "HomeMirror");
        float born;

        void Awake() { born = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }

        void Update()
        {
            if (Time.realtimeSinceStartup - born > 900f) { Log.AppendLine("HUNG"); HomeMirrorPlayCheck.Finish(); EditorApplication.Exit(1); }
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(OutDir);
            for (int i = 0; i < 30; i++) yield return null;
            yield return new WaitForSeconds(3f);
            var fm = ForecourtMode.Instance;
            var car = fm != null ? fm.playerCar : null;
            Log.AppendLine($"scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, forecourt {(fm != null)}, car {(car != null ? car.name : "none")}, on foot {ForecourtMode.OnFoot}");
            if (car == null) { Done(); yield break; }
            if (ForecourtMode.OnFoot)
            {
                fm.StartCoroutine((IEnumerator)typeof(ForecourtMode).GetMethod("GetIn", Any).Invoke(fm, null));
                float t0 = Time.time;
                while (ForecourtMode.OnFoot && Time.time - t0 < 10f) yield return null;
            }
            Log.AppendLine($"in the car: {!ForecourtMode.OnFoot}; car at {car.transform.position}");
            ChaseCamera.SetView(ChaseCamera.View.Cockpit);
            yield return new WaitForSeconds(3f);
            var ci = car.GetComponentInChildren<CockpitInterior>(true);
            Log.AppendLine($"cockpit {(ci != null ? "yes shown " + ci.Shown : "none")}, view {ChaseCamera.Current}");
            ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, "driver.png"));
            yield return null; yield return null;
            Camera mc = null;
            if (ci != null) foreach (var c in ci.GetComponentsInChildren<Camera>(true)) if (c.name == "CockpitMirrorCamera") mc = c;
            if (ci != null && ci.MirrorTexture != null) Save(ci.MirrorTexture, Path.Combine(OutDir, "rearview.png"));
            if (mc == null) { Log.AppendLine("no rear-view camera"); Done(); yield break; }
            Log.AppendLine($"rear-view camera at car-local {car.transform.InverseTransformPoint(mc.transform.position)}, near {mc.nearClipPlane}, mask {mc.cullingMask}");
            // what it sees, as it renders: the cockpit's cut for it
            ci.HideShell(true, true);
            var planes = GeometryUtility.CalculateFrustumPlanes(mc);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
                if ((mc.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                if ((r.bounds.ClosestPoint(mc.transform.position) - mc.transform.position).magnitude > 12f) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                bool mine = r.transform.IsChildOf(car.transform);
                var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb);
                float own = mpb.GetFloat("_PSXCabinOwn");
                string sh = r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "-";
                Log.AppendLine($"  {(mine ? "OWN " : "    ")}{PathOf(r.transform),-70} layer {r.gameObject.layer} shader {sh} cut-mark {own:0} dist {(r.bounds.center - mc.transform.position).magnitude:0.0} m");
            }
            ci.HideShell(false);
            Done();
        }

        static string PathOf(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }

        static void Save(RenderTexture rt, string path)
        {
            var keep = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); tex.Apply(false);
            RenderTexture.active = keep;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        void Done()
        {
            HomeMirrorPlayCheck.Finish();
            EditorApplication.Exit(0);
        }
    }
}
