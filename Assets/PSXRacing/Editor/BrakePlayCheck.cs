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
    /// The brakes in the RUNNING game (tools\brake-play-check.ps1), on the flat
    /// strip the top-speed check builds at y = 4000.
    ///
    /// The owner, 2026-09-25: "braking seems to over engage, leaving skid marks
    /// and locking steering. Sometimes braking properly. And sometimes braking
    /// does nothing to slow a car down." Three measurements, on three cars:
    ///   STOP - full brake from 100 km/h in a straight line: the distance, the
    ///          mean deceleration, and the worst scrub the tyres lay (a skid
    ///          mark starts at SkidMarks' 1.1 m/s).
    ///   TURN - full brake AND full lock from 80 km/h for one second: how far
    ///          the car turns. Nothing is "locked" (the ABS holds every wheel),
    ///          but a front axle spending 92% of its grip on stopping kept 39%
    ///          for steering, and that read as locked steering.
    ///   SLIDE - the car skating sideways at 12 m/s with the brake held: it
    ///          must never select REVERSE (it did: forwardSpeed of a sideways
    ///          car is ~0, and 0.4 s of brake at "zero" was reverse - after
    ///          which the brake pedal drove it backwards).
    /// </summary>
    public static class BrakePlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Brakes (play mode)")]
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

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("BrakePlayCheckRunner").AddComponent<BrakePlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE BRAKES STOP, STEER AND NEVER REVERSE A MOVING CAR." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_brake_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class BrakePlayCheckRunner : MonoBehaviour
    {
        CarController car;
        bool drive;
        float throttle, brake, steer;

        void FixedUpdate()
        {
            if (!drive || car == null) return;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.steerInput = steer;
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
            BrakePlayCheck.Check(car != null && applier != null, "there is a player car and the scene's applier");
            if (car == null || applier == null) { Done(); yield break; }

            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;

            var strip = new GameObject("BrakeStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            var box = strip.AddComponent<BoxCollider>();
            box.size = new Vector3(2000f, 2f, 30000f);
            float surfaceY = 4000f + 1f;

            BrakePlayCheck.Note("front brake share (ABS) " + CarController.FrontBrakeLockShare.ToString("0.00") +
                                " -> steering kept at the limit " +
                                (Mathf.Sqrt(1f - CarController.FrontBrakeLockShare * CarController.FrontBrakeLockShare) * 100f).ToString("0") + "%");

            // Three cars: the owner's Civic (light FF hatch), the RX-7 FD (the
            // reference car), and the heaviest road car.
            var picks = new List<CarSpec>();
            CarSpec Pick(System.Predicate<CarSpec> p, System.Comparison<CarSpec> order)
            {
                var list = new List<CarSpec>();
                foreach (var c in CarCatalog.All)
                    if (CarModelLibrary.LoadFor(c) != null && p(c)) list.Add(c);
                list.Sort(order);
                return list.Count > 0 ? list[0] : null;
            }
            var civic = Pick(c => c.name.Contains("CIVIC SiR-II (EG)"), (a, b) => 0);
            var rx7 = Pick(c => c.name.Contains("RX-7 Type RS"), (a, b) => 0);
            var heavy = Pick(c => !c.IsRaceCar, (a, b) => b.kg.CompareTo(a.kg));
            foreach (var c in new[] { civic, rx7, heavy }) if (c != null && !picks.Contains(c)) picks.Add(c);

            float x = -600f;
            foreach (var spec in picks)
            {
                RaceHandoff.CarSpecId = spec.id;
                RaceHandoff.CarPaintSkin = null;
                RaceHandoff.UpPower = RaceHandoff.UpWeight = RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = RaceHandoff.UpTires = 0;
                RaceHandoff.Supercharged = false;
                RaceHandoff.Welded = false;
                RaceHandoff.Setup = null;
                RaceHandoff.StartFuelPct = 100f;
                applier.SwapCar();
                string who = spec.name;
                x += 300f;

                // ---- STOP from 100 km/h --------------------------------------
                car.TeleportTo(new Vector3(x, surfaceY + 0.6f, -14000f), Quaternion.identity);
                for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
                car.SetRolling(100f / 3.6f);
                throttle = 0f; brake = 1f; steer = 0f; drive = true;
                Vector3 p0 = car.transform.position;
                float t = 0f, worstSlide = 0f, step = Time.fixedDeltaTime;
                while (t < 12f && car.Body.linearVelocity.magnitude > 0.3f)
                {
                    yield return new WaitForFixedUpdate();
                    t += step;
                    for (int w = 0; w < 4; w++) worstSlide = Mathf.Max(worstSlide, car.wheelContacts[w].slide);
                }
                float dist = Vector3.Distance(p0, car.transform.position);
                float decelG = (100f / 3.6f) / Mathf.Max(0.01f, t) / 9.81f;
                BrakePlayCheck.Check(decelG > 0.7f, who + ": STOP 100-0 km/h",
                    dist.ToString("0.0") + " m in " + t.ToString("0.00") + " s = " + decelG.ToString("0.00") + " g");
                BrakePlayCheck.Check(worstSlide < 2.0f, who + ": and the tyres only scrub (no black stripes)",
                    "worst scrub " + worstSlide.ToString("0.00") + " m/s; marks start at 1.1, full at 5.0");
                BrakePlayCheck.Check(car.currentGear >= 1, who + ": and it is still in a forward gear when it stops",
                    "gear " + car.currentGear);

                // ---- TURN under full brake from 80 km/h -----------------------
                drive = false;
                car.TeleportTo(new Vector3(x, surfaceY + 0.6f, -12000f), Quaternion.identity);
                for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
                car.SetRolling(80f / 3.6f);
                float h0 = car.transform.eulerAngles.y;
                throttle = 0f; brake = 1f; steer = 1f; drive = true;
                t = 0f;
                float latSum = 0f; int n = 0;
                while (t < 1.0f)
                {
                    yield return new WaitForFixedUpdate();
                    t += step;
                    latSum += Mathf.Abs(Vector3.Dot(car.Body.linearVelocity, car.transform.right)) > 0f
                        ? Mathf.Abs(car.Body.angularVelocity.y * Vector3.Dot(car.Body.linearVelocity, car.transform.forward)) : 0f;
                    n++;
                }
                float turned = Mathf.Abs(Mathf.DeltaAngle(h0, car.transform.eulerAngles.y));
                float latG = latSum / Mathf.Max(1, n) / 9.81f;
                BrakePlayCheck.Check(turned > 8f, who + ": TURN full brake + full lock at 80 km/h still steers",
                    "turned " + turned.ToString("0.0") + " deg in 1 s, ~" + latG.ToString("0.00") + " g lateral");

                // ---- SLIDE sideways with the brake held -----------------------
                drive = false;
                car.TeleportTo(new Vector3(x, surfaceY + 0.6f, -10000f), Quaternion.identity);
                for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
                throttle = 0f; brake = 1f; steer = 0f; drive = true;
                car.Body.linearVelocity = car.transform.right * 12f;
                // Holding the brake at a STANDSTILL is how the game selects
                // reverse, so reverse after the slide has stopped is right. What
                // must not happen is reverse while the car is still moving.
                float engagedAt = -1f, lastSpeed = car.Body.linearVelocity.magnitude;
                int lastGear = car.currentGear;
                float stoppedAfter = -1f;
                t = 0f;
                while (t < 2.5f)
                {
                    yield return new WaitForFixedUpdate();
                    t += step;
                    if (lastGear != -1 && car.currentGear == -1 && engagedAt < 0f) engagedAt = lastSpeed;
                    if (stoppedAfter < 0f && car.Body.linearVelocity.magnitude < 0.6f) stoppedAfter = t;
                    lastGear = car.currentGear;
                    lastSpeed = car.Body.linearVelocity.magnitude;
                }
                BrakePlayCheck.Check(engagedAt < 0.7f, who + ": SLIDE sideways at 12 m/s, brake held - no reverse until it has stopped",
                    (engagedAt < 0f ? "never selected" : "reverse selected at " + engagedAt.ToString("0.00") + " m/s") +
                    (stoppedAfter >= 0f ? "; slid to a stop in " + stoppedAfter.ToString("0.00") + " s" : "; still sliding after 2.5 s"));
                drive = false;
            }
            Done();
        }

        void Done()
        {
            drive = false;
            RaceHandoff.ClearAll();
            BrakePlayCheck.Finish();
            EditorApplication.Exit(BrakePlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
