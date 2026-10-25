using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The rain splash, looked at (TireSpray, 2026-10-12): the first venue in
    /// RAIN at noon, the grid car driven by script - full throttle from rest,
    /// then a hard handbrake turn at speed, then a slide - and the chase view
    /// written as Screenshots\splash_&lt;n&gt;_&lt;what&gt;.png at fixed moments.
    /// PSX_SPLASH_HOUR=dusk|night for the lit spray.
    /// Headless (with graphics): -executeMethod PSXRacing.EditorTools.SplashPlayShots.Run
    /// </summary>
    public static class SplashPlayShots
    {
        public static void Run()
        {
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(TrackCatalog.CheckVenue);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { Debug.LogError("[Splash] venue not built"); EditorApplication.Exit(1); return; }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
            RaceHandoff.CalendarDay = 0;
            string hn = System.Environment.GetEnvironmentVariable("PSX_SPLASH_HOUR");
            int hour = string.IsNullOrEmpty(hn) ? TimeOfDay.Noon
                : Mathf.Max(0, System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hn, System.StringComparison.OrdinalIgnoreCase)));
            RaceHandoff.TimeOfDayIndex = hour;
            RaceHandoff.Solo = true;
            RaceHandoff.WeatherOverride = (int)Weather.Rain;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("SplashPlayShotsRunner").AddComponent<SplashPlayShotsRunner>();
        }
    }

    [DefaultExecutionOrder(10000)]
    public class SplashPlayShotsRunner : MonoBehaviour
    {
        CarController car;
        Camera view;
        float throttle, brake, steer; bool hand, drive;
        int shot;

        void FixedUpdate()
        {
            if (!drive || car == null) return;
            car.throttleInput = throttle; car.brakeInput = brake; car.steerInput = steer; car.handbrakeInput = hand;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 120 && car == null; i++)
            {
                var input = FindFirstObjectByType<PlayerCarInput>();
                if (input != null) { car = input.GetComponent<CarController>(); input.enabled = false; }
                yield return null;
            }
            var chase = FindFirstObjectByType<ChaseCamera>();
            view = chase != null ? chase.GetComponent<Camera>() : Camera.main;
            if (car == null || view == null) { Debug.LogError("[Splash] no player car / camera"); Quit(); yield break; }
            Debug.Log("[Splash] raining=" + WeatherFx.Raining + " wet=" + Seasons.RoadWetT.ToString("0.00"));
            // let any countdown pass
            yield return new WaitForSeconds(4f);
            drive = true;
            throttle = 1f;
            yield return new WaitForSeconds(1.2f); Shot("launch");
            yield return new WaitForSeconds(3.0f); Shot("speed");
            yield return new WaitForSeconds(1.5f); Shot("speed2");
            steer = 1f; hand = true; throttle = 0.6f;
            yield return new WaitForSeconds(0.45f); Shot("slide");
            yield return new WaitForSeconds(0.4f); Shot("slide2");
            hand = false; throttle = 1f; steer = 0.8f;
            yield return new WaitForSeconds(0.8f); Shot("powerslide");
            Debug.Log("[Splash] done, " + shot + " shots, speed " + car.speedKmh.ToString("0") + " km/h");
            Quit();
        }

        void Shot(string what)
        {
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(view, req)) RenderPipeline.SubmitRenderRequest(view, req);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"splash_{shot++}_{what}.png"), tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt);
            float sl = 0f; for (int w = 0; w < 4; w++) sl = Mathf.Max(sl, car.wheelContacts[w].slide);
            Debug.Log($"[Splash] shot {what}: {car.speedKmh:0} km/h, max slide {sl:0.0} m/s");
        }

        static void Quit()
        {
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
