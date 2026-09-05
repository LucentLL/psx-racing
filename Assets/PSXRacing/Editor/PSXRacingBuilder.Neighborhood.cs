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
        static void BuildNbGround(Transform parent, TownMats m)
        {
            float streetLen = HomeStreetTop - NbStreetEnd;
            float midZ = (HomeStreetTop + NbStreetEnd) * 0.5f;

            // Past the fog wall in every direction, and SUNK below the tarmac
            // for the reason the town's is: at the grazing angle you see a
            // street from inside a car, two centimetres is not enough
            // separation and a band of grass crawls across the carriageway.
            WorldKit.GridSlab(parent, "NbGround", new Vector3(HomeStreetX, -0.06f, midZ),
                240f, streetLen + 220f, 4f, m.grass, true, 16f);

            // The street itself, on the ROAD LAYER — CarController decides
            // onRoad by layer number, so tarmac left on layer 0 is a whole
            // session of off-road grip with nothing on screen to say so.
            WorldKit.GridSlab(parent, "NbStreet",
                new Vector3(HomeStreetX, 0.02f, midZ),
                HomeRoadW, streetLen, 4f, m.road, true, 12f, WorldKit.RoadLayer);

            // Paint, not surface: no collider and off the road layer, because a
            // strip standing proud of the tarmac is something a wheel climbs.
            // Broken, because this is a residential street and a solid centre
            // line down one would be wrong in a way that is quietly obvious.
            for (float z = NbStreetEnd + 6f; z < HomeStreetTop - 6f; z += 9f)
                WorldKit.GridSlab(parent, "NbLine", new Vector3(HomeStreetX, 0.035f, z),
                    0.14f, 3.6f, 2f, m.line, false, 4f);

            for (int s = -1; s <= 1; s += 2)
                WorldKit.Box(parent, "NbKerb" + s,
                    new Vector3(HomeStreetX + s * (HomeRoadW * 0.5f + 0.2f), 0.08f, midZ),
                    new Vector3(0.4f, 0.16f, streetLen), m.kerb, false);
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
            for (float z = HomeStreetTop - 14f; z > NbStreetEnd + 26f; z -= NbPlotPitch)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = HomeStreetX + side * NbSetback;
                    // Facing the street, which is the whole reason the houses
                    // read as a street rather than as a field of sheds.
                    Vector3 facing = new Vector3(-side, 0f, 0f);

                    WorldKit.GridSlab(lots.transform, "NbYard",
                        new Vector3(x + side * 2f, -0.04f, z), 26f, NbPlotPitch - 3f,
                        3f, m.grass, false, 10f);

                    var house = WorldKit.Place(lots.transform,
                        TownHouseDir + "/house_simple.fbx", "NbHouse",
                        new Vector3(x + side * 4f, 0f, z), facing,
                        PSXRacing.City.CityProps.PackScale, glass: true);
                    if (house != null) WorldKit.AddColliders(house, WorldKit.SolidLayer);

                    // The drive, from the kerb to the front of the house. It
                    // OVERLAPS the kerb line, because a drive that merely
                    // touches one leaves a strip of lawn for a wheel to drop
                    // onto — the same correction the home lot's drive carries.
                    float kerbX = HomeStreetX + side * (HomeRoadW * 0.5f - 0.6f);
                    float driveX = (kerbX + x - side * 3f) * 0.5f;
                    WorldKit.GridSlab(lots.transform, "NbDrive",
                        new Vector3(driveX, 0.03f, z - 4.5f),
                        Mathf.Abs(x - side * 3f - kerbX), 5.0f, 3f,
                        m.drive, true, 5f, WorldKit.RoadLayer);

                    // Two plots in three get a car, on the drive or at the
                    // kerb. Not all of them: a street where every house has a
                    // car outside reads as a car park with houses behind it.
                    if (rng.NextDouble() < 0.66)
                    {
                        var def = CarModelLibrary.Load(parkedKeys[parked % parkedKeys.Length]);
                        if (def != null)
                        {
                            bool onDrive = rng.NextDouble() < 0.55;
                            Vector3 at = onDrive
                                ? new Vector3(x - side * 6f, 0f, z - 4.5f)
                                : new Vector3(kerbX + side * 2.2f, 0f, z + 6f);
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
            float hx = 58f;
            float len = HomeStreetTop - NbStreetEnd + 40f;
            float midZ = (HomeStreetTop + NbStreetEnd) * 0.5f;
            Wall("W", new Vector3(HomeStreetX - hx, 3f, midZ), new Vector3(1f, 6f, len));
            Wall("E", new Vector3(HomeStreetX + hx, 3f, midZ), new Vector3(1f, 6f, len));
            // North is BEHIND your house — far enough back that the building
            // stands in a garden rather than against a wall.
            Wall("N", new Vector3(HomeStreetX, 3f, TownHouseZ + 26f),
                 new Vector3(hx * 2f, 6f, 1f));
            // South is past the junction. The junction menu is the way out;
            // this is only what stops a player who drove through it.
            Wall("S", new Vector3(HomeStreetX, 3f, NbStreetEnd - 6f),
                 new Vector3(hx * 2f, 6f, 1f));
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
                    new Vector3(HomeStreetX, 0.4f, z),
                    Quaternion.LookRotation(Vector3.back, Vector3.up));
                list.Add(go.transform);
            }
            return list.ToArray();
        }
    }
}
