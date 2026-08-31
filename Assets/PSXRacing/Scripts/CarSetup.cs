using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// One car's ADVANCED TUNE, as stored in the save and carried into a race.
    ///
    /// Every field is a NORMALIZED OFFSET in [-1, +1], not a physical value.
    /// Zero is the car's own factory/derived number, whatever that happens to be
    /// on this particular car at this particular state of build. Three reasons,
    /// all load-bearing:
    ///
    ///   1. <see cref="CarController.ScaleChassisToMass"/> rewrites every spring,
    ///      damper and bar from MASS on every ApplySpec. If this stored 47100
    ///      N/m, then buying a WEIGHT stage would silently change what the player
    ///      had chosen — they would come back from the parts shop to a setup they
    ///      never made. "+0.35 of the stiffer half of this car's range" survives
    ///      a weight build, a power build, and a re-spec.
    ///   2. It makes CarSetupRanges the single place that decides what a number
    ///      means, which is the same fence CarTune draws between the shop screen
    ///      and the stopwatch.
    ///   3. Zero is also the C# default, so a save written before this feature
    ///      existed deserializes to a factory setup with NO migration step. That
    ///      is deliberate rather than lucky: the "-1 means unset" sentinel has
    ///      already been hit twice in this save format and cost a migration both
    ///      times. A design whose unset value IS the default cannot hit it.
    ///
    /// FIELD NAMES ARE SAVE-FORMAT API. Adding one is free; renaming one silently
    /// resets it to zero with no error anywhere. Do not rename these.
    /// </summary>
    [System.Serializable]
    public class CarSetup
    {
        // --- tires + brakes ---
        public float tyrePressureFront, tyrePressureRear;
        public float brakePressure, brakeBalance;

        // --- alignment ---
        public float camberFront, camberRear;
        public float toeFront, toeRear;
        public float steerLock, steerRate, selfCentre;

        // --- springs + dampers ---
        public float springFront, springRear;
        public float damperFront, damperRear;
        public float rideHeight;
        public float arbFront, arbRear;

        // --- differential ---
        public float diffAccel, diffDecel, diffPreload, driveSplit;

        // --- gearing ---
        /// <summary>Per-gear trim, one entry per gear, in the same order as
        /// <see cref="CarController.gearRatios"/>. Paired with
        /// <see cref="gearCount"/> rather than trusting Length, for the same
        /// reason RaceHandoff keeps OrderBoxes beside OrderToppings: an array
        /// that arrives null (an old save) or stale (the car was re-spec'd to a
        /// different gearbox) must be detectable, not silently mis-indexed.
        /// </summary>
        public float[] gear;
        public int gearCount;
        public float finalDriveScale;

        // --- aero ---
        public float aeroLevel, aeroBalance;

        /// <summary>Largest gearbox <see cref="CarSpec.BuildGearRatios"/> will
        /// build, so the largest this array ever needs to be.</summary>
        public const int MaxGears = 8;

        /// <summary>
        /// Make sure <see cref="gear"/> can be indexed 0..n-1. Called at every
        /// read and write site rather than once at load, because the gear count
        /// is a property of the CAR and a save can outlive a catalog rebake.
        /// Existing entries are preserved on a grow; a shrink keeps the array
        /// and just moves gearCount, so putting a 5-speed's box back on the car
        /// restores the trims the player had set.
        /// </summary>
        public void EnsureGears(int n)
        {
            n = Mathf.Clamp(n, 0, MaxGears);
            if (gear == null) gear = new float[MaxGears];
            else if (gear.Length < MaxGears)
            {
                var grown = new float[MaxGears];
                System.Array.Copy(gear, grown, gear.Length);
                gear = grown;
            }
            gearCount = n;
        }

        public CarSetup Clone()
        {
            var c = (CarSetup)MemberwiseClone();
            c.gear = gear == null ? null : (float[])gear.Clone();
            return c;
        }

        /// <summary>True when every parameter sits at its factory value, so the
        /// car drives exactly as it would with no setup at all.</summary>
        public bool IsFactory
        {
            get
            {
                // Reads gear[] defensively rather than through Get, which would
                // call EnsureGears — a property that allocates as a side effect
                // of being asked a question is a trap, and this one is asked on
                // the race-load path.
                for (int i = 0; i < CarSetupTable.Count; i++)
                {
                    var p = (SetupParam)i;
                    int g = CarSetupTable.GearIndex(p);
                    float v = g >= 0
                        ? (gear != null && g < gear.Length ? gear[g] : 0f)
                        : Get(p);
                    if (v > 1e-4f || v < -1e-4f) return false;
                }
                return true;
            }
        }

        // ---- uniform access -------------------------------------------------
        // Every consumer — the UI, the gate, the physics, the self-test — goes
        // through these rather than touching fields, so adding a parameter is
        // one enum entry and two switch arms and nothing else can forget it.

        public float Get(SetupParam p)
        {
            switch (p)
            {
                case SetupParam.TyrePressureFront: return tyrePressureFront;
                case SetupParam.TyrePressureRear: return tyrePressureRear;
                case SetupParam.BrakePressure: return brakePressure;
                case SetupParam.BrakeBalance: return brakeBalance;
                case SetupParam.CamberFront: return camberFront;
                case SetupParam.CamberRear: return camberRear;
                case SetupParam.ToeFront: return toeFront;
                case SetupParam.ToeRear: return toeRear;
                case SetupParam.SteerLock: return steerLock;
                case SetupParam.SteerRate: return steerRate;
                case SetupParam.SelfCentre: return selfCentre;
                case SetupParam.SpringFront: return springFront;
                case SetupParam.SpringRear: return springRear;
                case SetupParam.DamperFront: return damperFront;
                case SetupParam.DamperRear: return damperRear;
                case SetupParam.RideHeight: return rideHeight;
                case SetupParam.ArbFront: return arbFront;
                case SetupParam.ArbRear: return arbRear;
                case SetupParam.DiffAccel: return diffAccel;
                case SetupParam.DiffDecel: return diffDecel;
                case SetupParam.DiffPreload: return diffPreload;
                case SetupParam.DriveSplit: return driveSplit;
                case SetupParam.FinalDrive: return finalDriveScale;
                case SetupParam.AeroLevel: return aeroLevel;
                case SetupParam.AeroBalance: return aeroBalance;
                case SetupParam.Gear1: case SetupParam.Gear2:
                case SetupParam.Gear3: case SetupParam.Gear4:
                case SetupParam.Gear5: case SetupParam.Gear6:
                case SetupParam.Gear7: case SetupParam.Gear8:
                {
                    int g = CarSetupTable.GearIndex(p);
                    return gear != null && g < gear.Length ? gear[g] : 0f;
                }
                // Loud rather than silent. The enum is append-only, and a new
                // parameter that fell through to a quiet `return 0f` would be
                // adjustable on screen, save nothing and change nothing — three
                // symptoms with no error between them.
                default:
                    Debug.LogError("CarSetup.Get: unhandled parameter " + p);
                    return 0f;
            }
        }

        public void Set(SetupParam p, float t)
        {
            t = Mathf.Clamp(t, -1f, 1f);
            switch (p)
            {
                case SetupParam.TyrePressureFront: tyrePressureFront = t; break;
                case SetupParam.TyrePressureRear: tyrePressureRear = t; break;
                case SetupParam.BrakePressure: brakePressure = t; break;
                case SetupParam.BrakeBalance: brakeBalance = t; break;
                case SetupParam.CamberFront: camberFront = t; break;
                case SetupParam.CamberRear: camberRear = t; break;
                case SetupParam.ToeFront: toeFront = t; break;
                case SetupParam.ToeRear: toeRear = t; break;
                case SetupParam.SteerLock: steerLock = t; break;
                case SetupParam.SteerRate: steerRate = t; break;
                case SetupParam.SelfCentre: selfCentre = t; break;
                case SetupParam.SpringFront: springFront = t; break;
                case SetupParam.SpringRear: springRear = t; break;
                case SetupParam.DamperFront: damperFront = t; break;
                case SetupParam.DamperRear: damperRear = t; break;
                case SetupParam.RideHeight: rideHeight = t; break;
                case SetupParam.ArbFront: arbFront = t; break;
                case SetupParam.ArbRear: arbRear = t; break;
                case SetupParam.DiffAccel: diffAccel = t; break;
                case SetupParam.DiffDecel: diffDecel = t; break;
                case SetupParam.DiffPreload: diffPreload = t; break;
                case SetupParam.DriveSplit: driveSplit = t; break;
                case SetupParam.FinalDrive: finalDriveScale = t; break;
                case SetupParam.AeroLevel: aeroLevel = t; break;
                case SetupParam.AeroBalance: aeroBalance = t; break;
                case SetupParam.Gear1: case SetupParam.Gear2:
                case SetupParam.Gear3: case SetupParam.Gear4:
                case SetupParam.Gear5: case SetupParam.Gear6:
                case SetupParam.Gear7: case SetupParam.Gear8:
                {
                    int g = CarSetupTable.GearIndex(p);
                    // Grows the ARRAY but never gearCount. gearCount is how many
                    // gears the CAR has, and writing a trim into a slot is not a
                    // claim about the gearbox — Sanitize zeroes all eight on a
                    // five-speed, and a five-speed must not come out of it
                    // saying it has eight.
                    if (gear == null || gear.Length < MaxGears)
                    {
                        var grown = new float[MaxGears];
                        if (gear != null) System.Array.Copy(gear, grown, gear.Length);
                        gear = grown;
                    }
                    gear[g] = t;
                    break;
                }
                default:
                    Debug.LogError("CarSetup.Set: unhandled parameter " + p);
                    break;
            }
        }
    }

    /// <summary>Every adjustable parameter, once. The order is the order the
    /// pages show them in. Append-only: the ordinal is not persisted (the save
    /// holds named fields) but it IS the index into the UI's page tables.
    /// </summary>
    public enum SetupParam
    {
        // TIRES AND BRAKES
        TyrePressureFront = 0, TyrePressureRear, BrakePressure, BrakeBalance,
        // ALIGNMENT
        SteerLock, SteerRate, SelfCentre, CamberFront, CamberRear, ToeFront, ToeRear,
        // SPRINGS AND DAMPERS
        SpringFront, SpringRear, DamperFront, DamperRear, ArbFront, ArbRear, RideHeight,
        // DIFFERENTIAL
        DiffAccel, DiffDecel, DiffPreload, DriveSplit,
        // GEARING
        FinalDrive, Gear1, Gear2, Gear3, Gear4, Gear5, Gear6, Gear7, Gear8,
        // AERO
        AeroLevel, AeroBalance,
    }

    /// <summary>Which of the six reference screens a parameter lives on.</summary>
    public enum SetupPage { TiresBrakes = 0, Alignment, Springs, Differential, Gearing, Aero }

    /// <summary>
    /// One parameter's usable span on ONE car.
    ///
    /// <see cref="def"/> sits at t = 0 and the two halves are mapped
    /// independently, so a range that is asymmetric about its default — brake
    /// pressure, which may only ever come DOWN — needs no special case anywhere
    /// else.
    /// </summary>
    public struct CarSetupRange
    {
        public float min, def, max;
        /// <summary>Multiplier from the PHYSICAL value to the displayed one:
        /// 0.001 to show a spring rate in N/mm, 100 to show a fraction as a
        /// percentage.</summary>
        public float display;
        public int decimals;
        public string unit;
        /// <summary>Set when the range collapsed — a 3-speed car has no 6th gear
        /// and a 2WD car has no centre differential. The row still draws, so row
        /// positions stay put across cars the way the reference screens do; it
        /// just draws locked.</summary>
        public bool absent;

        public float Value(float t) => t < 0f
            ? Mathf.Lerp(def, min, Mathf.Min(1f, -t))
            : Mathf.Lerp(def, max, Mathf.Min(1f, t));

        public string Text(float t) =>
            (Value(t) * display).ToString("F" + decimals) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);

        /// <summary>Where t sits across the whole span, 0..1, for drawing the
        /// slider fill. NOT the same as (t+1)/2 when the range is asymmetric.
        /// </summary>
        public float Fill01(float t)
        {
            float span = max - min;
            return span > 1e-6f ? Mathf.Clamp01((Value(t) - min) / span) : 0.5f;
        }
    }

    /// <summary>
    /// Static facts about each parameter: what it is called, where it lives, how
    /// it is written down. Kept apart from the RANGES because these never vary
    /// by car and the ranges always do.
    /// </summary>
    public static class CarSetupTable
    {
        public static readonly int Count = System.Enum.GetValues(typeof(SetupParam)).Length;

        /// <summary>The step one press of &lt; or &gt; moves. 21 stops across
        /// the full span, which is what puts the discrete notches on the
        /// reference screens' sliders and keeps displayed numbers clean.
        /// </summary>
        public const float Step = 0.1f;

        public static readonly string[] PageNames =
            { "TIRES", "ALIGN", "SPRINGS", "DIFF", "GEARS", "AERO" };
        public static readonly string[] PageTitles =
        {
            "TIRES AND BRAKES", "ALIGNMENT", "SPRINGS AND DAMPERS",
            "DIFFERENTIAL", "GEARING", "AERO",
        };

        /// <summary>Gear index for a Gear1..Gear8 parameter, else -1.</summary>
        public static int GearIndex(SetupParam p) =>
            p >= SetupParam.Gear1 && p <= SetupParam.Gear8 ? p - SetupParam.Gear1 : -1;

        public static SetupParam GearParam(int i) => SetupParam.Gear1 + i;

        static readonly string[] GearNames =
            { "1ST GEAR", "2ND GEAR", "3RD GEAR", "4TH GEAR",
              "5TH GEAR", "6TH GEAR", "7TH GEAR", "8TH GEAR" };

        public static string Label(SetupParam p)
        {
            int g = GearIndex(p);
            if (g >= 0) return GearNames[g];
            switch (p)
            {
                case SetupParam.TyrePressureFront: return "TIRE PRESSURE FRONT";
                case SetupParam.TyrePressureRear: return "TIRE PRESSURE REAR";
                case SetupParam.BrakePressure: return "BRAKE PRESSURE";
                case SetupParam.BrakeBalance: return "BRAKE BALANCE";
                case SetupParam.SteerLock: return "STEERING LOCK";
                case SetupParam.SteerRate: return "STEERING RATE";
                case SetupParam.SelfCentre: return "SELF-CENTRING";
                case SetupParam.CamberFront: return "CAMBER FRONT";
                case SetupParam.CamberRear: return "CAMBER REAR";
                case SetupParam.ToeFront: return "TOE FRONT";
                case SetupParam.ToeRear: return "TOE REAR";
                case SetupParam.SpringFront: return "SPRING RATE FRONT";
                case SetupParam.SpringRear: return "SPRING RATE REAR";
                case SetupParam.DamperFront: return "DAMPER FRONT";
                case SetupParam.DamperRear: return "DAMPER REAR";
                case SetupParam.ArbFront: return "SWAY BAR FRONT";
                case SetupParam.ArbRear: return "SWAY BAR REAR";
                case SetupParam.RideHeight: return "RIDE HEIGHT";
                case SetupParam.DiffAccel: return "ACCEL LOCK";
                case SetupParam.DiffDecel: return "DECEL LOCK";
                case SetupParam.DiffPreload: return "PRELOAD";
                case SetupParam.DriveSplit: return "CENTRE SPLIT";
                case SetupParam.FinalDrive: return "FINAL DRIVE";
                case SetupParam.AeroLevel: return "DOWNFORCE";
                default: return "AERO BALANCE";
            }
        }

        /// <summary>The two ends of the slider, as the reference screens caption
        /// them. Left is what turning it DOWN buys.</summary>
        public static void EndLabels(SetupParam p, out string low, out string high)
        {
            if (GearIndex(p) >= 0 || p == SetupParam.FinalDrive)
            { low = "Speed"; high = "Acceleration"; return; }
            switch (p)
            {
                case SetupParam.ToeFront:
                case SetupParam.ToeRear: low = "In"; high = "Out"; break;
                case SetupParam.BrakeBalance:
                case SetupParam.DriveSplit:
                case SetupParam.AeroBalance: low = "Rear"; high = "Front"; break;
                case SetupParam.SpringFront:
                case SetupParam.SpringRear:
                case SetupParam.ArbFront:
                case SetupParam.ArbRear: low = "Soft"; high = "Stiff"; break;
                case SetupParam.DamperFront:
                case SetupParam.DamperRear: low = "Soft"; high = "Firm"; break;
                case SetupParam.DiffPreload: low = "None"; high = "Heavy"; break;
                case SetupParam.BrakePressure: low = "Gentle"; high = "Full"; break;
                case SetupParam.SelfCentre: low = "Slack"; high = "Snappy"; break;
                case SetupParam.CamberFront:
                case SetupParam.CamberRear: low = "Negative"; high = "Positive"; break;
                case SetupParam.AeroLevel: low = "Slippery"; high = "Planted"; break;
                case SetupParam.TyrePressureFront:
                case SetupParam.TyrePressureRear: low = "Soft"; high = "Hard"; break;
                case SetupParam.SteerLock:
                case SetupParam.SteerRate: low = "Slower"; high = "Faster"; break;
                case SetupParam.RideHeight: low = "Low"; high = "High"; break;
                case SetupParam.DiffAccel:
                case SetupParam.DiffDecel: low = "Open"; high = "Locked"; break;
                default: low = "Low"; high = "High"; break;
            }
        }

        public static SetupPage PageOf(SetupParam p)
        {
            if (GearIndex(p) >= 0 || p == SetupParam.FinalDrive) return SetupPage.Gearing;
            if (p <= SetupParam.BrakeBalance) return SetupPage.TiresBrakes;
            if (p <= SetupParam.ToeRear) return SetupPage.Alignment;
            if (p <= SetupParam.RideHeight) return SetupPage.Springs;
            if (p <= SetupParam.DriveSplit) return SetupPage.Differential;
            return SetupPage.Aero;
        }

        /// <summary>The parameters on one page, in row order. Built once.
        /// GEARING deliberately lists FINAL DRIVE first, as the reference
        /// screen does — it is the one most people reach for.</summary>
        public static SetupParam[] Page(SetupPage page)
        {
            if (pages == null)
            {
                pages = new SetupParam[6][];
                for (int pg = 0; pg < 6; pg++)
                {
                    var list = new System.Collections.Generic.List<SetupParam>();
                    for (int i = 0; i < Count; i++)
                        if (PageOf((SetupParam)i) == (SetupPage)pg) list.Add((SetupParam)i);
                    pages[pg] = list.ToArray();
                }
            }
            return pages[(int)page];
        }
        static SetupParam[][] pages;

        /// <summary>One line of help, shown under the rows the way the reference
        /// screens do.</summary>
        public static string Help(SetupParam p)
        {
            if (GearIndex(p) >= 0)
                return "A shorter ratio accelerates harder and runs out of revs sooner.";
            switch (p)
            {
                case SetupParam.TyrePressureFront:
                case SetupParam.TyrePressureRear:
                    return "Grip peaks at the recommended pressure. Higher is sharper and holds less.";
                case SetupParam.BrakePressure:
                    return "Less line pressure means less lockup, and a longer stop.";
                case SetupParam.BrakeBalance:
                    return "Front bias resists spinning under braking. Rear bias rotates the car in.";
                case SetupParam.SteerLock:
                    return "How far the wheels turn. More than the tires can use is just dead travel.";
                case SetupParam.SteerRate:
                    return "How fast the rack answers the wheel.";
                case SetupParam.SelfCentre:
                    return "How quickly the wheel unwinds when you let go.";
                case SetupParam.CamberFront:
                case SetupParam.CamberRear:
                    return "Negative camber trades straight-line grip for cornering grip.";
                case SetupParam.ToeFront:
                    return "Toe out sharpens turn-in. Toe in settles the car on a straight.";
                case SetupParam.ToeRear:
                    return "Toe in steadies the rear. Toe out lets it rotate.";
                case SetupParam.SpringFront:
                case SetupParam.SpringRear:
                    return "Stiffer springs react faster and ride worse. Stiffen one end to load the other.";
                case SetupParam.DamperFront:
                case SetupParam.DamperRear:
                    return "Dampers control how fast weight moves, not how much.";
                case SetupParam.ArbFront:
                case SetupParam.ArbRear:
                    return "A stiffer bar at one end takes grip from that end. Front stiff understeers.";
                case SetupParam.RideHeight:
                    return "Lower drops the centre of gravity and the suspension travel with it.";
                case SetupParam.DiffAccel:
                    return "How hard the diff locks under power. More lock puts the torque on the loaded wheel.";
                case SetupParam.DiffDecel:
                    return "How hard the diff locks off the throttle. More lock steadies the car into a corner.";
                case SetupParam.DiffPreload:
                    return "Standing clamp in the plate pack. Felt at low torque, irrelevant at high.";
                case SetupParam.DriveSplit:
                    return "How much torque goes forward. Rearward is livelier, forward is safer.";
                case SetupParam.FinalDrive:
                    return "The whole gearbox at once. Short accelerates; long runs on.";
                case SetupParam.AeroLevel:
                    return "Downforce at speed, and the drag-free grip that comes with it.";
                default:
                    return "Where the downforce acts. Front bias turns in; rear bias stabilises.";
            }
        }
    }
}
