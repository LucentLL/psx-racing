using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Feeds keyboard/gamepad/touch input into a CarController using the Input
    /// System. WASD/arrows drive, Space handbrake, Q/E manual shift (enables
    /// manual mode), M back to automatic, R respawn, C camera toggle.
    ///
    /// Analog sources (the steering wheel, gamepad sticks and triggers, the
    /// pedals) are passed through RAW. CarController already rate-limits the
    /// road wheels at 220 deg/s, so smoothing here as well puts two filters in
    /// series — and the resulting lag is what makes a car impossible to place
    /// and easy to overcorrect. Only the binary keyboard axis gets a ramp,
    /// because it has no travel of its own.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class PlayerCarInput : MonoBehaviour
    {
        /// <summary>Steer units per second the wheel unwinds toward centre once
        /// released (RG2's STEER_RELEASE_RATE). Attack and direction changes are
        /// instant; only the return is rate-limited, which is what a real
        /// caster does — and a one-frame snap to zero was an unwind rate an
        /// order of magnitude past anything physical.
        ///
        /// A field rather than a const since the advanced-tuning screen sells it
        /// as SELF-CENTRING: nothing in this model produces a self-aligning
        /// torque, so the input-side unwind rate is the honest home for "caster
        /// feel". A slider captioned in degrees of caster that secretly drove
        /// this would be a fake unit; this one is real.</summary>
        public float steerReleaseRate = DefaultSteerReleaseRate;
        public const float DefaultSteerReleaseRate = 3.0f;

        CarController car;
        float steerAxis;
        public bool inputEnabled = true;

        /// <summary>The live tank, when the scene has one. Optional: a race
        /// scene built before fuel existed simply has no tank and the throttle
        /// is never cut.</summary>
        FuelTank tank;

        void Awake()
        {
            car = GetComponent<CarController>();
            tank = GetComponent<FuelTank>();
        }

        void Start()
        {
            var touch = TouchControls.Instance;
            if (touch != null) touch.BindShift(ShiftBy);
        }

        void ShiftBy(int dir)
        {
            if (!inputEnabled) return;
            car.manualMode = true;
            car.ShiftTo(car.currentGear + dir);
        }

        void Update()
        {
            // The pause menu runs at timeScale 0, but Update still ticks — so
            // without this the press that confirms a menu item is also read as a
            // driving input, and B/Circle closing the menu doubles as a stab of
            // handbrake on the frame the race resumes.
            if (PauseMenu.IsOpen) return;

            var kb = Keyboard.current;
            var pad = Gamepad.current;

            float kbSteer = 0f, throttle = 0f, brake = 0f;
            bool handbrake = false;
            float? analogSteer = null;

            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) kbSteer -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) kbSteer += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle = 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) brake = 1f;
                handbrake = kb.spaceKey.isPressed;

                if (kb.eKey.wasPressedThisFrame) ShiftBy(1);
                if (kb.qKey.wasPressedThisFrame) ShiftBy(-1);
                if (kb.mKey.wasPressedThisFrame) car.manualMode = false;
                // Only while the player HAS the car. inputEnabled is false on
                // the grid, after the flag, and — the case that got reported —
                // while the driver is standing on the pavement: the walk-in
                // scenes read this same key as their second verb, so one press
                // of X beside a parked car was also a respawn order, and the
                // car teleported away from the person walking up to it.
                if (kb.rKey.wasPressedThisFrame && inputEnabled) DriveSession.Respawn(car);
            }

            if (pad != null)
            {
                float padSteer = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(padSteer) > 0.12f)
                {
                    // Mild expo: a stick has the same travel for a motorway
                    // correction and a hairpin, so the centre wants finer
                    // resolution than the edges.
                    analogSteer = Mathf.Sign(padSteer) * Mathf.Pow(Mathf.Abs(padSteer), 1.3f);
                }
                throttle = Mathf.Max(throttle, pad.rightTrigger.ReadValue());
                brake = Mathf.Max(brake, pad.leftTrigger.ReadValue());
                handbrake |= pad.buttonEast.isPressed;
                if (pad.rightShoulder.wasPressedThisFrame) ShiftBy(1);
                if (pad.leftShoulder.wasPressedThisFrame) ShiftBy(-1);
                // X/Square as well as Back/Share. Recovering a beached car is
                // the control a player reaches for in a hurry, and Back is a
                // button most people have never pressed on purpose — it is not
                // somewhere to hide the only way out of a wall.
                //
                // NOT Y/Triangle, which is what this used to be. ChaseCamera
                // cycles the view on that same button, so on a pad the two
                // fired together: every attempt to change camera mid-race also
                // teleported the car back to the racing line and stopped it
                // dead. Two features, one button, and the destructive one wins
                // — reported as changing view respawning the car.
                // Same inputEnabled gate as R above, and it matters MORE here:
                // pad-west is the on-foot interactors' second verb, so without
                // it every X press beside a parked car respawned the car.
                //
                // And X yields entirely while a door handle is on offer — the
                // town reads the SAME press as GET OUT, script order decides
                // which component sees it first, and the losing order was a
                // player stepping out of a car already teleporting back to the
                // racing line.
                if ((pad.selectButton.wasPressedThisFrame ||
                     (pad.buttonWest.wasPressedThisFrame && !OnFoot.ForecourtMode.OfferGetOut))
                    && inputEnabled)
                    DriveSession.Respawn(car);
            }

            // On-screen controls (phones, tablets, touch laptops). The wheel wins
            // over the gamepad only while it is actually being held.
            var touch = TouchControls.Instance;
            if (touch != null && touch.Visible)
            {
                if (touch.SteerAxis.HasValue) analogSteer = touch.SteerAxis.Value;
                throttle = Mathf.Max(throttle, touch.Throttle);
                brake = Mathf.Max(brake, touch.Brake);
                handbrake |= touch.Handbrake;
                // No touch RESET any more — it was a permanent button for a
                // once-a-race action, and it is a pause-menu row. A beached
                // phone player is not stranded: StuckRecovery auto-resets on its
                // own timer, and RESET CAR (UNSTICK) is one tap behind MENU.
            }

            if (!inputEnabled) { kbSteer = 0f; analogSteer = null; throttle = 0f; brake = 0.3f; handbrake = false; }

            // Two things take the throttle off the player, and both of them are
            // the CAR rather than the game: an empty tank, and a nozzle in the
            // filler neck.
            //
            // Fuelling parks the car on the HANDBRAKE rather than on the brake
            // pedal. A held brake pedal is what StuckRecovery reads as a driver
            // trying to dig themselves out, so a player standing at a pump for
            // six seconds would be warned they were stuck and then teleported
            // back to the racing line with the nozzle still in the car.
            if (tank != null && tank.Starved) throttle = 0f;
            if (GasPump.Fuelling) { throttle = 0f; handbrake = true; }

            if (analogSteer.HasValue)
            {
                // Raw. The control has travel; the car has an actuator rate.
                steerAxis = Mathf.Clamp(analogSteer.Value, -1f, 1f);
            }
            else
            {
                steerAxis = SlewRelease(steerAxis, kbSteer, Time.deltaTime);
            }

            car.steerInput = steerAxis;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.handbrakeInput = handbrake;

            // Let the on-screen controls show what the car is actually doing, so
            // they are not dead props for keyboard and gamepad players.
            if (touch != null && touch.Visible)
                touch.ReflectState(steerAxis, throttle, brake, car.currentGear);
        }

        /// <summary>
        /// Instant to add lock, instant to change direction, rate-limited only
        /// while unwinding toward centre.
        /// </summary>
        float SlewRelease(float current, float target, float dt)
        {
            bool addingLock = Mathf.Abs(target) >= Mathf.Abs(current);
            bool flipping = target != 0f && current != 0f &&
                            Mathf.Sign(target) != Mathf.Sign(current);
            if (addingLock || flipping) return target;
            return Mathf.MoveTowards(current, target, steerReleaseRate * dt);
        }
    }
}
