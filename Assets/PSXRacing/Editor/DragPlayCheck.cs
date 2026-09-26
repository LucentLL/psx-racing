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
    /// Standing quarter miles in the RUNNING game (tools\drag-play-check.ps1),
    /// on the flat strip the top-speed check builds at y = 4000.
    ///
    /// The owner, 2026-09-26: "I upgraded 95 Civic to fully built engine 323 HP.
    /// And got a 14.1s quarter mile. Is that a realistic improvement from
    /// stock?" - against a stock SiR-II's real 15.4-15.6 s. This runs the car
    /// stock, as a full NA build and as a full turbo build, from a standstill
    /// with the throttle pinned and the auto box, and reports ET, trap speed,
    /// 0-60 and where the time went (wheelspin, boost, the body drag the top-
    /// speed rule solved for).
    /// </summary>
    public static class DragPlayCheck
    {
        internal static StringBuilder log;

        [MenuItem("PSX Racing/Check Quarter Miles (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(0);
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = 0;
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.Solo = true;
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
            new GameObject("DragPlayCheckRunner").AddComponent<DragPlayCheckRunner>();
        }

        internal static void Finish()
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_drag_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class DragPlayCheckRunner : MonoBehaviour
    {
        CarController car;
        bool hold;

        struct Run { public CarSpec spec; public int power, weight, tires; public bool turbo; public string why; }

        void FixedUpdate()
        {
            if (!hold || car == null) return;
            car.throttleInput = 1f;
            car.brakeInput = 0f;
            car.steerInput = 0f;
            car.handbrakeInput = false;
            car.heatAccelMult = 1f;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 25; i++) yield return null;
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            var applier = DebugCarOps.LiveApplier();
            if (car == null || applier == null) { DragPlayCheck.log.AppendLine("no car"); Done(); yield break; }
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;

            var strip = new GameObject("DragStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            var box = strip.AddComponent<BoxCollider>();
            box.size = new Vector3(2000f, 2f, 30000f);
            float surfaceY = 4000f + 1f;

            CarSpec civic = null;
            foreach (var c in CarCatalog.All) if (c.name.Contains("CIVIC SiR-II (EG) `95")) civic = c;
            if (civic == null) { DragPlayCheck.log.AppendLine("no '95 Civic SiR-II"); Done(); yield break; }
            DragPlayCheck.log.AppendLine("quarter miles, " + civic.name + " (" + civic.hp + " hp, " +
                civic.peakTorqueNm + " Nm, " + civic.kg + " kg, stock top " +
                (civic.topSpeedMps * 2.23694f).ToString("0") + " mph):");
            var runs = new List<Run>
            {
                new Run { spec = civic, why = "stock" },
                new Run { spec = civic, power = 4, why = "full NA build" },
                new Run { spec = civic, power = 4, turbo = true, why = "full TURBO build (the owner's)" },
                new Run { spec = civic, power = 4, turbo = true, weight = 4, tires = 4, why = "full turbo + weight + tyres" },
            };

            float x = -600f;
            foreach (var run in runs)
            {
                RaceHandoff.CarSpecId = run.spec.id;
                RaceHandoff.CarPaintSkin = null;
                RaceHandoff.UpPower = run.power;
                RaceHandoff.UpWeight = run.weight;
                RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = 0;
                RaceHandoff.UpTires = run.tires;
                RaceHandoff.Supercharged = false;
                RaceHandoff.TurboKit = run.turbo;
                RaceHandoff.Welded = false;
                RaceHandoff.Setup = null;
                RaceHandoff.StartFuelPct = 100f;
                applier.SwapCar();
                x += 300f;

                // Stock body drag, for comparison: the same car unbuilt.
                // ResetTo, not TeleportTo: it puts the box back in FIRST. The
                // first version of this check left each run in the gear the
                // last one finished in, and a build started in 4th and spent
                // 2.5 s shifting down - a start no race ever makes.
                car.ResetTo(new Vector3(x, surfaceY + 0.6f, -14000f), Quaternion.identity);
                for (int f = 0; f < 30; f++) yield return new WaitForFixedUpdate();
                car.currentGear = 1;
                Vector3 p0 = car.transform.position;
                hold = true;
                float t = 0f, step = Time.fixedDeltaTime, t60 = -1f, spinSum = 0f, boostAt60 = -1f;
                int n = 0;
                float dist = 0f, v = 0f;
                var trace = new StringBuilder();
                float nextTrace = 0.25f, vPrev = 0f;
                trace.Append("      gears " + string.Join("/", System.Array.ConvertAll(car.gearRatios, r => r.ToString("0.00"))) +
                             " x fd " + car.finalDrive.ToString("0.00") + ", r " + car.wheelRadius.ToString("0.000") +
                             ", upshift " + Mathf.RoundToInt(car.upshiftRPM) + ", redline " + Mathf.RoundToInt(car.redlineRPM) + "\n");
                while (t < 40f)
                {
                    yield return new WaitForFixedUpdate();
                    t += step;
                    v = car.Body.linearVelocity.magnitude;
                    if (t >= nextTrace && t <= 8.01f)
                    {
                        // What the engine offers at the wheels in this gear (full
                        // boost x the turbo's live share), against what the body
                        // actually did, and what aero and rolling took.
                        int g = Mathf.Max(1, car.currentGear);
                        float ratio = car.gearRatios[Mathf.Clamp(g, 1, car.gearRatios.Length) - 1];
                        float tMult = car.HasTurbo ? car.turboOffBoost + (1f - car.turboOffBoost) * car.Boost : 1f;
                        float offer = car.GetTorqueAtRPM(car.currentRPM) * tMult * ratio * car.finalDrive *
                                      car.drivetrainEfficiency / car.wheelRadius;
                        float felt = car.massKg * (v - vPrev) / 0.25f;
                        float aero = car.dragCoefficient * v * v;
                        trace.Append("      t " + nextTrace.ToString("0.00") + "  " + (v * 2.23694f).ToString("0").PadLeft(3) +
                                     " mph  g" + car.currentGear + " " + Mathf.RoundToInt(car.currentRPM).ToString().PadLeft(5) +
                                     " rpm  offer " + Mathf.RoundToInt(offer).ToString().PadLeft(5) + " N  felt " +
                                     Mathf.RoundToInt(felt).ToString().PadLeft(5) + " N  aero " + Mathf.RoundToInt(aero).ToString().PadLeft(4) +
                                     "  spin " + car.wheelSpin.ToString("0.00") + (car.HasTurbo ? "  boost " + car.Boost.ToString("0.00") : "") + "\n");
                        vPrev = v;
                        nextTrace += 0.25f;
                    }
                    dist = Vector3.Distance(p0, car.transform.position);
                    if (t60 < 0f && v >= 26.8224f) { t60 = t; boostAt60 = car.Boost; }
                    if (t < 3f) { spinSum += car.wheelSpin; n++; }
                    if (dist >= 402.336f) break;
                }
                hold = false;
                int hp = run.spec.HpAtStage(run.power, run.turbo);
                DragPlayCheck.log.AppendLine("  " + run.why.PadRight(32) + hp + " hp, " +
                    Mathf.RoundToInt(car.massKg) + " kg:  ET " + t.ToString("0.00") + " s @ " +
                    (v * 2.23694f).ToString("0") + " mph   0-60 " + t60.ToString("0.00") + " s" +
                    "   ·  build top " + (car.BuildTopSpeedMps * 2.23694f).ToString("0") + " mph, body drag " +
                    car.dragCoefficient.ToString("0.000") + ", launch spin " + (spinSum / Mathf.Max(1, n)).ToString("0.00") +
                    (car.HasTurbo ? ", off-boost " + car.turboOffBoost.ToString("0.00") + ", boost at 60 " + boostAt60.ToString("0.00") : "") +
                    ", gears " + car.gearRatios.Length);
                if (run.power == 0 || run.weight == 0) DragPlayCheck.log.Append(trace.ToString());
                car.brakeInput = 1f;
                for (int f = 0; f < 5; f++) yield return new WaitForFixedUpdate();
            }
            Done();
        }

        void Done()
        {
            hold = false;
            RaceHandoff.ClearAll();
            DragPlayCheck.Finish();
            EditorApplication.Exit(0);
        }
    }
}
