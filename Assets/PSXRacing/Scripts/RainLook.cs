using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE RAIN AND LOOK ROUND (rainlook, 2026-11-27; the owner's five faults from PC82). Each fix is one LAB row
    /// (LabSwitches) over one static here; the shaders read them as two globals that are 0 for the NEW look, so a
    /// shader whose global was never set draws the new look:
    ///   _PSXRainLookOff.x 1 = the old runs on car glass and paint (PSXCarDrops.cginc CD2Rivulets: a run's wiped
    ///                         track a whole column wide and hard-edged - the "sliding rectangles")
    ///   _PSXRainLookOff.y 1 = the old lamp glow through the wet windscreen (PSXCabinGlass: a per-channel knee turned
    ///                         yellow lane lines red-orange and bloomed them; the glow now only from lights, in their hue)
    ///   _PSXRainLookOff.z 1 = the old screen-space reads of the cabin (PSXWetSSR: the march's refinement slid onto a
    ///                         near pillar or mirror; PSXCabinGlass: a drop's bent picture read the pillars and mirror)
    ///   _PSXRainLookOff.w 1 = the old leaves on a stage road (PSXLit: laid in the road's UV, stretched where the
    ///                         road's V is not metres; now laid on the ground in world metres)
    ///   _PSXLookOff2.x    1 = the old foliage light at dusk and by day (PSXLit FOLIAGE LIGHT)
    /// Every switch is live: a flip writes the globals at once.
    /// </summary>
    public static class RainLook
    {
        static readonly int OffId = Shader.PropertyToID("_PSXRainLookOff");
        static readonly int Off2Id = Shader.PropertyToID("_PSXLookOff2");

        static bool Env(string n) => System.Environment.GetEnvironmentVariable(n) != "0";

        static bool softRuns = Env("PSX_RAINLOOK_RUNS");
        static bool trueGlow = Env("PSX_RAINLOOK_WIPERGLOW");
        static bool cabinMask = Env("PSX_RAINLOOK_CABIN");
        static bool roadLeaves = Env("PSX_RAINLOOK_LEAVES");
        static bool duskLeaves = Env("PSX_RAINLOOK_DUSK");

        /// <summary>Runs on car glass and paint: thin, soft, wandering; no dark wiped column.</summary>
        public static bool SoftRuns { get => softRuns; set { softRuns = value; Push(); } }
        /// <summary>The windscreen's lamp glow only from lights, in their own colour (no red smear).</summary>
        public static bool TrueGlow { get => trueGlow; set { trueGlow = value; Push(); } }
        /// <summary>The cabin never feeds the wet road's reflections nor the drops' bent picture.</summary>
        public static bool CabinMask { get => cabinMask; set { cabinMask = value; Push(); } }
        /// <summary>Leaves on a stage road laid in world metres (square, never stretched).</summary>
        public static bool RoadLeaves { get => roadLeaves; set { roadLeaves = value; Push(); } }
        /// <summary>Foliage light at dusk and by day falls with the sun (no blacklight glow).</summary>
        public static bool DuskLeaves { get => duskLeaves; set { duskLeaves = value; Push(); } }

        /// <summary>The cabin mask needs the scene's depth while the cockpit glass draws: the water's depth copy
        /// (SpeedBlurFeature) is made for this camera too.</summary>
        public static bool CabinDepthFor(Camera cam) =>
            cabinMask && cam != null && cam == Camera.main && Rain2.On && Rain2.Glass && CockpitInterior.GlassShowing;

        public static void Push()
        {
            Shader.SetGlobalVector(OffId, new Vector4(softRuns ? 0f : 1f, trueGlow ? 0f : 1f, cabinMask ? 0f : 1f, roadLeaves ? 0f : 1f));
            Shader.SetGlobalVector(Off2Id, new Vector4(duskLeaves ? 0f : 1f, 0f, 0f, 0f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Push();
    }
}
