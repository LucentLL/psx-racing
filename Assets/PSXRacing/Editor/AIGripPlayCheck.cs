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
    /// THE AI'S GRIP, MEASURED (AI at the limit, 2026-10-07). On a flat road
    /// strip far above the venue, each car set up as an AI car is (ABS, the
    /// AI's countersteer assist, no brake-stab):
    ///   SKIDPAD  a steady circle at three radii, the speed ramped slowly
    ///            until the car can no longer hold the circle; the best
    ///            one-second lateral g held ON the circle is the limit.
    ///   STOP     a full ABS stop from 36 m/s and from 58 m/s.
    /// Each is printed against <see cref="AIGrip"/>'s prediction, and the
    /// ratios are what AIGrip's calibration constants are set from.
    /// PSX_AIGRIP_CARS="RX-7 Type RS;CIVIC SiR-II (EG)" picks the cars.
    /// </summary>
    public static class AIGripPlayCheck
    {
        internal static StringBuilder log;

        [MenuItem("PSX Racing/Check AI Grip (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(0);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                log.AppendLine("FAIL the venue is not built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = 0;
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.Solo = true;
            string sky = HandlingPlayCheck.ApplyEnvWeather();
            if (sky != null) log.AppendLine(sky);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("AIGripPlayCheckRunner").AddComponent<AIGripPlayCheckRunner>();
        }

        internal static void Finish()
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_aigrip_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class AIGripPlayCheckRunner : MonoBehaviour
    {
        CarController car;
        bool drive;
        float throttle, brake, steer;
        float surfaceY, xLane;

        // circle state
        bool circle;
        Vector3 centre;
        float radius, vTarget, ramp, radInt;

        void FixedUpdate()
        {
            if (!drive || car == null) return;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            car.heatAccelMult = 1f;
            if (circle)
            {
                float dt = Time.fixedDeltaTime;
                vTarget += ramp * dt;
                Vector3 p = car.transform.position - centre; p.y = 0f;
                float th = Mathf.Atan2(p.z, p.x);
                float v = Mathf.Max(car.forwardSpeed, 0f);
                float look = 4f + 0.35f * v;
                float tt = th + look / radius;
                Vector3 target = centre + new Vector3(Mathf.Cos(tt), 0f, Mathf.Sin(tt)) * radius;
                Vector3 local = car.transform.InverseTransformPoint(target);
                float ld = Mathf.Max(new Vector2(local.x, local.z).magnitude, 1f);
                float alpha = Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f));
                float wheel = Mathf.Atan(2f * car.wheelbase * Mathf.Sin(alpha) / ld) * Mathf.Rad2Deg;
                // Pure pursuit, plus the bend's own wheel angle fed forward and
                // the radial error integrated: the chase alone sits metres
                // outside a fast circle (it needs the error to steer), and the
                // window below wants the car ON it.
                float rNow = p.magnitude;
                radInt = Mathf.Clamp(radInt + (rNow - radius) * dt, -60f, 60f);
                float ffDeg = Mathf.Atan(car.wheelbase / radius) * Mathf.Rad2Deg;
                steer = Mathf.Clamp((1.15f * wheel - ffDeg * 0.5f - 1.5f * (rNow - radius) - 0.6f * radInt) / Mathf.Max(car.CurrentMaxSteerDeg, 1f), -1f, 1f);
                throttle = Mathf.Clamp01(0.25f + 0.6f * (vTarget - v));
                brake = v > vTarget + 1.5f ? Mathf.Clamp01((v - vTarget - 1.5f) * 0.3f) : 0f;
            }
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.steerInput = steer;
            car.handbrakeInput = false;
        }

        IEnumerator Place(Vector3 at, float yawDeg, float mps)
        {
            drive = false; circle = false; throttle = brake = steer = 0f;
            car.TeleportTo(at + Vector3.up * 0.6f, Quaternion.Euler(0f, yawDeg, 0f));
            for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
            car.SetRolling(mps);
        }

        static void Note(string s) => AIGripPlayCheck.log.AppendLine("  " + s);

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 25; i++) yield return null;
            Time.timeScale = 4f;
            Time.maximumDeltaTime = 0.4f;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 240;
            }
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            var applier = DebugCarOps.LiveApplier();
            if (car == null || applier == null) { Note("FAIL no player car / applier"); Done(); yield break; }

            var strip = new GameObject("AIGripStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            var box = strip.AddComponent<BoxCollider>();
            box.size = new Vector3(4000f, 2f, 30000f);
            surfaceY = 4000f + 1f;

            string want = System.Environment.GetEnvironmentVariable("PSX_AIGRIP_CARS");
            if (string.IsNullOrEmpty(want))
                want = "RX-7 Type RS;CIVIC SiR-II (EG);VIPER GTS;SKYLINE GT-R (R32);Acura NSX;LEVIN GT-APEX (AE86)";
            var picks = new List<CarSpec>();
            foreach (var n in want.Split(';'))
                foreach (var c in CarCatalog.All)
                    if (c.name.Contains(n.Trim()) && CarModelLibrary.LoadFor(c) != null) { if (!picks.Contains(c)) picks.Add(c); break; }

            var ratios = new List<float>();
            var bratios = new List<float>();
            bool trace = System.Environment.GetEnvironmentVariable("PSX_AIGRIP_TRACE") == "1";
            float zBand = -13000f;
            foreach (var spec in picks)
            {
                zBand += 3500f;
                xLane = -1900f;
                RaceHandoff.CarSpecId = spec.id;
                RaceHandoff.CarPaintSkin = null;
                RaceHandoff.UpPower = RaceHandoff.UpWeight = RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = RaceHandoff.UpTires = 0;
                RaceHandoff.Supercharged = false; RaceHandoff.Welded = false; RaceHandoff.Setup = null;
                RaceHandoff.StartFuelPct = 100f;
                applier.SwapCar();
                for (int f = 0; f < 5; f++) yield return null;
                car = rm.playerCar;
                var input = car.GetComponent<PlayerCarInput>();
                if (input != null) { input.inputEnabled = false; input.enabled = false; }
                foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
                var temp = car.GetComponent<EngineTemp>();
                if (temp != null) temp.enabled = false;
                car.manualMode = false;
                // Set up as the builder sets up an AI car.
                car.gripBonus = 1.04f;
                car.brakeStabDrift = 0f;
                car.countersteerAssist = 0.5f;
                car.allowReverse = false;
                car.hasAbs = true;

                Note($"{spec.name}: {car.massKg:0} kg, drive F{car.frontDriveShare:0.00}, wb {car.wheelbase:0.00} m, " +
                     $"roadGrip {car.roadGrip:0.00} tyres F{car.tireMuFront:0.00}/R{car.tireMuRear:0.00} gripBonus {car.gripBonus:0.00} " +
                     $"| nominal mu {AIGrip.NominalMu(car):0.000}, downforce coef {car.downforceCoefficient:0.000}, brake demand {car.brakeDemandG:0.00} g");

                var radii = new List<float> { 25f, 45f, 80f, 140f };
                string rEnv = System.Environment.GetEnvironmentVariable("PSX_AIGRIP_RADII");
                if (!string.IsNullOrEmpty(rEnv))
                {
                    radii.Clear();
                    foreach (var x in rEnv.Split(',')) if (float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rv)) radii.Add(rv);
                }
                foreach (float R in radii)
                {
                    xLane += R > 150f ? 700f : 300f;
                    centre = new Vector3(xLane, surfaceY, zBand);
                    float guess = Mathf.Sqrt(9.81f * AIGrip.LateralG(car, 20f) * R);
                    float v0 = Mathf.Max(Mathf.Min(0.6f * guess, 40f), 4f);
                    Vector3 lastVel = Vector3.zero; bool haveLast = false;
                    yield return Place(centre + new Vector3(R, 0f, 0f), 0f, v0);
                    circle = true; drive = true;
                    vTarget = v0; radius = R; radInt = 0f; ramp = R < 15f ? 0.12f : R < 40f ? 0.25f : 0.4f;
                    var winA = new Queue<float>();
                    float best = 0f, bestV = 0f, t = 0f, offFor = 0f;
                    bool winOk = true;
                    var winOkQ = new Queue<bool>();
                    string why = "time";
                    while (t < 110f)
                    {
                        yield return new WaitForFixedUpdate();
                        t += Time.fixedDeltaTime;
                        Vector3 p = car.transform.position - centre; p.y = 0f;
                        float r = p.magnitude;
                        Vector3 vel = car.Body.linearVelocity; vel.y = 0f;
                        // The lateral acceleration itself (the velocity turning),
                        // not v^2/r: a car spiralling in reads high on v^2/r.
                        float a = 0f;
                        if (haveLast && vel.sqrMagnitude > 1f)
                            a = Mathf.Abs(Vector3.Dot((vel - lastVel) / Time.fixedDeltaTime, Vector3.Cross(Vector3.up, vel.normalized)));
                        lastVel = vel; haveLast = true;
                        bool on = Mathf.Abs(r - R) < 2f && Mathf.Abs(car.chassisSlipAngle) < 0.25f;
                        winA.Enqueue(a); winOkQ.Enqueue(on);
                        int n = Mathf.RoundToInt(1f / Time.fixedDeltaTime);
                        while (winA.Count > n) { winA.Dequeue(); winOkQ.Dequeue(); }
                        if (winA.Count == n && t > 3f)
                        {
                            winOk = true;
                            foreach (var b in winOkQ) if (!b) { winOk = false; break; }
                            if (winOk)
                            {
                                float sum = 0f; foreach (var x in winA) sum += x;
                                float g = sum / n / 9.81f;
                                if (g > best) { best = g; bestV = vel.magnitude; }
                            }
                        }
                        if (trace && R < 30f && t < 8f && Mathf.Repeat(t, 0.5f) < Time.fixedDeltaTime)
                            Note($"    t {t:0.0} r {r:0.0} v {vel.magnitude:0.0} vt {vTarget:0.0} steer {car.steerInput:+0.00;-0.00} wheel {car.SteerAngleDeg:+0.0;-0.0} max {car.CurrentMaxSteerDeg:0.0} cs {car.chassisSlipAngle:+0.00;-0.00} pos {car.transform.position} fwd {car.transform.forward}");
                        if (Mathf.Abs(r - R) > 5f && t > 3f) offFor += Time.fixedDeltaTime; else offFor = 0f;
                        if (offFor > 0.5f) { why = r > R ? "ran wide" : "tucked in"; break; }
                        if (Mathf.Abs(car.chassisSlipAngle) > 0.7f) { why = "spun"; break; }
                        if (vel.magnitude > 80f) { why = "80 m/s"; break; }
                    }
                    drive = false; circle = false;
                    float pred = AIGrip.LateralG(car, bestV);
                    float ratio = best / Mathf.Max(pred, 0.01f);
                    if (best > 0.2f) ratios.Add(ratio);
                    Note($"  SKIDPAD R{R:0}: held {best:0.000} g at {bestV * 3.6f:0} km/h (ended: {why} at {vTarget * 3.6f:0} km/h target, " +
                         $"{car.Body.linearVelocity.magnitude * 3.6f:0} km/h th {car.throttleInput:0.00} gear {car.currentGear} steer {car.steerInput:+0.00;-0.00}) " +
                         $"| AIGrip {pred:0.000} g | measured/predicted {ratio:0.000}");
                }

                foreach (float v0 in new[] { 36f, 58f })
                {
                    xLane += 500f;
                    yield return Place(new Vector3(xLane, surfaceY, zBand - 200f), 0f, v0);
                    drive = true; throttle = 0f; brake = 1f; steer = 0f;
                    float tHi = -1f, tLo = -1f, t = 0f;
                    float hi = v0 - 3f, lo = v0 < 40f ? 12f : 38f;
                    while (t < 20f)
                    {
                        yield return new WaitForFixedUpdate();
                        t += Time.fixedDeltaTime;
                        float v = car.Body.linearVelocity.magnitude;
                        if (tHi < 0f && v <= hi) tHi = t;
                        if (tLo < 0f && v <= lo) { tLo = t; break; }
                    }
                    drive = false; brake = 0f;
                    float dec = tHi > 0f && tLo > tHi ? (hi - lo) / (tLo - tHi) : 0f;
                    float pred = AIGrip.BrakeDecel(car, (hi + lo) * 0.5f);
                    if (dec > 1f) bratios.Add(dec / pred);
                    Note($"  STOP {hi * 3.6f:0}->{lo * 3.6f:0} km/h: {dec:0.00} m/s2 ({dec / 9.81f:0.000} g) | AIGrip {pred:0.00} | measured/predicted {dec / Mathf.Max(pred, 0.01f):0.000}");
                }
            }
            if (ratios.Count > 0)
            {
                ratios.Sort();
                float mean = 0f; foreach (var r in ratios) mean += r; mean /= ratios.Count;
                Note($"SKIDPAD measured/predicted over {ratios.Count}: mean {mean:0.000}, min {ratios[0]:0.000}, median {ratios[ratios.Count / 2]:0.000}, max {ratios[ratios.Count - 1]:0.000}");
            }
            if (bratios.Count > 0)
            {
                bratios.Sort();
                float mean = 0f; foreach (var r in bratios) mean += r; mean /= bratios.Count;
                Note($"STOP measured/predicted over {bratios.Count}: mean {mean:0.000}, min {bratios[0]:0.000}, max {bratios[bratios.Count - 1]:0.000}");
            }
            Done();
        }

        void Done()
        {
            drive = false;
            Time.timeScale = 1f;
            RaceHandoff.ClearAll();
            AIGripPlayCheck.Finish();
            EditorApplication.Exit(0);
        }
    }
}
