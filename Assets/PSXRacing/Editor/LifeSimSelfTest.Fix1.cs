using System.Linq;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Fix pass 1 (owner reports on PC35, 2026-11-14): A a concussion keeps the
    /// drive in a car that still goes; B the debug bench reads and repairs the
    /// drive under way (its ledger, its pending file, the live car); E
    /// panel_damage priced by stage.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void RunFix1Tests()
        {
            bool carry = InjuryFx.CarryOnOn, live = DebugCarOps.LiveOn, panel = DamageHomes.PanelStageOn,
                 inj = CrashFates.InjuryOn, stay = CrashFates.FailStaysOn, ff = CrashFates.FastForwardOn;
            try
            {
                InjuryFx.CarryOnOn = DebugCarOps.LiveOn = DamageHomes.PanelStageOn = CrashFates.InjuryOn = CrashFates.FailStaysOn =
                    CrashFates.FastForwardOn = true;
                Guard(nameof(TestConcussionCarriesOn), TestConcussionCarriesOn);
                Guard(nameof(TestHospitalFastForward), TestHospitalFastForward);
                Guard(nameof(TestBenchLive), TestBenchLive);
                Guard(nameof(TestPanelByStage), TestPanelByStage);
            }
            finally
            {
                InjuryFx.CarryOnOn = carry; DebugCarOps.LiveOn = live; DamageHomes.PanelStageOn = panel;
                CrashFates.InjuryOn = inj; CrashFates.FailStaysOn = stay; CrashFates.FastForwardOn = ff;
            }
        }

        static void TestHospitalFastForward()
        {
            Line("Fix 1 - A2: a hospital stay fast-forwards the calendar to the discharge day; nothing is locked after (owner 11-14):");
            var s = LifeRules.SeedNewGame("FIX1H", 25, LifeRules.DefaultJobIndex);
            try
            {
                DmgUseState(s);
                s.health = 70f;
                const float score = 20f;
                var st = CrashFates.StayFor(score, s.health);
                int day0 = s.day;
                s.fate = new CrashFate { pending = CrashFates.Hospital, pendingScore = score, pendingDay = day0, pendingWhere = "in Charlotte" };
                Check(CrashFates.Process(s) && CrashFates.PageDue(s), "the hospital injury is processed and its one page is due");
                Check(s.day == day0 + st.recoveryDays && s.day == s.fate.injuredUntil && s.fate.hospDays == st.recoveryDays,
                      "the calendar runs through the whole stay and recovery to the discharge morning",
                      "day " + day0 + " -> " + s.day + " (" + LifeRules.DateLabel(s.day) + "), stay " + st.hospitalDays + " + recovery to " + st.recoveryDays + " days");
                Check(!CrashFates.Hurt(s) && CrashFates.HurtRefusal(s) == null && CrashFates.WorkRefusal(s) == null,
                      "nothing is locked afterwards: no RECOVERING gate, no clock-on gate");
                Check(CrashFates.ExcusedOn(s, day0 + 1) && CrashFates.ExcusedOn(s, s.day - 1) && !CrashFates.ExcusedOn(s, s.day),
                      "a shift or club event missed in hospital is medical leave; the discharge day is not");
                var slow = CrashFates.StayFor(score, 10f);
                Check(slow.recoveryDays > st.recoveryDays, "HEALTH still scales the stay", st.recoveryDays + " days at HEALTH 70, " + slow.recoveryDays + " at 10");
                CrashFates.FastForwardOn = false;
                var s2 = LifeRules.SeedNewGame("FIX1H2", 25, LifeRules.DefaultJobIndex);
                DmgUseState(s2);
                s2.health = 70f;
                int d2 = s2.day;
                s2.fate = new CrashFate { pending = CrashFates.Hospital, pendingScore = score, pendingDay = d2, pendingWhere = "in Charlotte" };
                CrashFates.Process(s2);
                Check(s2.day == d2 + st.hospitalDays && CrashFates.Hurt(s2), "PSX_HOSPITAL_FASTFORWARD=0: the hospital days, then recovering at home (R1c-A)");
                CrashFates.FastForwardOn = true;
            }
            finally
            {
                CrashFates.FastForwardOn = true;
                DmgUseState(null);
            }
        }

        static void TestConcussionCarriesOn()
        {
            Line("Fix 1 - A: a concussion keeps the drive in a car that still goes (owner 11-14):");
            Check(!InjuryFx.EndsDrive(CrashFates.Concussion, false), "a concussion, the car still goes: the drive goes on");
            Check(InjuryFx.EndsDrive(CrashFates.Concussion, true), "a concussion in a car that cannot go on: the tow ends it");
            Check(InjuryFx.EndsDrive(CrashFates.Hospital, false) && InjuryFx.EndsDrive(CrashFates.Death, false),
                  "hospital and death end the drive, as before");
            InjuryFx.CarryOnOn = false;
            Check(InjuryFx.EndsDrive(CrashFates.Concussion, false), "PSX_CONCUSSION_DRIVE=0: every concussion ends the drive (R1c-A)");
            InjuryFx.CarryOnOn = true;
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            var s = LifeRules.SeedNewGame("FIX1A", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#fix1a");
            s.activeCar = owned.id;
            var car = DmgBench(spec, out var temp, out var resp);
            try
            {
                DmgUseState(s);
                DmgCareerDrive(owned, spec);
                CrashStopGap.Arm(car);
                Check(resp.Damage != null && !InjuryFx.CarCannotGo(car), "a sound career car can go on");
                if (resp.Damage == null) return;
                resp.Damage.Failed = DamageRules.FailHit;
                Check(InjuryFx.CarCannotGo(car), "the structural FAIL on the drive's ledger: it cannot");
                resp.Damage.Failed = DamageRules.FailNone;
                temp.Seize("the fix 1 test");
                Check(InjuryFx.CarCannotGo(car), "a seized engine: it cannot");
            }
            finally
            {
                TuneDrop(car);
                RaceHandoff.ClearAll();
                DmgUseState(null);
            }
        }

        static void TestBenchLive()
        {
            Line("Fix 1 - B: the debug bench reads and repairs the drive under way (owner 11-14):");
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            var s = LifeRules.SeedNewGame("FIX1B", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#fix1b");
            s.activeCar = owned.id;
            var car = DmgBench(spec, out var temp, out var resp);
            try
            {
                DmgUseState(s);
                foreach (var p in DamageBank.Files()) System.IO.File.Delete(p);
                DmgCareerDrive(owned, spec);
                temp.radiatorCond = 90f;
                CrashStopGap.Arm(car);
                var L = resp.Damage;
                if (L == null) { Check(false, "a career ledger"); return; }
                L.DrawOverride = () => DmgDice(0.05f);
                L.PlantHit(DamageModel.ZFront, -1, -1, 6.5f, 10f);
                L.PlantHit(DamageModel.ZCorner, 1, 1, 4f, 12f);
                L.Flush(13f);
                var rows = DebugCarOps.UnbankedRows(s, owned);
                Check(owned.faults.Count == 0 && rows.Count >= 3 && rows.Exists(f => f.id == DamageModel.PanelId),
                      "during the drive the bench lists this drive's rows, NOT BANKED YET (car.faults is still empty)",
                      string.Join(",", rows.Select(f => f.id + " s" + f.stage)));
                Check(car.hitSteerPull != 0f && temp.radiatorWeep > 0f && DamageBank.Files().Length == 1,
                      "the car pulls and weeps, and the drive's file waits", "pull " + car.hitSteerPull.ToString("0.00") + ", weep " + temp.radiatorWeep.ToString("0.000"));

                // a single switch, live
                DebugCarOps.SetFault(owned, spec, DamageModel.PanelId, false);
                DebugCarOps.Commit(RaceHandoff.FromLifeSim ? s : s, owned);
                var look = car.GetComponent<DamageLook>();
                Check(!DebugCarOps.UnbankedRows(s, owned).Exists(f => f.id == DamageModel.PanelId) &&
                      !L.incidents.Any(i => i.outcome.HasRow(DamageModel.PanelId)) && (look == null || look.Now.zones.x == 0f),
                      "a single switch acts on the drive: the panel goes from the ledger and from the body's look",
                      look != null ? look.Now.zones.ToString() : "no look");
                DebugCarOps.SetFault(owned, spec, FaultCatalog.AlignmentId, false);
                DebugCarOps.Commit(s, owned);
                Check(car.hitSteerPull == 0f, "the alignment switched off: the pull goes at once", car.hitSteerPull.ToString("0.00"));

                // CLEAR ALL: the drive's damage, the file, the live car
                int n = DebugCarOps.ClearFaults(owned);
                DebugCarOps.Commit(s, owned);
                Check(n >= 1 && L.incidents.Count == 0 && DebugCarOps.UnbankedRows(s, owned).Count == 0 && !DebugCarOps.LiveDamage(owned),
                      "CLEAR ALL FAULTS: this drive's incidents go", n + " cleared");
                Check(car.hitSteerPull == 0f && temp.radiatorWeep == 0f && temp.radiatorHit == 0f && temp.coolantPct >= 99.9f &&
                      Mathf.Abs(temp.radiatorCond - RaceHandoff.RadiatorCond) < 0.01f,
                      "and live: no pull, no weep, the radiator's hit taken back, the coolant topped up",
                      "weep " + temp.radiatorWeep.ToString("0.000") + ", hit " + temp.radiatorHit.ToString("0") + ", coolant " + temp.coolantPct.ToString("0"));
                Check(DamageBank.Files().Length == 0, "the pending damage file goes");
                int folded = DamageLedger.FoldNow(L);
                Check(folded == 0 && owned.faults.Count == 0, "nothing folds back at the drive's end", folded + " folded");

                // a FAIL put right: the car goes again
                L.PlantHit(DamageModel.ZFront, 1, -1, 6.5f, 30f);
                L.Failed = DamageRules.FailHit;
                owned.wreckHit = 77;
                car.faultAccelMult = 0f;
                Check(DamageRules.Wrecked(car), "a FAIL stops the car");
                DebugCarOps.ClearFaults(owned);
                DebugCarOps.Commit(s, owned);
                Check(!DamageRules.Wrecked(car) && owned.wreckHit == 0 && car.faultAccelMult > 0f,
                      "CLEAR ALL FAULTS puts the FAIL right: no wreck mark, the power back", "accel x" + car.faultAccelMult.ToString("0.00"));
                Check(look == null || (!look.Now.Any && look.Now.lampOut == 0), "the look is clean: no dents, no lamp out");

                // banked rows (after a RESET) come off the live car too
                L.PlantHit(DamageModel.ZFront, -1, -1, 6.5f, 50f);
                DamageLedger.FoldNow(L);
                Check(owned.faults.Exists(f => f.id == DamageModel.RadiatorId), "a RESET banks the rows onto the car");
                resp.Damage = null; resp.Ledger = null; resp.ResetDamage();
                temp.radiatorWeep = 0f;
                CrashStopGap.Arm(car);
                Check(temp.radiatorWeep > 0f, "the next drive weeps from the banked radiator row");
                DebugCarOps.ClearFaults(owned);
                DebugCarOps.Commit(s, owned);
                look = car.GetComponent<DamageLook>();
                Check(owned.faults.Count == 0 && temp.radiatorWeep == 0f && (look == null || !look.Now.Any),
                      "CLEAR ALL FAULTS takes the banked rows off the car as it drives", "weep " + temp.radiatorWeep.ToString("0.000"));
                DebugCarOps.LiveOn = false;
                L = resp.Damage;
                L.DrawOverride = () => DmgDice(0.05f);
                L.PlantHit(DamageModel.ZFront, -1, -1, 6.5f, 70f);
                DebugCarOps.ClearFaults(owned);
                Check(L.incidents.Count > 0 && DebugCarOps.UnbankedRows(s, owned).Count == 0,
                      "PSX_BENCH_LIVE=0: the bench reads and clears car.faults only, as before");
                DebugCarOps.LiveOn = true;
            }
            finally
            {
                foreach (var p in DamageBank.Files()) try { System.IO.File.Delete(p); } catch { }
                TuneDrop(car);
                RaceHandoff.ClearAll();
                DmgUseState(null);
            }
        }

        static void TestPanelByStage()
        {
            Line("Fix 1 - E: panel_damage priced by stage (design: a used cover $40 + paint $60):");
            var f = FaultCatalog.MakeById(DamageModel.PanelId, "jpn");
            if (f == null) { Check(false, "a panel_damage row"); return; }
            int flat = f.cost;
            int s1 = DamageHomes.PanelCost(flat, 0f, 1f), s2 = DamageHomes.PanelCost(flat, 0f, 2f), s3 = DamageHomes.PanelCost(flat, 0f, 3f);
            Check(s1 < flat && s2 == flat && s3 > flat && (flat != 230 || s1 == 100), "a cracked cover is about $100, a dent the flat price, crumpled twice it",
                  "$" + s1 + " / $" + s2 + " / $" + s3 + " (flat $" + flat + ")");
            var s = LifeRules.SeedNewGame("FIX1E", 25, LifeRules.DefaultJobIndex);
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car"); return; }
            var owned = DmgCar(s, spec, spec.id + "#fix1e");
            DamageBank.IncidentRec Rec(int hit, int stage) => new DamageBank.IncidentRec
            {
                hitId = hit, kind = DamageModel.KHit, zone = DamageModel.ZFront, side = -1, corner = -1, band = stage, dv = 2f + stage,
                rows = { new DamageBank.RowRec { id = DamageModel.PanelId, stage = stage, zone = DamageModel.ZFront, side = -1, corner = -1 } },
            };
            DamageBank.ApplyIncident(s, owned, Rec(901, 1));
            var row = owned.faults.Find(x => x.id == DamageModel.PanelId);
            int c1 = row != null ? row.cost : -1;
            DamageBank.ApplyIncident(s, owned, Rec(902, 3));
            int c3 = row != null ? row.cost : -1;
            int fam = FaultCatalog.MakeById(DamageModel.PanelId, FaultCatalog.FamilyOf(owned)).cost;
            Check(c1 == DamageHomes.PanelCost(fam, 0f, 1f) && Mathf.Abs(c3 - DamageHomes.PanelCost(fam, 0f, 3f)) <= 2,
                  "through the fold: the cover's price, then raised to the crumpled price", "$" + c1 + " -> $" + c3);
            DamageHomes.PanelStageOn = false;
            Check(DamageHomes.PanelCost(flat, 0f, 1f) == flat, "PSX_FAULT_PANELSTAGE=0: the flat price at every stage");
            DamageHomes.PanelStageOn = true;
        }
    }
}
