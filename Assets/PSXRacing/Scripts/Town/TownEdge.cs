using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.Town
{
    /// <summary>
    /// Where the main street runs out — and, with a pizza on the seat, where
    /// the delivery run starts.
    ///
    /// The owner's ask, verbatim: "When I pick up a pizza for delivery, it
    /// tells me to deliver it inside of the little town map. I should drive to
    /// the end of the road and that transports me to a random race track."
    ///
    /// Both halves were wrong before this. The order was launched from a MENU
    /// at the junction at the bottom of the player's own street — a press, on a
    /// panel, two hundred metres from where they picked it up — and the HUD's
    /// errand arrow pointed back at it, so the whole delivery happened inside
    /// four hundred metres of the same town. Leaving town by driving out of it
    /// is the thing the fiction was already describing.
    ///
    /// NOT a menu, deliberately. Every other departure in this game asks first,
    /// because every other departure is reversible and the player might have
    /// been passing. This one is not: you cannot be carrying somebody's dinner
    /// to the edge of town by accident, and being asked to confirm the errand
    /// you are visibly running is the toll booth the junction was already told
    /// off for being.
    ///
    /// The claim rules are <see cref="TownVenue"/>'s, for the reasons listed
    /// there: identity is the FuelTank plus a PlayerCarInput, the prompt is a
    /// static the HUD coalesces, and every static resets in Awake or the last
    /// scene's line is still on screen in the race that follows it.
    /// </summary>
    public class TownEdge : MonoBehaviour
    {
        /// <summary>What crossing this line means.</summary>
        public enum Mode
        {
            /// <summary>The town's two ends: the road out is the road home, and
            /// with an order on the seat it is the start of the run.</summary>
            HeadHome = 0,
            /// <summary>
            /// The bottom of your own street, where the MAP stops.
            ///
            /// The town's edges can afford to ask, because a player who drives
            /// past the last shop by accident is still in a town they can turn
            /// round in. This one cannot: it is a cul-de-sac with a boundary
            /// wall four car-lengths behind it, so "drive on" is not an answer
            /// the world can give. Crossing it opens the junction panel itself
            /// — no stopping, no press — and TURN BACK is the row that means
            /// "I was only looking".
            /// </summary>
            AskWhereTo,
        }

        public Mode mode = Mode.HeadHome;

        /// <summary>Which way is INTO this zone from the line, set by the
        /// builder. A car arriving through the line is placed just inside it
        /// facing this way; a car leaving crosses it the other way.</summary>
        public Vector3 inward = Vector3.forward;

        /// <summary>The end of the town's street that the road home leaves
        /// from — where a car arriving FROM home is put. The town has two
        /// ends and only one of them is that.</summary>
        public bool homeSide;

        /// <summary>
        /// THE SPEED LIMIT, and the speed a car arrives at.
        ///
        /// "Warp going through the next area's border at speed limit." There
        /// was no speed limit in the game, so this is it: 45 km/h, a town
        /// street. A car placed inside the line rolling at this reads as having
        /// come through it, and it is slow enough that the three-tenths of a
        /// second before the driver has the controls costs nothing.
        /// </summary>
        public const float ArrivalKmh = 45f;

        /// <summary>
        /// Set by the departure menu when it leaves for a DRIVABLE scene, read
        /// and cleared by that scene's TownWorld when it puts the car down. A
        /// static on this class rather than on RaceHandoff, because the home
        /// screen's scene starters call RaceHandoff.ClearAll on the way, and
        /// this has to survive that; and cleared by the home screen whenever it
        /// actually builds a menu, so a flag left over from an aborted trip
        /// cannot teleport the next session.
        /// </summary>
        public static bool ArrivePending;

        /// <summary>
        /// True from being placed inside the line until the car has driven
        /// clear of it. Without this the arriving car, standing in the volume
        /// it was just put in, would be asked where it wanted to go before it
        /// had moved — a menu on the first frame of every arrival.
        /// </summary>
        bool arriving;

        /// <summary>Where a car arriving through this line is put: two metres
        /// inside it, on the road, facing in.</summary>
        public void ArrivalSpot(out Vector3 pos, out Quaternion rot)
        {
            var b = GetComponent<Collider>().bounds;
            Vector3 dir = inward.normalized;
            // The inner face is the one `inward` points out of; two metres
            // past it puts the car's origin inside the volume with its nose
            // clear, which is what the latch above measures against.
            float half = Mathf.Abs(Vector3.Dot(b.extents, dir));
            pos = b.center + dir * (half - 2f);
            rot = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>Called by TownWorld once the car is placed, so the line
        /// stands down until it is left.</summary>
        public void BeginArrival() { arriving = true; asked = false; }

        /// <summary>Centre-banner line, drained by RaceHUD beside GasPump's and
        /// TownVenue's. Null when nobody is near an edge.</summary>
        public static string Prompt { get; private set; }

        /// <summary>True while that line is one a PRESS would answer, so the
        /// touch ACTION button can be drawn for it. Beside GasPump.AtPump and
        /// TownVenue.AtVenue in RaceHUD's one-button chain — without it the
        /// edge printed "TAP ACTION — HEAD HOME" on a phone over a button
        /// that was not there.</summary>
        public static bool AtEdge { get; private set; }

        /// <summary>Set the frame the run launches, so a second trigger volume
        /// cannot fire the same delivery twice on the way through.</summary>
        static bool leaving;

        int lastSeenFrame;

        DepartScreen panel;
        /// <summary>The car the panel was opened for, and the two halves of its
        /// controls. Fields rather than closure captures, so a second crossing
        /// hands the CLOSE handler the car it is actually holding.</summary>
        CarController heldCar;
        PlayerCarInput heldInput;
        /// <summary>Set when the panel opens, and cleared only once the car has
        /// LEFT the volume. Clearing it in onClosed would reopen the panel on
        /// the next physics tick, because TURN BACK leaves the car standing on
        /// the line it was asked at.</summary>
        bool asked;
        /// <summary>What the latch is measured against: WHERE THE CAR IS, not
        /// how long it has been since a trigger callback.
        ///
        /// A frame-count watchdog is right for the PROMPT and wrong for this.
        /// It expires on silence, and there are three ways to be silent while
        /// standing perfectly still on the line: a rigidbody that has come to
        /// rest stops generating OnTriggerStay, the pause menu stops the physics
        /// clock while Update keeps counting frames, and a driver who gets out
        /// to walk fails the inputEnabled test the callback returns on. Every
        /// one of those would have cleared the latch under a stopped car and
        /// reopened the panel it had just dismissed — the toll booth
        /// DepartScreen's own note argues against.</summary>
        Collider box;
        /// <summary>Where the car was standing on the frame it was asked —
        /// which is the face it came in by.</summary>
        Vector3 askedFrom;

        /// <summary>How far outside the volume the car has to get before the
        /// junction is allowed to ask again. Longer than a car, because the
        /// thing being guarded against is a car whose collider is inside the
        /// volume while its origin is not.</summary>
        const float ReArmMarginM = 8f;

        void Awake()
        {
            Prompt = null;
            AtEdge = false;
            leaving = false;
            asked = false;
            box = GetComponent<Collider>();
        }

        void OnTriggerEnter(Collider other) => Cross(other);
        void OnTriggerStay(Collider other) => Cross(other);

        void Cross(Collider other)
        {
            if (leaving || arriving) return;
            var tank = other.GetComponentInParent<FuelTank>();
            if (tank == null) return;
            var car = tank.GetComponent<CarController>();
            if (car == null) return;
            var input = car.GetComponent<PlayerCarInput>();
            if (input == null || !input.inputEnabled) return;

            lastSeenFrame = Time.frameCount;

            // THE END OF YOUR OWN STREET, and it does not wait to be asked.
            //
            // The junction volume behind this line still has to be STOPPED in
            // and PRESSED at, which was right while this was a TURNING off the
            // town's main road: driving past it meant carrying on into town.
            // It is the end of a cul-de-sac now, and a player who arrives at
            // speed meets the boundary wall instead — where StuckRecovery
            // calls them stuck, RaceHUD ranks the watchdog's line above the
            // junction's, and seven seconds later the car is teleported back up
            // its own street. Reported, three times, as "I crash into an
            // invisible wall instead of being given the menu".
            //
            // NOT forked on PizzaRun.Carrying like the town's edges: the panel
            // makes the delivery its own first row when there is an order on
            // the seat, so one screen answers every case.
            if (mode == Mode.AskWhereTo)
            {
                heldCar = car;
                heldInput = input;
                if (asked || (panel != null && panel.IsOpen)) return;
                asked = true;
                askedFrom = car.transform.position;
                Prompt = null;
                AtEdge = false;
                OpenDepart();
                return;
            }

            // THE TOWN'S ENDS CROSS INTO THE MENU TOO. They used to prompt
            // and wait to be pressed, and with an order aboard they launched
            // the run on the spot. The owner's rule now is one rule for every
            // zone line: "a dividing line on the edges of areas, when driven
            // through, takes you to the menu; your car does not keep going;
            // the sound fades out." So this is the junction's behaviour, with
            // the town's own rows — HEAD HOME, GO RACING, and the delivery
            // first when there is one on the seat. The class note's argument
            // that a delivery should not ask still holds: the run is the
            // first row and the one highlighted, so it is one tap, not a
            // question.
            heldCar = car;
            heldInput = input;
            if (asked || (panel != null && panel.IsOpen)) return;
            asked = true;
            askedFrom = car.transform.position;
            Prompt = null;
            AtEdge = false;
            OpenDepart();
        }

        /// <summary>
        /// The junction's own menu, opened by driving over the line rather than
        /// by pressing at it.
        ///
        /// The same screen and the same rows TownVenue.OpenPanel uses: input off
        /// and the handbrake on, which hands the car to PlayerCarInput's
        /// !inputEnabled branch — 30% of pedal, and the lever once it is under
        /// 1 m/s.
        ///
        /// THAT IS NOT A STOP, and the note that used to stand here said so and
        /// then shrugged: "the worst case is a wall met behind a menu". It was
        /// wrong. 30% of pedal needs the better part of a hundred metres from
        /// motorway speed and there are twenty-two before the boundary wall, so
        /// the player got the menu AND the crash — heard it, and watched it
        /// through a backdrop that is 90% opaque rather than 100%. The stop
        /// itself now lives in DepartScreen.Open, so it covers this line and the
        /// volume behind it and anything that opens that screen later.
        /// </summary>
        void OpenDepart()
        {
            if (panel == null)
            {
                panel = gameObject.AddComponent<DepartScreen>();
                // Reads the FIELDS, the way TownVenue.OpenPanel does. A closure
                // over the locals would hand the controls back to whichever car
                // happened to be the first one over this line.
                panel.onClosed = () =>
                {
                    if (heldInput != null) heldInput.inputEnabled = true;
                    if (heldCar != null) heldCar.handbrakeInput = false;
                };
            }
            panel.playerCar = heldCar;
            // Which zone's edge this is decides the rows: the town's ends
            // offer HEAD HOME, the junction offers IN TOWN.
            panel.fromTown = mode == Mode.HeadHome;
            if (heldInput != null) heldInput.inputEnabled = false;
            if (heldCar != null) heldCar.handbrakeInput = true;
            panel.Open();
        }

        /// <summary>The same USE verb every venue in the town answers to —
        /// F, pad-south, or the touch ACTION button. Duplicated from TownVenue
        /// rather than shared because that one is an instance method gated on
        /// the venue that has CLAIMED the car, and an edge claims nothing.
        /// </summary>
        static bool HomePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) return true;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;
            var touch = TouchControls.Instance;
            return touch != null && touch.Visible && touch.ActionPressed;
        }

        static string HomeControlName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "TAP ACTION";
            return UnityEngine.InputSystem.Gamepad.current != null ? "PRESS X / A" : "PRESS F";
        }

        void Update()
        {
            // Same frame-count watchdog as TownVenue, and for the same reason:
            // OnTriggerExit does not fire reliably when a volume is left on a
            // physics tick the frame loop never sees.
            if (Prompt != null && Time.frameCount - lastSeenFrame > 6)
            {
                Prompt = null;
                AtEdge = false;
            }

            // BACK UP THE STREET, so the next crossing is a new question.
            //
            // "Off the line" is asked of the CAR'S POSITION, for the three
            // reasons `box` records. The volume is axis-aligned and unrotated,
            // so its world AABB is the box itself.
            //
            // And it is asked with a SIDE. The panel takes the controls away as
            // it opens, which hands the car to PlayerCarInput's 30% of pedal —
            // enough to stop a street car and not enough to stop it inside
            // twenty-two metres, so TURN BACK usually gives the player back a
            // car that has coasted out of the far side of the line. Clearing
            // the latch there means the drive back up the road crosses it again
            // and asks again, which is the toll booth this whole volume exists
            // to avoid being. Leaving by the face you came in at is turning
            // back; leaving by the other one is not leaving at all — there is
            // nothing past it but the boundary wall.
            //
            // AND IT HAS TO GET WELL CLEAR, not merely outside. The volume
            // triggers on the car's COLLIDER, whose nose is a couple of metres
            // ahead of the transform this test reads, and DepartScreen now
            // stops the car on the frame the panel opens — so it comes to rest
            // with its origin still OUTSIDE the volume it is standing in. The
            // plain Contains test read that as "left" on the very next frame
            // and dropped the latch while the menu was still up; the instant
            // TURN BACK handed the controls back, OnTriggerStay asked again.
            // A menu with no way out of it, which is worse than the wall it
            // replaced. The margin is a car length and change, so the latch can
            // only clear once the player has genuinely driven away.
            // ARRIVING: the line stands down until the car that was put inside
            // it has driven clear, by the same margin the re-arm uses. The
            // player's car is the one with a PlayerCarInput; there is only
            // ever one, and the latch has no car of its own to read yet.
            if (arriving)
            {
                var pc = FindAnyObjectByType<PlayerCarInput>();
                if (pc == null || box == null) { arriving = false; return; }
                var gone = box.bounds;
                gone.Expand(ReArmMarginM * 2f);
                if (!gone.Contains(pc.transform.position)) arriving = false;
                return;
            }

            if (!asked) return;
            if (heldCar == null || box == null) { asked = false; return; }
            Vector3 at = heldCar.transform.position;
            var clear = box.bounds;
            clear.Expand(ReArmMarginM * 2f);   // Expand adds half per side
            if (clear.Contains(at)) return;
            Vector3 c = box.bounds.center;
            Vector3 entry = askedFrom - c;
            // A car that somehow arrived dead on the centre gets the plain test:
            // no side to go back to, so being outside is enough.
            if (entry.sqrMagnitude < 0.01f || Vector3.Dot(at - c, entry) > 0f) asked = false;
        }
    }
}
