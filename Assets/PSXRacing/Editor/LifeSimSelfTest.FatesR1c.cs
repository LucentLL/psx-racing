using System.Linq;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R1c-A "what a crash costs you: the car and the driver" (CrashFates):
    /// the injury table, recovery against HEALTH, the hospital stay and
    /// medical leave, a FAIL car undrivable the next day and drivable after
    /// the repair, the write-off threshold and its two ends, death locking the
    /// career, the roof glass by stage and the spent airbag set. The in-play
    /// halves (25 / 65 km/h walls, a 130 km/h tree, the blur) are
    /// InjuryPlayCheck's.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void RunFatesR1cTests()
        {
            bool inj = CrashFates.InjuryOn, stay = CrashFates.FailStaysOn, wo = CrashFates.WriteOffOn, glass = DamageModel.GlassRuleOn, srs = DamageHomes.SrsOn;
            try
            {
                CrashFates.InjuryOn = CrashFates.FailStaysOn = CrashFates.WriteOffOn = DamageModel.GlassRuleOn = DamageHomes.SrsOn = true;
                CrashFates.FastForwardOn = false;   // fix pass 1: these test R1c-A's recovery at home (PSX_HOSPITAL_FASTFORWARD=0)
                Guard(nameof(TestInjuryTable), TestInjuryTable);
                Guard(nameof(TestRecoveryHealth), TestRecoveryHealth);
                Guard(nameof(TestFatesCareer), TestFatesCareer);
                Guard(nameof(TestFailStaysRepair), TestFailStaysRepair);
                Guard(nameof(TestWriteOff), TestWriteOff);
                Guard(nameof(TestDeathLocks), TestDeathLocks);
                Guard(nameof(TestGlassAndSrs), TestGlassAndSrs);
                Guard(nameof(TestSideHighPc34), TestSideHighPc34);
            }
            finally
            {
                CrashFates.InjuryOn = inj; CrashFates.FailStaysOn = stay; CrashFates.WriteOffOn = wo; DamageModel.GlassRuleOn = glass; DamageHomes.SrsOn = srs;
                CrashFates.FastForwardOn = System.Environment.GetEnvironmentVariable("PSX_HOSPITAL_FASTFORWARD") != "0";
            }
        }

        static CrashFates.Occupant Belted(float health = 100f) => new CrashFates.Occupant { belted = true, health = health };

        static int Out(int kind, int zone, float dv, bool narrow, int fail, CrashFates.Occupant o, int quarters = 0) =>
            CrashFates.OutcomeOf(CrashFates.Score(kind, zone, -1, dv, quarters, narrow, fail, o));

        static void TestInjuryTable()
        {
            Line("R1c-A - the injury table (score = delta-V as a belted head-on, x direction, object, belt, bags, cage, HEALTH):");
            int H = DamageModel.KHit, F = DamageModel.ZFront;
            var b = Belted();
            Check(Out(H, F, 7.3f, false, DamageRules.FailNone, b) == CrashFates.None, "25 km/h into a wall, belted: nothing");
            Check(Out(H, F, 11.6f, false, DamageRules.FailNone, b) == CrashFates.Concussion, "40 km/h into a wall, belted: a concussion");
            Check(Out(H, F, 18.1f, false, DamageRules.FailHit, b) == CrashFates.Hospital, "65 km/h into a wall (the FAIL), belted: hospital");
            Check(Out(H, F, 36f, true, DamageRules.FailHit, b) == CrashFates.Death, "130 km/h into a tree: nobody walks away (death)");
            var cage = b; cage.cage = true;
            Check(Out(H, F, 36f, true, DamageRules.FailHit, cage) == CrashFates.Hospital, "the same tree in a caged race car: hospital, alive");
            var loose = b; loose.belted = false;
            Check(Out(H, F, 13.9f, false, DamageRules.FailNone, b) == CrashFates.Concussion &&
                  Out(H, F, 13.9f, false, DamageRules.FailNone, loose) == CrashFates.Hospital,
                  "50 km/h head-on: belted a concussion, the belt off hospital");
            var bag = b; bag.bagsFired = true;
            Check(CrashFates.Score(H, F, -1, 18.1f, 0, false, DamageRules.FailHit, bag) < CrashFates.Score(H, F, -1, 18.1f, 0, false, DamageRules.FailHit, b),
                  "bags that fired soften a head-on");
            Check(Out(H, DamageModel.ZSide, 13.9f, true, DamageRules.FailNone, b) == CrashFates.Hospital &&
                  Out(H, DamageModel.ZRear, 13.9f, false, DamageRules.FailNone, b) == CrashFates.None,
                  "50 km/h: a pole in the door is hospital, a rear hit nothing");
            Check(Out(DamageModel.KRoll, DamageModel.ZRoof, 5f, false, DamageRules.FailNone, b, 4) == CrashFates.Concussion &&
                  Out(DamageModel.KRoll, DamageModel.ZRoof, 5f, false, DamageRules.FailNone, loose, 4) == CrashFates.Death,
                  "a four-quarter roll: belted a concussion, unbelted thrown out (death)");
            Check(Out(DamageModel.KBottom, DamageModel.ZCorner, 12f, false, DamageRules.FailNone, b) == CrashFates.None,
                  "a bump stop hurts nobody");
            Check(CrashFates.Score(H, F, -1, 7.51f, 0, false, DamageRules.FailNone, Belted(0f)) < CrashFates.ConcussAt,
                  "the hardest race contact of part 2b's census (7.5 m/s) hurts nobody even at HEALTH 0");
        }

        static void TestRecoveryHealth()
        {
            Line("R1c-A - HEALTH scales how bad and how long:");
            int H = DamageModel.KHit, F = DamageModel.ZFront;
            Check(Out(H, F, 15.5f, false, DamageRules.FailNone, Belted(100f)) == CrashFates.Concussion &&
                  Out(H, F, 15.5f, false, DamageRules.FailNone, Belted(10f)) == CrashFates.Hospital,
                  "the same 56 km/h wall: fit, a concussion; run down, hospital");
            var fit = CrashFates.StayFor(17f, 100f);
            var poor = CrashFates.StayFor(17f, 15f);
            Check(fit.recoveryDays < poor.recoveryDays && fit.hospitalDays == poor.hospitalDays,
                  "the same broken arm heals faster on HEALTH 100 than on 15", fit.recoveryDays + " vs " + poor.recoveryDays + " days");
            var arm = CrashFates.StayFor(15f, 70f);
            var lng = CrashFates.StayFor(27.9f, 70f);
            Check(arm.what.Contains("arm") && arm.bill >= 2000 && arm.bill <= 4000 && arm.hospitalDays <= 2 &&
                  lng.bill >= 30000 && lng.hospitalDays >= 10,
                  "1999 prices: a broken arm a few thousand dollars, a long stay tens of thousands",
                  arm.what + " $" + arm.bill + " " + arm.hospitalDays + "d | " + lng.what + " $" + lng.bill + " " + lng.hospitalDays + "d");
        }

        static DamageBank.IncidentRec FateRec(int hit, int zone, float dv, int band, int fail, int inj, float score) =>
            new DamageBank.IncidentRec { hitId = hit, kind = DamageModel.KHit, zone = zone, side = 0, corner = -1, band = band, dv = dv,
                                         fail = fail, inj = inj, injScore = score, what = "WallColl" };

        static void TestFatesCareer()
        {
            Line("R1c-A - the hospital: one summary, the bill through the hook, no work or driving, medical leave:");
            var s = LifeRules.SeedNewGame("R1CHOSP", 27, LifeRules.DefaultJobIndex);
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a car to crash"); return; }
            var car = DmgCar(s, spec, spec.id + "#r1chosp");
            s.activeCar = car.id;
            s.money = 1500;
            s.health = 70f;
            int day0 = s.day;
            DamageBank.ApplyIncident(s, car, FateRec(501, DamageModel.ZFront, 18.1f, 5, DamageRules.FailNone, CrashFates.Hospital, 17f));
            Check(s.fate.pending == CrashFates.Hospital, "the crash leaves a hospital stay pending for the home screen");
            Check(CrashFates.Process(s) && s.fate.page == "hospital" && CrashFates.PageDue(s) && s.day > day0 && s.fate.pending == CrashFates.None,
                  "processed once: days pass in hospital, the summary is due", "day " + day0 + " -> " + s.day);
            Check(!CrashFates.Process(s), "and only once");
            Check(s.fate.hospPaid + s.fate.hospitalOwed == s.fate.hospBill && s.money == 0 && s.fate.hospitalOwed > 0 && s.fate.hospInsurer == 0,
                  "no insurance set (CrashHooks null): the player pays what they have, the rest is owed",
                  "bill $" + s.fate.hospBill + ", paid $" + s.fate.hospPaid + ", owed $" + s.fate.hospitalOwed);
            string drive = LifeRules.DriveRefusal(s, car), work = WorkSchedule.ClockOnRefusal(s);
            Check(drive != null && drive.StartsWith("RECOVERING") && work != null && work.Contains("medical leave") && !DmgNamesPart(drive),
                  "until recovered: no driving and no work", drive + " | " + work);
            int guard = 0;
            while (s.day < s.fate.injuredUntil - 1 && ++guard < 200) LifeRules.SleepUntilMorning(s);
            Check(s.dutyLog.Exists(r => r != null && r.outcome == Duty.Medical) && !s.dutyLog.Exists(r => r != null && r.IsMiss) && !s.laidOff,
                  "shifts missed while hurt are medical leave: no miss, no lay-off",
                  s.dutyLog.Count(r => r != null && r.outcome == Duty.Medical) + " medical, laid off " + s.laidOff);
            LifeRules.SleepUntilMorning(s);
            Check(LifeRules.DriveRefusal(s, car) == null && (WorkSchedule.ClockOnRefusal(s) == null || !WorkSchedule.ClockOnRefusal(s).Contains("medical")),
                  "recovered: driving again, and the counter no longer says medical leave", LifeRules.DriveRefusal(s, car));
            CrashFates.InjuryOn = false;
            var s2 = LifeRules.SeedNewGame("R1CHOSP2", 27, LifeRules.DefaultJobIndex);
            var car2 = DmgCar(s2, spec, spec.id + "#r1chosp2");
            DamageBank.ApplyIncident(s2, car2, FateRec(502, DamageModel.ZFront, 18.1f, 5, DamageRules.FailNone, CrashFates.Hospital, 17f));
            Check(s2.fate.pending == CrashFates.None && LifeRules.DriveRefusal(s2, car2) == null, "PSX_INJURY=0: nobody is hurt");
            CrashFates.InjuryOn = true;
        }

        static void TestFailStaysRepair()
        {
            Line("R1c-A - a FAIL stays: undrivable the next day, drivable after the repair, the tow the only way it moves:");
            var s = LifeRules.SeedNewGame("R1CFAIL", 27, LifeRules.DefaultJobIndex);
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a car to crash"); return; }
            var car = DmgCar(s, spec, spec.id + "#r1cfail");
            s.activeCar = car.id;
            s.money = 50000;
            DamageBank.ApplyIncident(s, car, FateRec(601, DamageModel.ZFront, 12f, 5, DamageRules.FailSubframe, CrashFates.None, 0f));
            var row = car.faults.Find(f => f.id == CrashFates.SubframeId);
            Check(row != null && row.hidden && row.cause == FaultCause.Impact && car.wreckHit == 601,
                  "the subframe FAIL leaves its hidden row (cause stamped)", string.Join(",", car.faults.Select(f => f.id)));
            Check(LifeRules.DriveRefusal(s, car) == CrashFates.WreckLine && !DmgNamesPart(CrashFates.WreckLine),
                  "it will not drive, and the door names no part", LifeRules.DriveRefusal(s, car));
            LifeRules.SleepUntilMorning(s);
            Check(LifeRules.DriveRefusal(s, car) == CrashFates.WreckLine, "the next day: still wrecked");
            int before = s.money;
            Inspection.BookPro(s, car, Inspection.Pro.Mechanic);
            Check(!row.hidden && row.diagnosed && before - s.money == Inspection.ProCost(car, Inspection.Pro.Mechanic) + CareerDrive.TowHookUp,
                  "the mechanic always finds it (a wreck is plain to see) and tows it in", "paid " + (before - s.money));
            string why = LifeRules.OrderRepair(s, car, row, FaultCatalog.Venue.Mechanic);
            Check(why == null, "the repair books at Delmar's", why);
            int guard = 0;
            while (car.faults.Exists(f => f.id == CrashFates.SubframeId) && ++guard < 20) LifeRules.SleepUntilMorning(s);
            Check(!car.faults.Exists(f => f.id == CrashFates.SubframeId) && !CrashFates.Undrivable(car) && LifeRules.DriveRefusal(s, car) != CrashFates.WreckLine,
                  "repaired: it drives again", "after " + guard + " nights: " + LifeRules.DriveRefusal(s, car));
            CrashFates.FailStaysOn = false;
            var car2 = DmgCar(s, spec, spec.id + "#r1cfail2");
            DamageBank.ApplyIncident(s, car2, FateRec(602, DamageModel.ZFront, 12f, 5, DamageRules.FailSubframe, CrashFates.None, 0f));
            Check(!car2.faults.Exists(f => CrashFates.IsFailRow(f.id)) && LifeRules.DriveRefusal(s, car2) == null,
                  "PSX_FAIL_STAYS=0: the towed car drives again (part 2b)");
            CrashFates.FailStaysOn = true;
        }

        static void TestWriteOff()
        {
            Line("R1c-A - the write-off: a repair over 75% of the value totals it; keep it on a salvage title or junk it:");
            Check(CrashFates.IsWriteOff(751, 1000) && !CrashFates.IsWriteOff(750, 1000) && !CrashFates.IsWriteOff(5000, 0),
                  "the threshold is more than 75% of the value");
            var s = LifeRules.SeedNewGame("R1CWO", 27, LifeRules.DefaultJobIndex);
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a car to crash"); return; }
            var a = DmgCar(s, spec, spec.id + "#r1cwo1");
            var b = DmgCar(s, spec, spec.id + "#r1cwo2");
            var c = DmgCar(s, spec, spec.id + "#r1cwo3");
            s.activeCar = a.id;
            CrashFates.AfterIncident(s, a, FateRec(701, DamageModel.ZFront, 12f, 5, DamageRules.FailSubframe, CrashFates.None, 0f), 1000000);
            int est = CrashFates.RepairEstimate(s, a);
            Check(est > 0 && string.IsNullOrEmpty(s.fate.writeOffCar), "a car worth far more than the repair: no write-off", "repairs $" + est);
            CrashFates.AfterIncident(s, a, FateRec(704, DamageModel.ZFront, 12f, 5, DamageRules.FailNone, CrashFates.None, 0f), Mathf.CeilToInt(est / 0.75f) + 1);
            Check(string.IsNullOrEmpty(s.fate.writeOffCar), "repairs just under 75% of the value: no write-off");
            int v = Mathf.FloorToInt(est / 0.75f) - 1;
            CrashFates.AfterIncident(s, a, FateRec(705, DamageModel.ZFront, 12f, 5, DamageRules.FailNone, CrashFates.None, 0f), v);
            Check(s.fate.writeOffCar == a.id && CrashFates.PageDue(s) && s.fate.writeOffValue == v,
                  "repairs just over 75% of the value: a write-off, the page is due", "$" + est + " vs $" + v);
            Check(CrashFates.KeepSalvage(s) == null && a.title == CrashFates.TitleSalvage && !CrashFates.PageDue(s) && s.cars.Contains(a),
                  "KEEP IT: a salvage title, the car stays");
            CrashFates.AfterIncident(s, b, FateRec(702, DamageModel.ZFront, 12f, 5, DamageRules.FailSubframe, CrashFates.None, 0f), 100);
            int cars = s.cars.Count, money = s.money, scrap = Room.ScrapValue(b);
            Check(s.fate.writeOffCar == b.id && CrashFates.JunkWreck(s) == null && !s.cars.Contains(b) && s.cars.Count == cars - 1 &&
                  s.money == money + scrap, "JUNK IT (no insurance): the yard pays scrap, the car is gone", "+$" + (s.money - money));
            CrashFates.AfterIncident(s, c, new DamageBank.IncidentRec { hitId = 703, kind = DamageModel.KHit, zone = DamageModel.ZFront, band = 2, dv = 4f }, 10);
            Check(string.IsNullOrEmpty(s.fate.writeOffCar), "a 4 m/s bump is never a write-off, whatever the car is worth");
        }

        static void TestDeathLocks()
        {
            Line("R1c-A - death ends the career: the obituary, the career locked, NEW GAME:");
            LifeSimManager.UseScratchSave("selftest.r1cdeath");
            try
            {
                LifeSimManager.StartNewGame("R1CDEATH", 31, LifeRules.DefaultJobIndex);
                var s = LifeSimManager.State;
                if (s.cars.Count == 0) LifeRules.SeedFallbackCar(s);
                var car = s.ActiveCar ?? s.cars[0];
                var rec = FateRec(801, DamageModel.ZFront, 36f, 5, DamageRules.FailHit, CrashFates.Death, 48f);
                rec.narrow = true;
                rec.what = "Tree_07";
                DamageBank.ApplyIncident(s, car, rec);
                Check(CrashFates.Process(s) && CrashFates.Dead(s) && s.fate.deathLocked && CrashFates.PageDue(s),
                      "a crash nobody walks away from: dead, the page is due, the career written once and locked");
                string obit = s.fate.obituary;
                Check(obit.Contains("R1CDEATH") && obit.Contains("tree") && !DmgNamesPart(obit), "the Herald's notice names the driver and the tree, no part", obit);
                s.money = 424242;
                LifeSimManager.Save();
                var back = LifeSimManager.Load();
                Check(back != null && back.fate != null && back.fate.dead && back.money != 424242,
                      "after it, nothing is written (the career on disk is as it ended)", back != null ? "money on disk " + back.money : "no save");
                Check(!CrashFates.Process(back), "and nothing more happens to it");
            }
            finally
            {
                LifeSimManager.DeleteSave();
                LifeSimManager.UseCareerSave();
            }
        }

        static void TestSideHighPc34()
        {
            Line("R1c-A - PC34 (owner: 'I don't recall hitting anything and the car is completely decimated'): a tall wall on the side is a SIDE hit:");
            bool was = DamageRules.SideHighOn;
            try
            {
                DamageRules.SideHighOn = true;
                var sh = new DamageModel.Shell();
                float yGlass = sh.beltY + 0.5f * (sh.half.y - sh.beltY);
                // his three pending incidents (WallColl, upright, the patch wholly above the belt): dv 2.1 left;
                // 5.49 and 7.62 right, the wall leaning in over the road (and once with the car leaned onto it)
                var cases = new[] { (s: -1, n: new Vector3(1f, 0f, 0f), wy: 0f, dv: 2.1f),
                                    (s: 1, n: new Vector3(-0.95f, -0.31f, 0f).normalized, wy: -0.31f, dv: 5.49f),
                                    (s: 1, n: new Vector3(-0.6f, -0.8f, 0f).normalized, wy: -0.3f, dv: 7.62f) };
                foreach (var c in cases)
                {
                    DamageModel.Classify(sh, new Vector3(c.s * sh.half.x, yGlass, 0.1f), c.n, true, out int z, out int side, out _, yGlass - 0.05f, c.wy);
                    int band = DamageModel.Band(z, c.dv, CrashStopGap.Bumper.Plastic, false, false, false);
                    var inc = DmgInc(DamageModel.KHit, z, side, -1, band, DmgDice(0.1f));
                    Check(z == DamageModel.ZSide && side == c.s && !inc.outcome.HasRow(DamageModel.GlassId) && (c.dv > 3f || inc.outcome.ScuffOnly),
                          "dv " + c.dv + " on the " + (c.s < 0 ? "left" : "right") + " above the belt: SIDE, no roof and no glass" + (c.dv < 3f ? ", a scuff" : ""),
                          DamageModel.ZoneName(z) + " side " + side + " b" + band + ": " + inc.outcome);
                }
                DamageModel.Classify(sh, new Vector3(0f, sh.half.y, 0f), Vector3.down, true, out int za, out _, out _, float.NaN, -1f);
                DamageModel.Classify(sh, new Vector3(0f, sh.half.y, 0f), Vector3.down, false, out int zr, out _, out _, float.NaN, 1f);
                Check(za == DamageModel.ZRoof && zr == DamageModel.ZRoof, "a push from above, or the car on its roof: still ROOF");
                DamageRules.SideHighOn = false;
                DamageModel.Classify(sh, new Vector3(-sh.half.x, yGlass, 0.1f), Vector3.right, true, out int zo, out _, out _, yGlass - 0.05f, 0f);
                Check(zo == DamageModel.ZRoof, "PSX_FAULT_SIDEHIGH=0: part 2b (ROOF)");
            }
            finally { DamageRules.SideHighOn = was; }
        }

        static void TestGlassAndSrs()
        {
            Line("R1c-A - the roof glass needs a real roof or glass hit, priced by stage; the spent bag set:");
            var lo = DmgDice(0.1f);
            var b1 = DmgInc(DamageModel.KHit, DamageModel.ZRoof, 0, -1, 1, lo);
            var b2 = DmgInc(DamageModel.KHit, DamageModel.ZRoof, 0, -1, 2, lo);
            var b3 = DmgInc(DamageModel.KHit, DamageModel.ZRoof, 0, -1, 3, lo);
            var r1 = DmgInc(DamageModel.KRoll, DamageModel.ZRoof, 0, -1, 1, lo);
            int St(Incident i) => i.outcome.rows.Where(r => r.id == DamageModel.GlassId).Select(r => r.stage).DefaultIfEmpty(0).Max();
            Check(b1.outcome.ScuffOnly && St(b2) == 1 && St(b3) == 2 && St(r1) == 1,
                  "a 1.5-3 m/s brush of the glass on an upright car: a scuff (part 2b's $480 census contact); 3-5 cracked glass; 5+ a dented roof; a roll's table unchanged",
                  b1.outcome + " | " + b2.outcome + " | " + b3.outcome + " | roll " + r1.outcome);
            Check(DamageHomes.GlassCost(480, 0f, 1f) == 210 && DamageHomes.GlassCost(480, 0f, 2f) == 820 && DamageHomes.GlassCost(480, 0f, 3f) == 1950 &&
                  DamageHomes.GlassCost(210, 1f, 2f) == 820, "roof_glass by stage: glass $210, dented roof + windshield $820, crushed $1,950");
            var car = new OwnedCar { id = "srs#1", specId = "", displayName = "SRS" };
            var bag = FaultCatalog.MakeById(SrsBag.Id, "jpn");
            bag.mag = 1f;
            car.faults.Add(bag);
            Check(Mathf.Abs(DamageHomes.ValueMult(car) - 0.88f) < 0.001f, "a spent driver's bag takes 12% off the value", DamageHomes.ValueMult(car));
            var s = new LifeState { day = 10, money = 100000 };
            s.cars.Add(car);
            var mech = FaultCatalog.GetQuote(s, car, bag, FaultCatalog.Venue.Mechanic);
            var diy = FaultCatalog.GetQuote(s, car, bag, FaultCatalog.Venue.Diy);
            var dealer = FaultCatalog.GetQuote(s, car, bag, FaultCatalog.Venue.Dealer);
            Check(!mech.available && !diy.available && dealer.available && dealer.price > 0 && dealer.price <= bag.cost * 2,
                  "the bag set comes from the dealer only", "dealer $" + dealer.price);
        }
    }
}
