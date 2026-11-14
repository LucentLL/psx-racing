using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// DAMAGE in the ARCADE (owner, 2026-11-14: "yes" to per-race damage in
    /// arcade / quick race, reusing the career's damage pieces, nothing saved,
    /// the car fresh for the next race, and an option OFF / VISUAL ONLY / FULL,
    /// default FULL). OPTIONS > GAMEPLAY > DAMAGE, shown in an arcade session.
    ///
    ///   FULL         the career's located damage on this race's car: the
    ///                DamageModel ledger, the DamageLook (body, glass, lamps,
    ///                steam, bags), the steering pull, the power loss and the
    ///                radiator, and a structural FAIL ends the player's race DNF.
    ///   VISUAL ONLY  the same ledger read for its LOOK alone: dented panels and
    ///                cracked glass. No lamps out, steam or bags, no pull, no
    ///                power loss, no radiator, no FAIL.
    ///   OFF          as before this: nothing.
    ///
    /// Never a career: the ledger does not bank (nothing is written to a save,
    /// no injury), it lives on this race's car, and every race - a RESTART
    /// too, which reloads the scene - builds a new car and a new ledger.
    /// PSX_ARCADE_DAMAGE=0 = OFF whatever the option says (the base).
    /// </summary>
    public static class ArcadeDamage
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_ARCADE_DAMAGE") != "0";

        public const int Off = 0, Visual = 1, Full = 2;
        public static readonly string[] Names = { "OFF", "VISUAL ONLY", "FULL" };
        const string Key = "psx.arcadeDamage";
        static int cached = -1;

        /// <summary>The option, saved like every settings row (PlayerPrefs).</summary>
        public static int Mode
        {
            get
            {
                if (cached < 0) cached = Mathf.Clamp(PlayerPrefs.GetInt(Key, Full), Off, Full);
                return cached;
            }
            set
            {
                cached = Mathf.Clamp(value, Off, Full);
                PlayerPrefs.SetInt(Key, cached);
            }
        }

        public static string Label => Names[Mode];
        public static void Cycle() => Mode = (Mode + 1) % Names.Length;
        public static void ForgetCache() => cached = -1;

        /// <summary>The mode this drive was armed with (-1: not an arcade drive).</summary>
        public static int Armed { get; private set; } = -1;

        /// <summary>A FULL arcade race's FAIL ended the player's race. Read by the
        /// HUD's banner and taken once by the front end's RESULT.</summary>
        public static bool Dnf;

        /// <summary>RaceManager.RetirePlayer: in an arcade race with FULL damage
        /// the DNF ends the player's race (no tow, no career).</summary>
        public static bool DnfEndsRace => On && ArcadeMode.Active && Armed == Full;

        /// <summary>The RESULT page: was the last race a DNF? Cleared by reading.</summary>
        public static bool TakeDnf()
        {
            bool d = Dnf;
            Dnf = false;
            return d;
        }

        /// <summary>
        /// CrashStopGap.Arm's arcade branch: a non-banking ledger on the
        /// player's car, set for the option. False (nothing armed, today's
        /// arcade) when the switch is off, the option is OFF, this is not an
        /// arcade session, or the located damage model is off.
        /// </summary>
        public static bool Arm(CarController car)
        {
            Armed = -1;
            Dnf = false;
            if (!On || car == null || !ArcadeMode.Active) return false;
            int mode = Mode;
            if (mode == Off || !DamageModel.Live) return false;
            var resp = car.GetComponent<CollisionResponder>();
            if (resp == null) return false;
            var L = new DamageLedger(car, resp, false, System.Guid.NewGuid().GetHashCode())
            {
                bumper = CrashStopGap.LiveBumper(),
                LiveFx = mode == Full,
            };
            resp.Damage = L;
            resp.Ledger = null;
            car.hitSteerPull = 0f;
            DamageLook.Attach(car, L, mode == Visual);
            Armed = mode;
            Debug.Log("[ArcadeDamage] armed " + Names[mode] + " on " + car.name);
            return true;
        }
    }
}
