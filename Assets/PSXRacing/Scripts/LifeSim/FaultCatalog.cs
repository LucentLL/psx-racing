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
            UsedRowsOwnIds();
            HitRows();
        }

        /// <summary>
        /// R1a's located hit rows (design §4 BODY / COOLING, R1a): made only by
        /// DamageBank at a hit, never rolled - they live OUTSIDE the pools, so no
        /// wear, market or used-car roller can pick them. Priced per family
        /// (Delmar-style parts x1.4 + $48/h, rounded), effects: a struck exhaust
        /// loses power; the radiator leak acts live through the cooling model
        /// (radiatorWeep), the rest is value and the cues of later rounds.
        /// </summary>
        static void HitRows()
        {
            hitRows = new List<PoolEntry>();
            foreach (var fam in new[] { "jpn", "usa", "eur" })
            {
                hitRows.Add(new PoolEntry { origin = fam, id = "panel_damage", name = "Body Panel Damage", stat = "hp", type = "mechanic", minTier = "new", sources = "impact", cost = 230, days = 2, add = 10 });
                hitRows.Add(new PoolEntry { origin = fam, id = "bumper_bar", name = "Bumper Reinforcement Crushed", stat = "hp", type = "mechanic", minTier = "new", sources = "impact", cost = 140, days = 1, add = 5 });
                hitRows.Add(new PoolEntry { origin = fam, id = "lamp_out", name = "Lamp Broken", stat = "hp", type = "mechanic", minTier = "new", sources = "impact", cost = 90, days = 1, add = 0 });
                hitRows.Add(new PoolEntry { origin = fam, id = "radiator_leak", name = "Radiator Leaking", stat = "engine", type = "mechanic", minTier = "new", sources = "impact", cost = 260, days = 1, add = 0 });
                hitRows.Add(new PoolEntry { origin = fam, id = "exhaust_damage", name = "Exhaust Damaged", stat = "hp", type = "mechanic", minTier = "new", sources = "impact", cost = 130, days = 1, add = 5 });
                hitRows.Add(new PoolEntry { origin = fam, id = "roof_glass", name = "Roof / Glass Damage", stat = "hp", type = "mechanic", minTier = "new", sources = "impact", cost = 480, days = 2, add = 15 });
                // R1a part 2a: the bags a frontal hit fired (SrsBag) - bag, module and clock spring, Delmar's set price (design §8)
                hitRows.Add(new PoolEntry { origin = fam, id = "srs_deployed", name = "Airbag Deployed", stat = "hp", type = "mechanic", minTier = "new", sources = "impact", cost = 1320, days = 2, add = 0 });
            }
            if (!effectById.ContainsKey("exhaust_damage"))
                effectById["exhaust_damage"] = new EffectEntry { id = "exhaust_damage", desc = "Exhaust damaged - down on power", accelMult = DamageModel.ExhaustPower };
        }

        static List<PoolEntry> hitRows = new List<PoolEntry>();

        /// <summary>R1a's hit row for an id, the family's own first.</summary>
        static PoolEntry HitRow(string faultId, string family)
        {
            PoolEntry any = null;
            foreach (var p in hitRows)
            {
                if (p.id != faultId) continue;
                if (p.origin == family) return p;
                if (any == null) any = p;
            }
            return any;
        }

        /// <summary>R1a's hit row ids (made only at a hit).</summary>
        public static bool IsHitRow(string id) { Load(); return HitRow(id, "jpn") != null; }

        /// <summary>
        /// R0b: THE USED ROWS GET THEIR OWN IDS (design §4). The bake keyed
        /// "Engine Rebuild Needed" / "Engine Replacement" as trans_slip and
        /// "Fluid Top-Off Needed" as oil_leak, so a worn engine was found under
        /// TRANSMISSION, cleared by a clutch and acted as a slipping gearbox,
        /// and a top-off was found under the oil pan. Their effects are kept
        /// (a row that has not landed keeps its RG2 effect) except the shift
        /// delay, which was the gearbox's: an engine that wants a rebuild is
        /// down on power and thirsty, not slow to shift. The effects exist
        /// whatever the switch says, because a migrated save carries the ids.
        /// PSX_FAULT_USEDIDS=0: the rows roll under their old ids.
        /// </summary>
        static void UsedRowsOwnIds()
        {
            if (!effectById.ContainsKey(FaultIds.UsedEngineWorn) && effectById.TryGetValue("trans_slip", out var slip))
            {
                var worn = JsonUtility.FromJson<EffectEntry>(JsonUtility.ToJson(slip));
                worn.id = FaultIds.UsedEngineWorn;
                worn.desc = "Worn engine — down on power, thirsty";
                worn.shiftMult = 1f;
                effectById[worn.id] = worn;
            }
            if (!effectById.ContainsKey(FaultIds.UsedFluidsLow) && effectById.TryGetValue("oil_leak", out var leak))
            {
                var low = JsonUtility.FromJson<EffectEntry>(JsonUtility.ToJson(leak));
                low.id = FaultIds.UsedFluidsLow;
                low.desc = "Fluids low — engine wears faster";
                effectById[low.id] = low;
            }
            if (!FaultLeaks.UsedIdsOn) return;
            foreach (var u in data.used)
            {
                string to = FaultIds.UsedIdFor(u.id, u.name);
                if (to != null) u.id = to;
            }
        }

        /// <summary>The used pool's row for an id, the family's own first.</summary>
        static UsedEntry UsedRow(string faultId, string family)
        {
            UsedEntry any = null;
            foreach (var u in data.used)
            {
                if (u.id != faultId) continue;
                if (u.origin == family) return u;
                if (any == null) any = u;
            }
            return any;
        }

        /// <summary>Every id a car can carry: the pools, then the used rows that
        /// are not in a pool (R0b's two). For the leak test and the class map.</summary>
        public static List<string> AllIdsWithUsed()
        {
            var ids = AllIds();
            foreach (var u in data.used) if (!ids.Contains(u.id)) ids.Add(u.id);
            foreach (var h in hitRows) if (!ids.Contains(h.id)) ids.Add(h.id);   // R1a
            return ids;
        }

        public static bool Ready { get { Load(); return data.pools.Count > 0; } }
        public static IReadOnlyList<UsedEntry> UsedFaults { get { Load(); return data.used; } }

        /// <summary>Every fault the pools can roll. Exposed for the self-test,
        /// which asserts that the inspection map has a home for all of them —
        /// an id with nowhere to be found is a fault the player can never
        /// diagnose, and nothing else in the game would ever say so.</summary>
        public static IReadOnlyList<PoolEntry> Pools { get { Load(); return data.pools; } }

        // ---- origin FAMILIES (R0a, Fault_System_Design_2026-11-11 §9) ----
        //
        // The pools are keyed jpn / usa / eur; the catalog's origins are
        // seven - jpn, usa, ger, gbr, fra, ita, eur. Every lookup used to fall
        // through "unknown -> jpn", so 77 German, British, French, Italian and
        // other European cars rolled Japanese faults at Japanese prices, and
        // the crash, bump-stop and wear rollers passed no origin at all (the
        // default was "jpn"), so every car did. Each roller now reads the
        // car's own family.

        /// <summary>PSX_FAULT_FAMILY=0 puts the old lookup back (an unknown
        /// or missing origin reads the Japanese pool).</summary>
        public static bool FamilyOn = System.Environment.GetEnvironmentVariable("PSX_FAULT_FAMILY") != "0";

        /// <summary>The pool family an origin belongs to. False (and jpn) for
        /// an origin the table does not know, so a self-test can prove that no
        /// catalog row takes the fallback.</summary>
        public static bool TryFamily(string origin, out string family)
        {
            switch (origin)
            {
                case "jpn": family = "jpn"; return true;
                case "usa": family = "usa"; return true;
                case "eur": case "ger": case "gbr": case "fra": case "ita": case "swe":
                    family = "eur"; return true;
            }
            family = "jpn";
            return false;
        }

        public static string Family(string origin) { TryFamily(origin, out var f); return f; }

        /// <summary>The pool the car's own catalog row reads (jpn without one).</summary>
        public static string FamilyOf(OwnedCar car) =>
            PoolOrigin(car != null ? CarCatalog.Get(car.specId)?.origin : null, null);

        /// <summary>The pool a roll reads: the origin given, or the car's own
        /// when none was, as its family. Off, the old rule exactly.</summary>
        static string PoolOrigin(string origin, OwnedCar car)
        {
            Load();
            if (!FamilyOn)
                return origin != null && originCostMult.ContainsKey(origin) ? origin : "jpn";
            if (string.IsNullOrEmpty(origin) && car != null) origin = CarCatalog.Get(car.specId)?.origin;
            return Family(origin);
        }

        /// <summary>A pool row nothing but a hit can cause: every source it
        /// lists is "impact" (bumper_crack, bumper_dent). R0a: with the crash
        /// stop-gap on, these come from the table at the hit, never from the
        /// player's own wear rolls.</summary>
        public static bool ImpactOnly(PoolEntry p)
        {
            if (p == null || string.IsNullOrEmpty(p.sources)) return false;
            foreach (var src in p.sources.Split('|'))
                if (src != "impact") return false;
            return true;
        }

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
                                             string cause = "wear", string origin = null,
                                             bool noImpactOnly = false)
        {
            // No origin given (the wear-threshold roller): the car's own
            // family (R0a; PoolOrigin). PSX_FAULT_FAMILY=0: jpn, as before.
            // noImpactOnly (R0a, the player's own wear rolls while the crash
            // stop-gap is on): a row only a hit can cause is never a wear
            // fault - see ImpactOnly - and with nothing else eligible the
            // roll is a no.
            Load();
            if (data.pools.Count == 0) return null;

            // Gate on how many faults already sit on this stat lane.
            int existing = 0;
            var existingIds = new HashSet<string>();
            foreach (var f in car.faults)
                if (f.stat == stat) { existing++; existingIds.Add(f.id); }
            if (!severe && existing > 0) return null;
            if (severe && existing >= 2) return null;

            origin = PoolOrigin(origin, car);   // its family (off: unknown → jpn)
            int carRank = TierRank(MileageTier(car.odoMiles));

            var eligible = new List<PoolEntry>();
            foreach (var p in data.pools)
            {
                if (p.origin != origin || p.stat != stat) continue;
                if (carRank < TierRank(p.minTier)) continue;     // mileage gate
                if (existingIds.Contains(p.id)) continue;        // per-stat dedupe
                if (noImpactOnly && ImpactOnly(p)) continue;     // R0a: wear cracks no bumper
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
            origin = PoolOrigin(origin, null);   // its family (off: unknown → jpn)

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

        // ------------------------------------------------------------------
        //  A fault BY NAME — the debug bench (see DebugCarOps)
        // ------------------------------------------------------------------
        //
        // Everything above ROLLS: a lane, a mileage gate, one fault per lane,
        // a dice throw. That is the game, and it is exactly what makes a
        // particular fault impossible to test — "drive with a warped rotor"
        // was "buy American cars until one turns up with a warped rotor".

        /// <summary>
        /// The pool row for a fault id, preferring the car's own origin.
        ///
        /// An id can sit in more than one origin's pool under a different name
        /// and price (SPARK PLUGS is in all three), and most sit in only one.
        /// The car's own row wins where it has one; otherwise the first row
        /// that carries the id, so a Japanese car CAN be handed a European
        /// cooling failure. Null for an id no pool knows.
        /// </summary>
        public static PoolEntry PoolRow(string faultId, string origin)
        {
            Load();
            if (FamilyOn) origin = Family(origin);   // a German car's own row is the eur one
            PoolEntry any = null;
            foreach (var p in data.pools)
            {
                if (p.id != faultId) continue;
                if (p.origin == origin) return p;
                if (any == null) any = p;
            }
            return any;
        }

        /// <summary>Every distinct fault id the pools carry, in pool order —
        /// which is lane order within an origin, so a list built from this
        /// reads engine, handling, body without sorting anything.</summary>
        public static List<string> AllIds()
        {
            Load();
            var seen = new HashSet<string>();
            var ids = new List<string>();
            foreach (var p in data.pools) if (seen.Add(p.id)) ids.Add(p.id);
            return ids;
        }

        /// <summary>
        /// Mint one specific fault, exactly as <see cref="RollWearFault"/>
        /// would have — same fields, same origin price multiplier, HIDDEN like
        /// everything else — minus the dice. No lane gate and no mileage gate:
        /// the caller asked for this fault by name and gets it. Null for an
        /// unknown id.
        /// </summary>
        public static CarFault MakeById(string faultId, string origin)
        {
            Load();
            origin = PoolOrigin(origin, null);   // its family (off: unknown → jpn)
            var row = PoolRow(faultId, origin) ?? HitRow(faultId, origin);   // R1a's hit rows
            if (row == null)
            {
                // R0b: a row only the used pool has (used_engine_worn,
                // used_fluids_low) - an advert's "Engine knock", the bench.
                var u = UsedRow(faultId, origin);
                if (u == null) return null;
                float um = originCostMult.TryGetValue(u.origin, out float uo) ? uo : 1f;
                return new CarFault
                {
                    id = u.id, label = u.name, stat = u.stat,
                    cost = Mathf.RoundToInt(u.cost * um), days = u.days, add = u.add,
                    repairType = u.type, hidden = true, diagnosed = false,
                    severity = u.tier == "catastrophic" || u.tier == "extensive" ? 2f : 1f,
                    pullDir = UnityEngine.Random.value < 0.5f ? -1 : 1,
                };
            }
            // Priced as the ROW's origin, not the car's: a borrowed European
            // fault is still a European part.
            float mult = originCostMult.TryGetValue(row.origin, out float m) ? m : 1f;
            return new CarFault
            {
                id = row.id,
                label = row.name,
                stat = row.stat,
                cost = Mathf.RoundToInt(row.cost * mult),
                days = row.days,
                add = row.add,
                repairType = row.type,
                hidden = true,
                diagnosed = false,
                severity = 1f,
                pullDir = UnityEngine.Random.value < 0.5f ? -1 : 1,
            };
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
                // ALIGNMENT PULLS IN ROAD-WHEEL DEGREES (R0a): its severity is
                // the band, its pullDir the side - the 0.25 of lock it used to
                // be is five degrees at 100 km/h, a hundred times a real
                // alignment drift (CrashStopGap.PullDeg). The other pulling
                // faults keep their share of lock until R1b's corners.
                if (CrashStopGap.On && f.id == AlignmentId)
                    agg.steerPullDeg += CrashStopGap.PullDeg(f.severity) * (f.pullDir >= 0 ? 1f : -1f);
                else
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
            /// <summary>R0a: the alignment pull, road-wheel degrees (+ = right).</summary>
            public float steerPullDeg;
            public bool rpmFlutter, steerSlow, hideGauges;
        }

        /// <summary>The one row whose pull is in degrees (R0a).</summary>
        public const string AlignmentId = "alignment";

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
