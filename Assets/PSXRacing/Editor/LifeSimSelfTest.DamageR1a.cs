using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R1a PART 1 "HITS LAND WHERE YOU HIT" (Fault_System_Design_2026-11-11 §2,
    /// §3.1, §10): TestImpactZones (every shell: front, rear, sides, the four
    /// corners, UNDER and ROOF - the contacts the world-up test drops), the
    /// zone tables, the live ledger (pulse incidents, radiator, pull, bar),
    /// TestDamageBank (the small damage file: idempotent, merged once, QUIT
    /// keeps it, a test drive and the CITY edition never touched, Load banks a
    /// leftover) and the v26 step. RunDamage runs these alone and writes
    /// PSXRacing_selftest_damage.txt.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        public static void RunDamage()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            try { RunDamageR1aTests(); }
            finally { Edition.Simulate(editionWas); }
            Line(failures == 0 ? "DAMAGE SELF-TEST OK" : "DAMAGE SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_damage.txt"), log.ToString());
        }

        static void RunDamageR1aTests()
        {
            var randomWas = Random.state;
            bool onWas = DamageModel.On, crashWas = CrashStopGap.On;
            try
            {
                DamageModel.On = true;
                CrashStopGap.On = true;
                Guard(nameof(TestImpactZones), TestImpactZones);
                Guard(nameof(TestDamageTables), TestDamageTables);
                Guard(nameof(TestDamageLive), TestDamageLive);
                Guard(nameof(TestDamageBank), TestDamageBank);
                Guard(nameof(TestDamageMigration), TestDamageMigration);
                RunDamageR1a2Tests();   // R1a part 2b: LifeSimSelfTest.DamageR1a2.cs
            }
            finally
            {
                DamageModel.On = onWas;
                CrashStopGap.On = crashWas;
                Random.state = randomWas;
            }
        }

        static float[] DmgDice(float all) { var d = new float[DamageModel.Dice]; for (int i = 0; i < d.Length; i++) d[i] = all; return d; }

        static Incident DmgInc(int kind, int zone, int side, int corner, int band, float[] dice, bool glancing = false, bool running = false)
        {
            var inc = new Incident { kind = kind, zone = zone, side = side, corner = corner, band = band, draws = dice, glancing = glancing, running = running, hitId = 1 };
            DamageModel.Resolve(inc, CrashStopGap.Bumper.Plastic);
            return inc;
        }

        // ---------------------------------------------------------------
        static void TestImpactZones()
        {
            Line("R1a - TestImpactZones: every shell, in its own frame (front, rear, sides, four corners, UNDER, ROOF):");
            int shells = 0, missing = 0, worldUpDropped = 0, roofUnder = 0;
            var bad = new List<string>();
            var counts = new int[8];
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null) { missing++; continue; }
                shells++;
                var sh = DamageModel.Shell.FromDef(def);
                Vector3 h = sh.half;
                float midY = Mathf.Min(0f, sh.beltY - 0.05f);
                void Expect(string what, Vector3 p, Vector3 n, bool upright, int zone, int side, int corner = -1)
                {
                    bool ok = DamageModel.Classify(sh, p, n, upright, out int z, out int s, out int c);
                    if (zone == DamageModel.ZNone ? ok : (!ok || z != zone || s != side || c != corner))
                        bad.Add(m.key + " " + what + " -> " + (ok ? DamageModel.ZoneName(z) + "/" + s + "/" + c : "ignored"));
                    else counts[zone]++;
                    // the world-up test DamageScore keeps would have thrown this contact away
                    if ((zone == DamageModel.ZUnder || zone == DamageModel.ZRoof) && Mathf.Abs(n.y) > 0.7f) worldUpDropped++;
                }
                Expect("nose", new Vector3(0f, midY, h.z), Vector3.back, true, DamageModel.ZFront, 0);
                Expect("nose left", new Vector3(-0.45f * h.x, midY, h.z), Vector3.back, true, DamageModel.ZFront, -1);
                Expect("nose right", new Vector3(0.45f * h.x, midY, h.z), Vector3.back, true, DamageModel.ZFront, 1);
                Expect("tail", new Vector3(0f, midY, -h.z), Vector3.forward, true, DamageModel.ZRear, 0);
                float zMid = (sh.hubs[0].z + sh.hubs[2].z) * 0.5f;
                Expect("left door", new Vector3(-h.x, midY, zMid), Vector3.right, true, DamageModel.ZSide, -1);
                Expect("right door", new Vector3(h.x, midY, zMid), Vector3.left, true, DamageModel.ZSide, 1);
                for (int i = 0; i < 4; i++)
                {
                    float sx = i % 2 == 0 ? -1f : 1f;
                    Expect(DamageModel.CornerNames[i] + " arch", new Vector3(sx * h.x, Mathf.Min(sh.hubs[i].y, sh.beltY - 0.05f), sh.hubs[i].z),
                           new Vector3(-sx, 0f, 0f), true, DamageModel.ZCorner, (int)sx, i);
                }
                Expect("belly", new Vector3(0f, -h.y, zMid), Vector3.up, true, DamageModel.ZUnder, 0);
                Expect("belly at FL tyre (the suspension's)", new Vector3(sh.hubs[0].x, -h.y, sh.hubs[0].z), Vector3.up, true, DamageModel.ZNone, 0);
                Expect("roof from above", new Vector3(0f, h.y, zMid), Vector3.down, true, DamageModel.ZRoof, 0);
                Expect("on its roof (ground pushes toward the floor)", new Vector3(0f, h.y, zMid), Vector3.down, false, DamageModel.ZRoof, 0);
                Expect("left pillar", new Vector3(-h.x, sh.beltY + 0.5f * (h.y - sh.beltY), zMid), Vector3.right, true, DamageRules.SideHighOn ? DamageModel.ZSide : DamageModel.ZRoof, -1);   // R1c-A: a side push at the glass is SIDE
                Expect("belly, rolled on its side (no tyre excuse)", new Vector3(sh.hubs[0].x, -h.y, sh.hubs[0].z), Vector3.up, false, DamageModel.ZUnder, -1);
                roofUnder++;
            }
            Check(shells >= 52 && missing == 0, "every shell in the library loads (52 expected)", shells + " shells, " + missing + " missing");
            Check(bad.Count == 0, "every shell: nose (centre, left, right), tail, both doors, all four arches, belly, roof, pillar where they are",
                  bad.Count == 0 ? "front " + counts[1] + ", rear " + counts[2] + ", side " + counts[3] + ", under " + counts[4] +
                                   ", roof " + counts[5] + ", corner " + counts[6] : bad.Count + " wrong, first: " + string.Join("; ", bad.Take(4)));
            Check(worldUpDropped >= shells * 4 && roofUnder == shells,
                  "the UNDER and ROOF contacts the world-up test throws away (|n.up| > 0.7) now land in their zones", worldUpDropped + " contacts");
            var under = DmgInc(DamageModel.KHit, DamageModel.ZUnder, 0, -1, 2, DmgDice(0.1f));
            var roof = DmgInc(DamageModel.KHit, DamageModel.ZRoof, 0, -1, 2, DmgDice(0.1f));
            Check(under.outcome.HasRow(DamageModel.ExhaustId) && roof.outcome.HasRow(DamageModel.GlassId),
                  "and they cost: a belly strike at 2-3.5 m/s bends the exhaust, a roof hit at 3-5 m/s dents the roof", under.outcome + " | " + roof.outcome);
        }

        // ---------------------------------------------------------------
        static void TestDamageTables()
        {
            Line("R1a - the zone tables (REALISTIC, cumulative, one die per row):");
            var P = CrashStopGap.Bumper.Plastic;
            int B(int z, float v, CrashStopGap.Bumper b = CrashStopGap.Bumper.Plastic, bool narrow = false, bool glancing = false, bool bar = false) =>
                DamageModel.Band(z, v, b, narrow, glancing, bar);
            Check(B(1, 1.0f) == 0 && B(1, 1.2f) == 1 && B(1, 3.5f) == 2 && B(1, 6f) == 3 && B(1, 9f) == 4 && B(1, 12f) == 5,
                  "FRONT bands: under the 2.5-mph rating nothing, then 1.1-3 / 3-5 / 5-8 / 8-10 / 10+");
            Check(B(1, 2.0f, CrashStopGap.Bumper.FiveMph) == 0 && B(1, 2.3f, CrashStopGap.Bumper.FiveMph) == 1 &&
                  B(2, 1.4f, CrashStopGap.Bumper.Chrome) == 0 && B(2, 1.6f, CrashStopGap.Bumper.Chrome) == 1,
                  "5-mph bumpers take 2.2 m/s, chrome 1.5");
            Check(B(3, 4f, P, narrow: true) == 3 && B(3, 4f, P, glancing: true) == 1 && B(1, 2.5f, P, bar: true) == 2,
                  "a pole counts a band higher, a glancing hit a band lower, a crushed bar x1.3");
            var gl = DmgInc(DamageModel.KHit, DamageModel.ZFront, 0, -1, 1, DmgDice(0.01f), glancing: true);
            Check(gl.outcome.ScuffOnly, "a glancing rub at the bottom band is a scuff and nothing more", gl.outcome);
            var f2 = DmgInc(DamageModel.KHit, DamageModel.ZFront, -1, -1, 2, DmgDice(0.05f));
            Check(f2.outcome.HasRow(DamageModel.PanelId) && f2.outcome.HasRow(DamageModel.BarId) && f2.outcome.HasRow(DamageModel.LampId) &&
                  f2.outcome.HasRow(DamageModel.RadiatorId) && Mathf.Approximately(f2.outcome.leakPctMin, 0.5f) && f2.outcome.bodyLoss > 0f,
                  "a 3-5 m/s nose-in with every die low: cover, bar, a lamp, a weeping radiator (0.5 %/min), body", f2.outcome);
            var r2 = DmgInc(DamageModel.KHit, DamageModel.ZRear, 1, -1, 2, DmgDice(0.05f));
            Check(r2.outcome.HasRow(DamageModel.LampId) && r2.outcome.HasRow(DamageModel.ExhaustId) && !r2.outcome.HasRow(DamageModel.RadiatorId),
                  "a tail: a tail lamp and the exhaust tip, never the radiator", r2.outcome);
            var f4 = DmgInc(DamageModel.KHit, DamageModel.ZFront, 0, -1, 4, DmgDice(0.5f));
            Check(f4.outcome.rows.Any(r => r.id == DamageModel.RadiatorId && r.stage == 3) && Mathf.Approximately(f4.outcome.leakPctMin, 6f) &&
                  f4.outcome.alignSeverity == 0,
                  "8-10 m/s square on: the radiator crushed (6 %/min), and a centre-front hit bends no corner", f4.outcome);
            // monotone: for fixed dice a higher band never gives less
            var rng = new System.Random(4111);
            int nonMono = 0;
            foreach (int zone in new[] { 1, 2, 3, 4, 5, 6 })
                for (int t = 0; t < 200; t++)
                {
                    var d = DamageModel.Draw(rng);
                    Outcome prev = null;
                    for (int b = 1; b <= 5; b++)
                    {
                        var inc = DmgInc(DamageModel.KHit, zone, -1, zone == 6 ? 0 : -1, b, d);
                        var o = inc.outcome;
                        if (prev != null)
                        {
                            bool rowsKept = prev.rows.All(pr => o.rows.Any(r => r.id == pr.id && r.stage >= pr.stage));
                            if (!rowsKept || o.bodyLoss < prev.bodyLoss - 1e-4f || o.radiatorLoss < prev.radiatorLoss - 1e-4f ||
                                o.alignSeverity < prev.alignSeverity) nonMono++;
                        }
                        prev = new Outcome();
                        prev.bodyLoss = o.bodyLoss; prev.radiatorLoss = o.radiatorLoss; prev.alignSeverity = o.alignSeverity;
                        prev.rows.AddRange(o.rows);
                    }
                }
            Check(nonMono == 0, "for one set of dice a harder hit never takes anything away (6 zones x 200 dice x 5 bands)", nonMono + " regressions");
            var c1 = DmgInc(DamageModel.KHit, DamageModel.ZCorner, -1, 0, 1, DmgDice(0.1f));
            var c2 = DmgInc(DamageModel.KBottom, DamageModel.ZCorner, 1, 3, 2, DmgDice(0.3f));
            Check(c1.outcome.alignSeverity == 1 && c1.outcome.alignSide == -1 && c2.outcome.alignSeverity == 2 && c2.outcome.alignSide == 1 &&
                  c2.outcome.paintLoss == 0f && DamageModel.BottomBand(5.9f) == 0 && DamageModel.BottomBand(6f) == 1 && DamageModel.BottomBand(10f) == 3,
                  "a corner: toe on the struck side (minor), a tie rod (severe); a bump-stop strike at 6 / 8 / 10 m/s reads the corner columns, no paint",
                  c1.outcome + " | " + c2.outcome);
            var u1 = DmgInc(DamageModel.KHit, DamageModel.ZUnder, 0, -1, 1, DmgDice(0.1f));
            Check(!u1.outcome.Any && u1.outcome.sparks, "a belly at 1-2 m/s: sparks only");
            var w = DmgInc(DamageModel.KWater, DamageModel.ZUnder, 0, -1, 1, DmgDice(0.3f), running: true);
            var w2 = DmgInc(DamageModel.KWater, DamageModel.ZUnder, 0, -1, 1, DmgDice(0.3f), running: false);
            Check(w.outcome.flood && w.outcome.hydrolock && w2.outcome.flood && !w2.outcome.hydrolock,
                  "WATER: a flood title; hydrolock only with the engine running (50%)");
            Check(DamageModel.DropBand(Mathf.Sqrt(2f * 9.81f * 5f)) == 4 && DamageModel.DropBand(Mathf.Sqrt(2f * 9.81f * 1f)) <= 1 &&
                  DamageModel.RollBand(0f, 2) == 1 && DamageModel.RollBand(4f, 4) == 2 && DamageModel.RollBand(6f, 1) == 3,
                  "a 5 m DROP reads the frame band, a 1 m one sparks at most; a ROLL onto the roof costs at least the first ROOF band");
            // round D's bumper era through CarFacts
            CarSpec Era(string era) => CarCatalog.All.FirstOrDefault(c => c != null && CarFacts.Get(c) != null && CarFacts.Get(c).bumperEra == era);
            var chrome = Era("chrome"); var plastic = Era("2.5mph") ?? Era("plastic");
            Check(chrome != null && CrashStopGap.BumperFor(chrome) == CrashStopGap.Bumper.Chrome &&
                  plastic != null && CrashStopGap.BumperFor(plastic) == CrashStopGap.Bumper.Plastic &&
                  CrashStopGap.BumperFor(new CarSpec { origin = "usa", modelYear = 1978 }) == CrashStopGap.Bumper.FiveMph,
                  "BumperFor reads CarFacts.bumperEra (a car with no facts row: era and origin, as R0a)",
                  (chrome != null ? chrome.name : "-") + " / " + (plastic != null ? plastic.name : "-"));
        }

        // ---------------------------------------------------------------
        static OwnedCar DmgCar(LifeState s, CarSpec spec, string id)
        {
            var c = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            c.id = id;
            c.faults.Clear();
            c.carHP = c.paint = c.radiator = 80f;
            c.fuel = 60f;
            return c;
        }

        static void DmgUseState(LifeState s)
        {
            var field = typeof(LifeSimManager).GetField("state",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (s == null) { LifeSimManager.UseCareerSave(); return; }
            LifeSimManager.UseScratchSave("r1a");
            field.SetValue(null, s);
        }

        static CarController DmgBench(CarSpec spec, out EngineTemp temp, out CollisionResponder resp)
        {
            var car = TuneBenchCar(spec);
            temp = car.gameObject.AddComponent<EngineTemp>();
            temp.car = car;
            resp = car.gameObject.AddComponent<CollisionResponder>();
            return car;
        }

        static void DmgCareerDrive(OwnedCar owned, CarSpec spec)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = owned.id;
            RaceHandoff.CarSpecId = spec.id;
            RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
        }

        static void TestDamageLive()
        {
            Line("R1a - live: a pulse incident acts on the car at once (pull, radiator weep, bar), and grows on its own dice:");
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            var s = LifeRules.SeedNewGame("R1ALIVE", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#r1alive");
            s.activeCar = owned.id;
            var car = DmgBench(spec, out var temp, out var resp);
            try
            {
                DmgUseState(s);
                DmgCareerDrive(owned, spec);
                temp.radiatorCond = 90f;
                Check(CrashStopGap.Arm(car) && resp.Damage != null && resp.Damage.Banks && resp.Ledger == null,
                      "a career drive arms the LOCATED ledger (R0a's table stays behind PSX_FAULT_LOCATED=0)");
                var L = resp.Damage;
                L.DrawOverride = () => DmgDice(0.05f);
                var a = L.PlantHit(DamageModel.ZFront, -1, -1, 4f, 10f);
                var a2 = L.PlantHit(DamageModel.ZFront, -1, -1, 6.5f, 10.15f);
                Check(a == a2 && a.band == 3, "a harder pulse of the same crash (0.15 s, same place) is the same incident, re-read upward", "band " + a.band);
                Check(temp.radiatorCond < 90f && temp.radiatorHit > 0f &&
                      Mathf.Abs(temp.radiatorWeep - DamageModel.WeepFor(2f)) < 1e-4f && CrashStopGap.WeepLoss(temp.radiatorWeep, 95f) > 0f,
                      "a holed radiator at once: the core loses condition and weeps 2 %/min through the cooling model",
                      "cond " + temp.radiatorCond.ToString("0") + ", weep " + temp.radiatorWeep.ToString("0.000"));
                Check(L.barCrushed[DamageModel.ZFront], "the front bar is crushed: the next front hit counts x1.3");
                var b = L.PlantHit(DamageModel.ZCorner, 1, 1, 4f, 12f);
                Check(b != a && car.hitSteerPull > 0f && Mathf.Approximately(car.hitSteerPull, CrashStopGap.PullDegSevere),
                      "a separate hit on the FR corner pulls right at once (tie rod: the severe band)", car.hitSteerPull.ToString("0.000"));
                var c = L.PlantHit(DamageModel.ZFront, 0, -1, 2.4f, 20f);
                Check(c.band == 2, "a 2.4 m/s nose tap with the bar gone reads 3.1 (band 2)", "band " + c.band);
                Check(L.Events == 3 && L.incidents.Select(i => i.hitId).Distinct().Count() == 3, "three incidents, three hitIds", L.Events);
                RaceHandoff.TestDrive = true;
                resp.Damage = null;
                Check(!CrashStopGap.Arm(car) && resp.Damage == null, "a TEST DRIVE arms nothing: the seller's car is not the player's");
                RaceHandoff.TestDrive = false;
                bool was = DamageModel.On;
                DamageModel.On = false;
                Check(CrashStopGap.Arm(car) && resp.Ledger != null && resp.Damage == null, "PSX_FAULT_LOCATED=0: R0a's ledger, as before");
                DamageModel.On = was;
                resp.Ledger = null;
            }
            finally
            {
                TuneDrop(car);
                RaceHandoff.ClearAll();
                DmgUseState(null);
            }
        }

        // ---------------------------------------------------------------
        static void TestDamageBank()
        {
            Line("R1a - TestDamageBank: the small damage file, banked once:");
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            var s = LifeRules.SeedNewGame("R1ABANK", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#r1abank");
            var other = DmgCar(s, spec, spec.id + "#r1aother");
            s.activeCar = owned.id;
            var car = DmgBench(spec, out var temp, out var resp);
            try
            {
                DmgUseState(s);
                foreach (var p in DamageBank.Files()) System.IO.File.Delete(p);
                DmgCareerDrive(owned, spec);
                CrashStopGap.Arm(car);
                var L = resp.Damage;
                L.DrawOverride = () => DmgDice(0.05f);
                L.PlantHit(DamageModel.ZFront, -1, -1, 6.5f, 10f);
                L.PlantHit(DamageModel.ZRoof, 0, -1, 3.5f, 30f);
                L.Flush(31f);
                var files = DamageBank.Files();
                var fi = files.Length == 1 ? new System.IO.FileInfo(files[0]) : null;
                Check(fi != null && fi.Length <= 2048, "a hit writes ONE small per-car file (2 KB or less), no full save",
                      files.Length + " file(s), " + (fi != null ? fi.Length + " bytes" : "-"));
                string before = JsonUtility.ToJson(owned);
                int n1 = DamageBank.FoldFiles(s);
                var panel = owned.faults.Find(f => f.id == DamageModel.PanelId);
                var rad = owned.faults.Find(f => f.id == DamageModel.RadiatorId);
                var glass = owned.faults.Find(f => f.id == DamageModel.GlassId);
                Check(n1 == 2 && panel != null && rad != null && glass != null && panel.hidden && rad.hidden && glass.hidden,
                      "the fold banks both incidents onto the car the file names: hidden rows", n1 + ": " + string.Join(",", owned.faults.Select(f => f.id)));
                Check(panel != null && panel.cause == FaultCause.Impact && panel.zone1 == DamageModel.ZFront && panel.hitSide == -1 &&
                      panel.hitMps > 6f && panel.hitId != 0 && glass != null && glass.zone1 == DamageModel.ZRoof && rad.stage == 2,
                      "every row carries its cause: impact, zone, side, delta-V, hitId, stage",
                      panel != null ? panel.causeNote + " id " + panel.hitId : "-");
                Check(owned.history.Count == 2 && owned.carHP < 80f && owned.radiator < 80f && other.faults.Count == 0 && other.carHP == 80f,
                      "two events in the car's history; the other car untouched", "body " + owned.carHP.ToString("0.0"));
                string once = JsonUtility.ToJson(owned);
                int n2 = DamageBank.FoldFiles(s);
                Check(n2 == 0 && JsonUtility.ToJson(owned) == once && once != before, "folding again changes nothing (idempotent by hitId)");
                LifeSimManager.Save();
                Check(DamageBank.Files().Length == 0, "the file goes once the save that carries it is on disk");
                L.Flush(40f);   // nothing new: nothing written
                Check(DamageBank.Files().Length == 0, "a ledger with nothing new writes nothing");
                // the same file again (a crash before the delete): merged once
                var again = new DamageBank.DamageFile { carId = owned.id, drive = "again" };
                foreach (var inc in L.incidents) again.incidents.Add(DamageBank.Record(inc));
                DamageBank.Write(again);
                Check(DamageBank.FoldFiles(s) == 0 && JsonUtility.ToJson(owned) == once, "damage files merge once: the same file twice banks nothing more");
                LifeSimManager.Save();

                // QUIT keeps the damage (answer 1), banked once through the leg
                var quitter = DmgCar(s, spec, spec.id + "#r1aquit");
                DmgCareerDrive(quitter, spec);
                resp.Damage = null;
                CrashStopGap.Arm(car);
                resp.Damage.DrawOverride = () => DmgDice(0.05f);
                resp.Damage.PlantHit(DamageModel.ZFront, 1, -1, 4f, 50f);
                bool banked = CareerDrive.BankLeg(car, 3000f, 0f, "R1A TEST");
                int panels = quitter.faults.Count(f => f.id == DamageModel.PanelId);
                Check(banked && panels == 1 && quitter.history.Count == 1 && quitter.carHP < 80f,
                      "QUIT keeps the damage: the leg banks it once (one panel row, one event), no respawn needed",
                      string.Join(",", quitter.faults.Select(f => f.id)) + " body " + quitter.carHP.ToString("0.0"));
                LifeRules.ApplyRaceResult(s);
                Check(quitter.faults.Count(f => f.id == DamageModel.PanelId) == 1 && quitter.history.Count == 1,
                      "and a later apply-back banks nothing twice");

                // a test drive never touches the player's car
                string mine = JsonUtility.ToJson(owned);
                var seller = new DamageBank.DamageFile { carId = "seller#lot7", drive = "td" };
                seller.incidents.Add(new DamageBank.IncidentRec { hitId = 777, zone = 1, dv = 7f, body = 20f });
                DamageBank.Write(seller);
                Check(DamageBank.FoldFiles(s) == 0 && JsonUtility.ToJson(owned) == mine,
                      "a file for a car that is not the career's banks nothing - never onto the active car");
                LifeSimManager.Save();

                // the CITY edition has no career: nothing armed, nothing written
                var ed = Edition.Simulated;
                Edition.Simulate(EditionKind.City);
                resp.Damage = null;
                bool armed = CrashStopGap.Arm(car);
                bool wrote = DamageBank.Write(new DamageBank.DamageFile { carId = owned.id, drive = "city", incidents = { new DamageBank.IncidentRec { hitId = 5 } } });
                Edition.Simulate(ed);
                Check(!armed && !wrote && DamageBank.Files().Length == 0, "the CITY edition: no ledger, no file, no career touched");

                // a drive that never reached its exit: the next Load banks it, once
                var lost = new DamageBank.DamageFile { carId = owned.id, drive = "lost" };
                lost.incidents.Add(new DamageBank.IncidentRec { hitId = 4242, kind = DamageModel.KHit, zone = DamageModel.ZRear, dv = 4f, paint = 3f,
                    rows = { new DamageBank.RowRec { id = DamageModel.LampId, stage = 1, zone = DamageModel.ZRear, side = 1, corner = -1 } } });
                DamageBank.Write(lost);
                LifeSimManager.Save();
                var re = LifeSimManager.Reload();
                var reCar = re != null ? re.FindCar(owned.id) : null;
                Check(reCar != null && reCar.faults.Exists(f => f.id == DamageModel.LampId && f.hitId == 4242) &&
                      reCar.history.Exists(e => e.hitId == 4242) && DamageBank.Files().Length == 0,
                      "a leftover file is banked at the next Load (and deleted after that save)");
            }
            finally
            {
                foreach (var p in DamageBank.Files()) try { System.IO.File.Delete(p); } catch { }
                TuneDrop(car);
                RaceHandoff.ClearAll();
                DmgUseState(null);
            }
        }

        // ---------------------------------------------------------------
        static void TestDamageMigration()
        {
            Line("R1a - v26: history and stage, idempotent:");
            var s = new LifeState { saveVersion = 25, day = 30 };
            var car = new OwnedCar { id = "mig#1", specId = "", displayName = "MIG" };
            car.history = null;
            car.faults.Add(new CarFault { id = "alignment", severity = 2f, hidden = true });
            car.faults.Add(new CarFault { id = "oil_leak", severity = 1f, hidden = false, diagnosed = true });
            s.cars.Add(car);
            LifeSimManager.Migrate(s);
            string once = JsonUtility.ToJson(s);
            LifeSimManager.Migrate(s);
            Check(s.saveVersion >= 26 && car.history != null && DamageBank.StageOf(car.faults[0]) == 3f &&
                  DamageBank.StageOf(car.faults[1]) == 2f && car.faults[0].stage == 0f &&
                  car.faults[0].hidden && car.faults[1].diagnosed && car.title == 0 && JsonUtility.ToJson(s) == once,
                  "a v25 career: severity 2 -> stage 3, 1 -> 2, flags kept, empty history, clean title; twice = once",
                  "v" + s.saveVersion);
        }
    }
}
