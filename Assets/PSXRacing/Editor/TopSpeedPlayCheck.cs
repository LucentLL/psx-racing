using System.Collections;
using System.Collections.Generic;
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
    /// TOP SPEED, MEASURED — flat out in the running game (2026-09-21).
    ///
    /// The owner: "Something is very wrong with top speeds. One car has over
    /// 300MPH. All stock max speeds are listed in original GT4 specs. Upgrades
    /// should increase that by a %." The self-test checks the rule on the
    /// analytical force balance (CarController.ReachableTopSpeedMps). This
    /// checks the CAR: every tick of the real physics — tyres, gearbox, auto
    /// shifts, the limiter, downforce loading the springs — with the throttle
    /// held down on a flat strip of road long enough to stop gaining, and it
    /// asks where the needle stops.
    ///
    /// The strip is built in play mode far above the circuit (a road-layer
    /// box, so the tyres read tarmac), and the things that would interfere
    /// with a straight-line run are stood down: the driver's own input (the
    /// runner holds the pedal), the stuck-car recovery (a car 5 km from the
    /// racing line looks lost to it), the cooling model (a minute flat out is a
    /// real temperature test, which is not this one) and the tank.
    ///
    ///   tools\topspeed-play-check.ps1 -> PSXRacing_topspeed_play_check.txt
    /// </summary>
    public static class TopSpeedPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Top Speeds (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(0);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + TrackCatalog.At(0).id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = 0;
            RaceHandoff.CalendarDay = 0;               // clear: full grip, no weather
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.Solo = true;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TopSpeedPlayCheckRunner").AddComponent<TopSpeedPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "EVERY CAR TOPS OUT WHERE ITS BUILD SAYS." : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_topspeed_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class TopSpeedPlayCheckRunner : MonoBehaviour
    {
        CarController car;
        bool hold;

        struct Run
        {
            public CarSpec spec;
            public int power;
            public bool blower;
            public string why;
        }

        void FixedUpdate()
        {
            if (!hold || car == null) return;
            car.throttleInput = 1f;
            car.brakeInput = 0f;
            car.steerInput = 0f;
            // And the PARKING brake: PlayerCarInput holds it on while the car
            // stands still, so the one it last wrote before being stood down
            // (on the grid) was "on" — and the first run of this check spent
            // six seconds dragging a locked rear axle down from 85%.
            car.handbrakeInput = false;
            car.heatAccelMult = 1f;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            // Frames, never WaitForEndOfFrame: batch mode has no game view.
            for (int i = 0; i < 25; i++) yield return null;

            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            var applier = DebugCarOps.LiveApplier();
            TopSpeedPlayCheck.Check(car != null && applier != null, "there is a player car and the scene's applier");
            if (car == null || applier == null) { Done(); yield break; }

            // Everything that would fight a straight-line run, stood down.
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;

            // The strip: flat tarmac, 30 km long and 2 km wide, far above the
            // circuit so nothing of the venue is under it.
            var strip = new GameObject("TopSpeedStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            var box = strip.AddComponent<BoxCollider>();
            box.size = new Vector3(2000f, 2f, 30000f);
            float surfaceY = 4000f + 1f;

            // Who runs. The owner's own car first; then the ends of the field.
            var runs = new List<Run>();
            CarSpec Pick(System.Predicate<CarSpec> p, System.Comparison<CarSpec> order)
            {
                var list = new List<CarSpec>();
                foreach (var c in CarCatalog.All)
                    if (CarModelLibrary.LoadFor(c) != null && p(c)) list.Add(c);
                list.Sort(order);
                return list.Count > 0 ? list[0] : null;
            }
            var ruf = Pick(c => c.name.Contains("Yellow Bird"), (a, b) => 0);
            var fastestRoad = Pick(c => !c.IsRaceCar, (a, b) => b.topSpeedMps.CompareTo(a.topSpeedMps));
            var fastestNA = Pick(c => !c.IsRaceCar && !c.IsForcedInduction, (a, b) => b.topSpeedMps.CompareTo(a.topSpeedMps));
            var slowest = Pick(c => !c.IsRaceCar, (a, b) => a.topSpeedMps.CompareTo(b.topSpeedMps));
            var fastestRace = Pick(c => c.IsRaceCar, (a, b) => b.topSpeedMps.CompareTo(a.topSpeedMps));
            var rx7 = Pick(c => c.name.Contains("RX-7 Type RS"), (a, b) => 0);
            if (ruf != null)
            {
                runs.Add(new Run { spec = ruf, power = 0, why = "stock" });
                runs.Add(new Run { spec = ruf, power = CarTune.MaxStage, why = "fully built (the owner's car)" });
            }
            if (rx7 != null) runs.Add(new Run { spec = rx7, power = 2, why = "stage 2" });
            if (slowest != null) runs.Add(new Run { spec = slowest, power = 0, why = "slowest road car, stock" });
            if (fastestNA != null)
                runs.Add(new Run { spec = fastestNA, power = CarTune.MaxStage, blower = true,
                                   why = "fastest NA road car, stage 4 + blower" });
            if (fastestRoad != null && fastestRoad != fastestNA)
                runs.Add(new Run { spec = fastestRoad, power = CarTune.MaxStage, why = "fastest road car, stage 4" });
            if (fastestRace != null) runs.Add(new Run { spec = fastestRace, power = 0, why = "fastest race car" });

            float fastestSeen = 0f; string fastestWho = null;
            foreach (var run in runs)
            {
                RaceHandoff.CarSpecId = run.spec.id;
                RaceHandoff.CarPaintSkin = null;
                RaceHandoff.UpPower = run.power;
                RaceHandoff.UpWeight = RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = RaceHandoff.UpTires = 0;
                RaceHandoff.Supercharged = run.blower;
                RaceHandoff.Welded = false;
                RaceHandoff.Setup = null;
                RaceHandoff.StartFuelPct = 100f;
                applier.SwapCar();
                float target = car.BuildTopSpeedMps;
                string who = run.spec.name + " (" + run.why + ")";

                // Onto the strip, rolling at 85% of the target — the run is
                // about where the needle STOPS, not how long 0-60 takes.
                car.TeleportTo(new Vector3(0f, surfaceY + 0.6f, -14500f), Quaternion.identity);
                for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
                car.SetRolling(target * 0.85f);
                hold = true;

                // Flat out until the speed stops rising: under 0.05 m/s gained
                // in each of TWO consecutive 2 s windows (one window ends the run
                // on the dip of a gear change — it did, the first time), or
                // 150 s, or the end of the strip.
                float peak = 0f, lastCheck = 0f, lastSpeed = 0f, t = 0f;
                int flatWindows = 0;
                float step = Time.fixedDeltaTime;
                while (t < 150f)
                {
                    yield return new WaitForFixedUpdate();
                    t += step;
                    float v = car.Body.linearVelocity.magnitude;
                    peak = Mathf.Max(peak, v);
                    if (t - lastCheck >= 2f)
                    {
                        flatWindows = v - lastSpeed < 0.05f ? flatWindows + 1 : 0;
                        if (flatWindows >= 2 && t > 8f) break;
                        lastSpeed = v;
                        lastCheck = t;
                    }
                    if (car.transform.position.z > 14000f) break;
                }
                // WHERE THE FORCES STAND at the plateau: the engine's pull in the
                // gear it is in, the aero and rolling the drag solve knows
                // about, and what the body actually felt over the last second.
                // Whatever is left is a resistance the solve does not model.
                float v0 = car.Body.linearVelocity.magnitude;
                for (int f = 0; f < 50; f++) yield return new WaitForFixedUpdate();
                float v1 = car.Body.linearVelocity.magnitude;
                float felt = car.massKg * (v1 - v0) / (50f * step);
                int gear = car.currentGear;
                float ratio = gear >= 1 ? car.gearRatios[Mathf.Clamp(gear, 1, car.gearRatios.Length) - 1] : 0f;
                float pull = car.GetTorqueAtRPM(car.currentRPM) * ratio * car.finalDrive *
                             car.drivetrainEfficiency / car.wheelRadius;
                float aero = car.dragCoefficient * v1 * v1;
                float residual = pull - aero - car.rollingResistance - felt;
                TopSpeedPlayCheck.Note(who + ": gear " + gear + "/" + car.gearRatios.Length +
                    " at " + Mathf.RoundToInt(car.currentRPM) + " rpm (power peak " +
                    Mathf.RoundToInt(car.topSpeedAnchorRPM) + ", upshift " + Mathf.RoundToInt(car.upshiftRPM) +
                    ", redline " + Mathf.RoundToInt(car.redlineRPM) + ")  ·  pull " + Mathf.RoundToInt(pull) +
                    " N, aero " + Mathf.RoundToInt(aero) + " N, rolling " + Mathf.RoundToInt(car.rollingResistance) +
                    " N, felt " + Mathf.RoundToInt(felt) + " N  ->  unaccounted " + Mathf.RoundToInt(residual) +
                    " N  ·  pitch " + (Mathf.Asin(Mathf.Clamp(car.transform.forward.y, -1f, 1f)) *
                                        Mathf.Rad2Deg).ToString("+0.00;-0.00") + " deg" +
                    "  ·  spin " + car.wheelSpin.ToString("0.00") + ", limiter " + car.RevLimiterActive +
                    ", grounded " + car.anyWheelGrounded + ", on road " + car.onRoad +
                    ", heat x" + car.heatAccelMult.ToString("0.00") + ", fault x" + car.faultAccelMult.ToString("0.00"));
                hold = false;
                float settled = car.Body.linearVelocity.magnitude;
                float rel = peak / target;
                TopSpeedPlayCheck.Check(rel > 0.97f && rel < 1.03f,
                    who + ": tops out at its build's top speed",
                    Mph(peak) + " mph against " + Mph(target) + " mph (stock " + Mph(run.spec.topSpeedMps) +
                    ", +" + Mathf.RoundToInt((target / run.spec.topSpeedMps - 1f) * 100f) + "%), " +
                    t.ToString("0") + " s flat out, settled at " + Mph(settled));
                if (peak > fastestSeen) { fastestSeen = peak; fastestWho = who; }

                car.brakeInput = 1f;
                for (int f = 0; f < 5; f++) yield return new WaitForFixedUpdate();
            }

            TopSpeedPlayCheck.Check(fastestSeen > 1f && fastestSeen * 3.6f / 1.609344f < 260f,
                "nothing in the game gets anywhere near 300 mph",
                "fastest measured " + Mph(fastestSeen) + " mph, " + fastestWho);
            Done();
        }

        static string Mph(float mps) => (mps * 3.6f / 1.609344f).ToString("0");

        void Done()
        {
            hold = false;
            RaceHandoff.ClearAll();
            TopSpeedPlayCheck.Finish();
            EditorApplication.Exit(TopSpeedPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
