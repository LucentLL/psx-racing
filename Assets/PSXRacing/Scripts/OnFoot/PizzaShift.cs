using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// A delivery shift, from the counter to the door.
    ///
    /// FOOD DELIVERY had been a button on the MAIN tab that added money and
    /// burned an activity slot — the same shape as every other job in the game.
    /// This is the owner's ask to make it the one job you actually DO: collect
    /// an order at the shop, carry it out, and drive it somewhere. The driving
    /// half is a normal circuit run with the field retired, so the route, the
    /// fuel burn, the wear and the damage all come from the code that already
    /// handles those; what happens here is only the part before the key turns.
    ///
    /// The order is deliberately a THING the player carries rather than a flag.
    /// A shift that began by teleporting you into a race would not be worth
    /// leaving the menu for; walking to the counter, picking a box up and seeing
    /// it in your hands on the way out is the whole difference.
    ///
    /// THE DOOR IS THE WAY OUT, and that is a fix rather than a preference. The
    /// first version put the drive on a hook attached to the player's car, which
    /// the scene builder parks 8.2 m out on the street — and the shop front is a
    /// sealed shell whose door leaf gets a MeshCollider like every other panel
    /// in the pack, so that car was never once reachable. Meanwhile the door
    /// itself refused to open while carrying ("somebody is waiting on that"), so
    /// a player who collected an order had no remaining action anywhere in the
    /// room. Reported exactly that way: "I try to leave the front (and only)
    /// door and it doesn't let me leave to make the delivery." A door you cannot
    /// walk through has to BE the exit, not guard one.
    /// </summary>
    public class PizzaShift : MonoBehaviour
    {
        [Header("Wired by the scene builder")]
        public FootScreen screen;
        /// <summary>The top box on the counter. Hidden once it is in your
        /// hands.</summary>
        public Transform counterOrder;
        /// <summary>The whole pile the pack stacks there, top first. An order of
        /// three visibly takes three off the counter.</summary>
        public Transform[] counterStack;
        /// <summary>The stack parented to the camera while carried.</summary>
        public Transform carriedOrder;
        /// <summary>Its individual boxes, bottom first. Built at max size and
        /// revealed to the size of the order actually rolled.</summary>
        public Transform[] carriedBoxes;
        public FootTarget counterHook;
        /// <summary>The stand-in car out on the street. Scenery and a second way
        /// to start the run for anyone who can reach it; the door is the one
        /// that always works.</summary>
        public FootTarget carHook;
        public FootTarget doorHook;

        static LifeState S => LifeSimManager.State;

        bool carrying;
        /// <summary>Rolled once, at pick-up, so the player is told what the run
        /// is worth BEFORE they drive it. Rolling on arrival instead would make
        /// the number feel arbitrary — and it is the only reason to take the job
        /// seriously rather than treat it as a lap. It is a CEILING now: the
        /// clock and the state of the box grade it down on arrival.</summary>
        int pay;
        /// <summary>Where this one is going. Rolled at the counter with the pay,
        /// for the same reason: an address you are only told after you set off
        /// is not an address, and the quote has to be able to name the run and
        /// the time it is graded against.</summary>
        int trackIndex = -1;
        float parSeconds;
        /// <summary>One topping index per box, bottom of the stack first. This
        /// IS the order — it crosses into the race scene and becomes the boxes
        /// on the passenger seat.</summary>
        int[] toppings;

        void Start()
        {
            if (carriedOrder != null) carriedOrder.gameObject.SetActive(false);
            if (carriedBoxes != null)
                foreach (var b in carriedBoxes) if (b != null) b.gameObject.SetActive(false);

            if (counterHook != null) counterHook.onUse = UseCounter;
            if (carHook != null) carHook.onUse = Drive;
            if (doorHook != null) doorHook.onUse = UseDoor;

            RefreshLabels();
        }

        /// <summary>
        /// Every prompt in the room, rewritten from one bool.
        ///
        /// The hooks stay ENABLED throughout and refuse in the handler instead.
        /// Disabling a FootTarget drops it out of FootTarget.All, so the object
        /// stops offering any prompt at all — a counter you cannot read is
        /// indistinguishable from a counter that is not there, and the player has
        /// no way to learn why.
        /// </summary>
        void RefreshLabels()
        {
            string venue = trackIndex >= 0 && trackIndex < TrackCatalog.All.Length
                         ? TrackCatalog.All[trackIndex].name : "";

            if (counterHook != null)
            {
                counterHook.title = "PIZZA COUNTER";
                counterHook.detail = carrying
                    ? (venue.Length > 0 ? Boxes + " for " + venue : "those are yours")
                    : "an order is up";
                // Putting it back is the ESCAPE HATCH, and it is the reason this
                // is a two-way control rather than a one-shot pickup. The door
                // starts the run while you are carrying, so every one of Drive's
                // refusals — no car, dry tank — would otherwise leave a player
                // holding a pizza with no action anywhere in the room and no way
                // out of the scene. That is the same shape as the bug this whole
                // pass exists to fix, and it is not worth trading one for
                // another.
                counterHook.action = carrying ? "PUT THE ORDER BACK"
                                              : "COLLECT THE ORDER";
            }
            if (carHook != null)
            {
                carHook.title = "YOUR CAR";
                carHook.detail = carrying
                    ? "$" + pay + " on delivery"
                    : "the order is still on the counter";
                carHook.action = carrying ? "LOAD UP AND DRIVE" : "";
            }
            if (doorHook != null)
            {
                // The door is the only fixture in the room whose job changes
                // completely depending on whether you are holding an order, so
                // it says so. Carrying, it names the drop and the money — the
                // player should be reading the address on their way out of the
                // shop, not discovering it on a loading screen.
                doorHook.title = "FRONT DOOR";
                doorHook.detail = carrying
                    ? venue + "  ·  " + Boxes + ", $" + pay + ", more under " +
                      LifeRules.DeliveryClock(parSeconds)
                    : "nothing waiting on you";
                doorHook.action = carrying ? "OUT TO THE CAR — START THE RUN"
                                           : "CLOCK OFF — GO HOME";
            }
        }

        void UseCounter()
        {
            if (carrying) PutBack();
            else Collect();
        }

        /// <summary>Set it down again. The ticket does NOT re-roll: pay and
        /// destination are the shop's, not the driver's, and letting a player
        /// put a box back and pick it up until they liked the address would turn
        /// the one thing they are told before setting off into a slot
        /// machine.</summary>
        void PutBack()
        {
            if (!carrying) return;
            carrying = false;
            if (counterStack != null)
                foreach (var t in counterStack) if (t != null) t.gameObject.SetActive(true);
            else if (counterOrder != null) counterOrder.gameObject.SetActive(true);
            if (carriedOrder != null) carriedOrder.gameObject.SetActive(false);
            RefreshLabels();
            screen?.Toast("order back on the counter — it will keep");
        }

        void Collect()
        {
            if (carrying || S == null) return;
            carrying = true;
            // Rolled ONCE per shift. Coming back to the counter after putting a
            // box down hands you the same ticket, so the numbers below are the
            // shop's decision rather than something to shop around for.
            if (trackIndex < 0)
            {
                toppings = LifeRules.RollOrderToppings(MaxBoxes);
                pay = LifeRules.RollDeliveryPay(S) * toppings.Length;
                trackIndex = LifeRules.DeliveryTrackIndex(S);
                parSeconds = LifeRules.DeliveryParSeconds(trackIndex);
            }

            // The counter loses exactly what the player picked up.
            if (counterStack != null)
                for (int i = 0; i < counterStack.Length; i++)
                    if (counterStack[i] != null)
                        counterStack[i].gameObject.SetActive(i >= toppings.Length);
            else if (counterOrder != null) counterOrder.gameObject.SetActive(false);

            if (carriedOrder != null) carriedOrder.gameObject.SetActive(true);
            if (carriedBoxes != null)
                for (int i = 0; i < carriedBoxes.Length; i++)
                    if (carriedBoxes[i] != null)
                        carriedBoxes[i].gameObject.SetActive(i < toppings.Length);

            RefreshLabels();
            string venue = trackIndex >= 0 && trackIndex < TrackCatalog.All.Length
                         ? TrackCatalog.All[trackIndex].name : "the drop";
            // What the player is told has to be what the HUD then counts. The
            // quote is what the run pays ON PAR; beating par pays more and
            // taking twice as long pays a fraction, and the tip readout in the
            // corner is the same ScoreDelivery call saying so live. Promising
            // "$41 if it's there in 2:30" and then showing $51 on the grid
            // would read as the game inflating a number to take it back.
            screen?.Toast("ORDER UP — " + Boxes + " to " + venue + ", $" + pay +
                          " on the door. Beat " + LifeRules.DeliveryClock(parSeconds) +
                          " for more; keep them flat. Out the front.");
        }

        /// <summary>The door does whichever of the two things the player is
        /// actually in a position to do. Carrying an order it is the way out to
        /// the car; empty-handed it is the way home.</summary>
        void UseDoor()
        {
            if (carrying) Drive();
            else ClockOff();
        }

        /// <summary>
        /// Out of the door with the boxes — INTO THE TOWN, not into the race.
        ///
        /// The owner's ask, and the shape of the job now: the order rides the
        /// passenger seat from the shop kerb to the junction, and the junction
        /// is where the run proper starts (DepartScreen → MAKE THE DELIVERY →
        /// PizzaRun.LaunchDelivery). The town leg is real: it burns fuel,
        /// takes damage, and whatever happens to the boxes on Main Street is
        /// scored against the drop.
        ///
        /// The activity slot and the attendance are still banked HERE, before
        /// the door shuts: the shift costs the afternoon whether or not the
        /// player makes it anywhere, and clocking on must precede the spend
        /// because spending the last slot of the day rolls it — the rollover
        /// decides whether the player skived by reading the very latch this
        /// sets. Crediting the shift on arrival instead booked an absence for
        /// the night shift and credited the work to the following morning,
        /// which on a seven-day roster is most of the job.
        /// </summary>
        void Drive()
        {
            if (S == null) return;
            if (!carrying) { screen?.Toast("collect the order first"); return; }

            var car = S.ActiveCar;
            if (car == null) { screen?.Toast("no car to deliver in"); return; }
            if (car.fuel <= 5f) { screen?.Toast("not enough fuel — the tank is dry"); return; }
            // Belt and braces: the venue is rolled at the counter, and carrying
            // cannot be true without having been there. A -1 reaching
            // SceneManager.LoadScene is a black screen with no error.
            if (trackIndex < 0 || trackIndex >= TrackCatalog.All.Length)
                trackIndex = LifeRules.DeliveryTrackIndex(S);

            // The hour the run leaves the counter, read BEFORE the slot spend
            // rolls the clock: a night pickup must not arrive in tomorrow's
            // morning light.
            int tod = TimeOfDay.ForSlot(S.slotIndex, S.day);

            LifeRules.ClockOnShift(S);
            LifeRules.SpendActivitySlot(S);
            PizzaRun.StartRun(toppings, pay, trackIndex, parSeconds, tod);
            LifeSimManager.Save();

            // A build without the town (or a career caught mid-update) still
            // has to be able to deliver: fall straight through to the race the
            // way this door always used to.
            int townIdx = TrackCatalog.TownSceneIndex;
            if (townIdx <= 0 || townIdx >= SceneManager.sceneCountInBuildSettings)
            {
                PizzaRun.LaunchDelivery(S);
                return;
            }

            // The town leg is a free-roam session in the PLAYER'S car, so the
            // handoff is filled the way StartTown fills it — without this the
            // applier leaves the scene's built-in RX-7 standing and the player
            // walks out of the shop into somebody else's car.
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = car.specId;
            RaceHandoff.TimeOfDayIndex = tod;
            RaceHandoff.StartFuelPct = car.fuel;
            LifeHomeScreen.FillCarRequestFor(S);
            SceneManager.LoadScene(townIdx);
        }

        /// <summary>Biggest order the shop hands out. Aliases the rule in
        /// LifeRules so the scene builder (which builds the carried stack that
        /// tall) and this (which rolls the order) cannot disagree — a stack
        /// built three tall against an order rolled four long is two boxes the
        /// player is paid for and never sees.</summary>
        public const int MaxBoxes = LifeRules.MaxOrderBoxes;

        int BoxCount => toppings != null ? toppings.Length : 0;
        string Boxes => BoxCount == 1 ? "one box" : BoxCount + " boxes";

        /// <summary>Leave without taking a run. Costs nothing — the player has
        /// not started the shift, and a job you cannot walk out of before it
        /// begins is a trap rather than a choice.</summary>
        void ClockOff()
        {
            LifeSimManager.Save();
            SceneManager.LoadScene(0);
        }
    }
}
