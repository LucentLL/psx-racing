using UnityEngine;
using UnityEngine.InputSystem;
using PSXRacing.OnFoot;

namespace PSXRacing
{
    /// <summary>
    /// A restaurant's order bay: pull in, stop, order from the car.
    ///
    /// The pump taught this game how a car interacts with a place — a trigger
    /// volume that claims whatever FuelTank rolls into it, a prompt while the
    /// conditions hold, one static so overlapping venues cannot double-serve.
    /// This is that pattern minus the walking: nobody gets out at a drive-thru;
    /// the whole point of the window is that the car IS the queue. The menu is
    /// a StoreScreen with the venue's own stock, and the take-home packs on it
    /// are literally the EAT tab's grocery rows made physical — same dollars,
    /// same meals, so the economy does not fork.
    ///
    /// Baked onto the restaurant prefab by the prop baker, which is what lets
    /// a streamed city tile conjure a working restaurant with no scene wiring.
    /// </summary>
    public class DriveThru : MonoBehaviour
    {
        public enum Venue { Burger, Pizzeria }
        public Venue venue = Venue.Burger;

        /// <summary>Centre-banner line, drawn by RaceHUD's city path. Null when
        /// nobody is at a window.</summary>
        public static string Prompt { get; private set; }
        /// <summary>True while a stopped car holds a bay — drives the touch
        /// ACTION button the way GasPump.AtPump drives FUEL.</summary>
        public static bool AtBay { get; private set; }

        const float StopKmh = 4.5f;

        static DriveThru active;               // one order at a time, everywhere

        CarController car;
        PlayerCarInput carInput;
        StoreScreen store;
        int lastSeenFrame;

        public string Title => venue == Venue.Burger ? "STACK BURGER" : "SLICE HOUSE";

        void OnTriggerEnter(Collider other) => TryClaim(other);
        void OnTriggerStay(Collider other) => TryClaim(other);

        void TryClaim(Collider other)
        {
            // The tank is the identity the pump looks for, so it is the
            // identity everything car-shaped looks for.
            var tank = other.GetComponentInParent<FuelTank>();
            if (tank == null) return;
            var c = tank.GetComponent<CarController>();
            if (c == null) return;
            var input = c.GetComponent<PlayerCarInput>();
            if (input == null) return;               // AI cars do not order fries

            if (active != null && active != this) return;
            active = this;
            car = c;
            carInput = input;
            lastSeenFrame = Time.frameCount;
        }

        void OnTriggerExit(Collider other)
        {
            var tank = other.GetComponentInParent<FuelTank>();
            if (tank == null || active != this) return;
            if (store != null && store.IsOpen) return;   // mid-order rolls keep the claim
            Release();
        }

        void Release()
        {
            if (active == this) { active = null; Prompt = null; AtBay = false; }
            car = null;
            carInput = null;
        }

        void Update()
        {
            if (active != this) return;

            // A destroyed car (scene exit) or a stale claim lets go on its own.
            if (car == null || Time.frameCount - lastSeenFrame > 6)
            {
                if (store == null || !store.IsOpen) { Release(); return; }
            }

            if (store != null && store.IsOpen)
            {
                Prompt = null;
                AtBay = false;
                return;
            }

            bool stopped = car != null && Mathf.Abs(car.speedKmh) <= StopKmh;
            bool driving = carInput != null && carInput.inputEnabled;
            if (!stopped || !driving || PauseMenu.IsOpen)
            {
                Prompt = null;
                AtBay = false;
                return;
            }

            AtBay = true;
            Prompt = UseControlName() + " — ORDER AT " + Title;
            if (UsePressed()) OpenMenu();
        }

        void OpenMenu()
        {
            if (store == null)
            {
                store = gameObject.AddComponent<StoreScreen>();
                store.title = Title;
                store.subtitle = venue == Venue.Burger
                    ? "DRIVE-THRU OPEN LATE" : "CURBSIDE PICKUP";
                store.logPlace = venue == Venue.Burger ? "the Stack Burger window"
                                                       : "Slice House";
                store.stock = venue == Venue.Burger ? BurgerStock : PizzaStock;
                store.onClosed = () =>
                {
                    if (carInput != null) carInput.inputEnabled = true;
                    if (car != null) car.handbrakeInput = false;
                };
            }
            if (carInput != null) carInput.inputEnabled = false;
            if (car != null) car.handbrakeInput = true;
            Prompt = null;
            AtBay = false;
            store.Open();
        }

        // The single meals heal like the forecourt's hot food; the packs ARE
        // the grocery table rows (junk $8→4, regular $25→5), sold where the
        // food lives instead of on an abstract tab.
        static readonly StoreScreen.Item[] BurgerStock =
        {
            new StoreScreen.Item { name = "BURGER", blurb = "Flat-top since noon. Still going.", tier = "junk", price = 7, heal = 8f },
            new StoreScreen.Item { name = "COMBO MEAL", blurb = "Burger, fries, a drink the size of your head.", tier = "junk", price = 11, heal = 12f },
            new StoreScreen.Item { name = "FAMILY BAG — 4 MEALS", blurb = "Take it home. The fridge won't judge.", tier = "junk", price = 8, heal = 0f, packMeals = 4 },
        };

        static readonly StoreScreen.Item[] PizzaStock =
        {
            new StoreScreen.Item { name = "SLICE", blurb = "Folded, as the law requires.", tier = "junk", price = 5, heal = 6f },
            new StoreScreen.Item { name = "HOT PIE", blurb = "Twelve minutes in a real oven.", tier = "regular", price = 14, heal = 16f },
            new StoreScreen.Item { name = "PIES TO GO — 5 MEALS", blurb = "Dinner sorted for the week.", tier = "regular", price = 25, heal = 0f, packMeals = 5 },
        };

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
}
