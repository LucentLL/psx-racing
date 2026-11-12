using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R0a CRASH STOP-GAP (Fault_System_Design_2026-11-11 §9-10): the origin
    /// families every roller reads, the stop-gap table itself, where a hit is
    /// located, the 500-crash sweep per origin (the round's gate), the
    /// apply-back writing what was rolled at the hit and nothing else, the
    /// live pull and radiator, and PSX_FAULT_CRASH=0 putting today back.
    /// RunCrash runs these alone (seconds) and writes
    /// PSXRacing_selftest_crash.txt and PSXRacing_crash_sweep.csv.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        public static void RunCrash()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            try { RunCrashR0aTests(); }
            finally { Edition.Simulate(editionWas); }
            Line(failures == 0 ? "CRASH SELF-TEST OK" : "CRASH SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_crash.txt"), log.ToString());
        }

        static void RunCrashR0aTests()
        {
            Guard(nameof(TestCrashOriginFamilies), TestCrashOriginFamilies);
            Guard(nameof(TestCrashTable), TestCrashTable);
            Guard(nameof(TestCrashLocate), TestCrashLocate);
            Guard(nameof(TestCrashSweep), TestCrashSweep);
            Guard(nameof(TestCrashApplyBack), TestCrashApplyBack);
            Guard(nameof(TestCrashLive), TestCrashLive);
            Guard(nameof(TestCrashSwitchOff), TestCrashSwitchOff);
        }

        static CrashStopGap.Draws CrashDraws(float bumper = 0.5f, float radiator = 0.5f, float align = 0.5f,
                                             float body = 0.5f, float paint = 0.5f, float radAmt = 0.5f) =>
            new CrashStopGap.Draws { bumper = bumper, radiator = radiator, align = align, body = body, paint = paint, radAmt = radAmt };

        static CarSpec CrashSpec(string origin, int minYear = 1973) =>
            CarCatalog.All.FirstOrDefault(c => c != null && c.origin == origin && !c.IsRaceCar && !c.IsRallyCar &&
                                               c.modelYear >= minYear);

        // ---------------------------------------------------------------
        static void TestCrashOriginFamilies()
        {
            Line("R0a crash - every roller reads its car's origin FAMILY:");
            var count = new SortedDictionary<string, int>();
            int unresolved = 0; string firstBad = null;
            foreach (var c in CarCatalog.All)
            {
                if (c == null) continue;
                string o = c.origin ?? "(none)";
                count[o] = count.TryGetValue(o, out int n) ? n + 1 : 1;
                if (!FaultCatalog.TryFamily(c.origin, out _)) { unresolved++; if (firstBad == null) firstBad = c.id + ":" + o; }
            }
            Check(CarCatalog.All.Count > 0 && unresolved == 0,
                  "every catalog origin resolves to a family - none takes the jpn fallback",
                  string.Join(", ", count.Select(kv => kv.Key + " x" + kv.Value + " -> " + FaultCatalog.Family(kv.Key))) +
                  (firstBad != null ? "; FIRST UNRESOLVED " + firstBad : ""));
            Check(FaultCatalog.Family("ger") == "eur" && FaultCatalog.Family("gbr") == "eur" &&
                  FaultCatalog.Family("fra") == "eur" && FaultCatalog.Family("ita") == "eur" &&
                  FaultCatalog.Family("eur") == "eur" && FaultCatalog.Family("usa") == "usa" &&
                  FaultCatalog.Family("jpn") == "jpn",
                  "ger, gbr, fra, ita and eur are eur; usa is usa; jpn is jpn");
            var missing = new List<string>();
            foreach (var fam in new[] { "jpn", "usa", "eur" })
                foreach (var lane in new[] { "engine", "tires", "hp" })
                    if (!FaultCatalog.Pools.Any(p => p.origin == fam && p.stat == lane)) missing.Add(fam + "/" + lane);
            Check(missing.Count == 0, "and every family's pool has rows in every lane", string.Join(" ", missing));

            // As RollThresholdFault makes it: NO origin passed.
            bool famWas = FaultCatalog.FamilyOn;
            try
            {
                FaultCatalog.FamilyOn = true;
                int checkedN = 0, eurN = 0;
                foreach (var org in new[] { "ger", "gbr", "fra", "ita" })
                {
                    var spec = CrashSpec(org, 0);
                    if (spec == null) continue;
                    var car = new OwnedCar { id = "fam_" + org, specId = spec.id, odoMiles = 200000f };
                    foreach (var lane in new[] { "engine", "tires", "hp" })
                        for (int i = 0; i < 6; i++)
                        {
                            car.faults.Clear();
                            var f = FaultCatalog.RollWearFault(car, lane, i % 2 == 1);
                            if (f == null) continue;
                            checkedN++;
                            var eurRow = FaultCatalog.Pools.FirstOrDefault(p => p.origin == "eur" && p.id == f.id);
                            if (eurRow != null && f.cost == Mathf.RoundToInt(eurRow.cost * 1.35f)) eurN++;
                        }
                }
                Check(checkedN > 0 && eurN == checkedN,
                      "a German, British, French or Italian car's wear roll (no origin passed) is the eur pool at eur prices",
                      eurN + "/" + checkedN);

                var used = FaultCatalog.RollUsedFaults(30, 160000f, "ita");
                bool allEur = used.All(f => FaultCatalog.UsedFaults.Any(u => u.origin == "eur" && u.id == f.id &&
                                                                         Mathf.RoundToInt(u.cost * 1.35f) == f.cost));
                Check(allEur, "and an Italian seller's car carries the eur used-car rows",
                      string.Join(",", used.Select(f => f.id)));
                var mint = FaultCatalog.MakeById("alignment", "gbr");
                Check(mint != null && mint.cost == Mathf.RoundToInt(70f * 1.35f),
                      "a British car's minted alignment is the eur row at the eur price", mint != null ? mint.cost : -1);

                FaultCatalog.FamilyOn = false;
                var gerSpec = CrashSpec("ger", 0);
                var gcar = new OwnedCar { id = "fam_off", specId = gerSpec != null ? gerSpec.id : "", odoMiles = 200000f };
                int jpnN = 0, offN = 0;
                for (int i = 0; i < 12; i++)
                {
                    gcar.faults.Clear();
                    var f = FaultCatalog.RollWearFault(gcar, i % 2 == 0 ? "engine" : "hp", false);
                    if (f == null) continue;
                    offN++;
                    var jRow = FaultCatalog.Pools.FirstOrDefault(p => p.origin == "jpn" && p.id == f.id);
                    if (jRow != null && f.cost == jRow.cost) jpnN++;
                }
                Check(offN > 0 && jpnN == offN, "PSX_FAULT_FAMILY=0: the German car rolls the Japanese pool, as before",
                      jpnN + "/" + offN);
            }
            finally { FaultCatalog.FamilyOn = famWas; }
        }

        // ---------------------------------------------------------------
        static void TestCrashTable()
        {
            Line("R0a crash - the stop-gap table (zone x band x bumper):");
            var P = CrashStopGap.Bumper.Plastic; var F5 = CrashStopGap.Bumper.FiveMph; var Ch = CrashStopGap.Bumper.Chrome;
            const int Fr = CrashStopGap.ZoneFront, Re = CrashStopGap.ZoneRear, Si = CrashStopGap.ZoneSide;
            var d = CrashDraws();
            CrashStopGap.Outcome R(int z, int s, float v, CrashStopGap.Bumper b, CrashStopGap.Draws dd) =>
                CrashStopGap.Resolve(z, s, v, b, dd);

            Check(R(Fr, 1, 1.0f, P, d).classes == CrashStopGap.Cls.None, "front under a 2.5-mph bumper's 1.1 m/s: nothing");
            var sc = R(Fr, 1, 2.0f, P, d);
            Check(sc.classes == CrashStopGap.Cls.Scuff && sc.paintLoss >= 2f && sc.paintLoss <= 5f,
                  "front 1.1-3 m/s: a scuff, paint -2..-5, no fault", sc);
            Check(R(Fr, 1, 2.0f, F5, d).classes == CrashStopGap.Cls.None && R(Fr, 1, 2.5f, F5, d).Has(CrashStopGap.Cls.Scuff),
                  "a 5-mph bumper shrugs off 2.2 m/s");
            Check(R(Re, 1, 1.4f, Ch, d).classes == CrashStopGap.Cls.None && R(Re, 1, 1.6f, Ch, d).Has(CrashStopGap.Cls.Scuff),
                  "chrome dents from 1.5 m/s");
            var crack = R(Fr, 1, 4f, P, CrashDraws(bumper: 0.69f));
            var miss = R(Fr, 1, 4f, P, CrashDraws(bumper: 0.71f));
            Check(crack.bumperId == "bumper_crack" && !crack.Has(CrashStopGap.Cls.Body) &&
                  miss.bumperId == null && miss.Has(CrashStopGap.Cls.Body) && Mathf.Approximately(miss.bodyLoss, 5f),
                  "front 3-6: bumper_crack 70%, otherwise a scuff and body -5", crack + " | " + miss);
            Check(R(Fr, 1, 4f, Ch, d).bumperId == "bumper_dent" && R(Re, 1, 4f, F5, d).bumperId == "bumper_dent",
                  "chrome and 5-mph bumpers dent instead (bumper_dent)");
            var rad = R(Fr, -1, 7f, P, CrashDraws(radiator: 0.59f));
            Check(rad.Has(CrashStopGap.Cls.Radiator) && rad.radiatorLoss >= 25f && rad.radiatorLoss <= 60f && rad.weep > 0f,
                  "front 6-10: radiator -25..-60 and weeping, 60%", rad);
            Check(!R(Fr, -1, 7f, P, CrashDraws(radiator: 0.61f)).Has(CrashStopGap.Cls.Radiator) &&
                  !R(Re, -1, 7f, P, CrashDraws(radiator: 0.01f)).Has(CrashStopGap.Cls.Radiator),
                  "not otherwise, and never from behind");
            var heavy = R(Fr, -1, 12f, P, CrashDraws(align: 0.39f, bumper: 0.9f));
            Check(heavy.Has(CrashStopGap.Cls.Alignment) && heavy.alignSide == -1 && heavy.alignSeverity == 1 &&
                  heavy.bodyLoss >= 25f && heavy.bodyLoss <= 45f,
                  "front 10+: body -20..-40 on top, and alignment 40% on the STRUCK side (minor)", heavy);
            Check(!R(Fr, -1, 12f, P, CrashDraws(align: 0.41f)).Has(CrashStopGap.Cls.Alignment),
                  "the other 60% keeps its alignment");
            Check(R(Re, 1, 16f, P, CrashDraws(align: 0.1f)).alignSeverity == 2 &&
                  R(Re, 1, 16f, P, CrashDraws(align: 0.1f)).alignSide == 1,
                  "rear 15+: severe, on the right when the right was hit");
            Check(R(Si, -1, 1.4f, P, d).classes == CrashStopGap.Cls.None && R(Si, -1, 2f, P, d).classes == CrashStopGap.Cls.Scuff,
                  "side under 1.5: nothing; 1.5-3: a scuff");
            var dent = R(Si, -1, 4f, P, d);
            Check(dent.Has(CrashStopGap.Cls.Body) && dent.bodyLoss >= 5f && dent.bodyLoss <= 15f &&
                  !dent.Has(CrashStopGap.Cls.Alignment) && dent.bumperId == null,
                  "side 3-6: a dent, body -5..-15, no row", dent);
            var sideHeavy = R(Si, 1, 7f, P, CrashDraws(align: 0.49f));
            Check(sideHeavy.bodyLoss >= 15f && sideHeavy.bodyLoss <= 30f && sideHeavy.alignSeverity == 1 && sideHeavy.alignSide == 1 &&
                  !R(Si, 1, 7f, P, CrashDraws(align: 0.51f)).Has(CrashStopGap.Cls.Alignment) &&
                  R(Si, 1, 10f, P, CrashDraws(align: 0.1f)).alignSeverity == 2,
                  "side 6+: body -15..-30 and alignment 50% on the struck side, severe from 9", sideHeavy);
            Check(CrashStopGap.ResolveBottom(0, 5.9f).classes == CrashStopGap.Cls.None &&
                  CrashStopGap.ResolveBottom(0, 6f).alignSide == -1 && CrashStopGap.ResolveBottom(0, 6f).alignSeverity == 1 &&
                  CrashStopGap.ResolveBottom(3, 9f).alignSide == 1 && CrashStopGap.ResolveBottom(3, 9f).alignSeverity == 2,
                  "a bump-stop strike of 6 m/s: alignment on that wheel's side, severe from 9");

            // Monotone: one zone, one set of dice, a harder hit never gives less.
            var rng = new System.Random(4242);
            int broken = 0; string firstBroken = null;
            for (int i = 0; i < 3000; i++)
            {
                int z = 1 + i % 3;
                var b = (CrashStopGap.Bumper)(i % 3);
                var dd = CrashStopGap.Draw(rng);
                float v1 = (float)rng.NextDouble() * 17f, v2 = v1 + (float)rng.NextDouble() * 6f;
                var o1 = R(z, 1, v1, b, dd); var o2 = R(z, 1, v2, b, dd);
                bool ok = (o1.classes & ~o2.classes) == 0 && o2.paintLoss >= o1.paintLoss - 1e-4f &&
                          o2.bodyLoss >= o1.bodyLoss - 1e-4f && o2.radiatorLoss >= o1.radiatorLoss - 1e-4f &&
                          o2.alignSeverity >= o1.alignSeverity;
                if (!ok) { broken++; if (firstBroken == null) firstBroken = z + "@" + v1.ToString("0.0") + "->" + v2.ToString("0.0"); }
            }
            Check(broken == 0, "a harder hit in the same zone with the same dice never does less (3000 pairs)", firstBroken);

            var m = CrashStopGap.Merge(heavy, CrashStopGap.ResolveBottom(3, 9.5f));
            Check(m.Has(CrashStopGap.Cls.Alignment) && m.alignSeverity == 2 && m.alignSide == 1 &&
                  Mathf.Approximately(m.bodyLoss, heavy.bodyLoss),
                  "two hits in one leg: the worse alignment wins, nothing un-happens", m);

            var camaro = CarCatalog.All.FirstOrDefault(c => c.origin == "usa" && c.modelYear > 0 && c.modelYear < 1973);
            var modern = CarCatalog.All.FirstOrDefault(c => c.origin == "jpn" && c.modelYear >= 1990);
            var fed = new CarSpec { origin = "usa", modelYear = 1978 };
            Check(CrashStopGap.BumperFor(camaro) == Ch && CrashStopGap.BumperFor(modern) == P &&
                  CrashStopGap.BumperFor(fed) == F5 && CrashStopGap.BumperFor(new CarSpec { origin = "jpn", modelYear = 1978 }) == P,
                  "bumpers by era and market: chrome before 1973, 5-mph on a US car of 1973-82, 2.5-mph otherwise",
                  (camaro != null ? camaro.id : "no pre-73 US car") + " / " + (modern != null ? modern.id : "-"));
            Check(Mathf.Approximately(CrashStopGap.PullDeg(1f), CrashStopGap.PullDegMinor) &&
                  Mathf.Approximately(CrashStopGap.PullDeg(2f), CrashStopGap.PullDegSevere) &&
                  CrashStopGap.PullDegSevere > CrashStopGap.PullDegMinor && CrashStopGap.PullDegSevere < 0.5f,
                  "the pull is road-wheel DEGREES by band, a fraction of a degree (the old fault was 0.25 of lock)",
                  CrashStopGap.PullDegMinor + " / " + CrashStopGap.PullDegSevere + " deg");
        }

        // ---------------------------------------------------------------
        static void TestCrashLocate()
        {
            Line("R0a crash - where a hit landed, in the car's own frame:");
            var half = new Vector3(0.88f, 0.65f, 2.2f);
            string Z(Vector3 p) { CrashStopGap.Classify(p, half, out int z, out int s); return CrashStopGap.ZoneName(z) + "/" + CrashStopGap.SideName(s); }
            Check(Z(new Vector3(-0.4f, 0f, 2.2f)) == "front/left" && Z(new Vector3(0.5f, 0.2f, 2.15f)) == "front/right",
                  "a nose hit left of the centreline is FRONT LEFT, right of it FRONT RIGHT");
            Check(Z(new Vector3(0.3f, 0f, -2.2f)) == "rear/right", "a tail hit is REAR");
            Check(Z(new Vector3(-0.88f, 0f, 1.0f)) == "side/left" && Z(new Vector3(0.88f, 0f, 1.9f)) == "side/right",
                  "a flank is SIDE, even near the front corner of a long car (normalised by the box)");
            Check(Z(new Vector3(-0.88f, 0f, 2.2f)) == "front/left", "an exact corner counts as the end, on its own side");
        }

        // ---------------------------------------------------------------
        static readonly string[] CrashRowIds = { "bumper_crack", "bumper_dent", "alignment" };

        struct SweepTally
        {
            public int crashes, rows, bumperRows, alignRows, faultOutcomes, bumperOutcomes;
            public int[] cls;
            public HashSet<string> ids;
            public int alignCost, bumperCost;
        }

        /// <summary>Bands per zone, the table's own edges (top capped at the
        /// AI retire line, 17 m/s).</summary>
        static float[] CrashBands(int group, CrashStopGap.Bumper b)
        {
            float r = CrashStopGap.RatingMps(b);
            switch (group)
            {
                case 0:
                case 1: return new[] { 0f, r, 3f, 6f, 10f, 17f };
                case 2: return new[] { 1.2f, 1.5f, 3f, 6f, 9f, 17f };
                default: return new[] { 1f, 6f, 9f, 17f };
            }
        }

        static float Frac(double x) => (float)(x - System.Math.Floor(x));

        /// <summary>
        /// One origin's sweep. <paramref name="groups"/>: 0 front, 1 rear,
        /// 2 side, 3 bump stop, equally many crashes each. Stratified: every
        /// band of every zone equally often, speeds and dice spread evenly
        /// (additive recurrences), so 500 crashes read the table, not the
        /// luck of a seed. <paramref name="random"/> uses a seeded random
        /// draw instead (the noise check).
        /// </summary>
        static SweepTally CrashSweep(List<CarSpec> specs, int[] groups, int crashes, bool random, int seed)
        {
            var t = new SweepTally { cls = new int[6], ids = new HashSet<string>() };
            var rng = new System.Random(seed);
            for (int i = 0; i < crashes; i++)
            {
                int g = groups[i % groups.Length];
                int k = i / groups.Length;
                var spec = specs[random ? rng.Next(specs.Count) : (k % specs.Count)];
                var bumper = CrashStopGap.BumperFor(spec);
                float[] bands = CrashBands(g, bumper);
                int nb = bands.Length - 1;
                int band = random ? rng.Next(nb) : k % nb;
                float u = random ? (float)rng.NextDouble() : Frac(0.5 + k * 0.7548776662);
                float mps = Mathf.Lerp(bands[band], bands[band + 1], u);
                int side = random ? (rng.Next(2) == 0 ? -1 : 1) : ((k / nb) % 2 == 0 ? -1 : 1);
                var dr = random ? CrashStopGap.Draw(rng) : new CrashStopGap.Draws
                {
                    paint = Frac(0.5 + k * 0.6180339887), bumper = Frac(0.5 + k * 0.4142135624),
                    body = Frac(0.5 + k * 0.7320508076), radiator = Frac(0.5 + k * 0.2360679775),
                    radAmt = Frac(0.5 + k * 0.6457513111), align = Frac(0.5 + k * 0.3166247904),
                };
                var o = g == 3 ? CrashStopGap.ResolveBottom(random ? rng.Next(4) : k % 4, mps)
                               : CrashStopGap.Resolve(g == 0 ? CrashStopGap.ZoneFront : g == 1 ? CrashStopGap.ZoneRear : CrashStopGap.ZoneSide,
                                                      side, mps, bumper, dr);
                t.crashes++;
                if (o.classes == CrashStopGap.Cls.None) t.cls[0]++;
                for (int c = 0; c < 5; c++) if (((int)o.classes & (1 << c)) != 0) t.cls[c + 1]++;
                // A repair outcome (the scuff is cosmetic, "not a fault").
                if (o.Has(CrashStopGap.Cls.Body)) t.faultOutcomes++;
                if (o.Has(CrashStopGap.Cls.Bumper)) { t.faultOutcomes++; t.bumperOutcomes++; }
                if (o.Has(CrashStopGap.Cls.Radiator)) t.faultOutcomes++;
                if (o.Has(CrashStopGap.Cls.Alignment)) t.faultOutcomes++;
                var car = new OwnedCar { id = "sweep", specId = spec.id };
                foreach (var f in CrashBank.NewRows(car, o))
                {
                    t.rows++;
                    t.ids.Add(f.id);
                    if (f.id == "alignment") { t.alignRows++; t.alignCost = f.cost; }
                    else if (f.id.StartsWith("bumper")) { t.bumperRows++; t.bumperCost = f.cost; }
                }
            }
            return t;
        }

        static void TestCrashSweep()
        {
            Line("R0a crash - THE SWEEP: 500 crashes per origin over the table's zones and bands:");
            bool famWas = FaultCatalog.FamilyOn;
            FaultCatalog.FamilyOn = true;
            var csv = new StringBuilder();
            csv.AppendLine("origin,family,spread,crashes,nothing,scuff,body,bumper,radiator,alignment,classes_seen," +
                           "rows,bumper_rows,alignment_rows,bumper_share_of_rows,fault_outcomes,bumper_share_of_outcomes," +
                           "row_ids,alignment_cost,bumper_cost");
            try
            {
                var origins = CarCatalog.All.Where(c => c != null).Select(c => c.origin).Distinct().OrderBy(o => o).ToList();
                int[] all4 = { 0, 1, 2, 3 }, walls = { 0, 1, 2 };
                int gateFails = 0;
                foreach (var org in origins)
                {
                    var specs = CarCatalog.All.Where(c => c != null && c.origin == org).ToList();
                    var t = CrashSweep(specs, all4, 500, false, 1);
                    var r = CrashSweep(specs, all4, 500, true, 20261112 + org.Sum(ch => (int)ch));
                    var w = CrashSweep(specs, walls, 500, false, 1);
                    int seen = t.cls.Count(x => x > 0);
                    float share = t.rows > 0 ? (float)t.bumperRows / t.rows : 0f;
                    float shareOut = t.faultOutcomes > 0 ? (float)t.bumperOutcomes / t.faultOutcomes : 0f;
                    bool noElectrical = t.ids.All(id => CrashRowIds.Contains(id)) && r.ids.All(id => CrashRowIds.Contains(id)) &&
                                        w.ids.All(id => CrashRowIds.Contains(id));
                    bool ok = seen >= 4 && share <= 0.5f && noElectrical;
                    if (!ok) gateFails++;
                    Check(ok, org + " (" + FaultCatalog.Family(org) + ", " + specs.Count + " cars): " + seen +
                              " of 6 classes, bumper rows " + t.bumperRows + "/" + t.rows + " = " + (share * 100f).ToString("0") +
                              "% of the fault rows, no electrical rows",
                          "nothing " + t.cls[0] + " scuff " + t.cls[1] + " body " + t.cls[2] + " bumper " + t.cls[3] +
                          " radiator " + t.cls[4] + " alignment " + t.cls[5] + "; rows " + string.Join("+", t.ids.OrderBy(x => x)) +
                          "; bumper share of all repair outcomes " + (shareOut * 100f).ToString("0") + "%; alignment $" + t.alignCost);
                    Line("    note " + org + " random-seed run: bumper rows " + r.bumperRows + "/" + r.rows + " = " +
                         (r.rows > 0 ? 100f * r.bumperRows / r.rows : 0f).ToString("0") + "%; walls only (no bump strikes): " +
                         w.bumperRows + "/" + w.rows + " = " + (w.rows > 0 ? 100f * w.bumperRows / w.rows : 0f).ToString("0") +
                         "% of rows, " + (w.faultOutcomes > 0 ? 100f * w.bumperOutcomes / w.faultOutcomes : 0f).ToString("0") +
                         "% of repair outcomes");
                    foreach (var (name, x) in new[] { ("stratified", t), ("random", r), ("walls", w) })
                        csv.AppendLine(string.Join(",", org, FaultCatalog.Family(org), name, x.crashes,
                            x.cls[0], x.cls[1], x.cls[2], x.cls[3], x.cls[4], x.cls[5], x.cls.Count(v => v > 0),
                            x.rows, x.bumperRows, x.alignRows,
                            (x.rows > 0 ? (float)x.bumperRows / x.rows : 0f).ToString("0.000"),
                            x.faultOutcomes, (x.faultOutcomes > 0 ? (float)x.bumperOutcomes / x.faultOutcomes : 0f).ToString("0.000"),
                            string.Join("|", x.ids.OrderBy(id => id)), x.alignCost, x.bumperCost));
                }
                Check(gateFails == 0, "THE GATE: every origin passes (>=4 of 6 classes, bumper <= 50% of fault rows, no electrical rows)",
                      gateFails + " origin(s) failed");
            }
            finally
            {
                FaultCatalog.FamilyOn = famWas;
                System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../PSXRacing_crash_sweep.csv"), csv.ToString());
            }
        }

        // ---------------------------------------------------------------
        static void TestCrashApplyBack()
        {
            Line("R0a crash - the apply-back writes what was rolled AT THE HIT, and rolls nothing:");
            bool was = CrashStopGap.On, famWas = FaultCatalog.FamilyOn;
            CrashStopGap.On = true; FaultCatalog.FamilyOn = true;
            try
            {
                var spec = CrashSpec("ger");
                if (spec == null) { Check(false, "a German road car from 1973 on to crash"); return; }
                var s = LifeRules.SeedNewGame("CRASH", 25, LifeRules.DefaultJobIndex);
                var car = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
                s.activeCar = car.id;
                car.faults.Clear();
                car.carHP = car.paint = car.radiator = 80f;

                // A 12 m/s nose-in on the left; the dice came up bumper, radiator, alignment.
                var o = CrashStopGap.Resolve(CrashStopGap.ZoneFront, -1, 12f, CrashStopGap.Bumper.Plastic,
                                             CrashDraws(bumper: 0.1f, radiator: 0.1f, align: 0.1f));
                void Stamp(float score, CrashStopGap.Outcome outcome, bool rolled)
                {
                    RaceHandoff.ClearAll();
                    RaceHandoff.FromLifeSim = true;
                    RaceHandoff.ResultReady = true;
                    RaceHandoff.CarId = car.id;
                    RaceHandoff.MetersDriven = 0f;
                    RaceHandoff.CommuteLeg = true;   // no block spent; the lanes are what is asserted
                    RaceHandoff.FreeRoam = true;
                    RaceHandoff.DamageScore = score;
                    RaceHandoff.WorstHitMps = 12f;
                    RaceHandoff.WorstHitZone = CrashStopGap.ZoneFront;
                    RaceHandoff.WorstHitSide = -1;
                    CrashStopGap.StampOutcome(rolled, outcome);
                }
                Stamp(12f * 1.6f, o, true);
                float hp0 = car.carHP, paint0 = car.paint, rad0 = car.radiator;
                LifeRules.ApplyRaceResult(s);
                var bump = car.faults.Find(f => f.id == "bumper_crack");
                var al = car.faults.Find(f => f.id == "alignment");
                Check(car.faults.Count == 2 && bump != null && al != null,
                      "the rows rolled at the hit are the rows on the car - bumper and alignment, nothing else",
                      string.Join(",", car.faults.Select(f => f.id)));
                Check(car.faults.All(f => f.hidden && !f.diagnosed),
                      "HIDDEN and undiagnosed: the car pulls and the bumper hangs, nobody has named a part");
                Check(al != null && al.severity == 1f && al.pullDir == -1,
                      "the alignment carries the band (minor) and the side (left, where it was hit)",
                      al != null ? al.severity + " / " + al.pullDir : "none");
                Check(al != null && al.cost == Mathf.RoundToInt(70f * 1.35f),
                      "priced as the German car's own family (eur)", al != null ? al.cost : -1);
                float hpWant = hp0 - 12f * 1.6f * LifeRules.BodyDamagePerHit - o.bodyLoss;
                float paintWant = paint0 - 12f * 1.6f * LifeRules.PaintDamagePerHit - o.paintLoss;
                Check(Mathf.Abs(car.carHP - hpWant) < 0.01f && Mathf.Abs(car.paint - paintWant) < 0.01f,
                      "body and paint: today's DamageScore summary, plus the located hit's own loss",
                      car.carHP.ToString("0.00") + " / " + car.paint.ToString("0.00"));
                Check(Mathf.Abs(car.radiator - (rad0 - o.radiatorLoss)) < 0.01f,
                      "the radiator comes home down by what the hit took", (rad0 - car.radiator).ToString("0.0"));

                // NO SECOND ROLL: the same stamp on twenty fresh cars, twenty identical cars.
                int same = 0;
                for (int i = 0; i < 20; i++)
                {
                    var c2 = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
                    c2.faults.Clear(); c2.carHP = c2.paint = c2.radiator = 80f;
                    Stamp(12f * 1.6f, o, true);
                    RaceHandoff.CarId = c2.id;
                    LifeRules.ApplyRaceResult(s);
                    if (c2.faults.Count == 2 && c2.faults.Any(f => f.id == "bumper_crack") &&
                        c2.faults.Any(f => f.id == "alignment" && f.pullDir == -1 && f.severity == 1f) &&
                        Mathf.Abs(c2.radiator - car.radiator) < 0.001f && Mathf.Abs(c2.carHP - car.carHP) < 0.001f) same++;
                }
                Check(same == 20, "the apply-back has no dice: one rolled hit banked twenty times is twenty identical cars", same + "/20");

                // The sum alone no longer makes a fault.
                var c3 = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
                c3.faults.Clear();
                Stamp(LifeRules.ImpactFaultThreshold * 3f, new CrashStopGap.Outcome(), false);
                RaceHandoff.CarId = c3.id;
                LifeRules.ApplyRaceResult(s);
                Check(c3.faults.Count == 0,
                      "DamageScore at three times the old threshold with no hit rolled adds NO fault (the leg sum is not a crash)",
                      c3.faults.Count);

                // The bump stop: the row comes from the strike, on its wheel's side.
                var c4 = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
                c4.faults.Clear();
                Stamp(0f, CrashStopGap.ResolveBottom(1, 7f), true);
                RaceHandoff.CarId = c4.id;
                RaceHandoff.BottomWorst = 7f;
                RaceHandoff.BottomWorstWheel = 2;
                LifeRules.ApplyRaceResult(s);
                var al4 = c4.faults.Find(f => f.id == "alignment");
                Check(c4.faults.Count == 1 && al4 != null && al4.pullDir == 1 && al4.severity == 1f,
                      "a 7 m/s strike on the front RIGHT: a minor alignment to the right, and no RG2 roll beside it",
                      string.Join(",", c4.faults.Select(f => f.id + ":" + f.pullDir)));

                // A worse hit later makes the same alignment worse, in place.
                Stamp(0f, CrashStopGap.Resolve(CrashStopGap.ZoneSide, 1, 10f, CrashStopGap.Bumper.Plastic, CrashDraws(align: 0.1f)), true);
                LifeRules.ApplyRaceResult(s);
                var al1 = car.faults.Where(f => f.id == "alignment").ToList();
                Check(al1.Count == 1 && al1[0].severity == 2f && al1[0].pullDir == 1,
                      "a worse hit later makes the car's one alignment worse - severe, now to the right", al1.Count);

                // The next drive: the request carries the pull in DEGREES.
                LifeHomeScreen.FillCarRequestFor(s, car);
                Check(Mathf.Approximately(RaceHandoff.SteerPullDeg, CrashStopGap.PullDegSevere) &&
                      Mathf.Abs(RaceHandoff.SteerPull) < 1e-6f,
                      "the next drive's request pulls in road-wheel degrees, and nothing in shares of lock",
                      RaceHandoff.SteerPullDeg + " deg / " + RaceHandoff.SteerPull);

                // Cleared like the rest of the result.
                Stamp(5f, o, true);
                RaceHandoff.BottomWorstWheel = 3;
                RaceHandoff.ClearResult();
                Check(!RaceHandoff.CrashRolled && RaceHandoff.CrashClasses == 0 && RaceHandoff.CrashBumperId == null &&
                      RaceHandoff.CrashAlignSeverity == 0 && RaceHandoff.WorstHitMps == 0f && RaceHandoff.WorstHitZone == 0 &&
                      RaceHandoff.WorstHitSide == 0 && RaceHandoff.BottomWorstWheel == 0 && RaceHandoff.CrashBodyLoss == 0f,
                      "ClearResult empties the located hit and the rolled outcome");
                RaceHandoff.SteerPullDeg = 0.5f;
                Stamp(5f, o, true);
                RaceHandoff.SteerPullDeg = 0.5f;
                RaceHandoff.ClearAll();
                Check(!RaceHandoff.CrashRolled && RaceHandoff.WorstHitMps == 0f && RaceHandoff.SteerPullDeg == 0f,
                      "and ClearAll empties them and the request's degree pull");
            }
            finally
            {
                CrashStopGap.On = was; FaultCatalog.FamilyOn = famWas;
                RaceHandoff.ClearAll();
            }
        }

        // ---------------------------------------------------------------
        static void TestCrashLive()
        {
            Line("R0a crash - live AT THE HIT: the pull, the radiator, the weep:");
            bool was = CrashStopGap.On;
            CrashStopGap.On = true;
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            var car = TuneBenchCar(spec);
            var temp = car.gameObject.AddComponent<EngineTemp>();
            temp.car = car;
            var resp = car.gameObject.AddComponent<CollisionResponder>();
            var host = new GameObject("CrashApplier") { hideFlags = HideFlags.HideAndDontSave };
            var applier = host.AddComponent<RaceHandoffApplier>();
            applier.playerCar = car;
            EngineTemp a = null, b = null, c = null;
            try
            {
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.CarSpecId = spec.id;
                RaceHandoff.RadiatorCond = 90f;
                temp.radiatorCond = 90f;
                Check(CrashStopGap.Arm(car) && resp.Ledger != null, "a career drive arms the player's ledger");
                var L = resp.Ledger;
                L.DrawOverride = () => CrashDraws(bumper: 0.1f, radiator: 0.1f, align: 0.1f, radAmt: 0.5f);

                L.Hit(CrashStopGap.ZoneFront, -1, 11f, CrashStopGap.Bumper.Plastic, 10f);
                var t1 = L.Total;
                Check(Mathf.Approximately(car.hitSteerPull, -CrashStopGap.PullDegMinor),
                      "the pull is on the car AT THE HIT, toward the struck (left) side, in degrees", car.hitSteerPull);
                Check(Mathf.Approximately(temp.radiatorCond, 90f - t1.radiatorLoss) &&
                      Mathf.Approximately(temp.radiatorHit, t1.radiatorLoss) && temp.radiatorWeep > 0f,
                      "and the radiator is down and weeping, live", temp.radiatorCond.ToString("0.0") + " weep " + temp.radiatorWeep.ToString("0.00"));

                L.Hit(CrashStopGap.ZoneFront, -1, 12f, CrashStopGap.Bumper.Plastic, 10.1f);
                Check(Mathf.Approximately(temp.radiatorHit, t1.radiatorLoss) && L.Rolls == 2 &&
                      Mathf.Approximately(car.hitSteerPull, -CrashStopGap.PullDegMinor),
                      "a harder contact of the SAME crash re-reads the same dice: one radiator hit, one pull");

                L.DrawOverride = () => CrashDraws(bumper: 0.9f, radiator: 0.9f, align: 0.1f);
                L.Hit(CrashStopGap.ZoneSide, 1, 10f, CrashStopGap.Bumper.Plastic, 15f);
                Check(Mathf.Approximately(car.hitSteerPull, CrashStopGap.PullDegSevere),
                      "a second, harder crash on the right: the pull goes severe, to the right", car.hitSteerPull);

                // The bench reapplies the request mid-drive: the crash stays.
                float radBefore = temp.radiatorCond;
                applier.ReapplyCar();
                Check(Mathf.Approximately(car.hitSteerPull, CrashStopGap.PullDegSevere) &&
                      Mathf.Approximately(temp.radiatorCond, radBefore),
                      "a bench reapply (ReapplyCar) neither straightens the pull nor mends the radiator",
                      car.hitSteerPull + " / " + temp.radiatorCond.ToString("0.0"));

                CrashStopGap.StampHandoff(resp);
                Check(RaceHandoff.CrashRolled && RaceHandoff.CrashAlignSeverity == 2 && RaceHandoff.CrashAlignSide == 1 &&
                      RaceHandoff.CrashBumperId == "bumper_crack" &&
                      Mathf.Approximately(RaceHandoff.CrashRadiatorLoss, t1.radiatorLoss),
                      "the exit stamp hands over exactly what was rolled", CrashBank.FromHandoff());

                // THE WEEP, integrated the way TestEngineTemp integrates: a
                // cracked core loses coolant at a cruise and climbs; the same run
                // with no weep is the sound car to the bit.
                a = Bench(rad: 90f - t1.radiatorLoss); a.radiatorWeep = t1.weep;
                b = Bench(rad: 90f); b.radiatorWeep = 0f;
                c = Bench(rad: 90f);
                float load = EngineTemp.Load(0.3f, 0.35f), over = EngineTemp.OverRev(0.3f, 0.35f);
                float hotAt = -1f, leakAt = -1f, at2 = 0f;
                for (int i = 0; i < 60 * 60 * 30; i++)
                {
                    a.Tick(1f / 60f, load, over, 80f); b.Tick(1f / 60f, load, over, 80f); c.Tick(1f / 60f, load, over, 80f);
                    if (i == 60 * 60 * 2) at2 = a.celsius;
                    if (leakAt < 0f && a.coolantPct < CoolingModel.CoolantFullAbove) leakAt = i / 3600f;
                    if (hotAt < 0f && a.celsius > EngineTemp.RedMark) hotAt = i / 3600f;
                }
                Check(b.celsius == c.celsius && b.coolantPct == c.coolantPct && b.PeakC == c.PeakC,
                      "no weep is the sound car to the bit (30 min at 80 km/h)",
                      b.celsius + " / " + c.celsius);
                Check(at2 < EngineTemp.RedMark && leakAt > 3f && a.coolantPct < b.coolantPct - 30f,
                      "with it (the hit's weep " + t1.weep.ToString("0.00") + "), the system drains at a cruise and the " +
                      "needle climbs minutes after the crash, not at it",
                      Mathf.RoundToInt(at2) + " C at 2 min; below 55% coolant at " +
                      (leakAt < 0f ? "never" : leakAt.ToString("0.0") + " min") + "; past " + EngineTemp.RedMark + " C at " +
                      (hotAt < 0f ? "never (30 min)" : hotAt.ToString("0.0") + " min") + "; coolant " +
                      Mathf.RoundToInt(a.coolantPct) + "% vs " + Mathf.RoundToInt(b.coolantPct) + "% sound");
            }
            finally
            {
                CrashStopGap.On = was;
                Kill(a); Kill(b); Kill(c);
                Object.DestroyImmediate(host);
                TuneDrop(car);
                RaceHandoff.ClearAll();
            }
        }

        // ---------------------------------------------------------------
        static void TestCrashSwitchOff()
        {
            Line("R0a crash - PSX_FAULT_CRASH=0 is today's game:");
            bool was = CrashStopGap.On, famWas = FaultCatalog.FamilyOn;
            var spec = CrashSpec("jpn", 1990);
            var car = spec != null ? TuneBenchCar(spec) : null;
            if (car != null) car.gameObject.AddComponent<CollisionResponder>();
            try
            {
                CrashStopGap.On = false; FaultCatalog.FamilyOn = false;
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                Check(car != null && !CrashStopGap.Arm(car) && car.GetComponent<CollisionResponder>().Ledger == null,
                      "no ledger is armed, so nothing is rolled at the hit");
                var owned = new OwnedCar { id = "off", specId = spec != null ? spec.id : "" };
                owned.faults.Add(FaultCatalog.MakeById("alignment", "jpn"));
                var agg = FaultCatalog.Aggregate_(owned);
                Check(Mathf.Abs(Mathf.Abs(agg.steerPull) - 0.25f) < 1e-4f && agg.steerPullDeg == 0f,
                      "an alignment fault pulls 0.25 of lock again", agg.steerPull);

                var s = LifeRules.SeedNewGame("CRASHOFF", 25, LifeRules.DefaultJobIndex);
                LifeRules.SeedFallbackCar(s);
                var c = s.ActiveCar;
                c.faults.Clear();
                int hits = 0;
                for (int i = 0; i < 10; i++)
                {
                    c.faults.Clear();
                    RaceHandoff.ClearAll();
                    RaceHandoff.ResultReady = true;
                    RaceHandoff.CommuteLeg = true;
                    RaceHandoff.FreeRoam = true;
                    RaceHandoff.CarId = c.id;
                    RaceHandoff.DamageScore = LifeRules.ImpactFaultThreshold * 3f;
                    LifeRules.ApplyRaceResult(s);
                    if (c.faults.Count > 0 && c.faults.All(f => f.id == "bumper_crack" || f.id == "alignment")) hits++;
                }
                Check(hits == 10, "and the leg's DamageScore past 22 rolls the Japanese impact pool - bumper_crack or alignment - as before",
                      hits + "/10");
            }
            finally
            {
                CrashStopGap.On = was; FaultCatalog.FamilyOn = famWas;
                if (car != null) TuneDrop(car);
                RaceHandoff.ClearAll();
            }
        }
    }
}
