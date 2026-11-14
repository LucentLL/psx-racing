using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R1c-A in the RUNNING game (CrashPlayCheck.Run with PSX_INJURY_CHECK=1;
    /// report PSXRacing_crash_play_check.txt): a career car, belt on, HEALTH
    /// 100, real PhysX contacts on the flat strip -
    ///   a 25 km/h wall: nothing or a concussion;
    ///   a 40 km/h wall: the concussion, shot as the player sees it;
    ///   a 65 km/h wall: hospital, and the drive ends;
    ///   a 130 km/h tree in a DEBUG career: death; the files folded, the
    ///   career is dead (the obituary).
    /// </summary>
    public class InjuryPlayCheckRunner : MonoBehaviour
    {
        CarController car;
        CollisionResponder resp;
        Rigidbody rb;
        float surfaceY;
        float zNext = -14000f;
        const float XLane = -600f;
        bool hold;
        float holdMps;
        readonly List<GameObject> props = new List<GameObject>();
        int ended = -1;

        static void Check(bool ok, string what, object got = null) => CrashPlayCheck.Check(ok, what, got);
        static void Note(string what) => CrashPlayCheck.Note(what);
        internal static string ShotDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "r1ca");

        void FixedUpdate()
        {
            if (car == null || rb == null) return;
            float vf = Vector3.Dot(rb.linearVelocity, car.transform.forward);
            car.throttleInput = hold ? Mathf.Clamp01(0.35f + (holdMps - vf) * 0.6f) : 0f;
            car.brakeInput = 0f;
            car.steerInput = 0f;
            car.handbrakeInput = false;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            InjuryFx.EndDriveOverride = o => ended = o;
            yield return Guarded(Body());
            InjuryFx.EndDriveOverride = null;
            if (InjuryFx.Active != null) Destroy(InjuryFx.Active.gameObject);
            foreach (var p in props) if (p != null) Destroy(p);
            RaceHandoff.ClearAll();
            CrashPlayCheck.Finish();
            EditorApplication.Exit(CrashPlayCheck.failures == 0 ? 0 : 1);
        }

        IEnumerator Guarded(IEnumerator body)
        {
            while (true)
            {
                object cur = null;
                bool more;
                try { more = body.MoveNext(); if (more) cur = body.Current; }
                catch (System.Exception e)
                {
                    more = false;
                    Check(false, "a run THREW: " + e.GetType().Name + " - " + e.Message);
                    Debug.LogException(e);
                }
                if (!more) yield break;
                if (cur is IEnumerator nested) yield return Guarded(nested);
                else yield return cur;
            }
        }

        DamageLedger Fresh()
        {
            if (resp.Damage != null) resp.Damage.Flush();
            resp.Damage = null;
            resp.Ledger = null;
            resp.ResetDamage();
            car.faultAccelMult = 1f;
            CrashStopGap.Arm(car);
            return resp.Damage;
        }

        IEnumerator Fixed(float seconds)
        {
            int n = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        Vector3 NextStart()
        {
            var at = new Vector3(XLane, surfaceY + 0.6f, zNext);
            zNext += 400f;
            return at;
        }

        IEnumerator Body()
        {
            for (int i = 0; i < 25; i++) yield return null;
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            Check(car != null, "there is a player car");
            if (car == null) yield break;
            resp = car.GetComponent<CollisionResponder>();
            rb = car.GetComponent<Rigidbody>();
            Check(DamageModel.Live && CrashFates.InjuryOn, "the located model and PSX_INJURY are on");
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;
            var strip = new GameObject("InjuryStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            strip.AddComponent<BoxCollider>().size = new Vector3(3000f, 2f, 30000f);
            surfaceY = 4001f;
            props.Add(strip);

            var s = LifeSimManager.State;
            var spec = CarCatalog.Get(RaceHandoff.CarSpecId) ?? CarCatalog.All[0];
            var owned = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            owned.id = "r1ca#play";
            owned.faults.Clear();
            s.activeCar = owned.id;
            RaceHandoff.CarId = owned.id;
            s.debugMode = false;
            s.health = 100f;
            s.fate = new CrashFate();
            foreach (var p in DamageBank.Files()) File.Delete(p);
            var occ = CrashFates.DriveOccupant();
            Note("car " + spec.name + ", " + rb.mass.ToString("0") + " kg, bags fitted " + occ.bags + ", belted " + occ.belted +
                 ", cage " + occ.cage + ", HEALTH " + s.health.ToString("0"));
            Directory.CreateDirectory(ShotDir);

            yield return Run(25f, false, "25 km/h square into a wall: nothing or a concussion",
                             o => o == CrashFates.None || o == CrashFates.Concussion, null);
            yield return Run(40f, false, "40 km/h square into a wall (the blur)",
                             o => o == CrashFates.Concussion, Path.Combine(ShotDir, "r1ca_concussion_blur.png"));
            // fix pass 1 (owner 11-14): the same concussion in a car the FAIL stopped ends the drive
            yield return Run(40f, false, "40 km/h concussion in a car that cannot go on (a FAIL put on the ledger after the hit)",
                             o => o == CrashFates.Concussion, null, forceFail: true);
            // fix pass 1: a concussion in a car that still goes (the belt off at 25 km/h: no FAIL) keeps the drive
            s.fate.beltOff = true;
            yield return Run(25f, false, "25 km/h square into a wall with the belt OFF: a concussion, and the car still goes",
                             o => o == CrashFates.Concussion, null);
            s.fate.beltOff = false;
            yield return Run(65f, false, "65 km/h square into a wall: hospital, and the drive ends",
                             o => o == CrashFates.Hospital, null);
            s.debugMode = true;
            yield return Run(130f, true, "130 km/h into a tree in a DEBUG career: death",
                             o => o == CrashFates.Death, null);

            int folded = CrashStopGap.FoldDamage(car);
            var f = s.fate;
            Check(f.pending == CrashFates.Death, "the drive's files folded into the career (" + folded + "): the worst is the death",
                  CrashFates.OutcomeName(f.pending) + " score " + f.pendingScore.ToString("0.0"));
            Check(CrashFates.Process(s) && CrashFates.Dead(s) && CrashFates.PageDue(s) && f.obituary.Length > 40,
                  "the career is over: the obituary is the page", f.obituary);
            Check(LifeSimManager.ReadOnly || f.deathLocked || !LifeSimManager.Persists, "and the career is locked");
        }

        IEnumerator Run(float kmh, bool tree, string what, System.Func<int, bool> want, string shot, bool forceFail = false)
        {
            if (InjuryFx.Active != null) Destroy(InjuryFx.Active.gameObject);
            yield return null;
            AudioListener.volume = 1f;
            ended = -1;
            var L = Fresh();
            float v = kmh / 3.6f;
            var at = NextStart();
            var go = new GameObject(tree ? "Tree_play" + kmh : "Wall" + kmh);
            if (tree)
            {
                go.transform.position = at + new Vector3(0f, 3.4f, 40f);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.3f; cap.height = 8f; cap.direction = 1;
            }
            else
            {
                go.transform.position = at + new Vector3(0f, 0.6f, 24f);
                go.AddComponent<BoxCollider>().size = new Vector3(6f, 2.4f, 1f);
            }
            props.Add(go);
            car.TeleportTo(at, Quaternion.identity);
            yield return Fixed(0.2f);
            car.SetRolling(v);
            hold = true; holdMps = v;
            float t = 0f;
            while (t < 8f && (L == null || L.incidents.Count == 0)) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            hold = false;
            if (forceFail && L != null && L.incidents.Count > 0 && L.Failed == DamageRules.FailNone)
            {
                L.Failed = DamageRules.FailHit;
                L.FailHitId = L.incidents[0].hitId;
                car.faultAccelMult = 0f;
            }
            yield return Fixed(0.6f);
            if (L == null) { Check(false, what + ": no ledger armed"); yield break; }
            var worst = L.incidents.OrderByDescending(i => i.injScore).FirstOrDefault();
            int got = worst != null ? worst.injury : CrashFates.None;
            string desc = worst == null ? "no incident" :
                DamageModel.KindName(worst.kind) + " " + DamageModel.ZoneName(worst.zone) + " dv " + worst.dv.ToString("0.0") +
                (worst.narrow ? " narrow" : "") + ", FAIL " + DamageRules.FailName(worst.fail) + ", injury score " +
                worst.injScore.ToString("0.0") + " = " + CrashFates.OutcomeName(got);
            Check(want(got), what, desc);
            Check((got >= CrashFates.Concussion) == (InjuryFx.Active != null), "  the picture goes from a concussion up (and only then)",
                  InjuryFx.Active != null ? "fx " + CrashFates.OutcomeName(InjuryFx.Active.outcome) : "no fx");
            if (InjuryFx.Active == null) yield break;
            var fx = InjuryFx.Active;
            if (shot != null)
            {
                while (fx != null && fx.T < 1.6f) yield return null;
                bool ok = fx != null && fx.ShootPng(shot, 1024, 768);
                Check(ok && File.Exists(shot), "  the concussion as the player sees it, 1.6 s in: " + shot);
            }
            float hold0 = fx != null ? fx.HoldSeconds : 4f;
            bool goesOn = !InjuryFx.EndsDrive(got, InjuryFx.CarCannotGo(car));
            int came = InjuryFx.CameRound;
            float wait = 0f;
            while (ended < 0 && InjuryFx.Active != null && wait < 12f) { wait += Time.unscaledDeltaTime; yield return null; }
            if (!goesOn)
            {
                Check(ended == got, "  the drive ends after the black-out (" + hold0.ToString("0") + " s)" +
                      (InjuryFx.CarCannotGo(car) ? ": the car cannot go on" : ""), "ended " + CrashFates.OutcomeName(ended));
                yield break;
            }
            // fix pass 1: the driver comes round, the drive goes on
            Check(ended < 0 && InjuryFx.Active == null && InjuryFx.CameRound == came + 1,
                  "  a concussion in a car that still goes: the dark lifts after " + hold0.ToString("0") + " s + " +
                  InjuryFx.WakeSeconds.ToString("0.0") + " s and the drive goes on (no tow)",
                  "ended " + (ended < 0 ? "no" : CrashFates.OutcomeName(ended)) + ", fx " + (InjuryFx.Active != null ? "still up" : "gone") +
                  " after " + wait.ToString("0.0") + " s");
            Check(car.faultAccelMult > 0.5f && AudioListener.volume > 0.99f, "  the controls and the sound come back",
                  "accel x" + car.faultAccelMult.ToString("0.00") + ", volume " + AudioListener.volume.ToString("0.00"));
            car.TeleportTo(NextStart(), Quaternion.identity);
            yield return Fixed(0.2f);
            hold = true; holdMps = 12f;
            yield return Fixed(2.5f);
            hold = false;
            float vf = Vector3.Dot(rb.linearVelocity, car.transform.forward);
            Check(vf > 4f, "  and the car drives on under its own power", vf.ToString("0.0") + " m/s after 2.5 s of throttle");
        }
    }

    /// <summary>R1c-A: the home screen's crash pages at the narrowest aspect
    /// (4:3), with the CLIP report (batch job: InjuryPlayCheck.ShootPages).</summary>
    public static class InjuryPlayCheck
    {
        static DamageBank.IncidentRec Rec(int hit, float dv, int fail, int inj, float score, bool narrow, string what) =>
            new DamageBank.IncidentRec { hitId = hit, kind = DamageModel.KHit, zone = DamageModel.ZFront, corner = -1, band = 5, dv = dv,
                                         fail = fail, inj = inj, injScore = score, narrow = narrow, what = what };

        public static void ShootPages()
        {
            string outDir = InjuryPlayCheckRunner.ShotDir;
            Directory.CreateDirectory(outDir);
            var sizes = new[] { ("tablet_4x3", 1024, 768) };
            LifeSimManager.UseScratchSave("r1ca.pages");
            try
            {
                foreach (var page in new[] { "hospital", "writeoff", "obituary" })
                {
                    LifeSimManager.DeleteSave();
                    LifeSimManager.StartNewGame("JESSE CARVER", 24, LifeRules.DefaultJobIndex);
                    var s = LifeSimManager.State;
                    s.debugMode = false;
                    if (s.cars.Count == 0) LifeRules.SeedFallbackCar(s);
                    var car = s.ActiveCar ?? s.cars[0];
                    s.money = 2400;
                    s.health = 62f;
                    RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
                    if (page == "hospital")
                        DamageBank.ApplyIncident(s, car, Rec(9101, 18.1f, DamageRules.FailNone, CrashFates.Hospital, 21f, false, "WallColl"));
                    else if (page == "writeoff")
                        CrashFates.AfterIncident(s, car, Rec(9102, 18.1f, DamageRules.FailHit, CrashFates.None, 0f, false, "WallColl"),
                                                 Mathf.Max(400, CarMarket.CarValue(car) / 4));
                    else
                        DamageBank.ApplyIncident(s, car, Rec(9103, 36f, DamageRules.FailHit, CrashFates.Death, 48f, true, "Tree_07"));
                    LifeSimManager.Save();
                    LifeHomePreview.Shoot(outDir, "r1ca_" + page, tab: "main", sizes: sizes);
                    Debug.Log("[R1cA] page " + page + " shot; due " + CrashFates.PageDue(LifeSimManager.State));
                }
            }
            finally
            {
                LifeSimManager.DeleteSave();
                LifeSimManager.UseCareerSave();
                RaceHandoff.ClearAll();
            }
        }
    }
}
