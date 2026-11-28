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
    ///   _PSXLookOff2.w    1 = the old even leaf light (leaflight: PSXLit PSXLeafLight, the crown's sun and shade)
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
        static bool treeCalm = Env("PSX_RAINLOOK_CALM");
        static bool leafLight = Env("PSX_LEAFLIGHT");
        /// <summary>(round 4) TREE COLOUR: CALMER BY DAY's strength: the share of a leaf's brightness taken off by
        /// day and at dusk. 0 = the shader's own TREE_CALM_K (PSXLit.shader, 0.30). PSX_RAINLOOK_CALM_K: tools.</summary>
        public static float TreeCalmK = float.TryParse(System.Environment.GetEnvironmentVariable("PSX_RAINLOOK_CALM_K"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ck) ? ck : 0f;

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
        /// <summary>(round 4) The leaves' brightness eased by day and at dusk; hue, saturation and night kept.</summary>
        public static bool TreeCalm { get => treeCalm; set { treeCalm = value; Push(); } }
        /// <summary>(leaflight, 11-27) Each leaf lit by its place in its crown: a sun side and a shade side, a dark core
        /// and underside, light through back-lit leaves, a neighbour's shade; near and far trees alike. Night unchanged.
        /// PSX/Lit PSXLeafLight; _PSXLookOff2.w 1 = the old even foliage light.</summary>
        public static bool LeafLight { get => leafLight; set { leafLight = value; Push(); } }

        /// <summary>The cabin mask needs the scene's depth while the cockpit glass draws: the water's depth copy
        /// (SpeedBlurFeature) is made for this camera too.</summary>
        public static bool CabinDepthFor(Camera cam) =>
            cabinMask && cam != null && cam == Camera.main && Rain2.On && Rain2.Glass && CockpitInterior.GlassShowing;

        public static void Push()
        {
            Shader.SetGlobalVector(OffId, new Vector4(softRuns ? 0f : 1f, trueGlow ? 0f : 1f, cabinMask ? 0f : 1f, roadLeaves ? 0f : 1f));
            Shader.SetGlobalVector(Off2Id, new Vector4(duskLeaves ? 0f : 1f, treeCalm ? 0f : 1f, Mathf.Max(TreeCalmK, 0f), leafLight ? 0f : 1f));
#if !UNITY_EDITOR
            // the tools-only globals (PSX/Lit's id pass, the SSR's cabin split) are set only by Editor code, which a
            // player does not contain; held at 0 here as well, at boot and on every flip
            Shader.SetGlobalFloat(IdMaskId, 0f);
            Shader.SetGlobalFloat(CabinSplitId, 0f);
            Shader.SetGlobalFloat(LeafDiagId, 0f);
#endif
        }
#if !UNITY_EDITOR
        static readonly int IdMaskId = Shader.PropertyToID("_PSXIdMask");
        static readonly int CabinSplitId = Shader.PropertyToID("_PSXCabinSplit");
        static readonly int LeafDiagId = Shader.PropertyToID("_PSXLeafDiag");
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Push();
    }
}
