using System;
using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// RG2's fault tables, baked to JSON and loaded once from Resources.
    ///
    /// The data is generated from the TypeScript source rather than hand-copied
    /// (src/sim/faultPools.ts, faultEffects.ts, usedCarFaults.ts) so every price
    /// stays greppable against the game it came from. ROW ORDER IS LOAD-BEARING:
    /// the picker indexes with floor(random * eligible.length) over an
    /// order-sensitive array, and several RG2 files warn to keep it stable.
    ///
    /// Owns three things:
    ///   - the wear-fault picker (a port of diagnoseFault),
    ///   - the effect aggregator (computeFaultEffects) that turns a car's fault
    ///     list into the handicaps the race scene applies,
    ///   - the repair venue quotes (repairCost.ts).
    /// </summary>
    public static class FaultCatalog
    {
        // ---- serialized shapes (JsonUtility needs concrete [Serializable]) ----
        [Serializable] public class PoolEntry
        {
            public string origin, id, name, stat, type, minTier, sources;
            public int cost, days, add;
            public bool HasSource(string cause) =>
                !string.IsNullOrEmpty(sources) &&
                ("|" + sources + "|").Contains("|" + cause + "|");
        }

        [Serializable] public class EffectEntry
        {
            public string id, desc;
            public float accelMult = 1f, fuelMult = 1f, gripMult = 1f, brakeMult = 1f;
            public float steerPull, shiftMult = 1f, engineWearMult = 1f, nightVisMult = 1f;
            public bool rpmFlutter, steerSlow, hideGauges;
        }

        [Serializable] public class UsedEntry
        {
            public string origin, id, name, stat, type, tier;
            public int cost, days, add;
            public bool testDriveOnly;
        }

        [Serializable] class OriginMult { public string origin; public float mult; }
        [Serializable] class TierRow { public string tier; public float detectChance, priceMult; }

        [Serializable] class Bundle
        {
            public List<PoolEntry> pools = new List<PoolEntry>();
            public List<EffectEntry> effects = new List<EffectEntry>();
            public List<UsedEntry> used = new List<UsedEntry>();
            public List<OriginMult> originMult = new List<OriginMult>();
            public List<TierRow> repairTiers = new List<TierRow>();
        }

        const string ResourceName = "rg2_faults";
        static Bundle data;
        static Dictionary<string, EffectEntry> effectById;
        static Dictionary<string, float> originCostMult;

        static readonly EffectEntry Identity = new EffectEntry();

        static void Load()
        {
            if (data != null) return;
            var text = Resources.Load<TextAsset>(ResourceName);
            if (text == null)
            {
                Debug.LogWarning("FaultCatalog: Resources/" + ResourceName +
                                 ".json missing — faults disabled. Re-run the bake step.");
                data = new Bundle();
            }
            else data = JsonUtility.FromJson<Bundle>(text.text);

            effectById = new Dictionary<string, EffectEntry>();
            foreach (var e in data.effects) effectById[e.id] = e;
            originCostMult = new Dictionary<string, float>();
            foreach (var o in data.originMult) originCostMult[o.origin] = o.mult;
        }

        public static bool Ready { get { Load(); return data.pools.Count > 0; } }
        public static IReadOnlyList<UsedEntry> UsedFaults { get { Load(); return data.used; } }

        /// <summary>Every fault the pools can roll. Exposed for the self-test,
        /// which asserts that the inspection map has a home for all of them —
        /// an id with nowhere to be found is a fault the player can never
        /// diagnose, and nothing else in the game would ever say so.</summary>
        public static IReadOnlyList<PoolEntry> Pools { get { Load(); return data.pools; } }

        /// <summary>&lt;60k 'new' / &lt;150k 'mid' / else 'high' (mileageTier.ts).</summary>
        public static string MileageTier(float odoMiles) =>
            odoMiles < 60000f ? "new" : (odoMiles < 150000f ? "mid" : "high");

        static int TierRank(string tier) => tier == "high" ? 2 : (tier == "mid" ? 1 : 0);

        /// <summary>
        /// Port of diagnoseFault. Returns null when the gate rejects the roll —
        /// that is a normal outcome, not an error: one fault per stat at normal
        /// severity, two at severe, so a neglected car cannot accumulate an
        /// unbounded backlog.
        /// </summary>
        public static CarFault RollWearFault(OwnedCar car, string stat, bool severe,
                                             string cause = "wear", string origin = "jpn")
        {
            Load();
            if (data.pools.Count == 0) return null;

            // Gate on how many faults already sit on this stat lane.
            int existing = 0;
            var existingIds = new HashSet<string>();
            foreach (var f in car.faults)
                if (f.stat == stat) { existing++; existingIds.Add(f.id); }
            if (!severe && existing > 0) return null;
            if (severe && existing >= 2) return null;

            if (!originCostMult.ContainsKey(origin)) origin = "jpn";   // unknown → jpn
            int carRank = TierRank(MileageTier(car.odoMiles));

            var eligible = new List<PoolEntry>();
            foreach (var p in data.pools)
            {
                if (p.origin != origin || p.stat != stat) continue;
                if (carRank < TierRank(p.minTier)) continue;     // mileage gate
                if (existingIds.Contains(p.id)) continue;        // per-stat dedupe
                eligible.Add(p);
            }
            if (eligible.Count == 0) return null;

            // Cause-aware filter with a 'wear' fallback, then the raw set — so a
            // threshold cross always yields SOME diagnosis rather than a no-op.
            var byCause = eligible.FindAll(p => p.HasSource(cause));
            if (byCause.Count > 0) eligible = byCause;
            else if (cause != "wear")
            {
                var byWear = eligible.FindAll(p => p.HasSource("wear"));
                if (byWear.Count > 0) eligible = byWear;
            }

            // Severe prefers "real" faults. Snapshot-then-replace: filtering in
            // place would leave an empty list with no fallback.
            if (severe)
            {
                var strict = eligible.FindAll(p => p.cost >= 100);
                if (strict.Count > 0) eligible = strict;
            }

            var pick = eligible[UnityEngine.Random.Range(0, eligible.Count)];
            float mult = originCostMult.TryGetValue(origin, out float m) ? m : 1f;
            return new CarFault
            {
                id = pick.id,
                label = pick.name,
                stat = pick.stat,
                cost = Mathf.RoundToInt(pick.cost * mult),
                days = pick.days,
                add = pick.add,
                repairType = pick.type,
                // NOTHING arrives diagnosed. A fault is a thing the car has,
                // not a thing the player is told about — the car goes off song
                // (Aggregate_ counts hidden faults, so it really is slower) and
                // finding out what is wrong costs an inspection, whether that
                // is the player under it themselves, a mechanic, or a dealer.
                // Wear faults used to announce themselves the moment they were
                // rolled, which made the whole inspection layer decorative.
                hidden = true,
                diagnosed = false,
                severity = severe ? 2f : 1f,
                pullDir = UnityEngine.Random.value < 0.5f ? -1 : 1,
            };
        }

        // ------------------------------------------------------------------
        //  The USED-CAR pool — what a stranger's car has wrong with it
        // ------------------------------------------------------------------
        //
        // A SECOND pool, beside the wear pool above, and it was baked into
        // Resources with the rest of this file and then never read by anything.
        // It exists because a car you are being SOLD fails differently from one
        // you have been running: 21 of its 36 rows are `testDriveOnly`, which
        // is the single most interesting thing in RG2's used-car layer —
        // alignment, slipping gearboxes, warped rotors, sensors and bushings
        // are all invisible standing in a driveway and obvious in the first
        // hundred metres. That split is what makes a test drive worth asking
        // the seller for, and it is what makes an inspection NOT the whole
        // answer.

        /// <summary>
        /// How likely each repair tier is to give itself away to somebody
        /// looking, before any skill or tooling.
        ///
        /// From the monolith's REPAIR_TIERS. Note it is NOT monotonic —
        /// catastrophic sits BELOW extensive — and that is the source's
        /// number, not a slip: an extensive fault is a car that runs badly and
        /// announces it, while the catastrophic ones in these pools are things
        /// like a stretched timing chain, which is quiet right up until it is
        /// not. The bake carried the priceMult column and dropped this one, so
        /// it is spelled out here with its source rather than re-derived.
        /// </summary>
        public static float TierDetect(string tier) =>
            tier == "cheap" ? 0.30f :
            tier == "moderate" ? 0.50f :
            tier == "extensive" ? 0.70f : 0.60f;

        /// <summary>What one fault of this tier knocks off an asking price.
        /// From the bake's own repairTiers rows.</summary>
        public static float TierPriceMult(string tier)
        {
            Load();
            foreach (var t in data.repairTiers) if (t.tier == tier) return t.priceMult;
            return 0.9f;
        }

        /// <summary>Is this fault one only a DRIVE can surface? Looked up by id
        /// across the used pool, because a fault, once on a car, is just a
        /// CarFault and has forgotten which pool it came from.</summary>
        public static bool IsTestDriveOnly(string faultId)
        {
            Load();
            foreach (var u in data.used) if (u.id == faultId) return u.testDriveOnly;
            return false;
        }

        /// <summary>The repair tier of a fault id, or "moderate" when it is not
        /// a used-pool fault — every wear fault behaves like the middle of the
        /// range for pricing and detection.</summary>
        public static string TierOf(string faultId)
        {
            Load();
            foreach (var u in data.used) if (u.id == faultId) return u.tier;
            return "moderate";
        }

        /// <summary>
        /// Roll the problems a used car is carrying when you go to look at it.
        ///
        /// Port of generateUsedCarFaults. COUNT comes from condition and
        /// SEVERITY from mileage, which is the pairing that makes a tidy
        /// high-mileage car and a rough low-mileage one fail in different ways.
        /// Catastrophic rows are gated out under 60k miles and extensive under
        /// 30k — a nearly-new car can be neglected but it cannot be worn out.
        /// </summary>
        public static List<CarFault> RollUsedFaults(int cond, float odoMiles, string origin)
        {
            Load();
            var outv = new List<CarFault>();
            if (data.used.Count == 0) return outv;
            if (!originCostMult.ContainsKey(origin)) origin = "jpn";

            int count;
            float r = UnityEngine.Random.value;
            if (cond > 85) count = r < 0.30f ? 1 : 0;
            else if (cond > 60) count = 1 + (r < 0.5f ? 1 : 0);
            else if (cond > 40) count = 2 + (r < 0.5f ? 1 : 0);
            else count = 2 + UnityEngine.Random.Range(0, 3);

            float severeBias = odoMiles > 150000f ? 0.40f : odoMiles > 80000f ? 0.20f : 0.05f;

            var pool = new List<UsedEntry>();
            foreach (var u in data.used)
            {
                if (u.origin != origin) continue;
                if (u.tier == "catastrophic" && odoMiles < 60000f) continue;
                if (u.tier == "extensive" && odoMiles < 30000f) continue;
                pool.Add(u);
            }
            if (pool.Count == 0) return outv;

            var taken = new HashSet<string>();
            float mult = originCostMult.TryGetValue(origin, out float om) ? om : 1f;
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                // Bias toward the expensive half on a tired car. Two draws and
                // keep the worse is the cheapest honest way to skew a pick, and
                // it degrades gracefully when the pool is small.
                var a = pool[UnityEngine.Random.Range(0, pool.Count)];
                var b = pool[UnityEngine.Random.Range(0, pool.Count)];
                var pick = UnityEngine.Random.value < severeBias
                    ? (a.cost >= b.cost ? a : b) : a;
                if (!taken.Add(pick.id)) continue;
                outv.Add(new CarFault
                {
                    id = pick.id,
                    label = pick.name,
                    stat = pick.stat,
                    cost = Mathf.RoundToInt(pick.cost * mult),
                    days = pick.days,
                    add = pick.add,
                    repairType = pick.type,
                    // Hidden, like everything else in this game. What separates
                    // these from wear faults is HOW they come out: the walk-round
                    // finds the stationary ones and only a drive finds the rest.
                    hidden = true,
                    diagnosed = false,
                    severity = pick.tier == "catastrophic" || pick.tier == "extensive" ? 2f : 1f,
                    pullDir = UnityEngine.Random.value < 0.5f ? -1 : 1,
                });
            }
            return outv;
        }

        public static EffectEntry Effect(string faultId)
        {
            Load();
            return effectById != null && effectById.TryGetValue(faultId, out var e) ? e : Identity;
        }

        /// <summary>Human-readable effect line for the garage list, e.g.
        /// "accel x0.93". Empty for faults that only cost money.</summary>
        public static string EffectSummary(string faultId)
        {
            var e = Effect(faultId);
            var parts = new List<string>();
            if (e.accelMult < 0.999f) parts.Add("accel x" + e.accelMult.ToString("0.00"));
            if (e.gripMult < 0.999f) parts.Add("grip x" + e.gripMult.ToString("0.00"));
            if (e.brakeMult < 0.999f) parts.Add("brake x" + e.brakeMult.ToString("0.00"));
            if (e.shiftMult > 1.001f) parts.Add("shift x" + e.shiftMult.ToString("0.0"));
            if (e.fuelMult > 1.001f) parts.Add("fuel x" + e.fuelMult.ToString("0.00"));
            if (Mathf.Abs(e.steerPull) > 0.001f) parts.Add("pulls");
            if (e.engineWearMult > 1.001f) parts.Add("wear x" + e.engineWearMult.ToString("0.0"));
            if (e.hideGauges) parts.Add("no gauges");
            if (e.rpmFlutter) parts.Add("tach flutter");
            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }

        /// <summary>
        /// Aggregate a car's detected faults into race handicaps. The combining
        /// rule differs per field and is not interchangeable: accel/fuel/grip/
        /// brake MULTIPLY (two 0.9s really is 0.81), steerPull ADDS with a
        /// per-fault cached direction, shift and engine-wear take the MAX (the
        /// worst offender governs), and the HUD flags OR together.
        /// </summary>
        public static Aggregate Aggregate_(OwnedCar car)
        {
            var agg = new Aggregate();
            if (car == null) return agg;
            foreach (var f in car.faults)
            {
                // HIDDEN FAULTS COUNT. A worn set of plugs slows the car down
                // whether or not anyone has looked at them, and if they did not,
                // inspecting would only ever cost money — nobody would do it,
                // and a bad used car would be indistinguishable from a good one
                // until the bill arrived. What detection gates is the LISTING,
                // not the affliction.
                var e = Effect(f.id);
                agg.accelMult *= e.accelMult;
                agg.fuelMult *= e.fuelMult;
                agg.gripMult *= e.gripMult;
                agg.brakeMult *= e.brakeMult;
                agg.steerPull += e.steerPull * (f.pullDir >= 0 ? 1f : -1f);
                agg.shiftMult = Mathf.Max(agg.shiftMult, e.shiftMult);
                agg.engineWearMult = Mathf.Max(agg.engineWearMult, e.engineWearMult);
                agg.nightVisMult = Mathf.Min(agg.nightVisMult, e.nightVisMult);
                agg.rpmFlutter |= e.rpmFlutter;
                agg.steerSlow |= e.steerSlow;
                agg.hideGauges |= e.hideGauges;
            }
            return agg;
        }

        public class Aggregate
        {
            public float accelMult = 1f, fuelMult = 1f, gripMult = 1f, brakeMult = 1f;
            public float steerPull, shiftMult = 1f, engineWearMult = 1f, nightVisMult = 1f;
            public bool rpmFlutter, steerSlow, hideGauges;
        }

        // ================= repair quotes (repairCost.ts) =================

        public enum Venue { Diy = 0, Mechanic = 1, Dealer = 2 }

        public struct Quote
        {
            public int price;
            public int days;
            public int difficulty;
            public bool available;      // DIY only: skill gate
            public string blockedReason;
        }

        public const int RepairPriceCap = 12000;

        /// <summary>
        /// Job difficulty. Mechanical work is pricier to attempt than body work,
        /// and expensive parts imply a harder job — capped at +20 so a single
        /// costly component cannot make everything unattemptable.
        /// </summary>
        public static int Difficulty(CarFault f)
        {
            bool mechanical = f.stat == "engine" || f.stat == "tires";
            return (mechanical ? 55 : 45) + Mathf.Min(20, Mathf.FloorToInt(f.cost / 100f) * 3);
        }

        /// <summary>
        /// Labour on an expensive car costs more, but not proportionally: the
        /// laborFactor keeps an oil change on an exotic sane while a gearbox
        /// rebuild still scales. Without it, cheap jobs on expensive cars price
        /// like major surgery.
        /// </summary>
        static float EffectiveCostMult(OwnedCar car, int baseCost)
        {
            float catalogPrice = Mathf.Max(1f, car.paidPrice);
            float carCostMult = Mathf.Clamp(Mathf.Sqrt(catalogPrice / 15000f), 0.6f, 3.5f);
            float laborFactor = Mathf.Clamp(0.45f + (baseCost - 150f) / 450f * 0.55f, 0.45f, 1f);
            return 1f + (carCostMult - 1f) * laborFactor;
        }

        public static Quote GetQuote(LifeState s, OwnedCar car, CarFault f, Venue venue)
        {
            var q = new Quote { available = true };
            float effMult = EffectiveCostMult(car, f.cost);
            q.difficulty = Difficulty(f);

            switch (venue)
            {
                case Venue.Diy:
                    q.price = Mathf.RoundToInt(f.cost * effMult);
                    // mechSkill starts at 15 and difficulties start around 45-55,
                    // so the early game is deliberately mechanic-priced. DIY
                    // becoming affordable IS the progression.
                    q.available = s.mechSkill >= q.difficulty;
                    if (!q.available) q.blockedReason = "needs skill " + q.difficulty;
                    float baseDays = Mathf.Max(1f, f.days + Mathf.Ceil(q.difficulty / 25f));
                    float speedup = 1f + Mathf.Max(0f, s.mechSkill - q.difficulty) / 6f;
                    q.days = Mathf.Max(1, Mathf.RoundToInt(baseDays / speedup));
                    break;
                case Venue.Mechanic:
                    q.price = Mathf.RoundToInt(f.cost * 2f * effMult);
                    q.days = Mathf.Max(1, f.days);
                    break;
                default:
                    q.price = Mathf.RoundToInt(f.cost * 3f * effMult);
                    q.days = 0;      // dealer is same-day, at triple the price
                    break;
            }
            q.price = Mathf.Min(q.price, RepairPriceCap);
            return q;
        }

        /// <summary>DIY teaches. A job at or above your level teaches most; an
        /// easy one still teaches a little, tapering to nothing.</summary>
        public static float DiySkillGain(float skill, int difficulty)
        {
            float challenge = difficulty - skill;
            return challenge >= 0f
                ? 3f + Mathf.Min(5f, Mathf.Round(challenge / 8f))
                : Mathf.Max(0f, 2f + Mathf.Round(challenge / 10f));
        }
    }
}
