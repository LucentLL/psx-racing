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
        /// concrete foundations to level them out" is a thing you can see.</summary>
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
        const float NbFirstPlotZ = HomeStreetTop - 14f;
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
            townPizzaKerb = townDealerDoor =
                townYardGate = townHomeDoor = townMechanicDoor = townPaintDoor = null;
            townPizzaHooks = null;
            psxLit = Shader.Find("PSX/Lit");
            if (psxLit == null) throw new System.Exception("PSX/Lit not found");
            matByTex.Clear();
            matByKey.Clear();
            // Same reason the town clears it: BuildLighting reads `track` for
            // the fog scale, and a stage's three-kilometre band on a suburban
            // street would show the far bound wall with sky under it.
            track = null;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGO = BuildLighting();

            var root = new GameObject("Neighborhood");
            var mats = TownMaterials();

            BuildNbGround(root.transform, mats);
            var homeDrive = BuildTownHome(root.transform, mats);
            BuildNbPlots(root.transform, mats);
            BuildNbBounds(root.transform);

            // THE JUNCTION AT THE BOTTOM OF THE STREET. It used to be built
            // inside BuildTownHome, three metres from the main road, because
            // the home lot was a stub off that road. It belongs to the STREET
            // now, so it is placed by whoever built the street — which is also
            // why it moved out of the home lot: the town has no junction any
            // more and would have inherited a menu to nowhere.
            TownTrigger(root.transform, "DepartVenue", TownVenue.Kind.Depart,
                new Vector3(HomeStreetX, 1.4f, NbStreetEnd + 16f),
                new Vector3(HomeRoadW + 6f, 3f, 14f));

            // ---- the player, on their own drive, pointing down the street ----
            var physMat = GetOrCreatePhysMat("CarPhys", 0.15f, 0.05f);
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
        static float NbLandY(float x, float z)
        {
            float road = NbRoadY(z);
            float d = Mathf.Abs(x - HomeStreetX);
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
        static float NbPadY(int side, float plotZ)
        {
            float raw = NbLandY(HomeStreetX + side * (NbSetback + 2f), plotZ);
            float edge = NbRoadY(plotZ - 4.5f);
            float reach = NbDriveGrade * NbDriveRun;
            return Mathf.Clamp(raw, edge - reach, edge + reach);
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
        static float NbKerbOut => HomeRoadW * 0.5f - 0.6f;
        // The batter counts as bench. Landing the ramp on the bench's EDGE
        // still overlapped the two by the width of the batter, and over those
        // three metres the bench pulled the surface up while the ramp was
        // still climbing: 23% on a 15% drive. Ending it where the bench's
        // influence BEGINS gives the ramp three metres of level apron before
        // the garage and makes the measured gradient the designed one.
        static float NbBenchOut => NbSetback + 2f - (NbBenchW * 0.5f + NbBatter);
        static float NbDriveRun => NbBenchOut - NbKerbOut;

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
        /// reach — a bench plus its batter is 21 m across against a 27 m pitch,
        /// so no point is ever on two.
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
            // the hillside over the batter.
            float kBench = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(
                Mathf.InverseLerp(NbBenchW * 0.5f, NbBenchW * 0.5f + NbBatter,
                                  Mathf.Abs(x - cx)),
                Mathf.InverseLerp(NbBenchD * 0.5f, NbBenchD * 0.5f + NbBatter,
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
            float driveZ = pz - 4.5f;
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
        static void BuildNbGround(Transform parent, TownMats m)
        {
            float streetLen = HomeStreetTop - NbStreetEnd;
            float midZ = (HomeStreetTop + NbStreetEnd) * 0.5f;

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
            WorldKit.GridSlab(parent, "NbGround", new Vector3(HomeStreetX, 0f, midZ),
                240f, streetLen + 220f, 3f, m.grass, true, 16f, 0,
                (x, z) => NbGroundY(x, z) - 0.06f);

            // The street itself, on the ROAD LAYER — CarController decides
            // onRoad by layer number, so tarmac left on layer 0 is a whole
            // session of off-road grip with nothing on screen to say so.
            // 2 m cells along it, not 4: the tarmac is what the car drives on
            // and its collider is this mesh now, so the crest of a 7.8% hill
            // has to be a curve rather than a pair of ramps meeting at a hinge.
            WorldKit.GridSlab(parent, "NbStreet",
                new Vector3(HomeStreetX, 0f, midZ),
                HomeRoadW, streetLen, 2f, m.road, true, 12f, WorldKit.RoadLayer,
                (x, z) => NbRoadY(z) + 0.02f);

            // Paint, not surface: no collider and off the road layer, because a
            // strip standing proud of the tarmac is something a wheel climbs.
            // Broken, because this is a residential street and a solid centre
            // line down one would be wrong in a way that is quietly obvious.
            // NUMBERED, and that is not cosmetic. WorldKit.SaveMesh writes one
            // mesh ASSET per name and DELETES the existing one first, so a loop
            // that hands it the same name every time leaves exactly one live
            // mesh and a trail of MeshFilters pointing at deleted assets. This
            // shipped: 22 of these 23 dashes, 13 of 14 neighbour lawns and 13 of
            // 14 driveways rendered nothing at all, which is most of why the
            // street looked bare.
            int dash = 0;
            for (float z = NbStreetEnd + 6f; z < HomeStreetTop - 6f; z += 9f)
                WorldKit.GridSlab(parent, "NbLine" + (dash++),
                    new Vector3(HomeStreetX, 0f, z), 0.14f, 3.6f, 2f, m.line, false, 4f,
                    0, (px, pz) => NbRoadY(pz) + 0.035f);

            // THE KERB IS SEGMENTED, because a kerb is a box and a box cannot
            // bend. One 212 m cube laid on a street that climbs 7.8% either
            // buries itself in the hill or floats off it; eight-metre lengths,
            // each seated and pitched to the road under it, read as a kerb the
            // whole way. The pitch matters as much as the height — a level box
            // on a slope shows a wedge of daylight at one end.
            for (int side = -1; side <= 1; side += 2)
            {
                int seg = 0;
                for (float z = NbStreetEnd; z < HomeStreetTop; z += 8f)
                {
                    float z0 = z, z1 = Mathf.Min(z + 8f, HomeStreetTop);
                    float mid = (z0 + z1) * 0.5f;
                    float pitch = Mathf.Atan2(NbRoadY(z1) - NbRoadY(z0), z1 - z0) * Mathf.Rad2Deg;
                    var kerb = WorldKit.Box(parent, "NbKerb" + side + "_" + (seg++),
                        new Vector3(HomeStreetX + side * (HomeRoadW * 0.5f + 0.2f),
                                    NbRoadY(mid) + 0.08f, mid),
                        new Vector3(0.4f, 0.16f, (z1 - z0) + 0.15f), m.kerb, false);
                    // Pitched about X, which is across the street: the kerb runs
                    // along z, so it is the z-slope it has to lie along.
                    kerb.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);
                }
            }
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
                    float benchX = x + side * 2f;

                    // THE RETAINING WALL, and it is the only thing left that
                    // has to be measured. The bench stands proud of the land it
                    // was cut into by a different amount at each corner — on a
                    // cross-slope the low corner is not the low side — so the
                    // wall is sized off the worst of the four, out where the
                    // land is its own again. A bench that happens to land level
                    // gets no wall at all, which is why this is a measurement
                    // and not a constant.
                    //
                    // IT STOPS WHERE THE DRIVE STOPS. The wall wraps the two
                    // sides and the back of the bench and leaves the street
                    // face open, which is both what the lot needs and what the
                    // geometry needs: the drive comes up that face, and a wall
                    // across it would be a wall across the driveway. Nothing is
                    // lost by it — the street edge is the one edge held near
                    // road level by NbPadY, so it is the one with almost no
                    // drop to retain.
                    float wallInner = NbSetback + 2f - 5f;              // where the drive ends
                    float wallOuter = NbSetback + 2f + NbBenchW * 0.5f + NbBatter;
                    float wallHalfZ = NbBenchD * 0.5f + NbBatter;
                    float drop = 0f;
                    for (int cx = 0; cx <= 1; cx++)
                        for (int cz = -1; cz <= 1; cz += 2)
                            drop = Mathf.Max(drop, padY - NbLandY(
                                HomeStreetX + side * (cx == 0 ? wallInner : wallOuter),
                                z + cz * wallHalfZ));
                    if (drop > 0.12f)
                    {
                        // Its TOP sits 5 cm under the bench, so the lawn draws
                        // over it and only the faces the land has fallen away
                        // from are ever seen; 40 cm deeper than the worst corner
                        // at the bottom, so no facet of the ground grid can
                        // surface under it and show daylight beneath the house.
                        float wallH = drop + 0.45f;
                        WorldKit.Box(lots.transform, "NbFooting" + tag,
                            new Vector3(HomeStreetX + side * (wallInner + wallOuter) * 0.5f,
                                        padY - 0.05f - wallH * 0.5f, z),
                            new Vector3(wallOuter - wallInner, wallH, wallHalfZ * 2f),
                            m.drive, false);
                    }

                    var house = WorldKit.Place(lots.transform,
                        TownHouseDir + "/house_simple.fbx", "NbHouse" + tag,
                        new Vector3(x + side * 4f, padY, z), facing,
                        PSXRacing.City.CityProps.PackScale, glass: true);
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
                    }

                    // The drive, from the kerb to the front of the house. It
                    // OVERLAPS the kerb line, because a drive that merely
                    // touches one leaves a strip of lawn for a wheel to drop
                    // onto — the same correction the home lot's drive carries.
                    float kerbX = HomeStreetX + side * (HomeRoadW * 0.5f - 0.6f);
                    float houseX = x - side * 3f;
                    float driveX = (kerbX + houseX) * 0.5f;
                    float driveZ = z - 4.5f;
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
                    WorldKit.GridSlab(lots.transform, "NbDrive" + tag,
                        new Vector3(driveX, 0f, driveZ),
                        Mathf.Abs(houseX - kerbX), 5.0f, 1.5f,
                        m.drive, true, 5f, WorldKit.RoadLayer,
                        (px, pz) => NbGroundY(px, pz) + 0.05f);

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
                            Vector3 at = onDrive
                                ? new Vector3(x - side * 6f,
                                              NbGroundY(x - side * 6f, z - 4.5f) + 0.07f,
                                              z - 4.5f)
                                : new Vector3(kerbX + side * 2.2f, NbRoadY(z + 6f) + 0.02f, z + 6f);
                            var go = new GameObject("Parked_" + def.key);
                            go.transform.SetParent(lots.transform, false);
                            go.transform.position = at;
                            // On the drive it points at the house; at the kerb
                            // it runs with the street, some of them the other
                            // way, and none of them dead straight. A row of
                            // perfectly aligned cars reads as a texture.
                            Vector3 nose = onDrive ? -facing
                                : (rng.NextDouble() < 0.5 ? Vector3.forward : Vector3.back);
                            go.transform.rotation = Quaternion.LookRotation(nose, Vector3.up)
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
            float hx = 58f;
            float len = HomeStreetTop - NbStreetEnd + 40f;
            float midZ = (HomeStreetTop + NbStreetEnd) * 0.5f;
            Wall("W", new Vector3(HomeStreetX - hx, -5f, midZ), new Vector3(1f, 40f, len));
            Wall("E", new Vector3(HomeStreetX + hx, -5f, midZ), new Vector3(1f, 40f, len));
            // North is BEHIND your house — far enough back that the building
            // stands in a garden rather than against a wall.
            Wall("N", new Vector3(HomeStreetX, -5f, TownHouseZ + 26f),
                 new Vector3(hx * 2f, 40f, 1f));
            // South is past the junction. The junction menu is the way out;
            // this is only what stops a player who drove through it.
            Wall("S", new Vector3(HomeStreetX, -5f, NbStreetEnd - 6f),
                 new Vector3(hx * 2f, 40f, 1f));
        }

        static Transform[] BuildNbRespawns(Transform parent)
        {
            var root = new GameObject("NbRespawns");
            root.transform.SetParent(parent, false);
            var list = new List<Transform>();
            // Down the crown of the street, facing the junction — a car put
            // back on its own road should be pointing the way out of it.
            for (float z = HomeStreetTop - 8f; z > NbStreetEnd + 12f; z -= 34f)
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
