using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The apartment. RG2's home surface is eight tabs (main / bills / garage /
    /// newspaper / eat / calendar / mail / sleep — src/ui/screens/home/overlay.ts);
    /// v1 ships the load-bearing subset: MAIN / GARAGE / EAT / BILLS / JOBS.
    /// Sleep lives on MAIN because ending the day is the core verb.
    ///
    /// The whole UI is runtime-generated through MenuKit; the scene holds only
    /// a camera and this component. First run (no player name) shows the
    /// new-game wizard instead.
    /// </summary>
    public class LifeHomeScreen : MonoBehaviour
    {
        Canvas canvas;
        RectTransform body;
        Text moneyText, dateText, healthText;
        string tab = "main";
        CarListing buyTarget;

        /// <summary>
        /// Which tab to open on, set by another SCENE before it loads this one.
        ///
        /// The walk-in garage is a real place with a workbench, a parts rack and
        /// a tool board in it, and every one of those is a screen that already
        /// exists here. Rebuilding those screens in 3D would be two versions of
        /// the same tuning ladder to keep in agreement; walking up to the rack
        /// and coming back to this menu ON the parts tab is the same journey
        /// with one implementation.
        ///
        /// A static because it has to survive a scene load, and cleared the
        /// moment it is read so a later visit to Home opens where the player
        /// left it rather than where the garage last sent them.
        /// </summary>
        public static string PendingTab;

        /// <summary>The tab strip, in screen order, kept so the navigation graph
        /// can be rebuilt against it and so the shoulder buttons can page
        /// through it.</summary>
        readonly System.Collections.Generic.List<Button> tabButtons =
            new System.Collections.Generic.List<Button>();
        string[] tabIds = new string[0];
        /// <summary>Second-press arming for the erase-save button.</summary>
        bool confirmNewGame;

        CarViewer viewer;
        /// <summary>The turntable, built on first use. Two of the five tabs want
        /// one and the wizard wants none, so a render texture allocated in Start
        /// would be one nobody looks at on the screen that matters most — the
        /// first one a new player sees.</summary>
        CarViewer Viewer => viewer != null ? viewer : viewer = gameObject.AddComponent<CarViewer>();

        // wizard state
        bool wizard;
        int wizAge = 25;
        int wizJob;
        InputField wizNameField;

        LifeState S => LifeSimManager.State;

        void Start()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "HomeCanvas", 10);
            MenuKit.Panel(canvas.transform, "Backdrop", MenuKit.Bg);
            // Scanlines go down first so every panel built after sits on top of
            // them — the effect belongs to the screen, not to the content.
            MenuKit.Scanlines(canvas.transform);

            wizard = !LifeSimManager.HasSave || string.IsNullOrEmpty(S.playerName);
            if (wizard) { BuildWizard(); return; }

            // The wizard commits the character before the car is chosen, so a
            // player who quits between the two steps has a save with no car.
            // Resume them at the step they left rather than at a broken home.
            if (S.cars.Count == 0) { BuildCarPick(); return; }

            // Returning from a race? Bank the result, then the race burns a slot.
            string raceSummary = null;
            if (RaceHandoff.ResultReady)
            {
                raceSummary = LifeRules.ApplyRaceResult(S);   // burns the slot itself
                RaceHandoff.ClearAll();
                LifeSimManager.Save();
            }

            // Coming back in from somewhere that asked for a particular screen.
            // Read once and cleared, so it is a hand-off and not a preference.
            if (!string.IsNullOrEmpty(PendingTab)) { tab = PendingTab; PendingTab = null; }
            if (!string.IsNullOrEmpty(PendingInspectCar))
            {
                inspectCarId = PendingInspectCar;
                PendingInspectCar = null;
            }

            BuildChrome();
            Rebuild();
            if (raceSummary != null)
            {
                // A fault surfaced by the race is the headline, not a footnote —
                // it is the thing the player has to act on before racing again.
                if (!string.IsNullOrEmpty(LifeRules.lastDiagnosed))
                {
                    raceSummary += "  ·  DIAGNOSED: " + LifeRules.lastDiagnosed;
                    LifeRules.lastDiagnosed = null;
                }
                Toast("RACE RESULT: " + raceSummary);
            }
        }

        // =================== new-game wizard ===================
        void BuildWizard()
        {
            // The panel fills the canvas with a margin instead of taking a fixed
            // 760x640. A fixed panel cannot know how many jobs the list holds,
            // and the job rows grew past the confirm button — the same overlap
            // class as the body-over-tabs bug, in a different screen.
            // Everything below is anchored, never measured. Reading rect.width or
            // rect.height on a rect created this frame returns a value the layout
            // system has not resolved yet — that stale read is what produced both
            // the tab bar bunched to the left and a job list that overlapped its
            // own rows. Fractional anchors need no measurement to be correct.
            var root = MenuKit.Stretch(canvas.transform, "Wizard",
                Vector2.zero, Vector2.one, 40f, 40f, 24f, -24f, MenuKit.PanelBg);

            MenuKit.Label(root, "NEW GAME", MenuKit.Title, new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), TextAnchor.MiddleCenter, MenuKit.Accent, 700f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);

            // --- identity row: name on the left half, age stepper beside it ---
            var idRow = MenuKit.Stretch(root, "IdRow",
                new Vector2(0f, 1f), new Vector2(1f, 1f), 40f, 40f, -168f, -84f);

            // The debug name is advertised here rather than hidden: it is a
            // testing shortcut, and one nobody can find is one nobody uses.
            // Width 380 is the budget on the NARROWEST canvas — a 4:3 desktop
            // puts the age stepper at about x=416 — so it must not grow.
            MenuKit.Label(idRow, "NAME   (TEST = DEBUG CAREER)", MenuKit.Small,
                new Vector2(0f, 1f), new Vector2(0f, 0f), TextAnchor.MiddleLeft,
                MenuKit.Dim, 380f);
            var fieldRect = MenuKit.Stretch(idRow, "NameField",
                new Vector2(0f, 0f), new Vector2(0.45f, 0f), 0f, 20f, 0f, 60f,
                new Color(0f, 0f, 0f, 0.55f));
            wizNameField = fieldRect.gameObject.AddComponent<InputField>();
            var nameText = MenuKit.Label(fieldRect, "", MenuKit.Body, new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), TextAnchor.MiddleLeft, Color.white, 380f);
            nameText.raycastTarget = false;
            var placeholder = MenuKit.Label(fieldRect, "DRIVER", MenuKit.Body, new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), TextAnchor.MiddleLeft, MenuKit.Dim, 380f);
            placeholder.raycastTarget = false;
            wizNameField.textComponent = nameText;
            wizNameField.placeholder = placeholder;
            wizNameField.characterLimit = 10;   // nameEntry.ts limit
            wizNameField.targetGraphic = fieldRect.GetComponent<Image>();

            // Age 21-60 (nameEntry.ts), default 25. The stepper sits a clear row
            // below its own label; overlapping the two clipped the word "AGE".
            var ageRow = MenuKit.Stretch(idRow, "AgeRow",
                new Vector2(0.45f, 0f), new Vector2(0.45f, 0f), 20f, -300f, 0f, 60f);
            // Clear of the stepper: the label box hangs below its pivot, so a
            // smaller offset put its descenders through the button's top rule.
            MenuKit.Label(ageRow, "AGE", MenuKit.Small, new Vector2(0f, 1f),
                new Vector2(0f, 44f), TextAnchor.MiddleLeft, MenuKit.Dim, 200f);
            var ageLabel = MenuKit.Label(ageRow, wizAge.ToString(), MenuKit.Head,
                new Vector2(0f, 0.5f), new Vector2(78f, 0f), TextAnchor.MiddleCenter,
                Color.white, 90f, height: 58f, bold: true);
            ageLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            MenuKit.Button(ageRow, "-", new Vector2(0f, 0.5f), new Vector2(0f, 0f),
                new Vector2(60f, 58f), () =>
                { wizAge = Mathf.Max(21, wizAge - 1); ageLabel.text = wizAge.ToString(); }, MenuKit.Head);
            MenuKit.Button(ageRow, "+", new Vector2(0f, 0.5f), new Vector2(156f, 0f),
                new Vector2(60f, 58f), () =>
                { wizAge = Mathf.Min(60, wizAge + 1); ageLabel.text = wizAge.ToString(); }, MenuKit.Head);

            // --- job list: fills the gap between the identity row and the button
            MenuKit.Label(root, "PICK A DAY JOB  (pays weekly, on Fridays)", MenuKit.Small,
                new Vector2(0f, 1f), new Vector2(40f, -180f), TextAnchor.MiddleLeft,
                MenuKit.Dim, 700f);

            // Fills the name field for you. A UGUI InputField cannot be typed
            // into with a controller at all, so without this the debug career is
            // the one thing in the game a pad player simply cannot reach. Sits
            // right-anchored on this row: the label beside it is left-aligned
            // and its text runs out around x=480 on the narrowest canvas, well
            // clear of where this starts.
            // Pivots on its top-right corner (MenuKit.Button pivots on the
            // anchor), so it hangs into the 44-unit gap between the identity row
            // at -168 and the job list at -212: -171 to -209 clears both.
            MenuKit.Button(root, "USE TEST NAME", new Vector2(1f, 1f),
                new Vector2(-40f, -171f), new Vector2(200f, 38f),
                () => { if (wizNameField != null) wizNameField.text = LifeRules.DebugName; },
                MenuKit.Tiny);

            var list = MenuKit.Stretch(root, "JobList",
                Vector2.zero, Vector2.one, 40f, 40f, 100f, -212f);

            int n = LifeRules.Jobs.Length;
            var jobLabels = new Text[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var job = LifeRules.Jobs[i];
                // Row i claims the i-th horizontal band of the list container.
                // Rows therefore always fit, whatever the container ends up being.
                var btn = MenuKit.Button(list, "", new Vector2(0f, 1f), Vector2.zero, Vector2.zero,
                    () =>
                    {
                        wizJob = idx;
                        for (int j = 0; j < jobLabels.Length; j++)
                            jobLabels[j].color = j == idx ? MenuKit.Accent : Color.white;
                    }, MenuKit.Small);
                var brt = btn.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0f, 1f - (i + 1) / (float)n);
                brt.anchorMax = new Vector2(1f, 1f - i / (float)n);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.offsetMin = new Vector2(0f, 3f);
                brt.offsetMax = new Vector2(0f, -3f);

                jobLabels[i] = MenuKit.Label(btn.transform, job.name + "   —   $" + job.dailyPay + "/day",
                    MenuKit.Small, new Vector2(0f, 0.5f), new Vector2(20f, 0f),
                    TextAnchor.MiddleLeft, i == 0 ? MenuKit.Accent : Color.white, 700f);
                jobLabels[i].raycastTarget = false;
            }

            MenuKit.Button(root, "NEXT: PICK A CAR", new Vector2(0.5f, 0f), new Vector2(0f, 20f),
                new Vector2(380f, 62f), () =>
                {
                    // An empty name is not a valid save: Start() routes a state
                    // with no playerName straight back into this wizard, so
                    // committing one traps the player in a loop they cannot see
                    // the cause of. It is also unavoidable on a pad, which
                    // cannot type into the field at all.
                    string chosen = wizNameField.text.Trim().ToUpper();
                    if (chosen.Length == 0) chosen = "DRIVER";
                    LifeSimManager.StartNewGame(chosen, wizAge, wizJob);
                    Destroy(root.gameObject);
                    BuildCarPick();
                }, MenuKit.Body, new Color(0.62f, 0.48f, 0.12f, 1f));

            WirePanelNavigation(root);
        }

        /// <summary>
        /// Navigation for a one-off panel (the wizard, the car picker) that is
        /// not part of the tabbed home surface.
        ///
        /// The cursor starts on the first BUTTON rather than the first
        /// selectable: the wizard's first control is the name field, and
        /// selecting a UGUI InputField activates it — which on a handheld throws
        /// the soft keyboard over the screen before the player has asked for it.
        /// </summary>
        void WirePanelNavigation(RectTransform root)
        {
            var items = MenuNav.Collect(root);
            MenuNav.Column(items);
            Selectable first = null;
            foreach (var s in items) if (s is Button) { first = s; break; }
            if (first == null && items.Count > 0) first = items[0];
            MenuNav.Select(first);
            MenuNav.Watch(gameObject, first);
        }

        /// <summary>
        /// Step two of the wizard: which car you show up in. Three backstory
        /// lanes, and the rule that makes them backstory rather than a purchase —
        /// the down payment happened before day one, so picking the new car does
        /// not touch your savings. Only the monthly payment follows you.
        /// </summary>
        void BuildCarPick()
        {
            var root = MenuKit.Rect(canvas.transform, "CarPick",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(900f, 640f), MenuKit.PanelBg);

            MenuKit.Label(root, "WHAT ARE YOU DRIVING?", 30, new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), TextAnchor.MiddleCenter, MenuKit.Accent, 840f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);
            MenuKit.Label(root, "Your savings are yours either way — the payments are what follow you.",
                15, new Vector2(0.5f, 1f), new Vector2(0f, -66f), TextAnchor.MiddleCenter,
                MenuKit.Dim, 840f).rectTransform.pivot = new Vector2(0.5f, 1f);

            var lanes = CarMarket.RollStartingLanes(S.basePay, S.creditScore);
            if (lanes.Count == 0)
            {
                // No catalog: fall back to the built-in car rather than stranding
                // the player in a wizard with nothing to choose.
                LifeRules.SeedFallbackCar(S);
                LifeSimManager.Save();
                SceneManager.LoadScene(0);
                return;
            }

            float y = -110f;
            foreach (var lane in lanes)
            {
                var captured = lane;
                int age = CarMarket.GameStartYear - lane.spec.modelYear;
                string money = lane.financed
                    ? MenuKit.Money(lane.monthly) + "/mo for " + lane.months + " months"
                    : "paid off — no payments";

                MenuKit.Button(root, "", new Vector2(0f, 1f), new Vector2(40f, y),
                    new Vector2(820f, 118f), () =>
                    {
                        CarMarket.ApplyStartingLane(S, captured);
                        LifeSimManager.Save();
                        SceneManager.LoadScene(0);   // reload home, now in play mode
                    }, 16);

                MenuKit.Label(root, lane.label, 20, new Vector2(0f, 1f),
                    new Vector2(60f, y - 10f), TextAnchor.MiddleLeft, MenuKit.Accent, 400f, bold: true)
                    .raycastTarget = false;
                MenuKit.Label(root, lane.spec.name, 17, new Vector2(0f, 1f),
                    new Vector2(60f, y - 38f), TextAnchor.MiddleLeft, Color.white, 780f)
                    .raycastTarget = false;
                MenuKit.Label(root, lane.spec.hp + " hp · " + lane.spec.drv + " · " +
                        age + " yr · " + lane.odoMiles.ToString("N0") + " mi · cond " + lane.cond,
                    14, new Vector2(0f, 1f), new Vector2(60f, y - 62f),
                    TextAnchor.MiddleLeft, MenuKit.Dim, 780f).raycastTarget = false;
                MenuKit.Label(root, money + "   ·   " + lane.blurb, 14, new Vector2(0f, 1f),
                    new Vector2(60f, y - 86f), TextAnchor.MiddleLeft,
                    lane.financed ? MenuKit.Bad : MenuKit.Good, 780f).raycastTarget = false;
                y -= 132f;
            }

            WirePanelNavigation(root);
        }

        // =================== chrome ===================
        void BuildChrome()
        {
            // Chrome stretches to the canvas width instead of assuming 1280, so a
            // wide phone gets a full-width header rather than a 1280 island.
            var header = MenuKit.Stretch(canvas.transform, "Header",
                new Vector2(0f, 1f), new Vector2(1f, 1f), 0f, 0f, -HeaderH, 0f,
                new Color(0.03f, 0.03f, 0.07f, 1f));
            MenuKit.Label(header, "AT HOME", MenuKit.Head, new Vector2(0f, 0.5f),
                new Vector2(30f, 20f), TextAnchor.MiddleLeft, MenuKit.Accent, 320f, bold: true);
            dateText = MenuKit.Label(header, "", MenuKit.Small, new Vector2(0f, 0.5f),
                new Vector2(30f, -22f), TextAnchor.MiddleLeft, MenuKit.Dim, 620f);
            // Right-aligned to the right edge, under the money. Anchored at the
            // CENTRE and left-aligned, it started roughly where the date line
            // ended on a 16:9 monitor and printed straight through it on a 4:3
            // canvas — the two lines are only 960 units apart there, and both
            // were reserving 620+ of them. Aligning each to its own edge means
            // neither has to know how long the other's string turned out.
            healthText = MenuKit.Label(header, "", MenuKit.Small, new Vector2(1f, 0.5f),
                new Vector2(-30f, -22f), TextAnchor.MiddleRight, MenuKit.Dim, 640f);
            moneyText = MenuKit.Label(header, "", MenuKit.Head, new Vector2(1f, 0.5f),
                new Vector2(-30f, 8f), TextAnchor.MiddleRight, MenuKit.Good, 340f, bold: true);

            var bar = MenuKit.Stretch(canvas.transform, "Tabs",
                new Vector2(0f, 1f), new Vector2(1f, 1f), 0f, 0f,
                -(HeaderH + TabH), -HeaderH, new Color(0.07f, 0.06f, 0.12f, 1f));
            tabIds = new[] { "main", "garage", "rivals", "market", "eat", "bills", "jobs" };
            string[] tabs = tabIds;
            tabButtons.Clear();
            // Each tab claims its share of the bar by ANCHOR, not by a width
            // computed from the canvas. Reading canvas.rect.width here returns a
            // stale value — the scaler has not resolved for this frame yet — and
            // the tabs came out sized for a canvas that never existed, bunched
            // against the left edge. Fractional anchors need no measurement and
            // stay correct on every screen.
            for (int i = 0; i < tabs.Length; i++)
            {
                string captured = tabs[i];
                var btn = MenuKit.Button(bar, tabs[i].ToUpper(), new Vector2(0f, 0.5f),
                    Vector2.zero, Vector2.zero,
                    () => { tab = captured; buyTarget = null; confirmNewGame = false; Rebuild(); },
                    MenuKit.Small);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i / (float)tabs.Length, 0f);
                rt.anchorMax = new Vector2((i + 1) / (float)tabs.Length, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(6f, 7f);
                rt.offsetMax = new Vector2(-6f, -7f);
                tabButtons.Add(btn);
            }
            // Left/right walks the strip. The bar is built left to right, so
            // creation order is the order on screen — no rect has to be measured
            // to know which tab is next, which is the only way to get this right
            // in a menu that is assembled and navigated in the same frame.
            MenuNav.Row(tabButtons.ConvertAll(b => (Selectable)b));
        }

        /// <summary>
        /// Header and tab-bar heights. The body is positioned FROM these rather
        /// than given a fixed height, which is what stops it riding up over the
        /// chrome on a short canvas.
        ///
        /// Proportional to the design column, not fixed: on a handheld that
        /// column is 460 units, and a fixed 96+62 of chrome would eat a third of
        /// the screen before any content was drawn. As fractions the chrome
        /// stays the same share of the screen — and the tab bar stays
        /// finger-sized, because the whole canvas is magnified on exactly the
        /// devices where that matters.
        /// </summary>
        static float HeaderH => MenuKit.DesignHeight * 0.133f;   // 96 at 720
        static float TabH => MenuKit.DesignHeight * 0.086f;      // 62 at 720
        /// <summary>Height of the scrolling viewport, computed rather than
        /// measured — reading the rect of a panel created this frame is the
        /// stale-rect trap that has broken this menu twice.</summary>
        static float BodyH => MenuKit.DesignHeight - HeaderH - TabH;

        RectTransform bodyViewport;

        /// <summary>
        /// Left and right edges of the content column, in canvas units from the
        /// centre. Derived from the real canvas, because the design column is
        /// 720 units tall on a desktop and 560 on a handheld and the WIDTH
        /// follows the height — so a margin hard-coded for one screen runs off
        /// another. The narrowest case is a 4:3 desktop at +/-480; a 2.24:1
        /// phone gives +/-627.
        /// </summary>
        static float ColL => -MenuKit.HalfWidth * 0.95f;
        static float ColR => MenuKit.HalfWidth * 0.95f;
        static float ColW => ColR - ColL;

        void Rebuild()
        {
            if (bodyViewport != null) Destroy(bodyViewport.gameObject);
            // Fill everything below the tab bar. The old fixed 560-tall panel
            // anchored to the bottom overlapped the tabs on any canvas shorter
            // than 718 units — which a 2.24:1 phone is.
            bodyViewport = MenuKit.Stretch(canvas.transform, "Body",
                new Vector2(0f, 0f), new Vector2(1f, 1f), 0f, 0f, 0f, -(HeaderH + TabH));
            // Tabs build into the scroll CONTENT, not the viewport, so a screen
            // taller than the phone column scrolls instead of being cut off.
            body = MenuKit.ScrollBody(bodyViewport);

            moneyText.text = MenuKit.Money(S.money);
            dateText.text = LifeRules.DateLabel(S.day) + "  ·  " +
                            LifeRules.SlotNames[Mathf.Clamp(S.slotIndex, 0, 2)] +
                            (S.debugMode ? "  ·  DEBUG" : "");
            healthText.text = "HEALTH " + Mathf.RoundToInt(S.health) + " (" + LifeRules.HealthLabel(S.health) + ")" +
                              "   REP " + Mathf.RoundToInt(S.streetRep) + " (" + LifeRules.StreetTier(S.streetRep).name + ")";

            // The turntable is off unless the page about to be built asks for
            // it. Rebuild destroys the RawImage that was showing it, so a viewer
            // left running would be a camera rendering into nothing.
            if (viewer != null) viewer.SetVisible(false);

            switch (tab)
            {
                case "main": BuildMain(); break;
                case "garage": BuildGarage(); break;
                case "rivals": BuildRivals(); break;
                case "service": BuildService(); break;
                case "tune": BuildTune(); break;
                case "market": BuildMarket(); break;
                case "buy": BuildBuyDetail(); break;
                case "debugcars": BuildDebugCars(); break;
                case "inspect": BuildInspect(); break;
                case "inspectfocus": BuildInspectFocus(); break;
                case "toolbox": BuildToolbox(); break;
                case "eat": BuildEat(); break;
                case "bills": BuildBills(); break;
                case "jobs": BuildJobs(); break;
            }

            MenuKit.FitScrollContent(body, BodyH);
            WireNavigation();
        }

        /// <summary>
        /// Give the freshly built page a navigation graph and put the cursor on
        /// it. Called at the end of every Rebuild because Rebuild destroys and
        /// recreates the whole body — every Selectable the previous graph
        /// pointed at is gone, and a graph pointing at destroyed objects is the
        /// same dead pad as no graph at all.
        /// </summary>
        void WireNavigation()
        {
            var rows = MenuNav.Collect(body);
            MenuNav.Column(rows);

            var tabSel = tabButtons.ConvertAll(b => (Selectable)b);
            int idx = System.Array.IndexOf(tabIds, tab);
            // Detail pages ("buy", "service", "tune") are reached FROM a tab and
            // are not tabs themselves, so fall back to the tab that owns them.
            Selectable active = idx >= 0 && idx < tabSel.Count ? tabSel[idx] : OwningTabButton();
            MenuNav.Join(tabSel, rows, active);

            // The first row of content, not the tab bar: the player is already
            // on the tab they asked for, and starting on it would mean pressing
            // down before anything they came here to do is reachable.
            Selectable first = rows.Count > 0 ? rows[0] : active;
            MenuNav.Select(first);
            MenuNav.Watch(gameObject, first);
        }

        /// <summary>The tab a detail page belongs under, for the UP key and for
        /// CANCEL.</summary>
        Selectable OwningTabButton()
        {
            string owner = ParentTab();
            int i = System.Array.IndexOf(tabIds, owner);
            return i >= 0 && i < tabButtons.Count ? tabButtons[i] : null;
        }

        /// <summary>Where BACK goes from the current page.</summary>
        string ParentTab()
        {
            switch (tab)
            {
                case "buy": return "market";
                // The focus view backs out to the car map, not to the garage —
                // an inspection is a place you are IN, and BACK should walk you
                // out of it a room at a time.
                case "inspectfocus": return "inspect";
                case "service":
                case "tune":
                case "inspect":
                case "toolbox":
                case "debugcars": return "garage";
                default: return "main";
            }
        }

        /// <summary>
        /// Pad and keyboard chrome that has no on-screen control of its own:
        /// the shoulder buttons page through tabs the way a console menu does,
        /// and B / Escape backs out of a detail page.
        /// </summary>
        void Update()
        {
            if (wizard || tabButtons.Count == 0) return;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;

            int step = 0;
            if (pad != null)
            {
                if (pad.rightShoulder.wasPressedThisFrame) step = 1;
                else if (pad.leftShoulder.wasPressedThisFrame) step = -1;
            }
            if (step == 0 && kb != null)
            {
                if (kb.pageDownKey.wasPressedThisFrame) step = 1;
                else if (kb.pageUpKey.wasPressedThisFrame) step = -1;
            }
            if (step != 0)
            {
                int i = System.Array.IndexOf(tabIds, tab);
                if (i < 0) i = System.Array.IndexOf(tabIds, ParentTab());
                if (i < 0) i = 0;
                tab = tabIds[(i + step + tabIds.Length) % tabIds.Length];
                buyTarget = null;
                confirmNewGame = false;
                Rebuild();
                return;
            }

            bool cancel = (pad != null && pad.buttonEast.wasPressedThisFrame) ||
                          (kb != null && kb.escapeKey.wasPressedThisFrame);
            if (!cancel) return;
            if (confirmNewGame) { confirmNewGame = false; Rebuild(); }
            else if (tab != "main") { tab = ParentTab(); buyTarget = null; Rebuild(); }
        }

        // =================== tabs ===================
        /// <summary>
        /// The track picker: a map drawn from the circuit's own centreline, its
        /// numbers, and the hour the next race will run at.
        ///
        /// It sits ABOVE the race button rather than on a tab of its own. Where
        /// you are about to race is part of the decision to race, and a picker
        /// one tab away is one nobody would find twice.
        /// </summary>
        void BuildTrackBlock(ref float y)
        {
            var t = TrackCatalog.At(S.trackIndex);
            if (t.city) { S.trackIndex = 0; t = TrackCatalog.At(0); }   // saves never point races at the city
            const float mapSize = 116f;

            var mapPanel = MenuKit.Rect(body, "TrackMap",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, mapSize), y),
                new Vector2(mapSize, mapSize), new Color(0f, 0f, 0f, 0.55f));
            var mapGO = new GameObject("Map");
            mapGO.transform.SetParent(mapPanel, false);
            var mapImg = mapGO.AddComponent<RawImage>();
            mapImg.texture = TrackCatalog.Thumbnail(t);
            mapImg.raycastTarget = false;
            var mrt = mapImg.rectTransform;
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
            mrt.offsetMin = new Vector2(4f, 4f); mrt.offsetMax = new Vector2(-4f, -4f);

            float textX = ColL + mapSize + 16f;
            float textW = Mathf.Max(180f, ColW - mapSize - 16f);
            MenuKit.Label(body, t.name, 24, new Vector2(0.5f, 1f), new Vector2(textX, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, textW, height: 30f, bold: true);
            MenuKit.Label(body,
                // A drag race is a drag race whether the strip was generated or
                // surveyed, so this asks IsDragEvent and not where the geometry
                // came from. And it quotes SHORT runs in metres: the bridges
                // and the quarter mile all round to "1.4 km" and "0.4 km"
                // otherwise, which loses the only number that distinguishes
                // them.
                t.IsDragEvent
                    ? (t.RaceMeters < 1000f
                          ? Mathf.RoundToInt(t.RaceMeters) + " m"
                          : (t.RaceMeters / 1000f).ToString("0.00") + " km")
                      + "  ·  " + t.dragLabel + "  ·  standing start"
                : t.stage
                    ? (t.RaceMeters / 1000f).ToString("0.0") + " km  ·  " + t.dragLabel +
                      "  ·  point to point"
                    : Mathf.RoundToInt(t.LengthM) + " m  ·  " + t.laps + " laps  ·  " +
                      (t.RaceMeters / 1000f).ToString("0.0") + " km",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(textX, y - 32f),
                TextAnchor.MiddleLeft, Color.white, textW, height: 26f);
            float navW = Mathf.Min(150f, (textW - 10f) / 2f);
            MenuKit.Button(body, "< TRACK", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(textX, navW), y - 58f), new Vector2(navW, 34f),
                () => StepTrack(-1), 15);
            MenuKit.Button(body, "TRACK >", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(textX + navW + 10f, navW), y - 58f),
                new Vector2(navW, 34f), () => StepTrack(1), 15);

            y -= mapSize + 10f;
            MenuKit.Label(body, t.blurb, MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 26f);
            y -= 32f;

            // The hour, and a way to change it. It reads as one wide row rather
            // than sitting beside the map, because the whole point is that it is
            // a CHOICE — buried next to the track stats it read as a caption,
            // which is how a player ends up with seven skies and no idea any of
            // them can be picked.
            float half = Mathf.Min(300f, (ColW - 12f) / 2f);
            MenuKit.Button(body, "< TIME", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, half), y), new Vector2(half, 40f),
                () => StepHour(-1), 15);
            MenuKit.Button(body, "TIME >", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + half + 12f, half), y), new Vector2(half, 40f),
                () => StepHour(1), 15);
            y -= 46f;
            MenuKit.Label(body,
                S.raceTimeIndex < 0
                    ? "RACING AT " + TimeOfDay.Label(RaceHour()) + "  ·  FOLLOWING THE CLOCK"
                    : "RACING AT " + TimeOfDay.Label(RaceHour()),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y),
                TextAnchor.MiddleCenter, MenuKit.Accent, ColW, height: 24f);
            y -= 34f;
        }

        void StepTrack(int step)
        {
            int idx = S.trackIndex;
            do { idx = (idx + step + TrackCatalog.Count) % TrackCatalog.Count; }
            while (TrackCatalog.At(idx).city);   // the city is not a race venue — FREE ROAM is its door
            S.trackIndex = idx;
            LifeSimManager.Save();
            Rebuild();
        }

        /// <summary>Cycle the hour through FOLLOW-THE-CLOCK and the seven fixed
        /// ones. Follow sits at index -1 so it is the state a save defaults to
        /// and the one a player returns to by walking off either end.</summary>
        void StepHour(int step)
        {
            int n = TimeOfDay.Count + 1;                  // +1 for "follow"
            int cur = S.raceTimeIndex < 0 ? 0 : S.raceTimeIndex + 1;
            int next = (cur + step + n) % n;
            S.raceTimeIndex = next == 0 ? -1 : next - 1;
            LifeSimManager.Save();
            Rebuild();
        }

        /// <summary>The hour the NEXT race runs at: the player's pick, or the
        /// activity slot's own band when they have not made one.</summary>
        int RaceHour() => S.raceTimeIndex >= 0
            ? Mathf.Clamp(S.raceTimeIndex, 0, TimeOfDay.Count - 1)
            : TimeOfDay.ForSlot(S.slotIndex, S.day);

        void BuildMain()
        {
            float y = -20f;
            BuildDebugBlock(ref y);
            BuildTrackBlock(ref y);

            var track = TrackCatalog.At(S.trackIndex);
            bool racedToday = LifeRules.RacedToday(S);
            // Pre-race fuel gate, off the circuit the player actually picked: a
            // race on the long one burns half as much again as the short one,
            // and a fixed 3 x 1168 m estimate would wave a car onto Ridge Pass
            // with enough fuel for Harbor Point.
            //
            // The bar is much LOWER than it used to be. It used to demand fuel
            // for the whole race, because the whole race was the only unit fuel
            // came in; now every circuit has a forecourt on it, so the question
            // is only whether the car can REACH one. Running a race on a
            // half-tank and planning a stop is a strategy, not a mistake, and
            // the gate has no business calling it one.
            float burn = LifeRules.RaceFuelBurnPct(track.RaceMeters, S.ActiveCar);
            float need = LifeRules.RequiredFuelPct(track, S.ActiveCar);
            bool lowFuel = S.ActiveCar != null && S.ActiveCar.fuel <= need;
            bool needsStop = !lowFuel && S.ActiveCar != null && S.ActiveCar.fuel <= burn;
            string raceLabel = racedToday ? "RACED TODAY — SLEEP FIRST"
                             : lowFuel ? (track.hasFuelStop ? "TOO LOW TO REACH THE PUMPS"
                                          // A strip, a stage and the city all
                                          // have no forecourt, and only one of
                                          // them is a strip — the parkway read
                                          // "NO PUMPS ON A STRIP" for months.
                                          : track.drag ? "LOW FUEL — NO PUMPS ON A STRIP"
                                                       : "LOW FUEL — NO PUMPS OUT THERE")
                             : "GET IN CAR  >>";
            bool canRace = !racedToday && !lowFuel;
            int tier = LifeRules.StreetTier(S.streetRep).idx;
            MenuKit.Button(body, raceLabel,
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 72f),
                canRace ? (UnityEngine.Events.UnityAction)(() => StartRace(false)) : null, 22,
                canRace ? new Color(1f, 0.84f, 0.4f, 0.28f) : MenuKit.BtnBgDisabled);
            y -= 78f;

            MenuKit.Label(body, "WIN " + MenuKit.Money(LifeRules.WinPrize[tier]) + " · " +
                LifeRules.StreetTier(S.streetRep).name + " tier", 14,
                new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Dim, 460f);
            y -= 30f;

            // Told BEFORE the race rather than discovered halfway round it.
            // The player is allowed to start on a tank that will not finish —
            // that is the whole point of the forecourt — but only if they know.
            if (needsStop || lowFuel)
            {
                MenuKit.Label(body,
                    lowFuel
                        ? (track.hasFuelStop
                            ? "Not enough to get to the pumps. Call the truck from the garage."
                            : track.drag
                                ? "A strip has no pumps. Fill up before you go."
                                : "There are no services out on this route. Fill up before you go.")
                        : "This race burns more than you are carrying — plan a stop at the pumps.",
                    14, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                    lowFuel ? MenuKit.Bad : MenuKit.Accent, 470f);
                y -= 28f;
            }

            // Practice is the pressure valve on the one-paying-race-a-day cap:
            // it still costs a slot and still wears the car, but pays nothing
            // and does not stamp lastRaceDay.
            if (!lowFuel)
            {
                MenuKit.Button(body, "PRACTICE LAP (no purse)", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(460f, 46f),
                    () => StartRace(true), 16);
                y -= 62f;
            }

            // Charlotte. A drive costs a slot, burns real fuel and wears real
            // tyres, pays nothing — and there are no pumps out there yet, so
            // the door stays shut on a tank that would strand the car two
            // blocks in. Map data (c) OpenStreetMap contributors.
            bool roamFuel = S.ActiveCar != null && S.ActiveCar.fuel > 10f;
            MenuKit.Button(body,
                S.ActiveCar == null ? "FREE ROAM — NEEDS A CAR"
                    : roamFuel ? "FREE ROAM — CHARLOTTE" : "FREE ROAM — NEEDS FUEL",
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 52f),
                roamFuel ? (UnityEngine.Events.UnityAction)StartFreeRoam : null, 17,
                roamFuel ? new Color(0.45f, 0.75f, 1f, 0.22f) : MenuKit.BtnBgDisabled);
            y -= 58f;
            MenuKit.Label(body, "The whole city at 1:1 — no purse, real fuel, real time.", 14,
                new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Dim, 470f);
            y -= 30f;

            // Faults are the reason a race can go wrong, so they belong on the
            // screen you launch from, not buried a tab away.
            var activeCar = S.ActiveCar;
            if (activeCar != null && KnownFaults(activeCar) > 0)
            {
                foreach (var f in activeCar.faults)
                {
                    if (f.hidden) continue;
                    string fx = FaultCatalog.EffectSummary(f.id);
                    MenuKit.Label(body, "! " + f.label + (fx.Length > 0 ? " — " + fx : ""), 14,
                        new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                        MenuKit.Bad, 470f);
                    y -= 22f;
                }
                y -= 10f;
            }

            bool canWork = !string.IsNullOrEmpty(S.playerJob) &&
                           !LifeRules.IsWeekend(S.day) && !S.workedToday;
            string workLabel = string.IsNullOrEmpty(S.playerJob) ? "NO JOB (SEE JOBS TAB)"
                : LifeRules.IsWeekend(S.day) ? "WEEKEND — NO WORK"
                : S.workedToday ? "ALREADY WORKED TODAY"
                : "GO TO WORK (" + S.playerJob + ")";
            MenuKit.Button(body, workLabel, new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(460f, 56f), canWork ? DoWork : (UnityEngine.Events.UnityAction)null,
                18, canWork ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 74f;

            MenuKit.Button(body, "SLEEP UNTIL TOMORROW", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(460f, 56f), DoSleep, 18);
            y -= 74f;

            BuildStartOverBlock(ref y);
            y -= 10f;

            MenuKit.Label(body, "RECENTLY", 15, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, 460f);
            y -= 28f;
            int n = S.calendarLog.Count;
            for (int i = Mathf.Max(0, n - 6); i < n; i++)
            {
                MenuKit.Label(body, S.calendarLog[i], 14, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, Color.white, 700f);
                y -= 24f;
            }

        }

        /// <summary>
        /// Start over. <see cref="LifeSimManager.DeleteSave"/> had existed since
        /// the save format did and nothing ever called it, so a career could be
        /// entered and never left — and the new-game wizard, which is the only
        /// place a name can be typed, only appears when there is NO save. That
        /// is why "no option to restart game" and "no way to enter a name" are
        /// the same bug.
        ///
        /// It sits directly under SLEEP rather than at the foot of the page: the
        /// activity log below it grows by a line a day, so anything after the log
        /// drifts further below the fold the longer a career runs — which is how
        /// the first version of this button ended up invisible.
        /// </summary>
        void BuildStartOverBlock(ref float y)
        {
            if (!confirmNewGame)
            {
                // Muted and behind a confirm: it destroys the save and the
                // player is now one stray press from it.
                MenuKit.Button(body, "NEW GAME (ERASE SAVE)", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(460f, 44f),
                    () => { confirmNewGame = true; Rebuild(); }, 15, MenuKit.BtnBgDisabled);
                y -= 52f;
                return;
            }

            MenuKit.Label(body, "ERASE THIS CAREER? Money, cars, faults and rep are gone.",
                15, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Bad, ColW);
            y -= 34f;
            MenuKit.Button(body, "YES — START OVER", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(460f, 46f), () =>
                {
                    LifeSimManager.DeleteSave();
                    RaceHandoff.ClearAll();
                    SceneManager.LoadScene(0);   // reboots straight into the wizard
                }, 16, new Color(0.42f, 0.12f, 0.12f, 1f));
            y -= 54f;
            MenuKit.Button(body, "CANCEL", new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(460f, 44f), () => { confirmNewGame = false; Rebuild(); }, 15);
            y -= 52f;
        }

        /// <summary>
        /// Test tools, at the TOP of the first tab.
        ///
        /// The first cut of this put the debug career behind the new-game wizard
        /// and nothing else, which had two problems reported straight back: the
        /// wizard only appears when there is NO save, so an existing career had
        /// no way in at all short of erasing itself; and the NEW GAME button
        /// that would have erased it sat at the bottom of a scrolling page under
        /// an activity log that grows every day, i.e. below the fold. A debug
        /// switch nobody can find is a debug switch that does not exist — so it
        /// is the first thing on the page now, and it works on the save you are
        /// already playing.
        /// </summary>
        void BuildDebugBlock(ref float y)
        {
            if (!S.debugMode)
            {
                MenuKit.Button(body, "DEBUG MODE — $999,999 + 6 GARAGE SLOTS",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 40f),
                    () =>
                    {
                        LifeRules.EnableDebug(S);
                        LifeSimManager.Save();
                        Rebuild();
                        Toast("DEBUG MODE ON — " + MenuKit.Money(S.money) + ", " +
                              S.garageSlots + " garage slots, top credit");
                    }, 15, new Color(0.26f, 0.14f, 0.34f, 1f));
                y -= 50f;
                return;
            }

            // Height 24, not the default 40. A MenuKit label hangs DOWN from its
            // y (pivot follows the anchor), so a 40-tall box needs 40 units of
            // clearance — step less than that and the text descends through
            // whatever comes next. Harmless between two labels, visible when the
            // next thing has a background, which is what the preview caught here.
            MenuKit.Label(body, "DEBUG — " + MenuKit.Money(S.money) + "  ·  garage " +
                    S.cars.Count + "/" + S.garageSlots + "  ·  credit " + S.creditScore,
                15, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 24f);
            y -= 32f;

            // Three across. Positioned from ColL/ColW, which derive from the real
            // canvas — never from a measured rect, which on a page built this
            // frame has not resolved yet.
            const float Gap = 10f;
            float w = (ColW - Gap * 2f) / 3f;
            float rowY = y;   // a copy: a local function cannot capture a ref parameter
            void Cell(int i, string label, UnityEngine.Events.UnityAction act) =>
                MenuKit.Button(body, label, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + i * (w + Gap), w), rowY),
                    new Vector2(w, 38f), act, 14, new Color(0.26f, 0.14f, 0.34f, 1f));

            Cell(0, "TOP UP CASH", () =>
            {
                LifeRules.EnableDebug(S);
                LifeSimManager.Save(); Rebuild();
            });
            // The two gates that stop a tester racing back to back: one paying
            // race per day, and a tank a 3-lap race mostly empties.
            Cell(1, "RESET DAY", () =>
            {
                S.lastRaceDay = 0;
                S.workedToday = false;
                S.slotsActiveToday = 0;
                LifeSimManager.Save(); Rebuild();
                Toast("DEBUG: day limits cleared");
            });
            Cell(2, "FIX & FUEL CAR", () =>
            {
                var c = S.ActiveCar;
                if (c != null)
                {
                    c.fuel = 100f; c.engine = 100f; c.tires = 100f;
                    c.carHP = 100f; c.paint = 100f;
                    c.faults.Clear();
                }
                LifeSimManager.Save(); Rebuild();
                Toast("DEBUG: car restored");
            });
            y -= 46f;

            // Hidden faults only arrive with a car bought off the newspaper, so
            // a debug career granted its whole fleet for free has nothing to
            // find and INSPECT reads as doing nothing. This puts something under
            // the car to be found.
            float row2 = y;
            void Cell2(int i, string label, UnityEngine.Events.UnityAction act) =>
                MenuKit.Button(body, label, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + i * (w + Gap), w), row2),
                    new Vector2(w, 38f), act, 14, new Color(0.26f, 0.14f, 0.34f, 1f));

            Cell2(0, "PLANT FAULTS", () =>
            {
                var c = S.ActiveCar;
                int n = c == null ? 0
                      : Inspection.SeedHidden(S, c, CarCatalog.Get(c.specId), 55);
                LifeSimManager.Save(); Rebuild();
                Toast(n > 0 ? "DEBUG: " + n + " hidden fault" + (n == 1 ? "" : "s") +
                              " planted — go and find them"
                            : "DEBUG: nothing planted (the lanes are already full)");
            });
            Cell2(1, "GRANT TOOLS", () =>
            {
                foreach (var t in Toolbox.All)
                    if (!Toolbox.Owned(S, t.id)) S.tools.Add(t.id);
                LifeSimManager.Save(); Rebuild();
                Toast("DEBUG: full toolbox");
            });
            Cell2(2, "CLEAR LATCH", () =>
            {
                var c = S.ActiveCar;
                if (c != null) { c.inspectDay = -1; c.floorCheckedDay = -1; c.inspectedSubs.Clear(); }
                LifeSimManager.Save(); Rebuild();
                Toast("DEBUG: inspection reset");
            });
            y -= 50f;
        }

        /// <summary>
        /// Pick which car you are driving, from the tab that is actually about
        /// your cars.
        ///
        /// The switch already existed — a DRIVE button beside each owned car,
        /// under the classifieds, at the bottom of MARKET. Nobody looks for
        /// "which car am I driving" inside a shop, and the reporter SOLD their
        /// main car to change which one they drove: a destructive workaround for
        /// a control that was in the wrong room.
        /// </summary>
        void BuildGarageSwitcher(ref float y)
        {
            // Debug tools go FIRST, here as on MAIN. Below the car list it would
            // drift down every time another car was added — and a debug switch
            // that moves is one you have to hunt for.
            if (S.debugMode)
            {
                MenuKit.Button(body, "DEBUG — ADD ANY CAR (FREE)", new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL, 380f), y), new Vector2(380f, 40f),
                    () => { tab = "debugcars"; debugCarPage = 0; Rebuild(); }, 15,
                    new Color(0.26f, 0.14f, 0.34f, 1f));
                y -= 50f;
            }

            // The list draws even with ONE car. It used to return here when
            // there was nothing to choose between, which is exactly backwards:
            // "your cars" is the heading of the whole screen, and a garage that
            // shows no cars at all reads as broken rather than as uncluttered.
            MenuKit.Label(body, "YOUR CARS (" + S.cars.Count + ")   ·   TAP TO SWITCH", 16,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, ColW, height: 24f);
            y -= 30f;

            foreach (var owned in S.cars)
            {
                var captured = owned;
                bool active = owned.id == S.activeCar;
                var spec = CarCatalog.Get(owned.specId);
                const float rowH = 54f;

                var row = MenuKit.Button(body, "", new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL, ColW), y), new Vector2(ColW, rowH),
                    active
                        ? (UnityEngine.Events.UnityAction)(() =>
                            Toast("already driving " + captured.displayName))
                        : () =>
                        {
                            S.activeCar = captured.id;
                            LifeSimManager.Save(); Rebuild();
                            Toast("now driving " + captured.displayName);
                        },
                    14, active ? new Color(0.42f, 0.34f, 0.10f, 1f) : (Color?)null);

                // The button's own caption is left empty and the row is drawn
                // into it instead: MenuKit.Button centres one stretched label,
                // and this row needs a swatch, two columns of type and a right
                // margin. Children of the button still take its click.
                var rt = (RectTransform)row.transform;
                var swatch = MenuKit.Rect(rt, "Paint", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(30f, 0f), new Vector2(44f, 30f), PaintOf(spec));
                swatch.GetComponent<Image>().raycastTarget = false;

                MenuKit.Label(rt, (active ? "> " : "") + Clip(owned.displayName, 40),
                    16, new Vector2(0f, 0.5f), new Vector2(64f, 9f), TextAnchor.MiddleLeft,
                    active ? MenuKit.Accent : Color.white, ColW * 0.62f, height: 22f);

                string drv = spec != null ? spec.drv + "  ·  " + spec.hp + " hp  ·  " : "";
                MenuKit.Label(rt, drv + owned.odoMiles.ToString("N0") + " mi  ·  " +
                        (active ? "DRIVING" : "parked"),
                    14, new Vector2(0f, 0.5f), new Vector2(64f, -11f), TextAnchor.MiddleLeft,
                    MenuKit.Dim, ColW * 0.62f, height: 20f);

                // Worst condition stat, right-aligned — the one number that
                // decides whether this car is the one you should be racing.
                float worst = Mathf.Min(Mathf.Min(owned.engine, owned.tires),
                                        Mathf.Min(owned.carHP, owned.paint));
                MenuKit.Label(rt, Mathf.RoundToInt(worst) + "%",
                    16, new Vector2(1f, 0.5f), new Vector2(-24f, 9f), TextAnchor.MiddleRight,
                    worst > 60f ? MenuKit.Good : worst > 30f ? MenuKit.Accent : MenuKit.Bad,
                    120f, height: 22f);
                int known = KnownFaults(owned);
                MenuKit.Label(rt, known > 0
                        ? known + " known fault" + (known == 1 ? "" : "s")
                        : "no known faults",
                    14, new Vector2(1f, 0.5f), new Vector2(-24f, -11f), TextAnchor.MiddleRight,
                    MenuKit.Dim, 220f, height: 20f);

                y -= rowH + 6f;
            }
            y -= 6f;
        }

        /// <summary>Faults the player has actually been told about. Every list
        /// on these screens counts through here, because a hidden fault is on
        /// the car and is already costing them lap time — it is just not
        /// something anyone has said out loud yet.</summary>
        static int KnownFaults(OwnedCar car)
        {
            if (car == null) return 0;
            int n = 0;
            foreach (var f in car.faults) if (!f.hidden) n++;
            return n;
        }

        /// <summary>The colour a car's catalog entry claims, for the list
        /// swatch. Falls back to a neutral grey rather than to black, which on
        /// this background would read as a missing swatch.</summary>
        static Color PaintOf(CarSpec spec)
        {
            if (spec != null && !string.IsNullOrEmpty(spec.color) &&
                ColorUtility.TryParseHtmlString(spec.color, out var c))
                return c;
            return new Color(0.45f, 0.45f, 0.5f, 1f);
        }

        /// <summary>
        /// A viewport onto the turntable, plus the caption naming which of the
        /// sixteen shells the car is wearing.
        ///
        /// 16:10 to match the render texture. A viewport at a different ratio
        /// would either letterbox or stretch, and a stretched car in a garage is
        /// worse than no car at all.
        /// </summary>
        /// <param name="fallbackKey">Shell to draw when the car has no catalog
        /// entry. The seeded starter RX-7 carries an EMPTY specId — it is the
        /// built-in car the whole game was tuned on rather than a catalog row —
        /// so a garage that only drew catalog cars would show nothing at all
        /// until the player bought their second one. Null means draw nothing,
        /// which is what the market's buy page wants: inventing a shell for a
        /// listing whose spec failed to resolve would be a lie about what is
        /// for sale.</param>
        void DrawCarView(CarSpec spec, ref float y, float height = 230f,
                         string fallbackKey = null)
        {
            if (spec != null) Viewer.Show(spec);
            else if (fallbackKey != null) Viewer.Show(CarModelLibrary.Load(fallbackKey), 0);
            else return;
            if (Viewer.Shown == null) return;

            float w = Mathf.Min(ColW, height * 1.6f);
            var panel = MenuKit.Rect(body, "CarViewPanel",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(w, height), new Color(0f, 0f, 0f, 0.6f));
            Viewer.AttachTo(panel);
            // A gold hairline round the viewport. Without it a dark panel on a
            // dark page reads as blank space, which is how a player scrolls
            // straight past their own car and reports that the garage does not
            // show one — the same reason every other block on these screens has
            // a rule on it.
            foreach (var edge in new[]
            {
                new[] { 0f, 0f, 1f, 0f },   // bottom
                new[] { 0f, 1f, 1f, 1f },   // top
                new[] { 0f, 0f, 0f, 1f },   // left
                new[] { 1f, 0f, 1f, 1f },   // right
            })
            {
                var line = MenuKit.Stretch(panel, "Edge",
                    new Vector2(edge[0], edge[1]), new Vector2(edge[2], edge[3]),
                    0f, 0f, 0f, 0f, MenuKit.Line);
                var rt = line;
                // Two units thick whichever way it runs, grown inward from the
                // edge it was anchored to.
                if (edge[1] == edge[3]) rt.offsetMin = new Vector2(0f, edge[1] > 0.5f ? -2f : 0f);
                if (edge[1] == edge[3]) rt.offsetMax = new Vector2(0f, edge[1] > 0.5f ? 0f : 2f);
                if (edge[0] == edge[2]) rt.offsetMin = new Vector2(edge[0] > 0.5f ? -2f : 0f, 0f);
                if (edge[0] == edge[2]) rt.offsetMax = new Vector2(edge[0] > 0.5f ? 0f : 2f, 0f);
                var img = line.GetComponent<Image>();
                if (img != null) img.raycastTarget = false;
            }
            y -= height + 6f;

            var shell = Viewer.Shown;
            MenuKit.Label(body,
                (shell != null ? shell.displayName + "  ·  " : "") + "DRAG TO TURN, TAP TO SPIN",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y),
                TextAnchor.MiddleCenter, MenuKit.Dim, ColW, height: 24f);
            y -= 32f;
        }

        void BuildGarage()
        {
            // Standing at the garage screen spends the walk-in hand-off: an
            // inspection backed out of rather than finished must not leave a
            // flag behind that teleports a LATER inspection into the bays.
            InspectFromGarage = false;
            inspectCarId = "";

            var car = S.ActiveCar;
            if (car == null)
            {
                MenuKit.Label(body, "No car.", 20, new Vector2(0.5f, 1f), new Vector2(ColL, -40f));
                return;
            }
            float y = -20f;
            DrawCarView(CarCatalog.Get(car.specId), ref y,
                        fallbackKey: CarModelLibrary.Default);
            BuildGarageSwitcher(ref y);
            MenuKit.Label(body, car.displayName, 23, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, 800f, bold: true);
            y -= 38f;
            MenuKit.Label(body, "Odometer " + car.odoMiles.ToString("N0") + " mi   ·   paid " +
                MenuKit.Money(car.paidPrice), 16, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, 800f);
            y -= 44f;

            // The garage is a PLACE now, and this is the door. High on the tab
            // rather than under the condition bars: on a phone column the bars,
            // the fuel row and the fault list push anything below them off the
            // first screen, and a feature nobody scrolls to is one nobody finds.
            MenuKit.Button(body, "WALK INTO YOUR HOUSE  >>",
                new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(Mathf.Min(ColW, 460f), 52f), () =>
                {
                    LifeSimManager.Save();
                    SceneManager.LoadScene(TrackCatalog.GarageSceneIndex);
                }, 18, new Color(0.20f, 0.30f, 0.24f, 1f));
            y -= 64f;
            DrawBar("ENGINE", car.engine, ref y);
            DrawBar("TIRES", car.tires, ref y);
            DrawBar("BODY", car.carHP, ref y);
            DrawBar("PAINT", car.paint, ref y);
            DrawBar("FUEL", car.fuel, ref y);

            // A percentage on its own does not answer the question the player
            // is actually asking, which is whether this car gets to the end of
            // the thing they picked. Tank and economy are per-car now, so a
            // half-tank means something different in every car in the garage
            // and the number of kilometres has to be printed to be known.
            var fuel = FuelProfile.For(car);
            MenuKit.Label(body,
                fuel.tankGal.ToString("0.0") + " gal   ·   " + Mathf.RoundToInt(fuel.mpg) +
                " mpg   ·   about " +
                Mathf.RoundToInt(fuel.RangeKm(FuelModel.RacePaceLoad) * car.fuel / 100f) +
                " km left, driven hard",
                14, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, 800f);
            y -= 30f;

            // Fuel is bought at the PUMPS now — drive onto the forecourt on any
            // circuit, stop, and hold the fuel control. What is left here is the
            // call-out, which exists so that a car too empty to reach a pump is
            // never a career with no legal move in it. It carries a fee on top
            // of the fuel precisely so it reads as the expensive answer.
            int cost = LifeRules.CallOutRefuelCost(car);
            bool canFill = car.fuel < 99.5f && S.money >= cost;
            MenuKit.Button(body, car.fuel >= 99.5f ? "TANK FULL"
                    : "CALL FUEL TRUCK — " + MenuKit.Money(cost),
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(360f, 50f),
                canFill ? (UnityEngine.Events.UnityAction)(() =>
                {
                    S.money -= cost;
                    car.fuel = 100f;
                    S.calendarLog.Add("Day " + S.day + ": fuel truck call-out — " +
                                      MenuKit.Money(cost));
                    LifeSimManager.Save(); Rebuild();
                    Toast("TANK FILLED — " + MenuKit.Money(cost));
                }) : null, 17, canFill ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 56f;
            MenuKit.Label(body, "Pumps on the circuit cost " +
                MenuKit.Money(fuel.CostToFill(0f)) +
                " to fill this one — the truck adds " +
                MenuKit.Money(LifeRules.FuelCallOutFee) + ".",
                14, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, 800f);
            y -= 30f;

            // Repairs in progress. The car is not blocked from racing — RG2
            // blocks it, but with one car in the garage that would strand the
            // player for days with nothing to do.
            foreach (var p in S.pendingParts)
            {
                if (p.carId != car.id) continue;
                int daysLeft = Mathf.Max(0, p.readyDay - S.day);
                MenuKit.Label(body, "IN PROGRESS: " + p.label + " — ready in " +
                    daysLeft + (daysLeft == 1 ? " day" : " days"), 15,
                    new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.MiddleLeft, MenuKit.Good, 800f);
                y -= 26f;
            }

            if (KnownFaults(car) > 0)
            {
                MenuKit.Label(body, "FAULTS", 15, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Bad, 300f, bold: true);
                y -= 30f;
                // Hidden faults are on this car and are already slowing it down;
                // listing them here would hand the player the answer INSPECT
                // exists to make them work for.
                foreach (var f in car.faults) if (!f.hidden) DrawFaultRow(car, f, ref y);
            }
            else
            {
                MenuKit.Label(body, "No faults. Nothing needs fixing.", 15,
                    new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.MiddleLeft, MenuKit.Dim, 700f);
                y -= 30f;
            }

            y -= 10f;
            float gBtnW = Mathf.Min(300f, (ColW - 12f) / 2f);
            MenuKit.Button(body, "MECHANIC SERVICES", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, gBtnW), y), new Vector2(gBtnW, 44f),
                () => { tab = "service"; Rebuild(); }, 16);
            MenuKit.Button(body, "PARTS + TUNING", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + gBtnW + 12f, gBtnW), y), new Vector2(gBtnW, 44f),
                () => { tab = "tune"; Rebuild(); }, 16);
            y -= 54f;

            // INSPECT is the headline action of this pass, so it gets the full
            // width and says what it costs. A car with things wrong that the
            // player has not found is the only reason the button exists, and it
            // cannot advertise that without giving the answer away — so it
            // advertises the PRICE instead.
            bool openToday = Inspection.OpenToday(S, car);
            MenuKit.Button(body,
                openToday ? "CONTINUE INSPECTION" : "INSPECT CAR  (costs a time slot)",
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(Mathf.Min(ColW, 460f), 50f),
                () =>
                {
                    bool wasOpen = Inspection.OpenToday(S, car);
                    Inspection.Enter(S, car);
                    inspectComp = Inspection.Comp.Engine;
                    inspectLine = null;
                    // Opened from the menu, so FINISH goes back to the menu.
                    inspectCarId = car.id;
                    InspectFromGarage = false;
                    tab = "inspect";
                    LifeSimManager.Save();
                    Rebuild();
                    // Always toasts, even on a resume: Toast is what drains the
                    // pager, and spending the last slot of the day here rolls
                    // the calendar over exactly like going to work does.
                    Toast(wasOpen ? "back under " + Clip(car.displayName, 30)
                                  : "inspecting " + Clip(car.displayName, 30));
                }, 17, new Color(0.16f, 0.30f, 0.34f, 1f));
            y -= 60f;

            MenuKit.Button(body, "TOOLBOX", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(Mathf.Min(ColW, 460f), 42f),
                () => { tab = "toolbox"; Rebuild(); }, 16);
            y -= 52f;
        }

        // =================== inspection ===================
        /// <summary>
        /// Which car the inspection screens are about. Not necessarily the
        /// active one: the walk-in garage lets the player inspect the car they
        /// are STANDING at, and "take the keys to it first" would be a strange
        /// thing to demand of somebody who only wants to look underneath it.
        /// Empty falls back to the active car, which is every menu-side entry.
        /// </summary>
        string inspectCarId = "";
        /// <summary>Set by the garage world on its way out, read once. Same
        /// hand-off contract as <see cref="PendingTab"/>.</summary>
        public static string PendingInspectCar;
        /// <summary>The inspection was opened from inside the walk-in garage,
        /// so FINISH INSPECTION should put the player back where they were
        /// standing rather than in a menu they never opened.</summary>
        public static bool InspectFromGarage;

        OwnedCar InspectTarget
        {
            get
            {
                var c = string.IsNullOrEmpty(inspectCarId) ? null : S.FindCar(inspectCarId);
                return c ?? S.ActiveCar;
            }
        }

        Inspection.Comp inspectComp = Inspection.Comp.Engine;
        /// <summary>Prose from the last sub-check, printed under the diagram.
        /// Held on the screen rather than in a toast because the player is
        /// meant to read it and then decide where to look next.</summary>
        string inspectLine;
        readonly System.Collections.Generic.List<string> inspectFound =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// The X-ray plan view of the car being inspected. The geometry is
        /// <see cref="CarXray"/>, ported from the HTML game — this car's
        /// drivetrain layout, its engine's real cylinder count and arrangement,
        /// and the wheelbase and track of the shell it is actually wearing. The
        /// first version here was a generic front-engined box diagram, which is
        /// a lie about every mid-engined car in a 317-car catalog.
        /// </summary>
        void DrawChassis(RectTransform panel, float panelW, float panelH,
                         Inspection.Comp? highlight, OwnedCar car)
        {
            var spec = car != null ? CarCatalog.Get(car.specId) : null;
            var shell = spec != null ? CarModelLibrary.LoadFor(spec)
                                     : CarModelLibrary.Load(CarModelLibrary.Default);
            CarXray.Draw(panel, panelW, panelH, spec, shell, highlight,
                         car == null ? (System.Func<Inspection.Comp, bool>)null
                                     : c => Inspection.ComponentDone(S, car, c));
        }

        /// <summary>The whole car, with the eight places worth looking.</summary>
        void BuildInspect()
        {
            var car = InspectTarget;
            if (car == null) { tab = "garage"; Rebuild(); return; }
            float y = -16f;

            MenuKit.Label(body, "INSPECT — " + Clip(car.displayName, 34), 22,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 28f, bold: true);
            y -= 34f;

            // What the diagram is showing. The layout code is the single most
            // useful thing on this screen for reading the picture — the block
            // sits behind the cabin on an MR and across the axle on an FF, and
            // a player who does not know which they are looking at will read
            // either as the diagram being wrong.
            var carSpec = CarCatalog.Get(car.specId);
            string layout = carSpec != null && !string.IsNullOrEmpty(carSpec.drv)
                ? carSpec.drv.ToUpperInvariant() : "FR";
            string eng = carSpec != null && !string.IsNullOrEmpty(carSpec.eType) ? carSpec.eType : "";
            MenuKit.Label(body, layout + (eng.Length > 0 ? "   ·   " + eng : ""),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, Color.white, ColW, height: 22f);
            y -= 24f;

            var tools = Toolbox.AccessFor(S, car);
            MenuKit.Label(body,
                "TOOLS: JACK" + (tools.lift ? " · LIFT" : "") + (tools.impact ? " · IMPACT" : "") +
                (tools.scope ? " · SCOPE" : "") + (tools.lamp ? " · LAMP" : "") +
                "   ·   SKILL " + Mathf.RoundToInt(S.mechSkill),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 22f);
            y -= 26f;

            BuildRaiseRow(car, ref y);

            // Say what a clean result MEANS. Most checks find nothing, and
            // without this line a thorough inspection of a sound car reads as a
            // screen that does not work rather than as good news.
            MenuKit.Label(body,
                "A used car carries problems nobody told you about. Better tools and a "
                + "steadier hand find more of them; a clean check is worth having.",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.UpperLeft, MenuKit.Dim, ColW, height: 44f);
            y -= 50f;

            // Sized near the plan-view aspect of a real car (about 2.4:1) so the
            // diagram fills the panel instead of sitting in two black bars.
            float diagW = Mathf.Min(ColW, 460f), diagH = diagW / 2.4f;
            var panel = MenuKit.Rect(body, "Chassis", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(diagW, diagH),
                new Color(0f, 0f, 0f, 0.55f));
            DrawChassis(panel, diagW, diagH, null, car);
            y -= diagH + 12f;

            MenuKit.Label(body, "WHERE DO YOU WANT TO LOOK?", MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                Color.white, ColW, height: 22f);
            y -= 28f;

            float w = Mathf.Min(300f, (ColW - 12f) / 2f);
            for (int i = 0; i < Inspection.Order.Length; i++)
            {
                var c = Inspection.Order[i];
                var captured = c;
                bool done = Inspection.ComponentDone(S, car, c);
                float x = (i % 2 == 0) ? ColL : ColL + w + 12f;
                MenuKit.Button(body, (done ? "* " : "") + Inspection.Name(c),
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, w), y),
                    new Vector2(w, 42f),
                    () =>
                    {
                        inspectComp = captured;
                        inspectLine = Inspection.FloorCheck(S, car);
                        tab = "inspectfocus";
                        LifeSimManager.Save();
                        Rebuild();
                    }, 15, done ? new Color(0.14f, 0.24f, 0.16f, 1f) : (Color?)null);
                if (i % 2 == 1) y -= 50f;
            }
            if (Inspection.Order.Length % 2 == 1) y -= 50f;
            y -= 8f;

            BuildInspectFindings(ref y);

            MenuKit.Button(body, "FINISH INSPECTION", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(Mathf.Min(ColW, 460f), 48f),
                () =>
                {
                    string msg = inspectFound.Count == 0
                        ? "inspection done — nothing new found"
                        : "INSPECTION: " + string.Join(", ", inspectFound.ToArray());
                    inspectFound.Clear();
                    inspectLine = null;
                    tab = "garage";
                    LifeSimManager.Save();
                    // Walked in here from the bays? Then FINISHING is putting
                    // your tools down and standing up, not leaving the
                    // building — go back to where the player was stood.
                    if (InspectFromGarage)
                    {
                        InspectFromGarage = false;
                        SceneManager.LoadScene(TrackCatalog.GarageSceneIndex);
                        return;
                    }
                    Rebuild();
                    Toast(msg);
                }, 17, new Color(0.16f, 0.30f, 0.34f, 1f));
            y -= 58f;
        }

        /// <summary>
        /// Where the car is standing, and the one control that changes it.
        ///
        /// On BOTH inspection screens, because the map is where a player
        /// decides how thorough to be and the component screen is where they
        /// find out they cannot reach something. A control that only existed on
        /// the first would mean backing out of a greyed-out check to press it.
        ///
        /// One button, not a menu of heights: there is no reason to choose jack
        /// stands over a lift you have already bought, so the button raises to
        /// whatever the player owns and the label says which that is.
        /// </summary>
        void BuildRaiseRow(OwnedCar car, ref float y)
        {
            var now = Toolbox.RaiseOf(S, car);
            bool up = now != Toolbox.Raise.Ground;
            var best = Toolbox.BestRaise(S);

            float bw = Mathf.Min(200f, ColW * 0.46f);
            MenuKit.Label(body, "THE CAR IS " + Toolbox.RaiseName(now), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                up ? MenuKit.Good : MenuKit.Dim, ColW - bw - 12f, height: 36f, bold: true);

            MenuKit.Button(body,
                up ? "SET IT DOWN"
                   : best == Toolbox.Raise.Lift ? "PUT IT ON THE LIFT" : "PUT IT ON STANDS",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColRight(ColR, bw), y),
                new Vector2(bw, 36f),
                () =>
                {
                    var to = Toolbox.ToggleRaise(S, car);
                    LifeSimManager.Save();
                    Rebuild();
                    Toast(to == Toolbox.Raise.Ground ? "wheels back on the floor"
                                                     : "car is up " + Toolbox.RaiseName(to).ToLowerInvariant());
                }, 14, up ? (Color?)null : new Color(0.16f, 0.30f, 0.34f, 1f));
            y -= 44f;
        }

        /// <summary>Everything this session has turned up, kept on screen so the
        /// player can see the inspection paying for itself as they go.</summary>
        void BuildInspectFindings(ref float y)
        {
            if (inspectFound.Count == 0) return;
            MenuKit.Label(body, "FOUND SO FAR", MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Bad, ColW,
                height: 22f, bold: true);
            y -= 26f;
            foreach (var f in inspectFound)
            {
                MenuKit.Label(body, "! " + f, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Bad, ColW, height: 22f);
                y -= 24f;
            }
            y -= 8f;
        }

        /// <summary>One component, zoomed: what you can reach, and what you find
        /// when you reach it.</summary>
        void BuildInspectFocus()
        {
            var car = InspectTarget;
            if (car == null) { tab = "garage"; Rebuild(); return; }
            float y = -16f;

            int idx = System.Array.IndexOf(Inspection.Order, inspectComp);
            MenuKit.Label(body, Inspection.Name(inspectComp) + "   " + (idx + 1) + "/8",
                22, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 28f, bold: true);

            // Step through the components without going back to the map — the
            // spec's own switcher, and the difference between an inspection and
            // eight separate errands.
            float navW = 70f;
            MenuKit.Button(body, "<", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColRight(ColR - navW - 10f, navW), y), new Vector2(navW, 32f),
                () => StepComponent(-1), 16);
            MenuKit.Button(body, ">", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColRight(ColR, navW), y), new Vector2(navW, 32f),
                () => StepComponent(1), 16);
            y -= 38f;

            float diagW = Mathf.Min(ColW, 460f), diagH = diagW / 2.6f;
            var panel = MenuKit.Rect(body, "Chassis", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(diagW, diagH),
                new Color(0f, 0f, 0f, 0.55f));
            DrawChassis(panel, diagW, diagH, inspectComp, car);
            y -= diagH + 10f;

            MenuKit.Label(body, Inspection.AccessLine(S, car, inspectComp), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, ColW, height: 22f);
            y -= 28f;

            BuildRaiseRow(car, ref y);

            if (!string.IsNullOrEmpty(inspectLine))
            {
                MenuKit.Label(body, inspectLine, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, Color.white, ColW, height: 22f);
                y -= 30f;
            }

            var tools = Toolbox.AccessFor(S, car);
            float w = Mathf.Min(300f, (ColW - 12f) / 2f);
            var subs = Inspection.SubsOf(inspectComp);
            for (int i = 0; i < subs.Length; i++)
            {
                var sub = subs[i];
                bool reachable = Inspection.Reachable(sub, tools);
                bool done = Inspection.AlreadyChecked(car, inspectComp, sub);
                float x = (i % 2 == 0) ? ColL : ColL + w + 12f;
                var captured = sub;
                MenuKit.Button(body, (done ? "* " : "") + sub.label,
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, w), y),
                    new Vector2(w, 40f),
                    done || !reachable
                        ? (UnityEngine.Events.UnityAction)(() =>
                            Toast(reachable ? "already looked at that today"
                                            : Inspection.RefusalFor(captured, tools)))
                        : () =>
                        {
                            var res = Inspection.Check(S, car, inspectComp, captured);
                            inspectLine = res.line;
                            foreach (var f in res.revealed)
                                if (!inspectFound.Contains(f)) inspectFound.Add(f);
                            LifeSimManager.Save();
                            Rebuild();
                        },
                    14, done ? new Color(0.14f, 0.24f, 0.16f, 1f)
                             : !reachable ? MenuKit.BtnBgDisabled : (Color?)null);
                if (i % 2 == 1) y -= 48f;
            }
            if (subs.Length % 2 == 1) y -= 48f;
            y -= 10f;

            BuildInspectFindings(ref y);

            MenuKit.Button(body, "< BACK TO CAR", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(Mathf.Min(ColW, 460f), 44f),
                () => { tab = "inspect"; Rebuild(); }, 16);
            y -= 54f;
        }

        void StepComponent(int step)
        {
            int i = System.Array.IndexOf(Inspection.Order, inspectComp);
            if (i < 0) i = 0;
            inspectComp = Inspection.Order[
                (i + step + Inspection.Order.Length) % Inspection.Order.Length];
            inspectLine = null;
            Rebuild();
        }

        // =================== toolbox ===================
        void BuildToolbox()
        {
            float y = -16f;
            MenuKit.Label(body, "TOOLBOX", 22, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true);
            y -= 32f;
            MenuKit.Label(body, "Tools decide what an inspection can reach.",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 22f);
            y -= 32f;

            foreach (var t in Toolbox.All)
            {
                bool owned = Toolbox.Owned(S, t.id);
                bool afford = S.money >= t.price;
                var captured = t;
                MenuKit.Button(body,
                    t.name + (owned ? "   — OWNED" : "   " + MenuKit.Money(t.price)),
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, ColW), y),
                    new Vector2(ColW, 40f),
                    owned || !afford
                        ? (UnityEngine.Events.UnityAction)(() =>
                            Toast(owned ? "already in the box" : "need " + MenuKit.Money(captured.price)))
                        : () =>
                        {
                            string err = Toolbox.Buy(S, captured.id);
                            LifeSimManager.Save();
                            Rebuild();
                            Toast(err ?? ("bought " + captured.name));
                        },
                    15, owned ? new Color(0.14f, 0.24f, 0.16f, 1f)
                              : !afford ? MenuKit.BtnBgDisabled : (Color?)null);
                y -= 44f;
                MenuKit.Label(body, t.blurb, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 22f);
                y -= 30f;
            }

            MenuKit.Button(body, "< BACK TO GARAGE", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(Mathf.Min(ColW, 460f), 44f),
                () => { tab = "garage"; Rebuild(); }, 16);
            y -= 54f;
        }

        // =================== debug: the whole catalog ===================
        int debugCarPage;
        /// <summary>"" = every origin. "eur" folds ger/gbr/fra/ita/eur together —
        /// four of the seven origin codes have barely a dozen cars each, and a
        /// filter row with seven buttons costs more than it saves.</summary>
        string debugCarOrigin = "";
        bool debugCarByHp = true;
        const int DebugCarsPerPage = 14;

        static bool OriginMatches(string origin, string filter)
        {
            if (filter.Length == 0) return true;
            if (filter != "eur") return origin == filter;
            return origin == "ger" || origin == "gbr" || origin == "fra" ||
                   origin == "ita" || origin == "eur";
        }

        /// <summary>
        /// Every car in the catalog, grantable for nothing.
        ///
        /// Buying is the normal way in, and buying 317 cars is not a test plan.
        /// Sorted by power by DEFAULT rather than alphabetically: a tester
        /// reaching for this wants the extremes — the 660 cc kei car and the
        /// race V8 — and hp-descending puts one of those on page one instead of
        /// twenty pages apart.
        /// </summary>
        void BuildDebugCars()
        {
            float y = -16f;
            MenuKit.Label(body, "DEBUG — ADD ANY CAR", 20, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, ColW,
                height: 26f, bold: true);
            y -= 34f;

            if (!CarCatalog.Ready)
            {
                MenuKit.Label(body, "Catalog not loaded.", 16, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Bad, ColW);
                return;
            }

            var pool = new System.Collections.Generic.List<CarSpec>();
            foreach (var spec in CarCatalog.All)
                if (OriginMatches(spec.origin, debugCarOrigin)) pool.Add(spec);
            if (debugCarByHp) pool.Sort((a, b) => b.hp.CompareTo(a.hp));
            else pool.Sort((a, b) => string.Compare(a.name, b.name,
                                                    System.StringComparison.OrdinalIgnoreCase));

            int pages = Mathf.Max(1, Mathf.CeilToInt(pool.Count / (float)DebugCarsPerPage));
            debugCarPage = Mathf.Clamp(debugCarPage, 0, pages - 1);

            // --- controls, all ABOVE the list: they are the first things the pad
            // walks onto, and burying paging under fourteen car rows would mean
            // pressing down fourteen times to reach the next page.
            float ctlW = Mathf.Min(150f, (ColW - 24f) / 4f);
            MenuKit.Button(body, "< PREV", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, ctlW), y), new Vector2(ctlW, 38f),
                () => { debugCarPage--; Rebuild(); }, 14);
            MenuKit.Button(body, "NEXT >", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + ctlW + 8f, ctlW), y), new Vector2(ctlW, 38f),
                () => { debugCarPage++; Rebuild(); }, 14);
            MenuKit.Button(body, debugCarByHp ? "SORT: POWER" : "SORT: NAME", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + (ctlW + 8f) * 2f, ctlW + 40f), y),
                new Vector2(ctlW + 40f, 38f),
                () => { debugCarByHp = !debugCarByHp; debugCarPage = 0; Rebuild(); }, 14);
            MenuKit.Button(body, "BACK", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColRight(ColR, ctlW), y), new Vector2(ctlW, 38f),
                () => { tab = "garage"; Rebuild(); }, 14);
            y -= 46f;

            float fW = Mathf.Min(120f, (ColW - 24f) / 4f);
            string[] origins = { "", "jpn", "usa", "eur" };
            string[] originNames = { "ALL", "JPN", "USA", "EUR" };
            for (int i = 0; i < origins.Length; i++)
            {
                string captured = origins[i];
                bool on = debugCarOrigin == captured;
                MenuKit.Button(body, originNames[i], new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + i * (fW + 8f), fW), y),
                    new Vector2(fW, 36f),
                    () => { debugCarOrigin = captured; debugCarPage = 0; Rebuild(); }, 14,
                    on ? new Color(0.42f, 0.34f, 0.10f, 1f) : (Color?)null);
            }
            MenuKit.Label(body, pool.Count + " cars  ·  page " + (debugCarPage + 1) + "/" + pages,
                15, new Vector2(0.5f, 1f), new Vector2(ColR, y - 8f), TextAnchor.MiddleRight,
                MenuKit.Dim, ColW * 0.45f, height: 24f);
            y -= 46f;

            int start = debugCarPage * DebugCarsPerPage;
            int end = Mathf.Min(pool.Count, start + DebugCarsPerPage);
            for (int i = start; i < end; i++)
            {
                var spec = pool[i];
                MenuKit.Button(body,
                    Clip(spec.name, 40) + "   ·   " + spec.hp + " hp · " + spec.drv +
                    " · " + spec.modelYear,
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, ColW), y),
                    new Vector2(ColW, 36f), () => GrantCar(spec), 14);
                y -= 42f;
            }
        }

        /// <summary>Put a catalog car in the garage for nothing and drive it.</summary>
        void GrantCar(CarSpec spec)
        {
            var car = CarMarket.MakeOwnedCar(S, spec, 100, 0f, 0);
            car.fuel = 100f;                  // MakeOwnedCar rolls a used-car tank
            S.activeCar = car.id;
            // Never let the one-slot garage refuse a debug grant. This is the
            // same cap that makes buying a second car impossible in normal play.
            S.garageSlots = Mathf.Max(S.garageSlots, S.cars.Count);
            LifeSimManager.Save();
            tab = "garage";
            Rebuild();
            Toast("added " + spec.name);
        }

        /// <summary>
        /// The parts shop: five categories, four stages each, per car.
        ///
        /// Two prices per row rather than one, because the choice between them
        /// IS the mechanic skill progression — DIY is the parts alone and needs
        /// the skill; the shop is 1.6x and needs nothing. A row you cannot DIY
        /// shows the gate rather than hiding the button, so the ladder is
        /// visible from the first cheap car.
        /// </summary>
        void BuildTune()
        {
            var car = S.ActiveCar;
            if (car == null) { tab = "garage"; Rebuild(); return; }
            var spec = CarCatalog.Get(car.specId);

            float y = -20f;
            MenuKit.Label(body, "PARTS + TUNING — " + car.displayName, 20, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, 820f, bold: true);
            y -= 34f;

            if (spec == null)
            {
                MenuKit.Label(body, "This car has no catalog entry, so it cannot be tuned.", 16,
                    new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    MenuKit.Dim, 820f);
                y -= 40f;
                MenuKit.Button(body, "BACK TO GARAGE", new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL, 280f), y), new Vector2(280f, 44f),
                    () => { tab = "garage"; Rebuild(); }, 16);
                return;
            }

            // The built figures, next to the factory ones. This line is the whole
            // reason to buy anything, so it goes above the shop rather than
            // inside it.
            int effHp = Upgrades.EffectiveHp(car, spec);
            int effKg = Upgrades.EffectiveKg(car, spec);
            string power = effHp == spec.hp ? spec.hp + " hp" : spec.hp + " -> " + effHp + " hp";
            string weight = effKg == spec.kg ? spec.kg + " kg" : spec.kg + " -> " + effKg + " kg";
            MenuKit.Label(body, power + "   ·   " + weight + "   ·   " +
                (effKg > 0 ? (effHp / (float)effKg * 1000f).ToString("0") + " hp/tonne" : ""),
                16, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                Upgrades.IsStock(car) ? MenuKit.Dim : MenuKit.Good, 820f);
            y -= 26f;
            MenuKit.Label(body, "Ceiling for this engine: " + spec.builtHp + " hp at stage 4   ·   " +
                "mech skill " + Mathf.RoundToInt(S.mechSkill), 14,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, 820f);
            y -= 36f;

            for (int i = 0; i <= (int)Upgrades.Kind.Tires; i++)
                DrawUpgradeRow(car, spec, (Upgrades.Kind)i, ref y);

            y -= 6f;
            MenuKit.Label(body, "MODS", 15, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, 300f, bold: true);
            y -= 28f;
            for (int i = 0; i <= (int)Upgrades.Mod.Supercharger; i++)
                DrawModRow(car, spec, (Upgrades.Mod)i, ref y);

            y -= 8f;
            MenuKit.Button(body, "BACK TO GARAGE", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, 280f), y), new Vector2(280f, 44f),
                () => { tab = "garage"; Rebuild(); }, 16);
        }

        /// <summary>A one-off bolt-on: fitted, buyable, or refused with the
        /// reason showing (a turbo car cannot take a blower, and saying so is
        /// more use than hiding the row).</summary>
        void DrawModRow(OwnedCar car, CarSpec spec, Upgrades.Mod mod, ref float y)
        {
            var o = Upgrades.OfferFor(S, car, spec, mod);
            MenuKit.Label(body, (o.owned ? "# " : "- ") + o.name + "   " + o.effect, 15,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                o.owned ? MenuKit.Good : MenuKit.Dim, 560f);

            if (!o.available)
            {
                MenuKit.Label(body, o.blockedReason, 15, new Vector2(0.5f, 1f),
                    new Vector2(ColR, y), TextAnchor.MiddleRight,
                    o.owned ? MenuKit.Good : MenuKit.Dim, 300f);
                y -= 30f;
                return;
            }

            float btnW = Mathf.Min(280f, ColW * 0.42f);
            bool afford = S.money >= o.price;
            bool usable = afford && o.canDiy;
            var capturedMod = mod;
            MenuKit.Button(body,
                o.canDiy ? "FIT — " + MenuKit.Money(o.price) : "needs skill " + o.skillReq,
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColRight(ColR, btnW), y),
                new Vector2(btnW, 38f),
                usable ? (UnityEngine.Events.UnityAction)(() =>
                {
                    string err = Upgrades.OrderMod(S, car, spec, capturedMod);
                    LifeSimManager.Save();
                    Rebuild();
                    Toast(err ?? "fitted");
                }) : null, 14, usable ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 46f;
        }

        void DrawUpgradeRow(OwnedCar car, CarSpec spec, Upgrades.Kind kind, ref float y)
        {
            int stage = Upgrades.GetStage(car, kind);
            var pending = Upgrades.PendingFor(S, car, kind);
            var plan = Upgrades.NextStagePlan(S, car, spec, kind);

            // Stage pips read faster than "STAGE 2 OF 4" at a glance, and the
            // whole screen is five of these stacked.
            string pips = "";
            for (int i = 1; i <= Upgrades.MaxStage; i++) pips += i <= stage ? "#" : "-";

            MenuKit.Label(body, Upgrades.KindLabels[(int)kind] + "  [" + pips + "]  " +
                Upgrades.StageNames[(int)kind][stage], 17, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft,
                stage > 0 ? MenuKit.Good : Color.white, 500f, bold: true);

            if (pending != null)
            {
                int daysLeft = Mathf.Max(0, pending.readyDay - S.day);
                MenuKit.Label(body, "FITTING — ready in " + daysLeft +
                    (daysLeft == 1 ? " day" : " days"), 15, new Vector2(0.5f, 1f),
                    new Vector2(ColR, y), TextAnchor.MiddleRight, MenuKit.Accent, 380f);
                y -= 44f;
                return;
            }
            if (!plan.valid)
            {
                MenuKit.Label(body, "FULLY BUILT", 15, new Vector2(0.5f, 1f),
                    new Vector2(ColR, y), TextAnchor.MiddleRight, MenuKit.Good, 380f);
                y -= 44f;
                return;
            }

            y -= 24f;
            string gain = plan.unit == "kg"
                ? "-" + plan.delta + " kg"
                : "+" + plan.delta + " " + plan.unit;
            MenuKit.Label(body, "  next: " + plan.stageName + "   " + gain +
                "   (" + plan.fromVal + " -> " + plan.toVal + " " + plan.unit + ")   " +
                plan.days + "d", 14, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, 820f);
            y -= 28f;

            float btnW = Mathf.Min(280f, (ColW - 12f) / 2f);
            float x = MenuKit.ColLeft(ColL, btnW);
            for (int venue = 0; venue < 2; venue++)
            {
                bool shop = venue == 1;
                int price = shop ? plan.shopPrice : plan.diyPrice;
                bool afford = S.money >= price;
                // DIY shows the skill it wants rather than a bare dead button —
                // seeing "DIY needs 45" is what tells the player mechanical skill
                // is worth raising.
                bool usable = afford && (shop || plan.canDiy);
                string label = shop
                    ? "SHOP " + MenuKit.Money(price)
                    : (plan.canDiy ? "DIY " + MenuKit.Money(price)
                                   : "DIY needs skill " + plan.skillReq);

                var capturedKind = kind;
                bool capturedShop = shop;
                MenuKit.Button(body, label, new Vector2(0.5f, 1f),
                    new Vector2(x, y), new Vector2(btnW, 40f),
                    usable ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        string err = Upgrades.Order(S, car, spec, capturedKind, capturedShop);
                        LifeSimManager.Save();
                        Rebuild();
                        Toast(err ?? (Upgrades.KindLabels[(int)capturedKind] + " ordered"));
                    }) : null, 14,
                    usable ? (Color?)null : MenuKit.BtnBgDisabled);
                x += btnW + 12f;
            }
            y -= 52f;
        }

        /// <summary>One fault: what it is, what it does on track, and the three
        /// venue quotes side by side. DIY is skill-gated and shows the gate when
        /// it is out of reach, so the player can see the progression rather than
        /// just a dead button.</summary>
        void DrawFaultRow(OwnedCar car, CarFault f, ref float y)
        {
            string fx = FaultCatalog.EffectSummary(f.id);
            MenuKit.Label(body, "> " + f.label + (fx.Length > 0 ? "   (" + fx + ")" : ""), 16,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                f.severity >= 2f ? MenuKit.Bad : MenuKit.Accent, 820f);
            y -= 26f;

            float btnW = Mathf.Min(258f, (ColW - 16f) / 3f);
            float btnStep = btnW + 8f;
            float x = MenuKit.ColLeft(ColL, btnW);
            foreach (FaultCatalog.Venue v in new[] { FaultCatalog.Venue.Diy,
                                                     FaultCatalog.Venue.Mechanic,
                                                     FaultCatalog.Venue.Dealer })
            {
                var q = FaultCatalog.GetQuote(S, car, f, v);
                string name = v == FaultCatalog.Venue.Diy ? "DIY"
                            : v == FaultCatalog.Venue.Mechanic ? "MECH" : "DLR";
                bool booked = S.pendingParts.Exists(p => p.carId == car.id && p.faultId == f.id);
                bool afford = S.money >= q.price;
                bool usable = q.available && afford && !booked;

                string label = q.available
                    ? name + " " + MenuKit.Money(q.price) +
                      (q.days > 0 ? " · " + q.days + "d" : " · now")
                    : name + " sk" + q.difficulty;

                var captured = f;
                var capturedVenue = v;
                MenuKit.Button(body, label, new Vector2(0.5f, 1f),
                    new Vector2(x, y), new Vector2(btnW, 40f),
                    usable ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        string err = LifeRules.OrderRepair(S, car, captured, capturedVenue);
                        LifeSimManager.Save();
                        Rebuild();
                        Toast(err ?? (captured.label + " booked"));
                    }) : null, 14,
                    usable ? (Color?)null : MenuKit.BtnBgDisabled);
                x += btnStep;
            }
            y -= 50f;
        }

        /// <summary>
        /// The classifieds, plus your own garage as a sell list. Listings expire
        /// after a few days, so this is a page worth checking rather than a shop
        /// that is always the same.
        /// </summary>
        void BuildMarket()
        {
            float y = -16f;
            MenuKit.Label(body, "CLASSIFIEDS — " + LifeRules.DateLabel(S.day), 20,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, 600f, bold: true);
            MenuKit.Label(body, "CREDIT " + S.creditScore + " (" +
                    CarMarket.CreditTier(S.creditScore).name + ")   ·   GARAGE " +
                    S.cars.Count + "/" + S.garageSlots,
                15, new Vector2(0.5f, 1f), new Vector2(ColR, y), TextAnchor.MiddleRight,
                MenuKit.Dim, ColW * 0.55f);
            y -= 34f;

            if (S.newspaper.Count == 0)
            {
                MenuKit.Label(body, "Nothing for sale today. Sleep and check again.", 16,
                    new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    MenuKit.Dim, 700f);
                y -= 34f;
            }
            foreach (var listing in S.newspaper)
            {
                var captured = listing;
                int daysLeft = Mathf.Max(0, listing.expiresDay - S.day);
                MenuKit.Button(body, "", new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL, ColW), y),
                    new Vector2(ColW, 54f), () => { buyTarget = captured; tab = "buy"; Rebuild(); }, 14);
                MenuKit.Label(body, (listing.isNew ? "NEW  " : "") + listing.displayName, 16,
                    new Vector2(0.5f, 1f), new Vector2(ColL + 14f, y - 8f), TextAnchor.MiddleLeft,
                    listing.isNew ? MenuKit.Good : Color.white, ColW - 28f).raycastTarget = false;
                MenuKit.Label(body, MenuKit.Money(listing.price) + "  ·  " +
                        listing.odoMiles.ToString("N0") + " mi  ·  cond " + listing.cond +
                        (listing.problem.Length > 0 ? "  ·  " + listing.problem : "") +
                        "  ·  " + daysLeft + "d left",
                    13, new Vector2(0.5f, 1f), new Vector2(ColL + 14f, y - 32f), TextAnchor.MiddleLeft,
                    listing.problem.Length > 0 ? MenuKit.Bad : MenuKit.Dim,
                    ColW - 28f).raycastTarget = false;
                y -= 62f;
            }

            // ---- your garage: switch, advertise, quick-sell ----
            //
            // Continues DOWN the same column rather than sitting in a second one
            // beside the listings. The two-column version needed a canvas wider
            // than a phone has: the listings ran to +104 units and this started
            // at +120, so on a handheld they were one long car name from
            // overlapping. The body scrolls now, so length is free and width is
            // not.
            float sy = y - 24f;
            MenuKit.Label(body, "YOUR GARAGE", 18, new Vector2(0.5f, 1f),
                new Vector2(ColL, sy), TextAnchor.MiddleLeft, MenuKit.Accent, 400f, bold: true);
            sy -= 32f;
            foreach (var car in S.cars)
            {
                var captured = car;
                bool active = car.id == S.activeCar;
                var ad = S.carAds.Find(a => a.carId == car.id);
                MenuKit.Label(body, (active ? "> " : "  ") + car.displayName, 15,
                    new Vector2(0.5f, 1f), new Vector2(ColL, sy), TextAnchor.MiddleLeft,
                    active ? MenuKit.Accent : Color.white, 500f);
                sy -= 22f;
                MenuKit.Label(body, "worth " + MenuKit.Money(CarMarket.CarValue(car)) +
                        (CarMarket.LoanPayoff(S, car.id) > 0
                            ? "  ·  owes " + MenuKit.Money(CarMarket.LoanPayoff(S, car.id)) : ""),
                    13, new Vector2(0.5f, 1f), new Vector2(ColL, sy), TextAnchor.MiddleLeft,
                    MenuKit.Dim, 500f);
                sy -= 26f;

                if (!active)
                {
                    MenuKit.Button(body, "DRIVE", new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(ColL, 120f), sy),
                        new Vector2(120f, 34f), () =>
                        {
                            S.activeCar = captured.id;
                            LifeSimManager.Save(); Rebuild();
                            Toast("now driving " + captured.displayName);
                        }, 13);
                }
                if (ad != null && ad.offerAmount > 0)
                {
                    MenuKit.Button(body, "ACCEPT " + MenuKit.Money(ad.offerAmount),
                        new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL + 132f, 190f), sy), new Vector2(190f, 34f),
                        () =>
                        {
                            string err = CarMarket.AcceptOffer(S, ad);
                            LifeSimManager.Save(); Rebuild();
                            Toast(err ?? "sold");
                        }, 13, new Color(0.4f, 1f, 0.5f, 0.22f));
                }
                else if (ad != null)
                {
                    MenuKit.Label(body, "listed " + MenuKit.Money(ad.askPrice) +
                            " — " + ad.daysListed + "d, no offer yet",
                        13, new Vector2(0.5f, 1f), new Vector2(ColL + 132f, sy - 8f),
                        TextAnchor.MiddleLeft, MenuKit.Dim, 320f);
                }
                else if (S.cars.Count > 1)
                {
                    MenuKit.Button(body, "ADVERTISE", new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(ColL + 132f, 150f), sy), new Vector2(150f, 34f), () =>
                        {
                            string err = CarMarket.ListForSale(S, captured);
                            LifeSimManager.Save(); Rebuild();
                            Toast(err ?? "listed for sale");
                        }, 13);
                    MenuKit.Button(body, "QUICK-SELL 50%", new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(ColL + 294f, 180f), sy), new Vector2(180f, 34f), () =>
                        {
                            string err = CarMarket.QuickSell(S, captured);
                            LifeSimManager.Save(); Rebuild();
                            Toast(err ?? "sold");
                        }, 13);
                }
                sy -= 46f;
            }
        }

        /// <summary>Listing detail: the spec sheet and the ways to pay for it.</summary>
        void BuildBuyDetail()
        {
            if (buyTarget == null) { tab = "market"; Rebuild(); return; }
            var listing = buyTarget;
            var spec = CarCatalog.Get(listing.specId);
            float y = -20f;

            MenuKit.Label(body, listing.displayName, 24, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, 820f, bold: true);
            y -= 40f;
            // Shorter than the garage's: the buy page is a wall of numbers and
            // the car is one of them, not the whole page.
            DrawCarView(spec, ref y, 160f);
            if (spec != null)
            {
                MenuKit.Label(body, spec.hp + " hp  ·  " + spec.kg + " kg  ·  " + spec.drv +
                        "  ·  " + spec.gears + "-speed  ·  " + spec.modelYear +
                        "  ·  " + Mathf.RoundToInt(spec.topSpeedMps * 3.6f) + " km/h",
                    16, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    Color.white, 820f);
                y -= 26f;
                // The engine line. Every car in here wears the same body, so
                // what it IS mechanically — layout, boost, how far it can be
                // taken — is the only thing distinguishing one listing from the
                // next, and it is also what the buyer is actually shopping for.
                string boost = spec.IsTurbo ? "turbo" : spec.IsSupercharged ? "supercharged"
                                                                            : "naturally aspirated";
                string engine = string.IsNullOrEmpty(spec.eType) ? "engine" : spec.eType;
                MenuKit.Label(body, engine + (spec.dispCc > 0 ? "  ·  " + spec.dispCc + "cc" : "") +
                        "  ·  " + boost + "  ·  builds to " + spec.builtHp + " hp",
                    15, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    MenuKit.Dim, 820f);
                y -= 30f;
            }
            MenuKit.Label(body, listing.odoMiles.ToString("N0") + " miles  ·  condition " +
                    listing.cond + "  ·  asking " + MenuKit.Money(listing.price),
                16, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, 820f);
            y -= 30f;
            if (listing.problem.Length > 0)
            {
                MenuKit.Label(body, "! SELLER DISCLOSES: " + listing.problem +
                        " — priced 45% under book, and you will be fixing it.",
                    15, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    MenuKit.Bad, 820f);
                y -= 30f;
            }
            y -= 12f;

            foreach (var opt in CarMarket.FinanceOptions(S, listing))
            {
                var captured = opt;
                bool afford = S.money >= opt.downPayment && S.cars.Count < S.garageSlots;
                string label = opt.isCash
                    ? "CASH — " + MenuKit.Money(opt.downPayment)
                    : opt.label + " — " + MenuKit.Money(opt.downPayment) + " down, " +
                      MenuKit.Money(opt.monthlyPayment) + "/mo";
                MenuKit.Button(body, label, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL, Mathf.Min(660f, ColW)), y),
                    new Vector2(Mathf.Min(660f, ColW), 48f),
                    afford ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        string err = CarMarket.Buy(S, listing, captured);
                        LifeSimManager.Save();
                        if (err == null) { buyTarget = null; tab = "market"; }
                        Rebuild();
                        Toast(err ?? ("bought the " + listing.displayName));
                    }) : null, 16, afford ? (Color?)null : MenuKit.BtnBgDisabled);
                y -= 58f;
            }

            if (S.cars.Count >= S.garageSlots)
            {
                MenuKit.Label(body, "Garage is full (" + S.garageSlots +
                        " slot" + (S.garageSlots == 1 ? "" : "s") + "). Sell something first.",
                    15, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    MenuKit.Bad, 820f);
                y -= 30f;
            }

            MenuKit.Button(body, "BACK", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, 200f), y - 8f),
                new Vector2(200f, 44f), () => { buyTarget = null; tab = "market"; Rebuild(); }, 16);
        }

        /// <summary>The flat-rate counterpart to fault repair: no skill gate, no
        /// waiting, and it clears the fault lane it touches.</summary>
        void BuildService()
        {
            var car = S.ActiveCar;
            if (car == null) return;
            float y = -20f;
            MenuKit.Label(body, "MECHANIC — " + car.displayName, 20, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, 800f, bold: true);
            y -= 40f;

            for (int i = 0; i < LifeRules.MechanicServices.Length; i++)
            {
                var svc = LifeRules.MechanicServices[i];
                int price = LifeRules.ServiceCost(car, svc.cost);
                bool afford = S.money >= price;
                int idx = i;
                MenuKit.Button(body, svc.name + " — " + MenuKit.Money(price) +
                        "  (+" + svc.add + " " + svc.stat + ")",
                    new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, Mathf.Min(560f, ColW)), y), new Vector2(Mathf.Min(560f, ColW), 44f),
                    afford ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        string err = LifeRules.BuyService(S, car, idx);
                        LifeSimManager.Save(); Rebuild();
                        Toast(err ?? (LifeRules.MechanicServices[idx].name + " done"));
                    }) : null, 15, afford ? (Color?)null : MenuKit.BtnBgDisabled);
                y -= 52f;
            }

            MenuKit.Button(body, "BACK TO GARAGE", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, 280f), y - 6f), new Vector2(280f, 44f),
                () => { tab = "garage"; Rebuild(); }, 16);
        }

        void DrawBar(string label, float value, ref float y)
        {
            MenuKit.Label(body, label, 14, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, 170f);
            var track = MenuKit.Rect(body, "bar", new Vector2(0.5f, 1f),
                new Vector2(0f, 1f), new Vector2(-220f, y - 4f),
                new Vector2(430f, 16f), new Color(0f, 0f, 0f, 0.5f));
            Color c = value > 60f ? MenuKit.Good : value > 30f ? MenuKit.Accent : MenuKit.Bad;
            MenuKit.Rect(track, "fill", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0f), new Vector2(428f * Mathf.Clamp01(value / 100f), 14f), c);
            MenuKit.Label(body, Mathf.RoundToInt(value) + "%", 14, new Vector2(0.5f, 1f),
                new Vector2(226f, y), TextAnchor.MiddleLeft, c, 80f);
            y -= 38f;
        }

        void BuildEat()
        {
            float y = -20f;
            MenuKit.Label(body, "FOOD STOCK: " + S.foodStock + " meals" +
                (S.ateToday ? "   ·   eaten today: yes" : "   ·   NOT EATEN TODAY"),
                18, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                S.ateToday ? MenuKit.Good : MenuKit.Bad, 620f);
            y -= 52f;

            bool canEat = S.foodStock > 0 && !S.ateToday;
            MenuKit.Button(body, S.ateToday ? "ALREADY ATE TODAY" : "EAT A MEAL",
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 56f),
                canEat ? (UnityEngine.Events.UnityAction)(() =>
                {
                    Toast(LifeRules.EatMeal(S, S.lastMealTier == "" ? "regular" : S.lastMealTier));
                    LifeSimManager.Save(); Rebuild();
                }) : null, 18, canEat ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 84f;

            MenuKit.Label(body, "BUY GROCERIES", 15, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, 400f);
            y -= 30f;
            MenuKit.Label(body, "Also sold out in the world: the 6TWELVE at the pumps, " +
                "STACK BURGER drive-thrus and SLICE HOUSE pizzerias around Charlotte " +
                "and Emerald Isle.", MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 40f);
            y -= 44f;
            foreach (var g in LifeRules.Groceries)
            {
                var captured = g;
                bool afford = S.money >= g.cost;
                MenuKit.Button(body,
                    g.tier.ToUpper() + "  $" + g.cost + " → " + g.meals + " meals",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 50f),
                    afford ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        S.money -= captured.cost;
                        S.foodStock += captured.meals;
                        S.lastMealTier = captured.tier;
                        LifeSimManager.Save(); Rebuild();
                    }) : null, 17, afford ? (Color?)null : MenuKit.BtnBgDisabled);
                y -= 62f;
            }
        }

        // =================== the ladder (L4) ===================
        /// <summary>Cached so the taunt does not reroll on every Rebuild — a
        /// rival whose line changes each time you glance at the board reads as
        /// noise rather than as a person.</summary>
        string tauntLine;
        int tauntRank;

        // Column geometry as FRACTIONS of the available half-width, resolved at
        // build time against this screen.
        //
        // Two traps stacked here. MenuKit centres a label's rect on the x it is
        // given (pivot == anchor), so a left-aligned column at margin L belongs
        // at L + width/2 — passing L directly hangs half the rect off the left
        // edge, which is what the first cut of this board did. And absolute
        // offsets are only ever right for one canvas: the design column is 720
        // units tall on a desktop and 560 on a handheld, and the width follows
        // the height, so a column hard-coded at -460 fits a monitor and runs off
        // a phone. Fractions of the real half-width survive both.
        const float RivalMargin = 0.96f;    // keep off the very edge
        /// <summary>Characters of car name a row shows. 22B-STi Impreza spells
        /// itself out over sixty characters and would run straight through the
        /// gate column, and Text has no ellipsis mode to lean on.</summary>
        const int RivalCarChars = 30;

        void BuildRivals()
        {
            float y = -14f;
            var open = Blacklist.OpenRival(S);
            var next = Blacklist.NextRival(S);

            // Resolve the columns against THIS screen. Widths are shares of the
            // usable span; each x is the left margin plus half the width,
            // because MenuKit centres the rect on the x it is handed.
            // Labels now take the EDGE their text starts from, so these are the
            // column boundaries directly. Buttons still take a centre.
            float half = ColR;
            float span = ColW;
            float aliasW = span * 0.20f, carW = span * 0.38f;
            float statW = span * 0.16f, btnW = span * 0.22f;
            float colAlias = ColL;
            float colCar = ColL + aliasW;
            float colStat = ColL + aliasW + carW + statW;      // right-aligned edge
            float colBtn = MenuKit.ColRight(ColR, btnW);       // a button: centre
            float rowH = 46f;

            MenuKit.Label(body, "BLACKLIST", MenuKit.Small, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, aliasW * 1.6f, bold: true);
            MenuKit.Label(body, "WINS " + S.streetRacesWon + "  ·  REP " +
                    Mathf.RoundToInt(S.streetRep) + "  ·  " + S.blDefeated.Count + "/10 TAKEN",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColR, y),
                TextAnchor.MiddleRight, MenuKit.Dim, span * 0.55f);
            y -= 30f;

            // The line under the header is the whole reason the board has names
            // on it: a locked rung says what it wants, an open one talks back.
            string blurb;
            if (open != null)
            {
                if (tauntRank != open.rank || tauntLine == null)
                {
                    tauntRank = open.rank;
                    var pc = S.ActiveCar;
                    // The taunt names the player's car, and catalog names carry a
                    // chassis code, a market and a year. "Mazda RX-7" is what
                    // someone would actually sneer at you across a car park.
                    tauntLine = Blacklist.Taunt(open, ShortCarName(pc));
                }
                blurb = open.alias + ": \"" + tauntLine + "\"";
            }
            else if (next != null)
            {
                blurb = "#" + next.rank + " " + next.alias + " won't see you yet — need " +
                        next.gateWins + " wins and " + next.gateRep + " rep.";
            }
            else blurb = "The whole board is yours.";
            MenuKit.Label(body, blurb, MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft,
                open != null ? MenuKit.Accent : MenuKit.Dim, span);
            y -= 30f;

            // Pre-race gates. RacedToday deliberately is NOT one of them: a
            // challenge does not spend the daily purse race.
            var car = S.ActiveCar;
            // Same gate as the main screen, and for the same reason: enough to
            // REACH the forecourt, not enough to finish without one.
            bool lowFuel = car == null ||
                           car.fuel <= LifeRules.RequiredFuelPct(TrackCatalog.At(S.trackIndex), car);

            // Boss at the top, the way a wanted list reads.
            for (int rank = 1; rank <= 10; rank++)
            {
                var r = Blacklist.ByRank(rank);
                var status = Blacklist.StatusOf(S, r);
                bool isOpen = status == RivalStatus.Open;
                Color tone = status == RivalStatus.Beaten ? MenuKit.Good
                           : isOpen ? MenuKit.Accent : MenuKit.Dim;

                MenuKit.Label(body, "#" + rank + " " + r.alias, MenuKit.Tiny,
                    new Vector2(0.5f, 1f), new Vector2(colAlias, y), TextAnchor.MiddleLeft,
                    tone, aliasW, bold: isOpen);
                MenuKit.Label(body, Clip(Blacklist.CarName(r), RivalCarChars), MenuKit.Tiny,
                    new Vector2(0.5f, 1f), new Vector2(colCar, y), TextAnchor.MiddleLeft,
                    status == RivalStatus.Locked ? MenuKit.Dim : Color.white, carW);

                string right = status == RivalStatus.Beaten ? "DEFEATED"
                    : isOpen ? MenuKit.Money(Blacklist.Purse(rank))
                    : r.gateWins + "W / " + r.gateRep + "REP";
                MenuKit.Label(body, right, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(colStat, y), TextAnchor.MiddleRight, tone, statW);

                if (isOpen)
                {
                    var captured = r;
                    MenuKit.Button(body, lowFuel ? "LOW FUEL" : "CHALLENGE",
                        new Vector2(0.5f, 1f), new Vector2(colBtn, y),
                        new Vector2(btnW, rowH * 0.82f),
                        lowFuel ? (UnityEngine.Events.UnityAction)null
                                : () => StartRace(false, captured),
                        MenuKit.Tiny, lowFuel ? MenuKit.BtnBgDisabled
                                    : (Color?)new Color(1f, 0.84f, 0.4f, 0.28f));
                }
                y -= rowH;
            }
        }

        /// <summary>Truncate to fit a column. UGUI Text has no ellipsis mode —
        /// horizontalOverflow is either clip or run over the neighbour — so the
        /// string has to arrive the right length.</summary>
        static string Clip(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max
                ? s : s.Substring(0, max - 1).TrimEnd() + "…";

        /// <summary>"Mazda RX-7 Type RS (FD) '98" → "Mazda RX-7". Catalog names
        /// carry chassis code, market and year; a spoken line wants the badge.
        /// </summary>
        static string ShortCarName(OwnedCar car)
        {
            if (car == null || string.IsNullOrEmpty(car.displayName)) return null;
            var parts = car.displayName.Split(' ');
            return parts.Length <= 2 ? car.displayName : parts[0] + " " + parts[1];
        }

        void BuildBills()
        {
            float y = -20f;
            int housing = LifeRules.MonthlyHousing(S);
            int insurance = LifeRules.MonthlyInsurance(S);
            int loans = LifeRules.MonthlyLoanPayments(S);
            int due = housing + insurance + loans;
            int daysLeft = LifeRules.DaysPerMonth - LifeRules.DayOfMonth(S.day) + 1;

            Row(LifeRules.HousingLabel(S.housingType), MenuKit.Money(housing), ref y);
            Row("INSURANCE", MenuKit.Money(insurance), ref y);
            if (loans > 0) Row("LOAN PAYMENTS", MenuKit.Money(loans), ref y);
            y -= 12f;
            Row("DUE ON THE 1st (" + daysLeft + " days)", MenuKit.Money(due), ref y, MenuKit.Accent);
            y -= 24f;
            Row("CREDIT SCORE", S.creditScore.ToString(), ref y,
                S.creditScore >= 660 ? MenuKit.Good : S.creditScore >= 550 ? MenuKit.Accent : MenuKit.Bad);
            if (S.missedPayments > 0)
                Row("MISSED PAYMENTS", S.missedPayments.ToString(), ref y, MenuKit.Bad);
            // The insurer is already watching, even though the premium does not
            // move on it yet (that multiplier lands with the L5 record pass).
            if (S.atFaultIncidents > 0)
                Row("AT-FAULT INCIDENTS", S.atFaultIncidents.ToString(), ref y, MenuKit.Bad);
            if (S.pendingSalary > 0)
                Row("PAY PENDING (Friday)", MenuKit.Money(S.pendingSalary), ref y, MenuKit.Good);
        }

        void Row(string label, string value, ref float y, Color? valueColor = null)
        {
            MenuKit.Label(body, label, 17, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, 480f);
            MenuKit.Label(body, value, 17, new Vector2(0.5f, 1f),
                new Vector2(160f, y), TextAnchor.MiddleRight, valueColor ?? Color.white, 200f, bold: true);
            y -= 38f;
        }

        void BuildJobs()
        {
            float y = -20f;
            if (!string.IsNullOrEmpty(S.playerJob))
            {
                Row("CURRENT JOB", S.playerJob, ref y, MenuKit.Accent);
                Row("DAILY PAY", MenuKit.Money(Mathf.RoundToInt(S.basePay * S.payMultiplier)), ref y);
                Row("WORK REP", Mathf.RoundToInt(S.workRep) + "/100", ref y,
                    S.workRep >= 50 ? MenuKit.Good : MenuKit.Bad);
                Row("ATTENDANCE", S.workDaysPresent + "/" + S.workDaysTotal, ref y);
                y -= 16f;
                MenuKit.Label(body, "Miss weekday shifts and the rep ladder bites: −5, then −15, then −30 and fired.",
                    14, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, 760f);
                return;
            }

            MenuKit.Label(body, (S.fired ? "You were FIRED. " : "") + "Apply for work (55% hire odds per try):",
                17, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                S.fired ? MenuKit.Bad : Color.white, 740f);
            y -= 44f;
            foreach (var (name, dailyPay, _, _) in LifeRules.Jobs)
            {
                string jn = name; int jp = dailyPay;
                MenuKit.Button(body, name + "  —  $" + dailyPay + "/day",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 46f), () =>
                    {
                        if (Random.value < LifeRules.ApplyHireChance)
                        {
                            S.playerJob = jn; S.basePay = jp;
                            S.workRep = LifeRules.NewHireWorkRep;
                            S.consecutiveAbsences = 0; S.fired = false;
                            S.calendarLog.Add("Day " + S.day + ": hired — " + jn);
                            Toast("HIRED: " + jn);
                        }
                        else Toast("No luck at " + jn + ". Try again.");
                        LifeSimManager.Save(); Rebuild();
                    }, 16);
                y -= 56f;
            }
        }

        // =================== actions ===================
        void StartRace(bool practice = false, BlacklistRival rival = null)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = S.ActiveCar != null ? S.ActiveCar.specId : "";
            RaceHandoff.TrackIndex = Mathf.Clamp(S.trackIndex, 0, TrackCatalog.Count - 1);
            RaceHandoff.TimeOfDayIndex = RaceHour();
            RaceHandoff.IsPractice = practice;
            // The tank the car is actually carrying. It burns down in real time
            // out there now, and it can be topped up at the forecourt, so this
            // is a starting balance rather than a budget.
            RaceHandoff.StartFuelPct = S.ActiveCar != null ? S.ActiveCar.fuel : 100f;

            // The purse travels WITH the race so the HUD can show what is at
            // stake and the apply-back pays from one number instead of
            // recomputing the tier on return.
            int tier = LifeRules.StreetTier(S.streetRep).idx;
            RaceHandoff.PurseWin = practice ? 0 : LifeRules.WinPrize[tier];

            // Who lines up. A challenge is 1v1 against the named car; anything
            // else is a catalog field picked around what the player is driving.
            // Both can decline — an unresolvable rival car or a catalog too thin
            // at this price leaves the scene's built-in RX-7 field standing,
            // which is a worse race but never a broken one.
            if (rival != null && LifeRules.FillRivalField(rival))
            {
                RaceHandoff.RivalRank = rival.rank;
                RaceHandoff.RivalAlias = rival.alias;
                RaceHandoff.PurseWin = Blacklist.Purse(rival.rank);
            }
            else
            {
                LifeRules.FillOpponentField(S);
            }

            FillCarRequest();

            LifeSimManager.Save();
            SceneManager.LoadScene(TrackCatalog.SceneIndex(RaceHandoff.TrackIndex));
        }

        /// <summary>
        /// The half of the handoff that describes THE CAR — parts, faults, and
        /// getting it off the stands — shared by races and free roam, because
        /// a blown head gasket does not care which of the two you left in.
        /// </summary>
        void FillCarRequest()
        {
            // Parts the car is carrying become the advantage it races with.
            var tuned = S.ActiveCar;
            if (tuned != null)
            {
                RaceHandoff.UpPower = tuned.upPower;
                RaceHandoff.UpWeight = tuned.upWeight;
                RaceHandoff.UpBrakes = tuned.upBrakes;
                RaceHandoff.UpSuspension = tuned.upSuspension;
                RaceHandoff.UpTires = tuned.upTires;
                RaceHandoff.Welded = tuned.welded;
                RaceHandoff.Supercharged = tuned.supercharged;
            }

            // Faults the car is carrying become the handicap it races under.
            var agg = FaultCatalog.Aggregate_(S.ActiveCar);
            RaceHandoff.AccelMult = agg.accelMult;
            RaceHandoff.GripMult = agg.gripMult;
            RaceHandoff.BrakeMult = agg.brakeMult;
            RaceHandoff.SteerPull = agg.steerPull;
            RaceHandoff.ShiftMult = agg.shiftMult;
            RaceHandoff.FuelMult = agg.fuelMult;
            RaceHandoff.HideGauges = agg.hideGauges;
            RaceHandoff.RpmFlutter = agg.rpmFlutter;

            // Nobody drives away with the car still up on the stands. The
            // garage would happily draw it hovering over four of them for the
            // rest of the career otherwise, since raising it is a state and not
            // an errand.
            Toolbox.SetRaise(S, S.ActiveCar, Toolbox.Raise.Ground);
        }

        /// <summary>
        /// Out into Charlotte. Same car contract as a race, none of the race:
        /// no purse, no field, no lap count — the exit stamps the session via
        /// CityMode and the apply-back banks metres, fuel and wear.
        /// </summary>
        void StartFreeRoam()
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = S.ActiveCar != null ? S.ActiveCar.specId : "";
            RaceHandoff.TrackIndex = TrackCatalog.IndexOf("Charlotte");
            RaceHandoff.TimeOfDayIndex = RaceHour();
            RaceHandoff.StartFuelPct = S.ActiveCar != null ? S.ActiveCar.fuel : 100f;

            FillCarRequest();

            LifeSimManager.Save();
            SceneManager.LoadScene(TrackCatalog.SceneIndex(RaceHandoff.TrackIndex));
        }

        void DoWork()
        {
            string msg = LifeRules.WorkOneDay(S);
            LifeRules.SpendActivitySlot(S);
            LifeSimManager.Save();
            Rebuild();
            Toast(msg);
        }

        void DoSleep()
        {
            LifeRules.Sleep(S);
            LifeSimManager.Save();
            tab = "main";
            Rebuild();
            Toast(LifeRules.DateLabel(S.day).ToUpper());
        }

        // =================== toast ===================
        Text statusText;
        void Toast(string msg)
        {
            // The pager rides out on whatever toast follows the rollover that
            // fired it. Every path that can roll the day — sleep, work, an
            // all-nighter, the race apply-back — ends in exactly one Toast, so
            // draining it here catches all of them instead of four call sites
            // each remembering to ask.
            if (!string.IsNullOrEmpty(LifeRules.lastPage))
            {
                msg = "PAGER — " + LifeRules.lastPage + "  ·  " + msg;
                LifeRules.lastPage = null;
            }
            if (statusText != null) Destroy(statusText.transform.parent.gameObject);
            var box = MenuKit.Rect(canvas.transform, "Toast",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(780f, 52f), new Color(0f, 0f, 0f, 0.78f));
            statusText = MenuKit.Label(box, msg, 18, new Vector2(0.5f, 0.5f),
                Vector2.zero, TextAnchor.MiddleCenter, MenuKit.Accent, 760f, bold: true);
            statusText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
