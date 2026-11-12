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
    /// located, the 500-crash sweep per origin (the round's gate, walls only,
    /// with the bump-stop strikes swept on their own), every outcome banked
    /// through the real apply-back over a driven leg, the apply-back writing
    /// what was rolled at the hit and nothing else, ONE alignment (the live
    /// pull and the next drive's pull follow one rule), the live pull and
    /// radiator under the responder's own record rule, wear that never cracks
    /// a bumper, and each switch at 0 putting its own half of today back.
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
            // The rest of the suite sees the same UnityEngine.Random sequence
            // it saw before these were added.
            var randomWas = Random.state;
            try
            {
                Guard(nameof(TestCrashOriginFamilies), TestCrashOriginFamilies);
                Guard(nameof(TestCrashTable), TestCrashTable);
                Guard(nameof(TestCrashLocate), TestCrashLocate);
                Guard(nameof(TestCrashSweep), TestCrashSweep);
                Guard(nameof(TestCrashBankedLegs), TestCrashBankedLegs);
                Guard(nameof(TestCrashApplyBack), TestCrashApplyBack);
                Guard(nameof(TestCrashOneAlignment), TestCrashOneAlignment);
                Guard(nameof(TestCrashLive), TestCrashLive);
                Guard(nameof(TestCrashWearNoBumper), TestCrashWearNoBumper);
                Guard(nameof(TestCrashSwitchOff), TestCrashSwitchOff);
            }
            finally { Random.state = randomWas; }
        }

        static CrashStopGap.Draws CrashDraws(float bumper = 0.5f, float radiator = 0.5f, float align = 0.5f,
                                             float body = 0.5f, float paint = 0.5f, float radAmt = 0.5f) =>
            new CrashStopGap.Draws { bumper = bumper, radiator = radiator, align = align, body = body, paint = paint, radAmt = radAmt };

        static CarSpec CrashSpec(string origin, int minYear = 1973) =>
            CarCatalog.All.FirstOrDefault(c => c != null && c.origin == origin && !c.IsRaceCar && !c.IsRallyCar &&
                                               c.modelYear >= minYear);

        /// <summary>A seed per origin that two origins cannot share (FNV-1a of
        /// the name; the sum of its letters gave ger and ita the same one).</summary>
        static int StableSeed(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char ch in s ?? "") { h ^= ch; h *= 16777619u; }
                return (int)(h & 0x7fffffffu);
            }
        }

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
            // A CENTRED hit has no struck side: no alignment, whatever the dice.
            var centred = R(Fr, 0, 16f, P, CrashDraws(align: 0.01f));
            Check(!centred.Has(CrashStopGap.Cls.Alignment) && !R(Re, 0, 12f, P, CrashDraws(align: 0.01f)).Has(CrashStopGap.Cls.Alignment) &&
                  centred.Has(CrashStopGap.Cls.Body) && centred.Has(CrashStopGap.Cls.Radiator),
                  "a heavy hit centred on the nose or tail (side 0) rolls everything else but NO alignment - no struck side, no pull",
                  centred);
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

            // THE LEG'S OUTCOME IS THE WORST OF WHAT WAS ROLLED, never a sum.
            var m = CrashStopGap.Merge(heavy, CrashStopGap.ResolveBottom(3, 9.5f));
            Check(m.Has(CrashStopGap.Cls.Alignment) && m.alignSeverity == 2 && m.alignSide == 1 &&
                  Mathf.Approximately(m.bodyLoss, heavy.bodyLoss),
                  "two hits in one leg: the worse alignment wins, nothing un-happens", m);
            var radA = R(Fr, -1, 6.5f, P, CrashDraws(radiator: 0.1f, radAmt: 0.9f));
            var radB = R(Fr, -1, 7f, P, CrashDraws(radiator: 0.1f, radAmt: 0.2f));
            var ab = CrashStopGap.Merge(radA, radB);
            var ba = CrashStopGap.Merge(radB, radA);
            Check(Mathf.Approximately(ab.radiatorLoss, Mathf.Max(radA.radiatorLoss, radB.radiatorLoss)) &&
                  Mathf.Approximately(ab.radiatorLoss, ba.radiatorLoss) && ab.radiatorLoss <= 60f &&
                  Mathf.Approximately(ab.paintLoss, Mathf.Max(radA.paintLoss, radB.paintLoss)) &&
                  Mathf.Approximately(ab.weep, ba.weep),
                  "two radiator rolls in one leg: the radiator takes the WORSE (never the sum past the table's 60), in either order",
                  radA.radiatorLoss.ToString("0") + " + " + radB.radiatorLoss.ToString("0") + " -> " + ab.radiatorLoss.ToString("0"));
            var minorL = R(Fr, -1, 11f, P, CrashDraws(align: 0.1f));
            var minorR = R(Re, 1, 12f, P, CrashDraws(align: 0.1f));
            var severeL = R(Si, -1, 10f, P, CrashDraws(align: 0.1f));
            Check(CrashStopGap.Merge(minorL, minorR).alignSide == 1 && CrashStopGap.Merge(minorR, minorL).alignSide == -1 &&
                  CrashStopGap.Merge(severeL, minorR).alignSide == -1 && CrashStopGap.Merge(severeL, minorR).alignSeverity == 2,
                  "a band tie goes to the LATER hit (minor left then minor right pulls right); a worse band stays");

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
            Check(Z(new Vector3(0.01f, 0f, 2.2f)) == "front/centre" && Z(new Vector3(-0.01f, 0f, 2.2f)) == "front/centre" &&
                  Z(new Vector3(0f, 0f, 2.2f)) == "front/centre" && Z(new Vector3(0.1f, 0f, -2.2f)) == "rear/centre",
                  "a hit centred on the nose or tail (inside " + CrashStopGap.CentreBand.ToString("0.0#") +
                  " of the half-width) has NO side - not a coin flip from rounding noise");

            // THE PATCH, not the impulse. Synthetic box-on-wall manifolds: the
            // four corners of the overlap on the nose, the impulse piled onto
            // the wall's INNER edge the way the solver piles it (the crash play
            // check's W1 read the impulse-weighted point there, -0.27 m).
            float hx = half.x, nose = half.z;
            string Wall(float fromX, float toX, out float weightedX)
            {
                var pts = new[] { new Vector3(fromX, -0.3f, nose), new Vector3(fromX, 0.3f, nose),
                                  new Vector3(toX, -0.3f, nose), new Vector3(toX, 0.3f, nose) };
                float[] imp = { 0.05f, 0.05f, 1f, 1f };   // the inner edge carries it
                Vector3 wsum = Vector3.zero; float wt = 0f;
                for (int i = 0; i < 4; i++) { wsum += pts[i] * imp[i]; wt += imp[i]; }
                weightedX = (wsum / wt).x;
                return Z(CrashStopGap.PatchCentre(pts, 4));
            }
            string w35 = Wall(-hx, -0.3f * hx, out float i35);
            string w60 = Wall(-hx, 0.2f * hx, out float i60);
            string w75 = Wall(-hx, 0.5f * hx, out float i75);
            string wFull = Wall(-hx, hx, out _);
            Check(w35 == "front/left" && w60 == "front/left" && w75 == "front/left",
                  "a wall over the left 35%, 60% or 75% of the nose is FRONT LEFT - read from the patch, where the impulse-weighted " +
                  "point would sit on the wall's inner edge (x " + i60.ToString("+0.00;-0.00") + " at 60%, " + i75.ToString("+0.00;-0.00") +
                  " at 75%: the WRONG side)", w35 + " " + w60 + " " + w75);
            Check(wFull == "front/centre", "a wall across the whole nose is centred: no side", wFull);
            var pole = new[] { new Vector3(0.45f, -0.2f, nose), new Vector3(0.52f, 0.2f, nose) };
            Check(Z(CrashStopGap.PatchCentre(pole, 2)) == "front/right" && CrashStopGap.PatchCentre(null, 0) == Vector3.zero,
                  "a narrow patch (a pole off the right of the nose) is where it is: FRONT RIGHT");
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
        /// Crash number <paramref name="i"/> of a sweep. <paramref name="groups"/>:
        /// 0 front, 1 rear, 2 side, 3 bump stop, equally many crashes each.
        /// Stratified: every band of every zone equally often, speeds and dice
        /// spread evenly (additive recurrences), so 500 crashes read the table,
        /// not the luck of a seed; <paramref name="rng"/> non-null draws them
        /// at random instead (the noise check). Off-centre: the struck side
        /// alternates left and right (the centred hit has its own check).
        /// </summary>
        static CrashStopGap.Outcome CrashCase(List<CarSpec> specs, int[] groups, int i, System.Random rng,
                                              out CarSpec spec, out float mps, out int group)
        {
            bool random = rng != null;
            group = groups[i % groups.Length];
            int k = i / groups.Length;
            spec = specs[random ? rng.Next(specs.Count) : (k % specs.Count)];
            var bumper = CrashStopGap.BumperFor(spec);
            float[] bands = CrashBands(group, bumper);
            int nb = bands.Length - 1;
            int band = random ? rng.Next(nb) : k % nb;
            float u = random ? (float)rng.NextDouble() : Frac(0.5 + k * 0.7548776662);
            mps = Mathf.Lerp(bands[band], bands[band + 1], u);
            int side = random ? (rng.Next(2) == 0 ? -1 : 1) : ((k / nb) % 2 == 0 ? -1 : 1);
            var dr = random ? CrashStopGap.Draw(rng) : new CrashStopGap.Draws
            {
                paint = Frac(0.5 + k * 0.6180339887), bumper = Frac(0.5 + k * 0.4142135624),
                body = Frac(0.5 + k * 0.7320508076), radiator = Frac(0.5 + k * 0.2360679775),
                radAmt = Frac(0.5 + k * 0.6457513111), align = Frac(0.5 + k * 0.3166247904),
            };
            return group == 3 ? CrashStopGap.ResolveBottom(random ? rng.Next(4) : k % 4, mps)
                              : CrashStopGap.Resolve(group == 0 ? CrashStopGap.ZoneFront : group == 1 ? CrashStopGap.ZoneRear : CrashStopGap.ZoneSide,
                                                     side, mps, bumper, dr);
        }

        static SweepTally CrashSweep(List<CarSpec> specs, int[] groups, int crashes, System.Random rng)
        {
            var t = new SweepTally { cls = new int[6], ids = new HashSet<string>() };
            for (int i = 0; i < crashes; i++)
            {
                var o = CrashCase(specs, groups, i, rng, out var spec, out _, out _);
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

        static float Pct(int a, int b) => b > 0 ? 100f * a / b : 0f;

        static void TestCrashSweep()
        {
            Line("R0a crash - THE SWEEP: 500 crashes per origin, WALLS ONLY (front, rear, side; every band equally):");
            bool famWas = FaultCatalog.FamilyOn;
            FaultCatalog.FamilyOn = true;
            var csv = new StringBuilder();
            csv.AppendLine("origin,family,spread,crashes,nothing,scuff,body,bumper,radiator,alignment,classes_seen," +
                           "rows,bumper_rows,alignment_rows,bumper_share_of_rows,fault_outcomes,bumper_share_of_outcomes," +
                           "row_ids,alignment_cost,bumper_cost");
            try
            {
                var origins = CarCatalog.All.Where(c => c != null).Select(c => c.origin).Distinct().OrderBy(o => o).ToList();
                int[] walls = { 0, 1, 2 }, all4 = { 0, 1, 2, 3 }, bumps = { 3 };
                int gateFails = 0, rowsOver = 0;
                float rowsMin = 1f, rowsMax = 0f, rRowsMin = 1f, rRowsMax = 0f;
                foreach (var org in origins)
                {
                    var specs = CarCatalog.All.Where(c => c != null && c.origin == org).ToList();
                    var w = CrashSweep(specs, walls, 500, null);
                    var r = CrashSweep(specs, walls, 500, new System.Random(20261112 ^ StableSeed(org)));
                    var b = CrashSweep(specs, bumps, 500, null);
                    var mix = CrashSweep(specs, all4, 500, null);
                    int seen = w.cls.Count(x => x > 0);
                    float outShare = Pct(w.bumperOutcomes, w.faultOutcomes), rOut = Pct(r.bumperOutcomes, r.faultOutcomes);
                    float rowShare = Pct(w.bumperRows, w.rows), rRows = Pct(r.bumperRows, r.rows);
                    bool tableRows = w.ids.All(id => CrashRowIds.Contains(id)) && r.ids.All(id => CrashRowIds.Contains(id)) &&
                                     b.ids.All(id => id == "alignment");
                    bool ok = seen >= 4 && outShare <= 50f && rOut <= 50f && tableRows;
                    if (!ok) gateFails++;
                    if (rowShare > 50f) rowsOver++;
                    rowsMin = Mathf.Min(rowsMin, rowShare / 100f); rowsMax = Mathf.Max(rowsMax, rowShare / 100f);
                    rRowsMin = Mathf.Min(rRowsMin, rRows / 100f); rRowsMax = Mathf.Max(rRowsMax, rRows / 100f);
                    Check(ok, org + " (" + FaultCatalog.Family(org) + ", " + specs.Count + " cars): " + seen +
                              " of 6 classes, bumpers " + w.bumperOutcomes + "/" + w.faultOutcomes + " = " + outShare.ToString("0") +
                              "% of the REPAIR OUTCOMES (random-seed run " + rOut.ToString("0") + "%), and the table writes only bumper and alignment rows",
                          "nothing " + w.cls[0] + " scuff " + w.cls[1] + " body " + w.cls[2] + " bumper " + w.cls[3] +
                          " radiator " + w.cls[4] + " alignment " + w.cls[5] + "; rows " + string.Join("+", w.ids.OrderBy(x => x)) +
                          "; alignment $" + w.alignCost);
                    Line("    DECISION " + org + ": counted as FAULT ROWS the same walls give bumpers " + w.bumperRows + "/" + w.rows +
                         " = " + rowShare.ToString("0") + "% (random-seed run " + rRows.ToString("0") + "%) - OVER 50%");
                    Line("    note " + org + ": with a quarter of the 500 made bump-stop strikes (the earlier mix) bumpers are " +
                         Pct(mix.bumperRows, mix.rows).ToString("0") + "% of rows; the bump strikes alone write " + b.rows +
                         " rows, all alignment");
                    foreach (var (name, x) in new[] { ("walls", w), ("walls_random", r), ("bump_stops", b), ("quarter_bumps", mix) })
                        csv.AppendLine(string.Join(",", org, FaultCatalog.Family(org), name, x.crashes,
                            x.cls[0], x.cls[1], x.cls[2], x.cls[3], x.cls[4], x.cls[5], x.cls.Count(v => v > 0),
                            x.rows, x.bumperRows, x.alignRows,
                            (x.rows > 0 ? (float)x.bumperRows / x.rows : 0f).ToString("0.000"),
                            x.faultOutcomes, (x.faultOutcomes > 0 ? (float)x.bumperOutcomes / x.faultOutcomes : 0f).ToString("0.000"),
                            string.Join("|", x.ids.OrderBy(id => id)), x.alignCost, x.bumperCost));
                }
                Check(gateFails == 0,
                      "THE GATE, walls only, read as REPAIR OUTCOMES: every origin >= 4 of 6 classes, bumpers <= 50%, table rows only " +
                      "(banked over a driven leg with no electrical rows: the next test)", gateFails + " origin(s) failed");
                string Range(float lo, float hi) => Mathf.RoundToInt(lo * 100f) == Mathf.RoundToInt(hi * 100f)
                    ? (hi * 100f).ToString("0") + "%" : (lo * 100f).ToString("0") + "-" + (hi * 100f).ToString("0") + "%";
                Line("  DECISION (coordinator / owner): read as FAULT ROWS the gate FAILS - bumpers are " + Range(rowsMin, rowsMax) +
                     " of rows on the stratified sweep (" + Range(rRowsMin, rRowsMax) + " on the random-seed runs), over 50% in " +
                     rowsOver + " of " + origins.Count + " origins. The table's 70% bumper " +
                     "roll on every front/rear hit from 3 m/s, against alignment rows only from 10 m/s (6 on a flank) and no row for a " +
                     "dent or a radiator, cannot bring wall contact under 50% of rows. Which denominator the design's \"bumper rows at " +
                     "most 50% of the faults\" means is the decision.");

                // THE BUMP STOPS, on their own: certain, on the struck wheel's
                // side, minor from 6 m/s and severe from 9, nothing else.
                int strikes = 0, strikeBad = 0; string firstStrike = null;
                foreach (var org in new[] { "jpn", "usa", "eur" })
                {
                    var spec = CarCatalog.All.FirstOrDefault(c => c != null && FaultCatalog.Family(c.origin) == org);
                    for (int i = 0; i < 500; i++)
                    {
                        int wheel = i % 4;
                        float mps = 17f * Frac(0.5 + i * 0.7548776662);
                        var o = CrashStopGap.ResolveBottom(wheel, mps);
                        var rows = CrashBank.NewRows(new OwnedCar { id = "strike", specId = spec != null ? spec.id : "" }, o);
                        bool want = mps >= CrashStopGap.BottomMps;
                        bool good = want
                            ? rows.Count == 1 && rows[0].id == "alignment" && rows[0].pullDir == (wheel % 2 == 0 ? -1 : 1) &&
                              Mathf.Approximately(rows[0].severity, mps >= CrashStopGap.BottomSevereMps ? 2f : 1f) &&
                              o.classes == CrashStopGap.Cls.Alignment
                            : rows.Count == 0 && o.classes == CrashStopGap.Cls.None;
                        strikes++;
                        if (!good) { strikeBad++; if (firstStrike == null) firstStrike = org + " wheel " + wheel + " @ " + mps.ToString("0.0"); }
                    }
                }
                Check(strikeBad == 0,
                      "THE BUMP STOPS (their own sweep, " + strikes + " strikes): from 6 m/s exactly one alignment row, on the struck " +
                      "wheel's side, severe from 9; below 6 nothing", firstStrike);
            }
            finally
            {
                FaultCatalog.FamilyOn = famWas;
                System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../PSXRacing_crash_sweep.csv"), csv.ToString());
            }
        }

        // ---------------------------------------------------------------
        /// <summary>
        /// The sweep's outcomes BANKED through LifeRules.ApplyRaceResult over a
        /// driven leg (5 km), every origin, on a car whose body and paint start
        /// sound (70) and whose engine and tyres start new (a 5 km leg at
        /// 40,000 miles takes about 44 off the tyre lane - TireWearPerM - and
        /// from 70 that rolls the tyre lane's own wear, which is not the
        /// crash's): what a crash leg adds must be exactly the rows rolled at
        /// the hit - in particular, the body lane's wear roll must not fire off
        /// the crash's own dent (a European car's body pool is half electrical).
        /// </summary>
        static void TestCrashBankedLegs()
        {
            Line("R0a crash - every swept outcome BANKED through the real apply-back over a driven 5 km leg:");
            bool was = CrashStopGap.On, famWas = FaultCatalog.FamilyOn;
            CrashStopGap.On = true; FaultCatalog.FamilyOn = true;
            Random.InitState(20261112);
            try
            {
                var origins = CarCatalog.All.Where(c => c != null).Select(c => c.origin).Distinct().OrderBy(o => o).ToList();
                int legs = 0, bad = 0, under40 = 0, eurUnder40 = 0;
                float oldRows = 0f, oldEurRows = 0f;
                var added = new HashSet<string>();
                string firstBad = null;
                int[] groups = { 0, 1, 2, 3 };
                foreach (var org in origins)
                {
                    var specs = CarCatalog.All.Where(c => c != null && c.origin == org && !c.IsRaceCar && !c.IsRallyCar).ToList();
                    if (specs.Count == 0) specs = CarCatalog.All.Where(c => c != null && c.origin == org).ToList();
                    var s = LifeRules.SeedNewGame("BANK", 25, LifeRules.DefaultJobIndex);
                    var car = CarMarket.MakeOwnedCar(s, specs[0], 70, 40000f, specs[0].price);
                    s.activeCar = car.id;
                    for (int i = 0; i < 500; i++)
                    {
                        var o = CrashCase(specs, groups, i, null, out var spec, out float mps, out int group);
                        car.specId = spec.id;
                        car.faults.Clear();
                        car.engine = car.tires = 100f;
                        car.carHP = car.paint = 70f;
                        CoolingModel.Seed(car, 70f);
                        car.odoMiles = 40000f;
                        car.fuel = 100f;
                        var want = CrashBank.NewRows(car, o).Select(f => f.id).OrderBy(x => x).ToList();
                        RaceHandoff.ClearAll();
                        RaceHandoff.FromLifeSim = true;
                        RaceHandoff.ResultReady = true;
                        RaceHandoff.CarId = car.id;
                        RaceHandoff.MetersDriven = 5000f;
                        RaceHandoff.CommuteLeg = true;   // no block spent
                        RaceHandoff.FreeRoam = true;
                        RaceHandoff.DamageScore = group == 3 ? 0f : mps * 1.6f;   // one hard contact's summary
                        RaceHandoff.WorstHitMps = group == 3 ? 0f : mps;
                        if (group == 3) RaceHandoff.BottomWorst = mps;
                        CrashStopGap.StampOutcome(true, o);
                        LifeRules.ApplyRaceResult(s);
                        var got = car.faults.Select(f => f.id).OrderBy(x => x).ToList();
                        legs++;
                        foreach (var id in got) added.Add(id);
                        if (!got.SequenceEqual(want))
                        {
                            bad++;
                            if (firstBad == null) firstBad = org + " " + o + ": got " + string.Join(",", got) + ", rolled " + string.Join(",", want);
                        }
                        if (car.carHP < LifeRules.WearFaultBelow)
                        {
                            // What the old order (the wear roll read AFTER the
                            // crash) would have risked on this leg.
                            float p = LifeRules.WearFaultChance(car.carHP, 5000f);
                            under40++; oldRows += p;
                            if (FaultCatalog.Family(org) == "eur") { eurUnder40++; oldEurRows += p; }
                        }
                    }
                }
                Check(bad == 0,
                      "the rows a crash leg adds are exactly the rows rolled at the hit - no wear roll fires off the crash's own dent (" +
                      legs + " legs, " + origins.Count + " origins, a quarter of them bump strikes)", firstBad);
                Check(added.All(id => CrashRowIds.Contains(id)),
                      "so no crash leg adds an electrical row - or any row but bumper and alignment - European cars included",
                      string.Join(",", added.OrderBy(x => x)));
                Line("    note " + under40 + " of " + legs + " legs left the body under 40 (" + eurUnder40 + " of them European); read after " +
                     "the crash, as before, the body-lane wear roll would have added about " + oldRows.ToString("0.0") + " rows on those " +
                     "legs (" + oldEurRows.ToString("0.0") + " on European cars, half of them electrical)");
            }
            finally
            {
                CrashStopGap.On = was; FaultCatalog.FamilyOn = famWas;
                RaceHandoff.ClearAll();
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

                // A TIE later moves it to the newer hit's side; a lesser band does not.
                Stamp(0f, CrashStopGap.ResolveBottom(0, 9.5f), true);   // severe, front LEFT wheel
                LifeRules.ApplyRaceResult(s);
                var al2 = car.faults.Where(f => f.id == "alignment").ToList();
                Stamp(0f, CrashStopGap.ResolveBottom(1, 7f), true);     // minor, front RIGHT wheel
                LifeRules.ApplyRaceResult(s);
                var al3 = car.faults.Where(f => f.id == "alignment").ToList();
                Check(al2.Count == 1 && al2[0].severity == 2f && al2[0].pullDir == -1 &&
                      al3.Count == 1 && al3[0].severity == 2f && al3[0].pullDir == -1,
                      "a band tie later goes to the newer hit (severe right -> severe left); a lesser band later changes nothing",
                      (al2.Count > 0 ? al2[0].severity + "/" + al2[0].pullDir : "-") + " then " +
                      (al3.Count > 0 ? al3[0].severity + "/" + al3[0].pullDir : "-"));

                // The next drive: the request carries the pull in DEGREES.
                LifeHomeScreen.FillCarRequestFor(s, car);
                Check(Mathf.Approximately(RaceHandoff.SteerPullDeg, -CrashStopGap.PullDegSevere) &&
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
        /// <summary>
        /// ONE ALIGNMENT. The car may already carry one (a legacy row in the
        /// owner's save, a used-car row, an earlier strike). The pull felt
        /// after a crash (CarController: CrashStopGap.CombinePull of the
        /// carried pull and this drive's) must be the pull the next drive
        /// starts with (CrashBank.ApplyOutcome, then Aggregate_) - for every
        /// carried alignment and every crash alignment.
        /// </summary>
        static void TestCrashOneAlignment()
        {
            Line("R0a crash - ONE alignment: the pull after the crash is the pull the next drive starts with:");
            bool was = CrashStopGap.On, famWas = FaultCatalog.FamilyOn;
            CrashStopGap.On = true; FaultCatalog.FamilyOn = true;
            try
            {
                var spec = CrashSpec("jpn", 1990);
                int n = 0, bad = 0; string firstBad = null;
                var carried = new List<(int band, int side)> { (0, 0), (1, -1), (1, 1), (2, -1), (2, 1) };
                foreach (var (cb, cs) in carried)
                    foreach (int hb in new[] { 1, 2 })
                        foreach (int hs in new[] { -1, 1 })
                        {
                            var car = new OwnedCar { id = "one_align", specId = spec != null ? spec.id : "" };
                            if (cb > 0)
                            {
                                var row = FaultCatalog.MakeById(FaultCatalog.AlignmentId, "jpn");
                                row.severity = cb; row.pullDir = cs;
                                car.faults.Add(row);
                            }
                            float carriedDeg = FaultCatalog.Aggregate_(car).steerPullDeg;
                            var o = new CrashStopGap.Outcome { classes = CrashStopGap.Cls.Alignment, alignSeverity = hb, alignSide = hs };
                            float live = CrashStopGap.CombinePull(carriedDeg, o.PullDeg);
                            CrashBank.ApplyOutcome(car, o);
                            float next = FaultCatalog.Aggregate_(car).steerPullDeg;
                            int rows = car.faults.Count(f => f.id == FaultCatalog.AlignmentId);
                            n++;
                            if (Mathf.Abs(live - next) > 1e-6f || rows != 1)
                            {
                                bad++;
                                if (firstBad == null)
                                    firstBad = "carried " + cb + "/" + cs + " hit " + hb + "/" + hs + ": live " + live.ToString("+0.000;-0.000") +
                                               " next " + next.ToString("+0.000;-0.000") + " rows " + rows;
                            }
                        }
                Check(bad == 0,
                      "for every carried alignment (none, minor or severe, either side) and every crash alignment, the live pull " +
                      "equals the next drive's, and the car keeps ONE alignment row", (n - bad) + "/" + n + (firstBad != null ? "; " + firstBad : ""));

                float mn = CrashStopGap.PullDegMinor, sv = CrashStopGap.PullDegSevere;
                Check(CrashStopGap.CombinePull(+mn, -mn) == -mn,
                      "minor RIGHT carried + minor LEFT hit: pulls LEFT, toward the hit (the old sum was 0: dead straight, then right again next drive)");
                Check(CrashStopGap.CombinePull(-mn, -mn) == -mn,
                      "minor LEFT carried + minor LEFT hit: stays minor (the old sum was 0.084 deg - the severe band - then minor next drive)");
                Check(CrashStopGap.CombinePull(+mn, -sv) == -sv,
                      "minor RIGHT carried + severe LEFT hit: severe left (the old sum was -0.098)");
                Check(CrashStopGap.CombinePull(+sv, -mn) == +sv, "severe RIGHT carried + minor LEFT hit: the worse band stays");
                Check(CrashStopGap.CombinePull(0f, 0f) == 0f && CrashStopGap.CombinePull(+mn, 0f) == +mn &&
                      CrashStopGap.CombinePull(-sv, 0f) == -sv,
                      "no crash this drive: the carried pull exactly, and a sound car 0 exactly");
            }
            finally
            {
                CrashStopGap.On = was; FaultCatalog.FamilyOn = famWas;
            }
        }

        // ---------------------------------------------------------------
        static void TestCrashLive()
        {
            Line("R0a crash - live AT THE HIT, by the responder's own record rule: the pull, the radiator, the weep:");
            bool was = CrashStopGap.On;
            CrashStopGap.On = true;
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); CrashStopGap.On = was; return; }
            var car = TuneBenchCar(spec);
            var temp = car.gameObject.AddComponent<EngineTemp>();
            temp.car = car;
            var resp = car.gameObject.AddComponent<CollisionResponder>();
            var host = new GameObject("CrashApplier") { hideFlags = HideFlags.HideAndDontSave };
            var applier = host.AddComponent<RaceHandoffApplier>();
            applier.playerCar = car;
            EngineTemp a = null, sound = null;
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
                var nose = new Vector3(-0.5f, 0f, 2.1f);
                const int Fr = CrashStopGap.ZoneFront, Si = CrashStopGap.ZoneSide;

                // THROUGH RecordHit - the rule OnCollisionEnter applies: only a
                // hit harder than every one before it this leg is rolled.
                bool r1 = resp.RecordHit(11f, Fr, -1, nose, "wall", 10f);
                var t1 = L.Total;
                Check(r1 && Mathf.Approximately(car.hitSteerPull, -CrashStopGap.PullDegMinor),
                      "an 11 m/s nose-in on the left: the pull is on the car AT THE HIT, toward the struck side, in degrees", car.hitSteerPull);
                Check(Mathf.Approximately(temp.radiatorCond, 90f - t1.radiatorLoss) &&
                      Mathf.Approximately(temp.radiatorHit, t1.radiatorLoss) && temp.radiatorWeep > 0f,
                      "and the radiator is down and weeping, live", temp.radiatorCond.ToString("0.0") + " weep " + temp.radiatorWeep.ToString("0.00"));

                bool r2 = resp.RecordHit(12f, Fr, -1, nose, "wall", 10.1f);
                Check(r2 && L.Rolls == 2 && Mathf.Approximately(temp.radiatorHit, t1.radiatorLoss) &&
                      Mathf.Approximately(car.hitSteerPull, -CrashStopGap.PullDegMinor),
                      "a harder contact of the SAME crash re-reads the same dice: one radiator hit, one pull");
                L.Hit(Fr, 1, 11.5f, CrashStopGap.Bumper.Plastic, 10.2f);
                Check(L.Rolls == 2 && Mathf.Approximately(car.hitSteerPull, -CrashStopGap.PullDegMinor),
                      "a softer contact of the same crash, handed to the ledger directly, changes nothing (it cannot downgrade or flip it)");

                L.DrawOverride = () => CrashDraws(bumper: 0.9f, radiator: 0.9f, align: 0.1f);
                bool r3 = resp.RecordHit(13f, Si, 1, new Vector3(0.88f, 0f, 0f), "barrier", 15f);
                Check(r3 && L.Rolls == 3 && Mathf.Approximately(car.hitSteerPull, CrashStopGap.PullDegSevere),
                      "a second, HARDER crash (13 m/s) on the right flank: the pull goes severe, to the right", car.hitSteerPull);
                bool r4 = resp.RecordHit(10f, Si, -1, new Vector3(-0.88f, 0f, 0f), "barrier", 20f);
                Check(!r4 && L.Rolls == 3 && Mathf.Approximately(car.hitSteerPull, CrashStopGap.PullDegSevere) &&
                      Mathf.Approximately(resp.WorstHit, 13f) && resp.WorstHitSide == 1,
                      "a SOFTER crash later in the leg (10 m/s, left) is never rolled: R0a rolls the leg's hardest hit, not every crash");

                // The bench reapplies the request mid-drive: the crash stays.
                float radBefore = temp.radiatorCond;
                applier.ReapplyCar();
                Check(Mathf.Approximately(car.hitSteerPull, CrashStopGap.PullDegSevere) &&
                      Mathf.Approximately(temp.radiatorCond, radBefore),
                      "a bench reapply (ReapplyCar) neither straightens the pull nor mends the radiator",
                      car.hitSteerPull + " / " + temp.radiatorCond.ToString("0.0"));

                CrashStopGap.StampHandoff(resp);
                var stamped = CrashBank.FromHandoff();
                Check(RaceHandoff.CrashRolled && RaceHandoff.CrashAlignSeverity == 2 && RaceHandoff.CrashAlignSide == 1 &&
                      RaceHandoff.CrashBumperId == "bumper_crack" &&
                      Mathf.Approximately(RaceHandoff.CrashRadiatorLoss, t1.radiatorLoss) &&
                      Mathf.Approximately(RaceHandoff.CrashBodyLoss, 30f) && Mathf.Approximately(RaceHandoff.CrashPaintLoss, 3.5f),
                      "the exit stamp hands over the WORST of the leg, lane by lane: body -30 and paint -3.5 (the nose-in's), not the " +
                      "sum of the two crashes (-52.5 / -7)", stamped);
                Check(stamped.weep == 0f,
                      "the weep is live only - not in the handoff: the coolant it cost is banked through the coolant stamp (EndCoolantPct)");

                // A DIFFERENT CAR under the player (the debug bench's swap) starts clean.
                CrashStopGap.StartClean(car);
                Check(car.hitSteerPull == 0f && temp.radiatorHit == 0f && temp.radiatorWeep == 0f &&
                      resp.Ledger != null && resp.Ledger != L && resp.Ledger.Rolls == 0 && resp.WorstHit == 0f,
                      "a car swapped in mid-drive (CrashStopGap.StartClean) starts with no pull, no cracked core, no weep and its own ledger");

                // THE WEEP, integrated the way TestEngineTemp integrates. Both
                // cars warmed at the cruise first, so the minutes are minutes
                // after the crash, not after a cold start.
                float load = EngineTemp.Load(0.3f, 0.35f), over = EngineTemp.OverRev(0.3f, 0.35f);
                a = Bench(rad: 90f - t1.radiatorLoss);
                sound = Bench(rad: 90f);
                Settle(a, 300f, 0.3f, 0.35f, 80f);
                Settle(sound, 300f, 0.3f, 0.35f, 80f);
                float warmC = a.celsius;
                a.radiatorWeep = t1.weep;
                float hotAt = -1f, leakAt = -1f, at2 = 0f;
                for (int i = 0; i < 60 * 60 * 30; i++)
                {
                    a.Tick(1f / 60f, load, over, 80f); sound.Tick(1f / 60f, load, over, 80f);
                    if (i == 60 * 60 * 2) at2 = a.celsius;
                    if (leakAt < 0f && a.coolantPct < CoolingModel.CoolantFullAbove) leakAt = i / 3600f;
                    if (hotAt < 0f && a.celsius > EngineTemp.RedMark) hotAt = i / 3600f;
                }
                Check(sound.coolantPct >= 99.999f && sound.celsius > 84f && sound.celsius < 92f,
                      "a sound car (no weep) loses no coolant in 30 min at 80 km/h and holds where TestEngineTemp pins a cruise (84-92 C)",
                      sound.celsius.ToString("0.0") + " C, coolant " + sound.coolantPct.ToString("0.0") + "%");
                Check(at2 < EngineTemp.RedMark && at2 < warmC + 3f,
                      "with the hit's weep (" + t1.weep.ToString("0.00") + "): nothing at the hit - 2 min later the needle is where it was",
                      warmC.ToString("0.0") + " -> " + at2.ToString("0.0") + " C");
                Check(leakAt >= 6f && leakAt <= 18f,
                      "the system drains below " + CoolingModel.CoolantFullAbove + "% some minutes after the crash (about ten for this weep)",
                      leakAt < 0f ? "never" : leakAt.ToString("0.0") + " min");
                Check(hotAt > leakAt && hotAt > 0f && hotAt <= 30f,
                      "and only then does the needle climb past the red (" + EngineTemp.RedMark + " C), inside the 30-minute drive",
                      hotAt < 0f ? "never" : hotAt.ToString("0.0") + " min");
            }
            finally
            {
                CrashStopGap.On = was;
                Kill(a); Kill(sound);
                Object.DestroyImmediate(host);
                TuneDrop(car);
                RaceHandoff.ClearAll();
            }
        }

        // ---------------------------------------------------------------
        /// <summary>WEAR NEVER CRACKS A BUMPER: the player's own body-lane wear
        /// roll used to fall back to bumper_crack / bumper_dent (the only
        /// body rows a low-mileage Japanese or US car is eligible for).</summary>
        static void TestCrashWearNoBumper()
        {
            Line("R0a crash - wear never cracks a bumper (a hit's row comes from the hit):");
            bool was = CrashStopGap.On, famWas = FaultCatalog.FamilyOn;
            FaultCatalog.FamilyOn = true;
            Random.InitState(1112);
            try
            {
                int Run(bool on, string origin, out int rolled)
                {
                    CrashStopGap.On = on;
                    var spec = CrashSpec(origin, 1985);
                    var s = LifeRules.SeedNewGame("WEAR", 25, LifeRules.DefaultJobIndex);
                    var car = CarMarket.MakeOwnedCar(s, spec, 100, 20000f, spec.price);
                    s.activeCar = car.id;
                    int bumpers = 0; rolled = 0;
                    for (int i = 0; i < 20; i++)
                    {
                        car.faults.Clear();
                        car.engine = car.tires = 100f;
                        car.carHP = 10f;              // a ruined body lane
                        car.odoMiles = 20000f;        // under 60k: the 'new' tier
                        car.fuel = 100f;
                        RaceHandoff.ClearAll();
                        RaceHandoff.FromLifeSim = true;
                        RaceHandoff.ResultReady = true;
                        RaceHandoff.CarId = car.id;
                        RaceHandoff.MetersDriven = 50000f;
                        RaceHandoff.CommuteLeg = true;
                        RaceHandoff.FreeRoam = true;
                        LifeRules.ApplyRaceResult(s);
                        rolled += car.faults.Count;
                        bumpers += car.faults.Count(f => f.id == "bumper_crack" || f.id == "bumper_dent");
                    }
                    return bumpers;
                }
                int jOn = Run(true, "jpn", out int jOnN), uOn = Run(true, "usa", out _);
                int jOff = Run(false, "jpn", out int jOffN);
                Check(jOn == 0 && uOn == 0,
                      "with the stop-gap on, 20 long legs on a ruined body lane give a low-mileage Japanese or US car no bumper row " +
                      "(nothing else is eligible, so the roll is a no)", "jpn " + jOn + " (" + jOnN + " rows), usa " + uOn);
                Check(jOff > 0, "PSX_FAULT_CRASH=0: the same legs crack its bumper, as before", jOff + " of " + jOffN);
                var sp = CrashSpec("jpn", 1985);
                var mkt = new OwnedCar { id = "mkt", specId = sp.id, odoMiles = 20000f };
                var f2 = FaultCatalog.RollWearFault(mkt, "hp", false, "wear", "jpn");
                Check(f2 != null && f2.id == "bumper_crack",
                      "a seller's car still arrives with a previous owner's cracked bumper (market rolls are untouched; R4c's backfill)",
                      f2 != null ? f2.id : "none");
                Check(FaultCatalog.Pools.Where(FaultCatalog.ImpactOnly).Select(p => p.id).Distinct().OrderBy(x => x)
                          .SequenceEqual(new[] { "bumper_crack", "bumper_dent" }),
                      "the rows only a hit can cause are the two bumper rows",
                      string.Join(",", FaultCatalog.Pools.Where(FaultCatalog.ImpactOnly).Select(p => p.origin + ":" + p.id)));
            }
            finally
            {
                CrashStopGap.On = was; FaultCatalog.FamilyOn = famWas;
                RaceHandoff.ClearAll();
            }
        }

        // ---------------------------------------------------------------
        static void TestCrashSwitchOff()
        {
            Line("R0a crash - PSX_FAULT_CRASH=0 is today's crash, whatever PSX_FAULT_FAMILY says:");
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
                int Crash(OwnedCar target, int times, out string ids)
                {
                    int jpnRows = 0; var seen = new HashSet<string>();
                    for (int i = 0; i < times; i++)
                    {
                        target.faults.Clear();
                        RaceHandoff.ClearAll();
                        RaceHandoff.ResultReady = true;
                        RaceHandoff.CommuteLeg = true;
                        RaceHandoff.FreeRoam = true;
                        RaceHandoff.CarId = target.id;
                        RaceHandoff.DamageScore = LifeRules.ImpactFaultThreshold * 3f;
                        LifeRules.ApplyRaceResult(s);
                        foreach (var f in target.faults) seen.Add(f.id);
                        if (target.faults.Count > 0 && target.faults.All(f =>
                                (f.id == "bumper_crack" || f.id == "alignment") &&
                                FaultCatalog.Pools.Any(p => p.origin == "jpn" && p.id == f.id && p.cost == f.cost))) jpnRows++;
                    }
                    ids = string.Join(",", seen.OrderBy(x => x));
                    return jpnRows;
                }
                int hits = Crash(c, 10, out string ids0);
                Check(hits == 10, "and the leg's DamageScore past 22 rolls the Japanese impact pool - bumper_crack or alignment - as before",
                      hits + "/10 " + ids0);

                // THE CRASH SWITCH ALONE: families on, crash off - a German car's
                // crash is still today's (the jpn impact rows at jpn prices),
                // never the eur pool's electrical "impact" rows.
                FaultCatalog.FamilyOn = true;
                var gerSpec = CrashSpec("ger");
                if (gerSpec == null) { Check(false, "a German road car to crash"); return; }
                var ger = CarMarket.MakeOwnedCar(s, gerSpec, 80, 40000f, gerSpec.price);
                int gHits = Crash(ger, 10, out string gIds);
                Check(gHits == 10,
                      "PSX_FAULT_CRASH=0 with PSX_FAULT_FAMILY on: a German car's crash rolls today's Japanese impact rows at Japanese " +
                      "prices - no electrical_gremlin, display_failure or trim_rattle", gHits + "/10 " + gIds);
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
