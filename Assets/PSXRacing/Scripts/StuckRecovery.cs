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

        /// <summary>Set while the car has been pointing back down the road long
        /// enough to mean it. Read by <see cref="RaceHUD"/>, which ranks it
        /// under <see cref="Prompt"/> — being stuck is the more urgent news.</summary>
        public bool WrongWay { get; private set; }

        /// <summary>Alignment against the path tangent below which the car is
        /// going the wrong way, and the speed and patience it takes to say so.
        /// The dot and the speed are AIDriver's numbers; the four seconds are
        /// not, deliberately — a human is allowed to be halfway through a spin.
        /// The TEST is stricter than the AI's: see UpdateWrongWay.</summary>
        const float WrongWayDot = -0.3f;
        const float WrongWayMinSpeed = 3f;      // m/s, as AIDriver measures it
        const float WrongWaySeconds = 4f;
        float wrongWayTimer;
        /// <summary>Last waypoint index, so the search is a window and not a walk.</summary>
        int pathHint = -1;

        void UpdateWrongWay(float dt)
        {
            var mgr = RaceManager.Instance;
            var path = mgr != null ? mgr.path : null;
            if (path == null || path.Count < 2 || car == null || car.Body == null)
            { wrongWayTimer = 0f; WrongWay = false; pathHint = -1; return; }

            Vector3 v = car.Body.linearVelocity; v.y = 0f;
            if (v.magnitude < WrongWayMinSpeed)
            { wrongWayTimer = 0f; WrongWay = false; return; }

            // BOTH have to be wrong, and that is not pedantry — it is the
            // difference between the two cars this test has to tell apart.
            // TRAVEL alone condemns a car REVERSING back onto the line, which
            // is the recovery the banner should be praising. The NOSE alone
            // (which is all AIDriver checks) condemns a car sliding backwards
            // through a corner it is still carrying. Going the wrong way means
            // pointed down the road AND moving that way.
            //
            // Hinted, because NearestIndex without one walks every waypoint and
            // Beech Gap has 2819 of them — 2819 distance tests per rendered
            // frame, on a phone, for a banner. Every other per-frame caller in
            // the project passes a hint; this was the exception.
            pathHint = path.NearestIndex(transform.position, pathHint);
            Vector3 tangent = path.GetTangent(pathHint);
            bool travelWrong = Vector3.Dot(v.normalized, tangent) < WrongWayDot;
            bool noseWrong = Vector3.Dot(transform.forward, tangent) < WrongWayDot;
            if (travelWrong && noseWrong) wrongWayTimer += dt;
            else wrongWayTimer = 0f;
            WrongWay = wrongWayTimer >= WrongWaySeconds;
        }

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
                // This return skips the wrong-way block below, so the flag has
                // to be cleared here too or a recovered car keeps a banner it
                // earned somewhere it no longer is; and the index hint is now
                // a kilometre out.
                wrongWayTimer = 0f; WrongWay = false; pathHint = -1;
                return;
            }

            // WRONG WAY. Warned about, never acted on.
            //
            // The AI have carried this test since P2 (AIDriver.UpdateRecovery:
            // heading against the path tangent, under -0.3 for two seconds
            // above 3 m/s) and turn themselves round when it trips. The player
            // had no equivalent, which is how a grid facing the wrong way ends
            // up as "I started facing the other cars" — the whole field quietly
            // corrected itself and the one car nobody could correct was the
            // one being driven. It is also the cheapest possible answer to
            // being spun round mid-race with no landmark to tell you.
            //
            // A BANNER AND NOTHING ELSE. Turning the player's car round for
            // them is exactly the complaint this component was written to
            // answer; telling them is not. Four seconds rather than the AI's
            // two, because a human is allowed to be halfway through a spin.
            if (live) UpdateWrongWay(Time.deltaTime);
            else { wrongWayTimer = 0f; WrongWay = false; }

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
            //   at a venue — the same excuse as the pump, and it was missing.
            //     A car stopped at the junction at the end of your own street
            //     is a car being asked where it is going, and the watchdog took
            //     the banner off it: RaceHUD ranks this Prompt ABOVE
            //     TownVenue's, so "PRESS F — WHERE TO?" was replaced by
            //     "STUCK — AUTO-RESET IN 3", and then the car was teleported
            //     off the menu it was standing on.
            //     AND AT AN EDGE, which the same pass missed. The town's two
            //     ends print "PRESS F — HEAD HOME" and then wait to be
            //     pressed, so they are a car stopped on purpose in front of a
            //     question by exactly the same argument, and the watchdog was
            //     still free to take the banner off that one and teleport the
            //     car away from the road out of town.
            bool parkedOnPurpose = !rolled && !pinned;
            if (parkedOnPurpose && (GasPump.AtPump || Town.TownVenue.AtVenue ||
                                    Town.TownEdge.AtEdge ||
                                    (tank != null && tank.Empty)))
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
                return "OPEN MENU — RESET CAR";
            if (Gamepad.current != null) return "PRESS X / SQUARE TO RESET";
            return "PRESS R TO RESET";
        }
    }
}
