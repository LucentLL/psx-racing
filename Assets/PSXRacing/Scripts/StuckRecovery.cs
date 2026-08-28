using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Notices when the player's car can no longer drive itself out of trouble,
    /// says so, and eventually puts it back on the racing line.
    ///
    /// The AI has had this since P2 (<see cref="AIDriver"/>'s stuck/pinned
    /// timers) precisely because a car ground into a barrier never recovers on
    /// its own — the wall's friction is deliberately near zero so a shallow
    /// contact lets you keep your line, which also means a square contact leaves
    /// nothing to push against. The player had no equivalent, and the recovery
    /// controls that did exist were all invisible: R on a keyboard, Back on a
    /// pad, RESET CAR three items down a pause menu. A player who beached the
    /// car nose-first into an embankment therefore experienced it as the game
    /// taking the car away from them — reported as "I lost all control of my car
    /// during the middle of the race".
    ///
    /// Three ways to be stuck, with different patience for each:
    ///   pinned   — grinding a barrier. Never resolves; recover quickly.
    ///   beached  — stationary while asking for throttle or brake, in clear air.
    ///              Usually a kerb or the scenery.
    ///   rolled   — on the roof or on a side. Nothing the player does helps.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class StuckRecovery : MonoBehaviour
    {
        public CarController car;
        public CollisionResponder responder;
        public PlayerCarInput input;
        /// <summary>Optional: the live tank, so a car that ran dry is not
        /// mistaken for one wedged into a bank.</summary>
        public FuelTank tank;

        /// <summary>Below this the car is not making progress. 4 km/h — a car
        /// crawling out of a gravel trap is above it, a car pushing a wall is
        /// not.</summary>
        public float movingKmh = 4f;

        public float pinnedSeconds = 2.0f;
        public float beachedSeconds = 4.0f;
        public float rolledSeconds = 1.5f;
        /// <summary>Grace between the warning appearing and the car being moved,
        /// so a player who was about to free themselves still can — and so the
        /// reset never happens without being announced first.</summary>
        public float warningSeconds = 3.0f;

        /// <summary>What the HUD should be showing, or null. Read by
        /// <see cref="RaceHUD"/>; kept as state here rather than pushed, so the
        /// HUD's change-gating still works.</summary>
        public string Prompt { get; private set; }

        float stuckTimer;

        /// <summary>
        /// Below this the car has left the world, and no amount of patience
        /// brings it back.
        ///
        /// Every other state here resolves eventually or is at least standing
        /// on something. Falling does not: the car is well above `movingKmh`
        /// all the way down, so `crawling` is false, so the watchdog never
        /// arms and the car descends for ever. On the circuits there was
        /// nowhere to fall from. Bogue Banks put a 20 m bridge over open water
        /// with a low parapet, which is a place to fall from.
        ///
        /// Derived from the route rather than a constant, because "too low"
        /// means something different on a sea-level island and a mountain
        /// 1200 m up.
        /// </summary>
        float floorY = float.NegativeInfinity;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (responder == null) responder = GetComponent<CollisionResponder>();
            if (input == null) input = GetComponent<PlayerCarInput>();
            if (tank == null) tank = GetComponent<FuelTank>();
        }

        void Start()
        {
            // In Start, not Awake: RaceManager builds its path in Awake, and
            // asking too early gets a null every time and silently disables
            // the guard.
            var rm = RaceManager.Instance;
            if (rm != null && rm.path != null && rm.path.Count > 0)
            {
                float lowest = float.MaxValue;
                foreach (var w in rm.path.waypoints) if (w.y < lowest) lowest = w.y;
                // 60 m under the lowest point of the road. Deeper than any
                // gorge floor, any seabed and any legitimate excursion, so
                // nothing that is still in the world can reach it.
                floorY = lowest - 60f;
            }
        }

        void Update()
        {
            bool live = DriveSession.Live &&
                        (input == null || input.inputEnabled) && !PauseMenu.IsOpen;

            // Out of the world: recover NOW, with no warning banner and no
            // grace period. The grace exists so a player who was about to free
            // themselves still can, and there is no freeing yourself from this
            // — by the time the prompt could be read the car is a kilometre
            // down. Deliberately ahead of the parked-on-purpose excuses too:
            // nobody parks below the seabed.
            if (live && car != null && transform.position.y < floorY)
            {
                DriveSession.Respawn(car);
                stuckTimer = 0f;
                Prompt = null;
                return;
            }

            bool rolled = car != null && Vector3.Dot(transform.up, Vector3.up) < 0.25f;
            bool pinned = responder != null && responder.InWallContact;

            // Two states where a stationary car is not a stuck car, and where
            // respawning it would be the game taking it away rather than
            // handing it back:
            //
            //   at a pump — the player parked there on purpose, and the whole
            //     point of the forecourt is standing still on it.
            //   out of fuel — the racing line is no more drivable than where
            //     the car died, so the watchdog would fire, teleport, find the
            //     car still motionless, and fire again. The way out of this one
            //     is the fuel truck in the pause menu, which the HUD says so.
            //
            // NEITHER excuse covers a car on its ROOF or grinding a wall. Those
            // two readings are not explained by "the driver chose to stop", and
            // standing down for them meant a car that rolled onto the forecourt
            // stayed there for good — with the fuel prompt over the top of the
            // banner that would have told it how to get out.
            bool parkedOnPurpose = !rolled && !pinned;
            if (parkedOnPurpose && (GasPump.AtPump || (tank != null && tank.Empty)))
                live = false;

            // And never while the driver is out of it. A car with nobody in it
            // is not stuck, it is parked — and teleporting it back to the
            // racing line would leave its owner standing on the forecourt
            // watching it go.
            if (OnFoot.ForecourtMode.OnFoot) live = false;

            if (!live || car == null)
            {
                stuckTimer = 0f;
                Prompt = null;
                return;
            }

            bool asking = car.throttleInput > 0.15f || car.brakeInput > 0.15f;
            bool crawling = Mathf.Abs(car.speedKmh) < movingKmh;

            // Rolled counts even at rest and even with no input: a car on its
            // roof is not a player waiting on the grid, and the countdown gate
            // above already excludes the actual grid.
            bool trapped = rolled || (crawling && (pinned || asking));
            if (!trapped)
            {
                stuckTimer = 0f;
                Prompt = null;
                return;
            }

            stuckTimer += Time.deltaTime;
            float limit = rolled ? rolledSeconds : pinned ? pinnedSeconds : beachedSeconds;
            if (stuckTimer < limit) { Prompt = null; return; }

            float untilReset = limit + warningSeconds - stuckTimer;
            if (untilReset <= 0f)
            {
                DriveSession.Respawn(car);
                stuckTimer = 0f;
                Prompt = null;
                return;
            }

            Prompt = "STUCK — " + ResetControlName() + "\nAUTO-RESET IN " +
                     Mathf.CeilToInt(untilReset);
        }

        /// <summary>
        /// Name the control the player actually has. Telling a pad player to
        /// press R, or a phone player to press anything, is the game not knowing
        /// what it is running on — the same mistake the finish banner used to
        /// make.
        /// </summary>
        static string ResetControlName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "TAP RESET (TOP RIGHT)";
            if (Gamepad.current != null) return "PRESS X / SQUARE TO RESET";
            return "PRESS R TO RESET";
        }
    }
}
