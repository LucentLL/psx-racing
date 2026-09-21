using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE DEBUG BENCH'S WORLD AND CAR PAGES, IN THE RUNNING GAME (2026-09-21).
    ///
    /// The self-test drives the bench's rules on a bare CarController with no
    /// scene under it, which is the right place to pin arithmetic and the wrong
    /// place to find out what happens when a collider and four wheel radii are
    /// rewritten under a car doing 100 km/h. That only exists in play mode, so
    /// this enters it on a built circuit and does what the page does:
    ///
    ///   * A DIFFERENT CAR, AT SPEED, WITH THE CLOCK STOPPED. The bench is
    ///     opened from the pause menu, so the swap happens at timeScale 0 and
    ///     the physics meets the new car on the first step after RESUME. The
    ///     lightest car in the catalog, then the heaviest: each must BE that
    ///     car (shell, redline, mass), keep the speed it was carrying, and
    ///     still be on its wheels on the road two seconds later — not thrown,
    ///     not sunk, not stopped dead.
    ///   * THE HOUR. Night by the bench is the night a booked race gets: the
    ///     scene's globals say night and the sun's shadow map goes off; noon
    ///     brings both back.
    ///   * THE WEATHER. Rain falls and wets the road and costs grip; snow
    ///     REPLACES the rain (Ensure alone renamed the slab and it kept
    ///     raining) and puts the snow dress on; clear leaves nothing falling;
    ///     and handing the sky back is the calendar's own roll.
    ///
    ///   tools\bench-play-check.ps1 -> PSXRacing_bench_play_check.txt
    /// </summary>
    public static class BenchPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Debug Bench (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var def = TrackCatalog.At(0);
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(0);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + def.id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = 0;
            RaceHandoff.CalendarDay = 0;               // day 0 rolls CLEAR
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
            RaceHandoff.Solo = true;                   // nobody to be hit by while coasting
            var cars = CarCatalog.All;
            if (cars.Count > 2) RaceHandoff.CarSpecId = cars[cars.Count / 2].id;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("BenchPlayCheckRunner").AddComponent<BenchPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE BENCH CHANGES THE CAR AND THE SKY MID-DRIVE." : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bench_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class BenchPlayCheckRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            // Frames, never WaitForEndOfFrame: batch mode has no game view.
            for (int i = 0; i < 25; i++) yield return null;

            var rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            var applier = DebugCarOps.LiveApplier();
            BenchPlayCheck.Check(car != null, "there is a player car");
            BenchPlayCheck.Check(applier != null, "and the bench can find the scene's applier");
            if (car == null || applier == null) { Done(); yield break; }

            // ---- a different car, at speed --------------------------------
            // The SMALLEST and the LARGEST tyre in the pack, not the lightest and
            // heaviest car: the first run picked an Elan and an SL 600, which
            // wear the same shell, so the second swap changed no geometry at
            // all. The wheel radius is what moves the contact line and the
            // gearbox, so its two extremes are the two swaps worth making.
            CarSpec light = null, heavy = null;
            float rMin = float.MaxValue, rMax = 0f;
            foreach (var c in CarCatalog.All)
            {
                var d = CarModelLibrary.LoadFor(c);
                if (d == null || d.wheelRadius < 0.05f) continue;
                if (d.wheelRadius < rMin) { rMin = d.wheelRadius; light = c; }
                if (d.wheelRadius > rMax) { rMax = d.wheelRadius; heavy = c; }
            }
            BenchPlayCheck.Note("tyre radius " + rMin.ToString("0.000") + " m to " + rMax.ToString("0.000") + " m");
            var path = FindFirstObjectByType<TrackPath>();
            int at = 40;
            foreach (var spec in new[] { light, heavy })
            {
                if (spec == null) { BenchPlayCheck.Check(false, "a catalog car with a shell to swap to"); continue; }
                if (path != null && path.Count > at + 40)
                {
                    int i = path.Wrap(at);
                    Vector3 fwd = path.GetTangent(i).normalized;
                    car.TeleportTo(path.GetPoint(i) + Vector3.up * 0.4f, Quaternion.LookRotation(fwd, Vector3.up));
                    at += 60;
                }
                for (int f = 0; f < 40; f++) yield return null;      // settle on the springs
                car.SetRolling(28f);
                for (int f = 0; f < 20; f++) yield return null;
                yield return Swap(car, applier, spec);
            }

            // ---- the hour ---------------------------------------------------
            var globals = FindFirstObjectByType<PSXGlobals>();
            BenchPlayCheck.Check(globals != null, "the scene has its PSXGlobals");
            DebugWorldOps.SetHour(TimeOfDay.Night);
            for (int f = 0; f < 5; f++) yield return null;
            BenchPlayCheck.Check(TimeOfDay.Current == TimeOfDay.Night && globals != null && globals.night > 0.99f,
                                 "NIGHT from the bench is night in the scene", globals != null ? globals.night.ToString("0.00") : "-");
            BenchPlayCheck.Check(SunShadows.Strength == 0f, "the sun's shadow map goes off with it");
            BenchPlayCheck.Check(RaceHandoff.TimeOfDayIndex == TimeOfDay.Night, "and RESTART RACE would come back at night");

            // ---- the weather ------------------------------------------------
            DebugWorldOps.SetWeather((int)Weather.Rain);
            for (int f = 0; f < 5; f++) yield return null;
            BenchPlayCheck.Check(WeatherFx.Raining, "RAIN from the bench is falling");
            BenchPlayCheck.Check(globals != null && globals.wetness > 0.99f, "the road is soaked",
                                 globals != null ? globals.wetness.ToString("0.00") : "-");
            BenchPlayCheck.Check(Mathf.Approximately(Seasons.RoadGripMult, Seasons.GripMult(Weather.Rain, true)),
                                 "and the tyres are on a wet road", Seasons.RoadGripMult.ToString("0.00"));

            DebugWorldOps.SetWeather((int)Weather.Snow);
            for (int f = 0; f < 5; f++) yield return null;
            var fx = FindFirstObjectByType<WeatherFx>();
            BenchPlayCheck.Check(!WeatherFx.Raining && fx != null && fx.weather == Weather.Snow,
                                 "SNOW replaces the rain rather than renaming it");
            BenchPlayCheck.Check(FindObjectsByType<WeatherFx>(FindObjectsInactive.Exclude).Length == 1,
                                 "one slab over the camera, not two");
            var dress = FindFirstObjectByType<SeasonDress>();
            if (dress != null)
                BenchPlayCheck.Check(SeasonDress.AppliedDress == Seasons.DressSnow, "and the ground puts the snow dress on",
                                     Seasons.DressNames[Mathf.Clamp(SeasonDress.AppliedDress, 0, Seasons.DressCount - 1)]);
            else BenchPlayCheck.Note("this venue has no SeasonDress, so no snow dress to check");

            DebugWorldOps.SetWeather((int)Weather.Clear);
            for (int f = 0; f < 5; f++) yield return null;
            BenchPlayCheck.Check(FindFirstObjectByType<WeatherFx>() == null, "CLEAR leaves nothing falling");
            BenchPlayCheck.Check(globals != null && globals.wetness < 0.5f, "and the road is only a clear night's damp",
                                 globals != null ? globals.wetness.ToString("0.00") : "-");

            DebugWorldOps.SetWeather(-1);
            BenchPlayCheck.Check(Seasons.CurrentWeather == Seasons.WeatherFor(RaceHandoff.CalendarDay),
                                 "handing the sky back is the calendar's own roll");

            DebugWorldOps.SetHour(TimeOfDay.Noon);
            for (int f = 0; f < 8; f++) yield return null;
            BenchPlayCheck.Check(globals != null && globals.night < 0.01f && SunShadows.Strength > 0.9f,
                                 "NOON brings the sun and its shadows back",
                                 "night " + (globals != null ? globals.night.ToString("0.00") : "-") +
                                 ", shadows " + SunShadows.Strength.ToString("0.00"));
            Done();
        }

        /// <summary>The CAR page's press, as the pause menu makes it: clock
        /// stopped, request rewritten, SwapCar, clock started.</summary>
        IEnumerator Swap(CarController car, RaceHandoffApplier applier, CarSpec spec)
        {
            string who = spec.name + " (" + spec.kg + " kg)";
            float before = car.Body.linearVelocity.magnitude;
            float groundBefore = GroundGap(car);

            Time.timeScale = 0f;
            yield return null;
            RaceHandoff.CarSpecId = spec.id;
            bool swapped = applier.SwapCar();
            yield return null;
            yield return null;
            float carried = car.Body.linearVelocity.magnitude;
            Time.timeScale = 1f;

            BenchPlayCheck.Check(swapped, who + ": the applier builds it under the player");
            var shell = car.GetComponent<CarBody>();
            var def = CarModelLibrary.LoadFor(spec);
            BenchPlayCheck.Check(shell != null && def != null && shell.modelKey == def.key,
                                 who + ": it wears its own shell", shell != null ? shell.modelKey : "-");
            BenchPlayCheck.Check(Mathf.Approximately(car.redlineRPM, spec.redline) &&
                                 Mathf.Approximately(car.massKg, spec.kg) &&
                                 Mathf.Approximately(car.Body.mass, spec.kg),
                                 who + ": and has its own engine and its own weight",
                                 car.redlineRPM + " rpm, body " + car.Body.mass + " kg");
            BenchPlayCheck.Check(before > 15f && Mathf.Abs(carried - before) < 0.5f,
                                 who + ": the speed it was carrying is kept across the swap",
                                 before.ToString("0.0") + " -> " + carried.ToString("0.0") + " m/s");
            BenchPlayCheck.Check(car.currentGear >= 1 && car.currentGear <= car.gearRatios.Length &&
                                 car.KinematicRPM(carried, car.currentGear) <= car.revLimitRPM,
                                 who + ": in a gear of the NEW box that holds that speed",
                                 "gear " + car.currentGear);

            // Two seconds of road. Thrown, sunk or stopped dead all show here.
            float peakUp = 0f, worstTilt = 1f, y0 = car.transform.position.y;
            bool bad = false;
            for (int f = 0; f < 120; f++)
            {
                yield return null;
                var p = car.transform.position;
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) { bad = true; break; }
                peakUp = Mathf.Max(peakUp, car.Body.linearVelocity.y);
                worstTilt = Mathf.Min(worstTilt, car.transform.up.y);
            }
            float gap = GroundGap(car);
            BenchPlayCheck.Check(!bad, who + ": the body's pose stays a number");
            BenchPlayCheck.Check(peakUp < 2.5f, who + ": nothing threw it into the air",
                                 "peak " + peakUp.ToString("0.00") + " m/s upward");
            BenchPlayCheck.Check(worstTilt > 0.9f, who + ": it stayed on its wheels", worstTilt.ToString("0.00"));
            // Against where the car it REPLACED was standing, not against a
            // guess: this rig's origin rides at the contact plane (a settled
            // car reads about -0.03 m), so "some way above the road" was the
            // check's mistake, not the game's. Sunk into the road or left
            // floating over it both show as a gap that moved.
            BenchPlayCheck.Check(Mathf.Abs(gap - groundBefore) < 0.15f && gap < 0.5f,
                                 who + ": and it is standing on the road where the last car stood, not in it or over it",
                                 "origin " + gap.ToString("0.00") + " m up (was " + groundBefore.ToString("0.00") + ")");
            BenchPlayCheck.Check(car.ReachableTopSpeedMps > 10f && car.ReachableTopSpeedMps < 200f,
                                 who + ": the speedometer's scale is this car's",
                                 (car.ReachableTopSpeedMps * 3.6f).ToString("0") + " km/h against a sheet " +
                                 (car.topSpeedMps * 3.6f).ToString("0"));
        }

        /// <summary>Metres from the car's origin down to the first thing under
        /// it that is not the car.</summary>
        static float GroundGap(CarController car)
        {
            var from = car.transform.position + Vector3.up * 0.5f;
            float best = 99f;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == car.Body) continue;
                best = Mathf.Min(best, hit.distance - 0.5f);
            }
            return best;
        }

        void Done()
        {
            Time.timeScale = 1f;
            RaceHandoff.ClearAll();
            BenchPlayCheck.Finish();
            EditorApplication.Exit(BenchPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
