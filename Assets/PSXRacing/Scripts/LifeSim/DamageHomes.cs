using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R1a PART 2b: homes for part 1's six hit rows (panel_damage, bumper_bar,
    /// lamp_out, radiator_leak, exhaust_damage, roof_glass), PSX_FAULT_HOMES=0
    /// = part 1 (the rows exist and act, but no shelf part covers them, the
    /// floor and the chips never show them, CarValue ignores them, and the
    /// debug lists print nothing for them):
    ///   * Spares: which shelf parts replace them (a used radiator, a front
    ///     clip, a straight fender ...);
    ///   * the floor check: a holed core drips green under the nose, by stage
    ///     (R0b's puddle text: colour, place, size - never the part);
    ///   * NAME WHAT YOU FELT: the chip a driver could tie each to (the needle
    ///     climbs; a drone or rattle; a flap or roar at speed; a lamp dark at
    ///     night). The bar is hidden behind the cover: no chip;
    ///   * CarValue: what a buyer can see - a cracked cover, a dented door, a
    ///     dented roof - at the row's value % (mag). Crash HISTORY lowering a
    ///     repaired car's value is round R3;
    ///   * the debug fault lists (debug careers only): every row says what it
    ///     does on the road.
    /// </summary>
    public static class DamageHomes
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_FAULT_HOMES") != "0";

        /// <summary>The most visible damage takes off a car's value.</summary>
        public const float ValueCap = 0.6f;

        // R1c-A: PSX_SRS_DEALER=0 = a spent bag set takes nothing off the value and any shop fits it (part 2b).
        public static bool SrsOn = System.Environment.GetEnvironmentVariable("PSX_SRS_DEALER") != "0";
        /// <summary>A buyer sees the SRS lamp and the bag out: 12% off for the driver's bag, 18% for both.</summary>
        public const float SrsValuePct = 12f;
        /// <summary>Only the dealer sells an airbag set (no yard set, no DIY).</summary>
        public static bool DealerOnly(string id) => SrsOn && id == SrsBag.Id;

        // R1c-A: roof_glass by stage (PSX_FAULT_GLASS=0: the flat $480): glass $210, a dented roof and
        // the windshield $820, a crushed roof skin with most of the glass $1,950 (design: glass $150-300,
        // roof skin $600-1,500, shop only), scaled by the family's own flat price.
        static readonly int[] GlassPrice = { 480, 210, 820, 1950 };
        public static int GlassCost(int cost, float oldStage, float newStage)
        {
            if (!DamageModel.GlassRuleOn) return cost;
            int o = Mathf.Clamp(Mathf.RoundToInt(oldStage), 0, 3), n = Mathf.Clamp(Mathf.RoundToInt(newStage), 0, 3);
            float flat = cost * 480f / GlassPrice[o];
            return Mathf.RoundToInt(flat * GlassPrice[n] / 480f);
        }

        // Fix pass 1 (PSX_FAULT_PANELSTAGE; design: "used cover $40 + paint $60"): panel_damage by stage -
        // a scuff or a cracked cover $100, a dent $230 (the flat price before), crumpled $460, scaled by
        // the family's own flat price. =0: the flat price at every stage.
        public static bool PanelStageOn = System.Environment.GetEnvironmentVariable("PSX_FAULT_PANELSTAGE") != "0";
        static readonly int[] PanelPrice = { 230, 100, 230, 460 };
        public static int PanelCost(int cost, float oldStage, float newStage)
        {
            if (!PanelStageOn) return cost;
            int o = Mathf.Clamp(Mathf.RoundToInt(oldStage), 0, 3), n = Mathf.Clamp(Mathf.RoundToInt(newStage), 0, 3);
            float flat = cost * 230f / PanelPrice[o];
            return Mathf.RoundToInt(flat * PanelPrice[n] / 230f);
        }

        // ---------------- the shelf ----------------
        static void More(Dictionary<string, string[]> d, string key, params string[] ids)
        {
            if (!d.TryGetValue(key, out var have)) return;
            var l = new List<string>(have);
            foreach (var id in ids) if (!l.Contains(id)) l.Add(id);
            d[key] = l.ToArray();
        }

        /// <summary>Spares' static constructor: the parts that replace the hit rows.</summary>
        public static void ExtendSpares(Dictionary<string, string[]> byLabel, Dictionary<string, string[]> byKind)
        {
            if (!On || byLabel == null || byKind == null) return;
            More(byLabel, "USED RADIATOR", DamageModel.RadiatorId);
            More(byLabel, "BUMPER, UNCRACKED", DamageModel.PanelId, DamageModel.BarId);
            More(byLabel, "FRONT CLIP", DamageModel.PanelId, DamageModel.BarId, DamageModel.LampId, DamageModel.RadiatorId);
            More(byLabel, "TAIL LIGHT + TRIM", DamageModel.LampId);
            More(byLabel, "EXHAUST, CAT BACK", DamageModel.ExhaustId);
            More(byLabel, "STRAIGHT FENDER", DamageModel.PanelId);
            More(byLabel, "DOOR SKIN + GLASS", DamageModel.PanelId);
            More(byLabel, "SHELL PANELS, WHOLE CAR", DamageModel.PanelId, DamageModel.GlassId);
            More(byKind, "power", DamageModel.ExhaustId);
            More(byKind, "weight", DamageModel.PanelId);
        }

        // ---------------- the floor ----------------
        /// <summary>FaultSymptoms.FloorIds plus the holed core.</summary>
        public static string[] Floor(string[] ids)
        {
            if (!On || ids == null || System.Array.IndexOf(ids, DamageModel.RadiatorId) >= 0) return ids;
            var l = new List<string>(ids) { DamageModel.RadiatorId };
            return l.ToArray();
        }

        static int StageOf(CarFault f) => f == null ? 0 : f.stage >= 1f ? Mathf.RoundToInt(f.stage) : f.severity >= 2f ? 3 : 2;

        /// <summary>What the floor shows under a leaking core: colour, place,
        /// size by stage. Never the part. Null for any other row.</summary>
        public static string Puddle(CarFault f)
        {
            if (!On || f == null || f.id != DamageModel.RadiatorId) return null;
            int st = StageOf(f);
            if (st <= 1) return "A few bright green drops on the floor, right under the nose.";
            return "There is a fresh puddle under the car: bright green, " +
                   (st >= 3 ? "as big as a dinner plate" : "about the size of your palm") + ", right under the nose.";
        }

        // ---------------- NAME WHAT YOU FELT ----------------
        /// <summary>FaultSymptoms.CuesOf for the hit rows.</summary>
        public static void Cues(string id, List<FaultSymptoms.Cue> l)
        {
            if (!On || l == null) return;
            int chip = -1, conds = 0;
            switch (id)
            {
                case DamageModel.RadiatorId: chip = FaultSymptoms.ChipHot; conds = FaultSymptoms.CondHot; break;      // the needle climbs
                case DamageModel.ExhaustId: chip = FaultSymptoms.ChipRattle; conds = FaultSymptoms.CondDrove; break;  // drone, a rattle at one rpm
                case DamageModel.PanelId: chip = FaultSymptoms.ChipRattle; conds = FaultSymptoms.CondCruise; break;   // a flap at speed
                case DamageModel.GlassId: chip = FaultSymptoms.ChipRattle; conds = FaultSymptoms.CondCruise; break;   // wind roar
                case DamageModel.LampId: chip = FaultSymptoms.ChipGauges; conds = FaultSymptoms.CondDrove | FaultSymptoms.CondNight; break; // a beam dark
            }
            if (chip < 0) return;
            foreach (var c in l) if (c.chip == chip && c.conds == conds) return;
            l.Add(new FaultSymptoms.Cue { chip = chip, conds = conds });
        }

        // ---------------- value ----------------
        /// <summary>CarValue's share for the damage a buyer can see: the open
        /// panel and roof/glass rows at their value % (mag), capped.</summary>
        public static float ValueMult(OwnedCar car)
        {
            if (!On || car == null || car.faults == null) return 1f;
            float pct = 0f;
            foreach (var f in car.faults)
                if (f != null && (f.id == DamageModel.PanelId || f.id == DamageModel.GlassId)) pct += Mathf.Max(0f, f.mag);
                else if (f != null && SrsOn && f.id == SrsBag.Id && f.mag > 0f) pct += SrsValuePct * (1f + 0.5f * (Mathf.Min(f.mag, 2f) - 1f));   // R1c-A
                else if (f != null && WheelScuff.On && WheelScuff.Is(f.id)) pct += Mathf.Max(0f, f.mag);   // damage round 2: a scraped rim
            return 1f - Mathf.Min(ValueCap, pct / 100f);
        }

        // ---------------- the debug lists ----------------
        /// <summary>What a hit row does on the road, for the DEBUG lists only
        /// (FaultCatalog.EffectSummary, DebugCarOps.Describe). Null for other ids
        /// and for the exhaust, whose power cut is a catalog effect already.</summary>
        public static string EffectText(string id)
        {
            if (!On) return null;
            switch (id)
            {
                case DamageModel.RadiatorId: return "runs hot: loses coolant 0.5/2/6 %/min above 90 C by stage (live)";
                case DamageModel.BarId: return "next hit in its zone x" + DamageModel.BarCrushMult.ToString("0.0");
                case DamageModel.PanelId: return "value -1..-8% by stage";
                case DamageModel.LampId: return "that lamp dark";
                case DamageModel.GlassId: return "value -3/-15/-40% by stage";
                case SrsBag.Id: return SrsOn ? "value -12% (both bags -18%); dealer parts only" : null;   // R1c-A
            }
            if (WheelScuff.Is(id)) return "that rim scraped: value -0.4/-1/-2% by stage";   // damage round 2
            return null;
        }
    }

    /// <summary>
    /// R1a PART 2b: HEAT SOAK (design §5, OwnedCar parkedC; PSX_HEAT_SOAK=0 =
    /// every drive starts cold, as before). The apply-back parks the car with
    /// the needle where it ended; the next drive's fill reads how long it has
    /// sat. The engine sheds about two-thirds of its extra heat an hour: two
    /// legs in one block start warm (a quarter of an hour apart), the next
    /// block counts as straight after (the leg before used up its block), and
    /// a leg after an overnight - or two blocks on - starts cold. The brakes
    /// have no temperature model yet (parkedBrakeC waits for R2a's FEEL THE
    /// WHEELS). Added fields read 0 as cold: no save step.
    /// </summary>
    public static class HeatSoak
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_HEAT_SOAK") != "0";
        /// <summary>The share of the extra heat kept after an hour parked.</summary>
        public const float KeepPerHour = 1f / 3f;
        public const float SameBlockHours = 0.25f, NextBlockHours = 0.5f, BlockHours = 4f;

        /// <summary>The needle at the end of the leg (EngineTemp.StampResult).</summary>
        public static float EndC = float.NaN;
        static float primedC = float.NaN, primedKeep;

        /// <summary>Hours this car has sat since it was parked; infinity =
        /// cold (never parked, or an overnight since).</summary>
        public static float HoursParked(LifeState s, OwnedCar car)
        {
            if (s == null || car == null || car.parkedC <= 0f || car.parkedDay != s.day) return float.PositiveInfinity;
            int e = s.slotIndex - car.parkedSlot;
            if (e < 0) return float.PositiveInfinity;
            return e == 0 ? SameBlockHours : e == 1 ? NextBlockHours : NextBlockHours + BlockHours * (e - 1);
        }

        /// <summary>The share of its extra heat the engine still has.</summary>
        public static float Keep(LifeState s, OwnedCar car)
        {
            float h = HoursParked(s, car);
            return float.IsInfinity(h) ? 0f : Mathf.Pow(KeepPerHour, h);
        }

        /// <summary>The apply-back: park the car hot (ApplyHeatResult).</summary>
        public static void Park(LifeState s, OwnedCar car)
        {
            if (!On || s == null || car == null || float.IsNaN(EndC)) return;
            car.parkedC = EndC;
            car.parkedDay = s.day;
            car.parkedSlot = s.slotIndex;
            EndC = float.NaN;
        }

        /// <summary>The drive's fill (LifeHomeScreen.FillCarRequestFor).</summary>
        public static void Prime(LifeState s, OwnedCar car)
        {
            primedC = On && car != null && car.parkedC > 0f ? car.parkedC : float.NaN;
            primedKeep = Keep(s, car);
        }

        /// <summary>Where the needle starts for a parked temperature.</summary>
        public static float StartC(float parkedC, float keep, float ambient) =>
            parkedC > ambient ? ambient + (parkedC - ambient) * Mathf.Clamp01(keep) : ambient;

        /// <summary>The applier, right after StartCold: a warm engine starts
        /// warm. Once per fill. True when it warmed the engine.</summary>
        public static bool Warm(EngineTemp temp)
        {
            if (!On || temp == null || float.IsNaN(primedC) || !RaceHandoff.FromLifeSim) { primedC = float.NaN; return false; }
            float c = StartC(primedC, primedKeep, temp.celsius);
            primedC = float.NaN;
            if (c <= temp.celsius + 0.5f) return false;
            temp.celsius = c;
            return true;
        }
    }
}
