namespace PSXRacing.LifeSim
{
    /// <summary>
    /// What an insurer pays on one bill, and what is left for the player.
    /// </summary>
    public struct InsurancePay
    {
        public int insurerPaid, deductible, playerOwes;
        /// <summary>One line for the summary screen ("covered by your plan, less the $500 deductible").</summary>
        public string note;

        public static InsurancePay PlayerPaysAll(int bill) =>
            new InsurancePay { insurerPaid = 0, deductible = 0, playerOwes = bill < 0 ? 0 : bill, note = "" };
    }

    /// <summary>
    /// ROUND R1c's seam (2026-11-13) between the crash side (injury, hospital,
    /// write-off: R1c-A) and insurance (car policies, health plan: R1c-B). The
    /// crash side CALLS these; insurance SETS them. Null = no insurance in the
    /// game yet: the player pays the whole bill and a totaled car pays nothing.
    /// </summary>
    public static class CrashHooks
    {
        /// <summary>A hospital bill: (career, bill in dollars) -> who pays what.</summary>
        public static System.Func<LifeState, int, InsurancePay> HospitalPayer;

        /// <summary>A repair after a crash: (career, car, repair cost) -> what the policy pays.</summary>
        public static System.Func<LifeState, OwnedCar, int, InsurancePay> RepairClaim;

        /// <summary>A car written off: (career, car, its value) -> the payout.</summary>
        public static System.Func<LifeState, OwnedCar, int, InsurancePay> TotalLossClaim;

        public static InsurancePay Hospital(LifeState s, int bill) =>
            HospitalPayer != null ? HospitalPayer(s, bill) : InsurancePay.PlayerPaysAll(bill);

        public static InsurancePay Repair(LifeState s, OwnedCar car, int cost) =>
            RepairClaim != null ? RepairClaim(s, car, cost) : InsurancePay.PlayerPaysAll(cost);

        public static InsurancePay TotalLoss(LifeState s, OwnedCar car, int value) =>
            TotalLossClaim != null ? TotalLossClaim(s, car, value) : new InsurancePay { note = "" };
    }
}
