using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A stranger's street: five plots, a kerb, and one driveway with a car on
    /// it that somebody wants to sell you.
    ///
    /// ONE scene, dressed differently every visit. A build-settings scene
    /// cannot be generated per listing, so "randomly generate another house
    /// map" is a RUNTIME choice: every plot is baked with four stacked house
    /// variants and <see cref="SellerLotWorld"/> switches one on per plot from
    /// a seed derived from the advert. The same advert therefore gives the same
    /// street every time you drive back to it, which matters — a house that
    /// changed shape between two visits would read as the game losing its
    /// place.
    ///
    /// The player arrives ON FOOT. They drove here; the drive is the activity
    /// slot the visit spent, and standing in somebody's driveway is what the
    /// scene is for. Modelled on <see cref="PizzeriaSceneBuilder"/>, which is
    /// the canonical minimal walk-in scene: FootRig, its lighting, its display
    /// chain, and a systems object carrying the screen.
    /// </summary>
    public static class SellerLotSceneBuilder
    {
        public const string ScenePath = WorldKit.Root + "/Scenes/SellerLot.unity";

        const string HouseDir = WorldKit.Root + "/Art/LifeSim/House";
        const string TrailerDir = WorldKit.Root + "/Art/LifeSim/Trailer";
        const string HouseTex = HouseDir + "/Textures";

        /// <summary>How many plots the street has. The seller is always the
        /// MIDDLE one, so there is a neighbour either side however the dice
        /// fall — a lone house on an empty street reads as a test level.</summary>
        const int Plots = 5;
        /// <summary>Metres between plot centres. Wide enough that two 11.7 m
        /// houses at 0.81 scale have a garden between them.</summary>
        const float PlotPitch = 17f;
        /// <summary>Kerb line. Houses sit north of it and face it.</summary>
        const float StreetZ = -16f;
        const float HouseZ = 2f;

        /// <summary>
        /// The four things a plot can be wearing. One house and three trailers,
        /// which is the whole imported residential range — and the mix is
        /// right: a game whose starting job is pizza delivery is not shopping
        /// for cars in a good neighbourhood.
        /// </summary>
        static readonly string[] Variants =
        {
            HouseDir + "/house_simple.fbx",
            TrailerDir + "/trailer_00.fbx",
            TrailerDir + "/trailer_02.fbx",
            TrailerDir + "/trailer_05.fbx",
        };

        [MenuItem("PSX Racing/Build Seller Lot Scene")]
        public static void Build()
        {
            if (Shader.Find("PSX/Lit") == null)
            {
                Debug.LogError("[SellerLot] PSX/Lit missing — did shaders compile?");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lot = new GameObject("Street");

            var grass = WorldKit.Mat("SellerGrass", HouseTex + "/Grass.jpg", new Vector2(1f, 1f));
            var drive = WorldKit.Mat("SellerDrive", HouseTex + "/ConcreteBare.jpg", Vector2.one);
            // See the note on TownRoad: the house pack's "Asphalt" has a strip
            // of grass running through it and lays a green band across any road
            // it is tiled along.
            var road = WorldKit.Mat("SellerRoad",
                WorldKit.Root + "/Art/GasStation/Textures/AsphaltDamaged.jpg", Vector2.one);
            var kerb = WorldKit.Mat("SellerKerb", null, Vector2.one, new Color(0.62f, 0.60f, 0.57f));

            float width = Plots * PlotPitch + 24f;

            // Ground. Subdivided at 2 m — this is a surface the player STANDS
            // on and walks across, so the snap has to move each vertex a
            // distance nobody can see.
            // Sunk under the tarmac for the reason the town's is — see
            // PSXRacingBuilder.Town.BuildTownGround. Two coplanar slabs at a
            // grazing angle put a band of grass across the road.
            WorldKit.GridSlab(lot.transform, "SellerYard", new Vector3(0f, -0.06f, 2f),
                width, 46f, 2f, grass, true, 14f);
            WorldKit.GridSlab(lot.transform, "SellerStreet", new Vector3(0f, 0.01f, StreetZ),
                width, 9f, 3f, road, true, 10f, WorldKit.RoadLayer);
            WorldKit.Box(lot.transform, "KerbN", new Vector3(0f, 0.07f, StreetZ + 4.7f),
                new Vector3(width, 0.14f, 0.4f), kerb);
            WorldKit.Box(lot.transform, "KerbS", new Vector3(0f, 0.07f, StreetZ - 4.7f),
                new Vector3(width, 0.14f, 0.4f), kerb);

            // The world ends at the edge of the street. A walk-in scene with an
            // open horizon is a scene the player walks out of and then cannot
            // find their way back into.
            float halfW = width * 0.5f;
            Bound(lot.transform, new Vector3(-halfW, 1.5f, 2f), new Vector3(0.6f, 3f, 52f));
            Bound(lot.transform, new Vector3(halfW, 1.5f, 2f), new Vector3(0.6f, 3f, 52f));
            Bound(lot.transform, new Vector3(0f, 1.5f, 25f), new Vector3(width, 3f, 0.6f));
            Bound(lot.transform, new Vector3(0f, 1.5f, StreetZ - 6f), new Vector3(width, 3f, 0.6f));

            var plots = new Transform[Plots];
            var houseSets = new GameObject[Plots][];
            for (int i = 0; i < Plots; i++)
            {
                float x = (i - (Plots - 1) * 0.5f) * PlotPitch;

                var plotGO = new GameObject("Plot" + i);
                plotGO.transform.SetParent(lot.transform, false);
                plotGO.transform.position = new Vector3(x, 0f, 0f);
                plots[i] = plotGO.transform;

                // Driveway: from the kerb up to the house. Proud of the grass
                // so the seam never z-fights.
                // All the way to the house. The first cut stopped three
                // metres short and left a strip of lawn between the drive and
                // the door, which reads as a path to nowhere.
                const float driveTopZ = HouseZ + 3f;
                const float driveBotZ = StreetZ + 4.4f;
                WorldKit.GridSlab(plotGO.transform, "Drive" + i,
                    new Vector3(x, 0.012f, (driveTopZ + driveBotZ) * 0.5f),
                    4.8f, driveTopZ - driveBotZ, 2f, drive, false, 4f);

                // Four stacked variants, all disabled. The world picks one.
                var set = new GameObject[Variants.Length];
                for (int v = 0; v < Variants.Length; v++)
                {
                    // frontToward -Z: the houses look down at the street, which
                    // is south of them. WorldKit.Place carries the pack's own
                    // 180-degree front correction so this reads as written.
                    var go = WorldKit.Place(plotGO.transform, Variants[v], "House" + v,
                        new Vector3(x, 0f, HouseZ + 5f), Vector3.back,
                        PSXRacing.City.CityProps.PackScale);
                    if (go == null) continue;
                    // Seat by the MEASURED bottom, not by the origin. The house
                    // pack sits on a foundation and the trailers do not, so one
                    // shared y buries one and floats the other.
                    WorldKit.SeatOnGround(go, 0f);
                    WorldKit.AddColliders(go, WorldKit.SolidLayer);
                    go.SetActive(false);
                    set[v] = go;
                }
                houseSets[i] = set;

                // Where the car for sale stands, and where the player is when
                // they arrive. Both on the drive, both facing the house.
                var carAt = new GameObject("CarSpot");
                carAt.transform.SetParent(plotGO.transform, false);
                carAt.transform.position = new Vector3(x, 0.02f, StreetZ + 9.5f);
                carAt.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                var standAt = new GameObject("Stand");
                standAt.transform.SetParent(plotGO.transform, false);
                standAt.transform.position = new Vector3(x + 3.4f, 0.1f, StreetZ + 7.0f);
                standAt.transform.rotation = Quaternion.Euler(0f, -55f, 0f);
            }

            // ---- the player ----
            // Placed on the middle plot at bake time; the world moves them onto
            // whichever plot the seed chose, which is always the middle one
            // today but need not stay that way.
            var player = FootRig.Build(new Vector3(0f, 0.2f, StreetZ + 6.5f), 0f, out Camera cam);

            var systems = new GameObject("SellerSystems");
            systems.AddComponent<PSXBootstrap>();

            var walk = player.GetComponent<FirstPersonWalk>();
            var interactor = player.GetComponent<FootInteractor>();

            var touch = systems.AddComponent<FootTouchPanel>();
            touch.walker = walk;
            touch.interactor = interactor;

            var screen = systems.AddComponent<FootScreen>();
            screen.interactor = interactor;
            screen.walker = walk;
            screen.panel = touch;
            screen.place = "VIEWING";

            var world = systems.AddComponent<SellerLotWorld>();
            world.plots = plots;
            world.screen = screen;
            world.player = player.transform;
            world.houses = new SellerLotWorld.PlotHouses[Plots];
            for (int i = 0; i < Plots; i++)
                world.houses[i] = new SellerLotWorld.PlotHouses { variants = houseSets[i] };

            FootRig.BuildLighting(WorldKit.MatDir, indoors: false);
            string display = FootRig.BuildDisplay(cam, WorldKit.MatDir);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SellerLot] Scene saved: " + ScenePath + "  (" + Plots +
                      " plots x " + Variants.Length + " variants, display " + display + ")");
        }

        /// <summary>An invisible wall. Same job as the home lot's LotBound.
        /// </summary>
        static void Bound(Transform parent, Vector3 centre, Vector3 size)
        {
            var go = new GameObject("Bound");
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.layer = WorldKit.SolidLayer;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
        }
    }
}
