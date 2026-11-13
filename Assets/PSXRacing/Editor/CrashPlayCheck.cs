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
    ///      the pull on the car the same frame and the drift follows within the
    ///      leg. THE NEXT DRIVE is built the real way - the exit stamp, the
    ///      apply-back onto a scratch car (CrashBank), the request
    ///      (FillCarRequestFor) and the applier (ReapplyCar) - and drifts the
    ///      same; nothing writes a pull by hand. With an alignment the car
    ///      ALREADY carries (the other side, and the same side) the live pull
    ///      and the next drive's still match.
    ///   W  WALLS: real PhysX contacts - a nose into walls covering the LEFT
    ///      35%, 60% and 75% of the front, the RIGHT 35%, and the whole of it;
    ///      a right flank into a 200 m barrier whose bounding box is centred
    ///      100 m AHEAD of the car; a tail into a full wall - located FRONT
    ///      LEFT, FRONT RIGHT, FRONT CENTRE (no alignment), SIDE RIGHT and REAR
    ///      CENTRE; the hit rolled at once; DamageScore and WorstHit as the
    ///      switch-off run reads them.
    ///   S  A car swapped in mid-drive (the bench's DRIVE, SwapCar) starts with
    ///      no crash on it, on its own - after a planted crash on the old one.
    ///
    /// tools\crash-play-check.ps1 -> PSXRacing_crash_play_check.txt. Exits
    /// itself (no -quit: it enters play mode), also when a run throws (the
    /// runs are guarded). Watched by default - hidden batch play mode crashes
    /// in RenderTexture::Create on the owner's PC.
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
            // R1a: the located model's check while it is live; R0a's table
            // check with PSX_FAULT_LOCATED=0.
            if (System.Environment.GetEnvironmentVariable("PSX_INJURY_CHECK") == "1")   // R1c-A
                new GameObject("InjuryPlayCheckRunner").AddComponent<InjuryPlayCheckRunner>();
            else if (DamageModel.Live) new GameObject("DamagePlayCheckRunner").AddComponent<DamagePlayCheckRunner>();
            else new GameObject("CrashPlayCheckRunner").AddComponent<CrashPlayCheckRunner>();
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
        RaceHandoffApplier applier;
        bool drive, cruise, done;
        float throttle, brake, cruiseMps;
        float step, surfaceY;
        float zNext = -14000f;
        const float XLane = -600f;
        readonly List<GameObject> props = new List<GameObject>();

        /// <summary>The dice every planted hit is forced to: bumper, radiator
        /// and alignment all come up.</summary>
        static CrashStopGap.Draws Forced() => new CrashStopGap.Draws
            { paint = 0.5f, bumper = 0.1f, body = 0.5f, radiator = 0.1f, radAmt = 0.5f, align = 0.1f };

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
            yield return Guarded(Body());
            Done();
        }

        /// <summary>Run a coroutine (and the ones it yields) so that a throw is
        /// a FAIL line and the check still ends - a coroutine that throws just
        /// stops, and an editor left in play mode holds the sandbox.</summary>
        IEnumerator Guarded(IEnumerator body)
        {
            while (true)
            {
                object cur = null;
                bool more;
                try
                {
                    more = body.MoveNext();
                    if (more) cur = body.Current;
                }
                catch (System.Exception e)
                {
                    more = false;
                    CrashPlayCheck.Check(false, "a run THREW: " + e.GetType().Name + " - " + e.Message);
                    Debug.LogException(e);
                }
                if (!more) yield break;
                if (cur is IEnumerator nested) yield return Guarded(nested);
                else yield return cur;
            }
        }

        IEnumerator Body()
        {
            for (int i = 0; i < 25; i++) yield return null;
            step = Time.fixedDeltaTime;

            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            applier = DebugCarOps.LiveApplier();
            CrashPlayCheck.Check(car != null && applier != null, "there is a player car and the scene's applier");
            if (car == null || applier == null) yield break;
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
                // S: the car being swapped OUT of has just crashed.
                if (!first) PlantCrash();
                yield return Become(spec, expectClean: !first);
                string who = spec.name;
                float wb = car.wheelbase;
                CrashPlayCheck.Note(who + ": wheelbase " + wb.ToString("0.000") + " m");
                foreach (float assist in new[] { 0f, CarController.DefaultCountersteerAssist, 1f })
                {
                    float y0 = 0f;
                    yield return DriftRun(0f, assist, v => y0 = v);
                    var got = new Dictionary<float, float>();
                    foreach (float pull in new[] { CrashStopGap.PullDegMinor, -CrashStopGap.PullDegMinor,
                                                   CrashStopGap.PullDegSevere, -CrashStopGap.PullDegSevere })
                    {
                        float y = 0f;
                        yield return DriftRun(pull, assist, v => y = v);
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
                if (first)
                {
                    yield return LiveRun(who, spec);
                    yield return CarriedRun(who, spec, +1, "on the OTHER side");
                    yield return CarriedRun(who, spec, -1, "on the SAME side");
                    yield return WallRuns(who);
                }
                first = false;
            }
        }

        /// <summary>A crash on the car being driven, through the responder's
        /// own record rule: a 12 m/s nose-in on the left, every die forced.</summary>
        void PlantCrash()
        {
            if (resp == null) return;
            if (resp.Ledger == null) CrashStopGap.Arm(car);
            if (resp.Ledger == null) return;
            resp.ResetDamage();
            resp.Ledger.DrawOverride = Forced;
            resp.RecordHit(12f, CrashStopGap.ZoneFront, -1, new Vector3(-0.5f, 0f, 2f), "planted", Time.time);
            var temp = car.GetComponent<EngineTemp>();
            CrashPlayCheck.Note("planted on the old car before the swap: pull " + car.hitSteerPull.ToString("+0.000;-0.000") +
                                " deg, radiator hit " + (temp != null ? temp.radiatorHit.ToString("0") : "-") +
                                ", weep " + (temp != null ? temp.radiatorWeep.ToString("0.00") : "-"));
        }

        IEnumerator Become(CarSpec spec, bool expectClean)
        {
            RaceHandoff.CarSpecId = spec.id;
            RaceHandoff.CarPaintSkin = null;
            RaceHandoff.UpPower = RaceHandoff.UpWeight = RaceHandoff.UpBrakes = RaceHandoff.UpSuspension = RaceHandoff.UpTires = 0;
            RaceHandoff.Supercharged = false; RaceHandoff.Welded = false; RaceHandoff.Setup = null;
            RaceHandoff.StartFuelPct = 100f;
            var old = resp != null ? resp.Ledger : null;
            applier.SwapCar();
            for (int f = 0; f < 5; f++) yield return null;
            car.manualMode = false;
            if (expectClean)
            {
                // NOTHING zeroed by hand: SwapCar -> CrashStopGap.StartClean.
                var temp = car.GetComponent<EngineTemp>();
                CrashPlayCheck.Check(car.hitSteerPull == 0f && temp != null && temp.radiatorHit == 0f && temp.radiatorWeep == 0f &&
                                     resp.Ledger != null && resp.Ledger != old && resp.Ledger.Rolls == 0 && resp.WorstHit == 0f,
                    spec.name + ": S swapped in after the old car crashed, it starts clean on its own - no pull, no cracked core, " +
                    "no weep, its own ledger and its own hardest hit",
                    "pull " + car.hitSteerPull.ToString("+0.000;-0.000;0") + ", radiator hit " +
                    (temp != null ? temp.radiatorHit.ToString("0") : "-") + ", worst hit " + resp.WorstHit.ToString("0.0"));
            }
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
        /// Settle hands-off at 100 km/h for a second, then read how far the car
        /// has gone sideways from its line once it has covered 100 m along it.
        /// Calibration (<paramref name="asIs"/> false): every pull off, then
        /// <paramref name="pullDeg"/> put on as a crash pull after the settle.
        /// <paramref name="asIs"/>: the car exactly as the applier left it -
        /// the next drive's banked pull - nothing written.
        /// </summary>
        IEnumerator DriftRun(float pullDeg, float assist, System.Action<float> result, bool asIs = false)
        {
            car.countersteerAssist = assist;
            if (!asIs) { car.hitSteerPull = 0f; car.faultSteerPullDeg = 0f; car.faultSteerPull = 0f; }
            yield return Place(Kmh100);
            cruise = true; cruiseMps = Kmh100; drive = true;
            for (float t = 0f; t < 1.0f; t += step) yield return new WaitForFixedUpdate();
            Vector3 p0 = car.transform.position;
            Vector3 f0 = car.transform.forward; f0.y = 0f; f0.Normalize();
            Vector3 r0 = Vector3.Cross(Vector3.up, f0);
            if (!asIs) car.hitSteerPull = pullDeg;
            yield return Along(p0, f0);
            result(Vector3.Dot(car.transform.position - p0, r0));
            drive = false;
            if (!asIs) { car.hitSteerPull = 0f; car.faultSteerPullDeg = 0f; }
        }

        float alongT;
        IEnumerator Along(Vector3 p0, Vector3 f0)
        {
            float along = 0f;
            alongT = 0f;
            while (along < 100f && alongT < 10f)
            {
                yield return new WaitForFixedUpdate();
                alongT += step;
                along = Vector3.Dot(car.transform.position - p0, f0);
            }
        }

        // ---- the next drive, built the real way ----------------------------
        LifeState scratch;
        OwnedCar owned;

        /// <summary>A scratch career and a scratch car of this spec, its
        /// faults set to <paramref name="rows"/>, and the request and the car
        /// under the player made from it (FillCarRequestFor + ReapplyCar) - a
        /// drive's start, minus the scene load.</summary>
        void Request(CarSpec spec, params CarFault[] rows)
        {
            if (scratch == null) scratch = LifeRules.SeedNewGame("CRASHPLAY", 25, LifeRules.DefaultJobIndex);
            if (owned == null || owned.specId != spec.id)
            {
                owned = CarMarket.MakeOwnedCar(scratch, spec, 80, 40000f, spec.price);
                scratch.activeCar = owned.id;
            }
            owned.faults.Clear();
            foreach (var r in rows) if (r != null) owned.faults.Add(r);
            ApplyRequest(spec);
        }

        void ApplyRequest(CarSpec spec)
        {
            LifeHomeScreen.FillCarRequestFor(scratch, owned);
            RaceHandoff.CarSpecId = spec.id;
            FreshRun();                         // a new drive: no crash on it yet
            applier.ReapplyCar();
        }

        /// <summary>A run that stands for a new drive starts the way one does:
        /// its own hardest hit and its own ledger, no crash pull, no cracked
        /// core and no weep (CrashStopGap.StartClean - what a swapped-in car
        /// gets), every die forced. Without it the runs' radiator hits piled
        /// up on the one car the check drives (the game arms once per drive).</summary>
        void FreshRun()
        {
            resp.ResetDamage();
            if (resp.Ledger == null) CrashStopGap.Arm(car);
            CrashStopGap.StartClean(car);
            if (resp.Ledger != null) resp.Ledger.DrawOverride = Forced;
        }

        CarFault Alignment(int side)
        {
            var a = FaultCatalog.MakeById(FaultCatalog.AlignmentId, FaultCatalog.FamilyOf(owned));
            if (a != null) { a.severity = 1f; a.pullDir = side; }
            return a;
        }

        /// <summary>The exit stamp, the apply-back onto the scratch car and the
        /// next drive's request: what a drive's crash comes home as.</summary>
        void BankAndNextDrive(CarSpec spec)
        {
            CrashStopGap.StampHandoff(resp);
            CrashBank.ApplyOutcome(owned, CrashBank.FromHandoff());
            RaceHandoff.ClearResult();
            ApplyRequest(spec);
        }

        // ---- L: the hit through the ledger, at speed ---------------------
        IEnumerator LiveRun(string who, CarSpec spec)
        {
            Request(spec);
            car.countersteerAssist = CarController.DefaultCountersteerAssist;
            float y0 = 0f;
            yield return DriftRun(0f, CarController.DefaultCountersteerAssist, v => y0 = v, asIs: true);

            var L = resp.Ledger;
            L.DrawOverride = () => new CrashStopGap.Draws { paint = 0.5f, bumper = 0.1f, body = 0.5f, radiator = 0.9f, radAmt = 0.5f, align = 0.1f };
            yield return Place(Kmh100);
            cruise = true; cruiseMps = Kmh100; drive = true;
            for (float t = 0f; t < 1.0f; t += step) yield return new WaitForFixedUpdate();
            Vector3 p0 = car.transform.position;
            Vector3 f0 = car.transform.forward; f0.y = 0f; f0.Normalize();
            Vector3 r0 = Vector3.Cross(Vector3.up, f0);
            // A 12 m/s nose hit on the LEFT (minor band), through the
            // responder's own record rule, as OnCollisionEnter hands it over.
            float before = car.hitSteerPull;
            resp.ResetDamage();
            resp.RecordHit(12f, CrashStopGap.ZoneFront, -1, new Vector3(-0.5f, 0f, 2f), "live", Time.time);
            float sameFrame = car.hitSteerPull;
            CrashPlayCheck.Check(before == 0f && Mathf.Approximately(sameFrame, -CrashStopGap.PullDegMinor),
                who + ": L the pull is on the car the same frame as the hit, to the struck (left) side",
                before.ToString("0.000") + " -> " + sameFrame.ToString("0.000") + " deg");
            yield return Along(p0, f0);
            float y = Vector3.Dot(car.transform.position - p0, r0) - y0;
            CrashPlayCheck.Check(y <= -0.5f && y >= -1.5f,
                who + ": L and the car is drifting left within the minor band 100 m later, inside the leg",
                y.ToString("0.00") + " m in " + alongT.ToString("0.0") + " s");
            drive = false;
            CrashStopGap.StampHandoff(resp);
            CrashPlayCheck.Check(RaceHandoff.CrashRolled && RaceHandoff.CrashAlignSide == -1 && RaceHandoff.CrashAlignSeverity == 1 &&
                                 RaceHandoff.CrashBumperId == "bumper_crack",
                who + ": L the exit stamp hands the apply-back exactly that (no second roll there)", CrashBank.FromHandoff());

            // THE NEXT DRIVE, the real way: banked, requested, applied.
            BankAndNextDrive(spec);
            float banked = car.faultSteerPullDeg;
            float yp = 0f;
            yield return DriftRun(0f, CarController.DefaultCountersteerAssist, v => yp = v, asIs: true);
            yp -= y0;
            CrashPlayCheck.Check(Mathf.Approximately(banked, -CrashStopGap.PullDegMinor) && car.hitSteerPull == 0f &&
                                 Mathf.Abs(yp - y) < 0.25f,
                who + ": L next drive (stamp -> CrashBank -> FillCarRequestFor -> ReapplyCar, no pull written by hand) the banked " +
                "alignment drifts the same as the live pull",
                "banked " + banked.ToString("+0.000;-0.000") + " deg; " + yp.ToString("0.00") + " vs " + y.ToString("0.00") + " m");
            Request(spec);   // a sound car again
        }

        /// <summary>
        /// The car ALREADY carries a minor alignment on <paramref name="carriedSide"/>
        /// (+1 right, -1 left) and takes a minor LEFT nose hit. The pull it
        /// drives with after the hit (CombinePull: a tie goes to the hit, so
        /// LEFT either way - never the sum, which was dead straight or doubled)
        /// and the next drive's banked pull drift the same. The hit lands at
        /// the start of the run so both are measured from a steady line.
        /// </summary>
        IEnumerator CarriedRun(string who, CarSpec spec, int carriedSide, string what)
        {
            car.countersteerAssist = CarController.DefaultCountersteerAssist;
            Request(spec);
            float y0 = 0f;
            yield return DriftRun(0f, CarController.DefaultCountersteerAssist, v => y0 = v, asIs: true);

            Request(spec, Alignment(carriedSide));
            float carried = car.faultSteerPullDeg;
            resp.ResetDamage();
            resp.Ledger.DrawOverride = Forced;
            yield return Place(Kmh100);
            resp.RecordHit(12f, CrashStopGap.ZoneFront, -1, new Vector3(-0.5f, 0f, 2f), "carried", Time.time);
            float live = CrashStopGap.CombinePull(car.faultSteerPullDeg, car.hitSteerPull);
            cruise = true; cruiseMps = Kmh100; drive = true;
            for (float t = 0f; t < 1.0f; t += step) yield return new WaitForFixedUpdate();
            Vector3 p0 = car.transform.position;
            Vector3 f0 = car.transform.forward; f0.y = 0f; f0.Normalize();
            Vector3 r0 = Vector3.Cross(Vector3.up, f0);
            yield return Along(p0, f0);
            float yLive = Vector3.Dot(car.transform.position - p0, r0) - y0;
            drive = false;

            BankAndNextDrive(spec);
            float next = car.faultSteerPullDeg;
            float yNext = 0f;
            yield return DriftRun(0f, CarController.DefaultCountersteerAssist, v => yNext = v, asIs: true);
            yNext -= y0;
            CrashPlayCheck.Check(Mathf.Approximately(live, -CrashStopGap.PullDegMinor) && Mathf.Approximately(next, live) &&
                                 yLive <= -0.5f && yLive >= -1.5f && Mathf.Abs(yNext - yLive) < 0.25f,
                who + ": L carrying a minor alignment " + what + " (" + carried.ToString("+0.000;-0.000") + " deg), a minor LEFT hit " +
                "pulls minor LEFT, toward the hit, and the next drive pulls the same (the old sum: " +
                (carried - CrashStopGap.PullDegMinor).ToString("+0.000;-0.000;0") + " deg)",
                "live " + live.ToString("+0.000;-0.000") + " deg " + yLive.ToString("0.00") + " m; next " +
                next.ToString("+0.000;-0.000") + " deg " + yNext.ToString("0.00") + " m");
            Request(spec);   // a sound car again
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

        IEnumerator Hit(System.Action setVelocity, float seconds)
        {
            drive = true; cruise = false; throttle = 0f; brake = 0f;
            setVelocity();
            for (float t = 0f; t < seconds; t += step) yield return new WaitForFixedUpdate();
            drive = false;
        }

        string Where() =>
            CrashStopGap.ZoneName(resp.WorstHitZone) + "/" + CrashStopGap.SideName(resp.WorstHitSide) + " at " +
            resp.WorstHit.ToString("0.0") + " m/s, patch centre x " + resp.WorstHitLocal.x.ToString("+0.00;-0.00") +
            " z " + resp.WorstHitLocal.z.ToString("+0.00;-0.00") + " (impulse-weighted x " +
            resp.WorstHitWeighted.x.ToString("+0.00;-0.00") + ")";

        /// <summary>A nose into a wall whose RIGHT edge sits at
        /// <paramref name="edgeFrac"/> of the half-width from the centreline
        /// (negative = left of it), at 12.5 m/s, every die forced.</summary>
        IEnumerator NoseWall(float edgeFrac, float width, string name)
        {
            float hl = BodyHalfLength(), hw = BodyHalfWidth();
            FreshRun();
            yield return Place(12.5f);
            Vector3 c0 = car.transform.position;
            float edge = XLane + edgeFrac * hw;
            var w = Wall(new Vector3(edge - width * 0.5f, surfaceY + 1.2f, c0.z + hl + 5f), new Vector3(width, 2.4f, 1f), name);
            yield return Hit(() => car.Body.linearVelocity = car.transform.forward * 12.5f, 1.5f);
            Object.Destroy(w);
            yield return null;
        }

        IEnumerator WallRuns(string who)
        {
            float hl = BodyHalfLength(), hw = BodyHalfWidth();

            // W1: nose into a wall over the LEFT 35% of the front (its edge 0.3
            // of the half-width left of the centreline), with the switch off and
            // then on - the same hit read twice.
            float dsOn = 0f, whOn = 0f, dsOff = 0f, whOff = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                bool on = pass == 1;
                CrashStopGap.On = on;
                if (on) FreshRun();
                else { resp.ResetDamage(); resp.Ledger = null; car.hitSteerPull = 0f; }
                yield return Place(12.5f);
                Vector3 c0 = car.transform.position;
                float edge = 0.3f * hw;
                var w = Wall(new Vector3(XLane - edge - 5f, surfaceY + 1.2f, c0.z + hl + 5f), new Vector3(10f, 2.4f, 1f), "Left35Wall");
                yield return Hit(() => car.Body.linearVelocity = car.transform.forward * 12.5f, 1.5f);
                if (on) { dsOn = resp.DamageScore; whOn = resp.WorstHit; } else { dsOff = resp.DamageScore; whOff = resp.WorstHit; }
                if (on)
                {
                    var t = resp.Ledger != null ? resp.Ledger.Total : new CrashStopGap.Outcome();
                    CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneFront && resp.WorstHitSide == -1,
                        who + ": W a nose into a wall over the LEFT 35% of the front is FRONT LEFT", Where());
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

            // W1b: the mirror - a wall over the RIGHT 35% (placed by its left edge).
            FreshRun();
            yield return Place(12.5f);
            Vector3 cR = car.transform.position;
            var wr = Wall(new Vector3(XLane + 0.3f * hw + 5f, surfaceY + 1.2f, cR.z + hl + 5f), new Vector3(10f, 2.4f, 1f), "Right35Wall");
            yield return Hit(() => car.Body.linearVelocity = car.transform.forward * 12.5f, 1.5f);
            var tr = resp.Ledger != null ? resp.Ledger.Total : new CrashStopGap.Outcome();
            CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneFront && resp.WorstHitSide == 1 &&
                                 (resp.WorstHit < CrashStopGap.HeavyMps || (tr.alignSide == 1 && car.hitSteerPull > 0f)),
                who + ": W a nose into a wall over the RIGHT 35% of the front is FRONT RIGHT, and pulls right",
                Where() + "; " + tr + "; pull " + car.hitSteerPull.ToString("+0.000;-0.000") + " deg");
            Object.Destroy(wr);
            yield return null;

            // W1c/W1d: walls over MORE than half the nose, from the left - the
            // impulse piles onto the wall's inner edge, right of the centreline;
            // the patch is still mostly left.
            foreach (var (frac, pct) in new[] { (0.2f, "60%"), (0.5f, "75%") })
            {
                yield return NoseWall(frac, 10f, "Left" + pct + "Wall");
                var t = resp.Ledger != null ? resp.Ledger.Total : new CrashStopGap.Outcome();
                CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneFront && resp.WorstHitSide == -1 &&
                                     (!t.Has(CrashStopGap.Cls.Alignment) || (t.alignSide == -1 && car.hitSteerPull < 0f)),
                    who + ": W a nose into a wall over the LEFT " + pct + " of the front is still FRONT LEFT, and pulls left",
                    Where() + "; " + t + "; pull " + car.hitSteerPull.ToString("+0.000;-0.000;0") + " deg");
            }

            // W1e: a wall across the WHOLE nose, square: centred - no side, so
            // no alignment, whatever the dice (they are forced to give one).
            yield return NoseWall(5f, 10f, "FullWall");
            {
                var t = resp.Ledger != null ? resp.Ledger.Total : new CrashStopGap.Outcome();
                CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneFront && resp.WorstHitSide == 0 &&
                                     !t.Has(CrashStopGap.Cls.Alignment) && car.hitSteerPull == 0f &&
                                     resp.WorstHit >= CrashStopGap.HeavyMps,
                    who + ": W a square nose into a wall across the WHOLE front is FRONT CENTRE: no struck side, NO alignment " +
                    "at " + CrashStopGap.HeavyMps + "+ m/s with the alignment die forced",
                    Where() + "; " + t + "; pull " + car.hitSteerPull.ToString("+0.000;-0.000;0") + " deg");
            }

            // W2: a right flank into a 200 m barrier whose bounds sit AHEAD of the car.
            FreshRun();
            yield return Place(0f);
            Vector3 c1 = car.transform.position;
            var barrier = Wall(new Vector3(XLane + hw + 0.5f + 0.5f, surfaceY + 0.6f, c1.z + 100f - hl), new Vector3(1f, 1.2f, 200f), "LongBarrier");
            Vector3 bc = barrier.GetComponent<BoxCollider>().bounds.center - c1;
            yield return Hit(() => car.Body.linearVelocity = car.transform.forward * 3f + car.transform.right * 8f, 1.2f);
            CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneSide && resp.WorstHitSide == 1,
                who + ": W a right flank into a 200 m barrier is SIDE RIGHT, though the barrier's bounds are centred " +
                bc.z.ToString("0") + " m ahead",
                Where() + "; " + (resp.Ledger != null ? resp.Ledger.Total.ToString() : "-") +
                "; pull " + car.hitSteerPull.ToString("+0.000;-0.000") + " deg");
            Object.Destroy(barrier);
            yield return null;

            // W3: the tail, square into a full wall at 11 m/s: REAR, centred, no alignment.
            FreshRun();
            yield return Place(0f);
            Vector3 c2 = car.transform.position;
            var back = Wall(new Vector3(XLane, surfaceY + 1.2f, c2.z - hl - 2f), new Vector3(10f, 2.4f, 1f), "RearWall");
            yield return Hit(() => car.Body.linearVelocity = -car.transform.forward * 11f, 1.5f);
            {
                var t = resp.Ledger != null ? resp.Ledger.Total : new CrashStopGap.Outcome();
                CrashPlayCheck.Check(resp.WorstHitZone == CrashStopGap.ZoneRear && resp.WorstHitSide == 0 &&
                                     !t.Has(CrashStopGap.Cls.Alignment) && car.hitSteerPull == 0f,
                    who + ": W a tail square into a wall across the whole back is REAR CENTRE: no side, no alignment",
                    Where() + "; " + t);
            }
            Object.Destroy(back);
            FreshRun();
            yield return null;
        }

        void Done()
        {
            if (done) return;
            done = true;
            drive = false;
            CrashStopGap.On = System.Environment.GetEnvironmentVariable("PSX_FAULT_CRASH") != "0";
            foreach (var p in props) if (p != null) Destroy(p);
            RaceHandoff.ClearAll();
            CrashPlayCheck.Finish();
            EditorApplication.Exit(CrashPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
