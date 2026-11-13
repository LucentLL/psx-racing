using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R1a PART 2b "rules and proof": the structural FAIL, the crush order and
    /// one row per part and zone, multi-zone contacts, heat soak, the hit rows'
    /// homes (Spares, floor, chips, value, the debug lists) and the 10k-incident
    /// sweep. Run by RunDamageR1aTests (the damage-only and the full self-test).
    /// The in-play halves (a 65 km/h FAIL wall, the swipe, the roll, the traffic
    /// rear-end, the 112 C timing, RESTART) are DamagePlayCheck's.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void RunDamageR1a2Tests()
        {
            bool fail = DamageRules.FailOn, span = DamageRules.SpanOn, crush = DamageRules.CrushOn, soak = HeatSoak.On;
            try
            {
                DamageRules.FailOn = DamageRules.SpanOn = DamageRules.CrushOn = HeatSoak.On = true;
                Guard(nameof(TestDamageSweep10k), TestDamageSweep10k);
                Guard(nameof(TestDamageFail), TestDamageFail);
                Guard(nameof(TestDamageCrush), TestDamageCrush);
                Guard(nameof(TestDamageSpan), TestDamageSpan);
                Guard(nameof(TestHeatSoak), TestHeatSoak);
                Guard(nameof(TestDamageHomes), TestDamageHomes);
            }
            finally
            {
                DamageRules.FailOn = fail; DamageRules.SpanOn = span; DamageRules.CrushOn = crush; HeatSoak.On = soak;
            }
        }

        static readonly string[] DmgPartWords =
            { "radiator", "bumper", "lamp", "exhaust", "glass", "roof", "knuckle", "subframe", "coolant", "panel", "core" };
        static bool DmgNamesPart(string s) =>
            !string.IsNullOrEmpty(s) && DmgPartWords.Any(w => s.ToLowerInvariant().Contains(w));

        static Incident DmgFailInc(int kind, int zone, int band, float dv, float closing, float die) =>
            new Incident { kind = kind, zone = zone, band = band, dv = dv, closing = closing, draws = DmgDice(die), hitId = 1 };

        // ---------------------------------------------------------------
        static void TestDamageSweep10k()
        {
            Line("R1a part 2 - the 10k-incident sweep (random shells, faces, pushes, delta-V 0-20 m/s, bumpers, dice):");
            var defs = new List<CarModelDef>();
            foreach (var m in CarModelLibrary.Models) { var d = CarModelLibrary.Load(m.key); if (d != null) defs.Add(d); }
            if (defs.Count == 0) { Check(false, "car shells to sweep"); return; }
            var rng = new System.Random(1111);
            var bumpers = new[] { CrashStopGap.Bumper.Plastic, CrashStopGap.Bumper.FiveMph, CrashStopGap.Bumper.Chrome };
            int n = 0, centreFront = 0, cfCorner = 0, any = 0, scuff = 0, fails = 0, failUnder5 = 0, over17 = 0, over17Fail = 0,
                knuckleTop = 0, knuckleFail = 0, frontTop = 0, subFail = 0, dupRows = 0;
            var zones = new int[7];
            for (int k = 0; k < 10000; k++)
            {
                var sh = DamageModel.Shell.FromDef(defs[rng.Next(defs.Count)]);
                Vector3 h = sh.half;
                Vector3 p = new Vector3((float)(rng.NextDouble() * 2 - 1) * h.x, (float)(rng.NextDouble() * 2 - 1) * h.y,
                                        (float)(rng.NextDouble() * 2 - 1) * h.z);
                Vector3 nrm;
                switch (rng.Next(6))
                {
                    case 0: p.x = -h.x; nrm = Vector3.right; break;
                    case 1: p.x = h.x; nrm = Vector3.left; break;
                    case 2: p.y = -h.y; nrm = Vector3.up; break;
                    case 3: p.y = h.y; nrm = Vector3.down; break;
                    case 4: p.z = -h.z; nrm = Vector3.forward; break;
                    default: p.z = h.z; nrm = Vector3.back; break;
                }
                if (k % 5 == 0)
                {
                    // one in five: a nose hit square in the middle, under the belt
                    // (the gate's case; a random face lands there ~2% of the time)
                    p = new Vector3((float)(rng.NextDouble() * 2 - 1) * CrashStopGap.CentreBand * 0.9f * h.x,
                                    Mathf.Lerp(-h.y, sh.beltY, (float)rng.NextDouble()), h.z);
                    nrm = Vector3.back;
                }
                nrm = (nrm + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.8f).normalized;
                bool upright = rng.NextDouble() < 0.9;
                if (!DamageModel.Classify(sh, p, nrm, upright, out int zone, out int side, out int corner)) continue;
                n++;
                float dv = (float)(rng.NextDouble() * 20.0);
                bool glancing = zone != DamageModel.ZUnder && zone != DamageModel.ZRoof && rng.NextDouble() < 0.3;
                bool narrow = rng.NextDouble() < 0.1;
                var bumper = bumpers[rng.Next(bumpers.Length)];
                int band = DamageModel.Band(zone, dv, bumper, narrow, glancing, false);
                var inc = new Incident
                {
                    kind = DamageModel.KHit, zone = zone, side = side, corner = corner, band = band, dv = dv, closing = dv,
                    glancing = glancing, narrow = narrow, draws = DamageModel.Draw(rng), hitId = k + 1,
                };
                DamageModel.Resolve(inc, bumper);
                zones[zone]++;
                var o = inc.outcome;
                if (o.Any) any++;
                if (o.ScuffOnly) scuff++;
                if (zone == DamageModel.ZFront && side == 0) { centreFront++; if (o.alignSeverity > 0) cfCorner++; }
                int f = DamageRules.FailOf(inc);
                if (f != DamageRules.FailNone) { fails++; if (dv < 5f) failUnder5++; }
                if (dv >= DamageRules.FailMps) { over17++; if (f != DamageRules.FailNone) over17Fail++; }
                else if (zone == DamageModel.ZCorner && band >= 4) { knuckleTop++; if (f == DamageRules.FailKnuckle) knuckleFail++; }
                else if (zone == DamageModel.ZFront && band >= 5) { frontTop++; if (f == DamageRules.FailSubframe) subFail++; }
                var keys = new HashSet<string>();
                foreach (var r in o.rows) if (!keys.Add(r.id + "/" + r.zone + "/" + r.side)) dupRows++;
            }
            Line("    " + n + " incidents classified (front " + zones[1] + ", rear " + zones[2] + ", side " + zones[3] + ", under " + zones[4] +
                 ", roof " + zones[5] + ", corner " + zones[6] + "); any damage " + any + ", scuff-only " + scuff + "; FAIL " + fails +
                 " (knuckle " + knuckleFail + " of " + knuckleTop + " top-band corners, subframe " + subFail + " of " + frontTop + " top-band noses under 17)");
            Check(n >= 9000, "the sweep classified at least 9,000 incidents", n);
            Check(centreFront > 1500 && cfCorner == 0, "no corner parts (toe, tie rod, arm) from a centre-front hit", cfCorner + " of " + centreFront);
            Check(failUnder5 == 0 && over17 > 0 && over17Fail == over17, "no FAIL under 5 m/s; every hit at the 17 m/s retire line FAILs",
                  "under 5: " + failUnder5 + ", at 17+: " + over17Fail + "/" + over17);
            Check(knuckleTop > 50 && Mathf.Abs(knuckleFail / (float)knuckleTop - DamageRules.KnuckleP) < 0.08f &&
                  frontTop > 50 && Mathf.Abs(subFail / (float)frontTop - DamageRules.SubframeP) < 0.1f,
                  "the knuckle (20%) and subframe (50%) FAILs come up at their rates in the top bands",
                  (knuckleFail / (float)Mathf.Max(1, knuckleTop)).ToString("0.00") + " / " + (subFail / (float)Mathf.Max(1, frontTop)).ToString("0.00"));
            Check(dupRows == 0, "one row per part and zone inside an incident (no duplicates)", dupRows);
        }

        // ---------------------------------------------------------------
        static void TestDamageFail()
        {
            Line("R1a part 2 - the structural FAIL: the car cannot go on, the tow, banked once:");
            Check(DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZFront, 5, 41.2f, 41.2f, 0.9f)) == DamageRules.FailHit,
                  "the owner's tree (one 41 m/s FRONT pulse, PC33): FAIL whatever the dice");
            Check(DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZSide, 4, 9f, 17.5f, 0.9f)) == DamageRules.FailHit,
                  "17.5 m/s closing at the AI's retire line (delta-V capped lower by the other car's mass): FAIL");
            Check(DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZFront, 4, 9.9f, 9.9f, 0f)) == DamageRules.FailNone,
                  "a 9.9 m/s nose hit is not a FAIL, whatever the dice");
            Check(DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZFront, 5, 12f, 12f, 0.9f)) == DamageRules.FailNone &&
                  DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZFront, 5, 12f, 12f, 0.3f)) == DamageRules.FailSubframe,
                  "a 12 m/s nose hit: the subframe on its own die (50%)");
            Check(DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZCorner, 4, 9f, 9f, 0.1f)) == DamageRules.FailKnuckle &&
                  DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZCorner, 3, 7f, 7f, 0.1f)) == DamageRules.FailNone,
                  "a wheel struck at 8 m/s or more: the knuckle (20%); below the top band never");
            Check(DamageRules.FailOf(DmgFailInc(DamageModel.KRoll, DamageModel.ZRoof, 3, 6f, 0f, 0.9f)) == DamageRules.FailRoof &&
                  DamageRules.FailOf(DmgFailInc(DamageModel.KRoll, DamageModel.ZRoof, 2, 4f, 0f, 0.1f)) == DamageRules.FailNone,
                  "a roll that crushed the roof FAILs; a dented roof does not");
            DamageRules.FailOn = false;
            Check(DamageRules.FailOf(DmgFailInc(DamageModel.KHit, DamageModel.ZFront, 5, 41.2f, 41.2f, 0.9f)) == DamageRules.FailNone,
                  "PSX_FAULT_FAIL=0: no FAIL (part 1)");
            DamageRules.FailOn = true;

            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            var s = LifeRules.SeedNewGame("R1A2FAIL", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#r1a2fail");
            s.activeCar = owned.id;
            s.debugMode = false;
            var car = DmgBench(spec, out var temp, out var resp);
            try
            {
                DmgUseState(s);
                foreach (var p in DamageBank.Files()) System.IO.File.Delete(p);
                DmgCareerDrive(owned, spec);
                CrashStopGap.Arm(car);
                var L = resp.Damage;
                if (L == null) { Check(false, "a career drive arms the located ledger"); return; }
                L.DrawOverride = () => DmgDice(0.9f);
                L.PlantHit(DamageModel.ZFront, 0, -1, 6f, 10f);
                Check(L.Failed == DamageRules.FailNone && !DamageRules.Wrecked(car) && !CareerDrive.NeedsTow(car),
                      "a 6 m/s nose hit: the car drives on");
                var tree = L.PlantHit(DamageModel.ZFront, 0, -1, 41.2f, 30f, narrow: true);
                Check(L.Failed == DamageRules.FailHit && tree.fail == DamageRules.FailHit && DamageRules.Wrecked(car) && car.faultAccelMult == 0f,
                      "a 41 m/s tree: FAIL at the pulse, no power", "failed " + L.Failed + ", accel x" + car.faultAccelMult.ToString("0.00"));
                Check(CareerDrive.NeedsTow(car), "a career (not debug): the leave row is CALL TOW TRUCK (NeedsTow)");
                s.debugMode = true;
                Check(!CareerDrive.NeedsTow(car) && DamageRules.Wrecked(car), "a debug career: still wrecked, the leave row is the old TOW CAR HOME");
                s.debugMode = false;
                bool banked = CareerDrive.BankLeg(car, 3000f, 0f, "the check venue");
                int hist = owned.history.Count, again = DamageBank.FoldFiles(s);
                string day = s.calendarLog.Count > 0 ? s.calendarLog[s.calendarLog.Count - 1] : "";
                Check(banked && hist == 2 && again == 0 && owned.history.Count == 2,
                      "the tow / QUIT banks both incidents once; a second fold banks nothing", "history " + hist + ", again " + again);
                Check(day.Contains("DNF") && !DmgNamesPart(day), "the day's line says DNF and names no part", day);
            }
            finally
            {
                TuneDrop(car);
                RaceHandoff.ClearAll();
                DmgUseState(null);
            }
        }

        // ---------------------------------------------------------------
        static void TestDamageCrush()
        {
            Line("R1a part 2 - the crush order, the bar's stages, one row per part and zone (escalation):");
            var hi = DmgDice(0.95f);
            var b4 = DmgInc(DamageModel.KHit, DamageModel.ZFront, 0, -1, 4, hi);
            var b5 = DmgInc(DamageModel.KHit, DamageModel.ZFront, 0, -1, 5, hi);
            int Stage(Incident i, string id) => i.outcome.rows.Where(r => r.id == id).Select(r => r.stage).DefaultIfEmpty(0).Max();
            Check(Stage(b4, DamageModel.PanelId) == 3 && Stage(b4, DamageModel.BarId) == 2 &&
                  b4.outcome.rows.Count(r => r.id == DamageModel.LampId) == 2 && Stage(b4, DamageModel.RadiatorId) == 0,
                  "8 m/s square on, every die high: the cover and hood crushed, the bar pushed back, both lamps (the core on its die)",
                  b4.outcome.ToString());
            Check(Stage(b5, DamageModel.BarId) == 3 && Stage(b5, DamageModel.RadiatorId) == 3, "10 m/s: the bar torn away, the core crushed", b5.outcome.ToString());
            DamageRules.CrushOn = false;
            var p1 = DmgInc(DamageModel.KHit, DamageModel.ZFront, 0, -1, 5, hi);
            DamageRules.CrushOn = true;
            Check(!p1.outcome.HasRow(DamageModel.PanelId) && !p1.outcome.HasRow(DamageModel.BarId),
                  "PSX_FAULT_CRUSH=0: part 1's dice at every band (the PC33 tree: a 41 m/s nose with no crushed panel)", p1.outcome.ToString());

            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            var s = LifeRules.SeedNewGame("R1A2CRUSH", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#r1a2crush");
            s.activeCar = owned.id;
            var car = DmgBench(spec, out var temp, out var resp);
            try
            {
                DmgUseState(s);
                foreach (var p in DamageBank.Files()) System.IO.File.Delete(p);
                DmgCareerDrive(owned, spec);
                CrashStopGap.Arm(car);
                var L = resp.Damage;
                if (L == null) { Check(false, "a career drive arms the located ledger"); return; }
                L.DrawOverride = () => DmgDice(0.1f);
                L.PlantHit(DamageModel.ZFront, -1, -1, 3.5f, 10f);   // band 2 on the left of the nose
                DamageLedger.FoldNow(L);
                int bar1 = owned.faults.Where(f => f.id == DamageModel.BarId).Select(f => Mathf.RoundToInt(f.stage)).DefaultIfEmpty(0).Max();
                L.DrawOverride = () => DmgDice(0.95f);
                L.PlantHit(DamageModel.ZFront, 1, -1, 12f, 30f);    // a harder hit on the right of the nose
                DamageLedger.FoldNow(L);
                var bars = owned.faults.Where(f => f.id == DamageModel.BarId && f.zone1 == DamageModel.ZFront).ToList();
                var panels = owned.faults.Where(f => f.id == DamageModel.PanelId && f.zone1 == DamageModel.ZFront).ToList();
                Check(bar1 == 1 && bars.Count == 1 && Mathf.RoundToInt(bars[0].stage) == 3 && bars[0].hidden,
                      "a harder later hit raises the bar row the car carries (1 -> 3), one row, still hidden",
                      "first " + bar1 + ", rows " + bars.Count + (bars.Count > 0 ? ", stage " + bars[0].stage : ""));
                Check(panels.Count == 1 && Mathf.RoundToInt(panels[0].stage) == 3,
                      "one nose panel row whichever side took it, raised to the crushed stage", "rows " + panels.Count);
                var keys = owned.faults.Where(f => DamageModel.RowIds.Contains(f.id))
                                       .Select(f => f.id + "/" + f.zone1 + "/" + (f.id == DamageModel.LampId || (f.id == DamageModel.PanelId && f.zone1 == DamageModel.ZSide) ? f.hitSide : 0)).ToList();
                Check(keys.Count == keys.Distinct().Count(), "never a duplicate row (part and zone; lamps and doors per side)", string.Join(",", keys));
            }
            finally
            {
                TuneDrop(car);
                RaceHandoff.ClearAll();
                DmgUseState(null);
            }
        }

        // ---------------------------------------------------------------
        static void TestDamageSpan()
        {
            Line("R1a part 2 - multi-zone: a contact across the cowl line takes the glass and the panel under it:");
            var sh = new DamageModel.Shell();
            var nose = new Vector3(0f, sh.beltY + 0.12f, sh.half.z);
            DamageModel.Classify(sh, nose, Vector3.back, true, out int z, out _, out _);
            Check(z == DamageModel.ZRoof && DamageRules.SpanZone(sh, nose, Vector3.back, z) == DamageModel.ZFront,
                  "a van's tail on the nose above the beltline (PC33's RX-7): the ROOF and the FRONT", DamageModel.ZoneName(z));
            var tail = new Vector3(0.2f, sh.beltY + 0.1f, -sh.half.z);
            DamageModel.Classify(sh, tail, Vector3.forward, true, out int zt, out _, out _);
            Check(DamageRules.SpanZone(sh, tail, Vector3.forward, zt) == DamageModel.ZRear, "and a high hit on the tail: the REAR under it");
            var door = new Vector3(sh.half.x, sh.beltY + 0.1f, 0f);
            DamageModel.Classify(sh, door, Vector3.left, true, out int zd, out _, out _);
            var top = new Vector3(0f, sh.half.y, sh.half.z * 0.8f);
            Check(zd == DamageModel.ZRoof && DamageRules.SpanZone(sh, door, Vector3.left, zd) == DamageModel.ZNone &&
                  DamageRules.SpanZone(sh, top, Vector3.down, DamageModel.ZRoof) == DamageModel.ZNone,
                  "a side push above the belt is the glass alone; landing on the roof is the roof alone");
            DamageRules.SpanOn = false;
            Check(DamageRules.SpanZone(sh, nose, Vector3.back, z) == DamageModel.ZNone, "PSX_FAULT_SPAN=0: one zone per contact (part 1)");
            DamageRules.SpanOn = true;

            // a LOW car (the '99 Viper backed into a wall at 20 km/h read ROOF): the belt
            // just above the box centre, a wall patch centred above it but reaching below it
            var low = new DamageModel.Shell();
            low.beltY = 0.1f * low.half.y;
            var back = new Vector3(0.1f, 0.2f, -low.half.z);
            DamageModel.Classify(low, back, Vector3.forward, true, out int zw, out _, out _, -0.3f);
            DamageModel.Classify(low, back, Vector3.forward, true, out int zo, out _, out _, 0.12f);
            DamageModel.Classify(low, back, Vector3.forward, false, out int zu, out _, out _, -0.3f);
            DamageModel.Classify(low, new Vector3(low.half.x, 0.2f, 0f), Vector3.left, true, out int zs, out _, out _, -0.3f);
            DamageModel.Classify(low, new Vector3(0f, low.half.y, -1f), Vector3.down, true, out int zd2, out _, out _, -0.3f);
            Check(zw == DamageModel.ZRear && zs == DamageModel.ZSide && zo == DamageModel.ZRoof && zu == DamageModel.ZRoof && zd2 == DamageModel.ZRoof,
                  "a low car: a wall above its beltline that reaches below it is a REAR (SIDE) hit; an overhang wholly above the belt, " +
                  "the car on its roof, or a push from above is still ROOF",
                  DamageModel.ZoneName(zw) + " / " + DamageModel.ZoneName(zs) + " / " + DamageModel.ZoneName(zo) + " / " + DamageModel.ZoneName(zu) + " / " + DamageModel.ZoneName(zd2));
            DamageRules.BodyFirstOn = false;
            DamageModel.Classify(low, back, Vector3.forward, true, out int zp1, out _, out _, -0.3f);
            DamageRules.BodyFirstOn = true;
            Check(zp1 == DamageModel.ZRoof, "PSX_FAULT_BODYFIRST=0: part 1's ROOF");
        }

        // ---------------------------------------------------------------
        static void TestHeatSoak()
        {
            Line("R1a part 2 - heat soak: two legs in one block start warm; after an overnight, cold:");
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car"); return; }
            var s = LifeRules.SeedNewGame("R1A2SOAK", 25, LifeRules.DefaultJobIndex);
            var owned = DmgCar(s, spec, spec.id + "#r1a2soak");
            s.activeCar = owned.id;
            s.slotIndex = LifeRules.MorningSlot;
            float amb = 20f;
            HeatSoak.EndC = 96f;
            HeatSoak.Park(s, owned);
            Check(owned.parkedC == 96f && owned.parkedDay == s.day && owned.parkedSlot == s.slotIndex, "the apply-back parks the car hot (96 C)");
            float same = HeatSoak.StartC(owned.parkedC, HeatSoak.Keep(s, owned), amb);
            s.slotIndex++; float next = HeatSoak.StartC(owned.parkedC, HeatSoak.Keep(s, owned), amb);
            s.slotIndex++; float two = HeatSoak.StartC(owned.parkedC, HeatSoak.Keep(s, owned), amb);
            s.slotIndex = LifeRules.MorningSlot; s.day++;
            float overnight = HeatSoak.StartC(owned.parkedC, HeatSoak.Keep(s, owned), amb);
            s.day--;
            Check(same >= 70f && next >= 55f && two <= 22f && Mathf.Approximately(overnight, amb),
                  "same block warm, the next block (straight after) warm, two blocks on cold, after an overnight cold",
                  same.ToString("0") + " / " + next.ToString("0") + " / " + two.ToString("0") + " / " + overnight.ToString("0") + " C");

            var car = DmgBench(spec, out var temp, out var resp);
            try
            {
                DmgUseState(s);
                foreach (var p in DamageBank.Files()) System.IO.File.Delete(p);
                DmgCareerDrive(owned, spec);
                owned.parkedC = 0f;
                temp.celsius = 95f;
                CareerDrive.BankLeg(car, 2000f, 0f, "the check venue");   // stamps the needle, the apply-back parks it
                Check(Mathf.Abs(owned.parkedC - 95f) < 0.5f, "a real leg's apply-back parks it at the needle (EngineTemp.StampResult -> ApplyHeatResult)", owned.parkedC);
                RaceHandoff.FromLifeSim = true;
                HeatSoak.Prime(s, owned);
                temp.StartCold(amb);
                bool warm = HeatSoak.Warm(temp);
                float warmC = temp.celsius;
                s.day++;
                HeatSoak.Prime(s, owned);
                temp.StartCold(amb);
                bool cold = !HeatSoak.Warm(temp) && Mathf.Approximately(temp.celsius, amb);
                s.day--;
                Check(warm && warmC > 55f && cold, "the next leg starts warm through the applier's path (" + warmC.ToString("0") + " C); the morning after, cold");
                HeatSoak.On = false;
                HeatSoak.Prime(s, owned);
                temp.StartCold(amb);
                Check(!HeatSoak.Warm(temp) && Mathf.Approximately(temp.celsius, amb), "PSX_HEAT_SOAK=0: every drive starts cold");
                HeatSoak.On = true;
            }
            finally
            {
                TuneDrop(car);
                RaceHandoff.ClearAll();
                DmgUseState(null);
            }
        }

        // ---------------------------------------------------------------
        static void TestDamageHomes()
        {
            Line("R1a part 2 - homes for the six hit rows (shelf, floor, chips, value, debug lists):");
            if (!DamageHomes.On) { Check(false, "PSX_FAULT_HOMES is on for the test"); return; }
            SparePart Part(string label, string kind = "") => new SparePart { label = label, upgradeKind = kind };
            Check(Spares.Covers(Part("USED RADIATOR"), DamageModel.RadiatorId) && Spares.Covers(Part("FRONT CLIP"), DamageModel.BarId) &&
                  Spares.Covers(Part("STRAIGHT FENDER"), DamageModel.PanelId) && Spares.Covers(Part("TAIL LIGHT + TRIM"), DamageModel.LampId) &&
                  Spares.Covers(Part("EXHAUST, CAT BACK"), DamageModel.ExhaustId) && Spares.Covers(Part("SHELL PANELS, WHOLE CAR"), DamageModel.GlassId) &&
                  Spares.Covers(Part("PIPES", "power"), DamageModel.ExhaustId),
                  "Spares: a shelf part replaces each hit row");
            var w1 = FaultSymptoms.Puddle(new CarFault { id = DamageModel.RadiatorId, stage = 1f });
            var w3 = FaultSymptoms.Puddle(new CarFault { id = DamageModel.RadiatorId, stage = 3f });
            Check(System.Array.IndexOf(FaultSymptoms.FloorIds, DamageModel.RadiatorId) >= 0 && w1 != null && w3 != null &&
                  w1.Contains("green") && w3.Contains("dinner plate") && !DmgNamesPart(w1) && !DmgNamesPart(w3),
                  "the floor: a holed core drips green under the nose, by stage, naming nothing", w1 + " / " + w3);
            Check(FaultSymptoms.Showed(DamageModel.RadiatorId, FaultSymptoms.ChipHot, FaultSymptoms.CondHot) &&
                  FaultSymptoms.Showed(DamageModel.ExhaustId, FaultSymptoms.ChipRattle, FaultSymptoms.CondDrove) &&
                  FaultSymptoms.Showed(DamageModel.PanelId, FaultSymptoms.ChipRattle, FaultSymptoms.CondCruise) &&
                  FaultSymptoms.Showed(DamageModel.LampId, FaultSymptoms.ChipGauges, FaultSymptoms.CondDrove | FaultSymptoms.CondNight) &&
                  !FaultSymptoms.Showed(DamageModel.LampId, FaultSymptoms.ChipGauges, FaultSymptoms.CondDrove) &&
                  FaultSymptoms.CuesOf(DamageModel.BarId).Count == 0,
                  "NAME WHAT YOU FELT: runs hot, rattles, a lamp dark after dark; the hidden bar on no chip");
            var spec = CrashSpec("jpn", 1990);
            if (spec != null)
            {
                var s = LifeRules.SeedNewGame("R1A2HOME", 25, LifeRules.DefaultJobIndex);
                var c = DmgCar(s, spec, spec.id + "#r1a2home");
                int v0 = CarMarket.CarValue(c);
                c.faults.Add(new CarFault { id = DamageModel.PanelId, stage = 2f, mag = 4f, hidden = true });
                c.faults.Add(new CarFault { id = DamageModel.GlassId, stage = 2f, mag = 15f, hidden = true });
                c.faults.Add(new CarFault { id = DamageModel.BarId, stage = 1f, mag = DamageModel.BarCrushMult, hidden = true });
                int v1 = CarMarket.CarValue(c);
                Check(Mathf.Abs(v1 - v0 * 0.81f) <= 2f, "CarValue: the dented nose (-4%) and roof (-15%) a buyer can see; the hidden bar nothing", v0 + " -> " + v1);
            }
            var missing = DamageModel.RowIds.Where(id => string.IsNullOrEmpty(FaultCatalog.EffectSummary(id))).ToList();
            Check(missing.Count == 0, "the debug fault lists say what EVERY hit row does on the road",
                  string.Join(" | ", DamageModel.RowIds.Select(id => id + ": " + FaultCatalog.EffectSummary(id))));
        }
    }
}
