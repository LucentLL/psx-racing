using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R0a: the apply-back half of the crash stop-gap (see
    /// <see cref="CrashStopGap"/>). The drive rolled the table AT THE HIT and
    /// the exit stamp handed the result over in RaceHandoff.Crash*; this
    /// writes exactly that onto the owned car - paint, body, the radiator,
    /// and the bumper and alignment rows - and rolls nothing of its own.
    ///
    /// The rows are HIDDEN like every other fault (FaultCatalog.MakeById), so
    /// the player is told no part name: the car pulls, the bumper hangs, the
    /// needle climbs, and an inspection or a shop says why. Priced by the
    /// car's own origin family.
    /// </summary>
    public static class CrashBank
    {
        /// <summary>Bank the rolled outcome of the leg just stamped. Nothing
        /// when no ledger ran (a scene with no career car, or the switch off).</summary>
        public static void Apply(LifeState s, OwnedCar car)
        {
            if (car == null || !RaceHandoff.CrashRolled) return;
            // R0b: the rows carry their cause - the leg's hardest hit (zone,
            // side, closing speed), or the hardest bump-stop strike when the
            // leg met no wall.
            bool wall = RaceHandoff.WorstHitZone > 0;
            ApplyOutcome(car, FromHandoff(), s != null ? s.day : 0,
                wall ? RaceHandoff.WorstHitZone : (RaceHandoff.BottomWorstWheel > 0 ? FaultCause.ZoneUnder : 0),
                wall ? RaceHandoff.WorstHitSide : WheelSide(RaceHandoff.BottomWorstWheel),
                wall ? RaceHandoff.WorstHitMps : RaceHandoff.BottomWorst);
        }

        /// <summary>The side of a wheel stored +1 in the CarController order
        /// (FL FR RL RR): -1 left, +1 right, 0 none.</summary>
        public static int WheelSide(int wheel1) =>
            wheel1 == 1 || wheel1 == 3 ? -1 : wheel1 == 2 || wheel1 == 4 ? 1 : 0;

        /// <summary>The outcome the exit stamp wrote.</summary>
        public static CrashStopGap.Outcome FromHandoff() => new CrashStopGap.Outcome
        {
            classes = (CrashStopGap.Cls)RaceHandoff.CrashClasses,
            paintLoss = RaceHandoff.CrashPaintLoss,
            bodyLoss = RaceHandoff.CrashBodyLoss,
            radiatorLoss = RaceHandoff.CrashRadiatorLoss,
            bumperId = RaceHandoff.CrashBumperId,
            alignSide = RaceHandoff.CrashAlignSide,
            alignSeverity = RaceHandoff.CrashAlignSeverity,
        };

        /// <summary>
        /// Write an outcome onto a car: the lanes, then the rows. A bumper row
        /// the car already carries is not written twice. An alignment it
        /// already carries is never doubled - one car, one alignment - and is
        /// replaced in place by this hit's band and side when
        /// CrashStopGap.NewAlignWins says so (the worse band wins, a tie goes to
        /// this hit): the rule CarController steered by through
        /// CrashStopGap.CombinePull from the moment of the hit, so the car
        /// pulls on the next drive exactly as it pulled after the crash.
        /// </summary>
        public static void ApplyOutcome(OwnedCar car, CrashStopGap.Outcome o,
                                        int day = 0, int zone = 0, int side = 0, float mps = 0f)
        {
            if (car == null) return;
            if (o.paintLoss > 0f) car.paint = Mathf.Max(0f, car.paint - o.paintLoss);
            if (o.bodyLoss > 0f) car.carHP = Mathf.Max(0f, car.carHP - o.bodyLoss);
            if (o.radiatorLoss > 0f) car.radiator = Mathf.Max(0f, car.radiator - o.radiatorLoss);
            if (o.alignSeverity > 0)
            {
                var old = car.faults.Find(f => f != null && f.id == FaultCatalog.AlignmentId);
                if (old != null && CrashStopGap.NewAlignWins(CrashStopGap.Band(old.severity), o.alignSeverity))
                {
                    old.severity = o.alignSeverity;
                    old.pullDir = o.alignSide < 0 ? -1 : 1;
                    // R0b: the newer, worse hit is now the reason it pulls.
                    FaultCause.StampImpact(old, day, car.odoMiles, zone, side, mps);
                }
            }
            var rows = NewRows(car, o);
            foreach (var r in rows) FaultCause.StampImpact(r, day, car.odoMiles, zone, side, mps);
            car.faults.AddRange(rows);
        }

        /// <summary>The fault rows this outcome ADDS to this car, priced by
        /// its family (nothing is changed on the car). Exposed for the sweep.</summary>
        public static List<CarFault> NewRows(OwnedCar car, CrashStopGap.Outcome o)
        {
            var rows = new List<CarFault>();
            string family = FaultCatalog.FamilyOf(car);
            var have = car != null ? car.faults : new List<CarFault>();
            if (!string.IsNullOrEmpty(o.bumperId) && !have.Exists(f => f != null && f.id == o.bumperId))
            {
                var b = FaultCatalog.MakeById(o.bumperId, family);
                if (b != null) rows.Add(b);
            }
            if (o.alignSeverity > 0 && !have.Exists(f => f != null && f.id == FaultCatalog.AlignmentId))
            {
                var a = FaultCatalog.MakeById(FaultCatalog.AlignmentId, family);
                if (a != null)
                {
                    a.severity = o.alignSeverity;
                    a.pullDir = o.alignSide < 0 ? -1 : 1;
                    rows.Add(a);
                }
            }
            return rows;
        }
    }
}
