using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R0b: what a fault does that a person could NOTICE, in the player's own
    /// words - never the part, never what it means.
    ///   * the fixed chip list NAME WHAT YOU FELT offers after a test drive
    ///     (the same chips for every car, never filtered by what the car has),
    ///     and for each id which chip it shows on and the drive condition that
    ///     has to be met for it to show (design §6 "Seller viewings");
    ///   * the puddle the floor check sees: colour, where, how big;
    ///   * the starter car's one day-one line, the symptom only.
    /// </summary>
    public static class FaultSymptoms
    {
        // ---- NAME WHAT YOU FELT ------------------------------------------------

        /// <summary>The fixed list. Short: two to a row on a phone.</summary>
        public static readonly string[] Chips =
        {
            "PULLS TO A SIDE", "DOWN ON POWER", "MISFIRES", "SHIFTS SLIP", "BRAKES WEAK",
            "WALLOWS", "HEAVY STEERING", "RUNS HOT", "GAUGES / LIGHTS", "RATTLES",
        };
        public const int ChipPull = 0, ChipPower = 1, ChipMisfire = 2, ChipShift = 3, ChipBrakes = 4,
                         ChipWallow = 5, ChipHeavy = 6, ChipHot = 7, ChipGauges = 8, ChipRattle = 9;

        // What the drive did (bits, measured by TestDriveFeel). A cue shows
        // only when every bit it asks for was met on that drive.
        public const int CondCruise = 1 << 0;     // 5 s unbroken at 30 km/h or more
        public const int CondThrottle = 1 << 1;   // 3 s at 80% throttle or more, above 20 km/h
        public const int CondHighRpm = 1 << 2;    // 2 s above 3,500 rpm at 60% throttle or more
        public const int CondShifts = 1 << 3;     // two gear changes on the move
        public const int CondStop = 1 << 4;       // a brake from 60 km/h or more
        public const int CondCorner = 1 << 5;     // 2 s at 3 m/s^2 sideways or more, above 30 km/h
        public const int CondSlowSteer = 1 << 6;  // 1 s at half lock or more below 30 km/h
        public const int CondHot = 1 << 7;        // the needle reached HotMarkC
        public const int CondDrove = 1 << 8;      // 10 s on the move
        public const int CondNight = 1 << 9;      // after dark

        /// <summary>The temperature a RUNS HOT claim needs the needle to have
        /// reached on the drive.</summary>
        public const float HotMarkC = 105f;

        public struct Cue { public int chip, conds; }

        static readonly Dictionary<string, List<Cue>> cueCache = new Dictionary<string, List<Cue>>();

        static void Add(List<Cue> l, int chip, int conds)
        {
            foreach (var c in l) if (c.chip == chip && c.conds == conds) return;
            l.Add(new Cue { chip = chip, conds = conds });
        }

        /// <summary>
        /// The test-drive symptom lookup: which chips this id shows on, and
        /// when. Read off the effect it really has on the car (the same
        /// Aggregate_ rows the physics uses), so a fault that slows the car
        /// shows as DOWN ON POWER and one that does nothing a driver can feel
        /// (a dent, faded paint, a slow seep) shows on no chip at all.
        /// </summary>
        public static List<Cue> CuesOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return new List<Cue>();
            if (cueCache.TryGetValue(id, out var cached)) return cached;
            var l = new List<Cue>();
            var e = FaultCatalog.Effect(id);
            if (Mathf.Abs(e.steerPull) > 0.001f) Add(l, ChipPull, CondCruise);
            if (e.accelMult <= 0.921f) Add(l, ChipPower, CondThrottle);
            bool dimmer = e.nightVisMult < 0.999f;
            if (e.rpmFlutter && !dimmer) Add(l, ChipMisfire, CondHighRpm);
            if (e.rpmFlutter && dimmer) Add(l, ChipGauges, CondDrove);      // the gauges flicker
            if (e.shiftMult > 1.001f) Add(l, ChipShift, CondShifts);
            if (e.brakeMult < 0.999f) Add(l, ChipBrakes, CondStop);
            if (e.gripMult < 0.999f) Add(l, ChipWallow, CondCorner);
            if (e.steerSlow) Add(l, ChipHeavy, CondSlowSteer);
            if (e.hideGauges) Add(l, ChipGauges, CondDrove);
            if (dimmer) Add(l, ChipGauges, CondDrove | CondNight);             // dim lights, after dark
            if (id == "cooling_fail") Add(l, ChipHot, CondHot);
            if (id == "exhaust_rust" || id == "exhaust_rot" || id == "trim_rattle") Add(l, ChipRattle, CondDrove);
            DamageHomes.Cues(id, l);   // R1a part 2: the hit rows
            cueCache[id] = l;
            return l;
        }

        /// <summary>Did this fault show on this chip on a drive that met
        /// <paramref name="driveConds"/>?</summary>
        public static bool Showed(string id, int chip, int driveConds)
        {
            foreach (var c in CuesOf(id))
                if (c.chip == chip && (driveConds & c.conds) == c.conds) return true;
            return false;
        }

        // ---- the floor ------------------------------------------------------------

        /// <summary>Ids that leave something on the garage floor (or show at a
        /// corner by morning). PSX_FAULT_PUDDLE=0 reads the old four.</summary>
        public static readonly string[] FloorIds = DamageHomes.Floor(new[]   // R1a part 2: + the holed core
            { "oil_leak", "oil_pan_gasket", "ps_leak", "air_susp_leak", "cooling_fail", FaultIds.UsedFluidsLow });

        /// <summary>What the floor shows for one fault: colour, where, how big.
        /// Never the part.</summary>
        public static string Puddle(CarFault f)
        {
            if (f == null) return null;
            string size = f.severity >= 2f ? "as big as a dinner plate" : "about the size of your palm";
            switch (f.id)
            {
                case "oil_leak":
                case "oil_pan_gasket":
                    return "There is a fresh puddle under the car: dark brown, " + size +
                           ", under the front, toward the middle.";
                case "ps_leak":
                    return "There is a fresh puddle under the car: thin and reddish, " + size +
                           ", under the front, off to one side.";
                case "cooling_fail":
                    return "There is a fresh puddle under the car: bright green, " + size +
                           ", right under the nose.";
                case FaultIds.UsedFluidsLow:
                    return "A few dark drops on the floor, under the front.";
                case "air_susp_leak":
                    return "No puddle, but one corner sits lower than the others this morning.";
            }
            return DamageHomes.Puddle(f);   // R1a part 2: a holed core
        }

        // ---- the starter car ------------------------------------------------------

        /// <summary>
        /// The day-one line's verb phrase for a fault: what the driver would
        /// notice, nothing else ("the &lt;car&gt; idles up and down once it's
        /// warm"). The owner's own exception (2026-08-28): the starter's line
        /// exists to send a new player into the garage. Smells are the smell.
        /// </summary>
        public static string StarterSymptom(string id)
        {
            switch (id)
            {
                case "spark_plugs": return "stumbles and shakes at idle";
                case "timing_belt": return "feels flat when you push it";
                case "timing_chain": return "rattles for a few seconds on a cold start";
                case "valve_cover_gasket": return "smells of hot oil after a drive";
                case "o2_sensor": return "smells of raw fuel";
                case "oil_leak":
                case "oil_pan_gasket": return "leaves dark drops where it parks";
                case "trans_hesitation": return "hangs between gears";
                case "trans_slip": return "revs between gears without pulling";
                case "intake_manifold": return "idles up and down once it's warm";
                case "cam_sensor": return "stumbles when you open it up";
                case "alternator": return "has dim, flickering lights";
                case "cooling_fail": return "runs hot in traffic";
                case "carbon_buildup": return "is slow to answer the throttle";
                case "battery_drain": return "is slow to crank in the morning";
                case "electrical_sensor": return "cuts out for a moment now and then";
                case FaultIds.UsedEngineWorn: return "knocks when you lean on it";
                case FaultIds.UsedFluidsLow: return "leaves a few dark drops where it parks";
            }
            // Any other row: the first thing it does that a driver can feel.
            var cues = CuesOf(id);
            if (cues.Count > 0)
                switch (cues[0].chip)
                {
                    case ChipPull: return "pulls to one side";
                    case ChipPower: return "feels down on power";
                    case ChipMisfire: return "stumbles under load";
                    case ChipShift: return "slips between gears";
                    case ChipBrakes: return "takes a long way to stop";
                    case ChipWallow: return "wallows through corners";
                    case ChipHeavy: return "is heavy to steer at parking speed";
                    case ChipHot: return "runs hot";
                    case ChipGauges: return "has gauges that flicker";
                    case ChipRattle: return "rattles over bumps";
                }
            return "does not sound right";
        }
    }
}
