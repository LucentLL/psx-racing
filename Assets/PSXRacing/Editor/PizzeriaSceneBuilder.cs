using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The pizza shop the delivery shift starts in — the owner's own pack,
    /// stood up as its artist arranged it.
    ///
    /// The first version of this file built a room out of grey boxes on the
    /// belief that the pizzeria had no interior. That was wrong twice: it was
    /// reading `Art/LifeSim/Pizzeria/pizzeria.fbx`, a storefront from the
    /// BUILDINGS pack that Charlotte uses as a drive-thru prop, and it
    /// concluded "no interior" from that model having a solid box collider —
    /// which is a statement about collision and never about whether a mesh has
    /// an inside. The real pack is `Pizzeria_Scene.fbx`: a whole city block,
    /// 603 renderers, with a front of house, a kitchen and a walk-in.
    ///
    /// So: NOTHING here is authored. The shop, the street and every fitting are
    /// the pack's. What this adds is the three places the player can act, and
    /// even those are MEASURED off the model rather than typed in — the door
    /// tells us which way is out, a raycast tells us where the floor is, and
    /// the order the player picks up is one of the pack's own pizza boxes.
    /// </summary>
    public static class PizzeriaSceneBuilder
    {
        public const string ScenePath = "Assets/PSXRacing/Scenes/Pizzeria.unity";
        const string Root = "Assets/PSXRacing";
        const string MatDir = Root + "/Materials";
        const string PackDir = Root + "/Art/LifeSim/PizzeriaScene";

        static Shader psxLit;

        [MenuItem("PSX Racing/Build Pizzeria Scene")]
        public static void Build()
        {
            psxLit = Shader.Find("PSX/Lit");
            if (psxLit == null) { Debug.LogError("[Pizzeria] PSX/Lit missing"); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackDir + "/Pizzeria_Scene.fbx");
            if (prefab == null)
            {
                Debug.LogError("[Pizzeria] Pizzeria_Scene.fbx missing at " + PackDir);
                return;
            }

            var shop = (GameObject)Object.Instantiate(prefab);
            shop.name = "PizzeriaScene";
            PSXRacingBuilder.ConvertToPSXMaterials(shop);
            int cols = AddColliders(shop);

            // ---- measure ----
            // The double door on the west wall is the datum for everything: it
            // is the only feature whose position tells us both where the player
            // comes in and which way "outside" is.
            if (!FindDoor(shop, out Vector3 doorPos, out float doorH))
            {
                Debug.LogError("[Pizzeria] no Door in the pack — cannot place the player");
                return;
            }
            Physics.SyncTransforms();

            // Inside is +X (the kitchen and the prep tables are that way), out
            // is -X. Confirmed by raycast rather than assumed: whichever side
            // has a ceiling over it at head height is the inside.
            float inward = InteriorIsPlusX(doorPos, doorH) ? 1f : -1f;

            float insideY = FloorAt(doorPos + new Vector3(inward * 2.5f, 0f, 0f), doorPos.y);
            var spawn = new Vector3(doorPos.x + inward * 2.2f, insideY + 0.05f, doorPos.z);
            float yaw = inward > 0f ? 90f : -90f;   // face down the shop

            Debug.Log("[Pizzeria] door at " + doorPos.ToString("0.00") + " h " + doorH.ToString("0.00") +
                      "  inward x" + (inward > 0 ? "+" : "-") +
                      "  interior floor y " + insideY.ToString("0.00") +
                      "  (" + cols + " colliders)");

            // ---- the order: one of the pack's own boxes ----
            var order = FindOrderBox(shop, doorPos, inward);
            if (order == null) Debug.LogWarning("[Pizzeria] no Pizza_box found — the counter will have no order on it");

            // ---- player ----
            var player = FootRig.Build(spawn, yaw, out Camera cam);

            // The carried order is a COPY of the box on the counter, so what the
            // player walks out with is visibly the thing they picked up.
            Transform carried = order != null ? BuildCarriedBox(cam.transform, order) : null;

            // ---- the car, out on the street ----
            var carGO = BuildParkedCar(doorPos, inward, out Vector3 carAt);
            Debug.Log("[Pizzeria] car parked at " + carAt.ToString("0.00"));

            // ---- systems ----
            var systems = new GameObject("PizzeriaSystems");
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
            screen.place = "WORK";

            // doorPos.y is the door's BASE, i.e. the floor. Waist height above
            // it, not half a door height below it — the first cut subtracted
            // where it should have added and buried the prompt 20 cm under the
            // tiles, which reads as the door simply not being interactive.
            //
            // On the INSIDE of the threshold, not the outside. The door leaf is
            // 2.43 m tall so AddColliders gives it a MeshCollider like every
            // other panel in the pack: the shop is a sealed shell and the player
            // never stands on the street side of this anchor. An anchor 80 cm
            // out there was reachable only by leaning on the glass, and it is
            // the control that STARTS THE DELIVERY now — the one prompt in the
            // room that has to be impossible to miss.
            var doorAnchor = new GameObject("DoorAnchor");
            doorAnchor.transform.position =
                doorPos + new Vector3(inward * 0.35f, 1.0f, 0f);

            var shift = systems.AddComponent<PizzaShift>();
            shift.screen = screen;
            shift.counterOrder = order;
            shift.carriedOrder = carried;
            // Generous reach, because the order sits BEHIND the counter — which
            // is where an order sits in a pizza shop. The player stands on the
            // customer side and reaches over; a range that only worked from
            // inside the kitchen would mean walking round through the staff
            // door to pick up your own delivery.
            shift.counterHook = Hook(order != null ? order : shop.transform, 4.5f);
            shift.carHook = Hook(carGO.transform, 4.2f);
            // The door out-ranges everything else it competes with, because it
            // is the exit AND the start of the run and a player walking at it
            // holding a pizza should have the prompt up before they arrive. It
            // sits on the far side of the room from the counter, so a generous
            // radius here cannot steal the counter's prompt.
            shift.doorHook = Hook(doorAnchor.transform, 3.4f);

            FootRig.BuildLighting(MatDir, indoors: true);
            string display = FootRig.BuildDisplay(cam, MatDir);
            Debug.Log("[Pizzeria] display " + display);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Pizzeria] Scene saved: " + ScenePath);
        }

        // ------------------------------------------------------------------
        //  measuring the pack
        // ------------------------------------------------------------------
        /// <summary>
        /// Colliders on everything a player can bump into, and NOTHING smaller.
        ///
        /// The pack is 603 renderers and most of them are condiments: a mesh
        /// collider on a 6 cm ketchup bottle is a thing for the character
        /// controller to catch on, and there are fifteen of them on one shelf.
        /// The floor, the walls, the counter and the furniture are what the
        /// player actually walks on and into.
        /// </summary>
        static int AddColliders(GameObject root)
        {
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var s = r.bounds.size;
                bool bigEnough = (s.x > 0.35f && s.z > 0.35f) || s.y > 0.6f;
                if (!bigEnough) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                n++;
            }
            return n;
        }

        static bool FindDoor(GameObject root, out Vector3 pos, out float height)
        {
            pos = Vector3.zero; height = 0f;
            var doors = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name == "Door" || r.name.StartsWith("Door."))
                    doors.Add(r);
            if (doors.Count == 0) return false;

            var b = doors[0].bounds;
            foreach (var d in doors) b.Encapsulate(d.bounds);
            // The BASE of the door is the floor; its centre is not.
            pos = new Vector3(b.center.x, b.min.y, b.center.z);
            height = b.size.y;
            return true;
        }

        /// <summary>
        /// Which side of the door is indoors? The side with a ceiling over it.
        ///
        /// Asked rather than assumed because getting it backwards spawns the
        /// player in the middle of the road facing a wall, and that failure
        /// looks exactly like the scene not having loaded.
        /// </summary>
        static bool InteriorIsPlusX(Vector3 doorPos, float doorH)
        {
            var up = Vector3.up;
            float probeY = doorPos.y + 1.2f;
            bool plus = Physics.Raycast(new Vector3(doorPos.x + 2.5f, probeY, doorPos.z), up, 8f);
            bool minus = Physics.Raycast(new Vector3(doorPos.x - 2.5f, probeY, doorPos.z), up, 8f);
            if (plus != minus) return plus;
            return true;   // the pack's kitchen is +X; fall back to it
        }

        static float FloorAt(Vector3 at, float fallback)
        {
            var from = new Vector3(at.x, at.y + 2.0f, at.z);
            return Physics.Raycast(from, Vector3.down, out var h, 6f) ? h.point.y : fallback;
        }

        /// <summary>
        /// The order on the counter: the pack's own top pizza box, nearest the
        /// dining room rather than nearest the oven — that is the one a
        /// customer's order would be waiting on, and the one the player walks
        /// past on the way in.
        /// </summary>
        static Transform FindOrderBox(GameObject root, Vector3 doorPos, float inward)
        {
            Transform best = null;
            float bestScore = float.MaxValue;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("Pizza_box")) continue;
                // Closest to the door along the shop's length, and highest in
                // its stack: a box under four others cannot be lifted off.
                float along = (r.bounds.center.x - doorPos.x) * inward;
                float score = along - r.bounds.center.y * 2f;
                if (score < bestScore) { bestScore = score; best = r.transform; }
            }
            return best;
        }

        static Transform BuildCarriedBox(Transform cam, Transform source)
        {
            var copy = Object.Instantiate(source.gameObject);
            copy.name = "CarriedOrder";
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            copy.transform.SetParent(cam, false);
            // The box rides low and right, the way one does on a flat hand, and
            // is scaled to the size it looks in the shop rather than the size it
            // is: the pack's boxes are generous, and one at arm's length fills
            // half the screen.
            copy.transform.localScale = source.lossyScale * 0.5f;
            // Further out and lower than a first guess put it: the pack's boxes
            // are a generous 0.69 m, and one held at 55 cm filled the bottom
            // corner of the screen and hid the counter you are walking to.
            copy.transform.localPosition = new Vector3(0.30f, -0.40f, 0.86f);
            copy.transform.localRotation = Quaternion.Euler(8f, -16f, 5f);
            return copy.transform;
        }

        /// <summary>
        /// The player's car, out on the street in front of the shop.
        ///
        /// A grey block rather than their real car: the shells are baked per
        /// catalog entry into Resources and this scene is built ONCE for every
        /// career, so it cannot know which one to stand here. What it has to get
        /// right is the size and where the driver's door is, so walking up to it
        /// reads as walking up to a car. Seated on whatever the raycast finds,
        /// because the pack's pavement and road are not at the shop's floor
        /// level and a car floating over a kerb is the first thing anyone sees.
        ///
        /// SCENERY, though it still carries a hook. The shop is a sealed shell —
        /// the door leaf takes a collider like every other panel — so nobody
        /// walks out here, and putting the only "start the run" control on this
        /// object is what stranded the first version of the shift with a pizza
        /// in its hands and nothing to press. The door starts the run; this
        /// stands in the window so there is visibly a car to walk out to.
        /// </summary>
        static GameObject BuildParkedCar(Vector3 doorPos, float inward, out Vector3 at)
        {
            var body = Mat("PizzaCarStandIn", new Color(0.42f, 0.44f, 0.50f));
            var tyre = Mat("PizzaCarTyre", new Color(0.10f, 0.10f, 0.11f));

            // Out from the door, past the pavement AND past the kerb. Six
            // metres left the nearside wheels up on the flags — the pavement in
            // this pack is wide, and the kerb line is about seven metres out
            // from the shop front.
            var guess = new Vector3(doorPos.x - inward * 8.2f, doorPos.y, doorPos.z);
            float y = FloorAt(guess, doorPos.y);
            at = new Vector3(guess.x, y, guess.z);

            var go = new GameObject("PlayerCar");
            go.transform.position = at;
            // Parked along the kerb, so nose runs with the street (the Z axis
            // here) rather than pointing at the shop window.
            go.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

            Slab(go.transform, "Body", at + new Vector3(0f, 0.62f, 0f),
                 new Vector3(1.78f, 0.78f, 4.29f), body);
            Slab(go.transform, "Roof", at + new Vector3(0f, 1.22f, -0.18f),
                 new Vector3(1.62f, 0.52f, 2.10f), body);
            foreach (var w in new[] {
                new Vector3(-0.82f, 0.33f, 1.35f), new Vector3(0.82f, 0.33f, 1.35f),
                new Vector3(-0.82f, 0.33f, -1.35f), new Vector3(0.82f, 0.33f, -1.35f) })
                Slab(go.transform, "Wheel", at + w, new Vector3(0.22f, 0.66f, 0.66f), tyre);

            return go;
        }

        // ------------------------------------------------------------------
        static FootTarget Hook(Transform where, float range)
        {
            if (where == null) return null;
            var go = new GameObject("Hook");
            go.transform.SetParent(where, false);
            var h = go.AddComponent<FootTarget>();
            h.range = range;
            return h;
        }

        static GameObject Slab(Transform parent, string name, Vector3 centre, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = centre;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        static Material Mat(string name, Color tint)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(psxLit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = psxLit;
            mat.color = tint;
            if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
