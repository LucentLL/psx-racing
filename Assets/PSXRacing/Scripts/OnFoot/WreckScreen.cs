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
        /// <summary>The donor's catalog id, recorded on a part kept as a spare
        /// (v24). Empty when the shell is scenery with no catalog row.</summary>
        public string donorSpecId = "";

        public bool IsOpen { get; private set; }

        Canvas canvas;
        RectTransform panel;
        Text toastText;
        string toast;
        float toastUntil;
        /// <summary>The walk-up prompt we blanked while the page is up.</summary>
        FootScreen hiddenPrompt;
        bool promptWasShown;

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
            // THE GHOST BEHIND THE ROWS (owner's shot, 2026-11-11): the walk-up
            // prompt (FootScreen, its own overlay canvas at sort order 90) kept
            // printing this wreck's title and "5 parts still on it, yours for
            // the pulling" for as long as the page was open - the interactor
            // still had the shell in its sights - and this page's backdrop and
            // panel are 90% each on purpose (the GT2 grid ghosts through), so
            // the prompt ghosted through with it. Blank the prompt while the
            // page is up, give it back on the way out.
            hiddenPrompt = FindAnyObjectByType<FootScreen>();
            if (hiddenPrompt != null)
            {
                promptWasShown = hiddenPrompt.show;
                hiddenPrompt.show = false;
            }
            Build();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (canvas != null) Destroy(canvas.gameObject);
            canvas = null;
            if (hiddenPrompt != null) { hiddenPrompt.show = promptWasShown; hiddenPrompt.Invalidate(); }
            hiddenPrompt = null;
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
            // Keep the player's place: a rebuild after a pull must not throw
            // the cursor or the list back to the top.
            string sel = null;
            var cur = MenuNav.Selected(canvas != null ? canvas.transform : null);
            if (cur != null) sel = cur.gameObject.name;
            float scrollAt = rowsScroll != null ? rowsScroll.verticalNormalizedPosition : 1f;
            if (canvas != null) Destroy(canvas.gameObject);
            Build();
            if (toastText != null) toastText.text = toast;
            if (rowsScroll != null) rowsScroll.verticalNormalizedPosition = scrollAt;
            if (sel != null && panel != null)
                foreach (var b in panel.GetComponentsInChildren<Selectable>())
                    if (b.gameObject.name == sel && b.interactable) { MenuNav.Select(b); break; }
        }

        ScrollRect rowsScroll;

        /// <summary>Width of a part row: the panel less its margins, never
        /// wider than reads as one row.</summary>
        static float RowW => Mathf.Min(840f, MenuKit.HalfWidth * 2f - 80f - 48f);

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
            rowsScroll = null;

            // Head, not Title: a donor name is up to ~36 capitals, and at the
            // title size it ran edge to edge on 16:9 and off both edges at 4:3.
            MenuKit.Label(panel, donorName, MenuKit.Head, new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), TextAnchor.MiddleCenter, MenuKit.Accent, RowW, height: 40f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);

            if (s == null) { Close(); return; }

            bool looked = Junkyard.WreckLookedOver(s, wreck);
            MenuKit.Label(panel,
                (looked ? Junkyard.WreckPartsLeft(s, wreck) + " STILL ON IT"
                        : "NOBODY HAS BEEN OVER THIS ONE") +
                "   ·   " + MenuKit.Money(s.money) + " IN YOUR POCKET" +
                "   ·   SKILL " + Mathf.FloorToInt(s.mechSkill) +
                "   ·   SHELF " + (s.partsShelf != null ? s.partsShelf.Count : 0) + "/" + Spares.Capacity,
                15, new Vector2(0.5f, 1f), new Vector2(0f, -52f), TextAnchor.MiddleCenter,
                MenuKit.Dim, RowW, height: 26f);

            if (!looked)
            {
                // The whole page before the walk-round: one button and the
                // reason to press it. Nothing about what is on the car, because
                // nobody has looked.
                float y = -94f;
                MenuKit.Para(panel,
                    "Bonnet down, boot shut, wheels on. What is left on a shell is " +
                    "whatever the last person through here did not want.",
                    16, new Vector2(0.5f, 1f), new Vector2(0f, y), out float introH,
                    TextAnchor.UpperCenter, Color.white, Mathf.Min(RowW, 700f));
                y -= introH + 14f;
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
                    MenuKit.Dim, 700f, height: 26f);
            }
            else
            {
                var view = MenuKit.Stretch(panel, "Rows", Vector2.zero, Vector2.one, 12f, 12f, 102f, -86f);
                var content = MenuKit.ScrollBody(view);
                rowsScroll = view.GetComponent<ScrollRect>();
                var car = s.ActiveCar;
                var carSpec = car != null ? CarCatalog.Get(car.specId) : null;
                float y = -6f;
                foreach (var row in Junkyard.WreckContents(s, wreck))
                    DrawRow(content, s, car, carSpec, row, ref y);
                // Content height off what was drawn; the viewport height off the
                // canvas column (the panel is 52 units shorter, the rows' box a
                // further 188), never off a rect that has not resolved yet.
                MenuKit.FitScrollContent(content, Mathf.Max(0f, MenuKit.DesignHeight - 52f - 188f));
            }

            // The footer, pinned to the bottom of the panel so the list above
            // it can scroll on a short (handheld) column without pushing the
            // way out off the screen. Built after the rows so the pad starts on
            // the first part, not on the way out.
            toastText = MenuKit.Label(panel, "", 16, new Vector2(0.5f, 0f),
                new Vector2(0f, 70f), TextAnchor.MiddleCenter, MenuKit.Good, RowW, height: 28f);
            MenuKit.Button(panel, "STRAIGHTEN UP", new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), new Vector2(340f, 46f), Close, 18);

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

        /// <summary>
        /// One slot on the shell, as ONE block: what it is and how good on top,
        /// then the two things you can do with it side by side - FIT TO the car
        /// you are in, or KEEP AS A SPARE - and under EACH button, inside the
        /// same block, what that button will do or why it cannot. The reason
        /// used to be a red line floating in the gap between two rows (owner's
        /// shot), so it read as belonging to either; and "already maxed" was a
        /// reason to refuse the whole pull, which is gone - fitting can be
        /// refused, keeping cannot (money and shelf room aside).
        /// </summary>
        void DrawRow(RectTransform parent, LifeState s, OwnedCar car, CarSpec carSpec,
                     Junkyard.WreckSlot row, ref float y)
        {
            float w = RowW, left = -w * 0.5f;
            int slot = row.slot;
            var bg = MenuKit.Rect(parent, "Slot" + slot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(w, 60f), new Color(0.07f, 0.07f, 0.07f, 0.96f));
            bg.GetComponent<Image>().raycastTarget = false;

            if (row.stripped || row.taken)
            {
                // A row, not a gap. See the class note: a picked-over shell has
                // to LOOK picked over.
                MenuKit.Label(parent, (row.taken ? "TAKEN   " : "GONE   ") + row.part.label,
                    17, new Vector2(0.5f, 1f), new Vector2(left + 14f, y - 6f), TextAnchor.MiddleLeft,
                    MenuKit.Dim, w - 28f, height: 24f);
                MenuKit.Label(parent, row.taken ? "you pulled it - on your shelf or on its way onto a car"
                                                : "somebody got here first",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(left + 14f, y - 32f),
                    TextAnchor.MiddleLeft, MenuKit.Dim, w - 28f, height: 22f);
                bg.sizeDelta = new Vector2(w, 60f);
                y -= 68f;
                return;
            }

            var offer = Junkyard.GetPull(s, car, carSpec, wreck, slot);
            var part = row.part;
            var tint = part.grade >= 70 ? MenuKit.Good : part.grade >= 40 ? Color.white : MenuKit.Bad;
            int risk = Junkyard.FaultRisk(part.grade);

            // Line 1: grade + part on the left, where it came off on the right.
            MenuKit.Label(parent, Junkyard.GradeWord(part.grade) + "   " + part.label, 18,
                new Vector2(0.5f, 1f), new Vector2(left + 14f, y - 6f), TextAnchor.MiddleLeft,
                tint, w * 0.60f, height: 26f, bold: true);
            MenuKit.Label(parent, part.donorHint + (risk > 0 ? "  ·  " + risk + "% RISK" : ""), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(left + w - 14f, y - 6f), TextAnchor.MiddleRight,
                risk >= 20 ? MenuKit.Bad : MenuKit.Dim, w * 0.38f, height: 26f);

            // Line 2: the two actions.
            float bw = (w - 28f - 12f) * 0.5f;
            float bx0 = left + 14f + bw * 0.5f, bx1 = bx0 + bw + 12f;
            float by = y - 38f;
            string carName = car != null ? LifeRules.ShortName(car) : "YOUR CAR";
            var fit = MenuKit.Button(parent,
                offer.can ? "FIT TO " + carName + "  -  " + MenuKit.Money(offer.price)
                          : "FIT TO " + carName,
                new Vector2(0.5f, 1f), new Vector2(bx0, by), new Vector2(bw, 40f),
                offer.can ? (UnityEngine.Events.UnityAction)(() => Pull(slot)) : null,
                16, offer.can ? (Color?)new Color(0.20f, 0.30f, 0.24f, 1f) : MenuKit.BtnBgDisabled);
            fit.gameObject.name = "Btn_fit_" + slot;
            var keep = MenuKit.Button(parent,
                "KEEP AS A SPARE  -  " + MenuKit.Money(offer.keepPrice),
                new Vector2(0.5f, 1f), new Vector2(bx1, by), new Vector2(bw, 40f),
                offer.canKeep ? (UnityEngine.Events.UnityAction)(() => Keep(slot)) : null,
                16, offer.canKeep ? (Color?)null : MenuKit.BtnBgDisabled);
            keep.gameObject.name = "Btn_keep_" + slot;

            // Line 3: under each button, what it will do - or why not - in the
            // same block, so a reason can only be read as that button's.
            float ly = by - 46f;
            string fitLine;
            if (offer.can)
                fitLine = (offer.quote.isUpgrade ? "STAGE " + offer.quote.stage + " ON YOUR " + carName
                                                 : offer.quote.effect + " ON YOUR " + carName) +
                          "  ·  " + offer.quote.days + "d to fit";
            else
                fitLine = "can't fit: " + (offer.blocked ?? "no car");
            MenuKit.Para(parent, fitLine, MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(bx0 - bw * 0.5f, ly), out float fitH, TextAnchor.UpperLeft,
                offer.can ? MenuKit.Dim : MenuKit.Bad, bw);
            string keepLine = offer.canKeep
                ? "to your shelf  ·  " + (part.IsUpgrade
                      ? "any car, up to stage " + part.maxStage + " " +
                        Upgrades.KindLabels[(int)Upgrades.KindFromKey(part.upgradeKind)].ToLowerInvariant()
                      : "any car, +" + Junkyard.EffectiveAdd(part.add, part.grade) + " " + Junkyard.StatWord(part.stat))
                : "can't keep: " + offer.keepBlocked;
            MenuKit.Para(parent, keepLine, MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(bx1 - bw * 0.5f, ly), out float keepH, TextAnchor.UpperLeft,
                offer.canKeep ? MenuKit.Dim : MenuKit.Bad, bw);

            float h = (y - ly) + Mathf.Max(fitH, keepH) + 8f;
            bg.sizeDelta = new Vector2(w, h);
            y -= h + 8f;
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
                ? "PULLED AND FITTING - " + offer.part.label + ", " + offer.quote.days + " DAY" +
                  (offer.quote.days == 1 ? "" : "S")
                : err.ToUpperInvariant();
            toastUntil = Time.unscaledTime + 2.8f;
            if (err == null) LifeSimManager.Save();
            Rebuild();
        }

        void Keep(int slot)
        {
            var s = S;
            if (s == null) return;
            var row = Junkyard.WreckContents(s, wreck);
            string label = slot >= 0 && slot < row.Count ? row[slot].part.label : "IT";
            string donor = donorName != null && donorName.StartsWith("WRECKED ")
                ? donorName.Substring("WRECKED ".Length) : donorName;
            string err = Junkyard.PullToShelf(s, wreck, slot, donorSpecId, donor);
            toast = err == null ? "ON YOUR SHELF - " + label : err.ToUpperInvariant();
            toastUntil = Time.unscaledTime + 2.8f;
            if (err == null) LifeSimManager.Save();
            Rebuild();
        }
    }
}
