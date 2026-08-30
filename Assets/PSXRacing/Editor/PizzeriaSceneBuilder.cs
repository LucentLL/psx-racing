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
        const string CargoDir = Root + "/Resources/PizzaCargo";
        /// <summary>Most boxes one order can be. The stack is built this tall
        /// and revealed a box at a time — the scene is baked once and the order
        /// is not rolled until the player is at the counter. The NUMBER lives in
        /// LifeRules, with the rule that rolls against it.</summary>
        const int MaxCarriedBoxes = PSXRacing.LifeSim.LifeRules.MaxOrderBoxes;

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

            // ---- the order: the pack's own stack of boxes ----
            var counterStack = FindOrderStack(shop, doorPos, inward);
            var order = counterStack.Length > 0 ? counterStack[0] : null;
            if (order == null) Debug.LogWarning("[Pizzeria] no Pizza_box found — the counter will have no order on it");
            else Debug.Log("[Pizzeria] counter stack of " + counterStack.Length + ", top " + order.name);

            // ---- player ----
            var player = FootRig.Build(spawn, yaw, out Camera cam);

            // The carried order is built from the BAKED CARGO parts — the same
            // box, lid and pizza prefabs that ride on the passenger seat during
            // the run. Not a copy of the counter prop any more, and that is the
            // fix: Instantiate + SetParent(cam, false) throws away the local
            // rotation chain that made the pack's box lie flat on its shelf, so
            // what the player was handed was a 70 cm box held on its edge like a
            // briefcase. The baked prefabs are measured flat at bake time, so
            // there is no chain left to lose.
            var carriedStack = BuildCarriedStack(cam.transform, out Transform[] carriedBoxes);

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
            shift.counterStack = counterStack;
            shift.carriedOrder = carriedStack;
            shift.carriedBoxes = carriedBoxes;
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
        /// The order on the counter: the pack's own stack of pizza boxes,
        /// nearest the dining room rather than nearest the oven — that is where
        /// a customer's order waits, and it is the one the player walks past on
        /// the way in.
        ///
        /// The whole STACK now, top box first, because an order can be up to
        /// three boxes and the shop should visibly lose the ones the player
        /// walks out with. The pack stacks four of them in one place, which is
        /// more than enough.
        /// </summary>
        static Transform[] FindOrderStack(GameObject root, Vector3 doorPos, float inward)
        {
            // Group the pack's boxes by where they stand, then take the pile
            // nearest the door: they are stacked, so grouping by plan position
            // is what separates one pile from another.
            var piles = new List<List<Renderer>>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("Pizza_box")) continue;
                List<Renderer> pile = null;
                foreach (var p in piles)
                {
                    var c = p[0].bounds.center;
                    if (Mathf.Abs(c.x - r.bounds.center.x) < 0.4f &&
                        Mathf.Abs(c.z - r.bounds.center.z) < 0.4f) { pile = p; break; }
                }
                if (pile == null) { pile = new List<Renderer>(); piles.Add(pile); }
                pile.Add(r);
            }
            if (piles.Count == 0) return new Transform[0];

            List<Renderer> best = null;
            float bestAlong = float.MaxValue;
            foreach (var p in piles)
            {
                float along = (p[0].bounds.center.x - doorPos.x) * inward;
                if (along < bestAlong) { bestAlong = along; best = p; }
            }
            // Top first: a box under four others cannot be lifted off, so the
            // one the player takes is the highest.
            best.Sort((a, b) => b.bounds.center.y.CompareTo(a.bounds.center.y));
            var outp = new Transform[best.Count];
            for (int i = 0; i < best.Count; i++) outp[i] = best[i].transform;
            return outp;
        }

        /// <summary>
        /// What the player walks out holding: a stack of up to three real pizza
        /// boxes, FLAT, each with a pizza in it and a lid on top.
        ///
        /// Horizontal is the whole point of this rebuild. A pizza box is carried
        /// flat because the thing in it is a disc of molten cheese, and the
        /// previous version held it on its edge — not by choice but because
        /// Instantiate + SetParent(cam, false) discards the local rotation, and
        /// the pack's box only lies flat because of the transform chain it sits
        /// under on its shelf. The baked cargo prefabs are measured flat at bake
        /// time (see PizzaCargoBaker.SaveFlat), so there is nothing left to
        /// lose.
        ///
        /// Built at MAX SIZE and revealed a box at a time: the scene is baked
        /// once and the order is rolled at the counter, so the stack cannot know
        /// how tall it is until the player is standing in front of it.
        /// </summary>
        static Transform BuildCarriedStack(Transform cam, out Transform[] boxes)
        {
            boxes = new Transform[MaxCarriedBoxes];
            var root = new GameObject("CarriedOrder");
            root.transform.SetParent(cam, false);
            // Low and to the right, tilted so the player is looking down onto
            // the lids rather than at the front edge of the bottom one. Out at
            // 85 cm because these are 41 cm boxes and one held at arm's length
            // is a wall.
            // Down and right, out of the way of the counter you are walking to.
            // The first cut sat it at (0.14, -0.30, 0.85) and a 41 cm box at
            // that distance covers the middle of the room — which is where the
            // order, the till and the way out all are.
            root.transform.localPosition = new Vector3(0.24f, -0.40f, 0.95f);
            root.transform.localRotation = Quaternion.Euler(-20f, -14f, 3f);

            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CargoDir + "/pizza_box.prefab");
            var pizzaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CargoDir + "/pizza_top_0.prefab");
            if (boxPrefab == null)
            {
                Debug.LogError("[Pizzeria] no baked pizza box at " + CargoDir +
                               " — run PSX Racing/Bake Pizza Cargo first");
                return root.transform;
            }

            float step = 0.075f;
            var bb = MeshBounds(boxPrefab);
            if (bb.size.y > 0.005f) step = bb.size.y + 0.004f;

            for (int i = 0; i < MaxCarriedBoxes; i++)
            {
                var box = (GameObject)Object.Instantiate(boxPrefab);
                box.name = "Box" + i;
                box.transform.SetParent(root.transform, false);
                box.transform.localPosition = new Vector3(0f, i * step, 0f);

                // A REAL PIZZA IN IT. Invisible under a closed lid, and that is
                // fine — it is the same object that is in there when the box
                // comes open on the passenger seat, and the owner asked for
                // boxes that actually contain pizzas rather than props that
                // imply one.
                if (pizzaPrefab != null)
                {
                    var pz = (GameObject)Object.Instantiate(pizzaPrefab);
                    pz.name = "Pizza";
                    pz.transform.SetParent(box.transform, false);
                    pz.transform.localPosition = new Vector3(0f, step * 0.22f, 0f);
                }
                foreach (var c in box.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
                boxes[i] = box.transform;
            }
            Debug.Log("[Pizzeria] carried stack of " + MaxCarriedBoxes +
                      " boxes, " + step.ToString("0.000") + " m pitch");
            return root.transform;
        }

        static Bounds MeshBounds(GameObject prefab)
        {
            var probe = (GameObject)Object.Instantiate(prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rs = probe.GetComponentsInChildren<MeshRenderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(probe);
            return b;
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
