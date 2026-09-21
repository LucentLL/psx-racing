using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// The debug bench's page: every fault in the catalog as a switch, every
    /// upgrade ladder as five buttons, every bolt-on as a switch — opened from
    /// the pause menu mid-race or in free roam, and from a car's page in the
    /// garage. The rules are <see cref="DebugCarOps"/>; this is only the glass.
    ///
    /// Built on <see cref="OnFoot.MeetScreen"/>'s frame, which is StoreScreen's:
    /// its own MenuKit overlay canvas, Escape / B to leave, the pad wired the
    /// moment the page exists, an onClosed that hands control back to whoever
    /// opened it. Two things differ, and both are because this page is PRESSED
    /// REPEATEDLY rather than read once:
    ///
    ///   * EVERY PRESS REBUILDS THE PAGE, and a rebuild must not move the
    ///     player. Forty faults is a scrolling list, and a list that jumps to
    ///     the top each time a switch is thrown cannot be used — so the scroll
    ///     offset and the pad cursor are both carried across (the cursor by a
    ///     stable NAME, since a switch's caption changes when it is thrown and
    ///     MenuKit names a button after its caption).
    ///   * IT RUNS WITH THE CLOCK STOPPED. The pause menu holds timeScale at
    ///     zero underneath it, so nothing here may wait on scaled time. Nothing
    ///     does: UGUI, ScrollRect inertia and MenuNavWatch are all unscaled.
    ///
    /// Width is what these menus have and height is what they do not (the
    /// viewport is ~360 units on a phone), so both lists go to COLUMNS — as
    /// many as fit at a readable cell width — rather than to smaller type.
    /// </summary>
    public class DebugCarPanel : MonoBehaviour
    {
        /// <summary>Called when the page closes, so the opener can take the
        /// cursor back.</summary>
        public System.Action onClosed;

        /// <summary>The car to work on. Null means "the one being driven",
        /// resolved through <see cref="DebugCarOps.TargetCar"/> — which is
        /// what the pause menu wants. The garage names a car outright, because
        /// the page it opens from may be about a car nobody has the keys to.
        /// </summary>
        public OwnedCar target;

        public bool IsOpen { get; private set; }

        /// <summary>The frame the page last closed on. The pause menu reads
        /// it: B closes this page AND is the pause menu's own back key, and
        /// two Updates have no defined order between them — without this the
        /// press that backs out of the bench also resumes the race whenever
        /// this component happens to run first.</summary>
        public int ClosedFrame { get; private set; } = -1;

        public enum Page { Faults, Parts, World, Car }
        Page page = Page.Faults;

        /// <summary>
        /// Draw the WORLD and CAR pages even with no drive under the page.
        /// They exist only mid-drive — the garage has no sky to change and no
        /// car to climb out of — which also means the preview tool, which
        /// builds this page in edit mode with no scene behind it, would never
        /// photograph them. It sets this; nothing else does.
        /// </summary>
        public bool forceDrivePages;

        bool DrivePages => forceDrivePages || DebugCarOps.LiveApplier() != null;

        /// <summary>The make the CAR page has open, or null for the index of
        /// makes. Kept across close and reopen with the rest of the position:
        /// comparing three Skylines is three trips through this page.</summary>
        string carMake;

        /// <summary>Open the CAR page inside one make's list (null for the
        /// index). For the preview tool, which cannot press the make's button.</summary>
        public void PreviewMake(string make) => carMake = make;

        static readonly string[] TabKeys = { "tab_faults", "tab_parts", "tab_world", "tab_car" };

        /// <summary>
        /// Where the tester was when the page last closed: which page (kept in
        /// <see cref="page"/>), how far down it, and on which control.
        ///
        /// The bench is used in a LOOP — switch a fault on, resume, drive a
        /// corner, pause, switch it off, resume — and a page that reopened at
        /// the top of the list with the cursor on BACK would make every lap of
        /// that loop a walk back down forty rows to the same switch.
        /// </summary>
        float lastScroll;
        string lastFocus;

        /// <summary>Something on the car changed while the page was up, so the
        /// save is owed a write when it closes. ONE write, on the way out: a
        /// PlayerPrefs save on the web is the whole career serialised and
        /// pushed at IndexedDB, and a tester walking a ladder 0-1-2-3-4 should
        /// not pay for that five times with the game visibly paused behind
        /// it. Nothing is lost in between — the state is a static, and RESTART
        /// RACE reloads the scene, not the career.</summary>
        bool dirty;

        /// <summary>
        /// The OTHER menus' selection watchdogs, stood down while this page is
        /// up.
        ///
        /// A <see cref="MenuNavWatch"/> restores a lost selection to its own
        /// fallback the moment the pad is touched. The pause menu has one and
        /// it is still running under this page — so after a tap on empty
        /// space cleared the cursor, two watchdogs raced to restore it, and
        /// whenever the pause menu's won the pad was driving RESUME and
        /// RESTART RACE, unseen, underneath the bench. They are found rather
        /// than handed over so that an opener nobody has written yet is
        /// covered too, and only the ones that were RUNNING are woken again.
        /// </summary>
        readonly List<MenuNavWatch> stoodDown = new List<MenuNavWatch>();

        Canvas canvas;
        RectTransform root;
        RectTransform content;
        /// <summary>One line of feedback about the last press, for the things
        /// a switch cannot say by changing colour: a refusal, or the plate
        /// pack that came out because a weld went in.</summary>
        string note;

        static readonly Color DebugPurple = new Color(0.26f, 0.14f, 0.34f, 1f);
        const string DimTag = "<color=#adadad>";

        static LifeState S => LifeSimManager.State;

        OwnedCar Car => target ?? DebugCarOps.TargetCar(S);
        static CarSpec SpecOf(OwnedCar car) => car != null ? CarCatalog.Get(car.specId) : null;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            note = null;
            // The pointer belongs to the page while the page is up — the town
            // locks it for the walker.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "DebugBenchCanvas", 210);   // over the pause menu's 200
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.08f, 0.08f, 0.08f, 0.94f));
            MenuKit.GridBackdrop(canvas.transform);

            stoodDown.Clear();
            foreach (var w in FindObjectsByType<MenuNavWatch>(FindObjectsInactive.Exclude))
                if (w != null && w.enabled) { w.enabled = false; stoodDown.Add(w); }

            Build(lastFocus, lastScroll);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            var sel = MenuNav.Selected(root);
            lastFocus = sel != null ? sel.name : null;
            lastScroll = content != null ? content.anchoredPosition.y : 0f;
            foreach (var w in stoodDown) if (w != null) w.enabled = true;
            stoodDown.Clear();
            if (dirty) { LifeSimManager.Save(); dirty = false; }
            if (canvas != null) Drop(canvas.gameObject);
            canvas = null; root = null; content = null;
            onClosed?.Invoke();
        }

        void OnDisable() { if (IsOpen) Close(); }

        void Update()
        {
            if (!IsOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) ||
                (pad != null && (pad.buttonEast.wasPressedThisFrame ||
                                 pad.startButton.wasPressedThisFrame)))
            { Close(); return; }

            // The shoulders turn the page, as they do on every tabbed screen
            // a pad has ever driven.
            if (pad != null && (pad.leftShoulder.wasPressedThisFrame ||
                                pad.rightShoulder.wasPressedThisFrame))
            {
                int pages = DrivePages ? 4 : 2;
                int step = pad.rightShoulder.wasPressedThisFrame ? 1 : pages - 1;
                Show((Page)(((int)page + step) % pages));
            }
        }

        // ------------------------------------------------------------------
        //  Rebuild, without moving the player
        // ------------------------------------------------------------------

        /// <summary>Turn to a page. Public for the preview tool and the
        /// self-test, which need the second page without pressing anything;
        /// on a closed bench it only chooses what the next Open shows.</summary>
        public void Show(Page p)
        {
            if (p == page) return;
            page = p;
            note = null;
            lastFocus = null;
            lastScroll = 0f;
            // A different page: position means nothing on it, so the scroll
            // goes back to the top and the cursor to the tab just pressed.
            Rebuild("dbg_" + TabKeys[(int)p], keepScroll: false);
        }

        void Rebuild(string focus = null, bool keepScroll = true)
        {
            if (!IsOpen || canvas == null) return;
            if (focus == null)
            {
                var sel = MenuNav.Selected(root);
                if (sel != null) focus = sel.name;
            }
            float scrollY = keepScroll && content != null ? content.anchoredPosition.y : 0f;
            // Off at once and destroyed when the frame ends: Destroy is
            // deferred, and a page still active for the rest of this frame is
            // a page Collect would wire the new cursor into.
            if (root != null) { root.gameObject.SetActive(false); Drop(root.gameObject); }
            Build(focus, scrollY);
        }

        /// <summary>Destroy, in whichever mode this is. Edit mode has no end
        /// of frame for a deferred Destroy to happen at — the preview tool
        /// builds this page there.</summary>
        static void Drop(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        // ------------------------------------------------------------------
        //  The page
        // ------------------------------------------------------------------

        const float Margin = 20f, HeaderH = 104f, FooterH = 64f, Gap = 8f;

        /// <summary>Usable width inside the panel, from the real canvas —
        /// never from a measured rect, which on a page built this frame has
        /// not resolved yet.</summary>
        static float ColW => MenuKit.HalfWidth * 2f - Margin * 2f - 36f;
        static float ColL => -ColW * 0.5f;
        static float ColR => ColW * 0.5f;

        /// <summary>How many cells go side by side: as many as keep a cell
        /// wide enough for its effect line at the type floor.</summary>
        static int Cols => Mathf.Clamp(Mathf.FloorToInt((ColW + Gap) / (430f + Gap)), 1, 3);

        // HOW MANY CHARACTERS FIT, from the width the text is actually given.
        // MenuKit text overflows rather than clips, so a caption one word too
        // long is drawn across the cell beside it; and a fixed character count
        // is right on exactly one canvas. Bold capitals run about 13 units
        // each at the type floor and mixed lower case about 10 — the figures
        // the tab strip was budgeted with. DebugBenchPreview measures the real
        // preferredWidth of every caption at three aspects and fails on an
        // overflow, which is what keeps these two numbers honest.
        const float CapsUnit = 13.2f, LowerUnit = 9.8f;
        static int CapsFit(float width) => Mathf.Max(8, Mathf.FloorToInt(width / CapsUnit));
        static int LowerFit(float width) => Mathf.Max(12, Mathf.FloorToInt(width / LowerUnit));

        void Build(string focus, float scrollY)
        {
            var s = S;
            var car = Car;
            var spec = SpecOf(car);

            root = MenuKit.Stretch(canvas.transform, "Bench", Vector2.zero, Vector2.one,
                                   Margin, Margin, 14f, -14f, MenuKit.PanelBg);

            // ---- header: what this is, the way out, the two pages --------
            MenuKit.Label(root,
                "DEBUG BENCH   ·   " + (car != null ? Clip(car.displayName, 40).ToUpperInvariant()
                                                    : "NO CAR"),
                MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(ColL, -10f),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW - 200f, height: 36f, bold: true);
            Named(MenuKit.Button(root, "BACK", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColRight(ColR, 180f), -8f), new Vector2(180f, 40f),
                Close, 20), "back");

            // Two tabs in the garage, four in a drive: the hour, the weather
            // and the car under the player are things only a drive has. A page
            // left on WORLD by the last drive opens on FAULTS in the garage.
            bool drive = DrivePages;
            if (!drive && page > Page.Parts) page = Page.Faults;
            int faults = car != null ? car.faults.Count : 0;
            int tabs = drive ? 4 : 2;
            float tabW = (ColW - Gap * (tabs - 1)) / tabs;
            // The counts ride in the caption only while there is room for
            // them: a quarter of a 4:3 canvas holds fifteen capitals.
            string[] captions =
            {
                "FAULTS" + (faults > 0 ? "  (" + faults + (drive ? ")" : " ON)") : ""),
                (drive ? "PARTS" : "PARTS + STAGES") +
                    (car != null && spec != null ? "  (" + Upgrades.TotalStages(car) + ")" : ""),
                "WORLD", "CAR",
            };
            for (int i = 0; i < tabs; i++)
            {
                var p = (Page)i;
                var tab = Named(MenuKit.Button(root, Clip(captions[i], CapsFit(tabW - 16f)),
                    new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + i * (tabW + Gap), tabW), -56f),
                    new Vector2(tabW, 40f), () => Show(p), 20), TabKeys[i]);
                MenuKit.MarkTab(tab, page == p);
            }

            // ---- footer: what the car is driving under, right now --------
            var agg = car != null ? DebugCarOps.HandicapLine(car) : "";
            bool clean = car == null || car.faults.Count == 0 || agg == DebugCarOps.NoHandicap;
            var foot = MenuKit.Stretch(root, "Foot", new Vector2(0f, 0f), new Vector2(1f, 0f),
                                       0f, 0f, 0f, FooterH);
            // With everything wrong at once the handicap is nine terms, and on
            // a 4:3 canvas that is more than one line holds. It is the line
            // the page exists for, so it takes the BUILD line's place rather
            // than lose its tail to an ellipsis — the build is one tab away.
            int footFit = Mathf.FloorToInt(ColW / 11.6f);
            string foot1 = agg, foot2 = null;
            if (agg.Length > footFit)
            {
                int cut = agg.LastIndexOf(" · ", Mathf.Min(footFit, agg.Length - 1),
                                          System.StringComparison.Ordinal);
                if (cut > 0) { foot1 = agg.Substring(0, cut); foot2 = agg.Substring(cut + 3); }
            }
            MenuKit.Label(foot, Clip(foot1, footFit), MenuKit.Tiny,
                new Vector2(0.5f, 1f),
                new Vector2(ColL, -6f), TextAnchor.MiddleLeft,
                clean ? MenuKit.Good : MenuKit.Bad, ColW, height: 26f, bold: true);
            if (foot2 != null)
                MenuKit.Label(foot, Clip(foot2, footFit), MenuKit.Tiny,
                    new Vector2(0.5f, 1f), new Vector2(ColL, -34f), TextAnchor.MiddleLeft,
                    MenuKit.Bad, ColW, height: 26f, bold: true);
            else
                MenuKit.Label(foot, Clip(DebugCarOps.BuildLine(car, spec), LowerFit(ColW)), MenuKit.Tiny,
                    new Vector2(0.5f, 1f), new Vector2(ColL, -34f), TextAnchor.MiddleLeft,
                    MenuKit.Dim, ColW, height: 26f);

            // ---- the scrolling body --------------------------------------
            var view = MenuKit.Stretch(root, "View", Vector2.zero, Vector2.one,
                                       8f, 8f, FooterH + 2f, -HeaderH);
            content = MenuKit.ScrollBody(view);

            // The two drive pages first: neither needs a car on the bench (the
            // sky has no owner, and the CAR page is how a bench with no car
            // gets one).
            if (s != null && page == Page.World) BuildWorld();
            else if (s != null && page == Page.Car) BuildCar(s, car);
            else if (s == null || car == null)
                MenuKit.Label(content, "There is no car to work on.", MenuKit.Body,
                    new Vector2(0.5f, 1f), new Vector2(ColL, -20f), TextAnchor.MiddleLeft,
                    MenuKit.Dim, ColW);
            else if (page == Page.Faults) BuildFaults(s, car, spec);
            else BuildParts(s, car, spec);

            // The design column minus the panel's own margins, the header and
            // the footer: the viewport's height, worked out rather than read.
            float viewH = MenuKit.DesignHeight - 28f - HeaderH - FooterH - 2f;
            MenuKit.FitScrollContent(content, viewH);
            // Where the player was. Written straight onto the content rather
            // than through verticalNormalizedPosition, which needs resolved
            // rects this frame does not have; ScrollRect clamps it on its own
            // LateUpdate if the page got shorter.
            content.anchoredPosition = new Vector2(0f, Mathf.Max(0f, scrollY));

            var rows = MenuNav.Collect(root);
            MenuNav.Column(rows);
            if (rows.Count == 0) return;
            Selectable start = null;
            if (focus != null) start = rows.Find(r => r.name == focus);
            if (start == null) start = rows[0];
            MenuNav.Select(start);
            // On the page's OWN canvas, never on the host. The host is the
            // pause menu's GameObject (or the front end's), which already
            // carries a watchdog of its own — and MenuNav.Watch REUSES one it
            // finds, so hosting there re-pointed the pause menu's fallback at
            // a bench button that is destroyed when this page closes, and the
            // pause menu was left with a watchdog guarding nothing.
            var watch = MenuNav.Watch(canvas.gameObject, start);
            MenuNav.Defer(watch, null, rows, null);
        }

        // ---- FAULTS --------------------------------------------------------

        void BuildFaults(LifeState s, OwnedCar car, CarSpec spec)
        {
            float y = -8f;
            // The way back to a healthy car goes FIRST: a debug affordance
            // below forty rows of content is one nobody finds.
            Named(MenuKit.Button(content, "CLEAR ALL FAULTS  (" + car.faults.Count + ")",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, 330f), y),
                new Vector2(330f, 40f), car.faults.Count == 0 ? (UnityEngine.Events.UnityAction)null : () =>
                {
                    int n = DebugCarOps.ClearFaults(car);
                    note = n + " fault" + (n == 1 ? "" : "s") + " cleared";
                    Apply(s, car);
                }, 20, DebugPurple), "clear_faults");
            MenuKit.Label(content,
                Clip(note ?? "Tap to switch a fault on. It bites when you resume.",
                     LowerFit(ColW - 346f)),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL + 346f, y - 6f),
                TextAnchor.MiddleLeft, note != null ? MenuKit.Accent : MenuKit.Dim,
                ColW - 346f, height: 28f);
            y -= 52f;

            int cols = Cols;
            float cellW = (ColW - Gap * (cols - 1)) / cols;
            const float CellH = 56f;

            for (int lane = 0; lane < DebugCarOps.Lanes.Length; lane++)
            {
                var rows = DebugCarOps.FaultRows(spec, DebugCarOps.Lanes[lane]);
                if (rows.Count == 0) continue;

                MenuKit.Label(content, DebugCarOps.LaneTitles[lane], MenuKit.Tiny,
                    new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    MenuKit.Accent, ColW, height: 26f, bold: true);
                y -= 32f;

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    int col = i % cols;
                    if (i > 0 && col == 0) y -= CellH + Gap;
                    bool on = DebugCarOps.HasFault(car, row.id);
                    var f = on ? car.faults.Find(x => x.id == row.id) : null;

                    // HIDDEN is what a bench fault is born as and needs no
                    // saying; FOUND is the news — somebody has inspected it.
                    string state = !on ? "" : f != null && !f.hidden ? "   [ON, FOUND]" : "   [ON]";
                    string caption =
                        Clip(row.name.ToUpperInvariant(), CapsFit(cellW - 16f) - state.Length) + state +
                        "\n" + DimTag + Clip(row.effect, LowerFit(cellW - 16f)) + "</color>";
                    var b = Named(MenuKit.Button(content, caption, new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                        new Vector2(cellW, CellH), () =>
                        {
                            bool now = !DebugCarOps.HasFault(car, row.id);
                            DebugCarOps.SetFault(car, spec, row.id, now);
                            note = row.name + (now ? " — ON" : " — off") +
                                   (now && !row.felt ? "  (nothing to feel: see its line)" : "");
                            Apply(s, car);
                        }, 20), "fault_" + row.id);
                    if (on) MenuKit.MarkTab(b, true);
                }
                y -= CellH + 18f;
            }
        }

        // ---- PARTS ---------------------------------------------------------

        void BuildParts(LifeState s, OwnedCar car, CarSpec spec)
        {
            float y = -8f;
            bool noSpec = spec == null;

            Named(MenuKit.Button(content, "BUILD EVERYTHING", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, 260f), y), new Vector2(260f, 40f),
                noSpec ? (UnityEngine.Events.UnityAction)null : () =>
                {
                    DebugCarOps.BuildEverything(car, spec);
                    note = "every ladder to stage " + Upgrades.MaxStage + ", every part the car takes";
                    Apply(s, car);
                }, 20, DebugPurple), "build_all");
            Named(MenuKit.Button(content, "BACK TO STOCK", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + 260f + Gap, 230f), y), new Vector2(230f, 40f),
                noSpec ? (UnityEngine.Events.UnityAction)null : () =>
                {
                    DebugCarOps.StripEverything(car);
                    note = "as it left the factory";
                    Apply(s, car);
                }, 20, DebugPurple), "strip_all");
            float noteX = ColL + 260f + Gap + 230f + 16f;
            MenuKit.Label(content,
                Clip(note ?? (noSpec ? "This car has no catalog entry, so it takes faults but not parts."
                                     : "Changes land when you resume."),
                     LowerFit(ColR - noteX)),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(noteX, y - 6f),
                TextAnchor.MiddleLeft, note != null ? MenuKit.Accent : MenuKit.Dim,
                ColR - noteX, height: 28f);
            y -= 52f;
            if (noSpec) return;

            // ---- the six ladders: a row each, five buttons to a row ------
            MenuKit.Label(content, "STAGES   ·   0 is the factory part, 4 is race hardware",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 26f, bold: true);
            y -= 32f;

            const float StageW = 56f, StageH = 50f;
            float stagesW = StageW * 5f + Gap * 4f;
            float textW = ColW - stagesW - 16f;
            for (var kind = Upgrades.Kind.Power; kind <= Upgrades.LastKind; kind++)
            {
                var k = kind;
                int at = Upgrades.GetStage(car, k);
                string value = DebugCarOps.StageValue(spec, k, at) +
                    // The seat is built when a delivery loads, around boxes
                    // that are already standing on it. It cannot be changed
                    // under them, and the row has to say so.
                    (k == Upgrades.Kind.Seat ? "  ·  fitted when the NEXT delivery loads" : "");
                MenuKit.Label(content,
                    Clip(Upgrades.KindLabels[(int)k] + "   ·   " + Upgrades.StageNames[(int)k][at],
                         CapsFit(textW)) +
                    "\n" + DimTag + Clip(value, LowerFit(textW)) + "</color>",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.UpperLeft, Color.white, textW, height: StageH, bold: true);

                for (int st = 0; st <= Upgrades.MaxStage; st++)
                {
                    int stage = st;
                    var b = Named(MenuKit.Button(content, stage.ToString(), new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(ColR - stagesW + stage * (StageW + Gap), StageW), y),
                        new Vector2(StageW, StageH), () =>
                        {
                            DebugCarOps.SetStage(car, k, stage);
                            note = Upgrades.KindLabels[(int)k] + " stage " + stage + " — " +
                                   Upgrades.StageNames[(int)k][stage];
                            Apply(s, car);
                        }, 22), "stage_" + (int)k + "_" + stage);
                    if (stage == at) MenuKit.MarkTab(b, true);
                }
                y -= StageH + Gap;
            }
            y -= 14f;

            // ---- the eight bolt-ons --------------------------------------
            MenuKit.Label(content,
                Clip("PARTS   ·   an UNLOCKS part only lets your ADVANCED TUNING through",
                     Mathf.FloorToInt(ColW / 11.6f)),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 26f, bold: true);
            y -= 32f;

            int cols = Cols;
            float cellW = (ColW - Gap * (cols - 1)) / cols;
            const float CellH = 56f;
            for (int i = 0; i < Upgrades.AllMods.Length; i++)
            {
                var mod = Upgrades.AllMods[i];
                int col = i % cols;
                if (i > 0 && col == 0) y -= CellH + Gap;

                Upgrades.ModText(mod, out string name, out string effect);
                bool on = Upgrades.HasMod(car, mod);
                string no = on ? null : DebugCarOps.ModRefusal(spec, mod);
                string fitted = on ? "   [FITTED]" : "";
                string caption = Clip(name, CapsFit(cellW - 16f) - fitted.Length) + fitted +
                                 "\n" + DimTag +
                                 (no != null ? Clip("CANNOT FIT — " + no, CapsFit(cellW - 16f))
                                             : Clip(effect, LowerFit(cellW - 16f))) + "</color>";
                var b = Named(MenuKit.Button(content, caption, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                    new Vector2(cellW, CellH),
                    no != null ? (UnityEngine.Events.UnityAction)null : () =>
                    {
                        bool now = !Upgrades.HasMod(car, mod);
                        bool hadLsd = car.lsd, hadWeld = car.welded;
                        string refused = DebugCarOps.SetMod(car, spec, mod, now);
                        note = refused != null ? name + " — " + refused
                             : name + (now ? " — FITTED" : " — removed") +
                               (hadLsd && !car.lsd && now ? ", and the plate pack came out" : "") +
                               (hadWeld && !car.welded && now ? ", and the weld was cut out" : "");
                        Apply(s, car);
                    }, 20), "mod_" + (int)mod);
                // Only ever marked ON. MarkTab(false) repaints the caption white,
                // which would un-grey a part the car refuses.
                if (on) MenuKit.MarkTab(b, true);
            }
            y -= CellH + 20f;

            BuildCooling(s, car, ref y);
        }

        /// <summary>The four settings each cooling part can be dropped to.
        /// NEW, past its best, nearly gone, and dead — the four readings a
        /// tester actually wants, rather than a slider nobody can hit a number
        /// on with a thumbstick.</summary>
        static readonly int[] CoolSteps = { 100, 60, 30, 0 };

        /// <summary>
        /// The cooling system, on the bench.
        ///
        /// This page is the only way to test a temperature gauge without
        /// driving for an hour on a car that happens to have a bad radiator,
        /// and each of the four rows has a DIFFERENT symptom to go and look
        /// for: drop the fan and sit still, drop the radiator and hold it flat,
        /// drop the hoses and watch the coolant go, drop the coolant and watch
        /// the needle leave the scale.
        ///
        /// The parts are saved onto the car (they cross into the drive through
        /// the handoff like every other part), and the two LIVE buttons under
        /// them are not: they reach into the EngineTemp that is running right
        /// now, because "wait four minutes for it to boil" is not a test.
        /// </summary>
        void BuildCooling(LifeState s, OwnedCar car, ref float y)
        {
            MenuKit.Label(content,
                Clip("COOLING   ·   fan fails at a standstill, radiator under load",
                     Mathf.FloorToInt(ColW / 11.6f)),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 26f, bold: true);
            y -= 32f;

            const float StepW = 74f, StepH = 46f;
            float stepsW = StepW * CoolSteps.Length + Gap * (CoolSteps.Length - 1);
            float textW = ColW - stepsW - 16f;
            for (int p = 0; p < LifeRules.CoolingServices.Length; p++)
            {
                var part = LifeRules.CoolingServices[p].part;
                float at = LifeRules.CoolPartCond(car, part);
                MenuKit.Label(content,
                    Clip(part.ToString().ToUpperInvariant() + "   ·   " +
                         Mathf.RoundToInt(at) + "%", CapsFit(textW)) +
                    "\n" + DimTag + Clip(DebugCarOps.CoolPartNote(part), LowerFit(textW)) +
                    "</color>",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.UpperLeft, Color.white, textW, height: StepH, bold: true);

                for (int i = 0; i < CoolSteps.Length; i++)
                {
                    int want = CoolSteps[i];
                    var kind = part;
                    var b = Named(MenuKit.Button(content, want.ToString(), new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(ColR - stepsW + i * (StepW + Gap), StepW), y),
                        new Vector2(StepW, StepH), () =>
                        {
                            DebugCarOps.SetCoolPart(car, kind, want);
                            note = kind.ToString().ToUpperInvariant() + " at " + want + "%";
                            Apply(s, car);
                        }, 20), "cool_" + (int)kind + "_" + want);
                    if (Mathf.RoundToInt(at) == want) MenuKit.MarkTab(b, true);
                }
                y -= StepH + Gap;
            }

            // The live model, when there is one under us. Absent in the garage,
            // which is the honest answer there: there is no engine running to
            // heat up, and a button that silently did nothing would be worse
            // than one that is not drawn.
            var live = DebugCarOps.LiveTemp();
            if (live == null) return;
            y -= 10f;
            float bw = (ColW - Gap * 2f) / 3f;
            Named(MenuKit.Button(content,
                "HEAT TO " + Mathf.RoundToInt(EngineTemp.RedMark) + "C",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, bw), y),
                new Vector2(bw, 44f), () =>
                {
                    live.celsius = EngineTemp.RedMark + 1f;
                    note = "needle in the red — resume and watch it";
                    Apply(s, car);
                }, 20, DebugPurple), "cool_heat");
            Named(MenuKit.Button(content, "COOK IT (" +
                    Mathf.RoundToInt(EngineTemp.SeizeFromC + 15f) + "C)",
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL + bw + Gap, bw), y),
                new Vector2(bw, 44f), () =>
                {
                    live.celsius = EngineTemp.SeizeFromC + 15f;
                    note = "well past the point of no return";
                    Apply(s, car);
                }, 20, DebugPurple), "cool_cook");
            Named(MenuKit.Button(content, "SEIZE IT NOW", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + (bw + Gap) * 2f, bw), y),
                new Vector2(bw, 44f), () =>
                {
                    live.Seize();
                    note = "engine destroyed — exit to bank it";
                    Apply(s, car);
                }, 20, DebugPurple), "cool_seize");
            y -= 54f;
            MenuKit.Label(content,
                Clip("Now " + Mathf.RoundToInt(live.celsius) + "C, coolant " +
                     Mathf.RoundToInt(live.coolantPct) + "%, ambient " +
                     Mathf.RoundToInt(live.ambientC) + "C" +
                     (live.Seized ? "  ·  SEIZED" : ""), LowerFit(ColW)),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, live.Seized ? MenuKit.Bad : MenuKit.Dim,
                ColW, height: 26f);
            y -= 30f;
        }

        // ---- WORLD ---------------------------------------------------------

        /// <summary>As many cells across as keep a cell <paramref name="minW"/>
        /// wide — the WORLD and CAR grids hold short captions and want more,
        /// narrower cells than the fault list's.</summary>
        static int GridCols(float minW, int max) =>
            Mathf.Clamp(Mathf.FloorToInt((ColW + Gap) / (minW + Gap)), 1, max);

        /// <summary>
        /// The hour and the weather, as two rows of switches.
        ///
        /// The owner, 2026-09-21: "options in Debug mode to change time of
        /// day, weather, and car mid-race." Every one of the seven hours and
        /// every one of the four skies, whatever block the calendar is in —
        /// a BOOKED race keeps to its block's hours, a debug page does not.
        /// Nothing here is the save's: see <see cref="DebugWorldOps"/>.
        /// </summary>
        void BuildWorld()
        {
            float y = -8f;
            MenuKit.Label(content,
                Clip(note ?? "Changes land at once. RESTART RACE keeps them; the house takes them back.",
                     LowerFit(ColW)),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                note != null ? MenuKit.Accent : MenuKit.Dim, ColW, height: 28f);
            y -= 36f;

            int cols = GridCols(222f, 7);
            float cellW = (ColW - Gap * (cols - 1)) / cols;
            const float CellH = 50f;

            // ---- the seven hours ---------------------------------------
            int hour = DebugWorldOps.Hour;
            MenuKit.Label(content, "TIME OF DAY   ·   " + TimeOfDay.Label(hour), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 26f, bold: true);
            y -= 32f;
            for (int i = 0; i < TimeOfDay.Count; i++)
            {
                int h = i;
                int col = i % cols;
                if (i > 0 && col == 0) y -= CellH + Gap;
                var b = Named(MenuKit.Button(content, TimeOfDay.Label(h), new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                    new Vector2(cellW, CellH), () =>
                    {
                        DebugWorldOps.SetHour(h);
                        note = "it is " + TimeOfDay.Label(h) + " — resume and look";
                        Rebuild();
                    }, 20), "hour_" + h);
                if (h == hour) MenuKit.MarkTab(b, true);
            }
            y -= CellH + 20f;

            // ---- the four skies, and the calendar's own ------------------
            int forced = DebugWorldOps.ForcedWeather;
            var now = Seasons.CurrentWeather;
            var rolled = Seasons.WeatherFor(Seasons.CurrentDay);
            MenuKit.Label(content,
                "WEATHER   ·   " + DebugWorldOps.WeatherNames[(int)now] +
                (forced < 0 ? "  (the calendar's own)" : "  (forced)"), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 26f, bold: true);
            y -= 32f;
            // -1 first: the way back to what the day actually rolled.
            for (int i = 0; i <= DebugWorldOps.WeatherNames.Length; i++)
            {
                int w = i - 1;
                int col = i % cols;
                if (i > 0 && col == 0) y -= CellH + Gap;
                string caption = w < 0
                    ? "CALENDAR: " + DebugWorldOps.WeatherNames[(int)rolled]
                    : DebugWorldOps.WeatherNames[w];
                var b = Named(MenuKit.Button(content, Clip(caption, CapsFit(cellW - 16f)),
                    new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                    new Vector2(cellW, CellH), () =>
                    {
                        DebugWorldOps.SetWeather(w);
                        note = w < 0 ? "the sky is the calendar's again"
                                     : DebugWorldOps.WeatherNames[w] + " — " +
                                       DebugWorldOps.WeatherLine((Weather)w);
                        Rebuild();
                    }, 20), "weather_" + i);
                if (w == forced) MenuKit.MarkTab(b, true);
            }
            y -= CellH + 12f;
            MenuKit.Label(content, Clip(DebugWorldOps.WeatherLine(now), LowerFit(ColW)), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, ColW, height: 26f);
            y -= 30f;
        }

        // ---- CAR -----------------------------------------------------------

        /// <summary>
        /// Climb into a different car without leaving the road: one of the
        /// player's own, or anything in the catalog through the one LOANER
        /// slot (<see cref="DebugCarOps.Loan"/>). The catalog is an index of
        /// makes and then a make's cars — 317 rows is a list nobody scrolls
        /// with a thumbstick.
        /// </summary>
        void BuildCar(LifeState s, OwnedCar driving)
        {
            float y = -8f;
            string refusal = DebugCarOps.SwapRefusal();
            MenuKit.Label(content,
                Clip(note ?? (refusal != null ? "Not on this drive: " + refusal + "."
                                              : "Tap a car and you are in it, at the speed you were doing."),
                     LowerFit(ColW)),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                note != null || refusal != null ? MenuKit.Accent : MenuKit.Dim, ColW, height: 28f);
            y -= 36f;
            if (refusal != null) return;

            int cols = Cols;
            float cellW = (ColW - Gap * (cols - 1)) / cols;
            const float CellH = 56f;

            // ---- the garage ----------------------------------------------
            MenuKit.Label(content, "YOUR CARS   ·   as they stand, faults and parts and all",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 26f, bold: true);
            y -= 32f;
            for (int i = 0; i < s.cars.Count; i++)
            {
                var owned = s.cars[i];
                if (owned == null) continue;
                int col = i % cols;
                if (i > 0 && col == 0) y -= CellH + Gap;
                var ospec = SpecOf(owned);
                bool here = owned == driving;
                string state = here ? "   [DRIVING]" : owned.debugLoaner ? "   [LOANER]" : "";
                string line = ospec == null ? "built-in car — lands on RESTART RACE"
                    : Upgrades.EffectiveHp(owned, ospec) + " hp · " +
                      Upgrades.EffectiveKg(owned, ospec) + " kg · " + ospec.drv + " · " +
                      owned.faults.Count + " fault" + (owned.faults.Count == 1 ? "" : "s");
                string caption =
                    Clip((owned.displayName ?? "?").ToUpperInvariant(),
                         CapsFit(cellW - 16f) - state.Length) + state +
                    "\n" + DimTag + Clip(line, LowerFit(cellW - 16f)) + "</color>";
                var b = Named(MenuKit.Button(content, caption, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                    new Vector2(cellW, CellH),
                    here ? (UnityEngine.Events.UnityAction)null : () => TakeCar(s, owned), 20),
                    "own_" + i);
                if (here) MenuKit.MarkTab(b, true);
            }
            y -= CellH + 20f;

            // ---- the catalog: makes, then one make's cars ----------------
            if (carMake == null)
            {
                MenuKit.Label(content,
                    Clip("ANY CAR IN THE CATALOG   ·   borrowed factory-fresh into the one LOANER slot",
                         Mathf.FloorToInt(ColW / 11.6f)),
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft,
                    MenuKit.Accent, ColW, height: 26f, bold: true);
                y -= 32f;
                int mcols = GridCols(260f, 6);
                float mW = (ColW - Gap * (mcols - 1)) / mcols;
                const float MakeH = 44f;
                var makes = DebugCarOps.Makes();
                for (int i = 0; i < makes.Count; i++)
                {
                    string make = makes[i].Key;
                    int col = i % mcols;
                    if (i > 0 && col == 0) y -= MakeH + Gap;
                    string count = "  (" + makes[i].Value + ")";
                    Named(MenuKit.Button(content,
                        Clip(make, CapsFit(mW - 16f) - count.Length) + count, new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(ColL + col * (mW + Gap), mW), y),
                        new Vector2(mW, MakeH), () =>
                        {
                            carMake = make;
                            note = null;
                            Rebuild("dbg_all_makes", keepScroll: false);
                        }, 20), "make_" + make);
                }
                y -= MakeH + 20f;
                return;
            }

            var models = DebugCarOps.ModelsOf(carMake);
            Named(MenuKit.Button(content, "<  ALL MAKES", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, 230f), y), new Vector2(230f, 40f), () =>
                {
                    string back = "dbg_make_" + carMake;
                    carMake = null;
                    note = null;
                    Rebuild(back, keepScroll: false);
                }, 20, DebugPurple), "all_makes");
            MenuKit.Label(content, carMake + "   ·   " + models.Count + " car" +
                    (models.Count == 1 ? "" : "s"), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL + 246f, y - 6f), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW - 246f, height: 28f, bold: true);
            y -= 52f;
            for (int i = 0; i < models.Count; i++)
            {
                var m = models[i];
                int col = i % cols;
                if (i > 0 && col == 0) y -= CellH + Gap;
                string line = m.hp + " hp · " + m.kg + " kg · " + m.drv + " · " + MenuKit.Money(m.price);
                string caption = Clip((m.name ?? m.id).ToUpperInvariant(), CapsFit(cellW - 16f)) +
                                 "\n" + DimTag + Clip(line, LowerFit(cellW - 16f)) + "</color>";
                Named(MenuKit.Button(content, caption, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(ColL + col * (cellW + Gap), cellW), y),
                    new Vector2(cellW, CellH), () => TakeCar(s, DebugCarOps.Loan(s, m)), 20),
                    "model_" + m.id);
            }
            y -= CellH + 20f;
        }

        /// <summary>Into <paramref name="car"/>, and say so. The save is owed
        /// a write either way: the keys moved, and a loaner may have come and
        /// another gone.</summary>
        void TakeCar(LifeState s, OwnedCar car)
        {
            if (car == null) { note = "that car could not be built"; Rebuild(); return; }
            string late = DebugCarOps.DriveCar(s, car);
            note = Clip(car.displayName, 34) + (late != null ? " — " + late : " — you are in it");
            dirty = true;
            // The cursor goes to the row that now says DRIVING: the one that
            // was pressed may have been a catalog row, which is not this car's
            // row at all.
            int at = s.cars.IndexOf(car);
            Rebuild(at >= 0 ? "dbg_own_" + at : null, keepScroll: false);
        }

        // ------------------------------------------------------------------

        /// <summary>Onto the car, into the save, and redraw where we stood.
        /// </summary>
        void Apply(LifeState s, OwnedCar car)
        {
            DebugCarOps.Commit(s, car);
            dirty = true;
            Rebuild();
        }

        /// <summary>
        /// Give a control a name that SURVIVES ITS OWN PRESS. MenuKit names a
        /// button "Btn_" plus its caption, and every caption on this page
        /// changes when the button is pressed ("[ON]", "[FITTED]", a count) —
        /// so a cursor restored by MenuKit's name would be lost on exactly the
        /// presses it exists for.
        /// </summary>
        static Button Named(Button b, string name)
        {
            if (b != null) b.gameObject.name = "dbg_" + name;
            return b;
        }

        /// <summary>Text has no ellipsis mode, and an over-long label does not
        /// clip — it runs over the cell beside it.</summary>
        static string Clip(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max || max < 2 ? s
                : s.Substring(0, max - 1).TrimEnd() + "…";
    }
}
