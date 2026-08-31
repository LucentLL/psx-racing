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
    /// TOWN — the small drivable map the LifeSim's errands actually happen in:
    /// your own street at one end, and a main road with a pizza shop, a
    /// forecourt, a used-car lot and a salvage yard along it.
    ///
    /// The player SPAWNS IN THEIR OWN DRIVEWAY, in the car, facing the street.
    /// That is the whole reason the home lot is in this scene rather than the
    /// town being somewhere you are teleported to: "drive your car out of your
    /// garage" is a thing you either do or read about, and doing it costs one
    /// spawn point here against a whole drivable-car stack bolted into the
    /// walk-in garage — a chase camera, a HUD, touch controls, a pause menu, a
    /// session owner, road layers, and a hole cut in the lot's boundary walls,
    /// every one of which fails silently on its own.
    ///
    /// A partial of <see cref="PSXRacingBuilder"/> rather than its own class,
    /// so it can call BuildOneCar, BuildCameraAndHUD and the gas-station
    /// helpers without widening five more methods. Modelled throughout on
    /// PSXRacingBuilder.City.cs, which is the lean drivable-scene template:
    /// no TrackPath, path: null into BuildCameraAndHUD, and therefore no
    /// RaceManager anywhere — CityMode is the session instead.
    ///
    /// NOT a TrackCatalog entry. See TrackCatalog.TownSceneIndex.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        public const string TownScenePath = Root + "/Scenes/Town.unity";

        // ---- the map, in metres ------------------------------------------
        // A small town read at driving speed. Long enough that the main street
        // is a place you accelerate on and short enough that every errand is
        // inside twenty seconds of every other one.
        const float TownStreetHalf = 140f;   // main street runs +/- this in x
        const float TownRoadW = 11f;
        const float HomeStreetX = -110f;     // the turning to your street
        /// <summary>Where your street ENDS. Below the house, not past it —
        /// the road ran through the building on the first cut, and a house
        /// standing in the carriageway still renders, still collides and still
        /// measures. It just is not a house you can park outside.</summary>
        const float HomeStreetTop = 44f;
        const float HomeRoadW = 9f;
        const float TownHouseZ = 63f;        // your house, past the end of the road

        const string TownHouseDir = Root + "/Art/LifeSim/House";
        const string TownHouseTex = TownHouseDir + "/Textures";
        const string TownPizzeria = Root + "/Art/LifeSim/Pizzeria/pizzeria.fbx";
        // The four block shells, with the height each one is SUPPOSED to be.
        // These FBXs do not arrive at real scale — city_building_05 comes in
        // 2.2 m tall — and there is no importer setting that says so, which is
        // how the first town got a dealership you could step over. The figures
        // are CityProps' own, so the town and the streamed city agree about how
        // big a four-storey block is.
        // NOT all eight of the extracted block shells. Photographed, three of
        // the four first chosen were duds — city_building_11 is a featureless
        // cube wearing a wood grain and _08 is a flat brick panel — and a
        // plain cube beside a street reads as a level that has not been built
        // yet. _05 is a proper four-storey with shopfronts and windows, and it
        // is the one this street is made of, varied by HEIGHT and by which way
        // it faces so a row of them is not a row of clones.
        static readonly (string fbx, float tall)[] TownBlocks =
        {
            (Root + "/Art/LifeSim/Pizzeria/city_building_05.fbx", 13.5f),   // showroom
            (Root + "/Art/LifeSim/Pizzeria/city_building_05.fbx", 17.0f),
            (Root + "/Art/LifeSim/Pizzeria/city_building_05.fbx", 11.5f),
            (Root + "/Art/LifeSim/Pizzeria/city_building_05.fbx", 14.5f),
        };
        const string TownTrailerDir = Root + "/Art/LifeSim/Trailer";
        const string TownTrailerTex = TownTrailerDir + "/Textures";

        [MenuItem("PSX Racing/Build Town Scene")]
        public static void BuildTownMenu() => BuildTownScene();

        // Walk-up anchors, filled by the build functions below and wired onto
        // TownWorld at the end — the same static-scratch style the rest of the
        // builder uses. Nulled at the top of every build so a re-run cannot
        // wire last build's transforms.
        static Transform townPizzaKerb, townDealerDoor,
                         townYardGate, townHomeDoor, townMechanicDoor, townPaintDoor;
        /// <summary>Every place a walker can be offered a shift at Tony's:
        /// the frontage, the doorway, and a step inside it. An ARRAY because
        /// one was not enough — see BuildTownStrip.</summary>
        static Transform[] townPizzaHooks;

        static Transform TownAnchor(Transform parent, string name, Vector3 at, Vector3 facing)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at,
                Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward,
                                        Vector3.up));
            return go.transform;
        }

        public static string BuildTownScene()
        {
            townPizzaKerb = townDealerDoor =
                townYardGate = townHomeDoor = townMechanicDoor = townPaintDoor = null;
            townPizzaHooks = null;
            psxLit = Shader.Find("PSX/Lit");
            if (psxLit == null) throw new System.Exception("PSX/Lit not found");
            matByTex.Clear();
            matByKey.Clear();
            // CLEAR THE CURRENT TRACK. BuildLighting reads it to decide the fog
            // scale — a stage is seen at mountain distances — and the town is
            // built after every circuit, so it would otherwise inherit
            // whichever venue happened to be last in the catalog. Atlantic
            // Beach Bridge is a stage, so today that is a three-kilometre fog
            // band on a two-hundred-metre street.
            track = null;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGO = BuildLighting();

            var root = new GameObject("Town");
            var mats = TownMaterials();

            BuildTownGround(root.transform, mats);
            var homeDrive = BuildTownHome(root.transform, mats);
            BuildTownStrip(root.transform, mats);
            BuildTownTrade(root.transform, mats);
            var dealerAnchors = BuildTownDealer(root.transform, mats);
            var yardAnchors = BuildTownYard(root.transform, mats);
            BuildTownStation(root.transform);
            BuildTownBounds(root.transform);

            // ---- the player, on their own drive, pointing at the street ----
            var physMat = GetOrCreatePhysMat("CarPhys", 0.15f, 0.05f);
            var blobMat = MakeBlobShadowMaterial();
            var carsRoot = new GameObject("Cars");
            var player = BuildOneCar(carsRoot.transform, CarSetups[0], isPlayer: true,
                homeDrive.position, homeDrive.rotation, physMat, blobMat);
            var cars = new List<CarController> { player };

            BuildCameraAndHUD(player, cars, null, lightGO.GetComponent<Light>());

            // ---- the session ----
            // A CityMode with no world. DriveSession resolves "the session" to
            // RaceManager or CityMode and nothing else, so a free-roam map has
            // to BE one or StuckRecovery never acts and the respawn key does
            // nothing — with no error anywhere.
            var sessionGO = new GameObject("Session");
            var mode = sessionGO.AddComponent<PSXRacing.City.CityMode>();
            mode.player = player;
            mode.world = null;
            mode.venueName = "TOWN";
            mode.respawnPoints = BuildTownRespawns(root.transform);

            var systems = new GameObject("GameSystems");
            systems.AddComponent<PSXBootstrap>();
            systems.AddComponent<TouchControls>();
            var menu = systems.AddComponent<PauseMenu>();
            menu.playerCar = player;

            // Getting out of the car. The forecourt's own component, wired
            // exactly as the circuits wire it — it BORROWS the race camera
            // rather than making a second one, because that camera carries the
            // whole PSX display chain. anywhereInTown is the town's one
            // departure from the circuits: this is a map made of doors, so a
            // stopped car can be got out of at any of them, not only at a
            // pump with a thirsty tank.
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

            var world = systems.AddComponent<TownWorld>();
            world.dealerSpots = dealerAnchors;
            world.yardSpots = yardAnchors;
            world.player = player;
            world.pizzaKerb = townPizzaKerb;
            world.pizzaHooks = townPizzaHooks;
            world.dealerDoor = townDealerDoor;
            world.yardGate = townYardGate;
            world.homeDoor = townHomeDoor;
            world.mechanicDoor = townMechanicDoor;
            world.paintDoor = townPaintDoor;
            // Bare concrete grey for the cinder blocks under stripped wrecks —
            // a bake-time material for a runtime spawner, same contract as
            // GarageWorld's rig materials.
            world.blockMaterial = MakeMat("TownCinder", null,
                tint: new Color(0.56f, 0.56f, 0.52f));

            EditorSceneManager.SaveScene(scene, TownScenePath);
            AssetDatabase.SaveAssets();
            Log("[Town] Scene saved: " + TownScenePath);
            return TownScenePath;
        }

        // ------------------------------------------------------------------
        //  materials
        // ------------------------------------------------------------------
        class TownMats
        {
            public Material grass, road, drive, kerb, dirt, fence, metal, line, sign;
        }

        static TownMats TownMaterials() => new TownMats
        {
            grass = MakeMat("TownGrass", TownHouseTex + "/Grass.jpg"),
            // NOT the house pack's "Asphalt.jpg". That file is a concrete path
            // with a STRIP OF GRASS THROUGH THE MIDDLE OF IT — so tiled along a
            // road it lays a green band across the carriageway every twelve
            // metres. It cost two rebuilds and a ground-sinking experiment
            // before anyone opened the image: the artifact survived switching
            // the ground renderer off, which is what finally said it was the
            // road drawing grass rather than the grass drawing over the road.
            // AsphaltDamaged is seamless, dark, and has nothing painted on it.
            road = MakeMat("TownRoad", Root + "/Art/GasStation/Textures/AsphaltDamaged.jpg"),
            drive = MakeMat("TownDrive", TownHouseTex + "/ConcreteBare.jpg"),
            kerb = MakeMat("TownKerb", null, tint: new Color(0.62f, 0.60f, 0.57f)),
            // A yard is oil and hardcore, not paving. Shoulder.png tiled at
            // 10 m read as a floor of diamond tiles; the sand plate tinted
            // brown and tiled small reads as ground somebody parks wrecks on.
            dirt = MakeMat("TownDirt", Root + "/Art/Bogue/Gen/Sand.png",
                           tint: new Color(0.52f, 0.47f, 0.40f)),
            // The yard's fence. This texture is already imported with the
            // trailer pack, which is the only reason a chain-link fence exists
            // in this project at all — there is no fence MODEL anywhere in
            // either art tree, so the yard is authored panels wearing a
            // borrowed texture. Cutout, because chain link is mostly holes.
            fence = MakeMat("TownFence", TownTrailerTex + "/Metal_Fence.png", cutoff: 0.5f),
            metal = MakeMat("TownMetal", TownTrailerTex + "/MetalPlatesBare.jpg"),
            line = MakeMat("TownLine", null, tint: new Color(0.86f, 0.80f, 0.32f)),
            sign = MakeMat("TownSign", null, tint: new Color(0.24f, 0.30f, 0.46f)),
        };

        // ------------------------------------------------------------------
        //  ground and roads
        // ------------------------------------------------------------------
        static void BuildTownGround(Transform parent, TownMats m)
        {
            // One big subdivided field, then the tarmac laid on top of it. The
            // 3 m cell is the same anti-swim rule the home lot uses — a giant
            // quad has its whole area interpolated from four snapping corners.
            // Past the fog wall in every direction. The first cut ended at
            // 150 m deep and the far edge was visible from the main street as
            // a hard line with sky under it — the fog band at sunset is 220 m,
            // so the ground has to out-reach it.
            // SUNK. The ground and the tarmac were two centimetres apart, and
            // at the grazing angle you see a street from inside a car that is
            // not enough: a big ground triangle's interpolated depth crosses
            // the road's somewhere in the middle distance and a BAND OF GRASS
            // appears across the carriageway, moving as you drive. Eight
            // centimetres is four times the margin and reads as a kerb.
            WorldKit.GridSlab(parent, "TownGround", new Vector3(0f, -0.06f, 20f),
                TownStreetHalf * 2f + 200f, 320f, 4f, m.grass, true, 16f);

            // Main street, and the turning up to your house. Both on the ROAD
            // LAYER — CarController decides onRoad by layer number, so tarmac
            // left on layer 0 is tarmac the car drives on with off-road grip
            // for the whole session and nothing on screen says so.
            WorldKit.GridSlab(parent, "TownMain", new Vector3(0f, 0.02f, 0f),
                TownStreetHalf * 2f, TownRoadW, 4f, m.road, true, 12f, WorldKit.RoadLayer);
            WorldKit.GridSlab(parent, "TownHomeRoad",
                new Vector3(HomeStreetX, 0.02f, HomeStreetTop * 0.5f),
                HomeRoadW, HomeStreetTop, 4f, m.road, true, 12f, WorldKit.RoadLayer);

            // Centre line on the main street, and kerbs either side of it.
            // The line is PAINT, not surface: no collider, and off the road
            // layer, because a 22 cm strip standing 1.5 cm proud of the tarmac
            // is something a wheel steps onto.
            WorldKit.GridSlab(parent, "TownCentreLine", new Vector3(0f, 0.035f, 0f),
                TownStreetHalf * 2f, 0.22f, 8f, m.line, false, 6f);
            for (int s = -1; s <= 1; s += 2)
                WorldKit.Box(parent, "TownKerb" + s,
                    new Vector3(0f, 0.08f, s * (TownRoadW * 0.5f + 0.2f)),
                    new Vector3(TownStreetHalf * 2f, 0.16f, 0.4f), m.kerb);
        }

        /// <summary>
        /// Your house, your drive, and the car standing on it.
        ///
        /// The garage door is MEASURED off the instantiated model rather than
        /// taken from the numbers in GarageSceneBuilder: the exporter mirrors
        /// X, so a coordinate that is correct in Blender is on the wrong side
        /// of the house in Unity, and the widest Garage_Door mesh is the one
        /// feature immune to that. The walk-in scene learnt this the hard way
        /// and this is the same measurement, taken again here rather than
        /// shared through a constant that would be wrong for one of them.
        /// </summary>
        static Transform BuildTownHome(Transform parent, TownMats m)
        {
            var home = new GameObject("HomeLot");
            home.transform.SetParent(parent, false);

            WorldKit.GridSlab(home.transform, "HomeYard",
                new Vector3(HomeStreetX, -0.04f, TownHouseZ - 1f),
                36f, 30f, 2f, m.grass, false, 12f);

            float doorX = HomeStreetX + 4.45f;   // fallbacks if the model is missing
            float doorZ = TownHouseZ - 6.45f;
            // yawOffsetDeg: 0. house_hero.fbx is the pack's whole showcase
            // scene rather than a cut-out prop, and its front arrives facing
            // +Z — which is why GarageSceneBuilder turns it by a flat 180 and
            // why the 180 every OTHER model in this project needs would point
            // this one's back garden at the street.
            var house = WorldKit.Place(home.transform, TownHouseDir + "/house_hero.fbx",
                "House", new Vector3(HomeStreetX, 0f, TownHouseZ), Vector3.back,
                PSXRacing.City.CityProps.PackScale, glass: true, yawOffsetDeg: 0f);
            if (house != null)
            {
                // THE WIDE GARAGE DOOR IS THE DATUM, measured off the
                // instantiated model — the exporter mirrors X, so a coordinate
                // that is right in Blender is on the wrong side of the house
                // here, and max(x,z) is immune to whichever axis it came in on.
                // Walk TRANSFORMS and take each one's renderer: the pack draws
                // a door as a mesh under a node named for it, so searching
                // renderer names finds nothing at all. (It found nothing at all
                // on the first build of this scene, which is what the fallback
                // above was covering for.)
                //
                // Its BASE is the second datum and the more important one: the
                // house stands on a foundation, so its garage slab is most of a
                // metre above the model's own origin. Seating by the door base
                // puts the garage floor level with the drive; seating by the
                // bounding box puts the FOUNDATION there and leaves a
                // three-quarter-metre step into your own garage.
                float widest = 0f, doorBaseY = 0f;
                doorZ = TownHouseZ - 6.45f;   // fallback, from the walk-in scene
                foreach (var t in house.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("Garage_Door")) continue;
                    var r = t.GetComponentInChildren<MeshRenderer>();
                    if (r == null) continue;
                    var b = r.bounds;
                    float w = Mathf.Max(b.size.x, b.size.z);
                    if (w <= 2.2f || b.size.y <= 1.8f || w <= widest) continue;
                    widest = w;
                    doorX = b.center.x;
                    doorZ = b.center.z;
                    doorBaseY = b.min.y;
                }
                // OPEN THE BAY. The walk-in scene disables the same leaf, and
                // for the same reason: a shut door is a house with a car parked
                // outside it, and an open one is a car that has just been
                // driven out of its own garage. Found by the same width test —
                // the narrow shed door round the back stays shut.
                foreach (var t in house.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("Garage_Door")) continue;
                    var r = t.GetComponentInChildren<MeshRenderer>();
                    if (r == null) continue;
                    if (Mathf.Max(r.bounds.size.x, r.bounds.size.z) > 2.2f &&
                        r.bounds.size.y > 1.8f)
                        t.gameObject.SetActive(false);
                }

                if (widest > 0f)
                {
                    // Two moves, and the ORDER matters. Y first, off the door's
                    // base, so the garage floor lands level with the drive
                    // rather than the foundation doing; then X, to bring the
                    // door onto the street's centreline — the door is 4.45 m
                    // off the model's origin and a driveway offset by that much
                    // meets the kerb at the neighbour's.
                    float shiftX = HomeStreetX - doorX;
                    house.transform.position += new Vector3(shiftX, -doorBaseY, 0f);
                    doorX = HomeStreetX;
                    Log("[Town] garage door " + widest.ToString("0.00") + " m wide, moved " +
                        shiftX.ToString("0.00") + " m onto the street centreline, floor lifted " +
                        (-doorBaseY).ToString("0.00") + " m");
                }
                else
                {
                    WorldKit.SeatOnGround(house, 0f);
                    Log("[Town] WARN: no Garage_Door found — the drive is a guess.");
                }

                var collidersFbx = WorldKit.Place(home.transform,
                    TownHouseDir + "/house_hero_colliders.fbx", "HouseColliders",
                    new Vector3(HomeStreetX, 0f, TownHouseZ), Vector3.back,
                    PSXRacing.City.CityProps.PackScale, yawOffsetDeg: 0f);
                if (collidersFbx != null)
                {
                    // The collider FBX is a shell, not a model: keep the shape,
                    // strip the renderers. Moved by the SAME offset the house
                    // was, or the walls stand where the house used to be.
                    collidersFbx.transform.position = house.transform.position;
                    foreach (var r in collidersFbx.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            var mc = r.gameObject.AddComponent<MeshCollider>();
                            mc.sharedMesh = mf.sharedMesh;
                            r.gameObject.layer = WorldKit.SolidLayer;
                        }
                        Object.DestroyImmediate(r);
                    }
                }
                else WorldKit.AddColliders(house, WorldKit.SolidLayer);
            }

            // The drive: from the garage door down to where the road stops.
            // It OVERLAPS the road end by two metres, because a driveway that
            // merely touches a kerb leaves a strip of lawn between them for a
            // wheel to drop onto.
            float driveTopZ = doorZ;
            float driveBotZ = HomeStreetTop - 2f;
            WorldKit.GridSlab(home.transform, "HomeDrive",
                new Vector3(doorX, 0.03f, (driveTopZ + driveBotZ) * 0.5f),
                5.2f, driveTopZ - driveBotZ, 3f, m.drive, true, 5f, WorldKit.RoadLayer);

            // Where the car is parked at the start of a session, facing OUT.
            // Two metres clear of the door so the nose is not inside the house.
            var spawn = new GameObject("HomeSpawn");
            spawn.transform.SetParent(home.transform, false);
            spawn.transform.SetPositionAndRotation(
                new Vector3(doorX, 0.35f, driveTopZ - 3.5f),
                Quaternion.LookRotation(Vector3.back, Vector3.up));

            // Pull back in and the day is over.
            TownTrigger(home.transform, "HomeVenue", TownVenue.Kind.Home,
                new Vector3(doorX, 1.4f, driveTopZ - 5f), new Vector3(9f, 3f, 10f));

            // The open garage bay, for somebody who parked on the street and
            // walked up their own drive.
            townHomeDoor = TownAnchor(home.transform, "HomeDoorAnchor",
                new Vector3(doorX, 1.2f, driveTopZ - 1.2f), Vector3.forward);

            // The junction at the bottom of your street: the one place the game
            // asks where you are going. Placed just short of the main road, so
            // a player who simply drives on has answered the question.
            TownTrigger(home.transform, "DepartVenue", TownVenue.Kind.Depart,
                new Vector3(HomeStreetX, 1.4f, 13f), new Vector3(HomeRoadW + 4f, 3f, 12f));

            return spawn.transform;
        }

        /// <summary>The pizza shop and enough street frontage that the main
        /// road reads as a road through somewhere.</summary>
        static void BuildTownStrip(Transform parent, TownMats m)
        {
            var strip = new GameObject("Strip");
            strip.transform.SetParent(parent, false);

            // The shop. glass: true — this is the storefront the player drives
            // to, and its windows being an opaque pale wall is the exact thing
            // the owner asked to have fixed. The INTERIOR is a separate scene
            // (Pizzeria.unity), which is where a shift actually happens.
            // THE SHOPFRONT FACES THE STREET. It did not, and that is most of
            // "I drove to work but was unable to find a pizza inside to
            // deliver": this pack model is a corner block whose glazing, door
            // and signage are all on ONE 10.8 m face and whose other side is
            // 21.4 m of blank brick — and it had been stood up with the brick
            // toward the apron. A player parked outside their own workplace was
            // looking at the back of it. Photographed, finally, by
            // TownProbe's town_pizzeria shot.
            //
            // frontToward: LEFT. Place multiplies a flat 180 onto
            // LookRotation(frontToward) — the pack's fronts face -Z — and this
            // model's shopfront is a further 90 round from that, so the two
            // together land it on +Z. Everything downstream is MEASURED off
            // the result (WorldKit.DoorwayOf), so nothing else here has to
            // know which way it ended up.
            //
            // And BACK 8.5 m, because the depth and the frontage swap when it
            // turns: 21.4 m of building now runs away from the road instead of
            // along it, and left where it was it would have stood in its own
            // apron.
            var shop = WorldKit.Place(strip.transform, TownPizzeria, "Pizzeria",
                new Vector3(-6f, 0f, -30.5f), Vector3.left, 1f, glass: true);
            // MEASURED BEFORE THE HINGES GO ON. A door that has already been
            // reparented onto a pivot is a door that can be standing open, and
            // a doorway measured off an open leaf is a doorway round the
            // corner from itself.
            bool haveDoor = false;
            Bounds doorway = new Bounds();
            Vector3 outward = Vector3.forward;
            if (shop != null)
            {
                WorldKit.SeatOnGround(shop, 0f);
                haveDoor = WorldKit.DoorwayOf(shop, out doorway, out outward);
                // The double leaves SWING. A shut door with a collider was the
                // whole of "I am unable to go inside Pizzeria"; deleting the
                // leaves fixed that and became "the doors are missing to
                // Pizzeria". A hinge is the answer to both — see
                // WorldKit.HingeDoors.
                WorldKit.HingeDoors(shop);
                WorldKit.AddColliders(shop, WorldKit.SolidLayer);
            }
            // The apron, run UNDER the shopfront rather than up to it. Sized
            // off the bounding box it stopped two metres short of the wall and
            // left a band of lawn between the forecourt and the door, because
            // the far edge of this model's bounds is its AWNING, which
            // overhangs the pavement by a good stride. Concrete costs nothing
            // and a building standing on it is a building on a pavement.
            WorldKit.GridSlab(strip.transform, "PizzaApron", new Vector3(-6f, 0.015f, -14f),
                26f, 16f, 3f, m.drive, true, 6f, WorldKit.RoadLayer);
            // THE WHOLE APRON, not a box in the middle of it. The volume used
            // to be 12 x 9 on a 26 x 14 forecourt, so a car parked on the east
            // half of the shop's own frontage was offered nothing at all —
            // which is the driving half of "I selected go to work, drove to
            // work, but was unable to find a pizza".
            TownTrigger(strip.transform, "PizzaVenue", TownVenue.Kind.Pizzeria,
                new Vector3(-6f, 1.4f, -13f), new Vector3(24f, 3f, 12f));

            // ---- three ways to be offered a shift ----
            //
            // THREE, because one is demonstrably not enough and this shop is an
            // awkward shape. The hook used to hang off the centre of the
            // model's bounding box — 21 m of frontage, so eight metres from
            // anything — and the pack's Door meshes turn out to be round the
            // EAST end, in the far half of the side wall, while the apron the
            // car parks on is to the NORTH. A player could stop at the shop,
            // get out, walk to it, walk round it, walk in, and be offered
            // nothing anywhere: "I selected go to work, drove to work, but was
            // unable to find a pizza inside to deliver."
            //
            // So: one on the frontage the car is parked at, one at the measured
            // doorway, one a step inside it. TownWorld writes the same offer
            // onto all three, so there is nowhere in or around Tony's that says
            // nothing.
            var shopB = shop != null ? WorldKit.BoundsOf(shop)
                : new Bounds(new Vector3(-6f, 1f, -22f), new Vector3(8f, 4f, 8f));
            var hooks = new List<Transform>
            {
                TownAnchor(strip.transform, "PizzaFrontAnchor",
                    new Vector3(shopB.center.x, 1.2f, shopB.max.z + 1.2f), Vector3.back),
            };
            if (haveDoor)
            {
                Vector3 doorAt = new Vector3(doorway.center.x, 1.2f, doorway.center.z);
                hooks.Add(TownAnchor(strip.transform, "PizzaDoorAnchor",
                    doorAt + outward * 1.2f, -outward));
                hooks.Add(TownAnchor(strip.transform, "PizzaCounterAnchor",
                    doorAt - outward * 2.5f, outward));
            }
            townPizzaHooks = hooks.ToArray();
            townPizzaKerb = TownAnchor(strip.transform, "PizzaKerbAnchor",
                new Vector3(-14f, 0.35f, -8.5f), Vector3.left);

            // Neighbours, so the street is a street. Three blocks on the north
            // side facing the road, spread far enough apart to leave the
            // forecourt and the lot their frontage.
            float[] xs = { -88f, -30f, 12f };
            for (int i = 0; i < xs.Length; i++)
            {
                var src = TownBlocks[1 + i];
                var b = WorldKit.PlaceTall(strip.transform, src.fbx, "Block" + i,
                    new Vector3(xs[i], 0f, 24f), Vector3.back, src.tall);
                if (b == null) continue;
                WorldKit.SeatOnGround(b, 0f);
                WorldKit.AddColliders(b, WorldKit.SolidLayer);
            }
            // And two on the south side, either side of the shop.
            for (int i = 0; i < 2; i++)
            {
                var src = TownBlocks[1 + (i + 1) % 3];
                var b = WorldKit.PlaceTall(strip.transform, src.fbx, "BlockS" + i,
                    new Vector3(i == 0 ? -46f : 34f, 0f, -28f), Vector3.forward, src.tall);
                if (b == null) continue;
                WorldKit.SeatOnGround(b, 0f);
                WorldKit.AddColliders(b, WorldKit.SolidLayer);
            }
        }

        // ------------------------------------------------------------------
        //  the trade: a workshop and a body shop
        // ------------------------------------------------------------------
        /// <summary>
        /// The two trades the garage menu already had and the town did not:
        /// a MECHANIC and a PAINT + BODY shop, as places you drive to.
        ///
        /// The owner's ask, verbatim: "there should be a garage in town for
        /// Mechanic and one for Paint Shop. Currently there is no option to
        /// change the paint color of cars even though they have multiple
        /// options." Both halves are real — MECHANIC SERVICES lived four
        /// presses down a menu with no address in the world, and RESPRAY did
        /// not exist at all even though every shell in the pack carries a
        /// handful of baked liveries.
        ///
        /// Placed on the SOUTH side of the main street, in the two gaps the
        /// street blocks leave: the body shop out west past the junction (so
        /// it is the first trade you pass leaving home) and the workshop out
        /// east between the last block and the salvage yard, which puts the
        /// two places you take a broken car next door to each other.
        /// </summary>
        static void BuildTownTrade(Transform parent, TownMats m)
        {
            var paint = BuildTownUnit(parent, m, "PaintShop", new Vector3(-92f, 0f, -26f),
                "COLOURWORKS — PAINT + BODY", new Color(0.55f, 0.20f, 0.42f),
                out townPaintDoor);
            // A COLOUR CHART on the board. Nothing in this project can put
            // legible words on a wall, so two identical units 150 m apart would
            // be told apart only by walking up to them and reading the prompt.
            // A row of paint chips is a body shop from the far end of the
            // street, which is where the question gets asked.
            var chips = new[]
            {
                new Color(0.78f, 0.14f, 0.14f), new Color(0.90f, 0.62f, 0.10f),
                new Color(0.16f, 0.52f, 0.30f), new Color(0.16f, 0.34f, 0.66f),
                new Color(0.92f, 0.92f, 0.90f), new Color(0.11f, 0.11f, 0.13f),
            };
            for (int i = 0; i < chips.Length; i++)
                WorldKit.Panel(paint, "PaintChip" + i,
                    new Vector3(-92f - 3.6f + i * 1.45f, 6.7f, -20f + 0.32f),
                    1.25f, 1.25f, 0f, MakeMat("TownChip" + i, null, tint: chips[i]), false);
            TownTrigger(parent, "PaintVenue", TownVenue.Kind.PaintShop,
                new Vector3(-92f, 1.4f, -15f), new Vector3(24f, 3f, 18f));

            BuildTownUnit(parent, m, "Mechanic", new Vector3(58f, 0f, -26f),
                "DELMAR AUTO — SERVICE", new Color(0.18f, 0.42f, 0.30f),
                out townMechanicDoor);
            TownTrigger(parent, "MechanicVenue", TownVenue.Kind.Mechanic,
                new Vector3(58f, 1.4f, -15f), new Vector3(24f, 3f, 18f));
        }

        /// <summary>
        /// A two-bay unit on a concrete apron, with its shutters up.
        ///
        /// Both of the town's new trades are the same building — that is the
        /// point of the shared function rather than a shortcut. Neither art
        /// tree has a garage, a workshop, a spray booth or a body shop in it,
        /// so what a unit like this reads as comes entirely from the things
        /// this project can actually make: a slab, a shell with a hole in the
        /// front, a header over the hole, and a sign that says what the hole
        /// is for. The two differ by the colour of the sign, what is standing
        /// in the bays, and which page the door opens — which, from the road,
        /// is exactly how two industrial units on the same estate differ.
        ///
        /// THE FRONT IS FOUR PIECES, not one wall with a doorway in it: two
        /// piers, a header over the bays and a middle post between them. A
        /// BoxCollider cannot have a hole in it, so a shell built as one box
        /// is a building you cannot drive or walk into — which is the bug the
        /// gas station's single slab was, reported as invisible walls.
        /// </summary>
        /// <param name="signTint">The board over the door. It is the only
        /// thing on the building that says which trade this is.</param>
        static Transform BuildTownUnit(Transform parent, TownMats m, string name,
                                       Vector3 at, string sign, Color signTint,
                                       out Transform doorAnchor)
        {
            var unit = new GameObject(name);
            unit.transform.SetParent(parent, false);

            const float w = 18f;      // frontage
            const float d = 12f;      // depth
            const float h = 5.2f;     // eaves
            const float bayW = 5.4f;  // each roller opening
            const float wall = 0.4f;
            float fz = at.z + d * 0.5f;   // the front wall plane, toward the street
            float bz = at.z - d * 0.5f;

            // Apron, running from the shutters all the way OUT TO THE KERB.
            // Same lesson the dealership learnt the hard way: a lot whose
            // concrete stops six metres short of the road is a lot you reach
            // across a lawn, and it reads as scenery dropped on the map rather
            // than a unit built beside the street.
            // Front wall to kerb. Both units stand SOUTH of the main street, so
            // the kerb is the low-z edge of the carriageway and the apron runs
            // up in +z; the Max is a floor rather than a case, so a unit moved
            // to the far side gets a forecourt rather than an inside-out one.
            float apronDepth = Mathf.Max(14f, -(TownRoadW * 0.5f + 0.4f) - fz);
            WorldKit.GridSlab(unit.transform, name + "Apron",
                new Vector3(at.x, 0.015f, fz + apronDepth * 0.5f), w + 8f, apronDepth, 3f,
                m.drive, true, 6f, WorldKit.RoadLayer);
            // AND A FLOOR INSIDE. The shutters are 5.4 m openings you can drive
            // through, and without this the inside of a workshop is the town's
            // grass — off-road grip under a roof, which is exactly the trap
            // WorldKit.RoadLayer exists to name.
            WorldKit.GridSlab(unit.transform, name + "Floor",
                new Vector3(at.x, 0.02f, at.z), w, d, 3f,
                m.drive, true, 6f, WorldKit.RoadLayer);

            // Shell: back, two sides, roof.
            WorldKit.Box(unit.transform, name + "Back", new Vector3(at.x, h * 0.5f, bz),
                new Vector3(w, h, wall), m.metal, true, 0f, WorldKit.SolidLayer);
            for (int s = -1; s <= 1; s += 2)
                WorldKit.Box(unit.transform, name + "Side" + s,
                    new Vector3(at.x + s * (w * 0.5f), h * 0.5f, at.z),
                    new Vector3(wall, h, d), m.metal, true, 0f, WorldKit.SolidLayer);
            WorldKit.Box(unit.transform, name + "Roof",
                new Vector3(at.x, h + 0.15f, at.z),
                new Vector3(w + 0.6f, 0.3f, d + 0.6f), m.metal, true, 0f, WorldKit.SolidLayer);

            // Front: pier, bay, post, bay, pier — and a header over the lot.
            float pier = (w - bayW * 2f - 0.9f) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
                WorldKit.Box(unit.transform, name + "Pier" + s,
                    new Vector3(at.x + s * (w - pier) * 0.5f, h * 0.5f, fz),
                    new Vector3(pier, h, wall), m.metal, true, 0f, WorldKit.SolidLayer);
            WorldKit.Box(unit.transform, name + "Post",
                new Vector3(at.x, h * 0.5f, fz), new Vector3(0.9f, h, wall),
                m.metal, true, 0f, WorldKit.SolidLayer);
            // A 3.6 m opening under the header — high enough for anything in
            // the catalog and low enough that the building has a lintel.
            WorldKit.Box(unit.transform, name + "Header",
                new Vector3(at.x, (3.6f + h) * 0.5f, fz),
                new Vector3(w, h - 3.6f, wall), m.metal, true, 0f, WorldKit.SolidLayer);
            // The shutters, rolled up into their boxes over each opening. Not
            // doors: a roller shutter that swings would be a roller shutter
            // that is a door, and these are open all day anyway.
            for (int s = -1; s <= 1; s += 2)
                WorldKit.Box(unit.transform, name + "Shutter" + s,
                    new Vector3(at.x + s * (bayW * 0.5f + 0.45f), 3.35f, fz - 0.05f),
                    new Vector3(bayW, 0.45f, 0.5f), m.kerb, false);

            // The board. Panel rather than Box so it takes a tint cleanly and
            // reads flat from the road, on two posts so it is signage rather
            // than paint on the wall.
            var board = MakeMat("TownSign" + name, null, tint: signTint);
            WorldKit.Panel(unit.transform, name + "Sign",
                new Vector3(at.x, h + 1.5f, fz + 0.25f), w * 0.72f, 2.0f, 0f,
                board, false);
            for (int s = -1; s <= 1; s += 2)
                WorldKit.Post(unit.transform, name + "SignPost" + s,
                    new Vector3(at.x + s * w * 0.30f, h, fz + 0.25f), 0.16f, 1.6f, m.metal);

            // Something in the bays, so an empty unit is not an empty unit. Two
            // benches at the back wall and a stack of drums between them: the
            // only shapes this can honestly make, and enough that the inside
            // is somewhere rather than a void.
            WorldKit.Box(unit.transform, name + "Bench",
                new Vector3(at.x, 0.45f, bz + 1.1f), new Vector3(w - 3f, 0.9f, 0.8f),
                m.kerb, true, 0f, WorldKit.SolidLayer);
            for (int i = 0; i < 4; i++)
                WorldKit.Post(unit.transform, name + "Drum" + i,
                    new Vector3(at.x - 6.2f + i * 0.72f, 0f, bz + 2.4f),
                    0.58f, 0.88f, m.metal, solid: true);

            // The door: in the LEFT bay, one step inside the opening. Inside
            // rather than out on the apron, because that is where a walker
            // ends up once the bay is somewhere they can walk into — and a
            // hook they walk past is a hook that does not exist.
            doorAnchor = TownAnchor(unit.transform, name + "DoorAnchor",
                new Vector3(at.x - (bayW * 0.5f + 0.45f), 1.2f, fz - 1.6f),
                Vector3.forward);
            Log("[Town] " + name + ": unit " + w + " x " + d + " at " +
                at.ToString("0") + ", sign " + sign);
            return unit.transform;
        }

        /// <summary>
        /// CRESTLINE MOTORS: an apron, a showroom at the back, two rows of
        /// bays with lights over them, and a trigger at the mouth.
        ///
        /// There is no dealership MODEL in either art tree — no showroom, no
        /// flags, no banners, no pylon sign. What makes a lot read as a lot is
        /// the thing this project does have plenty of: cars, in rows, under
        /// lights, behind a low wall. The stock is filled at runtime from the
        /// save so the cars standing there are the cars for sale.
        /// </summary>
        static Transform[] BuildTownDealer(Transform parent, TownMats m)
        {
            var lot = new GameObject("Dealership");
            lot.transform.SetParent(parent, false);
            const float cx = 62f, cz = 27f;

            WorldKit.GridSlab(lot.transform, "DealerApron", new Vector3(cx, 0.015f, cz),
                48f, 30f, 3f, m.drive, true, 6f, WorldKit.RoadLayer);
            // AND A WAY IN. The apron stopped six metres short of the kerb and
            // the only route onto it was across the lawn — which works, drives
            // fine, and reads as the lot having been dropped on the map rather
            // than built beside the road.
            WorldKit.GridSlab(lot.transform, "DealerIn",
                new Vector3(cx - 8f, 0.016f, (cz - 15f + 4f) * 0.5f),
                11f, cz - 15f - 4f, 3f, m.drive, true, 6f, WorldKit.RoadLayer);
            var show = WorldKit.PlaceTall(lot.transform, TownBlocks[0].fbx, "Showroom",
                new Vector3(cx + 14f, 0f, cz + 12f), Vector3.back, TownBlocks[0].tall,
                glass: true);
            if (show != null)
            {
                WorldKit.SeatOnGround(show, 0f);
                WorldKit.AddColliders(show, WorldKit.SolidLayer);
            }

            // A low wall down the two open sides, so the lot is a lot and not
            // a wide spot in the road.
            WorldKit.Box(lot.transform, "DealerWallW", new Vector3(cx - 24f, 0.45f, cz),
                new Vector3(0.5f, 0.9f, 30f), m.kerb, true, 0f, WorldKit.SolidLayer);
            WorldKit.Box(lot.transform, "DealerWallN", new Vector3(cx, 0.45f, cz + 15f),
                new Vector3(48f, 0.9f, 0.5f), m.kerb, true, 0f, WorldKit.SolidLayer);

            // Two rows of bays, noses out toward the road, with a light between
            // every second pair.
            var spots = new List<Transform>();
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 4; i++)
                {
                    float x = cx - 16f + i * 8.5f;
                    float z = cz - 8f + row * 12f;
                    var t = new GameObject("Bay" + row + "_" + i);
                    t.transform.SetParent(lot.transform, false);
                    t.transform.SetPositionAndRotation(new Vector3(x, 0.02f, z),
                        Quaternion.Euler(0f, row == 0 ? 180f : 0f, 0f));
                    spots.Add(t.transform);
                    WorldKit.GridSlab(lot.transform, "BayLine" + row + "_" + i,
                        new Vector3(x + 4.25f, 0.03f, z), 0.18f, 5f, 2f, m.line, false, 3f);
                    if (i % 2 == 0)
                    {
                        var at = new Vector3(x + 4.25f, 0f, z + (row == 0 ? -3.4f : 3.4f));
                        WorldKit.Post(lot.transform, "LotLight" + row + "_" + i,
                            at, 0.26f, 6.5f, m.metal, solid: true);
                        // A head, or it is a bollard six metres tall. Two of
                        // them per pole, out either side, which is what a lot
                        // light actually looks like from the road.
                        WorldKit.Box(lot.transform, "LotLamp" + row + "_" + i,
                            at + new Vector3(0f, 6.4f, 0f),
                            new Vector3(2.6f, 0.28f, 0.7f), m.sign, false);
                    }
                }

            TownTrigger(lot.transform, "DealerVenue", TownVenue.Kind.Dealer,
                new Vector3(cx - 6f, 1.4f, cz - 12.5f), new Vector3(16f, 3f, 8f));

            // The sales office: the showroom's street face, for a walk-up.
            var showB = show != null ? WorldKit.BoundsOf(show)
                : new Bounds(new Vector3(cx + 14f, 2f, cz + 12f), new Vector3(10f, 6f, 10f));
            townDealerDoor = TownAnchor(lot.transform, "DealerDoorAnchor",
                new Vector3(showB.center.x, 1.2f, showB.min.z - 0.8f), Vector3.forward);
            return spots.ToArray();
        }

        /// <summary>
        /// The salvage yard: a fenced dirt compound with dead cars in it.
        ///
        /// Every piece is authored, because nothing in either art tree is a
        /// wreck, a crusher, a container or a tyre pile. What it DOES have is
        /// sixteen car models and a chain-link texture, and a yard is mostly
        /// cars that will never move again — so the wrecks are real car shells
        /// (filled at runtime, tipped and rotated) behind real fence panels,
        /// with stacked crates and a site hut for the rest.
        /// </summary>
        static Transform[] BuildTownYard(Transform parent, TownMats m)
        {
            var yard = new GameObject("Junkyard");
            yard.transform.SetParent(parent, false);
            const float cx = 106f, cz = -30f;
            const float halfX = 24f, halfZ = 16f;

            WorldKit.GridSlab(yard.transform, "YardGround", new Vector3(cx, 0.012f, cz),
                halfX * 2f, halfZ * 2f, 3f, m.dirt, true, 10f, WorldKit.RoadLayer);

            // A WAY IN, from the road. The gate used to be in the SOUTH fence,
            // which is the far side from the street: the only route to it was
            // forty metres across a lawn and round the back of the compound.
            WorldKit.GridSlab(yard.transform, "YardIn",
                new Vector3(cx, 0.016f, (cz + halfZ + 4f) * 0.5f),
                10f, cz + halfZ - 4f, 3f, m.dirt, true, 8f, WorldKit.RoadLayer);

            // Fence: three closed sides and a gate in the fourth, on the road
            // side. 2.4 m, which is a real yard fence and tall enough to hide a
            // stacked car.
            const float fh = 2.4f;
            WorldKit.Panel(yard.transform, "YardFenceN1",
                new Vector3(cx - halfX * 0.5f - 2f, fh * 0.5f, cz + halfZ),
                halfX - 4f, fh, 0f, m.fence, true, 4f, 2.4f, WorldKit.SolidLayer);
            WorldKit.Panel(yard.transform, "YardFenceN2",
                new Vector3(cx + halfX * 0.5f + 2f, fh * 0.5f, cz + halfZ),
                halfX - 4f, fh, 0f, m.fence, true, 4f, 2.4f, WorldKit.SolidLayer);
            WorldKit.Panel(yard.transform, "YardFenceE", new Vector3(cx + halfX, fh * 0.5f, cz),
                halfZ * 2f, fh, 90f, m.fence, true, 4f, 2.4f, WorldKit.SolidLayer);
            WorldKit.Panel(yard.transform, "YardFenceW", new Vector3(cx - halfX, fh * 0.5f, cz),
                halfZ * 2f, fh, 90f, m.fence, true, 4f, 2.4f, WorldKit.SolidLayer);
            // South side, closed all the way across — it backs onto nothing.
            WorldKit.Panel(yard.transform, "YardFenceS", new Vector3(cx, fh * 0.5f, cz - halfZ),
                halfX * 2f, fh, 0f, m.fence, true, 4f, 2.4f, WorldKit.SolidLayer);

            // POSTS. Chain link is ninety per cent holes, so a fence made only
            // of it is a faint scribble you cannot see from the road — the
            // whole point of a compound is that it reads as closed. The posts
            // are what draw the line; the mesh is what fills it.
            for (int i = -6; i <= 6; i++)
            {
                float t = i / 6f;
                // Skip the two nearest the middle of the NORTH run: that is the
                // gate, and a post standing in an opening is a bollard.
                if (Mathf.Abs(i) > 1)
                    WorldKit.Post(yard.transform, "FencePostN" + i,
                        new Vector3(cx + t * halfX, 0f, cz + halfZ), 0.16f, fh + 0.15f, m.metal);
                WorldKit.Post(yard.transform, "FencePostS" + i,
                    new Vector3(cx + t * halfX, 0f, cz - halfZ), 0.16f, fh + 0.15f, m.metal);
            }
            for (int i = -4; i <= 4; i++)
            {
                float t = i / 4f;
                WorldKit.Post(yard.transform, "FencePostE" + i,
                    new Vector3(cx + halfX, 0f, cz + t * halfZ), 0.16f, fh + 0.15f, m.metal);
                WorldKit.Post(yard.transform, "FencePostW" + i,
                    new Vector3(cx - halfX, 0f, cz + t * halfZ), 0.16f, fh + 0.15f, m.metal);
            }

            // The site hut, and a stack of crates beside it.
            var hut = WorldKit.Place(yard.transform, TownTrailerDir + "/trailer_02.fbx",
                "YardHut", new Vector3(cx + halfX - 7f, 0f, cz - halfZ + 6f),
                Vector3.back, PSXRacing.City.CityProps.PackScale);
            if (hut != null)
            {
                WorldKit.SeatOnGround(hut, 0f);
                WorldKit.AddColliders(hut, WorldKit.SolidLayer);
            }
            for (int i = 0; i < 5; i++)
                WorldKit.Box(yard.transform, "Crate" + i,
                    new Vector3(cx - halfX + 5f + (i % 3) * 1.4f,
                                0.6f + (i / 3) * 1.2f,
                                cz - halfZ + 4f + (i % 2) * 1.3f),
                    new Vector3(1.2f, 1.2f, 1.2f), m.metal, true, (i * 17) % 40,
                    WorldKit.SolidLayer);

            // Where the wrecks go. Two rows along the back, turned every which
            // way at bake time so the yard is not a car park; the SHELLS
            // arrive at runtime because which cars are in a yard is a save
            // question.
            //
            // UPRIGHT and at grade now, at the owner's instruction: "junkyard
            // cars don't need to be half buried — they can be on jack stands
            // or cinder blocks." The half-sunk lean was standing in for decay;
            // the decay is real now (TownWorld strips wheels to match the
            // shelves and stacks blocks under the bare corners), and a buried
            // sill under a car on blocks would read as a car on blocks in a
            // hole.
            var spots = new List<Transform>();
            for (int i = 0; i < 8; i++)
            {
                float x = cx - halfX + 6f + (i % 4) * 9f;
                float z = cz + (i < 4 ? 6f : -6f);
                var t = new GameObject("Wreck" + i);
                t.transform.SetParent(yard.transform, false);
                t.transform.SetPositionAndRotation(
                    new Vector3(x, 0.01f, z),
                    Quaternion.Euler(0f, 25f + i * 41f, 0f));
                spots.Add(t.transform);
            }

            // Tyre piles, out of the one wheel mesh this project has.
            var wheelMesh = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/Car/wheel.obj");
            if (wheelMesh != null)
                for (int pile = 0; pile < 3; pile++)
                    for (int k = 0; k < 5; k++)
                    {
                        var w = (GameObject)Object.Instantiate(wheelMesh);
                        w.name = "Tyre" + pile + "_" + k;
                        w.transform.SetParent(yard.transform, false);
                        w.transform.position = new Vector3(
                            cx - 12f + pile * 11f, 0.12f + k * 0.22f, cz - halfZ + 4f);
                        // FLAT, and flat for every tyre in the pile. The
                        // wheel mesh's axle is on X, so a 90-degree roll about
                        // Z stands the axle up and lays the tyre down; the yaw
                        // is the only thing allowed to vary, or the stack falls
                        // over as it climbs.
                        w.transform.rotation = Quaternion.Euler(0f, k * 37f, 90f);
                        ConvertToPSXMaterials(w);
                        foreach (var c in w.GetComponentsInChildren<Collider>())
                            Object.DestroyImmediate(c);
                    }

            TownTrigger(yard.transform, "YardVenue", TownVenue.Kind.Junkyard,
                new Vector3(cx, 1.4f, cz + halfZ - 3f), new Vector3(10f, 3f, 9f));

            // The gate, for a walk-up: just inside the opening in the north
            // fence, where the way in already is.
            townYardGate = TownAnchor(yard.transform, "YardGateAnchor",
                new Vector3(cx, 1.2f, cz + halfZ - 1.5f), Vector3.forward);
            return spots.ToArray();
        }

        /// <summary>
        /// The forecourt, from the owner's own gas-station pack.
        ///
        /// SpawnStation does the hard part and has done since the circuits
        /// needed it: it deletes the pack's painted skyline and checkerboard,
        /// radius-trims a 300 x 143 m diorama down to a lot, and scales the
        /// whole thing off the height of a Fuel_pump — the only object in the
        /// model with a size the real world agrees about.
        /// </summary>
        static void BuildTownStation(Transform parent)
        {
            var root = SpawnStation("GasStation", out var pumps);
            root.transform.SetParent(parent, false);
            // glass: true — the shop's four Glass_00N panes are one material
            // and this is a forecourt the player gets out and walks into.
            ConvertToPSXMaterials(root, glass: true);

            const float cx = -52f, cz = 26f;
            // WHICH WAY ROUND, decided by the PUMPS rather than by the model's
            // own idea of forward — which is a Blender export convention nobody
            // here chose. Half a forecourt is a shop with no doors on the back
            // of it, and the pumps are the half that has to face the street.
            // Same correction PlaceGasStation makes on every circuit.
            var toRoad = Vector3.back;   // the street is south of the lot
            root.transform.rotation = Quaternion.LookRotation(toRoad, Vector3.up);
            if (pumps.Count > 0)
            {
                var lot0 = WorldKit.BoundsOf(root);
                var face = PumpBounds(pumps).center - lot0.center;
                face.y = 0f;
                if (face.sqrMagnitude > 1f)
                    root.transform.rotation =
                        Quaternion.Euler(0f, Vector3.SignedAngle(face.normalized, toRoad,
                                                                 Vector3.up), 0f) *
                        root.transform.rotation;
            }
            else Log("[Town] WARN: no Fuel_pump objects — the forecourt has no pumps.");
            var b = WorldKit.BoundsOf(root);
            float floorY = pumps.Count > 0 ? PumpBounds(pumps).min.y : b.min.y;
            root.transform.position += new Vector3(cx - b.center.x, -floorY, cz - b.center.z);

            // Solid where a THING is and open everywhere else — the same
            // piece-collider pass the circuits run now. The old one-box-behind-
            // the-pump-line filled every open yard of concrete around the shop
            // with invisible wall, which nobody noticed from a car and the
            // player hit the moment they got out and walked ("invisible walls
            // stop me from walking into areas like one of the gas stations").
            b = WorldKit.BoundsOf(root);
            var shopBounds = AddStationPieceColliders(root, pumps);
            // And the shop DOOR, so the walk-in store works here the way it
            // does on every circuit forecourt. The town's ground is flat zero.
            PlaceStoreDoor(parent, shopBounds, pumps, 1.2f);

            // A drive-up volume per pump, exactly as the circuits build them.
            // No separate island box any more: the pump's own mesh is collided
            // by the piece pass above, so a car stops on the pump's actual
            // bodywork and a person walks the real gap between two of them.
            foreach (var pump in pumps)
            {
                var pb = CombinedBounds(pump.gameObject);
                var go = new GameObject("Pump");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(pb.center.x, 1.1f, pb.center.z);
                var trigger = go.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(7f, 3f, 5.5f);
                go.AddComponent<GasPump>();
            }
            // THE APRON. On a circuit BuildApron lays this off the pad; there
            // is no pad here, so the forecourt was a petrol station standing on
            // a lawn with its shop fittings in the grass. Sized off the trimmed
            // lot itself rather than typed in, because SpawnStation scales the
            // whole pack off pump height and the lot is whatever that leaves.
            WorldKit.GridSlab(parent, "StationApron",
                new Vector3(b.center.x, 0.018f, b.center.z),
                Mathf.Max(24f, b.size.x + 6f), Mathf.Max(24f, b.size.z + 6f), 4f,
                MakeMat("TownForecourt", TownHouseTex + "/ConcreteBare.jpg"),
                true, 6f, WorldKit.RoadLayer);
            Log("[Town] forecourt: " + pumps.Count + " pump(s), apron " +
                b.size.x.ToString("0") + " x " + b.size.z.ToString("0") + " m");
        }

        static void BuildTownBounds(Transform parent)
        {
            var b = new GameObject("TownBounds");
            b.transform.SetParent(parent, false);
            void Wall(string name, Vector3 at, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(b.transform, false);
                go.transform.position = at;
                go.layer = WorldKit.SolidLayer;
                go.AddComponent<BoxCollider>().size = size;
            }
            float hx = TownStreetHalf + 18f;
            Wall("W", new Vector3(-hx, 3f, 12f), new Vector3(1f, 6f, 170f));
            Wall("E", new Vector3(hx, 3f, 12f), new Vector3(1f, 6f, 170f));
            Wall("S", new Vector3(0f, 3f, -58f), new Vector3(hx * 2f, 6f, 1f));
            Wall("N", new Vector3(0f, 3f, 82f), new Vector3(hx * 2f, 6f, 1f));
        }

        /// <summary>Places a stuck car can be put back on. On the road, facing
        /// along it, spread out enough that the nearest one is never the thing
        /// the car just wedged itself against.</summary>
        static Transform[] BuildTownRespawns(Transform parent)
        {
            var root = new GameObject("Respawns");
            root.transform.SetParent(parent, false);
            var list = new List<Transform>();
            for (int i = -3; i <= 3; i++)
            {
                var go = new GameObject("Respawn" + (i + 3));
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(
                    new Vector3(i * 40f, 0.4f, -2.5f),
                    Quaternion.LookRotation(Vector3.right, Vector3.up));
                list.Add(go.transform);
            }
            var up = new GameObject("RespawnHome");
            up.transform.SetParent(root.transform, false);
            up.transform.SetPositionAndRotation(
                new Vector3(HomeStreetX, 0.4f, 30f),
                Quaternion.LookRotation(Vector3.back, Vector3.up));
            list.Add(up.transform);
            return list.ToArray();
        }

        static void TownTrigger(Transform parent, string name, TownVenue.Kind kind,
                                Vector3 at, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;
            go.AddComponent<TownVenue>().kind = kind;
        }
    }
}
