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

        /// <summary>
        /// How hard the cargo can be pushed, in m/s². FOUR AND A HALF g.
        ///
        /// This was twelve, and twelve was measured against a harness feeding a
        /// smooth step. A real car's per-frame velocity difference is nothing
        /// like smooth: it carries the suspension working, every kerb, and the
        /// solver's own contact impulses, and on a mountain stage those
        /// transients threw the top box off the stack on the first corner of a
        /// clean lap. Reported exactly that way.
        ///
        /// Four and a half g is still far more than any corner can produce
        /// (a g is a very good corner) so an impact still throws the load — it
        /// just no longer does it because a wheel found a bump.
        /// </summary>
        const float MaxAccel = 45f;

        /// <summary>
        /// Time constant for the acceleration filter, seconds.
        ///
        /// The clamp alone was not enough: a single frame at the ceiling is
        /// still a kick, and there are several of those a second on a rough
        /// road. A tenth of a second is about the length of an impact, so a
        /// crash still climbs most of the way to the ceiling while per-frame
        /// road noise flattens into the sustained number a passenger would
        /// actually feel.
        ///
        /// Fifty milliseconds was the first attempt and it was not enough: the
        /// rough-road case still opened two boxes out of three. What made the
        /// difference alongside it was giving the boxes a floor-level centre of
        /// mass — see BuildBox.
        /// </summary>
        const float AccelTau = 0.10f;
        /// <summary>Same idea for attitude. The car's body pitches and rolls on
        /// its springs over every ripple; a seat does too, but not at fifty
        /// hertz.</summary>
        const float TiltTau = 0.04f;

        /// <summary>
        /// An IMPACT is not an acceleration, and treating it as one is why a
        /// full-speed wall hit did almost nothing to the load.
        ///
        /// Everything above filters and clamps on purpose: it has to, or a kerb
        /// strike reads as a crash and the stack comes apart on a clean lap. But
        /// the same filter turns a 100 km/h stop into a four-and-a-half-g shove
        /// spread over a tenth of a second — a firm push, when what happened was
        /// the car stopping and the cargo not.
        ///
        /// So a collision gets its OWN channel. An unrestrained object in a car
        /// that suddenly loses speed simply keeps the speed it had; relative to
        /// the seat, it lurches by exactly what the car lost. That is applied as
        /// a velocity change rather than a force, which is what it physically
        /// is, and it needs no filter because it is not a continuous quantity.
        ///
        /// It is gated on the CollisionResponder reporting real contact rather
        /// than on the size of the velocity change alone. That distinction is
        /// the whole reason the filter exists: the suspension working over a
        /// rough road produces per-step velocity differences of the same order
        /// as a light collision, and only one of them is something hitting the
        /// car. The responder already classifies that — it ignores landings,
        /// where the normal points up — so this asks it instead of guessing.
        /// </summary>
        const float JoltMinSpeed = 1.2f;
        /// <summary>Ceiling on the lurch, in m/s. A car stopping dead from
        /// 140 km/h would otherwise hand the boxes 39 m/s and fire them through
        /// their own seat between two physics steps. Seven is still violent —
        /// it crosses the 60 cm seat in under a tenth of a second — and it is
        /// survivable by the solver.</summary>
        const float MaxJolt = 7f;
        /// <summary>Radians per second of tumble per m/s of lurch. Small: this
        /// is the difference between a box sliding flat and a box going over,
        /// not a reason for the load to cartwheel.</summary>
        const float JoltSpin = 0.9f;

        /// <summary>
        /// Seat geometry, in metres.
        ///
        /// A BENCH, and wide: the pan is what the Pizza Cam sees, and the owner
        /// asked for "just the car seat and pizzas" — no floorboard and no black
        /// background. A pan sized to a bucket seat left two thirds of the frame
        /// as void. This one fills it, while the BOLSTERS still stand at a real
        /// seat's width so the cargo is confined by the same geometry it always
        /// was.
        /// </summary>
        const float SeatW = 0.80f, SeatD = 0.62f;
        /// <summary>
        /// Where the bolsters stand, and how tall they are.
        ///
        /// This was 0.26 and it is the bug behind "I drove into a wall full
        /// speed and the bottom pizza barely moved, even side to side". A box is
        /// 41 cm across, so its edges are at 0.205 — with the ridges at 0.26 the
        /// bottom box had THREE AND A HALF CENTIMETRES of travel before it hit a
        /// wall of its own seat, in a seat 80 cm wide. It was not resisting the
        /// crash; it was in a jig. The only way out was inverting gravity, which
        /// is exactly what the player had to do.
        ///
        /// Out at 0.335 the ridges sit where a seat's actually do — just inside
        /// the door card and the tunnel at 0.36 — and a box gets 13 cm of slide
        /// before anything catches it. That is a slide worth watching, which was
        /// the whole point of simulating this at all.
        ///
        /// The HEIGHT still has to stay below the boxes' centre of mass: a box
        /// sliding into a ridge taller than its own centre of gravity levers
        /// over it instead of stopping against it. Three centimetres catches and
        /// holds; anything taller is a fulcrum.
        /// </summary>
        const float BolsterHalf = 0.335f, SeatLip = 0.03f;
        CarController car;
        Rigidbody carBody;
        Transform tray;
        Rigidbody trayBody;

        Vector3 lastVel;
        bool haveLastVel;
        /// <summary>How many boxes this order is, known before any of them are
        /// built — the seat's walls have to be tall enough for the whole stack
        /// and they are put up first.</summary>
        int boxesOrdered = 1;
        Vector3 smoothAccel;
        Quaternion smoothTilt = Quaternion.identity;
        bool haveTilt;
        /// <summary>Cardboard on seat cloth. Unity's default is 0.6, which is
        /// less than a hard corner produces — so on the default every box slid
        /// on every bend, which is not what a pizza box on a seat does.</summary>
        PhysicsMaterial grip;

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
            /// <summary>Where this box was put, in the seat's own axes. Kept so
            /// the harness can ask how far it has MOVED — "the bottom pizza
            /// barely moved" is a displacement complaint and the condition
            /// number cannot see it. A box pinned in a jig reads a perfect 1.00
            /// all day, which is exactly how a seat with its bolsters 3.5 cm off
            /// the cargo passed every test it had.</summary>
            public Vector3 startLocal;

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

        /// <summary>How far box <paramref name="i"/> has moved from where it was
        /// put, in metres, measured in the SEAT's axes so the car's own motion
        /// does not count. Zero means it has not moved at all — which is a
        /// failure, not a success, for anything short of a parked car.</summary>
        public float BoxSlide(int i) => BoxOffset(i).magnitude;

        /// <summary>The same displacement, per axis: +x toward the tunnel, +y up
        /// off the seat, +z forward into the footwell. A single magnitude cannot
        /// tell "slid across the seat" from "went out the front", and those are
        /// different bugs.</summary>
        public Vector3 BoxOffset(int i)
        {
            if (i < 0 || i >= slots.Count || slots[i].box == null || tray == null) return Vector3.zero;
            return tray.InverseTransformPoint(slots[i].box.position) - slots[i].startLocal;
        }

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
            boxesOrdered = toppings != null ? toppings.Length : 1;
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

            grip = new PhysicsMaterial("PizzaGrip")
            {
                staticFriction = 0.95f,
                dynamicFriction = 0.85f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                hideFlags = HideFlags.DontSave,
            };

            // The pan and the backrest are what the camera sees, and they are
            // sized to FILL it — the owner's note was "no black background, just
            // the car seat and pizzas", and a seat that does not reach the edges
            // of the frame is a black background by another name.
            // The two pieces that ARE drawn: the cushion and the squab. Sized to
            // a seat rather than to the frame, because the frame is transparent
            // now and does not need filling.
            // The two pieces that ARE drawn: the cushion and the squab, at the
            // FULL seat width — the same width the door card and the tunnel
            // stand at. A visible pan narrower than the walls that confine the
            // cargo would show a box pressed against the door apparently
            // floating off the edge of the seat.
            Slab(tray, "Pan", new Vector3(0f, -0.02f, 0f), new Vector3(SeatW, 0.04f, SeatD));
            Slab(tray, "Back", new Vector3(0f, 0.17f, -SeatD * 0.5f + 0.02f),
                 new Vector3(SeatW, 0.38f, 0.05f));

            // The bolsters stay where a real seat's are, INSIDE the visible pan,
            // so the cargo is still confined by seat-sized geometry. A box that
            // gets over one is out of its seat and on the bench, which the player
            // can see happen — better than the old footwell, which caught it out
            // of frame and told them nothing.
            Slab(tray, "BolsterL", new Vector3(-BolsterHalf, SeatLip * 0.5f, 0f),
                 new Vector3(0.04f, SeatLip, SeatD * 0.8f), visible: false);
            Slab(tray, "BolsterR", new Vector3(BolsterHalf, SeatLip * 0.5f, 0f),
                 new Vector3(0.04f, SeatLip, SeatD * 0.8f), visible: false);
            // THE CAR AROUND THE SEAT: door card one side, transmission tunnel
            // the other, and the dash ahead.
            //
            // Without them the low bolsters merely slowed a sliding box down and
            // it carried on off the edge of the bench — all three boxes read
            // "FLOOR" on a rough corner of a clean lap, which is the bug being
            // fixed. A pizza does not end up in the footwell because you took a
            // bend quickly; it ends up wedged against the door. Far enough out
            // (27 cm of travel from the middle) that the slide is worth
            // watching, and tall enough to hold — while a real impact still
            // throws a box clean over, which is where the damage should come
            // from and nowhere else.
            // TALL ENOUGH FOR THE WHOLE STACK. Fourteen centimetres held the
            // bottom box and nothing else: three 8.8 cm boxes reach 28 cm, so
            // the top two sat clear above the walls and slid off a seat that was
            //, as far as they were concerned, open on both sides. That is the
            // last of "the top pizza fell off on the first turn". A real door
            // card is about this high above a seat base anyway.
            float wallH = Mathf.Max(0.34f, 0.02f + boxesOrdered * 0.1f);
            Slab(tray, "DoorCard", new Vector3(-SeatW * 0.5f + 0.02f, wallH * 0.5f, 0f),
                 new Vector3(0.04f, wallH, SeatD), visible: false);
            Slab(tray, "Tunnel", new Vector3(SeatW * 0.5f - 0.02f, wallH * 0.5f, 0f),
                 new Vector3(0.04f, wallH, SeatD), visible: false);

            // NOTHING TALL ACROSS THE FRONT, and that asymmetry is the mechanic.
            //
            // Sideways there is a door one side and the transmission tunnel the
            // other, so a corner — however hard — slides the load across the seat
            // and stops it. Forward there is a FOOTWELL, so braking and crashing
            // throw it off the seat and onto the floor. That is where the damage
            // comes from and it should be the only place: the owner's report was
            // a top box lost on the first corner of a clean lap, and a seat
            // walled on all four sides fixes that by making a crash harmless
            // too, which is the same bug wearing the other hat.
            // NOTHING ACROSS THE FRONT AT ALL. The seat pan simply ends, and
            // that asymmetry is the mechanic.
            //
            // Sideways there is a door one side and the transmission tunnel the
            // other, so a corner — however hard — slides the load across the
            // seat and stops it. Forward there is a FOOTWELL.
            //
            // Two goes at putting an edge here both failed, and they failed in
            // the same way: a flat 1.5 cm lip stood exactly at a box's centre of
            // mass, so a box hit it square with no tipping moment and parked
            // against it at 80 km/h; pitching that lip into a ramp only made it
            // a 5 cm wall, and then even the middle box stayed put. What holds
            // the load under braking is not a kerb, it is FRICTION — cardboard
            // on seat cloth is 0.95, so it takes most of a g to start a box
            // moving forward at all, and most of a g is heavy braking, which is
            // exactly when a pizza does slide forward. That rule needs no
            // geometry and has no threshold to get wrong.

            // NOTHING BEHIND THE SEAT. The Pizza Cam clears to transparent and
            // the game shows through — "just have transparency around the pizza
            // boxes and seat, no black, no void". A backdrop slab was the first
            // answer to that and it was the wrong one: it is still a void, just
            // a grey one, and it has to be lit, sized and kept square to a lens
            // it knows nothing about.
            //
            // Out of shot: somewhere for a box that DOES clear the seat to land
            // and stop. Physics only — the renderer is off, because the owner
            // does not want to look at a floorboard, and a box falling forever
            // is not a state the condition can read.
            //
            // LONG, and walled at the far end. A box thrown by a crash leaves
            // the seat at several metres a second and covers 70 cm in a tenth of
            // a second while falling five — so the old footwell was something it
            // sailed clean over on its way to infinity. Two metres of floor and
            // a bulkhead catches one and lets it come to rest where the Pizza
            // Cam can still see what became of it.
            Slab(tray, "Footwell", new Vector3(0f, -0.34f, SeatD * 0.5f + 0.95f),
                 new Vector3(SeatW * 1.6f, 0.04f, 2.0f), visible: false);
            Slab(tray, "Bulkhead", new Vector3(0f, -0.20f, SeatD * 0.5f + 1.93f),
                 new Vector3(SeatW * 1.6f, 0.32f, 0.06f), visible: false);

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
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.sharedMaterial = grip;
            slot.escapeRadius = Mathf.Max(local.x, local.z) * 0.42f;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // A cardboard box on cloth: it slides before it rolls, and it does
            // not bounce like a ball.
            rb.linearDamping = 0.35f;
            rb.angularDamping = 3.0f;
            // NEVER SLEEPS, and this is the other half of "the bottom pizza
            // barely moved".
            //
            // PhysX puts a body that has been still for a moment to sleep, and a
            // sleeping body discards forces. The bottom box of a settled stack
            // is the stillest thing in the game — so it slept, and then the
            // acceleration channel pushed at it every frame for nothing and the
            // crash impulse landed on a body that was not listening. The top box
            // was still jostling, stayed awake, and flew off exactly as
            // reported. Two boxes, same impulse, opposite outcomes, and the only
            // difference was which one had gone to sleep.
            //
            // A cargo rig is six bodies that exist to be watched. Keeping them
            // awake costs nothing and removes a whole class of "it only happens
            // sometimes".
            rb.sleepThreshold = 0f;

            // CENTRE OF MASS ON THE FLOOR OF THE BOX.
            //
            // Unity puts it at the middle of the compound collider, which makes
            // a 41 x 9 cm box behave like a block that is happy to topple. It is
            // not: it is a flat tray with a pizza lying in the bottom of it, and
            // the mass is all in that bottom. This is most of why the load stops
            // opening itself on a rough road — a slab with a low centre slides
            // rather than tips, and only tipping opens a box.
            rb.centerOfMass = lc + new Vector3(0f, -hy * 0.34f, 0f);
            slot.box = rb;
            slot.startLocal = tray.InverseTransformPoint(go.transform.position);

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
                pc.sharedMaterial = grip;
                var prb = pz.AddComponent<Rigidbody>();
                prb.mass = 0.45f;
                prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                prb.interpolation = RigidbodyInterpolation.Interpolate;
                prb.linearDamping = 0.5f;
                prb.angularDamping = 1.8f;
                prb.sleepThreshold = 0f;   // see the box above

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
            var deltaV = v - lastVel;
            var accelWorld = deltaV / dt;
            lastVel = v;

            // Was that a crash, or was that the road? Only the responder knows,
            // and it already does the classifying — see JoltMinSpeed. Its window
            // is a quarter of a second, so the answer does not depend on whether
            // OnCollisionEnter happened to run before this FixedUpdate.
            if (!responderChecked)
            {
                responder = car.GetComponent<CollisionResponder>();
                responderChecked = true;
            }
            Vector3 jolt = Vector3.zero;
            if (responder != null && responder.InWallContact && deltaV.magnitude >= JoltMinSpeed)
                jolt = car.transform.InverseTransformDirection(deltaV);

            // Into the car's own axes. The tray holds the same pitch and roll, so
            // pushing the boxes in ITS frame is what makes "the car braked" mean
            // "forward" to a box regardless of which compass direction the car
            // happens to be pointing.
            Tick(car.transform.InverseTransformDirection(accelWorld), tilt, dt, jolt);
        }

        CollisionResponder responder;
        bool responderChecked;

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
        /// <param name="jolt">The velocity the CAR lost to a collision this
        /// step, in the car's own axes, or zero. Kept separate from the
        /// acceleration because it is not one: see JoltMinSpeed.</param>
        public void Tick(Vector3 accelCarLocal, Quaternion tilt, float dt,
                         Vector3 jolt = default)

        {
            if (trayBody == null || dt <= 0f) return;

            // FILTERED HERE, not in the caller.
            //
            // Both filters belong to the simulation, and they were in
            // FixedUpdate until the harness — which calls this directly —
            // reported all three boxes on the floor after a rough corner. It was
            // shaking the tray by up to five degrees PER FRAME, because the tilt
            // smoothing was on the other side of the door: the test was
            // measuring a car that whips its own seat about at fifty hertz.
            // A filter that only some callers get is not part of the model.
            smoothAccel = Vector3.Lerp(smoothAccel, accelCarLocal, dt / (AccelTau + dt));
            if (!haveTilt) { smoothTilt = tilt; haveTilt = true; }
            else smoothTilt = Quaternion.Slerp(smoothTilt, tilt, dt / (TiltTau + dt));

            trayBody.MoveRotation(smoothTilt);
            var push = tray.TransformDirection(-Vector3.ClampMagnitude(smoothAccel, MaxAccel));

            foreach (var s in slots)
            {
                if (s.box != null) s.box.AddForce(push, ForceMode.Acceleration);
                if (s.pizza != null) s.pizza.AddForce(push, ForceMode.Acceleration);
            }

            // The crash, on its own channel and unfiltered. The car lost this
            // much speed; the load did not, so relative to the seat it lurches
            // by the same amount in the opposite direction. VelocityChange
            // rather than a force because that is literally what it is — no
            // mass term, no time constant, no clamp except the one that stops
            // the solver being handed something it cannot integrate.
            if (jolt.sqrMagnitude > 1e-6f)
            {
                var kick = tray.TransformDirection(Vector3.ClampMagnitude(-jolt, MaxJolt));
                // A little TUMBLE with it. A box thrown across a seat does not
                // slide flat like a puck — it catches an edge and goes over, and
                // that is most of what a crash looks like from the Pizza Cam.
                // It is also what gets a box over the seat's front edge instead
                // of leaving it parked against it. Cross with the tray's up so
                // the spin is about a horizontal axis square to the throw, which
                // is the axis a box actually tips about.
                var spin = Vector3.Cross(tray.up, kick) * JoltSpin;
                foreach (var s in slots)
                {
                    if (s.box != null)
                    {
                        // Belt and braces alongside sleepThreshold: a body that
                        // is asleep when an impulse arrives silently eats it,
                        // and that is the failure this whole channel exists to
                        // fix. Cheap enough to do both.
                        s.box.WakeUp();
                        s.box.AddForce(kick, ForceMode.VelocityChange);
                        s.box.AddTorque(spin, ForceMode.VelocityChange);
                    }
                    if (s.pizza != null)
                    {
                        s.pizza.WakeUp();
                        s.pizza.AddForce(kick, ForceMode.VelocityChange);
                    }
                }

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
                  .Append(" at ").Append(BoxOffset(i).ToString("F2"))
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

                // OFF THE SEAT means below the pan — on the floor, out of shot.
                // Merely climbing over a bolster onto the rest of the bench is
                // not that: it is a box sliding about on a seat, which is the
                // thing the player is supposed to watch and worry about rather
                // than be charged for.
                float height = tray.InverseTransformPoint(s.box.position).y;
                if (height < -0.12f) { s.grounded = true; Open(s); }

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

        /// <summary>
        /// One piece of the seat. `visible` false leaves the collider and turns
        /// the renderer off.
        ///
        /// Most of this rig is invisible on purpose. The owner asked for "just
        /// the car seat and pizzas" with transparency around them, and said of
        /// the door specifically that it is "not shown" — so the door card, the
        /// tunnel, the bolsters and the footwell all still confine the cargo and
        /// none of them are drawn. What is left in frame is a seat with pizza on
        /// it and the game behind it, which is the whole brief.
        /// </summary>
        GameObject Slab(Transform parent, string name, Vector3 localCentre, Vector3 size,
                        bool visible = true, float pitchDeg = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCentre;
            go.transform.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
            go.transform.localScale = size;

            var col = go.GetComponent<Collider>();
            if (col != null && grip != null) col.sharedMaterial = grip;
            var mr = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("PSX/Lit");
            if (shader != null)
            {
                var m = new Material(shader) { hideFlags = HideFlags.DontSave };
                // Seat-cloth grey. Deliberately drab: the cargo is the subject
                // of this picture and the seat is the thing it is on.
                m.color = name == "Footwell" ? new Color(0.20f, 0.20f, 0.22f)
                                             : new Color(0.34f, 0.33f, 0.36f);
                mr.sharedMaterial = m;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = visible;
            return go;
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
