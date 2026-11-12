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
            ApplyOutcome(car, FromHandoff());
        }

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
        /// the car already carries is not written twice; an alignment it
        /// already carries is made worse in place when this hit was worse
        /// (the new band and side) and never doubled - one car, one alignment.
        /// </summary>
        public static void ApplyOutcome(OwnedCar car, CrashStopGap.Outcome o)
        {
            if (car == null) return;
            if (o.paintLoss > 0f) car.paint = Mathf.Max(0f, car.paint - o.paintLoss);
            if (o.bodyLoss > 0f) car.carHP = Mathf.Max(0f, car.carHP - o.bodyLoss);
            if (o.radiatorLoss > 0f) car.radiator = Mathf.Max(0f, car.radiator - o.radiatorLoss);
            if (o.alignSeverity > 0)
            {
                var old = car.faults.Find(f => f != null && f.id == FaultCatalog.AlignmentId);
                if (old != null && o.alignSeverity > old.severity)
                {
                    old.severity = o.alignSeverity;
                    old.pullDir = o.alignSide < 0 ? -1 : 1;
                }
            }
            car.faults.AddRange(NewRows(car, o));
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
