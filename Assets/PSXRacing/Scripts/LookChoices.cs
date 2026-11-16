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
    ///   C11, "G1" (<see cref="SunLift"/>; SHIPPED ON 2026-10-01 at 80%, the
    ///   owner's "D" - the figures below are the original half-cut's): the
    ///   film grade's matte lift - its
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
        /// <summary>C11's default: ON since 2026-10-01. The owner, on the
        /// washed-out daylight cars ("completely washed out and white... decent
        /// at Dusk and Night"), chose "D" off real player frames: the grade's
        /// matte lift cut by a NIGHT-SIZED share in clear or snowy daylight
        /// (PSX/Blit GRADE_SUN_LIFT_CUT 0.80, the same as GRADE_NIGHT_LIFT_CUT),
        /// not G1's original half.</summary>
        public const bool SunLiftDefault = true;
        /// <summary>C12's default: false = the signed-off sodium night.</summary>
        public const bool CoolNightDefault = false;

        public static bool SunLift => Env("PSX_G1") ?? SunLiftDefault;
        public static bool CoolNight => Env("PSX_COOLDARKS") ?? CoolNightDefault;

        /// <summary>NIGHT-LOOK PASS 5 (2026-10-28), the owner: "Light haze far
        /// out only", approved on condition of an A/B first. In the lit city at
        /// dusk and night only (never by day, never in Fog weather, which has
        /// its own), anything past <see cref="PSXGlobals.DistHazeStartM"/>
        /// fades a little toward the sky's own horizon colour (PSX/Lit's
        /// _PSXDistHaze, PSX/Halo's far lights dim with it): the depth his NFS
        /// frames have. Never near the car. Ships OFF until he picks it from
        /// the A/B (NightLookPass "haze" frames); PSX_DISTHAZE=1 in a tool.</summary>
        public const bool DistantHazeDefault = false;
        public static bool DistantHaze => Env("PSX_DISTHAZE") ?? DistantHazeDefault;

        /// <summary>THE SKYLINE PASS (2026-10-29), the owner's stadium-side
        /// aerial of uptown: "deep blue sky, cumulus and hard sun" against our
        /// pale, hazy, flat noon. A GLOBAL look change (the clear day's sky,
        /// haze, light and the film grade's hold on blue), so an A/B: OFF
        /// until the owner picks it from the sheet (Editor/SkylineMatchShots
        /// with PSX_SKYM_AB=1). PSX_CLEARDAY=1 in a tool; a tool may also force
        /// it per shot (<see cref="ClearDayForce"/>).</summary>
        /// <summary>THE BLUE DISTANCE (the stage horizon, 2026-11-09): past
        /// 2.5 km a ridge takes a little of the sky's blue with distance, about a
        /// tenth at 10 km (TimeOfDay.Apply, only in a scene that draws its land
        /// to the horizon). The owner's rule for any haze is an A/B first, so it
        /// ships OFF: clear air to the horizon. PSX_AERIAL=1 in a tool, or
        /// <see cref="AerialForce"/> per shot.</summary>
        public const bool AerialDepthDefault = false;
        public static bool? AerialForce;
        public static bool AerialDepth => AerialForce ?? Env("PSX_AERIAL") ?? AerialDepthDefault;

        /// <summary>THE FAR CANOPY (linncove, owner 2026-11-16: "It would be nice
        /// to find a solution to the barren mountains in the distance on
        /// mountain maps"): the land past the forest's reach as crowns in the
        /// dress's palette with the ridges' crown fringe (PSX/Lit _HorizonCrowns,
        /// PSXRacingBuilder.Horizon). PSX_FARCANOPY=0 = the land as it was
        /// (StageCulling sets the shader's _PSXFarCanopyOff); a tool may force
        /// it per shot (<see cref="FarCanopyForce"/>).</summary>
        public const bool FarCanopyDefault = true;
        public static bool? FarCanopyForce;
        public static bool FarCanopy => FarCanopyForce ?? Env("PSX_FARCANOPY") ?? FarCanopyDefault;

        /// <summary>THE LINN COVE GRANITE (linncove, owner 2026-11-16: "would
        /// also be nice to add some of the iconic rock face to the track and the
        /// title shot angle"): the boulder field under and above the viaduct,
        /// from the 2018 lidar (PSXRacingBuilder.LandmarkRocks).
        /// PSX_LINNCOVE_ROCKS=0 = without them (their root switched off at load,
        /// colliders with it).</summary>
        /// <summary>THE RIDGES' CROWN FRINGE (linncove 2026-11-16): upright cards
        /// of crowns along every crest past the forest's reach. OFF: from the
        /// title they read as dark combs laid along the ridges, not as treetops
        /// against the sky. PSX_RIDGEFRINGE=1 shows them (a scene built with
        /// PSX_RIDGEFRINGE=1 has them; StageCulling sets _PSXFringeOn).</summary>
        public const bool RidgeFringeDefault = false;
        public static bool RidgeFringe => Env("PSX_RIDGEFRINGE") ?? RidgeFringeDefault;

        public const bool LinnCoveRocksDefault = true;
        public static bool LinnCoveRocks => Env("PSX_LINNCOVE_ROCKS") ?? LinnCoveRocksDefault;

        public const bool ClearDayDefault = false;
        public static bool? ClearDayForce;
        public static bool ClearDay => ClearDayForce ?? Env("PSX_CLEARDAY") ?? ClearDayDefault;

        // (WarmWindows, NIGHT-LOOK PASS 5's never-shipped A/B - ONE window
        // colour as a warm white - is gone: the 1990s city, 2026-10-29, lights
        // offices in two families, incandescent gold and fluorescent white-green,
        // by building: PSX/Lit FAC_WIN_GOLD / FAC_WIN_TUBE.)

        static bool? Env(string name)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            if (v == "1") return true;
            if (v == "0") return false;
            return null;
        }
    }
}
