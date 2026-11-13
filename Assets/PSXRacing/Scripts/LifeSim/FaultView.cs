using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R0b "STOP THE LEAKS" (Fault_System_Design_2026-11-11 §1.2, §6 "Closing
    /// the leaks", §7, §9). The car tells; only a person names the part. Every
    /// fault list the player sees reads ONE gate, <see cref="Known"/>: the
    /// garage, the pre-race page, the viewing, the 3D garage bench, the spares
    /// page, the seller's driveway. Later rounds (knownBy, knownStage) change
    /// what "known" means here and nowhere else.
    /// </summary>
    public static class FaultView
    {
        /// <summary>May the player see this fault named? Today: somebody found
        /// it (an inspection, a mechanic, a dealer) or the seller disclosed it.</summary>
        public static bool Known(CarFault f) => f != null && !f.hidden;

        public static int KnownCount(OwnedCar car)
        {
            int n = 0;
            if (car != null && car.faults != null)
                foreach (var f in car.faults) if (Known(f)) n++;
            return n;
        }

        public static List<CarFault> KnownList(OwnedCar car)
        {
            var list = new List<CarFault>();
            if (car != null && car.faults != null)
                foreach (var f in car.faults) if (Known(f)) list.Add(f);
            return list;
        }
    }

    /// <summary>
    /// R0b's switches. Each =0 puts today's behaviour back (the text fixes
    /// have none: no part name or effect number comes back with a switch).
    /// </summary>
    public static class FaultLeaks
    {
        static bool On(string key) => System.Environment.GetEnvironmentVariable(key) != "0";

        /// <summary>PSX_FAULT_FELT=0: a test drive rolls dice that name faults,
        /// as many drives a day as the player likes, no NAME WHAT YOU FELT.</summary>
        public static bool FeltOn = On("PSX_FAULT_FELT");
        /// <summary>PSX_FAULT_BYCLASS=0: services clear their whole lane, a
        /// seizure and an engine job clear every engine-lane row, a respray the
        /// (empty) paint lane.</summary>
        public static bool ByClassOn = On("PSX_FAULT_BYCLASS");
        /// <summary>PSX_FAULT_USEDIDS=0: the used rows roll under their old ids
        /// (trans_slip, oil_leak) and an advert's problem rolls a random row of
        /// a lane. Migrated saves keep the new ids either way.</summary>
        public static bool UsedIdsOn = On("PSX_FAULT_USEDIDS");
        /// <summary>PSX_FAULT_PUDDLE=0: the floor check names the part and
        /// reveals the fault, as it did.</summary>
        public static bool PuddleOn = On("PSX_FAULT_PUDDLE");
        /// <summary>PSX_FAULT_DEBUGGATE=0: DEBUG INFO in every career.</summary>
        public static bool DebugGateOn = On("PSX_FAULT_DEBUGGATE");

        /// <summary>
        /// May the pause menu offer DEBUG INFO (the live physics readout)? Only
        /// in a DEBUG CAREER. A drive with no career under it (the CITY test
        /// page, arcade, an editor scene played on its own) has no faults to
        /// give away and keeps it.
        /// </summary>
        public static bool DebugInfoAllowed()
        {
            if (!DebugGateOn || !Edition.HasCareer || !RaceHandoff.FromLifeSim) return true;
            var s = LifeSimManager.State;
            return s != null && s.debugMode;
        }
    }

    /// <summary>
    /// R0b's legacy CLASS for every RG2 id (design §2 "Fault data", §7 "Engine
    /// jobs keep what is not the engine"), which the seizure, engine-job,
    /// respray and service rules read instead of the stat lane. The engine
    /// lane holds the gearbox, charging and sensor rows too, so a lane sweep
    /// let a seized engine cure a slipping gearbox and an oil change clear a
    /// hidden one.
    /// </summary>
    public enum FaultClassKind
    {
        Other = 0, EngineInternal, LongBlock, Gearbox, Charging, Sensors, Cooling,
        Body, PaintFinish, Chassis, Electrical,
    }

    public static class FaultClass
    {
        static readonly Dictionary<string, FaultClassKind> Map = new Dictionary<string, FaultClassKind>
        {
            // engine-internal: gone with a seizure, renewed by a rebuild or a swap
            { "timing_belt", FaultClassKind.EngineInternal },
            { "timing_chain", FaultClassKind.EngineInternal },
            { "carbon_buildup", FaultClassKind.EngineInternal },
            { FaultIds.UsedEngineWorn, FaultClassKind.EngineInternal },
            // long block: stays on a seized engine, renewed by a rebuild or a swap
            { "valve_cover_gasket", FaultClassKind.LongBlock },
            { "oil_pan_gasket", FaultClassKind.LongBlock },
            { "oil_leak", FaultClassKind.LongBlock },
            { "spark_plugs", FaultClassKind.LongBlock },
            { "intake_manifold", FaultClassKind.LongBlock },
            { FaultIds.UsedFluidsLow, FaultClassKind.LongBlock },
            // gearbox, charging, sensors: never the engine's
            { "trans_slip", FaultClassKind.Gearbox },
            { "trans_hesitation", FaultClassKind.Gearbox },
            { "alternator", FaultClassKind.Charging },
            { "battery_drain", FaultClassKind.Charging },
            { "o2_sensor", FaultClassKind.Sensors },
            { "cam_sensor", FaultClassKind.Sensors },
            { "electrical_sensor", FaultClassKind.Sensors },
            // cooling: an engine job renews the hoses and coolant, not the core
            { "cooling_fail", FaultClassKind.Cooling },
            // body (rust stays under fresh paint), and the finish a respray renews
            { "bumper_crack", FaultClassKind.Body },
            { "bumper_dent", FaultClassKind.Body },
            { "minor_rust", FaultClassKind.Body },
            { "panel_rust", FaultClassKind.Body },
            { "paint_bubble", FaultClassKind.Body },
            { "frame_rust", FaultClassKind.Body },
            { "exhaust_rust", FaultClassKind.Body },
            { "exhaust_rot", FaultClassKind.Body },
            { "trim_rattle", FaultClassKind.Body },
            // R1a's located hit rows
            { "panel_damage", FaultClassKind.Body },
            { "bumper_bar", FaultClassKind.Body },
            { "lamp_out", FaultClassKind.Body },
            { "srs_deployed", FaultClassKind.Body },   // R1a part 2a
            { "roof_glass", FaultClassKind.Body },
            { "exhaust_damage", FaultClassKind.Body },
            { "radiator_leak", FaultClassKind.Cooling },
            { "paint_fade", FaultClassKind.PaintFinish },
            { "electrical_gremlin", FaultClassKind.Electrical },
            { "display_failure", FaultClassKind.Electrical },
            // chassis: brakes, steering, suspension, tyres
            { "strut_bushings", FaultClassKind.Chassis },
            { "control_arm_bush", FaultClassKind.Chassis },
            { "ps_leak", FaultClassKind.Chassis },
            { "alignment", FaultClassKind.Chassis },
            { "tire_wear", FaultClassKind.Chassis },
            { "strut_wear", FaultClassKind.Chassis },
            { "control_arm_rust", FaultClassKind.Chassis },
            { "rotor_warp", FaultClassKind.Chassis },
            { "ball_joint", FaultClassKind.Chassis },
            { "air_susp_leak", FaultClassKind.Chassis },
            { "sport_brake_wear", FaultClassKind.Chassis },
            { "bushing_clunk", FaultClassKind.Chassis },
        };

        /// <summary>The class of an id; Other (cleared by no class rule) for an
        /// id the map does not know.</summary>
        public static FaultClassKind Of(string id) =>
            id != null && Map.TryGetValue(id, out var k) ? k : FaultClassKind.Other;

        /// <summary>Every id the map knows, for the self-test (every pool and
        /// used id must have a class).</summary>
        public static IEnumerable<string> Ids => Map.Keys;

        /// <summary>A seizure takes the engine's insides and nothing else.</summary>
        public static bool ClearedBySeizure(CarFault f) => f != null && Of(f.id) == FaultClassKind.EngineInternal;

        /// <summary>A rebuild or a swap: the insides and the long block (new
        /// gaskets, belt, plugs and fluids). Not the gearbox, the charging, the
        /// sensors or the radiator (no swap offers "with ancillaries" yet).</summary>
        public static bool ClearedByEngineJob(CarFault f)
        {
            var k = f != null ? Of(f.id) : FaultClassKind.Other;
            return k == FaultClassKind.EngineInternal || k == FaultClassKind.LongBlock;
        }

        /// <summary>A respray renews the finish (the clear coat); rust under it stays.</summary>
        public static bool ClearedByRespray(CarFault f) => f != null && Of(f.id) == FaultClassKind.PaintFinish;

        /// <summary>
        /// SERVICES WORK ON PARTS (§7): each removes only the faults whose part
        /// it replaces, keyed by the service's own name. An oil change tops the
        /// fluids up; a tune-up is plugs; new tyres are the tyres; body work is
        /// dents and rust it cuts out. A rotation and a touch-up replace nothing.
        /// </summary>
        static readonly Dictionary<string, string[]> ServiceIds = new Dictionary<string, string[]>
        {
            { "OIL CHANGE",     new[] { FaultIds.UsedFluidsLow } },
            { "ENGINE TUNE-UP", new[] { "spark_plugs" } },
            { "TIRE ROTATION",  new string[0] },
            { "NEW TIRES",      new[] { "tire_wear" } },
            { "BODY PATCH",     new[] { "minor_rust", "bumper_dent" } },
            { "FULL BODY WORK", new[] { "minor_rust", "bumper_dent", "bumper_crack", "panel_rust", "paint_bubble" } },
            { "PAINT TOUCH-UP", new string[0] },
        };

        public static bool ServiceClears(string serviceName, string faultId) =>
            serviceName != null && faultId != null &&
            ServiceIds.TryGetValue(serviceName, out var ids) && System.Array.IndexOf(ids, faultId) >= 0;

        /// <summary>The ids a service clears (empty for an unknown name).</summary>
        public static string[] ServiceClearsIds(string serviceName) =>
            serviceName != null && ServiceIds.TryGetValue(serviceName, out var ids) ? ids : new string[0];
    }

    /// <summary>
    /// WHY A FAULT HAPPENED (owner, 2026-11-12: "you can ask a mechanic or
    /// dealer why a fault happened once they have diagnosed or fixed it").
    /// Every row is stamped where it is created with its cause class, the day,
    /// the odometer and, for a hit, where and how hard. Never shown in R0b (the
    /// conversation that reads it is R2b's); TestNoPartLeak keeps it off screen.
    /// </summary>
    public static class FaultCause
    {
        public const string Impact = "impact";              // a hit, a kerb, a bump stop
        public const string Wear = "wear";                  // distance wear
        public const string Service = "service";            // an overdue service
        public const string Abuse = "abuse";
        public const string Heat = "heat";
        public const string Age = "age";
        public const string Mod = "mod";                    // a modification
        public const string Workmanship = "workmanship";
        public const string PreviousOwner = "previous_owner";
        public const string Seller = "seller";              // seller-disclosed
        public const string Legacy = "legacy";              // a row from before v25: unknown
        public const string Planted = "debug";              // the debug bench

        public static readonly string[] All =
            { Impact, Wear, Service, Abuse, Heat, Age, Mod, Workmanship, PreviousOwner, Seller, Legacy, Planted };

        /// <summary>Zone codes in <see cref="CarFault.zone1"/>: 0 unknown, then
        /// R0a's WorstHitZone (1 front, 2 rear, 3 side) and 4 under (a bump stop).</summary>
        public const int ZoneUnder = 4;
        /// <summary>R1a: 5 roof (glass, pillars), 6 a corner (corner1 says which).</summary>
        public const int ZoneRoof = 5, ZoneCorner = 6;

        /// <summary>Stamp a new row. Returns it, so a roll can be wrapped; null
        /// stays null (the picker's normal "no").</summary>
        public static CarFault Stamp(CarFault f, string cause, int day, float odo, string note = null)
        {
            if (f == null) return null;
            f.cause = cause ?? "";
            f.causeNote = note ?? "";
            f.bornDay = Mathf.Max(0, day);
            f.bornOdo = Mathf.Max(0f, odo);
            return f;
        }

        public static CarFault StampImpact(CarFault f, int day, float odo, int zone, int side, float mps)
        {
            if (f == null) return null;
            string where = zone == 1 ? "front" : zone == 2 ? "rear" : zone == 3 ? "side"
                         : zone == ZoneUnder ? "under" : zone == ZoneRoof ? "roof" : zone == ZoneCorner ? "corner" : "unknown";
            string lr = side < 0 ? " left" : side > 0 ? " right" : "";
            Stamp(f, Impact, day, odo, where + lr + (mps > 0f ? " " + mps.ToString("0.0") + " m/s" : ""));
            f.zone1 = Mathf.Clamp(zone, 0, ZoneCorner);
            f.hitSide = side < 0 ? -1 : side > 0 ? 1 : 0;
            f.hitMps = Mathf.Max(0f, mps);
            return f;
        }

        public static void StampAll(List<CarFault> rows, string cause, int day, float odo, string note = null)
        {
            if (rows == null) return;
            foreach (var f in rows) Stamp(f, cause, day, odo, note);
        }

        /// <summary>A row with no cause (from before v25, or a path that
        /// forgot) - what the migration reads.</summary>
        public static bool Missing(CarFault f) => f != null && string.IsNullOrEmpty(f.cause);
    }

    /// <summary>
    /// R0b's ids: the two used-car rows get their own (design §4 "How the RG2
    /// ids map"), the newspaper's problem phrases map to matching ids, and the
    /// v25 step moves an old career across.
    /// </summary>
    public static class FaultIds
    {
        /// <summary>The used pool's "Engine Rebuild Needed" / "Engine
        /// Replacement" (both carried trans_slip, so they were found under
        /// TRANSMISSION and acted as a slipping gearbox).</summary>
        public const string UsedEngineWorn = "used_engine_worn";
        /// <summary>The used pool's "Fluid Top-Off Needed" (it carried oil_leak,
        /// so it was found under the oil pan and cleared by the wrong service).</summary>
        public const string UsedFluidsLow = "used_fluids_low";

        static readonly string[] EngineWornLabels = { "Engine Rebuild Needed", "Engine Replacement" };
        const string FluidsLowLabel = "Fluid Top-Off Needed";

        /// <summary>The used row's own id for an old (id, label), or null when
        /// the row is not one of the two.</summary>
        public static string UsedIdFor(string oldId, string label)
        {
            if (oldId == "trans_slip" && System.Array.IndexOf(EngineWornLabels, label) >= 0) return UsedEngineWorn;
            if (oldId == "oil_leak" && label == FluidsLowLabel) return UsedFluidsLow;
            return null;
        }

        /// <summary>The paper's problem phrases with the switch on. "Cracked
        /// windshield" has no row (glass arrives with R1a's roof_glass), so the
        /// paper prints a problem that has one instead.</summary>
        public static readonly string[] AdProblems =
        {
            "Engine knock", "Leaky radiator", "Worn brakes",
            "Bad transmission", "Oil leak", "Rusty bodywork",
        };

        /// <summary>The fault an advert's phrase means, in the car's pool
        /// family. Null for a phrase with no row (an old paper's "Cracked
        /// windshield"): the seller told the truth about glass the fault
        /// system has no row for yet.</summary>
        public static string ForProblem(string problem, string family)
        {
            switch (problem)
            {
                case "Engine knock": return UsedEngineWorn;
                case "Leaky radiator": return "cooling_fail";
                case "Worn brakes": return family == "usa" ? "rotor_warp" : "sport_brake_wear";
                case "Bad transmission": return "trans_slip";
                case "Oil leak": return family == "usa" ? "oil_pan_gasket" : "oil_leak";
                case "Rusty bodywork": return family == "usa" ? "panel_rust" : family == "eur" ? "paint_bubble" : "minor_rust";
                default: return null;
            }
        }

        /// <summary>
        /// Put the advertised problem on a car: the matching row, SELLER-
        /// DISCLOSED (found and named - the seller said so). A row of that id
        /// already on the car (a used roll found the same thing) is marked
        /// known instead of doubled. Null when the phrase has no row.
        /// </summary>
        public static CarFault Disclose(OwnedCar car, string problem, string origin, int day)
        {
            if (car == null || string.IsNullOrEmpty(problem)) return null;
            string id = ForProblem(problem, FaultCatalog.Family(origin));
            if (id == null) return null;
            var have = car.faults.Find(x => x != null && x.id == id);
            if (have != null)
            {
                have.hidden = false; have.diagnosed = true;
                return have;
            }
            var f = FaultCatalog.MakeById(id, origin);
            if (f == null) return null;
            f.hidden = false; f.diagnosed = true;
            FaultCause.Stamp(f, FaultCause.Seller, day, car.odoMiles, problem);
            car.faults.Add(f);
            return f;
        }

        /// <summary>
        /// The v25 step for one car (owned or a viewing's phantom): the two used
        /// rows onto their own ids (hidden and diagnosed never change - the v7
        /// lesson), and every row without a cause marked LEGACY. Idempotent:
        /// a row already moved, or already stamped, is left alone.
        /// </summary>
        public static void MigrateCar(OwnedCar car)
        {
            if (car == null || car.faults == null) return;
            car.faults.RemoveAll(f => f == null);
            foreach (var f in car.faults)
            {
                string to = UsedIdFor(f.id, f.label);
                if (to != null) f.id = to;
                if (FaultCause.Missing(f)) FaultCause.Stamp(f, FaultCause.Legacy, 0, 0f);
            }
        }

        /// <summary>A booked repair of a moved row moves with it, by the same
        /// (old id, label) test.</summary>
        public static void MigrateJob(PendingPart p)
        {
            if (p == null || string.IsNullOrEmpty(p.faultId)) return;
            string to = UsedIdFor(p.faultId, p.label);
            if (to != null) p.faultId = to;
        }
    }
}
