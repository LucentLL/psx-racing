using UnityEngine;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// THE BOXES IN YOUR HANDS.
    ///
    /// Reported after the shift moved into the town's own pizzeria: "when I pick
    /// up a pizza for delivery, it doesn't show me carrying the boxes." Fair —
    /// the counter handed over an order, printed a line about it, and the player
    /// then walked out of the shop and across a car park holding thin air. The
    /// seat cargo and its little camera only exist once you are back in the car,
    /// so between the counter and the door there was nothing at all.
    ///
    /// This is deliberately NOT the cargo rig. That thing is a physics island
    /// with a camera on it, four rigidbodies and a tip score; this is a stack of
    /// the same baked prefabs parented to the walker's head, with a bob on it.
    /// A carried box does not need to be simulated — you are holding it, and the
    /// interesting question (does it survive the drive) starts when it goes on
    /// the seat.
    ///
    /// Spawned and destroyed by whoever is watching the errand — see
    /// TownWorld.Update, which owns the same decision for the seat rig.
    /// </summary>
    public class PizzaCarry : MonoBehaviour
    {
        public static PizzaCarry Instance { get; private set; }

        /// <summary>How far in front of the eye the stack sits, and how far
        /// below it the LID OF THE TOP BOX lands.
        ///
        /// Anchored on the top rather than on the base, which matters because
        /// an order is one box or three: hung off the base, one box sat on the
        /// bottom edge of the frame and three filled half of it. Hung off the
        /// top, both read the same way — you see the top lid and the front
        /// edges below it, and the bottom of a tall pile runs off the frame,
        /// which is what carrying a tall pile looks like.</summary>
        const float Forward = 0.50f, Down = 0.16f;
        /// <summary>Lean, in degrees. A stack carried dead level reads as a
        /// texture stuck to the lens; tipped back a little, it reads as
        /// something being held.</summary>
        const float Tilt = 9f;

        Transform rig;
        float bob;
        /// <summary>How tall the pile is, so the pose can hang it from its top
        /// whether it is one box or three.</summary>
        float stackH;

        public static PizzaCarry Spawn(int[] toppings, int bottles)
        {
            var head = FirstPersonWalk.Current != null ? FirstPersonWalk.Current.head : null;
            return head == null ? null : SpawnOn(head, toppings, bottles);
        }

        /// <summary>The same rig on an ARBITRARY head, which is how the harness
        /// stands it up outside play mode. FirstPersonWalk.Current only exists
        /// once somebody is walking, so a carry that could only be built from it
        /// could only be looked at by playing the game — the same argument
        /// PizzaCargo.Spawn(null, ...) is built on.</summary>
        public static PizzaCarry SpawnOn(Transform head, int[] toppings, int bottles)
        {
            if (Instance != null) return Instance;
            if (head == null) return null;
            var go = new GameObject("PizzaCarry");
            var carry = go.AddComponent<PizzaCarry>();
            // CLAIMED HERE, not in Awake. Awake does not run in edit mode for a
            // plain MonoBehaviour, so the harness's Instance stayed null, every
            // Clear was a no-op and every Spawn built ANOTHER rig on the same
            // head — four orders stacked in one frame, which is exactly what
            // the first contact sheet showed. Claiming it in the factory also
            // fixes the live case where a Clear and a Spawn land in the same
            // frame: Destroy is deferred, so OnDestroy has not run yet.
            Instance = carry;
            carry.Build(head, toppings, bottles);
            return carry;
        }

        /// <summary>Take the boxes away — got in the car, handed the order back,
        /// or the errand ended some other way.</summary>
        public static void Clear()
        {
            if (Instance == null) return;
            var go = Instance.gameObject;
            // Instance cleared HERE rather than left to OnDestroy. Destroy is
            // deferred to the end of the frame — and in edit mode never runs at
            // all — so a Clear followed by a Spawn hands back the rig that was
            // supposed to be gone. The preview pass shot the same one box four
            // times before this line existed.
            Instance = null;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        void Awake() { if (Instance == null) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build(Transform head, int[] toppings, int bottles)
        {
            transform.SetParent(head, false);
            var rigGO = new GameObject("Stack");
            rig = rigGO.transform;
            rig.SetParent(transform, false);

            var boxPrefab = Resources.Load<GameObject>(PizzaCargoBakerNames.Box);
            if (boxPrefab == null) return;
            float boxH = 0.075f;
            var bb = PrefabBounds(boxPrefab);
            if (bb.size.y > 0.005f) boxH = bb.size.y;

            int n = toppings != null ? toppings.Length : 1;
            stackH = (n - 1) * (boxH + 0.002f) + boxH;
            Place(0f);

            for (int i = 0; i < n; i++)
            {
                var box = Instantiate(boxPrefab, rig);
                box.name = "Box" + i;
                // Held CLOSED. The lid is a child of the same prefab the seat
                // rig opens on a spill; nothing opens it here.
                box.transform.localPosition = new Vector3(0f, i * (boxH + 0.002f), 0f);
                // A hand-stacked pile is never square, and a degree or two per
                // box is the difference between a stack and an extruded block.
                box.transform.localRotation = Quaternion.Euler(0f, (i % 2 == 0 ? 2.5f : -3f), 0f);
                Strip(box);
            }

            // The bottles ride on top, LYING DOWN — which is both where they go
            // when your hands are full and the only way they fit. A 33 cm
            // bottle standing on the lid reaches most of the way up the screen
            // and there are two of them; on their side they read as part of the
            // load instead of as a pair of railings in front of the camera.
            var bottlePrefab = Resources.Load<GameObject>(PizzaCargoBakerNames.Bottle);
            if (bottlePrefab == null) return;
            var pb = PrefabBounds(bottlePrefab);
            float br = Mathf.Max(0.02f, Mathf.Max(pb.size.x, pb.size.z) * 0.5f);
            for (int i = 0; i < bottles; i++)
            {
                var b = Instantiate(bottlePrefab, rig);
                b.name = "Bottle" + i;
                // Across the lid, alternating which way the neck points, so two
                // do not read as one extruded object.
                b.transform.localRotation = Quaternion.Euler(0f, i == 0 ? 4f : 184f, 90f);
                // MEASURED onto the lid, not placed by its pivot. The prefab is
                // seated on its BASE — which is the right datum for standing one
                // on a seat and the wrong one for laying it down, because once
                // it is on its side the base is an END and the bottle hangs a
                // third of a metre off whichever way it was turned. Setting the
                // position and then correcting by the bounds is the only way
                // that does not depend on which axis the pack happened to build
                // it along.
                b.transform.localPosition = Vector3.zero;
                var got = InverseTransformBoundsOf(rig, PrefabBounds(b));
                // At the BACK of the lid, both of them. Two litre bottles are big
                // and two of them across the middle of the lid cover the whole
                // order; pushed to the far edge they sit behind the print, which
                // is where you would put them and where they stop being the
                // subject of the shot.
                var want = new Vector3(0f, stackH + br, i == 0 ? 0.085f : 0.175f);
                b.transform.localPosition = want - got.center;
                Strip(b);
            }
        }

        /// <summary>Put the stack where it belongs for a given bob phase. ONE
        /// definition, so the resting pose and the walking pose cannot drift
        /// apart — the first version set the pose in two places and the rig
        /// jumped the first time the player moved.</summary>
        void Place(float amp)
        {
            rig.localPosition = new Vector3(
                Mathf.Sin(bob * 0.5f) * amp * 0.6f,
                -Down - stackH + Mathf.Sin(bob) * amp,
                Forward);
            rig.localRotation = Quaternion.Euler(
                -Tilt + Mathf.Sin(bob) * amp * 60f, 6f, -3f + Mathf.Sin(bob * 0.5f) * amp * 40f);
        }

        /// <summary>
        /// Colliders and rigidbodies OFF.
        ///
        /// These prefabs are built for the seat rig, where they are simulated.
        /// Parented to a camera and left alive, their colliders ride through
        /// walls and doorframes shoving whatever they touch, and the
        /// CharacterController the player is standing in is exactly the sort of
        /// thing that answers. A held box is a picture.
        /// </summary>
        static void Strip(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
        }

        /// <summary>World bounds expressed in a parent's local space. Only
        /// the CENTRE is used, so an axis-aligned box of a rotated object is
        /// good enough — and it is the centre that the pivot gets wrong.</summary>
        static Bounds InverseTransformBoundsOf(Transform space, Bounds world) =>
            new Bounds(space.InverseTransformPoint(world.center), world.size);

        static Bounds PrefabBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        void LateUpdate()
        {
            if (rig == null) return;
            // Bob with the walk, and only with the walk — a stack that sways
            // while the player stands still reads as a physics bug rather than
            // as weight. Driven off the walker's own speed so it stops dead
            // when they do.
            var w = FirstPersonWalk.Current;
            float speed = w != null ? w.PlanarSpeed : 0f;
            bob += Time.deltaTime * (4.6f + speed * 1.7f);
            Place(Mathf.Clamp01(speed / 2.9f) * 0.016f);
        }
    }
}
