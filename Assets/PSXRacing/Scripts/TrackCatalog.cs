using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Every circuit in the game, as data.
    ///
    /// One track used to be a `ControlPoints` array inside the scene builder,
    /// which is fine for one track and useless for four: the LifeSim has to be
    /// able to NAME the circuits, quote their length before a race, and draw
    /// them in a picker, and none of that is reachable from editor-only code.
    /// So the shapes live here, in runtime code, and the builder consumes them
    /// through <see cref="Sample"/> — the same resampler the waypoints are
    /// generated from, so the length the menu quotes is the length the car
    /// actually drives rather than a hand-typed number that drifts.
    ///
    /// The scene list in Build Settings is [0] LifeHome then one scene per
    /// entry here, IN THIS ORDER. <see cref="SceneIndex"/> is the only place
    /// that contract is written down.
    /// </summary>
    public static class TrackCatalog
    {
        public class TrackDef
        {
            /// <summary>Catalog key AND scene file name. Both, deliberately: a
            /// track whose scene is called something else is a lookup nobody
            /// can follow.</summary>
            public string id;
            public string name;
            public string blurb;
            /// <summary>Circuit control points in metres (x, z), a closed loop
            /// with a Catmull-Rom spline through them.</summary>
            public Vector2[] controlPoints;

            /// <summary>
            /// Height in metres at each control point, splined by exactly the
            /// same Catmull-Rom as the plan shape so a crest lands where the
            /// corner does. Null, or the wrong length, means a flat circuit —
            /// which is what a drag strip has to be.
            ///
            /// Authored rather than generated. A height field the road is
            /// draped over gives you gradients nobody chose, and the numbers
            /// that matter here are the ones you cannot see in a list: peak
            /// GRADE (these run 5% in town, 10% over the pass) and the vertical
            /// RADIUS of the crests, which has to stay large enough that the
            /// ground mesh under the road does not cut through it between its
            /// own vertices. tools/elevation-check prints both.
            /// </summary>
            public float[] controlHeights;

            /// <summary>
            /// Elevated spans, as (first metre, last metre) along the lap from
            /// the start line. Inside one the road keeps its graded height and
            /// the GROUND does not follow it — it drops away by
            /// <see cref="bridgeDepth"/> into a gorge, and the builder throws a
            /// deck and piers under the tarmac to carry it across.
            ///
            /// Spans are authored rather than inferred from "road is higher
            /// than terrain", because with the terrain built to follow the road
            /// that condition is never true by accident. A bridge is a decision.
            /// </summary>
            public Vector2[] bridges;
            /// <summary>Spans, in metres along the lap, where the road runs
            /// UNDER the mountain. The builder leaves the ridge standing over
            /// the road there and puts a tube round it. From the bake.</summary>
            public Vector2[] tunnels;
            /// <summary>Where the route crosses ITSELF at a grade separation:
            /// pairs of (metres of the upper road, metres of the lower road).
            /// Both Parkway loops pass under their own bridge once. From the
            /// bake, which also lifts the upper road and forces a span there;
            /// kept for the record and the self-test.</summary>
            public Vector2[] crossings;
            /// <summary>How far the ground falls away beneath a bridge span.
            /// Deep enough that the piers read as structure rather than as
            /// kerbstones.</summary>
            public float bridgeDepth = 9f;

            public float roadWidth = 12f;
            /// <summary>
            /// THE SPEED LIMIT on this road, km/h — the speed a delivery is
            /// already doing when the race scene opens on it. Zero means a
            /// STANDING start: a strip has no limit because the tree is the
            /// event, and a car placed rolling on one would cross the beams
            /// before the lights. Real-road venues carry their posted limit
            /// (the Parkway's 45 mph is 72); the fictional circuits carry a
            /// town street's 45. Read by RaceManager through
            /// RaceHandoff.RollingStartKmh, and copied by ReverseTwin — the
            /// same road has the same limit in both directions.
            ///
            /// Keep the name and the unit: `speedLimitKmh`, km/h. New venues
            /// (the Charlotte ones being appended) set it by this exact name.
            /// </summary>
            public float speedLimitKmh = DefaultSpeedLimitKmh;
            /// <summary>Laps for a full race. Short circuits run more of them so
            /// every race lands near 3.3 km — the distance the fuel and wear
            /// economy was balanced against when there was only one track.
            /// </summary>
            public int laps = 3;

            /// <summary>A SYNTHETIC straight strip rather than a closed loop:
            /// the waypoints are generated flat and dead straight from
            /// <see cref="dragMeters"/> instead of coming from map data or
            /// control points, and the road ribbon does not close.
            ///
            /// This is a statement about GEOMETRY, not about the event. The
            /// bridge runs are drag races on real, graded, mapped road, so they
            /// set <see cref="dragEvent"/> and leave this false — the builder
            /// keys its bridge decks and piers off this flag, and a strip
            /// cannot have either.</summary>
            public bool drag;

            /// <summary>
            /// Run this as a DRAG RACE regardless of where the geometry came
            /// from: field staged abreast on the line, trap speed on the HUD,
            /// the top-down camera unlocked.
            ///
            /// Split out from <see cref="drag"/> when Bogue Banks arrived. A
            /// drag race down a strip and a drag race over a 1.4 km high-rise
            /// bridge want the same presentation and completely different
            /// geometry, and the runtime already made exactly this distinction
            /// (TrackPath.pointToPoint vs TrackPath.drag) — the catalog was the
            /// only place still conflating them.
            /// </summary>
            public bool dragEvent;

            /// <summary>Show the drag presentation. Ask this, never
            /// <see cref="drag"/>, for anything the PLAYER sees.</summary>
            public bool IsDragEvent => drag || dragEvent;

            /// <summary>A streamed open city (Charlotte) rather than a circuit.
            /// The scene is built nearly empty and CityWorld generates tiles at
            /// runtime; there is no waypoint loop, no laps, no TrackPath —
            /// every loop-shaped consumer branches on this the way it already
            /// branches on <see cref="drag"/>.</summary>
            public bool city;

            /// <summary>
            /// A RACE through the streamed city: the id of a route baked into
            /// charlotte_city.bytes (an edge chain through the same graph
            /// FREE ROAM drives). The scene is a Charlotte scene like the
            /// free-roam one, and CityMode turns the route into the TrackPath
            /// RaceManager races on. Null on the open city and on every
            /// venue outside it. The three Charlotte venues were baked stages
            /// with their own SRTM terrain until 2026-09-11; now the race and
            /// free roam are the same map, which is what the owner asked for.
            /// </summary>
            public string cityRoute;

            /// <summary>The open city with no race in it: FREE ROAM's door,
            /// skipped by every picker. This is what "city" used to mean
            /// everywhere before the city races existed.</summary>
            public bool IsRoam => city && string.IsNullOrEmpty(cityRoute);
            /// <summary>A race venue whose scene is the streamed city.</summary>
            public bool IsCityRace => city && !string.IsNullOrEmpty(cityRoute);

            // Loaded lazily out of charlotte_routes.json by EnsureRoute: the
            // menu's copy of the route (length, line, finish, a 4 m polyline
            // for the map), so the front end never parses the 2.5 MB graph.
            [System.NonSerialized] public bool routeLoaded;
            [System.NonSerialized] public float routeLengthM, routeStartM, routeFinishM;
            [System.NonSerialized] public Vector3[] routePts;

            /// <summary>
            /// A point-to-point STAGE on a real road: the centreline comes out
            /// of a baked Resources JSON (real map data, real elevation) rather
            /// than from control points, and the run has ENDS like a drag strip
            /// — clamped waypoints, a finish at a distance, a standing start.
            /// Everything drag-SPECIFIC (staging four abreast, trap-speed talk,
            /// the top-down camera) stays on <see cref="drag"/>; a stage is a
            /// mountain road, not a strip.
            /// </summary>
            public bool stage;
            /// <summary>Resources name of the stage bake (e.g. "brp_stage").</summary>
            public string stageData;

            /// <summary>
            /// A stage whose ENDS MEET: the I-277 belt round uptown Charlotte
            /// is real map data with real elevation and no finish line — it
            /// is a lap. The terrain, the bridges and the scenery come from
            /// the stage builder; the laps, the AI and the reverse rule
            /// behave like a circuit. So: stage geometry, circuit race.
            /// <see cref="RaceMeters"/> is the lap times <see cref="laps"/>,
            /// <see cref="FinishIndex"/> is -1, and the bake's JSON says
            /// <c>loop</c> too so the two cannot disagree silently. The Bogue
            /// memory called this "the missing piece" for a year.
            /// </summary>
            public bool loop;

            /// <summary>
            /// ONE direction of traffic: paint lane dashes only, never the
            /// double yellow. A freeway carriageway with a no-passing line
            /// down the middle of it is the first thing a Charlotte driver
            /// would notice. Only the road texture reads this; the builder
            /// passes <c>drag || oneWay</c> to EnsureTrackRoadTex.
            /// </summary>
            public bool oneWay;

            /// <summary>Where this stage's origin sits in charlotte_city.json's
            /// frame, when the bake says it is in Charlotte at all
            /// (<see cref="stageInCity"/>). The stage builder uses it to find
            /// uptown and put the towers where the towers are; a mountain
            /// bake leaves it false and nothing asks.</summary>
            [System.NonSerialized] public bool stageInCity;
            [System.NonSerialized] public Vector2 stageCityOrigin;
            /// <summary>The road width the bake was made for, or 0 when the
            /// bake does not say. The self-test holds the catalog's
            /// <see cref="roadWidth"/> to it: the lane ladder and the barrier
            /// line are both derived from the catalog number, and a bake made
            /// for four lanes under a catalog row that says two is a road
            /// half the width of the map it was cut from.</summary>
            [System.NonSerialized] public float stageRoadWidthM;

            // Loaded lazily out of stageData by EnsureStage. The waypoints ARE
            // the bake — no spline pass here, the bake already resampled its
            // spline at Spacing.
            [System.NonSerialized] public Vector3[] stagePts;
            [System.NonSerialized] public float stageStartLineM;
            [System.NonSerialized] public string stageAttribution = "";
            /// <summary>World Y of the sea surface. 0 on a stage with no water
            /// — the mountain leaves it there and every water pass skips.
            /// </summary>
            [System.NonSerialized] public float stageWaterY;
            /// <summary>Metres from the line to the traps. 402.336 is a quarter
            /// mile, 201.168 an eighth — spelled out rather than rounded,
            /// because the whole point of a drag strip is the number at the end
            /// of it.</summary>
            public float dragMeters;
            /// <summary>What the HUD calls it. See TrackPath.dragLabel.</summary>
            public string dragLabel = "";
            /// <summary>Shutdown area past the traps. Long enough that a car
            /// doing 250 km/h through the lights has somewhere to stop.</summary>
            public float dragShutdown = 320f;

            float length = -1f;
            /// <summary>Centreline length in metres, measured off the resampled
            /// waypoints rather than declared.</summary>
            public float LengthM
            {
                get
                {
                    if (IsRoam) return 0f;   // an open city has no lap to measure
                    if (IsCityRace) { EnsureRoute(this); return routeLengthM; }
                    if (length < 0f) length = Sample(this, Spacing).Count * Spacing;
                    return length;
                }
            }

            /// <summary>What a full race covers, for the fuel gate and the
            /// pre-race quote. A strip is measured to the TRAPS — the shutdown
            /// area is real distance the car covers, but quoting a quarter mile
            /// as 722 m would be the one number a drag racer would not forgive.
            /// A stage is measured to its finish line the same way.
            /// </summary>
            public float RaceMeters
            {
                get
                {
                    // A stage's finishM is measured from WAYPOINT 0, which is
                    // the far end of the lead-in, so the raced distance is the
                    // finish less the lead-in. It matters more here than it did
                    // on the parkway: 60 m lost in 6.9 km is a rounding error,
                    // 60 m added to a quarter mile is a menu quoting 462 m for
                    // a race the player will time at 402.
                    // A loop stage has no finish line to measure to: it is
                    // a lap, raced <see cref="laps"/> times like a circuit.
                    if (IsCityRace)
                    {
                        // Same shape as a stage: a loop is a lap times laps, a
                        // route with ends is line to finish.
                        EnsureRoute(this);
                        return loop ? routeLengthM * laps : routeFinishM - routeStartM;
                    }
                    if (stage && loop) { EnsureStage(this); return LengthM * laps; }
                    if (stage) { EnsureStage(this); return dragMeters - stageStartLineM; }
                    return drag ? dragMeters : LengthM * laps;
                }
            }

            /// <summary>Waypoint index the traps (or the stage finish) sit at,
            /// -1 on a circuit.</summary>
            public int FinishIndex
            {
                get
                {
                    if (IsCityRace)
                    {
                        EnsureRoute(this);
                        return loop ? -1 : Mathf.RoundToInt(routeFinishM / Spacing);
                    }
                    if (stage && loop) return -1;    // a lap has no traps
                    if (stage) { EnsureStage(this); return Mathf.RoundToInt(dragMeters / Spacing); }
                    return drag ? Mathf.RoundToInt(dragMeters / Spacing) : -1;
                }
            }

            /// <summary>
            /// Whether this venue has pumps you can pull into mid-race.
            ///
            /// Every circuit does. A drag strip does not, and cannot: the race
            /// is 400 metres in a straight line and ends at the traps, so there
            /// is no point on the run where a forecourt would be reachable.
            /// The pre-race fuel gate branches on this — a strip still has to
            /// be entered with enough fuel for the whole run.
            /// </summary>
            // City pumps are a follow-up; the fuel truck covers it. The
            // parkway has no services on it in real life either — the fuel
            // gate demands a tank for the whole run, which at 7 km is small.
            public bool hasFuelStop => !drag && !city && !stage;

            /// <summary>
            /// The id of the circuit this one is the REVERSE of, or null.
            ///
            /// A reverse track has NO SCENE OF ITS OWN — it races in its
            /// forward twin's, with the waypoint list turned round at load. The
            /// road, the barriers, the scenery and the elevation are the same
            /// physical objects standing in the same places, so a second scene
            /// would buy nothing but a second copy of all of it in the
            /// download. See TrackPath.ReverseInPlace.
            /// </summary>
            public string reverseOf;
            /// <summary>Set on a FORWARD track to say it must not be offered
            /// backwards. Nothing sets it yet; it exists so a venue that cannot
            /// be reversed for a real reason can say so, rather than the rule
            /// below growing a list of exceptions.</summary>
            public bool noReverse;

            /// <summary>This entry IS a reverse of something else.</summary>
            public bool Reversed => !string.IsNullOrEmpty(reverseOf);

            /// <summary>
            /// Is this venue worth driving the other way?
            ///
            /// Everything that is a real ROAD rather than a straight: the four
            /// closed circuits, and the mountain stage, which northbound is a
            /// climb where southbound is a descent and is genuinely a different
            /// drive. Not the drag strips, where the direction IS the event;
            /// not the bridge and beach runs, which are drag events on real
            /// roads; and not the city, which has no centreline at all.
            /// </summary>
            public bool CanReverse => !drag && !dragEvent && !IsRoam && !noReverse && !Reversed;
        }

        /// <summary>Waypoint spacing, metres. The scene builder reads its own
        /// Spacing from here so the menu and the mesh cannot disagree.</summary>
        public const float Spacing = 4f;

        /// <summary>
        /// The speed limit a road has when nobody wrote one down: 45 km/h, a
        /// town street. It is ALSO the speed the town and the neighbourhood
        /// put an arriving car down at (TownEdge.ArrivalKmh reads it from
        /// here), so a car that rolls through the edge of town and a car that
        /// rolls into a delivery are doing the same speed for the same reason.
        /// </summary>
        public const float DefaultSpeedLimitKmh = 45f;

        // Layouts 2-4 were generated as polar loops — r(t) = R(1 + sum a_k
        // cos(k t + phi_k)) — which cannot self-intersect however the harmonics
        // are tuned, then checked for minimum corner radius (>= 22 m, so a
        // hairpin is tight rather than impossible) and for self-clearance (no
        // two parts of the circuit closer than road + both wall lines).
        /// <summary>The venues as WRITTEN. <see cref="All"/> is this plus a
        /// reverse twin for every one of them that has two directions worth
        /// driving, and it is this array — not that one — whose length decides
        /// how many scenes get built.</summary>
        static readonly TrackDef[] Authored =
        {
            new TrackDef
            {
                id = "CityCircuit",
                name = "SUNSET CITY GP",
                blurb = "Downtown blocks and close walls. The circuit this game was tuned on.",
                roadWidth = 12f,
                laps = 3,
                speedLimitKmh = 45f,    // downtown blocks: a city street
                controlPoints = new[]
                {
                    new Vector2(0, 0),      new Vector2(120, 0),   new Vector2(180, 8),
                    new Vector2(215, 40),   new Vector2(220, 95),  new Vector2(205, 150),
                    new Vector2(230, 205),  new Vector2(215, 260), new Vector2(160, 285),
                    new Vector2(80, 290),   new Vector2(0, 285),   new Vector2(-70, 265),
                    new Vector2(-110, 215), new Vector2(-105, 150),new Vector2(-140, 100),
                    new Vector2(-135, 40),  new Vector2(-90, -5),
                },
                // Downtown on a slope: flat along the pit straight, climbing
                // the whole east side to a bluff at the top of the circuit,
                // then back down through the west. Peak grade 6.8%, which is a
                // steep city street and nothing worse.
                controlHeights = new[]
                {
                    0f,    0.5f,  2f,    5f,    8f,    10f,   11f,   10f,   7f,
                    4f,    2f,    1f,    2f,    4f,    3f,    1f,    0f,
                },
                // The flyover across the top of the bluff, where the road is
                // already 11 m up: the ground drops out from under it into a
                // cutting and the circuit crosses on a deck.
                bridges = new[] { new Vector2(340f, 450f) },
                bridgeDepth = 9f,
            },
            new TrackDef
            {
                id = "HarborPoint",
                name = "HARBOR POINT",
                blurb = "Short, narrow and relentless. Nowhere to put the power down.",
                roadWidth = 10.5f,
                laps = 4,
                speedLimitKmh = 45f,    // dock roads
                controlPoints = new[]
                {
                    new Vector2(142, 0),   new Vector2(109, 45),  new Vector2(79, 79),
                    new Vector2(38, 92),   new Vector2(0, 110),   new Vector2(-53, 128),
                    new Vector2(-89, 89),  new Vector2(-94, 39),  new Vector2(-122, 0),
                    new Vector2(-128, -53),new Vector2(-91, -91), new Vector2(-47, -114),
                    new Vector2(0, -106),  new Vector2(32, -78),  new Vector2(80, -80),
                    new Vector2(143, -59),
                },
                // Dock land is flat. The one thing that is not is the lift over
                // the channel on the north side, and everything else here is
                // the approach to it and the run back down. 5.0% at its worst.
                controlHeights = new[]
                {
                    0f,    0.6f,  1.6f,  3.0f,  4.8f,  5.6f,  4.0f,  1.8f,
                    0.4f,  0f,    0f,    0.4f,  0.9f,  0.6f,  0.2f,  0f,
                },
                // The channel itself. Ten metres of water under the deck, which
                // is the only place on this circuit you can see daylight beside
                // the road.
                bridges = new[] { new Vector2(150f, 270f) },
                bridgeDepth = 10f,
            },
            new TrackDef
            {
                id = "RidgePass",
                name = "RIDGE PASS",
                blurb = "Long, wide and flowing, through the trees. Two corners bite.",
                roadWidth = 13f,
                laps = 2,
                speedLimitKmh = 72f,    // a mountain pass: 45 mph
                controlPoints = new[]
                {
                    new Vector2(295, 0),    new Vector2(221, 50),   new Vector2(170, 84),
                    new Vector2(138, 118),  new Vector2(93, 136),   new Vector2(46, 144),
                    new Vector2(0, 178),    new Vector2(-70, 222),  new Vector2(-152, 224),
                    new Vector2(-212, 180), new Vector2(-243, 119), new Vector2(-241, 55),
                    new Vector2(-204, 0),   new Vector2(-177, -40), new Vector2(-195, -96),
                    new Vector2(-198, -169),new Vector2(-138, -204),new Vector2(-57, -181),
                    new Vector2(0, -154),   new Vector2(48, -152),  new Vector2(107, -157),
                    new Vector2(180, -153), new Vector2(268, -131), new Vector2(323, -74),
                },
                // The reason the circuit is called a pass. 28 m from the valley
                // floor to the summit at the north end, down the far side into
                // a second, lower saddle, and a long descent home. Peak grade
                // 9.8% — a real mountain road, and the steepest thing in the
                // game.
                controlHeights = new[]
                {
                    0f,    5f,    10.5f, 15f,   19f,   22.5f, 26f,   28.5f,
                    27f,   23f,   18f,   13f,   9f,    10f,   15f,   18.5f,
                    17f,   12.5f, 8f,    5.5f,  6.5f,  6.5f,  4.5f,  1.5f,
                },
                // The viaduct over the gorge on the second saddle, 140 m of it
                // with the floor 14 m down. The road is around 18 m up here, so
                // what you are looking at over the parapet is a long way.
                bridges = new[] { new Vector2(920f, 1060f) },
                bridgeDepth = 14f,
            },
            new TrackDef
            {
                id = "AirfieldSprint",
                name = "AIRFIELD SPRINT",
                blurb = "Two long straights joined by hairpins. Gearing decides this one.",
                roadWidth = 14f,
                laps = 2,
                speedLimitKmh = 72f,    // perimeter road round an airfield
                controlPoints = new[]
                {
                    new Vector2(331, 0),   new Vector2(276, 62),  new Vector2(170, 88),
                    new Vector2(90, 96),   new Vector2(30, 106),  new Vector2(-30, 106),
                    new Vector2(-90, 96),  new Vector2(-170, 88), new Vector2(-276, 62),
                    new Vector2(-331, 0),  new Vector2(-276, -62),new Vector2(-170, -88),
                    new Vector2(-90, -96), new Vector2(-30, -106),new Vector2(30, -106),
                    new Vector2(90, -96),  new Vector2(170, -88), new Vector2(276, -62),
                },
                // An airfield is flat, and this one stays flat — 2 m of drainage
                // camber across the whole site, 1.3% at its worst. It is the
                // circuit that proves gearing rather than gradient decides a
                // race, and putting a hill on it would take that away.
                controlHeights = new[]
                {
                    0f,   0.8f,  1.6f,  2.0f,  1.6f,  1.6f,  2.0f,  1.6f,  0.8f,
                    0f,  -0.8f, -1.6f, -2.0f, -1.6f, -1.6f, -2.0f, -1.6f, -0.8f,
                },
            },

            // The strips. Wide enough for four lanes because the grid is four
            // cars; a real strip is two, and the LifeSim can send one opponent
            // when it wants a proper heads-up run.
            new TrackDef
            {
                id = "DragQuarter",
                name = "DRAG STRIP — 1/4 MILE",
                blurb = "402 m in a straight line. Gearing, launch, and nothing else.",
                roadWidth = 18f,
                laps = 1,
                speedLimitKmh = 0f,     // a strip: the tree IS the start
                drag = true,
                dragMeters = 402.336f,
                dragLabel = "1/4 MILE",
            },
            new TrackDef
            {
                id = "DragEighth",
                name = "DRAG STRIP — 1/8 MILE",
                blurb = "201 m. Over before a long gearbox has finished thinking.",
                roadWidth = 18f,
                laps = 1,
                speedLimitKmh = 0f,     // a strip: the tree IS the start
                drag = true,
                dragMeters = 201.168f,
                dragShutdown = 260f,
                dragLabel = "1/8 MILE",
            },
            // AFTER every circuit, deliberately: every scene index before it
            // holds. The city is not in the race picker (StepTrack skips it) —
            // FREE ROAM on the MAIN tab is its door.
            new TrackDef
            {
                id = "Charlotte",
                name = "CHARLOTTE",
                blurb = "The whole city at 1:1 — uptown to the 485 belt. Free roam.",
                roadWidth = 12f,
                laps = 1,
                speedLimitKmh = 45f,    // documentary: CityMode does not read it
                city = true,
            },
            // Appended after the city so every existing save's track index
            // still points where it always did. The garage index is a formula,
            // so it moves over by itself.
            new TrackDef
            {
                id = "BlueRidge",
                name = "BLUE RIDGE PARKWAY",
                blurb = "Grandfather Mountain at 1:1 — down from Rough Ridge, out over the " +
                        "Linn Cove Viaduct. Map (c) OpenStreetMap contributors.",
                roadWidth = 9.5f,
                laps = 1,
                speedLimitKmh = 72f,    // the Parkway's posted maximum, 45 mph
                stage = true,
                stageData = "brp_stage",
                dragLabel = "LINN COVE",
                bridgeDepth = 6f,   // reused as the audit's minimum daylight under a deck
            },

            // ----------------------------------------------------------------
            //  Bogue Banks — the Crystal Coast. Three venues off one barrier
            //  island, all real road, all baked by tools/bogue/fetch_bogue.mjs.
            //
            //  Appended after Blue Ridge for the same reason Blue Ridge was
            //  appended after the city: every existing save's track index still
            //  points where it always did, and GarageSceneIndex is a formula so
            //  it moves along by itself.
            // ----------------------------------------------------------------
            new TrackDef
            {
                id = "EmeraldIsle",
                name = "EMERALD ISLE — 1/4 MILE",
                blurb = "A real quarter mile on Emerald Drive, ocean one side, sound the other. " +
                        "Map (c) OpenStreetMap contributors.",
                roadWidth = 11f,
                laps = 1,
                speedLimitKmh = 72f,    // NC 58 through the island, 45 mph
                stage = true,
                dragEvent = true,
                stageData = "bogue_emerald",
                dragLabel = "1/4 MILE",
            },
            // The two bridges. Both cross the Atlantic Intracoastal Waterway,
            // which mandates 65 ft of clearance — so both are high-rises, and
            // the grade is the whole event. Trap speeds here are NOT comparable
            // with the flat strips and are not meant to be: a car that runs
            // 12s on tarmac will not run 12s up a 5% ramp, and finding out by
            // how much is the reason to come here.
            new TrackDef
            {
                id = "LangstonBridge",
                name = "LANGSTON BRIDGE",
                blurb = "1.4 km over Bogue Sound. Climb at 4.5%, crest, and run it out downhill. " +
                        "Map (c) OpenStreetMap contributors.",
                roadWidth = 11f,
                laps = 1,
                speedLimitKmh = 89f,    // a 55 mph high-rise
                stage = true,
                dragEvent = true,
                stageData = "bogue_langston",
                dragLabel = "THE SPAN",
                // Not a gorge: the ground under this deck is the SOUND, and it
                // only has to drop far enough to stay under the water plane the
                // bake fixed at y=6. Nine metres of dry canyon under a bridge
                // over water would be a hole in the sea.
                bridgeDepth = 4f,
            },
            new TrackDef
            {
                id = "AtlanticBeachBridge",
                name = "ATLANTIC BEACH BRIDGE",
                blurb = "1.3 km, four lanes, steeper than Langston. Morehead City to the island. " +
                        "Map (c) OpenStreetMap contributors.",
                roadWidth = 13f,
                laps = 1,
                speedLimitKmh = 72f,    // 45 mph across the causeway
                stage = true,
                dragEvent = true,
                stageData = "bogue_atlantic",
                dragLabel = "THE SPAN",
                bridgeDepth = 4f,
            },

            // ----------------------------------------------------------------
            //  Mount Mitchell — NC 128, straight off the Parkway and straight
            //  up. Appended after the coast for the same reason everything is
            //  appended: a saved career stores its venue by index.
            //
            //  THE ALTITUDE IS REAL AND IT IS THE POINT. The bake reads the
            //  same SRTM the Parkway does, and its endpoints were researched
            //  and independently fact-checked against USGS 3DEP lidar: 1,573 m
            //  at the Parkway junction, 2,004.7 m at the summit car park, a
            //  432 m climb in 7.5 km with nothing but up in between. The route
            //  came out of the bake at 1,574..2,003 m with a peak grade of
            //  9.2%, against a published maximum of 9.4% — so the mountain in
            //  the game is the mountain.
            // ----------------------------------------------------------------
            new TrackDef
            {
                id = "MtMitchell",
                name = "MOUNT MITCHELL — NC 128",
                blurb = "Off the Parkway at Ridge Junction and 432 m up in seven and a half " +
                        "kilometres, finishing at 2,003 m — the highest road in the eastern " +
                        "United States. Map (c) OpenStreetMap contributors.",
                // Nine metres, not the 7.5 the first cut guessed. NC 128 is a
                // two-lane park road — two 3.5 m lanes and a shoulder is about
                // nine — and the number is also load-bearing: the 1v1 restage
                // check asks whether a car can sit 5.2 m off the rival and
                // still be on tarmac, which a 7.5 m road cannot answer yes to.
                roadWidth = 9f,
                laps = 1,
                speedLimitKmh = 56f,    // a 35 mph park road
                stage = true,
                stageData = "mtm_stage",
                dragLabel = "THE SUMMIT",
            },

            // ----------------------------------------------------------------
            //  Beech Gap — NC 215, under the Parkway and down the other side.
            //
            //  1,623 m where it passes beneath the Parkway bridge and 887 m
            //  eleven kilometres later above Balsam Grove: 736 m of descent,
            //  the biggest drop of the three, on the narrowest road of the
            //  three. Both anchors landed on the matched road and the bake
            //  agreed with the survey to 3 m.
            // ----------------------------------------------------------------
            new TrackDef
            {
                id = "BeechGap",
                name = "BEECH GAP — NC 215",
                blurb = "From under the Parkway bridge at 1,623 m down to Balsam Grove: " +
                        "736 m of descent in eleven kilometres, and no straight long " +
                        "enough to rest on. Map (c) OpenStreetMap contributors.",
                roadWidth = 8.5f,
                laps = 1,
                speedLimitKmh = 72f,    // NC 215, 45 mph
                stage = true,
                stageData = "beech_stage",
                dragLabel = "THE GAP",
            },

            // ----------------------------------------------------------------
            //  Charlotte. Three real-road venues on the same streets FREE ROAM
            //  drives, baked by tools/clt/fetch_clt.mjs from the way ids the
            //  investigation verified against live OpenStreetMap, with SRTM
            //  under them — the first step toward the city itself being the
            //  race venue. Appended after Beech Gap because a saved career
            //  stores its venue by index.
            //
            //  Every one carries its posted limit in km/h (the bake's JSON
            //  says mph as posted, for the record), and the two that run on
            //  freeway carriageways are ONE-WAY: dashes only, no double
            //  yellow. Those two are not offered backwards either — "UPTOWN
            //  LOOP II" would drive the 277 belt against traffic, taking every
            //  ramp the wrong way; Tryon is a two-way street and its reverse
            //  is a real southbound drive.
            // ----------------------------------------------------------------
            new TrackDef
            {
                id = "UptownLoop",
                name = "UPTOWN LOOP — I-277",
                blurb = "The 277 belt round uptown Charlotte at 1:1 — Brookshire, the Belk, " +
                        "and the I-77 straight. 9.2 km, one lap, no pumps. " +
                        "Map (c) OpenStreetMap contributors.",
                roadWidth = 14.6f,      // three 3.66 m lanes and paved shoulders
                // ONE lap, and it must stay one: the thirstiest stage-4 car in
                // the catalog reaches the self-test's 85% tank at 14.2 km at
                // race load, and two laps of this is 18.4. The self-test's
                // tank check is what pins it.
                laps = 1,
                speedLimitKmh = 80f,    // I-277 is posted 50 mph
                // Raced ON THE CITY: the same streamed streets, buildings and
                // terrain as FREE ROAM, with the route as a chain of the
                // city graph's own edges (tools/city/export_osm.mjs).
                city = true,
                cityRoute = "uptown",
                loop = true,
                oneWay = true,
                noReverse = true,       // a freeway backwards is the wrong way up every ramp
                dragLabel = "THE 277",
            },
            new TrackDef
            {
                id = "TryonSprint",
                name = "TRYON STREET SPRINT",
                blurb = "South End to NoDa straight up Tryon — Camden Road, through the towers, " +
                        "out past 36th. Map (c) OpenStreetMap contributors.",
                roadWidth = 15.5f,      // two lanes a side plus parking, uptown's own width
                laps = 1,
                speedLimitKmh = 56f,    // 35 mph north of the core (25 through it)
                city = true,
                cityRoute = "tryon",
                dragLabel = "NODA",
            },
            new TrackDef
            {
                id = "IndependenceSprint",
                name = "INDEPENDENCE SPRINT — US 74",
                blurb = "Off the Belk and east down the Independence Expressway to Sharon Amity. " +
                        "Map (c) OpenStreetMap contributors.",
                roadWidth = 16f,        // three lanes and a full shoulder each way
                laps = 1,
                speedLimitKmh = 89f,    // 55 mph on the motorway section
                city = true,
                cityRoute = "independence",
                oneWay = true,
                noReverse = true,       // an eastbound expressway has no westbound
                dragLabel = "US 74",
            },

            // ----------------------------------------------------------------
            //  Two Parkway LOOPS — "more mountain races, but circuits": a
            //  section of the Blue Ridge Parkway and the roads that meet it,
            //  closed into a ring, baked by tools/roads/fetch_loop.mjs from
            //  OpenStreetMap + SRTM. Both pass under their own Parkway bridge
            //  once (the bake lifts the Parkway over the road it crosses and
            //  the builder keeps the lower road's corridor), and Little
            //  Switzerland runs through the Parkway's tunnel. Appended after
            //  Charlotte because a saved career stores its venue by index —
            //  and the twins move again, which save v12 remaps.
            // ----------------------------------------------------------------
            new TrackDef
            {
                id = "BlowingRock",
                name = "BLOWING ROCK — MOSES CONE LOOP",
                blurb = "The Parkway west from the US 321 interchange to Moses Cone, down " +
                        "Cone Road to US 221, Main Street through the village, and back " +
                        "under the Parkway's own bridge. 9.9 km. Map (c) OpenStreetMap contributors.",
                roadWidth = 10f,
                laps = 1,
                speedLimitKmh = 56f,    // 35 mph through the village; the Parkway is 45
                stage = true,
                loop = true,
                stageData = "brock_stage",
                dragLabel = "MOSES CONE",
                bridgeDepth = 6f,
            },
            new TrackDef
            {
                id = "LittleSwitzerland",
                name = "LITTLE SWITZERLAND — GILLESPIE GAP",
                blurb = "The Parkway west from Gillespie Gap through the Little Switzerland " +
                        "Tunnel, the village links onto NC 226A, and NC 226A back along the " +
                        "ridge under the Parkway bridge. 9.7 km. Map (c) OpenStreetMap contributors.",
                roadWidth = 9.5f,
                laps = 1,
                speedLimitKmh = 56f,    // NC 226A is posted 35 mph
                stage = true,
                loop = true,
                stageData = "swiss_stage",
                dragLabel = "THE TUNNEL",
                bridgeDepth = 6f,
            },
        };

        /// <summary>
        /// A reverse twin: the same venue driven backwards, named the way Gran
        /// Turismo named them.
        ///
        /// A SHALLOW copy on purpose. Every number that describes the place —
        /// the control points, the heights, the bridge spans, the road width,
        /// the stage bake — describes the same place, and a reverse carrying
        /// its own copies would be a second set of numbers to keep in step with
        /// the first. Only the identity changes.
        /// </summary>
        static TrackDef ReverseTwin(TrackDef f) => new TrackDef
        {
            id = f.id + "Rev",
            name = f.name + " II",
            blurb = "The same road, the other way round. " + f.blurb,
            controlPoints = f.controlPoints,
            controlHeights = f.controlHeights,
            bridges = f.bridges,
            tunnels = f.tunnels,
            crossings = f.crossings,
            bridgeDepth = f.bridgeDepth,
            roadWidth = f.roadWidth,
            // The one field that is NOT about the place's shape and still has
            // to be copied: left out, every twin silently rolls in at the 45
            // default while its forward venue does 72. The self-test asserts
            // the pair agree.
            speedLimitKmh = f.speedLimitKmh,
            // Same reason: a loop stage's twin must race by laps too, and a
            // one-way road is one-way in both directions of the picker.
            loop = f.loop,
            oneWay = f.oneWay,
            laps = f.laps,
            drag = f.drag,
            dragMeters = f.dragMeters,
            dragShutdown = f.dragShutdown,
            dragLabel = f.dragLabel,
            dragEvent = f.dragEvent,
            city = f.city,
            cityRoute = f.cityRoute,
            stage = f.stage,
            stageData = f.stageData,
            reverseOf = f.id,
        };

        /// <summary>
        /// Every venue the game offers, forward twins first.
        ///
        /// THE REVERSES GO ON THE END, and that ordering is load-bearing for
        /// the same reason the garage went after the circuits: a saved career
        /// stores its track by INDEX, so anything inserted before an existing
        /// entry silently sends every recorded race to a different venue.
        /// Appending costs nothing and cannot.
        /// </summary>
        public static readonly TrackDef[] All = BuildAll();

        static TrackDef[] BuildAll()
        {
            var list = new List<TrackDef>(Authored);
            foreach (var f in Authored) if (f.CanReverse) list.Add(ReverseTwin(f));
            return list.ToArray();
        }

        /// <summary>How many venues have a SCENE. The reverses do not — they
        /// race in their twin's — so this, not All.Length, is what the scene
        /// list and every index past it are built from.</summary>
        public static int SceneCount => Authored.Length;

        /// <summary>The venues that HAVE a scene of their own — everything the
        /// builder builds and every audit walks. A reverse twin is deliberately
        /// NOT here: it races in its forward venue s scene, so a pass that
        /// iterated All would go looking for CityCircuitRev.unity and fail on a
        /// file nothing was ever supposed to write.</summary>
        public static TrackDef[] Scened => Authored;

        public static int Count => All.Length;

        public static TrackDef At(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)];

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return 0;
        }

        /// <summary>
        /// How many venues the authored list held when saves were still
        /// version 10 — thirteen, Sunset City GP through Beech Gap — and so
        /// where the reverse twins began in every save written before the
        /// three Charlotte venues were appended (2026-09-07). A constant on
        /// purpose: the migration must describe the OLD layout, and the old
        /// layout does not change when the catalog grows again.
        /// </summary>
        public const int V10AuthoredCount = 13;

        /// <summary>
        /// A venue index from a version-10 save, in today's list.
        ///
        /// Appending to the authored list is the rule because a save stores
        /// its venue by index — but the twins sit AFTER the authored list, so
        /// an append moves every twin along by the number of venues added.
        /// The twins keep their order (generated in authored order, and new
        /// venues go on the end, so their twins do too), which makes the map
        /// one line: the k-th twin is still the k-th twin, counted from where
        /// the twins start NOW. Authored indices are untouched; anything past
        /// the old list is clamped like <see cref="At"/> would clamp it.
        /// </summary>
        public static int RemapV10Index(int oldIndex)
        {
            if (oldIndex < V10AuthoredCount) return Mathf.Max(0, oldIndex);
            int twin = oldIndex - V10AuthoredCount;
            return Mathf.Clamp(SceneCount + twin, 0, All.Length - 1);
        }

        /// <summary>How many venues the authored list held under save v11:
        /// the thirteen of v10 plus the three Charlotte venues. The two
        /// Parkway loops (2026-09-11) went on after them and moved every twin
        /// two places along. A constant, for the reason V10AuthoredCount is.</summary>
        public const int V11AuthoredCount = 16;

        /// <summary>A venue index from a version-11 save, in today's list.
        /// Same shape as <see cref="RemapV10Index"/>: authored indices stand,
        /// the k-th twin is still the k-th twin.</summary>
        public static int RemapV11Index(int oldIndex)
        {
            if (oldIndex < V11AuthoredCount) return Mathf.Max(0, oldIndex);
            int twin = oldIndex - V11AuthoredCount;
            return Mathf.Clamp(SceneCount + twin, 0, All.Length - 1);
        }

        /// <summary>Build-settings index of a track's scene. Scene 0 is
        /// LifeHome, so the tracks start at 1.</summary>
        public static int SceneIndex(int trackIndex)
        {
            int i = Mathf.Clamp(trackIndex, 0, All.Length - 1);
            // A reverse races in its twin's scene. Resolved by id rather than
            // by arithmetic on the index, so the two lists can never drift.
            var def = All[i];
            if (def.Reversed) i = IndexOf(def.reverseOf);
            return 1 + Mathf.Clamp(i, 0, SceneCount - 1);
        }

        /// <summary>
        /// Build-settings index of the walk-in garage.
        ///
        /// LAST, after every circuit, and that is the whole reason it is
        /// expressed as a formula rather than as a number: the track scenes are
        /// addressed by their position in this list, so a scene inserted
        /// anywhere before them would send every race to the wrong circuit.
        /// Adding one at the end costs nothing.
        /// </summary>
        public static int GarageSceneIndex => 1 + SceneCount;

        /// <summary>The pizza shop the delivery shift starts in. Appended after
        /// the garage for the same reason the garage went after the circuits:
        /// every index below it is addressed by position, so a new scene can
        /// only ever go on the END.</summary>
        public static int PizzeriaSceneIndex => 2 + SceneCount;

        /// <summary>
        /// The drivable town: your street, the forecourt, the shop, the
        /// dealership lot and the salvage yard, all in one small map with the
        /// home lot at the south end of it.
        ///
        /// NOT a TrackDef, deliberately. Entering it in <see cref="All"/> would
        /// buy the free-roam behaviours (skipped by the race picker, skipped by
        /// delivery routing) at the cost of every circuit assertion having to
        /// be short-circuited for it — and of <see cref="Thumbnail"/>, which
        /// loads "charlotte_thumb" by name and would draw Charlotte's street
        /// map in the town's venue block. A scene on the end of the list needs
        /// none of that, and the town is never a race venue.
        /// </summary>
        public static int TownSceneIndex => 3 + SceneCount;

        /// <summary>The seller's driveway: a stranger's house with a car for
        /// sale on it. One baked scene, dressed at runtime per listing — see
        /// SellerLotWorld.</summary>
        public static int SellerLotSceneIndex => 4 + SceneCount;

        /// <summary>
        /// YOUR STREET: the player's house, its garage and its neighbours.
        ///
        /// Its own map since the owner asked for it — "the player's
        /// house/neighborhood should be its own map. Driving to the end of road
        /// gives option to go into town or go race." The house used to be a
        /// corner of the town, which meant the game had one house you walked
        /// around in the front end and a different one you drove past.
        ///
        /// On the END of the list, like every scene added since the circuits,
        /// because everything below is addressed by position and a save stores
        /// its venue by index.
        /// </summary>
        public static int NeighborhoodSceneIndex => 5 + SceneCount;

        // ------------------------------------------------------------------
        //  Stage bake loading
        // ------------------------------------------------------------------
        [System.Serializable]
        class StageJson
        {
            public string name = "", attribution = "";
            public float spacing = 4f, startLineM, finishM, baseM;
            /// <summary>World Y of the sea surface, on a stage that has one.
            /// Zero means "no water on this stage" — which is what every
            /// inland bake leaves it as, and what the mountain is.</summary>
            public float waterY;
            /// <summary>The bake is a RING (tools/clt/fetch_clt.mjs writes it
            /// for the 277 belt). Confirms the catalog's TrackDef.loop; a
            /// bake that says loop under a row that does not is caught by
            /// EnsureStage rather than by a finish line nobody crosses.</summary>
            public bool loop;
            /// <summary>The road width the bake was cut for, or 0 when the
            /// bake predates the field. The self-test holds the catalog to it.</summary>
            public float roadWidthM;
            /// <summary>Registration into charlotte_city.json's frame: where
            /// this stage's origin sits in city metres. False/zero on every
            /// bake outside Charlotte.</summary>
            public bool inCity;
            public float cityX0, cityZ0;
            public float[] xyz;       // interleaved x,y,z per waypoint
            public float[] bridges;   // interleaved fromM,toM per span
            public float[] tunnels;   // interleaved fromM,toM per bore
            public float[] crossings; // interleaved upperM,lowerM per self-crossing
        }

        static Vector2[] Pairs(float[] flat)
        {
            if (flat == null || flat.Length < 2) return null;
            var out2 = new Vector2[flat.Length / 2];
            for (int i = 0; i < out2.Length; i++) out2[i] = new Vector2(flat[i * 2], flat[i * 2 + 1]);
            return out2;
        }

        /// <summary>Is a point <paramref name="metres"/> along the lap inside
        /// a tunnel? Loop-aware the way BridgeBlend is: a bore may wrap past
        /// the start line in the bake's table.</summary>
        public static bool InTunnel(TrackDef def, float metres)
        {
            if (def == null || def.tunnels == null) return false;
            float lap = Mathf.Max(def.LengthM, 1f);
            foreach (var span in def.tunnels)
            {
                float from, len;
                if (def.stage && !def.loop) { len = span.y - span.x; from = metres - span.x; }
                else { from = Mathf.Repeat(metres - span.x, lap); len = Mathf.Repeat(span.y - span.x, lap); }
                if (from >= 0f && from <= len) return true;
            }
            return false;
        }

        /// <summary>
        /// Pull a stage's baked centreline out of Resources, once. The bake is
        /// already an arc-length resample at <see cref="Spacing"/> of a spline
        /// through the real road's map geometry, with elevation sampled from
        /// the real terrain — so unlike a circuit there is no spline maths
        /// here, just the list.
        /// </summary>
        public static void EnsureStage(TrackDef def)
        {
            if (!def.stage || def.stagePts != null) return;
            var ta = Resources.Load<TextAsset>(def.stageData);
            if (ta == null)
            {
                // Fail LOUD but not fatal: a missing bake becomes a token
                // straight road, which no audit will mistake for the parkway.
                Debug.LogError("Stage bake missing from Resources: " + def.stageData);
                def.stagePts = new[] { Vector3.zero, new Vector3(Spacing, 0f, 0f) };
                def.dragMeters = Spacing;
                return;
            }
            var j = JsonUtility.FromJson<StageJson>(ta.text);
            int n = j.xyz.Length / 3;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
                pts[i] = new Vector3(j.xyz[i * 3], j.xyz[i * 3 + 1], j.xyz[i * 3 + 2]);
            def.stagePts = pts;
            def.stageStartLineM = j.startLineM;
            def.stageAttribution = j.attribution ?? "";
            def.stageWaterY = j.waterY;
            // The bake's word on the shape wins over silence, never over the
            // catalog: a ring that the row calls point-to-point would race to
            // a finish at metre zero, so say so loudly and run it as the lap
            // it is.
            if (j.loop && !def.loop)
            {
                Debug.LogError("Stage bake " + def.stageData + " is a loop but TrackDef." + def.id +
                               " is not — set loop = true in the catalog");
                def.loop = true;
            }
            def.stageRoadWidthM = j.roadWidthM;
            def.stageInCity = j.inCity;
            def.stageCityOrigin = new Vector2(j.cityX0, j.cityZ0);
            // finishM is measured from waypoint 0, and so is dragMeters — the
            // lead-in is inside both, which is what FinishIndex assumes.
            def.dragMeters = j.finishM;
            if (j.bridges != null && j.bridges.Length >= 2)
            {
                var spans = new Vector2[j.bridges.Length / 2];
                for (int i = 0; i < spans.Length; i++)
                    spans[i] = new Vector2(j.bridges[i * 2], j.bridges[i * 2 + 1]);
                def.bridges = spans;
            }
            def.tunnels = Pairs(j.tunnels);
            def.crossings = Pairs(j.crossings);
        }

        [System.Serializable]
        class RoutesJson
        {
            public string attribution = "";
            public RouteJson[] routes;
        }
        [System.Serializable]
        class RouteJson
        {
            public string id = "", name = "";
            public int loop, oneway;
            public float roadWidth, speed, lengthM, startM, finishM;
            public float[] pts;   // interleaved x,z every ~20 m along the route
        }
        static RoutesJson routesJson;

        /// <summary>
        /// Pull a city route's menu-side facts out of Resources, once: length,
        /// start line, finish and a polyline for the map. The small JSON the
        /// exporter writes beside the graph — the front end must never have
        /// to parse the 2.5 MB city blob to quote a race distance.
        /// </summary>
        public static void EnsureRoute(TrackDef def)
        {
            if (!def.IsCityRace || def.routeLoaded) return;
            def.routeLoaded = true;
            if (routesJson == null)
            {
                var ta = Resources.Load<TextAsset>("charlotte_routes");
                if (ta == null)
                {
                    Debug.LogError("charlotte_routes.json missing from Resources - run tools/city/export_osm.mjs");
                    routesJson = new RoutesJson { routes = new RouteJson[0] };
                }
                else routesJson = JsonUtility.FromJson<RoutesJson>(ta.text);
            }
            RouteJson r = null;
            foreach (var cand in routesJson.routes) if (cand.id == def.cityRoute) { r = cand; break; }
            if (r == null)
            {
                Debug.LogError("City route missing from charlotte_routes.json: " + def.cityRoute);
                def.routeLengthM = Spacing; def.routeStartM = 0f; def.routeFinishM = Spacing;
                def.routePts = new[] { Vector3.zero, new Vector3(Spacing, 0f, 0f) };
                return;
            }
            def.routeLengthM = r.lengthM;
            def.routeStartM = r.startM;
            def.routeFinishM = r.finishM;
            // The reversed-finish remap reads the start line off this field
            // for stages; a city route with ends is the same shape.
            def.stageStartLineM = r.startM;
            def.stageAttribution = routesJson.attribution ?? "";
            if (r.loop != 0 && !def.loop)
            {
                Debug.LogError("City route " + r.id + " is a loop but TrackDef." + def.id + " is not");
                def.loop = true;
            }

            // Resample the coarse polyline at Spacing so every index-based
            // consumer (the map's finish marker, the reversed start) can treat
            // it exactly like a stage's waypoint list. A loop starts at the
            // line, like the race scene's own path.
            int n = r.pts.Length / 2;
            var raw = new List<Vector2>(n + 1);
            for (int i = 0; i < n; i++) raw.Add(new Vector2(r.pts[i * 2], r.pts[i * 2 + 1]));
            if (r.loop != 0 && raw.Count > 1) raw.Add(raw[0]);
            var arcs = new List<float>(raw.Count) { 0f };
            for (int i = 1; i < raw.Count; i++) arcs.Add(arcs[i - 1] + Vector2.Distance(raw[i - 1], raw[i]));
            float total = arcs[arcs.Count - 1];
            int count = r.loop != 0 ? Mathf.Max(2, Mathf.RoundToInt(total / Spacing))
                                    : Mathf.Max(2, Mathf.FloorToInt(total / Spacing) + 1);
            float step = r.loop != 0 ? total / count : Spacing;   // a whole number of stations round a loop
            var pts = new Vector3[count];
            int seg = 0;
            for (int i = 0; i < count; i++)
            {
                float s = r.loop != 0 ? Mathf.Repeat(r.startM + i * step, total) : Mathf.Min(i * Spacing, total);
                if (s < arcs[seg]) seg = 0;
                while (seg < arcs.Count - 2 && arcs[seg + 1] < s) seg++;
                float segL = arcs[seg + 1] - arcs[seg];
                float t = segL > 1e-5f ? Mathf.Clamp01((s - arcs[seg]) / segL) : 0f;
                var p = Vector2.LerpUnclamped(raw[seg], raw[seg + 1], t);
                pts[i] = new Vector3(p.x, 0f, p.y);
            }
            def.routePts = pts;
        }

        /// <summary>
        /// Dense Catmull-Rom through the control points, then an arc-length
        /// resample at <paramref name="spacing"/> metres. This IS the track: the
        /// road ribbon, the walls, the AI racing line and the length the menu
        /// quotes are all derived from this one list.
        /// </summary>
        public static List<Vector3> Sample(TrackDef def, float spacing)
        {
            // A city has no centreline. Nothing should ask for one; a caller
            // that does gets a token stub rather than a NullReference deep in
            // spline maths it has no business reaching.
            if (def.IsRoam)
                return new List<Vector3> { Vector3.zero, new Vector3(spacing, 0f, 0f) };
            // A city race's line is the route polyline the menu carries,
            // already at Spacing (EnsureRoute resamples it). Heights are zero
            // here: the map only needs the plan, and the race scene builds its
            // real path from the graph (CityMode.BuildPath).
            if (def.IsCityRace)
            {
                EnsureRoute(def);
                return new List<Vector3>(def.routePts);
            }

            // A stage is pre-sampled at Spacing by its bake. Callers get a
            // copy — waypoint lists get handed around and this one is shared.
            if (def.stage)
            {
                EnsureStage(def);
                if (Mathf.Abs(spacing - Spacing) < 0.01f)
                    return new List<Vector3>(def.stagePts);
                // Nothing asks for a different spacing today; if something
                // does, resample linearly rather than lie about the pitch.
                var resampled = new List<Vector3>();
                float run = 0f;
                resampled.Add(def.stagePts[0]);
                for (int i = 1; i < def.stagePts.Length; i++)
                {
                    float d = Vector3.Distance(def.stagePts[i - 1], def.stagePts[i]);
                    run += d;
                    while (run >= spacing)
                    {
                        float over = run - spacing;
                        resampled.Add(Vector3.Lerp(def.stagePts[i], def.stagePts[i - 1],
                            over / Mathf.Max(d, 0.0001f)));
                        run = over;
                    }
                }
                return resampled;
            }

            // A strip is not a spline. Waypoint 0 IS the start line — the cars
            // stage on it rather than rolling up to it — and the list runs
            // forward to the traps and on through the shutdown area.
            if (def.drag)
            {
                var strip = new List<Vector3>();
                float total = def.dragMeters + def.dragShutdown;
                for (float d = 0f; d <= total + 0.001f; d += spacing)
                    strip.Add(new Vector3(d, 0f, 0f));
                return strip;
            }

            var cps = def.controlPoints;
            int cpCount = cps.Length;
            // The height spline is optional and is checked for LENGTH, not just
            // for null: a heights array that has drifted out of step with the
            // control points would otherwise put a crest on the wrong corner,
            // and every symptom of that is "the track feels wrong" rather than
            // an error.
            var hs = def.controlHeights != null && def.controlHeights.Length == cpCount
                ? def.controlHeights : null;
            var dense = new List<Vector3>(cpCount * 40);
            for (int i = 0; i < cpCount; i++)
            {
                Vector2 p0 = cps[(i - 1 + cpCount) % cpCount];
                Vector2 p1 = cps[i];
                Vector2 p2 = cps[(i + 1) % cpCount];
                Vector2 p3 = cps[(i + 2) % cpCount];
                float h0 = 0f, h1 = 0f, h2 = 0f, h3 = 0f;
                if (hs != null)
                {
                    h0 = hs[(i - 1 + cpCount) % cpCount]; h1 = hs[i];
                    h2 = hs[(i + 1) % cpCount]; h3 = hs[(i + 2) % cpCount];
                }
                for (int s = 0; s < 40; s++)
                {
                    float t = s / 40f;
                    Vector2 pt = 0.5f * ((2f * p1) + (-p0 + p2) * t
                        + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                    float y = hs == null ? 0f
                        : 0.5f * ((2f * h1) + (-h0 + h2) * t
                            + (2f * h0 - 5f * h1 + 4f * h2 - h3) * t * t
                            + (-h0 + 3f * h1 - 3f * h2 + h3) * t * t * t);
                    dense.Add(new Vector3(pt.x, y, pt.y));
                }
            }

            var pts = new List<Vector3>();
            float acc = 0f;
            pts.Add(dense[0]);
            for (int i = 1; i <= dense.Count; i++)
            {
                Vector3 prev = dense[i - 1];
                Vector3 cur = dense[i % dense.Count];
                float d = Vector3.Distance(prev, cur);
                acc += d;
                while (acc >= spacing)
                {
                    float overshoot = acc - spacing;
                    pts.Add(Vector3.Lerp(cur, prev, overshoot / Mathf.Max(d, 0.0001f)));
                    acc = overshoot;
                }
            }
            // Drop the last point if it landed on top of the first
            if (Vector3.Distance(pts[pts.Count - 1], pts[0]) < spacing * 0.5f)
                pts.RemoveAt(pts.Count - 1);
            return pts;
        }

        /// <summary>
        /// How much of a bridge there is at <paramref name="metres"/> along the
        /// lap: 0 on solid ground, 1 out over the middle of a span, and a
        /// cosine ramp in between.
        ///
        /// The ramp is the whole reason this is a fraction rather than a bool.
        /// A hard edge would drop the ground <see cref="TrackDef.bridgeDepth"/>
        /// metres between two adjacent waypoints — a cliff face across the road
        /// at the abutment, which is where the deck is supposed to meet solid
        /// ground. Ramping over <see cref="BridgeRampM"/> gives the gorge sloped
        /// ends and the deck something to land on.
        /// </summary>
        public const float BridgeRampM = 26f;

        public static float BridgeBlend(TrackDef def, float metres)
        {
            if (def == null || def.bridges == null || def.bridges.Length == 0) return 0f;
            float lap = Mathf.Max(def.LengthM, 1f);
            float best = 0f;
            foreach (var span in def.bridges)
            {
                float from, len;
                if (def.stage && !def.loop)
                {
                    // A stage has ENDS — a span near the finish must not bleed
                    // through the modulo onto the start of the run. (A LOOP
                    // stage takes the lap branch below: its bake writes a
                    // span that crosses the start line past the lap length,
                    // exactly as a circuit's table would.)
                    len = span.y - span.x;
                    from = metres - span.x;
                    if (from < 0f || from > len) continue;
                }
                else
                {
                    // Measured on the LAP, so a span may legitimately wrap past
                    // the start line. Both the distance forward from the start
                    // of the span and back from its end are taken modulo the lap.
                    from = Mathf.Repeat(metres - span.x, lap);
                    len = Mathf.Repeat(span.y - span.x, lap);
                    if (from > len) continue;                // outside this span
                }
                float into = Mathf.Min(from, len - from);    // metres from the nearer end
                float t = Mathf.Clamp01(into / BridgeRampM);
                // Cosine rather than linear: a linear ramp leaves a crease in
                // the ground at both ends of it, which on a hillside reads as a
                // modelling seam rather than as a valley.
                best = Mathf.Max(best, 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
            }
            return best;
        }

        /// <summary>Centre of a circuit's bounding box, which is where the
        /// ground plane and the sky have to be pinned.</summary>
        public static Bounds BoundsOf(TrackDef def)
        {
            var pts = Sample(def, Spacing);
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            return b;
        }

        // ------------------------------------------------------------------
        //  Picker map
        // ------------------------------------------------------------------
        static readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();

        /// <summary>
        /// How a map of a venue maps the WORLD onto its own pixels: one
        /// scale for both axes and an offset per axis, texture origin at the
        /// bottom-left with +Z north up the page. The picker draws the road
        /// with it and the race HUD puts the cars on the same picture with
        /// it — two drawings of one place have to share one projection or
        /// the dot drives beside the line.
        /// </summary>
        public struct MapFrame
        {
            public float scale, ox, oz;
            public int size;
            public float X(float worldX) => worldX * scale + ox;
            public float Y(float worldZ) => worldZ * scale + oz;
        }

        static readonly Dictionary<string, MapFrame> mapFrames = new Dictionary<string, MapFrame>();

        /// <summary>The projection a <paramref name="size"/>-pixel map of
        /// this venue uses. False for the city, which has no centreline to
        /// frame.</summary>
        public static bool MapFrameFor(TrackDef def, int size, out MapFrame f)
        {
            f = default;
            if (def == null || def.IsRoam) return false;
            string key = def.id + "|" + size;
            if (mapFrames.TryGetValue(key, out f)) return true;

            var pts = Sample(def, Spacing);
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            // ONE scale for both axes: a 660 m long circuit should read as long,
            // not be stretched to fill the same square as a compact one.
            float span = Mathf.Max(b.size.x, b.size.z);
            f.size = size;
            f.scale = (size - 12) / Mathf.Max(span, 1f);
            f.ox = size * 0.5f - b.center.x * f.scale;
            f.oz = size * 0.5f - b.center.z * f.scale;
            mapFrames[key] = f;
            return true;
        }

        /// <summary>
        /// A map of the circuit, drawn from the same centreline the road mesh is
        /// built from. Generated rather than authored: four hand-drawn PNGs
        /// would be four things to redraw the moment a corner moves, and the
        /// shape is already in the data.
        ///
        /// Cached per track — a menu rebuild happens on every button press, and
        /// rasterising four circuits per press is not free.
        /// </summary>
        /// <param name="hud">The race HUD's colours: a white road with a dark
        /// halo, readable over any world, instead of the picker's amber.</param>
        public static Texture2D Thumbnail(TrackDef def, int size = 128, bool hud = false)
        {
            // Keyed by size and style: the picker asks for 128, the HUD for
            // whatever a third of the framebuffer is, and the self-test for
            // 96 — one cache slot per id handed the second caller the first
            // caller's picture at the wrong size.
            string cacheKey = def.id + "|" + size + (hud ? "|hud" : "");
            if (thumbs.TryGetValue(cacheKey, out var hit) && hit != null) return hit;

            if (def.IsRoam)
            {
                // Baked at scene-build time from the real road graph — parsing
                // 1.5 MB of city JSON to draw a menu chip would be the wrong
                // trade. Missing asset just means no map on the button.
                var baked = Resources.Load<Texture2D>("charlotte_thumb");
                if (baked != null) thumbs[cacheKey] = baked;
                return baked;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            var pts = Sample(def, Spacing);
            MapFrameFor(def, size, out var frame);
            float scale = frame.scale, ox = frame.ox, oz = frame.oz;

            var line = hud ? new Color32(255, 255, 255, 255) : new Color32(255, 204, 64, 255);
            var halo = hud ? new Color32(0, 0, 0, 170) : new Color32(92, 70, 30, 255);
            // A route with ENDS must not close back onto itself: on a strip the
            // phantom closing segment hides inside the strip, on a 7 km stage
            // it is a chord drawn straight across the map.
            bool ends = def.drag || (def.stage && !def.loop) || (def.IsCityRace && !def.loop);
            int segs = ends ? pts.Count - 1 : pts.Count;
            for (int i = 0; i < segs; i++)
            {
                var a = pts[i];
                var c = pts[(i + 1) % pts.Count];
                // Waypoints are 4 m apart and the map is at most 128 px across,
                // so consecutive points land on the same pixel or its neighbour:
                // a dot per waypoint plus one at each midpoint draws a
                // continuous ribbon without needing a line rasteriser.
                Plot(px, size, a.x * scale + ox, a.z * scale + oz, line, halo);
                Plot(px, size, (a.x + c.x) * 0.5f * scale + ox,
                               (a.z + c.z) * 0.5f * scale + oz, line, halo);
            }
            // WHICH END IS THE START depends on which way round this venue is
            // driven, and the points below are always the FORWARD bake.
            //
            // Sample() hands back the twin's unreversed stage data — a reverse
            // venue has no bake of its own, it borrows its forward twin's —
            // and this method had no def.Reversed branch at all. So on every
            // "II" stage the picker drew the white start marker at the end the
            // race FINISHES and the red finish marker at the end it starts
            // from. This map is the only place in the game that tells a player
            // where a stage begins, and on a twin it was exactly inverted:
            // "when I select any II race, it starts me at the finish line."
            //
            // A LOOP is untouched. Waypoint 0 does not move when the list is
            // turned round — a start line is a band of paint and does not care
            // which way you cross it — so the single dot is right either way.
            var white = new Color32(255, 255, 255, 255);
            var red = new Color32(255, 90, 70, 255);
            bool flip = ends && def.Reversed && def.FinishIndex > 0 && def.FinishIndex < pts.Count;
            var startPt = flip ? pts[def.FinishIndex] : pts[0];
            Plot(px, size, startPt.x * scale + ox, startPt.z * scale + oz, white, white);
            // On a strip the interesting end is the OTHER one: a horizontal bar
            // with one dot on it says nothing about where the traps are. Same
            // for a stage's finish.
            if (ends && def.FinishIndex > 0 && def.FinishIndex < pts.Count)
            {
                // Reversed, the race ends on the forward START LINE — which is
                // a few hundred metres up the list from index 0, because index
                // 0 is the far end of the forward lead-in. FinishIndex has
                // already forced the bake to load, so stageStartLineM is warm.
                int finishIdx = flip
                    ? Mathf.Clamp(Mathf.RoundToInt(def.stageStartLineM / Spacing), 0, pts.Count - 1)
                    : def.FinishIndex;
                var f = pts[finishIdx];
                Plot(px, size, f.x * scale + ox, f.z * scale + oz, red, red);
            }

            tex.SetPixels32(px);
            tex.Apply();
            thumbs[cacheKey] = tex;
            return tex;
        }

        static void Plot(Color32[] px, int size, float fx, float fy, Color32 c, Color32 halo)
        {
            int x = Mathf.RoundToInt(fx), y = Mathf.RoundToInt(fy);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = x + dx, sy = y + dy;
                    if (sx < 0 || sy < 0 || sx >= size || sy >= size) continue;
                    int idx = sy * size + sx;
                    if (dx == 0 && dy == 0) px[idx] = c;
                    else if (px[idx].a == 0) px[idx] = halo;
                }
        }
    }
}
