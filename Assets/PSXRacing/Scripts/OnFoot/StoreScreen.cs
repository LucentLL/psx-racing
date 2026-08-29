using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Inside the shop. Four things on a shelf, a price on each, and a door
    /// back out to the forecourt.
    ///
    /// It sells FOOD, which is the one thing that makes a petrol station worth
    /// walking into in a game that already has a fuel pump outside. The LifeSim
    /// has a hunger clock with real teeth — miss enough days and health drains
    /// on every rollover — and until now the only answer to it was the EAT tab
    /// at home, which means being at home. A hot dog at eleven at night on the
    /// far side of the circuit is a worse meal and a better story.
    ///
    /// Deliberately NOT a room. Walking through a door into a modelled interior
    /// would need the shop's inside built, lit and collided, and the asset's
    /// interior is part of the 300 m diorama this project throws away. A panel
    /// is the honest version of what the player gets.
    /// </summary>
    public class StoreScreen : MonoBehaviour
    {
        /// <summary>Called when the door shuts behind the player, so whoever
        /// froze the walker can let it go again.</summary>
        public System.Action onClosed;

        public bool IsOpen { get; private set; }

        public class Item
        {
            public string name, blurb, tier;
            public int price;
            public float heal;
            /// <summary>0 = eat it now (health + hunger clock). Above 0 it is a
            /// take-home pack: N meals into foodStock at this tier, exactly the
            /// shape of the EAT tab's grocery table — the drive-thrus sell the
            /// same junk/regular packs the menu always priced, made physical.</summary>
            public int packMeals;
        }

        /// <summary>
        /// Petrol-station food, priced like petrol-station food. Nothing here
        /// is premium — that is the point of the tier: eat at a forecourt often
        /// enough and the daily rollover starts taking health off you for it
        /// (LifeRules.DailyHealth reads lastMealTier), which is the correct
        /// long-run opinion about living on hot dogs.
        /// </summary>
        static readonly Item[] Stock =
        {
            new Item { name = "COFFEE",       blurb = "Hot, brown, technically a drink.", tier = "junk",    price = 3,  heal = 3f },
            new Item { name = "ENERGY DRINK", blurb = "Tastes like a battery. Works like one.", tier = "junk", price = 5, heal = 5f },
            new Item { name = "HOT DOG",      blurb = "Been on those rollers a while.", tier = "junk",    price = 7,  heal = 8f },
            new Item { name = "SANDWICH",     blurb = "Wrapped this morning. Probably.", tier = "regular", price = 12, heal = 14f },
        };

        /// <summary>Custom menu + signage. Left null this is the 6TWELVE the
        /// forecourts have always opened; the drive-thrus set their own.</summary>
        public Item[] stock;
        public string title = "6TWELVE";
        public string subtitle = "OPEN 24 HOURS";
        public string logPlace = "the 6twelve";

        Canvas canvas;
        RectTransform panel;
        Text toastText;
        string toast;
        float toastUntil;

        LifeState S => LifeSimManager.State;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            // The pointer belongs to the shop while the shop is up. The walker
            // takes it back with a click, which is the same gesture that grabs
            // it anywhere else in this game.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Build();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (canvas != null) Destroy(canvas.gameObject);
            canvas = null;
            onClosed?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) ||
                (pad != null && pad.buttonEast.wasPressedThisFrame))
                Close();

            if (toastText != null)
            {
                string want = Time.unscaledTime < toastUntil ? toast : "";
                if (toastText.text != want) toastText.text = want;
            }
        }

        void Build()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "StoreCanvas", 140);
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.03f, 0.03f, 0.05f, 0.88f));
            MenuKit.Scanlines(canvas.transform);

            panel = MenuKit.Stretch(canvas.transform, "Shop",
                Vector2.zero, Vector2.one, 60f, 60f, 40f, -40f, MenuKit.PanelBg);

            MenuKit.Label(panel, title, MenuKit.Title, new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), TextAnchor.MiddleCenter, MenuKit.Accent, 700f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);

            float y = -78f;
            MenuKit.Label(panel, subtitle + "   ·   " + MenuKit.Money(S.money) + " IN YOUR POCKET",
                16, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Dim, 700f);
            y -= 42f;

            foreach (var item in stock ?? Stock)
            {
                var it = item;
                bool afford = S.money >= it.price;
                MenuKit.Button(panel, it.name + "  —  " + MenuKit.Money(it.price),
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 46f),
                    afford ? (UnityEngine.Events.UnityAction)(() => Buy(it)) : null,
                    18, afford ? (Color?)null : MenuKit.BtnBgDisabled);
                y -= 50f;
                MenuKit.Label(panel, it.blurb, 14, new Vector2(0.5f, 1f),
                    new Vector2(0f, y), TextAnchor.MiddleCenter, MenuKit.Dim, 520f);
                y -= 30f;
            }

            y -= 12f;
            toastText = MenuKit.Label(panel, "", 17, new Vector2(0.5f, 1f),
                new Vector2(0f, y), TextAnchor.MiddleCenter, MenuKit.Good, 620f);
            y -= 40f;

            MenuKit.Button(panel, "BACK OUTSIDE", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(340f, 48f), Close, 18);

            // Without this the shop is mouse- and touch-only: UGUI routes pad
            // navigation to the SELECTED object and nothing here ever selected
            // one, so a player who walked into the 6TWELVE on a pad could look
            // at the shelves and not buy anything.
            var items = MenuNav.Collect(panel);
            MenuNav.Column(items);
            if (items.Count > 0)
            {
                MenuNav.Select(items[0]);
                var watch = MenuNav.Watch(gameObject, items[0]);
                MenuNav.Defer(watch, null, items, null);
            }
        }

        void Buy(Item it)
        {
            var s = S;
            if (s.money < it.price) return;
            s.money -= it.price;
            if (it.packMeals > 0)
            {
                // Take-home: stock the fridge, remember the quality. Nothing is
                // eaten yet — the EAT tab (or the fridge at home) does that.
                s.foodStock += it.packMeals;
                s.lastMealTier = it.tier;
                toast = it.name + " — " + s.foodStock + " MEALS AT HOME";
            }
            else
            {
                s.health = Mathf.Min(100f, s.health + it.heal);
                // It counts as having eaten. The hunger clock does not care
                // where the food came from — only the QUALITY does, and that is
                // what the tier carries into tomorrow's rollover.
                s.daysSinceEat = 0;
                s.ateToday = true;
                s.lastMealTier = it.tier;
                toast = it.name + " — HEALTH " + Mathf.RoundToInt(s.health);
            }
            s.calendarLog.Add("Day " + s.day + ": " + it.name.ToLowerInvariant() +
                              " at " + logPlace + " — " + MenuKit.Money(it.price));
            LifeSimManager.Save();

            toastUntil = Time.unscaledTime + 2.4f;

            // Prices and the wallet line are baked into the panel, so redraw it.
            if (canvas != null) Destroy(canvas.gameObject);
            Build();
            toastText.text = toast;
        }
    }
}
