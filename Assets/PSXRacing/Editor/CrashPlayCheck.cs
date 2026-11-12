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
    /// R0a CRASH STOP-GAP in the RUNNING game (Fault_System_Design_2026-11-11
    /// §9-10), on the flat strip HandlingPlayCheck uses, in the check venue:
    ///
    ///   D  THE DRIFT. Hands off at 100 km/h, the crash pull
    ///      (CarController.hitSteerPull, road-wheel degrees) takes the car
    ///      0.5-1.5 m sideways per 100 m in the minor band and 2-5 m in the
    ///      severe band, toward the struck side - left and right, with the
    ///      counter-steer assist OFF, STANDARD and FULL, on three chassis (FR
    ///      reference, FF short, FR long). The pull-free run is the baseline.
    ///   L  LIVE: a hit through the career car's own ledger at 100 km/h puts
    ///      the pull on the car the same frame, the drift follows within the
    ///      leg, and the exit stamp hands the apply-back the same outcome. The
    ///      next drive's persisted pull (faultSteerPullDeg) drifts the same.
    ///   W  WALLS: real PhysX contacts - a nose into a wall that covers only
    ///      the LEFT half of the front, a right flank into a 200 m barrier
    ///      whose bounding box is centred 100 m AHEAD of the car, a tail into
    ///      a wall - located FRONT LEFT, SIDE RIGHT and REAR; the hit rolled at
    ///      once; DamageScore and WorstHit as the switch-off run reads them.
    ///
    /// tools\crash-play-check.ps1 -> PSXRacing_crash_play_check.txt. Exits
    /// itself (no -quit: it enters play mode). Watched by default - hidden
    /// batch play mode crashes in RenderTexture::Create on the owner's PC.
    /// </summary>
    public static class CrashPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Crash Stop-Gap (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(TrackCatalog.CheckVenue);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + TrackCatalog.At(TrackCatalog.CheckVenue).id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            // Never the owner's career: nothing here saves, and if it did it
            // would land under its own key.
            LifeSimManager.UseScratchSave("crashplay");
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;      // a career drive: the ledger arms
            RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
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
            new GameObject("CrashPlayCheckRunner").AddComponent<CrashPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "CRASH CHECK: ALL HOLD." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_crash_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class CrashPlayCheckRunner : MonoBehaviour
    {
        const float Kmh100 = 100f / 3.6f;
        CarController car;
        CollisionResponder resp;
        bool drive, cruise;
        float throttle, brake, cruiseMps;
        float step, surfaceY;
        float zNext = -14000f;
        const float XLane = -600f;
        readonly List<GameObject> props = new List<GameObject>();

        void FixedUpdate()
        {
            if (!drive || car == null) return;
            float vf = Vector3.Dot(car.Body.linearVelocity, car.transform.forward);
            car.throttleInput = cruise ? Mathf.Clamp01(0.35f + (cruiseMps - vf) * 0.6f) : throttle;
            car.brakeInput = brake;
            car.steerInput = 0f;          // HANDS OFF, all the way through
            car.handbrakeInput = false;
            car.heatAccelMult = 1f;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 25; i++) yield return null;
            step = Time.fixedDeltaTime;

            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            var applier = DebugCarOps.LiveApplier();
            CrashPlayCheck.Check(car != null && applier != null, "there is a player car and the scene's applier");
            if (car == null || applier == null) { Done(); yield break; }
            resp = car.GetComponent<CollisionResponder>();
            CrashPlayCheck.Check(CrashStopGap.On, "PSX_FAULT_CRASH is on (the stop-gap is live)");
            CrashPlayCheck.Check(resp != null && resp.Ledger != null,
                                 "the scene's applier armed the career car's crash ledger at boot (RaceHandoffApplier -> CrashStopGap.Arm)");

            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;

            var strip = new GameObject("CrashStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            var box = strip.AddComponent<BoxCollider>();
            box.size = new Vector3(3000f, 2f, 30000f);
            surfaceY = 4000f + 1f;
            props.Add(strip);

            CarSpec Pick(string name)
            {
                foreach (var c in CarCatalog.All)
                    if (c.name.Contains(name) && CarModelLibrary.LoadFor(c) != null) return c;
                return null;
            }
            var picks = new List<CarSpec>();
            foreach (var c in new[] { Pick("RX-7 Type RS"), Pick("CIVIC SiR-II (EG)"), Pick("VIPER GTS") })
                if (c != null && !picks.Contains(c)) picks.Add(c);
            CrashPlayCheck.Check(picks.Count == 3, "three chassis to drive (FR reference, FF short, FR long)", picks.Count);

            CrashPlayCheck.Note("pull: minor " + CrashStopGap.PullDegMinor.ToString("0.000") + " deg, severe " +
                                CrashStopGap.PullDegSevere.ToString("0.000") + " deg of road wheel");

            bool first = true;
            foreach (var spec in picks)
            {
                yield return Become(applier, spec);
                string who = spec.name;
                float wb = car.wheelbase;
                CrashPlayCheck.Note(who + ": wheelbase " + wb.ToString("0.000") + " m");
                foreach (float assist in new[] { 0f, CarController.DefaultCountersteerAssist, 1f })
                {
                    float y0 = 0f;
                    yield return DriftRun(0f, false, assist, v => y0 = v);
                    var got = new Dictionary<float, float>();
                    foreach (float pull in new[] { CrashStopGap.PullDegMinor, -CrashStopGap.PullDegMinor,
                                                   CrashStopGap.PullDegSevere, -CrashStopGap.PullDegSevere })
                    {
                        float y = 0f;
                        yield return DriftRun(pull, false, assist, v => y = v);
                        got[pull] = y - y0;
                    }
                    string a = assist <= 0f ? "assist OFF" : assist >= 1f ? "assist FULL" : "assist STANDARD";
                    float mR = got[CrashStopGap.PullDegMinor], mL = got[-CrashStopGap.PullDegMinor];
                    float sR = got[CrashStopGap.PullDegSevere], sL = got[-CrashStopGap.PullDegSevere];
                    CrashPlayCheck.Check(mR >= 0.5f && mR <= 1.5f && mL <= -0.5f && mL >= -1.5f,
                        who + ", " + a + ": MINOR drifts 0.5-1.5 m per 100 m at 100 km/h hands-off, toward the struck side",
                        "right +" + mR.ToString("0.00") + " m, left " + mL.ToString("0.00") + " m (pull-free " + y0.ToString("+0.00;-0.00") + ")");
                    CrashPlayCheck.Check(sR >= 2f && sR <= 5f && sL <= -2f && sL >= -5f,
                        who + ", " + a + ": SEVERE drifts 2-5 m per 100 m, toward the struck side",
                        "right +" + sR.ToString("0.00") + " m, left " + sL.ToString("0.00") + " m");
                    if (first && assist == CarController.DefaultCountersteerAssist)
                    {
                        // What the constants would need to be to land mid-band
                        // on this car (drift is linear in the pull this small).
                        float perDegM = (mR - mL) / (2f * CrashStopGap.PullDegMinor);
                        float perDegS = (sR - sL) / (2f * CrashStopGap.PullDegSevere);
                        CrashPlayCheck.Note(who + ": " + perDegM.ToString("0.0") + " m per degree (minor), " +
                                            perDegS.ToString("0.0") + " (severe); 1.0 m needs " +
                                            (1f / Mathf.Max(0.01f, perDegM)).ToString("0.000") + " deg, 3.5 m needs " +
                                            (3.5f / Mathf.Max(0.01f, perDegS)).ToString("0.000") + " deg");
                    }
                }
                if (first) { yield return LiveRun(who); yield return WallRuns(who, applier, spec); }
                first = false;
            }
            Done();
        }

        IEnumerator Become(RaceHandoffApplier applier, CarSpec spec)
        {
            RaceHandoff.CarSpecId = spec.id;
            RaceHandoff.CarPaintSkin = null;
            RaceHandoff.UpPower = RaceHandoff.UpWeight = RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = RaceHandoff.UpTires = 0;
            RaceHandoff.Supercharged = false; RaceHandoff.Welded = false; RaceHandoff.Setup = null;
            RaceHandoff.StartFuelPct = 100f;
            applier.SwapCar();
            for (int f = 0; f < 5; f++) yield return null;
            car.manualMode = false;
            car.faultSteerPull = 0f; car.faultSteerPullDeg = 0f; car.hitSteerPull = 0f;
        }

        IEnumerator Place(float mps)
        {
            drive = false;
            throttle = brake = 0f;
            car.TeleportTo(new Vector3(XLane, surfaceY + 0.6f, zNext), Quaternion.identity);
            zNext += 450f;
            for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
            car.SetRolling(mps);
        }

        /// <summary>
        /// Settle hands-off at 100 km/h for a second, then put the pull on and
        /// read how far the car has gone sideways from its line once it has
        /// covered 100 m along it.
        /// </summary>
        IEnumerator DriftRun(float pullDeg, bool persisted, float assist, System.Action<float> result)
        {
            car.countersteerAssist = assist;
            car.hitSteerPull = 0f; car.faultSteerPullDeg = 0f; car.faultSteerPull = 0f;
            yield return Place(Kmh100);
            cruise = true; cruiseMps = Kmh100; drive = true;
            for (float t = 0f; t < 1.0f; t += step) yield return new WaitForFixedUpdate();
            Vector3 p0 = car.transform.position;
            Vector3 f0 = car.transform.forward; f0.y = 0f; f0.Normalize();
            Vector3 r0 = Vector3.Cross(Vector3.up, f0);
            if (persisted) car.faultSteerPullDeg = pullDeg; else car.hitSteerPull = pullDeg;
            float along = 0f, tt = 0f;
            while (along < 100f && tt < 10f)
            {
                yield return new WaitForFixedUpdate();
                tt += step;
                along = Vector3.Dot(car.transform.position - p0, f0);
            }
            result(Vector3.Dot(car.transform.position - p0, r0));
            drive = false;
            car.hitSteerPull = 0f; car.faultSteerPullDeg = 0f;
        }

        // ---- L: the hit through the ledger, at speed ---------------------
        IEnumerator LiveRun(string who)
        {
            car.countersteerAssist = CarController.DefaultCountersteerAssist;
            float y0 = 0f;
            yield return DriftRun(0f, false, CarController.DefaultCountersteerAssist, v => y0 = v);

            CrashStopGap.Arm(car);
            var L = resp.Ledger;
            L.DrawOverride = () => new CrashStopGap.Draws { paint = 0.5f, bumper = 0.1f, body = 0.5f, radiator = 0.9f, radAmt = 0.5f, align = 0.1f };
            yield return Place(Kmh100);
            cruise = true; cruiseMps = Kmh100; drive = true;
            for (float t = 0f; t < 1.0f; t += step) yield return new WaitForFixedUpdate();
            Vector3 p0 = car.transform.position;
            Vector3 f0 = car.transform.forward; f0.y = 0f; f0.Normalize();
            Vector3 r0 = Vector3.Cross(Vector3.up, f0);
            // A 12 m/s nose hit on the LEFT (minor band), as OnCollisionEnter
            // hands it over.
            float before = car.hitSteerPull;
            L.Hit(CrashStopGap.ZoneFront, -1, 12f, CrashStopGap.LiveBumper(), Time.time);
            float sameFrame = car.hitSteerPull;
            CrashPlayCheck.Check(before == 0f && Mathf.Approximately(sameFrame, -CrashStopGap.PullDegMinor),
                who + ": L the pull is on the car the same frame as the hit, to the struck (left) side",
                before.ToString("0.000") + " -> " + sameFrame.ToString("0.000") + " deg");
            float along = 0f, tt = 0f;
            while (along < 100f && tt < 10f)
            {
                yield return new WaitForFixedUpdate();
                tt += step;
                along = Vector3.Dot(car.transform.position - p0, f0);
            }
            float y = Vector3.Dot(car.transform.position - p0, r0) - y0;
            CrashPlayCheck.Check(y <= -0.5f && y >= -1.5f,
                who + ": L and the car is drifting left within the minor band 100 m later, inside the leg",
                y.ToString("0.00") + " m in " + tt.ToString("0.0") + " s");
            drive = false;
            CrashStopGap.StampHandoff(resp);
            CrashPlayCheck.Check(RaceHandoff.CrashRolled && RaceHandoff.CrashAlignSide == -1 && RaceHandoff.CrashAlignSeverity == 1 &&
                                 RaceHandoff.CrashBumperId == "bumper_crack",
                who + ": L the exit stamp hands the apply-back exactly that (no second roll there)", CrashBank.FromHandoff());
            RaceHandoff.ClearResult();

            float yp = 0f;
            yield return DriftRun(-CrashStopGap.PullDegMinor, true, CarController.DefaultCountersteerAssist, v => yp = v);
            yp -= y0;
            CrashPlayCheck.Check(Mathf.Abs(yp - y) < 0.25f,
                who + ": L next drive, the banked alignment (faultSteerPullDeg) drifts the same as the live pull",
                yp.ToString("0.00") + " vs " + y.ToString("0.00") + " m");
            car.hitSteerPull = 0f;
            CrashStopGap.Arm(car);
        }

        // ---- W: real walls ------------------------------------------------
        GameObject Wall(Vector3 centre, Vector3 size, string name)
        {
            var go = new GameObject(name);
            go.transform.position = centre;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            props.Add(go);
            return go;
        }

        float BodyHalfLength()
        {
            var shell = car.GetComponent<CarBody>();
            if (shell != null && shell.box != null) return shell.box.size.z * 0.5f * shell.box.transform.lossyScale.z;
            return 2.2f;
        }

        float BodyHalfWidth()
        {
            var shell = car.GetComponent<CarBody>();
            if (shell != null && shell.box != null) return shell.box.size.x * 0.5f * shell.box.transform.lossyScale.x;
            return 0.88f;
        }

        IEnumerator Hit(string what, System.Action setVelocity, float seconds)
        {
            drive = true; cruise = false; throttle = 0f; brake = 0f;
            setVelocity();
            for (float t = 0f; t < seconds; t += step) yield return new WaitForFixedUpdate();
            drive = false;
        }

        IEnumerator WallRuns(string who, RaceHandoffApplier applier, CarSpec spec)
        {
            float hl = BodyHalfLength(), hw = BodyHalfWidth();
            var forced = (System.Func<CrashStopGap.Draws>)(() => new CrashStopGap.Draws
                { paint = 0.5f, bumper = 0.1f, body = 0.5f, radiator = 0.1f, radAmt = 0.5f, align = 0.1f });

            // W1: nose into a wall that covers only the LEFT half of the front.
            float dsOn = 0f, whOn = 0f, dsOff = 0f, whOff = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                bool on = pass == 1;
                CrashStopGap.On = on;
                car.hitSteerPull = 0f;
                resp.ResetDamage();
                resp.Ledger = null;
                if (on) { CrashStopGap.Arm(car); resp.Ledger.DrawOverride = forced; }
                yield return Place(12.5f);
                Vector3 c0 = car.transform.position;
                var w = Wall(new Vector3(XLane - 5f, surfaceY + 1.2f, c0.z + hl + 5f), new Vector3(10f, 2.4f, 1f), "LeftHalfWall");
                yield return Hit("front", () => car.Body.linearVelocity = car.transform.forward * 12.5f, 1.5f);
                if (on) { dsOn = resp.DamageScore; whOn = resp.WorstHit; } else { dsOff = resp.DamageScore; whOff = resp.WorstHit; }
                if (on)
                {
                    var t = resp.Ledger != null ? resp.Ledger.Total : new CrashStopGap.Outcome();
                    CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneFront && resp.WorstHitSide == -1,
                        who + ": W a nose into a wall covering the LEFT half of the front is FRONT LEFT",
                        CrashStopGap.ZoneName(resp.WorstHitZone) + "/" + CrashStopGap.SideName(resp.WorstHitSide) +
                        " at " + resp.WorstHit.ToString("0.0") + " m/s");
                    CrashPlayCheck.Check(resp.Ledger != null && resp.Ledger.Rolls > 0 && t.Has(CrashStopGap.Cls.Bumper) &&
                                         (resp.WorstHit < CrashStopGap.HeavyMps || (t.alignSide == -1 && car.hitSteerPull < 0f)),
                        who + ": W rolled at the hit (dice forced): " + t, "pull now " + car.hitSteerPull.ToString("0.000") + " deg");
                    CrashStopGap.StampHandoff(resp);
                    CrashPlayCheck.Check(RaceHandoff.WorstHitZone == CrashStopGap.ZoneFront && RaceHandoff.WorstHitSide == -1 &&
                                         Mathf.Approximately(RaceHandoff.WorstHitMps, resp.WorstHit),
                        who + ": W the stamp carries WorstHitMps, zone and side to the apply-back");
                    RaceHandoff.ClearResult();
                }
                Object.Destroy(w);
                yield return null;
            }
            CrashStopGap.On = true;
            CrashPlayCheck.Check(Mathf.Abs(whOn - whOff) < 0.15f && Mathf.Abs(dsOn - dsOff) <= 0.05f * Mathf.Max(1f, dsOff),
                who + ": W DamageScore and WorstHit read the same hit the same with the switch off and on (AI retirement does not move)",
                "WorstHit " + whOff.ToString("0.00") + " / " + whOn.ToString("0.00") + " m/s, DamageScore " +
                dsOff.ToString("0.0") + " / " + dsOn.ToString("0.0"));

            // W2: a right flank into a 200 m barrier whose bounds sit AHEAD of the car.
            car.hitSteerPull = 0f; resp.ResetDamage(); CrashStopGap.Arm(car); resp.Ledger.DrawOverride = forced;
            yield return Place(0f);
            Vector3 c1 = car.transform.position;
            var barrier = Wall(new Vector3(XLane + hw + 0.5f + 0.5f, surfaceY + 0.6f, c1.z + 100f - hl), new Vector3(1f, 1.2f, 200f), "LongBarrier");
            Vector3 bc = barrier.GetComponent<BoxCollider>().bounds.center - c1;
            yield return Hit("side", () => car.Body.linearVelocity = car.transform.forward * 3f + car.transform.right * 8f, 1.2f);
            CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneSide && resp.WorstHitSide == 1,
                who + ": W a right flank into a 200 m barrier is SIDE RIGHT, though the barrier's bounds are centred " +
                bc.z.ToString("0") + " m ahead",
                CrashStopGap.ZoneName(resp.WorstHitZone) + "/" + CrashStopGap.SideName(resp.WorstHitSide) + " at " +
                resp.WorstHit.ToString("0.0") + " m/s; " + (resp.Ledger != null ? resp.Ledger.Total.ToString() : "-"));
            Object.Destroy(barrier);

            // W3: the tail into a wall.
            car.hitSteerPull = 0f; resp.ResetDamage(); CrashStopGap.Arm(car); resp.Ledger.DrawOverride = forced;
            yield return Place(0f);
            Vector3 c2 = car.transform.position;
            var back = Wall(new Vector3(XLane + 1f, surfaceY + 1.2f, c2.z - hl - 2f), new Vector3(10f, 2.4f, 1f), "RearWall");
            yield return Hit("rear", () => car.Body.linearVelocity = -car.transform.forward * 6f, 1.5f);
            CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneRear,
                who + ": W a tail into a wall is REAR",
                CrashStopGap.ZoneName(resp.WorstHitZone) + "/" + CrashStopGap.SideName(resp.WorstHitSide) + " at " +
                resp.WorstHit.ToString("0.0") + " m/s; " + (resp.Ledger != null ? resp.Ledger.Total.ToString() : "-"));
            Object.Destroy(back);
            car.hitSteerPull = 0f;
            resp.ResetDamage();
            CrashStopGap.Arm(car);
            yield return null;
        }

        void Done()
        {
            drive = false;
            CrashStopGap.On = System.Environment.GetEnvironmentVariable("PSX_FAULT_CRASH") != "0";
            foreach (var p in props) if (p != null) Destroy(p);
            RaceHandoff.ClearAll();
            CrashPlayCheck.Finish();
            EditorApplication.Exit(CrashPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
