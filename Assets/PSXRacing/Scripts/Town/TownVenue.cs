using UnityEngine;
using UnityEngine.InputSystem;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;
using UnityEngine.SceneManagement;

namespace PSXRacing.Town
{
    /// <summary>
    /// Somewhere in town worth stopping at: the junction at the end of your
    /// street, the pizza shop's door, the dealership lot, the salvage yard.
    ///
    /// Straight off <see cref="DriveThru"/>, which is this project's smallest
    /// working copy of the claim pattern the fuel pump established. Every one
    /// of its six rules is here for the reason it is there, and each of them
    /// has already cost this project a bug once:
    ///
    ///   1. IDENTITY IS THE FuelTank, found with GetComponentInParent — plus a
    ///      PlayerCarInput, because nothing else in the scene should be able to
    ///      walk into a dealership.
    ///   2. CLAIM ON ENTER **AND** STAY. Volumes overlap; with Enter alone,
    ///      rolling out of one into its neighbour leaves the player parked at a
    ///      venue with no prompt.
    ///   3. ONE static claimant, so two overlapping venues cannot both serve.
    ///   4. A FRAME-COUNT WATCHDOG closes the visit, never OnTriggerExit — a
    ///      hand-off between overlapping volumes happens on a physics tick and
    ///      not on a frame.
    ///   5. EVERY STATIC IS RESET IN Awake, or last scene's prompt survives the
    ///      load and the HUD advertises a shop that is not there.
    ///   6. The prompt is a public static string the HUD coalesces, beside
    ///      GasPump.Prompt and DriveThru.Prompt.
    ///
    /// What is NOT here: the fuel pumps. Those are real <see cref="GasPump"/>
    /// volumes on the station model, so the forecourt in town fills a car the
    /// same way every forecourt in the game does, and
    /// <see cref="ForecourtMode"/> gets the player out to do it. A second
    /// implementation of buying petrol is the last thing this needed.
    /// </summary>
    public class TownVenue : MonoBehaviour
    {
        public enum Kind
        {
            /// <summary>The junction at the end of the home street: the one
            /// place the player is asked where they are going.</summary>
            Depart = 0,
            /// <summary>Your own driveway. Pull in and the day is over.</summary>
            Home,
            /// <summary>The pizza shop's door. Walking in is a shift.</summary>
            Pizzeria,
            /// <summary>The dealership's lot.</summary>
            Dealer,
            /// <summary>The salvage yard's gate.</summary>
            Junkyard,
        }

        public Kind kind = Kind.Depart;

        /// <summary>Centre-banner line, drained by RaceHUD beside GasPump's
        /// and DriveThru's. Null when nobody is holding a venue.</summary>
        public static string Prompt { get; private set; }
        /// <summary>True while a stopped car holds one — drives the touch
        /// ACTION button the way GasPump.AtPump drives FUEL.</summary>
        public static bool AtVenue { get; private set; }

        const float StopKmh = 4.5f;

        static TownVenue active;

        CarController car;
        PlayerCarInput carInput;
        DepartScreen panel;
        StoreScreen counter;
        int lastSeenFrame;

        LifeState S => LifeSimManager.State;

        void Awake()
        {
            // Statics do not die with the scene. Without this the town's last
            // prompt is still on screen in the race that follows it.
            active = null;
            Prompt = null;
            AtVenue = false;
        }

        public string Title
        {
            get
            {
                switch (kind)
                {
                    case Kind.Depart: return "THE JUNCTION";
                    case Kind.Home: return "HOME";
                    case Kind.Pizzeria: return "TONY'S — SLICE HOUSE";
                    case Kind.Dealer: return "CRESTLINE MOTORS";
                    default: return "THE SALVAGE YARD";
                }
            }
        }

        string Verb
        {
            get
            {
                switch (kind)
                {
                    case Kind.Depart: return PizzaRun.Carrying ? "MAKE THE DELIVERY"
                                                               : "WHERE TO?";
                    case Kind.Home: return "PARK UP AND GO IN";
                    case Kind.Pizzeria: return PizzaRun.Carrying ? "HAND THE ORDER BACK"
                                             : CanClockOn ? "CLOCK ON AT " + Title
                                                          : "ORDER AT " + Title;
                    case Kind.Dealer: return "WALK THE LOT";
                    default: return "GO IN THE YARD";
                }
            }
        }

        void OnTriggerEnter(Collider other) => TryClaim(other);
        void OnTriggerStay(Collider other) => TryClaim(other);

        void TryClaim(Collider other)
        {
            var tank = other.GetComponentInParent<FuelTank>();
            if (tank == null) return;
            var c = tank.GetComponent<CarController>();
            if (c == null) return;
            var input = c.GetComponent<PlayerCarInput>();
            if (input == null) return;

            if (active != null && active != this) return;
            active = this;
            car = c;
            carInput = input;
            lastSeenFrame = Time.frameCount;
        }

        void Release()
        {
            if (active == this) { active = null; Prompt = null; AtVenue = false; }
            car = null;
            carInput = null;
        }

        void Update()
        {
            if (active != this) return;

            bool panelUp = (panel != null && panel.IsOpen) ||
                           (counter != null && counter.IsOpen);
            if (car == null || Time.frameCount - lastSeenFrame > 6)
            {
                if (!panelUp) { Release(); return; }
            }

            if (panelUp) { Prompt = null; AtVenue = false; return; }

            bool stopped = car != null && Mathf.Abs(car.speedKmh) <= StopKmh;
            bool driving = carInput != null && carInput.inputEnabled;
            if (!stopped || !driving || PauseMenu.IsOpen)
            {
                // Driving PAST the junction is how you say "I'm staying in
                // town". A menu that opened every time you left your street
                // would be a toll booth on the only road out of it.
                Prompt = stopped ? null : UseControlName() + " — STOP AT " + Title;
                AtVenue = false;
                return;
            }

            // HOME is the one venue the player STARTS inside — the car is
            // parked on its own drive when the scene opens. Without this the
            // first prompt of a session is an offer to end it, one press after
            // the engine catches.
            if (kind == Kind.Home && !HasBeenOut)
            {
                Prompt = null; AtVenue = false;
                return;
            }

            AtVenue = true;
            Prompt = UseControlName() + " — " + Verb + GetOutHint();
            if (UsePressed()) Act();
        }

        /// <summary>The other thing a stopped car can do here. Appended to the
        /// venue's own line rather than fighting it for the banner — and only
        /// where a second physical button exists, because on touch the one
        /// ACTION button is already spoken for by the venue.</summary>
        static string GetOutHint()
        {
            if (!OnFoot.ForecourtMode.OfferGetOut) return "";
            if (TouchControls.Instance != null && TouchControls.Instance.Visible) return "";
            return Gamepad.current != null ? "   ·   SQUARE / X — GET OUT"
                                           : "   ·   E — GET OUT";
        }

        void Act()
        {
            switch (kind)
            {
                case Kind.Depart: OpenPanel(); return;
                case Kind.Home: TownExit.GoHome(car, "garage"); return;
                case Kind.Dealer: TownExit.GoHome(car, "dealer"); return;
                case Kind.Junkyard: TownExit.GoHome(car, "junkyard"); return;
                default:
                    if (PizzaRun.Carrying) { HandBack(); return; }
                    if (CanClockOn) TownExit.ClockOn(car);
                    else OpenCounter();
                    return;
            }
        }

        /// <summary>
        /// Drive back to the shop with the order still on the seat and give it
        /// up. The shift stays worked — the slot and the attendance were spent
        /// when the box left the counter, and an evening that delivered
        /// nothing is exactly what it cost. The cargo and its little window
        /// come off the car on the spot, which is also the confirmation.
        /// </summary>
        void HandBack()
        {
            PizzaRun.AbandonRun(S, "brought the order back to the shop — no tip, no harm");
            if (PizzaCargo.Instance != null) Destroy(PizzaCargo.Instance.gameObject);
            if (PizzaCam.Instance != null) Destroy(PizzaCam.Instance.gameObject);
            LifeSimManager.Save();
        }

        /// <summary>Is there a shift to take right now? The shop is open
        /// afternoons and nights, seven days a week — see LifeRules.ShopOpen —
        /// and outside that, or with no job, the door is a counter rather than
        /// a time clock.</summary>
        /// <summary>Has the player actually gone anywhere this session? A
        /// short walk down the drive does not count; a trip to the shop does.
        /// </summary>
        static bool HasBeenOut =>
            City.CityMode.Instance != null && City.CityMode.Instance.MetersDriven > 40f;

        bool CanClockOn =>
            S != null && !string.IsNullOrEmpty(S.playerJob) && LifeRules.ShopOpen(S);

        /// <summary>
        /// Buying a pizza rather than delivering one.
        ///
        /// The same StoreScreen the forecourt and the drive-thrus use, with the
        /// same stock the Charlotte pizzeria sells — so the take-home packs are
        /// the EAT tab's grocery rows at the EAT tab's prices, and driving to
        /// the shop for dinner does not fork the food economy.
        /// </summary>
        void OpenCounter()
        {
            if (counter == null)
            {
                counter = gameObject.AddComponent<StoreScreen>();
                counter.title = Title;
                counter.subtitle = "COUNTER SERVICE";
                counter.logPlace = "the pizza shop";
                counter.stock = PizzaCounter;
                counter.onClosed = () =>
                {
                    if (carInput != null) carInput.inputEnabled = true;
                    if (car != null) car.handbrakeInput = false;
                };
            }
            if (carInput != null) carInput.inputEnabled = false;
            if (car != null) car.handbrakeInput = true;
            Prompt = null;
            AtVenue = false;
            counter.Open();
        }

        /// <summary>The shop's own menu. Deliberately the Charlotte pizzeria's
        /// numbers rather than new ones — one shop, one price list, whichever
        /// door you came in by. PUBLIC because the door serves walk-ups now
        /// too: the on-foot counter (TownWorld) sells off this same list.</summary>
        public static readonly StoreScreen.Item[] PizzaCounter =
        {
            new StoreScreen.Item { name = "SLICE", blurb = "Folded, as the law requires.", tier = "junk", price = 5, heal = 6f },
            new StoreScreen.Item { name = "HOT PIE", blurb = "Twelve minutes in a real oven.", tier = "regular", price = 14, heal = 16f },
            new StoreScreen.Item { name = "PIES TO GO — 5 MEALS", blurb = "Dinner sorted for the week.", tier = "regular", price = 25, heal = 0f, packMeals = 5 },
        };

        void OpenPanel()
        {
            if (panel == null)
            {
                panel = gameObject.AddComponent<DepartScreen>();
                panel.onClosed = () =>
                {
                    if (carInput != null) carInput.inputEnabled = true;
                    if (car != null) car.handbrakeInput = false;
                };
            }
            panel.playerCar = car;
            if (carInput != null) carInput.inputEnabled = false;
            if (car != null) car.handbrakeInput = true;
            Prompt = null;
            AtVenue = false;
            panel.Open();
        }

        static bool UsePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;
            var touch = TouchControls.Instance;
            return touch != null && touch.Visible && touch.ActionPressed;
        }

        static string UseControlName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "TAP ACTION";
            return Gamepad.current != null ? "PRESS X / A" : "PRESS F";
        }
    }

    /// <summary>
    /// Leaving town, in the two shapes it takes.
    ///
    /// Both of them BANK THE DRIVE FIRST. A town session is a
    /// <see cref="City.CityMode"/> like Charlotte's, and free roam has no
    /// finish line — so the moment you leave IS the finish, and it is the only
    /// moment the LifeSim hears what the drive cost in metres, fuel and paint.
    /// Skipping the stamp is not an error anywhere; it is simply a drive that
    /// never happened.
    /// </summary>
    public static class TownExit
    {
        /// <summary>Back to the front end, on a named page.</summary>
        public static void GoHome(CarController car, string tab)
        {
            // The two exits that CONTINUE the shift ride slot-free — the
            // shift's slot is paid at the shop door, once. Every other exit is
            // a drive that ends here and costs what a drive costs.
            RaceHandoff.CommuteLeg = tab == "work" || tab == "deliverrun";

            // An order still on the seat on any exit that is not the delivery
            // itself went nowhere: parked up for the night, wandered off to
            // the dealership, whatever — the shop is not coming to find you.
            if (PizzaRun.Carrying && tab != "deliverrun")
                PizzaRun.AbandonRun(LifeSimManager.State,
                    "the order went cold on the passenger seat — no tip");

            City.CityMode.Instance?.StampExitResult();
            LifeHomeScreen.PendingTab = tab;
            LifeSimManager.Save();
            // A browser keeps pointer lock across a scene load, so without this
            // the player arrives at a menu they cannot click and no cursor to
            // see where they are clicking.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(0);
        }

        /// <summary>
        /// Into the pizza shop for a shift.
        ///
        /// Goes VIA THE FRONT END rather than straight to the shop, and that is
        /// not a detour — it is the only place the drive across town gets
        /// banked. PizzaShift.Drive opens with RaceHandoff.ClearAll(), so a
        /// route that loaded the shop directly would wipe the session this exit
        /// just stamped: the fuel burned getting to work, and the miles, would
        /// simply not have happened. The menu applies the drive and then hops
        /// straight on to the shift, so the player sees one loading screen and
        /// arrives at the counter.
        /// </summary>
        public static void ClockOn(CarController car)
        {
            // The commute is DONE: the next DoWork is the shift itself, not
            // another drive to a shop the player is already parked outside.
            PizzaRun.ArrivedAtShop = true;
            GoHome(car, "work");
        }
    }
}
