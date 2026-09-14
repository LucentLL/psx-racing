using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.OnFoot;
using PSXRacing.Town;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// YOUR STREET — the player's own house, its garage, and the half-dozen
    /// neighbours either side of it, as a map of its own.
    ///
    /// It used to be a corner of the town. The owner's report: "I don't like
    /// that the player has one house they are warped to and a different house
    /// in town", which was exactly true — the walk-in front end had one house
    /// and the town had a second one at the end of a stub street, and they were
    /// not the same building in any sense a player could act on. The ask, and
    /// the shape of this file: "for now, the player's house/neighborhood should
    /// be its own map. Driving to the end of road gives option to go into town
    /// or go race. Maybe one day it becomes all one world, but for now they are
    /// separate maps that have warps to drive between."
    ///
    /// So: the home lot moved OUT of the town and into here, unchanged — same
    /// house, same garage door, same driveway, same spawn — and the town kept
    /// the shops. The junction menu at the bottom of the street came with it,
    /// which is what makes "drive to the end of the road" the question it was
    /// already asking.
    ///
    /// A partial of <see cref="PSXRacingBuilder"/> and modelled on
    /// PSXRacingBuilder.Town.cs, because a drivable free-roam scene is a stack
    /// of eight things that each fail silently on their own: a chase camera, a
    /// HUD, touch controls, a pause menu, a CityMode to be the session,
    /// a RaceHandoffApplier so the car is the one the save says it is, road
    /// LAYERS on the tarmac, and bounds. Copying the town's is how this one
    /// gets all eight right on the first build.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        public const string NeighborhoodScenePath = Root + "/Scenes/Neighborhood.unity";

        // ---- the street, in metres ---------------------------------------
        // Shares the HOME LOT's coordinate frame (HomeStreetX, TownHouseZ,
        // HomeStreetTop) so BuildTownHome needs no argument and no second
        // version. A scene's origin is arbitrary; agreeing with the code that
        // was already written is not.
        /// <summary>Where your street runs out, and the junction menu sits.
        /// Long enough that pulling off the drive and reaching the end is a
        /// drive rather than a manoeuvre.</summary>
        const float NbStreetEnd = -168f;
        /// <summary>Grass between the end of the tarmac and the boundary wall.
        ///
        /// The wall is a BACKSTOP now rather than the thing you meet. It stood
        /// six metres past the tarmac — three and a half metres past the
        /// junction volume's own south face — so a car that ran the street out
        /// and stopped nose-on against it had barely half a metre of its
        /// four-metre body left inside the trigger, and any bounce or yaw took
        /// even that.
        ///
        /// Thirty metres is what a suburban street is actually left at: a car
        /// that comes down its own road at fifty and crosses the line loses the
        /// throttle to the panel and stops on the grass with the wall still
        /// ahead of it. It is NOT enough for a car doing a hundred and forty,
        /// and no honest number would be — 30% of pedal over thirteen metres
        /// is not a stop. What makes that case harmless is not the distance: the
        /// panel is already up, the game already has the controls
        /// (PlayerCarInput's !inputEnabled branch), and StuckRecovery is already
        /// standing down for the same reason. The wall is met behind a menu.
        /// </summary>
        const float NbRunoff = 30f;
        /// <summary>Half the map's width. It was a local in BuildNbBounds; the
        /// depart line has to be exactly as wide as the wall it stands in front
        /// of, or a player who leaves the tarmac reaches the wall without ever
        /// crossing the line.</summary>
        const float NbHalfWidth = 58f;
        /// <summary>Metres between neighbouring plot centres. A 11.7 m house at
        /// the pack's scale needs a garden either side or the street reads as a
        /// terrace, which North Carolina suburbs are not.</summary>
        const float NbPlotPitch = 27f;
        /// <summary>How far the neighbours' houses stand back from the
        /// centreline. Their drives run from the kerb to the front of the
        /// house, so this is also how long a drive is.</summary>
        const float NbSetback = 24f;
        /// <summary>The LEVELLED part of a plot — the house and a walk round
        /// it. Smaller than the plot on purpose: the rest of the garden is
        /// allowed to be the hill it was cut out of.</summary>
        const float NbBenchW = 17f, NbBenchD = 15f;
        /// <summary>How far the bench eases out into the land around it.
        ///
        /// SHORT ON PURPOSE, and it was three metres first. A wide grass batter
        /// is a graded earth bank, and an earth bank BURIES the foundation: the
        /// land arrived at the bench's own level by the time it reached the
        /// house, so the concrete wall under it was underground along its whole
        /// length and no lot in the street ever showed one. At 1.5 m the
        /// transition is a face rather than a slope, the retaining wall stands
        /// in front of it, and "houses are often built on hills, but use
        /// concrete foundations to level them out" is a thing you can see.
        ///
        /// It is now the width of the street edge and of every GARDEN edge that
        /// cannot be graded to 1V:3H in the room it has; the ones that can are
        /// wider, and have no wall to bury (NbEdgesOf).</summary>
        const float NbBatter = 1.5f;
        /// <summary>The steepest drive the grader will build. 15% is steep for
        /// a residential drive and not rare on a hill; past it a loaded car
        /// scrapes and the ramp reads as a launch. This is what BOUNDS the
        /// whole system: the pad cannot be further from the road than a drivable
        /// ramp reaches, so nothing downstream — wall heights, cut depths —
        /// can run away.</summary>
        const float NbDriveGrade = 0.15f;

        /// <summary>The first plot down from your own, and the pitch to the
        /// next. ONE definition, read by both the loop that builds the houses
        /// and the height field that benches the ground under them — if those
        /// two disagreed by one plot, the ground would step where no house
        /// stands and a house would stand on a hill.</summary>
        const float NbFirstPlotZ = HomeStreetTop - 26f;

        /// <summary>
        /// THE TURNING HEAD. "House at end of street should be a culdasec, not
        /// a straight line to driveway."
        ///
        /// A dead-end street with no way to turn round is the tell that a map
        /// was laid out as a rectangle, and this one was: the carriageway ran
        /// straight into the player's own drive. The bulb is a proper
        /// residential turning head — 10 m of radius, which is the small end of
        /// what a fire appliance needs and the large end of what fits here.
        ///
        /// The centre is DERIVED, not chosen: back off from the garage door by
        /// the apron the drive needs and then by the radius. Using the door's
        /// fallback constant rather than its measured position on purpose —
        /// the height field reads this, and a height field cannot depend on a
        /// measurement taken later during the build.
        ///
        /// What it has to clear at the other end is plot 0, which is why
        /// NbFirstPlotZ moved from -14 to -26: the bulb's south pole is at
        /// 30.5 and the first plot's bench now ends at 27, with its drive
        /// entrance further down still. That costs the street one plot pair.
        /// </summary>
        const float NbBulbR = 10f;
        const float NbBulbCz = TownHouseZ - 6.45f - 6f - NbBulbR;   // door - apron - radius

        /// <summary>Where the straight street gives out and the head takes
        /// over: the point where the disc first reaches the full carriageway
        /// width, so neither surface ever has to neck down to meet the
        /// other.</summary>
        static float NbBulbThroatZ =>
            NbBulbCz - Mathf.Sqrt(NbBulbR * NbBulbR - HomeRoadW * 0.5f * HomeRoadW * 0.5f);
        static int NbPlotCount =>
            Mathf.CeilToInt((NbFirstPlotZ - (NbStreetEnd + 26f)) / NbPlotPitch);
        static float NbPlotZ(int i) => NbFirstPlotZ - i * NbPlotPitch;

        /// <summary>The street on its own. The full build takes twenty minutes
        /// and rebuilds eleven circuits to look at one suburban road; iterating
        /// on terrain should not cost that.</summary>
        [MenuItem("PSX Racing/Build Neighborhood")]
        public static void BuildNeighborhoodOnly()
        {
            EnsureFolders();
            psxLit = Shader.Find("PSX/Lit");
            BuildNeighborhoodScene();
            AssetDatabase.SaveAssets();
        }

        public static string BuildNeighborhoodScene()
        {
            ClearSeasonEntries();
            townPizzaKerb = townDealerDoor =
                townYardGate = townHomeDoor = townMechanicDoor = townPaintDoor = null;
            townPizzaHooks = null;
            psxLit = Shader.Find("PSX/Lit");
            if (psxLit == null) throw new System.Exception("PSX/Lit not found");
            matByTex.Clear();
            matByKey.Clear();
            nbGarageChecked = false;
            // Re-graded from the functions on every build, so an edit to the
            // street's constants is never built on last run's table.
            nbPlotEdges = null;
            // Same reason the town clears it: BuildLighting reads `track` for
            // the fog scale, and a stage's three-kilometre band on a suburban
            // street would show the far bound wall with sky under it.
            track = null;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGO = BuildLighting();

            var root = new GameObject("Neighborhood");
            var mats = TownMaterials();
            // The lawns turn with the year like everything else outdoors.
            RegisterSeasonalGround("TownGrass", TownHouseTex + "/Grass.jpg", mats.grass, Color.white, "grass");

            BuildNbGround(root.transform, mats);
            var homeDrive = BuildTownHome(root.transform, mats);
            BuildNbPlots(root.transform, mats);
            BuildNbBounds(root.transform);

            // THE JUNCTION AT THE BOTTOM OF THE STREET is the LINE below, and
            // nothing else.
            //
            // There used to be a VOLUME here as well — "DepartVenue", a
            // TownVenue.Kind.Depart forty metres long that you stopped inside
            // and pressed at. It had a history: built inside BuildTownHome
            // when the lot was a stub off the main road; moved out here when
            // the street became its own map; seated on the road after its
            // literal Y left it 10.9 m in the air; stretched from 14 m to 40
            // because nobody could stop inside 14. Every one of those was
            // right, and none of them answered a player who does not stop —
            // that answer is the TownEdge below, which opens the same menu on
            // crossing. Once it did, the volume was a SECOND menu on the same
            // spot, and its moving-car prompt ("TAP ACTION — STOP AT THE
            // JUNCTION", TownVenue's line for any venue you are not yet
            // stopped in) sat in the middle of the screen for the whole last
            // forty metres of the street, telling the player to stop at a
            // junction they could not see on the approach to a line they were
            // about to drive through. Removed 2026-09-07. The enum value stays
            // because TownVenue's Depart rows still name the menu.

            // THE LINE YOU CANNOT MISS — the end of the street, and the only
            // junction there is now.
            //
            // It began as a companion to that volume: forty metres of
            // stop-and-press was the right answer to "the junction was too
            // short to stop in" and no answer at all to a player who never
            // stops. TownVenue will not claim a car over 4.5 km/h and then
            // wants a PRESS — and while the car is moving AtVenue stays false,
            // so on a phone the ACTION button that press lives on is not even
            // drawn. What the player met instead was the boundary wall, and
            // what happened there was worse than nothing: StuckRecovery read a
            // car pinned against it as stuck, RaceHUD ranked the watchdog's
            // line ABOVE the junction's, and seven seconds later the car was
            // teleported back up its own street. "I crash into an invisible
            // wall instead of being given the menu", reported three times.
            //
            // A TownEdge and NOT a TownVenue: a venue claims a STOPPED car and
            // waits to be pressed; this claims a car at any speed and opens
            // the menu itself.
            //
            // FULL MAP WIDTH, exactly like the wall behind it. The run-off is a
            // hundred and sixteen metres of open grass, and a road-width line is
            // one a car can drive round to find the wall on the verge. Twenty-two
            // metres deep: the fixed step is 0.02 s, so even at 200 km/h a car
            // moves 1.1 m a tick and spends twenty of them inside.
            var edge = new GameObject("DepartEdge");
            edge.transform.SetParent(root.transform, false);
            edge.transform.position = new Vector3(HomeStreetX, -5f, NbStreetEnd - 5f);
            var edgeCol = edge.AddComponent<BoxCollider>();
            edgeCol.isTrigger = true;
            edgeCol.size = new Vector3(NbHalfWidth * 2f, 40f, 22f);
            var junction = edge.AddComponent<TownEdge>();
            junction.mode = TownEdge.Mode.AskWhereTo;
            // Into the neighbourhood is UP the street, toward the house: a car
            // arriving from town is put just inside this line facing that way.
            junction.inward = Vector3.forward;
            // THE LINE, on the face the car crosses first coming down the
            // street — the north one, 11 m up from the volume's centre — and
            // seated on the ROAD SURFACE there (the NbStreet slab's
            // NbRoadY + 0.02), which is eleven metres below the volume's
            // centre and the reason the volume is forty tall. The marker seats
            // its own parts, so this is the tarmac and not a lift above it.
            // Across the carriageway, not the whole map: NbRoadY is a function
            // of z alone and the last fifty metres are flat, so a level
            // curtain is right over the full width of the road and wrong the
            // moment it reaches the graded verge.
            float lineZ = NbStreetEnd - 5f + 11f;
            EdgeMarkers(root.transform,
                new Vector3(HomeStreetX, NbRoadY(lineZ) + 0.02f, lineZ),
                Vector3.right, HomeRoadW);

            // ---- the player, on their own drive, pointing down the street ----
            var physMat = GetOrCreatePhysMat("CarPhys", CarSlideFriction, 0.05f);
            var blobMat = MakeBlobShadowMaterial();
            var carsRoot = new GameObject("Cars");
            var player = BuildOneCar(carsRoot.transform, CarSetups[0], isPlayer: true,
                homeDrive.position, homeDrive.rotation, physMat, blobMat);
            var cars = new List<CarController> { player };

            BuildCameraAndHUD(player, cars, null, lightGO.GetComponent<Light>());

            var sessionGO = new GameObject("Session");
            var mode = sessionGO.AddComponent<PSXRacing.City.CityMode>();
            mode.player = player;
            mode.world = null;
            mode.venueName = "HOME";
            mode.respawnPoints = BuildNbRespawns(root.transform);

            var handoff = sessionGO.AddComponent<RaceHandoffApplier>();
            handoff.playerCar = player;
            handoff.sun = lightGO.GetComponent<Light>();
            handoff.hud = Object.FindFirstObjectByType<RaceHUD>();

            var systems = new GameObject("GameSystems");
            systems.AddComponent<PSXBootstrap>();
            systems.AddComponent<TouchControls>();
            var menu = systems.AddComponent<PauseMenu>();
            menu.playerCar = player;

            var forecourt = systems.AddComponent<ForecourtMode>();
            forecourt.playerCar = player;
            forecourt.carInput = player.GetComponent<PlayerCarInput>();
            forecourt.engine = player.GetComponent<EngineAudio>();
            forecourt.anywhereInTown = true;
            var psxCam = GameObject.Find("PSXCamera");
            if (psxCam != null)
            {
                forecourt.raceCamera = psxCam.GetComponent<Camera>();
                forecourt.chase = psxCam.GetComponent<ChaseCamera>();
            }

            // A TownWorld with almost nothing wired, and it earns its place:
            // it owns the walk-up garage door, the errand signpost, and BOTH
            // pizza rigs — the boxes in your hands and the ones on the seat.
            // Drive home mid-errand and the order still has to be visible.
            var world = systems.AddComponent<TownWorld>();
            world.player = player;
            world.homeDoor = townHomeDoor;
            world.blockMaterial = MakeMat("TownCinder", null,
                tint: new Color(0.56f, 0.56f, 0.52f));

            AttachSeasonDress();
            EditorSceneManager.SaveScene(scene, NeighborhoodScenePath);
            Log("[Neighborhood] Scene saved: " + NeighborhoodScenePath);
            return NeighborhoodScenePath;
        }


        // ------------------------------------------------------------------
        //  Verticality
        // ------------------------------------------------------------------
        /// <summary>
        /// THE STREET'S OWN PROFILE, in metres, as a function of z.
        ///
        /// "Maps needs some verticality. Not just flatlands forever." The free
        /// roam maps were stacks of flat slabs at hardcoded Y while the
        /// circuits had had height splines for months; this is the third,
        /// smallest elevation system in the project and it follows the same
        /// doctrine as the other two — THE LAND IS GRADED TO THE ROAD. The road
        /// is authored, everything else is answered relative to it.
        ///
        /// Measured DOWN the street from the top, so it is exactly zero at the
        /// player's own drive. That matters more than it looks: the home lot,
        /// its garage-door datum, its spawn point and its HOME trigger were all
        /// built flat by BuildTownHome, and a profile that did not start at zero
        /// there would lift the house off its own driveway.
        ///
        /// THE FIRST VERSION OF THIS WAS TOO SMALL TO SEE. Two sines at 2.6 m
        /// and 0.9 m gave the street 2.7 m of range over 212 m — real relief by
        /// the numbers, and a photograph of flat ground, because a 2.6 m rise a
        /// hundred metres away is under two degrees and the eye reads it as
        /// nothing. Amplitude that a GRADIENT check calls steep is not the same
        /// as amplitude a PLAYER calls a hill.
        ///
        /// So the street falls off a ridge instead of rippling along one: your
        /// house is on the high ground and the junction is eleven metres below
        /// it, with a pair of rolls on the way down. Shaped as smoothstep
        /// rather than a sine because both its ENDS are flat — the top matters
        /// (the home lot is flat geometry and the drive has to meet it level)
        /// and the bottom is where the junction trigger sits.
        ///
        /// Worst gradient is 12.6%, where the roll's slope lands on the fall's:
        /// steep for a suburb and not unheard of in one — Asheville has streets
        /// steeper — and a long way inside what the car climbs. The kerbs, the
        /// drives and the plot pads all read this function, so steepening it
        /// steepens them too, which is the point: this is what makes the
        /// foundations tall enough to see.
        /// </summary>
        static float NbRoadY(float z)
        {
            // CLAMPED AT THE TOP. Above HomeStreetTop there is no street —
            // there is your lot, which is flat geometry — and an unclamped
            // profile keeps curving up there and lifts the land out from under
            // your own house by half a metre.
            float u = Mathf.Max(0f, HomeStreetTop - z);        // metres down the street
            float t = Mathf.Clamp01(u / (HomeStreetTop - NbStreetEnd));
            float fall = -11f * t * t * (3f - 2f * t);         // the ridge, falling
            float roll = -1.6f * (1f - Mathf.Cos(u / 16f)) * 0.5f;
            return fall + roll;
        }

        /// <summary>
        /// The LAND, anywhere in the neighbourhood.
        ///
        /// Pinned to the road across the carriageway and its verge, then
        /// released into its own relief over the next thirty metres — the same
        /// three-term shape GroundHeightAt uses on the circuits, at a tenth of
        /// the scale. The pin is what keeps the kerb line honest: a lawn that
        /// did its own thing right up to the tarmac would cut through it.
        /// </summary>
        /// <summary>
        /// Distance from the CROWN of the carriageway — the centreline running
        /// down the street, and the centre of the turning head at the top of
        /// it, with the head's extra width taken off so the pin reaches its rim
        /// exactly as it reaches the kerb line everywhere else.
        ///
        /// Without this the bulb's flanks would stand in unpinned hillside: the
        /// pin is solid out to 7 m from the crown and the head reaches 10, so
        /// its shoulders would have had up to three metres of relief cutting
        /// across a piece of tarmac.
        /// </summary>
        static float NbCrownDist(float x, float z)
        {
            float dx = Mathf.Abs(x - HomeStreetX);
            float dz = z - NbBulbCz;
            float radial = Mathf.Sqrt(dx * dx + dz * dz);
            float street = dz <= 0f ? dx : radial;
            float head = radial - (NbBulbR - HomeRoadW * 0.5f);
            return Mathf.Max(0f, Mathf.Min(street, head));
        }

        static float NbLandY(float x, float z)
        {
            float road = NbRoadY(z);
            float d = NbCrownDist(x, z);
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(HomeRoadW * 0.5f + 2.5f, 34f, d));
            // Three terms rather than two, and half again the amplitude, for
            // the same reason the road profile grew: at 3.4 m over a 240 m
            // slab the land was a putting green. The third term is a ROLL
            // ACROSS the street — it varies with x alone — which is what makes
            // one side of the road sit above the other, and that is the thing
            // a driver actually sees.
            float relief = 5.6f * Mathf.Sin(x / 38f + 1.3f) * Mathf.Cos(z / 71f)
                         + 2.4f * Mathf.Sin((x + z) / 29f)
                         + 3.2f * Mathf.Cos(x / 26f - 0.7f);
            // Faded to nothing at the top of the street for the same reason the
            // road profile starts at zero there — the player's own lot is flat
            // geometry and the land has to arrive at it level.
            float nearHome = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(HomeStreetTop + 4f, HomeStreetTop - 40f, z));
            return road + relief * t * nearHome;
        }

        /// <summary>
        /// How high a plot's bench sits.
        ///
        /// The land where the house goes, MOVED until the drive that has to
        /// reach it is drivable. Without that clamp the first build put a plot
        /// 5 m above its own kerb with 17 m to climb: a 33% driveway, which is
        /// not a driveway. Everything else about a plot is derived from this
        /// number, so bounding it here bounds the wall heights and the cut
        /// depths too.
        /// </summary>
        /// <remarks>
        /// CLAMPED AGAINST THE ROAD WHERE THE DRIVE ACTUALLY LEAVES IT. This
        /// read NbRoadY(plotZ - 4.5) — where the drive was before it moved to
        /// the garage — while the ramp in NbGroundY starts from the road AT the
        /// drive, NbDriveZ. On the -x side the two are 0.43 m apart; on the +x
        /// side 9.43 m, on a street falling up to 12.6%, and the difference
        /// was added to the 15% allowance: every +x drive came out at 17-24%
        /// down to a plot 1.8-2.2 m under the street. Measured on the replica
        /// after the fix, the worst drive centreline on either side is 15.0%.
        /// (A drive's two EDGES still reach 16-17.7% on the +x side: each
        /// starts from the road at its own z, 5 m apart on a street falling up
        /// to 12.6%, so the slab warps across its width. That is NbGroundY's
        /// ramp, not this clamp.)
        /// </remarks>
        static float NbPadY(int side, float plotZ)
        {
            float raw = NbLandY(HomeStreetX + side * (NbSetback + 2f), plotZ);
            float edge = NbRoadY(NbDriveZ(side, plotZ));
            float reach = NbDriveGrade * NbDriveRun;
            return Mathf.Clamp(raw, edge - reach, edge + reach);
        }

        /// <summary>
        /// How wide each of a plot's garden edges is graded, and whether it
        /// could be graded at all.
        ///
        /// "Plot edges graded at 1V:3H where the land allows." Every edge of
        /// every bench used to fall into the hillside over NbBatter's 1.5 m —
        /// faces up to 6.5 m tall, 23 facets past 45 degrees that nothing can
        /// climb — because a wide batter buries the retaining wall the owner
        /// asked to see. Both are honoured now, edge by edge:
        ///   * where a batter of RoadsideRules.TraversableSlope fits in the
        ///     room the edge has, it is graded (and there is no wall to bury);
        ///   * where it does not, the edge keeps its 1.5 m face and its
        ///     retaining wall, which is solid if the fall is critical
        ///     (BuildNbPlots).
        ///
        /// Smoothstep peaks at 1.5x its average slope and the land under the
        /// batter has a slope of its own, so the width asked for is
        /// NbGradePeak x drop / (1/3) — 1.75 measured on the replica, which
        /// holds the graded batters at 26-32% (14 of the 36 garden edges on
        /// the street grade; the other 22 keep their walls, 11 of them solid).
        ///
        /// THE ROOM is what keeps the ground continuous. Which plot a point
        /// belongs to switches half a pitch from its centre (NbGroundY), so a
        /// side edge's batter must finish within NbPlotPitch/2 - NbBenchD/2 or
        /// the ground steps at the switch. The back edge runs toward the
        /// boundary wall and stops NbBackRoomMargin short of it. The street
        /// edge is not graded here at all: it is where the drive lands, and
        /// NbBenchOut — which NbPadY's reach is measured over — reads its width.
        /// </summary>
        struct NbPlotEdges
        {
            public float back, zLo, zHi;
            public bool backGraded, zLoGraded, zHiGraded;
        }
        const float NbGradePeak = 1.75f;
        const float NbBackRoomMargin = 3.5f;
        static NbPlotEdges[] nbPlotEdges;

        static NbPlotEdges NbEdgesOf(int plot, int side)
        {
            if (nbPlotEdges == null)
            {
                var table = new NbPlotEdges[NbPlotCount * 2];
                float roomZ = NbPlotPitch * 0.5f - NbBenchD * 0.5f;
                float roomBack = NbHalfWidth - (NbSetback + 2f + NbBenchW * 0.5f) - NbBackRoomMargin;
                for (int i = 0; i < NbPlotCount; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var e = new NbPlotEdges();
                        e.backGraded = NbGradeEdge(i, s, 0, roomBack, out e.back);
                        e.zLoGraded = NbGradeEdge(i, s, -1, roomZ, out e.zLo);
                        e.zHiGraded = NbGradeEdge(i, s, 1, roomZ, out e.zHi);
                        table[i * 2 + (s > 0 ? 1 : 0)] = e;
                    }
                nbPlotEdges = table;
            }
            return nbPlotEdges[plot * 2 + (side > 0 ? 1 : 0)];
        }

        /// <summary>A point on one of a plot's garden edges, <paramref
        /// name="past"/> metres beyond the bench, <paramref name="t"/> from -1
        /// to 1 along it. <paramref name="which"/>: 0 the back, -1 / +1 the
        /// low- and high-z sides.</summary>
        static Vector2 NbEdgePoint(int side, float plotZ, int which, float past, float t)
        {
            float cx = NbSetback + 2f;
            return which == 0
                ? new Vector2(HomeStreetX + side * (cx + NbBenchW * 0.5f + past),
                              plotZ + t * NbBenchD * 0.5f)
                : new Vector2(HomeStreetX + side * (cx + t * NbBenchW * 0.5f),
                              plotZ + which * (NbBenchD * 0.5f + past));
        }

        /// <summary>The widest drop (or rise) between the bench and the raw
        /// land along an edge, out to <paramref name="reach"/>.</summary>
        static float NbEdgeRelief(int plot, int side, int which, float reach)
        {
            float pz = NbPlotZ(plot), pad = NbPadY(side, pz), worst = 0f;
            for (float s = 0f; s <= reach + 1e-3f; s += 0.5f)
                for (int k = -10; k <= 10; k++)
                {
                    var p = NbEdgePoint(side, pz, which, s, k / 10f);
                    worst = Mathf.Max(worst, Mathf.Abs(pad - NbLandY(p.x, p.y)));
                }
            return worst;
        }

        static bool NbGradeEdge(int plot, int side, int which, float room, out float batter)
        {
            batter = NbBatter;
            for (int it = 0; it < 6; it++)
            {
                float need = Mathf.Max(NbBatter, NbGradePeak * NbEdgeRelief(plot, side, which, batter)
                                                 / RoadsideRules.TraversableSlope);
                if (need > room) { batter = NbBatter; return false; }
                if (need <= batter + 1e-3f) return true;
                batter = need;
            }
            return true;
        }

        /// <summary>How far out from the street's centreline the kerb is, and
        /// how far out the bench starts. THE RAMP RUNS BETWEEN THEM, and its
        /// length is what bounds the bench height — a drive that has to climb
        /// more than its own length allows is a kerb the car cannot mount.
        ///
        /// The ramp finishing exactly where the bench begins is not tidiness.
        /// The first version ran it on to the house, which is three metres
        /// INSIDE the bench, so over those three metres the bench was pulling
        /// the surface to its level while the ramp was still climbing to it:
        /// 36% measured, on a drive whose average was 15%. Two graders working
        /// on the same three metres.</summary>
        ///
        /// THE CARRIAGEWAY EDGE, WHICH IS ALSO THE KERB'S INNER FACE. It was
        /// HomeRoadW/2 - 0.6, i.e. 0.6 m INSIDE the kerb it is named for, so
        /// the ramp began under the tarmac: the graded ground was already
        /// climbing across the last 0.6 m of carriageway and reached 1 cm ABOVE
        /// the road surface at its edge, hidden only because the driveway slab
        /// was parked on top of it. The drive read the same wrong number and so
        /// ran 0.6 m out onto the road — 42 square metres of concrete on the
        /// street, which is "driveways should not extend into the road".
        ///
        /// The two are the same expression and have to move together: pulling
        /// the slab in without pulling the ramp in exposes the grass, and the
        /// slab lands on a 12 cm step off the tarmac.
        static float NbKerbOut => HomeRoadW * 0.5f;
        // The batter counts as bench. Landing the ramp on the bench's EDGE
        // still overlapped the two by the width of the batter, and over those
        // three metres the bench pulled the surface up while the ramp was
        // still climbing: 23% on a 15% drive. Ending it where the bench's
        // influence BEGINS gives the ramp three metres of level apron before
        // the garage and makes the measured gradient the designed one.
        static float NbBenchOut => NbSetback + 2f - (NbBenchW * 0.5f + NbBatter);
        static float NbDriveRun => NbBenchOut - NbKerbOut;

        /// <summary>
        /// How far the wide garage door sits from the middle of house_simple,
        /// along the axis that ends up pointing down the street.
        ///
        /// MEASURED IN UNITY, off the instantiated model, by
        /// <see cref="PackProbe"/> — and the sign is the whole point of saying
        /// so. This was measured in BLENDER and converted by hand, which is a
        /// thing the town builder's own comment already warns about ("the
        /// exporter mirrors X, so a coordinate that is correct in Blender is on
        /// the wrong side of the house in Unity"). It came out NEGATIVE, and
        /// so did every driveway on the street: the drive ran to model local
        /// x -6.08, which is not merely the far side of the garage but two
        /// thirds of a metre off the END of a house that spans -5.52 to +8.90.
        /// Fourteen driveways to a patch of lawn beside the building. Reported
        /// in one line: "driveways go to the wrong side of house. they should
        /// go to the garage."
        ///
        /// The magnitude was right all along. PackProbe puts the Garage_Door
        /// material's wide leaf — 3.51 x 3.01, against the 2.17 shed door round
        /// the back — at Unity local x +6.082, which at CityProps.PackScale
        /// 0.81 is 4.93 m. The house is turned to face the street, so that X
        /// offset lands on world Z — and it lands on the OPPOSITE side of the
        /// plot for the two rows, because they are turned opposite ways.
        /// </summary>
        const float NbGarageOffsetZ = 4.93f;

        /// <summary>
        /// How far out from the street's crown the garage door's face is, on
        /// the axis that runs up the drive.
        ///
        /// The same measurement, on the model's other horizontal axis: the door
        /// is at Unity local z +6.754, the house is turned so local +Z points
        /// AT the street, and its origin stands <c>NbSetback + 2</c> out — so
        /// the door faces the street from 26 - 6.754*0.81 = 20.53 m out.
        ///
        /// IT IS WHERE THE DRIVE HAS TO END. The drive used to stop at a flat
        /// 18.5, which is two metres short of the door with the model's own
        /// concrete path lying in the gap: half a fix, and the half that still
        /// reads as "the driveway does not go to the garage" even once the
        /// drive is on the right side of the house. A little past it rather
        /// than exactly on it, so the concrete runs under the door line and
        /// there is no hairline of lawn where the two meet.
        /// </summary>
        const float NbGarageFaceOut = 20.53f + 0.6f;

        /// <summary>
        /// Where a plot's drive runs, in z. Signed by <paramref name="side"/>,
        /// which the first version was not: it was a flat <c>z - 4.5</c>, so it
        /// could only ever meet the garage on ONE side of the street and missed
        /// the other row's door by 9.4 m — a driveway to a blank wall, with the
        /// garage round the corner. Read by the height field AND the slab, so
        /// the graded ramp and the concrete on top of it agree.
        ///
        /// PLUS, not minus. See <see cref="NbGarageOffsetZ"/>: signing this the
        /// other way put all fourteen drives past the end of the house.
        /// </summary>
        static float NbDriveZ(int side, float plotZ) => plotZ + side * NbGarageOffsetZ;

        static bool nbGarageChecked;

        /// <summary>
        /// Does the drive actually arrive at the garage door?
        ///
        /// Measures the door on a house that has just been placed and seated,
        /// and compares it with where <see cref="NbDriveZ"/> and
        /// <see cref="NbGarageFaceOut"/> say the drive is going. Once per
        /// build, on the first house — every plot is the same model turned the
        /// same way, so a second measurement would only be the first one again.
        ///
        /// It LOGS rather than throws. The numbers it checks are cosmetic
        /// geometry, and a street that builds with a warning on it is a street
        /// somebody can look at; one that refuses to build is forty minutes of
        /// nothing. But it is a WARNING and it names the correction, because
        /// the failure it is looking for is invisible in a screenshot taken
        /// from anywhere but directly over the plot.
        ///
        /// BY MATERIAL, through <see cref="WorldKit.GarageDoorOf"/>. The first
        /// version of this check searched transform names the way the town's
        /// does, and reported "no wide Garage_Door on house_simple" on every
        /// build — house_hero ships its door as a named node and house_simple
        /// is one mesh called "House" with the door on a material slot. A
        /// check that cannot see its subject passes for the wrong reason,
        /// which is worse than not having one.
        /// </summary>
        static void NbCheckGarage(GameObject house, int side, float plotZ)
        {
            nbGarageChecked = true;
            if (!WorldKit.GarageDoorOf(house, out var door))
            {
                Log("[Neighborhood] WARN: no wide Garage_Door on house_simple — " +
                    "NbGarageOffsetZ is unchecked and the drives are a guess.");
                return;
            }
            float widest = Mathf.Max(door.size.x, door.size.z);

            float driveZ = NbDriveZ(side, plotZ);
            float offZ = Mathf.Abs(door.center.z - driveZ);
            float doorOut = (door.center.x - HomeStreetX) * side;
            float offX = NbGarageFaceOut - doorOut;

            string verdict = (offZ < 1.2f && offX > -0.5f && offX < 2.5f) ? "OK" : "WRONG";
            Log("[Neighborhood] garage check " + verdict + ": door " +
                widest.ToString("0.00") + " m wide at z " + door.center.z.ToString("0.00") +
                " / " + doorOut.ToString("0.00") + " m out; drive centred z " +
                driveZ.ToString("0.00") + " (off by " + offZ.ToString("0.00") +
                " m) and ends " + offX.ToString("0.00") + " m past the door.");
            if (verdict == "WRONG")
                Log("[Neighborhood] WARN: the drives do not meet the garage. " +
                    "NbGarageOffsetZ should be " +
                    (Mathf.Abs(door.center.z - plotZ)).ToString("0.000") + " signed " +
                    ((door.center.z - plotZ) * side > 0f ? "+" : "-") +
                    ", NbGarageFaceOut " + doorOut.ToString("0.00") + ".");
        }

        /// <summary>
        /// The land as it has been GRADED: the hillside everywhere, except
        /// where a plot has been benched into it.
        ///
        /// THE BENCHES BELONG IN THE HEIGHT FIELD, not on top of it. The first
        /// version laid a flat lawn slab over each plot and left the ground
        /// beneath it alone, which works only while the ground is nearly flat:
        /// at the relief this street has now, the hillside stood up to 2.8 m
        /// THROUGH the lawn it was supposed to be under. Two surfaces
        /// describing one piece of land will always find a way to disagree.
        /// One function, read by the ground mesh, the driveways and the plot
        /// builder alike, cannot.
        ///
        /// Which plot a point belongs to is arithmetic, not a search: the plots
        /// are a regular grid, and the nearest one is the only one that can
        /// reach — a bench's side batters are held inside half a pitch
        /// (NbEdgesOf), so they have finished by the line where the next plot
        /// takes over and no point is ever on two.
        /// </summary>
        static float NbGroundY(float x, float z)
        {
            float land = NbLandY(x, z);
            int i = Mathf.RoundToInt((NbFirstPlotZ - z) / NbPlotPitch);
            if (i < 0 || i >= NbPlotCount) return land;

            int side = x >= HomeStreetX ? 1 : -1;
            float pz = NbPlotZ(i);
            float cx = HomeStreetX + side * (NbSetback + 2f);
            float padY = NbPadY(side, pz);

            // The BENCH: level ground where the house stands, eased out into
            // the hillside over the batter — each garden edge over its own
            // width (NbEdgesOf), the street edge over NbBatter as ever.
            var edges = NbEdgesOf(i, side);
            float toBack = (x - cx) * side;
            float batterX = toBack > 0f ? edges.back : NbBatter;
            float batterZ = z > pz ? edges.zHi : edges.zLo;
            float kBench = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(
                Mathf.InverseLerp(NbBenchW * 0.5f, NbBenchW * 0.5f + batterX,
                                  Mathf.Abs(toBack)),
                Mathf.InverseLerp(NbBenchD * 0.5f, NbBenchD * 0.5f + batterZ,
                                  Mathf.Abs(z - pz))));

            // THE DRIVE IS GRADED TOO, and it has to be its own corridor rather
            // than something laid over the bench's edge. The batter is 3 m
            // wide, so a drive crossing it climbed the whole foundation in
            // three metres: a 109% ramp into the garden, measured. A drive is
            // cut its own way up the slope in real life for exactly this
            // reason, and here it runs the thirteen metres from the kerb to the
            // edge of the bench — which is also the length NbPadY divides by,
            // so the gradient can never exceed NbDriveGrade.
            //
            // LINEAR, not eased. A smoothstep ramp peaks at 1.5x its average
            // and would cost a third of the bench height to stay legal; a
            // straight ramp with a break at the kerb and another at the apron
            // is both steeper-looking and what a driveway actually is.
            float driveZ = NbDriveZ(side, pz);
            float outward = (x - HomeStreetX) * side;      // metres out from the crown
            float kDrive = (1f - Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(2.5f, 6.5f, Mathf.Abs(z - driveZ))))
                         * (1f - Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(NbSetback + 2f + NbBenchW * 0.5f,
                                                  NbSetback + 2f + NbBenchW * 0.5f + 4f,
                                                  outward)));
            // From THIS station's road height, not the drive centreline's. The
            // corridor is 13 m of z and the street falls through it, so a ramp
            // that started at one fixed height would hold a flat shelf across
            // the carriageway and stand up to 0.8 m through its own tarmac.
            // Starting from NbRoadY(z) makes the ramp identically equal to the
            // pinned land at the kerb, which means the corridor changes nothing
            // it should not touch and needs no fade at that end.
            float ramp = Mathf.Lerp(NbRoadY(z), padY,
                Mathf.InverseLerp(NbKerbOut, NbBenchOut, outward));

            float k = Mathf.Max(kBench, kDrive);
            if (k <= 0f) return land;
            // Weighted rather than switched: the ramp has already ARRIVED at
            // padY by the time it is inside the bench, so the two agree where
            // they overlap and there is no seam to hide.
            float target = (kBench * padY + kDrive * ramp) / (kBench + kDrive);
            return Mathf.Lerp(land, target, k);
        }
        // ------------------------------------------------------------------
        //  The lawn, the street, and where they meet
        // ------------------------------------------------------------------
        /// <summary>The lawn mesh: how far past the street it runs, and its cell.</summary>
        const float NbGroundSizeX = 240f, NbGroundOverZ = 220f, NbGroundCell = 3f;
        const float NbStreetCell = 2f, NbBulbCell = 2f;
        /// <summary>The street's and the turning head's surfaces over NbRoadY.
        /// The head a centimetre proud of the street where the two overlap —
        /// two coplanar road surfaces are a z-fight.</summary>
        const float NbStreetLift = 0.02f, NbBulbLift = 0.03f;
        /// <summary>How far the lawn mesh is sunk under the height field, for
        /// the same grazing-angle band of grass the town's is sunk against.</summary>
        const float NbLawnSink = 0.06f;
        /// <summary>
        /// The least the lawn mesh may come within any driven surface above it
        /// — street, kerb, head, drive. Less than the 8 cm the sink designs in,
        /// so a lattice that does what the function says is left alone; the
        /// solver only moves vertices whose CHORDS climb toward a surface.
        /// Tighter than RoadsideRules.CityHideMarginM (0.10) because the lawn
        /// here is a 3 m lattice following a smooth field, not an 8 m one
        /// following a DEM.
        /// </summary>
        const float NbHideMarginM = 0.06f;
        /// <summary>
        /// A drive's surface over the graded field: 3 cm, where it was 5. At
        /// the street end the field IS the road (the ramp starts from
        /// NbRoadY), so the drive mouth sits a centimetre over the tarmac at
        /// NbStreetLift instead of three — flush, and inside the owner's inch.
        /// </summary>
        const float NbDriveLift = 0.03f;
        const float NbDriveW = 5f, NbDriveCell = 1.5f;

        static float NbStreetLen => NbBulbThroatZ - NbStreetEnd;
        static float NbStreetMidZ => (NbBulbThroatZ + NbStreetEnd) * 0.5f;

        /// <summary>The drive slab of one plot, as the grid it is built on.</summary>
        static WorldKit.SlabGrid NbDriveGrid(int side, float plotZ)
        {
            float kerbX = HomeStreetX + side * NbKerbOut;
            float houseX = HomeStreetX + side * NbGarageFaceOut;
            return new WorldKit.SlabGrid(new Vector3((kerbX + houseX) * 0.5f, 0f, NbDriveZ(side, plotZ)),
                                         Mathf.Abs(houseX - kerbX), NbDriveW, NbDriveCell);
        }

        static float NbDriveSurfaceY(float x, float z) => NbGroundY(x, z) + NbDriveLift;

        /// <summary>The lawn mesh as built: its grid, and the world Y of every
        /// vertex after SolveNbLattice.</summary>
        static WorldKit.SlabGrid nbLattice;
        static float[] nbLatticeY;

        /// <summary>The height of the lawn's COLLIDER at a world point — its
        /// triangles, not the field it sampled. What every feather on the
        /// street tucks under.</summary>
        static float NbLatticeAt(float x, float z) => nbLattice.SurfaceY(nbLatticeY, x, z);

        /// <summary>
        /// THE LAWN MESH, HELD UNDER EVERYTHING DRIVEN ON.
        ///
        /// The lawn is a 3 m lattice sampling NbGroundY, and between its
        /// vertices it is a chord, not the field. Where the field bends
        /// upward under a surface — a downhill drive's 15% ramp landing on its
        /// level bench — the chord stands above the bend. Measured on the
        /// replica: the lawn came through the edge of every +x drive by up to
        /// 16 cm at the bench, and through the tarmac's last 20 cm by 2 cm
        /// where an uphill drive's corridor lifts the verge. Grass drawn on
        /// the concrete, and a lip a lowered car finds.
        ///
        /// So every lattice cell that lies under a driven surface is sampled
        /// seven by seven against that surface's own triangles, and where the
        /// chord comes within NbHideMarginM its four vertices are sunk by the
        /// excess, until nothing does. One pass settles it on this street; the
        /// loop is there so a steeper one cannot ship half-solved. The cost is
        /// a soft dip one cell wide in the lawn beside a sunk crease, which the
        /// feathers follow because they are measured against THIS surface.
        /// </summary>
        static void SolveNbLattice()
        {
            var g = new WorldKit.SlabGrid(new Vector3(HomeStreetX, 0f, NbStreetMidZ),
                                          NbGroundSizeX, NbStreetLen + NbGroundOverZ, NbGroundCell);
            nbLattice = g;
            nbLatticeY = new float[g.VertexCount];
            for (int j = 0; j <= g.nz; j++)
                for (int i = 0; i <= g.nx; i++)
                    nbLatticeY[g.Index(i, j)] = NbGroundY(g.WorldX(i), g.WorldZ(j)) - NbLawnSink;

            // The driven surfaces, as the triangles BuildNbPlots will lay.
            var drives = new List<(WorldKit.SlabGrid grid, float[] y, Rect foot)>();
            for (int p = 0; p < NbPlotCount; p++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var dg = NbDriveGrid(side, NbPlotZ(p));
                    var dy = new float[dg.VertexCount];
                    for (int j = 0; j <= dg.nz; j++)
                        for (int i = 0; i <= dg.nx; i++)
                            dy[dg.Index(i, j)] = NbDriveSurfaceY(dg.WorldX(i), dg.WorldZ(j));
                    drives.Add((dg, dy, Rect.MinMaxRect(
                        dg.centre.x - dg.sizeX * 0.5f, dg.centre.z - dg.sizeZ * 0.5f,
                        dg.centre.x + dg.sizeX * 0.5f, dg.centre.z + dg.sizeZ * 0.5f)));
                }
            // The street reaches to the back of its kerb: the kerb's ramp
            // collider rises from the street's own height across that width.
            float streetHalf = HomeRoadW * 0.5f + WorldKit.KerbWidthM;
            float headReach = NbBulbR + WorldKit.KerbWidthM;
            var near = new List<Rect>
            {
                Rect.MinMaxRect(HomeStreetX - streetHalf, NbStreetEnd, HomeStreetX + streetHalf, NbBulbThroatZ),
                Rect.MinMaxRect(HomeStreetX - headReach, NbBulbCz - headReach,
                                HomeStreetX + headReach, NbBulbCz + headReach),
            };
            foreach (var d in drives) near.Add(d.foot);

            // INCLUSIVE BY A MILLIMETRE. The replica lost the whole back edge of
            // the kerb to this: |x - crown| at the band's edge came out
            // 4.950000000000003, the sample fell outside, and the lawn was left
            // 2 cm under a kerb it should have been 6 cm under.
            const float Eps = 0.001f;
            float Ceiling(float x, float z)
            {
                float c = float.PositiveInfinity;
                if (Mathf.Abs(x - HomeStreetX) <= streetHalf + Eps &&
                    z >= NbStreetEnd - Eps && z <= NbBulbThroatZ + Eps)
                    c = Mathf.Min(c, NbRoadY(z) + NbStreetLift);
                float hx = x - HomeStreetX, hz = z - NbBulbCz;
                if (hx * hx + hz * hz <= (headReach + Eps) * (headReach + Eps))
                    c = Mathf.Min(c, NbRoadY(z) + NbBulbLift);
                foreach (var d in drives)
                    if (x >= d.foot.xMin - Eps && x <= d.foot.xMax + Eps &&
                        z >= d.foot.yMin - Eps && z <= d.foot.yMax + Eps)
                        c = Mathf.Min(c, d.grid.SurfaceY(d.y, x, z));
                return c - NbHideMarginM;
            }

            // WHERE TO LOOK. Both surfaces are piecewise linear, so how far the
            // lawn climbs toward a surface peaks on a line where one of them
            // folds: a footprint's edge, a drive's own vertex rows and columns,
            // the lattice's diagonal. A regular grid of samples alone missed
            // 4.6 cm at the edge of a drive, between two samples a quarter of a
            // metre either side of the fold. So each cell is sampled on a 0.5 m
            // grid PLUS every such line that crosses it, plus where the cell's
            // diagonal crosses each of those lines.
            var linesX = new List<float>
            {
                HomeStreetX - streetHalf, HomeStreetX - HomeRoadW * 0.5f,
                HomeStreetX + HomeRoadW * 0.5f, HomeStreetX + streetHalf,
            };
            var linesZ = new List<float> { NbStreetEnd, NbBulbThroatZ };
            foreach (var d in drives)
            {
                for (int i = 0; i <= d.grid.nx; i++) linesX.Add(d.grid.WorldX(i));
                for (int j = 0; j <= d.grid.nz; j++) linesZ.Add(d.grid.WorldZ(j));
            }
            const int Samples = 6;
            var xs = new List<float>();
            var zs = new List<float>();
            var total = new float[g.VertexCount];
            float lastWorst = 0f;
            for (int pass = 0; pass < 4; pass++)
            {
                var sink = new float[g.VertexCount];
                float worst = 0f;
                for (int j = 0; j < g.nz; j++)
                    for (int i = 0; i < g.nx; i++)
                    {
                        float x0 = g.WorldX(i), x1 = g.WorldX(i + 1);
                        float z0 = g.WorldZ(j), z1 = g.WorldZ(j + 1);
                        var cell = Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(z0, z1),
                                                   Mathf.Max(x0, x1), Mathf.Max(z0, z1));
                        bool under = false;
                        foreach (var r in near) if (r.Overlaps(cell)) { under = true; break; }
                        if (!under) continue;
                        xs.Clear();
                        zs.Clear();
                        for (int a = 0; a <= Samples; a++)
                        {
                            xs.Add(Mathf.Lerp(x0, x1, a / (float)Samples));
                            zs.Add(Mathf.Lerp(z0, z1, a / (float)Samples));
                        }
                        foreach (float lx in linesX) if (lx >= cell.xMin && lx <= cell.xMax) xs.Add(lx);
                        foreach (float lz in linesZ) if (lz >= cell.yMin && lz <= cell.yMax) zs.Add(lz);
                        float excess = 0f;
                        void Try(float sx, float sz)
                        {
                            float c = Ceiling(sx, sz);
                            if (float.IsPositiveInfinity(c)) return;
                            excess = Mathf.Max(excess, NbLatticeAt(sx, sz) - c);
                        }
                        foreach (float sx in xs)
                        {
                            foreach (float sz in zs) Try(sx, sz);
                            // the cell's diagonal, (x0,z0) to (x1,z1), crossing this column
                            Try(sx, z0 + (sx - x0) / (x1 - x0) * (z1 - z0));
                        }
                        foreach (float sz in zs)
                            Try(x0 + (sz - z0) / (z1 - z0) * (x1 - x0), sz);
                        if (excess <= 0.001f) continue;
                        worst = Mathf.Max(worst, excess);
                        int v00 = g.Index(i, j), v10 = g.Index(i + 1, j);
                        int v01 = g.Index(i, j + 1), v11 = g.Index(i + 1, j + 1);
                        sink[v00] = Mathf.Max(sink[v00], excess);
                        sink[v10] = Mathf.Max(sink[v10], excess);
                        sink[v01] = Mathf.Max(sink[v01], excess);
                        sink[v11] = Mathf.Max(sink[v11], excess);
                    }
                lastWorst = worst;
                if (worst <= 0.001f) break;
                for (int v = 0; v < sink.Length; v++)
                {
                    nbLatticeY[v] -= sink[v];
                    total[v] += sink[v];
                }
            }
            int sunk = 0;
            float deepest = 0f;
            foreach (float t in total)
                if (t > 0f) { sunk++; deepest = Mathf.Max(deepest, t); }
            Log("[Neighborhood] lawn held " + NbHideMarginM.ToString("0.00") +
                " m under the street, head and drives: " + sunk + " vertices sunk, deepest " +
                deepest.ToString("0.000") + " m" +
                (lastWorst > 0.001f ? "  WARN: still " + lastWorst.ToString("0.000") +
                                      " m through after four passes" : ""));
        }

        static void BuildNbGround(Transform parent, TownMats m)
        {
            // THE STREET ENDS AT THE THROAT and the turning head carries the
            // rest. They meet where the disc is exactly as wide as the
            // carriageway, so there is no pinch and no gap; the head is laid a
            // centimetre proud over the overlap because two coplanar road
            // surfaces are a z-fight, and a centimetre is a hundredth of the
            // kerb beside them.
            float streetLen = NbStreetLen;
            float midZ = NbStreetMidZ;

            // Past the fog wall in every direction, and SUNK below the tarmac
            // for the reason the town's is: at the grazing angle you see a
            // street from inside a car, two centimetres is not enough
            // separation and a band of grass crawls across the carriageway.
            // FOLLOWS THE LAND, and its cell is 4 m rather than the town's
            // because a coarse grid on a shaped surface is a facet that pokes
            // through whatever is laid on top of it.
            // 3 m cells, not 4. This mesh CARRIES THE BENCHES now, and a bench
            // is 17 m across: at 4 m the levelled part of a garden was four
            // cells wide and its edge landed wherever the grid happened to be.
            // AND IT IS THE SOLVED LATTICE, not the field: see SolveNbLattice.
            SolveNbLattice();
            WorldKit.GridSlab(parent, "NbGround", nbLattice.centre,
                nbLattice.sizeX, nbLattice.sizeZ, NbGroundCell, m.grass, true, 16f, 0,
                (x, z) => nbLatticeY[nbLattice.Index(nbLattice.NearestX(x), nbLattice.NearestZ(z))]);

            // The street itself, on the ROAD LAYER — CarController decides
            // onRoad by layer number, so tarmac left on layer 0 is a whole
            // session of off-road grip with nothing on screen to say so.
            // 2 m cells along it, not 4: the tarmac is what the car drives on
            // and its collider is this mesh now, so the crest of a 7.8% hill
            // has to be a curve rather than a pair of ramps meeting at a hinge.
            WorldKit.GridSlab(parent, "NbStreet",
                new Vector3(HomeStreetX, 0f, midZ),
                HomeRoadW, streetLen, NbStreetCell, m.road, true, 12f, WorldKit.RoadLayer,
                (x, z) => NbRoadY(z) + NbStreetLift);

            // The turning head. Its surface is the street's own profile, so it
            // drains across itself the way a real one does — NbRoadY is a
            // function of z alone, which over the head's twenty metres is a
            // 1.6% cross-fall, and that is a feature rather than a compromise.
            WorldKit.Disc(parent, "NbBulb", new Vector3(HomeStreetX, 0f, NbBulbCz),
                NbBulbR, NbBulbCell, m.road, true, 12f, WorldKit.RoadLayer,
                (x, z) => NbRoadY(z) + NbBulbLift);

            // NO CENTRE LINE. There was one — 23 yellow dashes down the middle
            // — and the comment justifying it argued only about whether it
            // should be broken or solid, having never asked the question the
            // owner did: "neighborhood road should not have road lines." It
            // should not. A residential street is unmarked; a centre line is
            // what makes a 9 m carriageway read as a highway with houses
            // improbably close to it. The town's main street keeps its own.

            BuildNbKerbs(parent, m);
            BuildNbStreetEnd(parent, m);
        }

        /// <summary>
        /// THE KERB, down both sides of the street and round the head: raised
        /// stone, a ramp a car can mount, and the lawn graded up behind it,
        /// dropped flush at every drive.
        ///
        /// It was 192 render-only boxes: eight-metre then two-metre lengths
        /// pitched to the fall, and chords yawed round the head. No collider,
        /// so a car drove straight through a drawn 14 cm kerb onto a lawn
        /// 8 cm under the tarmac, and back across an 8 cm step. One continuous
        /// stone per side now (WorldKit.Kerb), laid on the street's own edge
        /// vertices and then on the disc's own rim vertices — so its foot is
        /// the tarmac's edge exactly, with no daylight under a box and no slot
        /// between a true circle and the 31-gon the head really is. (In plan
        /// exactly; in height, for the first NbBulbCell past each throat the
        /// foot climbs from the street's lift to the head's and runs up to a
        /// centimetre under the head's rim — see THE HEAD'S CENTIMETRE below.)
        ///
        /// DROPPED AT EVERY DRIVE, per side, exactly the drive's width, with a
        /// transition either side (WorldKit.KerbTaperM) — and at the south end,
        /// where the street simply stops, and over the top of the head where
        /// your own drive leaves it.
        /// </summary>
        static void BuildNbKerbs(Transform parent, TownMats m)
        {
            var root = new GameObject("NbKerbs");
            root.transform.SetParent(parent, false);
            var street = new WorldKit.SlabGrid(new Vector3(HomeStreetX, 0f, NbStreetMidZ),
                                               HomeRoadW, NbStreetLen, NbStreetCell);
            int sectors = WorldKit.DiscSectors(NbBulbR, NbBulbCell);
            float sectorA = 2f * Mathf.PI / sectors;
            int built = 0;
            for (int s = -1; s <= 1; s += 2)
            {
                int side = s;
                var edge = new List<Vector3>();
                var outward = new List<Vector3>();
                for (int j = 0; j <= street.nz; j++)
                {
                    float z = street.WorldZ(j);
                    edge.Add(new Vector3(HomeStreetX + side * HomeRoadW * 0.5f, NbRoadY(z) + NbStreetLift, z));
                    outward.Add(new Vector3(side, 0f, 0f));
                }

                // Round the head on the disc's rim vertices, from the throat
                // toward the top — this side's way round — until one lands
                // inside your drive, which is where the run ends.
                float throat = Mathf.Atan2(NbBulbThroatZ - NbBulbCz, side * HomeRoadW * 0.5f);
                var rim = new List<(float swept, float a)>();
                for (int k = 0; k < sectors; k++)
                {
                    float a = k * sectorA;
                    float swept = side > 0 ? Mathf.Repeat(a - throat, 2f * Mathf.PI)
                                           : Mathf.Repeat(throat - a, 2f * Mathf.PI);
                    if (swept > 1e-3f && swept < Mathf.PI) rim.Add((swept, a));
                }
                rim.Sort((p, q) => p.swept.CompareTo(q.swept));
                // Where the rim's chord crosses the END of the street, if it
                // crosses inside the carriageway: the third corner of the notch
                // filled by NbThroatPlate below.
                Vector3? notch = null;
                foreach (var r in rim)
                {
                    var p = new Vector3(HomeStreetX + Mathf.Cos(r.a) * NbBulbR, 0f,
                                        NbBulbCz + Mathf.Sin(r.a) * NbBulbR);
                    p.y = NbRoadY(p.z) + NbBulbLift;
                    if (edge.Count == street.nz + 1)
                    {
                        // THE CORNER AT THE THROAT, turned where the disc's rim
                        // actually crosses the street's edge line — not at the
                        // throat. The street's last edge vertex is on the TRUE
                        // circle, but the head there is a chord from the rim
                        // vertex before the throat to this one, sagging inside
                        // the circle. Laid straight from the throat to this
                        // vertex the stone's foot stood off the tarmac over a
                        // slot of lawn 8 cm down: 5.1 cm wide on the east side,
                        // 1.0 on the west (the disc's sectors start at +x, so
                        // the two throats cut their chords differently). So the
                        // foot runs on up the edge line to the chord before it
                        // turns onto the rim.
                        //
                        // Both ends of that few centimetres carry the MITRE.
                        // With the street's own +x at the first, the stone's back
                        // edge would run south while its foot runs north — a
                        // folded quad, two kerb tops in one plane z-fighting.
                        float prevA = r.a - side * sectorA;
                        float px0 = HomeStreetX + Mathf.Cos(prevA) * NbBulbR;
                        float pz0 = NbBulbCz + Mathf.Sin(prevA) * NbBulbR;
                        float lineX = HomeStreetX + side * HomeRoadW * 0.5f;
                        float t = (lineX - px0) / (p.x - px0);
                        Vector3 chordOut = new Vector3(Mathf.Cos((prevA + r.a) * 0.5f), 0f,
                                                       Mathf.Sin((prevA + r.a) * 0.5f));
                        Vector3 mitre = (new Vector3(side, 0f, 0f) + chordOut).normalized;
                        outward[street.nz] = mitre;
                        float zc = Mathf.Lerp(pz0, p.z, t);
                        if (t > 0f && t < 1f && zc > edge[street.nz].z + 1e-3f)
                        {
                            edge.Add(new Vector3(lineX, NbRoadY(zc) + NbBulbLift, zc));
                            outward.Add(mitre);

                            // AND THE TARMAC SIDE OF THE SAME SAG. Running the foot
                            // up the edge line closed the slot under the STONE; the
                            // chord still crosses the street's end row inside the
                            // carriageway, so between that row, the chord and the
                            // foot a triangle of neither surface was left — 11 cm
                            // by 5.6 cm on the east side, lawn 8.8 cm down, right
                            // where a wheel runs along the gutter (measured off
                            // the built meshes, by a replica of TownProbe's lip
                            // scan, which fails it).
                            Vector3 end = edge[street.nz];
                            float u = (end.z - pz0) / (p.z - pz0);
                            float xb = Mathf.Lerp(px0, p.x, u);
                            if (u > 0f && u < 1f && (xb - lineX) * side < 0f)
                                notch = new Vector3(xb, end.y, end.z);
                        }
                    }
                    edge.Add(p);
                    outward.Add(new Vector3(Mathf.Cos(r.a), 0f, Mathf.Sin(r.a)));
                    if (p.z > NbBulbCz && Mathf.Abs(p.x - HomeStreetX) < HomeDriveW * 0.5f) break;
                }

                // THE HEAD'S CENTIMETRE, TAKEN OVER A FACET OF IT. The head is
                // laid NbBulbLift - NbStreetLift proud of the street, and the
                // foot used to take that whole step between the street's last
                // edge vertex and the station where the rim's chord crosses the
                // edge line — 1.2 cm apart on the west side. A 1 cm rise in
                // 1.2 cm of foot tilts the ramp collider's two triangles there
                // to a 0.64 normal, past CollisionResponder's 0.7 landing test:
                // two faces of the one collider this kerb promises has none
                // (the self-test's "2 of 480 triangles steeper than a landing").
                // So the foot's lift is blended from the street's to the head's
                // over the first NbBulbCell of arc past the throat — the length
                // of one of the disc's own facets — and the step becomes a 0.5%
                // grade along the gutter. Where the foot runs under the head's
                // rim by that blend it is at most the centimetre, and the head
                // is the higher of the two: a wheel leaving the head steps down
                // it onto the ramp, well inside the owner's inch.
                float arc = 0f;
                for (int k = street.nz + 1; k < edge.Count; k++)
                {
                    Vector3 was = edge[k - 1], at = edge[k];
                    arc += Mathf.Sqrt((at.x - was.x) * (at.x - was.x) + (at.z - was.z) * (at.z - was.z));
                    at.y = NbRoadY(at.z) + Mathf.Lerp(NbStreetLift, NbBulbLift, Mathf.Clamp01(arc / NbBulbCell));
                    edge[k] = at;
                }
                // After the blend, so the plate's corner on the edge line is the
                // foot's own point.
                if (notch.HasValue)
                    NbThroatPlate(parent, m, "NbStreetThroat" + (side < 0 ? "W" : "E"),
                                  edge[street.nz], notch.Value, edge[street.nz + 1]);

                bool InGap(Vector3 p)
                {
                    if (p.z > NbBulbCz && Mathf.Abs(p.x - HomeStreetX) < HomeDriveW * 0.5f) return true;
                    return p.z <= NbBulbThroatZ + 1e-3f && NbInDriveway(side, p.z);
                }
                var runs = WorldKit.KerbRuns(edge, outward, InGap, WorldKit.KerbHeightM,
                                             taperStart: true, taperEnd: true, maxPitch: 4f);
                string tag = side < 0 ? "W" : "E";
                for (int r = 0; r < runs.Count; r++)
                {
                    WorldKit.Kerb(root.transform, "NbKerb" + tag + r, runs[r], m.kerb, WorldKit.RoadLayer);
                    WorldKit.KerbVerge(root.transform, "NbVerge" + tag + r, runs[r],
                                       NbLatticeAt, m.grass, 16f);
                    built++;
                }
            }
            Log("[Neighborhood] kerbs: " + built + " runs, dropped at " + (NbPlotCount * 2) +
                " drives and your own");
        }

        /// <summary>
        /// The corner of tarmac the turning head's polygon leaves out at the
        /// throat: one triangle on the ROAD LAYER, drawn in the street's own
        /// material on its world UVs, between the street's last edge vertex
        /// (<paramref name="corner"/>), the point where the rim's chord crosses
        /// the street's end row (<paramref name="onEnd"/>) and the kerb foot's
        /// station where the same chord crosses the edge line
        /// (<paramref name="onLine"/>).
        ///
        /// A plate rather than a longer street, because the street's length is
        /// also the lawn lattice's (SolveNbLattice sizes its grid off
        /// NbStreetLen) and every bench, footing and feather on the street is
        /// measured against that lattice; and rather than a rounder head,
        /// because no sector count puts a rim vertex on both throats. Its three
        /// corners are points the street, the head and the kerb foot already
        /// have, so it meets each of them edge to edge and shares a plane with
        /// none: level with the street's end row and with the kerb's foot, and
        /// along the head's chord a centimetre under its rim — the same
        /// NbBulbLift - NbStreetLift step the head stands over the street
        /// everywhere the two overlap, climbed inward from the plate. A notch smaller than a coin — the west side's is 1.9 cm by
        /// 1.2 — is still built: a sliver triangle is a MeshCollider PhysX
        /// cooks, while one with no area is one it refuses.
        /// </summary>
        static void NbThroatPlate(Transform parent, TownMats m, string name,
                                  Vector3 corner, Vector3 onEnd, Vector3 onLine)
        {
            Vector3 n = Vector3.Cross(onEnd - corner, onLine - corner);
            if (n.magnitude * 0.5f < 1e-5f) return;
            // Wound to face up whichever side of the street this is.
            var verts = n.y >= 0f ? new[] { corner, onEnd, onLine } : new[] { corner, onLine, onEnd };
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.uv = System.Array.ConvertAll(verts, v => new Vector2(v.x / 12f, v.z / 12f));
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            WorldKit.SaveMesh(mesh, name);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.layer = WorldKit.RoadLayer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m.road;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>
        /// Where the street simply stops, at the run-off: the lawn graded up to
        /// the end of the tarmac and round both corners into the verges.
        ///
        /// The end was an 8 cm square edge onto grass, reachable by anyone who
        /// turns back at the junction line and drives on — and it is the edge a
        /// car coming back up out of the run-off meets head on.
        /// </summary>
        static void BuildNbStreetEnd(Transform parent, TownMats m)
        {
            float y = NbRoadY(NbStreetEnd) + NbStreetLift;
            float half = HomeRoadW * 0.5f + WorldKit.KerbWidthM;
            var edge = new List<Vector3>();
            var outward = new List<Vector3>();
            void Fan(float x, Vector3 from, Vector3 to)
            {
                for (int f = 0; f <= 4; f++)
                {
                    edge.Add(new Vector3(x, y, NbStreetEnd));
                    outward.Add(Vector3.Slerp(from, to, f / 4f).normalized);
                }
            }
            // Round the west corner from the verge's direction to the end's,
            // across, and round the east corner back to the verge's. The kerb
            // is dropped to flush at the end (taperStart in BuildNbKerbs), so
            // the corners are at the street's own height.
            Fan(HomeStreetX - half, Vector3.left, Vector3.back);
            int steps = Mathf.CeilToInt(half);
            for (int k = 1; k < steps; k++)
            {
                edge.Add(new Vector3(HomeStreetX - half + 2f * half * k / steps, y, NbStreetEnd));
                outward.Add(Vector3.back);
            }
            Fan(HomeStreetX + half, Vector3.back, Vector3.right);
            WorldKit.Feather(parent, "NbStreetEndVerge", edge, outward, NbLatticeAt, m.grass, 16f);
        }

        /// <summary>
        /// Is this station of THIS SIDE of the street inside a driveway
        /// entrance?
        ///
        /// Exactly the drive's own width: the kerb is not built at all across
        /// it (the drive runs out to the tarmac's edge) and falls to flush over
        /// WorldKit.KerbTaperM either side, so the stone never ends standing up
        /// beside the concrete.
        ///
        /// PER SIDE. Dropping both sides wherever either had a drive sounded
        /// tidy and took 84 of 200 segments out — the two rows' drives are
        /// 9.9 m apart in z, so between them they punched holes through nearly
        /// half the kerb on the street, most of them opposite a driveway rather
        /// than at one.
        /// </summary>
        static bool NbInDriveway(int side, float z)
        {
            for (int i = 0; i < NbPlotCount; i++)
                if (Mathf.Abs(z - NbDriveZ(side, NbPlotZ(i))) < NbDriveW * 0.5f) return true;
            return false;
        }

        /// <summary>
        /// The neighbours: a house, a drive and sometimes a car, down both
        /// sides of the street.
        ///
        /// The owner asked for them by implication — "I like that extra cars
        /// are parked in the driveway and on the street" — and they are what
        /// stops the map being one house in a field. Deterministic off a fixed
        /// seed: a street that reshuffled between visits would read as the game
        /// losing its place, which is the same argument SellerLotWorld makes
        /// about its plots.
        /// </summary>
        static void BuildNbPlots(Transform parent, TownMats m)
        {
            var lots = new GameObject("Neighbours");
            lots.transform.SetParent(parent, false);

            string[] parkedKeys = { "euro_hatch", "volvo_estate", "classic_van",
                                    "bmw_e30", "landrover", "audi_saloon", "jdm_pickup" };
            var rng = new System.Random(4071);
            int parked = 0;

            // Start clear of your own drive and stop clear of the junction, so
            // neither the house you live in nor the menu you leave by has a
            // neighbour's garden across it.
            int plot = 0;
            for (int pi = 0; pi < NbPlotCount; pi++)
            {
                float z = NbPlotZ(pi);
                for (int side = -1; side <= 1; side += 2)
                {
                    string tag = (plot++).ToString();
                    float x = HomeStreetX + side * NbSetback;
                    // Facing the street, which is the whole reason the houses
                    // read as a street rather than as a field of sheds.
                    Vector3 facing = new Vector3(-side, 0f, 0f);

                    // A LEVEL BENCH, CUT AND FILLED INTO THE SLOPE.
                    //
                    // "Houses are often built on hills, but use concrete
                    // foundations to level them out." Exactly so, and it is the
                    // only honest way to put a rectangular building on a
                    // gradient. THE BENCH IS IN THE GROUND ITSELF — see
                    // NbGroundY — so there is no lawn slab here any more: the
                    // land arrives already levelled where the house goes and
                    // already sloping where the garden is, and the two cannot
                    // disagree because they are the same function.
                    float padY = NbPadY(side, z);

                    // THE RETAINING WALLS, one per garden edge that could not be
                    // graded (NbEdgesOf) — a graded edge has nothing to retain.
                    // Each is sized off the worst drop along ITS face, at the
                    // batter's toe: on a cross-slope the low corner is not the
                    // low side, and a face that happens to land level gets no
                    // wall at all.
                    //
                    // They were one box under the whole bench, and they are its
                    // RIM now — the batter's width, along each face — because a
                    // graded edge beside a walled one would otherwise have shown
                    // the box's side standing up out of the graded slope.
                    //
                    // THEY STOP WHERE THE DRIVE STOPS. The walls wrap the two
                    // sides and the back and leave the street face open: the
                    // drive comes up that face, and the street edge is the one
                    // NbPadY holds near road level, so it has almost no drop to
                    // retain.
                    //
                    // AND A CRITICAL ONE IS SOLID. A stretch of face whose fall
                    // RoadsideRules.IsCriticalFall calls critical is a real
                    // retaining wall — the one barrier the owner's rule warrants
                    // off the road — so what a car meets below it is the concrete
                    // it can see rather than a 66-degree grass facet it cannot
                    // climb, and what it rolls onto from the garden is the wall's
                    // top. Lesser stretches stay drawing only, as every footing
                    // was; see NbFooting for why the fall is the FINISHED ground's.
                    var edges = NbEdgesOf(pi, side);
                    float wallInner = 18.5f;                            // where the walls start
                    float wallOuter = NbSetback + 2f + NbBenchW * 0.5f + NbBatter;
                    float wallHalfZ = NbBenchD * 0.5f + NbBatter;
                    if (!edges.backGraded)
                        NbFooting(lots.transform, m, "NbFooting" + tag + "Back", padY,
                            side, z, 0, wallOuter - NbBatter, wallOuter, -wallHalfZ, wallHalfZ);
                    if (!edges.zLoGraded)
                        NbFooting(lots.transform, m, "NbFooting" + tag + "ZLo", padY,
                            side, z, -1, wallInner, wallOuter, -wallHalfZ, -wallHalfZ + NbBatter);
                    if (!edges.zHiGraded)
                        NbFooting(lots.transform, m, "NbFooting" + tag + "ZHi", padY,
                            side, z, 1, wallInner, wallOuter, wallHalfZ - NbBatter, wallHalfZ);

                    // TURNED THE RIGHT WAY ROUND, AND STANDING ON ITS OWN BENCH.
                    //
                    // yawOffsetDeg: 0f. WorldKit.Place defaults to 180, and its
                    // doc comment claims that is right for every prop in the
                    // project — it is not right for this pack. Measured in
                    // Blender off the shipped FBX, house_simple's garage door,
                    // front door and concrete path are all at Unity local +Z
                    // and only the veranda is at -Z, so LookRotation already
                    // aims the front where `facing` says and the extra 180
                    // turned it to face the back gardens. Both rows showed the
                    // street a veranda and a shed door, with the drive running
                    // to a blank wall: "homes are facing the wrong way,
                    // driveway not leading to garage." house_hero is placed
                    // with yawOffsetDeg 0 two files over for the same reason,
                    // and the two models carry the same door at the same
                    // coordinates.
                    //
                    // x + side * 2f, not 4f: the bench NbGroundY cuts is centred
                    // at NbSetback + 2, and the house was two metres outboard of
                    // it. The model is 16.33 m deep, so it overhung the graded
                    // ground by 1.67 m at the back and stood on raw hillside —
                    // which is the other half of "houses ... have hills going
                    // through the floors", and which no retaining wall covered,
                    // because the wall is sized by where land FALLS AWAY and a
                    // corner where it rises contributes nothing.
                    var house = WorldKit.Place(lots.transform,
                        TownHouseDir + "/house_simple.fbx", "NbHouse" + tag,
                        new Vector3(x + side * 2f, padY, z), facing,
                        PSXRacing.City.CityProps.PackScale,
                        glass: false, yawOffsetDeg: 0f);
                    if (house != null)
                    {
                        // SEAT IT. WorldKit.Place OVERWRITES the instantiated
                        // root's position, which throws away the node
                        // translation the pack uses to lift its mesh onto its
                        // own base — house_simple spans -2.865 to +6.226 about
                        // that origin, so at 0.81 scale the model lands 2.27 m
                        // UNDERGROUND. Every other Place call site in the
                        // project follows it with this line; this one did not,
                        // and the owner reported the result in three words:
                        // "houses should never be underground."
                        WorldKit.SeatOnGround(house, padY);
                        WorldKit.AddColliders(house, WorldKit.SolidLayer);
                        // AND CHECK THE CONSTANT AGAINST THE MODEL, once.
                        //
                        // NbGarageOffsetZ cannot be measured here and used —
                        // the height field grades the drive corridor long
                        // before any house is instantiated, and a height field
                        // that depended on a measurement taken later in the
                        // build would be a different street every time the
                        // order changed. So the constant stays a constant and
                        // this says, in the build log, whether it is still
                        // true. It was not, by 9.86 m and a sign, for the whole
                        // life of the street, and nothing in the build said a
                        // word: a driveway to a blank wall renders perfectly.
                        if (!nbGarageChecked) NbCheckGarage(house, side, z);
                    }

                    // The drive, from the kerb to the front of the house.
                    //
                    // IT STOPS AT THE KERB. It used to run 0.6 m past it onto
                    // the carriageway, on the reasoning that "a drive that
                    // merely touches one leaves a strip of lawn for a wheel to
                    // drop onto". The premise was wrong: the tarmac edge and
                    // the kerb's inner face are the SAME line, so a drive ending
                    // there shares an edge with the road and there is no gap for
                    // lawn to appear in. What the overlap actually bought was
                    // 42 square metres of concrete lying on the street, five of
                    // them stepping up out of it by as much as 12 cm.
                    //
                    // The lawn hazard the comment feared is real, but it lives
                    // one metre further out and the fix for it is the dropped
                    // kerb in BuildNbKerbs, not a wider slab.
                    //
                    // AND IT ENDS AT THE GARAGE DOOR. It ended at a flat 18.5,
                    // which was a guess at "the front of the house" and is two
                    // metres short of the door PackProbe measures — a gap the
                    // model fills with its own concrete front path, so the
                    // drive arrived at a footpath rather than at a garage.
                    // Bounded by the measurement now, like everything else on
                    // this street.
                    float kerbX = HomeStreetX + side * NbKerbOut;
                    float driveZ = NbDriveZ(side, z);
                    // The same grid SolveNbLattice held the lawn under.
                    var driveGrid = NbDriveGrid(side, z);
                    // THE DRIVE RAMPS, because it has to: it starts at the kerb
                    // and ends on a pad that is not at kerb height. Interpolated
                    // across its own width, from the road's own surface at the
                    // street end to the pad at the house end, so it meets both
                    // exactly and there is no step at either.
                    // LAID ON THE LAND, not interpolated across it. The first
                    // version eased from the road's surface to the pad in a
                    // smoothstep of its own, which meets both ENDS exactly and
                    // agrees with the ground at neither: a strip of concrete
                    // buried at one point of its run and a foot in the air at
                    // the next. Reading NbGroundY gets the ramp for free —
                    // the field is already pinned to the road at the kerb and
                    // already level on the bench at the house.
                    //
                    // AND IT IS A SLAB OF CONCRETE, not a decal. "Driveways do
                    // not have any depth/thickness. They should be a few inches
                    // thick." They were a single-sided skin: from the side, a
                    // drive was a painted stripe on the lawn with nothing
                    // holding it up, and from a low camera it disappeared
                    // edge-on entirely.
                    //
                    // The two LAWN edges only. The slab runs in x, so those are
                    // its z faces. The street end must not have one — it would
                    // be a 30 cm wall in the gutter, on the one edge the car
                    // crosses at speed — and the house end is inside the
                    // building.
                    WorldKit.GridSlab(lots.transform, "NbDrive" + tag,
                        driveGrid.centre, driveGrid.sizeX, driveGrid.sizeZ, NbDriveCell,
                        m.drive, true, 5f, WorldKit.RoadLayer,
                        NbDriveSurfaceY,
                        WorldKit.SlabEdge.SidesZ);

                    // AND THE LAWN GRADED UP TO BOTH OF THOSE EDGES. A drive
                    // with a side to it still stood 9 cm over the lawn along its
                    // whole length — more where the lawn mesh had to be sunk
                    // under a crease — and its skirt has no collider, so what a
                    // car met coming back onto it was the slab's edge. A feather
                    // on each side (WorldKit.Feather), on the drive's own edge
                    // vertices, from the kerb line to the garage. NOT named
                    // "NbDrive…": the self-test finds drives by that prefix.
                    for (int e = -1; e <= 1; e += 2)
                    {
                        var edge = new List<Vector3>();
                        var outs = new List<Vector3>();
                        int row = e < 0 ? 0 : driveGrid.nz;
                        for (int c = 0; c <= driveGrid.nx; c++)
                        {
                            float wx = driveGrid.WorldX(c), wz = driveGrid.WorldZ(row);
                            edge.Add(new Vector3(wx, NbDriveSurfaceY(wx, wz), wz));
                            outs.Add(new Vector3(0f, 0f, e));
                        }
                        WorldKit.Feather(lots.transform, "NbVergeDrive" + tag + (e < 0 ? "S" : "N"),
                                         edge, outs, NbLatticeAt, m.grass, 16f);
                    }

                    // Two plots in three get a car, on the drive or at the
                    // kerb. Not all of them: a street where every house has a
                    // car outside reads as a car park with houses behind it.
                    if (rng.NextDouble() < 0.66)
                    {
                        var def = CarModelLibrary.Load(parkedKeys[parked % parkedKeys.Length]);
                        if (def != null)
                        {
                            bool onDrive = rng.NextDouble() < 0.55;
                            // Seated on what it is standing on, not on y=0.
                            // A car parked on a drive that climbs two metres to
                            // the house is two metres in the air otherwise.
                            // ON the drive, which means on the drive's own
                            // centreline — that moved with the garage, so a car
                            // parked off the old fixed z sat on the lawn beside
                            // it for half the street.
                            float parkX = HomeStreetX + side * 12f;
                            Vector3 at = onDrive
                                ? new Vector3(parkX,
                                              NbDriveSurfaceY(parkX, driveZ) + 0.02f, driveZ)
                                // AT the kerb means ON the tarmac, inboard of
                                // it. This was kerbX + side * 2.2, which is two
                                // metres OUTSIDE the carriageway — every
                                // kerbside car on the street was parked on the
                                // grass verge with its wheels in a lawn.
                                : new Vector3(kerbX - side * 1.3f,
                                              NbRoadY(z + 9f) + 0.02f, z + 9f);
                            var go = new GameObject("Parked_" + def.key);
                            go.transform.SetParent(lots.transform, false);
                            go.transform.position = at;
                            // On the drive it points at the house; at the kerb
                            // it runs with the street, some of them the other
                            // way, and none of them dead straight. A row of
                            // perfectly aligned cars reads as a texture.
                            Vector3 nose = onDrive ? -facing
                                : (rng.NextDouble() < 0.5 ? Vector3.forward : Vector3.back);
                            // AND IT SITS ON THE SLOPE IT IS PARKED ON.
                            //
                            // Seating a car by its Y and then turning it with a
                            // level LookRotation puts it flat on a driveway that
                            // climbs 15% — nose in the air at one end, boot in
                            // the concrete at the other. "Cars are level in
                            // driveways, even when driveways are not level."
                            // Measure the surface a wheelbase apart along the
                            // car's own nose and look along THAT, which pitches
                            // it and leaves the yaw alone.
                            Vector3 ahead = at + nose * 2.2f, behind = at - nose * 2.2f;
                            ahead.y = onDrive ? NbGroundY(ahead.x, ahead.z) : NbRoadY(ahead.z);
                            behind.y = onDrive ? NbGroundY(behind.x, behind.z) : NbRoadY(behind.z);
                            Vector3 alongSlope = ahead - behind;
                            if (alongSlope.sqrMagnitude < 1e-4f) alongSlope = nose;
                            go.transform.rotation =
                                Quaternion.LookRotation(alongSlope.normalized, Vector3.up)
                                * Quaternion.Euler(0f, (float)(rng.NextDouble() * 6.0 - 3.0), 0f);
                            DressProp(go.transform, def, rng.Next(Mathf.Max(1, def.SkinCount)));
                            foreach (var t in go.GetComponentsInChildren<Transform>())
                                t.gameObject.isStatic = true;
                            parked++;
                        }
                    }
                }
            }
            Log("[Neighborhood] " + parked + " cars parked along the street.");
        }

        /// <summary>A face shorter than this gets no wall: the lawn batter
        /// shows instead.</summary>
        const float NbFootingMinDrop = 0.12f;
        /// <summary>A wall's top sits this far under the DRAWN lawn — which is
        /// NbLawnSink under the bench — so the grass draws over it where the
        /// bench is level and only the faces the land has fallen away from
        /// show. (It sat 5 cm under the bench's design height, which is 1 cm
        /// ABOVE the drawn lawn: every footing's top poked through the garden.)</summary>
        const float NbFootingUnderLawn = 0.05f;
        /// <summary>How far under the worst of the land a wall's foot goes, so
        /// no facet of the 3 m lawn lattice surfaces under it and shows daylight
        /// beneath the house.</summary>
        const float NbFootingBuried = 0.5f;

        /// <summary>Stations along a footing's face at which its drop is
        /// measured: about one a metre on the 17.5 m side walls.</summary>
        const int NbFootingSamples = 16;

        /// <summary>
        /// One retaining wall on one garden edge of a bench: the rim of the
        /// old footing box along that face, from the bench's edge to the
        /// batter's toe.
        ///
        /// ITS DROP IS WHAT IT STANDS OVER — the FINISHED ground at the toe,
        /// not the raw hillside. On the side of a plot its drive runs up, the
        /// drive corridor (NbGroundY) holds the land up across that whole edge,
        /// and the hillside overstated the fall there by up to 2.5 m (measured on
        /// the replica): plot 0's east drive-side wall read 1.91 m against the
        /// land, went solid end to end, and stands over at most 1.05 m of real
        /// fall; plot 1's put a 44 cm concrete face 1.6 m off the side of its
        /// drive, over ground only 0.6 m down — a wall standing on a slope a car
        /// rolls off that drive onto and back from.
        ///
        /// And A WALL IS SOLID ONLY ALONG THE STRETCH WHOSE FALL IS CRITICAL
        /// (RoadsideRules.IsCriticalFall at each station): the box is drawn
        /// whole and the collider covers each critical run of the face. The
        /// rest of it is drawing only, as every footing's lesser face is.
        /// </summary>
        /// <param name="which">The face the drop is measured on: 0 the back,
        /// -1 / +1 the low- and high-z sides.</param>
        /// <param name="out0">The wall's extent, in metres out from the street's
        /// crown (<paramref name="out0"/>..<paramref name="out1"/>) and in z
        /// from the plot's centre (<paramref name="z0"/>..<paramref name="z1"/>).</param>
        static void NbFooting(Transform parent, TownMats m, string name, float padY,
                              int side, float plotZ, int which,
                              float out0, float out1, float z0, float z1)
        {
            // The drop along the face, out at the batter's toe.
            var drops = new float[NbFootingSamples + 1];
            float drop = 0f;
            for (int k = 0; k <= NbFootingSamples; k++)
            {
                float t = k / (float)NbFootingSamples;
                float ox = which == 0 ? out1 : Mathf.Lerp(out0, out1, t);
                float oz = which == 0 ? Mathf.Lerp(z0, z1, t) : which * (NbBenchD * 0.5f + NbBatter);
                drops[k] = padY - NbGroundY(HomeStreetX + side * ox, plotZ + oz);
                drop = Mathf.Max(drop, drops[k]);
            }
            if (drop <= NbFootingMinDrop) return;
            float top = padY - NbLawnSink - NbFootingUnderLawn;
            float bottom = padY - drop - NbFootingBuried;
            WorldKit.Box(parent, name,
                new Vector3(HomeStreetX + side * (out0 + out1) * 0.5f, (top + bottom) * 0.5f,
                            plotZ + (z0 + z1) * 0.5f),
                new Vector3(out1 - out0, top - bottom, z1 - z0), m.drive, solid: false);

            // The colliders: one box per run of critical stations, reaching half
            // a station past each end of the run (and no further than the face).
            int solid = 0;
            for (int k = 0; k <= NbFootingSamples; k++)
            {
                if (!RoadsideRules.IsCriticalFall(drops[k], NbBatter)) continue;
                int k0 = k;
                while (k < NbFootingSamples && RoadsideRules.IsCriticalFall(drops[k + 1], NbBatter)) k++;
                float t0 = Mathf.Max(0f, (k0 - 0.5f) / NbFootingSamples);
                float t1 = Mathf.Min(1f, (k + 0.5f) / NbFootingSamples);
                // Along the face: out from the crown for a side wall, z for the back.
                float a0 = which == 0 ? out0 : Mathf.Lerp(out0, out1, t0);
                float a1 = which == 0 ? out1 : Mathf.Lerp(out0, out1, t1);
                float b0 = which == 0 ? Mathf.Lerp(z0, z1, t0) : z0;
                float b1 = which == 0 ? Mathf.Lerp(z0, z1, t1) : z1;
                var col = new GameObject(name + "Solid" + solid++);
                col.transform.SetParent(parent, false);
                col.transform.position = new Vector3(HomeStreetX + side * (a0 + a1) * 0.5f,
                                                     (top + bottom) * 0.5f, plotZ + (b0 + b1) * 0.5f);
                col.isStatic = true;
                col.AddComponent<BoxCollider>().size = new Vector3(a1 - a0, top - bottom, b1 - b0);
            }
        }

        static void BuildNbBounds(Transform parent)
        {
            var b = new GameObject("NbBounds");
            b.transform.SetParent(parent, false);
            void Wall(string name, Vector3 at, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(b.transform, false);
                go.transform.position = at;
                go.layer = WorldKit.SolidLayer;
                go.AddComponent<BoxCollider>().size = size;
            }
            // Taller than the town s, because the land under them now moves
            // several metres and a 6 m wall on a 3.5 m dip is a 2.5 m wall.
            // 40 m tall and centred BELOW the datum, because the land under
            // them now falls thirteen metres from one end of the street to the
            // other and a 16 m wall hung off y=3 leaves the far end of the map
            // open under its own fence.
            float hx = NbHalfWidth;
            // The south end moved out by the run-off, so the side walls have to
            // follow it: leaving them where they were opens the map at both
            // southern corners.
            float southZ = NbStreetEnd - NbRunoff;
            float len = HomeStreetTop - southZ + 40f;
            float midZ = (HomeStreetTop + southZ) * 0.5f;
            Wall("W", new Vector3(HomeStreetX - hx, -5f, midZ), new Vector3(1f, 40f, len));
            Wall("E", new Vector3(HomeStreetX + hx, -5f, midZ), new Vector3(1f, 40f, len));
            // North is BEHIND your house — far enough back that the building
            // stands in a garden rather than against a wall.
            Wall("N", new Vector3(HomeStreetX, -5f, TownHouseZ + 26f),
                 new Vector3(hx * 2f, 40f, 1f));
            // South stands BEHIND the depart line, not on top of it.
            //
            // It used to sit six metres past the tarmac, which is three and a
            // half metres past the junction volume's own south face — so the
            // last thing on the way out of the neighbourhood was a wall, with
            // the menu that was meant to be the way out ending BEHIND the car
            // that had stopped against it. It is a backstop now: the line is at
            // NbStreetEnd - 5 with eleven metres of body length either side of
            // it, and this is thirteen and a half metres of grass behind that.
            Wall("S", new Vector3(HomeStreetX, -5f, southZ),
                 new Vector3(hx * 2f, 40f, 1f));
        }

        static Transform[] BuildNbRespawns(Transform parent)
        {
            var root = new GameObject("NbRespawns");
            root.transform.SetParent(parent, false);
            var list = new List<Transform>();
            // Down the crown of the street, facing the junction — a car put
            // back on its own road should be pointing the way out of it.
            for (float z = NbBulbThroatZ - 6f; z > NbStreetEnd + 12f; z -= 34f)
            {
                var go = new GameObject("NbRespawn");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(
                    new Vector3(HomeStreetX, NbRoadY(z) + 0.4f, z),
                    Quaternion.LookRotation(Vector3.back, Vector3.up));
                list.Add(go.transform);
            }
            return list.ToArray();
        }
    }
}
