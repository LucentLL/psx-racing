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
        /// <summary>Centre-banner line, drained by RaceHUD beside GasPump's and
        /// TownVenue's. Null when nobody is near an edge.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Set the frame the run launches, so a second trigger volume
        /// cannot fire the same delivery twice on the way through.</summary>
        static bool leaving;

        int lastSeenFrame;

        void Awake()
        {
            Prompt = null;
            leaving = false;
        }

        void OnTriggerEnter(Collider other) => Cross(other);
        void OnTriggerStay(Collider other) => Cross(other);

        void Cross(Collider other)
        {
            if (leaving) return;
            var tank = other.GetComponentInParent<FuelTank>();
            if (tank == null) return;
            var car = tank.GetComponent<CarController>();
            if (car == null) return;
            var input = car.GetComponent<PlayerCarInput>();
            if (input == null || !input.inputEnabled) return;

            lastSeenFrame = Time.frameCount;

            if (!PizzaRun.Carrying)
            {
                // Nothing to take anywhere. The road still ends, and saying so
                // is kinder than an invisible wall with no explanation — the
                // bounds collider is four metres further on either way.
                Prompt = "THE ROAD RUNS OUT — TURN BACK";
                return;
            }

            // Out of town with the order. GoHome banks the town leg first (free
            // roam has no finish line, so leaving IS the finish) and the front
            // end hops straight on to PizzaRun.LaunchDelivery — one loading
            // screen, and the player arrives on the grid.
            leaving = true;
            Prompt = null;
            TownExit.GoHome(car, "deliverrun");
        }

        void Update()
        {
            // Same frame-count watchdog as TownVenue, and for the same reason:
            // OnTriggerExit does not fire reliably when a volume is left on a
            // physics tick the frame loop never sees.
            if (Prompt != null && Time.frameCount - lastSeenFrame > 6) Prompt = null;
        }
    }
}
