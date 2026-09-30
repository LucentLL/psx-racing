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

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "HANDLING CHECK: ALL FIVE BEHAVE." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_handling_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

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

                yield return TestA(who);
                yield return TestBC(who, true);
                yield return TestBC(who, false);
                if (first)
                {
                    yield return TestD(who);
                    yield return TestE(who);
                }
                first = false;
            }
            Done();
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
            HandlingPlayCheck.Check(dist < 115f && g > 0.75f, who + ": A 100-0 mph full brake stops sensibly",
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
            HandlingPlayCheck.Check(lobes <= 2 && settle >= 0f && settle < 1.5f,
                who + ": E lane change at 70 mph settles without swinging",
                "yaw lobes >3 deg/s after the input " + lobes + ", peak " + peakYawAfter.ToString("0") +
                " deg/s, settled " + (settle >= 0f ? settle.ToString("0.00") + " s" : "NEVER in 4 s") +
                ", peak body slip " + peakSlip.ToString("0.0") + " deg, heading swung " + headMin.ToString("0.0") + ".." + headMax.ToString("0.0") +
                " deg; lens vs heading max " + inLag.ToString("0.0") + " deg in the input, " + camLagMax.ToString("0.0") + " after, " + camLobes + " lobes");
            drive = false;
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
