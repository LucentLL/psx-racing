using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// One wrecked car, opened up: everything still bolted to it, everything
    /// somebody else already took, and a price on each thing you could have.
    ///
    /// This is what "the point of a Junkyard is for the customers to inspect
    /// cars and pull parts they want, then pay a used price cheaper than buying
    /// the parts from a dealership" turned into. Before it, a shell in the
    /// compound offered exactly one part, on sight, with no looking involved —
    /// the yard was a vending machine with cars drawn on it.
    ///
    /// Three rules the page is built around:
    ///
    ///   * YOU LOOK BEFORE YOU BUY. Nothing is priced or even named until the
    ///     player has been over the car. That press is free and takes a
    ///     moment, and it is the difference between searching a yard and
    ///     shopping in one.
    ///   * GONE IS SHOWN. A slot somebody else stripped is a row on the page,
    ///     struck out. Hiding it would make a picked-over shell look identical
    ///     to a lucky one, and the whole game of walking the rows is telling
    ///     those two apart.
    ///   * THE GRADE IS THE HEADLINE. CLEAN / SOLID / SERVICEABLE / ROUGH /
    ///     SCRAP, in front of the part, because that is the number that decides
    ///     whether this is a bargain or a hidden fault you are paying for.
    ///
    /// Built on <see cref="StoreScreen"/>'s frame — same overlay canvas, same
    /// Escape handling, same pad wiring, same onClosed contract that hands the
    /// walker back to whoever froze it.
    /// </summary>
    public class WreckScreen : MonoBehaviour
    {
        /// <summary>Called when the player straightens up and walks off, so the
        /// interactor can let the walker go again.</summary>
        public System.Action onClosed;

        /// <summary>Which shell in the compound. Index into TownWorld's yard
        /// spots, and the key everything in Junkyard is seeded off.</summary>
        public int wreck;
        /// <summary>What the donor was, for the headline. Scenery, but a player
        /// pulling a fender off a Charger should be told it is a Charger.</summary>
        public string donorName = "A WRECK";

        public bool IsOpen { get; private set; }

        Canvas canvas;
        RectTransform panel;
        Text toastText;
        string toast;
        float toastUntil;

        static LifeState S => LifeSimManager.State;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            // The pointer belongs to the page while the page is up. The walker
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

        void Rebuild()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            Build();
            if (toastText != null) toastText.text = toast;
        }

        void Build()
        {
            var s = S;
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "WreckCanvas", 140);
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.10f, 0.10f, 0.10f, 0.90f));
            MenuKit.GridBackdrop(canvas.transform);
            MenuKit.Scanlines(canvas.transform);

            panel = MenuKit.Stretch(canvas.transform, "Wreck",
                Vector2.zero, Vector2.one, 40f, 40f, 26f, -26f, MenuKit.PanelBg);

            MenuKit.Label(panel, donorName, MenuKit.Title, new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), TextAnchor.MiddleCenter, MenuKit.Accent, 820f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);

            float y = -62f;
            if (s == null) { Close(); return; }

            bool looked = Junkyard.WreckLookedOver(s, wreck);
            MenuKit.Label(panel,
                (looked ? Junkyard.WreckPartsLeft(s, wreck) + " STILL ON IT"
                        : "NOBODY HAS BEEN OVER THIS ONE") +
                "   ·   " + MenuKit.Money(s.money) + " IN YOUR POCKET" +
                "   ·   SKILL " + Mathf.FloorToInt(s.mechSkill),
                15, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Dim, 820f);
            y -= 34f;

            if (!looked)
            {
                // The whole page before the walk-round: one button and the
                // reason to press it. Nothing about what is on the car, because
                // nobody has looked.
                MenuKit.Label(panel,
                    "Bonnet down, boot shut, wheels on. What is left on a shell is " +
                    "whatever the last person through here did not want.",
                    16, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                    Color.white, 760f, height: 52f);
                y -= 64f;
                MenuKit.Button(panel, "LOOK IT OVER", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(440f, 50f), () =>
                    {
                        Junkyard.LookOverWreck(s, wreck);
                        LifeSimManager.Save();
                        toast = "";
                        Rebuild();
                    }, 20, new Color(0.16f, 0.30f, 0.34f, 1f));
                y -= 60f;
                MenuKit.Label(panel, "Costs nothing but the walk.", MenuKit.Tiny,
                    new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                    MenuKit.Dim, 700f);
                y -= 34f;
            }
            else
            {
                var car = s.ActiveCar;
                var carSpec = car != null ? CarCatalog.Get(car.specId) : null;
                foreach (var row in Junkyard.WreckContents(s, wreck))
                {
                    int slot = row.slot;
                    var offer = Junkyard.GetPull(s, car, carSpec, wreck, slot);

                    if (row.stripped || row.taken)
                    {
                        // A row, not a gap. See the class note: a picked-over
                        // shell has to LOOK picked over.
                        MenuKit.Label(panel,
                            (row.taken ? "TAKEN — " : "GONE — ") + row.part.label,
                            16, new Vector2(0.5f, 1f), new Vector2(0f, y),
                            TextAnchor.MiddleCenter, MenuKit.Dim, 780f);
                        y -= 24f;
                        MenuKit.Label(panel,
                            row.taken ? "in your boot, " + offer.quote.days + " day" +
                                        (offer.quote.days == 1 ? "" : "s") + " to fit"
                                      : "somebody got here first",
                            MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y),
                            TextAnchor.MiddleCenter, MenuKit.Dim, 700f);
                        y -= 30f;
                        continue;
                    }

                    string head = Junkyard.GradeWord(row.part.grade) + "   " + row.part.label;
                    var tint = row.part.grade >= 70 ? MenuKit.Good
                             : row.part.grade >= 40 ? (Color?)null : MenuKit.Bad;

                    MenuKit.Button(panel,
                        head + "   —   " + MenuKit.Money(offer.price),
                        new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(700f, 42f),
                        offer.can ? (UnityEngine.Events.UnityAction)(() => Pull(slot)) : null,
                        17, offer.can ? tint : MenuKit.BtnBgDisabled);
                    y -= 46f;

                    string effect = offer.quote.effect;
                    MenuKit.Label(panel,
                        offer.can || offer.blocked == null
                            ? (string.IsNullOrEmpty(effect) ? row.part.donorHint
                                                            : effect + "   ·   " + row.part.donorHint) +
                              "   ·   " + offer.quote.days + "d to fit" +
                              (offer.rental > 0 ? "   ·   incl. tool rental" : "")
                            : offer.blocked,
                        MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y),
                        TextAnchor.MiddleCenter, offer.can ? MenuKit.Dim : MenuKit.Bad, 780f);
                    y -= 30f;
                }
            }

            y -= 6f;
            toastText = MenuKit.Label(panel, "", 16, new Vector2(0.5f, 1f),
                new Vector2(0f, y), TextAnchor.MiddleCenter, MenuKit.Good, 760f);
            y -= 32f;

            MenuKit.Button(panel, "STRAIGHTEN UP", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(340f, 46f), Close, 18);

            // Without this the page is mouse- and touch-only: UGUI routes pad
            // navigation to the SELECTED object and nothing here would ever set
            // one, so a controller player could read a wreck and pull nothing.
            var rows = MenuNav.Collect(panel);
            MenuNav.Column(rows);
            if (rows.Count > 0)
            {
                MenuNav.Select(rows[0]);
                var watch = MenuNav.Watch(gameObject, rows[0]);
                MenuNav.Defer(watch, null, rows, null);
            }
        }

        void Pull(int slot)
        {
            var s = S;
            if (s == null) return;
            var car = s.ActiveCar;
            var carSpec = car != null ? CarCatalog.Get(car.specId) : null;
            var offer = Junkyard.GetPull(s, car, carSpec, wreck, slot);
            string err = Junkyard.PullFromWreck(s, car, carSpec, wreck, slot);
            toast = err == null
                ? "PULLED — " + offer.part.label + ", " + offer.quote.days + " DAY" +
                  (offer.quote.days == 1 ? "" : "S") + " TO FIT"
                : err.ToUpperInvariant();
            toastUntil = Time.unscaledTime + 2.8f;
            if (err == null) LifeSimManager.Save();
            Rebuild();
        }
    }
}
