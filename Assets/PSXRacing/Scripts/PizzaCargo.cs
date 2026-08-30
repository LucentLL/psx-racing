using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The pizza on the passenger seat, simulated for real.
    ///
    /// This is the game's Initial D water cup. The owner's pitch is a delivery
    /// driver who happens to be racing, so the cargo is not a number that ticks
    /// down off a damage counter — it is an object on a seat that slides when
    /// you brake, leans when you turn, lifts over a crest and goes everywhere
    /// when you hit something. The tip comes off what is left of it.
    ///
    /// HOW IT IS SIMULATED, and why it is not simply parented to the car:
    /// rigidbodies do not inherit a parent's motion. A box made a child of a
    /// moving car falls straight out of the back of it, and a box made
    /// kinematic is not a simulation at all. So the cargo lives in its OWN
    /// PLACE — a tray far below the world, at <see cref="IslandY"/>, where
    /// nothing else exists to collide with — and the car is brought to the
    /// cargo instead:
    ///
    ///   * the tray takes the car's rotation WITH THE YAW REMOVED, so it pitches
    ///     under braking and rolls in a corner (and tips right over if the car
    ///     does) without spinning on its own axis every time the car turns;
    ///   * every loose body gets the car's own acceleration applied backwards,
    ///     which is exactly the pseudo-force a passenger feels. Measured, not
    ///     modelled: a car in free fall accelerates at g, so the boxes get -g,
    ///     cancel gravity, and float — which is what happens to a pizza over a
    ///     crest, and it falls out of the arithmetic rather than being special-
    ///     cased.
    ///
    /// A stack needs no special rule for "the top one is most at risk" either.
    /// It is the one with nothing on top holding it down, so it goes first.
    /// </summary>
    public class PizzaCargo : MonoBehaviour
    {
        public static PizzaCargo Instance { get; private set; }

        /// <summary>Where the cargo actually lives. Four kilometres under the
        /// track, which is cheaper and far more robust than a physics layer:
        /// there is nothing down here to collide with, nothing down here casts
        /// rays, and the cargo camera's two-metre far plane cannot see the world
        /// from here even if there were.</summary>
        const float IslandY = -4000f;

        /// <summary>How hard an impact is allowed to hit the cargo, in m/s².
        /// A finite-difference acceleration through a wall impact is a single
        /// frame of an enormous number and it will tunnel a box straight out of
        /// the tray. Twelve g is still a violent throw and is survivable by the
        /// solver.</summary>
        const float MaxAccel = 120f;

        /// <summary>Seat geometry, in metres, measured off a real passenger
        /// seat rather than off the box: the cargo has to fit the car, not the
        /// other way round.</summary>
        /// A bench rather than a bucket, and the width is the point: a 41 cm box
        /// on a 52 cm seat has five centimetres of travel and nothing to watch.
        /// Sixty-four gives it a hand's width either way, which is what makes
        /// the Pizza Cam worth looking at.
        const float SeatW = 0.64f, SeatD = 0.56f, SeatLip = 0.045f;
        const float FootwellDrop = 0.26f;

        CarController car;
        Rigidbody carBody;
        Transform tray;
        Rigidbody trayBody;

        Vector3 lastVel;
        bool haveLastVel;

        /// <summary>One box and its contents.</summary>
        class Slot
        {
            public Rigidbody box;
            public Transform lid;
            public Rigidbody pizza;
            /// <summary>The lid's collider while the box is shut. Destroying it
            /// IS opening the box: until then it is the ceiling that keeps the
            /// pizza in.</summary>
            public Collider ceiling;
            /// <summary>How far from the middle of its own box, in that box's
            /// local units, the pizza has to get before it counts as out.
            /// Measured off the box rather than typed in — the prefab carries a
            /// scale and a literal here would mean different things at different
            /// box sizes.</summary>
            public float escapeRadius = 0.26f;
            public bool open;              // lid has come off its seat
            public bool escaped;           // pizza is out of the box
            public bool flipped;           // box went past horizontal at some point
            public bool grounded;          // box left the seat
            public float slideWear;        // accumulated jostling, 0-1
            public float Condition => Mathf.Clamp01(
                1f - slideWear
                   - (escaped ? 0.45f : 0f)
                   - (flipped ? 0.30f : 0f)
                   - (grounded ? 0.22f : 0f)
                   - (open && !escaped ? 0.08f : 0f));
        }

        readonly List<Slot> slots = new List<Slot>();

        /// <summary>The order, worst-case first is NOT how it is reported: the
        /// customer opens every box, so the mean is what the tip is graded on.
        /// One ruined pizza in three is a third of an order ruined.</summary>
        public float Condition
        {
            get
            {
                if (slots.Count == 0) return 1f;
                float sum = 0f;
                foreach (var s in slots) sum += s.Condition;
                return Mathf.Clamp01(sum / slots.Count);
            }
        }

        public int BoxCount => slots.Count;
        /// <summary>Where the cargo camera should look.</summary>
        public Transform Tray => tray;

        // ------------------------------------------------------------------
        /// <summary>
        /// Stand the cargo up for a delivery. Called by RaceHandoffApplier, so
        /// nothing exists on a normal race and no scene needs rebuilding for the
        /// cargo to appear on a delivery.
        /// </summary>
        /// <param name="player">The car to take acceleration and attitude from.
        /// NULL builds a detached rig that ticks nothing on its own — which is
        /// how the headless harness drives it, one step at a time, with
        /// accelerations it chose. A simulation that can only be observed by
        /// playing the game is a simulation that ships unverified.</param>
        public static PizzaCargo Spawn(CarController player, int[] toppings)
        {
            if (toppings == null || toppings.Length == 0) return null;
            var go = new GameObject("PizzaCargo");
            var cargo = go.AddComponent<PizzaCargo>();
            cargo.car = player;
            cargo.carBody = player != null ? player.Body : null;
            cargo.BuildIsland(toppings);
            return cargo;
        }

        void Awake() { if (Instance == null) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void BuildIsland(int[] toppings)
        {
            var origin = new Vector3(0f, IslandY, 0f);
            transform.position = origin;

            var trayGO = new GameObject("Seat");
            trayGO.transform.SetParent(transform, false);
            tray = trayGO.transform;
            trayBody = trayGO.AddComponent<Rigidbody>();
            trayBody.isKinematic = true;
            trayBody.useGravity = false;
            // Interpolation off: the tray is driven from the car's rotation in
            // FixedUpdate and interpolating it would lag the boxes behind their
            // own floor.
            trayBody.interpolation = RigidbodyInterpolation.None;

            // The seat pan, its two bolsters, the backrest — and a footwell
            // floor in front of and below it, because a box that slides off the
            // seat under braking has somewhere real to land. "On the floor" is a
            // state the player can see and understand, and it is much better
            // than a box that falls forever.
            Slab(tray, "Pan", new Vector3(0f, -0.02f, 0f), new Vector3(SeatW, 0.04f, SeatD));
            Slab(tray, "BolsterL", new Vector3(-SeatW * 0.5f, SeatLip * 0.5f, 0f),
                 new Vector3(0.03f, SeatLip, SeatD));
            Slab(tray, "BolsterR", new Vector3(SeatW * 0.5f, SeatLip * 0.5f, 0f),
                 new Vector3(0.03f, SeatLip, SeatD));
            Slab(tray, "Back", new Vector3(0f, 0.16f, -SeatD * 0.5f),
                 new Vector3(SeatW, 0.36f, 0.04f));
            Slab(tray, "Footwell", new Vector3(0f, -FootwellDrop, SeatD * 0.5f + 0.22f),
                 new Vector3(SeatW, 0.04f, 0.44f));
            Slab(tray, "Bulkhead", new Vector3(0f, -FootwellDrop + 0.18f, SeatD * 0.5f + 0.44f),
                 new Vector3(SeatW, 0.36f, 0.04f));

            var boxPrefab = Resources.Load<GameObject>(PizzaCargoBakerNames.Box);
            if (boxPrefab == null)
            {
                Debug.LogWarning("[PizzaCargo] no baked box prefab — run PSX Racing/Bake Pizza Cargo");
                return;
            }

            // The CLOSED box's height, lid included. Stacking on the tray's
            // height alone buries every lid 6 mm into the box above it, and the
            // solver's answer to that is to fire the stack across the car before
            // the lights have gone out.
            float boxH = 0.075f;
            var bb = Bounds(boxPrefab);
            if (bb.size.y > 0.005f) boxH = bb.size.y;

            for (int i = 0; i < toppings.Length; i++)
            {
                // Stacked, with a hair of daylight between them so the solver
                // does not start the race resolving an interpenetration.
                var at = new Vector3(0f, 0.01f + i * (boxH + 0.004f), 0f);
                slots.Add(BuildBox(boxPrefab, toppings[i], at, boxH));
            }
        }

        Slot BuildBox(GameObject boxPrefab, int topping, Vector3 localPos, float boxH)
        {
            var slot = new Slot();

            var go = Instantiate(boxPrefab, tray.TransformPoint(localPos), tray.rotation, transform);
            go.name = "Box" + slots.Count;

            // A BOX, not a block. One BoxCollider over the whole thing is a
            // solid lump, and the very first thing the solver would do is shove
            // the pizza out of the box it is supposed to be inside. So: a floor,
            // four walls, and — while the lid is on — a ceiling.
            //
            // Sizes are LOCAL. The prefab root carries the scale that takes the
            // pack's 70 cm box down to a real 41 cm one, and a collider sized
            // from world bounds on a scaled object is that scale applied twice.
            var b = Bounds(go);
            Vector3 ls = go.transform.lossyScale;
            Vector3 local = new Vector3(b.size.x / Mathf.Max(1e-4f, ls.x),
                                        b.size.y / Mathf.Max(1e-4f, ls.y),
                                        b.size.z / Mathf.Max(1e-4f, ls.z));
            Vector3 lc = go.transform.InverseTransformPoint(b.center);
            // Thicknesses as FRACTIONS of the box, not literals. A 3 cm wall in
            // the prefab's local units is 37% of the height of a box this
            // shallow, and it leaves a pizza no room to be inside at all.
            float hy = Mathf.Max(local.y, 0.02f);
            float wall = hy * 0.16f;
            Wall(go, "Floor", lc + new Vector3(0f, -hy * 0.5f + wall * 0.5f, 0f),
                 new Vector3(local.x, wall, local.z));
            float side = Mathf.Max(local.x, local.z) * 0.05f;
            Wall(go, "WallXn", lc + new Vector3(-local.x * 0.5f + side * 0.5f, 0f, 0f),
                 new Vector3(side, hy, local.z));
            Wall(go, "WallXp", lc + new Vector3(local.x * 0.5f - side * 0.5f, 0f, 0f),
                 new Vector3(side, hy, local.z));
            Wall(go, "WallZn", lc + new Vector3(0f, 0f, -local.z * 0.5f + side * 0.5f),
                 new Vector3(local.x, hy, side));
            Wall(go, "WallZp", lc + new Vector3(0f, 0f, local.z * 0.5f - side * 0.5f),
                 new Vector3(local.x, hy, side));
            slot.ceiling = Wall(go, "Ceiling", lc + new Vector3(0f, hy * 0.5f - wall * 0.5f, 0f),
                                new Vector3(local.x, wall, local.z));
            slot.escapeRadius = Mathf.Max(local.x, local.z) * 0.42f;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.9f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // A cardboard box on cloth: it slides before it rolls, and it does
            // not bounce like a ball.
            rb.linearDamping = 0.35f;
            rb.angularDamping = 1.4f;
            slot.box = rb;

            // The pizza, inside. Its own body from the start rather than
            // parented and released, because a body that pops into existence
            // mid-crash arrives with no velocity and looks pasted on. It is held
            // in by the box walls, which is how a pizza is held in by a box.
            var topPrefab = Resources.Load<GameObject>(PizzaCargoBakerNames.Topping(topping));
            if (topPrefab != null)
            {
                // Just clear of the tray floor. The prefabs are seated on their
                // own base, so this is the floor's thickness plus a millimetre —
                // any higher and it spawns inside the lid, which the solver
                // resolves by launching it.
                float rest = wall * ls.y + 0.002f;
                var pz = Instantiate(topPrefab, go.transform.position + go.transform.up * rest,
                                     go.transform.rotation, transform);
                pz.name = "Pizza" + slots.Count;
                var pb = Bounds(pz);
                Vector3 pls = pz.transform.lossyScale;
                var pc = pz.AddComponent<BoxCollider>();
                pc.center = pz.transform.InverseTransformPoint(pb.center);
                pc.size = new Vector3(pb.size.x / Mathf.Max(1e-4f, pls.x) * 0.92f,
                                      Mathf.Max(pb.size.y / Mathf.Max(1e-4f, pls.y), 0.02f),
                                      pb.size.z / Mathf.Max(1e-4f, pls.z) * 0.92f);
                var prb = pz.AddComponent<Rigidbody>();
                prb.mass = 0.45f;
                prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                prb.interpolation = RigidbodyInterpolation.Interpolate;
                prb.linearDamping = 0.5f;
                prb.angularDamping = 1.8f;
                slot.pizza = prb;
            }

            // The lid came in ON the box — the baked prefab is the assembled,
            // closed thing — and rides there until the box opens, when it is cut
            // loose as its own body. Non-physical while shut, so a three-box
            // stack is three bodies and not six.
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "Lid") { slot.lid = t; break; }

            return slot;
        }

        // ------------------------------------------------------------------
        void FixedUpdate()
        {
            if (car == null || carBody == null) return;
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            // The car's rotation with the yaw taken out. Pitch and roll are what
            // change which way is downhill for a box; yaw does not, and letting
            // it through would spin the seat under the cargo every time the
            // player turned the wheel.
            var rot = car.transform.rotation;
            var yawOnly = Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f);
            var tilt = Quaternion.Inverse(yawOnly) * rot;

            var v = carBody.linearVelocity;
            if (!haveLastVel) { lastVel = v; haveLastVel = true; Tick(Vector3.zero, tilt, dt); return; }
            var accelWorld = (v - lastVel) / dt;
            lastVel = v;

            // Into the car's own axes. The tray holds the same pitch and roll, so
            // pushing the boxes in ITS frame is what makes "the car braked" mean
            // "forward" to a box regardless of which compass direction the car
            // happens to be pointing.
            Tick(car.transform.InverseTransformDirection(accelWorld), tilt, dt);
        }

        /// <summary>
        /// One step of the cargo, given the car's acceleration in the car's own
        /// axes and its attitude with the yaw removed.
        ///
        /// Split out from FixedUpdate so the simulation can be DRIVEN — the
        /// headless harness steps it with accelerations it chose and calls
        /// Physics.Simulate itself. Everything that can go wrong here (a stack
        /// that explodes on frame one, a pizza that tunnels through its own box,
        /// a condition that decays while the car is parked) is invisible in a
        /// still and would otherwise only ever be found by playing.
        /// </summary>
        public void Tick(Vector3 accelCarLocal, Quaternion tilt, float dt)
        {
            if (trayBody == null || dt <= 0f) return;
            trayBody.MoveRotation(tilt);
            var push = tray.TransformDirection(-Vector3.ClampMagnitude(accelCarLocal, MaxAccel));

            foreach (var s in slots)
            {
                if (s.box != null) s.box.AddForce(push, ForceMode.Acceleration);
                if (s.pizza != null) s.pizza.AddForce(push, ForceMode.Acceleration);
            }

            Assess(dt);
        }

        /// <summary>One box's state, for the harness and for a bug report.
        /// Nothing in the game reads this.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                sb.Append("box").Append(i)
                  .Append(" cond ").Append(s.Condition.ToString("0.00"))
                  .Append(s.open ? " OPEN" : " shut")
                  .Append(s.escaped ? " SPILLED" : "")
                  .Append(s.flipped ? " FLIPPED" : "")
                  .Append(s.grounded ? " FLOOR" : "")
                  .Append(" wear ").Append(s.slideWear.ToString("0.00"))
                  .Append("; ");
            }
            return sb.ToString();
        }

        /// <summary>
        /// What state is the order in? Read off the simulation every tick, so
        /// the HUD's falling tip is describing something the player can watch
        /// happen rather than a hidden counter.
        /// </summary>
        void Assess(float dt)
        {
            foreach (var s in slots)
            {
                if (s.box == null) continue;

                // Upside down, or near enough that the lid is not holding
                // anything in.
                float upness = Vector3.Dot(s.box.transform.up, tray.up);
                if (!s.open && upness < 0.62f) Open(s);
                if (upness < -0.1f) s.flipped = true;

                // Off the seat: below the seat pan means it is in the footwell.
                float height = tray.InverseTransformPoint(s.box.position).y;
                if (height < -0.08f) { s.grounded = true; Open(s); }

                if (s.pizza == null) continue;

                // Out of its box: measured against the box, not the world, so a
                // pizza riding along in a box that is itself sliding about is
                // not counted as lost.
                var inBox = s.box.transform.InverseTransformPoint(s.pizza.position);
                float away = new Vector2(inBox.x, inBox.z).magnitude;
                if (away > s.escapeRadius || inBox.y < -0.25f) { s.escaped = true; Open(s); }

                // Jostling. Only the pizza's motion RELATIVE to its box counts —
                // the whole car is moving and none of that matters to the
                // cheese.
                var rel = s.pizza.linearVelocity - s.box.linearVelocity;
                float slide = rel.magnitude;
                if (slide > 0.35f)
                    s.slideWear = Mathf.Clamp01(s.slideWear + (slide - 0.35f) * 0.045f * dt);
            }
        }

        /// <summary>Pop the lid. Once open it stays open: a pizza box that has
        /// been upside down does not close itself, and the pizza can now leave.
        /// </summary>
        void Open(Slot s)
        {
            if (s.open) return;
            s.open = true;
            // The lid stops being a lid. Until this moment the ceiling collider
            // is what keeps the pizza in through every bump on the road; after
            // it, the box is an open tray and physics decides the rest.
            if (s.ceiling != null) { Destroy(s.ceiling); s.ceiling = null; }
            if (s.lid == null) return;
            s.lid.SetParent(transform, true);
            var lb = s.lid.gameObject.AddComponent<BoxCollider>();
            var b = Bounds(s.lid.gameObject);
            Vector3 lls = s.lid.lossyScale;
            lb.center = s.lid.InverseTransformPoint(b.center);
            lb.size = new Vector3(b.size.x / Mathf.Max(1e-4f, lls.x),
                                  Mathf.Max(b.size.y / Mathf.Max(1e-4f, lls.y), 0.015f),
                                  b.size.z / Mathf.Max(1e-4f, lls.z));
            var rb = s.lid.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.12f;
            rb.linearDamping = 0.6f;
            rb.angularDamping = 2.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // A shove off the box, so it visibly comes away rather than sitting
            // in place looking like nothing happened.
            rb.AddForce(s.box.transform.up * 0.4f + s.box.transform.forward * 0.2f,
                        ForceMode.Impulse);
        }

        // ------------------------------------------------------------------
        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>One face of a box's compound collider, as a child so each
        /// face can be sized and the ceiling can be removed on its own.</summary>
        static Collider Wall(GameObject box, string name, Vector3 localCentre, Vector3 localSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(box.transform, false);
            var c = go.AddComponent<BoxCollider>();
            c.center = localCentre;
            c.size = localSize;
            return c;
        }

        static void Slab(Transform parent, string name, Vector3 localCentre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCentre;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("PSX/Lit");
            if (shader != null)
            {
                var m = new Material(shader) { hideFlags = HideFlags.DontSave };
                // Seat-cloth grey. Deliberately drab: the cargo is the subject
                // of this picture and the seat is the thing it is on.
                m.color = name == "Footwell" || name == "Bulkhead"
                        ? new Color(0.20f, 0.20f, 0.22f)
                        : new Color(0.32f, 0.31f, 0.34f);
                mr.sharedMaterial = m;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    /// <summary>Resource paths for the baked cargo, in one place so the baker's
    /// naming and the runtime's loading cannot drift.</summary>
    public static class PizzaCargoBakerNames
    {
        public const string Dir = "PizzaCargo/";
        public static string Topping(int i) => Dir + "pizza_top_" + Mathf.Max(0, i);
        public static string Slice(int i) => Dir + "pizza_slice_" + Mathf.Max(0, i);
        /// <summary>One prefab, assembled: its `Lid` is a child, not a separate
        /// asset. The closed box's HEIGHT is what every consumer needs.</summary>
        public const string Box = Dir + "pizza_box";
        /// <summary>How many toppings the baker writes. A saved order names its
        /// pizzas by index into this, so it is append-only.</summary>
        public const int ToppingCount = 10;
    }
}
