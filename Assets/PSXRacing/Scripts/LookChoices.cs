namespace PSXRacing
{
    /// <summary>
    /// THE OWNER'S TWO OPEN LOOK CHOICES (the colour pass, 2026-09-29).
    ///
    /// Both change a look he signed off, so both ship OFF - the picture he
    /// approved - and wait for his yes on the labelled A/B frames
    /// (tools\colour\colour-shots.ps1 -Sets look). Flipping a default below is
    /// the whole of shipping either one.
    ///
    ///   C11, "G1" (<see cref="SunLift"/>): the film grade's matte lift - its
    ///   "faded print" floor, (0.112, 0.108, 0.104), about Ycode 28 through
    ///   the 6-bit dither - fades by half in clear or snowy daylight (morning,
    ///   noon, afternoon; snow joined on review, 2026-09-29 - "noon,
    ///   especially with snow"), the way a lens's veiling glare would under a hard sun:
    ///   deeper blacks and about 6.5 stops at noon instead of 6.2, and the
    ///   owner's sunlit fresh asphalt DISPLAYS darker (about 53 to 40). Every
    ///   other hour, the night end and FILM GRADE OFF are bit for bit as they
    ///   are.
    ///
    ///   C12, "cool darks" (<see cref="CoolNight"/>): a city night's murk and
    ///   its shadows' split-tone lean blue-teal (his NFS references measure
    ///   b* -4.7 to +0.6 in the darks) instead of the sodium brown the 09-21
    ///   night pass chose (ours b* +3.5). The lamps stay yellow bulbs; only
    ///   the dark between them changes.
    ///
    /// PSX_G1=1 / PSX_COOLDARKS=1 (or =0) in a tool's environment override the
    /// defaults for that run; a build has neither and plays the defaults.
    /// </summary>
    public static class LookChoices
    {
        /// <summary>C11's default: false = G0, the grade as signed off.</summary>
        public const bool SunLiftDefault = false;
        /// <summary>C12's default: false = the signed-off sodium night.</summary>
        public const bool CoolNightDefault = false;

        public static bool SunLift => Env("PSX_G1") ?? SunLiftDefault;
        public static bool CoolNight => Env("PSX_COOLDARKS") ?? CoolNightDefault;

        static bool? Env(string name)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            if (v == "1") return true;
            if (v == "0") return false;
            return null;
        }
    }
}
