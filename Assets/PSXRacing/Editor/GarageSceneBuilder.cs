using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Builds the walk-in HOME: the player's small house with its one-car
    /// garage, on a grass lot with a driveway down to the street. Replaces the
    /// windowless workshop this scene used to be — the LifeSim's starting rung
    /// is "SMALL HOUSE — 1-CAR GARAGE", and this is that house, standing where
    /// the player can walk up its driveway.
    ///
    /// Same philosophy as ever — the builder owns everything, nothing is
    /// hand-authored — and the same split down the middle. The LOT is saved
    /// into the scene, because it is the same house for everybody. What is ON
    /// the lot is spawned at runtime by <see cref="GarageWorld"/> out of the
    /// save file: the active car in the garage, spares on the driveway and the
    /// kerb, parts on the rack.
    ///
    /// The scene goes LAST in build settings, after every circuit.
    /// <see cref="TrackCatalog.SceneIndex"/> addresses tracks by position, so a
    /// scene inserted before them would quietly send every race to the wrong
    /// place; appended, it costs nothing.
    /// </summary>
    public static class GarageSceneBuilder
    {
        const string Root = "Assets/PSXRacing";
        const string MatDir = Root + "/Materials";
        /// <summary>The owner's art folder, outside the project. Same shape as
        /// the building and tree packs use: source art lives there, the build
        /// copies what it needs into Assets, and the copy is what ships.</summary>
        const string SrcArtRoot =
            @"C:UsersmcgeeOneDriveDocumentsGame DevelopmentPSX AssetsPSX Racing";
        const string TexDir = Root + "/Art/GasStation/Textures";
        const string HouseDir = Root + "/Art/LifeSim/House";
        public const string ScenePath = Root + "/Scenes/Garage.unity";

        // ---- the lot, in metres ----
        const float LotHalfX = 32f;
        const float YardBackZ = 21f;      // rear fence line
        const float StreetZ = -24f;       // street centreline
        const float LotFrontZ = -27f;     // far kerb — the world ends past it

        // ---- the house, measured off the INSTANTIATED model ----
        // The numbers came out mirrored when derived from the Blender source
        // (the exporter's axis bake flips X), so the door is now MEASURED off
        // the wide Garage_Door mesh after instantiation and everything on the
        // lot — driveway, bays, fixtures, spawn — keys off that. These are the
        // fallbacks for a build where the model is missing entirely.
        static float garX = 4.45f;        // garage doorway centre
        static float garZ = -6.45f;       // garage doorway plane
        const float GarageDepth = 4.6f;   // doorway to rear wall, at scale

        /// <summary>
        /// A US residential interior door is 80 inches. It is the only feature
        /// in this pack with a dimension the real world agrees about, so it is
        /// the ruler the whole house is scaled by.
        ///
        /// The pack ships ~1.23x oversized, and that is not a cosmetic problem:
        /// the player's eye is a fixed 1.62 m, so an oversized house makes the
        /// PLAYER look like a child. The report was "my character appears 3ft
        /// tall (half the door height)" — measured, the doors were 2.51 m.
        /// </summary>
        const float RealInteriorDoorH = 2.03f;

        /// <summary>Five parking spots: the garage bay, the driveway, and
        /// three kerbside. The LifeSim's slot ladder decides how many hold
        /// cars; an empty kerb is a picture of room to grow.</summary>
        const int Bays = 5;

        static Shader psxLit;

        [MenuItem("PSX Racing/Build Garage Scene")]
        public static void Build()
        {
            psxLit = Shader.Find("PSX/Lit");
            if (psxLit == null)
            {
                Debug.LogError("[Home] PSX/Lit not found — did shaders compile?");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lot = new GameObject("Lot");

            var grassMat = Mat("HomeGrass", "Grass.jpg", new Vector2(14f, 11f), texDir: HouseDir + "/Textures");
            var driveMat = Mat("HomeDrive", "ConcreteBare.jpg", new Vector2(2f, 8f), texDir: HouseDir + "/Textures");
            var roadMat = Mat("HomeRoad", "Asphalt.jpg", new Vector2(10f, 2f), texDir: HouseDir + "/Textures");
            var kerbMat = Mat("HomeKerb", null, Vector2.one, new Color(0.62f, 0.60f, 0.57f));
            var shelfMat = Mat("GarageShelf", "Metal_02.jpg", new Vector2(3f, 1f));
            var benchMat = Mat("GarageBench", "Wood.jpg", new Vector2(2f, 1f));
            var boardMat = Mat("GarageBoard", "Board.jpg", Vector2.one);
            var lineMat = Mat("GarageLine", null, Vector2.one, new Color(0.85f, 0.72f, 0.18f));
            var crateMat = Mat("GarageCrate", "Deposit.jpg", Vector2.one);
            var toolMat = Mat("GarageTool", "Metal_01.jpg", Vector2.one);
            var fridgeMat = Mat("HomeFridge", "Refrigerator.png", Vector2.one, texDir: HouseDir + "/Textures");
            var rigMat = Mat("GarageRig", null, Vector2.one, new Color(0.62f, 0.15f, 0.12f));

            // House first: it MEASURES the garage door, and every slab after
            // it — the driveway included — is laid out from that measurement.
            PlaceHouse(lot.transform);
            BuildGrounds(lot.transform, grassMat, driveMat, roadMat, kerbMat);
            var bays = BuildBays(lot.transform, lineMat);
            var rack = BuildRack(lot.transform, shelfMat, out Transform crateAnchor);
            var board = BuildToolBoard(lot.transform, boardMat, out Transform toolAnchor);
            var bench = BuildBench(lot.transform, benchMat, shelfMat);
            var fridge = BuildFridge(lot.transform, fridgeMat);
            BuildEngineHoist(lot.transform, rigMat);
            var door = BuildDoorAnchor(lot.transform);

            BuildLighting();
            var player = BuildPlayer(out Camera cam);
            var display = BuildDisplay(cam);

            // ---- systems ----
            var systems = new GameObject("GarageSystems");
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
            screen.place = "HOME";

            var world = systems.AddComponent<GarageWorld>();
            world.bays = bays;
            world.partsRack = rack;
            world.toolBoard = board;
            world.workbench = bench;
            world.exitDoor = door;
            world.fridge = fridge;
            world.screen = screen;
            world.crateAnchor = crateAnchor;
            world.toolAnchor = toolAnchor;
            world.crateMaterial = crateMat;
            world.toolMaterial = toolMat;
            world.rigMaterial = rigMat;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Home] Scene saved: " + ScenePath +
                      "  (house lot, " + Bays + " bays, display " + display + ")");
        }

        // ------------------------------------------------------------------
        //  the lot
        // ------------------------------------------------------------------
        static void BuildGrounds(Transform parent, Material grass, Material drive,
                                 Material road, Material kerb)
        {
            float depth = YardBackZ - LotFrontZ;
            float midZ = (YardBackZ + LotFrontZ) * 0.5f;

            // SUBDIVIDED, not one big quad. The PSX shader snaps vertices to a
            // coarse grid, and a 64 x 48 m surface drawn as two triangles has
            // its whole area interpolated from four snapping corners — so the
            // ground swims underfoot as you walk. That is the same bug the
            // circuits' ground had, and the same fix: enough vertices that the
            // snap moves each of them a distance you cannot see. Cells are
            // ~2 m, which is finer than the tracks' 9 m because this is a
            // surface the player stands ON rather than drives over at 200 km/h.
            GridSlab(parent, "Yard", new Vector3(0f, 0f, midZ),
                     LotHalfX * 2f, depth, 2f, grass, true, 14f);

            // Driveway, just proud of the grass so the seam never z-fights.
            float driveTop = (garZ + StreetZ + 2.4f) * 0.5f;
            GridSlab(parent, "Driveway", new Vector3(garX, 0.012f, driveTop),
                     4.4f, garZ - StreetZ - 2.4f, 1.5f, drive, false, 5f);

            // The street out front, and its far kerb. The world ends past the
            // kerb — the fog is closed long before the eye gets there.
            GridSlab(parent, "Street", new Vector3(0f, 0.014f, StreetZ),
                     LotHalfX * 2f, 7f, 2f, road, false, 8f);
            Slab(parent, "Kerb", new Vector3(0f, 0.05f, StreetZ + 3.65f),
                 new Vector3(LotHalfX * 2f, 0.14f, 0.3f), kerb, false);

            // Invisible lot boundary: a yard you can wander, not fall out of.
            Wall(parent, new Vector3(0f, 1.5f, YardBackZ), new Vector3(LotHalfX * 2f, 3f, 0.3f));
            Wall(parent, new Vector3(0f, 1.5f, LotFrontZ), new Vector3(LotHalfX * 2f, 3f, 0.3f));
            Wall(parent, new Vector3(-LotHalfX, 1.5f, 0f), new Vector3(0.3f, 3f, depth + 6f));
            Wall(parent, new Vector3(LotHalfX, 1.5f, 0f), new Vector3(0.3f, 3f, depth + 6f));
        }

        /// <summary>
        /// How much to shrink the pack's house so a person fits it: the median
        /// interior-door height against a real 80-inch door. The median rather
        /// than any one door, because the model carries nine of them and two
        /// are a different size; and interior doors rather than the garage door
        /// or the house's overall size, because a door is the one thing whose
        /// real dimension is not a matter of taste.
        /// </summary>
        static float MeasuredScale(GameObject house)
        {
            var heights = new System.Collections.Generic.List<float>();
            foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name;
                if (n != "Door" && !(n.StartsWith("Door_0") && !n.Contains("frame"))) continue;
                heights.Add(r.bounds.size.y);
            }
            if (heights.Count == 0)
            {
                Debug.LogWarning("[Home] no interior doors to measure — house left at 1:1");
                return 1f;
            }
            heights.Sort();
            float median = heights[heights.Count / 2];
            float scale = median > 0.1f ? RealInteriorDoorH / median : 1f;
            Debug.Log("[Home] " + heights.Count + " interior doors, median " +
                      median.ToString("0.00") + " m -> scale " + scale.ToString("0.000"));
            return scale;
        }

        static void Wall(Transform parent, Vector3 centre, Vector3 size)
        {
            var go = new GameObject("LotBound");
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.isStatic = true;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// The house itself: the furnished hero model from the asset pack, its
        /// matching collider mesh, and the wide garage door REMOVED — the bay
        /// stands open so the car inside is the first thing a player sees
        /// walking up the drive.
        /// </summary>
        static void PlaceHouse(Transform parent)
        {
            var housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseDir + "/house_hero.fbx");
            if (housePrefab == null)
            {
                Debug.LogError("[Home] house_hero.fbx missing — the lot builds empty.");
                return;
            }
            var house = (GameObject)Object.Instantiate(housePrefab);
            house.name = "House";
            house.transform.SetParent(parent, false);
            // The pack fronts face +Z after import (the wide garage door and
            // the house's own drive apron measured at POSITIVE z on the first
            // build) — turn the whole house so its true front greets the
            // street. Everything downstream is measured, so it follows.
            house.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            // SCALE, from the model's own doors. Applied before anything is
            // measured off it, because every number below is a world-space
            // bound and world-space bounds move when the scale does.
            float scale = MeasuredScale(house);
            house.transform.localScale = Vector3.one * scale;

            PSXRacingBuilder.ConvertToPSXMaterials(house);
            foreach (var t in house.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;

            // The one-car door opens; the narrow shed door round the back stays
            // shut. Wide vs narrow is the only thing telling them apart — with
            // the width taken as max(x,z) so no import-axis surprise can hide
            // it — and the wide door's centre is the datum the whole lot is
            // laid out from before it disappears.
            //
            // Its BASE is the second datum, and the more important one: the
            // house stands on a foundation, so its garage slab is most of a
            // metre above the model's own origin. Seat the house by that and
            // the garage floor lands at y=0 with the driveway — which is what
            // the lot's own ground slab then IS, because the collider mesh has
            // no garage floor in it at all. Placing the house at a flat -0.04
            // is what left the car parked most of a metre under its own floor.
            bool measured = false;
            float doorBaseY = 0f;
            float widest = 0f;
            foreach (var t in house.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Garage_Door")) continue;
                var r = t.GetComponentInChildren<MeshRenderer>();
                if (r == null) continue;
                var b = r.bounds;
                float w = Mathf.Max(b.size.x, b.size.z);
                if (w > 2.2f && b.size.y > 1.8f && w > widest)
                {
                    widest = w;
                    garX = b.center.x;
                    garZ = b.center.z;
                    doorBaseY = b.min.y;
                    measured = true;
                }
            }
            if (measured)
            {
                // A pure Y move, so the X/Z datums just measured still hold.
                house.transform.position = new Vector3(0f, -doorBaseY, 0f);
                foreach (var t in house.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("Garage_Door")) continue;
                    var r = t.GetComponentInChildren<MeshRenderer>();
                    if (r == null) continue;
                    if (Mathf.Max(r.bounds.size.x, r.bounds.size.z) > 2.2f && r.bounds.size.y > 1.8f)
                        t.gameObject.SetActive(false);
                }
            }
            else house.transform.position = Vector3.zero;
            // Sweep the doorway region for stacked leaves: the pack draws the
            // closed door as more than one mesh, and a bay that still shows a
            // shut door after "opening" hides the car the whole scene is for.
            if (measured)
            {
                foreach (var t in house.GetComponentsInChildren<Transform>(true))
                {
                    var r = t.GetComponent<MeshRenderer>();
                    if (r == null || !t.gameObject.activeInHierarchy) continue;
                    var b = r.bounds;
                    if (Mathf.Abs(b.center.x - garX) > 1.7f) continue;
                    if (Mathf.Abs(b.center.z - garZ) > 0.45f) continue;
                    if (b.size.y < 1.5f || b.center.y > 3.4f) continue;
                    float wide = Mathf.Max(b.size.x, b.size.z);
                    if (wide < 1.2f || wide > 4f) continue;
                    t.gameObject.SetActive(false);
                }
            }
            Debug.Log("[Home] garage door measured=" + measured + " at x=" +
                      garX.ToString("0.00") + " z=" + garZ.ToString("0.00") +
                      "  floor now y=0 (was " + doorBaseY.ToString("0.00") + ")");

            var colPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseDir + "/house_hero_colliders.fbx");
            if (colPrefab != null)
            {
                var cols = (GameObject)Object.Instantiate(colPrefab);
                cols.name = "HouseColliders";
                cols.transform.SetParent(parent, false);
                // Scale and seat IDENTICALLY to the visual house. A collider
                // shell at 1:1 around a house at 0.81 is a set of invisible
                // walls a metre outside the ones you can see.
                cols.transform.localScale = house.transform.localScale;
                cols.transform.position = house.transform.position;
                cols.transform.rotation = house.transform.rotation;
                foreach (var mf in cols.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null) Object.DestroyImmediate(mr);
                    mf.gameObject.isStatic = true;
                }
            }
            else Debug.LogWarning("[Home] house_hero_colliders.fbx missing — house is a ghost.");
        }

        // ------------------------------------------------------------------
        //  bays
        // ------------------------------------------------------------------
        static Transform[] BuildBays(Transform parent, Material lineMat)
        {
            var bays = new Transform[Bays];

            Transform Bay(int i, Vector3 pos, float yawDeg)
            {
                var go = new GameObject("Bay" + i);
                go.transform.SetParent(parent, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
                bays[i] = go.transform;
                return go.transform;
            }

            // Bay 0: inside the garage, nose out the door — the front
            // three-quarter is what greets a player walking up the drive.
            // Dead centre of the doorway: the model's furniture stands off
            // both walls, so the aisles clear on either side of the car.
            Bay(0, new Vector3(garX, 0f, garZ + 2.4f), 180f);
            // Bay 1: the driveway.
            Bay(1, new Vector3(garX, 0f, -14.5f), 180f);
            // Bays 2-4: parallel-parked along the kerb.
            Bay(2, new Vector3(4.5f, 0f, StreetZ + 1.6f), 90f);
            Bay(3, new Vector3(12.5f, 0f, StreetZ + 1.6f), 90f);
            Bay(4, new Vector3(-13.5f, 0f, StreetZ + 1.6f), 90f);

            // Painted kerb ticks so the parking reads as parking.
            foreach (float x in new[] { 0.5f, 8.5f, 16.5f, -9.5f, -17.5f })
                Slab(parent, "KerbTick", new Vector3(x, 0.012f, StreetZ + 1.6f),
                     new Vector3(0.1f, 0.02f, 4.6f), lineMat, false);

            return bays;
        }

        // ------------------------------------------------------------------
        //  garage fixtures — all against the garage's own walls
        // ------------------------------------------------------------------
        /// <summary>
        /// The model's garage is already FURNISHED — red cabinets and a
        /// work-cart down one wall, a shelving run with bins down the other —
        /// so unlike the old empty room, this builder spawns almost no decor
        /// of its own: the first pass duplicated all of it in grey slabs ON
        /// TOP of the pack's furniture. What remains ours: the anchors the
        /// GarageWorld hooks hang from (placed on the model's real fixtures),
        /// the part-crate stack in the rear corner, and the fridge.
        /// </summary>
        static Transform BuildRack(Transform parent, Material shelfMat, out Transform crateAnchor)
        {
            // The rear-left corner floor: crates stack where a garage actually
            // piles its boxes. (garX is the door centre; -X is the wall away
            // from the house interior.)
            // Measured: the garage is 3.10 m across at the scale the doors set,
            // so anything further than ~1.3 m off the centreline is inside a
            // wall rather than against it.
            float x = garX - 1.25f, z = garZ + GarageDepth - 0.5f;

            var root = new GameObject("PartsRack");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x + 0.3f, 0.6f, z - 0.4f);

            var anchor = new GameObject("CrateAnchor");
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = new Vector3(x, 0.05f, z);
            // Local +X runs along the wall, local +Z out into the garage.
            anchor.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            crateAnchor = anchor.transform;

            return root.transform;
        }

        static Transform BuildToolBoard(Transform parent, Material boardMat, out Transform toolAnchor)
        {
            // The model's shelving run on the house-side wall carries the
            // toolbox hook; bought tools spawn as small plaques over the bins.
            float x = garX + 1.35f, z = garZ + 2.8f;

            var root = new GameObject("ToolBoard");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x - 0.45f, 1.4f, z);

            var anchor = new GameObject("ToolAnchor");
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = new Vector3(x - 0.2f, 1.55f, z);
            // Local +Z into the garage (toward -X), local +X along the wall.
            anchor.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            toolAnchor = anchor.transform;

            return root.transform;
        }

        static Transform BuildBench(Transform parent, Material topMat, Material legMat)
        {
            // The model's wooden work-cart on the outer wall IS the bench —
            // the hook just stands next to it.
            var root = new GameObject("Workbench");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(garX - 1.3f, 1.0f, garZ + 3.4f);
            return root.transform;
        }

        // ------------------------------------------------------------------
        //  engine hoist
        // ------------------------------------------------------------------
        /// <summary>
        /// A shop crane in the corner with an LS1 swinging off the chain.
        ///
        /// The engine is a SPRITE, not a model, and that is the whole point of
        /// it being here: the question was whether this game needs modelled
        /// engines or whether a photographed one on a billboard holds up, and
        /// the only way to answer that is to hang one at eye height in a room
        /// the player walks round. The crane itself is primitives, like the
        /// jack and the stands - what has to be right is the HEIGHT and the
        /// reach, because a sprite that floats a foot off the hook reads as a
        /// bug no matter how good the photograph is.
        ///
        /// Placed beside the front bumper of bay 0 rather than over it. There
        /// is no spot in a 3.1 m garage that is not also where the car is, and
        /// of the two ways to be wrong, standing next to the car is the one
        /// that still lets the player see the engine.
        ///
        /// Nothing here is solid. A crane you can walk through is odd; a crane
        /// that wedges the player between itself and the car in a one-car
        /// garage is worse, and the doorway is the only way out.
        /// </summary>
        static void BuildEngineHoist(Transform parent, Material rigMat)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(EngineTexPath);
            if (tex == null)
            {
                tex = ImportEngineSheet();
                if (tex == null) { Debug.LogWarning("[Home] engine sprite missing — no hoist"); return; }
            }

            var root = new GameObject("EngineHoist");
            root.transform.SetParent(parent, false);

            // Mast base, against the house-side wall level with the front
            // bumper. The legs run toward the door so the crane is parked the
            // way one is parked: nose out, ready to be rolled.
            float x = garX + 1.05f, z = garZ + 1.62f;
            const float MastH = 1.92f;
            const float BoomReach = 1.00f;
            const float ChainLen = 0.26f;
            const float EngineSize = 0.86f;

            Slab(root.transform, "HoistMast", new Vector3(x, MastH * 0.5f, z),
                 new Vector3(0.11f, MastH, 0.11f), rigMat, false);
            foreach (float side in new[] { -0.26f, 0.26f })
            {
                Slab(root.transform, "HoistLeg",
                     new Vector3(x + side, 0.09f, z - 0.62f),
                     new Vector3(0.08f, 0.10f, 1.30f), rigMat, false);
                // Casters, so the legs end in something rather than in mid-air.
                foreach (float end in new[] { -1.22f, -0.06f })
                    Slab(root.transform, "HoistCaster",
                         new Vector3(x + side, 0.035f, z + end),
                         new Vector3(0.09f, 0.07f, 0.09f), rigMat, false);
            }
            Slab(root.transform, "HoistCross", new Vector3(x, 0.09f, z + 0.02f),
                 new Vector3(0.62f, 0.10f, 0.10f), rigMat, false);

            // Boom: out over the legs and tilted down a little, the way a
            // loaded one sits. Rotated rather than stepped, because a staircase
            // of little cubes is exactly what the first raise rig looked like.
            float dropY = 0.09f;
            var boom = Slab(root.transform, "HoistBoom",
                new Vector3(x, MastH - 0.05f - dropY * 0.5f, z - BoomReach * 0.5f),
                new Vector3(0.09f, 0.10f, BoomReach + 0.12f), rigMat, false);
            boom.transform.rotation = Quaternion.Euler(
                -Mathf.Atan2(dropY, BoomReach) * Mathf.Rad2Deg, 0f, 0f);

            // Ram: mast to mid-boom, the diagonal that makes it read as a crane
            // rather than as a coat stand.
            var ramA = new Vector3(x, 0.72f, z - 0.04f);
            var ramB = new Vector3(x, MastH - 0.14f, z - BoomReach * 0.45f);
            var ram = Slab(root.transform, "HoistRam", (ramA + ramB) * 0.5f,
                new Vector3(0.08f, 0.08f, Vector3.Distance(ramA, ramB)), rigMat, false);
            ram.transform.rotation = Quaternion.LookRotation(ramB - ramA, Vector3.up);

            float tipY = MastH - 0.05f - dropY;
            float tipZ = z - BoomReach;
            Slab(root.transform, "HoistChain",
                 new Vector3(x, tipY - ChainLen * 0.5f, tipZ),
                 new Vector3(0.035f, ChainLen, 0.035f), rigMat, false);

            // ---- the engine ----
            var engMat = LoadOrCreate("GarageEngineSprite", psxLit);
            engMat.shader = psxLit;
            engMat.mainTexture = tex;
            engMat.color = Color.white;
            // Cut out, not blended: the sheet has a real alpha channel and a
            // cutout costs no sorting. 0.4 rather than 0.5 because the export
            // tops out at alpha 253, so the solid body of the engine is not
            // quite 1.0 anywhere.
            if (engMat.HasProperty("_Cutoff")) engMat.SetFloat("_Cutoff", 0.4f);
            if (engMat.HasProperty("_Affine")) engMat.SetFloat("_Affine", 0f);
            engMat.renderQueue = 2450;
            EditorUtility.SetDirty(engMat);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "EngineSprite";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.position = new Vector3(
                x, tipY - ChainLen - EngineSize * 0.5f + 0.04f, tipZ);
            quad.transform.localScale = new Vector3(EngineSize, EngineSize, 1f);
            var qmr = quad.GetComponent<MeshRenderer>();
            qmr.sharedMaterial = engMat;
            qmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            qmr.receiveShadows = false;

            var bb = quad.AddComponent<PSXRacing.OnFoot.AtlasBillboard>();
            bb.cellSize = new Vector2(1f / 3f, 0.5f);
            // The sheet is 3x2. Unity's UV origin is bottom-left and a PNG's
            // first row is the TOP one, so the sheet's top row lives at v=0.5.
            //   top row:    front | rear  | side
            //   bottom row: side  | above | below
            // Only the four HORIZONTAL views are listed — above and below are
            // on the sheet and are exactly the two a walking player never gets
            // to, so putting them in the rotation would show a plan view of an
            // engine to somebody standing beside it.
            bb.viewOffsets = new[]
            {
                new Vector2(0f / 3f, 0.5f),   // front — accessory drive
                new Vector2(0f / 3f, 0.0f),   // side
                new Vector2(1f / 3f, 0.5f),   // rear — flywheel
                new Vector2(2f / 3f, 0.5f),   // the other side
            };
            bb.facing = Vector3.back;   // the crane faces the door
        }

        const string EngineArtDir = Root + "/Art/Engines";
        const string EngineTexPath = EngineArtDir + "/LS1_V8.png";

        /// <summary>
        /// Copy the sheet out of the art folder and set it up PSX-style.
        ///
        /// Point-filtered with no mips, and NOT compressed: a 512 px cell of
        /// alpha-cut machinery is all high-frequency edges, and DXT eats the
        /// cutout boundary first — the engine comes back with a fringe of
        /// half-transparent grey around every header pipe.
        /// </summary>
        static Texture2D ImportEngineSheet()
        {
            string proj = Directory.GetParent(Application.dataPath).FullName;
            string dst = Path.Combine(proj, EngineTexPath);
            if (!File.Exists(dst))
            {
                string src = Path.Combine(SrcArtRoot, "Engine Sprites", "LS1 V8.png");
                if (!File.Exists(src)) { Debug.LogWarning("[Home] no LS1 sheet at " + src); return null; }
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst);
                AssetDatabase.ImportAsset(EngineTexPath, ImportAssetOptions.ForceUpdate);
            }
            var ti = AssetImporter.GetAtPath(EngineTexPath) as TextureImporter;
            if (ti != null && (ti.filterMode != FilterMode.Point || ti.mipmapEnabled ||
                               ti.textureCompression != TextureImporterCompression.Uncompressed))
            {
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(EngineTexPath);
        }

        /// <summary>The garage fridge — a man-cave staple, and the LifeSim's
        /// meals made walk-up-able. GarageWorld hangs the EAT hook on it.</summary>
        static Transform BuildFridge(Transform parent, Material fridgeMat)
        {
            // Just inside the door on the left, where a garage fridge lives and
            // where it is clear of both the car and the model's own shelving.
            float x = garX - 1.22f, z = garZ + 0.85f;
            Slab(parent, "GarageFridge", new Vector3(x, 0.44f, z),
                 new Vector3(0.58f, 0.88f, 0.58f), fridgeMat, true);
            var root = new GameObject("FridgeAnchor");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0.9f, z + 0.45f);
            return root.transform;
        }

        static Transform BuildDoorAnchor(Transform parent)
        {
            // The front porch door: walking to your own front door is how you
            // get back to the desk, the phone, and the rest of the menus.
            var root = new GameObject("ExitDoor");
            root.transform.SetParent(parent, false);
            // Beside the garage opening, on the porch side, at the scale the
            // doors set. Derived from the door datum so it follows the house.
            root.transform.position = new Vector3(garX + 3.2f, 1.3f, garZ - 0.3f);
            return root.transform;
        }

        // ------------------------------------------------------------------
        //  player, lighting, display
        // ------------------------------------------------------------------
        /// <summary>
        /// The player, on the driveway looking up at the house and the open
        /// garage — the first thing they see is their own place with their own
        /// car in it. The RIG itself comes from FootRig, which is where the
        /// six-foot height lives now that a second walk-in scene wants the
        /// same person standing in it.
        /// </summary>
        static GameObject BuildPlayer(out Camera cam) =>
            FootRig.Build(new Vector3(garX, 0.2f, -13.5f), 0f, out cam);

        /// <summary>
        /// Late-afternoon sun over the lot. Outdoors now, so the fog closes at
        /// the far kerb line and the backdrop past it is the fog's own colour —
        /// the same trick every circuit uses to end its world.
        /// </summary>
        static GameObject BuildLighting() => FootRig.BuildLighting(MatDir, indoors: false);

        /// <summary>
        /// The same low-resolution pipeline the circuits render through: camera
        /// into a 240-line buffer, buffer onto a full-screen RawImage through
        /// the dither blit. The home is part of the same game and has to be
        /// made of the same pixels.
        /// </summary>
        /// <summary>The PSX display chain — see FootRig.BuildDisplay, which is
        /// where it lives now that a second walk-in scene needed it and got only
        /// half of it.</summary>
        static string BuildDisplay(Camera cam) => FootRig.BuildDisplay(cam, MatDir);


        // ------------------------------------------------------------------
        //  primitives
        // ------------------------------------------------------------------
        /// <summary>
        /// One box. Built from Unity's cube primitive rather than from a
        /// generated mesh: a cube's UVs run 0..1 on every face, so the tiling
        /// lives on the MATERIAL, and the alternative — a mesh asset per
        /// surface with hand-authored UVs — would be a dozen more files in
        /// Generated/ for a lot made of a handful of slabs.
        /// </summary>
        /// <summary>
        /// A flat, SUBDIVIDED ground panel. The PSX shader snaps vertices to a
        /// coarse grid; on a two-triangle surface the size of a garden that
        /// snap is shared by four corners and the whole plane visibly swims as
        /// the camera moves. Enough vertices and each one moves by less than a
        /// pixel. UVs are world-space over <paramref name="tile"/> metres, so
        /// the texture does not stretch when a panel changes size.
        /// </summary>
        static GameObject GridSlab(Transform parent, string name, Vector3 centre,
                                   float sizeX, float sizeZ, float cell,
                                   Material mat, bool solid, float tile)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(sizeX) / cell));
            int nz = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(sizeZ) / cell));
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[nx * nz * 6];

            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    float fx = (x / (float)nx - 0.5f) * sizeX;
                    float fz = (z / (float)nz - 0.5f) * sizeZ;
                    int v = z * (nx + 1) + x;
                    verts[v] = new Vector3(fx, 0f, fz);
                    uvs[v] = new Vector2((fx + centre.x) / tile, (fz + centre.z) / tile);
                }
            int t = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int v = z * (nx + 1) + x;
                    tris[t++] = v; tris[t++] = v + nx + 1; tris[t++] = v + nx + 2;
                    tris[t++] = v; tris[t++] = v + nx + 2; tris[t++] = v + 1;
                }

            var mesh = new Mesh { name = "Home_" + name };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, "Home_" + name);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // A plane has no underside to stand on, so the collider is a thin
            // BOX rather than a MeshCollider: a car dropped onto a one-sided
            // mesh at the wrong moment falls through it.
            if (solid)
            {
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(Mathf.Abs(sizeX), 0.4f, Mathf.Abs(sizeZ));
                col.center = new Vector3(0f, -0.2f, 0f);
            }
            return go;
        }

        static void SaveMesh(Mesh mesh, string name)
        {
            const string dir = Root + "/Generated";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Root, "Generated");
            string path = dir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        static GameObject Slab(Transform parent, string name, Vector3 centre, Vector3 size,
                               Material mat, bool solid)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            go.isStatic = true;

            var col = go.GetComponent<Collider>();
            if (!solid) Object.DestroyImmediate(col);

            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        static Material Mat(string name, string texFile, Vector2 tiling,
                            Color? tint = null, string texDir = null)
        {
            var mat = LoadOrCreate(name, psxLit);
            mat.shader = psxLit;
            if (!string.IsNullOrEmpty(texFile))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    (texDir ?? TexDir) + "/" + texFile);
                if (tex == null) Debug.LogWarning("[Home] texture missing: " + texFile);
                mat.mainTexture = tex;
            }
            mat.mainTextureScale = tiling;
            mat.color = tint ?? Color.white;
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0f);
            // Affine off. A yard is a place you stand still in and look around,
            // and the wobble that sells a road going past sells nothing on a
            // wall two metres from your face.
            if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreate(string name, Shader shader)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }
    }
}
