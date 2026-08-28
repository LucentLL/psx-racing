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
        const float GarageDepth = 5.7f;   // doorway to rear wall

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

            Slab(parent, "Yard", new Vector3(0f, -0.1f, midZ),
                 new Vector3(LotHalfX * 2f, 0.2f, depth), grass, true);

            // Driveway, just proud of the grass so the seam never z-fights.
            float driveTop = (garZ + StreetZ + 2.4f) * 0.5f;
            Slab(parent, "Driveway", new Vector3(garX, -0.088f, driveTop),
                 new Vector3(4.4f, 0.22f, garZ - StreetZ - 2.4f), drive, false);

            // The street out front, and its far kerb. The world ends past the
            // kerb — the fog is closed long before the eye gets there.
            Slab(parent, "Street", new Vector3(0f, -0.086f, StreetZ),
                 new Vector3(LotHalfX * 2f, 0.22f, 7f), road, false);
            Slab(parent, "Kerb", new Vector3(0f, 0.05f, StreetZ + 3.65f),
                 new Vector3(LotHalfX * 2f, 0.14f, 0.3f), kerb, false);

            // Invisible lot boundary: a yard you can wander, not fall out of.
            Wall(parent, new Vector3(0f, 1.5f, YardBackZ), new Vector3(LotHalfX * 2f, 3f, 0.3f));
            Wall(parent, new Vector3(0f, 1.5f, LotFrontZ), new Vector3(LotHalfX * 2f, 3f, 0.3f));
            Wall(parent, new Vector3(-LotHalfX, 1.5f, 0f), new Vector3(0.3f, 3f, depth + 6f));
            Wall(parent, new Vector3(LotHalfX, 1.5f, 0f), new Vector3(0.3f, 3f, depth + 6f));
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
            house.transform.position = new Vector3(0f, -0.04f, 0f);
            // The pack fronts face +Z after import (the wide garage door and
            // the house's own drive apron measured at POSITIVE z on the first
            // build) — turn the whole house so its true front greets the
            // street. Everything downstream is measured, so it follows.
            house.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            PSXRacingBuilder.ConvertToPSXMaterials(house);
            foreach (var t in house.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;

            // The one-car door opens; the narrow shed door round the back stays
            // shut. Wide vs narrow is the only thing telling them apart — with
            // the width taken as max(x,z) so no import-axis surprise can hide
            // it — and the wide door's centre is the datum the whole lot is
            // laid out from before it disappears.
            bool measured = false;
            foreach (var t in house.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Garage_Door")) continue;
                var r = t.GetComponentInChildren<MeshRenderer>();
                if (r == null) continue;
                var b = r.bounds;
                if (Mathf.Max(b.size.x, b.size.z) > 3f && b.size.y > 2f)
                {
                    garX = b.center.x;
                    garZ = b.center.z;
                    measured = true;
                    t.gameObject.SetActive(false);
                }
            }
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
                    if (Mathf.Abs(b.center.x - garX) > 2.0f) continue;
                    if (Mathf.Abs(b.center.z - garZ) > 0.55f) continue;
                    if (b.size.y < 1.8f || b.center.y > 4f) continue;
                    float wide = Mathf.Max(b.size.x, b.size.z);
                    if (wide < 1.5f || wide > 5f) continue;
                    t.gameObject.SetActive(false);
                }
            }
            Debug.Log("[Home] garage door measured=" + measured + " at x=" +
                      garX.ToString("0.00") + " z=" + garZ.ToString("0.00"));

            var colPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseDir + "/house_hero_colliders.fbx");
            if (colPrefab != null)
            {
                var cols = (GameObject)Object.Instantiate(colPrefab);
                cols.name = "HouseColliders";
                cols.transform.SetParent(parent, false);
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
            Bay(0, new Vector3(garX, 0f, garZ + 2.55f), 180f);
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
            float x = garX - 1.75f, z = garZ + GarageDepth - 0.55f;

            var root = new GameObject("PartsRack");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x + 0.4f, 0.6f, z - 0.4f);

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
            float x = garX + 1.85f, z = garZ + 3.0f;

            var root = new GameObject("ToolBoard");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x - 0.6f, 1.4f, z);

            var anchor = new GameObject("ToolAnchor");
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = new Vector3(x - 0.25f, 1.7f, z);
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
            root.transform.position = new Vector3(garX - 1.8f, 1.0f, garZ + 3.6f);
            return root.transform;
        }

        /// <summary>The garage fridge — a man-cave staple, and the LifeSim's
        /// meals made walk-up-able. GarageWorld hangs the EAT hook on it.</summary>
        static Transform BuildFridge(Transform parent, Material fridgeMat)
        {
            // Front-left inside the bay, clear of the model's furniture runs.
            float x = garX - 1.75f, z = garZ + 1.0f;
            Slab(parent, "GarageFridge", new Vector3(x, 0.44f, z),
                 new Vector3(0.6f, 0.88f, 0.6f), fridgeMat, true);
            var root = new GameObject("FridgeAnchor");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0.9f, z + 0.4f);
            return root.transform;
        }

        static Transform BuildDoorAnchor(Transform parent)
        {
            // The front porch door: walking to your own front door is how you
            // get back to the desk, the phone, and the rest of the menus.
            var root = new GameObject("ExitDoor");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(-2.4f, 1.3f, garZ - 0.4f);
            return root.transform;
        }

        // ------------------------------------------------------------------
        //  player, lighting, display
        // ------------------------------------------------------------------
        static GameObject BuildPlayer(out Camera cam)
        {
            var go = new GameObject("Player");
            // On the driveway, looking up at the house and the open garage —
            // the first thing a player sees is their own place with their own
            // car in it.
            go.transform.position = new Vector3(garX, 0.2f, -13.5f);

            var body = go.AddComponent<CharacterController>();
            body.height = 1.75f;
            body.radius = 0.32f;
            body.center = new Vector3(0f, 0.9f, 0f);
            body.slopeLimit = 50f;
            // Low. A parked car's collider starts about 22 cm off the floor, and
            // the default step height is enough to walk straight up onto the
            // bonnet of one — which is the sort of thing a player finds in the
            // first thirty seconds and never unsees.
            body.stepOffset = 0.15f;

            var headGO = new GameObject("Head");
            headGO.transform.SetParent(go.transform, false);
            headGO.transform.localPosition = new Vector3(0f, 1.62f, 0f);

            var camGO = new GameObject("PSXCamera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(headGO.transform, false);
            cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 160f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.63f, 0.72f, 0.83f);
            camGO.AddComponent<AudioListener>();

            var walk = go.AddComponent<FirstPersonWalk>();
            walk.head = headGO.transform;

            var interactor = go.AddComponent<FootInteractor>();
            interactor.eye = camGO.transform;

            return go;
        }

        /// <summary>
        /// Late-afternoon sun over the lot. Outdoors now, so the fog closes at
        /// the far kerb line and the backdrop past it is the fog's own colour —
        /// the same trick every circuit uses to end its world.
        /// </summary>
        static GameObject BuildLighting()
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.80f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var globals = go.AddComponent<PSXGlobals>();
            globals.sun = light;
            globals.ambient = new Color(0.50f, 0.49f, 0.52f);
            globals.fogColor = new Color(0.63f, 0.72f, 0.83f);
            globals.fogNear = 70f;
            globals.fogFar = 220f;

            var skyShader = Shader.Find("PSX/Sky");
            if (skyShader != null)
            {
                string p = MatDir + "/HomeSky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (sky == null)
                {
                    sky = new Material(skyShader);
                    AssetDatabase.CreateAsset(sky, p);
                }
                sky.shader = skyShader;
                sky.SetColor("_TopColor", new Color(0.25f, 0.44f, 0.75f));
                sky.SetColor("_HorizonColor", new Color(0.70f, 0.78f, 0.87f));
                sky.SetColor("_BottomColor", new Color(0.63f, 0.72f, 0.83f));
                sky.SetFloat("_HorizonSharpness", 1.4f);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }
            else RenderSettings.skybox = null;
            return go;
        }

        /// <summary>
        /// The same low-resolution pipeline the circuits render through: camera
        /// into a 240-line buffer, buffer onto a full-screen RawImage through
        /// the dither blit. The home is part of the same game and has to be
        /// made of the same pixels.
        /// </summary>
        static string BuildDisplay(Camera cam)
        {
            var output = cam.gameObject.AddComponent<PSXCameraOutput>();
            output.height = PSXQuality.Height;

            var outCamGO = new GameObject("OutputCamera");
            var outCam = outCamGO.AddComponent<Camera>();
            outCam.clearFlags = CameraClearFlags.SolidColor;
            outCam.backgroundColor = Color.black;
            outCam.cullingMask = 0;
            outCam.depth = 50f;

            var canvasGO = new GameObject("DisplayCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            canvasGO.AddComponent<CanvasScaler>();

            var rawGO = new GameObject("PSXDisplay");
            rawGO.transform.SetParent(canvasGO.transform, false);
            var raw = rawGO.AddComponent<RawImage>();
            var fitter = rawGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            var rrt = raw.rectTransform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            var blitShader = Shader.Find("PSX/Blit");
            if (blitShader != null)
            {
                var blit = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Blit.mat");
                if (blit == null)
                {
                    blit = new Material(blitShader);
                    AssetDatabase.CreateAsset(blit, MatDir + "/Blit.mat");
                }
                blit.shader = blitShader;
                raw.material = blit;
            }
            output.display = raw;
            return output.height + " lines";
        }

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
