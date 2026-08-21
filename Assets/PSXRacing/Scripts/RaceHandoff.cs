namespace PSXRacing
{
    /// <summary>
    /// Static mailbox between the LifeSim menu scene and the race scene.
    /// Statics survive a scene load (they die on domain reload / app restart,
    /// which is fine: a race abandoned mid-way through an app kill just never
    /// happened, same as RG2's interim-save behavior).
    ///
    /// The LifeSim fills the request half before loading the race; RaceManager
    /// stamps the result half when the player finishes. Applying the result to
    /// LifeState (payout, odometer, wear, fuel, time-of-day) happens back in
    /// the menu scene, so all economy rules live in one place.
    /// </summary>
    public static class RaceHandoff
    {
        // ---- request: filled by the LifeSim before loading the race ----
        /// <summary>True when the race was entered from Home. When false the
        /// race scene behaves as the standalone demo it was before the
        /// LifeSim existed (direct scene play in the editor still works).</summary>
        public static bool FromLifeSim;
        /// <summary>Catalog id of the car being driven, for odometer/wear.</summary>
        public static string CarId;
        /// <summary>Purse for winning, from the LifeSim's race offer.</summary>
        public static int PurseWin;
        public static int PurseSecond;
        public static int PurseThird;
        /// <summary>0 morning / 1 afternoon / 2 night — drives race lighting.</summary>
        public static int TimeSlot;
        public static bool IsPractice;
        // Fault-effect handicaps the race scene applies to the player car
        // (ComputeFaultEffects). All neutral by default so the standalone
        // race is untouched.
        public static float AccelMult = 1f;
        public static float GripMult = 1f;
        public static float BrakeMult = 1f;
        public static float SteerPull;         // signed
        public static float ShiftMult = 1f;
        public static float FuelMult = 1f;
        public static bool HideGauges;
        public static bool RpmFlutter;

        // ---- result: stamped by RaceManager when the player finishes ----
        public static bool ResultReady;
        public static int FinishPos;
        public static int FieldSize;
        public static float RaceTimeSeconds;
        public static float BestLapSeconds;
        public static float MetersDriven;
        public static float DriftSeconds;

        public static void ClearResult()
        {
            ResultReady = false;
            FinishPos = 0;
            FieldSize = 0;
            RaceTimeSeconds = 0f;
            BestLapSeconds = 0f;
            MetersDriven = 0f;
            DriftSeconds = 0f;
        }

        public static void ClearAll()
        {
            FromLifeSim = false;
            CarId = null;
            PurseWin = PurseSecond = PurseThird = 0;
            TimeSlot = 0; IsPractice = false;
            AccelMult = GripMult = BrakeMult = ShiftMult = FuelMult = 1f;
            SteerPull = 0f; HideGauges = false; RpmFlutter = false;
            ClearResult();
        }
    }
}
