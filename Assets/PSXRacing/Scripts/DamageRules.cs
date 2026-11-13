using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// R1a PART 2b "rules and proof" (Fault_System_Design_2026-11-11 §3.1, the
    /// R1a row of §10; OWNER'S ANSWERS 1 and 8). Rules the located model
    /// (DamageModel / DamageLedger) reads. Each has its switch; 0 = part 1.
    ///
    /// THE STRUCTURAL FAIL (PSX_FAULT_FAIL=0: every hit leaves a car that
    /// drives, as part 1 shipped). The car cannot go on after:
    ///   * a hit at the AI's retire line (AIDriver.RetireHitMps, 17 m/s: the
    ///     closing speed's normal component, as the AI reads it, or the pulse's
    ///     delta-V), or a DROP that lands at 17 m/s or more;
    ///   * a ROLL that crushed the roof (the ROOF table's third band);
    ///   * a broken knuckle: a CORNER hit in the top band (8 m/s), 20% ("wheel
    ///     folds: FAIL, tow");
    ///   * a failed subframe: a FRONT hit in the top band (10 m/s), 50%.
    /// The drive goes (no power: the throttle idles the engine), the HUD shows
    /// the tow line, the leave row is CALL TOW TRUCK in a career
    /// (CareerDrive.NeedsTow), and a race scores the player DNF while it goes
    /// on (RaceManager.RetirePlayer). The incident is banked once, like every
    /// other (hitId). WATER's hydrolock already seizes through EngineTemp.
    /// Owner, PC33 (2026-11-13): "I drove 80mph head-on into a tree and didn't
    /// have any immediate impact on my ability to drive the rest of the race."
    ///
    /// MULTI-ZONE (PSX_FAULT_SPAN=0: one zone per contact). A horizontal push
    /// above the beltline at the car's END (a van's high tail, a truck bed)
    /// spans the cowl line: the glass takes it AND the nose (or tail) under it.
    /// PC33's census: an RX-7 into a van's tail at 12 m/s got ROOF only.
    ///
    /// THE SCRAPE KEEPS ITS INCIDENT OPEN (PSX_FAULT_SCRAPE=0: part 1). A held
    /// contact after its 0.2 s pulse adds no delta-V, but the incident's merge
    /// window runs from the LAST contact, so a swipe along 4 m barrier
    /// sections is one incident, not one per section.
    ///
    /// THE CRUSH ORDER (PSX_FAULT_CRUSH=0: part 1's dice at every band). The
    /// FRONT order is cover, bar, lamps, hood/fenders, ... radiator, core
    /// support, rails: once a hit reaches the rails (8 m/s) everything ahead of
    /// them in the order is gone, whatever the dice said; at 10 m/s the core
    /// is crushed. The bar takes stages (crushed / pushed back / torn away),
    /// so a harder later hit raises the row the car already carries.
    /// </summary>
    public static class DamageRules
    {
        public static bool FailOn = Env("PSX_FAULT_FAIL") != "0";
        public static bool SpanOn = Env("PSX_FAULT_SPAN") != "0";
        public static bool ScrapeOn = Env("PSX_FAULT_SCRAPE") != "0";
        public static bool CrushOn = Env("PSX_FAULT_CRUSH") != "0";
        /// <summary>R1c-A, owner PC34 (2026-11-13: "I don't recall hitting anything and the car is
        /// completely decimated" - a snowy mountain road, three side contacts with the tall roadside
        /// WallColl read as ROOF: the roof crumpled, the glass cracked). On an upright car a mostly
        /// horizontal push is a FRONT, REAR or SIDE hit at ANY height (above the belt it reaches the
        /// side glass and the mirror, not the roof). ROOF only for a push from above - in the car's
        /// frame AND in the world (a wall leaning over the road is not one) - or a car on its side or
        /// roof. PSX_FAULT_SIDEHIGH=0: part 2b (a patch wholly above the belt is ROOF).</summary>
        public static bool SideHighOn = Env("PSX_FAULT_SIDEHIGH") != "0";
        /// <summary>A push from above points at least 30 degrees below the horizon in the world.</summary>
        public const float FromAboveWorldY = -0.5f;
        /// <summary>PSX_FAULT_BODYFIRST=0: a contact centred above the beltline is ROOF
        /// whatever lies under it (part 1; a low car backed into a wall read ROOF).</summary>
        public static bool BodyFirstOn = Env("PSX_FAULT_BODYFIRST") != "0";
        /// <summary>PSX_FAULT_HOLED=0: a crash-holed core cools like any other until the
        /// system is 45% down (CoolingModel's 55% curve; part 1: 2 %/min took 48 min to 112 C).</summary>
        public static bool HoledOn = Env("PSX_FAULT_HOLED") != "0";
        /// <summary>A crash-holed core (EngineTemp.radiatorWeep from the located model)
        /// runs unpressurised and draws air as the level drops: cooling falls away from
        /// 97% coolant to nothing at 80% (about a litre of seven gone) instead of
        /// CoolingModel's 55%. Tuned on EngineTemp's balance to the design's gate: a holed
        /// core (2 %/min) at 80 km/h reaches 112 C in about 8 min (6-12); a seam weep
        /// (0.5) in about half an hour; a crushed core (6) in about 3.</summary>
        public const float HoledFullAbove = 97f, HoledDryAt = 80f;

        public static float CoolantEff(float pct, float weep)
        {
            float eff = CoolingModel.CoolantEff(pct);
            if (!HoledOn || weep <= 0f || !DamageModel.Live) return eff;
            return Mathf.Min(eff, Mathf.Clamp01((pct - HoledDryAt) / (HoledFullAbove - HoledDryAt)));
        }
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);

        public const float FailMps = AIDriver.RetireHitMps;
        public const float KnuckleP = 0.20f, SubframeP = 0.50f;
        /// <summary>The FAIL rolls on the water die: hits never read it.</summary>
        public const int FailDie = 11;
        public const int FailNone = 0, FailHit = 1, FailRoof = 2, FailKnuckle = 3, FailSubframe = 4;

        /// <summary>Logs, the census and the self-test only - never shown to
        /// the player (it names parts).</summary>
        public static string FailName(int f) =>
            f == FailHit ? "retire-line hit" : f == FailRoof ? "roof crushed" :
            f == FailKnuckle ? "knuckle" : f == FailSubframe ? "subframe" : "none";

        /// <summary>Which FAIL an incident is (pure; for one set of dice a
        /// harder incident never fails less).</summary>
        public static int FailOf(Incident inc)
        {
            if (!FailOn || inc == null) return FailNone;
            if (inc.kind == DamageModel.KHit && (inc.dv >= FailMps || inc.closing >= FailMps)) return FailHit;
            if (inc.kind == DamageModel.KDrop && inc.dv >= FailMps) return FailHit;
            if (inc.kind == DamageModel.KRoll && inc.band >= 3) return FailRoof;
            if (inc.kind != DamageModel.KHit) return FailNone;
            float die = inc.draws != null && inc.draws.Length > FailDie ? inc.draws[FailDie] : 1f;
            if (inc.zone == DamageModel.ZCorner && inc.band >= 4 && die < KnuckleP) return FailKnuckle;
            if (inc.zone == DamageModel.ZFront && inc.band >= 5 && die < SubframeP) return FailSubframe;
            return FailNone;
        }

        /// <summary>The ledger's FAIL on the banking car (once a drive).</summary>
        internal static void OnFail(CarController car, Incident inc, int fail)
        {
            Debug.Log("[Damage] structural FAIL (" + FailName(fail) + "): " + DamageModel.KindName(inc.kind) + " " +
                      DamageModel.ZoneName(inc.zone) + ", delta-V " + inc.dv.ToString("0.0") + ", closing " +
                      inc.closing.ToString("0.0") + " m/s - the car cannot go on");
            if (car == null) return;
            car.faultAccelMult = 0f;
            var rm = RaceManager.Instance;
            if (rm != null && rm.playerCar == car) rm.RetirePlayer();
        }

        /// <summary>This car's drive is over: it cannot go on (a career car's
        /// banking ledger reached a FAIL).</summary>
        public static bool Wrecked(CarController car)
        {
            if (car == null) return false;
            var r = car.GetComponent<CollisionResponder>();
            var L = r != null ? r.Damage : null;
            return L != null && L.Banks && L.Failed != FailNone;
        }

        /// <summary>
        /// A contact the located model put in the ROOF that also reached the
        /// panel under it: a horizontal push (not a landing on the roof),
        /// mostly fore-aft, at the car's end - the glass and the nose (tail)
        /// both took it. ZNone otherwise.
        /// </summary>
        public static bool BodyFirst(DamageModel.Shell sh, float minY) => BodyFirstOn && sh != null && minY <= sh.beltY;

        public static int SpanZone(DamageModel.Shell sh, Vector3 p, Vector3 n, int zone)
        {
            if (!SpanOn || sh == null || zone != DamageModel.ZRoof) return DamageModel.ZNone;
            if (Mathf.Abs(n.y) > DamageModel.VerticalDot) return DamageModel.ZNone;
            if (Mathf.Abs(n.z) < 0.5f || Mathf.Abs(p.z) < 0.5f * sh.half.z) return DamageModel.ZNone;
            return p.z > 0f ? DamageModel.ZFront : DamageModel.ZRear;
        }
    }
}
