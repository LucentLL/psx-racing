using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>When a LAB row's flip shows: on the next frame, on the next level load, or never at run time
    /// (baked into the scenes or the city tiles by the build).</summary>
    public enum LabWhen { Now, NextLoad, BuiltIn }

    /// <summary>The LAB page's tabs, in strip order.</summary>
    public enum LabGroup { Rain, Lights, World, Cars, Traffic, Tapes }

    /// <summary>
    /// One DECISION the owner has to make (not one environment variable): a plain name, one line saying what to
    /// look at, and the statics it writes. NEW = every member at its <see cref="newValues"/> value (true unless
    /// said), OLD = the opposite. The game's own code keeps reading its own static; the lab only writes it.
    /// </summary>
    public sealed class LabRow
    {
        public string id;
        public LabGroup group;
        public string name;
        /// <summary>What to look at, one short line (the page's help line).</summary>
        public string look;
        public LabWhen when;
        /// <summary>The variables: one set in the environment holds the row (the test tools depend on it).</summary>
        public string[] env = new string[0];
        /// <summary>"Type.Member" (namespace PSXRacing or PSXRacing.City may be left off): static bool fields or
        /// writable properties. A member that does not exist hides the row (a switch from an unmerged branch).</summary>
        public string[] members = new string[0];
        /// <summary>Per member, its value for NEW (null: true for all).</summary>
        public bool[] newValues;
        /// <summary>The default state, true = NEW (or ON). Null: NEW.</summary>
        public Func<bool> def;
        /// <summary>ON / OFF wording (a player option such as the tunnel lamps) instead of NEW / OLD.</summary>
        public bool option;
        /// <summary>Hidden off the PC profile (the feature is a PC one).</summary>
        public bool pcOnly;
        /// <summary>For a BUILT IN row with no members: a member that must exist for the row to show.</summary>
        public string requires;
        /// <summary>Run after the members are written (a shader global push, a material re-mark...).</summary>
        public Action apply;

        // ---- resolved at boot --------------------------------------------
        internal MemberInfo[] resolved;
        internal bool present;
        internal bool envHeld;
        /// <summary>What the page shows and what is saved: live for a NOW row, pending for a NEXT LOAD row.</summary>
        internal bool chosen;

        public bool Default => def == null || def();
        public bool Chosen => chosen;
        public bool EnvHeld => envHeld;
        public bool Present => present;
        public bool Changed => present && when != LabWhen.BuiltIn && chosen != Default;
        public string StateWord(bool v) => option ? (v ? "ON" : "OFF") : (v ? "NEW" : "OLD");
        public bool NewValue(int i) => newValues == null || i >= newValues.Length || newValues[i];
    }

    /// <summary>
    /// THE LAB (owner, 2026-11-23, PC64, over the build folder's eight PSXRacing_*.cmd files: "I really don't
    /// like all these being individual builds that I have to load up separately to test each. Why can't they be
    /// toggles in the main build and I can approve to keep as option or make default?").
    ///
    /// Every rendering / behaviour switch that was a launcher is a row here (<see cref="Register"/>), flipped
    /// in the settings menu's LAB page (<see cref="LabCatalog"/>), live where that is honest, remembered in
    /// PlayerPrefs (psx.lab.&lt;id&gt;).
    ///
    /// PRECEDENCE: a set environment variable wins (the tools set them), then the saved choice, then the default.
    /// TEST RUNS (a perf tour, -batchmode, the editor, -titleshots, PSX_LAB_IGNORE=1) neither read nor write the
    /// saved choices: a Windows player's PlayerPrefs are the owner's, and every measurement would silently follow
    /// his toggles. COST: nothing per frame - the switches stay plain statics read by their own code; this class
    /// runs at boot, on a flip and at a level load.
    ///
    /// RETIRING A ROW (his verdict per row):
    ///  - MAKE IT THE DEFAULT and delete the old path: delete the row's Register line, then the old branch of the
    ///    code and the switch (keep the environment variable only if a tool still sets it).
    ///  - KEEP IT AS AN OPTION: move the row to a normal settings tab as a Step row (SettingsCatalog) over a
    ///    *Prefs class, and delete its Register line here (its saved key can be read once as the option's first
    ///    value: <see cref="SavedChoice"/>).
    ///  - DROP THE NEW PATH: set the row's default to OLD for one build if wanted, then delete the row and the new
    ///    branch of the code.
    /// A retired row's saved key is simply never read again.
    /// </summary>
    public static class LabSwitches
    {
        /// <summary>The saved keys' prefix (the self-test uses its own).</summary>
        public static string KeyPrefix = "psx.lab.";
        static readonly List<LabRow> all = new List<LabRow>();
        static bool booted;

        /// <summary>A test run: saved choices are neither read nor written.</summary>
        public static bool TestRun { get; private set; }

        /// <summary>"LAB: NIGHT 3 old, FAR HAZE old" while any row is off its default; null when none.</summary>
        public static string ChangedLine { get; private set; }
        /// <summary>Bumped on every change (the pause screen re-reads the line by it).</summary>
        public static int Version { get; private set; }

        // =====================================================================
        //  THE ROWS. One Register call per decision; a row is retired by deleting its line.
        // =====================================================================
        static void RegisterAll()
        {
            all.Clear();
            // ---- RAIN -----------------------------------------------------------
            Register(new LabRow
            {
                id = "rain2", group = LabGroup.Rain, name = "RAIN 2", when = LabWhen.NextLoad, pcOnly = true,
                look = "Pools, rings and splashes on the road, wipers, tyre spray, the rain's sound.",
                env = new[] { "PSX_RAIN2" }, members = new[] { "Rain2.On" },
                def = () => PlatformProfile.PC,
            });
            Register(new LabRow
            {
                id = "rainglass1", group = LabGroup.Rain, name = "RAIN ON GLASS: ROUND 1", when = LabWhen.Now, pcOnly = true,
                look = "Cockpit: the beads and the wipe. Chase: rain on your car, finer streaks in the air.",
                env = new[] { "PSX_RAIN2_STREAKS", "PSX_RAIN2_BEADS2", "PSX_RAIN2_WIPERMAP", "PSX_RAIN2_CARGLASS" },
                members = new[] { "Rain2.Streaks", "Rain2.Beads2", "Rain2.WiperMap", "Rain2.CarGlass" },
                apply = () => Rain2.PushGlass2(),
            });
            Register(new LabRow
            {
                id = "rainglass2", group = LabGroup.Rain, name = "RAIN ON GLASS: ROUND 2", when = LabWhen.Now, pcOnly = true,
                look = "Cockpit: water pushed by the wiper, a covered screen parked. Chase: smaller lens drops.",
                env = new[] { "PSX_RAIN2_BANK2", "PSX_RAIN2_COVER", "PSX_RAIN2_CLEAR", "PSX_RAIN2_LENSDROPS", "PSX_RAIN2_CARBEADS" },
                members = new[] { "Rain2.Bank2", "Rain2.Cover", "Rain2.Clear", "Rain2.LensDrops", "Rain2.CarBeads" },
                apply = () => Rain2.PushGlass2(),
            });
            // rainlook (11-27): the owner's PC82 rain faults (RainLook.cs says what each one changes)
            Register(new LabRow
            {
                id = "softruns", group = LabGroup.Rain, name = "RAIN RUNS ON CAR GLASS", when = LabWhen.Now,
                look = "Chase view in rain: water runs down the glass as thin soft trails, no sliding dark rectangles. Beads unchanged.",
                env = new[] { "PSX_RAINLOOK_RUNS" }, members = new[] { "RainLook.SoftRuns" },
            });
            Register(new LabRow
            {
                id = "wiperglow", group = LabGroup.Rain, name = "WIPER GLOW: TRUE COLOURS", when = LabWhen.Now, pcOnly = true,
                look = "Cockpit in rain at night: no red smear on lane lines, blades or windows; lamps glow in their own colour.",
                env = new[] { "PSX_RAINLOOK_WIPERGLOW" }, members = new[] { "RainLook.TrueGlow" },
            });
            Register(new LabRow
            {
                id = "cabinmask", group = LabGroup.Rain, name = "CABIN OUT OF REFLECTIONS", when = LabWhen.Now, pcOnly = true,
                look = "Wet roads: no pillar or mirror in the road's reflection; no torn pillar or mirror outlines in the drops.",
                env = new[] { "PSX_RAINLOOK_CABIN" }, members = new[] { "RainLook.CabinMask" },
            });
            Register(new LabRow
            {
                id = "spray2", group = LabGroup.Rain, name = "SPRAY FROM TYRES", when = LabWhen.NextLoad, pcOnly = true,
                look = "Rain spray from the rear tyres, low and tyre-sized, not out of the tail lamps.",
                env = new[] { "PSX_SPRAY2" }, members = new[] { "TireSpray.Spray2" },
                // the traffic's shared mist lives for the session: built again with the new look
                apply = () => Rain2Traffic.Discard(),
            });
            // ---- LIGHTS ---------------------------------------------------------
            Register(new LabRow
            {
                id = "lightlook", group = LabGroup.Lights, name = "LIGHT LOOK", when = LabWhen.Now,
                look = "Lamp heads as points to the horizon, far road pools, car lamps far off, the glow on wet glass.",
                env = new[] { "PSX_LIGHT_LOOK", "PSX_RAIN2_LAMPGLOW" }, members = new[] { "LightLook.On", "Rain2.LampGlow" },
                apply = () => { LightLook.CarPointsAtStart = LightLook.On && LightLook.CarPoints; Rain2.PushGlass2(); },
            });
            Register(new LabRow
            {
                id = "nopop", group = LabGroup.Lights, name = "LIGHTS: NO POP-IN", when = LabWhen.Now,
                look = "Street lamps and their pools fade in as you drive up; none switch on in front of you.",
                env = new[] { "PSX_LIGHT_NOPOP" }, members = new[] { "StreetLights.NoPop" },
            });
            Register(new LabRow
            {
                id = "night3", group = LabGroup.Lights, name = "NIGHT 3", when = LabWhen.Now,
                look = "The city at night: how dark it is between lamps, the lamp pools, the sky glow.",
                env = new[] { "PSX_NIGHT3" }, members = new[] { "StreetLights.Night3" },
            });
            // NIGHT 4 (city-night4): flips live - the static field is gathered and splatted again (each lamp's
            // view is kept, found once), the sky, the albedo cap and the car reach follow the static every frame
            Register(new LabRow
            {
                id = "night4", group = LabGroup.Lights, name = "NIGHT 4", when = LabWhen.Now,
                look = "Lamps and headlights blocked by walls, buildings, medians and rock; one smooth night sky; pale pavements no brighter than the road.",
                env = new[] { "PSX_NIGHT4" }, members = new[] { "StreetLights.Night4" },
                def = () => false,   // (PC67) OLD until the lane paint keeps its white under NIGHT 4
                apply = () => StreetLights.N4TouchNow(),
            });
            Register(new LabRow
            {
                id = "citylamps3", group = LabGroup.Lights, name = "CITY LAMP ROWS 3", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: the lamp posts' spacing. Changed by a city bake only.",
                env = new[] { "PSX_CITY_LAMPS3" }, requires = "CityMeshes.Lamps3",
            });
            // ---- WORLD ----------------------------------------------------------
            Register(new LabRow
            {
                id = "farhaze2", group = LabGroup.World, name = "FAR HAZE", when = LabWhen.Now,
                look = "Far ridges in haze, rain or fog: never brighter than the sky behind them.",
                env = new[] { "PSX_FARHAZE2" }, members = new[] { "TimeOfDay.FarHaze2" },
                apply = () => TimeOfDay.RefreshSky(),
            });
            Register(new LabRow
            {
                id = "tunnellamps", group = LabGroup.World, name = "TUNNEL WALL LAMPS", when = LabWhen.NextLoad, option = true,
                look = "The amber lamp row along the Parkway tunnels' walls. OFF: unlit, as the real bores.",
                env = new[] { "PSX_TUNNEL_LAMPS" }, members = new[] { "TunnelLamps.WallLampsOn" },
                def = () => false,
            });
            Register(new LabRow
            {
                id = "maparrow", group = LabGroup.World, name = "MAP ARROWS", when = LabWhen.Now,
                look = "The race map's arrows: a slender notched head that points one way only.",
                env = new[] { "PSX_MAP_ARROW_OLD" }, members = new[] { "RouteMapGraphic.OldArrow" },
                newValues = new[] { false },
                apply = () => { foreach (var g in UnityEngine.Object.FindObjectsByType<RouteMapGraphic>(FindObjectsInactive.Exclude)) g.SetVerticesDirty(); },
            });
            // roadevents (11-26): the start gates' fade
            Register(new LabRow
            {
                id = "gatefade", group = LabGroup.World, name = "RACE GATES FADE IN", when = LabWhen.Now,
                look = "Start gates in Charlotte grow out of the distance instead of switching on at 450 m.",
                env = new[] { "PSX_GATEFADE" }, members = new[] { "CityRaceStarts.FadeIn" },
            });
            Register(new LabRow
            {
                id = "fall2", group = LabGroup.World, name = "FALL COLOURS 2", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the fall leaves, verges and leaves on the road. A scene build.",
                env = new[] { "PSX_FALL2" }, requires = "Seasons.Fall2Live",
            });
            // rainlook (11-27)
            Register(new LabRow
            {
                id = "roadleaves", group = LabGroup.World, name = "LEAVES ON ROAD: SQUARE", when = LabWhen.Now,
                look = "Mountain stages in fall: the leaves on the road are leaf-sized, never stretched into dashes.",
                env = new[] { "PSX_RAINLOOK_LEAVES" }, members = new[] { "RainLook.RoadLeaves" },
            });
            Register(new LabRow
            {
                id = "duskleaves", group = LabGroup.World, name = "NO TREE GLOW BY DAY", when = LabWhen.Now,
                look = "Trees at dusk and by day lit as the sun and sky allow, no blacklight glow. Night unchanged.",
                env = new[] { "PSX_RAINLOOK_DUSK" }, members = new[] { "RainLook.DuskLeaves" },
            });
            Register(new LabRow
            {
                id = "tunnel2", group = LabGroup.World, name = "PARKWAY TUNNELS", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the tunnels' bore, walls and fixtures. A scene build.",
                env = new[] { "PSX_TUNNEL2" }, requires = "TunnelLamps.Tunnel2",
            });
            // parkway-tunnel2 (not merged yet): both build-time, so listed as built in; they show by themselves
            // once TunnelHeadlights exists.
            Register(new LabRow
            {
                id = "tunnel3", group = LabGroup.World, name = "TUNNELS: ROUND 3", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the real portals, hillside and signs. A scene build.",
                env = new[] { "PSX_TUNNEL3" }, requires = "TunnelHeadlights.On",
            });
            Register(new LabRow
            {
                id = "roaduv2", group = LabGroup.World, name = "ROAD TEXTURE LENGTH", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the road texture's joints at their real spacing. A scene build.",
                env = new[] { "PSX_ROADUV2" }, requires = "TunnelHeadlights.On",
            });
            // tunnel round 2b (merged 11-25): build-time switches of the stage scenes, listed so a screenshot's build is known
            Register(new LabRow
            {
                id = "tunnel3b", group = LabGroup.World, name = "TUNNELS: PORTAL SLOPES", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: leaf-litter slopes with trees down to the portal wall, a darker bore. A scene build.",
                env = new[] { "PSX_TUNNEL3B" }, requires = "TunnelHeadlights.On",
            });
            Register(new LabRow
            {
                id = "roadsideuv2", group = LabGroup.World, name = "ROADSIDE WITHOUT STRETCH", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: gravel strips, shoulders, cut rock and rails with square texels. A scene build.",
                env = new[] { "PSX_ROADSIDEUV2" }, requires = "TunnelHeadlights.On",
            });
            // c7roads (11-25): build-time switches of the city tiles, listed so a screenshot's build is known
            Register(new LabRow
            {
                id = "islandsolid", group = LabGroup.World, name = "JUNCTION ISLANDS AS SOLIDS", when = LabWhen.BuiltIn,
                look = "Opt-in, built into the city tiles (PSX_CITY_ISLANDSOLID=1): a junction's islands are closed curbed solids standing in one paved surface. Off by default: worse than the base on the line gate (round 2). A city bake.",
                env = new[] { "PSX_CITY_ISLANDSOLID" }, requires = "CityMeshes.IslandSolidsOn",
            });
            // road-surface stage 1 (11-26): a measured junction as one paved surface (CityMeshes.RoadSurface.cs)
            Register(new LabRow
            {
                id = "roadsurface", group = LabGroup.World, name = "JUNCTIONS AS ONE SURFACE", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: a measured junction is one paved surface inside one smooth outline, with real kerbs, islands as solids and its own paint. Two junctions are measured so far (W T Harris at McCullough, the Harris to UC Blvd split). A city bake",
                env = new[] { "PSX_CITY_ROADSURFACE" }, requires = "CityMeshes.RoadSurfaceOn",
            });
            Register(new LabRow
            {
                id = "medianruns", group = LabGroup.World, name = "MEDIANS IN ONE PIECE", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: a median is one run (no fragments or concrete slabs). A city bake.",
                env = new[] { "PSX_CITY_MEDIANRUNS" }, requires = "CityMeshes.MedianRunsOn",
            });
            Register(new LabRow
            {
                id = "baylanes", group = LabGroup.World, name = "TURN BAYS AS LANES", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: a turn bay the map draws as its own road is a lane of its host (the host widens, the bay's own road starts where it turns across). A city bake.",
                env = new[] { "PSX_CITY_BAYLANES" }, requires = "CityMeshes.BayLanesOn",
            });
            Register(new LabRow
            {
                id = "baymedian", group = LabGroup.World, name = "CONCRETE MEDIAN AT BAYS", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: the raised median beside a turn bay is concrete over its whole run (no grass and concrete patches). A city bake.",
                env = new[] { "PSX_CITY_BAYMEDIAN" }, requires = "CityMeshes.BayMedianOn",
            });
            Register(new LabRow
            {
                id = "baymarks", group = LabGroup.World, name = "TURN BAY MARKINGS", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: a turn bay has a solid white line beside the through lanes, a left arrow every 40 m and a stop line at its nose. A city bake.",
                env = new[] { "PSX_CITY_BAYMARKS" }, requires = "CityMeshes.BayMarksOn",
            });
            Register(new LabRow
            {
                id = "smoothcurb", group = LabGroup.World, name = "SMOOTH CURB RETURNS", when = LabWhen.BuiltIn,
                look = "Opt-in (PSX_CITY_SMOOTHCURB=1), built into the city tiles: curb returns within 1 cm of their arc. Off by default: +69 % tile build time. A city bake.",
                env = new[] { "PSX_CITY_SMOOTHCURB" }, requires = "CityMeshes.SmoothCurbOn",
            });
            Register(new LabRow
            {
                id = "goresplit", group = LabGroup.World, name = "SPLITS SHARE ONE EDGE", when = LabWhen.BuiltIn,
                look = "Opt-in (PSX_CITY_GORESPLIT=1), built into the city tiles: two roads leaving a node on the same side no longer overlap. Off by default: worse than the base on the line gate. A city bake.",
                env = new[] { "PSX_CITY_GORESPLIT" }, requires = "LineModel.GoreSplitOn",
            });
            Register(new LabRow
            {
                id = "laneclip", group = LabGroup.World, name = "NOTHING IN A LANE", when = LabWhen.BuiltIn,
                look = "Built into the city tiles: no verge, curb, face, slab or ground piece stands inside a lane; each is cut back at the lane's edge. A city bake.",
                env = new[] { "PSX_CITY_LANECLIP" }, requires = "CityMeshes.LaneClipOn",
            });
            // stage round 3 (merged 11-25): build-time switches of the stage scenes; they show once its play-time code is in
            Register(new LabRow
            {
                id = "bankfit", group = LabGroup.World, name = "ROCK CUTS: SOLID ON ROCK", when = LabWhen.BuiltIn,
                look = "OFF from PC75 by your ruling of 11-26 (in PC72-PC74): a car stops on the rock it sees, but the lowest 2 m of a cut is flat. The blocky rock is back until the solid is shaped to the blocks. A scene build.",
                env = new[] { "PSX_BANKFIT" }, requires = "TunnelHeadlights.EaseHandBack",
            });
            Register(new LabRow
            {
                id = "banksolid", group = LabGroup.World, name = "ROCK CUTS: SOLID IS ROCK", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the rock cuts look as in PC71, and a car now stops on the rock blocks it sees, not on a flat wall in front of them; the soil at a cut's low end is solid too. OLD = the flat wall. A scene build.",
                env = new[] { "PSX_BANKSOLID" }, requires = "TunnelHeadlights.EaseHandBack",
            });
            Register(new LabRow
            {
                id = "timberuv", group = LabGroup.World, name = "TIMBER RAIL UNSTRETCHED", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the timber rail's boards and posts with square texels. A scene build.",
                env = new[] { "PSX_TIMBERUV" }, requires = "TunnelHeadlights.EaseHandBack",
            });
            Register(new LabRow
            {
                id = "portalgaps", group = LabGroup.World, name = "TUNNELS: PORTAL GAPS", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: the portal wall's end and the arch's edge closed to the stones. A scene build.",
                env = new[] { "PSX_PORTALEND", "PSX_PORTALARRIS" }, requires = "TunnelHeadlights.EaseHandBack",
            });
            Register(new LabRow
            {
                id = "tunnelexact", group = LabGroup.World, name = "TUNNELS: EXACT MESHES", when = LabWhen.BuiltIn,
                look = "Built into the stage scenes: tunnel and near-ground meshes saved exact, no hairline see-through. A scene build.",
                env = new[] { "PSX_TUNNELEXACT" }, requires = "TunnelHeadlights.EaseHandBack",
            });
            // tunnel round 2 (merged 11-23): the cars' lights come on by day through a tunnel; read every frame, so live.
            Register(new LabRow
            {
                id = "tunnellights", group = LabGroup.World, name = "HEADLIGHTS IN TUNNELS", when = LabWhen.Now,
                look = "By day the cars' lights come on from 50 m before a tunnel mouth to 50 m past it. OLD: lights follow the hour.",
                env = new[] { "PSX_TUNNEL3" }, members = new[] { "TunnelHeadlights.On" },
            });
            // stage round 3 (merged 11-25): read where the tunnel hands the lights back, so live
            Register(new LabRow
            {
                id = "easehandback", group = LabGroup.World, name = "TUNNEL EXIT: LAMPS EASE", when = LabWhen.Now,
                look = "Leaving a tunnel by day the headlights go out with no one-frame flash of the lenses.",
                env = new[] { "PSX_EASEHANDBACK" }, members = new[] { "TunnelHeadlights.EaseHandBack" },
            });
            // ---- CARS -----------------------------------------------------------
            Register(new LabRow
            {
                id = "cardots", group = LabGroup.Cars, name = "NO DOTS ON PAINT IN SHADE", when = LabWhen.Now, pcOnly = true,
                look = "A car's paint in shade, indoors and in the menus: no dark speckle.",
                env = new[] { "PSX_CARDOTS" }, members = new[] { "CarDots.On" },
                apply = RemarkCarPaint,
            });
            // replayfx (2026-11-26): the paint's keep only where the paint is SEEN; nothing else changes
            Register(new LabRow
            {
                id = "carseen", group = LabGroup.Cars, name = "NO CAR SHAPE ON POLES", when = LabWhen.Now, pcOnly = true,
                look = "No lighter car-shaped patch on a pole, tree, fence or sign standing in front of a car (replays, night).",
                env = new[] { "PSX_CARSEEN" }, members = new[] { "CarDots.Seen" },
            });
            Register(new LabRow
            {
                id = "cardrops2", group = LabGroup.Cars, name = "SMALL BEADS ON CARS", when = LabWhen.Now,
                look = "Rain on the paint at real size (2-6 mm beads, thin runs); cars stay wet in the menus.",
                env = new[] { "PSX_CARDROPS2" }, members = new[] { "TireSpray.CarDrops2" },
                apply = TireSpray.PushCarDrops2,
            });
            // stage round 3 (merged 11-25): the lamp switch is read every frame, so live
            Register(new LabRow
            {
                id = "brakelamphold", group = LabGroup.Cars, name = "BRAKE LAMPS: NO FLICKER", when = LabWhen.Now,
                look = "Brake lamps stay lit through light braking instead of flickering every frame or two.",
                env = new[] { "PSX_BRAKELAMPHOLD" }, members = new[] { "CarLights.BrakeLampHold" },
            });
            // carparts (stage 1): live - switched off, every part on every car shuts at once
            Register(new LabRow
            {
                id = "carparts", group = LabGroup.Cars, name = "OPEN HOOD, TRUNK AND DOORS", when = LabWhen.Now,
                look = "Parked or walking pace: O / D-pad down / OPEN opens the hood, trunk and doors; the engine shows under the hood.",
                env = new[] { "PSX_CARPARTS" }, members = new[] { "CarParts.On" },
                apply = CarParts.ApplyLab,
            });
            // carparts (stage 3, coordinator 11-26): the stand-in, so a build can open something before a pilot car exists
            Register(new LabRow
            {
                id = "carpartspreview", group = LabGroup.Cars, name = "OPENING: STAND-IN PREVIEW", when = LabWhen.Now,
                option = true, def = () => false,
                look = "Striped placeholder panels on one car (the late-80s coupe) until real car art arrives; they open with the normal control.",
                env = new[] { "PSX_PARTS_STANDIN" }, members = new[] { "CarPartsStandIn.Preview" },
                apply = CarPartsStandIn.ApplyLab,
            });
            // ---- TRAFFIC --------------------------------------------------------
            Register(new LabRow
            {
                id = "onewayhold", group = LabGroup.Traffic, name = "ONE-WAY STREETS", when = LabWhen.Now,
                look = "Oncoming cars wait short of a one-way street and turn off out of view.",
                env = new[] { "PSX_TRAFFIC_ONEWAY_HOLD" }, members = new[] { "TrafficSystem.OneWayHoldOn" },
            });
            Register(new LabRow
            {
                id = "aishutbrake", group = LabGroup.Traffic, name = "RIVALS SHUT IN: BRAKE", when = LabWhen.Now,
                look = "A rival boxed in on its right brakes fully and drops back, instead of a head-on.",
                env = new[] { "PSX_AI_SHUT_BRAKE" }, members = new[] { "AIDriver.ShutBrakeOn" },
            });
            Register(new LabRow
            {
                id = "airacingturns", group = LabGroup.Traffic, name = "AI RACING TURNS", when = LabWhen.NextLoad,
                look = "City races: rivals take junction turns on a smooth arc on the pavement, not a 90 degree pivot.",
                env = new[] { "PSX_CP_TURNS" }, members = new[] { "RacingTurns.On" },
            });
            Register(new LabRow
            {
                id = "rulerhold", group = LabGroup.Traffic, name = "RACE DISTANCE: LANE HOLD", when = LabWhen.Now,
                look = "City races: your distance to the gate and your place do not jump in a slip lane or turn bay.",
                env = new[] { "PSX_CP_RULER_HOLD" }, members = new[] { "CheckpointRace.RulerHoldOn" },
            });
            Register(new LabRow
            {
                id = "finishrunout", group = LabGroup.Traffic, name = "FINISHERS BRAKE FOR ROAD", when = LabWhen.Now,
                look = "After the flag, rivals brake for the bends and the end of their run-out instead of coasting on.",
                env = new[] { "PSX_AI_FINISH_RUNOUT" }, members = new[] { "AIDriver.FinishRunOutOn" },
            });
            Register(new LabRow
            {
                id = "junctionwait", group = LabGroup.Traffic, name = "RIVALS WAIT IN JUNCTIONS", when = LabWhen.Now,
                look = "City races: in a junction a rival keeps behind turning traffic and waits, not knocking it or reversing.",
                env = new[] { "PSX_AI_JUNCTION_WAIT", "PSX_AI_JUNCTION_TRAFFIC" }, members = new[] { "AIDriver.JunctionWaitOn", "AIDriver.JunctionTrafficOn" },
            });
            // cityai round 3: the traffic side and the finishers
            Register(new LabRow
            {
                id = "trafficlanesfit", group = LabGroup.Traffic, name = "TRAFFIC KEEPS TO PAVEMENT", when = LabWhen.NextLoad,
                look = "City races: where the road narrows (a ramp, a merge) traffic moves in a lane instead of into the barrier.",
                env = new[] { "PSX_TRAFFIC_LANES_FIT" }, members = new[] { "TrafficSystem.LanesFitOn" },
            });
            Register(new LabRow
            {
                id = "trafficgapstanding", group = LabGroup.Traffic, name = "TRAFFIC SEES STOPPED CARS", when = LabWhen.Now,
                look = "City races: traffic changes lane or edges out only into a gap clear of every car, stopped ones included.",
                env = new[] { "PSX_TRAFFIC_GAP_STANDING" }, members = new[] { "TrafficSystem.GapCountsStandingOn" },
            });
            Register(new LabRow
            {
                id = "trafficlanedrop", group = LabGroup.Traffic, name = "TRAFFIC MERGES AT LANE END", when = LabWhen.Now,
                look = "City races: where four lanes become three (a bridge), traffic merges early and waits short of the end, not into the parapet.",
                env = new[] { "PSX_TRAFFIC_LANE_DROP" }, members = new[] { "TrafficSystem.LaneDropOn" },
            });
            Register(new LabRow
            {
                id = "rivalsroundstopped", group = LabGroup.Traffic, name = "RIVALS ROUND STOPPED CARS", when = LabWhen.Now,
                look = "City races: a rival behind a stopped car or wreck goes round it at a crawl when nothing is coming; none stands longer than 8 s.",
                env = new[] { "PSX_AI_ROUND_STANDING" }, members = new[] { "AIDriver.RoundStandingOn" },
            });
            Register(new LabRow
            {
                id = "trafficnosein", group = LabGroup.Traffic, name = "TRAFFIC NOSES IN, LANE END", when = LabWhen.Now,
                look = "City races: a car waiting where its lane ends takes a shorter gap the longer it waits, and creeps a half-made lane change out.",
                env = new[] { "PSX_TRAFFIC_LANE_END_NOSE" }, members = new[] { "TrafficSystem.LaneEndNoseOn" },
            });
            Register(new LabRow
            {
                id = "trafficknockbytouch", group = LabGroup.Traffic, name = "TRAFFIC KNOCK: WHO TOUCHES", when = LabWhen.Now,
                look = "City races: a traffic car knocked while a racer presses on it shrugs it off and, pushed from behind, drives on.",
                env = new[] { "PSX_TRAFFIC_KNOCK_BY_TOUCH" }, members = new[] { "TrafficSystem.KnockByTouchOn" },
            });
            Register(new LabRow
            {
                id = "rivalsofftails", group = LabGroup.Traffic, name = "RIVALS OFF TRAFFIC TAILS", when = LabWhen.Now,
                look = "City races: a rival nose to tail with a traffic car at its speed lifts and drops back; it does not push it along.",
                env = new[] { "PSX_AI_NO_TAIL_PUSH" }, members = new[] { "AIDriver.NoTailPushOn" },
            });
            Register(new LabRow
            {
                id = "turnsahead", group = LabGroup.Traffic, name = "RIVAL TURNS LAID AHEAD", when = LabWhen.NextLoad,
                look = "City races: every junction turn near the route is laid at load; a new one mid-race costs a slice a frame.",
                env = new[] { "PSX_CP_TURNS_WARMALL", "PSX_CP_TURNS_DEFER" }, members = new[] { "RacingTurns.WarmAllTurns", "RacingTurns.DeferOn" },
            });
            Register(new LabRow
            {
                id = "packfilein", group = LabGroup.Traffic, name = "RIVALS FILE IN, NO SQUEEZE", when = LabWhen.Now,
                look = "With oncoming traffic, a rival squeezed toward the centre line lifts and drops in behind, never over it.",
                env = new[] { "PSX_AI_PACK_FILEIN" }, members = new[] { "AIDriver.PackFileInOn" },
            });
            Register(new LabRow
            {
                id = "finishdriveon", group = LabGroup.Traffic, name = "FINISHERS CLEAR THE LANE", when = LabWhen.Now,
                look = "After the flag a rival drives on at traffic speed and stops on the verge, never parked in the lane.",
                env = new[] { "PSX_AI_FINISH_DRIVEON" }, members = new[] { "AIDriver.FinishDriveOnOn" },
            });
            Register(new LabRow
            {
                id = "trafficsoftknock", group = LabGroup.Traffic, name = "TRAFFIC SHRUGS OFF KNOCKS", when = LabWhen.Now,
                look = "A traffic car touched under 6 m/s stops a moment and drives on, instead of standing wrecked all race.",
                env = new[] { "PSX_TRAFFIC_SOFTKNOCK" }, members = new[] { "TrafficSystem.SoftKnockOn" },
            });
            Register(new LabRow
            {
                id = "trafficroundwreck", group = LabGroup.Traffic, name = "TRAFFIC GOES ROUND WRECKS", when = LabWhen.Now,
                look = "Traffic eases out round a wrecked car in its lane where there is room, instead of queueing behind it.",
                env = new[] { "PSX_TRAFFIC_ROUND_WRECK" }, members = new[] { "TrafficSystem.RoundWreckOn" },
            });
            Register(new LabRow
            {
                id = "trafficseestanding", group = LabGroup.Traffic, name = "TRAFFIC STOPS FOR CARS", when = LabWhen.Now,
                look = "Traffic brakes for a car standing or crawling in its lane, slewed or coming its way, not into it.",
                env = new[] { "PSX_TRAFFIC_SEE_STANDING" }, members = new[] { "TrafficSystem.SeeStandingOn" },
            });
            Register(new LabRow
            {
                id = "trafficlanecorners", group = LabGroup.Traffic, name = "TRAFFIC LANES ON CORNERS", when = LabWhen.Now,
                look = "Traffic in the outer lane turns a sharp junction corner on its own curve, not sweeping across.",
                env = new[] { "PSX_TRAFFIC_LANE_CORNERS" }, members = new[] { "TrafficSystem.LaneCornersOn" },
            });
            // stage round 3 (merged 11-25): the planner reads it every tick, so live; every car, the autopilot too
            Register(new LabRow
            {
                id = "aiplancap", group = LabGroup.Traffic, name = "RIVALS: BEND SPEED CAP", when = LabWhen.Now,
                look = "Rivals plan fast bends by measured grip, so they stop running wide into trees. OLD: faster, wider.",
                env = new[] { "PSX_AI_PLANCAP" }, members = new[] { "AIDriver.PlanAeroCap" },
            });
            // ---- TAPES (the replayfx round, 2026-11-26: the camcorder's switches were environment-only) ----
            Register(new LabRow
            {
                id = "steadyhands", group = LabGroup.Tapes, name = "CAMCORDER: STEADIER HANDS", when = LabWhen.Now,
                look = "Replay cameras shake far less, each bystander their own way: most steady, a few shakier, some rock steady.",
                env = new[] { "PSX_STEADYHANDS" }, members = new[] { "CamcorderCam.SteadyHands" },
            });
            Register(new LabRow
            {
                id = "camcorder", group = LabGroup.Tapes, name = "CAMCORDER REPLAYS", when = LabWhen.Now,
                look = "From the next replay: bystanders' tapes from the start, each checkpoint and the finish. OLD: trackside replay.",
                env = new[] { "PSX_CAMCORDER" }, members = new[] { "Camcorder.Enabled" },
            });
            Register(new LabRow
            {
                id = "cammic", group = LabGroup.Tapes, name = "CAMCORDER: TAPE SOUND", when = LabWhen.Now, option = true,
                look = "From the next replay: the camcorder's own mic - mono, wind, hiss. OFF: the game's own sound.",
                env = new[] { "PSX_CAM_MIC" }, members = new[] { "Camcorder.MicOn" },
            });
            Register(new LabRow
            {
                id = "camgain", group = LabGroup.Tapes, name = "CAMCORDER: NIGHT GAIN", when = LabWhen.Now,
                look = "Night tapes brightened up to 3.2 times, as a camcorder's gain does. OLD: 1.7 times, darker nights.",
                env = new[] { "PSX_CAM_GAIN" }, members = new[] { "Camcorder.GainRound2" },
            });
            Register(new LabRow
            {
                id = "camclearview", group = LabGroup.Tapes, name = "CAMERAS: CLEAR VIEW", when = LabWhen.Now,
                look = "From the next replay: no bystander films with a pole or trunk right in front of the lens. Not yet played.",
                env = new[] { "PSX_CAM_CLEARVIEW" }, members = new[] { "RacePlan.ClearViewOn" },
                def = () => false,
            });
            Register(new LabRow
            {
                id = "tapes", group = LabGroup.Tapes, name = "TAPES ON THE SHELF", when = LabWhen.Now, option = true,
                look = "SAVE TAPE after a race, the tape shelf at home and the VIDEO counter. OFF hides them; saved tapes stay.",
                env = new[] { "PSX_TAPES" }, members = new[] { "Tapes.Enabled" },
            });
            Register(new LabRow
            {
                id = "tapeflex", group = LabGroup.Tapes, name = "TAPES: PRICE BY CLIPS", when = LabWhen.Now,
                look = "A tape is worth what its clips show plus the result. OLD: round 1's price, by the result alone.",
                env = new[] { "PSX_TAPE_FLEX" }, members = new[] { "Tapes.FlexOn" },
            });
            Register(new LabRow
            {
                id = "montage", group = LabGroup.Tapes, name = "TAPES: THE MONTAGE", when = LabWhen.Now, option = true,
                look = "A MONTAGE button on the tape shelf: mark clips in and out to cut your own tape. Not yet played through.",
                env = new[] { "PSX_MONTAGE" }, members = new[] { "Tapes.MontageOn" },
                def = () => false,
            });
        }

        /// <summary>Add a row (at boot, in RegisterAll). Resolves its members by name; a row whose member is missing
        /// (a branch not merged) is kept but not <see cref="LabRow.Present"/>, so it never shows.</summary>
        public static void Register(LabRow r)
        {
            if (r == null || string.IsNullOrEmpty(r.id)) return;
            all.RemoveAll(x => x.id == r.id);
            r.resolved = new MemberInfo[r.members.Length];
            bool ok = true;
            for (int i = 0; i < r.members.Length; i++)
            {
                r.resolved[i] = Resolve(r.members[i], r.when != LabWhen.BuiltIn);
                if (r.resolved[i] == null) ok = false;
            }
            if (ok && !string.IsNullOrEmpty(r.requires)) ok = Resolve(r.requires, false) != null;
            if (ok && r.members.Length == 0 && r.when != LabWhen.BuiltIn) ok = false;
            r.present = ok;
            all.Add(r);
        }

        const BindingFlags SF = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static MemberInfo Resolve(string path, bool writable)
        {
            int dot = path.LastIndexOf('.');
            if (dot <= 0) return null;
            string tn = path.Substring(0, dot), mn = path.Substring(dot + 1);
            var asm = typeof(LabSwitches).Assembly;
            var t = asm.GetType(tn) ?? asm.GetType("PSXRacing." + tn) ?? asm.GetType("PSXRacing.City." + tn);
            if (t == null) return null;
            var f = t.GetField(mn, SF);
            if (f != null) return f.FieldType == typeof(bool) && (!writable || (!f.IsInitOnly && !f.IsLiteral)) ? f : null;
            var p = t.GetProperty(mn, SF);
            if (p != null && p.PropertyType == typeof(bool) && p.CanRead && (!writable || p.CanWrite)) return p;
            return null;
        }

        static bool Read(MemberInfo m)
        {
            try
            {
                if (m is FieldInfo f) return (bool)f.GetValue(null);
                if (m is PropertyInfo p) return (bool)p.GetValue(null);
            }
            catch (Exception e) { Debug.LogWarning("[Lab] read " + m.Name + ": " + e.Message); }
            return false;
        }

        static void Write(MemberInfo m, bool v)
        {
            if (m is FieldInfo f) { if ((bool)f.GetValue(null) != v) f.SetValue(null, v); }
            else if (m is PropertyInfo p) { if ((bool)p.GetValue(null) != v) p.SetValue(null, v); }
        }

        // =====================================================================
        //  Boot, load, persistence
        // =====================================================================
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BootHook() => Boot();

        /// <summary>Resolve every row; unless this is a test run, put each row's saved choice into its statics.
        /// Before the first scene, so a NEXT LOAD row's choice is in place for the first load too.</summary>
        public static void Boot() => Boot(DetectTestRun());

        /// <summary>The boot with a given test-run verdict (the self-test's way in).</summary>
        public static void Boot(bool testRun)
        {
            TestRun = testRun;
            RegisterAll();
            foreach (var r in all)
            {
                if (!r.present) continue;
                r.envHeld = false;
                foreach (var e in r.env)
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(e))) { r.envHeld = true; break; }
                if (r.when == LabWhen.BuiltIn) { r.chosen = r.Default; continue; }
                r.chosen = Live(r);
                if (r.envHeld || TestRun) continue;
                int saved = PlayerPrefs.GetInt(KeyPrefix + r.id, -1);
                if (saved < 0) continue;
                r.chosen = saved == 1;
                if (r.chosen != Live(r)) WriteRow(r, r.chosen);
            }
            if (!booted)
            {
                booted = true;
                SceneManager.sceneUnloaded += _ => ApplyPending();
                SceneManager.sceneLoaded += (_, __) => ApplyPending();
            }
            Rebuild();
        }

        /// <summary>A level load: every NEXT LOAD row's pending choice goes into its statics now (on the way out of
        /// the old scene and again as the new one arrives: whichever comes first, nothing of the new level is built
        /// with the old value).</summary>
        public static void ApplyPending()
        {
            bool any = false;
            foreach (var r in all)
                if (r.present && r.when == LabWhen.NextLoad && r.chosen != Live(r)) { WriteRow(r, r.chosen); any = true; }
            if (any) Rebuild();
        }

        /// <summary>Every member's value now (tests: a snapshot to compare and restore).</summary>
        public static bool[] MemberValues(LabRow r)
        {
            var v = new bool[r.resolved != null ? r.resolved.Length : 0];
            for (int i = 0; i < v.Length; i++) v[i] = r.resolved[i] != null && Read(r.resolved[i]);
            return v;
        }

        /// <summary>Put a snapshot back as it was (tests), without the row's apply.</summary>
        public static void RestoreMembers(LabRow r, bool[] v)
        {
            for (int i = 0; i < v.Length && i < r.resolved.Length; i++) if (r.resolved[i] != null) Write(r.resolved[i], v[i]);
        }

        /// <summary>The members' names as resolved (the hand-back table, the self-test).</summary>
        public static string MemberList(LabRow r) => string.Join(", ", r.members);

        /// <summary>The state the statics hold right now (the first member).</summary>
        public static bool Live(LabRow r)
        {
            if (r == null || r.resolved == null || r.resolved.Length == 0 || r.resolved[0] == null) return r != null && r.Default;
            return Read(r.resolved[0]) == r.NewValue(0);
        }

        /// <summary>True when a NEXT LOAD row waits for a load.</summary>
        public static bool Pending(LabRow r) => r != null && r.present && r.when == LabWhen.NextLoad && r.chosen != Live(r);
        public static bool AnyPending { get { foreach (var r in all) if (Pending(r)) return true; return false; } }

        static void WriteRow(LabRow r, bool v)
        {
            for (int i = 0; i < r.resolved.Length; i++) Write(r.resolved[i], r.NewValue(i) ? v : !v);
            try { r.apply?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[Lab] " + r.id + " apply: " + e.Message); }
        }

        /// <summary>The page's press: NEW &lt;-&gt; OLD. False when the row cannot change (built in, held by a variable).</summary>
        public static bool Flip(LabRow r) => Set(r, !r.chosen, !TestRun && !PerfTour.Active);

        /// <summary>Choose a state. <paramref name="persist"/>: remember it (never in a test run). A NOW row is
        /// written at once, a NEXT LOAD row at the next load.</summary>
        public static bool Set(LabRow r, bool v, bool persist)
        {
            if (r == null || !r.present || r.when == LabWhen.BuiltIn || r.envHeld) return false;
            r.chosen = v;
            if (persist && !TestRun)
            {
                if (v == r.Default) PlayerPrefs.DeleteKey(KeyPrefix + r.id);
                else PlayerPrefs.SetInt(KeyPrefix + r.id, v ? 1 : 0);
                PlayerPrefs.Save();
            }
            if (r.when == LabWhen.Now && v != Live(r)) WriteRow(r, v);
            Rebuild();
            return true;
        }

        /// <summary>RESET ALL TO DEFAULT (rows held by a variable stay as the variable says).</summary>
        public static void ResetAll(bool persist)
        {
            foreach (var r in all) if (r.present && r.when != LabWhen.BuiltIn && !r.envHeld) Set(r, r.Default, persist);
        }

        /// <summary>A row's saved choice (-1 none, 0 OLD/OFF, 1 NEW/ON): for a row retired into a player option.</summary>
        public static int SavedChoice(string id) => PlayerPrefs.GetInt(KeyPrefix + id, -1);

        /// <summary>Every row that shows on this platform.</summary>
        public static List<LabRow> Rows(LabGroup? g = null)
        {
            if (!booted) Boot();
            var list = new List<LabRow>();
            foreach (var r in all)
                if (r.present && (!r.pcOnly || PlatformProfile.PC) && (g == null || r.group == g.Value)) list.Add(r);
            return list;
        }

        public static LabRow Find(string id)
        {
            if (!booted) Boot();
            foreach (var r in all) if (r.id == id) return r;
            return null;
        }

        public static int ChangedCount { get { int n = 0; foreach (var r in Rows()) if (r.Changed) n++; return n; } }

        static void Rebuild()
        {
            Version++;
            StringBuilder sb = null;
            foreach (var r in all)
            {
                if (!r.Changed || (r.pcOnly && !PlatformProfile.PC)) continue;
                if (sb == null) sb = new StringBuilder("LAB: "); else sb.Append(", ");
                sb.Append(r.name).Append(' ').Append(r.option ? r.StateWord(r.chosen) : r.chosen ? "new" : "old");
                if (Pending(r)) sb.Append(" (next load)");
                if (r.envHeld) sb.Append(" (env)");
            }
            ChangedLine = sb?.ToString();
        }

        // =====================================================================
        //  Test runs
        // =====================================================================
        static readonly string[] TestArgs = { "-perftour", "-titleshots", "-psx-title-hour", "-psx-lab-ignore", "-batchmode", "-nographics" };

        /// <summary>A run whose numbers or pictures must not follow the owner's toggles.</summary>
        static bool DetectTestRun()
        {
            if (Environment.GetEnvironmentVariable("PSX_LAB_IGNORE") == "1") return true;
            if (Application.isBatchMode) return true;
#if UNITY_EDITOR
            // the editor's PlayerPrefs are not the owner's, and every agent's play check runs here:
            // saved choices only when asked for (PSX_LAB_PREFS=1)
            return Environment.GetEnvironmentVariable("PSX_LAB_PREFS") != "1";
#elif UNITY_WEBGL
            string url = Application.absoluteURL ?? "";
            return url.Contains("perftour=") || url.Contains("shot=");
#else
            foreach (var a in Environment.GetCommandLineArgs())
                foreach (var t in TestArgs)
                    if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
#endif
        }

        /// <summary>Every PSX/CarPaint material in the scene marked (or unmarked) as CarDots.On now says.</summary>
        static void RemarkCarPaint()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) CarDots.Mark(mats[i]);
            }
        }
    }
}
