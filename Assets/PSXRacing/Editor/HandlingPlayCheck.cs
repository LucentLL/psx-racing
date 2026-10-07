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
    /// The owner's handling notes of 2026-09-30, measured in the RUNNING game
    /// (tools\handling-play-check.ps1) on the flat strip BrakePlayCheck uses:
    ///   1 "Brakes feel like they have ABS. No tire screech during full braking."
    ///   2 "When car gets turned around (180) gas pedal does not slow down or
    ///      reverse the cars vector ... brake doesn't help because it tries to
    ///      reverse."
    ///   3 "Car swings side to side away from camera too much ... still retains
    ///      sliding on ice feel once it was initiated at high speed."
    /// Five scripted runs:
    ///   A  100 mph straight-line full brake: distance, wheel lock, screech.
    ///   B  100 mph handbrake 180, then full throttle: seconds until the
    ///      backward velocity is gone.
    ///   C  the same 180, then full brake: it slows, and reverse is never
    ///      selected while the car is still moving.
    ///   D  a drift started at 90 mph, slowed to 30 mph: seconds until the
    ///      drift state, the loose blend and the body slip are back to grip.
    ///   E  a keyboard lane change at 70 mph: yaw-rate lobes after the input,
    ///      settle time, and how far the lens trails the car's heading.
    ///   F  a Carbon drift (2026-10-02, the owner over two NFS Carbon drift
    ///      videos: "the car is always centered on the screen while drifting
    ///      ... the current build sends the car to the far sides of the
    ///      screen", and "more weight, even when sliding"): a lever kick at
    ///      60 mph, 35 deg held round an arc, a switch to the other side, held
    ///      again - where the car sits on SCREEN all the way (the chase lens's
    ///      own projection, read after it moved), how hard the kick spins it,
    ///      how far past the angle it swings and how much lock holding it takes.
    ///   G  a slide that gets away (the owner's "impossible to handle or regain
    ///      control because sense of center is lost"): a kick at 70 mph with
    ///      the KEY held into it, then full opposite lock, then back - the
    ///      keyboard pendulum - and where the car sits on screen through it.
    ///   H  the Viper over 100 mph (2026-10-02, the owner: "When I drive the
    ///      Viper, over 100MPH, making adjustments side to side, the car still
    ///      swings wildly away from the camera and it feels like I'm
    ///      completely separated from the car"): keyboard taps, then an
    ///      analog weave, at 110 mph - how far the NOSE turns from the lens,
    ///      where the car sits on screen, whether the drift layer joins in,
    ///      and whether the car itself settles.
    ///   S  STEERING SENSITIVITY (the settings menu, 2026-10-04): a scripted
    ///      control at 50 / 100 / 150% read as the command and the wheel
    ///      angle, then the settings read back after a reload.
    ///   P  TO SPEC handling (2026-10-07): the tuned FD, then one car per
    ///      layout (Civic EG FF, AE86 FR, NSX MR, RUF CTR RR) - rest attitude
    ///      and axle loads, launch, a 40 mph steer ramp (balance at the
    ///      limit), a lift mid-corner, a 100 mph stop, a 70 mph lane change.
    /// PSX_HANDLING_ONLY="D,E,F,G,H" runs just those (the first car only; H
    /// in the Viper; S last, as it reloads the scene).
    /// </summary>
    public static class HandlingPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Handling (play mode)")]
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
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.Solo = true;
            string sky = ApplyEnvWeather();
            if (sky != null) Note(sky);
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
            new GameObject("HandlingPlayCheckRunner").AddComponent<HandlingPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        /// <summary>
        /// PSX_WEATHER=rain|snow|fog|clear forces the sky (RaceHandoff.
        /// WeatherOverride) for a wet-vs-dry run of the handling, brake and
        /// race checks; unset leaves the day's own. Returns the line to log.
        /// </summary>
        internal static string ApplyEnvWeather()
        {
            string w = System.Environment.GetEnvironmentVariable("PSX_WEATHER");
            if (string.IsNullOrEmpty(w) || !System.Enum.TryParse(w, true, out Weather sky)) return null;
            RaceHandoff.WeatherOverride = (int)sky;
            return "WEATHER forced " + sky.ToString().ToUpperInvariant() + ": road grip x" +
                   Seasons.GripMult(sky, true).ToString("0.00") + " (PSX_WEATHER)";
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "HANDLING CHECK: ALL BEHAVE." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_handling_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    // Late, so LateUpdate reads the chase lens AFTER it moved this frame.
    [DefaultExecutionOrder(10000)]
    public class HandlingPlayCheckRunner : MonoBehaviour
    {
        const float Mph = 0.44704f;
        CarController car;
        bool drive, hand;
        float throttle, brake, steer;
        AudioSource skid;
        ChaseCamera cam;
        float surfaceY;
        float step;

        void FixedUpdate()
        {
            if (!drive || car == null) return;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.steerInput = steer;
            car.handbrakeInput = hand;
            car.heatAccelMult = 1f;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        // ---- where the car sits on screen (F, and noted in D and E) --------
        bool camTrack;
        float camOffMax, camOffTime, camLastX = 0.5f, camNoseLens;
        void CamReset() { camOffMax = 0f; camOffTime = 0f; camTrack = true; }
        void LateUpdate()
        {
            if (!camTrack || cam == null || car == null) return;
            var c = cam.GetComponent<Camera>();
            if (c == null) return;
            Vector3 vp = c.WorldToViewportPoint(car.transform.TransformPoint(car.Body.centerOfMass));
            if (vp.z <= 0f) { camOffMax = 0.5f; return; }
            camLastX = vp.x;
            // The nose against the lens, read HERE - after the lens moved,
            // on the same interpolated pose it framed (a FixedUpdate read is
            // a render frame stale: 2-3 deg at 69 deg/s).
            camNoseLens = Mathf.DeltaAngle(cam.transform.eulerAngles.y, car.transform.eulerAngles.y);
            float off = Mathf.Abs(vp.x - 0.5f);
            camOffMax = Mathf.Max(camOffMax, off);
            if (off > 0.15f) camOffTime += Time.deltaTime;
        }
        string CamLine() => "car on screen: x off centre max " + camOffMax.ToString("0.000") +
                            " of the width, " + camOffTime.ToString("0.00") + " s beyond 0.15";

        static string Only => System.Environment.GetEnvironmentVariable("PSX_HANDLING_ONLY");
        static bool Want(string t) => string.IsNullOrEmpty(Only) || Only.ToUpperInvariant().Contains(t);

        float CamLag() => cam == null ? 0f : Mathf.Abs(Mathf.DeltaAngle(car.transform.eulerAngles.y, cam.transform.eulerAngles.y));
        float Skid() => skid != null ? skid.volume : -1f;
        float VFwd() => Vector3.Dot(car.Body.linearVelocity, car.transform.forward);
        float Planar() { var v = car.Body.linearVelocity; v.y = 0f; return v.magnitude; }
        float BodySlipDeg()
        {
            float s = Mathf.Abs(car.chassisSlipAngle);
            if (s > Mathf.PI * 0.5f) s = Mathf.PI - s;
            return s * Mathf.Rad2Deg;
        }
        float MaxSlide()
        {
            float m = 0f;
            for (int w = 0; w < 4; w++) m = Mathf.Max(m, car.wheelContacts[w].slide);
            return m;
        }

        IEnumerator Place(float z, float yawDeg, float mps)
        {
            drive = false; hand = false; throttle = brake = steer = 0f;
            car.TeleportTo(new Vector3(xLane, surfaceY + 0.6f, z), Quaternion.Euler(0f, yawDeg, 0f));
            for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
            car.SetRolling(mps);
            if (cam != null) cam.ForgetAim();
        }
        float xLane;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 25; i++) yield return null;
            step = Time.fixedDeltaTime;

            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            var applier = DebugCarOps.LiveApplier();
            HandlingPlayCheck.Check(car != null && applier != null, "there is a player car and the scene's applier");
            if (car == null || applier == null) { Done(); yield break; }

            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;

            var strip = new GameObject("HandlingStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            var box = strip.AddComponent<BoxCollider>();
            box.size = new Vector3(3000f, 2f, 30000f);
            surfaceY = 4000f + 1f;

            CarSpec Pick(string name)
            {
                foreach (var c in CarCatalog.All)
                    if (c.name.Contains(name) && CarModelLibrary.LoadFor(c) != null) return c;
                return null;
            }
            var picks = new List<CarSpec>();
            foreach (var c in new[] { Pick("RX-7 Type RS"), Pick("CIVIC SiR-II (EG)") })
                if (c != null && !picks.Contains(c)) picks.Add(c);

            xLane = -900f;
            bool first = true;
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
                for (int f = 0; f < 5; f++) yield return null;
                car.manualMode = false;
                skid = null;
                foreach (var a in car.GetComponentsInChildren<AudioSource>(true))
                    if (a.gameObject.name == "snd_skid") skid = a;
                cam = Object.FindFirstObjectByType<ChaseCamera>();
                xLane += 400f;
                string who = spec.name;
                HandlingPlayCheck.Note(who + ": skid source " + (skid != null ? "found" : "MISSING") +
                                       ", chase camera " + (cam != null ? "found" : "MISSING"));

                if (Want("A")) yield return TestA(who);
                if (Want("B")) yield return TestBC(who, true);
                if (Want("C")) yield return TestBC(who, false);
                if (first)
                {
                    if (Want("D")) yield return TestD(who);
                    if (Want("E")) yield return TestE(who);
                    if (Want("F")) yield return TestF(who);
                    if (Want("G")) yield return TestG(who);
                }
                first = false;
                if (!Want("A") && !Want("B") && !Want("C")) break;
            }
            if (Want("H"))
            {
                var viper = Pick("VIPER GTS");
                HandlingPlayCheck.Check(viper != null, "the Viper GTS is in the catalog with a model");
                if (viper != null)
                {
                    RaceHandoff.CarSpecId = viper.id;
                    RaceHandoff.CarPaintSkin = null;
                    RaceHandoff.UpPower = RaceHandoff.UpWeight = RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = RaceHandoff.UpTires = 0;
                    RaceHandoff.Supercharged = false; RaceHandoff.Welded = false; RaceHandoff.Setup = null;
                    RaceHandoff.StartFuelPct = 100f;
                    applier.SwapCar();
                    for (int f = 0; f < 5; f++) yield return null;
                    car.manualMode = false;
                    cam = Object.FindFirstObjectByType<ChaseCamera>();
                    xLane += 400f;
                    yield return TestH(viper.name);
                }
            }
            // P: the sheet's handling columns, one car per layout (the tuned FD
            // as the reference, then FF / FR / MR / RR).
            if (Want("P"))
            {
                foreach (var name in new[] { "RX-7 Type R (FD, J) `91", "CIVIC SiR-II (EG) `91",
                                             "SPRINTER TRUENO GT-APEX (AE86) `83", "Honda NSX `90",
                                             "\"Yellow Bird\" `87" })
                {
                    var ps = Pick(name);
                    HandlingPlayCheck.Check(ps != null, "P: " + name + " is in the catalog with a model");
                    if (ps == null) continue;
                    RaceHandoff.CarSpecId = ps.id;
                    RaceHandoff.CarPaintSkin = null;
                    RaceHandoff.UpPower = RaceHandoff.UpWeight = RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = RaceHandoff.UpTires = 0;
                    RaceHandoff.Supercharged = false; RaceHandoff.Welded = false; RaceHandoff.Setup = null;
                    RaceHandoff.StartFuelPct = 100f;
                    applier.SwapCar();
                    for (int f = 0; f < 5; f++) yield return null;
                    car.manualMode = false;
                    cam = Object.FindFirstObjectByType<ChaseCamera>();
                    xLane += 400f;
                    yield return TestP(ps.name);
                }
            }
            // Last: it reloads the scene.
            if (Want("S")) yield return TestS();
            Done();
        }

        // ---- P: TO SPEC handling, one car per layout (2026-10-07) ---------
        // Rest attitude and axle loads, a standing launch, a 40 mph steer
        // ramp (balance at the limit), a lift mid-corner, a 100 mph stop and
        // a 70 mph lane change. Logged as numbers for a before/after table;
        // FAILs only for what makes a car undriveable (spin from a lane
        // change, rollover, a stop that swaps ends).
        float Yaw() => car.transform.eulerAngles.y;
        float YawRateDeg() => car.Body.angularVelocity.y * Mathf.Rad2Deg;
        float HoldThrottle(float target) => Mathf.Clamp01(0.35f + (target - VFwd()) * 0.6f);
        IEnumerator TestP(string who)
        {
            float minUp = 1f;
            // REST: three seconds on the handbrake.
            yield return Place(-14000f, 0f, 0f);
            throttle = 0f; brake = 0f; steer = 0f; hand = true; drive = true;
            for (float t = 0f; t < 3f; t += step) yield return new WaitForFixedUpdate();
            float pitch = -Mathf.DeltaAngle(0f, car.transform.eulerAngles.x);
            float ride = car.transform.position.y - surfaceY;
            float lf = car.wheelContacts[0].load + car.wheelContacts[1].load;
            float lr = car.wheelContacts[2].load + car.wheelContacts[3].load;
            float split = lf / Mathf.Max(1f, lf + lr);
            HandlingPlayCheck.Note(who + ": P chassis wdF " + car.weightDistFront.ToString("0.00") +
                " CoM z " + car.Body.centerOfMass.z.ToString("0.000") + " springs " + (car.springRateFront / 1000f).ToString("0.0") +
                "/" + (car.springRateRear / 1000f).ToString("0.0") + " N/mm dampers " + car.damperFront.ToString("0") + "/" +
                car.damperRear.ToString("0") + " bars " + car.antiRollFront.ToString("0") + "/" + car.antiRollRear.ToString("0") +
                " mu " + car.tireMuFront.ToString("0.000") + "/" + car.tireMuRear.ToString("0.000") +
                " limiter " + car.revLimitRPM.ToString("0") + " (redline " + car.redlineRPM.ToString("0") + ") diff " +
                car.diffAccelLock.ToString("0.00") + "/" + car.diffDecelLock.ToString("0.00") + "/" + car.diffPreloadN.ToString("0") + " N");
            HandlingPlayCheck.Note(who + ": P rest pitch " + pitch.ToString("+0.00;-0.00") + " deg (nose up +), body " +
                ride.ToString("0.000") + " m over the road, front axle carries " + (split * 100f).ToString("0.0") + "%");

            // LAUNCH: full throttle from a standstill.
            yield return Place(-13000f, 0f, 0f);
            hand = false; throttle = 1f; brake = 0f; steer = 0f; drive = true;
            float h0 = Yaw(), tl = 0f, v2 = -1f, t60 = -1f, slide2 = 0f;
            while (tl < 15f && t60 < 0f)
            {
                yield return new WaitForFixedUpdate();
                tl += step;
                if (tl <= 2f) slide2 = Mathf.Max(slide2, MaxSlide());
                if (v2 < 0f && tl >= 2f) v2 = VFwd();
                if (VFwd() >= 60f * Mph) t60 = tl;
                minUp = Mathf.Min(minUp, car.transform.up.y);
            }
            float launchYaw = Mathf.Abs(Mathf.DeltaAngle(h0, Yaw()));
            HandlingPlayCheck.Note(who + ": P launch 0-60 " + (t60 > 0f ? t60.ToString("0.00") + " s" : "not in 15 s") +
                ", " + (v2 / Mph).ToString("0.0") + " mph at 2 s, worst wheel slide in the first 2 s " +
                slide2.ToString("0.0") + " m/s, heading moved " + launchYaw.ToString("0.0") + " deg");

            // SKIDPAD: 40 mph held, the steer ramped 0 -> 1 over 8 s. Balance =
            // |front slip| - |rear slip| at the most lateral g (+ = understeer).
            yield return Place(-11000f, 0f, 40f * Mph);
            drive = true;
            float best = 0f, bF = 0f, bR = 0f, bSteer = 0f, bBody = 0f, maxBody = 0f;
            for (float t = 0f; t < 8f; t += step)
            {
                steer = Mathf.Clamp01(t / 8f);
                throttle = HoldThrottle(40f * Mph);
                yield return new WaitForFixedUpdate();
                float latG = Mathf.Abs(Planar() * car.Body.angularVelocity.y) / 9.81f;
                maxBody = Mathf.Max(maxBody, BodySlipDeg());
                minUp = Mathf.Min(minUp, car.transform.up.y);
                if (t > 1f && latG > best && BodySlipDeg() < 25f)
                {
                    best = latG; bSteer = steer; bBody = BodySlipDeg();
                    bF = Mathf.Abs(car.frontSlipAngle) * Mathf.Rad2Deg;
                    bR = Mathf.Abs(car.rearSlipAngle) * Mathf.Rad2Deg;
                }
            }
            HandlingPlayCheck.Note(who + ": P skidpad 40 mph max " + best.ToString("0.00") + " g at steer " + bSteer.ToString("0.00") +
                ", slip F " + bF.ToString("0.0") + " R " + bR.ToString("0.0") + " deg, balance " + (bF - bR).ToString("+0.0;-0.0") +
                " deg (+ understeer), body slip there " + bBody.ToString("0.0") + ", max " + maxBody.ToString("0.0"));

            // LIFT mid-corner: 80% of that steer at 40 mph, settle 3 s, then off.
            yield return Place(-9000f, 0f, 40f * Mph);
            drive = true;
            float holdSteer = Mathf.Max(0.15f, bSteer * 0.8f), r0 = 0f, s0 = 0f;
            for (float t = 0f; t < 3f; t += step)
            {
                steer = holdSteer; throttle = HoldThrottle(40f * Mph);
                yield return new WaitForFixedUpdate();
                r0 = Mathf.Abs(YawRateDeg()); s0 = BodySlipDeg();
            }
            float r1 = r0, s1 = s0;
            for (float t = 0f; t < 1.5f; t += step)
            {
                steer = holdSteer; throttle = 0f;
                yield return new WaitForFixedUpdate();
                r1 = Mathf.Max(r1, Mathf.Abs(YawRateDeg())); s1 = Mathf.Max(s1, BodySlipDeg());
                minUp = Mathf.Min(minUp, car.transform.up.y);
            }
            HandlingPlayCheck.Note(who + ": P lift at steer " + holdSteer.ToString("0.00") + ": yaw " + r0.ToString("0.0") + " -> peak " +
                r1.ToString("0.0") + " deg/s (" + ((r1 / Mathf.Max(0.1f, r0) - 1f) * 100f).ToString("+0;-0") + "%), body slip " +
                s0.ToString("0.0") + " -> " + s1.ToString("0.0") + " deg");
            HandlingPlayCheck.Check(s1 < 45f, who + ": P a lift mid-corner does not spin it", s1.ToString("0.0") + " deg body slip");

            // BRAKE: 100 mph, full pedal, straight.
            yield return Place(-7000f, 0f, 100f * Mph);
            throttle = 0f; brake = 1f; steer = 0f; drive = true;
            float hb = Yaw(), tb = 0f, maxR = 0f; Vector3 pb = car.transform.position;
            while (tb < 15f && car.Body.linearVelocity.magnitude > 0.3f)
            {
                yield return new WaitForFixedUpdate();
                tb += step;
                maxR = Mathf.Max(maxR, Mathf.Abs(YawRateDeg()));
                minUp = Mathf.Min(minUp, car.transform.up.y);
            }
            float bHead = Mathf.Abs(Mathf.DeltaAngle(hb, Yaw()));
            HandlingPlayCheck.Note(who + ": P brake 100-0 " + Vector3.Distance(pb, car.transform.position).ToString("0.0") +
                " m, heading moved " + bHead.ToString("0.0") + " deg, peak yaw " + maxR.ToString("0.0") + " deg/s");
            HandlingPlayCheck.Check(bHead < 20f, who + ": P a straight stop stays straight", bHead.ToString("0.0") + " deg");
            brake = 0f;

            // LANE CHANGE: 70 mph, half lock one way then the other, let go.
            yield return Place(-5000f, 0f, 70f * Mph);
            drive = true;
            float hl = Yaw(), lcSlip = 0f, lcYaw = 0f;
            for (float t = 0f; t < 4f; t += step)
            {
                steer = t < 0.6f ? 0.5f : t < 1.2f ? -0.5f : 0f;
                throttle = HoldThrottle(70f * Mph);
                yield return new WaitForFixedUpdate();
                lcSlip = Mathf.Max(lcSlip, BodySlipDeg());
                lcYaw = Mathf.Max(lcYaw, Mathf.Abs(YawRateDeg()));
                minUp = Mathf.Min(minUp, car.transform.up.y);
            }
            float lcHead = Mathf.Abs(Mathf.DeltaAngle(hl, Yaw()));
            HandlingPlayCheck.Note(who + ": P lane change 70 mph body slip max " + lcSlip.ToString("0.0") + " deg, yaw max " +
                lcYaw.ToString("0.0") + " deg/s, heading after " + lcHead.ToString("0.0") + " deg");
            HandlingPlayCheck.Check(lcSlip < 15f, who + ": P a plain lane change does not spin it", lcSlip.ToString("0.0") + " deg");
            HandlingPlayCheck.Check(minUp > 0.5f, who + ": P never rolls over", "lowest up.y " + minUp.ToString("0.00"));
            drive = false; steer = 0f; throttle = 0f;
        }

        // ---- S: STEERING SENSITIVITY + settings that persist (2026-10-04) --
        // The settings menu's STEERING slider through the REAL input path: a
        // scripted control position fed where a pad stick is read
        // (PlayerCarInput.ScriptedSteer), at 50%, 100% and 150%, read back as
        // the car's command and its actuated wheel angle at a standstill.
        // Then the settings survive a reload: the statics dropped (what a
        // fresh page load does) and the scene loaded again, with the MASTER
        // volume riding on the listener after the reload's fade-in.
        IEnumerator TestS()
        {
            yield return Place(0f, 0f, 0f);
            var input = car.GetComponent<PlayerCarInput>();
            HandlingPlayCheck.Check(input != null, "S: the player car has its input component");
            if (input == null) yield break;
            int steerWas = SteerPrefs.Percent;
            int masterWas = AudioPrefs.Percent(AudioPrefs.Channel.Master);
            int engineWas = AudioPrefs.Percent(AudioPrefs.Channel.Engine);
            input.enabled = true;
            input.inputEnabled = true;
            const float Control = 0.4f;
            PlayerCarInput.ScriptedSteer = Control;
            int[] pct = { 50, 100, 150 };
            float[] cmd = new float[3], deg = new float[3];
            for (int i = 0; i < pct.Length; i++)
            {
                SteerPrefs.Percent = pct[i];
                for (int f = 0; f < 40; f++) yield return new WaitForFixedUpdate();
                cmd[i] = car.steerInput;
                deg[i] = car.SteerAngleDeg;
            }
            PlayerCarInput.ScriptedSteer = null;
            input.enabled = false;
            HandlingPlayCheck.Note("S: control " + Control + " -> command " + cmd[0].ToString("0.000") + " / " +
                                   cmd[1].ToString("0.000") + " / " + cmd[2].ToString("0.000") + ", wheels " +
                                   deg[0].ToString("0.00") + " / " + deg[1].ToString("0.00") + " / " +
                                   deg[2].ToString("0.00") + " deg at 50 / 100 / 150%");
            HandlingPlayCheck.Check(Mathf.Abs(cmd[1] - Control) < 0.005f,
                                    "S: at 100% the car gets the control unchanged (the feel it had)", cmd[1]);
            HandlingPlayCheck.Check(Mathf.Abs(cmd[0] - Control * 0.5f) < 0.005f && Mathf.Abs(cmd[2] - Control * 1.5f) < 0.005f,
                                    "S: 50% halves the steer command, 150% takes it to 1.5x",
                                    cmd[0].ToString("0.000") + " / " + cmd[2].ToString("0.000"));
            float r150 = deg[2] / Mathf.Max(0.01f, deg[0]), r100 = deg[1] / Mathf.Max(0.01f, deg[0]);
            HandlingPlayCheck.Check(deg[0] > 1f && Mathf.Abs(r150 - 3f) < 0.15f && Mathf.Abs(r100 - 2f) < 0.1f,
                                    "S: the wheels turn in proportion - 150% is 3x and 100% 2x the 50% angle",
                                    r100.ToString("0.00") + "x / " + r150.ToString("0.00") + "x");

            // Persistence: write, drop the statics, reload the scene, read.
            SteerPrefs.Percent = 135;
            AudioPrefs.SetPercent(AudioPrefs.Channel.Master, 80);
            AudioPrefs.SetPercent(AudioPrefs.Channel.Engine, 70);
            SteerPrefs.ForgetCache();
            AudioPrefs.ForgetCache();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            for (int i = 0; i < 10; i++) yield return null;
            yield return new WaitForSecondsRealtime(2.5f);
            HandlingPlayCheck.Check(SteerPrefs.Percent == 135 &&
                                    AudioPrefs.Percent(AudioPrefs.Channel.Engine) == 70 &&
                                    AudioPrefs.Percent(AudioPrefs.Channel.Master) == 80,
                                    "S: STEERING and the volumes come back after a reload",
                                    SteerPrefs.Percent + "% / engine " + AudioPrefs.Percent(AudioPrefs.Channel.Engine) +
                                    "% / master " + AudioPrefs.Percent(AudioPrefs.Channel.Master) + "%");
            HandlingPlayCheck.Check(Mathf.Abs(AudioListener.volume - 0.8f) < 0.02f,
                                    "S: MASTER 80% is the listener's level once the reload has faded in",
                                    AudioListener.volume.ToString("0.000"));
            SteerPrefs.Percent = steerWas;
            AudioPrefs.SetPercent(AudioPrefs.Channel.Master, masterWas);
            AudioPrefs.SetPercent(AudioPrefs.Channel.Engine, engineWas);
        }

        // ---- A: 100 mph straight-line full brake ----------------------------
        IEnumerator TestA(string who)
        {
            yield return Place(-14000f, 0f, 100f * Mph);
            throttle = 0f; brake = 1f; steer = 0f; drive = true;
            Vector3 p0 = car.transform.position;
            float t = 0f, worstSlide = 0f, maxSkid = 0f, lockedT = 0f;
            while (t < 15f && car.Body.linearVelocity.magnitude > 0.3f)
            {
                yield return new WaitForFixedUpdate();
                t += step;
                float v = car.Body.linearVelocity.magnitude;
                int locked = 0;
                for (int w = 0; w < 4; w++)
                {
                    worstSlide = Mathf.Max(worstSlide, car.wheelContacts[w].slide);
                    if (v > 3f && car.wheelContacts[w].slide > 0.5f * v) locked++;
                }
                if (locked > 0) lockedT += step;
                maxSkid = Mathf.Max(maxSkid, Skid());
            }
            float dist = Vector3.Distance(p0, car.transform.position);
            float g = (100f * Mph) / Mathf.Max(0.01f, t) / 9.81f;
            // The bars move with the weather's grip (both 1 on a dry road).
            float wetBar = Seasons.RoadGripMult;
            HandlingPlayCheck.Check(dist < 115f / wetBar && g > 0.75f * wetBar, who + ": A 100-0 mph full brake stops sensibly",
                dist.ToString("0.0") + " m (" + (dist * 3.281f).ToString("0") + " ft) in " + t.ToString("0.00") + " s = " + g.ToString("0.00") + " g");
            HandlingPlayCheck.Check(worstSlide > 5f && lockedT > 0.5f, who + ": A full pedal locks the wheels (no ABS)",
                "worst wheel slide " + worstSlide.ToString("0.0") + " m/s, a wheel locked for " + lockedT.ToString("0.00") + " s");
            HandlingPlayCheck.Check(maxSkid > 0.2f, who + ": A and the tyres screech",
                "skid volume peak " + maxSkid.ToString("0.00"));
            drive = false;
        }

        // ---- B / C: the 180, then throttle (B) or brake (C) -----------------
        IEnumerator Spin180(string who)
        {
            yield return Place(-10000f, 0f, 100f * Mph);
            // Hold 100 mph for a moment so the box settles in its gear.
            throttle = 1f; brake = 0f; steer = 0f; hand = false; drive = true;
            for (float t = 0f; t < 0.5f; t += step) yield return new WaitForFixedUpdate();
            float h0 = car.transform.eulerAngles.y;
            throttle = 0f; hand = true; steer = 1f;
            float spun = 0f, ts = 0f;
            while (ts < 3f)
            {
                yield return new WaitForFixedUpdate();
                ts += step;
                spun = Mathf.Abs(Mathf.DeltaAngle(h0, car.transform.eulerAngles.y));
                // Facing back against the travel: the owner's "car sliding
                // backwards" state.
                if (VFwd() < -0.9f * Planar() && Planar() > 5f) break;
            }
            steer = 0f; hand = false;
            if (!(VFwd() < -0.9f * Planar()))
            {
                // The spin did not come round in 3 s: finish it by hand, so the
                // measurement below still starts from a car sliding backwards.
                HandlingPlayCheck.Note(who + ": handbrake spin reached " + spun.ToString("0") + " deg in 3 s; turned the rest by hand");
                Vector3 v = car.Body.linearVelocity; v.y = 0f;
                if (v.sqrMagnitude > 1f)
                {
                    Quaternion back = Quaternion.LookRotation(-v.normalized, Vector3.up);
                    car.Body.rotation = back;
                    car.transform.rotation = back;
                    car.Body.angularVelocity = Vector3.zero;
                }
            }
        }

        IEnumerator TestBC(string who, bool gas)
        {
            yield return Spin180(who);
            float v0 = -VFwd();
            int g0 = car.currentGear;
            throttle = gas ? 1f : 0f; brake = gas ? 0f : 1f; steer = 0f; hand = false;
            float t = 0f, tZero = -1f, reverseAt = -1f, t1 = -1f;
            float v1s = -1f, v2s = -1f;
            int gearMin = 99, gearMax = -99;
            while (t < 15f)
            {
                yield return new WaitForFixedUpdate();
                t += step;
                float vf = VFwd();
                gearMin = Mathf.Min(gearMin, car.currentGear);
                gearMax = Mathf.Max(gearMax, car.currentGear);
                if (v1s < 0f && t >= 1f) v1s = Planar();
                if (v2s < 0f && t >= 2f) v2s = Planar();
                if (car.currentGear == -1 && reverseAt < 0f) reverseAt = Planar();
                if (gas && tZero < 0f && vf >= 0f) { tZero = t; }
                if (gas && tZero >= 0f && t > tZero + 1f) break;
                if (!gas && Planar() < 0.3f) { tZero = t; break; }
            }
            if (gas)
            {
                HandlingPlayCheck.Check(tZero > 0f && tZero < 7f, who + ": B 180 at 100 mph then full GAS: the backward slide dies",
                    "backwards at " + (v0 / Mph).ToString("0") + " mph, gear " + g0 + " -> " +
                    (tZero > 0f ? "0 in " + tZero.ToString("0.00") + " s (" + (v0 / tZero / 9.81f).ToString("0.00") + " g)" : "STILL BACKWARDS after 15 s") +
                    "; speed at 1 s " + (v1s / Mph).ToString("0") + " mph, at 2 s " + (v2s / Mph).ToString("0") +
                    " mph; gears " + gearMin + ".." + gearMax + "; forward " + (Mathf.Max(0f, VFwd()) / Mph).ToString("0") + " mph 1 s later");
            }
            else
            {
                HandlingPlayCheck.Check(tZero > 0f && tZero < 7f && (reverseAt < 0f || reverseAt < 1f),
                    who + ": C 180 at 100 mph then full BRAKE: it stops and never selects reverse while moving",
                    "backwards at " + (v0 / Mph).ToString("0") + " mph, gear " + g0 + " -> " +
                    (tZero > 0f ? "stopped in " + tZero.ToString("0.00") + " s (" + (v0 / tZero / 9.81f).ToString("0.00") + " g)" : "STILL MOVING after 15 s") +
                    "; speed at 1 s " + (v1s / Mph).ToString("0") + " mph; " +
                    (reverseAt < 0f ? "reverse never selected" : "reverse selected at " + reverseAt.ToString("0.00") + " m/s") +
                    "; gears " + gearMin + ".." + gearMax);
            }
            drive = false;
        }

        // ---- D: a drift at 90 mph, slowed to 30 mph -------------------------
        string Trace(float t) =>
            "t " + t.ToString("0.0") + " v " + (Planar() / Mph).ToString("0") + " slip " + BodySlipDeg().ToString("0") +
            " rear " + (Mathf.Abs(car.rearSlipAngle) * Mathf.Rad2Deg).ToString("0") +
            " yaw " + (car.Body.angularVelocity.y * Mathf.Rad2Deg).ToString("0") +
            (car.Drifting ? " DRIFT" : " grip") + " blend " + car.DriftBlend.ToString("0.00") +
            " eb " + car.EbrakeTimer.ToString("0.00") + " st " + car.steerInput.ToString("0.0") + " g" + car.currentGear;

        // A steer step at 30 mph: peak body slip and yaw rate, the "is it
        // still on ice" measure, compared against a car that never slid.
        IEnumerator Step30(float[] outv)
        {
            throttle = 0.25f; brake = 0f; hand = false; steer = 0.6f;
            float ps = 0f, py = 0f;
            for (float t = 0f; t < 0.8f; t += step)
            {
                yield return new WaitForFixedUpdate();
                ps = Mathf.Max(ps, BodySlipDeg());
                py = Mathf.Max(py, Mathf.Abs(car.Body.angularVelocity.y * Mathf.Rad2Deg));
            }
            steer = 0f;
            outv[0] = ps; outv[1] = py;
        }

        IEnumerator TestD(string who)
        {
            // Control: the same step from a car that never slid.
            yield return Place(-7000f, 0f, 30f * Mph);
            drive = true; throttle = 0.25f;
            for (float t = 0f; t < 0.5f; t += step) yield return new WaitForFixedUpdate();
            var fresh = new float[2];
            yield return Step30(fresh);

            yield return Place(-6000f, 0f, 90f * Mph);
            drive = true;
            throttle = 0.6f; steer = 0f;
            CamReset();
            for (float t = 0f; t < 0.3f; t += step) yield return new WaitForFixedUpdate();
            // The kick: lever with lock on.
            hand = true; throttle = 0f; steer = 1f;
            for (float t = 0f; t < 0.2f; t += step) yield return new WaitForFixedUpdate();
            hand = false; throttle = 1f;
            // Hold it near 25 deg on the gas: counter-steer as it grows.
            float peakSlip = 0f, tr = 0f;
            bool spun = false;
            for (float t = 0f; t < 1.5f; t += step)
            {
                yield return new WaitForFixedUpdate();
                float sd = BodySlipDeg();
                peakSlip = Mathf.Max(peakSlip, sd);
                if (sd > 75f) { spun = true; break; }
                steer = Mathf.Clamp((25f - sd) / 20f, -1f, 1f) * (car.chassisSlipAngle >= 0f ? 1f : -1f);
                tr += step;
                if (tr >= 0.25f) { tr = 0f; HandlingPlayCheck.Note("  D drift  " + Trace(t)); }
            }
            // Out of it: off the gas, wheel straight, a firm brake down to 30.
            throttle = 0f; steer = 0f; brake = 0.5f;
            float tRel = 0f, tAt30 = -1f;
            bool driftAt30 = false; float blendAt30 = 0f, slipAt30 = 0f;
            float recovered = -1f, calm = 0f;
            tr = 0f;
            while (tRel < 12f)
            {
                yield return new WaitForFixedUpdate();
                tRel += step;
                tr += step;
                if (tr >= 0.25f) { tr = 0f; HandlingPlayCheck.Note("  D out    " + Trace(tRel)); }
                if (tAt30 < 0f && Planar() <= 30f * Mph)
                {
                    tAt30 = tRel; brake = 0f; throttle = 0.25f;
                    driftAt30 = car.Drifting; blendAt30 = car.DriftBlend; slipAt30 = BodySlipDeg();
                }
                bool grip = !car.Drifting && car.DriftBlend < 0.05f && BodySlipDeg() < 3f;
                calm = grip ? calm + step : 0f;
                if (recovered < 0f && calm >= 0.2f) recovered = tRel - 0.2f;
                if (tAt30 >= 0f && recovered >= 0f && tRel > tAt30 + 0.3f) break;
            }
            camTrack = false;
            HandlingPlayCheck.Note(who + ": D " + CamLine());
            var after = new float[2];
            yield return Step30(after);
            float afterSlow = recovered < 0f ? -1f : Mathf.Max(0f, recovered - tAt30);
            HandlingPlayCheck.Check(!spun && recovered >= 0f && afterSlow < 0.5f && after[0] < fresh[0] + 2f,
                who + ": D drift at 90 mph slowed to 30: grip is back",
                (spun ? "SPUN (slip > 75 deg); " : "") + "peak body slip " + peakSlip.ToString("0") + " deg; released -> grip " +
                (recovered >= 0f ? recovered.ToString("0.00") + " s" : "NEVER in 12 s") +
                "; reached 30 mph at " + tAt30.ToString("0.00") + " s with Drifting=" + driftAt30 +
                " blend " + blendAt30.ToString("0.00") + " slip " + slipAt30.ToString("0.0") + " deg; still loose " +
                (afterSlow < 0f ? "-" : afterSlow.ToString("0.00")) + " s after 30 mph; 30 mph steer step: slip " +
                after[0].ToString("0.0") + " deg / yaw " + after[1].ToString("0") + " deg/s vs never-slid " +
                fresh[0].ToString("0.0") + " / " + fresh[1].ToString("0"));
            drive = false; brake = 0f;
        }

        // ---- E: a keyboard lane change at 70 mph ----------------------------
        IEnumerator TestE(string who)
        {
            yield return Place(-2000f, 0f, 70f * Mph);
            drive = true; throttle = 0.35f; steer = 0f;
            for (float t = 0f; t < 1.0f; t += step) yield return new WaitForFixedUpdate();
            float h0 = car.transform.eulerAngles.y;
            float inLag = 0f;
            CamReset();
            steer = 1f;
            for (float t = 0f; t < 0.35f; t += step) { yield return new WaitForFixedUpdate(); inLag = Mathf.Max(inLag, CamLag()); if (Mathf.Repeat(t, 0.1f) < step * 0.99f) HandlingPlayCheck.Note("  E in +   " + Trace(t) + " lens " + CamLag().ToString("0.0")); }
            steer = -1f;
            for (float t = 0f; t < 0.35f; t += step) { yield return new WaitForFixedUpdate(); inLag = Mathf.Max(inLag, CamLag()); if (Mathf.Repeat(t, 0.1f) < step * 0.99f) HandlingPlayCheck.Note("  E in -   " + Trace(t) + " lens " + CamLag().ToString("0.0")); }
            steer = 0f;
            // After the input: count yaw-rate lobes, find the settle time and
            // how far the lens trails the heading.
            int lobes = 0; float lastSign = 0f;
            float peakYawAfter = 0f, settle = -1f, quiet = 0f;
            float camLagMax = 0f; int camLobes = 0; float camSign = 0f;
            float headMin = 0f, headMax = 0f, peakSlip = 0f;
            for (float t = 0f; t < 4f; t += step)
            {
                yield return new WaitForFixedUpdate();
                if (Mathf.Repeat(t, 0.2f) < step * 0.99f && t < 2f) HandlingPlayCheck.Note("  E after  " + Trace(t) + " lens " + CamLag().ToString("0.0"));
                float r = car.Body.angularVelocity.y * Mathf.Rad2Deg;
                peakYawAfter = Mathf.Max(peakYawAfter, Mathf.Abs(r));
                peakSlip = Mathf.Max(peakSlip, BodySlipDeg());
                float s = Mathf.Abs(r) > 3f ? Mathf.Sign(r) : 0f;
                if (s != 0f)
                {
                    if (s != lastSign) { lobes++; lastSign = s; }
                }
                float head = Mathf.DeltaAngle(h0, car.transform.eulerAngles.y);
                headMin = Mathf.Min(headMin, head); headMax = Mathf.Max(headMax, head);
                bool q = Mathf.Abs(r) < 2f && BodySlipDeg() < 1f;
                quiet = q ? quiet + step : 0f;
                if (settle < 0f && quiet >= 0.3f) settle = t - 0.3f;
                if (cam != null)
                {
                    float d = Mathf.DeltaAngle(car.transform.eulerAngles.y, cam.transform.eulerAngles.y);
                    camLagMax = Mathf.Max(camLagMax, Mathf.Abs(d));
                    float cs = Mathf.Abs(d) > 1.5f ? Mathf.Sign(d) : 0f;
                    if (cs != 0f && cs != camSign) { camLobes++; camSign = cs; }
                }
            }
            camTrack = false;
            HandlingPlayCheck.Note(who + ": E " + CamLine());
            HandlingPlayCheck.Check(lobes <= 2 && settle >= 0f && settle < 1.5f,
                who + ": E lane change at 70 mph settles without swinging",
                "yaw lobes >3 deg/s after the input " + lobes + ", peak " + peakYawAfter.ToString("0") +
                " deg/s, settled " + (settle >= 0f ? settle.ToString("0.00") + " s" : "NEVER in 4 s") +
                ", peak body slip " + peakSlip.ToString("0.0") + " deg, heading swung " + headMin.ToString("0.0") + ".." + headMax.ToString("0.0") +
                " deg; lens vs heading max " + inLag.ToString("0.0") + " deg in the input, " + camLagMax.ToString("0.0") + " after, " + camLobes + " lobes");
            drive = false;
        }

        // ---- F: a Carbon drift - kick, hold, switch sides, hold -------------
        const float FTarget = 35f;
        /// <summary>Body slip in degrees, positive in direction <paramref name="dir"/>
        /// (+1 = nose right of the travel, a right-hand drift).</summary>
        float SlipIn(float dir) => car.chassisSlipAngle * Mathf.Rad2Deg * dir;

        /// <summary>One held phase: a PD driver on the slip toward FTarget in
        /// <paramref name="dir"/>. o: [rms error after settleS, share of that
        /// time at full lock, peak slip, peak |yaw| deg/s, spun 0/1].</summary>
        IEnumerator HoldF(float dir, float seconds, float settleS, float[] o, string tag)
        {
            float prev = SlipIn(dir), sq = 0f, n = 0f, sat = 0f, peak = -999f, peakYaw = 0f, tr = 0f;
            for (float t = 0f; t < seconds; t += step)
            {
                yield return new WaitForFixedUpdate();
                float sd = SlipIn(dir);
                float rate = (sd - prev) / step; prev = sd;
                steer = Mathf.Clamp(((FTarget - sd) - 0.12f * rate) / 20f, -1f, 1f) * dir;
                peak = Mathf.Max(peak, sd);
                peakYaw = Mathf.Max(peakYaw, Mathf.Abs(car.Body.angularVelocity.y * Mathf.Rad2Deg));
                if (Mathf.Abs(car.chassisSlipAngle) > 1.75f) { o[4] = 1f; break; }
                if (t >= settleS) { sq += (sd - FTarget) * (sd - FTarget); n += 1f; if (Mathf.Abs(steer) > 0.98f) sat += 1f; }
                tr += step;
                if (tr >= 0.25f) { tr = 0f; HandlingPlayCheck.Note("  F " + tag + " " + Trace(t) + " cam x " + camLastX.ToString("0.00")); }
            }
            o[0] = n > 0f ? Mathf.Sqrt(sq / n) : -1f;
            o[1] = n > 0f ? sat / n : -1f;
            o[2] = peak; o[3] = peakYaw;
        }

        IEnumerator TestF(string who)
        {
            yield return Place(4000f, 0f, 60f * Mph);
            drive = true; throttle = 0.6f; steer = 0f; hand = false;
            for (float t = 0f; t < 0.3f; t += step) yield return new WaitForFixedUpdate();
            CamReset();
            float v0 = Planar();
            // The kick: lever with lock on, into a right-hand drift.
            float dir = 1f;
            hand = true; throttle = 0f; steer = dir;
            for (float t = 0f; t < 0.2f; t += step) yield return new WaitForFixedUpdate();
            hand = false; throttle = 0.85f;
            var h1 = new float[5];
            yield return HoldF(dir, 3.0f, 1.2f, h1, "hold R");
            float v1 = Planar();
            // The switch: off the gas, the wheel the other way, then the gas
            // and the driver holding the new side.
            var h2 = new float[5];
            if (h1[4] < 0.5f)
            {
                dir = -1f;
                throttle = 0.3f; steer = dir;
                for (float t = 0f; t < 0.35f; t += step) yield return new WaitForFixedUpdate();
                throttle = 0.85f;
                yield return HoldF(dir, 2.6f, 1.2f, h2, "hold L");
            }
            float v2 = Planar();
            // Let go.
            throttle = 0f; steer = 0f;
            for (float t = 0f; t < 1.5f; t += step) yield return new WaitForFixedUpdate();
            camTrack = false;
            bool spun = h1[4] > 0.5f || h2[4] > 0.5f;
            HandlingPlayCheck.Note(who + ": F " + CamLine());
            HandlingPlayCheck.Note(who + ": F speed " + (v0 / Mph).ToString("0") + " -> " + (v1 / Mph).ToString("0") +
                                   " after the first hold -> " + (v2 / Mph).ToString("0") + " mph after the second");
            HandlingPlayCheck.Check(camOffMax <= 0.12f,
                who + ": F the car stays centred on screen through the drift and the switch (Carbon: within ~0.1)", CamLine());
            // Measured 2026-10-02: 136 deg/s / 51 deg before the slide's
            // weight (CarController.SlideYawInertiaMul), 107 / 48 after. The
            // bound is the regression line: back toward the weightless kick
            // fails.
            HandlingPlayCheck.Check(!spun && h1[3] <= 115f && h1[2] <= 50f,
                who + ": F the kick turns the car with weight (peak yaw <= 115 deg/s - it was 136 - slip <= 50 for a 35 target)",
                (spun ? "SPUN; " : "") + "peak yaw " + h1[3].ToString("0") + " deg/s, peak slip " + h1[2].ToString("0") + " deg");
            // A measurement, not a verdict: the scripted driver loses the slide
            // as the speed falls under the drift layer's 36 mph (it did before
            // and after the 2026-10-02 pass alike), so what it can hold says as
            // much about the driver as about the car.
            HandlingPlayCheck.Note(who + ": F holding 35 deg - " +
                "right: rms " + h1[0].ToString("0.0") + " deg, full lock " + (100f * h1[1]).ToString("0") + "%; left after the switch: rms " +
                h2[0].ToString("0.0") + " deg, full lock " + (100f * h2[1]).ToString("0") + "%, peak yaw " + h2[3].ToString("0") +
                " deg/s, peak slip " + h2[2].ToString("0") + " deg");
            drive = false;
        }

        // ---- H: the Viper over 100 mph, small corrections ------------------
        /// <summary>One phase of H, steering by <paramref name="steerAt"/>(t).
        /// o: [max nose-vs-lens deg, lens lobes, peak body slip deg, peak |yaw|
        /// deg/s, max DriftBlend, steps Drifting].</summary>
        IEnumerator HPhase(float seconds, System.Func<float, float> steerAt, float[] o, string tag)
        {
            float tr = 0f, camSign = 0f;
            for (float t = 0f; t < seconds; t += step)
            {
                steer = steerAt(t);
                yield return new WaitForFixedUpdate();
                float d = camNoseLens;
                o[0] = Mathf.Max(o[0], Mathf.Abs(d));
                float cs = Mathf.Abs(d) > 1.5f ? Mathf.Sign(d) : 0f;
                if (cs != 0f && cs != camSign) { o[1] += 1f; camSign = cs; }
                o[2] = Mathf.Max(o[2], BodySlipDeg());
                o[3] = Mathf.Max(o[3], Mathf.Abs(car.Body.angularVelocity.y * Mathf.Rad2Deg));
                o[4] = Mathf.Max(o[4], car.DriftBlend);
                if (car.Drifting) o[5] += 1f;
                tr += step;
                if (tr >= 0.1f) { tr = 0f; HandlingPlayCheck.Note("  H " + tag + " " + Trace(t) + " nose-lens " + d.ToString("0.0") + " cam x " + camLastX.ToString("0.00")); }
            }
        }

        IEnumerator TestH(string who)
        {
            yield return Place(12000f, 0f, 110f * Mph);
            drive = true; throttle = 0.75f; steer = 0f; hand = false;
            for (float t = 0f; t < 1.0f; t += step) yield return new WaitForFixedUpdate();
            float v0 = Planar();
            // Keyboard: the owner's side-to-side - full lock HELD a third to
            // half a second each way, flicked straight across (he watched the
            // first cut of this, 0.1 s taps, and said it barely turned the wheel).
            CamReset();
            var k = new float[6];
            float[] taps = { 0.35f, 0.45f, 0.40f, 0.50f, 0.35f };
            for (int i = 0; i < taps.Length; i++)
            {
                float sgn = i % 2 == 0 ? 1f : -1f, len = taps[i];
                yield return HPhase(len, _ => sgn, k, "key" + (sgn > 0 ? "+" : "-"));
                yield return HPhase(0.05f, _ => 0f, k, "key0");
            }
            yield return HPhase(2.0f, _ => 0f, k, "after");
            camTrack = false;
            float keysOff = camOffMax;
            float v1 = Planar();
            // A pad: a hard weave, 80% of the stick once a second.
            CamReset();
            var w = new float[6];
            yield return HPhase(3.0f, t => 0.8f * Mathf.Sin(2f * Mathf.PI * 1.0f * t), w, "weave");
            yield return HPhase(1.5f, _ => 0f, w, "after");
            camTrack = false;
            float weaveOff = camOffMax;
            HandlingPlayCheck.Note(who + ": H speed " + (v0 / Mph).ToString("0") + " -> " + (v1 / Mph).ToString("0") + " -> " + (Planar() / Mph).ToString("0") + " mph");
            string Line(float[] o, float off) =>
                "nose vs lens max " + o[0].ToString("0.0") + " deg (" + o[1].ToString("0") + " lobes), car off centre " + off.ToString("0.000") +
                ", peak slip " + o[2].ToString("0.0") + " deg, peak yaw " + o[3].ToString("0") + " deg/s, drift blend max " + o[4].ToString("0.00") +
                ", Drifting " + (o[5] * step).ToString("0.00") + " s";
            HandlingPlayCheck.Check(k[0] <= 7f && keysOff <= 0.05f && k[4] < 0.1f,
                who + ": H 110 mph keyboard corrections: the car stays square to the lens and the drift layer stays out", Line(k, keysOff));
            HandlingPlayCheck.Check(w[0] <= 6f && weaveOff <= 0.05f && w[4] < 0.1f,
                who + ": H 110 mph pad weave: the car stays square to the lens and the drift layer stays out", Line(w, weaveOff));
            drive = false; throttle = 0f; steer = 0f;
        }

        // ---- G: the slide that gets away - the keyboard pendulum -----------
        IEnumerator GPhase(float seconds, string tag, float[] o)
        {
            float tr = 0f;
            for (float t = 0f; t < seconds; t += step)
            {
                yield return new WaitForFixedUpdate();
                o[0] = Mathf.Max(o[0], Mathf.Abs(car.Body.angularVelocity.y * Mathf.Rad2Deg));
                o[1] = Mathf.Max(o[1], Mathf.Abs(car.chassisSlipAngle) * Mathf.Rad2Deg);
                tr += step;
                if (tr >= 0.2f) { tr = 0f; HandlingPlayCheck.Note("  G " + tag + " " + Trace(t) + " cam x " + camLastX.ToString("0.00")); }
            }
        }

        IEnumerator TestG(string who)
        {
            yield return Place(8000f, 0f, 70f * Mph);
            drive = true; throttle = 0.6f; steer = 0f; hand = false;
            for (float t = 0f; t < 0.3f; t += step) yield return new WaitForFixedUpdate();
            CamReset();
            var o = new float[2];
            hand = true; throttle = 0f; steer = 1f;
            yield return GPhase(0.25f, "kick ", o);
            hand = false; throttle = 1f; steer = 1f;
            yield return GPhase(0.7f, "into ", o);
            steer = -1f;
            yield return GPhase(0.8f, "catch", o);
            steer = 1f;
            yield return GPhase(0.6f, "back ", o);
            steer = 0f; throttle = 0.5f;
            yield return GPhase(1.5f, "let  ", o);
            camTrack = false;
            HandlingPlayCheck.Note(who + ": G peak yaw " + o[0].ToString("0") + " deg/s, peak body slip " + o[1].ToString("0") +
                                   " deg (over 90 = it went round), end speed " + (Planar() / Mph).ToString("0") + " mph");
            HandlingPlayCheck.Check(camOffMax <= 0.12f,
                who + ": G a slide that gets away still keeps the car in the middle of the screen", CamLine());
            drive = false; throttle = 0f; steer = 0f;
        }

        void Done()
        {
            drive = false;
            RaceHandoff.ClearAll();
            HandlingPlayCheck.Finish();
            EditorApplication.Exit(HandlingPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
