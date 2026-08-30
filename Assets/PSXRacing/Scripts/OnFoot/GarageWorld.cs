using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Fills the empty room the scene builder saved with whatever the save file
    /// says the player owns: their cars in the bays, their parts on the rack,
    /// their tools on the board.
    ///
    /// The split is deliberate. The ROOM is baked — walls, floor, lights,
    /// shelving, the door — because none of it depends on the player and
    /// nothing is cheaper than geometry that is already there. The CONTENTS are
    /// spawned here, because a garage that showed four cars in a scene file
    /// would be showing four cars to a player who owns one.
    ///
    /// Every interaction that needs a MENU walks the player back to the
    /// LifeHome screen on the right tab rather than rebuilding that menu in 3D.
    /// The tuning ladder, the fault quotes and the toolbox are hundreds of lines
    /// of rules each; a second implementation of them standing at a workbench
    /// would be a second set of prices to keep in agreement with the first.
    /// What happens IN here is what only makes sense in here — walking up to a
    /// particular car and choosing it because you are looking at it.
    /// </summary>
    public class GarageWorld : MonoBehaviour
    {
        [Header("Wired by the scene builder")]
        /// <summary>Parking spots, nose along each transform's +Z.</summary>
        public Transform[] bays = new Transform[0];
        public Transform partsRack;
        public Transform toolBoard;
        public Transform workbench;
        public Transform exitDoor;
        /// <summary>The garage fridge. Eating at home, without the menu: the
        /// same EatMeal rule the EAT tab runs, standing in front of the thing
        /// the food is actually in.</summary>
        public Transform fridge;
        public FootScreen screen;

        /// <summary>Where spawned crates go — the empty shelf run beside the
        /// rack. PSX materials come from the builder as assets rather than
        /// being made here, so the room and its contents shade identically.
        /// </summary>
        public Transform crateAnchor;
        public Transform toolAnchor;
        public Material crateMaterial;
        public Material toolMaterial;
        /// <summary>The jack, the stands and the lift. Its own material, and a
        /// flat unpainted colour: the tool-board texture stretched up a
        /// three-metre column reads as a concrete pillar, which turned the
        /// first version of this room into a car park.</summary>
        public Material rigMaterial;

        /// <summary>
        /// One parking spot and everything the room knows about it: whose car
        /// is in it, the two things the player can do to that car, and the gear
        /// that holds it up when it is in the air.
        ///
        /// The shell is kept as ONE transform — body, wheels and the collider
        /// you walk around, all under a single parent — for exactly one reason:
        /// a car goes up as a car. Raising six separate objects by the same
        /// amount is six chances for one of them to be left on the floor.
        /// </summary>
        class BayState
        {
            public Transform bay;
            public OwnedCar car;
            public FootTarget hook;      // the car itself
            public FootTarget rigHook;   // the jack, or the lift
            public Transform shell;      // body + wheels + collider, as one
            public Transform stands;     // four of them, standing on the floor
            public Transform arms;       // lift arms, which ride up with the car
            public float standsHeight, liftHeight;
            /// <summary>Where the shell is, and where it is going. Separate
            /// because the car takes a couple of seconds to get there and the
            /// player is watching it happen.</summary>
            public float height, target;
            /// <summary>Which gear is currently drawn under the car. Held past
            /// the moment the player presses LOWER so the stands do not blink
            /// out from under a car that is still two feet in the air.</summary>
            public Toolbox.Raise drawn = Toolbox.Raise.Ground;
        }

        readonly System.Collections.Generic.List<BayState> bayStates =
            new System.Collections.Generic.List<BayState>();

        /// <summary>Metres per second. A real two-post lift is slower than
        /// this; two seconds is as long as anyone wants to stand and watch.
        /// </summary>
        const float RaiseSpeed = 0.9f;

        FootTarget rackHook, toolHook, benchHook, doorHook, fridgeHook;

        LifeState S => LifeSimManager.State;

        bool built;

        void Start() => PreviewBuild();

        /// <summary>
        /// Fill the room.
        ///
        /// Named for the tool that needs it public: AddComponent does not call
        /// Start outside play mode, so a reference shot of this scene would
        /// otherwise be a photograph of an empty garage — the same trap
        /// <see cref="CarLights.PreviewBuild"/> exists to get out of, and the
        /// reason the screenshot pass can say anything about the bays at all.
        /// Idempotent, so the two callers cannot double the contents.
        /// </summary>
        public void PreviewBuild()
        {
            if (built) return;
            built = true;
            BuildCars();
            BuildParts();
            BuildTools();
            BuildFixtures();
            RefreshLabels();
        }

        /// <summary>Destroy that works in both modes. Object.Destroy is
        /// deferred to the end of the frame and there are no frames in the
        /// editor, so a collider killed that way outside play mode is still
        /// there when the scene is photographed.</summary>
        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ------------------------------------------------------------------
        //  cars
        // ------------------------------------------------------------------
        void BuildCars()
        {
            var cars = S.cars;
            for (int i = 0; i < bays.Length; i++)
            {
                var bay = bays[i];
                if (bay == null) continue;

                OwnedCar car = i < cars.Count ? cars[i] : null;
                var st = new BayState { bay = bay, car = car };
                bayStates.Add(st);

                var hookGO = new GameObject(car != null ? "Bay_" + car.displayName : "Bay_Empty");
                hookGO.transform.SetParent(bay, false);
                var hook = hookGO.AddComponent<FootTarget>();
                hook.range = 4.6f;
                st.hook = hook;

                // Every bay gets an aim point off the floor, INCLUDING an empty
                // one. A bay marks a parking spot, so its transform sits on the
                // tarmac — and a hook that aims at the tarmac is a hook the
                // ground itself stands between you and, which is a sight test
                // that hides the EMPTY BAY prompt from every position on Earth.
                // An occupied bay overwrites this with its own roof line below.
                var floorFocus = new GameObject("Focus");
                floorFocus.transform.SetParent(bay, false);
                floorFocus.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                hook.focus = floorFocus.transform;

                if (car == null) continue;
                var spec = CarCatalog.Get(car.specId);
                var def = spec != null ? CarModelLibrary.LoadFor(spec)
                                       : CarModelLibrary.Load(CarModelLibrary.Default);
                if (def == null) continue;

                int skin = spec != null
                    ? def.SkinFor(spec.color, Mathf.Abs(car.id != null ? car.id.GetHashCode() : i) % 97)
                    : 0;
                st.shell = SpawnShell(bay, def, skin, out Vector3 roofPoint);

                BuildRaiseRig(st, def);
                // Straight to wherever the save says it was left. A car the
                // player put on the lift last night is on the lift when they
                // walk back in, and it does not perform the two seconds of
                // going up again for an audience that has just arrived.
                SetRaise(st, Toolbox.RaiseOf(S, car), instant: true);

                // Aim at the roof line rather than at the parking spot on the
                // floor: standing beside a car and looking at it means looking
                // at the bodywork, and a focus point at ground level between the
                // axles is one the player has to look DOWN to find.
                //
                // Parented to the BAY and not to the shell, so a car up on the
                // lift keeps a focus point at head height. Aiming at the roof of
                // a car two metres in the air means looking at the ceiling to
                // select the thing you are standing under.
                hook.focus.localPosition = roofPoint;
            }
        }

        /// <summary>
        /// A parked car: body and four wheels, placed with exactly the geometry
        /// <see cref="CarBody"/> and the menu turntable use. The numbers are
        /// measured off the mesh at bake time, so re-deriving them here would be
        /// a third opinion about where a Charger's wheels go.
        /// </summary>
        Transform SpawnShell(Transform bay, CarModelDef def, int skin, out Vector3 roofPoint)
        {
            var mat = def.SkinCount > 0
                ? def.skinMaterials[Mathf.Clamp(skin, 0, def.SkinCount - 1)] : null;
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;

            // The bay marks where the car's MIDDLE is, so the same offset the
            // turntable applies puts a long car and a short one both centred in
            // their bay instead of both hanging out of the front of it.
            float centre = def.colliderCenter.z;

            // Everything that IS the car hangs off this one transform, whose
            // only job is to be the thing a jack lifts.
            var shellGO = new GameObject("Shell");
            shellGO.transform.SetParent(bay, false);
            var shell = shellGO.transform;

            var body = new GameObject("Body");
            body.transform.SetParent(shell, false);
            body.transform.localPosition = new Vector3(0f, def.bodyYOffset, def.bodyZOffset - centre);
            body.transform.localRotation = Quaternion.Euler(0f, def.bodyYaw, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = def.bodyMesh;
            var br = body.AddComponent<MeshRenderer>();
            if (mat != null) br.sharedMaterial = mat;

            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0;
                var w = new GameObject("Wheel" + i);
                w.transform.SetParent(shell, false);
                w.transform.localPosition = new Vector3(
                    (left ? -0.5f : 0.5f) * def.trackWidth,
                    def.wheelRadius,
                    (i < 2 ? 0.5f : -0.5f) * def.wheelbase - centre);
                w.transform.localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f);
                w.transform.localScale = Vector3.one * def.wheelMeshScale;
                w.AddComponent<MeshFilter>().sharedMesh = def.wheelMesh;
                var wr = w.AddComponent<MeshRenderer>();
                if (wheelMat != null) wr.sharedMaterial = wheelMat;
            }

            // Solid enough to walk around rather than through. One box per car,
            // sized off the same measured collider the physics uses. Inside the
            // shell, so a car on the lift takes its solidity up with it and the
            // player can walk underneath — which is the entire point of paying
            // two thousand dollars for a lift.
            var col = new GameObject("Solid");
            col.transform.SetParent(shell, false);
            col.transform.localPosition = new Vector3(0f, def.colliderCenter.y, 0f);
            var box = col.AddComponent<BoxCollider>();
            box.size = def.colliderSize;

            roofPoint = new Vector3(0f, Mathf.Max(def.roofY, 1.1f) * 0.82f, 0f);
            return shell;
        }

        // ------------------------------------------------------------------
        //  getting the car in the air
        // ------------------------------------------------------------------
        /// <summary>
        /// The jack, the stands and — once it is paid for — the lift.
        ///
        /// All of it is primitives. There are no models for this gear yet, and
        /// a box on four posts is an honest placeholder in a game whose cars
        /// are three hundred triangles: what has to be right today is the
        /// HEIGHT and the position, because those are what the player reads as
        /// "the car is up and I can see under it". Swapping in real meshes
        /// later is a change to this one method.
        /// </summary>
        void BuildRaiseRig(BayState st, CarModelDef def)
        {
            var bay = st.bay;
            // Under the sills, inboard of the wheels — where a jack stand goes.
            float sillX = Mathf.Max(0.52f, def.trackWidth * 0.42f);
            float sillZ = Mathf.Max(0.62f, def.wheelbase * 0.30f);
            float centre = def.colliderCenter.z;
            float floor = def.colliderCenter.y - def.colliderSize.y * 0.5f;

            // Real stands run 33-53 cm. The low end is honest and reads as
            // nothing at all from across the room, so this sits high in the
            // range: the whole point is that a player can SEE which cars are up.
            st.standsHeight = 0.44f;
            // High enough to walk under. The player capsule is 1.75 m tall and
            // the measured underside is not the same on any two shells, so the
            // clearance is computed rather than picked — a number that happens
            // to work on the FD is a number that traps somebody's Land Rover.
            st.liftHeight = Mathf.Clamp(1.95f - floor, 1.55f, 2.15f);

            // The trolley jack, parked at the nose. Always present: it came
            // with the first car, and a garage with no jack in it cannot
            // explain where the stands came from. Kept small — the first pass
            // was a 0.64 x 1.7 m slab with a beam through it, which read as a
            // pile of scrap rather than as a tool.
            Prop(bay, "FloorJack", new Vector3(-1.32f, 0.09f, sillZ + 1.30f - centre),
                 new Vector3(0.16f, 0.09f, 0.34f));
            Prop(bay, "JackHandle", new Vector3(-1.32f, 0.30f, sillZ + 0.92f - centre),
                 new Vector3(0.03f, 0.03f, 0.36f), pitch: 32f);

            // Somewhere to stand while you work the jack: at the front corner,
            // outside the bay lines and well clear of the next car along, so
            // the interactor is never choosing between a jack and a bonnet.
            var rigGO = new GameObject("RaiseHook");
            rigGO.transform.SetParent(bay, false);
            rigGO.transform.localPosition = new Vector3(-1.35f, 0f, sillZ + 1.1f - centre);
            st.rigHook = rigGO.AddComponent<FootTarget>();
            st.rigHook.range = 2.7f;
            var rigFocus = new GameObject("Focus");
            rigFocus.transform.SetParent(rigGO.transform, false);
            rigFocus.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            st.rigHook.focus = rigFocus.transform;

            var standsGO = new GameObject("Stands");
            standsGO.transform.SetParent(bay, false);
            st.stands = standsGO.transform;
            // Top of the saddle meets the underside of the raised car. Derived
            // rather than dialled in: the underside sits at the shell's own
            // measured floor, which is a different height on every model, and a
            // stand that stops two inches short reads as broken.
            float saddleTop = st.standsHeight + floor;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * sillX;
                float z = (i < 2 ? 1f : -1f) * sillZ - centre;
                // A stand is a foot, a post and a saddle.
                Prop(standsGO.transform, "StandFoot", new Vector3(x, 0.03f, z),
                     new Vector3(0.26f, 0.03f, 0.26f), PrimitiveType.Cylinder);
                Prop(standsGO.transform, "StandPost",
                     new Vector3(x, (saddleTop - 0.12f) * 0.5f, z),
                     new Vector3(0.12f, (saddleTop - 0.12f) * 0.5f, 0.12f), PrimitiveType.Cylinder);
                Prop(standsGO.transform, "StandSaddle", new Vector3(x, saddleTop - 0.06f, z),
                     new Vector3(0.16f, 0.06f, 0.20f));
            }
            standsGO.SetActive(false);

            if (!Toolbox.Owned(S, Toolbox.Lift)) return;

            // Two posts, bolted to the floor, standing whether or not anything
            // is on them — a lift is installed equipment, not something you get
            // out of a drawer. Every occupied bay gets one: the money bought
            // the fit-out, and the alternative is one lift bay and a
            // car-shuffling errand between the player and the frame rails.
            //
            // Solid, because they are three metres of steel in the middle of
            // the walking route and walking through them would say otherwise.
            float postX = Mathf.Min(1.48f, sillX + 0.62f);
            // Tall enough to clear the roof of THIS car once it is up there.
            // A fixed 3.15 m is right for a coupe and cuts a van in half —
            // every shell in the pack is a different height and the lift is
            // built per bay anyway, so it may as well be built to fit.
            float postTop = Mathf.Clamp(
                st.liftHeight + Mathf.Max(def.roofY, 1.1f) + 0.22f, 3.0f, 4.25f);
            foreach (float side in new[] { -1f, 1f })
            {
                Prop(bay, "LiftPost", new Vector3(side * postX, postTop * 0.5f, -centre),
                     new Vector3(0.15f, postTop * 0.5f, 0.15f), solid: true);
                Prop(bay, "LiftBase", new Vector3(side * postX, 0.035f, -centre),
                     new Vector3(0.30f, 0.035f, 0.44f));
            }
            // The overhead beam is what makes two posts read as a LIFT rather
            // than as two pillars. It is also the piece a player recognises
            // from every workshop they have ever stood in.
            Prop(bay, "LiftBeam", new Vector3(0f, postTop - 0.09f, -centre),
                 new Vector3(postX + 0.15f, 0.09f, 0.13f));

            // The arms ride WITH the car: parented to the shell, so they hold
            // the sills at every height instead of being drawn at one.
            var armsGO = new GameObject("LiftArms");
            armsGO.transform.SetParent(st.shell, false);
            st.arms = armsGO.transform;
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = (i < 2 ? 1f : -1f) * sillZ - centre;
                float inner = sillX * 0.98f;
                Prop(armsGO.transform, "LiftArm",
                     new Vector3(side * (postX + inner) * 0.5f, floor - 0.05f, z),
                     new Vector3((postX - inner) * 0.5f + 0.16f, 0.05f, 0.09f));
                Prop(armsGO.transform, "LiftPad", new Vector3(side * inner, floor - 0.01f, z),
                     new Vector3(0.13f, 0.04f, 0.13f), PrimitiveType.Cylinder);
            }
            armsGO.SetActive(false);
        }

        /// <summary>A placeholder box or post, shaded like everything else the
        /// room spawns. Sizes are given the way Unity's primitives take them:
        /// HALF-EXTENTS for a cube, and diameter across by half-height up for a
        /// cylinder, whose mesh is one unit wide and two tall.</summary>
        Transform Prop(Transform parent, string name, Vector3 pos, Vector3 halfSize,
                       PrimitiveType type = PrimitiveType.Cube, float pitch = 0f,
                       bool solid = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!solid) Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            go.transform.localScale = type == PrimitiveType.Cube ? halfSize * 2f : halfSize;
            var mat = rigMaterial != null ? rigMaterial : toolMaterial;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        float HeightFor(BayState st, Toolbox.Raise r) =>
            r == Toolbox.Raise.Lift ? st.liftHeight
            : r == Toolbox.Raise.Stands ? st.standsHeight : 0f;

        /// <summary>Send the car up or down. The gear appears the moment it is
        /// asked for and is cleared only when the car is back on the floor, so
        /// nothing ever hangs unsupported.</summary>
        void SetRaise(BayState st, Toolbox.Raise to, bool instant = false)
        {
            st.target = HeightFor(st, to);
            if (to != Toolbox.Raise.Ground) st.drawn = to;
            if (instant)
            {
                st.height = st.target;
                if (to == Toolbox.Raise.Ground) st.drawn = Toolbox.Raise.Ground;
                Place(st);
            }
            RefreshRig(st);
        }

        void Place(BayState st)
        {
            if (st.shell == null) return;
            var p = st.shell.localPosition;
            st.shell.localPosition = new Vector3(p.x, st.height, p.z);
        }

        void RefreshRig(BayState st)
        {
            bool up = st.height > 0.01f || st.target > 0.01f;
            if (st.stands != null)
                st.stands.gameObject.SetActive(up && st.drawn == Toolbox.Raise.Stands);
            if (st.arms != null)
                st.arms.gameObject.SetActive(up && st.drawn == Toolbox.Raise.Lift);
        }

        void Update()
        {
            for (int i = 0; i < bayStates.Count; i++)
            {
                var st = bayStates[i];
                if (st.shell == null || st.height == st.target) continue;
                st.height = Mathf.MoveTowards(st.height, st.target, RaiseSpeed * Time.deltaTime);
                Place(st);
                if (st.height == st.target && st.target <= 0f) st.drawn = Toolbox.Raise.Ground;
                RefreshRig(st);
            }
        }

        // ------------------------------------------------------------------
        //  parts and tools
        // ------------------------------------------------------------------
        /// <summary>
        /// Boxes on the rack: one per upgrade stage the player has actually
        /// bought across every car they own, plus one per job still in the post.
        /// A rack with nothing on it is the correct picture for a new career and
        /// a full one is the correct picture for a career that has been spending
        /// — which is the only reason to draw the rack at all.
        /// </summary>
        void BuildParts()
        {
            if (crateAnchor == null) return;

            int crates = 0;
            foreach (var c in S.cars)
                crates += c.upPower + c.upWeight + c.upBrakes + c.upSuspension + c.upTires +
                          (c.welded ? 1 : 0) + (c.supercharged ? 1 : 0);
            crates += S.pendingParts.Count;
            crates = Mathf.Min(crates, 24);

            // Three shelves of eight, filled from the bottom up the way a shelf
            // actually fills.
            const int perShelf = 8;
            for (int i = 0; i < crates; i++)
            {
                int shelf = i / perShelf;
                int slot = i % perShelf;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Crate";
                Kill(go.GetComponent<Collider>());
                go.transform.SetParent(crateAnchor, false);
                go.transform.localScale = new Vector3(0.42f, 0.3f, 0.36f);
                go.transform.localPosition = new Vector3(
                    -1.7f + slot * 0.48f, 0.22f + shelf * 0.62f, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, (i % 3 - 1) * 4f, 0f);
                if (crateMaterial != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial = crateMaterial;
            }
        }

        void BuildTools()
        {
            if (toolAnchor == null) return;

            int slot = 0;
            foreach (var t in Toolbox.All)
            {
                if (!Toolbox.Owned(S, t.id)) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Tool_" + t.id;
                Kill(go.GetComponent<Collider>());
                go.transform.SetParent(toolAnchor, false);
                // Hung on the board: wide and flat against the wall, and
                // progressively bigger down the list, so the two-post lift is
                // visibly the thing that cost two thousand dollars.
                float size = 0.26f + slot * 0.06f;
                go.transform.localScale = new Vector3(size, size * 0.8f, 0.08f);
                go.transform.localPosition = new Vector3(-1.1f + slot * 0.58f, 0.1f, 0f);
                if (toolMaterial != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial = toolMaterial;
                slot++;
            }
        }

        // ------------------------------------------------------------------
        //  fixtures
        // ------------------------------------------------------------------
        void BuildFixtures()
        {
            rackHook = Hook(partsRack, 3.4f);
            toolHook = Hook(toolBoard, 3.0f);
            benchHook = Hook(workbench, 3.0f);
            doorHook = Hook(exitDoor, 4.0f);
            fridgeHook = Hook(fridge, 3.0f);

            if (rackHook != null) rackHook.onUse = () => GoHome("tune");
            if (toolHook != null) toolHook.onUse = () => GoHome("toolbox");
            if (benchHook != null) benchHook.onUse = () => GoHome("service");
            if (doorHook != null) doorHook.onUse = () => GoHome("main");
            if (fridgeHook != null) fridgeHook.onUse = EatFromFridge;
        }

        static FootTarget Hook(Transform where, float range)
        {
            if (where == null) return null;
            var go = new GameObject("Hook");
            go.transform.SetParent(where, false);
            var h = go.AddComponent<FootTarget>();
            h.range = range;
            return h;
        }

        // ------------------------------------------------------------------
        //  labels
        // ------------------------------------------------------------------
        /// <summary>
        /// Rewrite every prompt from the current save. Called once at Start and
        /// again whenever something in here changes the state — which today is
        /// only choosing a car, but choosing a car changes the wording on FIVE
        /// separate prompts, and rebuilding the room to say so would be absurd.
        /// </summary>
        void RefreshLabels()
        {
            for (int i = 0; i < bayStates.Count; i++)
            {
                var st = bayStates[i];
                var hook = st.hook;
                if (hook == null) continue;
                var car = st.car;

                if (car == null)
                {
                    hook.title = "EMPTY BAY";
                    hook.detail = S.cars.Count >= S.garageSlots
                        ? "No room booked for another car."
                        : "Room for one more.";
                    hook.action = "READ THE CLASSIFIEDS";
                    hook.onUse = () => GoHome("market");
                    continue;
                }

                bool active = car.id == S.activeCar;
                var spec = CarCatalog.Get(car.specId);
                hook.title = car.displayName.ToUpperInvariant() + (active ? "   ·   YOURS" : "");
                hook.detail = Condition(car) + "   ·   " + car.odoMiles.ToString("N0") + " mi" +
                              (spec != null ? "   ·   " + spec.name : "");
                hook.action = active ? "OPEN THE GARAGE MENU" : "TAKE THE KEYS TO THIS ONE";

                var target = car;
                hook.onUse = active
                    ? (System.Action)(() => GoHome("garage"))
                    : () =>
                    {
                        S.activeCar = target.id;
                        S.calendarLog.Add("Day " + S.day + ": took the keys to " + target.displayName);
                        LifeSimManager.Save();
                        RefreshLabels();
                        screen?.Toast("NOW DRIVING: " + target.displayName.ToUpperInvariant());
                    };

                // The second verb on a car, and the only second verb in the
                // room: getting UNDER it rather than into it. It says what it
                // costs, because it costs a third of a day and nothing else you
                // can press in here does.
                bool open = Inspection.OpenToday(S, car);
                hook.action2 = open ? "CARRY ON INSPECTING IT" : "INSPECT IT  (a time slot)";
                hook.onUse2 = () => OpenInspection(target);

                RefreshRigLabel(st);
            }

            var activeCar = S.ActiveCar;

            if (rackHook != null)
            {
                rackHook.title = "PARTS RACK";
                rackHook.detail = RackLine(activeCar);
                rackHook.action = "OPEN PARTS + TUNING";
            }

            if (toolHook != null)
            {
                toolHook.title = "TOOL BOARD";
                toolHook.detail = ToolLine();
                toolHook.action = "OPEN THE TOOLBOX";
            }

            if (benchHook != null)
            {
                benchHook.title = "WORKBENCH";
                benchHook.detail = BenchLine(activeCar);
                benchHook.action = "BOOK MECHANIC WORK";
            }

            if (doorHook != null)
            {
                doorHook.title = "THE FRONT DOOR";
                doorHook.detail = "Back inside, to the desk and the phone.";
                doorHook.action = "GO INSIDE";
            }

            if (fridgeHook != null)
            {
                bool canEat = S.foodStock > 0 && !S.ateToday;
                fridgeHook.title = "GARAGE FRIDGE";
                fridgeHook.detail = S.foodStock <= 0
                    ? "Empty. The drive-thrus and the EAT page sell more."
                    : S.foodStock + " meal" + (S.foodStock == 1 ? "" : "s") + " in there (" +
                      (string.IsNullOrEmpty(S.lastMealTier) ? "regular" : S.lastMealTier) + ")" +
                      (S.ateToday ? "  ·  already ate today" : "");
                fridgeHook.action = canEat ? "EAT A MEAL" : "";
            }

            // The prompt on screen is change-gated on WHICH interactable is in
            // front of the player, and this rewrote the wording of the one they
            // are already looking at without changing which one it is. Without
            // this the car they just took the keys to still says TAKE THE KEYS
            // until they look away and back.
            screen?.Invalidate();
        }

        /// <summary>Eat standing at the fridge — the same rule the EAT tab
        /// runs, so the two doors into a meal can never disagree on what one
        /// costs or does.</summary>
        void EatFromFridge()
        {
            if (S.foodStock <= 0 || S.ateToday) return;
            string tier = string.IsNullOrEmpty(S.lastMealTier) ? "regular" : S.lastMealTier;
            LifeRules.EatMeal(S, tier);
            LifeSimManager.Save();
            RefreshLabels();
            screen?.Toast("ATE A " + tier.ToUpperInvariant() + " MEAL — " +
                          S.foodStock + " LEFT");
        }

        /// <summary>
        /// The jack in each bay: what the car is standing on now, and the one
        /// button that changes it.
        ///
        /// One control rather than a choice of heights. There is no reason to
        /// pick jack stands over a lift already paid for, so this raises to the
        /// best gear owned and the title says which that is.
        /// </summary>
        void RefreshRigLabel(BayState st)
        {
            var hook = st.rigHook;
            if (hook == null || st.car == null) return;

            var now = Toolbox.RaiseOf(S, st.car);
            var best = Toolbox.BestRaise(S);
            bool up = now != Toolbox.Raise.Ground;
            string name = st.car.displayName.ToUpperInvariant();

            hook.title = best == Toolbox.Raise.Lift ? "TWO-POST LIFT" : "FLOOR JACK + STANDS";
            hook.detail = now == Toolbox.Raise.Lift
                ? name + " is up on the lift — you can walk under it."
                : now == Toolbox.Raise.Stands
                    ? name + " is up on stands. Room to get underneath."
                    : name + " is sat on its wheels.";
            hook.action = up ? "SET IT BACK DOWN"
                             : best == Toolbox.Raise.Lift ? "RAISE IT ON THE LIFT"
                                                          : "PUT IT UP ON STANDS";

            var bay = st;
            hook.onUse = () =>
            {
                var to = Toolbox.ToggleRaise(S, bay.car);
                SetRaise(bay, to);
                LifeSimManager.Save();
                RefreshLabels();
                screen?.Toast(to == Toolbox.Raise.Ground
                    ? "WHEELS BACK ON THE FLOOR"
                    : "UP " + Toolbox.RaiseName(to));
            };
        }

        static string Condition(OwnedCar car) =>
            "ENG " + Mathf.RoundToInt(car.engine) + "%   TYR " + Mathf.RoundToInt(car.tires) +
            "%   BODY " + Mathf.RoundToInt(car.carHP) + "%   FUEL " + Mathf.RoundToInt(car.fuel) + "%";

        string RackLine(OwnedCar car)
        {
            var sb = new StringBuilder();
            if (car != null)
            {
                int stages = car.upPower + car.upWeight + car.upBrakes + car.upSuspension + car.upTires;
                sb.Append(stages == 0 ? "Nothing bolted to your car yet."
                                      : stages + " upgrade stage" + (stages == 1 ? "" : "s") +
                                        " fitted to " + car.displayName + ".");
                if (car.welded) sb.Append("  Welded diff.");
                if (car.supercharged) sb.Append("  Blower.");
            }
            else sb.Append("No car to fit anything to.");

            int onOrder = S.pendingParts.Count;
            if (onOrder > 0)
            {
                int soonest = int.MaxValue;
                foreach (var p in S.pendingParts) soonest = Mathf.Min(soonest, p.readyDay - S.day);
                sb.Append("   ·   ").Append(onOrder).Append(" on order, next in ")
                  .Append(Mathf.Max(0, soonest)).Append(soonest == 1 ? " day" : " days");
            }
            return sb.ToString();
        }

        string ToolLine()
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var t in Toolbox.All)
            {
                if (!Toolbox.Owned(S, t.id)) continue;
                if (n++ > 0) sb.Append(", ");
                sb.Append(t.name);
            }
            if (n == 0) return "Bare board.";
            int missing = Toolbox.Missing(S).Count;
            if (missing > 0) sb.Append("   ·   ").Append(missing).Append(" still to buy");
            return sb.ToString();
        }

        string BenchLine(OwnedCar car)
        {
            if (car == null) return "Nothing on the bench.";
            int known = 0;
            foreach (var f in car.faults) if (!f.hidden) known++;
            if (known == 0) return "Nothing wrong that anyone has found.";
            return known + (known == 1 ? " fault" : " faults") + " waiting on " + car.displayName + ".";
        }

        // ------------------------------------------------------------------
        //  leaving
        // ------------------------------------------------------------------
        /// <summary>
        /// Open an inspection on the car the player is standing at.
        ///
        /// The car does NOT have to be the one they have the keys to. Looking
        /// underneath a car you own is not driving it, and making the player
        /// take the keys first would be an errand between them and the thing
        /// they walked over here to do.
        ///
        /// The slot is spent HERE rather than on arrival at the menu, for the
        /// same reason the garage screen spends it in its own button: Enter is
        /// what knows whether today's inspection is already open, and a second
        /// caller guessing at that is a second chance to charge twice.
        /// </summary>
        void OpenInspection(OwnedCar car)
        {
            if (car == null) return;
            bool wasOpen = Inspection.OpenToday(S, car);
            Inspection.Enter(S, car);
            LifeHomeScreen.PendingInspectCar = car.id;
            // FINISH INSPECTION walks back in here rather than dropping the
            // player in a menu they never opened.
            LifeHomeScreen.InspectFromGarage = true;
            screen?.Toast(wasOpen ? "BACK UNDER " + car.displayName.ToUpperInvariant()
                                  : "INSPECTING " + car.displayName.ToUpperInvariant());
            GoHome("inspect");
        }


        /// <summary>
        /// Walk back to the front end, opening it on a particular tab.
        ///
        /// The cursor is released first and explicitly. A browser keeps pointer
        /// lock across a Unity scene load, so without this the player arrives at
        /// a menu they cannot click on and no cursor to see where they are
        /// clicking — which reads as the menu being broken.
        /// </summary>
        void GoHome(string tab)
        {
            LifeHomeScreen.PendingTab = tab;
            LifeSimManager.Save();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(0);
        }
    }
}
