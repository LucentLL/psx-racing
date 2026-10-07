using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// WHAT THIS CAR CAN HOLD, read off the car itself (AI at the limit,
    /// 2026-10-07; owner: "the AI is very brake happy, timid, scared to take
    /// a corner ... they rarely push their cars to the limits").
    ///
    /// The AI used to plan every corner on mu = skill x weather, times 0.92:
    /// about 0.8 g for a car whose tyres hold 1.4-1.7. Here the grip is the
    /// car's OWN, read live every call - the surface it is on, the tyres
    /// front and rear, the tyre upgrades (gripBonus), a grip fault, the
    /// weather, and downforce at this speed - so a car whose geometry, weight
    /// or tyres change is driven on the grip it has now, not on a table baked
    /// for the car it used to be.
    ///
    /// The calibration was MEASURED (Editor/AIGripPlayCheck.cs,
    /// tools/aigrip-play-check.ps1), 2026-10-07, on a flat road strip with
    /// each car set up as an AI car (ABS, gripBonus 1.04, assist 0.5):
    ///   skidpad, steady circle, lateral g held (RX-7 FD / Civic EG / Viper
    ///   GTS; nominal mu 1.30 each):
    ///     R25  @ 68 km/h   1.39 / 1.37 / 1.37 g
    ///     R45  @ 96 km/h   1.57 / 1.56 / 1.56
    ///     R80  @ 130 km/h  1.66 / 1.69 / 1.63
    ///     R140 @ 131-150   1.66 / 1.85 / 1.64   (each ended at full lock,
    ///                                            running wide: the limit)
    ///   = nominal mu x (1 + downforce share) x 1.00-1.03 at 19 m/s, rising
    ///     to x 1.06-1.12 from 27 m/s (LateralCalAt);
    ///   tight circles (R8 / R12 / R17) bind on the TURN RATE, not the g:
    ///     ~0.93 rad/s at full lock whatever the radius (YawRateMax);
    ///   full ABS stop, six cars: 9.5-10.3 m/s2 from 119 to 43 km/h, 10.5-10.9
    ///     from 198 to 137 - the brakes' 0.9 g demand x 1.087 plus drag
    ///     4.8e-4 v^2 (BrakeDecel).
    /// </summary>
    public static class AIGrip
    {
        /// <summary>Steady skidpad g per unit of nominal mu x (1 + downforce
        /// share), at a crawl and from CalFullMps up (measured: see above).</summary>
        public const float LateralCalLow = 1.0f, LateralCalHigh = 1.09f;
        const float CalLowMps = 12f, CalFullMps = 27f;
        /// <summary>A full ABS stop's deceleration per g of brake demand, and
        /// the drag on top of it per (m/s)^2.</summary>
        public const float BrakeHwCal = 1.087f, BrakeDragPerV2 = 4.8e-4f;
        /// <summary>A stop the tyres limit (wet road, or brakes uprated past
        /// the tyres): per unit of nominal mu x (1 + downforce share).</summary>
        public const float BrakeTyreCal = 1.0f;

        public static float LateralCalAt(float mps) =>
            Mathf.Lerp(LateralCalLow, LateralCalHigh, Mathf.InverseLerp(CalLowMps, CalFullMps, mps));

        /// <summary>The friction the tyres have on today's road: the surface
        /// the car is on now (road, or off it: a verge, a deck floor that is
        /// not road), the weaker axle's tyre, upgrades, a grip fault and the
        /// weather - each exactly as CarController.TireForces takes it.</summary>
        public static float NominalMu(CarController c)
        {
            if (c == null) return 1f;
            // Each axle's tyres at the load they carry at rest (tyre load
            // sensitivity, 2026-10-07): a front-heavy car's fronts hold less
            // per kilo than a 50:50 car's. 1 on the FD the constants were
            // measured on.
            float wf = c.weightDistFront;
            float tyre = Mathf.Min(c.tireMuFront * CarController.StaticAxleMuFactor(wf),
                                   c.tireMuRear * CarController.StaticAxleMuFactor(1f - wf));
            float surface = c.onRoad ? c.roadGrip * Seasons.RoadGripMult : c.offroadGrip * Seasons.OffroadGripMult;
            return surface * tyre * c.gripBonus * c.faultGripMult;
        }

        /// <summary>Downforce per (m/s)^2 as a share of the car's weight (its
        /// own k v^2 law, CarController.DeriveDownforce).</summary>
        static float AeroPerV2(CarController c) =>
            c == null ? 0f : c.downforceCoefficient / Mathf.Max(100f, c.massKg * 9.81f);

        /// <summary>Downforce at <paramref name="mps"/> as a share of weight.</summary>
        public static float AeroFrac(CarController c, float mps) => AeroPerV2(c) * mps * mps;

        /// <summary>The steady cornering limit at this speed, in g (the
        /// tyres; see also YawRateMax, which binds first in a hairpin).</summary>
        public static float LateralG(CarController c, float mps) =>
            LateralCalAt(mps) * NominalMu(c) * (1f + AeroFrac(c, mps));

        /// <summary>...or the turn rate's (v x YawRateMax), whichever binds:
        /// the most lateral g the car can pull at this speed.</summary>
        public static float LateralGMax(CarController c, float mps) =>
            Mathf.Min(LateralG(c, mps), mps * YawRateMax / 9.81f);

        /// <summary>
        /// The fastest this car takes a bend of curvature <paramref name="k"/>
        /// (1/m) at <paramref name="share"/> of its limit: v^2 k = g G0 s (1 +
        /// A v^2), solved for v, twice (the calibration moves with speed) -
        /// and no faster than the car can TURN it (YawSpeed; on a hairpin,
        /// that binds first). <paramref name="kYaw"/>: the curvature the turn
        /// rate is judged on, when it is not k's own (a short corner).
        /// Downforce grows with v^2 too, so past the speed where the two cross
        /// the tyres' side is flat out.
        /// </summary>
        public static float CornerSpeed(CarController c, float k, float share, float kYaw = -1f)
        {
            float mu = NominalMu(c) * share * 9.81f;
            float a = AeroPerV2(c);
            k = Mathf.Max(k, 1e-5f);
            float v = 20f;
            for (int it = 0; it < 2; it++)
            {
                float g0 = mu * LateralCalAt(v);
                float den = k - g0 * a;
                if (den <= 1e-6f) { v = float.PositiveInfinity; break; }
                v = Mathf.Sqrt(g0 / den);
            }
            return Mathf.Min(v, YawSpeed(kYaw >= 0f ? kYaw : k, share));
        }

        /// <summary>
        /// THE YAW-RATE CEILING, measured: on tight circles every car ran wide
        /// at full lock at the same TURN RATE, not the same g - R8 at 27-28
        /// km/h (0.94-0.98 rad/s, 0.75-0.77 g), R12 at 40 km/h (0.93 rad/s,
        /// 1.0 g), R17 at 51-52 km/h (0.83-0.85 rad/s, 1.17 g), against 1.38 g
        /// at R25 (RX-7 FD, Viper GTS). CarController's yaw damping takes the
        /// rotation out faster than a tight bend asks for it. So a hairpin is
        /// taken at YawRateMax x radius, whatever the tyres would hold.
        /// </summary>
        public const float YawRateMax = 0.92f;

        /// <summary>The fastest a bend of curvature k can be turned by the
        /// car's turn rate (rad/s) at this share of it.</summary>
        public static float YawSpeed(float k, float share) =>
            k <= 1e-5f ? float.PositiveInfinity : YawRateMax * share / k;

        /// <summary>
        /// A full stop's deceleration at this speed (m/s^2): the brake
        /// hardware's demand or the tyres' circle, whichever is less (AI cars
        /// have ABS: CarController.Abs), plus the drag the car has then.
        /// </summary>
        public static float BrakeDecel(CarController c, float mps)
        {
            if (c == null) return 8.5f;
            float hardware = c.brakeDemandG * c.faultBrakeMult * BrakeHwCal;
            float tyres = BrakeTyreCal * NominalMu(c) * (1f + AeroFrac(c, mps));
            return 9.81f * Mathf.Min(hardware, tyres) + BrakeDragPerV2 * mps * mps;
        }
    }
}
