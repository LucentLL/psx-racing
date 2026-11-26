using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LAB CHECK IN THE EDITOR (labswitches, 2026-11-23): the same check as the perf tour's &amp;lab=1
    /// (Scripts/Dev/LabCheck.cs), as a play check - for a sandbox while the owner's game runs (a play check runs
    /// hidden; a perf tour may not run at all then).
    ///   PSX_LAB_PLACE = city (Charlotte free roam, the default) | a stage id (LakeLure, BlueRidge...: its start)
    ///                 | town (the meet lot on a meet night: the PEOPLE rows)
    ///   PSX_LAB_HOUR = Night (default), PSX_LAB_WEATHER = Rain (default) | Clear | Fog..., PSX_LAB_VIEW = Chase | Cockpit...
    ///   PSX_LAB_SNAPS=1: each row's flipped and returned frames; PSX_LAB_OUT: the folder (default Screenshots/lab_check)
    ///   Unity.exe -batchmode -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.LabPlayCheck.Run (no -quit)
    /// Writes &lt;out&gt;/labplay_&lt;place&gt;_lab.txt and PSXRacing_lab_play.txt.
    /// </summary>
    public static class LabPlayCheck
    {
        internal static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
        internal static string Place => Env("PSX_LAB_PLACE", "city");

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            if (Place.ToLowerInvariant() == "town") { RunTown(); return; }
            bool city = Place.ToLowerInvariant() == "city";
            int vi = TrackCatalog.IndexOf(city ? "Charlotte" : Place);
            var scenes = EditorBuildSettings.scenes;
            int s = vi >= 0 ? TrackCatalog.SceneIndex(vi) : -1;
            if (vi < 0 || s < 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path))
            {
                Debug.LogError("[LabPlayCheck] no built scene for " + Place);
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = city;
            RaceHandoff.TrackIndex = vi;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.StartFuelPct = 100f;
            string hour = Env("PSX_LAB_HOUR", "Night");
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Night;
            for (int k = 0; k < TimeOfDay.All.Length; k++)
                if (string.Equals(TimeOfDay.All[k].name, hour, System.StringComparison.OrdinalIgnoreCase)) RaceHandoff.TimeOfDayIndex = k;
            if (System.Enum.TryParse(Env("PSX_LAB_WEATHER", "Rain"), true, out Weather w)) RaceHandoff.WeatherOverride = (int)w;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>PSX_LAB_PLACE=town (people, for the PEOPLE rows' flip snaps): the town on a meet night, the
        /// meet lot full - a scratch career on its first Friday, the night block - driven in as LifeHomeScreen's
        /// StartShops does. Hour and weather as for the other places.</summary>
        static void RunTown()
        {
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.TownSceneIndex;
            if (s <= 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path))
            {
                Debug.LogError("[LabPlayCheck] no built Town scene");
                EditorApplication.Exit(1);
                return;
            }
            PSXRacing.LifeSim.LifeSimManager.UseScratchSave("labtown");
            var st = PSXRacing.LifeSim.LifeSimManager.State;
            if (!PSXRacing.LifeSim.LifeSimManager.HasSave || string.IsNullOrEmpty(st.playerName))
                PSXRacing.LifeSim.LifeSimManager.StartNewGame("LAB CHECK", 30, PSXRacing.LifeSim.LifeRules.DefaultJobIndex);
            st = PSXRacing.LifeSim.LifeSimManager.State;
            if (st.cars.Count == 0) PSXRacing.LifeSim.LifeRules.SeedFallbackCar(st);
            if (st.day < 1) st.day = 1;
            while (!PSXRacing.LifeSim.CarMeets.MeetOn(st.day)) st.day++;
            st.slotIndex = PSXRacing.LifeSim.LifeRules.NightSlot;
            PSXRacing.LifeSim.LifeSimManager.Save();
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CarId = st.activeCar;
            RaceHandoff.CarSpecId = st.ActiveCar != null ? st.ActiveCar.specId : "";
            RaceHandoff.StartFuelPct = 100f;
            string hour = Env("PSX_LAB_HOUR", "Night");
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Night;
            for (int k = 0; k < TimeOfDay.All.Length; k++)
                if (string.Equals(TimeOfDay.All[k].name, hour, System.StringComparison.OrdinalIgnoreCase)) RaceHandoff.TimeOfDayIndex = k;
            if (System.Enum.TryParse(Env("PSX_LAB_WEATHER", "Rain"), true, out Weather w)) RaceHandoff.WeatherOverride = (int)w;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("LabPlayCheckRunner").AddComponent<LabPlayCheckRunner>();
        }
    }

    public class LabPlayCheckRunner : MonoBehaviour
    {
        string report = "";
        float hardStop;

        void Update()
        {
            // a hard limit, whatever happens (no runner may hang a batch editor)
            if (hardStop > 0f && Time.realtimeSinceStartup > hardStop) { Write("TIMEOUT"); EditorApplication.Exit(1); }
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            hardStop = Time.realtimeSinceStartup + 600f;
            string place = LabPlayCheck.Place;
            string outDir = LabPlayCheck.Env("PSX_LAB_OUT", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots", "lab_check"));
            System.IO.Directory.CreateDirectory(outDir);
            CarController car = null;
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 180f;)
            {
                if (CityMode.Instance != null && CityMode.Instance.player != null && CityMode.Instance.Live) car = CityMode.Instance.player;
                else if (RaceManager.Instance != null && RaceManager.Instance.playerCar != null) car = RaceManager.Instance.playerCar;
                if (car != null && Camera.main != null) break;
                yield return null;
            }
            if (car == null || Camera.main == null) { Write("FAIL: no car / camera came up at " + place); EditorApplication.Exit(1); yield break; }
            if (System.Enum.TryParse(LabPlayCheck.Env("PSX_LAB_VIEW", ""), true, out ChaseCamera.View v)) ChaseCamera.PreviewView(v);
            // streaming and the lamp field settle; the car held still
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 20f;)
            {
                car.throttleInput = 0f; car.brakeInput = 1f;
                yield return null;
            }
            bool snaps = LabPlayCheck.Env("PSX_LAB_SNAPS", "0") == "1";
            yield return LabCheck.Run(place, "labplay", LabPlayCheck.Env("PSX_LAB_WEATHER", "Rain"), snaps,
                                      (n, b) => System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, n), b),
                                      s => { report += s + "\n"; Debug.Log("[LabPlayCheck] " + s); });
            Write(report);
            EditorApplication.Exit(report.Contains("fail 0") ? 0 : 1);
        }

        static void Write(string s)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "PSXRacing_lab_play.txt"), s);
        }
    }
}
