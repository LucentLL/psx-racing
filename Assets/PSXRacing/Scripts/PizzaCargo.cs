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

        /// <summary>Smoothed speed a box has to have been carrying for a stop
        /// to count as a slam — see the term in Assess. Well under the metre a
        /// second a stock bench lets a box reach across 11 cm, and well over
        /// anything solver jitter can sustain for the length of the smoother.
        /// </summary>
        const float SlamMinSpeed = 0.18f;
        /// <summary>The smoother's time constant. Three frames: long enough
        /// that a one-step contact spike never registers, short enough that a
        /// box crossing the seat in a tenth of a second does.</summary>
        const float SlamTau = 0.06f;
        /// <summary>Condition lost per metre-per-second of slam. A box
        /// arriving at a metre a second — a stock bench, a hard corner —
        /// costs twelve percent; the same corner on a race bucket, where the
        /// box cannot reach the threshold, costs nothing. That gap is the
        /// upgrade.</summary>
        const float SlamWear = 0.15f;

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

        /// <summary>
        /// THE SEAT LADDER. One table, read by the physics that builds the seat
        /// and by the shop that sells it, so the number on the parts page is
        /// the number the boxes feel.
        ///
        /// Two things change up the ladder and both are the owner's brief —
        /// "the more racing oriented seats, the better they are suited to keep
        /// pizza boxes safe from side to side movement":
        ///
        ///   THE BOLSTERS COME IN, AND UP. A box is 41 cm across, so its edge is
        ///   at 0.205; the stock seat's bolster stands 11 cm away from it and 3
        ///   cm tall, and a box has to slide that far, gathering speed, before
        ///   anything catches it. A race bucket's stands a centimetre off it
        ///   and as tall as the stack, so there is nowhere to go and no speed
        ///   to gather. That second half matters: the old note that a bolster
        ///   taller than the box's centre of mass is a FULCRUM was measured
        ///   with 11 cm of run-up. A box that arrives at walking pace does not
        ///   lever over anything.
        ///
        ///   THE CLOTH GETS GRIPPIER. Cardboard on stock seat fabric is about
        ///   0.7; on alcantara it is nearer 0.9. Which is the difference between
        ///   a box that starts moving in a hard corner and one that does not.
        ///
        /// HoldsG is what the shop quotes: the lateral g a box on this seat
        /// takes before it starts to move, from the friction and the pan's own
        /// tilt. It is derived, not typed, so it cannot drift from the physics.
        /// </summary>
        public struct SeatSpec
        {
            public string name;
            /// <summary>Distance from the seat's centreline to the bolster's
            /// centre.</summary>
            public float bolsterHalf;
            /// <summary>
            /// How tall the bolster is, IN BOXES — resolved against the real
            /// baked box at build time, never typed in centimetres.
            ///
            /// Because a bolster's top edge must never land in the MIDDLE of a
            /// box. A box pressed sideways against an edge that meets it
            /// halfway up is levered over that edge: the harness put a 10 cm
            /// bolster halfway up the second box of a stack and a 14 cm one
            /// halfway up the third, and both seats — the middle of the ladder
            /// — lost pizzas that had moved a centimetre. Sized in whole boxes
            /// the edge always lands on a seam between boxes, where it holds
            /// the box below and simply does not meet the box above. Half a
            /// box is the stock bench's lip; a big number is "the whole
            /// stack".
            /// </summary>
            public float bolsterBoxes;

            /// <summary>The height that rule produces for a given box pitch,
            /// capped so the tallest seat never stands proud of the door card
            /// beside it.</summary>
            public float BolsterHeight(float boxPitch, float wallH) =>
                Mathf.Min(wallH, bolsterBoxes * boxPitch + 0.008f);
            /// <summary>What the shop prints: the same rule, against a nominal
            /// real pizza box, so the parts page and the seat cannot disagree
            /// by more than the box does.</summary>
            public int NominalHeightMm => Mathf.RoundToInt(BolsterHeight(0.059f, 0.34f) * 1000f);
            /// <summary>Static and kinetic friction of the seat surface against
            /// cardboard.</summary>
            public float muStatic, muKinetic;
            /// <summary>Drawn? A stock bench has no bolster worth looking at; a
            /// bucket IS its bolsters.</summary>
            public bool bolstersVisible;

            /// <summary>Lateral acceleration, in g, at which a box on the pan
            /// starts to slide sideways: mu on a pan tilted PanPitchDeg is mu
            /// times the cosine of that tilt.</summary>
            public float HoldsG => muStatic * Mathf.Cos(PanPitchDeg * Mathf.Deg2Rad);
            /// <summary>
            /// The same, forward — braking — which the pan's tilt helps with
            /// and the bolsters do not.
            ///
            /// NOT mu cos(t) + sin(t). That is the threshold for a box sitting
            /// still on an incline, and a box under braking is not sitting
            /// still: the pseudo-force that pushes it up the slope also presses
            /// it INTO the slope, and the extra normal load buys extra friction.
            /// Solving a cos(t) - g sin(t) = mu (g cos(t) + a sin(t)) for a
            /// gives the form below, and it is the difference between a stock
            /// seat that lets go at 0.89 g and one that holds through a full
            /// 1.07 g — which is the difference between a panic stop costing
            /// the order and not. The harness measured the second number before
            /// this comment caught up with it.
            /// </summary>
            public float HoldsBrakingG
            {
                get
                {
                    float c = Mathf.Cos(PanPitchDeg * Mathf.Deg2Rad);
                    float s = Mathf.Sin(PanPitchDeg * Mathf.Deg2Rad);
                    return (s + muStatic * c) / Mathf.Max(0.05f, c - muStatic * s);
                }
            }
        }

        /// <summary>Stage 0 is the stock seat: the geometry this file has
        /// always built, with honest friction under it now.</summary>
        public static readonly SeatSpec[] Seats =
        {
            new SeatSpec { name = "STOCK",          bolsterHalf = 0.335f, bolsterBoxes = 0.5f, muStatic = 0.70f, muKinetic = 0.60f, bolstersVisible = false },
            new SeatSpec { name = "SPORT SEAT",     bolsterHalf = 0.300f, bolsterBoxes = 1f,   muStatic = 0.76f, muKinetic = 0.66f, bolstersVisible = true  },
            new SeatSpec { name = "BUCKET SEAT",    bolsterHalf = 0.270f, bolsterBoxes = 2f,   muStatic = 0.80f, muKinetic = 0.70f, bolstersVisible = true  },
            new SeatSpec { name = "RACE BUCKET",    bolsterHalf = 0.250f, bolsterBoxes = 3f,   muStatic = 0.86f, muKinetic = 0.76f, bolstersVisible = true  },
            new SeatSpec { name = "FIXED-BACK",     bolsterHalf = 0.235f, bolsterBoxes = 99f,  muStatic = 0.92f, muKinetic = 0.82f, bolstersVisible = true  },
        };

        public static SeatSpec SeatAt(int stage) =>
            Seats[Mathf.Clamp(stage, 0, Seats.Length - 1)];

        /// <summary>
        /// The pan is not flat. A seat cushion rises toward the knees — about
        /// twelve degrees on a road car — and that tilt is what lets braking
        /// and cornering come apart. The game's brakes and its tyres both top
        /// out near a g, so friction alone can never make a hard corner slide
        /// a box while a hard stop keeps it: they are the same number. Tilt
        /// the pan and gravity holds the box back against the squab, adding
        /// sin(12 deg) of a g to what it takes to push it forward and nothing
        /// to what it takes to push it sideways. Forward is the footwell;
        /// sideways is a door card. That is the asymmetry the seat's geometry
        /// was always relying on, made honest.
        /// </summary>
        public const float PanPitchDeg = 12f;

        /// <summary>The pan's attitude in the tray's frame: front up.</summary>
        static Quaternion PanRot => Quaternion.Euler(-PanPitchDeg, 0f, 0f);

        /// <summary>Height of the pan's top surface at tray-local
        /// <paramref name="z"/>. Zero at the centre, rising toward the front
        /// — anything placed on the cushion away from its middle has to be
        /// lifted by this or it spawns inside it.</summary>
        static float PanTop(float z) => z * Mathf.Tan(PanPitchDeg * Mathf.Deg2Rad);

        /// <summary>Which rung of <see cref="Seats"/> this cargo was built on.</summary>
        public int SeatStage { get; private set; }
        CarController car;
        Rigidbody carBody;
        Transform tray;
        Rigidbody trayBody;

        Vector3 lastVel;
        bool haveLastVel;

        /// <summary>
        /// THAT WAS NOT BRAKING.
        ///
        /// The whole drive here is a velocity DIFFERENCE between two physics
        /// ticks, so anything that writes the car's velocity from outside the
        /// solver — a scripted stop, a respawn, a teleport — arrives looking
        /// exactly like the hardest deceleration the model can represent. A car
        /// arrested at 140 km/h reads as roughly 1950 m/s^2, which the smoother
        /// then holds pinned against the 4.5 g ceiling for a fifth of a second:
        /// several times the hardest real braking this model ever sees, and
        /// sustained. The collision channel does NOT save us, because it is
        /// gated on the responder having actually hit something and nothing was
        /// hit. The boxes go across the seat and the tip goes with them, for a
        /// stop the game performed rather than one the player caused.
        ///
        /// Forgetting the last sample makes the next tick re-seed instead of
        /// differencing, which is exactly what the first frame after the cargo
        /// is built already does.
        /// </summary>
        public void ForgetMotion() => haveLastVel = false;
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
            /// <summary>The box's speed across the seat, smoothed over a few
            /// steps, for the slam test in Assess.</summary>
            public float speedEma;
            /// <summary>Where the pizza's collider sits when the box is as it
            /// was packed, in the BOX's local frame: the interior's centre, a
            /// pizza's half-height above the floor. The point a shut box puts
            /// its pizza back at.</summary>
            public Vector3 homeLocal;
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

        /// <summary>
        /// How far the MOST-MOVED box has gone. The question "did the load
        /// move" is answered by whichever box moved, and in a stack that is
        /// never the bottom one: it carries every box above it on its lid and
        /// gets back only their sliding friction, so it stays put at
        /// accelerations that walk the top box clean across the seat. The
        /// harness asserted on the bottom box, read 0.00, and called the
        /// physics stuck while two boxes were sliding into the door card
        /// above it.
        /// </summary>
        public float BoxSlideMax()
        {
            float m = 0f;
            for (int i = 0; i < slots.Count; i++) m = Mathf.Max(m, BoxSlide(i));
            return m;
        }

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
        /// <param name="bottles">Two litre bottles riding with the order.
        /// They are NOT slots and never reach <see cref="Condition"/> — at the
        /// owner's ask, "bottles don't impact tip if they fall or shake around,
        /// but it just adds a little more action". A thing that rolls around
        /// the footwell and costs nothing is a better passenger than one more
        /// way to lose money.</param>
        /// <param name="seatStage">Which rung of <see cref="Seats"/> to build
        /// the seat from. Negative means "the car's own", read off the handoff
        /// — which FillCarRequestFor stamps for every drive, the town run
        /// included, so the seat the boxes ride across town is the one the
        /// player bought. The harness passes each rung explicitly.</param>
        public static PizzaCargo Spawn(CarController player, int[] toppings, int bottles = 0,
                                       int seatStage = -1)
        {
            if (toppings == null || toppings.Length == 0) return null;
            var go = new GameObject("PizzaCargo");
            var cargo = go.AddComponent<PizzaCargo>();
            cargo.car = player;
            cargo.carBody = player != null ? player.Body : null;
            cargo.SeatStage = Mathf.Clamp(seatStage < 0 ? RaceHandoff.UpSeat : seatStage,
                                          0, Seats.Length - 1);
            cargo.BuildIsland(toppings, bottles);
            return cargo;
        }

        void Awake() { if (Instance == null) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void BuildIsland(int[] toppings, int bottles = 0)
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

            var seat = SeatAt(SeatStage);

            // HONEST FRICTION, from the seat table.
            //
            // This was 0.95 static and it was chosen, not measured: the note
            // said "less than a hard corner produces, so on the default every
            // box slid on every bend". True — and it meant no corner short of
            // a crash moved a box at all, which is what "I pull the ebrake for
            // a 180 at 80 mph and the boxes barely move" is. Cardboard on seat
            // cloth is about 0.7. On alcantara, nearer 0.9. The difference
            // between those two numbers is the seat upgrade.
            //
            // MAXIMUM combine, still: the box's own material is this one too,
            // and Average would halve the seat's grip against a bottle's.
            grip = new PhysicsMaterial("PizzaGrip")
            {
                staticFriction = seat.muStatic,
                dynamicFriction = seat.muKinetic,
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
            // THE PAN IS TILTED, front up, by PanPitchDeg — see that constant
            // for why. Pitched about its own centre, so the surface at z = 0,
            // where the stack stands, is exactly where it always was.
            Slab(tray, "Pan", new Vector3(0f, -0.02f, 0f), new Vector3(SeatW, 0.04f, SeatD),
                 pitchDeg: -PanPitchDeg);
            Slab(tray, "Back", new Vector3(0f, 0.17f, -SeatD * 0.5f + 0.02f),
                 new Vector3(SeatW, 0.38f, 0.05f));

            // THE BOLSTERS ARE THE SEAT UPGRADE. Where they stand and how tall
            // they are comes off the seat table: a stock bench's are 11 cm off
            // a box and 3 cm tall, a fixed-back shell's are a centimetre off it
            // and as tall as the stack. Drawn on the bucket tiers — a bucket IS
            // its bolsters, and a player who bought one should see it holding
            // the load — and left invisible on the stock bench, whose 3 cm ridge
            // is not worth looking at.
            //
            // Capped at the door card's height so the tallest seat never stands
            // proud of the wall beside it; a box that clears a bolster should
            // still meet a door.
            float wallH = Mathf.Max(0.34f, 0.02f + boxesOrdered * 0.1f);
            // The box has to be MEASURED before the bolster can be sized,
            // because the bolster is sized in boxes — see SeatSpec.bolsterBoxes.
            // The same measurement the stack below is built from.
            float pitch = 0.075f + 0.004f;
            {
                var probe = Resources.Load<GameObject>(PizzaCargoBakerNames.Box);
                if (probe != null)
                {
                    var pbb = Bounds(probe);
                    if (pbb.size.y > 0.005f) pitch = pbb.size.y + 0.004f;
                }
            }
            float bolsterH = seat.BolsterHeight(pitch, wallH);
            // Seated on the tilted pan at the bolster's own z (it is centred on
            // the seat, so that is the pan's centre and the surface is at 0),
            // and pitched with it, so the ridge lies along the cushion rather
            // than through it.
            // THE FULL DEPTH OF THE CUSHION, not 80% of it. At 80% a bolster
            // ended 25 cm forward of the seat's centre, and a box that had been
            // leaning on it since the last corner slid forward under braking,
            // ran its leading corner off the END of the bolster while its tail
            // was still propped on it, and went over. It showed up as the one
            // seat in the middle of the ladder losing a box in a panic stop
            // that the seats either side of it held — a bucket's bolsters run
            // the whole cushion for the same reason.
            Slab(tray, "BolsterL", new Vector3(-seat.bolsterHalf, bolsterH * 0.5f, 0f),
                 new Vector3(0.04f, bolsterH, SeatD),
                 visible: seat.bolstersVisible, pitchDeg: -PanPitchDeg);
            Slab(tray, "BolsterR", new Vector3(seat.bolsterHalf, bolsterH * 0.5f, 0f),
                 new Vector3(0.04f, bolsterH, SeatD),
                 visible: seat.bolstersVisible, pitchDeg: -PanPitchDeg);
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

            // ON THE TILTED PAN, and pitched with it. The stack is placed along
            // the pan's own normal rather than straight up, and each box is
            // spawned already lying at the pan's angle — a flat box dropped on
            // a tilted cushion settles for the first few frames, and a settling
            // stack is a moving one, which is exactly what the at-rest case
            // exists to say never happens.
            var panUp = PanRot * Vector3.up;
            for (int i = 0; i < toppings.Length; i++)
            {
                // Stacked, with a hair of daylight between them so the solver
                // does not start the race resolving an interpenetration.
                var at = panUp * (0.01f + i * (boxH + 0.004f));
                slots.Add(BuildBox(boxPrefab, toppings[i], at, boxH));
            }

            // A different look per bottle: the two on a seat were the same
            // model, and two identical bottles side by side read as one thing.
            for (int i = 0; i < bottles; i++)
            {
                var bottlePrefab = PizzaCargoBakerNames.LoadBottle(i);
                if (bottlePrefab == null) return;
                BuildBottle(bottlePrefab, i);
            }
        }

        /// <summary>
        /// A two litre bottle, IN FRONT OF THE STACK.
        /// </summary>
        ///
        /// <remarks>
        /// Where it goes was decided twice, and the second answer is the one
        /// that keeps the promise.
        ///
        /// Beside the stack is impossible: the bolsters leave 63 cm of usable
        /// seat and three 41 cm boxes plus a bottle either side is 63 cm
        /// exactly, so anything in that gap starts the run touching both the
        /// stack and the bolster, and a solver handed an interpenetration on
        /// frame one answers by firing it across the car.
        ///
        /// ON TOP of the stack fits, looks right, and QUIETLY CHANGES THE
        /// GAME. Two kilos of cola resting on a 1.2 kg box presses it into the
        /// seat and buys it friction: the self-test caught it immediately — the
        /// bottom box stopped sliding on a rough corner, an assertion that
        /// exists because a load that never moves is the bug. Easier is still
        /// an impact, and the ask was that a bottle be action rather than a
        /// mechanic.
        ///
        /// So: the strip between the front edge of the boxes and the front edge
        /// of the seat, which is 10.5 cm and a bottle is 8.9 cm across. Nothing
        /// touches at rest, so the graded physics is exactly what it was
        /// without them. Under braking the lying one rolls into the seat front
        /// and under power it rolls back into the stack, so contact happens
        /// because of DRIVING — which is the "little more action" that was
        /// asked for, arriving the only honest way.
        ///
        /// The first lies down and the second stands up, because the two read
        /// completely differently through the cam: a lying bottle rolls the
        /// width of the seat and comes back, an upright one wobbles for half a
        /// corner and goes over. One behaviour would be half the value.
        ///
        /// A BOX collider, not a capsule. A capsule's bottom is a hemisphere,
        /// so an upright bottle stands on a curve and falls before the car has
        /// moved; a box has a flat base and stands until something tips it.
        ///
        /// NOT a slot either way — <see cref="Condition"/> averages slots, and
        /// a bottle in that list would be scored.
        /// </remarks>
        void BuildBottle(GameObject prefab, int index)
        {
            bool upright = index != 0;

            var pb = Bounds(prefab);
            float h = Mathf.Max(0.05f, pb.size.y);
            float r = Mathf.Max(0.02f, Mathf.Max(pb.size.x, pb.size.z) * 0.5f);

            // Measured off the seat rather than typed in, so the clearance
            // survives a change to either the seat or the pack's bottle.
            float z = SeatD * 0.5f - r - 0.008f;
            // The lying one runs across the car and is 33 cm long, so it is
            // offset to leave the standing one a place to be.
            float x = upright ? 0.20f : -0.10f;

            // Lifted by the pan's own rise at this z: the bottles sit at the
            // FRONT of the cushion, which on a tilted pan is six centimetres
            // above its middle, and a bottle placed for a flat pan spawned with
            // its base inside the seat.
            float lift = PanTop(z);
            var local = upright ? new Vector3(x, 0.012f + lift, z)
                                : new Vector3(x, r + 0.012f + lift, z);
            var rot = upright ? Quaternion.Euler(0f, index * 47f, 0f)
                              : Quaternion.Euler(0f, 4f, 90f);

            var go = Instantiate(prefab, tray.TransformPoint(local), tray.rotation * rot, transform);
            go.name = "Bottle" + index;
            // Placed by its BOUNDS, not by its pivot. The prefab is seated on
            // its base — the right datum for standing one up and the wrong one
            // for laying it down, because on its side the base is an END and
            // the bottle hangs a third of a metre off whichever way it turned.
            if (!upright)
            {
                var got = Bounds(go);
                go.transform.position += tray.TransformPoint(local) - got.center;
            }

            // Sized in LOCAL units for the same reason the box walls are: the
            // prefab root carries the scale that takes the pack's mesh to a real
            // bottle, and a collider built from world bounds on a scaled object
            // applies that scale twice.
            Vector3 ls = prefab.transform.lossyScale;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(r * 1.7f / Mathf.Max(1e-4f, ls.x),
                                   h / Mathf.Max(1e-4f, ls.y),
                                   r * 1.7f / Mathf.Max(1e-4f, ls.z));
            col.center = new Vector3(0f, col.size.y * 0.5f, 0f);
            col.sharedMaterial = grip;

            var rb = go.AddComponent<Rigidbody>();
            // Two kilos, because it is two litres of water, and the mass is what
            // decides whether it shoves a box or is shoved by one.
            rb.mass = 2.0f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Less damping than a cardboard box: a bottle on cloth keeps going,
            // which is the whole reason it is worth having.
            rb.linearDamping = 0.12f;
            rb.angularDamping = 0.6f;
            // Centre of mass at the middle of the liquid, not at the origin,
            // which is on the base — left there the bottle is a weeble and will
            // not tip at all.
            rb.centerOfMass = new Vector3(0f, h * 0.42f / Mathf.Max(1e-4f, ls.y), 0f);
        }

        Slot BuildBox(GameObject boxPrefab, int topping, Vector3 localPos, float boxH)
        {
            var slot = new Slot();

            // BUILT SQUARE, THEN TURNED. The box lies at the pan's angle from
            // its first frame — see the stack in BuildIsland — but it is
            // instantiated UPRIGHT here and pitched only at the very end,
            // because everything below is measured with Bounds(), and a
            // renderer's bounds are a WORLD-axis-aligned box. A 41 by 5.5 cm
            // tray tilted twelve degrees has an AABB fourteen centimetres
            // tall. Measured tilted, every box's collider was built fourteen
            // centimetres high in its own frame and then stacked six apart —
            // eight centimetres of overlap per box, which the solver answered
            // by lifting the stack into the air at rest — and the pizza sized
            // to that phantom interior hung four centimetres below its own box,
            // into the one beneath. Three runs of the harness were spent
            // tuning around that before it was found.
            var go = Instantiate(boxPrefab, tray.TransformPoint(localPos), Quaternion.identity,
                                 transform);
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
            // THICKER THAN ONE STEP OF SLAM. A box thrown into a bolster at a
            // metre and a half a second stops in one step, and the pizza inside
            // it does not: it has 3 cm of travel that step against a wall that
            // was 2 cm thick and a millimetre away, and PhysX resolved the
            // overlap by pushing it out the FAR side. Every "spilled" pizza in
            // a box that had neither flipped nor left the seat was this — the
            // pizza tunnelling out of a shut box — and once loose its motion
            // against its own box ground the wear to 1.00 in a second. Nine
            // percent of the box is 3.7 cm, more than a step's travel at any
            // speed the seat can throw one, and the pizza below is sized to
            // leave it clearance.
            float side = Mathf.Max(local.x, local.z) * 0.09f;
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
                // SIZED FROM THE BOX IT LIVES IN, not from its own mesh.
                //
                // The collider used to be 0.92 of the pizza mesh, and nobody
                // had measured the mesh against the box. The pack's top is
                // drawn to fill a lid, so it is wider than the tray's inside:
                // the collider was born a centimetre or two INSIDE the box's
                // walls, the solver spent every frame shoving it back out, and
                // that is why the stack jumped ten centimetres into the air at
                // rest, why a pizza "tunnelled" out of a shut box on the first
                // real slam — it was already most of the way through — and why
                // the load read as sticky. Ninety percent of the interior, with
                // the bottom on the floor, cannot overlap anything by
                // construction, whatever the artist drew.
                float innerX = local.x - 2f * side, innerZ = local.z - 2f * side;
                float innerY = hy - 2f * wall;
                // The interior's centre, in box-local, at the top of the floor.
                var home = lc + new Vector3(0f, -hy * 0.5f + wall + 0.002f, 0f);

                var pz = Instantiate(topPrefab, go.transform.TransformPoint(home),
                                     go.transform.rotation, transform);
                pz.name = "Pizza" + slots.Count;
                // Slide the MESH so its bounds sit centred on that point with
                // their bottom on the floor: the collider below is placed off
                // the bounds, so this is what keeps picture and physics
                // together whatever the mesh's own origin is.
                var pb = Bounds(pz);
                Vector3 want = go.transform.TransformPoint(home);
                pz.transform.position += (want - pb.center) +
                                         go.transform.up * (pb.size.y * 0.5f);
                pb = Bounds(pz);
                Vector3 pls = pz.transform.lossyScale;
                float pizzaH = Mathf.Clamp(pb.size.y, 0.012f, innerY * 0.8f);
                // The collider's centre is the INTERIOR'S, a half-height above
                // the floor — not the mesh's. A mesh taller than the inside of
                // its box has a centre above where the collider's can be, and a
                // collider hung off it floats and then settles, which is a
                // stack that moves at rest.
                slot.homeLocal = home + new Vector3(0f, pizzaH * 0.5f, 0f);
                var pc = pz.AddComponent<BoxCollider>();
                pc.center = pz.transform.InverseTransformPoint(
                                go.transform.TransformPoint(slot.homeLocal));
                pc.size = new Vector3(innerX * 0.90f / Mathf.Max(1e-4f, pls.x),
                                      pizzaH / Mathf.Max(1e-4f, pls.y),
                                      innerZ * 0.90f / Mathf.Max(1e-4f, pls.z));
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

            // NOW turn it to the pan's angle — the box about its own base, and
            // the pizza with it, so the two arrive pitched together exactly as
            // they were built square. The base is the origin, and the origin
            // lies on the pan's normal that the stack is laid along, so each
            // box turning about its own base keeps the stack aligned.
            var pitched = tray.rotation * PanRot;
            if (slot.pizza != null)
            {
                // Through the TRANSFORMS, not Rigidbody.position: nothing has
                // simulated yet, PhysX takes its first pose from the transform,
                // and the rigidbody setter would leave the transform — and so
                // the picture — a step behind.
                var pt = slot.pizza.transform;
                Vector3 inBox = go.transform.InverseTransformPoint(pt.position);
                go.transform.rotation = pitched;
                pt.SetPositionAndRotation(go.transform.TransformPoint(inBox), pitched);
            }
            else go.transform.rotation = pitched;

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
            var w = car.transform.InverseTransformDirection(carBody.angularVelocity);
            if (!haveLastVel)
            {
                lastVel = v; lastW = w; haveLastVel = true;
                Tick(Vector3.zero, tilt, dt);
                return;
            }
            var deltaV = v - lastVel;
            var accelWorld = deltaV / dt;
            lastVel = v;

            // THE SEAT IS NOT THE CENTRE OF MASS, and this is the whole of "I
            // pull the ebrake for a 180 at 80 mph and the boxes barely move".
            //
            // linearVelocity is the car's centre of mass. The passenger seat
            // sits half a metre from it, and a point half a metre from the
            // axis of a spinning car is being flung outward at omega-squared-r
            // — the force that throws a passenger against the door in a spin,
            // and the thing that was missing here. At the three or four
            // radians a second a handbrake turn reaches that is most of a g,
            // on top of the scrub the centre of mass feels; the snap into it
            // adds alpha-cross-r on top of that. None of it existed, so a spin
            // arrived on the seat as a gentle sweep of the same 0.8 g the car
            // was losing to its tyres, which friction held with room to spare.
            //
            // a_seat = a_com + alpha x r + omega x (omega x r), in the car's
            // own axes. omega comes off the body; alpha is its per-step
            // difference, which gets the same filter as everything else in
            // Tick. The pseudo-force the boxes feel is the negative, so the
            // centripetal term above becomes the centrifugal push.
            var alpha = (w - lastW) / dt;
            lastW = w;
            var aSeat = Vector3.Cross(alpha, SeatOffset) +
                        Vector3.Cross(w, Vector3.Cross(w, SeatOffset));

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
            Tick(car.transform.InverseTransformDirection(accelWorld) + aSeat, tilt, dt, jolt);
        }

        /// <summary>The car's angular velocity last step, in its own axes.</summary>
        Vector3 lastW;

        /// <summary>
        /// Where the passenger seat is, relative to the car's centre of mass,
        /// in the car's axes: 40 cm to the right and a little ahead. The
        /// magnitude is what matters and the sign barely does — the seat is
        /// walled on both sides at the same height, so a box flung toward the
        /// door and one flung toward the tunnel meet the same wall.
        /// </summary>
        static readonly Vector3 SeatOffset = new Vector3(0.40f, 0f, 0.15f);

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
                if (away > s.escapeRadius || inBox.y < -0.25f)
                {
                    // A SHUT BOX KEEPS ITS PIZZA. That is not a rule of the
                    // game, it is what a box is — and a pizza found outside one
                    // whose ceiling is still in place did not leave, it
                    // TUNNELLED, through a wall the solver could not hold in one
                    // step. The thicker walls above make that rare; this makes
                    // it impossible to score. Put back where it was, still, and
                    // the tick carries on as if the wall had held — which is
                    // the only honest reading of a lid that never opened.
                    if (!s.open && s.ceiling != null)
                    {
                        // To its HOME inside the box, not to the box's origin:
                        // the origin is the base, which is inside the floor
                        // collider, and a pizza put there is a pizza the solver
                        // has to fire out of the floor.
                        s.pizza.position = s.box.transform.TransformPoint(s.homeLocal);
                        s.pizza.rotation = s.box.transform.rotation;
                        s.pizza.linearVelocity = s.box.linearVelocity;
                        s.pizza.angularVelocity = Vector3.zero;
                    }
                    else { s.escaped = true; Open(s); }
                }

                // Jostling. Only the pizza's motion RELATIVE to its box counts —
                // the whole car is moving and none of that matters to the
                // cheese.
                var rel = s.pizza.linearVelocity - s.box.linearVelocity;
                float slide = rel.magnitude;
                if (slide > 0.35f)
                    s.slideWear = Mathf.Clamp01(s.slideWear + (slide - 0.35f) * 0.045f * dt);

                // THE SLAM. A box that gathers speed across a seat and stops
                // against the door card has a pizza inside it that did not
                // stop — it kept going and hit the far wall of its own box,
                // which is how toppings end up on one side. The term above
                // cannot see that: the pizza is against the wall for one step
                // and the relative speed is gone before the next.
                //
                // So it is read off the BOX, as the speed it loses in a single
                // step. Friction slows a box at about mu g, seven metres a
                // second squared, which over one step is fourteen centimetres
                // a second; a wall stops it outright. Anything over twenty-five
                // centimetres a second in one step is a wall.
                //
                // This is the dial the original design left for "if the middle
                // band ever needs texture", and it is what makes the seat
                // ladder worth money: on a stock bench a box has 11 cm of
                // run-up and arrives at a metre a second; between a race
                // bucket's bolsters it has one centimetre and never gets going.
                // The island does not translate, so the box's own velocity IS
                // its velocity across the seat.
                //
                // GATED ON HAVING BEEN MOVING, over several steps, and not on
                // a one-step drop. The first version asked only whether the
                // speed fell by a quarter of a metre a second in one step, and
                // the solver answers yes to that constantly: a box resting
                // against a wall a centimetre away jitters in and out of
                // contact at fifty hertz, and every spike back to zero read as
                // a slam. A parked car lost three percent doing nothing, and
                // the seats with the CLOSEST walls — the ones sold as the
                // safest — lost the most, which inverted the whole ladder. A
                // sixty-millisecond smoother on the speed is what a spike
                // cannot climb and a slide across the seat cannot help but.
                if (!s.grounded)
                {
                    float sp = s.box.linearVelocity.magnitude;
                    if (s.speedEma > SlamMinSpeed && sp < s.speedEma * 0.35f)
                    {
                        s.slideWear = Mathf.Clamp01(s.slideWear + s.speedEma * SlamWear);
                        s.speedEma = sp;    // one slam, counted once
                    }
                    else s.speedEma = Mathf.Lerp(s.speedEma, sp, dt / (SlamTau + dt));
                }
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
        /// <summary>The 2 litre bottle that rides with the order. Baked from
        /// the same props pack and standing on its own base, so the runtime
        /// stands one up by putting its origin on the seat and lays one down by
        /// turning it ninety degrees.</summary>
        public const string Bottle = Dir + "soda_bottle";
        /// <summary>How many looks the baker paints — cola, lemon-lime, orange,
        /// cherry. Variant 0 is <see cref="Bottle"/> itself, so a load that
        /// only knows the old name still gets a bottle.</summary>
        public const int BottleVariants = 4;
        /// <summary>The i-th look, wrapping, so an order's second bottle is
        /// never the same colour as its first.</summary>
        public static string BottleVariant(int i)
        {
            int v = ((i % BottleVariants) + BottleVariants) % BottleVariants;
            return v == 0 ? Bottle : Bottle + "_" + v;
        }
        /// <summary>Load the i-th look, or the base bottle if that variant was
        /// never baked — a bake from before the variants existed has only
        /// the one.</summary>
        public static GameObject LoadBottle(int i) =>
            Resources.Load<GameObject>(BottleVariant(i)) ?? Resources.Load<GameObject>(Bottle);
        /// <summary>How many toppings the baker writes. A saved order names its
        /// pizzas by index into this, so it is append-only.</summary>
        public const int ToppingCount = 10;
    }
}
